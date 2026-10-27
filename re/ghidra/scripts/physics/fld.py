"""Field lookup in a reflection dump: which property sits at a byte offset of a class.

fld.py <Class> <offset> [mask] [--game]     e.g. fld.py SBasePawn 0x4a8 0x10000
fld.py <Class> <name>                       offset of a field by name
"""

import json
import os
import sys

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..', '..'))
args = [a for a in sys.argv[1:] if not a.startswith('--')]
dump = json.load(open(os.path.join(REPO, 'local', 'reflect', ('game' if '--game' in sys.argv else 'editor') + '.json'),
                      encoding='utf-8'))
classes = {c['name']: c for c in dump['classes']}
props = {}
for pr in dump['properties']:
    props.setdefault(pr['owner'], []).append(pr)


def chain(name):
    out = []
    c = classes.get(name)
    while c:
        out.append(c['path'])
        sup = c['super']
        c = classes.get(sup.split('.')[-1]) if sup else None
    return out


cls = args[0]
allp = [pr for owner in chain(cls) for pr in props.get(owner, [])]
key = args[1]
if key.lower().startswith('0x') or key.isdigit():
    off = int(key, 0)
    mask = int(args[2], 0) if len(args) > 2 else None
    best = []
    for pr in allp:
        o = pr['offset']
        size = pr['elementSize'] * pr['arrayDim']
        if o <= off < o + size:
            if mask is not None and pr['kind'] == 'BoolProperty' and int(pr['bitMask'], 16) & mask == 0:
                continue
            best.append(pr)
    for pr in best:
        extra = ''
        if pr['arrayDim'] > 1 or pr['kind'] == 'StructProperty':
            extra = ' +%d' % (off - pr['offset'])
        print('%s %s off=%#x %s%s %s' % (pr['owner'], pr['name'], pr['offset'], pr['kind'],
                                         extra, pr.get('bitMask', pr.get('struct', ''))))
    if not best:
        print('nothing at %#x in %s' % (off, cls))
else:
    for pr in allp:
        if pr['name'].lower() == key.lower():
            print('%s %s off=%#x %s %s' % (pr['owner'], pr['name'], pr['offset'], pr['kind'], pr.get('bitMask', '')))
