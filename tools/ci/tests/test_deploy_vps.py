"""Exercise deployment orchestration with synthetic packages and SSH executables."""

import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

import pytest


ROOT = Path(__file__).resolve().parents[3]
PWSH = shutil.which("pwsh")
pytestmark = pytest.mark.skipif(PWSH is None, reason="PowerShell 7 is required")

MOCK_SSH = """
import json
import os
from pathlib import Path
import sys
import time
import uuid

tool, *args = sys.argv[1:]
if tool == "scp":
    host = args[-1].split(":")[0]
    operation = "upload"
else:
    host, command = args[-2:]
    operation = command.split()[2] if command.startswith("bash ") else (
        "mkdir" if command.startswith("mkdir ") else "cleanup"
    )

events = Path(os.environ["MOCK_EVENTS"])
start = time.time()
if operation == "upload":
    (events / (host + ".ready")).touch()
    deadline = time.monotonic() + 20
    while len(list(events.glob("*.ready"))) < int(os.environ["MOCK_HOST_COUNT"]):
        if time.monotonic() > deadline:
            sys.exit(2)
        time.sleep(0.02)
elif operation.startswith("svm-"):
    if operation != "svm-master" and os.environ["MOCK_HOST_COUNT"] == "3":
        (events / (operation + ".worker")).touch()
        deadline = time.monotonic() + 20
        while len(list(events.glob("*.worker"))) < 2:
            if time.monotonic() > deadline:
                sys.exit(2)
            time.sleep(0.02)
    time.sleep(0.3)
end = time.time()
(events / (str(uuid.uuid4()) + ".json")).write_text(
    json.dumps(dict(host=host, operation=operation, start=start, end=end, args=args))
)
if os.environ.get("MOCK_FAIL") == host + "/" + operation:
    sys.exit(1)
"""


@pytest.fixture
def deploy(tmp_path):
    package = tmp_path / "package"
    package.mkdir()
    hashes = {}
    for name in ("svm-master.tgz", "svm-server.tgz", "svm-hubmaps.tgz"):
        data = name.encode()
        (package / name).write_bytes(data)
        hashes[name] = hashlib.sha256(data).hexdigest()
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    manifest = {"commit": commit, "sha256": hashes}
    (package / "manifest.json").write_text(json.dumps(manifest))
    mock = tmp_path / "mock.py"
    mock.write_text(MOCK_SSH)
    for name in ("ssh", "scp"):
        if os.name == "nt":
            (tmp_path / (name + ".cmd")).write_text(
                f'@echo off\n"{sys.executable}" "{mock}" {name} %*\n'
            )
        else:
            executable = tmp_path / name
            executable.write_text(
                f"#!{sys.executable}\nimport runpy, sys\nsys.argv.insert(1, '{name}')\n"
                f"runpy.run_path({str(mock)!r}, run_name='__main__')\n"
            )
            executable.chmod(0o755)

    def run(*, shared=False, fail="", corrupt=False, wrong_commit=False):
        events = tmp_path / "events"
        events.mkdir()
        if corrupt:
            (package / "svm-master.tgz").write_bytes(b"corrupt")
        if wrong_commit:
            (package / "manifest.json").write_text(json.dumps(dict(manifest, commit="0" * 40)))
        command = [
            PWSH, "-NoProfile", "-File", str(ROOT / "tools/deploy/Deploy-Vps.ps1"),
            "-FromPackage", str(package), "-VpsHost", "mock-master.invalid",
        ]
        if not shared:
            command += ["-ServerVpsHost", "mock-server.invalid", "-HubVpsHost", "mock-hub.invalid"]
        result = subprocess.run(
            command, cwd=ROOT, text=True, capture_output=True, timeout=90,
            env=dict(
                os.environ, PATH=str(tmp_path) + os.pathsep + os.environ["PATH"],
                MOCK_EVENTS=str(events), MOCK_FAIL=fail, MOCK_HOST_COUNT="1" if shared else "3",
            ),
        )
        recorded = {}
        for path in events.glob("*.json"):
            event = json.loads(path.read_text())
            recorded.setdefault(event["operation"], []).append(event)
        return result, recorded

    return run


def test_parallel_uploads_finish_before_master_then_workers(deploy):
    result, events = deploy()
    assert result.returncode == 0, result.stdout + result.stderr
    assert len(events["upload"]) == 3
    assert max(event["end"] for event in events["upload"]) <= events["svm-master"][0]["start"]
    assert events["svm-master"][0]["end"] <= min(
        events["svm-server"][0]["start"], events["svm-hub"][0]["start"]
    )
    assert len(events["cleanup"]) == 3
    stages = {event["args"][-1].split(":")[1] for event in events["upload"]}
    assert len(stages) == 1 and "/openscct-deploy-" in stages.pop()


def test_shared_host_uploads_once_and_installs_sequentially(deploy):
    result, events = deploy(shared=True)
    assert result.returncode == 0, result.stdout + result.stderr
    assert len(events["upload"]) == 1 and len(events["cleanup"]) == 1
    assert events["svm-server"][0]["end"] <= events["svm-hub"][0]["start"]


@pytest.mark.parametrize("failure", ["upload", "svm-master"])
def test_failure_stops_promotion_and_cleans_every_host(deploy, failure):
    result, events = deploy(fail="ubuntu@mock-master.invalid/" + failure)
    assert result.returncode != 0
    assert "svm-server" not in events and "svm-hub" not in events
    assert ("svm-master" in events) == (failure == "svm-master")
    assert len(events["cleanup"]) == 3


@pytest.mark.parametrize("mismatch", ["corrupt", "wrong_commit"])
def test_invalid_package_is_rejected_before_ssh(deploy, mismatch):
    result, events = deploy(**{mismatch: True})
    assert result.returncode != 0 and not events
