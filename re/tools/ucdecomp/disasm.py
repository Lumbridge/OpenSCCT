"""Flat token listing (one line per top-level statement) for debugging the decoder and structurer."""

from __future__ import annotations

from typing import List

from . import tokens as T
from .bytecode import Expr, f32_bits


def describe(e: Expr, nm=lambda n: "n%d" % n[0], ob=lambda r: "o%d" % r) -> str:
    parts = []
    for k, v in zip(e.shape, e.vals):
        if k in ("u8", "u16", "i32", "u32"):
            parts.append(("0x%X" % v) if k == "u16" else str(v))
        elif k == "f32":
            parts.append("%g" % f32_bits(v))
        elif k == "obj":
            parts.append(ob(v))
        elif k == "name":
            parts.append("'%s'" % nm(v))
        elif k == "expr":
            parts.append(describe(v, nm, ob))
        elif k == "params":
            parts.append("(" + ", ".join(describe(a, nm, ob) for a in v) + ")")
        elif k == "cstr":
            parts.append(repr(v.decode("latin1")))
        elif k == "ustr":
            parts.append(repr("".join(chr(c) for c in v)))
        elif k == "case":
            parts.append("0x%X" % v[0] + ("" if v[1] is None else " " + describe(v[1], nm, ob)))
        elif k == "labels":
            parts.append(",".join("%s@0x%X" % (nm(n), o) for n, o in v))
    name = T.mnemonic(e.tok)
    if e.native_index is not None:
        name = "Native%d" % e.native_index
    return name + ("[" + " ".join(parts) + "]" if parts else "")


def listing(stmts: List[Expr], nm=lambda n: "n%d" % n[0], ob=lambda r: "o%d" % r) -> List[str]:
    return ["%04X  %s" % (s.mem, describe(s, nm, ob)) for s in stmts]
