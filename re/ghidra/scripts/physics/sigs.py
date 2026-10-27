"""Applies C signatures to functions in the private project copy (ghq.py --exec sigs.py <prog>).

SIGS maps an address to a signature without the class qualifier, for example
'void physWalking(float deltaTime, int Iterations)'. The function keeps its name and namespace and
stays __thiscall when it is a class method.
"""

from ghidra.app.cmd.function import ApplyFunctionSignatureCmd
from ghidra.app.util.parser import FunctionSignatureParser
from ghidra.program.model.symbol import SourceType

SIGS = globals().get('SIGS') or {}
fm = p.getFunctionManager()  # noqa: F821
dtm = p.getDataTypeManager()  # noqa: F821
parser = FunctionSignatureParser(dtm, None)
tid = p.startTransaction('svm sigs')  # noqa: F821
try:
    for a, sig in SIGS.items():
        f = fm.getFunctionAt(addr(p, a))  # noqa: F821
        if f is None:
            print('no function', a)
            continue
        f.setCustomVariableStorage(False)
        fdef = parser.parse(f.getSignature(), sig.replace(sig.split('(')[0].split()[-1], f.getName(), 1))
        cmd = ApplyFunctionSignatureCmd(f.getEntryPoint(), fdef, SourceType.USER_DEFINED)
        ok = cmd.applyTo(p, monitor)  # noqa: F821
        if f.getParentNamespace() is not None and not f.getParentNamespace().isGlobal() and not sig.startswith('static'):
            f.setCallingConvention('__thiscall')
        print(a, f.getName(True), 'ok' if ok else cmd.getStatusMsg())
finally:
    p.endTransaction(tid, True)  # noqa: F821
