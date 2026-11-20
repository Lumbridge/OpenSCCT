"""Disassemble local EE code and compare an independently compiled function."""

import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parent / "tools"))
from images import inspect


def file_offset(image, address, size):
    if address < 0 or address % 4 or size <= 0 or size % 4:
        raise ValueError("use a positive instruction-sized range at an aligned EE address")
    for section in image["executable_sections"]:
        relative = address - section["address"]
        if 0 <= relative and relative + size <= section["size"]:
            return section["offset"] + relative
    raise ValueError("range is outside one file-backed executable section")


def disassemble(folder, symbol, address, original):
    (folder / "reference.bin").write_bytes(original)
    (folder / "symbols.txt").write_text(
        f"{symbol} = 0x{address:08X}; // type:func size:0x{len(original):X}\n", encoding="utf-8")
    config = {
        "name": symbol, "sha1": hashlib.sha1(original).hexdigest(),
        "options": {"basename": "function", "platform": "ps2", "base_path": ".",
                    "target_path": "reference.bin", "symbol_addrs_path": "symbols.txt"},
        "segments": [{"type": "code", "name": "function", "start": 0, "vram": address,
                      "subsegments": [[0, "asm", "function"]]}, [len(original)]],
    }
    (folder / "splat.yaml").write_text(json.dumps(config, indent=2), encoding="utf-8")
    subprocess.run([sys.executable, "-m", "splat", "split", "splat.yaml"], cwd=folder, check=True)


def compiled_function(path, name):
    from elftools.elf.elffile import ELFFile
    from elftools.elf.relocation import RelocationSection

    with path.open("rb") as stream:
        elf = ELFFile(stream)
        if not (elf.little_endian and elf.elfclass == 32 and elf["e_machine"] == "EM_MIPS"
                and elf["e_type"] == "ET_REL"):
            raise ValueError("expected a little-endian MIPS ELF32 object")
        table = elf.get_section_by_name(".symtab")
        symbols = table.get_symbol_by_name(name) if table else None
        if not symbols or len(symbols) != 1 or symbols[0]["st_info"]["type"] != "STT_FUNC":
            raise ValueError("expected one defined function with the requested symbol")
        symbol = symbols[0]
        index = symbol["st_shndx"]
        if not isinstance(index, int) or index <= 0:
            raise ValueError("symbol has no file-backed section")
        section = elf.get_section(index)
        start, size = symbol["st_value"], symbol["st_size"]
        if (section["sh_type"] != "SHT_PROGBITS" or not section["sh_flags"] & 4 or
                size <= 0 or start + size > section["sh_size"]):
            raise ValueError("invalid function bounds or section")
        for relocs in elf.iter_sections():
            if isinstance(relocs, RelocationSection) and relocs["sh_info"] == index and relocs.num_relocations():
                raise ValueError("code section has pending relocations; compare a final linked artifact")
        return section.data()[start:start + size]


def verify(folder, unit, original):
    symbol = unit["symbol"]
    source = ROOT / unit["inputs"][0]
    cc = ["mipsel-linux-gnu-gcc", *unit["cflags"]]
    checked = folder / "candidate.bin"
    checked.unlink(missing_ok=True)
    subprocess.run([*cc, "-c", str(source), "-o", "candidate.o"], cwd=folder, check=True)
    body = compiled_function(folder / "candidate.o", symbol)

    # A separate target object gives objdiff a named reference symbol, including its delay slot.
    (folder / "reference.bin").write_bytes(original)
    asm = ('.text\n' + f'.globl {symbol}\n.type {symbol}, @function\n{symbol}:\n'
           + f'.incbin "reference.bin"\n.size {symbol}, . - {symbol}\n')
    (folder / "reference.s").write_text(asm, encoding="utf-8")
    subprocess.run([*cc, "-c", "reference.s", "-o", "reference.o"], cwd=folder, check=True)
    subprocess.run(["objdiff-cli", "diff", "-1", "reference.o", "-2", "candidate.o",
                    "-o", "objdiff.json", symbol], cwd=folder, check=True)
    if body != original:
        raise ValueError(f"{symbol} differs from retail; inspect {folder / 'objdiff.json'}")
    checked.write_bytes(body)
    print(f"{symbol}: all {len(body)} compiled bytes agree. Object diff: {folder / 'objdiff.json'}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["disassemble", "verify"])
    parser.add_argument("target", choices=sorted(p.stem for p in (ROOT / "config").glob("*.json")))
    parser.add_argument("elf", type=Path)
    selection = parser.add_mutually_exclusive_group(required=True)
    selection.add_argument("--unit", help="function ID in the build manifest")
    selection.add_argument("--range", nargs=2, type=lambda s: int(s, 0), metavar=("EE_ADDRESS", "SIZE"),
                           help="review another instruction range in a locked executable")
    parser.add_argument("--draft", action="store_true", help="run m2c on the local disassembly")
    args = parser.parse_args()
    if args.action == "verify" and (args.range or args.draft):
        parser.error("verify takes a declared --unit; use disassemble for range exploration or m2c")
    target = json.loads((ROOT / "config" / (args.target + ".json")).read_text(encoding="utf-8"))
    data = args.elf.read_bytes()
    image = target["image"]
    if image != inspect(data):
        raise ValueError("original ELF differs from the selected manifest")
    if args.unit:
        unit = next((u for u in target["units"] if u["id"] == args.unit), None)
        if unit is None:
            raise ValueError("unknown unit for this executable")
        address, size, symbol = unit["address"], unit["size"], unit["symbol"]
    else:
        address, size = args.range
        symbol = f"func_{address:08X}"
    if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", symbol):
        raise ValueError("invalid function symbol")
    offset = file_offset(image, address, size)
    if args.unit and offset != unit["offset"]:
        raise ValueError("unit EE address and manifest file offset disagree")
    original = data[offset:offset + size]
    folder = ROOT / "local" / "work" / args.target / symbol
    folder.mkdir(parents=True, exist_ok=True)
    if args.action == "verify":
        verify(folder, unit, original)
        return
    disassemble(folder, symbol, address, original)
    print(f"Assembly: {folder / 'asm' / 'function.s'}")
    if args.draft:
        result = subprocess.run(["m2c", "-t", "mipsee-gcc-c", "asm/function.s"],
                                cwd=folder, capture_output=True, check=True)
        (folder / "m2c-draft.c").write_bytes(result.stdout)
        print(f"Local analysis draft: {folder / 'm2c-draft.c'}")


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        if isinstance(error, subprocess.CalledProcessError) and error.stderr:
            print(error.stderr.decode(errors="replace"), file=sys.stderr)
        sys.exit(str(error))
