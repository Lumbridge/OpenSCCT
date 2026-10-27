"""Gives APawn the ASBasePawn layout and AController/APlayerController the ASPlayerController layout
in the private project copy (every pawn in Versus is an SBasePawn), then drops any custom 'this'
storage on APawn methods, so they are typed by their namespace again."""

from ghidra.program.model.data import DataTypeConflictHandler

dtm = p.getDataTypeManager()  # noqa: F821
tid = p.startTransaction('svm widen')  # noqa: F821
try:
    for dst, src in (('APawn', 'ASBasePawn'), ('AController', 'ASPlayerController'),
                     ('APlayerController', 'ASPlayerController')):
        s = dtm.getDataType('/' + src)
        d = dtm.getDataType('/' + dst)
        d.replaceWith(s)
        print(dst, d.getLength())
    fm = p.getFunctionManager()  # noqa: F821
    n = 0
    for f in fm.getFunctions(True):
        ns = f.getParentNamespace()
        if ns is not None and ns.getName() == 'APawn' and f.hasCustomVariableStorage():
            f.setCustomVariableStorage(False)
            n += 1
    print('reset', n)
finally:
    p.endTransaction(tid, True)  # noqa: F821
