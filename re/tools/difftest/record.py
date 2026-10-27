"""Record golden traces of the original game for differential tests.

    python -I -B re/tools/difftest/record.py record tests/scenarios/movement/*.scenario.json \\
        [--runs 3] [--out local/difftest/golden] [--sandbox F:\\SCCT-Automation\\svm\\Sandbox]
    python -I -B re/tools/difftest/record.py envelope local/difftest/golden/<id>
    python -I -B re/tools/difftest/record.py summary local/difftest/golden
    python -I -B re/tools/difftest/record.py check tests/scenarios           # validate scenario files

`record` starts the sandbox editor headless, opens each scenario's map, and for each team runs one
Play Level game under the play lock (game_tools mutes it on attach; the frame cap is 60 for the
run and restored after). In that game every scenario of the team is played `--runs` times: setup
(teleport to the scenario's spot, stance, view, writes), settle, then the frame-indexed inputs,
sampling one stable state per frame (livegame.py). Each run is written to
<out>/<scenario id>/run<N>.jsonl.gz (svm.trace/1) and, when all runs are in, the noise envelope to
<out>/<scenario id>/envelope.json. The game is quit and the lock let go as soon as the team's runs
are done; every process this tool started is ended at the end.

Traces are game-derived data: they stay under local/ (gitignored).
"""
from __future__ import annotations

import argparse
import glob
import json
import os
import platform
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import livegame  # noqa: E402
import sandbox as sb  # noqa: E402
import scenario as scn  # noqa: E402
import trace as tr  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
RECORDER_VERSION = 1
CLEARANCE = 2.0          # uu above the floor a teleported pawn's cylinder bottom is put at
STILL_SPEED = 0.5        # uu/s: "still" for waitStill
STILL_FRAMES = 10


def rel(path):
    try:
        return os.path.relpath(path, REPO).replace('\\', '/')
    except ValueError:
        return path


class Runner:
    """Plays one scenario in a running game and writes its trace."""

    def __init__(self, game, log=sb.log):
        self.game = game
        self.log = log
        self.prev_t = game.time_seconds()

    def tick(self, n=1):
        s = None
        for _ in range(n):
            s = self.game.next_sample(self.prev_t)
            self.prev_t = s['t']
        return s

    def release_all(self, names):
        for b in names:
            self.game.write_byte(b, 0)

    def setup(self, sc):
        g = self.game
        info = {}
        spawn = sc['spawn']
        for op in sc.get('setup', []):
            kind = op['op']
            if kind == 'teleport':
                s = self.tick()
                height = s['pawn']['CollisionHeight']
                x, y, fz = spawn['floor']
                loc = [x, y, fz + height + spawn.get('clearance', CLEARANCE)]
                g.teleport(loc, spawn['yaw'])
                g.set_view(0, spawn['yaw'])
                info['teleport'] = {'location': loc, 'yaw': spawn['yaw'], 'collisionHeight': height}
                self.tick(2)
            elif kind == 'write':
                g.write_setup(op['target'], op['field'], op['value'])
                self.tick()
            elif kind == 'fastSpeed':
                g.set_fast_speed(op['value'])
                self.tick()
            elif kind == 'view':
                g.set_view(op['pitch'], op['yaw'])
                self.tick()
            elif kind == 'wait':
                self.tick(op['frames'])
            elif kind == 'stance':
                info['stance'] = self.stance(op['crouched'])
            elif kind == 'waitStill':
                info['still'] = self.wait_still(op['maxFrames'])
            elif kind == 'leaveGE':
                info['leaveGE'] = self.leave_ge(op['maxFrames'])
        return info

    def leave_ge(self, max_frames):
        """Recorder-side reset before a traversal scenario: a pawn left on a GE (hanging, on a pipe or a zip
        line) or in a root-motion move is dropped with a crouch press (every GE state drops on the crouch
        latch) and the tool waits until it walks again. Nothing happens when it already walks; the sim
        starts each scenario fresh, so for it this step is empty."""
        s = self.tick()
        if s['pawn']['Physics'] == 1:
            return {'frames': 0}
        presses = 0
        for n in range(max_frames):
            phys = s['pawn']['Physics']
            if phys == 1:
                self.tick(20)
                return {'frames': n, 'presses': presses, 'from': s['cs']}
            if phys not in (1, 2) and n % 30 == 0:
                self.game.write_byte('bCrouch', 1)
                self.tick(2)
                self.game.write_byte('bCrouch', 0)
                presses += 1
            s = self.tick()
        return {'frames': max_frames, 'presses': presses, 'timedOut': True, 'physics': s['pawn']['Physics']}

    def stance(self, crouched, tries=4):
        """Press crouch (a toggle) until bIsCrouched is what the scenario wants: hold 3 frames,
        release, wait up to 1.5 s for the transition (the stance change has a 0.5 s cooldown)."""
        for attempt in range(tries + 1):
            s = self.tick()
            if bool(s['pawn']['bIsCrouched']) == crouched and s['ps'] is not None:
                if attempt:
                    self.tick(30)          # let the transition state finish
                return {'presses': attempt}
            self.game.write_byte('bCrouch', 1)
            self.tick(3)
            self.game.write_byte('bCrouch', 0)
            for _ in range(90):
                s = self.tick()
                if bool(s['pawn']['bIsCrouched']) == crouched:
                    break
            self.tick(36)
        raise RuntimeError('the pawn did not reach crouched=%s' % crouched)

    def wait_still(self, max_frames):
        still = 0
        for n in range(max_frames):
            s = self.tick()
            v = s['pawn']['Velocity']
            moving = livegame.horizontal_speed(v) > STILL_SPEED or abs(v[2]) > STILL_SPEED
            still = 0 if moving or s['pawn']['Physics'] != 1 else still + 1
            if still >= STILL_FRAMES:
                return {'frames': n + 1}
        return {'frames': max_frames, 'timedOut': True}

    def frame_record(self, f, s, inputs, late, gap, wall0):
        pawn, pc, level = s['pawn'], s['pc'], s['level']
        rec = {'f': f, 't': s['t'], 'dt': level.get('m_dT'), 'wall': round(s['wallAt'] - wall0, 5),
               'loc': pawn['Location'], 'vel': pawn['Velocity'], 'acc': pawn.get('Acceleration'),
               'rot': pawn['Rotation'], 'crot': pc['Rotation'], 'phys': pawn['Physics'], 'ps': s['ps'],
               'cs': s['cs'], 'crouch': pawn.get('bIsCrouched'), 'roll': pawn.get('bIsRolling'),
               'hp': pawn.get('Health'), 'gs': pawn.get('GroundSpeed'), 'ch': pawn.get('CollisionHeight'),
               'zmax': pc.get('fZMaxFalling'), 'jn': pawn.get('nJumpNumber'), 'fwd': pc.get('aForward'),
               'str': pc.get('aStrafe'), 'npm': pc.get('fNormePadMove'), 'imove': pc.get('iMove'),
               'fast': pc.get('bFastSpeed'), 'cwr': pc.get('bCrouchWithRoll'), 'pj': pc.get('bPressedJump'),
               'in': inputs, 'anim': s['anim'], 'late': int(late), 'gap': gap, 'tries': s['tries']}
        if not s['stable']:
            rec['unstable'] = 1
        tv = s.get('traversal')
        if tv is not None:
            tp, tc = tv['pawn'], tv['pc']
            ges = self.game.ges
            rec['tv'] = {'pawn': tp, 'pc': tc,
                         'ge': livegame.ge_index(ges, tp.get('ZipP1'), tp.get('ZipP2')),
                         'newGe': livegame.ge_index(ges, tp.get('NewZipP1'), tp.get('NewZipP2')),
                         'base': self.game.object_name(pawn.get('Base'))}
            rec['rm'] = tv['rootMotion']
            rec['ach'] = tv['channels']
        return rec

    def apply(self, state, buttons):
        g = self.game
        for b in buttons:
            g.write_byte(b, state['buttons'].get(b, 0))
        if state['fastSpeed'] is not None:
            g.set_fast_speed(state['fastSpeed'])
        if state['view'] is not None:
            g.set_view(*state['view'])
        if state['jump']:
            g.press_jump()
        # Late: the next frame began before the writes landed, so its tick may not have seen them.
        return self.game.time_seconds() != self.prev_t

    @staticmethod
    def inputs_of(state):
        out = {k: v for k, v in state['buttons'].items() if v}
        if state['jump']:
            out['jump'] = 1
        if state['view'] is not None:
            out['view'] = list(state['view'])
        if state['fastSpeed'] is not None:
            out['fastSpeed'] = int(state['fastSpeed'])
        return out

    def play(self, sc, path, header, traversal=False, ges=()):
        g = self.game
        g.enable_traversal(traversal, ges)
        timeline = scn.timeline(sc)
        buttons = sorted({b for ev in sc['inputs'] for b in ev.get('buttons', {})} | {'BT_Up', 'BT_Down',
                                                                                        'BT_Left', 'BT_Right'})
        self.release_all(scn.INPUT_BYTES)
        info = self.setup(sc)
        last = self.tick(max(1, sc['settleFrames']))
        header = dict(header, setup=info, timeDilation=last['level'].get('TimeDilation'))
        if traversal:
            header['traversal'] = dict(header.get('traversal') or {}, offsets=g.traversal_offsets())
        writer = tr.TraceWriter(path, header)
        wall0 = time.perf_counter()
        last['wallAt'] = wall0
        writer.frame(self.frame_record(-1, last, {}, False, 0, wall0))
        late = self.apply(timeline[0], buttons)
        stats = {'torn': 0, 'gaps': 0, 'late': 0, 'unstable': 0, 'dts': []}
        f = 0
        t_first = None
        try:
            while f < len(timeline):
                s = g.next_sample(self.prev_t)
                s['wallAt'] = time.perf_counter()
                dt = s['level'].get('m_dT') or 0.0
                step = s['t'] - self.prev_t
                gap = max(0, int(round(step / dt)) - 1) if dt > 0 else 0
                gap = max(gap, s['torn'])
                if gap:
                    stats['gaps'] += gap
                    f += gap
                    if f >= len(timeline):
                        break
                self.prev_t = s['t']
                t_first = s['t'] if t_first is None else t_first
                stats['torn'] += s['torn']
                stats['late'] += int(late)
                stats['unstable'] += 0 if s['stable'] else 1
                stats['dts'].append(dt)
                writer.frame(self.frame_record(f, s, self.inputs_of(timeline[f]), late, gap, wall0))
                f += 1
                if f < len(timeline):
                    late = self.apply(timeline[f], buttons)
        finally:
            self.release_all(scn.INPUT_BYTES)
        dts = stats.pop('dts') or [0.0]
        footer = dict(stats, wallSeconds=round(time.perf_counter() - wall0, 3),
                      gameSeconds=tr.f32(self.prev_t - t_first) if t_first is not None else 0.0,
                      dtMean=sum(dts) / len(dts), dtMin=min(dts), dtMax=max(dts),
                      fps=len(dts) / max(1e-9, (self.prev_t - t_first)) if t_first is not None else 0.0)
        writer.close(footer)
        return footer


def binaries_check(found):
    lock_path = os.path.join(REPO, 're', 'binaries.lock.json')
    try:
        with open(lock_path, encoding='utf-8') as f:
            lock = {e['path']: e['sha256'] for e in json.load(f)['files']}
    except (OSError, ValueError, KeyError):
        return None
    return {path: (lock.get(path) == sha) if path in lock else None for path, sha in found.items()}


def load_ges(map_name):
    """The map's GE list from the decoder (local/formats/ge/<Map>.json), with the file's path and hash, or
    ([], None) when it has not been decoded."""
    path = os.path.join(REPO, 'local', 'formats', 'ge', map_name + '.json')
    if not os.path.isfile(path):
        return [], None
    with open(path, encoding='utf-8') as f:
        d = json.load(f)
    ges = [{'index': g['index'], 'kind': g['kind'], 'points': g['points']} for g in d.get('ges', [])]
    return ges, {'geFile': rel(path), 'geSha256': sb.sha256(path)}


def record(args):
    scenarios = []
    for pattern in args.scenarios:
        for path in sorted(glob.glob(pattern)) or [pattern]:
            scenarios.append(scn.load(path))
    if args.only:
        wanted = set(args.only.split(','))
        scenarios = [s for s in scenarios if s['id'] in wanted]
    if not scenarios:
        raise SystemExit('no scenarios')
    groups = {}
    for s in scenarios:
        groups.setdefault((s['map'], scn.team(s)), []).append(s)
    tools = sb.load_tools(args.re_tools)
    root = os.path.abspath(args.sandbox)
    out = os.path.abspath(args.out)
    hashes = sb.binaries(root)
    results = []
    for sc in scenarios:
        # Runs left from an earlier recording with more repeats would join this one's envelope.
        for old in glob.glob(os.path.join(out, sc['id'], 'run*.jsonl.gz')):
            n = os.path.basename(old)[3:].split('.')[0]
            if n.isdigit() and int(n) > args.runs:
                os.remove(old)
    livegame.fine_timer(True)
    try:
        with sb.Sandbox(tools, root, fps=args.fps) as box:
            for (map_name, team), group in sorted(groups.items()):
                # One game per run index by default: run N of every scenario of the group shares a game,
                # so the runs of one scenario come from different games and the envelope includes the
                # game-to-game spread (frame rate settles slightly differently in each game).
                sessions = [[r] for r in range(1, args.runs + 1)] if not args.one_game else [
                    list(range(1, args.runs + 1))]
                for index, run_numbers in enumerate(sessions, 1):
                    map_info = box.load_map(map_name)
                    ges, ge_info = load_ges(map_name)
                    with box.play(team, lock_timeout=args.lock_timeout) as (session, game_info):
                        game = livegame.LiveGame(session, delay=args.delay / 1000.0)
                        sb.log('game %d of %d: %d scenarios as %s, runs %s' % (
                            index, len(sessions), len(group), team, run_numbers))
                        runner = Runner(game)
                        for sc in group:
                            folder = os.path.join(out, sc['id'])
                            os.makedirs(folder, exist_ok=True)
                            doc = {k: v for k, v in sc.items() if not k.startswith('_')}
                            for run in run_numbers:
                                header = {
                                    'scenario': {'id': sc['id'], 'path': rel(sc['_path']), 'sha256': sc['_sha256'],
                                                 'doc': doc},
                                    'run': run, 'runs': args.runs, 'team': team,
                                    'game': {'index': index, 'of': len(sessions), 'pid': game_info.get('gamePid')},
                                    'map': {'name': map_name, 'mapsEd': {'path': map_info['path'],
                                                                         'sha256': map_info['sha256']},
                                            'autoplaySha256': game_info.get('autoplaySha256')},
                                    'binaries': hashes, 'binariesMatchLock': binaries_check(hashes),
                                    'fps': {'cap': args.fps}, 'pawnClass': game.pawn_class,
                                    'controllerClass': game.pc_class, 'offsets': game.offsets(),
                                    'recorder': {'tool': 're/tools/difftest/record.py', 'version': RECORDER_VERSION,
                                                 'sampleDelayMs': args.delay, 'python': platform.python_version()},
                                    'sandbox': root, 'startedAt': time.strftime('%Y-%m-%dT%H:%M:%S'),
                                }
                                path = os.path.join(folder, 'run%d.jsonl.gz' % run)
                                traversal = args.traversal or scn.profile(sc) == 'traversal'
                                if traversal:
                                    header['recorder']['extras'] = ['traversal']
                                    header['traversal'] = dict(ge_info or {})
                                t = time.time()
                                footer = runner.play(sc, path, header, traversal=traversal, ges=ges)
                                sb.log('%s run %d: %d frames, %.1f fps, torn %d, gaps %d, late %d, unstable %d '
                                       '(%.1fs)' % (sc['id'], run, len(scn.timeline(sc)), footer['fps'],
                                                    footer['torn'], footer['gaps'], footer['late'],
                                                    footer['unstable'], time.time() - t))
                                results.append({'id': sc['id'], 'run': run, 'game': index, 'path': rel(path),
                                                **footer})
                for sc in group:
                    write_envelope(os.path.join(out, sc['id']))
        report = dict(box.report)   # after the exit: it holds the processes ended
    finally:
        livegame.fine_timer(False)
    report['results'] = results
    sb_out = os.path.join(out, '_runs')
    os.makedirs(sb_out, exist_ok=True)
    with open(os.path.join(sb_out, time.strftime('%Y%m%d-%H%M%S') + '.json'), 'w', encoding='utf-8') as f:
        json.dump(report, f, indent=1, default=str)
    return report


def write_envelope(folder):
    runs = {}
    for path in sorted(glob.glob(os.path.join(folder, 'run*.jsonl.gz'))):
        _, frames, _ = tr.read(path)
        runs[os.path.basename(path).split('.')[0]] = frames
    env = tr.envelope(runs)
    with open(os.path.join(folder, 'envelope.json'), 'w', encoding='utf-8', newline='\n') as f:
        json.dump(env, f, indent=1)
    return env


def summary(root):
    for folder in sorted(glob.glob(os.path.join(root, '*'))):
        env_path = os.path.join(folder, 'envelope.json')
        if not os.path.isfile(env_path):
            continue
        with open(env_path, encoding='utf-8') as f:
            env = json.load(f)
        e = env['envelope']
        same = {k: v['same'] for k, v in env['states'].items()}
        print('%-34s runs %d ref %-5s locRms %6.2f locEnd %6.2f locMax %6.2f velRms %7.2f same %s' % (
            os.path.basename(folder), len(env['runs']), env['reference'], e['locRms'], e['locEnd'], e['locMax'],
            e['velRms'], same))


def show(path, every=1, first=None, last=None):
    """A trace as a table: one line per frame (or every Nth), changes of state always shown."""
    import math
    header, frames, footer = tr.read(path)
    print('# %s run %s team %s pawn %s, setup %s' % (header['scenario']['id'], header['run'], header['team'],
                                                     header['pawnClass'], json.dumps(header.get('setup'))))
    prev = None
    for fr in frames:
        if first is not None and fr['f'] < first or last is not None and fr['f'] > last:
            continue
        key = (fr['phys'], fr['ps'], fr['cs'], fr['crouch'], fr['hp'])
        if fr['f'] % every and key == prev and fr['f'] >= 0:
            continue
        prev = key
        v = fr['vel']
        anim = ' '.join('%d:%s@%.2f/%.2f' % (c[0], c[1], c[2], c[4]) for c in fr['anim'][:4])
        if 'tv' in fr:
            rm = fr.get('rm') or {}
            anim += ' | ge=%s new=%s lock=%s base=%s' % (fr['tv'].get('ge'), fr['tv'].get('newGe'), rm.get('mode'),
                                                       fr['tv'].get('base'))
        print('%4d t=%8.4f dt=%.4f loc=(%8.1f %8.1f %8.1f) h=%6.1f vz=%7.1f phys=%d %-26s crouch=%d hp=%3d '
              'zmax=%8.1f gs=%5.1f fwd=%6.0f npm=%.2f in=%s %s' % (
                  fr['f'], fr['t'], fr['dt'], fr['loc'][0], fr['loc'][1], fr['loc'][2], math.hypot(v[0], v[1]), v[2],
                  fr['phys'], fr['cs'], fr['crouch'], fr['hp'], fr['zmax'], fr['gs'], fr['fwd'], fr['npm'],
                  json.dumps(fr['in'], separators=(',', ':')), anim))
    print('# footer', json.dumps(footer))


def check(paths):
    bad = 0
    files = []
    for p in paths:
        files += glob.glob(os.path.join(p, '**', '*.scenario.json'), recursive=True) if os.path.isdir(p) else [p]
    ids = {}
    for path in sorted(files):
        try:
            sc = scn.load(path)
            ids.setdefault(sc['id'], []).append(path)
            print('ok   %s (%s, %d frames, %d inputs)' % (rel(path), sc['id'], sc['durationFrames'], len(sc['inputs'])))
        except (scn.ScenarioError, ValueError) as error:
            bad += 1
            print('FAIL %s' % error)
    for i, ps in ids.items():
        if len(ps) > 1:
            bad += 1
            print('FAIL duplicate id %s: %s' % (i, ps))
    return bad


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n\n')[0])
    sub = ap.add_subparsers(dest='cmd', required=True)
    p = sub.add_parser('record', help='play scenarios in the original and write golden traces')
    p.add_argument('scenarios', nargs='+')
    p.add_argument('--runs', type=int, default=3)
    p.add_argument('--out', default=os.path.join(REPO, 'local', 'difftest', 'golden'))
    p.add_argument('--sandbox', default=sb.DEFAULT_SANDBOX)
    p.add_argument('--re-tools', default=os.environ.get('SVM_RE_TOOLS', sb.DEFAULT_RE_TOOLS))
    p.add_argument('--fps', type=int, default=60)
    p.add_argument('--delay', type=float, default=2.0, help='ms after a frame starts before sampling it')
    p.add_argument('--lock-timeout', type=int, default=3600)
    p.add_argument('--only', help='comma-separated scenario ids')
    p.add_argument('--one-game', action='store_true',
                   help='all runs in one game (faster; the envelope then misses the game-to-game spread)')
    p.add_argument('--traversal', action='store_true',
                   help='write the traversal extras for every scenario (always on for profile traversal)')
    p = sub.add_parser('envelope', help='recompute a scenario folder\'s noise envelope')
    p.add_argument('folder')
    p = sub.add_parser('summary', help='one line per recorded scenario')
    p.add_argument('root', nargs='?', default=os.path.join(REPO, 'local', 'difftest', 'golden'))
    p = sub.add_parser('check', help='validate scenario files')
    p.add_argument('paths', nargs='+')
    p = sub.add_parser('show', help='print a trace as a table')
    p.add_argument('path')
    p.add_argument('--every', type=int, default=1)
    p.add_argument('--first', type=int)
    p.add_argument('--last', type=int)
    args = ap.parse_args()
    sys.stdout.reconfigure(encoding='utf-8')
    if args.cmd == 'record':
        report = record(args)
        print(json.dumps({'results': report['results'], 'killed': report.get('killed')}, indent=1, default=str))
    elif args.cmd == 'envelope':
        print(json.dumps(write_envelope(args.folder)['envelope'], indent=1))
    elif args.cmd == 'summary':
        summary(args.root)
    elif args.cmd == 'check':
        sys.exit(1 if check(args.paths) else 0)
    elif args.cmd == 'show':
        show(args.path, args.every, args.first, args.last)


if __name__ == '__main__':
    main()
