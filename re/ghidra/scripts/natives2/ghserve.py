"""Long-running PyGhidra query server for the natives2 study (natives the ports still mark Estimated).

Opens the private copy local/ghidra-work/natives2 (both programs) once and answers JSON requests on a
localhost TCP port. Everything it writes that holds game-derived text (decompiled C, disassembly,
cross-reference lists) goes only under local/re-out/c/natives2/.

Run:  re/.venv-ghidra/Scripts/python.exe re/ghidra/scripts/natives2/ghserve.py [project_dir]
Ask:  re/.venv-ghidra/Scripts/python.exe re/ghidra/scripts/natives2/ghq.py '{"op": "decomp", ...}'
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
PROJECT_DIR = sys.argv[1] if len(sys.argv) > 1 else os.path.join(REPO, 'local', 'ghidra-work', 'natives2')
OUT = os.path.join(REPO, 'local', 're-out', 'c', 'natives2')
PORT = int(os.environ.get('SVM_GHQ_PORT', '47351'))
PROGRAMS = {'editor': '/ChaosTheory_Editor.exe', 'game': '/SCCT Versus'}

if os.path.normcase(os.path.abspath(PROJECT_DIR)) == os.path.normcase(os.path.join(REPO, 'local', 'ghidra')):
    raise SystemExit('refusing to open the shared project')

import pyghidra  # noqa: E402

launcher = pyghidra.HeadlessPyGhidraLauncher(install_dir=GHIDRA)
launcher.add_vmargs('-Xmx' + os.environ.get('MAXMEM', '3G'))
launcher.start()

from ghidra.app.decompiler import DecompInterface, DecompileOptions  # noqa: E402
from ghidra.util.task import ConsoleTaskMonitor  # noqa: E402
from java.lang import Object  # noqa: E402

project = pyghidra.open_project(PROJECT_DIR, 'SVM')
consumer = Object()
progs = {}
decomps = {}
lock = threading.Lock()
monitor = ConsoleTaskMonitor()

def open_into(proj, key, path):
    df = proj.getProjectData().getFile(path)
    p = df.getDomainObject(consumer, True, False, monitor)
    progs[key] = p
    d = DecompInterface()
    d.setOptions(DecompileOptions())
    d.openProgram(p)
    decomps[key] = d


for key, path in PROGRAMS.items():
    open_into(project, key, path)

# Optional: a copy of the Reloaded.Core project next to the SVM one (local/ghidra-work/natives2/reloaded).
RELOADED_DIR = os.path.join(PROJECT_DIR, 'reloaded')
if os.path.isfile(os.path.join(RELOADED_DIR, 'Reloaded.gpr')):
    rproject = pyghidra.open_project(RELOADED_DIR, 'Reloaded')
    open_into(rproject, 'reloaded', '/Reloaded.Core.dll')


def addr(p, a):
    if isinstance(a, str):
        a = int(a, 16)
    return p.getAddressFactory().getDefaultAddressSpace().getAddress(a)


def find_func(p, spec):
    fm = p.getFunctionManager()
    if isinstance(spec, str) and not spec.lower().startswith('0x'):
        for f in fm.getFunctions(True):
            full = f.getName(True)
            if full == spec or f.getName() == spec or full.endswith('::' + spec):
                return f
        raise KeyError('no function named %s' % spec)
    a = addr(p, spec)
    f = fm.getFunctionAt(a) or fm.getFunctionContaining(a)
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
    return ''.join(c if c.isalnum() or c in '_-' else '_' for c in name)[:80]


def write_local(key, stem, text):
    fn = os.path.join(OUT, key, stem)
    os.makedirs(os.path.dirname(fn), exist_ok=True)
    with open(fn, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write(text)
    return fn


def handle(req):
    op = req['op']
    key = req.get('prog', 'editor')
    p = progs.get(key)
    if op == 'decomp':
        f, c = decomp(key, req['addr'], req.get('timeout', 180))
        name = f.getName(True)
        fn = write_local(key, '%s_%s.c' % (f.getEntryPoint(), safe(name)),
                         '// natives2 ghserve decompile (local only) %s %s %s\n%s' % (key, f.getEntryPoint(), name, c))
        return {'file': fn, 'name': name, 'entry': str(f.getEntryPoint()), 'lines': c.count('\n')}
    if op == 'exec':
        buf = io.StringIO()
        g = {'progs': progs, 'p': p, 'decomps': decomps, 'addr': addr, 'find_func': find_func,
             'decomp': decomp, 'monitor': monitor, 'OUT': OUT, 'write_local': write_local, 'key': key}
        with redirect_stdout(buf):
            exec(req['code'], g)
        return {'out': buf.getvalue()}
    if op == 'save':
        for pr in progs.values():
            if pr.isChanged():
                pr.save('natives2 study names', monitor)
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


print('natives2 ghserve ready on port %d' % PORT, flush=True)
with Server(('127.0.0.1', PORT), Handler) as srv:
    srv.serve_forever()
