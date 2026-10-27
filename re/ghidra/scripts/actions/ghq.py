"""Client for ghserve.py: sends one JSON request and prints the reply.

ghq.py '{"op": "decomp", "prog": "editor", "addr": "0x112025e0"}'
ghq.py --exec file.py [prog]      runs a local Python file inside the server (globals: p, progs, ...)
"""

import json
import os
import socket
import sys

PORT = int(os.environ.get('SVM_GHQ_PORT', '47331'))


def ask(req):
    with socket.create_connection(('127.0.0.1', PORT)) as s:
        s.sendall((json.dumps(req) + '\n').encode('utf-8'))
        data = b''
        while not data.endswith(b'\n'):
            chunk = s.recv(1 << 20)
            if not chunk:
                break
            data += chunk
    return json.loads(data)


if __name__ == '__main__':
    if sys.argv[1] == '--exec':
        with open(sys.argv[2], encoding='utf-8') as f:
            code = f.read()
        req = {'op': 'exec', 'code': code, 'prog': sys.argv[3] if len(sys.argv) > 3 else 'editor'}
    else:
        req = json.loads(sys.argv[1])
    res = ask(req)
    if not res.get('ok'):
        print(res.get('error'))
        sys.exit(1)
    if 'out' in res:
        sys.stdout.write(res['out'])
    else:
        print(json.dumps(res))
