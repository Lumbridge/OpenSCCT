"""Control-flow structuring of a statement list, and lowering back to jumps.

The UE2 compiler emits a fixed layout for every structured statement (offsets are in-memory bytes):

    if (c) A                JumpIfNot(L1) c; A; L1:
    if (c) A else B         JumpIfNot(L1) c; A; Jump(L2); L1: B; L2:
    while (c) A             L0: JumpIfNot(L1) c; A; Jump(L0); L1:
    for (i; c; n) A         i; L0: JumpIfNot(L1) c; A; n; Jump(L0); L1:
    do A until (c)          L0: A; JumpIfNot(L0) c;
    switch (e) {...}        Switch e; Case(next) v; ...; Case(0xFFFF); ...; end:
    foreach F(...) A        Iterator(L1) F(...); A; IteratorNext; L1: IteratorPop;
    break                   Jump(end of the innermost loop or switch, or the IteratorPop of a foreach)
    continue                Jump(loop head or the increment of a for loop); IteratorNext in a foreach
    return inside foreach   IteratorPop per enclosing foreach, then Return

structure() rebuilds that tree; anything that does not fit stays a goto to a generated label.
lower() emits the tree back as a flat statement list with recomputed jump offsets, which check T2
compares with the original bytes.
"""

from __future__ import annotations

import copy
from dataclasses import dataclass, field
from typing import Callable, Dict, List, Optional, Sequence, Set, Tuple

from . import tokens as T
from .bytecode import Expr, encode_script, relayout


# structured statements ----------------------------------------------------------------------------
class S:
    pass


@dataclass
class SExpr(S):
    expr: Expr


@dataclass
class SLabel(S):
    name: str
    mem: int
    generated: bool = False


@dataclass
class SIf(S):
    head: Expr                  # JumpIfNot
    then: List[S]
    else_jump: Optional[Expr] = None
    orelse: Optional[List[S]] = None


@dataclass
class SWhile(S):
    head: Expr                  # JumpIfNot
    body: List[S]
    back: Expr                  # Jump to head


@dataclass
class SFor(S):
    init: Optional[S]
    head: Expr
    body: List[S]
    inc: Expr
    back: Expr


@dataclass
class SDo(S):
    body: List[S]
    tail: Expr                  # backward JumpIfNot


@dataclass
class SSwitch(S):
    head: Expr                  # Switch
    cases: List[Tuple[Expr, List[S]]]


@dataclass
class SForEach(S):
    head: Expr                  # Iterator
    body: List[S]
    next: Expr                  # IteratorNext
    pop: Expr                   # IteratorPop


@dataclass
class SBreak(S):
    jump: Expr
    pops: List[Expr] = field(default_factory=list)


@dataclass
class SContinue(S):
    jump: Expr


@dataclass
class SReturn(S):
    ret: Expr
    pops: List[Expr] = field(default_factory=list)


@dataclass
class SGoto(S):
    jump: Expr                  # Jump, or JumpIfNot for 'if (!c) goto L'
    label: str
    pops: List[Expr] = field(default_factory=list)


@dataclass
class Ctx:
    kind: str                   # loop, switch, foreach
    brk: int                    # break target
    cont: Optional[int]         # continue target


def _target(e: Expr) -> int:
    return e.vals[0]


def generated_label(mem: int) -> str:
    return "J0x%04X" % mem


class Structurer:
    def __init__(self, stmts: Sequence[Expr], script_size: int, labels: Dict[int, List[str]]):
        self.st = list(stmts)
        self.size = script_size
        self.idx = {s.mem: i for i, s in enumerate(self.st)}
        self.idx[script_size] = len(self.st)
        self.labels = {m: list(v) for m, v in labels.items()}
        self.wanted: Set[int] = set()            # goto targets found during this pass
        self.unstructured = 0
        self.back_jin: Dict[int, List[int]] = {}
        for k, s in enumerate(self.st):
            if s.tok == T.EX_JumpIfNot and _target(s) <= s.mem:
                self.back_jin.setdefault(_target(s), []).append(k)
        self.emitted_labels: Set[int] = set()

    def mem_at(self, i: int) -> int:
        return self.st[i].mem if i < len(self.st) else self.size

    def label_for(self, mem: int) -> str:
        names = self.labels.get(mem)
        if not names:
            self.wanted.add(mem)
            return generated_label(mem)
        return names[0]

    def _labels_at(self, i: int, out: List[S]) -> None:
        if i >= len(self.st):
            return
        m = self.st[i].mem
        if m in self.labels and m not in self.emitted_labels:
            self.emitted_labels.add(m)
            for name in self.labels[m]:
                out.append(SLabel(name, m, name.startswith("J0x")))

    # ----------------------------------------------------------------------------------------------
    def build(self, lo: int, hi: int, ctx: List[Ctx]) -> List[S]:
        out: List[S] = []
        i = lo
        himem = self.mem_at(hi)
        while i < hi:
            s = self.st[i]
            self._labels_at(i, out)
            k = self._do_tail(i, hi)
            if k is not None:
                tail = self.st[k]
                body = self.build(i, k, ctx + [Ctx("loop", tail.mend, tail.mem)])
                out.append(SDo(body, tail))
                i = k + 1
                continue
            tok = s.tok
            if tok == T.EX_JumpIfNot:
                n = self._jumpifnot(i, hi, himem, ctx, out)
                if n is not None:
                    i = n
                    continue
                out.append(SGoto(s, self.label_for(_target(s))))
                self.unstructured += 1
                i += 1
                continue
            if tok == T.EX_Jump:
                out.append(self._jump(s, [], ctx))
                i += 1
                continue
            if tok == T.EX_Switch:
                n = self._switch(i, hi, himem, ctx, out)
                if n is not None:
                    i = n
                    continue
            if tok == T.EX_Iterator:
                n = self._foreach(i, hi, ctx, out)
                if n is not None:
                    i = n
                    continue
            if tok == T.EX_IteratorPop and any(c.kind == "foreach" for c in ctx):
                pops = []
                j = i
                while j < hi and self.st[j].tok == T.EX_IteratorPop and (j == i or self.st[j].mem not in self.labels):
                    pops.append(self.st[j])
                    j += 1
                if j < hi and self.st[j].mem not in self.labels:
                    if self.st[j].tok == T.EX_Return:
                        out.append(SReturn(self.st[j], pops))
                        i = j + 1
                        continue
                    if self.st[j].tok == T.EX_Jump:
                        out.append(self._jump(self.st[j], pops, ctx))
                        i = j + 1
                        continue
            if tok == T.EX_IteratorNext and self._innermost_loop(ctx) == "foreach":
                # continue inside foreach is the IteratorNext token itself
                out.append(SContinue(s))
                i += 1
                continue
            if tok == T.EX_Return:
                out.append(SReturn(s))
            else:
                out.append(SExpr(s))
            i += 1
        return out

    @staticmethod
    def _innermost_loop(ctx: List[Ctx]) -> Optional[str]:
        for c in reversed(ctx):
            if c.kind in ("loop", "foreach"):
                return c.kind
        return None

    # patterns -----------------------------------------------------------------------------------
    def _do_tail(self, i: int, hi: int) -> Optional[int]:
        ks = [k for k in self.back_jin.get(self.st[i].mem, ()) if i < k < hi]
        return max(ks) if ks else None

    def _jumpifnot(self, i, hi, himem, ctx, out) -> Optional[int]:
        s = self.st[i]
        t = _target(s)
        if t <= s.mem or t > himem or t not in self.idx:
            return None
        j = self.idx[t]
        last = self.st[j - 1] if j - 1 > i else None
        if last is not None and last.tok == T.EX_Jump:
            t2 = _target(last)
            if t2 == s.mem:
                return self._loop(i, j, t, last, ctx, out)
            targets = {c.brk for c in ctx[-1:]} | {c.cont for c in ctx if c.cont is not None}
            if j - 1 == i + 1 and t2 in targets:
                # 'if (c) continue;' / 'if (c) break;' rather than an if with an empty then block
                out.append(SIf(s, self.build(i + 1, j, ctx)))
                return j
            if t < t2 <= himem and t2 in self.idx:
                k = self.idx[t2]
                then = self.build(i + 1, j - 1, ctx)
                orelse = self.build(j, k, ctx)
                out.append(SIf(s, then, last, orelse))
                return k
            if t2 == t and not any(x.tok == T.EX_Switch or (x.tok == T.EX_Jump and _target(x) == t)
                                   for x in self.st[i + 1:j - 1]):
                # a jump to where the if ends anyway: an empty else (a break of a switch or loop
                # inside the then block is recognised by the other jumps to the same place)
                then = self.build(i + 1, j - 1, ctx)
                out.append(SIf(s, then, last, []))
                return j
        out.append(SIf(s, self.build(i + 1, j, ctx)))
        return j

    def _loop(self, i, j, t, back, ctx, out) -> int:
        s = self.st[i]
        inc_i = j - 2
        is_for = False
        has_init = False
        if inc_i > i and self.st[inc_i].tok not in (T.EX_Jump, T.EX_JumpIfNot, T.EX_Return) \
                and self.st[inc_i].mem not in self.labels:
            inc_mem = self.st[inc_i].mem
            back_mem = self.st[j - 1].mem
            body = self.st[i + 1:inc_i]
            # a jump past the increment (an else branch ending at the back jump) rules a for loop out
            past = any(x.tok in (T.EX_Jump, T.EX_JumpIfNot) and inc_mem < _target(x) <= back_mem for x in body)
            # a continue that targets the increment proves a for loop
            if past:
                is_for = False
            elif any(x.tok == T.EX_Jump and _target(x) == inc_mem for x in body):
                is_for = True
            elif self._for_shape(i, inc_i, out):
                is_for = True
            has_init = is_for and self._for_shape(i, inc_i, out)
        if is_for:
            init = out.pop() if has_init else None
            inc = self.st[inc_i]
            body = self.build(i + 1, inc_i, ctx + [Ctx("loop", t, inc.mem)])
            out.append(SFor(init, s, body, inc, back))
        else:
            body = self.build(i + 1, j - 1, ctx + [Ctx("loop", t, s.mem)])
            out.append(SWhile(s, body, back))
        return j

    def _for_shape(self, i: int, inc_i: int, out: List[S]) -> bool:
        """Previous statement initialises, and the last body statement updates, a variable that the
        condition reads (printed as a for loop; the bytes are the same as the while form)."""
        if not out or not isinstance(out[-1], SExpr):
            return False
        init = out[-1].expr
        if init.tok not in (T.EX_Let, T.EX_LetBool) and init.native_index is None:
            return False
        v = _assigned_var(init)
        if v is None or _assigned_var(self.st[inc_i]) != v:
            return False
        cond = self.st[i].vals[1]
        return any(_var_key(n) == v for n in cond.walk())

    def _jump(self, s: Expr, pops: List[Expr], ctx: List[Ctx]) -> S:
        t = _target(s)
        if ctx:
            inner = ctx[-1]
            if t == inner.brk and not pops:
                return SBreak(s, pops)
            if not pops:
                for c in reversed(ctx):
                    if c.kind in ("loop", "foreach"):
                        if c.cont is not None and t == c.cont:
                            return SContinue(s)
                        break
        self.unstructured += 1
        return SGoto(s, self.label_for(t), pops)

    def _switch(self, i, hi, himem, ctx, out) -> Optional[int]:
        s = self.st[i]
        if i + 1 >= hi or self.st[i + 1].tok != T.EX_Case:
            return None
        case_idx = []
        k = i + 1
        end = None
        while True:
            c = self.st[k]
            case_idx.append(k)
            nxt = c.vals[0][0]
            if nxt == 0xFFFF:
                break
            if nxt not in self.idx or self.idx[nxt] <= k or nxt > himem:
                return None
            k = self.idx[nxt]
            if k >= hi or self.st[k].tok != T.EX_Case:
                end = nxt          # the last case's next offset is the end of the switch
                break
        if end is None:
            # The default case is last. A jump from an earlier case past the default label can only
            # be a break; otherwise a trailing break in the default body jumps to the next statement.
            d = case_idx[-1]
            dmem = self.st[d].mem
            earlier = [_target(x) for x in self.st[case_idx[0]:d]
                       if x.tok == T.EX_Jump and dmem < _target(x) <= himem]
            if earlier:
                end = min(earlier)
            else:
                end = himem
                for k in range(d + 1, hi):
                    x = self.st[k]
                    if x.tok == T.EX_Jump and _target(x) == self.mem_at(k + 1):
                        end = _target(x)
                        break
        e = self.idx.get(end)
        if e is None or e > hi:
            return None
        sw = Ctx("switch", end, None)
        cases = []
        bounds = case_idx + [e]
        for a, b in zip(bounds, bounds[1:]):
            body: List[S] = []
            if a != case_idx[0]:
                self._labels_at(a, body)
            cases.append((self.st[a], body + self.build(a + 1, b, ctx + [sw])))
        out.append(SSwitch(s, cases))
        return e

    def _foreach(self, i, hi, ctx, out) -> Optional[int]:
        s = self.st[i]
        end = s.vals[1]
        p = self.idx.get(end)
        if p is None or p >= hi or p - 1 <= i:
            return None
        if self.st[p].tok != T.EX_IteratorPop or self.st[p - 1].tok != T.EX_IteratorNext:
            return None
        if self.st[p].mem in self.labels or self.st[p - 1].mem in self.labels:
            return None
        nxt, pop = self.st[p - 1], self.st[p]
        body = self.build(i + 1, p - 1, ctx + [Ctx("foreach", pop.mem, nxt.mem)])
        out.append(SForEach(s, body, nxt, pop))
        return p + 1


def _var_key(e: Expr):
    if e.tok in (T.EX_LocalVariable, T.EX_InstanceVariable):
        return (e.tok, e.vals[0])
    if e.tok == T.EX_BoolVariable:
        return _var_key(e.vals[0])
    return None


def _assigned_var(e: Expr):
    """Variable written by an assignment or an ++/--/+=/-= style native call."""
    if e.tok in (T.EX_Let, T.EX_LetBool):
        return _var_key(e.vals[0])
    if e.native_index is not None and e.params:
        return _var_key(e.params[0])
    return None


def structure(stmts: Sequence[Expr], script_size: int, labels: Optional[Dict[int, List[str]]] = None):
    """Returns (tree, number of unstructured jumps). A second pass places labels for goto targets."""
    labels = {m: list(v) for m, v in (labels or {}).items()}
    s = Structurer(stmts, script_size, labels)
    tree = s.build(0, len(stmts), [])
    if s.wanted:
        for m in s.wanted:
            labels.setdefault(m, [generated_label(m)])
        s = Structurer(stmts, script_size, labels)
        tree = s.build(0, len(stmts), [])
    return tree, s.unstructured


# lowering -----------------------------------------------------------------------------------------
class Lowerer:
    def __init__(self, name_text: Optional[Callable[[int], str]] = None):
        self.out: List[Expr] = []
        self.fixups: List[Tuple[Expr, object, object]] = []   # (expr, value slot, marker)
        self.markers: Dict[int, int] = {}                     # id(marker) -> statement index
        self._keep: List[object] = []
        self.label_pos: Dict[str, int] = {}
        self.label_tables: List[Expr] = []
        self.name_text = name_text

    def marker(self):
        m = object()
        self._keep.append(m)
        return m

    def mark(self, m) -> None:
        self.markers[id(m)] = len(self.out)

    def emit(self, e: Expr) -> Expr:
        c = copy.copy(e)
        c.vals = list(e.vals)
        self.out.append(c)
        if c.tok == T.EX_LabelTable:
            self.label_tables.append(c)
        return c

    def jump(self, e: Expr, slot, marker) -> Expr:
        c = self.emit(e)
        self.fixups.append((c, slot, marker))
        return c

    def lower(self, tree: List[S], ctx):
        for s in tree:
            self.lower_one(s, ctx)

    def lower_one(self, s: S, ctx):
        if isinstance(s, SExpr):
            self.emit(s.expr)
        elif isinstance(s, SLabel):
            self.label_pos.setdefault(s.name, len(self.out))
        elif isinstance(s, SIf):
            end, els = self.marker(), self.marker()
            self.jump(s.head, 0, els if s.else_jump is not None else end)
            self.lower(s.then, ctx)
            if s.else_jump is not None:
                self.jump(s.else_jump, 0, end)
                self.mark(els)
                self.lower(s.orelse or [], ctx)
            self.mark(end)
        elif isinstance(s, SWhile):
            top, end = self.marker(), self.marker()
            self.mark(top)
            self.jump(s.head, 0, end)
            self.lower(s.body, ctx + [("loop", end, top)])
            self.jump(s.back, 0, top)
            self.mark(end)
        elif isinstance(s, SFor):
            top, end, cont = self.marker(), self.marker(), self.marker()
            if s.init is not None:
                self.lower_one(s.init, ctx)
            self.mark(top)
            self.jump(s.head, 0, end)
            self.lower(s.body, ctx + [("loop", end, cont)])
            self.mark(cont)
            self.emit(s.inc)
            self.jump(s.back, 0, top)
            self.mark(end)
        elif isinstance(s, SDo):
            top, end, cont = self.marker(), self.marker(), self.marker()
            self.mark(top)
            self.lower(s.body, ctx + [("loop", end, cont)])
            self.mark(cont)
            self.jump(s.tail, 0, top)
            self.mark(end)
        elif isinstance(s, SSwitch):
            end = self.marker()
            self.emit(s.head)
            starts = [self.marker() for _ in s.cases]
            for n, (case, body) in enumerate(s.cases):
                self.mark(starts[n])
                if case.vals[0][0] == 0xFFFF:
                    self.emit(case)
                else:
                    nxt = starts[n + 1] if n + 1 < len(s.cases) else end
                    self.jump(case, "case", nxt)
                self.lower(body, ctx + [("switch", end, None)])
            self.mark(end)
        elif isinstance(s, SForEach):
            end, cont, popm = self.marker(), self.marker(), self.marker()
            self.jump(s.head, 1, popm)
            self.lower(s.body, ctx + [("foreach", popm, cont)])
            self.mark(cont)
            self.emit(s.next)
            self.mark(popm)
            self.emit(s.pop)
            self.mark(end)
        elif isinstance(s, SBreak):
            for p in s.pops:
                self.emit(p)
            self.jump(s.jump, 0, ctx[-1][1])
        elif isinstance(s, SContinue):
            if s.jump.tok == T.EX_IteratorNext:
                self.emit(s.jump)
            else:
                tgt = next(c for c in reversed(ctx) if c[0] in ("loop", "foreach"))
                self.jump(s.jump, 0, tgt[2])
        elif isinstance(s, SReturn):
            for p in s.pops:
                self.emit(p)
            self.emit(s.ret)
        elif isinstance(s, SGoto):
            for p in s.pops:
                self.emit(p)
            self.jump(s.jump, 0, ("label", s.label))
        else:  # pragma: no cover
            raise TypeError(s)

    def finish(self) -> List[Expr]:
        end = relayout(self.out)

        def at(k: int) -> int:
            return self.out[k].mem if k < len(self.out) else end

        def pos(marker) -> int:
            if isinstance(marker, tuple):
                k = self.label_pos.get(marker[1])
            else:
                k = self.markers.get(id(marker))
            if k is None:
                raise KeyError("unplaced jump target %r" % (marker,))
            return at(k)

        for e, slot, marker in self.fixups:
            if slot == "case":
                e.vals[0] = (pos(marker), e.vals[0][1])
            else:
                e.vals[slot] = pos(marker)
        if self.name_text is not None:
            for lt in self.label_tables:
                ents = []
                for nm, off in lt.vals[0]:
                    k = self.label_pos.get(self.name_text(nm[0]))
                    ents.append((nm, at(k) if k is not None else off))
                lt.vals[0] = ents
        return self.out


def lower(tree: List[S], name_text: Optional[Callable[[int], str]] = None) -> List[Expr]:
    lw = Lowerer(name_text)
    lw.lower(tree, [])
    return lw.finish()


def lower_bytes(tree: List[S], name_text: Optional[Callable[[int], str]] = None) -> bytes:
    return encode_script(lower(tree, name_text))


def count_kinds(tree: List[S], acc: Optional[Dict[str, int]] = None) -> Dict[str, int]:
    acc = {} if acc is None else acc
    for s in tree:
        k = type(s).__name__
        acc[k] = acc.get(k, 0) + 1
        if isinstance(s, SIf):
            count_kinds(s.then, acc)
            count_kinds(s.orelse or [], acc)
        elif isinstance(s, (SWhile, SFor, SDo, SForEach)):
            count_kinds(s.body, acc)
        elif isinstance(s, SSwitch):
            for _, b in s.cases:
                count_kinds(b, acc)
    return acc
