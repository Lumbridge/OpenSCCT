"""Local PC matching tools for locked retail images and independently rebuilt x86 COFF units."""

import argparse
import hashlib
import json
import struct
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "tools"))
from images import coff_symbol_offset, inspect, span


def read_image(game, target):
    manifest = json.loads((HERE / "config" / (target + ".json")).read_text(encoding="utf-8"))
    data = (game / manifest["file"]).read_bytes()
    if inspect(data) != manifest["image"]:
        raise ValueError(f"{manifest['file']} differs from the locked Steam build")
    return manifest, data


def rich_records(data, pe):
    at = data.rfind(b"Rich", 0, pe)
    if at < 0:
        return None
    key, = struct.unpack_from("<I", data, at + 4)
    words = [value ^ key for (value,) in struct.iter_unpack("<I", data[:at])]
    start = words.index(int.from_bytes(b"DanS", "little"))
    if words[start + 1:start + 4] != [0, 0, 0] or (len(words) - start - 4) % 2:
        raise ValueError("malformed Rich records")
    return [(value >> 16, value & 0xffff, count) for value, count in
            zip(words[start + 4::2], words[start + 5::2])]


def describe(manifest, data):
    pe, = struct.unpack_from("<I", data, 0x3c)
    major, minor = struct.unpack_from("BB", data, pe + 26)
    print(f"{manifest['id']}  sha256 {manifest['image']['sha256']}\nPE linker version {major}.{minor:02}")
    records = rich_records(data, pe)
    if records is None:
        print("Rich marker absent before the PE header")
    else:
        for product, build, count in records:
            print(f"Rich product 0x{product:04x}, build {build}, count {count}")
    for section in manifest["image"]["executable_sections"]:
        if section["code"]:
            print(f"{section['name']} VA {section['address']:#x} file {section['offset']:#x} size {section['size']:#x}")


def code_at(manifest, data, address, length):
    for section in manifest["image"]["executable_sections"]:
        relative = address - section["address"]
        if section["code"] and 0 <= relative and relative + length <= section["size"] and length > 0:
            start = section["offset"] + relative
            return data[start:start + length]
    raise ValueError("the requested range is outside a single locked code section")


def disassemble(manifest, data, address, length):
    from capstone import CS_ARCH_X86, CS_MODE_32, Cs
    body = code_at(manifest, data, address, length)
    print(f"{manifest['id']} VA {address:#x}, {len(body)} bytes, SHA-256 {hashlib.sha256(body).hexdigest()}")
    decoded = 0
    for ins in Cs(CS_ARCH_X86, CS_MODE_32).disasm(body, address):
        print(f"{ins.address:08x}  {ins.mnemonic:8} {ins.op_str}")
        decoded += ins.size
    if decoded != length:
        raise ValueError(f"decoded {decoded} of {length} bytes; check the instruction boundaries")


def retail_coff(symbol, body):
    """A single .text section and an external function symbol for objdiff. No candidate bytes are used."""
    string = symbol.encode("ascii") + b"\0"
    table = 20 + 40 + len(body)
    header = struct.pack("<HHIIIHH", 0x14c, 1, 0, table, 1, 0, 0)
    section = struct.pack("<8sIIIIIIHHI", b".text", 0, 0, len(body), 60, 0, 0, 0, 0, 0x60500020)
    entry = struct.pack("<IIIhHBB", 0, 4, 0, 1, 0x20, 2, 0)
    return header + section + body + entry + struct.pack("<I", len(string) + 4) + string


def compare(manifest, data, unit_id, rebuilt, objdiff):
    unit = next((entry for entry in manifest["units"] if entry["id"] == unit_id), None)
    if unit is None or "rebuilt_symbol" not in unit:
        raise ValueError("select a registered x86 COFF unit from this target's manifest")
    symbol, size = unit["rebuilt_symbol"], unit["size"]
    start, end = span(unit["offset"], size, len(data))
    section = next((s for s in manifest["image"]["executable_sections"] if s["code"] and
                    s["offset"] <= start < end <= s["offset"] + s["size"]), None)
    if section is None:
        raise ValueError("registered unit lies outside a single locked code section")
    body = code_at(manifest, data, section["address"] + start - section["offset"], size)
    candidate = rebuilt.read_bytes()
    out = HERE / "local" / manifest["id"] / unit_id
    out.mkdir(parents=True, exist_ok=True)
    target, report = out / "retail.obj", out / "objdiff.json"
    target.write_bytes(retail_coff(symbol, body))
    subprocess.run([objdiff, "diff", "-1", str(target), "-2", str(rebuilt), "-o", str(report), "--format", "json"], check=True)
    print(f"objdiff result: {report}")
    candidate_at = coff_symbol_offset(candidate, symbol, size)
    if candidate[candidate_at:candidate_at + size] != body:
        raise ValueError("rebuilt instructions differ from the retail function")
    print(f"Exact {size}-byte comparison for {symbol}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("target", choices=sorted(f.stem for f in (HERE / "config").glob("*.json")))
    parser.add_argument("steam_folder", type=Path)
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("inspect", help="verify the image, print linker metadata and Rich tool records")
    dis = sub.add_parser("disasm", help="disassemble a local range by preferred virtual address (requires Capstone)")
    dis.add_argument("address", type=lambda value: int(value, 0))
    dis.add_argument("size", type=lambda value: int(value, 0))
    diff = sub.add_parser("diff", help="compare an entire registered unit to your compiler's COFF object")
    diff.add_argument("unit")
    diff.add_argument("rebuilt", type=Path)
    diff.add_argument("--objdiff", default="objdiff-cli", help="objdiff-cli executable")
    args = parser.parse_args()
    manifest, data = read_image(args.steam_folder, args.target)
    if args.command == "inspect":
        describe(manifest, data)
    elif args.command == "disasm":
        disassemble(manifest, data, args.address, args.size)
    else:
        compare(manifest, data, args.unit, args.rebuilt, args.objdiff)


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, subprocess.CalledProcessError) as exc:
        sys.exit(str(exc))
