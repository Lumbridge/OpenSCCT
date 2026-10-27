"""Client for the natives2 ghserve.py.

ghq.py '{"op": "decomp", "prog": "game", "addr": "0x10a8fc40"}'
ghq.py --exec file.py [prog] ['<json globals>']   runs a Python file inside the server
ghq.py --decomp prog addr [addr ...]              decompiles each one (files under local/re-out/c/natives2/<prog>/)
"""

import json
import os
import socket
import sys

PORT = int(os.environ.get('SVM_GHQ_PORT', '47351'))


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


def main(argv):
    if argv[1] == '--decomp':
        prog = argv[2]
        for spec in argv[3:]:
            res = ask({'op': 'decomp', 'prog': prog, 'addr': spec, 'timeout': 600})
            if not res.get('ok'):
                print(spec, 'ERROR', res.get('error', '').strip().splitlines()[-1])
                continue
            print(res['entry'], res['name'], res['lines'], os.path.relpath(res['file']))
        return 0
    if argv[1] == '--exec':
        with open(argv[2], encoding='utf-8') as f:
            code = f.read()
        if len(argv) > 4:
            code = 'WITH = %r\nglobals().update(WITH)\n' % json.loads(argv[4]) + code
        req = {'op': 'exec', 'code': code, 'prog': argv[3] if len(argv) > 3 else 'editor'}
    else:
        req = json.loads(argv[1])
    res = ask(req)
    if not res.get('ok'):
        print(res.get('error'))
        return 1
    if 'out' in res:
        sys.stdout.write(res['out'])
    else:
        print(json.dumps(res))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
