"""Identify an x86 PE32 or little-endian MIPS ELF32 without running the image."""

import hashlib
import struct


def span(offset, size, limit):
    if type(offset) is not int or type(size) is not int or offset < 0 or size <= 0 or offset + size > limit:
        raise ValueError("invalid or out-of-bounds file span")
    return offset, offset + size


def section(name, offset, size, address, code, data):
    span(offset, size, len(data))
    return dict(name=name, offset=offset, size=size, address=address, code=code)


def pe_sections(data):
    header, = struct.unpack_from("<I", data, 60)
    if data[header:header + 4] != b"PE\0\0":
        raise ValueError("invalid PE signature")
    machine, count = struct.unpack_from("<HH", data, header + 4)
    optional_size, = struct.unpack_from("<H", data, header + 20)
    optional = header + 24
    if machine != 0x14C or struct.unpack_from("<H", data, optional)[0] != 0x10B or optional_size < 32:
        raise ValueError("expected an x86 PE32 image")
    base, = struct.unpack_from("<I", data, optional + 28)
    for i in range(count):
        at = optional + optional_size + i * 40
        name, virtual_size, rva, raw_size, offset = struct.unpack_from("<8s4I", data, at)
        flags, = struct.unpack_from("<I", data, at + 36)
        size = min(raw_size, virtual_size or raw_size)
        if flags & 0x20000000 and size:
            code = bool(flags & 0x20) and not (flags & 0x80000000)
            yield section(name.strip(b"\0").decode("ascii"), offset, size, base + rva, code, data)


def elf_sections(data):
    if data[:7] != b"\x7fELF\x01\x01\x01" or struct.unpack_from("<HH", data, 16) != (2, 8):
        raise ValueError("expected a little-endian MIPS ELF32 executable")
    table, = struct.unpack_from("<I", data, 32)
    stride, count = struct.unpack_from("<HH", data, 46)
    if stride < 40 or not count:
        raise ValueError("ELF has no supported section table")
    span(table, stride * count, len(data))
    for i in range(count):
        _, kind, flags, va, offset, size = struct.unpack_from("<6I", data, table + i * stride)
        if kind == 1 and flags & 6 == 6 and size:
            yield section(f"section-{i}", offset, size, va, not (flags & 1), data)


def inspect(data):
    if data.startswith(b"MZ"):
        file_format, sections = "pe32-x86", list(pe_sections(data))
    else:
        file_format, sections = "elf32-mipsel", list(elf_sections(data))
    end = 0
    for item in sorted(sections, key=lambda s: s["offset"]):
        if item["offset"] < end:
            raise ValueError("overlapping executable sections")
        end = item["offset"] + item["size"]
    if not sections:
        raise ValueError("no file-backed executable sections found")
    return dict(sha256=hashlib.sha256(data).hexdigest(), size=len(data), format=file_format,
                executable_sections=sections)


def coff_symbol_offset(data, symbol, size):
    """Find a complete, relocation-free function in a one-function COFF section."""
    machine, count, _, table, symbols, opt, _ = struct.unpack_from("<HHIIIHH", data, 0)
    if machine != 0x14C or opt or not symbols:
        raise ValueError("expected an x86 COFF object with a symbol table")
    _, strings = span(table, symbols * 18, len(data))
    string_size, = struct.unpack_from("<I", data, strings)
    _, strings_end = span(strings, string_size, len(data))
    entry = 0
    while entry < symbols:
        at = table + 18 * entry
        name, value, index, kind, storage, extra = struct.unpack_from("<8sIhHBB", data, at)
        entry += 1 + extra
        if name.startswith(b"\0" * 4):
            offset, = struct.unpack_from("<I", name, 4)
            if not 4 <= offset < string_size:
                raise ValueError("invalid COFF string offset")
            name = data[strings + offset:strings_end].split(b"\0", 1)[0]
        if name.rstrip(b"\0") != symbol.encode("ascii") or kind != 0x20 or storage != 2:
            continue
        if not 1 <= index <= count:
            raise ValueError("invalid COFF symbol section")
        header = 20 + 40 * (index - 1)
        length, start = struct.unpack_from("<II", data, header + 16)
        relocs, = struct.unpack_from("<H", data, header + 32)
        flags, = struct.unpack_from("<I", data, header + 36)
        if value or length != size or relocs or flags & 0x20000020 != 0x20000020:
            raise ValueError("object match requires a single complete function with no relocations")
        span(start, size, len(data))
        return start
    raise ValueError(f"function symbol missing from object: {symbol}")
