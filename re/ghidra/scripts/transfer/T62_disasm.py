# Writes the instruction listing of the given functions, with the values of float and double
# constants they load, to local/re-out/c/transfer/disasm/<program id>/<address>.txt. Local only:
# disassembly is never tracked. Read-only. Argument: addrs=<comma-separated hex addresses>
# @category SVM.transfer
# @runtime PyGhidra

import os
import struct
import sys

sys.path.insert(0, os.path.join(os.path.dirname(str(getSourceFile().getAbsolutePath())), '..', '..', 'lib'))
import svm_ghidra as svm  # noqa: E402

BANNER = '// svm-transfer T62_disasm output: local only, never commit'

ctx = svm.setup(globals())
program = ctx.program
args = dict(a.split('=', 1) for a in getScriptArgs() if '=' in a)
fm = program.getFunctionManager()
mem = program.getMemory()
listing = program.getListing()
out_dir = os.path.join(svm.REPO_DIR, 'local', 're-out', 'c', 'transfer', 'disasm', ctx.id)
os.makedirs(out_dir, exist_ok=True)
for a in args['addrs'].split(','):
    a = a.strip()
    if not a:
        continue
    f = fm.getFunctionContaining(ctx.addr(int(a, 16)))
    if f is None:
        continue
    lines = [BANNER, '// %s at %s' % (f.getName(True), f.getEntryPoint())]
    for ins in listing.getInstructions(f.getBody(), True):
        note = ''
        for r in ins.getReferencesFrom():
            to = r.getToAddress()
            if not to.isMemoryAddress():
                continue
            try:
                raw = bytearray(8)
                for i in range(8):
                    raw[i] = mem.getByte(to.add(i)) & 0xff
                fv = struct.unpack('<f', bytes(raw[:4]))[0]
                dv = struct.unpack('<d', bytes(raw))[0]
                note += '  ; [%s] f=%g d=%g' % (to, fv, dv)
            except Exception:  # noqa: BLE001
                pass
        lines.append('%s  %s%s' % (ins.getAddress(), ins, note))
    with open(os.path.join(out_dir, '%08x.txt' % f.getEntryPoint().getOffset()), 'w', encoding='utf-8', newline='\n') as fh:
        fh.write('\n'.join(lines) + '\n')
println('transfer: disasm -> %s' % out_dir)
