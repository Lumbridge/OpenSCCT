"""Writes re/ghidra/annotations/actions.json from names_table.py: the names, namespaces and plate comments the
actions study added, per program, in the svm.ghidra.annotations/1 layout (one entry per line). Functions the
tracked editor.json, game.json, physics.json or net.json already name are left out.

Plain Python (no Ghidra): python re/ghidra/scripts/actions/write_annotations.py
"""

import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..'))
ANN = os.path.join(REPO, 're', 'ghidra', 'annotations')
sys.path.insert(0, HERE)

import names_table  # noqa: E402


def known(prog_id):
    """Addresses already named for this program in the tracked annotation files."""
    have = set()
    for fn in ('editor.json', 'game.json', 'physics.json', 'net.json'):
        path = os.path.join(ANN, fn)
        if not os.path.exists(path):
            continue
        d = json.load(open(path, encoding='utf-8'))
        secs = d.get('programs') or [d]
        for s in secs:
            if s.get('id') == prog_id:
                have |= {int(f['address'], 16) for f in s.get('functions', [])}
    return have


def base(prog_id):
    d = json.load(open(os.path.join(ANN, prog_id + '.json'), encoding='utf-8'))
    return d


def section(prog_id, table, comments):
    b = base(prog_id)
    have = known(prog_id)
    fl = sorted((a, ns or '', name) for a, (ns, name, _sig, _tc) in table.items() if a not in have)
    ns = sorted({f[1] for f in fl if f[1]})
    cm = sorted(comments.items())
    return {
        'format': 'svm.ghidra.annotations/1', 'id': b['id'], 'program': b['program'],
        'sha256': b['sha256'], 'imageBase': b['imageBase'],
        'counts': {'namespaces': len(ns), 'functions': len(fl), 'labels': 0, 'comments': len(cm)},
        'namespaces': [{'path': n, 'kind': 'class'} for n in ns],
        'functions': [{'address': '0x%08x' % a, 'namespace': n, 'name': nm} for a, n, nm in fl],
        'labels': [],
        'comments': [{'address': '0x%08x' % a, 'type': 'plate', 'text': t} for a, t in cm],
    }


def dump(doc):
    out = ['{', '  "format": "svm.ghidra.annotations/1",', '  "id": "actions",',
           '  "note": %s,' % json.dumps(doc['note']), '  "programs": [']
    secs = []
    for s in doc['programs']:
        lines = ['    {']
        for k in ('format', 'id', 'program', 'sha256', 'imageBase'):
            lines.append('      %s: %s,' % (json.dumps(k), json.dumps(s[k])))
        lines.append('      "counts": %s,' % json.dumps(s['counts']))
        keys = ('namespaces', 'functions', 'labels', 'comments')
        for i, k in enumerate(keys):
            last = i == len(keys) - 1
            items = s[k]
            if not items:
                lines.append('      %s: []%s' % (json.dumps(k), '' if last else ','))
                continue
            lines.append('      %s: [' % json.dumps(k))
            lines.append(',\n'.join('        ' + json.dumps(x) for x in items))
            lines.append('      ]' + ('' if last else ','))
        lines.append('    }')
        secs.append('\n'.join(lines))
    out.append(',\n'.join(secs))
    out.append('  ]')
    out.append('}')
    return '\n'.join(out) + '\n'


def main():
    doc = {'note': 'Per-program sections in the svm.ghidra.annotations/1 layout; merge each into '
                   'annotations/<program id>.json.',
           'programs': [section('editor', names_table.EDITOR, names_table.COMMENTS['editor']),
                        section('game', names_table.GAME, names_table.COMMENTS['game'])]}
    text = dump(doc)
    json.loads(text)
    with open(os.path.join(ANN, 'actions.json'), 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)
    print([(s['id'], s['counts']) for s in doc['programs']])


if __name__ == '__main__':
    main()
