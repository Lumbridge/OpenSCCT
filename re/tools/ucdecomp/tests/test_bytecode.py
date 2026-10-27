"""Token decoder and encoder on hand-written byte sequences."""

import pytest

from ucdecomp import tokens as T
from ucdecomp.bytecode import (DecodeError, decode_script, encode_script, mem_size, read_compact, relayout,
                               write_compact)


@pytest.mark.parametrize("v", [0, 1, 63, 64, 65, 127, 8191, 8192, 1 << 20, (1 << 27) + 5, -1, -63, -64, -100000])
def test_compact_round_trip(v):
    b = write_compact(v)
    got, end = read_compact(b, 0)
    assert got == v and end == len(b)


def test_compact_known_bytes():
    assert write_compact(0) == b"\x00"
    assert write_compact(-13) == b"\x8d"
    assert write_compact(112) == b"\x70\x01"


def dec(b, size, none=lambda i: i == 0):
    stmts, end = decode_script(b, 0, size, none)
    assert end == len(b)
    assert encode_script(stmts) == b
    assert sum(mem_size(s) for s in stmts) == size
    return stmts


def test_let_return_offsets():
    # Let(Local 5, IntConst 7); Return(Nothing)
    b = bytes([0x0F, 0x00, 0x05, 0x1D, 7, 0, 0, 0, 0x04, 0x0B])
    s = dec(b, 13)
    assert [x.tok for x in s] == [T.EX_Let, T.EX_Return]
    assert [x.mem for x in s] == [0, 11]
    assert s[0].vals[0].vals[0] == 5 and s[0].vals[1].vals[0] == 7


def test_multi_byte_object_ref_counts_four_in_memory():
    # InstanceVariable with a two-byte compact ref is still 5 bytes in memory
    b = bytes([0x01, 0x70, 0x01, 0x0B])
    s = dec(b, 6)
    assert s[0].vals[0] == 112 and s[1].mem == 5


def test_names_are_paired_compacts():
    # VirtualFunction('n3' global 200) with one IntZero argument, then Nothing
    b = bytes([0x1B, 0x03]) + write_compact(200) + bytes([0x25, 0x16, 0x0B])
    s = dec(b, 1 + 4 + 1 + 1 + 1)
    assert s[0].vals[0] == (3, 200)
    assert len(s[0].params) == 1


def test_natives_and_extended_natives():
    # native 0x70(IntOne, IntZero); extended native 0x47 0x39 = index 0x139 with no arguments
    b = bytes([0x70, 0x26, 0x25, 0x16, 0x47, 0x39, 0x16])
    s = dec(b, 4 + 3)
    assert s[0].native_index == 0x70 and len(s[0].params) == 2
    assert s[1].native_index == 0x139 and s[1].params == []


def test_constants():
    b = (bytes([0x1F]) + b"hi\0"                       # string
         + bytes([0x34]) + "é".encode("utf-16-le") + b"\0\0"   # unicode string
         + bytes([0x1E]) + bytes.fromhex("0000003f")    # 0.5
         + bytes([0x22]) + bytes(12)                    # rot
         + bytes([0x23]) + bytes(12)                    # vect
         + bytes([0x24, 9, 0x2C, 200, 0x27, 0x28, 0x2A, 0x17]))
    size = 4 + 5 + 5 + 13 + 13 + 2 + 2 + 1 + 1 + 1 + 1
    s = dec(b, size)
    assert s[0].vals[0] == b"hi"
    assert s[1].vals[0] == [0xE9]
    assert s[2].vals[0] == 0x3F000000


def test_case_default_has_no_expression():
    b = bytes([0x0A, 0x10, 0x00, 0x24, 1, 0x0A, 0xFF, 0xFF])
    s = dec(b, 5 + 3)
    assert s[0].vals[0][1] is not None and s[1].vals[0] == (0xFFFF, None)


def test_context_and_skip_sizes_kept():
    # Context(Local 1, skip 0x0B, size 4, InstanceVariable 2); Skip(0x6, IntZero) inside a native
    b = bytes([0x19, 0x00, 0x01, 0x0B, 0x00, 4, 0x01, 0x02, 0x6B, 0x27, 0x18, 0x03, 0x00, 0x28, 0x16])
    s = dec(b, 1 + 5 + 2 + 1 + 5 + 1 + 1 + 3 + 1 + 1)
    assert s[0].vals[1] == 0x0B and s[0].vals[2] == 4
    assert s[1].params[1].tok == T.EX_Skip


def test_label_table_stops_at_none():
    # LabelTable: ('n4' @ 0), (None @ 0xFFFF)
    b = bytes([0x0C, 0x04, 0x04, 0, 0, 0, 0, 0x00, 0x00, 0xFF, 0xFF, 0, 0])
    s = dec(b, 1 + 16)
    assert len(s[0].vals[0]) == 2


def test_dynamic_arrays_and_delegates():
    # Insert(Local 1, IntZero, IntOne); Remove(...); Length(Local 1); DelegateFunction(o3, n5, ()); DelegateProperty
    b = (bytes([0x40, 0x00, 0x01, 0x25, 0x26, 0x41, 0x00, 0x01, 0x25, 0x26, 0x37, 0x00, 0x01])
         + bytes([0x43, 0x03, 0x05, 0x05, 0x16, 0x44, 0x06, 0x06]))
    s = dec(b, 8 + 8 + 6 + 1 + 4 + 4 + 1 + 1 + 4)
    assert [x.tok for x in s] == [T.EX_DynArrayInsert, T.EX_DynArrayRemove, T.EX_DynArrayLength,
                                  T.EX_DelegateFunction, T.EX_DelegateProperty]


def test_unknown_token_and_overrun():
    with pytest.raises(DecodeError):
        decode_script(bytes([0x03]), 0, 1)
    with pytest.raises(DecodeError):
        decode_script(bytes([0x1D, 1, 0, 0, 0]), 0, 3)     # IntConst is 5 bytes, ScriptSize says 3
    with pytest.raises(DecodeError):
        decode_script(bytes([0x1B, 0x01, 0x00]), 0, 10)    # missing EndFunctionParms


def test_relayout_matches_decoded_offsets():
    b = bytes([0x0F, 0x00, 0x05, 0x1D, 7, 0, 0, 0, 0x04, 0x0B])
    s = dec(b, 13)
    want = [x.mem for x in s]
    for x in s:
        x.mem = -1
    assert relayout(s) == 13 and [x.mem for x in s] == want
