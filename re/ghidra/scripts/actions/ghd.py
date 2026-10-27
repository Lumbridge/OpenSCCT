"""Decompiles several functions through ghserve.py (actions study) and prints where each one went.

ghd.py <prog> <addr-or-name> [...]     output files land in local/re-out/c/actions/<prog>/
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ghq import ask  # noqa: E402

prog = sys.argv[1]
for spec in sys.argv[2:]:
    res = ask({'op': 'decomp', 'prog': prog, 'addr': spec, 'timeout': 600})
    if not res.get('ok'):
        print(spec, 'ERROR', res.get('error', '').strip().splitlines()[-1])
        continue
    print(res['entry'], res['name'], res['lines'], os.path.relpath(res['file']))
