# Applies the tracked annotations re/ghidra/annotations/<id>.json to the current program, so a fresh
# import plus this script restores every name, namespace, function entry and comment we added.
# Skips quietly when there is no annotations file for this program yet. Never overwrites a user
# name that differs; those are listed in the report instead.
# @category SVM
# @runtime PyGhidra

import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(str(getSourceFile().getAbsolutePath())), '..', 'lib'))
import svm_ghidra as svm  # noqa: E402

ctx = svm.setup(globals())
program = ctx.program
path = os.path.join(svm.ANNOTATIONS_DIR, '%s.json' % ctx.id)

if not os.path.isfile(path):
    println('SVM apply: no %s, nothing to apply' % path)
else:
    with open(path, encoding='utf-8') as f:
        ann = json.load(f)
    if ann.get('sha256') != ctx.target['sha256']:
        raise svm.binlock.BuildMismatch('%s was exported from another build (%s)' % (path, ann.get('sha256')))

    counts = {'namespaces': 0, 'functions': 0, 'functionsCreated': 0, 'labels': 0, 'comments': 0}
    problems = []
    for ns in ann.get('namespaces', []):
        svm.ensure_namespace(program, ns['path'], as_class=(ns.get('kind', 'class') == 'class'))
        counts['namespaces'] += 1

    for fn in ann.get('functions', []):
        a = ctx.addr(int(fn['address'], 16))
        f, how = svm.ensure_function(program, monitor, a)
        if f is None:
            problems.append({'address': fn['address'], 'name': fn['name'], 'problem': how})
            continue
        if how != 'existing':
            counts['functionsCreated'] += 1
        ns = svm.ensure_namespace(program, fn.get('namespace', ''))
        result = svm.set_function_name(f, fn['name'], ns)
        if result == 'kept-user':
            problems.append({'address': fn['address'], 'name': fn['name'], 'problem': 'user name kept: %s'
                             % f.getName(True)})
        counts['functions'] += 1

    for lb in ann.get('labels', []):
        a = ctx.addr(int(lb['address'], 16))
        ns = svm.ensure_namespace(program, lb.get('namespace', ''))
        svm.ensure_label(program, a, lb['name'], ns, primary=lb.get('primary', False))
        counts['labels'] += 1

    listing = program.getListing()
    for c in ann.get('comments', []):
        a = ctx.addr(int(c['address'], 16))
        if listing.getComment(svm.comment_type(c['type']), a) != c['text']:
            svm.set_comment(program, a, c['type'], c['text'])
        counts['comments'] += 1

    ctx.write_report('apply-annotations.json', {'counts': counts, 'problems': problems})
    println('SVM apply: %s %s, %d problems' % (ctx.id, counts, len(problems)))
