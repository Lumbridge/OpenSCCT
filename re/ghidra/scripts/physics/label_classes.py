"""Labels static UClass objects from the reflection dump in the private copy: <CppName>_StaticClass."""

import json
import os

from ghidra.program.model.symbol import SourceType

REPO = os.path.abspath(os.path.join(os.path.dirname(OUT), '..', '..', '..'))  # noqa: F821
prog_id = 'editor' if 'Editor' in p.getName() else 'game'  # noqa: F821
dump = json.load(open(os.path.join(REPO, 'local', 'reflect', prog_id + '.json'), encoding='utf-8'))
classes = {c['path']: c for c in dump['classes']}


def is_actor(path):
    while path:
        if path == 'Engine.Actor':
            return True
        c = classes.get(path)
        path = c['super'] if c else None
    return False


st = p.getSymbolTable()  # noqa: F821
tid = p.startTransaction('svm class labels')  # noqa: F821
n = 0
try:
    for path, c in classes.items():
        if not c.get('static'):
            continue
        a = addr(p, int(c['address'], 16))  # noqa: F821
        nm = ('A' if is_actor(path) else 'U') + c['name'] + '_StaticClass'
        st.createLabel(a, nm, SourceType.USER_DEFINED).setPrimary()
        n += 1
finally:
    p.endTransaction(tid, True)  # noqa: F821
print('labels', n)
