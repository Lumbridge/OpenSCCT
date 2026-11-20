"""Resolve x86 COFF external references in one complete C function at its retail VA."""

from collections import Counter, defaultdict
from functools import lru_cache
import struct


def region(data, offset, size):
    if offset < 0 or size < 0 or offset + size > len(data):
        raise ValueError("COFF range outside object")
    return data[offset:offset + size]


@lru_cache(maxsize=1)
def symbol_index(obj):
    """Parse the current translation unit once even when hundreds of its functions match."""
    machine, sections, _, table, entries, optional, _ = struct.unpack_from("<HHIIIHH", obj)
    if machine != 0x14c or optional or not entries:
        raise ValueError("expected x86 COFF with a symbol table")
    region(obj, 20, sections * 40)
    region(obj, table, entries * 18)
    strings = table + entries * 18
    string_size, = struct.unpack_from("<I", obj, strings)
    names = region(obj, strings, string_size)
    symbols = {}
    functions = defaultdict(list)
    section_functions = Counter()
    index = 0
    while index < entries:
        short, value, section, kind, storage, aux = struct.unpack_from("<8sIhHBB", obj, table + index * 18)
        if short[:4] == b"\0" * 4:
            start = int.from_bytes(short[4:], "little")
            end = names.find(b"\0", start)
            if not 4 <= start <= end:
                raise ValueError("invalid COFF symbol name")
            short = names[start:end]
        name = short.rstrip(b"\0")
        symbols[index] = (name, value, section, kind, storage)
        if kind == 0x20 and 1 <= section <= sections:
            section_functions[section] += 1
            if value == 0 and storage in (2, 3):
                functions[name].append(section)
        index += 1 + aux
    if index != entries:
        raise ValueError("invalid COFF auxiliary symbol count")
    return sections, symbols, functions, section_functions


def link_function(obj, name, length, address, bindings, *, read_constant=None):
    """Return the fully linked section. The retail function bytes are never an input."""
    sections, symbols, functions, section_functions = symbol_index(bytes(obj))
    matches = functions.get(name.encode("ascii"), ())
    if len(matches) != 1:
        raise ValueError(f"expected one complete COFF function named {name}")
    section = matches[0]
    if section_functions[section] != 1:
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
        local = (storage == 3 and kind == 0 and sym_section == section
                 and symbol.startswith(b"$L") and symbol[2:].isdigit())
        private = storage == 3 and not local
        if local:
            if relocation != 6 or value >= size:
                raise ValueError("invalid section-local switch label")
            dest = address + value
        else:
            if private:
                # MSVC may choose a register ABI for a static procedure within this translation unit.
                if (relocation != 0x14 or sym_section == section or functions.get(symbol) != [sym_section]
                        or section_functions[sym_section] != 1 or kind != 0x20 or value != 0):
                    raise ValueError("expected a relative call or jump to one defined private function")
                dest_header = 20 + (sym_section - 1) * 40
                dest_size, dest_raw = struct.unpack_from("<II", obj, dest_header + 16)
                dest_flags, = struct.unpack_from("<I", obj, dest_header + 36)
                if not dest_size or dest_flags & 0x20000020 != 0x20000020:
                    raise ValueError("private function must start an executable contribution")
                region(obj, dest_raw, dest_size)
            elif value != 0 or storage != 2 or not 0 <= sym_section <= sections:
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
            used.add(symbol)
        field = set(range(position, position + 4))
        if position + 4 > size or fields & field:
            raise ValueError("invalid or overlapping relocation fields")
        fields.update(field)
        addend, = struct.unpack_from("<I", body, position)
        if local and addend:
            raise ValueError("switch label relocation has a nonzero addend")
        if private and addend:
            raise ValueError("private function relocation has a nonzero addend")
        if relocation == 0x14:  # IMAGE_REL_I386_REL32
            dest -= address + position + 4
        elif relocation != 6:  # IMAGE_REL_I386_DIR32
            raise ValueError(f"unsupported i386 relocation {relocation:#x}")
        struct.pack_into("<I", body, position, (addend + dest) & 0xffffffff)
    if used != set(bindings):
        raise ValueError("binding table includes an unused symbol")
    return bytes(body)
