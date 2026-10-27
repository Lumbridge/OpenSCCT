"""Lists user-defined function names in the private copy that are not in the tracked
annotations of the program yet (one JSON object per line: address, namespace, name).
Globals: SKIP_PREFIXES (names not to export), OUTFILE (path under local/)."""

import json
import os

REPO = os.path.abspath(os.path.join(os.path.dirname(OUT), '..', '..', '..'))  # noqa: F821
prog_id = 'editor' if 'Editor' in p.getName() else 'game'  # noqa: F821
known = set()
for fn in ('%s.json' % prog_id, 'net.json'):
    path = os.path.join(REPO, 're', 'ghidra', 'annotations', fn)
    if os.path.isfile(path):
        d = json.load(open(path, encoding='utf-8'))
        secs = d.get('programs', [d])
        for s in secs:
            if s.get('id') == prog_id:
                for f in s.get('functions', []):
                    known.add(int(f['address'], 16))
from ghidra.program.model.symbol import SourceType
out = []
for f in p.getFunctionManager().getFunctions(True):  # noqa: F821
    sym = f.getSymbol()
    if sym.getSource() != SourceType.USER_DEFINED:
        continue
    a = f.getEntryPoint().getOffset()
    if a in known:
        continue
    nm = f.getName()
    if any(nm.startswith(x) for x in SKIP_PREFIXES):  # noqa: F821
        continue
    ns = f.getParentNamespace()
    nsp = '' if ns is None or ns.isGlobal() else ns.getName(True)
    out.append({'address': '0x%08x' % a, 'namespace': nsp, 'name': nm})
with open(OUTFILE, 'w', encoding='utf-8') as fh:  # noqa: F821
    json.dump(out, fh, indent=0)
print(len(out))
