"""Canonical JSON, the float32 text form and SVMap group hashes.

The rules are in spec/svmap/1.0/conventions.md. The C# reader implements the same rules, so any change here is a
format change.
"""

from __future__ import annotations

import hashlib
import json
import math
import struct
from fractions import Fraction
from pathlib import Path
from typing import Any, Iterable

import numpy as np

GROUPS = ("sim", "render", "audio", "ui")
_F32_MAX = struct.unpack("<f", b"\xff\xff\x7f\x7f")[0]
# Values at or beyond max + half an ulp round to infinity.
_F32_OVERFLOW = Fraction(2) ** 128 - Fraction(2) ** 103


def f32_bits(x: float) -> int:
    return struct.unpack("<I", struct.pack("<f", x))[0]


def to_f32(x: float) -> float:
    """Round a Python float (binary64) to the nearest binary32, returned as a Python float."""
    if not math.isfinite(x):
        raise ValueError(f"non-finite float {x!r}")
    if abs(x) > _F32_MAX and Fraction(abs(x)) >= _F32_OVERFLOW:
        raise ValueError(f"float {x!r} overflows binary32")
    return float(np.float32(x))


def parse_f32(token: str) -> float:
    """Correctly rounded decimal to binary32 (round-to-nearest-even), without a double rounding step."""
    q = Fraction(token)
    negative = token.lstrip().startswith("-")
    if q == 0:
        return -0.0 if negative else 0.0
    if abs(q) >= _F32_OVERFLOW:
        raise ValueError(f"float {token!r} overflows binary32")
    try:
        d = float(q)
    except OverflowError as e:  # pragma: no cover - guarded by the overflow check
        raise ValueError(f"float {token!r} overflows binary32") from e
    c = np.float32(d)
    if not np.isfinite(c):
        c = np.float32(math.copysign(_F32_MAX, d))
    with np.errstate(over="ignore"):
        up = np.nextafter(c, np.float32(np.inf))
        down = np.nextafter(c, np.float32(-np.inf))
    candidates = {float(c), float(up), float(down)}
    best = None
    for x in candidates:
        if not math.isfinite(x):
            continue
        key = (abs(Fraction(x) - q), f32_bits(x) & 1)
        if best is None or key < best[0]:
            best = (key, x)
    assert best is not None
    x = best[1]
    if x == 0.0 and q < 0:
        return -0.0
    return x


def format_f32(x: float) -> str:
    """The float32 text form: shortest round-trip digits, positional, at least one digit each side of the point."""
    v = to_f32(x)
    text = np.format_float_positional(np.float32(v), unique=True, trim="0")
    back = parse_f32(text)
    if f32_bits(back) != f32_bits(v):  # pragma: no cover - self check of the formatter
        raise AssertionError(f"float32 text {text!r} does not round trip {v!r}")
    return text


_ESCAPES = {'"': '\\"', "\\": "\\\\", "\b": "\\b", "\t": "\\t", "\n": "\\n", "\f": "\\f", "\r": "\\r"}


def _escape(s: str) -> str:
    out = ['"']
    for ch in s:
        e = _ESCAPES.get(ch)
        if e is not None:
            out.append(e)
        elif ord(ch) < 0x20:
            out.append("\\u%04x" % ord(ch))
        else:
            out.append(ch)
    out.append('"')
    return "".join(out)


def _key(k: Any, ascii_keys: bool = True) -> str:
    if not isinstance(k, str):
        raise TypeError(f"object key must be a string, got {type(k).__name__}")
    if ascii_keys and not k.isascii():
        raise ValueError(f"object key {k!r} is not ASCII")
    return k


def _sorted_items(v: dict, ascii_keys: bool) -> list:
    """Object members sorted by the bytes of their keys (ASCII keys: the canonical order)."""
    return sorted(((_key(k, ascii_keys), x) for k, x in v.items()), key=lambda kv: kv[0].encode("utf-8"))


def _scalar(v: Any) -> str:
    if v is None:
        return "null"
    if v is True:
        return "true"
    if v is False:
        return "false"
    if isinstance(v, int):
        return str(int(v))
    if isinstance(v, float):
        return format_f32(v)
    if isinstance(v, np.floating):
        return format_f32(float(v))
    if isinstance(v, np.integer):
        return str(int(v))
    if isinstance(v, str):
        return _escape(v)
    raise TypeError(f"cannot serialise {type(v).__name__}")


_INLINE_WIDTH = 100


def _flat(v: Any) -> bool:
    if isinstance(v, dict):
        return all(not isinstance(x, (dict, list, tuple)) for x in v.values())
    if isinstance(v, (list, tuple)):
        return all(not isinstance(x, (dict, list, tuple)) for x in v)
    return False


def _inline(v: Any, ascii_keys: bool = True) -> str:
    """Pretty form of a container of scalars on one line."""
    if isinstance(v, dict):
        items = _sorted_items(v, ascii_keys)
        return "{" + ", ".join(f"{_escape(k)}: {_scalar(x)}" for k, x in items) + "}"
    return "[" + ", ".join(_scalar(x) for x in v) + "]"


def dumps(obj: Any, indent: int | None = None, ascii_keys: bool = True) -> str:
    """Serialise with sorted keys and float32 floats. indent=None gives the canonical compact form.

    With an indent, containers holding only scalars stay on one line when short (layout never affects hashes).
    ascii_keys=False lets client-only files (render/, ui/) use non-ASCII object keys, sorted by their UTF-8 bytes;
    sim/ JSON keeps ASCII keys (conventions.md).
    """
    parts: list[str] = []

    def emit(v: Any, level: int) -> None:
        if indent is not None and v and _flat(v):
            text = _inline(v, ascii_keys)
            if len(text) + indent * level <= _INLINE_WIDTH:
                parts.append(text)
                return
        if isinstance(v, dict):
            if not v:
                parts.append("{}")
                return
            items = _sorted_items(v, ascii_keys)
            if indent is None:
                parts.append("{")
                for i, (k, x) in enumerate(items):
                    if i:
                        parts.append(",")
                    parts.append(_escape(k))
                    parts.append(":")
                    emit(x, level + 1)
                parts.append("}")
            else:
                pad = "\n" + " " * (indent * (level + 1))
                parts.append("{")
                for i, (k, x) in enumerate(items):
                    parts.append("," + pad if i else pad)
                    parts.append(_escape(k))
                    parts.append(": ")
                    emit(x, level + 1)
                parts.append("\n" + " " * (indent * level) + "}")
        elif isinstance(v, (list, tuple)):
            if not v:
                parts.append("[]")
                return
            if indent is None:
                parts.append("[")
                for i, x in enumerate(v):
                    if i:
                        parts.append(",")
                    emit(x, level + 1)
                parts.append("]")
            else:
                pad = "\n" + " " * (indent * (level + 1))
                parts.append("[")
                for i, x in enumerate(v):
                    parts.append("," + pad if i else pad)
                    emit(x, level + 1)
                parts.append("\n" + " " * (indent * level) + "]")
        else:
            parts.append(_scalar(v))

    emit(obj, 0)
    return "".join(parts)


def dump_file(obj: Any, ascii_keys: bool = True) -> bytes:
    """On-disk form of SVMap JSON: sorted keys, one-space indent, trailing newline."""
    return (dumps(obj, indent=1, ascii_keys=ascii_keys) + "\n").encode("utf-8")


def _reject_constant(name: str) -> Any:
    raise ValueError(f"JSON constant {name} is not allowed")


def _no_duplicates(pairs: list[tuple[str, Any]]) -> dict:
    out: dict = {}
    for k, v in pairs:
        if k in out:
            raise ValueError(f"duplicate key {k!r}")
        out[k] = v
    return out


def loads(data: bytes | str) -> Any:
    """Parse SVMap JSON strictly: UTF-8, no BOM, no duplicate keys, float tokens rounded to binary32."""
    text = data.decode("utf-8") if isinstance(data, (bytes, bytearray)) else data
    if text.startswith("﻿"):
        raise ValueError("byte order mark is not allowed")
    return json.loads(text, parse_float=parse_f32, parse_constant=_reject_constant, object_pairs_hook=_no_duplicates)


def canonical_bytes(data: bytes | str) -> bytes:
    return dumps(loads(data)).encode("utf-8")


def sha256_hex(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def file_digest(rel_path: str, data: bytes) -> str:
    """Digest of one map file: canonical bytes for sim/*.json, raw bytes otherwise."""
    if rel_path.startswith("sim/") and rel_path.endswith(".json"):
        return sha256_hex(canonical_bytes(data))
    return sha256_hex(data)


def group_listing(digests: Iterable[tuple[str, str]]) -> str:
    rows = sorted(digests, key=lambda r: r[0].encode("utf-8"))
    return "".join(f"{digest}  {path}\n" for path, digest in rows)


def group_hash(digests: Iterable[tuple[str, str]]) -> str:
    """digests: (relative path, file digest) pairs of one group."""
    return sha256_hex(group_listing(digests).encode("utf-8"))


def map_files(root: Path) -> list[str]:
    """Relative paths of every file of an SVMap folder except manifest.json, / separators, sorted."""
    out = []
    for p in root.rglob("*"):
        if p.is_file():
            rel = p.relative_to(root).as_posix()
            if rel != "manifest.json":
                out.append(rel)
    return sorted(out, key=lambda r: r.encode("utf-8"))


def hash_map(root: Path) -> tuple[dict[str, dict], dict[str, str]]:
    """(files, hashes) for a manifest: per-file digest and size, and the hash of every non-empty group."""
    files: dict[str, dict] = {}
    by_group: dict[str, list[tuple[str, str]]] = {}
    for rel in map_files(root):
        group = rel.split("/", 1)[0]
        if group not in GROUPS or "/" not in rel:
            raise ValueError(f"{rel}: files must live in one of {', '.join(GROUPS)}")
        data = (root / rel).read_bytes()
        digest = file_digest(rel, data)
        files[rel] = {"sha256": digest, "size": len(data)}
        by_group.setdefault(group, []).append((rel, digest))
    hashes = {g: group_hash(rows) for g, rows in sorted(by_group.items())}
    return files, hashes
