import json
from pathlib import Path
import tempfile
import unittest

from manifests import load_target, save_receipt
from progress import write


class ManifestTests(unittest.TestCase):
    def test_inline_and_split_units_share_one_view_and_keep_ownership(self):
        with tempfile.TemporaryDirectory() as tmp:
            edition = Path(tmp) / "pc"
            (edition / "config").mkdir(parents=True)
            path = edition / "config/target.json"
            inline = {"id": "one", "offset": 20}
            separate = {"id": "two", "offset": 10}
            write(path, {"units": [inline], "unit_files": ["other.json"]})
            write(edition / "other.json", [separate])
            self.assertEqual(load_target(path)["units"], [separate, inline])
            receipt = dict(separate, match={"bytes_sha256": "fixture"})
            save_receipt(path, receipt, write)
            self.assertEqual(json.loads((edition / "other.json").read_text()), [receipt])
            self.assertEqual(json.loads(path.read_text())["units"], [inline])
            with self.assertRaisesRegex(ValueError, "changed"):
                save_receipt(path, dict(receipt, offset=15), write)
            write(path, {"units": [inline]})
            self.assertEqual(load_target(path)["units"], [inline])

    def test_file_lists_reject_duplicates_escape_and_non_list_files(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "pc"
            (root / "config").mkdir(parents=True)
            path = root / "config/target.json"
            write(root / "unit.json", [])
            for files in [["unit.json", "unit.json"], ["../other.json"], ["config/target.json"]]:
                write(path, {"units": [], "unit_files": files})
                with self.subTest(files=files), self.assertRaises(ValueError):
                    load_target(path)
            write(root / "unit.json", {"unexpected": 1})
            write(path, {"units": [], "unit_files": ["unit.json"]})
            with self.assertRaises(ValueError):
                load_target(path)
