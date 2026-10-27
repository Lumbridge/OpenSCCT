"""Class files (.uc text) and the JSON index."""

from __future__ import annotations

import hashlib
import json
import os
from typing import Dict, List, Optional, Tuple

from . import tokens as T
from .check import state_labels
from .disasm import listing
from .defaults import DefaultsFormatter
from .loader import (Class, Const, Enum, Field, Function, Property, State, Struct, flag_names,
                     CLASS_FLAG_NAMES, CPF_FLAG_NAMES, FUNC_FLAG_NAMES, STATE_FLAG_NAMES,
                     CPF_Parm, CPF_ReturnParm, CPF_OptionalParm, CPF_OutParm, CPF_CoerceParm, CPF_Edit,
                     CPF_Net, FUNC_Defined, FUNC_Delegate, FUNC_Event, FUNC_Exec, FUNC_Final, FUNC_Iterator,
                     FUNC_Latent, FUNC_Native, FUNC_Net, FUNC_NetReliable, FUNC_Operator, FUNC_PreOperator,
                     FUNC_Private, FUNC_Protected, FUNC_Simulated, FUNC_Singular, FUNC_Static)
from .printer import IND, Printer
from .structure import SReturn, SExpr, structure
from .universe import Universe

RF_Native = 0x04000000

VAR_MODIFIERS = [
    (0x00000002, "const"), (0x00000004, "input"), (0x00000008, "export"), (0x00001000, "native"),
    (0x00002000, "transient"), (0x00004000, "config"), (0x00008000, "localized"), (0x00010000, "travel"),
    (0x00020000, "editconst"), (0x00040000, "globalconfig"), (0x00800000, "noexport"),
    (0x04000000, "editinline"), (0x08000000, "edfindable"), (0x10000000, "editinlineuse"),
    (0x20000000, "deprecated"), (0x00000040, "editconstarray"), (0x01000000, "button"),
    (0x40000000, "editinlinenotify"),
]

CLASS_MODIFIERS = [
    (0x00000001, "abstract", False), (0x00000008, "transient", True), (0x00000200, "placeable", False),
    (0x00000400, "perobjectconfig", True), (0x00000800, "nativereplication", False),
    (0x00001000, "editinlinenew", True), (0x00002000, "collapsecategories", True),
    (0x00000040, "safereplace", True), (0x00004000, "exportstructs", False),
]


def sha1(b: bytes) -> str:
    return hashlib.sha1(b).hexdigest()


def text_hash(stmts, lp) -> str:
    """sha1 of the token listing with names and objects spelled out (stable across packages and
    rebuilds that only renumber the name and object tables)."""
    lines = listing(stmts, lambda n: lp.name_of(n[0]), lp.path_of)
    return sha1("\n".join(lines).encode("utf-8"))


class ClassEmitter:
    def __init__(self, u: Universe, cls: Class):
        self.u = u
        self.c = cls
        self.lp = cls.pkg
        self.p = Printer(u, self.lp)
        self.p.cls = cls
        self.stats: Dict[str, int] = {}
        self.index: dict = {}
        self.dfmt = DefaultsFormatter(u, self.p)

    # helpers ------------------------------------------------------------------------------------
    def members(self, s: Struct, reverse: bool) -> List[Field]:
        m = self.u.members(s)
        return list(reversed(m)) if reverse else m

    def bump(self, k: str, n: int = 1):
        self.stats[k] = self.stats.get(k, 0) + n

    # declarations -------------------------------------------------------------------------------
    def var_decl(self, p: Property, owner: Struct) -> Optional[str]:
        if p.kind == "DelegateProperty" and p.name.startswith("__") and p.name.endswith("__Delegate"):
            return None
        mods = [n for b, n in VAR_MODIFIERS if p.flags & b]
        head = "var"
        if p.flags & CPF_Edit:
            cat = p.category
            head = "var()" if cat in ("None", owner.name) else "var(%s)" % cat
        dim = "[%d]" % p.array_dim if p.array_dim > 1 else ""
        return "%s %s%s %s%s;" % (head, " ".join(mods) + (" " if mods else ""), self.p.type_name(p), p.name, dim)

    def enum_decl(self, e: Enum, depth: int) -> List[str]:
        ind = IND * depth
        out = [ind + "enum %s" % e.name, ind + "{"]
        for n, v in enumerate(e.values):
            out.append(ind + IND + v + ("," if n + 1 < len(e.values) else ""))
        out.append(ind + "};")
        return out

    def struct_decl(self, s: Struct, depth: int) -> List[str]:
        ind = IND * depth
        sup = self.u.super_of(s)
        ext = " extends %s" % sup.name if sup else ""
        native = "native " if s.export.flags & RF_Native else ""
        out = [ind + "struct %s%s%s" % (native, s.name, ext), ind + "{"]
        for m in self.members(s, False):
            if isinstance(m, Enum):
                out += self.enum_decl(m, depth + 1)
            elif isinstance(m, Struct) and m.kind == "Struct":
                out += self.struct_decl(m, depth + 1)
            elif isinstance(m, Const):
                out.append(ind + IND + "const %s = %s;" % (m.name, m.value.strip()))
        for m in self.members(s, False):
            if isinstance(m, Property):
                d = self.var_decl(m, s)
                if d:
                    out.append(ind + IND + d)
        out.append(ind + "};")
        return out

    def class_header(self) -> List[str]:
        c = self.c
        sup = self.u.super_of(c)
        out = ["class %s%s" % (c.name, " extends %s" % sup.name if sup else "")]
        mods = []
        pflags = sup.class_flags if isinstance(sup, Class) else 0
        if c.within and self.lp.obj_name(c.within) != "Object":
            if not isinstance(sup, Class) or sup.within == 0 or self.u.field(sup.pkg, sup.within) is not \
                    self.u.field(self.lp, c.within):
                mods.append("within %s" % self.lp.obj_name(c.within))
        if c.export.flags & RF_Native:
            mods.append("native")
        for bit, name, inherited in CLASS_MODIFIERS:
            if c.class_flags & bit and not (inherited and pflags & bit):
                mods.append(name)
        if c.class_flags & 0x4:
            if not (pflags & 0x4) or (isinstance(sup, Class) and sup.config_name != c.config_name):
                mods.append("config(%s)" % c.config_name if c.config_name not in ("None", "System") else "config")
        hide = [h for h in c.hide_categories if not (isinstance(sup, Class) and h in sup.hide_categories)]
        if hide:
            mods.append("hidecategories(%s)" % ", ".join(hide))
        for m in mods:
            out.append(IND + m)
        out[-1] += ";"
        return out

    # functions ----------------------------------------------------------------------------------
    def func_header(self, f: Function) -> str:
        fl = f.func_flags
        mods = []
        if fl & FUNC_Private:
            mods.append("private")
        if fl & FUNC_Protected:
            mods.append("protected")
        if fl & FUNC_Native:
            mods.append("native(%d)" % f.native if f.native else "native")
        if fl & FUNC_Static:
            mods.append("static")
        if fl & FUNC_Final:
            mods.append("final")
        if fl & FUNC_Simulated:
            mods.append("simulated")
        if fl & FUNC_Singular:
            mods.append("singular")
        if fl & FUNC_Iterator:
            mods.append("iterator")
        if fl & FUNC_Latent:
            mods.append("latent")
        if fl & FUNC_Exec:
            mods.append("exec")
        params = self.p.params_of(f)
        if fl & FUNC_Delegate:
            kw = "delegate"
        elif fl & FUNC_Operator:
            if fl & FUNC_PreOperator:
                kw = "preoperator"
            elif len(params) == 1:
                kw = "postoperator"
            else:
                kw = "operator(%d)" % f.oper_prec
        elif fl & FUNC_Event:
            kw = "event"
        else:
            kw = "function"
        ret = self.p.return_prop(f)
        rtxt = self.p.type_name(ret) + " " if ret else ""
        name = f.friendly_name if fl & FUNC_Operator else f.name
        ptxt = []
        for p in params:
            pm = []
            if p.flags & CPF_OptionalParm:
                pm.append("optional")
            if p.flags & CPF_OutParm:
                pm.append("out")
            if p.flags & CPF_CoerceParm:
                pm.append("coerce")
            dim = "[%d]" % p.array_dim if p.array_dim > 1 else ""
            ptxt.append(" ".join(pm + [self.p.type_name(p), p.name + dim]))
        return "%s%s %s%s(%s)" % (" ".join(mods) + (" " if mods else ""), kw, rtxt, name, ", ".join(ptxt))

    def is_stub(self, f: Function) -> bool:
        return (not f.func_flags & FUNC_Defined and not f.func_flags & FUNC_Native and f.script_size == 2
                and len(f.script) == 1 and f.script[0].tok == T.EX_Return)

    def function(self, f: Function, depth: int, fi: dict) -> List[str]:
        ind = IND * depth
        out = []
        head = self.func_header(f)
        body_wanted = bool(f.func_flags & FUNC_Defined) and not f.func_flags & FUNC_Native
        if not body_wanted and not (f.script_size > 2 and not f.func_flags & FUNC_Native):
            out.append(ind + head + ";")
            return out
        out.append(ind + head)
        out.append(ind + "{")
        for m in self.u.members(f):
            if isinstance(m, Property) and not m.flags & (CPF_Parm | CPF_ReturnParm):
                dim = "[%d]" % m.array_dim if m.array_dim > 1 else ""
                out.append(ind + IND + "local %s %s%s;" % (self.p.type_name(m), m.name, dim))
        if any(isinstance(m, Property) and not m.flags & (CPF_Parm | CPF_ReturnParm) for m in self.u.members(f)):
            out.append("")
        self.p.func = f
        self.p.latent_calls = []
        tree, ung = structure(f.script, f.script_size, state_labels(self.lp, f))
        fi["gotos"] = ung
        self.bump("gotos", ung)
        if tree and isinstance(tree[-1], SReturn) and tree[-1].ret.vals[0].tok == T.EX_Nothing and not tree[-1].pops:
            tree = tree[:-1]
        body: List[str] = []
        self.p.block(tree, depth + 1, body)
        out += body
        out.append(ind + "}")
        fi["latent_calls"] = sorted(set(self.p.latent_calls))
        return out

    def func_info(self, f: Function, owner: str) -> dict:
        return {
            "name": f.name,
            "owner": owner,
            "flags": flag_names(f.func_flags, FUNC_FLAG_NAMES),
            "native": f.native,
            "oper_prec": f.oper_prec,
            "rep_offset": f.rep_offset if f.func_flags & FUNC_Net else None,
            "script_size": f.script_size,
            "file_bytes": len(f.script_bytes),
            "sha1": sha1(f.script_bytes),
            "line": f.line,
            "sha1_text": text_hash(f.script, self.lp),
            "params": [self.prop_info(p) for p in self.p.params_of(f)],
            "locals": [self.prop_info(m) for m in self.u.members(f)
                       if isinstance(m, Property) and not m.flags & (CPF_Parm | CPF_ReturnParm)],
            "return": self.p.type_name(self.p.return_prop(f)) if self.p.return_prop(f) else None,
        }

    def prop_info(self, p: Property) -> dict:
        d = {"name": p.name, "type": self.p.type_name(p), "flags": flag_names(p.flags, CPF_FLAG_NAMES)}
        if p.array_dim > 1:
            d["dim"] = p.array_dim
        if p.category not in ("None",):
            d["category"] = p.category
        if p.flags & CPF_Net:
            d["rep_offset"] = p.rep_offset
        return d

    # states -------------------------------------------------------------------------------------
    def state(self, s: State) -> Tuple[List[str], dict]:
        mods = []
        if s.state_flags & 0x2:
            mods.append("auto")
        if s.state_flags & 0x4:
            mods.append("simulated")
        sup = self.u.super_of(s)
        ext = ""
        if isinstance(sup, State) and sup.name.lower() != s.name.lower():
            ext = " extends %s" % sup.name
        edit = "()" if s.state_flags & 0x1 else ""
        out = ["%sstate%s %s%s" % (" ".join(mods) + (" " if mods else ""), edit, s.name, ext), "{"]
        ignored = [self.u.probe_name(b) for b in range(64) if not (s.ignore_mask >> b) & 1]
        funcs = [m for m in self.members(s, True) if isinstance(m, Function)]
        stubs = [f for f in funcs if self.is_stub(f)]
        # A stub sharing its source line with another stub, or whose parameters differ from the
        # function it overrides, comes from an ignores list; a lone stub is a bodiless declaration.
        # Both compile to the same thing.
        lines: Dict[int, int] = {}
        for f in stubs:
            lines[f.line] = lines.get(f.line, 0) + 1
        ign_stubs = []
        for f in stubs:
            sup = self.u.field(f.pkg, f.super_field) if f.super_field else None
            nparams = len(self.u.members(f))
            if lines[f.line] > 1 or (isinstance(sup, Function) and len(self.u.members(sup)) != nparams):
                ign_stubs.append(f)
        ignored += [f.name for f in ign_stubs]
        stubs = ign_stubs
        si = {"name": s.name, "extends": sup.name if isinstance(sup, State) else None,
              "flags": flag_names(s.state_flags, STATE_FLAG_NAMES), "ignores": ignored,
              "functions": [], "labels": [], "script_size": s.script_size,
              "sha1": sha1(s.script_bytes) if s.script_size else None}
        if ignored:
            out.append(IND + "ignores %s;" % ", ".join(ignored))
            out.append("")
        for f in funcs:
            if f in stubs:
                continue
            fi = self.func_info(f, "%s.%s" % (self.c.name, s.name))
            out += self.function(f, 1, fi)
            out.append("")
            si["functions"].append(fi)
            self.bump("functions")
        if s.script_size:
            labels = state_labels(self.lp, s)
            si["labels"] = sorted((n, m) for m, ns in labels.items() for n in ns)
            self.p.func = s
            self.p.latent_calls = []
            tree, ung = structure(s.script, s.script_size, labels)
            self.bump("gotos", ung)
            tree = _strip_state_tail(tree)
            body: List[str] = []
            self.p.block(tree, 1, body, state_code=True)
            out += body
            si["latent_calls"] = sorted(set(self.p.latent_calls))
        while out and out[-1] == "":
            out.pop()
        out.append("}")
        return out, si

    # replication --------------------------------------------------------------------------------
    def replication(self) -> Tuple[List[str], list]:
        c = self.c
        by_mem = {x.mem: x for x in c.script}
        groups: Dict[int, List[Field]] = {}
        inherited: List[str] = []
        for m in self.members(c, True):
            off = None
            if isinstance(m, Property) and m.flags & CPF_Net:
                off = m.rep_offset
            elif isinstance(m, Function) and m.func_flags & FUNC_Net:
                off = m.rep_offset
            if off == 0xFFFF:
                inherited.append(m.name)     # replicated by a parent class's statement
            elif off is not None:
                groups.setdefault(off, []).append(m)
        self.rep_inherited = inherited
        if not groups:
            return [], []
        out = ["replication", "{"]
        info = []
        self.p.func = c
        for off in sorted(groups):
            ms = groups[off]
            cond = by_mem.get(off)
            if cond is None:
                self.bump("rep_unresolved")
                ctext = "/* condition at 0x%04X not found */" % off
            else:
                ctext = self.p.x(cond)
            funcs = [m for m in ms if isinstance(m, Function)]
            rel = all(f.func_flags & FUNC_NetReliable for f in funcs) if funcs else True
            out.append(IND + "%s if (%s)" % ("reliable" if rel else "unreliable", ctext))
            out.append(IND * 2 + ", ".join(m.name for m in ms) + ";")
            info.append({"reliable": rel, "offset": off, "condition": ctext,
                         "members": [m.name for m in ms],
                         "kinds": ["function" if isinstance(m, Function) else "var" for m in ms]})
        out.append("}")
        return out, info

    # class file ---------------------------------------------------------------------------------
    def emit(self) -> Tuple[str, dict]:
        c = self.c
        out: List[str] = []
        out += self.class_header()
        out.append("")
        mem = self.members(c, True)
        consts = [m for m in mem if isinstance(m, Const)]
        enums = [m for m in mem if isinstance(m, Enum)]
        structs = [m for m in mem if isinstance(m, Struct) and m.kind == "Struct"]
        props = [m for m in mem if isinstance(m, Property)]
        funcs = [m for m in mem if isinstance(m, Function)]
        states = [m for m in mem if isinstance(m, State) and m.kind == "State"]
        for k in consts:
            out.append("const %s = %s;" % (k.name, k.value.strip()))
        if consts:
            out.append("")
        for e in enums:
            out += self.enum_decl(e, 0)
            out.append("")
        for s in structs:
            out += self.struct_decl(s, 0)
            out.append("")
        nv = 0
        for p in props:
            d = self.var_decl(p, c)
            if d:
                out.append(d)
                nv += 1
        if nv:
            out.append("")
        if c.cpp_text:
            txt = self._textbuffer(c.cpp_text)
            if txt is not None:
                out.append("cpptext")
                out.append("{")
                out += [l.rstrip() for l in txt.replace("\r\n", "\n").split("\n")]
                out.append("}")
                out.append("")
        rep, rinfo = self.replication()
        if rep:
            out += rep
            out.append("")
        finfo = []
        sinfo = []
        # functions and states interleaved in source order (by line; a state sorts at its first line)
        body = [(f.line, 0, n, f) for n, f in enumerate(funcs)]
        for n, st in enumerate(states):
            lines = [st.line] + [m.line for m in self.u.members(st) if isinstance(m, Function)]
            body.append((min(x for x in lines if x > 0) if any(x > 0 for x in lines) else st.line, 1, n, st))
        body.sort(key=lambda b: (b[0], b[1], b[2]))
        for _, kind, _, m in body:
            if kind == 0:
                fi = self.func_info(m, c.name)
                out += self.function(m, 0, fi)
                out.append("")
                finfo.append(fi)
                self.bump("functions")
            else:
                lines, si = self.state(m)
                out += lines
                out.append("")
                sinfo.append(si)
                self.bump("states")
        dlines, dstat = self.dfmt.format(c)
        out += dlines
        for k, v in dstat.items():
            self.bump(k, v)
        sup = self.u.super_of(c)
        self.index = {
            "package": self.lp.name,
            "class": c.name,
            "super": sup.path if sup else None,
            "flags": flag_names(c.class_flags, CLASS_FLAG_NAMES),
            "native": bool(c.export.flags & RF_Native),
            "within": self.lp.path_of(c.within) if c.within else None,
            "config": c.config_name,
            "hide_categories": c.hide_categories,
            "replication_script_size": c.script_size,
            "replication_sha1": sha1(c.script_bytes) if c.script_size else None,
            "replication": rinfo,
            "replication_inherited": getattr(self, "rep_inherited", []),
            "consts": [k.name for k in consts],
            "enums": [e.name for e in enums],
            "structs": [s.name for s in structs],
            "vars": [self.prop_info(p) for p in props],
            "functions": finfo,
            "states": sinfo,
            "defaults": dstat,
            "sizes": {"export_bytes": len(c.raw),
                      "script_bytes": sum(f["script_size"] for f in finfo) +
                      sum(s["script_size"] + sum(f["script_size"] for f in s["functions"]) for s in sinfo)},
        }
        return "\n".join(out).rstrip() + "\n", self.index

    def _textbuffer(self, ref: int) -> Optional[str]:
        if ref <= 0:
            return None
        e = self.lp.pkg.exports[ref - 1]
        from .pkgload import Reader
        try:
            r = Reader(self.lp.pkg.data[e.offset:e.offset + e.size], 0, self.lp.pkg, base=e.offset)
            r.name()
            r.u32()
            r.u32()
            return r.fstring()
        except Exception:   # noqa: BLE001
            return None


def _strip_state_tail(tree):
    """Drop the label table, the padding and the Stop the compiler appends to state code."""
    out = list(tree)
    while out and isinstance(out[-1], SExpr) and out[-1].expr.tok in (T.EX_LabelTable, T.EX_Nothing):
        out.pop()
    if out and isinstance(out[-1], SExpr) and out[-1].expr.tok == T.EX_Stop:
        out.pop()
    return out


def decompile_all(u: Universe, out_dir: str, packages=None) -> dict:
    """Write <out>/<Pkg>/<Class>.uc for every class and <out>/uc-index.json."""
    index = {"packages": {}, "classes": []}
    totals: Dict[str, int] = {}
    for name, lp in u.packages.items():
        if packages and name not in packages:
            continue
        pdir = os.path.join(out_dir, name)
        os.makedirs(pdir, exist_ok=True)
        pstats: Dict[str, int] = {"classes": 0}
        for c in sorted(lp.classes(), key=lambda k: k.name.lower()):
            em = ClassEmitter(u, c)
            try:
                text, ci = em.emit()
            except Exception as ex:   # noqa: BLE001 - keep going, count it
                pstats["class_errors"] = pstats.get("class_errors", 0) + 1
                index["classes"].append({"package": name, "class": c.name, "error": "%s: %s" % (type(ex).__name__, ex)})
                continue
            with open(os.path.join(pdir, c.name + ".uc"), "w", encoding="utf-8", newline="\n") as fh:
                fh.write(text)
            index["classes"].append(ci)
            pstats["classes"] += 1
            for k, v in em.stats.items():
                pstats[k] = pstats.get(k, 0) + v
        index["packages"][name] = pstats
        for k, v in pstats.items():
            totals[k] = totals.get(k, 0) + v
    index["totals"] = totals
    with open(os.path.join(out_dir, "uc-index.json"), "w", encoding="utf-8", newline="\n") as fh:
        json.dump(index, fh, indent=1)
    return index
