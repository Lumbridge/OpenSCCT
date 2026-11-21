"""C++ exception frames read FS:[__except_list], which the C runtime defines as absolute 0."""

import struct
import unittest

from link_x86 import link_function


def object_file(external):
    names = b"__except_list\0"
    strings = struct.pack("<I", 4 + len(names)) + names
    header = struct.pack("<HHIIIHH", 0x14c, 1, 0, 74, 2, 0, 0)
    section = struct.pack("<8sIIIIIIHHI", b".text", 0, 0, 4, 60, 64, 0, 1, 0, 0x60000020)
    data = struct.pack("<I", 0)
    relocation = struct.pack("<IIH", 0, 1, 6)
    unit = struct.pack("<8sIhHBB", b"_unit", 0, 1, 0x20, 2, 0)
    label = struct.pack("<II", 0, 4) if external == "__except_list" else external.encode().ljust(8, b"\0")
    target = struct.pack("<8sIhHBB", label, 0, 0, 0, 2, 0)
    return header + section + data + relocation + unit + target + strings


class ExceptListTests(unittest.TestCase):
    def test_except_list_links_at_absolute_zero(self):
        bindings = {"__except_list": {"va": 0, "basis": "C runtime absolute TIB offset"}}
        self.assertEqual(link_function(object_file("__except_list"), "_unit", 4, 0x41000, bindings), bytes(4))

    def test_other_externals_still_reject_zero(self):
        with self.assertRaisesRegex(ValueError, "invalid address"):
            link_function(object_file("_data"), "_unit", 4, 0x41000, {"_data": {"va": 0, "basis": "fixture"}})


if __name__ == "__main__":
    unittest.main()
