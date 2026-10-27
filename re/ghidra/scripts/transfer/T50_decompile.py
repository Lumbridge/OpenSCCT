# Decompiles a list of functions (and optionally their callees, to a depth) of the current program
# into local/re-out/c/transfer/<id>/<address>.c for reading. Local only: decompiled code is never
# tracked. Arguments: list=<file of hex addresses, one per line, '#' comments allowed>
# [depth=<callee depth, default 0>] [out=<subfolder name, default the program id>] [create=1 to create
# missing functions].
# @category SVM.transfer
# @runtime PyGhidra

import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(str(getSourceFile().getAbsolutePath())), '..', '..', 'lib'))
import svm_ghidra as svm  # noqa: E402

from ghidra.app.decompiler import DecompInterface, DecompileOptions  # noqa: E402

BANNER = '// svm-transfer T50_decompile output: local only, never commit'

ctx = svm.setup(globals())
program = ctx.program
args = dict(a.split('=', 1) for a in getScriptArgs() if '=' in a)
out = os.path.join(svm.REPO_DIR, 'local', 're-out', 'c', 'transfer', args.get('out', ctx.id))
os.makedirs(out, exist_ok=True)
depth = int(args.get('depth', '0'))
create = args.get('create') == '1'

addrs = []
with open(args['list'], encoding='utf-8') as fh:
    for line in fh:
        line = line.split('#', 1)[0].strip()
        if line:
            addrs.append(int(line, 16))

fm = program.getFunctionManager()
dec = DecompInterface()
opts = DecompileOptions()
dec.setOptions(opts)
dec.toggleCCode(True)
dec.toggleSyntaxTree(False)
dec.openProgram(program)

done = set()
queue = [(a, 0) for a in addrs]
count = 0
while queue:
    a, d = queue.pop(0)
    if a in done:
        continue
    done.add(a)
    addr = ctx.addr(a)
    f = fm.getFunctionAt(addr)
    if f is None and create:
        f, how = svm.ensure_function(program, monitor, addr)
    if f is None:
        f = fm.getFunctionContaining(addr)
    if f is None:
        println('decompile: no function at %x' % a)
        continue
    res = dec.decompileFunction(f, 120, monitor)
    text = res.getDecompiledFunction().getC() if res is not None and res.getDecompiledFunction() is not None else '/* failed: %s */' % (res.getErrorMessage() if res else '?')
    callees = sorted(set(c.getEntryPoint().getOffset() for c in f.getCalledFunctions(monitor) if not c.isExternal()))
    ext = sorted(set(str(c.getName(True)) for c in f.getCalledFunctions(monitor) if c.isExternal() or c.isThunk()))
    path = os.path.join(out, '%08x.c' % f.getEntryPoint().getOffset())
    with open(path, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write(BANNER + '\n')
        fh.write('// %s at %08x, size %d, depth %d\n' % (f.getName(True), f.getEntryPoint().getOffset(), f.getBody().getNumAddresses(), d))
        fh.write('// callees: %s\n' % ' '.join('%08x' % c for c in callees))
        if ext:
            fh.write('// imports: %s\n' % ' '.join(ext))
        fh.write(text)
    count += 1
    if d < depth:
        for c in callees:
            queue.append((c, d + 1))
dec.dispose()
println('decompile: %d functions -> %s' % (count, out))
