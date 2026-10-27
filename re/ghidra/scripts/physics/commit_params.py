"""Commits decompiler-recovered parameters to the database for functions reachable from ROOTS.

Callers then decompile with real argument lists. Functions whose signature we set by hand
(USER_DEFINED) are left alone. Run: ghq.py --exec <file with ROOTS = [...], DEPTH = n + this> <prog>
"""

from ghidra.program.model.pcode import HighFunctionDBUtil
from ghidra.program.model.symbol import SourceType

fm = p.getFunctionManager()  # noqa: F821
key = 'editor' if 'Editor' in p.getName() else 'game'  # noqa: F821
d = decomps[key]  # noqa: F821
depth = globals().get('DEPTH', 2)
todo = []
seen = set()
frontier = [find_func(p, r) for r in ROOTS]  # noqa: F821
for lvl in range(depth + 1):
    nxt = []
    for f in frontier:
        if f is None or f.getEntryPoint().getOffset() in seen:
            continue
        seen.add(f.getEntryPoint().getOffset())
        if f.isThunk():
            f = f.getThunkedFunction(True)
        todo.append(f)
        if lvl < depth:
            nxt.extend(list(f.getCalledFunctions(monitor)))  # noqa: F821
    frontier = nxt
# leaves first, so callers see committed callee params
todo.reverse()
done = skipped = 0
tid = p.startTransaction('svm commit params')  # noqa: F821
try:
    for f in todo:
        if (f.getSignatureSource() == SourceType.USER_DEFINED and f.getParameterCount() > 1) or f.isExternal():
            skipped += 1
            continue
        if f.getBody().getNumAddresses() > 20000:
            skipped += 1
            continue
        res = d.decompileFunction(f, 60, monitor)  # noqa: F821
        if not res.decompileCompleted():
            continue
        hf = res.getHighFunction()
        try:
            HighFunctionDBUtil.commitParamsToDatabase(hf, True, HighFunctionDBUtil.ReturnCommitOption.COMMIT, SourceType.ANALYSIS)
            done += 1
        except Exception as exc:  # noqa: BLE001
            print('fail', f.getName(True), exc)
finally:
    p.endTransaction(tid, True)  # noqa: F821
print('committed', done, 'skipped', skipped, 'of', len(todo))
