"""Build the offline svmbuild runtime for a desktop game release."""

import argparse
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
import tempfile
import urllib.request
import zipfile


ROOT = Path(__file__).resolve().parents[2]
CACHE = ROOT / "local/release-cache/contenttools"
WINDOWS_PYTHON = (
    "https://www.python.org/ftp/python/3.11.9/python-3.11.9-embed-amd64.zip",
    "009d6bf7e3b2ddca3d784fa09f90fe54336d5b60f0e0f305c37f400bf83cfd3b",
)
LINUX_PYTHON = (
    "https://github.com/astral-sh/python-build-standalone/releases/download/20261110/"
    "cpython-3.11.18%2B20261110-x86_64-unknown-linux-gnu-install_only_stripped.tar.gz",
    "3288c54cea3db350052019b3f8b558dd8a3da8afa6fda573f5fd2bcb37635b4a",
)
SOURCES = (
    "content/svmbuild", "content/assets/cosmetics", "content/assets/sidearms/export",
    "content/assets/sidearms/CREDITS.txt", "content/assets/weapons/export", "content/assets/rifles/export",
    "content/assets/sidearm_audio/export", "content/assets/user_gun_audio/export", "content/assets/*/CREDITS.txt",
    "tools/scctpkg/scctpkg",
    "re/formats/scctformats", "spec/svmap", "shared/SteamVersusMaps.txt",
)


def fetch(url, expected):
    CACHE.mkdir(parents=True, exist_ok=True)
    target = CACHE / url.rsplit("/", 1)[1]
    if not target.exists() or hashlib.sha256(target.read_bytes()).hexdigest() != expected:
        with tempfile.NamedTemporaryFile(dir=CACHE, delete=False) as temp:
            pending = Path(temp.name)
        try:
            with urllib.request.urlopen(url, timeout=120) as src, pending.open("wb") as stream:
                shutil.copyfileobj(src, stream)
            if hashlib.sha256(pending.read_bytes()).hexdigest() != expected:
                raise ValueError(f"Unexpected digest for {url}")
            os.replace(pending, target)
        finally:
            pending.unlink(missing_ok=True)
    return target


def linux_runtime(folder):
    # The release installer handles regular files. Materialize the real executable
    # at bin/python3 rather than shipping the interpreter's symlink tree.
    with tarfile.open(fetch(*LINUX_PYTHON)) as archive:
        for member in archive:
            path = Path(member.name).relative_to("python")
            if not member.isfile() or ".." in path.parts:
                continue
            if path.as_posix() == "bin/python3.11":
                path = Path("bin/python3")
            elif path.as_posix() != "lib/libpython3.11.so.1.0":
                if path.parts[:2] != ("lib", "python3.11"):
                    continue
                if any(p in ("test", "tests", "site-packages", "__pycache__", "idlelib", "tkinter", "turtledemo",
                             "ensurepip") or p.startswith("config-3.11") for p in path.parts):
                    continue
            dest = folder / path
            dest.parent.mkdir(parents=True, exist_ok=True)
            with archive.extractfile(member) as src, dest.open("wb") as out:
                shutil.copyfileobj(src, out)
    exe = folder / "bin/python3"
    exe.chmod(0o755)
    return folder / "lib/python3.11/site-packages", exe


def windows_runtime(folder):
    with zipfile.ZipFile(fetch(*WINDOWS_PYTHON)) as archive:
        archive.extractall(folder)
    (folder / "python311._pth").write_text(
        "python311.zip\n.\nLib/site-packages\n../repo/content\nimport site\n", encoding="ascii")
    return folder / "Lib/site-packages", folder / "python.exe"


def package(out, platform):
    windows = platform == "win-x64"
    out = out.resolve()
    if out.exists() and any(out.iterdir()) and not (out / "steam-builder-v1").is_file():
        raise ValueError(f"Expected an empty output or a previous content builder: {out}")
    out.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=out.parent) as temporary:
        stage = Path(temporary) / "contenttools"
        site, exe = (windows_runtime if windows else linux_runtime)(stage / "python")
        requirements = [s for s in (ROOT / "content/requirements.txt").read_text().splitlines()
                        if s and s[0].isalpha() and not s.startswith("pytest")]
        platforms = (["win_amd64"] if windows else
                     [f"manylinux_2_{version}_x86_64" for version in range(35, 16, -1)] + ["manylinux2014_x86_64"])
        tags = [item for tag in platforms for item in ("--platform", tag)]
        subprocess.run([sys.executable, "-m", "pip", "install", "--disable-pip-version-check", "--no-compile",
                        "--target", str(site), "--python-version", "3.11", "--implementation", "cp",
                        "--only-binary=:all:", *tags, "--cache-dir", str(CACHE / "pip"), *requirements], check=True)
        shutil.rmtree(site / "bin", ignore_errors=True)
        for tests in list(site.glob("**/tests")):
            if tests.is_dir():
                shutil.rmtree(tests)
        files = subprocess.check_output(["git", "ls-files", "-z", "--", *SOURCES], cwd=ROOT)
        for name in files.decode().split("\0"):
            if name:
                dest = stage / "repo" / name
                dest.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(ROOT / name, dest)
        if (stage / "repo/tools/scctpkg/scctpkg/class_defaults.json").exists():
            raise ValueError("A release cannot contain game-derived class defaults")
        if windows == (os.name == "nt"):
            subprocess.run([str(exe), "-B", "-c", "import numpy, PIL, jsonschema, svmbuild.cli, svmbuild.steps"],
                           cwd=stage / "repo/content", check=True)
        (stage / "steam-builder-v1").write_text(platform + "\n", encoding="ascii")
        previous = Path(temporary) / "previous"
        if out.exists():
            out.rename(previous)
        try:
            stage.rename(out)
        except OSError:
            if previous.exists():
                previous.rename(out)
            raise


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("out", type=Path)
    parser.add_argument("platform", choices=["win-x64", "linux-x64"])
    args = parser.parse_args()
    package(args.out, args.platform)
