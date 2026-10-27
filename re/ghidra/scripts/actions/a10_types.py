# Builds one Ghidra structure per UClass and per script struct from the reflection dump of the
# running program (local/reflect/<id>.json), so decompiled natives show field names instead of
# offsets. Actor classes become A<Name>, other objects U<Name>, script structs F<Name>. Bool fields
# that share a dword become one uint named bf_<offset>; local/re-out/c/actions/<id>.bools.json maps
# (struct, offset, mask) back to the script names. Native-only bytes stay undefined.
# Run on a working copy of the project only (it changes data types).
# @category SVM
# @runtime PyGhidra

import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(str(getSourceFile().getAbsolutePath())), '..', '..', 'lib'))
import svm_ghidra as svm  # noqa: E402

ctx = svm.setup(globals(), expect=('game', 'editor'))
program = ctx.program

from ghidra.program.model.data import (  # noqa: E402
    ArrayDataType, ByteDataType, CategoryPath, DataTypeConflictHandler, FloatDataType, IntegerDataType,
    PointerDataType, StructureDataType, TypedefDataType, UnsignedIntegerDataType, Undefined1DataType)

REFLECT = os.path.join(svm.REPO_DIR, 'local', 'reflect', '%s.json' % ctx.id)
OUT = os.path.join(svm.OUT_DIR, 'c', 'actions')
CAT = CategoryPath.ROOT

with open(REFLECT, encoding='utf-8') as f:
    refl = json.load(f)

classes = {c['path']: c for c in refl['classes']}
structs = {s['path']: s for s in refl['structs']}
props_by_owner = {}
for p in refl['properties']:
    props_by_owner.setdefault(p['owner'], []).append(p)


def is_actor(path):
    seen = set()
    while path and path not in seen:
        seen.add(path)
        if path == 'Engine.Actor':
            return True
        path = classes.get(path, {}).get('super')
    return False


def cpp_name(path):
    if path in structs:
        return 'F' + path.split('.')[-1]
    short = path.split('.')[-1]
    return ('A' if is_actor(path) else 'U') + short


dtm = program.getDataTypeManager()
tx = program.startTransaction('SVM actions: reflection types')
created = {}
bools = []
try:
    fname = dtm.addDataType(TypedefDataType(CAT, 'FName', IntegerDataType()), DataTypeConflictHandler.REPLACE_HANDLER)
    tarray = StructureDataType(CAT, 'TArrayRaw', 0)
    tarray.add(PointerDataType(), 4, 'Data', None)
    tarray.add(IntegerDataType(), 4, 'ArrayNum', None)
    tarray.add(IntegerDataType(), 4, 'ArrayMax', None)
    tarray = dtm.addDataType(tarray, DataTypeConflictHandler.REPLACE_HANDLER)

    def size_of(path):
        if path in structs:
            return max(1, structs[path]['propertiesSize'])
        return max(1, classes[path]['propertiesSize'])

    # Pass 1: empty structures with the right sizes, so pointers and nested structs resolve.
    for path in list(classes) + list(structs):
        name = cpp_name(path)
        s = StructureDataType(CAT, name, size_of(path))
        created[path] = dtm.addDataType(s, DataTypeConflictHandler.REPLACE_HANDLER)

    def field_type(p):
        k = p['kind']
        if k == 'IntProperty':
            return IntegerDataType()
        if k == 'FloatProperty':
            return FloatDataType()
        if k == 'ByteProperty':
            return ByteDataType()
        if k == 'NameProperty':
            return fname
        if k in ('StrProperty', 'ArrayProperty'):
            return tarray
        if k in ('ObjectProperty', 'ClassProperty'):
            target = created.get(p.get('propertyClass'))
            return PointerDataType(target) if target is not None else PointerDataType()
        if k == 'StructProperty':
            return created.get(p.get('struct'))
        return None

    def fill(path):
        st = created[path]
        chain = []
        cur = path
        while cur:
            chain.append(cur)
            cur = (classes.get(cur) or structs.get(cur) or {}).get('super')
        placed = {}
        for owner in reversed(chain):
            for p in props_by_owner.get(owner, []):
                if p['kind'] == 'BoolProperty':
                    placed.setdefault(p['offset'], ('bool', []))[1].append((int(p.get('bitMask', '0'), 16), p['name']))
                else:
                    placed[p['offset']] = ('field', p)
        total = st.getLength()
        for off in sorted(placed):
            kind, v = placed[off]
            try:
                if kind == 'bool':
                    if off + 4 > total:
                        continue
                    st.replaceAtOffset(off, UnsignedIntegerDataType(), 4, 'bf_%x' % off,
                                       ' '.join('%s=%#x' % (n, m) for m, n in sorted(v)))
                    for m, n in v:
                        bools.append({'struct': st.getName(), 'offset': off, 'mask': m, 'name': n})
                    continue
                p = v
                t = field_type(p)
                n = p['arrayDim'] * p['elementSize']
                if off + n > total or n <= 0:
                    continue
                if t is None or t.getLength() != p['elementSize']:
                    t = ArrayDataType(Undefined1DataType(), p['elementSize'], 1)
                if p['arrayDim'] > 1:
                    t = ArrayDataType(t, p['arrayDim'], p['elementSize'])
                st.replaceAtOffset(off, t, n, p['name'], None)
            except Exception as exc:  # noqa: BLE001
                println('SVM types: %s +%#x: %s' % (st.getName(), off, exc))

    for path in structs:
        fill(path)
    for path in classes:
        fill(path)
finally:
    program.endTransaction(tx, True)

os.makedirs(OUT, exist_ok=True)
with open(os.path.join(OUT, '%s.bools.json' % ctx.id), 'w', encoding='utf-8') as f:
    json.dump(bools, f, indent=0)
println('SVM types: %d structures, %d bool bits' % (len(created), len(bools)))
