# Records what auto-analysis produced, right after import: function count and statistics for the
# report, and a digest of every comment so 30_export.py can tell analysis comments from ours.
# @category SVM
# @runtime PyGhidra

import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(str(getSourceFile().getAbsolutePath())), '..', 'lib'))
import svm_ghidra as svm  # noqa: E402

ctx = svm.setup(globals())
program = ctx.program

pm = program.getUsrPropertyManager()
baseline = pm.getStringPropertyMap(svm.BASELINE_MAP)
recorded = 0
if baseline is None:
    baseline = pm.createStringPropertyMap(svm.BASELINE_MAP)
    per_address = {}
    for a, key, text in svm.iter_comments(program):
        per_address.setdefault(a, []).append('%s:%s' % (key, svm.text_digest(text)))
    for a, items in per_address.items():
        baseline.add(a, ';'.join(sorted(items)))
        recorded += 1

fm = program.getFunctionManager()
listing = program.getListing()
stats = {
    'program': program.getName(),
    'id': ctx.id,
    'sha256': ctx.target['sha256'],
    'imageBase': svm.hexaddr(program.getImageBase().getOffset()),
    'language': str(program.getLanguageID()),
    'compilerSpec': str(program.getCompilerSpec().getCompilerSpecID()),
    'functions': fm.getFunctionCount(),
    'instructions': listing.getNumInstructions(),
    'definedData': listing.getNumDefinedData(),
    'symbols': program.getSymbolTable().getNumSymbols(),
    'baselineCommentAddresses': recorded,
}
path = ctx.write_report('analysis-stats.json', stats)
println('SVM baseline: %s functions=%d instructions=%d comments recorded=%d -> %s'
        % (ctx.id, stats['functions'], stats['instructions'], recorded, path))
