"""Control-flow structuring and lowering on hand-assembled scripts."""

from ucdecomp.bytecode import decode_script
from ucdecomp.structure import (SDo, SFor, SForEach, SGoto, SIf, SLabel, SReturn,
                                SSwitch, SWhile, count_kinds, lower_bytes, structure)
from ucdecomp.tests.synth import (Asm, boolvar, byte_const, call, int_byte, int_zero, iterator_next,
                                  iterator_pop, let, local, native, nothing, ret, stop, true_)

A = let(local(1), int_zero())
B = let(local(2), int_zero())
C = let(local(3), int_zero())
COND = boolvar(local(9))
COND2 = boolvar(local(8))


def roundtrip(asm: Asm, labels=None, name_text=None):
    code, size, pos = asm.build()
    stmts, end = decode_script(code, 0, size)
    assert end == len(code)
    lab = {}
    for name, mem in (labels or {}).items():
        lab.setdefault(pos[name], []).append(name)
    tree, gotos = structure(stmts, size, lab)
    assert lower_bytes(tree, name_text) == code, "lowering must reproduce the original bytes"
    return tree, gotos


def test_if():
    t, g = roundtrip(Asm().jumpifnot("L1", COND).s(A).label("L1").s(ret()))
    assert g == 0 and isinstance(t[0], SIf) and t[0].else_jump is None and len(t[0].then) == 1


def test_if_else_and_else_if():
    asm = (Asm().jumpifnot("L1", COND).s(A).jump("END")
           .label("L1").jumpifnot("L2", COND2).s(B).jump("END")
           .label("L2").s(C)
           .label("END").s(ret()))
    t, g = roundtrip(asm)
    assert g == 0 and isinstance(t[0], SIf)
    inner = t[0].orelse
    assert len(inner) == 1 and isinstance(inner[0], SIf) and inner[0].orelse


def test_while_with_break_and_continue():
    asm = (Asm().label("TOP").jumpifnot("END", COND)
           .jumpifnot("L2", COND2).jump("END")              # break
           .label("L2").jumpifnot("L3", true_()).jump("TOP")  # continue
           .label("L3").s(A).jump("TOP")
           .label("END").s(ret()))
    t, g = roundtrip(asm)
    assert g == 0 and isinstance(t[0], SWhile)
    k = count_kinds(t)
    assert k.get("SBreak") == 1 and k.get("SContinue") == 1


def test_for_loop_detected_from_init_and_increment():
    i = local(4)
    asm = (Asm().s(let(i, int_zero()))
           .label("TOP").jumpifnot("END", native(0x59, i, int_byte(10)))
           .s(A)
           .s(native(0x68, i))                               # i++
           .jump("TOP")
           .label("END").s(ret()))
    t, g = roundtrip(asm)
    assert g == 0 and isinstance(t[0], SFor) and t[0].init is not None


def test_for_loop_proved_by_continue_to_increment():
    i = local(4)
    asm = (Asm().label("TOP").jumpifnot("END", COND)
           .jumpifnot("L1", COND2).jump("INC")               # continue -> increment
           .label("L1").s(A)
           .label("INC").s(native(0x68, i))
           .jump("TOP")
           .label("END").s(ret()))
    t, g = roundtrip(asm)
    assert g == 0 and isinstance(t[0], SFor) and t[0].init is None
    assert count_kinds(t).get("SContinue") == 1


def test_while_whose_else_ends_at_back_jump_is_not_a_for():
    i = local(4)
    asm = (Asm().s(let(i, int_zero()))
           .label("TOP").jumpifnot("END", native(0x59, i, int_byte(10)))
           .jumpifnot("ELSE", COND).s(A).jump("BACK")
           .label("ELSE").s(native(0x68, i))
           .label("BACK").jump("TOP")
           .label("END").s(ret()))
    t, g = roundtrip(asm)
    assert g == 0 and isinstance(t[1], SWhile)


def test_switch_with_default_and_breaks():
    asm = (Asm().switch(local(5))
           .case("C2", byte_const(1)).s(A).jump("END")
           .label("C2").case("D", byte_const(2))
           .label("D2").s(B).jump("END")
           .label("D").default().s(C).jump("END")
           .label("END").s(ret()))
    # fall-through label D2 is unused on purpose (labels are positions only)
    t, g = roundtrip(asm)
    assert g == 0 and isinstance(t[0], SSwitch) and len(t[0].cases) == 3
    assert count_kinds(t).get("SBreak") == 3


def test_switch_without_default_ends_at_last_case_next():
    asm = (Asm().switch(local(5))
           .case("C2", byte_const(1)).s(A).jump("END")
           .label("C2").case("END", byte_const(2)).s(B)
           .label("END").s(C).s(ret()))
    t, g = roundtrip(asm)
    assert g == 0 and isinstance(t[0], SSwitch) and len(t[0].cases) == 2
    assert len(t) == 3      # switch, C, return


def test_foreach_continue_break_and_return():
    it = call(7, local(1))
    asm = (Asm().foreach("POP", it)
           .jumpifnot("L1", COND).s(iterator_next())        # continue
           .label("L1").jumpifnot("L2", COND2).jump("POP")  # break
           .label("L2").jumpifnot("L3", true_()).s(iterator_pop()).s(ret())   # return inside foreach
           .label("L3").s(A)
           .s(iterator_next())
           .label("POP").s(iterator_pop())
           .s(ret()))
    t, g = roundtrip(asm)
    assert g == 0 and isinstance(t[0], SForEach)
    k = count_kinds(t)
    assert k.get("SContinue") == 1 and k.get("SBreak") == 1
    rets = [s for s in t[0].body[2].then if isinstance(s, SReturn)]
    assert rets and len(rets[0].pops) == 1


def test_do_until():
    asm = Asm().label("TOP").s(A).s(B).jumpifnot("TOP", COND).s(ret())
    t, g = roundtrip(asm)
    assert g == 0 and isinstance(t[0], SDo) and len(t[0].body) == 2


def test_irreducible_jump_stays_goto():
    asm = (Asm().jumpifnot("L1", COND).s(A).label("MID").s(B)
           .label("L1").jumpifnot("END", COND2).jump("MID")
           .label("END").s(ret()))
    t, g = roundtrip(asm)
    assert g >= 1
    assert any(isinstance(s, SLabel) and s.generated for s in _flatten(t))
    assert any(isinstance(s, SGoto) for s in _flatten(t))


def test_state_code_labels_and_label_table():
    names = {4: "Begin", 5: "Loop"}
    asm = (Asm().label("Begin").s(A)
           .label("Loop").s(B).jumpifnot("Loop", COND)
           .s(stop()).s(nothing()).s(nothing()).s(nothing())
           .label_table([(4, "Begin"), (5, "Loop")]))
    t, g = roundtrip(asm, labels={"Begin": 0, "Loop": 0}, name_text=lambda i: names.get(i, "None"))
    assert g == 0
    labels = [s.name for s in _flatten(t) if isinstance(s, SLabel)]
    assert labels == ["Begin", "Loop"]


def _flatten(tree):
    for s in tree:
        yield s
        for attr in ("then", "orelse", "body"):
            v = getattr(s, attr, None)
            if v:
                yield from _flatten(v)
        if isinstance(s, SSwitch):
            for _, b in s.cases:
                yield from _flatten(b)
