"""Tests on synthetic memory images: the walker, the value decoder, both name readers and the diff.

    python -I -B -m unittest discover -s re/tools/reflect -p "test_*.py"
"""
import json
import os
import struct
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import dump  # noqa: E402
import procread  # noqa: E402
import ue2  # noqa: E402

EDITOR = ue2.PROFILES['editor']
OBJ_BASE = 0x30000000
OBJ_SPAN = 0x200


class Image:
    """A tiny editor-shaped address space built field by field."""

    def __init__(self):
        self.blocks = {}
        self.names = []
        self.objects = []      # (name, class index, outer index)

    def word(self, address, value, fmt='<I'):
        base = address & ~0xFFFFF
        block = self.blocks.setdefault(base, bytearray(0x100000))
        struct.pack_into(fmt, block, address - base, value)

    def name(self, text):
        if text not in self.names:
            self.names.append(text)
        return self.names.index(text)

    def obj(self, name, cls=None, outer=None):
        """Index of a new object; cls and outer are object indices (None = itself / none)."""
        self.objects.append((name, cls, outer))
        return len(self.objects) - 1

    @staticmethod
    def addr(i):
        return OBJ_BASE + i * OBJ_SPAN

    def build(self):
        names_data, objs_data = 0x21000000, 0x20000000
        for name, _, _ in self.objects:
            self.name(name)
        self.word(EDITOR['names'][1], names_data)
        self.word(EDITOR['names'][1] + 4, len(self.names))
        self.word(EDITOR['names'][1] + 8, len(self.names))
        for i, text in enumerate(self.names):
            entry = 0x22000000 + i * 0x50
            self.word(names_data + 4 * i, entry)
            self.word(entry, i)
            for j, ch in enumerate(text.encode('latin-1') + b'\0'):
                self.word(entry + 12 + j, ch, '<B')
        self.word(EDITOR['gobjects'], objs_data)
        self.word(EDITOR['gobjects'] + 4, len(self.objects))
        self.word(EDITOR['gobjects'] + 8, len(self.objects))
        for i, (name, cls, outer) in enumerate(self.objects):
            a = self.addr(i)
            self.word(objs_data + 4 * i, a)
            self.word(a + ue2.O_VTABLE, 0x11400000 + (cls if cls is not None else i) * 0x10)
            self.word(a + ue2.O_INDEX, i)
            self.word(a + ue2.O_OUTER, self.addr(outer) if outer is not None else 0)
            self.word(a + ue2.O_NAME, self.name(name))
            self.word(a + ue2.O_CLASS, self.addr(cls if cls is not None else i))
        # the image itself: an MZ/PE header so pe_image works, and room for vtables
        base = EDITOR['imageBase']
        self.word(base, 0x5A4D, '<H')
        self.word(base + 0x3C, 0x80)
        self.word(base + 0x80, 0x00004550)
        self.word(base + 0x80 + 24 + 28, base)
        self.word(base + 0x80 + 24 + 56, 0x00D93000)
        return procread.BytesMemory({k: bytes(v) for k, v in self.blocks.items()})


def sample():
    im = Image()
    for n in ('None', 'Core'):
        im.name(n)
    k_class = im.obj('Class')                      # Class of itself
    k_package = im.obj('Package', k_class)
    core = im.obj('Core', k_package)
    k_int = im.obj('IntProperty', k_class, core)
    k_bool = im.obj('BoolProperty', k_class, core)
    k_byte = im.obj('ByteProperty', k_class, core)
    k_func = im.obj('Function', k_class, core)
    k_enum = im.obj('Enum', k_class, core)
    obj = im.obj('Object', k_class, core)
    foo = im.obj('Foo', k_int, obj)
    baz = im.obj('bBaz', k_bool, obj)
    color = im.obj('Color', k_byte, obj)
    bar = im.obj('Bar', k_func, obj)
    ecolor = im.obj('EColor', k_enum, obj)
    for i in (k_class, k_package, k_int, k_bool, k_byte, k_func, k_enum):
        im.word(im.addr(i) + 0x30, 0)
    a = im.addr
    cl, pl = EDITOR['class'], EDITOR['property']
    # Object: children Foo -> bBaz -> Color -> Bar -> EColor; PropertyLink Foo -> bBaz -> Color
    im.word(a(obj) + ue2.S_CHILDREN, a(foo))
    im.word(a(foo) + ue2.F_NEXT, a(baz))
    im.word(a(baz) + ue2.F_NEXT, a(color))
    im.word(a(color) + ue2.F_NEXT, a(bar))
    im.word(a(bar) + ue2.F_NEXT, a(ecolor))
    im.word(a(obj) + ue2.S_PROPLINK, a(foo))
    im.word(a(foo) + ue2.P_LINKNEXT, a(baz))
    im.word(a(baz) + ue2.P_LINKNEXT, a(color))
    im.word(a(obj) + ue2.S_SIZE, 0x30)
    im.word(a(obj) + ue2.S_SCRIPTSIZE, 16)
    im.word(a(obj) + cl['flags'], 0x112)
    # properties
    for p, off, size, flags in ((foo, 0x28, 4, 0x21), (baz, 0x2C, 4, 0x1), (color, 0x2D, 1, 0x1)):
        im.word(a(p) + ue2.P_DIM, 1, '<H')
        im.word(a(p) + ue2.P_SIZE, size, '<H')
        im.word(a(p) + ue2.P_FLAGS, flags)
        im.word(a(p) + ue2.P_OFFSET, off)
    im.word(a(foo) + pl['repOffset'], 3, '<H')
    im.word(a(baz) + pl['extra'], 0x100)
    im.word(a(color) + pl['extra'], a(ecolor))
    # function
    im.word(a(bar) + ue2.FN_FLAGS, 0x401)
    im.word(a(bar) + ue2.FN_FUNC, EDITOR['imageBase'] + 0x1000)
    im.word(a(bar) + ue2.FN_INATIVE, 5, '<H')
    im.word(a(bar) + ue2.FN_PARMSSIZE, 0, '<H')
    im.word(a(bar) + ue2.FN_RETOFFSET, 0xFFFF, '<H')
    im.word(a(bar) + ue2.S_SCRIPTSIZE, 2)
    # enum names
    names_at = 0x23000000
    im.word(a(ecolor) + ue2.E_NAMES, names_at)
    im.word(a(ecolor) + ue2.E_NAMES + 4, 2)
    im.word(names_at, im.name('Red'))
    im.word(names_at + 4, im.name('Green'))
    # class defaults: Foo = 42, bBaz set (mask 0x100), Color = 1 (Green)
    defaults = 0x24000000
    im.word(a(obj) + cl['defaults'], defaults)
    im.word(a(obj) + cl['defaults'] + 4, 0x30)
    im.word(a(obj) + cl['defaults'] + 8, 0x30)
    im.word(defaults + 0x28, 42)
    im.word(defaults + 0x2C, 0x100)
    im.word(defaults + 0x2D, 1, '<B')
    return im


class WalkerTest(unittest.TestCase):
    def setUp(self):
        self.im = sample()
        self.memory = self.im.build()
        self.engine = ue2.Engine(self.memory, 'editor', image_size=0x00D93000, min_objects=1)

    def test_names_and_objects(self):
        self.assertEqual(self.engine.name_info['indexMismatches'], 0)
        self.assertEqual(self.engine.checks['objectTable']['live'], len(self.im.objects))
        self.assertEqual(self.engine.name_info['objectsWithUnresolvedName'], 0)

    def test_reflect(self):
        out = self.engine.reflect()
        classes = {c['path']: c for c in out['classes']}
        self.assertIn('Core.Object', classes)
        self.assertEqual(classes['Core.Object']['propertiesSize'], 0x30)
        self.assertEqual(classes['Core.Object']['flagNames'], ['Compiled', 'Parsed', 'NoExport'])
        props = {p['path']: p for p in out['properties']}
        foo = props['Core.Object.Foo']
        self.assertEqual((foo['kind'], foo['offset'], foo['elementSize'], foo['repOffset']), ('IntProperty', 0x28, 4, 3))
        self.assertEqual(foo['flagNames'], ['Edit', 'Net'])
        self.assertEqual(props['Core.Object.bBaz']['bitMask'], '0x00000100')
        self.assertEqual(props['Core.Object.Color']['enum'], 'Core.Object.EColor')
        bar = {f['path']: f for f in out['functions']}['Core.Object.Bar']
        self.assertEqual((bar['iNative'], bar['flagNames'], bar['funcRva'], bar['scriptSize']),
                         (5, ['Final', 'Native'], '0x001000', 2))
        self.assertIsNone(bar['returnValueOffset'])
        self.assertEqual(out['enums'][0]['names'], ['Red', 'Green'])
        self.assertEqual(self.engine.checks['classClassSelf'], ['Class'])

    def test_class_defaults(self):
        cdo = {c['path']: c for c in self.engine.class_defaults(['Core'])}['Core.Object']
        self.assertEqual(cdo['values']['Foo'], 42)
        self.assertIs(cdo['values']['bBaz'], True)
        self.assertEqual(cdo['values']['Color'], {'value': 1, 'enum': 'Green'})
        self.assertEqual(len(cdo['raw']), 0x30 * 2)


class WideNameTest(unittest.TestCase):
    def entry(self, link_a, link_b, text, index):
        raw = bytearray(0x24)
        struct.pack_into('<II', raw, 0, link_a, link_b)
        encoded = text.encode('utf-16-le') + b'\0\0'
        raw[8:8 + len(encoded)] = encoded
        struct.pack_into('<III', raw, 0x18, len(text), 7, index)
        return bytes(raw)

    def test_short_entry(self):
        got = ue2.decode_wide_entry(self.entry(0, 0, 'Pawn', 1234), lambda a, n: b'')
        self.assertEqual(got[:2], (1234, 'Pawn'))

    def test_rejects_unterminated_and_bad_capacity(self):
        raw = bytearray(self.entry(0, 0, 'Pawn', 1))
        struct.pack_into('<I', raw, 0x1C, 8)
        self.assertIsNone(ue2.decode_wide_entry(bytes(raw), lambda a, n: b''))

    def test_scan_prefers_linked_entries(self):
        base = 0x05000000
        a = self.entry(base + 0x40, base + 0x40, 'Actor', 7)
        b = self.entry(base, base, 'Pawn', 8)
        loose = self.entry(0, 0, 'Loose', 9)
        heap = a.ljust(0x40, b'\0') + b.ljust(0x40, b'\0') + loose.ljust(0x40, b'\0')
        names, info = ue2.scan_wide_names(procread.BytesMemory({base: heap}))
        self.assertEqual(names, {7: 'Actor', 8: 'Pawn', 9: 'Loose'})
        self.assertEqual((info['strict'], info['relaxed']), (2, 1))


class DiffTest(unittest.TestCase):
    def test_diff_counts(self):
        a = {'process': 'editor', 'classes': [{'path': 'X.A', 'propertiesSize': 4}, {'path': 'X.B', 'propertiesSize': 8}],
             'properties': [{'path': 'X.A.p', 'offset': 0}], 'functions': [], 'states': [], 'structs': [], 'enums': [],
             'nameTable': {'1': 'A', '2': 'B'}}
        b = {'process': 'game', 'classes': [{'path': 'X.A', 'propertiesSize': 8}],
             'properties': [{'path': 'X.A.p', 'offset': 4}], 'functions': [], 'states': [], 'structs': [], 'enums': [],
             'nameTable': {'1': 'A', '2': 'C'}}
        with tempfile.TemporaryDirectory() as d:
            pa, pb, out = (os.path.join(d, n) for n in ('a.json', 'b.json', 'diff.json'))
            for path, value in ((pa, a), (pb, b)):
                with open(path, 'w', encoding='utf-8') as f:
                    json.dump(value, f)
            summary = dump.diff(pa, pb, out)
        self.assertEqual(summary['classes']['onlyA'], 1)
        self.assertEqual(summary['classes']['changedFields'], {'propertiesSize': 1})
        self.assertEqual(summary['properties']['changedFields'], {'offset': 1})
        self.assertEqual(summary['names']['sameText'], 1)


if __name__ == '__main__':
    unittest.main()
