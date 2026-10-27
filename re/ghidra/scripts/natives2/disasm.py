"""Server script (ghq.py --exec): writes the disassembly of functions to local/re-out/c/natives2/<prog>/<addr>.asm.

Input globals: FUNCS, a list of function addresses. Each line: address, instruction, and the float value of any
4-byte constant operand that reads as a plain float, so literals are easy to spot.
"""

import struct

lst = p.getListing()  # noqa: F821
for a in FUNCS:  # noqa: F821
    f = find_func(p, a)  # noqa: F821
    lines = ['; natives2 disassembly (local only) %s %s' % (f.getEntryPoint(), f.getName(True))]
    for ins in lst.getInstructions(f.getBody(), True):
        s = '%s  %s' % (ins.getAddress(), ins)
        for i in range(ins.getNumOperands()):
            for o in ins.getOpObjects(i):
                if hasattr(o, 'getUnsignedValue'):
                    v = o.getUnsignedValue() & 0xffffffff
                    if 0x3c000000 <= v <= 0x48000000 or 0xbc000000 <= v <= 0xc8000000:
                        s += '    ; %r' % struct.unpack('<f', struct.pack('<I', v))[0]
        refs = [r for r in ins.getReferencesFrom() if r.getReferenceType().isData()]
        for r in refs:
            try:
                b = bytes(bytearray([p.getMemory().getByte(r.getToAddress().add(k)) & 0xff for k in range(4)]))  # noqa: F821
                fv = struct.unpack('<f', b)[0]
                if 1e-4 < abs(fv) < 1e7:
                    s += '    ; [%s]=%r' % (r.getToAddress(), fv)
            except Exception:  # noqa: BLE001
                pass
        lines.append(s)
    fn = write_local(key, '%s_%s.asm' % (f.getEntryPoint(), f.getName().replace(':', '_')), '\n'.join(lines) + '\n')  # noqa: F821
    print(fn, len(lines))
