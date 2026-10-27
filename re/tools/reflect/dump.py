"""Reflection dumps of a running SCCT Versus editor or game, and the layout diff between them.

Memory is only ever read (procread.Process: PROCESS_VM_READ | PROCESS_QUERY_INFORMATION,
ReadProcessMemory). Nothing is written to, injected into or suspended in the target.

    python -I -B re/tools/reflect/dump.py attach --pid N --out local/reflect/editor.json
    python -I -B re/tools/reflect/dump.py attach --pid N --out local/reflect/game.json \\
        --cdo-out local/reflect/game_cdo.json --cdo-packages SBase,Engine
    python -I -B re/tools/reflect/dump.py diff local/reflect/editor.json local/reflect/game.json \\
        --out local/reflect/layout-diff.json
    python -I -B re/tools/reflect/dump.py sandbox --sandbox F:\\SCCT-Automation\\svm\\Sandbox --map SteeE \\
        --out local/reflect

`sandbox` does the whole capture in a headless sandbox through the RE+ private test tools
(headless.py, gamewait.py, game_tools.py, mcp_server.py; --re-tools names their folder):
start the editor on a hidden desktop, dump it, open the map, Play Level under the sandbox's own
play lock, wait for the pawn (game_tools mutes the game on attach, as every scripted run does),
dump the game and its class defaults, then end every process that runs from the sandbox.
See README.md.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import procread  # noqa: E402
import ue2  # noqa: E402

FORMAT = 'svm.reflect/1'
CDO_FORMAT = 'svm.reflect.cdo/1'
DIFF_FORMAT = 'svm.reflect.diff/1'
DEFAULT_RE_TOOLS = os.path.expanduser(r'~\source\repos\SCCT_Versus_Reloaded_Editor_mb-main\tools\automation')


def log(*parts):
    print('[reflect %s] %s' % (time.strftime('%H:%M:%S'), ' '.join(str(p) for p in parts)), file=sys.stderr, flush=True)


def sha256(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for block in iter(lambda: f.read(1 << 20), b''):
            h.update(block)
    return h.hexdigest()


def profile_for(image_path):
    name = os.path.basename(image_path or '').lower()
    if name.startswith('chaostheory_editor'):
        return 'editor'
    if name == 'scct versus' or name.startswith('scct versus.'):
        return 'game'
    raise SystemExit('%s is neither the editor nor the game' % image_path)


def write_json(path, value):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    tmp = path + '.part'
    with open(tmp, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(value, f, indent=1, sort_keys=False, ensure_ascii=False)
        f.write('\n')
    os.replace(tmp, path)


def attach(pid, out, cdo_out=None, cdo_packages=('SBase', 'Engine'), profile=None, extra=None, raw=True):
    """Dump one running process. Returns the Engine for further use (e.g. the CDO pass)."""
    started = time.time()
    with procread.Process(pid) as memory:
        image = memory.image_path()
        profile = profile or profile_for(image)
        base = ue2.PROFILES[profile]['imageBase']
        pe = procread.pe_image(memory, base)
        if not pe or pe['imageBase'] != base:
            raise SystemExit('pid %d: no %s image at 0x%08x (%s)' % (pid, profile, base, pe))
        log('pid', pid, profile, image)
        t = time.time()
        engine = ue2.Engine(memory, profile, image_size=pe['sizeOfImage'])
        engine.modules = memory.modules()
        log('objects', len(engine.headers), 'names', len(engine.names), '%.1fs' % (time.time() - t))
        t = time.time()
        data = engine.reflect(log=log)
        log('reflected in %.1fs, %d reads' % (time.time() - t, memory.calls))
        header = {
            'format': FORMAT, 'process': profile, 'pid': pid, 'image': image,
            'imageSha256': sha256(image) if image and os.path.isfile(image) else None,
            'imageBase': ue2.hx(base), 'sizeOfImage': ue2.hx(pe['sizeOfImage']), 'timeDateStamp': pe['timeDateStamp'],
            'capturedAt': time.strftime('%Y-%m-%dT%H:%M:%S'), 'objects': len(engine.headers),
            'modules': [{'name': n, 'base': ue2.hx(b), 'size': ue2.hx(z)} for b, z, n in engine.modules],
            'names': engine.name_info, 'checks': engine.checks,
            'counts': {k: len(v) for k, v in data.items() if isinstance(v, list)},
        }
        if extra:
            header.update(extra)
        header.update(data)
        header['nameTable'] = {str(k): v for k, v in sorted(engine.names.items())}
        write_json(out, header)
        log('wrote', out)
        if cdo_out:
            t = time.time()
            cdos = engine.class_defaults(cdo_packages, raw=raw, log=log)
            write_json(cdo_out, {'format': CDO_FORMAT, 'process': profile, 'pid': pid, 'image': image,
                                 'imageSha256': header['imageSha256'], 'capturedAt': header['capturedAt'],
                                 'packages': list(cdo_packages), 'charSize': engine.char,
                                 'note': 'raw = UClass::Defaults bytes (the whole object block, header included); '
                                         'values = every property of the class (inherited too), decoded by kind',
                                 'classes': cdos, **(extra or {})})
            log('wrote', cdo_out, 'in %.1fs' % (time.time() - t))
        log('done in %.1fs' % (time.time() - started))
        return header


# ---- diff ---------------------------------------------------------------------------------------

def _index(records):
    return {r['path']: r for r in records if r.get('path')}


def diff(editor_path, game_path, out, cdo_a=None, cdo_b=None):
    with open(editor_path, encoding='utf-8') as f:
        a = json.load(f)
    with open(game_path, encoding='utf-8') as f:
        b = json.load(f)
    result = {'format': DIFF_FORMAT, 'a': {'file': editor_path, 'process': a['process'], 'sha256': a.get('imageSha256')},
              'b': {'file': game_path, 'process': b['process'], 'sha256': b.get('imageSha256')}, 'summary': {}}

    def compare(section, keys, extra_filter=None):
        ia, ib = _index(a[section]), _index(b[section])
        only_a = sorted(set(ia) - set(ib))
        only_b = sorted(set(ib) - set(ia))
        changed = []
        field_counts = {}
        for path in sorted(set(ia) & set(ib)):
            ra, rb = ia[path], ib[path]
            d = {k: [ra.get(k), rb.get(k)] for k in keys if ra.get(k) != rb.get(k)}
            if d:
                changed.append({'path': path, **d})
                for k in d:
                    field_counts[k] = field_counts.get(k, 0) + 1
        result[section] = {'onlyIn' + a['process'].title(): only_a, 'onlyIn' + b['process'].title(): only_b,
                           'changed': changed}
        result['summary'][section] = {'a': len(ia), 'b': len(ib), 'common': len(set(ia) & set(ib)),
                                      'onlyA': len(only_a), 'onlyB': len(only_b), 'changed': len(changed),
                                      'changedFields': dict(sorted(field_counts.items()))}

    compare('classes', ['super', 'within', 'flags', 'propertiesSize', 'configName', 'static', 'scriptSize',
                        'probeMask', 'ignoreMask'])
    compare('properties', ['kind', 'offset', 'elementSize', 'arrayDim', 'flags', 'repOffset', 'bitMask', 'enum',
                           'propertyClass', 'metaClass', 'struct', 'inner'])
    compare('functions', ['flags', 'iNative', 'parmsSize', 'returnValueOffset', 'numParms', 'operPrecedence',
                          'repOffset', 'scriptSize', 'super'])
    compare('states', ['super', 'probeMask', 'ignoreMask', 'stateFlags', 'labelTableOffset', 'scriptSize'])
    compare('structs', ['super', 'propertiesSize', 'structFlags'])
    compare('enums', ['names'])
    # Per package: where the differences are.
    by_package = {}
    for section in ('classes', 'properties', 'functions'):
        for side, key in (('onlyA', 'onlyIn' + a['process'].title()), ('onlyB', 'onlyIn' + b['process'].title())):
            for path in result[section][key]:
                pkg = path.split('.')[0]
                by_package.setdefault(pkg, {}).setdefault(section + '.' + side, 0)
                by_package[pkg][section + '.' + side] += 1
        for r in result[section]['changed']:
            pkg = r['path'].split('.')[0]
            by_package.setdefault(pkg, {}).setdefault(section + '.changed', 0)
            by_package[pkg][section + '.changed'] += 1
    result['summary']['byPackage'] = dict(sorted(by_package.items()))
    na, nb = a.get('nameTable') or {}, b.get('nameTable') or {}
    common = set(na) & set(nb)
    differing = sorted((int(k) for k in common if na[k] != nb[k]))
    result['names'] = {'a': len(na), 'b': len(nb), 'commonIndices': len(common), 'sameText': len(common) - len(differing),
                       'differing': [{'index': i, 'a': na[str(i)], 'b': nb[str(i)]} for i in differing[:200]]}
    result['summary']['names'] = {k: v for k, v in result['names'].items() if k != 'differing'}
    if cdo_a and cdo_b:
        localized = {p['name'] for p in a['properties'] + b['properties'] if 'Localized' in p['flagNames']}
        result['classDefaults'] = diff_defaults(cdo_a, cdo_b, localized)
        result['summary']['classDefaults'] = {k: v for k, v in result['classDefaults'].items() if k != 'classes'}
    write_json(out, result)
    return result['summary']


HEADER_PROPERTIES = {'ObjectInternal', 'Outer', 'ObjectFlags', 'Name', 'Class'}   # Core.Object's own


def diff_defaults(path_a, path_b, localized=frozenset()):
    """Decoded default values of the classes both CDO files hold, property by property. Raw bytes
    are not compared (they hold pointers, which differ between processes), nor is the UObject
    header (Core.Object's ObjectInternal, Outer, ObjectFlags, Name, Class). A difference in a
    property whose name is in `localized` (CPF_Localized) is counted apart: those values come
    from the localisation files, which the game applies and the editor does not."""
    with open(path_a, encoding='utf-8') as f:
        a = {c['path']: c for c in json.load(f)['classes']}
    with open(path_b, encoding='utf-8') as f:
        b = {c['path']: c for c in json.load(f)['classes']}
    classes, same_classes, values, same_values, loc_diffs, other_diffs = [], 0, 0, 0, 0, 0
    for path in sorted(set(a) & set(b)):
        va, vb = a[path].get('values') or {}, b[path].get('values') or {}
        keys = sorted((set(va) | set(vb)) - HEADER_PROPERTIES)
        differ = [k for k in keys if va.get(k) != vb.get(k)]
        values += len(keys)
        same_values += len(keys) - len(differ)
        loc = [k for k in differ if k.split('.')[-1] in localized]
        loc_diffs += len(loc)
        other_diffs += len(differ) - len(loc)
        if differ:
            classes.append({'path': path, 'differCount': len(differ), 'localized': loc,
                            'differ': {k: [va.get(k), vb.get(k)] for k in differ[:40]}})
        else:
            same_classes += 1
    return {'commonClasses': len(set(a) & set(b)), 'classesIdentical': same_classes, 'values': values,
            'valuesIdentical': same_values, 'differLocalized': loc_diffs, 'differOther': other_diffs,
            'onlyA': len(set(a) - set(b)), 'onlyB': len(set(b) - set(a)), 'classes': classes}


# ---- the sandbox run ----------------------------------------------------------------------------

class Watcher:
    """Records, twice a second, every process running from the sandbox's System folder and every
    top-level window those processes have on the hidden desktop (class, title and the text of a
    dialog's controls). Read only: it lists and reads, it never sends a window anything."""

    def __init__(self, headless, root, desktop):
        import threading
        self.h, self.root, self.desktop = headless, root, desktop
        self.processes = {}
        self.windows = {}
        self.stop = threading.Event()
        self.thread = threading.Thread(target=self._run, daemon=True)

    def _texts(self, hwnd):
        import ctypes
        from ctypes import wintypes
        user32 = ctypes.WinDLL('user32', use_last_error=True)
        found = []
        proc = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)

        def visit(child, _):
            buf = ctypes.create_unicode_buffer(1024)
            user32.GetWindowTextW(child, buf, 1024)
            if buf.value:
                found.append(buf.value)
            return True
        user32.EnumChildWindows(hwnd, proc(visit), 0)
        return found

    def _run(self):
        import game_tools
        while not self.stop.is_set():
            try:
                now = time.strftime('%H:%M:%S')
                pids = self.h.sandbox_pids(self.root)
                for pid in pids:
                    image = game_tools._image(pid)
                    rec = self.processes.setdefault(pid, {'image': image, 'first': now})
                    rec['last'] = now
                for w in self.h.windows(self.desktop):
                    if w['pid'] in pids and w['hwnd'] not in self.windows:
                        rec = dict(w, seen=now)
                        if w['class'] == '#32770':
                            try:
                                rec['texts'] = self.h.on_desktop(self.desktop, self._texts, w['hwnd'])
                            except Exception as error:
                                rec['texts'] = ['unreadable: %s' % error]
                        self.windows[w['hwnd']] = rec
            except Exception:   # the hidden desktop does not exist until the editor starts
                pass
            self.stop.wait(0.5)

    def __enter__(self):
        self.thread.start()
        return self

    def __exit__(self, *exc):
        self.stop.set()
        self.thread.join()

    def report(self):
        return {'processes': {str(k): v for k, v in self.processes.items()},
                'windows': [dict(v, hwnd=hex(k)) for k, v in self.windows.items() if v.get('visible')]}


def sandbox_run(args):
    """Editor and game dumps in one headless, slotted sandbox run (see the module docstring)."""
    sys.path.insert(0, args.re_tools)
    import game_tools
    import gamewait
    import headless
    import mcp_server
    root = os.path.abspath(args.sandbox)
    out = os.path.abspath(args.out)
    os.makedirs(out, exist_ok=True)
    report = {'sandbox': root, 'map': args.map, 'desktop': args.desktop, 'slot': headless.slot(root),
              'lock': gamewait.lock_path(root), 'started': time.strftime('%Y-%m-%dT%H:%M:%S')}
    if report['slot']['slot'] is None and not args.unslotted:
        raise SystemExit('%s is not slotted (headless.py slot <sandbox> <letter>); refusing the shared lock' % root)
    if headless.sandbox_pids(root):
        raise SystemExit('something already runs from %s: %s' % (root, headless.sandbox_pids(root)))
    system = os.path.join(root, 'System')
    report['editorDll'] = sha256(os.path.join(system, 'Reloaded.Editor.dll'))
    report['proxy'] = sha256(os.path.join(system, 'd3d9.dll'))
    map_path = args.map if os.path.isabs(args.map) else os.path.join(root, 'Packages', 'MapsEd', args.map + '.sdc')
    config = os.path.join(system, 'SCCT_Versus.config')
    with open(config, 'rb') as f:
        original_config = f.read()
    started_pids = set()
    watcher = Watcher(headless, root, args.desktop)
    watcher.__enter__()
    try:
        if args.fps:
            report['frameCap'] = headless.frame_cap(root, args.fps)
        launched = headless.launch_editor(root, args.desktop)
        report['editor'] = launched
        started_pids.add(launched['pid'])
        log('editor', launched)
        if not args.skip_editor_dump:
            report['editorDump'] = attach(launched['pid'], os.path.join(out, 'editor.json'),
                                          cdo_out=os.path.join(out, 'editor_cdo.json'),
                                          cdo_packages=args.cdo_packages.split(','),
                                          extra={'sandbox': root, 'editorDllSha256': report['editorDll']})['counts']
        editor = mcp_server.Editor(launched['pid'])
        t = time.time()
        loaded = editor.call('map.load', {'path': map_path})
        report['mapLoad'] = {'ok': loaded.get('ok'), 'seconds': round(time.time() - t, 1), 'path': map_path}
        if not loaded.get('ok'):
            raise SystemExit('map.load failed: %s' % loaded.get('error'))
        log('map loaded', report['mapLoad'])
        with gamewait.play_lock(root, timeout=args.lock_timeout, log=log):
            report['lockTaken'] = time.strftime('%H:%M:%S')
            t = time.time()
            game_tools.start_game(root, lambda: editor.call('play', {'side': args.side}), timeout=600,
                                  say=lambda m: log(m))
            report['gameUpSeconds'] = round(time.time() - t, 1)
            session = game_tools.Session(root)
            try:
                deadline = time.time() + args.pawn_timeout
                state = None
                while time.time() < deadline:
                    # Nobody can answer a box on the hidden desktop. One from the game side (the
                    # launcher or the game: an integrity or CD key prompt, a crash) ends the run;
                    # it is never answered or dismissed by the tool.
                    game_side = set(headless.sandbox_pids(root)) - {launched['pid']}
                    boxes = headless.dialogs(args.desktop, game_side)
                    if boxes:
                        report['dialogs'] = boxes
                        raise SystemExit('a dialog opened on the hidden desktop: %s' % boxes)
                    try:
                        state = session.wait(timeout=20)   # mutes the game (game_tools) once it attaches
                        break
                    except game_tools.GameError as error:
                        log('waiting for the pawn:', error)
                if state is None:
                    raise SystemExit('no pawn within %ds' % args.pawn_timeout)
                report['pawnSeconds'] = round(time.time() - t, 1)
                report['pawn'] = state.get('pawn')
                game_pid = session.find()
                started_pids.add(game_pid)
                report['gamePid'] = game_pid
                log('pawn ready', state.get('pawn'))
                time.sleep(args.settle)
                report['gameDump'] = attach(game_pid, os.path.join(out, 'game.json'),
                                            cdo_out=os.path.join(out, 'game_cdo.json'),
                                            cdo_packages=args.cdo_packages.split(','),
                                            extra={'sandbox': root, 'map': args.map, 'side': args.side})['counts']
                report['gameAliveAfterDump'] = bool(game_tools.sandbox_games(root))
            finally:
                report['gameQuit'] = session.quit(force=True)
                report['lockReleased'] = time.strftime('%H:%M:%S')
    finally:
        watcher.__exit__()
        report['watch'] = watcher.report()
        report['killed'] = headless.kill_sandbox(root)
        with open(config, 'wb') as f:
            f.write(original_config)
        report['finished'] = time.strftime('%Y-%m-%dT%H:%M:%S')
        write_json(os.path.join(out, 'run.json'), report)
        log('run report', os.path.join(out, 'run.json'))
    return report


def probe(pid, paths, size, profile=None):
    """Annotated dwords of named objects: every word that is an object prints its path and class,
    every small word that is a name index prints the name. For checking layouts by eye."""
    with procread.Process(pid) as memory:
        profile = profile or profile_for(memory.image_path())
        pe = procread.pe_image(memory, ue2.PROFILES[profile]['imageBase'])
        engine = ue2.Engine(memory, profile, image_size=pe['sizeOfImage'])
        by_path = {}
        for o in engine.headers:
            by_path.setdefault(engine.path(o), o)
        for path in paths:
            o = by_path.get(path)
            if not o:
                print('%s: not found' % path)
                continue
            print('== %s 0x%08x %s' % (path, o, engine.kind(o)))
            raw = memory.read(o, size)
            for off in range(0, len(raw) - 3, 4):
                w = int.from_bytes(raw[off:off + 4], 'little')
                note = ''
                if engine.is_object(w):
                    note = '%s (%s)' % (engine.path(w), engine.kind(w))
                elif 0 < w < 4_000_000 and w in engine.names:
                    note = 'name? %s' % engine.names[w]
                elif engine.in_image(w):
                    note = 'image +0x%06x' % (w - engine.image_base)
                print('  +%03x %08x %6d %6d  %s' % (off, w, w & 0xFFFF, w >> 16, note))


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n\n')[0])
    sub = ap.add_subparsers(dest='cmd', required=True)
    p = sub.add_parser('probe', help='annotated raw words of named objects (layout checks)')
    p.add_argument('--pid', type=int, required=True)
    p.add_argument('--size', type=lambda v: int(v, 0), default=0x80)
    p.add_argument('paths', nargs='+')
    p = sub.add_parser('attach', help='dump a running editor or game (read only)')
    p.add_argument('--pid', type=int, required=True)
    p.add_argument('--out', required=True)
    p.add_argument('--cdo-out')
    p.add_argument('--cdo-packages', default='SBase,Engine')
    p.add_argument('--profile', choices=sorted(ue2.PROFILES))
    p.add_argument('--no-raw', action='store_true', help='leave the raw default bytes out of the CDO file')
    p = sub.add_parser('diff', help='layout diff between two dumps')
    p.add_argument('a')
    p.add_argument('b')
    p.add_argument('--out', required=True)
    p.add_argument('--cdo-a', help='class defaults of a (editor_cdo.json)')
    p.add_argument('--cdo-b', help='class defaults of b (game_cdo.json)')
    p = sub.add_parser('sandbox', help='headless editor + Play Level dumps in a slotted sandbox')
    p.add_argument('--sandbox', required=True)
    p.add_argument('--map', default='SteeE', help='a MapsEd map name or a full .sdc path')
    p.add_argument('--out', required=True)
    p.add_argument('--re-tools', default=os.environ.get('SVM_RE_TOOLS', DEFAULT_RE_TOOLS),
                   help='the RE+ private checkout\'s tools folder (headless.py, gamewait.py, ...)')
    p.add_argument('--desktop', default='SVMHeadless')
    p.add_argument('--side', default='spy', choices=('spy', 'merc'))
    p.add_argument('--fps', type=int, default=60)
    p.add_argument('--cdo-packages', default='SBase,Engine')
    p.add_argument('--pawn-timeout', type=int, default=240)
    p.add_argument('--lock-timeout', type=int, default=3600)
    p.add_argument('--settle', type=float, default=3.0)
    p.add_argument('--skip-editor-dump', action='store_true')
    p.add_argument('--unslotted', action='store_true', help='allow the machine-wide lock (not recommended)')
    args = ap.parse_args()
    sys.stdout.reconfigure(encoding='utf-8')
    if args.cmd == 'probe':
        probe(args.pid, args.paths, args.size)
    elif args.cmd == 'attach':
        h = attach(args.pid, args.out, args.cdo_out, args.cdo_packages.split(','), args.profile, raw=not args.no_raw)
        print(json.dumps({'counts': h['counts'], 'names': h['names'], 'checks': h['checks']}, indent=1))
    elif args.cmd == 'diff':
        print(json.dumps(diff(args.a, args.b, args.out, args.cdo_a, args.cdo_b), indent=1))
    elif args.cmd == 'sandbox':
        r = sandbox_run(args)
        print(json.dumps({k: r.get(k) for k in ('slot', 'editor', 'mapLoad', 'gameUpSeconds', 'pawnSeconds',
                                                'editorDump', 'gameDump', 'gameQuit', 'killed')}, indent=1, default=str))


if __name__ == '__main__':
    main()
