"""Names the small script-event stubs in the private project copy (ghq.py --exec name_events.py <prog>).

An event stub looks up a script function by FName index (FindFunctionChecked) and calls
ProcessEvent. The pushed constant is the name index; the name comes from the reflection dump's
GNames table. Unnamed stubs (FUN_*) get 'event<Name>'. FIND_FUNCTION is the FindFunctionChecked
address in that program, given in the globals.
"""

import json
import os

from ghidra.program.model.symbol import SourceType

REPO = os.path.abspath(os.path.join(os.path.dirname(OUT), '..', '..', '..'))  # noqa: F821
prog_id = 'editor' if 'Editor' in p.getName() else 'game'  # noqa: F821
names = json.load(open(os.path.join(REPO, 'local', 'reflect', prog_id + '.json'), encoding='utf-8'))['nameTable']
fm = p.getFunctionManager()  # noqa: F821
listing = p.getListing()  # noqa: F821
refm = p.getReferenceManager()  # noqa: F821
target = addr(p, FIND_FUNCTION)  # noqa: F821
tid = p.startTransaction('svm event names')  # noqa: F821
done = 0
try:
    for ref in refm.getReferencesTo(target):
        call = listing.getInstructionAt(ref.getFromAddress())
        if call is None:
            continue
        f = fm.getFunctionContaining(call.getAddress())
        if f is None:
            continue
        # walk back to the first push of an immediate
        ins = call.getPrevious()
        idx = None
        for _ in range(6):
            if ins is None:
                break
            m = ins.getMnemonicString()
            if m == 'PUSH' and ins.getScalar(0) is not None:
                v = ins.getScalar(0).getUnsignedValue()
                if v != 0:
                    idx = v
                    break
            if m == 'MOV' and ins.getNumOperands() == 2 and ins.getScalar(1) is not None and '[' in ins.getDefaultOperandRepresentation(0):
                idx = ins.getScalar(1).getUnsignedValue()
                break
            ins = ins.getPrevious()
        if idx is None or str(idx) not in names:
            continue
        nm = names[str(idx)]
        if f.getName().startswith('FUN_') and f.getBody().getNumAddresses() < 400:
            f.setName('event' + nm, SourceType.USER_DEFINED)
            done += 1
        cu = listing.getCodeUnitAt(call.getAddress())
        cu.setComment(cu.EOL_COMMENT, 'name ' + nm)
finally:
    p.endTransaction(tid, True)  # noqa: F821
print('named', done)
