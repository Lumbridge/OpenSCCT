"""Publish the checked-in progress report to a running Linux master over SSH."""

import argparse
import hashlib
from pathlib import Path, PurePosixPath
import shlex
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[2]
REPORT = "decomp/reports/progress.json"

# The content lock covers all publishers to the same directory. Existing requests
# can continue reading the prior inode until the checked replacement is ready.
INSTALL = """
import fcntl, hashlib, os, pathlib, sys, tempfile
target = pathlib.Path(sys.argv[1])
data = sys.stdin.buffer.read()
if hashlib.sha256(data).hexdigest() != sys.argv[3]:
    sys.exit("Incomplete progress report upload")
directory = os.open(target.parent, os.O_RDONLY)
fcntl.flock(directory, fcntl.LOCK_EX)
current = target.read_bytes() if target.exists() else b""
if current == data:
    sys.exit(0)
if hashlib.sha256(current).hexdigest() != sys.argv[2]:
    sys.exit("Another checkpoint was published concurrently; retry against the current report")
with tempfile.NamedTemporaryFile(dir=target.parent, delete=False) as out:
    tmp = pathlib.Path(out.name)
    try:
        out.write(data)
        out.flush()
        os.fchmod(out.fileno(), 0o644)
        os.fsync(out.fileno())
        os.replace(tmp, target)
        os.fsync(directory)
    finally:
        tmp.unlink(missing_ok=True)
"""


def git(*args, data=None, root=ROOT):
    return subprocess.check_output(["git", *args], cwd=root, input=data)


def checked_report(root=ROOT):
    subprocess.run([sys.executable, "-I", "decomp/tools/progress.py", "report", "--check"], cwd=root, check=True)
    data = git("show", f"HEAD:{REPORT}", root=root)
    if data.decode() != (root / REPORT).read_text(encoding="utf-8"):
        raise ValueError("Commit the generated progress report before publishing it.")
    return data


def check_predecessor(previous, root=ROOT):
    if not previous:
        return
    blob = git("hash-object", "--stdin", data=previous, root=root).strip()
    history = git("rev-list", "--objects", "HEAD", "--", REPORT, root=root)
    if blob + b" " + REPORT.encode() not in history.splitlines():
        raise ValueError("The installed report is outside this checkout's history. Update the checkout before publishing.")


def publish(ssh, directory, data):
    target = shlex.quote(str(PurePosixPath(directory) / "project-progress.json"))
    read = 'if test -e "$1"; then cat -- "$1"; fi'
    previous = subprocess.check_output([*ssh, f"sudo -n sh -c {shlex.quote(read)} sh {target}"])
    if previous == data:
        return
    check_predecessor(previous)
    before = hashlib.sha256(previous).hexdigest()
    after = hashlib.sha256(data).hexdigest()
    command = f"sudo -n python3 -c {shlex.quote(INSTALL)} {target} {before} {after}"
    subprocess.run([*ssh, command], input=data, check=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("destination", help="SSH login, e.g. ubuntu@master.lumbridge.co.uk")
    parser.add_argument("--key", type=Path, help="SSH private key (default: your SSH configuration)")
    parser.add_argument("--data-directory", default="/opt/svm/master", help="Linux Master.DataDirectory")
    args = parser.parse_args()
    if not PurePosixPath(args.data_directory).is_absolute():
        parser.error("--data-directory must be absolute")
    ssh = ["ssh", "-o", "BatchMode=yes", "-o", "ConnectTimeout=15"]
    if args.key:
        ssh.extend(["-i", str(args.key)])
    try:
        data = checked_report()
        publish([*ssh, "--", args.destination], args.data_directory, data)
        print(f"Published project-progress.json, SHA-256 {hashlib.sha256(data).hexdigest()}.")
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        sys.exit(f"Progress publication failed: {error}")
