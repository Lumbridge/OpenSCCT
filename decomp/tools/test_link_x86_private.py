"""Check VC71's separate COFF contributions for a caller and its static helper."""
import struct
import unittest

from link_x86 import link_function


def private_fixture(*, addend=0, relocation=0x14, private_offset=0, private_section=2,
                    private_kind=0x20, flags=0x60000020, duplicate=False):
    # The caller has a REL32 call. The callee returns an external object's address.
    first = b"\xe8" + struct.pack("<I", addend) + b"\xc3"
    second = b"\xb8" + bytes(4) + b"\xc3"
    functions = [(b"_outer", 0, 1, 0x20, 2),
                 (b"_inner", private_offset, private_section, private_kind, 3),
                 (b"_data", 0, 0, 0, 2)]
    if duplicate:
        functions.append((b"_also", 0, 2, 0x20, 2))
    symbols = b"".join(struct.pack("<8sIhHBB", *f, 0) for f in functions)
    header = struct.pack("<HHIIIHH", 0x14c, 2, 0, 132, len(functions), 0, 0)
    outer = struct.pack("<8sIIIIIIHHI", b".text", 0, 0, 6, 100, 106, 0, 1, 0, 0x60000020)
    inner = struct.pack("<8sIIIIIIHHI", b".text", 0, 0, 6, 116, 122, 0, 1, 0, flags)
    return (header + outer + inner + first + struct.pack("<IIH", 1, 1, relocation) +
            second + struct.pack("<IIH", 1, 2, 6) + symbols + struct.pack("<I", 4))


class PrivateFunctionTests(unittest.TestCase):
    def setUp(self):
        self.callee = {"_inner": {"va": 0x401020, "basis": "separate procedure contribution"}}
        self.data = {"_data": {"va": 0x404000, "basis": "external object"}}

    def test_links_and_compares_caller_and_private_callee_separately(self):
        obj = private_fixture()
        self.assertEqual(link_function(obj, "_outer", 6, 0x401000, self.callee),
                         b"\xe8\x1b\x00\x00\x00\xc3")
        self.assertEqual(link_function(obj, "_inner", 6, 0x401020, self.data),
                         b"\xb8\x00\x40\x40\x00\xc3")

    def test_rejects_invalid_private_procedure_references(self):
        for invalid in ({"addend": 1}, {"relocation": 6}, {"private_offset": 1},
                        {"private_section": 0}, {"private_section": 1}, {"private_kind": 0},
                        {"flags": 0x40000040}, {"duplicate": True}):
            with self.subTest(invalid=invalid), self.assertRaises(ValueError):
                link_function(private_fixture(**invalid), "_outer", 6, 0x401000, self.callee)
        with self.assertRaisesRegex(ValueError, "missing link address"):
            link_function(private_fixture(), "_outer", 6, 0x401000, {})
