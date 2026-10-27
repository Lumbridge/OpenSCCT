"""Loader, printer and class emitter on a small synthetic v300 package written by the test."""

import os

import pytest

from ucdecomp.bytecode import write_compact
from ucdecomp.check import check_package
from ucdecomp.emit import ClassEmitter
from ucdecomp.loader import (CPF_Edit, CPF_Net, CPF_OutParm, CPF_Parm, CPF_ReturnParm, FUNC_Defined, FUNC_Net,
                             FUNC_NetReliable, FUNC_Public, FUNC_Simulated, Class, Function, LoadedPackage, Property,
                             State)
from ucdecomp.tests.synth import PackageWriter, i32, name_ref, u16, u32
from ucdecomp.universe import Universe

NAMES = ["None", "Core", "Package", "Class", "Function", "IntProperty", "BoolProperty", "State", "TestPkg",
         "Base", "TestClass", "Add", "A", "ReturnValue", "Count", "bReady", "Idle", "Begin", "System",
         "Role", "ServerPing"]

RF_Public = 0x00000004


def obj_header() -> bytes:
    return name_ref(0)          # empty tagged property list (None)


def ufield(sup: int, nxt: int) -> bytes:
    return write_compact(sup) + write_compact(nxt)


def ustruct(children: int, friendly: int, script: bytes, script_size: int, line: int = 0) -> bytes:
    return (write_compact(0) + write_compact(children) + name_ref(friendly) + write_compact(0)
            + i32(line) + i32(0) + i32(script_size) + script)


def uproperty(nxt: int, flags: int, rep: int = None) -> bytes:
    b = obj_header() + ufield(0, nxt) + i32(1) + u32(flags) + name_ref(0)
    if flags & CPF_Net:
        b += u16(rep)
    return b


def ustate_tail(ignore=0xFFFFFFFFFFFFFFFF, lto=0xFFFF, sflags=0) -> bytes:
    return (0).to_bytes(8, "little") + ignore.to_bytes(8, "little") + u16(lto) + u32(sflags)


def uclass_tail(flags: int, config: int) -> bytes:
    return (u32(flags) + bytes(16) + write_compact(0) + write_compact(0) + write_compact(0)
            + name_ref(config) + write_compact(0))


@pytest.fixture(scope="module")
def universe(tmp_path_factory):
    w = PackageWriter(NAMES)
    n = w.n
    core = w.add_import("Core", "Package", 0, "Core")
    c_function = w.add_import("Core", "Class", core, "Function")
    c_int = w.add_import("Core", "Class", core, "IntProperty")
    c_bool = w.add_import("Core", "Class", core, "BoolProperty")
    c_state = w.add_import("Core", "Class", core, "State")
    # exports, data filled in below once every ref is known
    base = w.add_export(0, 0, 0, "Base", RF_Public, b"")
    cls = w.add_export(0, base, 0, "TestClass", RF_Public, b"")
    add = w.add_export(c_function, 0, cls, "Add", RF_Public, b"")
    p_a = w.add_export(c_int, 0, add, "A", RF_Public, b"")
    p_ret = w.add_export(c_int, 0, add, "ReturnValue", RF_Public, b"")
    count = w.add_export(c_int, 0, cls, "Count", RF_Public, b"")
    ready = w.add_export(c_bool, 0, cls, "bReady", RF_Public, b"")
    idle = w.add_export(c_state, 0, cls, "Idle", RF_Public, b"")
    ping = w.add_export(c_function, 0, cls, "ServerPing", RF_Public, b"")
    role = w.add_export(c_int, 0, base, "Role", RF_Public, b"")

    # Base: one int var, no script
    w.set_data(base, ufield(0, 0) + ustruct(role, n("Base"), b"", 0) + ustate_tail() + uclass_tail(0, n("System"))
               + name_ref(0))
    w.set_data(role, uproperty(0, 0))
    # TestClass: replication condition script = (Role) at mem 0 then Nothing
    rep_script = bytes([0x01]) + write_compact(role) + b"\x0B"
    defaults = name_ref(n("Count")) + bytes([0x22]) + i32(5) + name_ref(0)   # Count=5 (IntProperty, 4 bytes)
    w.set_data(cls, ufield(base, 0) + ustruct(add, n("TestClass"), rep_script, 6) + ustate_tail()
               + uclass_tail(0, n("System")) + defaults)
    # function int Add(int A) { if (bReady) Count = A; return A; }
    body = (bytes([0x07]) + u16(0x14) + bytes([0x2D, 0x01]) + write_compact(ready)       # JumpIfNot(0x14, bReady)
            + bytes([0x0F, 0x01]) + write_compact(count) + bytes([0x00]) + write_compact(p_a)  # Count = A
            + bytes([0x04, 0x00]) + write_compact(p_a)                                          # return A
            + bytes([0x04, 0x0B]))
    body_mem = 9 + 11 + 6 + 2
    w.set_data(add, obj_header() + ufield(0, idle) + ustruct(p_a, n("Add"), body, body_mem, line=12)
               + u16(0) + bytes([0]) + u32(FUNC_Defined | FUNC_Public))
    w.set_data(p_a, uproperty(p_ret, CPF_Parm))
    w.set_data(p_ret, uproperty(0, CPF_Parm | CPF_OutParm | CPF_ReturnParm))
    w.set_data(count, uproperty(ready, CPF_Edit | CPF_Net, rep=0))
    w.set_data(ready, uproperty(ping, 0))
    # state Idle { Begin: Count = 0; stop; } with a label table
    code = (bytes([0x0F, 0x01]) + write_compact(count) + b"\x25" + b"\x08" + b"\x0B" * 3
            + b"\x0C" + name_ref(n("Begin")) + u32(0) + name_ref(0) + u32(0xFFFF))
    code_mem = 7 + 1 + 3 + 1 + 16
    w.set_data(idle, obj_header() + ufield(0, count) + ustruct(0, n("Idle"), code, code_mem)
               + ustate_tail(lto=12, sflags=0x2))
    # replicated function: reliable if (Role) ServerPing;
    w.set_data(ping, obj_header() + ufield(0, 0) + ustruct(0, n("ServerPing"), b"\x04\x0B", 2)
               + u16(0) + bytes([0]) + u32(FUNC_Defined | FUNC_Public | FUNC_Net | FUNC_NetReliable | FUNC_Simulated)
               + u16(0))
    # the class Children chain is Add -> Idle -> Count -> bReady -> ServerPing
    d = tmp_path_factory.mktemp("pkg")
    path = os.path.join(str(d), "TestPkg.u")
    with open(path, "wb") as fh:
        fh.write(w.build())
    lp = LoadedPackage(path)
    return Universe({"TestPkg": lp})


def test_loader_reads_every_export(universe):
    lp = universe.packages["TestPkg"]
    assert lp.errors == {}
    kinds = sorted(f.kind for f in lp.fields.values())
    assert kinds.count("Class") == 2 and kinds.count("Function") == 2 and kinds.count("State") == 1
    add = universe.find("TestPkg.TestClass.Add")
    assert isinstance(add, Function) and add.script_size == 28 and add.line == 12
    assert [m.name for m in universe.members(add)] == ["A", "ReturnValue"]
    idle = universe.find("TestPkg.TestClass.Idle")
    assert isinstance(idle, State) and idle.label_table_offset == 12 and idle.state_flags == 2
    count = universe.find("TestPkg.TestClass.Count")
    assert isinstance(count, Property) and count.flags & CPF_Net and count.rep_offset == 0
    cls = universe.find("TestPkg.TestClass")
    assert isinstance(cls, Class) and cls.config_name == "System"
    assert universe.super_of(cls).name == "Base"


def test_check_round_trips(universe):
    r = check_package(universe, "TestPkg")
    assert r["T1_ok"] == r["with_script"] == 4
    assert r["T2_ok"] == 4 and r.get("unstructured_jumps", 0) == 0


def test_emitted_class_text(universe):
    cls = universe.find("TestPkg.TestClass")
    text, info = ClassEmitter(universe, cls).emit()
    assert text.startswith("class TestClass extends Base")
    assert "var() int Count;" in text and "var bool bReady;" in text
    assert "function int Add(int A)" in text
    assert "if (bReady)" in text and "Count = A;" in text and "return A;" in text
    assert "auto state Idle" in text and "Begin:" in text and "Count = 0;" in text
    assert "reliable if (Role)" in text
    assert "ServerPing, Count;" in text
    assert "Count=5" in text
    names = [f["name"] for f in info["functions"]]
    assert names == ["ServerPing", "Add"]      # class members are printed in reverse chain order
    assert info["functions"][0]["sha1"] and info["states"][0]["labels"] == [["Begin", 0]] or \
        info["states"][0]["labels"] == [("Begin", 0)]
