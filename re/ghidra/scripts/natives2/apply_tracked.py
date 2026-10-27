"""Server script (ghq.py --exec): applies the function names of tracked study annotations to the open copy.

Input globals (optional): KEYS, the annotation files to apply (default actions, net, transfer, natives2). Names
are set only where the function still has a default name (FUN_...), so names from the physics copy are kept.
"""

import json
import os

from ghidra.program.model.symbol import SourceType

REPO_ROOT = os.path.abspath(os.path.join(OUT, '..', '..', '..', '..'))  # noqa: F821
KEYS = globals().get('KEYS', ['actions', 'net', 'transfer', 'natives2'])
PROG_KEY = {'ChaosTheory_Editor.exe': 'editor', 'SCCT Versus': 'game'}


def ns_of(program, path):
    if not path:
        return program.getGlobalNamespace()
    st = program.getSymbolTable()
    parent = program.getGlobalNamespace()
    for part in path.split('::'):
        found = st.getNamespace(part, parent)
        if found is None:
            found = st.createClass(parent, part, SourceType.USER_DEFINED)
        parent = found
    return parent


for k in KEYS:
    fn = os.path.join(REPO_ROOT, 're', 'ghidra', 'annotations', k + '.json')
    if not os.path.isfile(fn):
        continue
    with open(fn, encoding='utf-8') as f:
        doc = json.load(f)
    for sec in doc.get('programs', []):
        pk = PROG_KEY.get(sec['program'])
        prog = progs[pk]  # noqa: F821
        fm = prog.getFunctionManager()
        tx = prog.startTransaction('natives2 apply ' + k)
        n_set = n_new = n_kept = 0
        try:
            for e in sec.get('functions', []):
                a = addr(prog, e['address'])  # noqa: F821
                f = fm.getFunctionAt(a)
                if f is None:
                    from ghidra.app.cmd.disassemble import DisassembleCommand
                    from ghidra.app.cmd.function import CreateFunctionCmd
                    DisassembleCommand(a, None, True).applyTo(prog, monitor)  # noqa: F821
                    CreateFunctionCmd(a).applyTo(prog, monitor)  # noqa: F821
                    f = fm.getFunctionAt(a)
                    if f is None:
                        continue
                    n_new += 1
                if not f.getName().startswith('FUN_') and f.getName() != e['name']:
                    n_kept += 1
                    continue
                f.setParentNamespace(ns_of(prog, e.get('namespace', '')))
                f.setName(e['name'], SourceType.USER_DEFINED)
                n_set += 1
        finally:
            prog.endTransaction(tx, True)
        print('%s %s: set %d, created %d, kept %d' % (k, pk, n_set, n_new, n_kept))
