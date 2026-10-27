"""The combat results table: every measured scenario folder's numbers next to the spec values, as results.json and
RESULTS.md in the golden root (game-derived: local/ only). combat_measure.measure_all calls write().
"""
from __future__ import annotations

import json
import os
import time


def _fmt(v, nd=3):
    if v is None:
        return '-'
    if isinstance(v, float):
        return ('%.' + str(nd) + 'f') % v
    if isinstance(v, (list, tuple)):
        return '[' + ', '.join(_fmt(x, nd) for x in v) + ']'
    return str(v)


def _table(head, rows):
    out = ['| ' + ' | '.join(head) + ' |', '|' + '---|' * len(head)]
    for r in rows:
        out.append('| ' + ' | '.join(_fmt(x) for x in r) + ' |')
    return out


def _states(run, who, kinds=('state',), limit=40):
    seq = (run.get('states') or {}).get('timeline', {}).get(who, [])
    out = []
    for t, kind, to, typ in seq:
        if kind in kinds:
            out.append('%.2f %s' % (t, to if typ is None else '%s %s' % (to, typ)))
    return out[:limit]


def summary(results):
    """Totals per measure kind, for results.json and the report."""
    s = {'hits': [0, 0, 0], 'disp': [0, 0], 'kickPitch': [None, None], 'kickYaw': [None, None], 'spreadU': [None, None],
         'cadence': set(), 'reloads': [], 'shocks': [], 'energyDrops': set(), 'refill': [], 'blasts': []}
    for r in results:
        for run in r['runs']:
            h = run.get('hits')
            if h:
                s['hits'][0] += h['n']
                s['hits'][1] += h['checked']
                s['hits'][2] += h['matched']
            sh = run.get('shots')
            if sh:
                s['disp'][0] += sh['dispChecked']
                s['disp'][1] += sh['dispMatched']
                for key, rng in (('kickPitch', sh['kickPitchRange']), ('kickYaw', sh['kickYawRange'])):
                    if rng[0] is not None:
                        lo, hi = s[key]
                        s[key] = [rng[0] if lo is None else min(lo, rng[0]), rng[1] if hi is None else max(hi, rng[1])]
                for row in sh['shots']:
                    for u in (row.get('uY'), row.get('uZ')):
                        if u is not None:
                            lo, hi = s['spreadU']
                            s['spreadU'] = [u if lo is None else min(lo, u), u if hi is None else max(hi, u)]
                s['cadence'] |= {round(c, 3) for c in sh['cadence']}
            for rl in (run.get('reload') or {}).get('reloads', []):
                s['reloads'].append([r['id'], run['run'], rl['state'], rl['seconds'], rl['refillAfter'], rl['clip']])
            sk = run.get('shock')
            if sk:
                for st in sk['stretches']:
                    s['shocks'].append([r['id'], run['run'], st['what'], st.get('seconds')])
            en = run.get('energy')
            if en:
                s['energyDrops'] |= {x['drop'] for x in en['shots']}
                s['refill'].append([en.get('refillSizes'), en.get('refillGaps'), en.get('max')])
            for b in (run.get('blast') or {}).get('blasts', []):
                s['blasts'].append([r['id'], run['run'], b])
    s['cadence'] = sorted(s['cadence'])
    s['energyDrops'] = sorted(s['energyDrops'])
    return s


def markdown(results, s):
    lines = ['# Combat ground truth (real game, two-pawn rig)', '',
             'Written by re/tools/difftest/combat.py measure on %s. Game-derived: keep under local/.' %
             time.strftime('%Y-%m-%d %H:%M'), '']
    lines += ['## Totals', '',
              '- rifle hits checked against the float32 damage formula: %d of %d hits had an impact; %d matched' %
              (s['hits'][1], s['hits'][0], s['hits'][2]),
              '- DispersionActuelle against GetAccuracy: %d of %d shots matched' % (s['disp'][1], s['disp'][0]),
              '- aim kick per shot: pitch %s, yaw %s (spec: pitch 66..230, yaw within +-296)' %
              (_fmt(s['kickPitch']), _fmt(s['kickYaw'])),
              '- spread position u per axis (spec: 0..1): %s' % _fmt(s['spreadU']),
              '- shot intervals seen (s): %s' % _fmt(s['cadence']),
              '- shocker energy per shot: %s; refill steps, spacing, highest: %s' % (_fmt(s['energyDrops']),
                                                                                      _fmt(s['refill'])), '']
    for r in results:
        lines += ['## %s' % r['id'], '', r['title'], '']
        for run in r['runs']:
            lines.append('### run %d' % run['run'])
            lines.append('')
            h = run.get('hits')
            if h and h['hits']:
                lines += _table(['t', 'type', 'pill', 'zone', 'damage', 'distance', 'expect', 'match'],
                                [[x['t'], x['type'], x['pill'], x['zone'], x['damage'], x.get('distance'),
                                  x.get('expect'), x.get('match')] for x in h['hits']])
                lines.append('')
            sh = run.get('shots')
            if sh and sh['shots']:
                lines += _table(['t', 'dt', 'clip', 'bir', 'disp', 'expect', 'speed', 'kick p', 'kick y', 'uY', 'uZ'],
                                [[x['t'], x['dt'], x['clip'], x['bir'], x['disp'], x['expectDisp'], x['speed'],
                                  x.get('kickPitch'), x.get('kickYaw'), x.get('uY'), x.get('uZ')] for x in sh['shots']])
                lines.append('')
            rl = run.get('reload')
            if rl and rl['reloads']:
                lines += _table(['state', 'start', 'end', 'seconds', 'refill after', 'clip', 'to', 'expect clip'],
                                [[x['state'], x['start'], x['end'], x['seconds'], x['refillAfter'], x['clip'], x['to'],
                                  x['expect']] for x in rl['reloads']])
                lines.append('')
            sk = run.get('shock')
            if sk:
                lines += _table(['stretch', 'start', 'end', 'seconds'],
                                [[x['what'], x['start'], x.get('end'), x.get('seconds')] for x in sk['stretches']])
                lines.append('')
                lines += _table(['t', 'TimeShock', 'stop', 'expect stop', 'elec', 'expect elec', 'immunity', 'expect'],
                                [[x['t'], x.get('timeShock'), x.get('stop'), x.get('expectStop'), x.get('elec'),
                                  x.get('expectElec'), x.get('immunity'), x.get('expectImmunity')]
                                 for x in sk['deadlines']])
                lines.append('')
            en = run.get('energy')
            if en and en['shots']:
                lines += _table(['t', 'before', 'after', 'drop'], [[x['t'], x['before'], x['after'], x['drop']]
                                                                   for x in en['shots']])
                lines.append('')
                lines.append('refill steps %s, spacing %s s, highest %s' % (
                    _fmt(en.get('refillSizes')), _fmt(en.get('refillGaps')), _fmt(en.get('max'))))
                lines.append('')
            bl = run.get('blast')
            if bl and bl['blasts']:
                rows = []
                for b in bl['blasts']:
                    for who in ('L', 'T'):
                        x = b.get(who)
                        if x:
                            rows.append([b['t'], who, x['distance'], x['damage'], x['expectType'], x['expectDamage'],
                                         x['hpBefore'], x['hpAfter'], x['stateAfter'], x['spawnProtected'],
                                         b['rigFuse']])
                lines += _table(['t', 'who', 'distance', 'damage entries', 'expect type', 'expect', 'hp before',
                                 'hp after', 'state after', 'protected', 'rig fuse'], rows)
                lines.append('')
            if run.get('falls'):
                lines += _table(['t', 'drop', 'hp before', 'hp after', 'damage', 'expect', 'match', 'landing state'],
                                [[x['t'], x['drop'], x['hpBefore'], x['hpAfter'], x['damage'], x['expect'], x['match'],
                                  x['landState']] for x in run['falls']])
                lines.append('')
            de = run.get('deaths')
            if de and de['deaths']:
                lines += _table(['t', 'shooter on', 'hp', 'state', 'action', 'clip', 'target yaw'],
                                [[x['t'], x['shooterOn'], x['hp'], x['state'], x['action'], x['clip'], x['targetYaw']]
                                 for x in de['deaths']])
                lines.append('')
            if run.get('states'):
                for who in ('L', 'T'):
                    seq = _states(run, who, ('state', 'health', 'damage', 'padFreeze', 'cough', 'lying'))
                    if seq:
                        lines.append('%s: %s' % ('local' if who == 'L' else 'target', '; '.join(seq)))
                lines.append('')
    return '\n'.join(lines) + '\n'


def write(root, results):
    s = summary(results)
    with open(os.path.join(root, 'results.json'), 'w', encoding='utf-8', newline='\n') as f:
        json.dump({'summary': s, 'scenarios': [{k: v for k, v in r.items() if k != 'runs'} | {
            'runs': [{k: v for k, v in run.items() if k != 'events'} for run in r['runs']]} for r in results]},
                  f, indent=1, default=str)
    with open(os.path.join(root, 'RESULTS.md'), 'w', encoding='utf-8', newline='\n') as f:
        f.write(markdown(results, s))
    return os.path.join(root, 'RESULTS.md')
