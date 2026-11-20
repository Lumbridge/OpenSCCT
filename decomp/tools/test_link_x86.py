"""Exercise the two supported i386 relocations on a project-authored COFF fixture."""

import struct
import unittest

from link_x86 import link_function


def object_file(second_relocation=0x14, second_position=4):
    header = struct.pack("<HHIIIHH", 0x14c, 1, 0, 88, 3, 0, 0)
    section = struct.pack("<8sIIIIIIHHI", b".text", 0, 0, 8, 60, 68, 0, 2, 0, 0x60000020)
    data = struct.pack("<II", 8, 4)
    relocations = struct.pack("<IIHIIH", 0, 1, 6, second_position, 2, second_relocation)
    symbols = b"".join(struct.pack("<8sIhHBB", name, 0, sec, kind, 2, 0)
                       for name, sec, kind in [(b"_unit", 1, 0x20), (b"_data", 0, 0), (b"_callee", 0, 0x20)])
    return header + section + data + relocations + symbols + struct.pack("<I", 4)


class LinkTests(unittest.TestCase):
    def setUp(self):
        self.bindings = {"_data": {"va": 0x400000, "basis": "fixture data"},
                         "_callee": {"va": 0x40f00, "basis": "fixture function"}}

    def test_internal_addends_and_backward_relative_call(self):
        linked = link_function(object_file(), "_unit", 8, 0x41000, self.bindings)
        self.assertEqual(struct.unpack("<Ii", linked), (0x400008, -0x104))

    def test_rejects_incomplete_and_unexplained_links(self):
        for obj, bindings in [
            (object_file(second_relocation=3), self.bindings),
            (object_file(second_position=2), self.bindings),
            (object_file(), {k: v for k, v in self.bindings.items() if k != "_data"}),
            (object_file(), dict(self.bindings, unused={"va": 1, "basis": "unused"})),
        ]:
            with self.subTest(bindings=bindings), self.assertRaises(ValueError):
                link_function(obj, "_unit", 8, 0x41000, bindings)
        with self.assertRaises(ValueError):
            link_function(object_file(), "_unit", 7, 0x41000, self.bindings)
