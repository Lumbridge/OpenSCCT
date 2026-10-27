"""Magma localisation files (System/_PC_/Magma.int, MagmaDynamic.int) to JSON.

Both are INI text: `[Section]` headers and `Key=Value` lines. In Magma.int a section is an area name and a key a
text widget in it, whose STRING the game replaces with the value (for example [A_ChronoHack] Text_Hacking=Hacking).
MagmaDynamic.int holds strings the native page code looks up by section and key ([HUD] TextChat1=Speak).
Engine.int and GUI.int quote most values (IPBanned="Your IP address..."); the quotes are not part of the string and
are removed, as the engine's localisation reader does.
Files are read as UTF-16 when they start with a byte order mark, else as Windows-1252.
"""

from __future__ import annotations


def decode_int(data: bytes) -> str:
    if data.startswith(b"\xff\xfe") or data.startswith(b"\xfe\xff"):
        return data.decode("utf-16")
    if data.startswith(b"\xef\xbb\xbf"):
        return data[3:].decode("utf-8")
    return data.decode("cp1252", errors="replace")


def parse_int(data: bytes | str) -> dict[str, dict[str, str]]:
    """Sections in file order; a repeated key keeps its last value, a repeated section merges."""
    text = decode_int(data) if isinstance(data, (bytes, bytearray)) else data
    out: dict[str, dict[str, str]] = {}
    section: dict[str, str] | None = None
    for raw in text.splitlines():
        line = raw.strip()
        if not line or line.startswith(";"):
            continue
        if line.startswith("[") and line.endswith("]"):
            section = out.setdefault(line[1:-1].strip(), {})
            continue
        if section is None or "=" not in line:
            continue
        key, value = line.split("=", 1)
        section[key.strip()] = unquote(value)
    return out


def unquote(value: str) -> str:
    """A value without the double quotes around it (only a fully quoted value loses them)."""
    stripped = value.strip()
    if len(stripped) >= 2 and stripped[0] == '"' and stripped[-1] == '"':
        return stripped[1:-1]
    return value
