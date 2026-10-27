"""Numbers from combat traces (svm.combat-trace/1), compared with the decoded specs.

`events(frames)` turns a run into a time-ordered list of what changed: controller, pawn and weapon states of both
players, health, damage history entries (type, damage, pill), shots (the local rifle's clip going down), bullet
impacts, input freeze and shock flags, cough, energy, grenades and the rig commands. The measures named in a
scenario's `measure` object then read that list:

- `hits`: every damage entry on the target from a local shot: pill, damage, the shot distance (eye to impact), the
  spec damage for that distance and pill (hit-zones-and-weapons.md rules 7 to 9, float32);
- `shots`: every local rifle shot: time, cadence, BulletInARow, DispersionActuelle against GetAccuracy, the aim kick
  (MyDesiredRotation) against ApplyWeaponJump and its decay, the impact's place in the spread square;
- `reload`: the s_Reloading stretches of the local rifle: start, clip refill, end, against the 31 / 15 s clip;
- `shock`: the victim's freeze and electric times, and the whole-second deadlines of shock-and-taser.md rule 17;
- `energy`: the local spy's EnergyLevel per shot and its refill steps;
- `blast`: every grenade: where it went off, each player's distance, damage entry, state and blast velocity after,
  against natives2-damage.md rule 4, the pipeline's explosion rules and the blast speed law;
- `states`: the state sequence of both controllers (grab, melee, smoke, death) with times;
- `deaths`: each death of the target: where the shooter was, the death clip.

Single-player falls (svm.trace/1 from record.py) get `falls`: the drop at landing and the health lost, against
blast-and-fall-damage.md rule 15.

`measure_all(root)` measures every scenario folder and writes results.json and RESULTS.md (game-derived: they stay
under local/).
"""
from __future__ import annotations

import glob
import gzip
import json
import math
import os

try:
    import numpy as np
except ImportError:  # pragma: no cover - the RE tools need numpy anyway
    np = None

TRACE_FORMAT = 'svm.combat-trace/1'
DAMAGE_TYPES = ('NoDamageType', 'SDamage_Charged', 'SDamage_DropAttacked', 'SDamage_ElbowShock', 'SDamage_Explosion',
                'SDTBase', 'SDTBase_NoAnim', 'SDTExtincteurProjection', 'SDTFlash', 'SDTFragFlash', 'SDTLSD',
                'SDTSnipeShot', 'SDTStickyShocker', 'SFell', 'Crushed', 'Suicided', 'Gibbed', 'DamTypeTelefragged',
                'DamTypeGrabChoke', 'DamTypeGrabBreak', 'SDamage_Jammered', 'SDamage_Poison', 'SDTStickyBlast')
# Spec values (combat/hit-zones-and-weapons.md, equipment/merc-rifle.md, natives2-damage.md rule 4).
ZONE_TABLE = (300, 100, 40, 40, 20, 100, 100, 20, 100, 40, 40, 20, 100, 100, 20)
PILL_NAMES = ('head', 'torso L', 'upper arm L', 'forearm L', 'hand L', 'thigh L', 'calf L', 'foot L', 'torso R',
              'upper arm R', 'forearm R', 'hand R', 'thigh R', 'calf R', 'foot R')
RIFLE = {'base': 17, 'cmax': 1.471, 'cac': 1.471, 'med': 0.882, 'min': 0.588, 'dcac': 300.0, 'dmed': 1200.0,
         'dloin': 1800.0, 'minDisp': 0.15, 'maxDisp': 0.7, 'rof': 0.2, 'reload': 31.0 / 15.0}
FRAG = {'damage': 334, 'range': 500.0, 'blast': 350.0}
SHOCK = {'merc': (3.5, 1.0), 'spy': (3.0, 0.0)}


# -- reading ----------------------------------------------------------------------------------------------------------

def read_trace(path):
    with gzip.open(path, 'rt', encoding='utf-8') as f:
        lines = [json.loads(line) for line in f if line.strip()]
    if not lines or lines[0].get('format') != TRACE_FORMAT:
        raise ValueError('%s is not a %s file' % (path, TRACE_FORMAT))
    footer = lines[-1] if lines[-1].get('end') else None
    return lines[0], [x for x in lines[1:] if 'f' in x], footer


def f32(x):
    return float(np.float32(x)) if np is not None else x


def rifle_damage(d, pill=None, w=RIFLE):
    """TraceFire's distance damage in float32, one operation at a time, then the victim's zone table (SRTD)."""
    if np is None:
        raise RuntimeError('numpy is needed for the float32 damage formula')
    F = np.float32
    d = F(d)
    if d < F(w['dcac']):
        c = (F(w['cac']) - F(w['cmax'])) / F(w['dcac']) * d + F(w['cmax'])
    elif d < F(w['dmed']):
        c = (F(w['med']) - F(w['cac'])) / (F(w['dmed']) - F(w['dcac'])) * (d - F(w['dcac'])) + F(w['cac'])
    elif d < F(w['dloin']):
        c = (F(w['min']) - F(w['med'])) / (F(w['dloin']) - F(w['dmed'])) * (d - F(w['dmed'])) + F(w['med'])
    else:
        c = F(w['min'])
    dmg = int(F(w['base']) * c)
    if pill is None or not 1 <= pill <= 15:
        return dmg
    return int(F(dmg) * F(ZONE_TABLE[pill - 1]) * F(0.01))


def accuracy(speed, bullets, w=RIFLE):
    """GetAccuracy (merc-rifle.md rule 13)."""
    return min(speed / 600.0 + w['minDisp'] + 0.1 * bullets, w['maxDisp'])


def frag_expect(d, victim_team, protected=False):
    """natives2-damage.md rule 4 and the pipeline: (damage type, damage taken)."""
    if d >= FRAG['range']:
        return None, 0
    if d < FRAG['blast']:
        if protected:
            return 'SDamage_Explosion', 0
        return 'SDamage_Explosion', 200
    return 'SDTBase', int((1.0 - f32(d) / FRAG['range']) * FRAG['damage'])


def dist(a, b):
    return math.sqrt(sum((a[i] - b[i]) ** 2 for i in range(3)))


def _hist_key(h):
    return tuple(h) if h else None


# -- events -----------------------------------------------------------------------------------------------------------

def new_history(prev, cur):
    """Damage entries added between two snapshots of LastDamages (newest first, shifted by one per entry)."""
    if not cur:
        return []
    if not prev:
        return [h for h in cur if h[0] or h[1]][::-1]
    for shift in range(0, len(cur) + 1):
        if shift == len(cur) or [tuple(x) for x in cur[shift:]] == [tuple(x) for x in prev[:len(cur) - shift]]:
            # Rows with no damage type are not hits (a cleared history, or one the death code reset).
            return [h for h in reversed(cur[:shift]) if h[0]]
    return []


def events(frames):
    out = []
    prev = None
    for fr in frames:
        f, t = fr['f'], fr['t']
        for seq, do in fr.get('sent') or []:
            out.append({'f': f, 't': t, 'kind': 'sent', 'seq': seq, 'do': do})
        for imp in fr.get('imp') or []:
            out.append({'f': f, 't': t, 'kind': 'impact', 'at': imp[:3], 'time': imp[3], 'start': imp[4:7]})
        for who in ('L', 'T'):
            cur = fr.get(who)
            old = (prev or {}).get(who) if prev else None
            if cur is None:
                if old is not None:
                    out.append({'f': f, 't': t, 'kind': 'gone', 'who': who})
                continue
            if old is None:
                out.append({'f': f, 't': t, 'kind': 'appear', 'who': who, 'cs': cur.get('cs'), 'hp': cur.get('hp')})
                old = {}
            for key, kind in (('cs', 'state'), ('ws', 'weaponState'), ('ps', 'pawnState'), ('hp', 'health'),
                              ('pad', 'padFreeze'), ('sticky', 'sticky'), ('cough', 'cough'), ('smoke', 'smoke'),
                              ('crouch', 'crouch'), ('phys', 'physics'), ('wl', 'weaponLoaded'), ('lie', 'lying'),
                              ('taz', 'tazered'), ('att', 'attackTarget'), ('by', 'attackedBy')):
                if old and cur.get(key) != old.get(key):
                    out.append({'f': f, 't': t, 'kind': kind, 'who': who, 'from': old.get(key), 'to': cur.get(key)})
            if 'energy' in cur and old and cur.get('energy') != old.get('energy'):
                out.append({'f': f, 't': t, 'kind': 'energy', 'who': who, 'from': old.get('energy'),
                            'to': cur.get('energy')})
            for h in new_history(old.get('hist') if old else None, cur.get('hist')) if old else []:
                out.append({'f': f, 't': t, 'kind': 'damage', 'who': who, 'type': h[0],
                            'typeName': DAMAGE_TYPES[h[0]] if 0 <= h[0] < len(DAMAGE_TYPES) else h[0],
                            'damage': h[1], 'pill': h[2], 'time': h[3]})
            w, ow = cur.get('w'), (old or {}).get('w')
            if w and ow and w.get('clip') is not None and ow.get('clip') is not None and w['clip'] < ow['clip']:
                out.append({'f': f, 't': t, 'kind': 'shot', 'who': who, 'clip': w['clip'], 'bir': w.get('bir'),
                            'disp': w.get('disp'), 'acc': w.get('acc'), 'mdr': cur.get('mdr'),
                            'mdrBefore': old.get('mdr'),
                            'speed': math.hypot(*cur['vel'][:2]) if cur.get('vel') else None,
                            'speedBefore': math.hypot(*old['vel'][:2]) if old.get('vel') else None,
                            'loc': cur.get('loc'), 'eye': cur.get('eye'), 'bob': cur.get('bob'),
                            'shots': ow['clip'] - w['clip']})
            if w and ow and w.get('clip') is not None and ow.get('clip') is not None and w['clip'] > ow['clip']:
                out.append({'f': f, 't': t, 'kind': 'clipRefill', 'who': who, 'from': ow['clip'], 'to': w['clip']})
        gs, ogs = fr.get('gs'), (prev or {}).get('gs')
        if gs and ogs:
            if gs[0] != ogs[0] and gs[0]:
                out.append({'f': f, 't': t, 'kind': 'grenade', 'spawnedAt': gs[0], 'at': gs[3]})
            if gs[1] != ogs[1] and gs[1]:
                out.append({'f': f, 't': t, 'kind': 'grenadeGone', 'goneAt': gs[1], 'rigFuse': gs[2], 'at': gs[3]})
        prev = fr
    return out


# -- measures ---------------------------------------------------------------------------------------------------------

def _frame_at(frames, f):
    for fr in frames:
        if fr['f'] == f:
            return fr
    return None


def measure_hits(frames, evs):
    """Damage entries on the target, each with the shot that made it and its impact."""
    shots = [e for e in evs if e['kind'] == 'shot' and e['who'] == 'L']
    impacts = [e for e in evs if e['kind'] == 'impact']
    rows = []
    for e in evs:
        if e['kind'] != 'damage' or e['who'] != 'T' or e['typeName'] not in ('SDTBase', 'SDTSnipeShot',
                                                                                'SDTStickyShocker'):
            continue
        shot = None
        for s in shots:
            if s['f'] <= e['f'] and e['f'] - s['f'] <= 2:
                shot = s
        if shot is None and e['typeName'] != 'SDTStickyShocker':
            continue
        row = {'f': e['f'], 't': e['t'], 'type': e['typeName'], 'damage': e['damage'], 'pill': e['pill'],
               'zone': PILL_NAMES[e['pill'] - 1] if 1 <= e['pill'] <= 15 else None}
        imp = None
        for i in impacts:
            if shot is not None and 0 <= i['f'] - shot['f'] <= 2:
                imp = i
        if shot is not None:
            eye = list(shot['loc'])
            eye[2] += shot.get('eye') or 0.0
            if shot.get('bob'):
                eye = [eye[i] + shot['bob'][i] for i in range(3)]
            row['eye'] = [round(v, 2) for v in eye]
            if imp is not None:
                d = dist(imp['at'], eye)
                row['impact'] = [round(v, 2) for v in imp['at']]
                row['distance'] = round(d, 2)
                if e['typeName'] == 'SDTBase' and np is not None:
                    row['expectBase'] = rifle_damage(d)
                    row['expect'] = rifle_damage(d, e['pill'])
                    row['match'] = row['expect'] == e['damage']
        rows.append(row)
    return {'hits': rows, 'n': len(rows), 'matched': sum(1 for r in rows if r.get('match')),
            'checked': sum(1 for r in rows if 'match' in r)}


def measure_shots(frames, evs):
    shots = [e for e in evs if e['kind'] == 'shot' and e['who'] == 'L']
    impacts = [e for e in evs if e['kind'] == 'impact']
    rows = []
    prev_t = None
    # The aim the scenario set (the first shot's AccurateRotation): later shots' offsets from it are the kick that
    # was still there when they were fired.
    k_aim = shots[0]['acc'] if shots and shots[0].get('acc') else None
    for k, s in enumerate(shots):
        speed = s['speedBefore'] if s.get('speedBefore') is not None else s.get('speed') or 0.0
        row = {'f': s['f'], 't': s['t'], 'dt': round(s['t'] - prev_t, 4) if prev_t is not None else None,
               'clip': s['clip'], 'bir': s['bir'], 'disp': s['disp'],
               'expectDisp': round(accuracy(speed, s['bir'] or 0), 4) if s.get('bir') is not None else None,
               'speed': round(speed, 2)}
        if s.get('mdr') and s.get('mdrBefore'):
            row['kickPitch'] = s['mdr'][0] - s['mdrBefore'][0]
            row['kickYaw'] = ((s['mdr'][1] - s['mdrBefore'][1] + 32768) % 65536) - 32768
            # The local view (LocalMyDesiredRotation, copied over MyDesiredRotation every tick) for six frames from
            # the shot, against the aim before it: how the kick decays.
            rec = []
            for k in range(6):
                x = (_frame_at(frames, s['f'] + k) or {}).get('L') or {}
                v = x.get('lmdr')
                if v:
                    rec.append([v[0] - s['mdrBefore'][0], ((v[1] - s['mdrBefore'][1] + 32768) % 65536) - 32768])
            row['recovery'] = rec
        if s.get('acc') and k_aim is not None:
            row['aimOff'] = [s['acc'][0] - k_aim[0], ((s['acc'][1] - k_aim[1] + 32768) % 65536) - 32768]
        imp = None
        for i in impacts:
            if 0 <= i['f'] - s['f'] <= 2:
                imp = i
        if imp is not None and s.get('acc'):
            row['impact'] = [round(v, 2) for v in imp['at']]
            eye = list(s['loc'])
            eye[2] += s.get('eye') or 0.0
            row.update(spread_offset(eye, s['acc'], imp['at'], s.get('disp')))
        rows.append(row)
        prev_t = s['t']
    disp_ok = [r for r in rows if r.get('expectDisp') is not None]
    return {'shots': rows, 'n': len(rows),
            'dispMatched': sum(1 for r in disp_ok if abs(r['disp'] - r['expectDisp']) < 1e-4),
            'dispChecked': len(disp_ok),
            'kickPitchRange': [min((r['kickPitch'] for r in rows if 'kickPitch' in r), default=None),
                               max((r['kickPitch'] for r in rows if 'kickPitch' in r), default=None)],
            'kickYawRange': [min((r['kickYaw'] for r in rows if 'kickYaw' in r), default=None),
                             max((r['kickYaw'] for r in rows if 'kickYaw' in r), default=None)],
            'cadence': sorted({r['dt'] for r in rows if r['dt'] is not None})}


def _axes(rot):
    """GetAxes of a rotator (pitch, yaw, roll in 65536ths): X forward, Y right, Z up."""
    k = math.pi / 32768.0
    p, y, r = rot[0] * k, rot[1] * k, rot[2] * k
    sp, cp, sy, cy, sr, cr = math.sin(p), math.cos(p), math.sin(y), math.cos(y), math.sin(r), math.cos(r)
    x = (cp * cy, cp * sy, sp)
    yv = (sr * sp * cy - cr * sy, sr * sp * sy + cr * cy, -sr * cp)
    z = (-(cr * sp * cy + sr * sy), cy * sr - cr * sp * sy, cr * cp)
    return x, yv, z


def spread_offset(eye, rot, at, disp):
    """The impact's offset from the aim line in the spread's own units: TraceFire's end point is
    eye + A (u1 - 0.5) 1000 Y + A (u2 - 0.5) 1000 Z + 15000 X, so the impact direction scaled to 15000 along X gives
    A (u - 0.5) 1000 on Y and Z: |u - 0.5| <= 0.5 when the spread rule holds."""
    x, y, z = _axes(rot)
    d = [at[i] - eye[i] for i in range(3)]
    along = sum(d[i] * x[i] for i in range(3))
    if along <= 1.0:
        return {}
    s = 15000.0 / along
    oy = sum(d[i] * y[i] for i in range(3)) * s
    oz = sum(d[i] * z[i] for i in range(3)) * s
    out = {'offY': round(oy, 2), 'offZ': round(oz, 2)}
    if disp:
        out['uY'] = round(oy / (disp * 1000.0) + 0.5, 4)
        out['uZ'] = round(oz / (disp * 1000.0) + 0.5, 4)
    return out


def measure_reload(frames, evs):
    rows = []
    start = None
    for e in evs:
        if e['kind'] == 'weaponState' and e['who'] == 'L':
            if e['to'] in ('s_Reloading', 's_FullReloading'):
                start = e
            elif start is not None and e['from'] == start['to']:
                refill = [r for r in evs
                          if r['kind'] == 'clipRefill' and r['who'] == 'L' and start['f'] <= r['f'] <= e['f']]
                rows.append({'state': start['to'], 'start': start['t'], 'end': e['t'], 'to': e['to'],
                             'seconds': round(e['t'] - start['t'], 4),
                             'refillAfter': round(refill[0]['t'] - start['t'], 4) if refill else None,
                             'clip': [refill[0]['from'], refill[0]['to']] if refill else None,
                             'expect': round(RIFLE['reload'], 4)})
                start = None
    return {'reloads': rows}


def measure_shock(frames, evs, victim):
    """Freeze (iDontUpdatePadInput) and electric (bStickyShocking) stretches of the victim."""
    rows = []
    pad_on = sticky_on = None
    for e in evs:
        if e.get('who') != victim:
            continue
        if e['kind'] == 'padFreeze':
            if e['to'] and not e['from']:
                pad_on = e
            elif pad_on is not None and not e['to']:
                rows.append({'what': 'freeze', 'start': pad_on['t'], 'end': e['t'],
                             'seconds': round(e['t'] - pad_on['t'], 4)})
                pad_on = None
        if e['kind'] == 'sticky':
            if e['to'] and not e['from']:
                sticky_on = e
            elif sticky_on is not None and not e['to']:
                rows.append({'what': 'electric', 'start': sticky_on['t'], 'end': e['t'],
                             'seconds': round(e['t'] - sticky_on['t'], 4)})
                sticky_on = None
    if pad_on is not None:
        rows.append({'what': 'freeze', 'start': pad_on['t'], 'end': None})
    if sticky_on is not None:
        rows.append({'what': 'electric', 'start': sticky_on['t'], 'end': None})
    # The deadlines the shock wrote, against the spec: int(now + freeze), int(now + freeze + extra).
    team_of = {}
    deadlines = []
    for fr in frames:
        side = fr.get(victim)
        if not side:
            continue
        team = 'spy' if side.get('energy') is not None else 'merc'
        team_of[victim] = team
        key = (side.get('stop'), side.get('elec'), side.get('tshock'))
        if not deadlines or deadlines[-1]['key'] != key:
            deadlines.append({'key': key, 'f': fr['f'], 't': fr['t'], 'stop': side.get('stop'),
                              'elec': side.get('elec'), 'timeShock': side.get('tshock'), 'immunity': side.get('inv')})
    for d in deadlines:
        d.pop('key')
        team = team_of.get(victim, 'merc')
        freeze, extra = SHOCK[team]
        if d.get('timeShock'):
            d['expectStop'] = int(f32(d['timeShock'] + freeze))
            d['expectElec'] = int(f32(d['timeShock'] + freeze + extra))
            d['expectImmunity'] = freeze + extra + 8.0
    return {'stretches': rows, 'deadlines': deadlines[1:] if len(deadlines) > 1 else deadlines}


def measure_energy(frames, evs):
    shots = [e for e in evs if e['kind'] == 'sent' and e['do'] == 'fire']
    energy = [(fr['f'], fr['t'], (fr.get('L') or {}).get('energy')) for fr in frames]
    rows = []
    for s in shots:
        before = [x for x in energy if x[0] < s['f']]
        after = [x for x in energy if s['f'] <= x[0] <= s['f'] + 3]
        if before and after:
            rows.append({'f': s['f'], 't': s['t'], 'before': before[-1][2], 'after': min(a[2] for a in after),
                         'drop': round(before[-1][2] - min(a[2] for a in after), 3)})
    # Refill: the frames where energy rises (whole-second steps), their sizes and spacing.
    steps = []
    for (f0, t0, e0), (f1, t1, e1) in zip(energy, energy[1:]):
        if e0 is not None and e1 is not None and e1 > e0:
            steps.append((t1, round(e1 - e0, 3), e1))
    gaps = [round(b[0] - a[0], 3) for a, b in zip(steps, steps[1:])]
    top = max((e for _, _, e in energy if e is not None), default=None)
    return {'shots': rows, 'refillSteps': [[round(t, 3), d, round(e, 3)] for t, d, e in steps],
            'refillSizes': sorted({d for _, d, _ in steps}), 'refillGaps': sorted(set(gaps)), 'max': top}


def measure_blast(frames, evs):
    rows = []
    for e in evs:
        if e['kind'] != 'grenadeGone':
            continue
        # The rig sees the grenade gone in the Tick after it went off: the blast is in the frame before.
        spawned = [x for x in evs if x['kind'] == 'grenade' and x['f'] <= e['f']]
        fr = _frame_at(frames, e['f'] - 3) or _frame_at(frames, e['f'] - 1) or _frame_at(frames, e['f'])
        at = e['at']
        row = {'f': e['f'], 't': e['t'], 'at': [round(v, 2) for v in at], 'rigFuse': bool(e['rigFuse']),
               'fuse': round(e['goneAt'] - spawned[-1]['spawnedAt'], 4) if spawned else None}
        for who in ('L', 'T'):
            side = (fr or {}).get(who)
            if not side:
                continue
            d = dist(side['loc'], at)
            dmg = [x for x in evs if x['kind'] == 'damage' and x['who'] == who and e['f'] - 2 <= x['f'] <= e['f'] + 3]
            prot = side.get('prot') or 0.0
            protected = (fr['t'] < prot) and side.get('energy') is not None
            etype, edmg = frag_expect(d, None, protected)
            after = _frame_at(frames, e['f'] + 30) or frames[-1]
            blast = {}
            for k in range(0, 12):
                x = (_frame_at(frames, e['f'] - 1 + k) or {}).get(who)
                if x and x.get('vel'):
                    h = math.hypot(x['vel'][0], x['vel'][1])
                    if h > blast.get('speed', 0.0):
                        blast = {'speed': round(h, 2), 'vz': round(x['vel'][2], 2), 'f': e['f'] - 1 + k}
            row[who] = {'distance': round(d, 2), 'damage': [[x['typeName'], x['damage'], x['pill']] for x in dmg],
                        'hpBefore': side.get('hp'), 'hpAfter': (after.get(who) or {}).get('hp'),
                        'stateAfter': (after.get(who) or {}).get('cs'), 'expectType': etype, 'expectDamage': edmg,
                        'spawnProtected': protected, 'blastVelocity': blast or None,
                        'expectBlastSpeed': round(800.0 + (350.0 - 800.0) * d / 1000.0, 2)
                        if etype == 'SDamage_Explosion' and edmg else None}
        rows.append(row)
    return {'blasts': rows}


def measure_states(frames, evs):
    seq = {}
    for e in evs:
        if e['kind'] in ('state', 'weaponState', 'health', 'damage', 'padFreeze', 'cough', 'lying'):
            seq.setdefault(e['who'], []).append(
                [round(e['t'], 4), e['kind'], e.get('to', e.get('damage')), e.get('typeName')])
    return {'timeline': seq}


def measure_deaths(frames, evs):
    """Each death of the target: the shot's direction relative to its facing, the death action and clip."""
    rows = []
    for e in evs:
        if e['kind'] != 'health' or e['who'] != 'T' or e['to'] is None or e['to'] > 0 or (e['from'] or 0) <= 0:
            continue
        fr = _frame_at(frames, e['f'])
        later = _frame_at(frames, e['f'] + 6) or fr
        T, L = fr['T'], fr['L']
        k = math.pi / 32768.0
        yaw = T['rot'][1] * k
        dx, dy = T['loc'][0] - L['loc'][0], T['loc'][1] - L['loc'][1]
        # The bullet's travel direction (shooter to target) in the target's frame: forward x, right y.
        fx, fy = math.cos(yaw), math.sin(yaw)
        rx, ry = math.cos(yaw + math.pi / 2), math.sin(yaw + math.pi / 2)
        n = math.hypot(dx, dy) or 1.0
        fwd, right = (dx * fx + dy * fy) / n, (dx * rx + dy * ry) / n
        shooter = 'behind' if fwd > 0.7 else 'front' if fwd < -0.7 else ('left' if right > 0 else 'right')
        rows.append({'t': e['t'], 'hp': e['to'], 'shooterOn': shooter, 'state': later['T'].get('cs'),
                     'action': later['T'].get('act'), 'clip': (later['T'].get('anim') or [[None, None]])[0][1],
                     'targetYaw': T['rot'][1] & 0xFFFF})
    return {'deaths': rows}


MEASURES = {
    'hits': lambda fr, ev, opt: measure_hits(fr, ev),
    'shots': lambda fr, ev, opt: measure_shots(fr, ev),
    'reload': lambda fr, ev, opt: measure_reload(fr, ev),
    'shock': lambda fr, ev, opt: measure_shock(fr, ev, opt.get('victim', 'T')),
    'energy': lambda fr, ev, opt: measure_energy(fr, ev),
    'blast': lambda fr, ev, opt: measure_blast(fr, ev),
    'states': lambda fr, ev, opt: measure_states(fr, ev),
    'deaths': lambda fr, ev, opt: measure_deaths(fr, ev),
}


def measure_run(path):
    header, frames, footer = read_trace(path)
    doc = header['scenario']['doc']
    evs = events(frames)
    out = {'run': header['run'], 'path': path, 'footer': footer}
    for name, opt in (doc.get('measure') or {}).items():
        fn = MEASURES.get(name)
        if fn is None:
            out.setdefault('unknown', []).append(name)
            continue
        out[name] = fn(frames, evs, opt if isinstance(opt, dict) else {})
    out['events'] = [e for e in evs if e['kind'] not in ('energy',)]
    return doc, out


FALL = {'spy': (600.0, 1500.0), 'merc': (450.0, 1000.0)}


def measure_fall_run(path):
    """A single-player svm.trace/1 fall (record.py): the drop at landing (fZMaxFalling - Location.Z) and the
    health lost, against blast-and-fall-damage.md rule 15."""
    import trace as tr
    header, frames, footer = tr.read(path)
    lo, hi = FALL[header['team']]
    out = {'run': header['run'], 'path': path, 'footer': footer, 'falls': []}
    for a, b in zip(frames, frames[1:]):
        if a['phys'] == 2 and b['phys'] == 1:
            # Landing on a step the pawn can be lower in its last air frame than where it rests: the fall damage
            # is taken at the landing, before the pawn is lifted onto the step.
            h = (a.get('zmax') or 0.0) - min(a['loc'][2], b['loc'][2])
            later = [x for x in frames if b['f'] <= x['f'] <= b['f'] + 30]
            hp_after = min((x['hp'] for x in later), default=b['hp'])
            expect = int(f32((f32(h) - lo) / (hi - lo) * 100.0)) if h > lo else 0
            out['falls'].append({'t': b['t'], 'drop': round(h, 3), 'hpBefore': a['hp'], 'hpAfter': hp_after,
                                 'damage': a['hp'] - hp_after, 'expect': expect, 'landState': b.get('cs'),
                                 'match': a['hp'] - hp_after == expect})
    return header['scenario']['doc'], out


def measure_folder(folder):
    runs = []
    doc = None
    first = sorted(glob.glob(os.path.join(folder, 'run*.jsonl.gz')))
    if first:
        with gzip.open(first[0], 'rt', encoding='utf-8') as f:
            fmt = json.loads(f.readline()).get('format')
        if fmt == 'svm.trace/1':
            for path in first:
                doc, run = measure_fall_run(path)
                runs.append(run)
            result = {'id': doc['id'], 'title': doc['title'], 'measure': doc.get('measure'), 'runs': runs}
            with open(os.path.join(folder, 'measure.json'), 'w', encoding='utf-8', newline='\n') as f:
                json.dump(result, f, indent=1)
            return result
    for path in sorted(glob.glob(os.path.join(folder, 'run*.jsonl.gz'))):
        doc, run = measure_run(path)
        runs.append(run)
    if doc is None:
        return None
    result = {'id': doc['id'], 'title': doc['title'], 'measure': doc.get('measure'), 'runs': runs}
    with open(os.path.join(folder, 'measure.json'), 'w', encoding='utf-8', newline='\n') as f:
        json.dump(result, f, indent=1)
    return result


def measure_all(root):
    import combat_results
    results = []
    for folder in sorted(glob.glob(os.path.join(root, 'combat.*'))):
        r = measure_folder(folder)
        if r is not None:
            results.append(r)
    return combat_results.write(root, results)
