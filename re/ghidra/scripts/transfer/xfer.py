"""Shared loading and feature code for the editor -> game name transfer (plain Python 3.10).

The inputs are local only (they hold instruction tokens and data taken from the binaries):

* local/re-out/c/transfer/<id>.features.json   from T10_export_features.py
* local/re-out/c/transfer/<id>.vectors.json    BSim feature vectors, from T20_bsim_sigs.py
* local/re-out/c/transfer/game.nearest.json    BSim nearest editor functions per game function
* local/re-out/native-anchors.json             native index pairs (game and editor exec thunks)
* the PE import tables of both executables, read from the reference install (read only)

Nothing here imports Ghidra. numpy and scipy are optional: without them the global candidate
search falls back to an inverted index.
"""

import collections
import json
import math
import os
import re
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..'))
TRANSFER_OUT = os.path.join(REPO, 'local', 're-out', 'c', 'transfer')
WORK = os.path.join(REPO, 'local', 'ghidra-work', 'transfer', 'match')
ANNOTATIONS = os.path.join(REPO, 're', 'ghidra', 'annotations')
RE_TOOLS = os.path.join(REPO, 're', 'tools')

GP_REGS = frozenset(('EAX', 'EBX', 'ECX', 'EDX', 'ESI', 'EDI'))
HEX_RE = re.compile(r'^[0-9a-f]+$')
TDB_FLOOR = 0xffdff000          # Ghidra's thread data block (FS segment) addresses
HUB_CALLERS = 120                # callees with more callers than this are never treated as inlined

# Feature kinds and their weight multipliers. S strings, I imports, F float constants, K other
# immediates, O field offsets of [reg+disp] operands (class layouts match between the builds),
# R other read-only constants.
KIND_WEIGHT = {'S': 1.6, 'I': 1.3, 'F': 1.2, 'K': 0.7, 'O': 1.0, 'R': 0.8}


def floatish(v):
    """Text of v read as a float32 when it looks like a real constant (sane exponent), else None."""
    if v == 0 or v == 0x80000000:
        return None
    ex = (v >> 23) & 0xff
    if 100 <= ex <= 160:
        f = struct.unpack('<f', struct.pack('<I', v))[0]
        return '%.6g' % f
    return None


def pe_imports(path):
    """{IAT slot address: (dll!name, file value of the slot)} of a PE32 image."""
    with open(path, 'rb') as fh:
        b = fh.read()
    pe = struct.unpack_from('<I', b, 0x3c)[0]
    opt = pe + 24
    base = struct.unpack_from('<I', b, opt + 28)[0]
    nsec = struct.unpack_from('<H', b, pe + 6)[0]
    sh = opt + struct.unpack_from('<H', b, pe + 20)[0]
    secs = [struct.unpack_from('<IIII', b, sh + i * 40 + 8) for i in range(nsec)]

    def off(rva):
        for vs, va, rs, ra in secs:
            if va <= rva < va + max(vs, rs):
                return rva - va + ra
        return None

    irva = struct.unpack_from('<I', b, opt + 96 + 8)[0]
    o = off(irva)
    out = {}
    while o is not None:
        oft, _, _, nm, ft = struct.unpack_from('<IIIII', b, o)
        if nm == 0:
            break
        dll = b[off(nm):b.index(b'\0', off(nm))].decode('latin-1').lower()
        t = oft or ft
        k = 0
        while True:
            v = struct.unpack_from('<I', b, off(t) + 4 * k)[0]
            if v == 0:
                break
            if v & 0x80000000:
                name = '%s!#%d' % (dll, v & 0xffff)
            else:
                p = off(v) + 2
                name = dll + '!' + b[p:b.index(b'\0', p)].decode('latin-1')
            fv = struct.unpack_from('<I', b, off(ft) + 4 * k)[0]
            out[base + ft + 4 * k] = (name, fv)
            k += 1
        o += 20
    return out


_WIN32_DLLS = frozenset(('kernel32.dll', 'user32.dll', 'gdi32.dll', 'advapi32.dll', 'shell32.dll',
                         'comdlg32.dll', 'comctl32.dll', 'winmm.dll', 'imm32.dll', 'ole32.dll'))
_CRT_WIDE = {'_wtoi': 'atoi', '_wtol': 'atol', '_wtof': 'atof', 'swprintf': 'sprintf', 'vswprintf': 'vsprintf',
             '_snwprintf': '_snprintf', '_vsnwprintf': '_vsnprintf', 'fwprintf': 'fprintf', '_wfopen': 'fopen',
             'towupper': 'toupper', 'towlower': 'tolower', 'iswspace': 'isspace', 'iswdigit': 'isdigit',
             'iswalpha': 'isalpha', 'iswalnum': 'isalnum', '_wgetcwd': '_getcwd', '_wchdir': '_chdir',
             '_wmkdir': '_mkdir', '_wremove': 'remove', '_wrename': 'rename', '_wsystem': 'system',
             '_wfindfirst': '_findfirst', '_wfindnext': '_findnext', '_wstat': '_stat', '_wunlink': '_unlink',
             '_waccess': '_access', '_wsplitpath': '_splitpath', '_wcsicmp': '_stricmp', '_wcsnicmp': '_strnicmp',
             'wcsstr': 'strstr', 'wcschr': 'strchr', 'wcsrchr': 'strrchr', 'wcslen': 'strlen', 'wcscpy': 'strcpy',
             'wcsncpy': 'strncpy', 'wcscmp': 'strcmp', 'wcsncmp': 'strncmp', 'wcscat': 'strcat', 'wcstod': 'strtod',
             'wcstol': 'strtol', 'wcstoul': 'strtoul', '_wcsupr': '_strupr', '_wcslwr': '_strlwr', 'fgetws': 'fgets',
             'fputws': 'fputs', '_wgetenv': 'getenv', '_wputenv': '_putenv', '_itow': '_itoa', '_ltow': '_ltoa'}


class PeImage:
    """Read-only view of a PE32 file by virtual address."""

    def __init__(self, path):
        with open(path, 'rb') as fh:
            self.b = fh.read()
        b = self.b
        pe = struct.unpack_from('<I', b, 0x3c)[0]
        opt = pe + 24
        self.base = struct.unpack_from('<I', b, opt + 28)[0]
        nsec = struct.unpack_from('<H', b, pe + 6)[0]
        sh = opt + struct.unpack_from('<H', b, pe + 20)[0]
        self.secs = [struct.unpack_from('<IIII', b, sh + i * 40 + 8) for i in range(nsec)]

    def offset(self, va):
        rva = va - self.base
        for vs, sva, rs, ra in self.secs:
            if sva <= rva < sva + min(vs, rs):
                return rva - sva + ra
        return None

    def jump_target(self, va):
        """Destination of a 'jmp rel32' at va, or None."""
        o = self.offset(va)
        if o is None or o + 5 > len(self.b) or self.b[o] != 0xE9:
            return None
        return (va + 5 + struct.unpack_from('<i', self.b, o + 1)[0]) & 0xffffffff


def _normalise_import(name):
    """dll!function with the ANSI/wide differences between the builds folded away: the editor is an
    8-bit build and the game a UTF-16 one, and the game links WS2_32 where the editor links WSOCK32."""
    dll, _, fn = name.partition('!')
    if dll in ('ws2_32.dll', 'wsock32.dll'):
        dll = 'winsock'
    elif dll in _WIN32_DLLS and len(fn) > 2 and fn[-1] in 'AW' and fn[-2].islower():
        fn = fn[:-1]
    elif dll == 'msvcr70.dll':
        fn = _CRT_WIDE.get(fn, fn)
    return dll + '!' + fn


class Program:
    """One executable's functions, call graph, static features and BSim vectors."""

    def __init__(self, pid, features, vectors, imports, image=None):
        self.id = pid
        self.image = image
        self.base = features['base']
        blocks = [b for b in features['blocks'] if b[0] != 'tdb']
        self.lo = min(b[1] for b in blocks)
        self.hi = max(b[2] for b in blocks)
        self.code = [(b[1], b[2]) for b in blocks if b[3]]
        self.funcs = {}
        for f in features['functions']:
            self.funcs[f['a']] = f
        # Vtable slots of the editor point at incremental-link jump stubs; resolve them.
        self.vtables = [{'a': v['a'], 'slots': [self.resolve_thunk(s) for s in v['slots']]}
                        for v in features.get('vtables', [])]
        self.iat_by_slot = {slot: _normalise_import(n) for slot, (n, _) in imports.items()}
        self.iat_by_value = {fv: _normalise_import(n) for _, (n, fv) in imports.items()}
        self.callees = {}
        self.callers = collections.defaultdict(set)
        for a, f in self.funcs.items():
            seen = []
            for c in f['calls']:
                if c != a and c in self.funcs and c not in seen:
                    seen.append(c)
            self.callees[a] = seen
            for c in seen:
                self.callers[c].add(a)
        self.vec = {}
        for k, v in vectors.items():
            ln = v['len'] or 1.0
            self.vec[int(k, 16)] = {h: c / ln for h, _, c in v['h']}
        self.static = {}
        self.drefs = {}
        self.imports = {}
        for a, f in self.funcs.items():
            self.static[a], self.drefs[a], self.imports[a] = self._features(f)
        self.names = {a: f['name'] for a, f in self.funcs.items() if f.get('name') and f['src'] == 'user'}
        # Exported symbols (the editor exports the sound engine's SND_ API): undecorated stdcall names.
        for a, f in self.funcs.items():
            if f.get('name') and f['src'] == 'imported' and f['name'] != 'entry' and a not in self.names:
                n = f['name']
                if n.startswith('_') and '@' in n:
                    n = n[1:n.rindex('@')]
                self.names[a] = n
        self._reach = {}

    def resolve_thunk(self, a):
        """The function a jump stub (or a chain of them) leads to; a itself otherwise. Stubs that
        Ghidra did not make functions are decoded from the image."""
        for _ in range(8):
            f = self.funcs.get(a)
            if f is not None:
                if not f['thunk'] or not f['calls']:
                    return a
                a = f['calls'][0]
                continue
            t = self.image.jump_target(a) if self.image is not None else None
            if t is None:
                return a
            a = t
        return a

    def in_image(self, v):
        return self.lo <= v < self.hi

    def _features(self, f):
        feats = collections.Counter()
        drefs = []
        imps = set()
        for s in f.get('str', ()):
            feats['S:' + s] += 1
        for c in f.get('c', ()):
            if c.startswith('i'):
                v = int(c[1:], 16)
                if v >= 0xffff0000:
                    continue
                fl = floatish(v)
                feats[('F:' + fl) if fl else ('K:%x' % v)] += 1
            else:
                v = int(c, 16)
                if v in self.iat_by_value:
                    imps.add(self.iat_by_value[v])
                    continue
                fl = floatish(v)
                if fl:
                    feats['F:' + fl] += 1
                elif v:
                    feats['R:%x' % v] += 1
        for d in f.get('d', ()):
            if d in self.iat_by_slot:
                imps.add(self.iat_by_slot[d])
            elif not self.in_image(d):
                if d >= TDB_FLOOR:
                    continue
                fl = floatish(d)
                feats[('F:' + fl) if fl else ('K:%x' % d)] += 1
            else:
                drefs.append(d)
        for imp in imps:
            feats['I:' + imp] += 1
        for line in f['t'].split('\n'):
            sp = line.split(' ', 1)
            if len(sp) == 1:
                continue
            mn = sp[0]
            ops = sp[1].split(';')
            for op in ops:
                parts = op.split(',')
                if len(parts) >= 2 and parts[0] in GP_REGS and HEX_RE.match(parts[1]):
                    feats['O:' + parts[1]] += 1
                elif len(parts) == 1 and HEX_RE.match(parts[0]):
                    if mn in ('RET', 'ENTER') or (mn in ('ADD', 'SUB') and ops[0] == 'ESP'):
                        continue
                    v = int(parts[0], 16)
                    if v > 1:
                        feats['K:%x' % v] += 1
        return feats, drefs, imps

    def reach(self, a, direction, depth, cap=1500):
        """Functions reachable from a within depth call edges (callees or callers), not a itself.
        Hub functions are not expanded through."""
        key = (a, direction, depth)
        r = self._reach.get(key)
        if r is not None:
            return r
        nxt = self.callees if direction == 'out' else self.callers
        seen = set()
        frontier = [a]
        for _ in range(depth):
            new = []
            for x in frontier:
                if x != a and len(self.callers.get(x, ())) > HUB_CALLERS:
                    continue
                for y in nxt.get(x, ()):
                    if y not in seen and y != a:
                        seen.add(y)
                        new.append(y)
            frontier = new
            if len(seen) > cap or not frontier:
                break
        r = frozenset(seen)
        self._reach[key] = r
        return r

    def is_hub(self, a):
        return len(self.callers.get(a, ())) > HUB_CALLERS


def load_json(path):
    with open(path, encoding='utf-8') as fh:
        return json.load(fh)


def load_programs(install=None):
    """(game, editor) Program objects from the exported features and the reference install."""
    if RE_TOOLS not in sys.path:
        sys.path.insert(0, RE_TOOLS)
    import binlock  # noqa: E402
    lock = binlock.load_lock()
    progs = []
    for pid in ('game', 'editor'):
        exe = binlock.locked_path(pid, install, lock)
        imports = pe_imports(exe)
        feats = load_json(os.path.join(TRANSFER_OUT, '%s.features.json' % pid))
        vecs = load_json(os.path.join(TRANSFER_OUT, '%s.vectors.json' % pid))['vectors']
        progs.append(Program(pid, feats, vecs, imports, PeImage(exe)))
    return progs[0], progs[1]


def load_nearest():
    path = os.path.join(TRANSFER_OUT, 'game.nearest.json')
    data = load_json(path)['nearest']
    return {int(k, 16): [(int(e, 16), s) for e, s, _ in v] for k, v in data.items()}


def load_native_pairs():
    path = os.path.join(REPO, 'local', 're-out', 'native-anchors.json')
    out = []
    for p in load_json(path)['pairs']:
        out.append((int(p['game'], 16), int(p['editor'], 16), p['index'], p.get('name')))
    return out


def load_reflection_pairs():
    """(constructor pairs, vtable pairs) of the native classes both reflection dumps know
    (local/reflect/{game,editor}.json): [(game address, editor address, class path)]."""
    out_c, out_v = [], []
    root = os.path.join(REPO, 'local', 'reflect')
    try:
        g = load_json(os.path.join(root, 'game.json'))
        e = load_json(os.path.join(root, 'editor.json'))
    except OSError:
        return out_c, out_v
    ec = {c['path']: c for c in e['classes']}
    for c in g['classes']:
        d = ec.get(c['path'])
        if d is None:
            continue
        if c.get('constructor') and d.get('constructor'):
            out_c.append((int(c['constructor'], 16), int(d['constructor'], 16), c['path']))
        if c.get('vtable') and d.get('vtable'):
            out_v.append((int(c['vtable'], 16), int(d['vtable'], 16), c['path']))
    return out_c, out_v


def annotation_programs(ann_id):
    """{program id: section} of a tracked annotations file (single or per-program layout)."""
    path = os.path.join(ANNOTATIONS, '%s.json' % ann_id)
    if not os.path.isfile(path):
        return {}
    d = load_json(path)
    if 'programs' in d:
        return {p['id']: p for p in d['programs']}
    return {d['id']: d}


def full_name(fn):
    ns = fn.get('namespace') or ''
    return (ns + '::' + fn['name']) if ns else fn['name']


def split_name(full):
    if '::' in full:
        ns, _, name = full.rpartition('::')
        return ns, name
    return '', full


class Idf:
    """Feature ids and inverse document frequencies over both programs."""

    def __init__(self, programs):
        df = collections.Counter()
        n = 0
        for p in programs:
            for feats in p.static.values():
                n += 1
                df.update(feats.keys())
        self.ids = {}
        self.weight = []
        for k, c in df.items():
            if c > n * 0.2:
                continue
            self.ids[k] = len(self.weight)
            self.weight.append(math.log(n / c) * KIND_WEIGHT.get(k[0], 1.0))

    def raw(self, feats):
        out = {}
        for k, c in feats.items():
            i = self.ids.get(k)
            if i is not None:
                out[i] = (1.0 + math.log(c)) * self.weight[i]
        return out


def normalise(v):
    n = math.sqrt(sum(x * x for x in v.values()))
    if n == 0:
        return {}
    return {k: x / n for k, x in v.items()}


def dot(a, b):
    if len(a) > len(b):
        a, b = b, a
    return sum(x * b[k] for k, x in a.items() if k in b)
