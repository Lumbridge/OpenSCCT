"""ghx.py <prog> <script relative to repo> '<json globals>': runs a server script with inputs set."""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ghq import ask  # noqa: E402

prog, script = sys.argv[1], sys.argv[2]
inputs = json.loads(sys.argv[3]) if len(sys.argv) > 3 else {}
with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'run_with.py'), encoding='utf-8') as f:
    body = f.read()
code = 'WITH = %r\nSCRIPT = %r\n' % (inputs, script) + body
res = ask({'op': 'exec', 'code': code, 'prog': prog})
if not res.get('ok'):
    print(res.get('error'))
    sys.exit(1)
sys.stdout.write(res.get('out', ''))
