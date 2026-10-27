"""Disassembles jump-table targets that analysis left undefined and refreshes function bodies
(private project copy). TARGETS: function addresses; tables are read until a non-code entry."""

from ghidra.app.cmd.disassemble import DisassembleCommand
from ghidra.app.cmd.function import CreateFunctionCmd
import re

listing = p.getListing()  # noqa: F821
mem = p.getMemory()  # noqa: F821
fm = p.getFunctionManager()  # noqa: F821
tid = p.startTransaction('svm switches')  # noqa: F821
total = 0
try:
    for t in TARGETS:  # noqa: F821
        f = find_func(p, t)  # noqa: F821
        for rnd in range(4):
            new = 0
            for ins in list(listing.getInstructions(f.getBody(), True)):
                s = str(ins)
                m = re.match(r'JMP dword ptr \[(\w+)\*0x4 \+ (0x[0-9a-f]+)\]', s)
                if not m:
                    continue
                base = int(m.group(2), 16)
                for k in range(64):
                    v = mem.getInt(addr(p, base + 4 * k)) & 0xffffffff  # noqa: F821
                    blk = mem.getBlock(addr(p, v))  # noqa: F821
                    if blk is None or not blk.isExecute() or not (f.getEntryPoint().getOffset() - 0x10000 < v < f.getEntryPoint().getOffset() + 0x10000):
                        break
                    if listing.getInstructionAt(addr(p, v)) is None:  # noqa: F821
                        DisassembleCommand(addr(p, v), None, True).applyTo(p, monitor)  # noqa: F821
                        new += 1
            CreateFunctionCmd.fixupFunctionBody(p, f, monitor)  # noqa: F821
            total += new
            if not new:
                break
finally:
    p.endTransaction(tid, True)  # noqa: F821
print('disassembled', total)
