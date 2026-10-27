"""Creates GE (traversal element) types in the private project copy: struct FGE with its vtable
(slots typed from what the study found), and types callers given in GE_FUNCS."""

from ghidra.program.model.data import (StructureDataType, CategoryPath, DataTypeConflictHandler,
                                       PointerDataType, FunctionDefinitionDataType, ParameterDefinitionImpl,
                                       FloatDataType, IntegerDataType, VoidDataType, UnsignedCharDataType,
                                       GenericCallingConvention)

dtm = p.getDataTypeManager()  # noqa: F821
tid = p.startTransaction('svm ge types')  # noqa: F821
try:
    cat = CategoryPath('/svm')
    fv = dtm.getDataType('/FVector')
    ge = StructureDataType(CategoryPath.ROOT, 'FGE', 0)
    gep = PointerDataType(ge)
    vt = StructureDataType(cat, 'FGE_vtbl', 0)

    def fd(name, ret, params):
        f = FunctionDefinitionDataType(cat, 'fd_GE_' + name)
        f.setReturnType(ret)
        args = [ParameterDefinitionImpl('this', gep, None)] + [ParameterDefinitionImpl(n, t, None) for n, t in params]
        f.setArguments(args)
        f.setGenericCallingConvention(GenericCallingConvention.thiscall)
        return PointerDataType(dtm.addDataType(f, DataTypeConflictHandler.REPLACE_HANDLER))

    vp = PointerDataType(VoidDataType.dataType)
    fvp = PointerDataType(fv)
    slots = [('s00_ClipBox', vp), ('s04_Bounds', vp), ('s08_IsSelected', vp), ('s0c_ClearSelected', vp),
             ('s10_GetPointPtrs', vp), ('Connected', fd('Connected', IntegerDataType.dataType, [('Other', gep)])),
             ('Distance', fd('Distance', FloatDataType.dataType, [('Point', fvp)])),
             ('Kind', fd('Kind', IntegerDataType.dataType, [])),
             ('Equals', fd('Equals', IntegerDataType.dataType, [('Other', gep)])),
             ('s24', vp), ('s28_Snap', vp), ('s2c_Size', vp), ('s30_Serialize', vp),
             ('ClosestPoint', fd('ClosestPoint', VoidDataType.dataType, [('OutPoint', fvp), ('OutDir', fvp), ('Point', fvp)])),
             ('GetPoints', fd('GetPoints', VoidDataType.dataType, [('OutP1', fvp), ('OutP2', fvp)]))]
    for n, t in slots:
        vt.add(t, 4, n, None)
    vtd = dtm.addDataType(vt, DataTypeConflictHandler.REPLACE_HANDLER)
    ge.add(PointerDataType(vtd), 4, 'vftable', None)
    ge.add(IntegerDataType.dataType, 4, 'f04', None)
    ge.add(IntegerDataType.dataType, 4, 'Flags', None)
    ge.add(IntegerDataType.dataType, 4, 'Platform', None)
    ge.add(UnsignedCharDataType.dataType, 1, 'ID', None)
    ge.add(UnsignedCharDataType.dataType, 1, 'pad11', None)
    ge.add(UnsignedCharDataType.dataType, 1, 'pad12', None)
    ge.add(UnsignedCharDataType.dataType, 1, 'pad13', None)
    ge.add(fv, 12, 'P1', None)
    ge.add(fv, 12, 'P2', None)
    ge.add(fv, 12, 'P3', None)
    dtm.addDataType(ge, DataTypeConflictHandler.REPLACE_HANDLER)
finally:
    p.endTransaction(tid, True)  # noqa: F821
print('ok')
