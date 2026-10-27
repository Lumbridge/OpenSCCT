from __future__ import annotations

import json
import struct

import pytest
from svmbuild import canonical
from svmbuild.paths import SPEC_ROOT
from svmbuild.vectors import OUT as VECTORS
from svmbuild.vectors import build as build_vectors


def _f(bits: str) -> float:
    return struct.unpack("<f", struct.pack("<I", int(bits, 16)))[0]


def _vectors() -> dict:
    return json.loads(VECTORS.read_text(encoding="utf-8"))


def test_vectors_file_is_current():
    assert VECTORS.read_bytes() == canonical.dump_file(build_vectors())


@pytest.mark.parametrize("case", _vectors()["float32Format"], ids=lambda c: c["bits"])
def test_float32_format(case):
    assert canonical.format_f32(_f(case["bits"])) == case["text"]
    assert "%08x" % canonical.f32_bits(canonical.parse_f32(case["text"])) == case["bits"]


@pytest.mark.parametrize("case", _vectors()["float32Parse"], ids=lambda c: c["token"][:24])
def test_float32_parse(case):
    assert "%08x" % canonical.f32_bits(canonical.parse_f32(case["token"])) == case["bits"]


@pytest.mark.parametrize("case", _vectors()["json"], ids=lambda c: c["sha256"][:8])
def test_canonical_json(case):
    c = canonical.canonical_bytes(case["input"])
    assert c.hex() == case["canonicalHex"]
    assert c.decode("utf-8") == case["canonical"]
    assert canonical.sha256_hex(c) == case["sha256"]


def test_group_hash_vector():
    g = _vectors()["group"]
    digests = [(f["path"], canonical.file_digest(f["path"], f["content"].encode("utf-8"))) for f in g["files"]]
    assert canonical.group_listing(digests) == g["listing"]
    assert canonical.group_hash(digests) == g["sha256"]


def test_double_rounding_case_differs_from_naive_parse():
    token = _vectors()["float32Parse"][-1]["token"]
    naive = struct.unpack("<f", struct.pack("<f", float(token)))[0]
    assert canonical.f32_bits(naive) != canonical.f32_bits(canonical.parse_f32(token))


def test_layout_does_not_change_hash():
    obj = {"b": [1.0, {"x": 2}], "a": "s"}
    pretty = canonical.dumps(obj, indent=1)
    compact = canonical.dumps(obj)
    assert canonical.canonical_bytes(pretty) == canonical.canonical_bytes(compact) == compact.encode("utf-8")
    assert canonical.file_digest("sim/x.json", pretty.encode()) == canonical.file_digest("sim/x.json", compact.encode())
    # Outside sim/ files are raw bytes, so layout matters there.
    assert canonical.file_digest("render/x.json", pretty.encode()) != canonical.file_digest(
        "render/x.json", compact.encode())


def test_int_and_float_are_distinct():
    assert canonical.canonical_bytes('{"a": 10}') == b'{"a":10}'
    assert canonical.canonical_bytes('{"a": 10.0}') == b'{"a":10.0}'
    assert canonical.canonical_bytes('{"a": 1e1}') == b'{"a":10.0}'


@pytest.mark.parametrize("bad", ['{"a": 1, "a": 2}', '{"a": NaN}', '[Infinity]', '﻿{}', '[1e39]'])
def test_rejects(bad):
    with pytest.raises(ValueError):
        canonical.canonical_bytes(bad)


def test_rejects_non_ascii_keys():
    with pytest.raises(ValueError):
        canonical.dumps({"é": 1})


def test_conventions_table_matches_vectors():
    text = (SPEC_ROOT / "conventions.md").read_text(encoding="utf-8")
    rows = {c["bits"]: c["text"] for c in _vectors()["float32Format"]}
    found = 0
    for line in text.splitlines():
        if line.startswith("| `0x"):
            bits, value = [cell.strip().strip("`") for cell in line.strip("|").split("|")]
            assert rows[bits[2:]] == value
            found += 1
    assert found >= 8
