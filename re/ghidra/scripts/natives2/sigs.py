"""Server script (ghq.py --exec): names and C signatures for functions of the open copy.

Input globals: SIGS, a list of [address, 'Namespace::Name' or 'Name', signature or None]. A class method keeps
__thiscall; a signature may name types the copy already has (FVector, FGE, ASPlayerController...).
"""

from ghidra.app.cmd.function import ApplyFunctionSignatureCmd
from ghidra.app.util.parser import FunctionSignatureParser
from ghidra.program.model.symbol import SourceType

fm = p.getFunctionManager()  # noqa: F821
st = p.getSymbolTable()  # noqa: F821
parser = FunctionSignatureParser(p.getDataTypeManager(), None)  # noqa: F821
tid = p.startTransaction('natives2 sigs')  # noqa: F821
try:
    for a, full, sig in SIGS:  # noqa: F821
        f = fm.getFunctionAt(addr(p, a))  # noqa: F821
        if f is None:
            print('no function', a)
            continue
        parts = full.split('::')
        ns = p.getGlobalNamespace()  # noqa: F821
        for part in parts[:-1]:
            ex = st.getNamespace(part, ns)
            ns = ex if ex is not None else st.createClass(ns, part, SourceType.USER_DEFINED)
        f.setParentNamespace(ns)
        f.setName(parts[-1], SourceType.USER_DEFINED)
        if sig:
            f.setCustomVariableStorage(False)
            fdef = parser.parse(f.getSignature(), sig)
            cmd = ApplyFunctionSignatureCmd(f.getEntryPoint(), fdef, SourceType.USER_DEFINED)
            ok = cmd.applyTo(p, monitor)  # noqa: F821
            if len(parts) > 1 and not sig.startswith('static'):
                f.setCallingConvention('__thiscall')
            print(a, f.getName(True), 'ok' if ok else cmd.getStatusMsg())
        else:
            print(a, f.getName(True), 'named')
finally:
    p.endTransaction(tid, True)  # noqa: F821
