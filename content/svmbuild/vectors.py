"""Writes spec/svmap/1.0/vectors/canonical.json: test cases for the float32 text form, canonical JSON and group
hashes, shared by the Python tests and the C# reader tests.

    python -m svmbuild.vectors [--check]
"""

from __future__ import annotations

import argparse
import struct
from decimal import Decimal, getcontext
from fractions import Fraction

from . import canonical
from .paths import SPEC_ROOT

OUT = SPEC_ROOT / "vectors" / "canonical.json"


def _bits(x: float) -> str:
    return "%08x" % canonical.f32_bits(x)


def _from_bits(h: str) -> float:
    return struct.unpack("<f", struct.pack("<I", int(h, 16)))[0]


def _exact_decimal(q: Fraction) -> str:
    getcontext().prec = 80
    return format(Decimal(q.numerator) / Decimal(q.denominator), "f")


# (binary32 bits, expected text)
FORMAT_CASES = [
    ("00000000", "0.0"), ("80000000", "-0.0"), ("3f800000", "1.0"), ("bfc00000", "-1.5"), ("3dcccccd", "0.1"),
    ("3e99999a", "0.3"), ("3eaaaaab", "0.33333334"), ("42c80000", "100.0"), ("33d6bf95", "0.0000001"),
    ("4b800000", "16777216.0"), ("4ceb79a3", "123456790.0"), ("47800040", "65536.5"), ("4313cd3a", "147.80167"),
    ("7f7fffff", "340282350000000000000000000000000000000.0"),
    ("00000001", "0.000000000000000000000000000000000000000000001"),
    ("00800000", "0.000000000000000000000000000000000000011754944"),
]
# (decimal token, expected binary32 bits): ties to even and a case that double rounding gets wrong.
_ABOVE_MID = Fraction(1) + Fraction(1, 2 ** 24) + Fraction(1, 2 ** 60)
PARSE_CASES = [
    ("0.1", "3dcccccd"), ("-0.0", "80000000"), ("1e-50", "00000000"), ("-1e-50", "80000000"),
    ("16777217.0", "4b800000"), ("16777219.0", "4b800002"), ("3.4028235e38", "7f7fffff"),
    ("0.10000000149011612", "3dcccccd"), (_exact_decimal(_ABOVE_MID), "3f800001"),
]
JSON_CASES = [
    '{"b": 1, "a": [1.0, 2, -0.0, 0.1000000001, 1e3], "c": {"z": null, "y": true, "x": "q\\"\\\\/\\u00e9\\n\\u0001"}}',
    '[]', '{}', '"\\u007f\\u2028"', '{"Location": {"X": 512.0, "Y": -1024.5, "Z": 0.30000001192092896}}',
]


def build() -> dict:
    fmt = []
    for bits, text in FORMAT_CASES:
        got = canonical.format_f32(_from_bits(bits))
        if got != text:
            raise AssertionError(f"format {bits}: {got} != {text}")
        fmt.append({"bits": bits, "text": text})
    parse = []
    for token, bits in PARSE_CASES:
        got = _bits(canonical.parse_f32(token))
        if got != bits:
            raise AssertionError(f"parse {token}: {got} != {bits}")
        parse.append({"token": token, "bits": bits})
    js = []
    for text in JSON_CASES:
        c = canonical.canonical_bytes(text)
        js.append({"input": text, "canonical": c.decode("utf-8"), "canonicalHex": c.hex(),
                   "sha256": canonical.sha256_hex(c)})
    files = [("sim/traversal.json", '{"ges": [], "map": "X", "formatVersion": "1.0.0"}'),
             ("sim/gameplay.json", '{"map": "X"}')]
    digests = [(p, canonical.file_digest(p, t.encode("utf-8"))) for p, t in files]
    group = {
        "files": [{"path": p, "content": t} for p, t in files],
        "listing": canonical.group_listing(digests),
        "sha256": canonical.group_hash(digests),
    }
    return {"float32Format": fmt, "float32Parse": parse, "json": js, "group": group}


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(prog="python -m svmbuild.vectors")
    ap.add_argument("--check", action="store_true", help="fail if the committed file differs")
    args = ap.parse_args(argv)
    data = canonical.dump_file(build())
    if args.check:
        same = OUT.exists() and OUT.read_bytes() == data
        print("up to date" if same else f"{OUT} is out of date")
        return 0 if same else 1
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_bytes(data)
    print(f"wrote {OUT}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
