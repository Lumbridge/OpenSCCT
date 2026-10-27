"""Long-running PyGhidra query server for the native networking study.

Opens the private project copy local/ghidra-work/net (both programs) once and answers JSON requests
on a localhost TCP port, so each question costs one decompile instead of one JVM start. Anything that
holds game-derived text (decompiled C, disassembly, string dumps) is written only under
local/re-out/c/net/, which is gitignored. Replies carry file paths and counts, not code.

Run:  re/.venv-ghidra/Scripts/python.exe re/ghidra/scripts/net/netserve.py [project_dir]
Ask:  re/.venv-ghidra/Scripts/python.exe re/ghidra/scripts/net/netq.py '{"op": "decomp", "prog": "editor", "addr": "0x11142700"}'

Ops:
  decomp   {prog, addr | name}            decompile one function to OUT/<prog>/<entry>_<name>.c
  many     {prog, items: [addr|name]}     decompile several, one file each
  exec     {prog, code}                   run Python inside the server (globals: p, progs, decomp, ...)
  rename   {prog, items: [[addr, ns, name]]}  name functions in the private copy (creates them if needed)
  save     {}                             save changed programs in the private copy
"""

import io
import json
import os
import socketserver
import sys
import threading
import traceback
from contextlib import redirect_stdout

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..'))
GHIDRA = os.environ.get('GHIDRA_INSTALL_DIR', os.path.expanduser(r'~\Desktop\ghidra_12.2_PUBLIC'))
PROJECT_DIR = sys.argv[1] if len(sys.argv) > 1 else os.path.join(REPO, 'local', 'ghidra-work', 'net')
OUT = os.path.join(REPO, 'local', 're-out', 'c', 'net')
PORT = int(os.environ.get('SVM_NETQ_PORT', '47331'))
PROGRAMS = {'editor': '/ChaosTheory_Editor.exe', 'game': '/SCCT Versus'}

import pyghidra  # noqa: E402

launcher = pyghidra.HeadlessPyGhidraLauncher(install_dir=GHIDRA)
launcher.add_vmargs('-Xmx' + os.environ.get('MAXMEM', '3G'))
launcher.start()

from ghidra.app.decompiler import DecompInterface, DecompileOptions  # noqa: E402
from ghidra.program.model.symbol import SourceType  # noqa: E402
from ghidra.util.task import ConsoleTaskMonitor  # noqa: E402
from java.lang import Object  # noqa: E402

project = pyghidra.open_project(PROJECT_DIR, 'SVM')
consumer = Object()
progs = {}
decomps = {}
lock = threading.Lock()
monitor = ConsoleTaskMonitor()

sys.path.insert(0, os.path.join(REPO, 're', 'tools'))
import binlock  # noqa: E402

LOCK = binlock.load_lock()

for key, path in PROGRAMS.items():
    df = project.getProjectData().getFile(path)
    prog = df.getDomainObject(consumer, True, False, monitor)
    sha = str(prog.getExecutableSHA256() or '')
    if binlock.find_by_sha256(LOCK, sha) is None:
        raise SystemExit('%s has sha256 %s, which is not in re/binaries.lock.json; refusing to run' % (path, sha))
    progs[key] = prog
    d = DecompInterface()
    d.setOptions(DecompileOptions())
    d.openProgram(prog)
    decomps[key] = d


def addr(prog, a):
    if isinstance(a, str):
        a = int(a, 16)
    return prog.getAddressFactory().getDefaultAddressSpace().getAddress(a)


def find_func(prog, spec, create=True):
    fm = prog.getFunctionManager()
    if isinstance(spec, str) and not spec.lower().startswith('0x'):
        for f in fm.getFunctions(True):
            full = f.getName(True)
            if full == spec or full.endswith('::' + spec):
                return f
        raise KeyError('no function named %s' % spec)
    a = addr(prog, spec)
    f = fm.getFunctionAt(a)
    if f is None and create:
        from ghidra.app.cmd.function import CreateFunctionCmd
        tx = prog.startTransaction('net: create function')
        try:
            CreateFunctionCmd(a).applyTo(prog, monitor)
        finally:
            prog.endTransaction(tx, True)
        f = fm.getFunctionAt(a)
    if f is None:
        f = fm.getFunctionContaining(a)
    if f is None:
        raise KeyError('no function at %s' % spec)
    return f


def decomp(key, spec, timeout=180):
    f = find_func(progs[key], spec)
    res = decomps[key].decompileFunction(f, timeout, monitor)
    if not res.decompileCompleted():
        return f, 'DECOMPILE FAILED: ' + str(res.getErrorMessage())
    return f, res.getDecompiledFunction().getC()


def safe(name):
    return ''.join(c if c.isalnum() or c in '_-' else '_' for c in name)


def write_decomp(key, spec, timeout=180):
    f, c = decomp(key, spec, timeout)
    name = f.getName(True)
    fn = os.path.join(OUT, key, '%s_%s.c' % (f.getEntryPoint(), safe(name)))
    os.makedirs(os.path.dirname(fn), exist_ok=True)
    with open(fn, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write('// local only: %s %s %s\n' % (key, f.getEntryPoint(), name))
        fh.write(c)
    return {'file': fn, 'name': name, 'entry': str(f.getEntryPoint()), 'lines': c.count('\n')}


def get_ns(prog, path):
    st = prog.getSymbolTable()
    ns = prog.getGlobalNamespace()
    if not path:
        return ns
    for part in path.split('::'):
        nxt = st.getNamespace(part, ns)
        if nxt is None:
            nxt = st.createClass(ns, part, SourceType.USER_DEFINED)
        ns = nxt
    return ns


def rename(key, items):
    prog = progs[key]
    done = []
    tx = prog.startTransaction('net: names')
    try:
        for a, ns, name in items:
            f = find_func(prog, a)
            f.setParentNamespace(get_ns(prog, ns))
            f.setName(name, SourceType.USER_DEFINED)
            done.append([str(f.getEntryPoint()), f.getName(True)])
    finally:
        prog.endTransaction(tx, True)
    return done


def handle(req):
    op = req['op']
    key = req.get('prog', 'editor')
    prog = progs.get(key)
    if op == 'decomp':
        return write_decomp(key, req.get('addr') or req.get('name'), req.get('timeout', 180))
    if op == 'many':
        out = []
        for spec in req['items']:
            try:
                out.append(write_decomp(key, spec, req.get('timeout', 180)))
            except Exception as e:  # noqa: BLE001
                out.append({'spec': spec, 'error': str(e)})
        return {'items': out}
    if op == 'rename':
        return {'done': rename(key, req['items'])}
    if op == 'exec':
        buf = io.StringIO()
        g = {'progs': progs, 'p': prog, 'decomps': decomps, 'addr': addr, 'find_func': find_func,
             'decomp': decomp, 'write_decomp': write_decomp, 'monitor': monitor, 'OUT': OUT,
             'rename': rename, 'get_ns': get_ns}
        with redirect_stdout(buf):
            exec(req['code'], g)
        return {'out': buf.getvalue()}
    if op == 'save':
        for k, pr in progs.items():
            if pr.isChanged():
                pr.save('net study names', monitor)
        return {'saved': True}
    raise ValueError('unknown op %s' % op)


class Handler(socketserver.StreamRequestHandler):
    def handle(self):
        line = self.rfile.readline()
        try:
            req = json.loads(line)
            with lock:
                res = handle(req)
            res['ok'] = True
        except Exception:  # noqa: BLE001
            res = {'ok': False, 'error': traceback.format_exc()}
        self.wfile.write((json.dumps(res) + '\n').encode('utf-8'))


class Server(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True


print('netserve ready on port %d' % PORT, flush=True)
with Server(('127.0.0.1', PORT), Handler) as srv:
    srv.serve_forever()
