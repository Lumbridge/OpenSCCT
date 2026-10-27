"""Hand-written synthetic bytecode and packages for the unit tests (no game data).

Asm assembles statements with symbolic jump labels: each item knows its file bytes and its in-memory
size, so jump offsets are resolved in the in-memory offset space the decoder uses.
"""

from __future__ import annotations

import struct
from typing import Dict, List, Tuple

from ucdecomp.bytecode import write_compact


def u16(v: int) -> bytes:
    return struct.pack("<H", v)


def u32(v: int) -> bytes:
    return struct.pack("<I", v)


def i32(v: int) -> bytes:
    return struct.pack("<i", v)


# Expression fragments: (file bytes, in-memory size). Object refs and names are single-byte compacts here.
def local(ref: int):
    return bytes([0x00]) + write_compact(ref), 5


def inst(ref: int):
    return bytes([0x01]) + write_compact(ref), 5


def boolvar(e):
    return bytes([0x2D]) + e[0], 1 + e[1]


def true_():
    return b"\x27", 1


def int_zero():
    return b"\x25", 1


def byte_const(v: int):
    return bytes([0x24, v]), 2


def int_byte(v: int):
    return bytes([0x2C, v]), 2


def native(tok: int, *args):
    b = bytes([tok]) + b"".join(a[0] for a in args) + b"\x16"
    return b, 1 + sum(a[1] for a in args) + 1


def let(a, b):
    return b"\x0F" + a[0] + b[0], 1 + a[1] + b[1]


def ret(e=None):
    e = e or (b"\x0B", 1)
    return b"\x04" + e[0], 1 + e[1]


def nothing():
    return b"\x0B", 1


def stop():
    return b"\x08", 1


def iterator_next():
    return b"\x31", 1


def iterator_pop():
    return b"\x30", 1


def name_const(local_idx: int, global_idx: int = 0):
    return b"\x21" + write_compact(local_idx) + write_compact(global_idx), 5


def call(name_idx: int, *args):
    b = b"\x1B" + write_compact(name_idx) + write_compact(0) + b"".join(a[0] for a in args) + b"\x16"
    return b, 1 + 4 + sum(a[1] for a in args) + 1


class Asm:
    """Statements and labels; jump-like items are resolved in build()."""

    def __init__(self):
        self.items: List[tuple] = []

    def label(self, name: str):
        self.items.append(("label", name))
        return self

    def s(self, frag):
        self.items.append(("stmt", frag[0], frag[1]))
        return self

    def jump(self, target: str):
        self.items.append(("jump", target))
        return self

    def jumpifnot(self, target: str, cond):
        self.items.append(("jin", target, cond))
        return self

    def switch(self, e, size: int = 0):
        self.items.append(("stmt", bytes([0x05, size]) + e[0], 2 + e[1]))
        return self

    def case(self, target: str, value):
        self.items.append(("case", target, value))
        return self

    def default(self):
        self.items.append(("stmt", b"\x0A\xFF\xFF", 3))
        return self

    def foreach(self, target: str, call_frag):
        self.items.append(("iter", target, call_frag))
        return self

    def label_table(self, entries: List[Tuple[int, str]], none_idx: int = 0):
        """entries: (name index, label name); terminated by None (name index none_idx)."""
        self.items.append(("ltab", entries, none_idx))
        return self

    @staticmethod
    def _size(it) -> int:
        k = it[0]
        if k == "stmt":
            return it[2]
        if k == "jump":
            return 3
        if k == "jin":
            return 3 + it[2][1]
        if k == "case":
            return 3 + it[2][1]
        if k == "iter":
            return 1 + it[2][1] + 2
        if k == "ltab":
            return 1 + 8 * (len(it[1]) + 1)
        return 0

    def build(self) -> Tuple[bytes, int, Dict[str, int]]:
        pos: Dict[str, int] = {}
        m = 0
        for it in self.items:
            if it[0] == "label":
                pos[it[1]] = m
            m += self._size(it)
        out = bytearray()
        for it in self.items:
            k = it[0]
            if k == "stmt":
                out += it[1]
            elif k == "jump":
                out += b"\x06" + u16(pos[it[1]])
            elif k == "jin":
                out += b"\x07" + u16(pos[it[1]]) + it[2][0]
            elif k == "case":
                out += b"\x0A" + u16(pos[it[1]]) + it[2][0]
            elif k == "iter":
                out += b"\x2F" + it[2][0] + u16(pos[it[1]])
            elif k == "ltab":
                out += b"\x0C"
                for nidx, lname in it[1]:
                    out += write_compact(nidx) + write_compact(0) + u32(pos[lname])
                out += write_compact(it[2]) + write_compact(0) + u32(0xFFFF)
        return bytes(out), m, pos


# ------------------------------------------------------------------------------------------------
# A minimal v300 package: XOR-obfuscated names, import and export tables, UField exports.
def name_ref(i: int) -> bytes:
    return write_compact(i) + write_compact(i)


class PackageWriter:
    def __init__(self, names: List[str]):
        self.names = list(names)
        self.imports: List[bytes] = []
        self.exports: List[dict] = []

    def n(self, s: str) -> int:
        return self.names.index(s)

    def add_import(self, class_package: str, class_name: str, outer: int, name: str) -> int:
        self.imports.append(name_ref(self.n(class_package)) + name_ref(self.n(class_name)) + i32(outer)
                            + name_ref(self.n(name)))
        return -len(self.imports)

    def add_export(self, class_ref: int, super_ref: int, outer: int, name: str, flags: int, data: bytes) -> int:
        self.exports.append(dict(c=class_ref, s=super_ref, o=outer, n=self.n(name), f=flags, d=data))
        return len(self.exports)

    def set_data(self, ref: int, data: bytes):
        self.exports[ref - 1]["d"] = data

    def build(self) -> bytes:
        header = 64
        out = bytearray(header)
        name_off = len(out)
        for s in self.names:
            raw = s.encode("latin1") + b"\0"
            out += write_compact(len(raw))
            p = len(out)
            out += bytes(b ^ ((p + j) & 0xFF) for j, b in enumerate(raw))
            out += u32(0)
        imp_off = len(out)
        for imp in self.imports:
            out += imp
        data_offs = []
        for e in self.exports:
            data_offs.append(len(out))
            out += e["d"]
        exp_off = len(out)
        for e, off in zip(self.exports, data_offs):
            out += write_compact(e["c"]) + write_compact(e["s"]) + i32(e["o"]) + name_ref(e["n"]) + u32(e["f"])
            out += write_compact(len(e["d"]))
            if e["d"]:
                out += write_compact(off)
        struct.pack_into("<IHHI", out, 0, 0x9E2A83C1, 300, 0, 1)
        struct.pack_into("<6i", out, 12, len(self.names), name_off, len(self.exports), exp_off,
                         len(self.imports), imp_off)
        return bytes(out)
