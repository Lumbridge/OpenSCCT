# Decompiles, per topic, every function that references one of the topic's strings (8-bit or
# UTF-16, case-insensitive substring) or calls one of its imports, plus their direct callees, into
# local/re-out/c/transfer/topics/<program id>/<topic>/<address>.c for reading. Local only: the output
# is decompiled code and is never tracked. Argument: topics=<JSON file {topic: {"strings": [...],
# "imports": [...], "addresses": [...], "data": [...], "up": <caller levels>}}> [depth=<callee depth, default 1>] [max=<functions per topic, default 120>].
# @category SVM.transfer
# @runtime PyGhidra

import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(str(getSourceFile().getAbsolutePath())), '..', '..', 'lib'))
import svm_ghidra as svm  # noqa: E402

from ghidra.app.decompiler import DecompInterface, DecompileOptions  # noqa: E402

BANNER = '// svm-transfer T60_string_topics output: local only, never commit'

ctx = svm.setup(globals())
program = ctx.program
args = dict(a.split('=', 1) for a in getScriptArgs() if '=' in a)
with open(args['topics'], encoding='utf-8') as fh:
    topics = json.load(fh)
depth = int(args.get('depth', '1'))
cap = int(args.get('max', '120'))
root = os.path.join(svm.REPO_DIR, 'local', 're-out', 'c', 'transfer', 'topics', ctx.id)
lo, image, blocks = svm.loaded_image(program)
fm = program.getFunctionManager()
rm = program.getReferenceManager()
st = program.getSymbolTable()
low = image.lower()


def find_all(needle):
    out = []
    i = low.find(needle)
    while i >= 0:
        out.append(lo + i)
        i = low.find(needle, i + 1)
    return out


def string_starts(text):
    """Addresses of the strings that contain text: walk back from each hit to the string start."""
    hits = set()
    t8 = text.lower().encode('latin-1')
    for a in find_all(t8):
        o = a - lo
        while o > 0 and 0x20 <= image[o - 1] < 0x7f:
            o -= 1
        hits.add(lo + o)
    t16 = text.lower().encode('utf-16-le')
    for a in find_all(t16):
        o = a - lo
        while o > 1 and image[o - 1] == 0 and 0x20 <= image[o - 2] < 0x7f:
            o -= 2
        hits.add(lo + o)
    return hits


def referencing_functions(addr_int):
    out = set()
    a = ctx.addr(addr_int)
    for r in rm.getReferencesTo(a):
        f = fm.getFunctionContaining(r.getFromAddress())
        if f is not None:
            out.add(f)
    return out


def import_callers(name):
    out = set()
    for s in st.getExternalSymbols():
        if str(s.getName()).lower() == name.lower():
            for r in rm.getReferencesTo(s.getAddress()):
                f = fm.getFunctionContaining(r.getFromAddress())
                if f is not None:
                    out.add(f)
                # Calls go through the IAT slot or a thunk; follow thunks one level.
                g = fm.getFunctionAt(r.getFromAddress())
                if g is not None and g.isThunk():
                    for r2 in rm.getReferencesTo(g.getEntryPoint()):
                        f2 = fm.getFunctionContaining(r2.getFromAddress())
                        if f2 is not None:
                            out.add(f2)
    return out


dec = DecompInterface()
dec.setOptions(DecompileOptions())
dec.toggleCCode(True)
dec.openProgram(program)
summary = {}
for topic, spec in topics.items():
    out_dir = os.path.join(root, topic)
    os.makedirs(out_dir, exist_ok=True)
    seeds = {}
    for text in spec.get('strings', []):
        for s in string_starts(text):
            for f in referencing_functions(s):
                seeds.setdefault(f.getEntryPoint().getOffset(), (f, set()))[1].add(text)
    for name in spec.get('imports', []):
        for f in import_callers(name):
            seeds.setdefault(f.getEntryPoint().getOffset(), (f, set()))[1].add('import ' + name)
    for a in spec.get('addresses', []):
        f = fm.getFunctionContaining(ctx.addr(int(a, 16)))
        if f is not None:
            seeds.setdefault(f.getEntryPoint().getOffset(), (f, set()))[1].add('address ' + a)
    for a in spec.get('data', []):
        for f in referencing_functions(int(a, 16)):
            seeds.setdefault(f.getEntryPoint().getOffset(), (f, set()))[1].add('data ' + a)
    # Callers of the seeds, up to 'up' levels (finds the code that drives a linked library).
    frontier = [f for _, (f, _) in sorted(seeds.items())]
    for level in range(int(spec.get('up', 0))):
        nxt = []
        for f in frontier:
            for c in f.getCallingFunctions(monitor):
                ea = c.getEntryPoint().getOffset()
                if ea not in seeds:
                    seeds[ea] = (c, {'caller L%d of %08x' % (level + 1, f.getEntryPoint().getOffset())})
                    nxt.append(c)
        frontier = nxt
    queue = [(f, 0, why) for _, (f, why) in sorted(seeds.items())]
    done = set()
    index = []
    while queue and len(done) < cap:
        f, d, why = queue.pop(0)
        ea = f.getEntryPoint().getOffset()
        if ea in done or f.isExternal():
            continue
        done.add(ea)
        res = dec.decompileFunction(f, 90, monitor)
        text = res.getDecompiledFunction().getC() if res and res.getDecompiledFunction() else '/* failed */'
        callees = sorted((c for c in f.getCalledFunctions(monitor) if not c.isExternal()), key=lambda c: c.getEntryPoint().getOffset())
        ext = sorted(set(str(c.getName()) for c in f.getCalledFunctions(monitor) if c.isExternal() or c.isThunk()))
        with open(os.path.join(out_dir, '%08x.c' % ea), 'w', encoding='utf-8', newline='\n') as fh:
            fh.write(BANNER + '\n')
            fh.write('// %s at %08x size %d depth %d matched: %s\n' % (f.getName(True), ea, f.getBody().getNumAddresses(), d,
                                                                     ', '.join(sorted(why))))
            fh.write('// callees: %s\n' % ' '.join('%08x' % c.getEntryPoint().getOffset() for c in callees))
            if ext:
                fh.write('// imports: %s\n' % ' '.join(ext))
            fh.write(text)
        index.append('%08x d%d %5d %s | %s' % (ea, d, f.getBody().getNumAddresses(), f.getName(True), ', '.join(sorted(why))))
        if d < depth:
            for c in callees:
                queue.append((c, d + 1, {'callee of %08x' % ea}))
    with open(os.path.join(out_dir, 'INDEX.txt'), 'w', encoding='utf-8', newline='\n') as fh:
        fh.write(BANNER + '\n' + '\n'.join(index) + '\n')
    summary[topic] = len(done)
dec.dispose()
println('transfer: topics %s' % summary)
