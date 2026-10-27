"""Finds game copies of editor functions by constant fingerprints (private study helper).

Fingerprint = the set of float constants a function loads from read-only data (by value) plus the
immediates it pushes or compares that look like trace flags or name indices. Editor guard blocks
add no float constants, so the sets survive the build difference. For each editor function in
TARGETS, prints the best game functions by Jaccard similarity. Globals: TARGETS, TOP.
"""

import struct

ed, gm = progs['editor'], progs['game']  # noqa: F821


def fingerprint(prog, f):
    mem = prog.getMemory()
    out = set()
    for ins in prog.getListing().getInstructions(f.getBody(), True):
        m = ins.getMnemonicString()
        for r in ins.getReferencesFrom():
            if not r.getReferenceType().isData():
                continue
            blk = mem.getBlock(r.getToAddress())
            if blk is None or blk.isExecute() or blk.getName() != '.rdata':
                continue
            if m.startswith('F'):
                try:
                    iv = mem.getInt(r.getToAddress()) & 0xffffffff
                except Exception:  # noqa: BLE001
                    continue
                v = struct.unpack('<f', struct.pack('<I', iv))[0]
                if v == v and abs(v) < 1e9:
                    out.add(('f', round(v, 5)))
        if m in ('PUSH', 'CMP', 'MOV', 'TEST', 'AND', 'OR'):
            for k in range(ins.getNumOperands()):
                sc = ins.getScalar(k)
                if sc is None:
                    continue
                v = sc.getUnsignedValue()
                if 0x80 <= v <= 0x3ff or v in (0x3f800000,):
                    out.add(('i', v))
                elif 0x3c000000 <= v <= 0x47000000:  # float immediates
                    fv = struct.unpack('<f', struct.pack('<I', v))[0]
                    out.add(('f', round(fv, 5)))
    return out


if 'GAME_FP' not in globals():
    fp = {}
    for f in gm.getFunctionManager().getFunctions(True):
        s = fingerprint(gm, f)
        if len(s) >= 2:
            fp[f.getEntryPoint().getOffset()] = s
    globals()['GAME_FP'] = fp
GAME_FP = globals()['GAME_FP']
top = globals().get('TOP', 3)
efm = ed.getFunctionManager()
for t in TARGETS:  # noqa: F821
    f = efm.getFunctionAt(addr(ed, t))  # noqa: F821
    s = fingerprint(ed, f)
    best = []
    for go, gs in GAME_FP.items():
        inter = len(s & gs)
        if not inter:
            continue
        j = inter / float(len(s | gs))
        best.append((j, go, inter, len(gs)))
    best.sort(reverse=True)
    print('%s %-42s n=%-3d %s' % (f.getEntryPoint(), f.getName(True), len(s),
                                  '  '.join('%08x j=%.2f (%d/%d)' % (go, j, i, n) for j, go, i, n in best[:top])))
