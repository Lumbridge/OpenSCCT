# Generates BSim signatures for every function of the current program, commits them to the local
# H2 BSim database (local/ghidra-work/transfer/bsim/svm, made with 'bsim createdatabase ... medium_32'),
# and writes each function's feature vector to local/re-out/c/transfer/<id>.vectors.json so the
# matcher can score any pair offline. With the argument 'query=<other exe name>' it then asks the
# database for the nearest functions of that other executable for every function of this program
# and writes local/re-out/c/transfer/<id>.nearest.json.
# Run: analyzeHeadless <work project> SVM -process <program> -noanalysis -readOnly -postScript T20_bsim_sigs.py
# @category SVM.transfer
# @runtime PyGhidra

import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(str(getSourceFile().getAbsolutePath())), '..', '..', 'lib'))
import svm_ghidra as svm  # noqa: E402

from ghidra.features.bsim.query import BSimServerInfo, BSimClientFactory, GenSignatures  # noqa: E402
from ghidra.features.bsim.query.protocol import InsertRequest, QueryExeInfo, QueryNearest  # noqa: E402
from ghidra.features.bsim.query.protocol import BSimFilter  # noqa: E402
from ghidra.features.bsim.gui.filters import ExecutableNameBSimFilterType  # noqa: E402

OUT = os.path.join(svm.REPO_DIR, 'local', 're-out', 'c', 'transfer')
DB = os.path.join(svm.REPO_DIR, 'local', 'ghidra-work', 'transfer', 'bsim', 'svm')
CHUNK = 400

ctx = svm.setup(globals())
program = ctx.program
args = dict(a.split('=', 1) for a in getScriptArgs() if '=' in a)
os.makedirs(OUT, exist_ok=True)

info = BSimServerInfo(DB)
db = BSimClientFactory.buildClient(info, False)
try:
    if not db.initialize():
        raise RuntimeError('cannot open BSim database %s: %s' % (DB, db.getLastError()))
    dbinfo = db.getInfo()
    factory = db.getLSHVectorFactory()
    fm = program.getFunctionManager()

    def new_gensig():
        g = GenSignatures(dbinfo.trackcallgraph)
        g.setVectorFactory(factory)
        g.addExecutableCategories(dbinfo.execats)
        g.addFunctionTags(dbinfo.functionTags)
        g.addDateColumnName(dbinfo.dateColumnName)
        dfile = program.getDomainFile()
        url = dfile.getSharedProjectURL(None) or dfile.getLocalProjectURL(None)
        from ghidra.framework.protocol.ghidra import GhidraURL
        path = GhidraURL.getProjectPathname(url)
        slash = path.rfind('/')
        path = '/' if slash == 0 else path[:slash]
        repo = GhidraURL.getProjectURL(url).toExternalForm()
        g.openProgram(program, None, None, None, repo, path)
        return g

    # 1. Signatures for every function with a body.
    gensig = new_gensig()
    gensig.scanFunctions(fm.getFunctions(True), fm.getFunctionCount(), monitor)
    manager = gensig.getDescriptionManager()
    vectors = {}
    it = manager.listAllFunctions()
    while it.hasNext():
        fd = it.next()
        fd.sortCallgraph()
        sr = fd.getSignatureRecord()
        if sr is None:
            continue
        vec = sr.getLSHVector()
        entries = [[int(h.getHash()), int(h.getTF()), float(h.getCoeff())] for h in vec.getEntries()]
        vectors['%x' % fd.getAddress()] = {'len': float(vec.getLength()), 'sig': float(factory.getSelfSignificance(vec)),
                                           'h': entries}
    with open(os.path.join(OUT, '%s.vectors.json' % ctx.id), 'w', encoding='utf-8', newline='\n') as fh:
        json.dump({'id': ctx.id, 'md5': str(program.getExecutableMD5()), 'vectors': vectors}, fh)
    println('bsim: %s %d function vectors' % (ctx.id, len(vectors)))

    # 2. Commit unless this executable is already in the database.
    q = QueryExeInfo()
    q.filterMd5 = program.getExecutableMD5()
    resp = q.execute(db)
    if resp is not None and resp.recordCount > 0:
        println('bsim: %s already in the database' % ctx.id)
    else:
        req = InsertRequest()
        req.manage = manager
        if req.execute(db) is None:
            raise RuntimeError('insert failed: %s' % db.getLastError().message)
        println('bsim: %s committed (%d functions)' % (ctx.id, manager.numFunctions()))
    gensig.dispose()

    # 3. Nearest functions of another executable for each function of this one.
    other = args.get('query')
    if other:
        results = {}
        funcs = [f for f in fm.getFunctions(True) if not f.isExternal() and not f.isThunk()]
        for start in range(0, len(funcs), CHUNK):
            g = new_gensig()
            from java.util import ArrayList
            lst = ArrayList()
            for f in funcs[start:start + CHUNK]:
                lst.add(f)
            g.scanFunctions(lst.iterator(), lst.size(), monitor)
            qn = QueryNearest()
            qn.manage = g.getDescriptionManager()
            qn.thresh = float(args.get('thresh', '0.5'))
            qn.signifthresh = 0.0
            qn.max = int(args.get('max', '10'))
            flt = BSimFilter()
            flt.addAtom(ExecutableNameBSimFilterType(), other)
            qn.bsimFilter = flt
            rn = qn.execute(db)
            if rn is None:
                raise RuntimeError('query failed: %s' % db.getLastError().message)
            for sres in rn.result:
                base = sres.getBase()
                notes = []
                for note in sres:
                    fdn = note.getFunctionDescription()
                    notes.append(['%x' % fdn.getAddress(), round(float(note.getSimilarity()), 5),
                                  round(float(note.getSignificance()), 3)])
                results['%x' % base.getAddress()] = notes
            g.dispose()
            println('bsim: queried %d/%d' % (min(start + CHUNK, len(funcs)), len(funcs)))
        with open(os.path.join(OUT, '%s.nearest.json' % ctx.id), 'w', encoding='utf-8', newline='\n') as fh:
            json.dump({'id': ctx.id, 'other': other, 'nearest': results}, fh)
        println('bsim: %s nearest written for %d functions' % (ctx.id, len(results)))
finally:
    db.close()
