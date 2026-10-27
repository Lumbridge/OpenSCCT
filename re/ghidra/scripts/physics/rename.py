"""Renames functions and applies signatures in the private project copy.

RENAMES: list of (address, 'Namespace::Name' or 'Name', optional signature without qualifier).
A namespace becomes a class namespace; signatures use the same parser as sigs.py.
Run: ghq.py --exec <file with RENAMES = [...] followed by this script> <prog>
"""

from ghidra.app.cmd.function import ApplyFunctionSignatureCmd
from ghidra.app.util.parser import FunctionSignatureParser
from ghidra.program.model.data import CategoryPath, DataTypeConflictHandler
from ghidra.program.model.symbol import SourceType

fm = p.getFunctionManager()  # noqa: F821
st = p.getSymbolTable()  # noqa: F821
dtm = p.getDataTypeManager()  # noqa: F821
tid = p.startTransaction('svm renames')  # noqa: F821
try:
    for nm in ('FVector', 'FRotator', 'FPlane', 'FCoords', 'FBox'):
        src = dtm.getDataType('/svm/' + nm)
        if src is not None and dtm.getDataType('/' + nm) is None:
            dtm.addDataType(src.copy(dtm).clone(dtm), DataTypeConflictHandler.KEEP_HANDLER)
            c = dtm.getDataType('/svm/' + nm)
            # move a copy to root
            cp = src.clone(dtm)
            cp.setCategoryPath(CategoryPath.ROOT)
            dtm.addDataType(cp, DataTypeConflictHandler.REPLACE_HANDLER)
    parser = FunctionSignatureParser(dtm, None)
    for item in RENAMES:  # noqa: F821
        a, full = item[0], item[1]
        sig = item[2] if len(item) > 2 else None
        f = fm.getFunctionAt(addr(p, a))  # noqa: F821
        if f is None:
            print('no function', a)
            continue
        parts = full.split('::')
        ns = p.getGlobalNamespace()  # noqa: F821
        for part in parts[:-1]:
            ex = st.getNamespace(part, ns)
            if ex is None:
                ex = st.createClass(ns, part, SourceType.USER_DEFINED)
            ns = ex
        f.setParentNamespace(ns)
        f.setName(parts[-1], SourceType.USER_DEFINED)
        if sig:
            fdef = parser.parse(f.getSignature(), sig.replace(sig.split('(')[0].split()[-1], parts[-1], 1))
            ApplyFunctionSignatureCmd(f.getEntryPoint(), fdef, SourceType.USER_DEFINED).applyTo(p, monitor)  # noqa: F821
            if len(parts) > 1 and not sig.startswith('static'):
                f.setCallingConvention('__thiscall')
        print(a, f.getName(True), str(f.getSignature()))
finally:
    p.endTransaction(tid, True)  # noqa: F821
