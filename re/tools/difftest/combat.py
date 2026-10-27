"""Record real-game combat ground truth with the two-pawn rig.

    python -I -B re/tools/difftest/combat.py build                     # compile the rig into SteeECombat (once)
    python -I -B re/tools/difftest/combat.py check [tests/scenarios/combat]
    python -I -B re/tools/difftest/combat.py record "tests/scenarios/combat/*.combat.json" [--runs N] [--only ids]
    python -I -B re/tools/difftest/combat.py measure [local/difftest/golden-combat]   # results.json, RESULTS.md
    python -I -B re/tools/difftest/combat.py show local/difftest/golden-combat/<id>/run1.jsonl.gz [--every 5]

`record` starts the project sandbox's editor headless, opens the rig map (`<map>Combat`, built from the stock
map by combat_rig.build_map when missing or older than the rig sources), and per local team plays one Play Level
game under the play lock (game_tools mutes it; frame cap 60 for the run, restored after). In it every scenario of
the team is played `--runs` times: setup (the local player placed by the rig, stance, gun, ammunition; the target
player spawned, placed, its stance; both healed), settle, then the frame-indexed events, sampling one stable state
of both players per frame (combat_live.py). Each run goes to <out>/<id>/run<N>.jsonl.gz (svm.combat-trace/1) and
its numbers to <out>/<id>/measure.json (combat_measure.py).

Traces and measurements are game-derived data: they stay under local/ (gitignored).
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

import combat_live  # noqa: E402
import combat_rig as cr  # noqa: E402
import combat_scenario as cs  # noqa: E402
import livegame  # noqa: E402
import sandbox as sb  # noqa: E402
import trace as tr  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
TRACE_FORMAT = 'svm.combat-trace/1'
RECORDER_VERSION = 1
DEFAULT_OUT = os.path.join(REPO, 'local', 'difftest', 'golden-combat')
DEFAULT_SCENARIOS = os.path.join(REPO, 'tests', 'scenarios', 'combat', '*.combat.json')
STILL_SPEED = 0.5
PLACE_LIFT = 80.0         # uu a placed target falls from (combat_rig: placed on the floor it sank through)
WALKING_STATES = ('s_PlayerWalking',)


def rel(path):
    try:
        return os.path.relpath(path, REPO).replace('\\', '/')
    except ValueError:
        return path


# -- the frame record -------------------------------------------------------------------------------------------------

def _r(v, nd=3):
    return [round(x, nd) for x in v] if isinstance(v, list) else v


class Recorder:
    """Turns raw samples into frame records (state names, sequence names, the side blocks flattened)."""

    def __init__(self, cg):
        self.cg = cg
        self.names = {}

    def node(self, ptr):
        if not ptr:
            return None
        if ptr not in self.names:
            self.names[ptr] = self.cg.obj_name(ptr)
        return self.names[ptr]

    def side(self, s):
        if s is None:
            return None
        pawn, pc, w = s['pawn'] or {}, s['pc'] or {}, s['weapon']
        ptrs, nodes = s['ptrs'], s['nodes']
        out = {
            'loc': pawn.get('Location'), 'vel': pawn.get('Velocity'), 'acc': pawn.get('Acceleration'),
            'rot': pawn.get('Rotation'), 'phys': pawn.get('Physics'), 'hp': pawn.get('Health'),
            'crouch': pawn.get('bIsCrouched'), 'ch': pawn.get('CollisionHeight'), 'eye': pawn.get('EyeHeight'),
            'bob': pawn.get('WalkBob'),
            'ps': self.node(nodes[0]) or (self.cg.class_name(ptrs[0]) if ptrs[0] else None),
            'cs': self.node(nodes[1]), 'ws': self.node(nodes[2]),
            'wl': pawn.get('WeaponLoaded'), 'sticky': pawn.get('bStickyShocking'),
            'head': pawn.get('bStickyShockedInTheHead'), 'act': pawn.get('sAction'), 'ik': pawn.get('IK_Type'),
            'grabbable': pawn.get('bCanBeGrabbed'), 'lie': pawn.get('bLyingOnGround'),
            'term': pawn.get('bCanBeTerminated'), 'sleep': pawn.get('bSleeping'), 'wake': pawn.get('iLastWakeTime'),
            'att': self.cg.obj_name(pawn.get('m_AttackTarget')) if pawn.get('m_AttackTarget') else None,
            'by': self.cg.obj_name(pawn.get('m_AttackedBy')) if pawn.get('m_AttackedBy') else None,
            'crot': pc.get('Rotation'), 'mdr': pc.get('MyDesiredRotation'), 'lmdr': pc.get('LocalMyDesiredRotation'),
            'pad': pc.get('iDontUpdatePadInput'),
            'stop': pc.get('iTimeToStopStickyShock'), 'elec': pc.get('iTimeElecStickyShock'),
            'tshock': pc.get('TimeShock'), 'tgrab': pc.get('TimeShockForGrab'), 'inv': pc.get('RealTimeInvulSticky'),
            'prot': pc.get('fSpawnProtectionEnd'), 'taz': pc.get('bTazered'), 'smoke': pc.get('bInSmoke'),
            'cough': pc.get('btoCought'), 'fall': pc.get('fSmoke_FallDate'),
            'coughEnd': pc.get('fSmoke_EndOfCoughDate'), 'wakeUp': pc.get('fLastTimeWakeUp'),
            'hit': pc.get('fTimeWhenHit'), 'elbowAt': pc.get('fLastElbowAttackTime'),
            'chargeAt': pc.get('fLastChargingTime'), 'berserkAt': pc.get('fLastBerserkTime'),
            'grabAt': pc.get('fGrabMatchStartedTime'), 'release': pc.get('GrabReleaseMethode'),
            'lying': pc.get('fLyingOnGroundDuration'), 'fwd': pc.get('aForward'), 'str': pc.get('aStrafe'),
            'btn': [pc.get(k) for k in ('bFire', 'bAltFire', 'bUsing', 'bFireMode', 'bReload', 'bCrouch', 'BT_Up')],
            'hist': s['hist'],
            'anim': [[c, self.cg.name(seq), fr, a] for c, seq, fr, rate, a in s['anim']],
        }
        if 'EnergyLevel' in pawn:
            out['energy'] = pawn.get('EnergyLevel')
            out['refill'] = [pawn.get('HealthRefill'), pawn.get('NbPointsRefillable')]
        hud = s.get('hud')
        if hud:
            out['hud'] = [hud.get('bPerturbation'), hud.get('HudPerturbationLength'), hud.get('TimetobeginPerturb'),
                          hud.get('bProjection'), hud.get('bNeigeDisp')]
        if w is not None:
            out['w'] = {'cls': self.cg.class_name(ptrs[2]), 'clip': w.get('ClipAmmo'), 'ammo': w.get('Ammo'),
                        'rof': w.get('ROFMode'), 'bir': w.get('BulletInARow'), 'btf': w.get('BulletsToFire'),
                        'disp': w.get('DispersionActuelle'), 'acc': w.get('AccurateRotation'),
                        'tar': w.get('TimeAccReset'), 'last': w.get('LastShot'), 'loc': w.get('Location'),
                        'rot': w.get('Rotation')}
        return out

    def frame(self, f, s, sent, inputs, late, gap, wall0, impacts):
        lvl, rig = s['level'], s['rig']
        return {'f': f, 't': s['t'], 'dt': lvl.get('m_dT'), 'wall': round(s['wallAt'] - wall0, 5),
                'late': int(late), 'gap': gap, 'tries': s['tries'], **({} if s['stable'] else {'unstable': 1}),
                'ack': rig.get('Ack'), 'st': rig.get('Status'), 'sent': sent, 'in': inputs,
                'L': self.side(s['local']), 'T': self.side(s['target']),
                'g': s['grenade'], 'gs': [rig.get('GrenadeSpawnedAt'), rig.get('GrenadeGoneAt'),
                                          rig.get('bGrenadeRigFuse'), rig.get('GrenadeLastLocation')],
                'imp': impacts}


# -- playing a scenario -----------------------------------------------------------------------------------------------

class Runner:
    def __init__(self, cg, rig, log=sb.log):
        self.cg = cg
        self.rig = rig
        self.log = log
        self.rec = Recorder(cg)
        self.prev_t = cg.time_seconds()
        self.last = None
        self.pending = None

    def tick(self, n=1):
        s = None
        for _ in range(n):
            s = self.cg.next_sample(self.prev_t)
            self.prev_t = s['t']
        self.last = s
        return s

    def cmd(self, code, timeout=3.0, **args):
        """A rig command outside the timed part (setup): waits for it."""
        return self.rig.cmd(code, timeout=timeout, **args)

    # -- setup ---------------------------------------------------------------------------------------------------

    def local_stance(self, crouched, tries=4):
        for attempt in range(tries + 1):
            s = self.tick()
            if bool(s['local']['pawn']['bIsCrouched']) == crouched:
                if attempt:
                    self.tick(30)
                return {'presses': attempt}
            self.cg.write_input('bCrouch', 1)
            self.tick(3)
            self.cg.write_input('bCrouch', 0)
            for _ in range(90):
                s = self.tick()
                if bool(s['local']['pawn']['bIsCrouched']) == crouched:
                    break
            self.tick(36)
        raise RuntimeError('the local player did not reach crouched=%s' % crouched)

    def local_gun(self, drawn, tries=4):
        """A spy draws or holsters the shocker with the fire-mode key (a toggle); a merc is always armed."""
        for attempt in range(tries + 1):
            s = self.tick()
            wl = s['local']['pawn'].get('WeaponLoaded') or 0
            if (wl != 0) == drawn or s['local']['pawn'].get('EnergyLevel') is None:
                if attempt:
                    self.tick(30)
                return {'presses': attempt}
            self.cg.write_input('bFireMode', 1)
            self.tick(4)
            self.cg.write_input('bFireMode', 0)
            self.tick(60)
        raise RuntimeError('the spy gun did not reach drawn=%s' % drawn)

    def target_settled_stance(self, max_frames=240, still=30):
        """The target's stance once bIsCrouched and CollisionHeight stopped changing (a spawn or a stance change
        finishes over several frames)."""
        last, same = None, 0
        for _ in range(max_frames):
            s = self.tick()
            t = s['target']
            if t is None:
                raise RuntimeError('no target')
            key = (t['pawn']['bIsCrouched'], t['pawn']['CollisionHeight'])
            same = same + 1 if key == last and t['pawn']['Physics'] == 1 else 0
            last = key
            if same >= still:
                return bool(key[0])
        raise RuntimeError('the target stance did not settle: %s' % (last,))

    def target_stance(self, crouched, tries=3):
        for attempt in range(tries + 1):
            if self.target_settled_stance() == crouched:
                return {'presses': attempt}
            # First the crouch button's handler, then the actions it would start.
            self.cmd(cr.CMD_DUMMY_CROUCH, I0=int(crouched), I1=0 if attempt == 0 else 1)
        raise RuntimeError('the target did not reach crouched=%s' % crouched)

    def wait_still(self, max_frames):
        still = 0
        for n in range(max_frames):
            s = self.tick()
            moving = False
            for side in (s['local'], s['target']):
                if side is None or side['pawn'] is None:
                    continue
                v = side['pawn']['Velocity']
                if (livegame.horizontal_speed(v) > STILL_SPEED or abs(v[2]) > STILL_SPEED
                        or side['pawn']['Physics'] != 1):
                    moving = True
            still = 0 if moving else still + 1
            if still >= 10:
                return {'frames': n + 1}
        return {'frames': max_frames, 'timedOut': True}

    def spawn_target(self, t):
        x, y, z = t['floor']
        self.cmd(cr.CMD_SPAWN, I0=cr.TEAM[t['team']], F0=float(x), F1=float(y), F2=float(z), I1=int(t['yaw']),
                 I2=int(bool(t.get('atStart'))), I3=0, F3=PLACE_LIFT)
        self.tick(3)
        info = {'startSpot': _r(self.rig.vec('StartSpotLocation')), 'spawnedAt': _r(self.rig.vec('V2'))}
        self.tick(20)
        return info

    def target_walking(self):
        """The target's controller in its walking state. Landing states end in the owner's PlayerTick, which the
        target does not run, so after a placement the rig calls GotoWaitingState (what the owner would reach)."""
        s = self.tick()
        state = self.rec.node(s['target']['nodes'][1]) or ''
        if state.startswith('s_PlayerWalking') and '_T_' not in state:
            return {'state': state}
        self.cmd(cr.CMD_DUMMY_WAITING)
        s = self.tick(10)
        return {'was': state, 'state': self.rec.node(s['target']['nodes'][1])}

    def place_target(self, t, tries=4):
        """Put the target on its spot, 1 uu above the floor plus its collision height (a stance change in progress
        changes that height: wait it out), until it stands there walking."""
        x, y, z = t['floor']
        for attempt in range(tries):
            self.tick(30)
            self.cmd(cr.CMD_PLACE, F0=float(x), F1=float(y), F2=float(z), I1=int(t['yaw']), F3=PLACE_LIFT)
            self.tick(30)
            for _ in range(120):
                s = self.tick()
                p = s['target']['pawn']
                want = z + p['CollisionHeight'] + 1.0
                if p['Physics'] == 1 and abs(p['Location'][2] - want) < 3.0 and abs(p['Location'][1] - y) < 3.0:
                    return {'tries': attempt + 1, 'z': round(p['Location'][2], 3)}
                if abs(p['Location'][2] - want) >= 3.0 and p['Physics'] == 1:
                    break
        raise RuntimeError('the target did not stay on its spot: %s, Physics %s, CollisionHeight %s, state %s' % (
            p['Location'], p['Physics'], p['CollisionHeight'], self.rec.node(s['target']['nodes'][1])))

    def setup(self, sc):
        info = {}
        loc = sc['local']
        x, y, z = loc['floor']
        self.release_inputs()
        self.cmd(cr.CMD_REMOVE)
        self.tick(2)
        self.cmd(cr.CMD_TELEPORT, F0=float(x), F1=float(y), F2=float(z), I1=int(loc['yaw']))
        self.tick(5)
        info['localStance'] = self.local_stance(bool(loc.get('crouched', False)))
        self.cmd(cr.CMD_TELEPORT, F0=float(x), F1=float(y), F2=float(z), I1=int(loc['yaw']))
        self.cmd(cr.CMD_LOCAL_HEALTH, I0=100)
        self.cmd(cr.CMD_REFILL, I0=0)
        t = sc.get('target')
        if t is not None:
            info['target'] = self.spawn_target(t)
            if not t.get('atStart'):
                info['targetStance'] = self.target_stance(bool(t.get('crouched', False)))
                info['targetPlace'] = self.place_target(t)
                info['targetState'] = self.target_walking()
            self.cmd(cr.CMD_HEALTH, I0=100)
            self.cmd(cr.CMD_REFILL, I0=1)
            self.cmd(cr.CMD_CLEAR_HISTORY)
        for op in sc.get('setup', []):
            kind = op['op']
            if kind == 'stance':
                info['stance'] = self.local_stance(bool(op['crouched']))
            elif kind in ('draw', 'holster'):
                info[kind] = self.local_gun(kind == 'draw')
            elif kind == 'refill':
                self.cmd(cr.CMD_REFILL, I0=1 if op.get('who') == 'target' else 0)
            elif kind == 'rof':
                self.cmd(cr.CMD_ROF, I0=cs.ROF[op['mode']])
            elif kind == 'heal':
                self.cmd(cr.CMD_HEALTH if op.get('who') == 'target' else cr.CMD_LOCAL_HEALTH, I0=int(op['hp']))
            elif kind == 'clearHistory':
                self.cmd(cr.CMD_CLEAR_HISTORY)
            elif kind == 'targetRole':
                self.cmd(cr.CMD_DUMMY_ROLE, I0=0 if op['role'] == 'host' else 3)
            elif kind == 'wait':
                self.tick(int(op['frames']))
            elif kind == 'waitStill':
                info['still'] = self.wait_still(int(op['maxFrames']))
        return info

    def release_inputs(self):
        for name in cs.INPUTS:
            try:
                self.cg.write_input(name, 0)
            except KeyError:
                pass
        for code in (cr.CMD_FIRE_UP, cr.CMD_ALT_UP):
            try:
                self.cmd(code)
            except cr.RigError:
                pass

    # -- the timed part ------------------------------------------------------------------------------------------

    def _floor_of(self, side):
        p = side['pawn']
        loc = p['Location']
        return [loc[0], loc[1], loc[2] - p['CollisionHeight']]

    def rig_args(self, ev, s):
        """(command, arguments) of a rig event, computed from the last sample where it depends on positions."""
        do = ev['do']
        if do == 'aim':
            off = ev.get('offset', [0, 0, 0])
            return cr.CMD_AIM, dict(I0=cr.BONES[ev['bone']], I1=cr.BONES[ev['to']] if ev.get('to') else 0,
                                    F0=float(off[0]), F1=float(off[1]), F2=float(off[2]))
        if do == 'aimPoint':
            p = ev['point']
            return cr.CMD_AIM, dict(I0=0, I1=0, F0=float(p[0]), F1=float(p[1]), F2=float(p[2]))
        if do in ('fire', 'fireUp', 'altFire', 'altFireUp', 'clearHistory', 'removeTarget'):
            return {'fire': cr.CMD_FIRE, 'fireUp': cr.CMD_FIRE_UP, 'altFire': cr.CMD_ALT, 'altFireUp': cr.CMD_ALT_UP,
                    'clearHistory': cr.CMD_CLEAR_HISTORY, 'removeTarget': cr.CMD_REMOVE}[do], {}
        if do == 'rof':
            return cr.CMD_ROF, dict(I0=cs.ROF[ev['mode']])
        if do == 'heal':
            return cr.CMD_HEALTH, dict(I0=int(ev['hp']))
        if do == 'healLocal':
            return cr.CMD_LOCAL_HEALTH, dict(I0=int(ev['hp']))
        if do in ('place', 'teleport'):
            x, y, z = ev['floor']
            return (cr.CMD_PLACE if do == 'place' else cr.CMD_TELEPORT), dict(F0=float(x), F1=float(y), F2=float(z),
                                                                              I1=int(ev['yaw']), F3=PLACE_LIFT)
        if do == 'crouchTarget':
            return cr.CMD_DUMMY_CROUCH, dict(I0=int(ev['crouched']), I1=1)
        if do == 'grenade':
            if 'at' in ev:
                at = ev['at']
            else:
                side = s['target'] if 'fromTarget' in ev else s['local']
                base = self._floor_of(side)
                off = ev.get('fromTarget') or ev.get('fromLocal')
                at = [base[i] + off[i] for i in range(3)]
            return cr.CMD_GRENADE, dict(I0=cs.GRENADES[ev['kind']], I1=1 if ev.get('by') == 'target' else 0,
                                        F0=float(at[0]), F1=float(at[1]), F2=float(at[2]), F3=float(ev.get('fuse', 0)))
        if do == 'targetFire':
            off = ev.get('offset', [0, 0, 0])
            return cr.CMD_DUMMY_FIRE, dict(I0=cr.BONES[ev['bone']], F0=float(off[0]), F1=float(off[1]),
                                           F2=float(off[2]), I1=1)
        if do == 'spawnTarget':
            x, y, z = ev['floor']
            return cr.CMD_SPAWN, dict(I0=cr.TEAM[ev['team']], F0=float(x), F1=float(y), F2=float(z),
                                      I1=int(ev['yaw']), I2=int(bool(ev.get('atStart'))), I3=0, F3=PLACE_LIFT)
        if do == 'refill':
            return cr.CMD_REFILL, dict(I0=1 if ev.get('who') == 'target' else 0)
        if do == 'targetRole':
            return cr.CMD_DUMMY_ROLE, dict(I0=0 if ev['role'] == 'host' else 3)
        raise ValueError('not a rig event: %s' % do)

    def apply(self, f, by_frame, held, s):
        """Write frame f's inputs and its rig command (if any); returns (sent, inputs, late)."""
        inputs = held[f]
        for name in self.used_inputs:
            self.cg.write_input(name, inputs.get(name, 0))
        sent = []
        for ev in by_frame.get(f, []):
            if ev['do'] in cs.RIG_EVENTS:
                if self.pending is not None and not self.rig.done(self.pending):
                    self.rig.wait(self.pending, timeout=1.0)
                code, args = self.rig_args(ev, s)
                seq = self.rig.send(code, **args)
                self.pending = seq
                sent.append([seq, ev['do']])
        late = self.cg.time_seconds() != self.prev_t
        return sent, {k: v for k, v in inputs.items() if v}, late

    def play(self, sc, path, header):
        by_frame = cs.events_by_frame(sc)
        held = cs.held_inputs(sc)
        self.pending = None
        self.used_inputs = sorted({k for ev in sc['events'] if ev['do'] == 'buttons' for k in ev['buttons']})
        info = self.setup(sc)
        last = self.tick(max(1, sc['settleFrames']))
        self.cg.new_impacts(last['rig'].get('NumImpacts') or 0)
        header = dict(header, setup=info, timeDilation=last['level'].get('TimeDilation'), offsets=self.cg.offsets())
        writer = tr.TraceWriter(path, dict({'format': TRACE_FORMAT}, **header))
        wall0 = time.perf_counter()
        last['wallAt'] = wall0
        writer.frame(self.rec.frame(-1, last, [], {}, False, 0, wall0, []))
        sent, inputs, late = self.apply(0, by_frame, held, last)
        stats = {'torn': 0, 'gaps': 0, 'late': 0, 'unstable': 0, 'dts': []}
        duration = sc['durationFrames']
        f = 0
        t_first = None
        try:
            while f < duration:
                s = self.cg.next_sample(self.prev_t)
                s['wallAt'] = time.perf_counter()
                dt = s['level'].get('m_dT') or 0.0
                step = s['t'] - self.prev_t
                gap = max(0, int(round(step / dt)) - 1) if dt > 0 else 0
                gap = max(gap, s['torn'])
                if gap:
                    stats['gaps'] += gap
                self.prev_t = s['t']
                t_first = s['t'] if t_first is None else t_first
                stats['torn'] += s['torn']
                stats['late'] += int(late)
                stats['unstable'] += 0 if s['stable'] else 1
                stats['dts'].append(dt)
                impacts = self.cg.new_impacts(s['rig'].get('NumImpacts') or 0)
                writer.frame(self.rec.frame(f, s, sent, inputs, late, gap, wall0, impacts))
                self.last = s
                f += 1
                if f < duration:
                    sent, inputs, late = self.apply(f, by_frame, held, s)
        finally:
            for name in self.used_inputs:
                self.cg.write_input(name, 0)
        dts = stats.pop('dts') or [0.0]
        footer = dict(stats, wallSeconds=round(time.perf_counter() - wall0, 3),
                      gameSeconds=tr.f32(self.prev_t - t_first) if t_first is not None else 0.0,
                      dtMean=sum(dts) / len(dts), dtMin=min(dts), dtMax=max(dts),
                      fps=len(dts) / max(1e-9, (self.prev_t - t_first)) if t_first is not None else 0.0)
        writer.close(footer)
        return footer


# -- the rig map ------------------------------------------------------------------------------------------------------

def rig_map_name(base):
    return base + 'Combat'


def ensure_rig_map(tools, root, base, out, force=False):
    """Build the rig map when it is missing, when its record is missing, or when the rig sources changed."""
    name = rig_map_name(base)
    installed = os.path.join(root, 'Packages', 'MapsEd', name + '.sdc')
    record_path = os.path.join(out, '_rigmap', name + '.json')
    record = None
    if os.path.isfile(record_path):
        with open(record_path, encoding='utf-8') as f:
            record = json.load(f)
    fresh = (record is not None and os.path.isfile(installed) and record.get('rigSha256') == cr.rig_sources_sha256()
             and record.get('map', {}).get('sha256') == cr.sha256(installed))
    if fresh and not force:
        return record
    info = cr.build_map(tools, sb, root, base=base, name=name)
    os.makedirs(os.path.dirname(record_path), exist_ok=True)
    with open(record_path, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(info, f, indent=1, default=str)
    return info


# -- commands ---------------------------------------------------------------------------------------------------------

def load_scenarios(patterns, only=None):
    out = []
    for pattern in patterns:
        for path in sorted(glob.glob(pattern)) or [pattern]:
            out.append(cs.load(path))
    if only:
        wanted = set(only.split(','))
        out = [s for s in out if s['id'] in wanted or s['id'].split('.', 1)[1] in wanted]
    return out


def record(args):
    import combat_measure as cm
    scenarios = load_scenarios(args.scenarios, args.only)
    if not scenarios:
        raise SystemExit('no scenarios')
    groups = {}
    for s in sorted(scenarios, key=lambda d: (d.get('order', 50), d['id'])):
        groups.setdefault((s['map'], s['local']['team']), []).append(s)
    tools = sb.load_tools(args.re_tools)
    root = os.path.abspath(args.sandbox)
    out = os.path.abspath(args.out)
    hashes = sb.binaries(root)
    rig_maps = {m: ensure_rig_map(tools, root, m, out, force=args.build) for m in sorted({s['map'] for s in scenarios})}
    results = []
    livegame.fine_timer(True)
    try:
        with sb.Sandbox(tools, root, fps=args.fps) as box:
            for (map_name, team), group in sorted(groups.items()):
                sessions = [[r] for r in range(1, args.runs + 1)] if args.game_per_run else [
                    list(range(1, args.runs + 1))]
                for index, run_numbers in enumerate(sessions, 1):
                    map_info = box.load_map(rig_map_name(map_name))
                    with box.play(team, lock_timeout=args.lock_timeout) as (session, game_info):
                        rig = cr.Rig(session, log=sb.log)
                        cg = combat_live.CombatGame(session, rig, delay=args.delay / 1000.0)
                        runner = Runner(cg, rig)
                        sb.log('game %d of %d: %d scenarios as %s, runs %s' % (index, len(sessions), len(group), team,
                                                                             run_numbers))
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
                                    'map': {'name': map_name, 'rigMap': rig_map_name(map_name),
                                            'mapsEd': {'path': map_info['path'], 'sha256': map_info['sha256']},
                                            'autoplaySha256': game_info.get('autoplaySha256'),
                                            'rigBuild': rig_maps.get(map_name)},
                                    'rig': {'sources': [rel(os.path.join(cr.RIG_DIR, n + '.uc'))
                                                        for n, _ in cr.RIG_CLASSES],
                                            'sha256': cr.rig_sources_sha256()},
                                    'binaries': hashes, 'fps': {'cap': args.fps},
                                    'localClasses': list(cg.local_classes),
                                    'recorder': {'tool': 're/tools/difftest/combat.py', 'version': RECORDER_VERSION,
                                                 'sampleDelayMs': args.delay, 'python': platform.python_version()},
                                    'sandbox': root, 'startedAt': time.strftime('%Y-%m-%dT%H:%M:%S'),
                                }
                                path = os.path.join(folder, 'run%d.jsonl.gz' % run)
                                t = time.time()
                                try:
                                    footer = runner.play(sc, path, header)
                                except (cr.RigError, RuntimeError, TimeoutError) as error:
                                    sb.log('%s run %d FAILED: %s' % (sc['id'], run, error))
                                    results.append({'id': sc['id'], 'run': run, 'error': str(error)})
                                    if not cr_game_alive(tools, root):
                                        raise
                                    continue
                                sb.log('%s run %d: %d frames, %.1f fps, torn %d, gaps %d, late %d, unstable %d (%.1fs)'
                                       % (sc['id'], run, sc['durationFrames'], footer['fps'], footer['torn'],
                                          footer['gaps'], footer['late'], footer['unstable'], time.time() - t))
                                results.append({'id': sc['id'], 'run': run, 'path': rel(path), **footer})
                for sc in group:
                    try:
                        cm.measure_folder(os.path.join(out, sc['id']))
                    except Exception as error:  # noqa: BLE001 - a measurement bug must not lose the traces
                        sb.log('measure %s failed: %s' % (sc['id'], error))
        report = dict(box.report)
    finally:
        livegame.fine_timer(False)
    report['results'] = results
    runs_dir = os.path.join(out, '_runs')
    os.makedirs(runs_dir, exist_ok=True)
    with open(os.path.join(runs_dir, time.strftime('%Y%m%d-%H%M%S') + '.json'), 'w', encoding='utf-8') as f:
        json.dump(report, f, indent=1, default=str)
    return report


def cr_game_alive(tools, root):
    return bool(tools.game_tools.sandbox_games(root))


def check(paths):
    files = []
    for p in paths:
        files += glob.glob(os.path.join(p, '**', '*.combat.json'), recursive=True) if os.path.isdir(p) else [p]
    bad = 0
    ids = {}
    for path in sorted(files):
        try:
            sc = cs.load(path)
            ids.setdefault(sc['id'], []).append(path)
            print('ok   %s (%s, %d frames, %d events)' % (rel(path), sc['id'], sc['durationFrames'], len(sc['events'])))
        except (cs.ScenarioError, ValueError) as error:
            bad += 1
            print('FAIL %s' % error)
    for i, ps in ids.items():
        if len(ps) > 1:
            bad += 1
            print('FAIL duplicate id %s: %s' % (i, ps))
    return bad


def read_trace(path):
    import gzip
    with gzip.open(path, 'rt', encoding='utf-8') as f:
        lines = [json.loads(line) for line in f if line.strip()]
    if not lines or lines[0].get('format') != TRACE_FORMAT:
        raise ValueError('%s is not a %s file' % (path, TRACE_FORMAT))
    footer = lines[-1] if lines[-1].get('end') else None
    return lines[0], [x for x in lines[1:] if 'f' in x], footer


def show(path, every=1):
    header, frames, footer = read_trace(path)
    print('# %s run %s local %s, setup %s' % (header['scenario']['id'], header['run'], header['team'],
                                            json.dumps(header.get('setup'))))
    prev = None
    for fr in frames:
        L, T = fr['L'] or {}, fr['T'] or {}
        key = (L.get('cs'), L.get('ws'), L.get('hp'), T.get('cs'), T.get('hp'), L.get('pad'), T.get('pad'))
        if fr['f'] % every and key == prev and not fr['sent'] and not fr['imp']:
            continue
        prev = key
        w = L.get('w') or {}
        print('%4d t=%8.4f L[%s %s hp=%s w=%s clip=%s bir=%s disp=%.3f pad=%s] T[%s hp=%s pad=%s hist=%s] %s %s' % (
            fr['f'], fr['t'], L.get('cs'), L.get('ps'), L.get('hp'), L.get('ws'), w.get('clip'), w.get('bir'),
            w.get('disp') or 0.0, L.get('pad'), T.get('cs'), T.get('hp'), T.get('pad'), (T.get('hist') or [None])[0],
            fr['sent'] or '', fr['imp'] or ''))
    print('# footer', json.dumps(footer))


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n\n')[0])
    sub = ap.add_subparsers(dest='cmd', required=True)
    p = sub.add_parser('build', help='compile the rig into <map>Combat and install it in the project sandbox')
    p.add_argument('--map', default=cr.DEFAULT_BASE_MAP)
    p.add_argument('--sandbox', default=sb.DEFAULT_SANDBOX)
    p.add_argument('--re-tools', default=os.environ.get('SVM_RE_TOOLS', sb.DEFAULT_RE_TOOLS))
    p.add_argument('--out', default=DEFAULT_OUT)
    p = sub.add_parser('record', help='play combat scenarios in the original and write traces')
    p.add_argument('scenarios', nargs='*', default=[DEFAULT_SCENARIOS])
    p.add_argument('--runs', type=int, default=1)
    p.add_argument('--out', default=DEFAULT_OUT)
    p.add_argument('--sandbox', default=sb.DEFAULT_SANDBOX)
    p.add_argument('--re-tools', default=os.environ.get('SVM_RE_TOOLS', sb.DEFAULT_RE_TOOLS))
    p.add_argument('--fps', type=int, default=60)
    p.add_argument('--delay', type=float, default=2.0, help='ms after a frame starts before sampling it')
    p.add_argument('--lock-timeout', type=int, default=3600)
    p.add_argument('--only', help='comma-separated scenario ids')
    p.add_argument('--build', action='store_true', help='rebuild the rig map first')
    p.add_argument('--game-per-run', action='store_true', help='run N of every scenario in game N')
    p = sub.add_parser('check', help='validate combat scenario files')
    p.add_argument('paths', nargs='*', default=[os.path.join(REPO, 'tests', 'scenarios', 'combat')])
    p = sub.add_parser('measure', help='measure every recorded scenario and write results.json and RESULTS.md')
    p.add_argument('root', nargs='?', default=DEFAULT_OUT)
    p = sub.add_parser('show', help='print a trace as a table')
    p.add_argument('path')
    p.add_argument('--every', type=int, default=1)
    args = ap.parse_args()
    sys.stdout.reconfigure(encoding='utf-8')
    if args.cmd == 'build':
        tools = sb.load_tools(args.re_tools)
        print(json.dumps(ensure_rig_map(tools, os.path.abspath(args.sandbox), args.map, os.path.abspath(args.out),
                                        force=True), indent=1, default=str))
    elif args.cmd == 'record':
        report = record(args)
        print(json.dumps({'results': report['results'], 'killed': report.get('killed')}, indent=1, default=str))
    elif args.cmd == 'check':
        sys.exit(1 if check(args.paths) else 0)
    elif args.cmd == 'measure':
        import combat_measure as cm
        print(cm.measure_all(os.path.abspath(args.root)))
    elif args.cmd == 'show':
        show(args.path, args.every)


if __name__ == '__main__':
    main()
