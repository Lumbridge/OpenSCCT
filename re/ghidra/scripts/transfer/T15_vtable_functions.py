# Creates functions at every vtable slot target of the current program that analysis left without
# one (code only reachable through a vtable), following incremental-link jump stubs ('jmp rel32')
# to their destination. The vtables come from the T10 export (local/re-out/c/transfer/<id>.features.json),
# so run T10 first and again afterwards. Work copy only (local/ghidra-work/transfer); saves the program.
# @category SVM.transfer
# @runtime PyGhidra

import json
import os
import struct
import sys

sys.path.insert(0, os.path.join(os.path.dirname(str(getSourceFile().getAbsolutePath())), '..', '..', 'lib'))
import svm_ghidra as svm  # noqa: E402

ctx = svm.setup(globals())
program = ctx.program
path = os.path.join(svm.REPO_DIR, 'local', 're-out', 'c', 'transfer', '%s.features.json' % ctx.id)
with open(path, encoding='utf-8') as fh:
    feats = json.load(fh)
lo, image, blocks = svm.loaded_image(program)
code = [(b, e) for name, b, e, x in blocks if x]


def in_code(v):
    return any(b <= v < e for b, e in code)


def follow(a):
    for _ in range(8):
        o = a - lo
        if 0 <= o < len(image) - 5 and image[o] == 0xE9:
            a = (a + 5 + struct.unpack_from('<i', image, o + 1)[0]) & 0xffffffff
        else:
            break
    return a


fm = program.getFunctionManager()
targets = set()
for vt in feats['vtables']:
    for s in vt['slots']:
        t = follow(s)
        if in_code(t):
            targets.add(t)
counts = {'targets': len(targets), 'existing': 0, 'created': 0, 'split': 0, 'failed': 0}
failed = []
for t in sorted(targets):
    a = ctx.addr(t)
    if fm.getFunctionAt(a) is not None:
        counts['existing'] += 1
        continue
    f, how = svm.ensure_function(program, monitor, a)
    if f is None:
        counts['failed'] += 1
        failed.append('%08x %s' % (t, how))
    elif how == 'split':
        counts['split'] += 1
    else:
        counts['created'] += 1
ctx.write_report('transfer-vtable-functions.json', {'counts': counts, 'failed': failed[:200]})
println('transfer: %s vtable targets %s' % (ctx.id, counts))
