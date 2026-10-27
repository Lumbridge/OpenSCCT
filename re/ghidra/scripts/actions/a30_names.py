"""Applies the actions study's names and signatures (names_table.py) in the study's project copy.

Run inside ghserve.py: ghq.py --exec re/ghidra/scripts/actions/a30_names.py editor|game. Each entry is our
reading of a function from its behaviour (the ExecuteAction helpers, the animation channel natives, the
vector helpers, the camera and input natives), recorded so the decompiler gives the callers real argument
lists. The same table is exported to re/ghidra/annotations/actions.json by write_annotations.py.
"""

import os

from ghidra.app.cmd.function import ApplyFunctionSignatureCmd
from ghidra.app.util.parser import FunctionSignatureParser
from ghidra.program.model.symbol import SourceType

_REPO = os.path.abspath(os.path.join(os.path.dirname(OUT), '..', '..', '..'))  # noqa: F821 (OUT from ghserve)
_TABLE = {}
with open(os.path.join(_REPO, 're', 'ghidra', 'scripts', 'actions', 'names_table.py'), encoding='utf-8') as _f:
    exec(compile(_f.read(), 'names_table.py', 'exec'), _TABLE)

key = 'editor' if 'Editor' in p.getName() else 'game'  # noqa: F821
table = dict(_TABLE['EDITOR'] if key == 'editor' else _TABLE['GAME'])
table.update(globals().get('EXTRA', {}))
fm = p.getFunctionManager()  # noqa: F821
dtm = p.getDataTypeManager()  # noqa: F821
st = p.getSymbolTable()  # noqa: F821
parser = FunctionSignatureParser(dtm, None)
tid = p.startTransaction('svm actions names')  # noqa: F821
try:
    for a, (ns_name, name, sig, thiscall) in sorted(table.items()):
        f = fm.getFunctionAt(addr(p, a))  # noqa: F821
        if f is None:
            print('no function', hex(a))
            continue
        if f.isThunk():
            f = f.getThunkedFunction(True)
        ns = p.getGlobalNamespace()  # noqa: F821
        if ns_name:
            ns = st.getNamespace(ns_name, None) or st.createClass(None, ns_name, SourceType.USER_DEFINED)
        try:
            f.setParentNamespace(ns)
            f.setName(name, SourceType.USER_DEFINED)
        except Exception as exc:  # noqa: BLE001
            print('rename', hex(a), exc)
        if sig:
            f.setCustomVariableStorage(False)
            fdef = parser.parse(f.getSignature(), sig.replace(' f(', ' %s(' % f.getName(), 1))
            cmd = ApplyFunctionSignatureCmd(f.getEntryPoint(), fdef, SourceType.USER_DEFINED)
            if not cmd.applyTo(p, monitor):  # noqa: F821
                print('sig', hex(a), cmd.getStatusMsg())
        if thiscall:
            f.setCallingConvention('__thiscall')
        print(hex(a), f.getName(True), f.getSignature())
finally:
    p.endTransaction(tid, True)  # noqa: F821
