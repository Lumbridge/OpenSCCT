"""Lists the bool properties sharing the dword of a 'bits_<FirstBool>' field (reflection dump).

bits.py <Class> <FirstBool> [mask ...]    e.g. bits.py SBasePawn bInitSlowReplication 0x800000
"""

import json
import os
import sys

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..', '..'))
game = '--game' in sys.argv
args = [a for a in sys.argv[1:] if not a.startswith('--')]
dump = json.load(open(os.path.join(REPO, 'local', 'reflect', ('game' if game else 'editor') + '.json'), encoding='utf-8'))
classes = {c['name']: c for c in dump['classes']}
chain = []
c = classes.get(args[0])
while c:
    chain.append(c['path'])
    c = classes.get(c['super'].split('.')[-1]) if c['super'] else None
props = [pr for pr in dump['properties'] if pr['owner'] in chain]
first = [pr for pr in props if pr['name'].lower() == args[1].lower()]
if not first:
    sys.exit('no field ' + args[1])
off = first[0]['offset']
masks = [int(m, 0) for m in args[2:]]
for pr in sorted((pr for pr in props if pr['offset'] == off and pr['kind'] == 'BoolProperty'),
                 key=lambda pr: int(pr['bitMask'], 16)):
    m = int(pr['bitMask'], 16)
    if masks and not any(m & x for x in masks):
        continue
    print('%#06x %#010x %s.%s' % (off, m, pr['owner'].split('.')[-1], pr['name']))
