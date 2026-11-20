"""Resolve x86 COFF external references in one complete C function at its retail VA."""

import struct


def region(data, offset, size):
    if offset < 0 or size < 0 or offset + size > len(data):
        raise ValueError("COFF range outside object")
    return data[offset:offset + size]


def link_function(obj, name, length, address, bindings, *, read_constant=None):
    """Return the fully linked section. The retail function bytes are never an input."""
    machine, sections, _, table, entries, optional, _ = struct.unpack_from("<HHIIIHH", obj)
    if machine != 0x14c or optional or not entries or not bindings:
        raise ValueError("expected x86 COFF with external symbol bindings")
    region(obj, 20, sections * 40)
    region(obj, table, entries * 18)
    strings = table + entries * 18
    string_size, = struct.unpack_from("<I", obj, strings)
    names = region(obj, strings, string_size)
    symbols = {}
    index = 0
    while index < entries:
        short, value, section, kind, storage, aux = struct.unpack_from("<8sIhHBB", obj, table + index * 18)
        if short[:4] == b"\0" * 4:
            start = int.from_bytes(short[4:], "little")
            end = names.find(b"\0", start)
            if not 4 <= start <= end:
                raise ValueError("invalid COFF symbol name")
            short = names[start:end]
        symbols[index] = (short.rstrip(b"\0"), value, section, kind, storage)
        index += 1 + aux
    if index != entries:
        raise ValueError("invalid COFF auxiliary symbol count")
    matches = [section for symbol, value, section, kind, storage in symbols.values()
               if symbol == name.encode("ascii") and value == 0 and kind == 0x20 and storage == 2]
    if len(matches) != 1 or not 1 <= matches[0] <= sections:
        raise ValueError(f"expected one complete COFF function named {name}")
    section = matches[0]
    if sum(s == section and k == 0x20 for _, _, s, k, _ in symbols.values()) != 1:
        raise ValueError("COFF section contains multiple functions")
    header = 20 + (section - 1) * 40
    size, start, relocs = struct.unpack_from("<III", obj, header + 16)
    count, _, flags = struct.unpack_from("<HHI", obj, header + 32)
    if size != length or not count or flags & 0x20000020 != 0x20000020 or flags & 0x01000000:
        raise ValueError("expected a complete executable section with standard relocations")
    body = bytearray(region(obj, start, size))
    relocation_data = region(obj, relocs, count * 10)
    used = set()
    fields = set()
    for position, sym_index, relocation in struct.iter_unpack("<IIH", relocation_data):
        symbol, value, sym_section, kind, storage = symbols[sym_index]
        if value != 0 or storage != 2 or not 0 <= sym_section <= sections:
            raise ValueError("expected an external symbol at the start of its contribution")
        symbol = symbol.decode("ascii")
        if symbol not in bindings:
            raise ValueError(f"missing link address for {symbol}")
        binding = bindings[symbol]
        dest = binding["va"]
        if type(dest) is not int or not 0 < dest <= 0xffffffff or not binding["basis"].strip():
            raise ValueError(f"invalid address or missing evidence for {symbol}")
        if sym_section and kind != 0x20:
            const_header = 20 + (sym_section - 1) * 40
            const_size, const_start = struct.unpack_from("<II", obj, const_header + 16)
            const_relocs, _, const_flags = struct.unpack_from("<HHI", obj, const_header + 32)
            if (kind != 0 or not const_size or const_relocs or const_flags & 0xe0000040 != 0x40000040
                    or read_constant is None):
                raise ValueError(f"expected an initialized read-only constant for {symbol}")
            if region(obj, const_start, const_size) != read_constant(dest, const_size):
                raise ValueError(f"compiled constant differs from retail for {symbol}")
        field = set(range(position, position + 4))
        if position + 4 > size or fields & field:
            raise ValueError("invalid or overlapping relocation fields")
        fields.update(field)
        addend, = struct.unpack_from("<I", body, position)
        if relocation == 0x14:  # IMAGE_REL_I386_REL32
            dest -= address + position + 4
        elif relocation != 6:  # IMAGE_REL_I386_DIR32
            raise ValueError(f"unsupported i386 relocation {relocation:#x}")
        struct.pack_into("<I", body, position, (addend + dest) & 0xffffffff)
        used.add(symbol)
    if used != set(bindings):
        raise ValueError("binding table includes an unused symbol")
    return bytes(body)
