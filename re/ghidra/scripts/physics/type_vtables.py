"""Types vtable slots as function pointers built from the target functions' current signatures
(private project copy), so virtual calls decompile with argument lists. VTABLES: struct names."""

from ghidra.program.model.data import FunctionDefinitionDataType, PointerDataType, CategoryPath, DataTypeConflictHandler
from ghidra.program.model.symbol import SourceType

dtm = p.getDataTypeManager()  # noqa: F821
fm = p.getFunctionManager()  # noqa: F821
mem = p.getMemory()  # noqa: F821
tid = p.startTransaction('svm vtable types')  # noqa: F821
n = 0
try:
    import json, os
    REPO = os.path.abspath(os.path.join(os.path.dirname(OUT), '..', '..', '..'))  # noqa: F821
    prog_id = 'editor' if 'Editor' in p.getName() else 'game'  # noqa: F821
    dump = json.load(open(os.path.join(REPO, 'local', 'reflect', prog_id + '.json'), encoding='utf-8'))
    byname = {}
    classes = {c['path']: c for c in dump['classes']}
    def is_actor(path):
        while path:
            if path == 'Engine.Actor':
                return True
            c = classes.get(path)
            path = c['super'] if c else None
        return False
    for c in dump['classes']:
        nm = ('A' if is_actor(c['path']) else 'U') + c['name'] if c['path'] != 'Core.Object' else 'UObject'
        byname[nm] = c
    cat = CategoryPath('/svm/fdefs')
    for vt in VTABLES:  # noqa: F821
        c = byname[vt]
        base = int(c.get('instanceVtable') or c['vtable'], 16)
        st = dtm.getDataType('/svm/' + vt + '_vtbl')
        for comp in list(st.getDefinedComponents()):
            o = comp.getOffset()
            tgt = mem.getInt(addr(p, base + o)) & 0xffffffff  # noqa: F821
            f = fm.getFunctionAt(addr(p, tgt))  # noqa: F821
            if f is None:
                continue
            if f.isThunk():
                f = f.getThunkedFunction(True)
            if f.getSignatureSource() == SourceType.DEFAULT:
                continue
            fd = FunctionDefinitionDataType(f, False)
            fd.setNameAndCategory(cat, 'fd_%s_%x' % (vt, o))
            fd = dtm.addDataType(fd, DataTypeConflictHandler.REPLACE_HANDLER)
            st.replaceAtOffset(o, PointerDataType(fd), 4, comp.getFieldName(), comp.getComment())
            n += 1
finally:
    p.endTransaction(tid, True)  # noqa: F821
print('typed slots', n)
