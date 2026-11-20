"""Check staged files against a GitHub release store, then optionally append missing files."""

import argparse
from concurrent.futures import ThreadPoolExecutor
import gzip
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys
import tempfile
from urllib.parse import quote


def digest(stream):
    result = hashlib.sha256()
    for block in iter(lambda: stream.read(1024 * 1024), b""):
        result.update(block)
    return result.hexdigest()


def file_digest(path):
    with path.open("rb") as stream:
        return digest(stream)


def gzip_identity(path, expected):
    with gzip.open(path, "rb") as stream:
        actual = digest(stream)
    if actual != expected:
        raise ValueError(f"{path.name}: decompressed SHA-256 {actual} differs from filename {expected}")
    return actual


def gh_json(endpoint):
    return json.loads(subprocess.check_output(["gh", "api", endpoint]))


def store_assets(repo, tag):
    release = gh_json(f"repos/{repo}/releases/tags/{quote(tag, safe='')}")
    assets = {}
    for page in range(1, 100):
        rows = gh_json(f"repos/{repo}/releases/{release['id']}/assets?per_page=100&page={page}")
        for row in rows:
            name = row["name"]
            if name in assets:
                raise ValueError(f"Duplicate remote store asset name: {name}")
            assets[name] = row
        if len(rows) < 100:
            return assets
    raise ValueError("GitHub release inventory exceeded the page limit")


def download_asset(repo, row, path):
    with path.open("wb") as stream:
        subprocess.run(["gh", "api", "-H", "Accept: application/octet-stream",
                        f"repos/{repo}/releases/assets/{row['id']}"], stdout=stream, check=True)


def verify(name, paths, remote, repo, directory):
    raw = name[:-3] if re.fullmatch(r"[a-f0-9]{64}\.gz", name) else None
    size = paths[0].stat().st_size
    hashes = set()
    for path in paths:
        if path.stat().st_size != size:
            raise ValueError(f"{name}: two staged compressed sizes differ; manifests depend on the exact size")
        hashes.add(file_digest(path))
        if raw:
            gzip_identity(path, raw)
    if len(hashes) > 1 and not raw:
        raise ValueError(f"{name}: staged files have different SHA-256 digests")
    item = {"name": name, "size": size, "stagedSha256": sorted(hashes), "status": "missing"}
    if raw:
        item["contentSha256"] = raw
    if remote is None:
        return item
    if remote["state"] != "uploaded" or remote["size"] != size:
        raise ValueError(f"{name}: remote state={remote['state']} size={remote['size']}, expected uploaded size={size}")
    reported = remote.get("digest")
    if reported and not re.fullmatch(r"sha256:[0-9a-fA-F]{64}", reported):
        raise ValueError(f"{name}: unrecognized remote digest {reported!r}")
    expected = reported[7:].lower() if reported else None
    item["remoteAssetId"] = remote["id"]
    item["remoteSha256"] = expected
    if expected in hashes:
        item["status"] = "same-file"
        return item
    if expected and not raw:
        raise ValueError(f"{name}: existing store file has a different SHA-256")
    stored = Path(directory) / name
    download_asset(repo, remote, stored)
    actual = file_digest(stored)
    if stored.stat().st_size != size or (expected and actual != expected):
        raise ValueError(f"{name}: downloaded store file does not match GitHub's size or SHA-256")
    item["remoteSha256"] = actual
    if actual in hashes:
        item["status"] = "same-file"
    elif raw:
        gzip_identity(stored, raw)
        item["status"] = "same-gzip-content"
    else:
        raise ValueError(f"{name}: downloaded store file has a different SHA-256")
    return item


def save_receipt(path, repo, tag, assets, records):
    if path is not None:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps({"repository": repo, "tag": tag, "storeAssetCount": len(assets),
                                    "assets": records}, indent=2) + "\n", encoding="utf-8")


def verify_manifest_sizes(groups, assets, tag):
    for name, paths in groups.items():
        if not (name.startswith("OpenSCCT-") and name.endswith("-files.json")):
            continue
        for manifest in paths:
            for entry in json.loads(manifest.read_text(encoding="utf-8-sig"))["files"]:
                blob = entry["blob"]
                if blob["tag"] != tag:
                    continue
                blob_name = blob["name"]
                staged = groups.get(blob_name)
                existing = assets.get(blob_name)
                if not staged and not existing:
                    raise ValueError(f"{name}: {blob_name} is missing from both staging and {tag}")
                size = staged[0].stat().st_size if staged else existing["size"]
                if blob["size"] != size:
                    raise ValueError(f"{name}: {blob_name} expects {blob['size']} compressed bytes, found {size}")


def publish(repo, tag, folders, receipt, upload):
    groups = {}
    for folder in folders:
        files = sorted(path for path in folder.iterdir() if path.is_file())
        if not files:
            raise ValueError(f"No staged release assets in {folder}")
        for path in files:
            groups.setdefault(path.name, []).append(path)
    assets = store_assets(repo, tag)
    verify_manifest_sizes(groups, assets, tag)
    with tempfile.TemporaryDirectory(prefix="openscct-store-") as temp:
        def check(name):
            return verify(name, groups[name], assets.get(name), repo, temp)

        with ThreadPoolExecutor(max_workers=8) as pool:
            records = list(pool.map(check, sorted(groups)))
        missing = [item["name"] for item in records if item["status"] == "missing"]
        alternate = sum(item["status"] == "same-gzip-content" for item in records)
        if missing and len(assets) + len(missing) > 990:
            raise ValueError(f"{tag}: {len(assets)} existing + {len(missing)} missing would exceed 990 assets")
        save_receipt(receipt, repo, tag, assets, records)
        print(f"{tag}: {len(groups)} staged names, {len(missing)} missing, "
              f"{alternate} byte-different gzip files with verified identical content")
        if not upload:
            return
        for offset in range(0, len(missing), 20):
            batch = missing[offset:offset+20]
            paths = [str(groups[name][0].resolve()) for name in batch]
            # GitHub atomically rejects an existing name. An interrupted or concurrent upload can be retried.
            subprocess.run(["gh", "release", "upload", tag, "--repo", repo, *paths], check=True)
        if missing:
            after = store_assets(repo, tag)
            for item in records:
                if item["status"] == "missing":
                    row = after.get(item["name"])
                    if row is None:
                        raise ValueError(f"Uploaded asset missing from GitHub: {item['name']}")
                    item.update(verify(item["name"], groups[item["name"]], row, repo, temp))
                    item["status"] = "uploaded"
            save_receipt(receipt, repo, tag, after, records)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", required=True)
    parser.add_argument("--tag", required=True)
    parser.add_argument("--asset-dir", action="append", required=True, type=Path)
    parser.add_argument("--receipt", type=Path)
    parser.add_argument("--upload", action="store_true", help="Append missing assets after all files pass verification")
    args = parser.parse_args()
    try:
        publish(args.repo, args.tag, args.asset_dir, args.receipt, args.upload)
    except (OSError, ValueError, subprocess.CalledProcessError) as exc:
        sys.exit(str(exc))
