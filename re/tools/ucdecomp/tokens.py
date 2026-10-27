"""UnrealScript bytecode tokens as stored in SCCT Versus v300 packages.

Each token has a shape: the operand items that follow the token byte, in serialization order.
Item kinds and their sizes (file form / in-memory form, the latter is what ScriptSize and every jump
offset count):

    u8      1 / 1        u16     2 / 2        i32, u32, f32   4 / 4
    obj     compact object index / 4 (a pointer)
    name    two compacts (local name index, cooked global index) / 4 (an FName)
    expr    one nested expression
    params  expressions up to and including EX_EndFunctionParms (0x16)
    cstr    NUL-terminated 8-bit string (bytes as stored)
    ustr    NUL-terminated 16-bit string
    case    u16 offset, then an expression unless the offset is 0xFFFF (default:)
    labels  label entries (name, u32 offset) up to a None name

The table follows the UE2 layout with the SCCT changes found in these packages: native functions
start at token 0x56 and extended natives use 0x46-0x55 (16 x 256 indices).
"""

from __future__ import annotations

# token -> (mnemonic, shape)
TOKENS = {
    0x00: ("LocalVariable", ("obj",)),
    0x01: ("InstanceVariable", ("obj",)),
    0x02: ("DefaultVariable", ("obj",)),
    0x04: ("Return", ("expr",)),
    0x05: ("Switch", ("u8", "expr")),
    0x06: ("Jump", ("u16",)),
    0x07: ("JumpIfNot", ("u16", "expr")),
    0x08: ("Stop", ()),
    0x09: ("Assert", ("u16", "expr")),
    0x0A: ("Case", ("case",)),
    0x0B: ("Nothing", ()),
    0x0C: ("LabelTable", ("labels",)),
    0x0D: ("GotoLabel", ("expr",)),
    0x0E: ("EatString", ("expr",)),
    0x0F: ("Let", ("expr", "expr")),
    0x10: ("DynArrayElement", ("expr", "expr")),
    0x11: ("New", ("expr", "expr", "expr", "expr")),
    0x12: ("ClassContext", ("expr", "u16", "u8", "expr")),
    0x13: ("MetaCast", ("obj", "expr")),
    0x14: ("LetBool", ("expr", "expr")),
    0x15: ("EndParmValue", ()),
    0x16: ("EndFunctionParms", ()),
    0x17: ("Self", ()),
    0x18: ("Skip", ("u16", "expr")),
    0x19: ("Context", ("expr", "u16", "u8", "expr")),
    0x1A: ("ArrayElement", ("expr", "expr")),
    0x1B: ("VirtualFunction", ("name", "params")),
    0x1C: ("FinalFunction", ("obj", "params")),
    0x1D: ("IntConst", ("i32",)),
    0x1E: ("FloatConst", ("f32",)),
    0x1F: ("StringConst", ("cstr",)),
    0x20: ("ObjectConst", ("obj",)),
    0x21: ("NameConst", ("name",)),
    0x22: ("RotationConst", ("i32", "i32", "i32")),
    0x23: ("VectorConst", ("f32", "f32", "f32")),
    0x24: ("ByteConst", ("u8",)),
    0x25: ("IntZero", ()),
    0x26: ("IntOne", ()),
    0x27: ("True", ()),
    0x28: ("False", ()),
    0x29: ("NativeParm", ("obj",)),
    0x2A: ("NoObject", ()),
    0x2C: ("IntConstByte", ("u8",)),
    0x2D: ("BoolVariable", ("expr",)),
    0x2E: ("DynamicCast", ("obj", "expr")),
    0x2F: ("Iterator", ("expr", "u16")),
    0x30: ("IteratorPop", ()),
    0x31: ("IteratorNext", ()),
    0x32: ("StructCmpEq", ("obj", "expr", "expr")),
    0x33: ("StructCmpNe", ("obj", "expr", "expr")),
    0x34: ("UnicodeStringConst", ("ustr",)),
    0x36: ("StructMember", ("obj", "expr")),
    0x37: ("DynArrayLength", ("expr",)),
    0x38: ("GlobalFunction", ("name", "params")),
    0x39: ("PrimitiveCast", ("u8", "expr")),
    0x40: ("DynArrayInsert", ("expr", "expr", "expr")),
    0x41: ("DynArrayRemove", ("expr", "expr", "expr")),
    0x43: ("DelegateFunction", ("obj", "name", "params")),
    0x44: ("DelegateProperty", ("name",)),
    0x45: ("LetDelegate", ("expr", "expr")),
}

EX_ExtendedNative = 0x46   # 0x46..0x55: index = ((tok - 0x46) << 8) | next byte
EX_FirstNative = 0x56      # 0x56..0xFF: index = tok
EXTENDED_SHAPE = ("u8", "params")
NATIVE_SHAPE = ("params",)

MEM_SIZE = {"u8": 1, "u16": 2, "i32": 4, "u32": 4, "f32": 4, "obj": 4, "name": 4}

# Named tokens used by later passes.
EX_LocalVariable = 0x00
EX_InstanceVariable = 0x01
EX_DefaultVariable = 0x02
EX_Return = 0x04
EX_Switch = 0x05
EX_Jump = 0x06
EX_JumpIfNot = 0x07
EX_Stop = 0x08
EX_Assert = 0x09
EX_Case = 0x0A
EX_Nothing = 0x0B
EX_LabelTable = 0x0C
EX_GotoLabel = 0x0D
EX_EatString = 0x0E
EX_Let = 0x0F
EX_DynArrayElement = 0x10
EX_New = 0x11
EX_ClassContext = 0x12
EX_MetaCast = 0x13
EX_LetBool = 0x14
EX_EndParmValue = 0x15
EX_EndFunctionParms = 0x16
EX_Self = 0x17
EX_Skip = 0x18
EX_Context = 0x19
EX_ArrayElement = 0x1A
EX_VirtualFunction = 0x1B
EX_FinalFunction = 0x1C
EX_IntConst = 0x1D
EX_FloatConst = 0x1E
EX_StringConst = 0x1F
EX_ObjectConst = 0x20
EX_NameConst = 0x21
EX_RotationConst = 0x22
EX_VectorConst = 0x23
EX_ByteConst = 0x24
EX_IntZero = 0x25
EX_IntOne = 0x26
EX_True = 0x27
EX_False = 0x28
EX_NativeParm = 0x29
EX_NoObject = 0x2A
EX_IntConstByte = 0x2C
EX_BoolVariable = 0x2D
EX_DynamicCast = 0x2E
EX_Iterator = 0x2F
EX_IteratorPop = 0x30
EX_IteratorNext = 0x31
EX_StructCmpEq = 0x32
EX_StructCmpNe = 0x33
EX_UnicodeStringConst = 0x34
EX_StructMember = 0x36
EX_DynArrayLength = 0x37
EX_GlobalFunction = 0x38
EX_PrimitiveCast = 0x39
EX_DynArrayInsert = 0x40
EX_DynArrayRemove = 0x41
EX_DelegateFunction = 0x43
EX_DelegateProperty = 0x44
EX_LetDelegate = 0x45

# Primitive cast codes (the byte after EX_PrimitiveCast), UE2 numbering.
CASTS = {
    0x39: ("RotatorToVector", "vector"),
    0x3A: ("ByteToInt", "int"),
    0x3B: ("ByteToBool", "bool"),
    0x3C: ("ByteToFloat", "float"),
    0x3D: ("IntToByte", "byte"),
    0x3E: ("IntToBool", "bool"),
    0x3F: ("IntToFloat", "float"),
    0x40: ("BoolToByte", "byte"),
    0x41: ("BoolToInt", "int"),
    0x42: ("BoolToFloat", "float"),
    0x43: ("FloatToByte", "byte"),
    0x44: ("FloatToInt", "int"),
    0x45: ("FloatToBool", "bool"),
    0x46: ("ObjectToInt", "int"),
    0x47: ("ObjectToBool", "bool"),
    0x48: ("NameToBool", "bool"),
    0x49: ("StringToByte", "byte"),
    0x4A: ("StringToInt", "int"),
    0x4B: ("StringToBool", "bool"),
    0x4C: ("StringToFloat", "float"),
    0x4D: ("StringToVector", "vector"),
    0x4E: ("StringToRotator", "rotator"),
    0x4F: ("VectorToBool", "bool"),
    0x50: ("VectorToRotator", "rotator"),
    0x51: ("RotatorToBool", "bool"),
    0x52: ("ByteToString", "string"),
    0x53: ("IntToString", "string"),
    0x54: ("BoolToString", "string"),
    0x55: ("FloatToString", "string"),
    0x56: ("ObjectToString", "string"),
    0x57: ("NameToString", "string"),
    0x58: ("VectorToString", "string"),
    0x59: ("RotatorToString", "string"),
}


def shape_of(tok: int):
    if tok >= EX_FirstNative:
        return NATIVE_SHAPE
    if tok >= EX_ExtendedNative:
        return EXTENDED_SHAPE
    t = TOKENS.get(tok)
    return t[1] if t else None


def mnemonic(tok: int) -> str:
    if tok >= EX_FirstNative:
        return "Native"
    if tok >= EX_ExtendedNative:
        return "ExtendedNative"
    t = TOKENS.get(tok)
    return t[0] if t else "Unknown%02X" % tok
