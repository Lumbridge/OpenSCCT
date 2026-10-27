"""Compares the ExecuteAction cases of two builds (editor and game) after cases.py split both.

Plain Python. For every case it extracts the facts the spec records (physics modes passed to
setPhysics, PlayAnim argument tuples, literal field writes, bool dword updates, float literals and
the named natives called) from local/re-out/c/actions/cases/<a>/ and <b>/, normalises the names
that differ between the builds (vtable slot names, data addresses, helper names) and prints the
cases whose facts differ. Output goes to stdout and to local/re-out/c/actions/compare-<a>-<b>.json.

    python re/ghidra/scripts/actions/compare_cases.py editor game
"""

import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..'))
ROOT = os.path.join(REPO, 'local', 're-out', 'c', 'actions')

# SBasePawn vtable slots by byte offset: editor setPhysics 0x10c, PlayAnim 0x134, Crouch 0x158; the game
# build has the same functions 0x10 lower (0xfc, 0x124, 0x148). Slot members are named <target>_<offset>.
SLOT = [
    (re.compile(r'\(\*this->vftable->\w+_(?:10c|fc)\)'), 'setPhysics'),
    (re.compile(r'\(\*\w+->(?:vftable->)?\w+_(?:134|124)\)'), 'PlayAnim'),
    (re.compile(r'\(\*this->vftable->\w+_(?:158|148)\)'), 'Crouch'),
]


def body(path):
    with open(path, encoding='utf-8') as f:
        text = f.read()
    text = re.sub(r'/\*.*?\*/', '', text)           # our annotations
    text = re.sub(r'\b([iufp]Var|bVar|iStack|uStack)\d+\b', 'V', text)   # decompiler temporaries
    text = re.sub(r'\bthis_\d+\b', 'this', text)
    for rx, name in SLOT:
        text = rx.sub(name, text)
    text = re.sub(r'DAT_[0-9a-f]+', 'DAT', text)
    return text


def facts(text):
    out = {}
    out['phys'] = sorted(set(re.findall(r'setPhysics\(this,(\w+),', text)))
    anims = set(re.sub(r'\s+', '', m) for m in re.findall(r'PlayAnim\s*\(\s*(?:\(AActor \*\))?\w+,([^;]*)\);', text))
    out['anim'] = sorted(a for a in anims if not a.startswith(('V,V,V,', 'Channel,V,V,')))   # the shared tail call
    out['set'] = sorted(set(re.findall(r'this->(\w+(?:\)?\.\w+)?) = ((?:-?[0-9][0-9a-fx.e+-]*)|\'[^\']*\');', text)))
    out['bits'] = sorted(set(m for m in re.findall(r'(bits_\w+) = this->\1 ([|&^] 0x[0-9a-f]+(?: \| 0x[0-9a-f]+)?)', text)))
    out['floats'] = sorted(set(f for f in re.findall(r'(?<![\w.])(-?\d+\.\d+(?:e[+-]?\d+)?)(?![\w.])', text) if f not in ('0.0', '1.0')))
    calls = re.findall(r'\b([A-Z][A-Za-z]+(?:::[A-Za-z]+)?)\(', text)
    out['calls'] = sorted(set(c.split('::')[-1] for c in calls if not c.startswith('FUN') and c not in (
        'AActor', 'USkeletalMeshInstance')))
    return out


def main():
    a, b = sys.argv[1], sys.argv[2]
    da, db = os.path.join(ROOT, 'cases', a), os.path.join(ROOT, 'cases', b)
    ia = json.load(open(os.path.join(da, 'index.json')))
    ib = json.load(open(os.path.join(db, 'index.json')))
    ga = {tuple(g['cases']): g['file'] for g in ia['groups']}
    gb = {tuple(g['cases']): g['file'] for g in ib['groups']}
    report = {'only_' + a: [list(k) for k in ga if k not in gb], 'only_' + b: [list(k) for k in gb if k not in ga],
              'diff': {}, 'same': 0}
    for key in sorted(set(ga) & set(gb), key=lambda k: (str(type(k[0])), k)):
        fa, fb = facts(body(os.path.join(da, ga[key]))), facts(body(os.path.join(db, gb[key])))
        d = {k: [fa[k], fb[k]] for k in fa if fa[k] != fb[k] and k != 'calls'}
        if d:
            report['diff'][str(key[0])] = d
        else:
            report['same'] += 1
    for t in ('tails.c',):
        fa, fb = facts(body(os.path.join(da, t))), facts(body(os.path.join(db, t)))
        d = {k: [fa[k], fb[k]] for k in fa if fa[k] != fb[k] and k != 'calls'}
        if d:
            report['diff'][t] = d
    path = os.path.join(ROOT, 'compare-%s-%s.json' % (a, b))
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(report, f, indent=1)
    print('groups', len(ga), len(gb), 'same', report['same'], 'differ', len(report['diff']),
          'only_a', report['only_' + a], 'only_b', report['only_' + b])
    for k, v in report['diff'].items():
        print(k, json.dumps(v)[:600])


if __name__ == '__main__':
    main()
