"""Builds flat Ghidra structures for UnrealScript classes and structs from a reflection dump.

Run inside ghserve.py through ghq.py --exec (globals: p = the program, progs). It reads
local/reflect/<id>.json for the program (editor or game), creates one structure per class in
category /svm (C++ names: A prefix for actors, U otherwise; F prefix for script structs), a
vtable structure per class with slots named after their targets, and marks every function in a
matching class namespace as __thiscall so the decompiler types 'this'. Only names, offsets and
types are written; the result lives in the private project copy.

Set SVM_TYPES_PROG to 'editor' or 'game' (default: the program's name decides).
"""

import json
import os

from ghidra.program.model.data import (ArrayDataType, CategoryPath, DataTypeConflictHandler,
                                       FloatDataType, FunctionDefinitionDataType, IntegerDataType,
                                       PointerDataType, StructureDataType, UnsignedCharDataType,
                                       UnsignedIntegerDataType, Undefined1DataType, VoidDataType)
from ghidra.program.model.symbol import SourceType
from ghidra.app.cmd.disassemble import DisassembleCommand
from ghidra.app.cmd.function import CreateFunctionCmd

REPO = os.path.abspath(os.path.join(os.path.dirname(OUT), '..', '..', '..'))  # noqa: F821 (OUT from ghserve)
prog_id = 'editor' if 'Editor' in p.getName() else 'game'  # noqa: F821
dump = json.load(open(os.path.join(REPO, 'local', 'reflect', prog_id + '.json'), encoding='utf-8'))
dtm = p.getDataTypeManager()  # noqa: F821
CAT = CategoryPath('/svm')

classes = {c['path']: c for c in dump['classes']}
structs = {s['path']: s for s in dump['structs']}
props_by_owner = {}
for pr in dump['properties']:
    props_by_owner.setdefault(pr['owner'], []).append(pr)


def is_actor(path):
    while path:
        if path == 'Engine.Actor':
            return True
        c = classes.get(path)
        path = c['super'] if c else None
    return False


def cpp_name(path):
    short = path.split('.')[-1]
    if path == 'Core.Object':
        return 'UObject'
    return ('A' if is_actor(path) else 'U') + short


short_count = {}
for s in structs:
    short_count[s.split('.')[-1]] = short_count.get(s.split('.')[-1], 0) + 1


def struct_name(path):
    short = path.split('.')[-1]
    if short_count[short] > 1:
        return 'F' + path.split('.')[-2] + '_' + short
    return 'F' + short


made = {}


def empty(name, size):
    st = StructureDataType(CAT, name, size)
    return dtm.addDataType(st, DataTypeConflictHandler.REPLACE_HANDLER)


tid = p.startTransaction('svm reflect types')  # noqa: F821
ROOT = CategoryPath.ROOT
for path, c in classes.items():
    st = StructureDataType(ROOT, cpp_name(path), max(int(c['propertiesSize']), 4))
    made[path] = dtm.addDataType(st, DataTypeConflictHandler.REPLACE_HANDLER)
for path, s in structs.items():
    made[path] = empty(struct_name(path), max(int(s['propertiesSize']), 1))

tarray = dtm.addDataType(StructureDataType(CAT, 'TArray', 0), DataTypeConflictHandler.REPLACE_HANDLER)
tarray.add(PointerDataType(VoidDataType.dataType), 4, 'Data', None)
tarray.add(IntegerDataType.dataType, 4, 'ArrayNum', None)
tarray.add(IntegerDataType.dataType, 4, 'ArrayMax', None)


def chain(path, table):
    out = []
    while path:
        out.append(path)
        rec = table.get(path)
        path = rec['super'] if rec else None
    return list(reversed(out))


def field_type(pr):
    k = pr['kind']
    if k == 'FloatProperty':
        t = FloatDataType.dataType
    elif k == 'IntProperty':
        t = IntegerDataType.dataType
    elif k == 'ByteProperty':
        t = UnsignedCharDataType.dataType
    elif k == 'BoolProperty':
        t = UnsignedIntegerDataType.dataType
    elif k == 'NameProperty':
        t = IntegerDataType.dataType
    elif k in ('ObjectProperty', 'ClassProperty'):
        tgt = made.get(pr.get('propertyClass'))
        t = PointerDataType(tgt if tgt is not None else VoidDataType.dataType)
    elif k == 'StructProperty':
        t = made.get(pr.get('struct'))
        if t is None:
            t = ArrayDataType(Undefined1DataType.dataType, pr['elementSize'], 1)
    elif k in ('ArrayProperty', 'StrProperty'):
        t = tarray
    else:
        t = ArrayDataType(Undefined1DataType.dataType, pr['elementSize'], 1)
    if pr['arrayDim'] > 1:
        t = ArrayDataType(t, pr['arrayDim'], pr['elementSize'])
    return t


def fill(dt, owners, size):
    seen = {}
    bools = {}
    for owner in owners:
        for pr in props_by_owner.get(owner, []):
            off = int(pr['offset'])
            if pr['kind'] == 'BoolProperty':
                bools.setdefault(off, []).append((int(pr['bitMask'], 16), pr['name']))
                if off in seen:
                    continue
            if off in seen:
                continue
            seen[off] = pr
    for off in sorted(seen):
        pr = seen[off]
        t = field_type(pr)
        ln = t.getLength()
        if off + ln > size:
            continue
        name = pr['name']
        comment = None
        if pr['kind'] == 'BoolProperty':
            bits = sorted(bools[off])
            name = 'bits_' + bits[0][1]
            comment = ' '.join('%s=0x%x' % (n, m) for m, n in bits)
        try:
            dt.replaceAtOffset(off, t, ln, name, comment)
        except Exception as exc:  # noqa: BLE001
            print('skip', dt.getName(), name, off, exc)


for path, c in classes.items():
    fill(made[path], chain(path, classes), max(int(c['propertiesSize']), 4))
for path, s in structs.items():
    fill(made[path], chain(path, structs), max(int(s['propertiesSize']), 1))

# vtables ---------------------------------------------------------------------------------------
mem = p.getMemory()  # noqa: F821
fm = p.getFunctionManager()  # noqa: F821
listing = p.getListing()  # noqa: F821
text = [b for b in mem.getBlocks() if b.isExecute()]


def in_text(v):
    a = addr(p, v)  # noqa: F821
    return any(b.contains(a) for b in text)


def read_u32(a):
    return mem.getInt(addr(p, a)) & 0xffffffff  # noqa: F821


vt_types = {}
for path, c in classes.items():
    vt = c.get('instanceVtable') or c.get('vtable')
    if not vt:
        continue
    base = int(vt, 16)
    st = StructureDataType(CAT, cpp_name(path) + '_vtbl', 0)
    names = {}
    i = 0
    while i < 400:
        v = read_u32(base + 4 * i)
        if not in_text(v):
            break
        f = fm.getFunctionAt(addr(p, v))  # noqa: F821
        if f is None:
            if listing.getInstructionAt(addr(p, v)) is None:  # noqa: F821
                DisassembleCommand(addr(p, v), None, True).applyTo(p, monitor)  # noqa: F821
            CreateFunctionCmd(addr(p, v)).applyTo(p, monitor)  # noqa: F821
            f = fm.getFunctionAt(addr(p, v))  # noqa: F821
        nm = f.getName() if f is not None else 'slot'
        nm = ''.join(ch if ch.isalnum() or ch == '_' else '_' for ch in nm)
        nm = '%s_%x' % (nm, 4 * i)
        st.add(PointerDataType(VoidDataType.dataType), 4, nm, f.getName(True) if f is not None else None)
        i += 1
    if i == 0:
        continue
    vtd = dtm.addDataType(st, DataTypeConflictHandler.REPLACE_HANDLER)
    vt_types[path] = vtd
    try:
        made[path].replaceAtOffset(0, PointerDataType(vtd), 4, 'vftable', None)
    except Exception as exc:  # noqa: BLE001
        print('vt skip', path, exc)

# thiscall on class methods ----------------------------------------------------------------------
by_cpp = {cpp_name(path): made[path] for path in classes}
count = 0
for f in fm.getFunctions(True):
    ns = f.getParentNamespace()
    if ns is None or ns.isGlobal():
        continue
    dt = by_cpp.get(ns.getName())
    if dt is None:
        continue
    try:
        if f.getCallingConventionName() != '__thiscall':
            f.setCallingConvention('__thiscall')
        count += 1
    except Exception as exc:  # noqa: BLE001
        print('cc skip', f.getName(True), exc)

p.endTransaction(tid, True)  # noqa: F821
print('classes', len(classes), 'structs', len(structs), 'vtables', len(vt_types), 'methods', count)
