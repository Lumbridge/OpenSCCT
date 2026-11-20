import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

PS2 = Path(__file__).resolve().parents[1] / "ps2"
sys.path.insert(0, str(PS2))
import workflow


class Ps2FunctionTests(unittest.TestCase):
    def test_ee_offsets_require_one_aligned_file_backed_section(self):
        image = {"executable_sections": [{"address": 0x1000, "offset": 128, "size": 16}]}
        self.assertEqual(workflow.file_offset(image, 0x1008, 8), 136)
        for address, size in [(0x1001, 4), (0x1000, 2), (0x1000, 0), (0x100c, 8), (0x0ffc, 4)]:
            with self.subTest(address=address, size=size), self.assertRaises(ValueError):
                workflow.file_offset(image, address, size)

    def test_wrong_image_fails_before_splitting(self):
        with tempfile.TemporaryDirectory() as tmp:
            original = Path(tmp) / "wrong.elf"
            original.write_bytes(bytes(64))
            result = subprocess.run([sys.executable, str(PS2 / "workflow.py"), "disassemble",
                                     "sles-53287-online", str(original), "--range", "0x100000", "8"],
                                    capture_output=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn(b"MIPS ELF32", result.stderr)

    @unittest.skipUnless(shutil.which("objdiff-cli") and shutil.which("mipsel-linux-gnu-gcc"),
                         "requires PS2 toolchain")
    def test_entire_symbol_and_relocations(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp)
            unit = {"symbol": "sample", "inputs": ["sample.c"], "cflags": ["-O2"]}
            with patch.object(workflow, "ROOT", folder):
                (folder / "sample.c").write_text("unsigned sample(unsigned a) { return a ^ 13; }\n")
                subprocess.run(["mipsel-linux-gnu-gcc", "-O2", "-c", "sample.c", "-o", "first.o"],
                               cwd=folder, check=True)
                expected = workflow.compiled_function(folder / "first.o", "sample")
                workflow.verify(folder, unit, expected)
                self.assertEqual((folder / "candidate.bin").read_bytes(), expected)
                self.assertIn("left", json.loads((folder / "objdiff.json").read_text()))
                for different in [expected[:-4], expected[:-1] + bytes([expected[-1] ^ 1])]:
                    with self.assertRaisesRegex(ValueError, "differs from retail"):
                        workflow.verify(folder, unit, different)
                    self.assertFalse((folder / "candidate.bin").exists())
                (folder / "sample.c").write_text("extern int other(void); int sample(void) { return other(); }\n")
                with self.assertRaisesRegex(ValueError, "pending relocations"):
                    workflow.verify(folder, unit, expected)
