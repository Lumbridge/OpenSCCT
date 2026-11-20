"""Regression tests use only synthetic executable headers and project-authored input."""

from copy import deepcopy
from pathlib import Path
import struct
import tempfile
import unittest

from images import coff_symbol_offset, inspect
from progress import match, summary, verify


def pe():
    data = bytearray(2048)
    data[:2] = b"MZ"
    struct.pack_into("<I", data, 60, 128)
    data[128:132] = b"PE\0\0"
    struct.pack_into("<HH", data, 132, 0x14C, 1)
    struct.pack_into("<H", data, 148, 224)
    struct.pack_into("<H", data, 152, 0x10B)
    struct.pack_into("<8s4I", data, 376, b".text", 100, 4096, 512, 1024)
    struct.pack_into("<I", data, 412, 0x60000020)
    data[1024:1028] = b"TEST"
    return data


def elf():
    data = bytearray(256)
    data[:7] = b"\x7fELF\x01\x01\x01"
    struct.pack_into("<HH", data, 16, 2, 8)
    struct.pack_into("<I", data, 32, 64)
    struct.pack_into("<HH", data, 46, 40, 2)
    struct.pack_into("<6I", data, 104, 0, 1, 7, 4096, 160, 32)
    return data


def target(data):
    return dict(id="synthetic", label="Synthetic", scope="Test", image=inspect(data), units=[])


class ProgressTests(unittest.TestCase):
    def test_ps2_mixed_segment_has_no_percentage(self):
        item = target(elf())
        result = summary(Path("ps2"), item)
        self.assertIsNone(result["total_bytes"])
        self.assertIsNone(result["percent"])
        item["code_map"] = dict(basis="Synthetic layout", ranges=[dict(offset=160, size=16)])
        self.assertEqual(summary(Path("ps2"), item)["total_bytes"], 16)

    def test_match_roundtrip_and_rejections(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp) / "pc"
            folder.mkdir()
            source = folder / "example.c"
            source.write_text("int sample(void) { return 0; }")
            original = Path(tmp) / "retail"
            rebuilt = Path(tmp) / "candidate"
            original.write_bytes(pe())
            rebuilt.write_bytes(b"TEST")
            item = target(pe())
            unit = dict(id="sample", offset=1024, size=4, rebuilt_offset=0,
                        inputs=["example.c"], toolchain="synthetic", build_command="synthetic compiler")
            item["units"] = [unit]
            self.assertEqual(summary(folder, item)["percent"], 0)
            match(folder, item, "sample", original, rebuilt)
            provisional = summary(folder, item)
            self.assertEqual((provisional["percent"], provisional["candidate_bytes"]), (0, 4))
            note = folder / "compiler.md"
            note.write_text("Synthetic compiler and flag evidence for fixture.", encoding="utf-8")
            unit["recipe_evidence"] = dict(compiler="synthetic", flags=[], basis=note.name)
            with self.assertRaisesRegex(ValueError, "changed build inputs"):
                summary(folder, item)
            match(folder, item, "sample", original, rebuilt)
            self.assertEqual(summary(folder, item)["percent"], 4)
            self.assertEqual(summary(folder, item)["candidate_units"], 0)
            note.write_text("Changed evidence", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "changed build inputs"):
                summary(folder, item)
            note.write_text("Synthetic compiler and flag evidence for fixture.", encoding="utf-8")
            verify(item, original)
            with self.assertRaisesRegex(ValueError, "rebuilt artifact"):
                match(folder, item, "sample", original, original)
            rebuilt.write_bytes(b"FAIL")
            with self.assertRaisesRegex(ValueError, "differs from retail"):
                match(folder, item, "sample", original, rebuilt)
            changed = deepcopy(item)
            changed["units"][0]["match"]["bytes_sha256"] = "0" * 64
            with self.assertRaisesRegex(ValueError, "receipt differs"):
                verify(changed, original)
            overlap = deepcopy(item)
            overlap["units"].append(dict(unit, id="second"))
            with self.assertRaisesRegex(ValueError, "overlapping"):
                summary(folder, overlap)
            source.write_text("int sample(void) { return 1; }")
            with self.assertRaisesRegex(ValueError, "changed build inputs"):
                summary(folder, item)
            original.write_bytes(pe() + b"another revision")
            with self.assertRaisesRegex(ValueError, "locked build"):
                verify(item, original)

    def test_file_padding_is_excluded(self):
        item = target(pe())
        self.assertEqual(summary(Path("pc"), item)["total_bytes"], 100)

    def test_coff_function_bounds_and_relocations(self):
        data = bytearray(110)
        struct.pack_into("<HHIIIHH", data, 0, 0x14C, 1, 0, 76, 1, 0, 0)
        struct.pack_into("<II", data, 36, 4, 60)
        struct.pack_into("<I", data, 56, 0x60000020)
        struct.pack_into("<8sIhHBB", data, 76, b"_foo@0", 0, 1, 0x20, 2, 0)
        struct.pack_into("<I", data, 94, 4)
        self.assertEqual(coff_symbol_offset(data, "_foo@0", 4), 60)
        with self.assertRaisesRegex(ValueError, "complete function"):
            coff_symbol_offset(data, "_foo@0", 3)
        struct.pack_into("<H", data, 52, 1)
        with self.assertRaisesRegex(ValueError, "relocations"):
            coff_symbol_offset(data, "_foo@0", 4)


if __name__ == "__main__":
    unittest.main()
