"""Clears the wrong no-return flag on the editor's log function and repairs the code after its calls.

Ghidra's analysis marked the formatted-log helper (editor 0x10f9db90, also used for warnings that the
engine survives) as non-returning, so the instructions after each call were never disassembled and
the decompiler cut those paths ("Subroutine does not return"). Run inside ghserve.py:
ghq.py --exec a35_noreturn_fix.py editor. Works on the actions study copy only.
"""

from ghidra.app.cmd.disassemble import DisassembleCommand
from ghidra.app.cmd.function import CreateFunctionCmd

TARGETS = globals().get('TARGETS', [0x10f9db90])
fm = p.getFunctionManager()  # noqa: F821
lst = p.getListing()  # noqa: F821
refm = p.getReferenceManager()  # noqa: F821
tid = p.startTransaction('svm actions noreturn fix')  # noqa: F821
fixed_funcs = set()
n = 0
try:
    for t in TARGETS:
        f = fm.getFunctionAt(addr(p, t))  # noqa: F821
        f.setNoReturn(False)
        for ref in refm.getReferencesTo(f.getEntryPoint()):
            if not ref.getReferenceType().isCall():
                continue
            ins = lst.getInstructionAt(ref.getFromAddress())
            if ins is None:
                continue
            at = ins.getAddress()
            nxt = at.add(ins.getLength())
            if ins.getFlowType().isTerminal() or lst.getInstructionAt(nxt) is None:
                ins.clearFallThroughOverride()
                lst.clearCodeUnits(at, at, False)
                DisassembleCommand(at, None, True).applyTo(p, monitor)  # noqa: F821
                if lst.getInstructionAt(nxt) is None:
                    DisassembleCommand(nxt, None, True).applyTo(p, monitor)  # noqa: F821
                n += 1
            caller = fm.getFunctionContaining(ref.getFromAddress())
            if caller is not None:
                fixed_funcs.add(caller.getEntryPoint())
    for ep in fixed_funcs:
        CreateFunctionCmd.fixupFunctionBody(p, fm.getFunctionAt(ep), monitor)  # noqa: F821
finally:
    p.endTransaction(tid, True)  # noqa: F821
print('disassembled after', n, 'calls; repaired', len(fixed_funcs), 'functions')
