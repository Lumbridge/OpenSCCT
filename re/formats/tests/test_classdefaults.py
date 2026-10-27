"""Class default decoding on synthetic script-package bytes (no game data)."""

from pathlib import Path
from types import SimpleNamespace

import pytest

from scctformats import classdefaults as cd
from scctformats._env import bytes_reader
from scctformats._writer import Writer

NAMES = ["None", "Health", "Speed", "bOn", "Role", "Tag", "Title", "Mesh", "Cls", "Arr", "Dyn", "Loc", "Inner",
         "A", "B", "Vector", "MyStruct", "OnHit", "Label1", "Func", "System", "Engine", "Core", "ENetRole"]
N = {n: i for i, n in enumerate(NAMES)}


# ---- bytecode --------------------------------------------------------------------------------

def script():
    """(bytes, memory size) of a script that touches every operand kind."""
    w = Writer()
    mem = 0
    w.u8(0x0F).u8(0x01).compact(5).u8(0x1D).i32(42)             # Let(InstanceVariable, IntConst)
    mem += 1 + 5 + 5
    w.u8(0x1B).name(N["Func"]).u8(0x24).u8(7).u8(0x16)          # VirtualFunction(ByteConst) ;
    mem += 5 + 2 + 1
    w.u8(0x60).u8(0x27).u8(0x16)                                # native 0x60(True)
    mem += 3
    w.u8(0x47).u8(0x12).u8(0x16)                                # extended native
    mem += 3
    w.u8(0x1F).raw(b"hi\0")                                     # StringConst
    mem += 4
    w.u8(0x0A).u16(0xFFFF)                                      # Case default
    mem += 3
    w.u8(0x0A).u16(5).u8(0x25)                                  # Case 5: IntZero
    mem += 4
    w.u8(0x12).u8(0x17).u16(3).u8(4).u8(0x01).compact(-3)       # ClassContext(Self, skip, size, var)
    mem += 1 + 1 + 2 + 1 + 5
    w.u8(0x22).i32(1).i32(2).i32(3)                             # RotationConst
    mem += 13
    w.u8(0x23).f32(1).f32(2).f32(3)                             # VectorConst
    mem += 13
    w.u8(0x11).u8(0x2A).u8(0x2A).u8(0x2A).u8(0x20).compact(9)   # New(None, None, None, ObjectConst)
    mem += 1 + 3 + 5
    w.u8(0x32).compact(4).u8(0x25).u8(0x26)                     # StructCmpEq(struct, 0, 1)
    mem += 5 + 2
    w.u8(0x07).u16(0).u8(0x27)                                  # JumpIfNot
    mem += 4
    w.u8(0x0C).name(N["Label1"]).u32(0).name(N["None"]).u32(0xFFFF)   # LabelTable
    mem += 1 + 16
    w.u8(0x04).u8(0x0B)                                         # Return(Nothing)
    mem += 2
    return w.bytes(), mem


def test_script_walker_counts_memory_bytes():
    data, mem = script()
    r = bytes_reader(data, NAMES)
    w = cd.ScriptWalker(r)
    disk = w.run(mem)
    assert disk == len(data) and w.mem == mem
    assert disk < mem          # compact refs and names are shorter on disk than in memory


def test_script_walker_rejects_bad_tokens_and_overrun():
    with pytest.raises(cd.ClassFormatError, match="bad bytecode token"):
        cd.ScriptWalker(bytes_reader(bytes([0x03]), NAMES)).run(1)
    data, mem = script()
    with pytest.raises(cd.ClassFormatError, match="past ScriptSize"):
        cd.ScriptWalker(bytes_reader(data, NAMES)).run(mem - 1)


def test_native_token_ranges():
    assert cd.EX_EXTENDED_NATIVE == 0x46 and cd.EX_FIRST_NATIVE == 0x56
    assert all(t in cd.SCRIPT_OPS for t in range(0x46))


# ---- class header ----------------------------------------------------------------------------

def class_bytes(defaults: bytes, script_bytes=b"", script_mem=0) -> bytes:
    w = Writer()
    w.compact(-1).compact(0)                       # SuperField (import), Next
    w.compact(0).compact(3).name(N["None"])        # ScriptText, Children, FriendlyName
    w.compact(0)                                   # CppText
    w.i32(-1).i32(-1).i32(script_mem).raw(script_bytes)
    w.raw((0x00800000000000C0).to_bytes(8, "little")).raw(b"\xff" * 8).u16(0xFFFF).u32(0)
    w.u32(0x212).raw(b"\0" * 16)
    w.compact(1).compact(7).i32(1).u32(0x1234)    # one dependency
    w.compact(2).name(N["Engine"]).name(N["Core"])
    w.compact(-5).name(N["System"])               # within, config
    w.compact(1).name(N["Mesh"])                  # hide categories
    return w.bytes() + defaults


def test_class_header_fields():
    data, mem = script()
    body = class_bytes(Writer().name(0).bytes(), data, mem)
    d = {}
    r = bytes_reader(body, NAMES)
    r.compact(); r.compact()
    cd.read_class_header(r, 300, d)
    assert d["script_size"] == mem and d["script_disk"] == len(data)
    assert d["probe_mask"] == 0x00800000000000C0 and d["ignore_mask"] == 2 ** 64 - 1
    assert d["class_flags"] == 0x212 and d["dependencies"] == [(7, 1, 0x1234)]
    assert d["package_imports"] == ["Engine", "Core"] and d["within"] == -5 and d["config_name"] == "System"
    assert d["hide_categories"] == ["Mesh"]
    assert body[d["defaults_offset"]:] == b"\0\0"


# ---- tagged defaults -------------------------------------------------------------------------

class FakePkg:
    def __init__(self):
        self.path = Path("Test.u")
        self.exports = []
        self.names = NAMES
        self.names_obfuscated = False


def make_field(pkg, name, kind, **data):
    e = SimpleNamespace(name=name, outer=0, index=len(pkg.exports))
    pkg.exports.append(e)
    f = cd.Field(pkg, e, kind)
    f.data.update(data)
    return f


class FakeUniverse:
    def __init__(self):
        self.pkg = FakePkg()
        p = self.pkg
        self.enum = make_field(p, "ENetRole", "Enum", names=["ROLE_None", "ROLE_DumbProxy", "ROLE_SimulatedProxy"])
        self.vector = make_field(p, "Vector", "Struct")
        self.inner_int = make_field(p, "Inner", "IntProperty", array_dim=1, flags=0)
        self.mystruct = make_field(p, "MyStruct", "Struct")
        a = make_field(p, "A", "IntProperty", array_dim=1, flags=0)
        b = make_field(p, "B", "FloatProperty", array_dim=1, flags=0)
        self.struct_props = {"a": a, "b": b}
        ref = lambda f: f.export.index + 1
        self.cls = make_field(p, "TestClass", "Class")
        self.props = {
            "health": make_field(p, "Health", "IntProperty", array_dim=1, flags=0),
            "speed": make_field(p, "Speed", "FloatProperty", array_dim=1, flags=0),
            "bon": make_field(p, "bOn", "BoolProperty", array_dim=1, flags=0),
            "role": make_field(p, "Role", "ByteProperty", array_dim=1, flags=0x20, enum=ref(self.enum)),
            "tag": make_field(p, "Tag", "NameProperty", array_dim=1, flags=0),
            "title": make_field(p, "Title", "StrProperty", array_dim=1, flags=0x8000),
            "mesh": make_field(p, "Mesh", "ObjectProperty", array_dim=1, flags=0),
            "cls": make_field(p, "Cls", "ClassProperty", array_dim=1, flags=0),
            "arr": make_field(p, "Arr", "IntProperty", array_dim=4, flags=0),
            "dyn": make_field(p, "Dyn", "ArrayProperty", array_dim=1, flags=0, inner=ref(self.inner_int)),
            "loc": make_field(p, "Loc", "StructProperty", array_dim=1, flags=0, struct=ref(self.vector)),
            "mystruct": make_field(p, "MyStruct", "StructProperty", array_dim=1, flags=0, struct=ref(self.mystruct)),
            "onhit": make_field(p, "OnHit", "DelegateProperty", array_dim=1, flags=0),
        }

    def properties(self, f):
        return self.struct_props if f is self.mystruct else self.props

    def field_ref(self, pkg, ref):
        return pkg.exports[ref - 1]._field if ref > 0 else None

    def path(self, pkg, ref):
        return None if ref == 0 else f"Test.Obj{ref}"


def wire_fields(u):
    for f in [u.enum, u.vector, u.inner_int, u.mystruct, u.cls, *u.props.values(), *u.struct_props.values()]:
        f.export._field = f


def tag(w, name, ptype, size, value: bytes, index=None, flag=False, struct=None):
    code = {1: 0, 2: 1, 4: 2, 12: 3, 16: 4}.get(size, 5)
    info = ptype | (code << 4) | (0x80 if (flag or index is not None) else 0)
    w.name(N[name]).u8(info)
    if struct is not None:
        w.name(N[struct])
    if code == 5:
        w.u8(size)
    if index is not None:
        w.u8(index)
    w.raw(value)


def defaults_bytes():
    w = Writer()
    tag(w, "Health", 2, 4, (150).to_bytes(4, "little"))
    tag(w, "Speed", 4, 4, Writer().f32(440.0).bytes())
    tag(w, "bOn", 3, 0, b"", flag=True)                       # bool: value in the info byte, size byte 0
    tag(w, "Role", 1, 1, bytes([2]))
    tag(w, "Tag", 6, 1, Writer().name(N["Label1"]).bytes())   # size leaves out the global name index
    tag(w, "Title", 13, 5, Writer().fstring("Hi!").bytes())
    tag(w, "Mesh", 5, 1, Writer().compact(9).bytes())
    tag(w, "Cls", 5, 1, Writer().compact(-2).bytes())        # a ClassProperty is tagged as an object
    tag(w, "Arr", 2, 4, (7).to_bytes(4, "little"), index=2)
    tag(w, "Dyn", 9, 9, Writer().compact(2).i32(1).i32(2).bytes())
    tag(w, "Loc", 10, 12, Writer().vec3((1, 2, 3)).bytes(), struct="Vector")
    inner = Writer()
    tag(inner, "A", 2, 4, (5).to_bytes(4, "little"))
    inner.name(0)
    body = inner.bytes()
    tag(w, "MyStruct", 10, len(body) - 2, body, struct="MyStruct")   # nested list; size without global names
    tag(w, "OnHit", 7, 2, Writer().compact(3).name(N["Func"]).bytes())  # delegate on the old string id
    w.name(0)
    return w.bytes()


def test_tagged_defaults_decode_by_property_type():
    u = FakeUniverse()
    wire_fields(u)
    stats = cd.TagStats()
    dec = cd.Decoder(u, stats)
    r = bytes_reader(defaults_bytes(), NAMES)
    out = dec.tagged(r, u.pkg, u.cls)
    assert r.remaining() == 0
    assert out["Health"] == 150 and out["Speed"] == 440.0 and out["bOn"] is True
    assert out["Role"] == {"value": 2, "enum": "ROLE_SimulatedProxy"}
    assert out["Tag"] == "Label1" and out["Title"] == "Hi!"
    assert out["Mesh"] == "Test.Obj9" and out["Cls"] == "Test.Obj-2"
    assert out["Arr"] == {2: 7}
    assert out["Dyn"] == [1, 2]
    assert out["Loc"] == {"X": 1.0, "Y": 2.0, "Z": 3.0}
    assert out["MyStruct"] == {"A": 5}
    assert out["OnHit"] == {"object": "Test.Obj3", "function": "Func"}
    assert stats.type_mismatch == 0 and stats.unknown == 0 and stats.size_mismatch == 0


def test_unknown_property_is_kept_raw():
    u = FakeUniverse()
    wire_fields(u)
    stats = cd.TagStats()
    w = Writer()
    tag(w, "Label1", 2, 4, (1).to_bytes(4, "little"))
    w.name(0)
    out = cd.Decoder(u, stats).tagged(bytes_reader(w.bytes(), NAMES), u.pkg, u.cls)
    assert out["Label1"] == {"_unknown_type": "IntProperty", "_raw": "01000000"} and stats.unknown == 1


def test_effective_merge_of_static_arrays():
    u = FakeUniverse()
    prop = u.props["arr"]
    assert cd._merge(None, {2: 7}, prop) == [None, None, 7, None]
    assert cd._merge([1, None, 7, None], {0: 9}, prop) == [9, None, 7, None]
    assert cd._merge({"A": 1, "B": 2.0}, {"A": 5}, None) == {"A": 5, "B": 2.0}


# ---- localization ----------------------------------------------------------------------------

def test_int_files(tmp_path):
    (tmp_path / "Test.int").write_bytes(
        b'[TestClass]\r\nTitle="Hello there"\r\nNames=("a","b,c","")\r\nList=()\r\nArr[1]="x"\r\n; comment\r\n')
    loc = cd.read_localization(tmp_path, "Test")
    sect = loc["testclass"]
    assert sect["title"] == {None: "Hello there"}
    assert sect["names"] == {None: ["a", "b,c", ""]}
    assert sect["list"] == {None: []}
    assert sect["arr"] == {1: "x"}
    assert cd.read_localization(tmp_path, "Missing") == {}


def test_live_value_helpers():
    assert cd._zero({"value": 0, "enum": "X"}) and cd._zero([0, 0.0, None]) and not cd._zero({"X": 1.0})
    assert cd._same(0.1, 0.10000000149011612)
    assert cd._same({"value": 2, "enum": "ROLE_SimulatedProxy"}, {"value": 2, "enum": "ROLE_SimulatedProxy"})
    assert cd._same("Engine.Group.Thing", "Pkg.Thing")
    assert not cd._same(1, 2)
