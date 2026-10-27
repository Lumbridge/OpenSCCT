"""Round-trip checks on real packages.

T1  every struct's token tree re-encodes to the original script bytes, and the in-memory size computed
    from the tree equals ScriptSize.
T2  the structured form, lowered back to jumps, reproduces the original bytes (jump layout included).
Also: native and function call argument counts against the callee's declared parameters.
"""

from __future__ import annotations

import collections
from typing import Dict, List

from . import tokens as T
from .bytecode import encode_script, mem_size
from .loader import Function, Struct, CPF_OptionalParm, CPF_Parm, CPF_ReturnParm
from .structure import count_kinds, lower_bytes, structure
from .universe import Universe


def state_labels(lp, s: Struct) -> Dict[int, List[str]]:
    labels: Dict[int, List[str]] = {}
    for x in s.script:
        if x.tok == T.EX_LabelTable:
            for nm, off in x.vals[0]:
                if nm[0] not in lp.none_index:
                    labels.setdefault(off, []).append(lp.name_of(nm[0]))
    return labels


def structured(lp, s: Struct):
    return structure(s.script, s.script_size, state_labels(lp, s))


def param_count(u: Universe, f: Function) -> int:
    n = 0
    for m in u.members(f):
        if getattr(m, "flags", 0) & CPF_Parm and not m.flags & CPF_ReturnParm:
            n += 1
    return n


def required_count(u: Universe, f: Function) -> int:
    """Parameters before the first optional one (trailing optional arguments may be left out)."""
    n = 0
    for m in u.members(f):
        if getattr(m, "flags", 0) & CPF_Parm and not m.flags & CPF_ReturnParm:
            if m.flags & CPF_OptionalParm:
                break
            n += 1
    return n


def check_package(u: Universe, name: str) -> dict:
    lp = u.packages[name]
    r = collections.Counter()
    fails = {"T1": [], "T2": [], "args": [], "skip": []}
    kinds: Dict[str, int] = {}
    for s in lp.fields.values():
        if not isinstance(s, Struct):
            continue
        r["structs"] += 1
        if s.kind == "Function":
            r["functions"] += 1
        if s.script_size == 0:
            continue
        r["with_script"] += 1
        r["script_bytes"] += s.script_size
        ok1 = (not s.script_error and encode_script(s.script) == s.script_bytes
               and sum(mem_size(x) for x in s.script) == s.script_size)
        if ok1:
            r["T1_ok"] += 1
        else:
            fails["T1"].append(s.path)
            continue
        # call argument counts, and the skip sizes the compiler stored (each must equal the in-memory
        # size of the expression it skips, an independent check of every operand size)
        for x in s.script:
            for n in x.walk():
                if n.tok in (T.EX_Context, T.EX_ClassContext):
                    r["context_checked"] += 1
                    if n.vals[1] != mem_size(n.vals[3]):
                        r["context_skip_mismatch"] += 1
                        if len(fails["skip"]) < 20:
                            fails["skip"].append(s.path)
                elif n.tok == T.EX_Skip:
                    # the operand of a short-circuit operator; the skip also covers the operator's
                    # EndFunctionParms byte
                    r["skip_checked"] += 1
                    if n.vals[0] != mem_size(n.vals[1]) + 1:
                        r["skip_mismatch"] += 1
                        if len(fails["skip"]) < 20:
                            fails["skip"].append(s.path)
                callee = None
                if n.native_index is not None:
                    callee = u.natives.get(n.native_index)
                elif n.tok == T.EX_FinalFunction:
                    callee = u.field(lp, n.vals[0])
                if callee is None or not isinstance(callee, Function):
                    if n.native_index is not None:
                        r["native_unknown"] += 1
                    continue
                r["calls_checked"] += 1
                if len(n.params) < required_count(u, callee):
                    r["calls_too_few_args"] += 1
                    if len(fails["args"]) < 20:
                        fails["args"].append("%s -> %s (few)" % (s.path, callee.path))
                if len(n.params) > param_count(u, callee):
                    r["calls_too_many_args"] += 1
                    if len(fails["args"]) < 20:
                        fails["args"].append("%s -> %s" % (s.path, callee.path))
        if s.kind == "Class":
            r["T2_ok"] += 1          # replication conditions only, no control flow
            continue
        tree, unstructured = structured(lp, s)
        count_kinds(tree, kinds)
        r["unstructured_jumps"] += unstructured
        if unstructured:
            r["with_goto"] += 1
        try:
            ok2 = lower_bytes(tree, lp.name_of) == s.script_bytes
        except Exception:   # noqa: BLE001 - report, do not stop the run
            ok2 = False
            r["T2_exceptions"] += 1
        if ok2:
            r["T2_ok"] += 1
        else:
            fails["T2"].append(s.path)
    out = dict(r)
    out["structured_kinds"] = kinds
    out["fail_samples"] = {k: v[:25] for k, v in fails.items()}
    out["fail_counts"] = {k: len(v) for k, v in fails.items()}
    return out
