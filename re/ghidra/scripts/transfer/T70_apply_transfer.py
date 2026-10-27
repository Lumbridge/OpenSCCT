# Applies the names of re/ghidra/annotations/transfer.json (written by write_transfer.py) to the
# current program, for the section whose id matches the program. Like 05_apply_annotations.py it
# never overwrites a user name that differs (those are reported), creates missing functions, and
# adds the plate comments. Run on the transfer work copy only:
#   re\ghidra\scripts\transfer\run.ps1 -Program 'SCCT Versus' -Scripts T70_apply_transfer.py
# Argument: ann=<annotations id, default transfer>.
# Report: local/re-out/<id>.apply-transfer.json.
# @category SVM.transfer
# @runtime PyGhidra

import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(str(getSourceFile().getAbsolutePath())), '..', '..', 'lib'))
import svm_ghidra as svm  # noqa: E402

ctx = svm.setup(globals())
program = ctx.program
args = dict(a.split('=', 1) for a in getScriptArgs() if '=' in a)
ann_id = args.get('ann', 'transfer')
path = os.path.join(svm.ANNOTATIONS_DIR, '%s.json' % ann_id)
with open(path, encoding='utf-8') as fh:
    data = json.load(fh)
sections = [p for p in data.get('programs', [data]) if p.get('id') == ctx.id]
if not sections:
    println('transfer: %s has no section for %s' % (path, ctx.id))
else:
    sec = sections[0]
    if sec.get('sha256') != ctx.target['sha256']:
        raise svm.binlock.BuildMismatch('%s section %s was written for another build' % (path, ctx.id))
    counts = {'namespaces': 0, 'functions': 0, 'functionsCreated': 0, 'renamed': 0, 'same': 0, 'comments': 0}
    problems = []
    for ns in sec.get('namespaces', []):
        svm.ensure_namespace(program, ns['path'], as_class=(ns.get('kind', 'class') == 'class'))
        counts['namespaces'] += 1
    for fn in sec.get('functions', []):
        a = ctx.addr(int(fn['address'], 16))
        f, how = svm.ensure_function(program, monitor, a)
        if f is None:
            problems.append({'address': fn['address'], 'name': fn['name'], 'problem': how})
            continue
        if how != 'existing':
            counts['functionsCreated'] += 1
        try:
            ns = svm.ensure_namespace(program, fn.get('namespace', ''))
            result = svm.set_function_name(f, fn['name'], ns)
        except Exception as exc:  # noqa: BLE001 - report and go on (duplicate or invalid names)
            problems.append({'address': fn['address'], 'name': fn['name'], 'problem': str(exc)})
            continue
        if result == 'kept-user':
            problems.append({'address': fn['address'], 'name': fn['name'],
                             'problem': 'user name kept: %s' % f.getName(True)})
        elif result == 'same':
            counts['same'] += 1
        else:
            counts['renamed'] += 1
        counts['functions'] += 1
    listing = program.getListing()
    for c in sec.get('comments', []):
        a = ctx.addr(int(c['address'], 16))
        if listing.getComment(svm.comment_type(c['type']), a) != c['text']:
            svm.set_comment(program, a, c['type'], c['text'])
        counts['comments'] += 1
    ctx.write_report('apply-%s.json' % ann_id, {'counts': counts, 'problems': problems})
    println('transfer: applied %s to %s: %s, %d problems' % (ann_id, ctx.id, counts, len(problems)))
