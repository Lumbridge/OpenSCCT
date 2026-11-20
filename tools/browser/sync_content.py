"""Mirror verified public map archives for the browser's same-origin downloads.

Shared runtime data is supplied by the browser bundle. This index exposes maps
and optional bot navigation, avoiding the desktop's 1 GB character pack download.
Run on the web host; archives are addressed by SHA-256 and the index switches last.
"""
import argparse
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
import os
from pathlib import Path
import subprocess
from urllib.parse import unquote, urlsplit
from urllib.request import Request, urlopen

INDEX = "https://raw.githubusercontent.com/OpenSCCT/OpenSCCT-Content/main/index.json"
PREFIX = "/OpenSCCT/OpenSCCT-Content/releases/download/"
LEGACY_PREFIX = "/Lumbridge/OpenSCCT-Content/releases/download/"


def approved(download):
    uri = urlsplit(download["url"])
    path = unquote(uri.path)
    if (uri.scheme != "https" or uri.netloc != "github.com" or not path.startswith((PREFIX, LEGACY_PREFIX))
            or any(part in (".", "..") for part in path.split("/")) or uri.query or uri.fragment):
        raise ValueError("Not an official OpenSCCT content release: " + download["url"])
    sha = download["sha256"].lower()
    if len(sha) != 64 or any(c not in "0123456789abcdef" for c in sha):
        raise ValueError("Invalid archive SHA-256")
    if not 0 < download["size"] <= 1536 * 1024 * 1024:
        raise ValueError("Invalid archive size")
    return sha


def digest(path):
    sha = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            sha.update(chunk)
    return sha.hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--maps", nargs="+", help="Only these map codes (default: every published map)")
    args = parser.parse_args()
    with urlopen(Request(INDEX, headers={"User-Agent": "OpenSCCT-Browser-Content"}), timeout=30) as source:
        raw = source.read(16 * 1024 * 1024 + 1)
    if len(raw) > 16 * 1024 * 1024:
        raise ValueError("Content index exceeds 16 MB")
    index = json.loads(raw)
    if index.get("format") != "openscct.content" or index.get("formatVersion") != 1:
        raise ValueError("Unsupported official content index")
    wanted = {code.lower() for code in args.maps} if args.maps else None
    index["maps"] = [m for m in index["maps"] if wanted is None or m["code"].lower() in wanted]
    if wanted and wanted - {m["code"].lower() for m in index["maps"]}:
        raise ValueError("Unknown map codes")
    index["shared"] = []
    downloads = {}

    def mirror(record):
        sha = approved(record)
        # Older published indexes use the former owner. Fetch their pinned bytes from the current repository.
        source = dict(record)
        legacy = "https://github.com" + LEGACY_PREFIX
        if source["url"].startswith(legacy):
            source["url"] = "https://github.com" + PREFIX + source["url"][len(legacy):]
        if sha in downloads and downloads[sha] != source:
            raise ValueError("Conflicting archive records")
        downloads[sha] = source
        record["url"] = "/play/content/" + sha + ".zip"

    for entry in index["maps"]:
        mirror(entry["full"])
        entry["sim"] = dict(entry["full"])
        if entry.get("botNavigation"):
            mirror(entry["botNavigation"]["download"])
    args.output.mkdir(parents=True, exist_ok=True)

    def fetch(pair):
        sha, record = pair
        target = args.output / (sha + ".zip")
        if target.is_file() and target.stat().st_size == record["size"] and digest(target) == sha:
            return
        partial = target.with_suffix(".part")
        # curl uses the platform trust store, including Windows' intermediate certificate resolution.
        # Keep TLS verification enabled, and forbid redirects to plaintext or non-HTTP protocols.
        subprocess.run(["curl.exe" if os.name == "nt" else "curl", "--fail", "--location", "--silent", "--show-error",
                        "--proto", "=https", "--proto-redir", "=https", "--max-time", "300",
                        "--max-filesize", str(record["size"]), "--output", str(partial), record["url"]], check=True)
        length = partial.stat().st_size
        if length != record["size"] or digest(partial) != sha:
            raise ValueError("Archive does not match published size/SHA-256: " + record["url"])
        os.replace(partial, target)
        print("Verified " + target.name + " (" + str(length) + " bytes)", flush=True)

    print("Mirroring " + str(len(index["maps"])) + " maps, " + str(len(downloads)) + " archives", flush=True)
    with ThreadPoolExecutor(max_workers=4) as pool:
        list(pool.map(fetch, downloads.items()))
    temporary = args.output / "index.pending"
    temporary.write_text(json.dumps(index, separators=(",", ":")), encoding="utf-8")
    os.replace(temporary, args.output / "index.json")
    print("Published browser content index", flush=True)


if __name__ == "__main__":
    main()
