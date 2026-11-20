import struct
import unittest

from progress import built_unit_bytes


class CoffAlignmentTests(unittest.TestCase):
    def setUp(self):
        header = struct.pack("<HHIIIHH", 0x14c, 1, 0, 76, 1, 0, 0)
        section = struct.pack("<8sIIIIIIHHI", b".text", 0, 0, 16, 60, 0, 0, 0, 0, 0x60000020)
        symbol = struct.pack("<8sIhHBB", b"_unit", 0, 1, 0x20, 2, 0)
        self.image = bytes(14) + bytes([0xc3, 0x90])
        self.obj = header + section + self.image + symbol + struct.pack("<I", 4)
        self.target = {"image": {"executable_sections": [{"code": True, "address": 4096, "offset": 0, "size": 16}]}}
        self.unit = {"offset": 0, "size": 15, "rebuilt_symbol": "_unit", "coff_alignment": 16}

    def test_verified_padding_is_excluded_from_coverage(self):
        self.assertEqual(built_unit_bytes(self.target, self.unit, self.obj, self.image), self.image[:-1])

    def test_padding_requires_original_and_compiled_agreement(self):
        for obj, image in ((self.obj, self.image[:-1] + bytes([0xcc])),
                           (self.obj[:75] + bytes([0xcc]) + self.obj[76:], self.image)):
            with self.subTest(), self.assertRaises(ValueError):
                built_unit_bytes(self.target, self.unit, obj, image)

    def test_shortening_the_body_cannot_hide_the_return(self):
        with self.assertRaises(ValueError):
            built_unit_bytes(self.target, dict(self.unit, size=14), self.obj, self.image)
