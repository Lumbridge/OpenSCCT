"""Writes re/ghidra/annotations/natives2.json from names_table.py, in the svm.ghidra.annotations/1 layout.

Entries already named in another tracked annotations file (editor, game, physics, actions, net, transfer) are
left out, so applying every file in turn gives one name per address. Plain Python:
python re/ghidra/scripts/natives2/write_annotations.py
"""

import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..'))
ANN = os.path.join(REPO, 're', 'ghidra', 'annotations')
sys.path.insert(0, HERE)
import names_table as nt  # noqa: E402

OTHER = ['editor', 'game', 'physics', 'actions', 'net', 'transfer']


def known_addresses(program_id):
    seen = set()
    for k in OTHER:
        fn = os.path.join(ANN, k + '.json')
        if not os.path.isfile(fn):
            continue
        with open(fn, encoding='utf-8') as f:
            doc = json.load(f)
        sections = doc['programs'] if 'programs' in doc else [doc]
        for sec in sections:
            if sec.get('id') != program_id:
                continue
            for e in sec.get('functions', []):
                seen.add(int(e['address'], 16))
    return seen


def program_header(program_id):
    with open(os.path.join(ANN, program_id + '.json'), encoding='utf-8') as f:
        doc = json.load(f)
    return {k: doc[k] for k in ('program', 'sha256', 'imageBase')}


def section(program_id, funcs, comments):
    seen = known_addresses(program_id)
    namespaces, functions = [], []
    for a, full, _sig in funcs:
        if a in seen:
            continue
        ns, _, name = full.rpartition('::')
        if ns and ns not in namespaces:
            namespaces.append(ns)
        functions.append({'address': '0x%08x' % a, 'namespace': ns, 'name': name})
    plates = [{'address': '0x%08x' % a, 'type': 'plate', 'text': t} for a, t in comments]
    sec = {'format': 'svm.ghidra.annotations/1', 'id': program_id}
    sec.update(program_header(program_id))
    sec['counts'] = {'namespaces': len(namespaces), 'functions': len(functions), 'labels': 0,
                     'comments': len(plates)}
    sec['namespaces'] = [{'path': ns, 'kind': 'class'} for ns in namespaces]
    sec['functions'] = functions
    sec['labels'] = []
    sec['comments'] = plates
    return sec


def main():
    doc = {
        'format': 'svm.ghidra.annotations/1',
        'id': 'natives2',
        'note': 'Per-program sections in the svm.ghidra.annotations/1 layout; merge each into annotations/<program id>.json.',
        'programs': [section('editor', nt.EDITOR_FUNCS, nt.EDITOR_COMMENTS),
                     section('game', nt.GAME_FUNCS, nt.GAME_COMMENTS)],
    }
    out = os.path.join(ANN, 'natives2.json')
    lines = ['{']
    lines.append('  "format": %s,' % json.dumps(doc['format']))
    lines.append('  "id": %s,' % json.dumps(doc['id']))
    lines.append('  "note": %s,' % json.dumps(doc['note']))
    lines.append('  "programs": [')
    for i, sec in enumerate(doc['programs']):
        lines.append('    {')
        keys = list(sec.keys())
        for j, k in enumerate(keys):
            v = sec[k]
            tail = ',' if j < len(keys) - 1 else ''
            if isinstance(v, list):
                if not v:
                    lines.append('      %s: []%s' % (json.dumps(k), tail))
                    continue
                lines.append('      %s: [' % json.dumps(k))
                for n, e in enumerate(v):
                    lines.append('        %s%s' % (json.dumps(e), ',' if n < len(v) - 1 else ''))
                lines.append('      ]%s' % tail)
            else:
                lines.append('      %s: %s%s' % (json.dumps(k), json.dumps(v), tail))
        lines.append('    }%s' % (',' if i < len(doc['programs']) - 1 else ''))
    lines.append('  ]')
    lines.append('}')
    with open(out, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(lines) + '\n')
    json.load(open(out, encoding='utf-8'))
    print('wrote', os.path.relpath(out, REPO), [len(s['functions']) for s in doc['programs']])


if __name__ == '__main__':
    main()
