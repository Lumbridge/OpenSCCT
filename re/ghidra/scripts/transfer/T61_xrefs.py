# Lists every reference to the given addresses with its type (read, write, call, data) and the
# function it comes from, into local/re-out/c/transfer/xrefs/<program id>.txt. Read-only.
# Argument: addrs=<comma-separated hex addresses>
# @category SVM.transfer
# @runtime PyGhidra

import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(str(getSourceFile().getAbsolutePath())), '..', '..', 'lib'))
import svm_ghidra as svm  # noqa: E402

ctx = svm.setup(globals())
program = ctx.program
args = dict(a.split('=', 1) for a in getScriptArgs() if '=' in a)
fm = program.getFunctionManager()
rm = program.getReferenceManager()
out_dir = os.path.join(svm.REPO_DIR, 'local', 're-out', 'c', 'transfer', 'xrefs')
os.makedirs(out_dir, exist_ok=True)
lines = []
for a in args['addrs'].split(','):
    a = a.strip()
    if not a:
        continue
    addr = ctx.addr(int(a, 16))
    lines.append('== %s' % a)
    for r in rm.getReferencesTo(addr):
        f = fm.getFunctionContaining(r.getFromAddress())
        lines.append('  %s %-12s %s' % (r.getFromAddress(), r.getReferenceType(), f.getName(True) if f else '-'))
path = os.path.join(out_dir, '%s.txt' % ctx.id)
with open(path, 'w', encoding='utf-8', newline='\n') as fh:
    fh.write('\n'.join(lines) + '\n')
println('transfer: xrefs -> %s' % path)
