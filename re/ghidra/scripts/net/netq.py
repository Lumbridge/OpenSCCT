"""Client for netserve.py: sends one JSON request and prints the reply.

netq.py '{"op": "decomp", "prog": "editor", "addr": "0x11142700"}'
netq.py --exec file.py [prog]      runs a local Python file inside the server
"""

import json
import os
import socket
import sys

PORT = int(os.environ.get('SVM_NETQ_PORT', '47331'))


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
    elif sys.argv[1] == '--file':
        with open(sys.argv[2], encoding='utf-8') as f:
            req = json.load(f)
    else:
        req = json.loads(sys.argv[1])
    res = ask(req)
    if not res.get('ok'):
        print(res.get('error'))
        sys.exit(1)
    if 'out' in res:
        sys.stdout.write(res['out'])
    else:
        print(json.dumps(res, indent=1))
