"""Bytecode <-> token tree.

decode_script() turns the file-form script of one UStruct into a list of top-level statements; each
statement is an Expr tree that records, for every node, its file offset and its in-memory offset (the
offset space ScriptSize, jumps, label tables and RepOffset use). encode_* writes a tree back to file
form; check T1 asserts that this reproduces the original bytes exactly.
"""

from __future__ import annotations

import struct
from typing import Callable, List, Optional, Sequence, Tuple

from . import tokens as T


class DecodeError(Exception):
    pass


class Expr:
    """One token and its operands.

    vals holds one entry per shape item: int for u8/u16/i32/u32/obj, float bits as int for f32 (kept
    as raw u32 so re-encoding is exact; .fval gives the float), (local, global) for name, Expr for
    expr, list[Expr] for params, bytes for cstr, list[int] for ustr, (offset, Expr|None) for case,
    list[((local, global), offset)] for labels (the None terminator included).
    """

    __slots__ = ("tok", "vals", "foff", "fend", "mem", "mend")

    def __init__(self, tok: int, vals: list, foff: int = -1, fend: int = -1, mem: int = -1, mend: int = -1):
        self.tok = tok
        self.vals = vals
        self.foff = foff
        self.fend = fend
        self.mem = mem
        self.mend = mend

    @property
    def shape(self):
        return T.shape_of(self.tok)

    @property
    def native_index(self) -> Optional[int]:
        if self.tok >= T.EX_FirstNative:
            return self.tok
        if self.tok >= T.EX_ExtendedNative:
            return ((self.tok - T.EX_ExtendedNative) << 8) | self.vals[0]
        return None

    @property
    def params(self) -> List["Expr"]:
        shp = self.shape
        for i, k in enumerate(shp):
            if k == "params":
                return self.vals[i]
        return []

    def children(self):
        """Nested expressions in serialization order."""
        for k, v in zip(self.shape, self.vals):
            if k == "expr":
                yield v
            elif k == "params":
                yield from v
            elif k == "case" and v[1] is not None:
                yield v[1]

    def walk(self):
        yield self
        for c in self.children():
            yield from c.walk()

    def __repr__(self):
        return "Expr(%s@%d)" % (T.mnemonic(self.tok), self.mem)


def f32_bits(v: int) -> float:
    return struct.unpack("<f", struct.pack("<I", v & 0xFFFFFFFF))[0]


def read_compact(d: bytes, p: int) -> Tuple[int, int]:
    b = d[p]
    p += 1
    n = b & 0x3F
    neg = b & 0x80
    more = b & 0x40
    shift = 6
    while more:
        b = d[p]
        p += 1
        n |= (b & 0x7F) << shift
        shift += 7
        more = b & 0x80
    return (-n if neg else n), p


def write_compact(v: int) -> bytes:
    """Canonical UE compact index."""
    neg = v < 0
    v = -v if neg else v
    out = bytearray()
    b = (0x80 if neg else 0) | (v & 0x3F)
    v >>= 6
    if v:
        b |= 0x40
    out.append(b)
    while v:
        b = v & 0x7F
        v >>= 7
        if v:
            b |= 0x80
        out.append(b)
    return bytes(out)


class Decoder:
    def __init__(self, data: bytes, pos: int = 0, none_name: Callable[[int], bool] = lambda i: i == 0):
        self.d = data
        self.p = pos
        self.mem = 0
        self.none_name = none_name

    # primitives ---------------------------------------------------------------------------------
    def _need(self, n: int):
        if self.p + n > len(self.d):
            raise DecodeError("read past end at file +%d" % self.p)

    def u8(self) -> int:
        self._need(1)
        v = self.d[self.p]
        self.p += 1
        self.mem += 1
        return v

    def u16(self) -> int:
        self._need(2)
        v = struct.unpack_from("<H", self.d, self.p)[0]
        self.p += 2
        self.mem += 2
        return v

    def u32(self) -> int:
        self._need(4)
        v = struct.unpack_from("<I", self.d, self.p)[0]
        self.p += 4
        self.mem += 4
        return v

    def i32(self) -> int:
        self._need(4)
        v = struct.unpack_from("<i", self.d, self.p)[0]
        self.p += 4
        self.mem += 4
        return v

    def obj(self) -> int:
        self._need(1)
        v, self.p = read_compact(self.d, self.p)
        self.mem += 4
        return v

    def name(self) -> Tuple[int, int]:
        self._need(1)
        a, self.p = read_compact(self.d, self.p)
        b, self.p = read_compact(self.d, self.p)
        self.mem += 4
        return a, b

    # expressions --------------------------------------------------------------------------------
    def expr(self) -> Expr:
        foff, mem = self.p, self.mem
        tok = self.u8()
        shape = T.shape_of(tok)
        if shape is None:
            raise DecodeError("unknown token 0x%02X at mem %d (file +%d)" % (tok, mem, foff))
        vals = []
        for k in shape:
            if k == "u8":
                vals.append(self.u8())
            elif k == "u16":
                vals.append(self.u16())
            elif k == "i32":
                vals.append(self.i32())
            elif k in ("u32", "f32"):
                vals.append(self.u32())
            elif k == "obj":
                vals.append(self.obj())
            elif k == "name":
                vals.append(self.name())
            elif k == "expr":
                vals.append(self.expr())
            elif k == "params":
                args = []
                while True:
                    self._need(1)
                    if self.d[self.p] == T.EX_EndFunctionParms:
                        self.u8()
                        break
                    args.append(self.expr())
                vals.append(args)
            elif k == "cstr":
                try:
                    e = self.d.index(0, self.p)
                except ValueError:
                    raise DecodeError("unterminated string at file +%d" % self.p)
                s = self.d[self.p:e]
                self.mem += e + 1 - self.p
                self.p = e + 1
                vals.append(bytes(s))
            elif k == "ustr":
                cs = []
                while True:
                    c = self.u16()
                    if c == 0:
                        break
                    cs.append(c)
                vals.append(cs)
            elif k == "case":
                off = self.u16()
                vals.append((off, None if off == 0xFFFF else self.expr()))
            elif k == "labels":
                ents = []
                while True:
                    nm = self.name()
                    off = self.u32()
                    ents.append((nm, off))
                    if self.none_name(nm[0]):
                        break
                vals.append(ents)
            else:  # pragma: no cover
                raise DecodeError("bad shape item " + k)
        return Expr(tok, vals, foff, self.p, mem, self.mem)


def decode_script(data: bytes, pos: int, script_size: int,
                  none_name: Callable[[int], bool] = lambda i: i == 0) -> Tuple[List[Expr], int]:
    """Decode statements from data[pos:] until script_size in-memory bytes are consumed.

    Returns (statements, file end offset). Raises DecodeError on unknown tokens or an overrun."""
    dec = Decoder(data, pos, none_name)
    out = []
    while dec.mem < script_size:
        out.append(dec.expr())
    if dec.mem != script_size:
        raise DecodeError("script overrun: decoded %d of %d bytes" % (dec.mem, script_size))
    return out, dec.p


# encoding ---------------------------------------------------------------------------------------
def encode_expr(e: Expr, out: bytearray) -> None:
    out.append(e.tok)
    for k, v in zip(e.shape, e.vals):
        if k == "u8":
            out.append(v & 0xFF)
        elif k == "u16":
            out += struct.pack("<H", v)
        elif k == "i32":
            out += struct.pack("<i", v)
        elif k in ("u32", "f32"):
            out += struct.pack("<I", v)
        elif k == "obj":
            out += write_compact(v)
        elif k == "name":
            out += write_compact(v[0]) + write_compact(v[1])
        elif k == "expr":
            encode_expr(v, out)
        elif k == "params":
            for a in v:
                encode_expr(a, out)
            out.append(T.EX_EndFunctionParms)
        elif k == "cstr":
            out += v + b"\0"
        elif k == "ustr":
            for c in v:
                out += struct.pack("<H", c)
            out += b"\0\0"
        elif k == "case":
            out += struct.pack("<H", v[0])
            if v[1] is not None:
                encode_expr(v[1], out)
        elif k == "labels":
            for nm, off in v:
                out += write_compact(nm[0]) + write_compact(nm[1]) + struct.pack("<I", off)


def encode_script(stmts: Sequence[Expr]) -> bytes:
    out = bytearray()
    for s in stmts:
        encode_expr(s, out)
    return bytes(out)


def mem_size(e: Expr) -> int:
    """In-memory size of a tree, computed from its contents (independent of the recorded offsets)."""
    n = 1
    for k, v in zip(e.shape, e.vals):
        if k in T.MEM_SIZE:
            n += T.MEM_SIZE[k]
        elif k == "expr":
            n += mem_size(v)
        elif k == "params":
            n += sum(mem_size(a) for a in v) + 1
        elif k == "cstr":
            n += len(v) + 1
        elif k == "ustr":
            n += 2 * len(v) + 2
        elif k == "case":
            n += 2 + (mem_size(v[1]) if v[1] is not None else 0)
        elif k == "labels":
            n += 8 * len(v)
    return n


def relayout(stmts: Sequence[Expr], start_mem: int = 0) -> int:
    """Recompute mem/mend of every node from the tree contents; returns the end offset."""
    def go(e: Expr, m: int) -> int:
        e.mem = m
        m += 1
        for k, v in zip(e.shape, e.vals):
            if k in T.MEM_SIZE:
                m += T.MEM_SIZE[k]
            elif k == "expr":
                m = go(v, m)
            elif k == "params":
                for a in v:
                    m = go(a, m)
                m += 1
            elif k == "cstr":
                m += len(v) + 1
            elif k == "ustr":
                m += 2 * len(v) + 2
            elif k == "case":
                m += 2
                if v[1] is not None:
                    m = go(v[1], m)
            elif k == "labels":
                m += 8 * len(v)
        e.mend = m
        return m

    m = start_mem
    for s in stmts:
        m = go(s, m)
    return m
