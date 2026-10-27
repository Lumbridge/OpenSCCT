# Decompiles a list of functions into local/re-out/c/actions/<id>/ (never tracked). The job file
# (first script argument, JSON) lists {"address": "0x...", "namespace": "ASBasePawn", "name": "X",
# "depth": 1}. namespace/name, when given, are applied in this working copy so the decompiler types
# `this` with the matching reflection structure (a10_types.py). depth > 0 also decompiles the direct
# callees, up to "maxCallees" in all. Each output file starts with a banner line naming this tool.
# @category SVM
# @runtime PyGhidra

import json
import os
import re
import sys

sys.path.insert(0, os.path.join(os.path.dirname(str(getSourceFile().getAbsolutePath())), '..', '..', 'lib'))
import svm_ghidra as svm  # noqa: E402

ctx = svm.setup(globals(), expect=('game', 'editor', 'reloaded_core'))
program = ctx.program
args = list(getScriptArgs())
with open(args[0], encoding='utf-8') as f:
    job = json.load(f)

from ghidra.app.decompiler import DecompInterface, DecompileOptions  # noqa: E402

OUT = os.path.join(svm.OUT_DIR, 'c', 'actions', ctx.id)
os.makedirs(OUT, exist_ok=True)

fm = program.getFunctionManager()
dtm = program.getDataTypeManager()


def type_this(f, struct_name=None):
    """Give the first parameter the reflection structure of the function's class (or struct_name)."""
    from ghidra.program.model.data import CategoryPath, PointerDataType
    from ghidra.program.model.symbol import SourceType
    name = struct_name
    if name is None:
        ns = f.getParentNamespace()
        if ns is None or ns.isGlobal():
            return
        name = ns.getName()
    dt = dtm.getDataType(CategoryPath.ROOT, name)
    if dt is None:
        return
    params = f.getParameters()
    if f.getCallingConventionName() in ('unknown', '__stdcall') and len(params) == 0 and struct_name is None:
        try:
            f.setCallingConvention('__thiscall')
        except Exception as exc:  # noqa: BLE001
            println('SVM decomp: cc %s failed: %s' % (f.getName(True), exc))
        return
    if len(params) == 0 or params[0].isAutoParameter():
        return
    try:
        params[0].setDataType(PointerDataType(dt), SourceType.USER_DEFINED)
        params[0].setName('this_', SourceType.USER_DEFINED)
    except Exception as exc:  # noqa: BLE001
        println('SVM decomp: type this %s failed: %s' % (f.getName(True), exc))

dec = DecompInterface()
opts = DecompileOptions()
opts.grabFromProgram(program)
dec.setOptions(opts)
dec.toggleCCode(True)
dec.toggleSyntaxTree(True)
dec.openProgram(program)

tx = program.startTransaction('SVM actions: decompile job')
done = set()
index = []
try:
    queue = []
    for item in job['functions']:
        a = ctx.addr(int(item['address'], 16))
        f, how = svm.ensure_function(program, monitor, a)
        if f is None:
            index.append({'address': item['address'], 'error': how})
            continue
        if item.get('namespace') or item.get('name'):
            ns = svm.ensure_namespace(program, item.get('namespace', ''))
            name = item.get('name') or f.getName()
            try:
                f.setParentNamespace(ns)
                f.setName(name, svm.user_source())
            except Exception as exc:  # noqa: BLE001
                println('SVM decomp: rename %s failed: %s' % (item['address'], exc))
        if item.get('thiscall'):
            try:
                f.setCallingConvention('__thiscall')
            except Exception as exc:  # noqa: BLE001
                println('SVM decomp: cc %s failed: %s' % (item['address'], exc))
        type_this(f, item.get('thisType'))
        queue.append((f, int(item.get('depth', 0))))
    max_callees = int(job.get('maxCallees', 200))
    callees = 0
    timeout = int(job.get('timeout', 300))
    while queue:
        f, depth = queue.pop(0)
        key = f.getEntryPoint().getOffset()
        if key in done:
            continue
        done.add(key)
        type_this(f)
        res = dec.decompileFunction(f, timeout, monitor)
        text = res.getDecompiledFunction().getC() if res and res.decompileCompleted() else None
        fname = '%08x_%s.c' % (key, re.sub(r'[^A-Za-z0-9_]+', '_', f.getName(True))[:80])
        with open(os.path.join(OUT, fname), 'w', encoding='utf-8', newline='\n') as out:
            out.write('// svm-actions a20_decomp output (local only): %s %s %s\n'
                      % (program.getName(), svm.hexaddr(key), f.getName(True)))
            out.write(text if text else '// decompile failed: %s\n' % (res.getErrorMessage() if res else 'none'))
        index.append({'address': svm.hexaddr(key), 'name': f.getName(True), 'file': fname,
                      'ok': bool(text), 'size': int(f.getBody().getNumAddresses())})
        if depth > 0:
            for c in sorted(f.getCalledFunctions(monitor), key=lambda x: x.getEntryPoint().getOffset()):
                if c.isThunk() or c.isExternal():
                    continue
                if c.getEntryPoint().getOffset() in done or callees >= max_callees:
                    continue
                callees += 1
                queue.append((c, depth - 1))
finally:
    program.endTransaction(tx, True)

with open(os.path.join(OUT, 'index-%s.json' % job.get('name', 'job')), 'w', encoding='utf-8') as f:
    json.dump(index, f, indent=1)
println('SVM decomp: %d functions -> %s' % (len(index), OUT))
