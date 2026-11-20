import gzip
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import publish_store


class ImmutableStoreTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.stage = self.root / "stage"
        self.stage.mkdir()
        self.body = b"known immutable content"
        self.raw = hashlib.sha256(self.body).hexdigest()
        self.name = self.raw + ".gz"
        self.path = self.stage / self.name
        self.path.write_bytes(gzip.compress(self.body, mtime=1))

    def row(self, data):
        return {"state": "uploaded", "id": 4, "size": len(data),
                "digest": "sha256:" + hashlib.sha256(data).hexdigest()}

    def check(self, data):
        with patch.object(publish_store, "download_asset", side_effect=lambda _repo, _row, path: path.write_bytes(data)):
            return publish_store.verify(self.path.name, [self.path], self.row(data), "owner/repo", self.root)

    def test_identical_digest_skips_download(self):
        with patch.object(publish_store, "download_asset", side_effect=AssertionError("unexpected download")):
            item = publish_store.verify(self.name, [self.path], self.row(self.path.read_bytes()), "", self.root)
        self.assertEqual(item["status"], "same-file")

    def test_gzip_with_different_header_requires_matching_content_and_size(self):
        self.assertEqual(self.check(gzip.compress(self.body, mtime=2))["status"], "same-gzip-content")
        with self.assertRaisesRegex(ValueError, "decompressed SHA-256"):
            self.check(gzip.compress(self.body.upper(), mtime=2))
        with self.assertRaisesRegex(ValueError, "expected uploaded size"):
            self.check(gzip.compress(self.body, mtime=2) + b"\0")

    def test_conflicting_metadata_prevents_any_upload(self):
        manifest = self.stage / "files.json"
        manifest.write_bytes(b"right")
        with patch.object(publish_store, "store_assets", return_value={manifest.name: self.row(b"wrong")}), \
             patch.object(publish_store.subprocess, "run") as run, self.assertRaisesRegex(ValueError, "different SHA-256"):
            publish_store.publish("owner/repo", "store", [self.stage], None, True)
        run.assert_not_called()

    def test_local_file_must_match_hash_in_filename(self):
        self.path.write_bytes(gzip.compress(b"wrong", mtime=1))
        with self.assertRaisesRegex(ValueError, "decompressed SHA-256"):
            publish_store.verify(self.name, [self.path], None, "", self.root)

    def test_only_absent_files_uploaded_and_observed_afterwards(self):
        row = self.row(self.path.read_bytes())
        with patch.object(publish_store, "store_assets", side_effect=[{}, {self.name: row}]), \
             patch.object(publish_store.subprocess, "run") as run:
            publish_store.publish("owner/repo", "store", [self.stage], None, True)
        run.assert_called_once_with(["gh", "release", "upload", "store", "--repo", "owner/repo", str(self.path.resolve())], check=True)

    def test_inherited_blob_size_must_match_the_player_manifest(self):
        manifest = self.stage / "OpenSCCT-1.0.0-linux-x64-files.json"
        manifest.write_text(json.dumps({"files": [{"blob": {"tag": "store", "name": "archive.zip", "size": 18}}]}))
        with self.assertRaisesRegex(ValueError, "expects 18 compressed bytes, found 17"):
            publish_store.verify_manifest_sizes({manifest.name: [manifest]}, {"archive.zip": {"size": 17}}, "store")


if __name__ == "__main__":
    unittest.main()
