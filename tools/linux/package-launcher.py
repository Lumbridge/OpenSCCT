#!/usr/bin/env python3
"""Package the exact native launcher binary with modes, checksums and desktop integration."""
import argparse
import hashlib
from pathlib import Path
import tarfile
import xml.etree.ElementTree as ET


def package(binary: Path, output: Path, repo: Path) -> tuple[Path, Path]:
    header = binary.read_bytes()[:20]
    if header[:6] != b"\x7fELF\x02\x01" or header[18:20] != b"\x3e\x00":
        raise ValueError("Expected a native Linux x86_64 ELF launcher, not a Windows executable.")
    version = ET.parse(repo / "Directory.Build.props").findtext(".//LauncherVersion")
    if not version:
        raise ValueError("Directory.Build.props has no LauncherVersion")
    output.mkdir(parents=True, exist_ok=True)
    archive = output / "OpenSCCT.Launcher-linux-x64.tar.gz"
    files = [(binary, "OpenSCCT.Launcher-linux-x64", 0o755)]
    for name in ("install.sh", "README.txt"):
        files.append((repo / "packaging/linux-launcher" / name, name, 0o755 if name.endswith(".sh") else 0o644))
    files.append((repo / "game/branding/icon.svg", "openscct.svg", 0o644))
    for name in ("Dotnet-LICENSE.txt", "Dotnet-ThirdPartyNotices.txt"):
        files.append((repo / "packaging/licenses" / name, "licenses/" + name, 0o644))
    for license_file in sorted((repo / "src/OpenScct.Launcher.Linux/licenses").glob("*")):
        if license_file.is_file():
            files.append((license_file, "licenses/" + license_file.name, 0o644))
    with tarfile.open(str(archive) + ".tmp", "w:gz", format=tarfile.PAX_FORMAT) as tar:
        for source, name, mode in files:
            info = tar.gettarinfo(str(source), "OpenSCCT-Launcher/" + name)
            info.mode, info.uid, info.gid, info.uname, info.gname = mode, 0, 0, "", ""
            with source.open("rb") as stream:
                tar.addfile(info, stream)
    Path(str(archive) + ".tmp").replace(archive)
    checksum = output / f"OpenSCCT.Launcher-{version}-linux-x64.sha256"
    checksum.write_text(f"{hashlib.sha256(binary.read_bytes()).hexdigest()}  OpenSCCT.Launcher-linux-x64\n", encoding="utf-8")
    (output / (archive.name + ".sha256")).write_text(
        f"{hashlib.sha256(archive.read_bytes()).hexdigest()}  {archive.name}\n", encoding="utf-8")
    return archive, checksum


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("binary", type=Path)
    parser.add_argument("output", type=Path, nargs="?", default=Path("local/dist"))
    args = parser.parse_args()
    for result in package(args.binary.resolve(), args.output.resolve(), Path(__file__).resolve().parents[2]):
        print(result)
