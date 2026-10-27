# Exports what we added to the program into the tracked file re/ghidra/annotations/<id>.json:
# user-defined function names and labels with their namespaces and addresses, the namespaces
# themselves, and every comment that differs from the auto-analysis baseline recorded by
# 01_baseline.py. Names and addresses only, never code. Sorted, one entry per line, for stable diffs.
# An optional script argument writes to another directory instead.
# @category SVM
# @runtime PyGhidra

import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(str(getSourceFile().getAbsolutePath())), '..', 'lib'))
import svm_ghidra as svm  # noqa: E402

FORMAT = 'svm.ghidra.annotations/1'


def dump(obj):
    return json.dumps(obj, ensure_ascii=False, sort_keys=False, separators=(', ', ': '))


def write(path, header, sections):
    lines = ['{']
    for k, v in header:
        lines.append('  %s: %s,' % (dump(k), dump(v)))
    for i, (name, items) in enumerate(sections):
        last = i == len(sections) - 1
        if not items:
            lines.append('  %s: []%s' % (dump(name), '' if last else ','))
            continue
        lines.append('  %s: [' % dump(name))
        for j, item in enumerate(items):
            lines.append('    %s%s' % (dump(item), '' if j == len(items) - 1 else ','))
        lines.append('  ]%s' % ('' if last else ','))
    lines.append('}')
    tmp = path + '.tmp'
    with open(tmp, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(lines) + '\n')
    os.replace(tmp, path)


ctx = svm.setup(globals())
program = ctx.program
from ghidra.program.model.symbol import SourceType, SymbolType  # noqa: E402

st = program.getSymbolTable()
functions, labels = [], []
namespaces = {}
it = st.getAllSymbols(True)
while it.hasNext():
    s = it.next()
    if s.getSource() != SourceType.USER_DEFINED or s.isExternal():
        continue
    stype = s.getSymbolType()
    if stype == SymbolType.CLASS or stype == SymbolType.NAMESPACE:
        continue
    if stype != SymbolType.FUNCTION and stype != SymbolType.LABEL:
        continue
    ns_path = svm.namespace_path(s.getParentNamespace())
    entry = {'address': svm.hexaddr(s.getAddress().getOffset()), 'namespace': ns_path, 'name': s.getName()}
    if stype == SymbolType.FUNCTION:
        functions.append(entry)
    else:
        entry['primary'] = bool(s.isPrimary())
        labels.append(entry)
    ns = s.getParentNamespace()
    while ns is not None and not ns.isGlobal():
        kind = 'class' if ns.getSymbol().getSymbolType() == SymbolType.CLASS else 'namespace'
        namespaces[svm.namespace_path(ns)] = kind
        ns = ns.getParentNamespace()

baseline = program.getUsrPropertyManager().getStringPropertyMap(svm.BASELINE_MAP)
comments = []
for a, key, text in svm.iter_comments(program):
    if baseline is not None:
        known = baseline.getString(a)
        if known and ('%s:%s' % (key, svm.text_digest(text))) in known.split(';'):
            continue
    comments.append({'address': svm.hexaddr(a.getOffset()), 'type': key, 'text': text})


def by_address(e):
    return (e['address'], e.get('namespace', ''), e.get('name', e.get('type', '')))


functions.sort(key=by_address)
labels.sort(key=by_address)
comments.sort(key=by_address)
ns_list = [{'path': p, 'kind': k} for p, k in sorted(namespaces.items())]

# Optional first argument: another output directory (used to compare a rebuild with the tracked file).
args = list(getScriptArgs())
out_dir = args[0] if args else svm.ANNOTATIONS_DIR
os.makedirs(out_dir, exist_ok=True)
path = os.path.join(out_dir, '%s.json' % ctx.id)
header = [('format', FORMAT), ('id', ctx.id), ('program', program.getName()), ('sha256', ctx.target['sha256']),
          ('imageBase', svm.hexaddr(program.getImageBase().getOffset())),
          ('counts', {'namespaces': len(ns_list), 'functions': len(functions), 'labels': len(labels),
                      'comments': len(comments)})]
write(path, header, [('namespaces', ns_list), ('functions', functions), ('labels', labels), ('comments', comments)])
println('SVM export: %s namespaces=%d functions=%d labels=%d comments=%d -> %s'
        % (ctx.id, len(ns_list), len(functions), len(labels), len(comments), path))
