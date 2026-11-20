"""Exercise compiler-owned switch destination relocations using a synthetic i386 COFF."""
import struct
import unittest

from link_x86 import link_function


def switch_object(*, value=12, section=1, kind=0, storage=3, label=b"$L200",
                  relocation=6, addend=0, second_position=4):
    header = struct.pack("<HHIIIHH", 0x14c, 2, 0, 136, 3, 0, 0)
    text = struct.pack("<8sIIIIIIHHI", b".text", 0, 0, 16, 100, 116, 0, 2, 0, 0x60000020)
    other = struct.pack("<8sIIIIIIHHI", b".other", 0, 0, 0, 0, 0, 0, 0, 0, 0x40000040)
    code = struct.pack("<IIII", 8, addend, 0, 0)
    relocs = struct.pack("<IIHIIH", 0, 1, 6, second_position, 2, relocation)
    symbols = b"".join(struct.pack("<8sIhHBB", *fields, 0) for fields in [
        (b"_unit", 0, 1, 0x20, 2), (b"_data", 0, 0, 0, 2),
        (label, value, section, kind, storage),
    ])
    return header + text + other + code + relocs + symbols + struct.pack("<I", 4)


class SwitchLabelTests(unittest.TestCase):
    bindings = {"_data": {"va": 0x500000, "basis": "fixture external data"}}

    def test_local_destination_follows_compiled_section(self):
        obj = switch_object()
        for address in [0x41000, 0x50000]:
            with self.subTest(address=address):
                linked = link_function(obj, "_unit", 16, address, self.bindings)
                self.assertEqual(struct.unpack("<IIII", linked), (0x500008, address + 12, 0, 0))

    def test_rejects_invalid_local_destinations(self):
        for changes in [
            {"value": 16}, {"value": 17}, {"section": 2}, {"section": 0},
            {"kind": 0x20}, {"storage": 6}, {"label": b"$L"}, {"label": b"$L20x"},
            {"relocation": 0x14}, {"addend": 1}, {"second_position": 2},
        ]:
            with self.subTest(changes=changes), self.assertRaises(ValueError):
                link_function(switch_object(**changes), "_unit", 16, 0x41000, self.bindings)

    def test_extra_binding_cannot_supply_local_label(self):
        bindings = dict(self.bindings, **{"$L200": {"va": 0x500000, "basis": "unrelated external"}})
        with self.assertRaisesRegex(ValueError, "unused"):
            link_function(switch_object(), "_unit", 16, 0x41000, bindings)
