"""Combat scenario files (svm.combat/1): two players in a combat rig game, frame-indexed events.

A combat scenario names a stock map (the rig map is built from it), the local player (team, spot, stance), the
rig's target player (team, spot, stance; or none), a settle, then events by frame: rig commands (aim, fire, heal,
place, grenade, the target's own shots...) and the local player's input bytes. The format is described in
tests/scenarios/combat/README.md and checked by tests/scenarios/combat/svm.combat-1.schema.json; validate() here
applies the same rules without a schema library.

Single-player combat cases (fall damage on a merc) are plain svm.scenario/1 files in the same folder; they are
recorded with record.py and replayed by the sim runner like the movement scenarios.
"""
from __future__ import annotations

import hashlib
import json
import os
import re

FORMAT = 'svm.combat/1'
TEAMS = ('spy', 'merc')
ID_RE = re.compile(r'^combat\.[a-z0-9][a-z0-9-]*$')
UNIT_RE = re.compile(r'^(uc|nat|eng|rep|def|fmt|rl):\S+$')
BONES = ('head', 'spine2', 'spine1', 'pelvis', 'l_upperarm', 'l_forearm', 'l_hand', 'l_thigh', 'l_calf', 'l_foot',
         'r_upperarm', 'r_forearm', 'r_hand', 'r_thigh', 'r_calf', 'r_foot', 'spine', 'neck')
INPUTS = ('BT_Up', 'BT_Down', 'BT_Left', 'BT_Right', 'bCrouch', 'bRoll', 'bUsing', 'bFireMode', 'bReload', 'bROF',
          'bUseBackToWall')
ROF = {'single': 0, 'burst': 1, 'auto': 2}
GRENADES = {'frag': 1, 'smoke': 2}
# Event kinds that are rig commands: one per frame (the rig runs one command per Tick).
RIG_EVENTS = ('aim', 'aimPoint', 'fire', 'fireUp', 'altFire', 'altFireUp', 'rof', 'heal', 'healLocal', 'place',
              'teleport', 'crouchTarget', 'grenade', 'targetFire', 'clearHistory', 'spawnTarget', 'removeTarget',
              'refill', 'targetRole')
LOCAL_EVENTS = ('buttons',)
SETUP_OPS = ('stance', 'draw', 'holster', 'refill', 'rof', 'heal', 'clearHistory', 'wait', 'waitStill', 'targetRole')


class ScenarioError(ValueError):
    pass


def _num(v):
    return isinstance(v, (int, float)) and not isinstance(v, bool)


def _int(v):
    return isinstance(v, int) and not isinstance(v, bool)


def _vec(v, n=3):
    return isinstance(v, list) and len(v) == n and all(_num(x) for x in v)


def _player(e, where, p, target=False):
    if not isinstance(p, dict):
        e.append('%s must be an object' % where)
        return
    if p.get('team') not in TEAMS:
        e.append('%s.team must be spy or merc' % where)
    if not _vec(p.get('floor')):
        e.append('%s.floor must be [x, y, floorZ]' % where)
    if not _int(p.get('yaw')):
        e.append('%s.yaw must be an int' % where)
    if 'crouched' in p and not isinstance(p['crouched'], bool):
        e.append('%s.crouched must be a bool' % where)
    if target and 'atStart' in p and not isinstance(p['atStart'], bool):
        e.append('%s.atStart must be a bool' % where)


def _event(e, k, ev, has_target):
    where = 'events[%d]' % k
    do = ev.get('do')
    if do not in RIG_EVENTS + LOCAL_EVENTS + ('note',):
        e.append('%s.do %r is not an event kind' % (where, do))
        return
    if do in ('aim', 'targetFire'):
        if ev.get('bone') not in BONES:
            e.append('%s.bone must be one of %s' % (where, ', '.join(BONES)))
        if 'to' in ev and (do != 'aim' or ev['to'] not in BONES):
            e.append('%s.to (aim at the middle of two bones) must be one of %s' % (where, ', '.join(BONES)))
        if 'offset' in ev and not _vec(ev['offset']):
            e.append('%s.offset must be [x, y, z]' % where)
    if do == 'aimPoint' and not _vec(ev.get('point')):
        e.append('%s.point must be [x, y, z]' % where)
    if do == 'rof' and ev.get('mode') not in ROF:
        e.append('%s.mode must be single, burst or auto' % where)
    if do in ('heal', 'healLocal') and not (_int(ev.get('hp')) and 0 < ev['hp'] <= 1000):
        e.append('%s.hp must be an int in 1..1000' % where)
    if do in ('place', 'teleport'):
        if not _vec(ev.get('floor')) or not _int(ev.get('yaw')):
            e.append('%s needs floor [x, y, floorZ] and an int yaw' % where)
    if do == 'crouchTarget' and not isinstance(ev.get('crouched'), bool):
        e.append('%s.crouched must be a bool' % where)
    if do == 'grenade':
        if ev.get('kind') not in GRENADES:
            e.append('%s.kind must be frag or smoke' % where)
        anchors = [a for a in ('at', 'fromTarget', 'fromLocal') if a in ev]
        if len(anchors) != 1 or not _vec(ev[anchors[0]]):
            e.append('%s needs one of at / fromTarget / fromLocal as [x, y, z]' % where)
        if ev.get('by', 'local') not in ('local', 'target'):
            e.append('%s.by must be local or target' % where)
        if 'fuse' in ev and not (_num(ev['fuse']) and ev['fuse'] >= 0):
            e.append('%s.fuse must be a number >= 0' % where)
    if do == 'spawnTarget':
        _player(e, where, ev, target=True)
    if do == 'targetRole' and ev.get('role') not in ('host', 'remote'):
        e.append('%s.role must be host or remote' % where)
    if do == 'buttons':
        b = ev.get('buttons')
        if not isinstance(b, dict) or not b or any(k not in INPUTS for k in b) or \
                any(not (_int(v) and 0 <= v <= 255) for v in b.values()):
            e.append('%s.buttons must map %s to 0..255' % (where, ', '.join(INPUTS)))
    if do in ('targetFire', 'heal', 'place', 'crouchTarget', 'clearHistory', 'removeTarget', 'targetRole') \
            and not has_target:
        e.append('%s: %s needs a target' % (where, do))
    if do == 'grenade' and ('fromTarget' in ev or ev.get('by') == 'target') and not has_target:
        e.append('%s: the grenade needs a target' % where)


def validate(doc):
    e = []
    if not isinstance(doc, dict):
        return ['the scenario is not an object']
    if doc.get('format') != FORMAT:
        e.append('format must be %s' % FORMAT)
    if not isinstance(doc.get('id'), str) or not ID_RE.match(doc['id']):
        e.append('id must be combat.<name>')
    for key in ('title', 'map'):
        if not isinstance(doc.get(key), str) or not doc[key]:
            e.append('%s must be a non-empty string' % key)
    if doc.get('mode') != 'playlevel':
        e.append('mode must be playlevel')
    _player(e, 'local', doc.get('local'))
    target = doc.get('target')
    if target is not None:
        _player(e, 'target', target, target=True)
    for k, op in enumerate(doc.get('setup', [])):
        if not isinstance(op, dict) or op.get('op') not in SETUP_OPS:
            e.append('setup[%d].op must be one of %s' % (k, ', '.join(SETUP_OPS)))
    for key in ('settleFrames', 'durationFrames', 'repeats'):
        if not _int(doc.get(key)) or doc[key] < (1 if key != 'settleFrames' else 0):
            e.append('%s must be an int >= %d' % (key, 1 if key != 'settleFrames' else 0))
    duration = doc.get('durationFrames') if _int(doc.get('durationFrames')) else 0
    events = doc.get('events')
    if not isinstance(events, list):
        e.append('events must be a list')
        events = []
    last = -1
    rig_frames = set()
    has_target = target is not None
    for k, ev in enumerate(events):
        if not isinstance(ev, dict) or not _int(ev.get('frame')):
            e.append('events[%d] needs an int frame' % k)
            continue
        if ev['frame'] < last:
            e.append('events[%d]: frames must be in order' % k)
        last = ev['frame']
        if not 0 <= ev['frame'] < max(1, duration):
            e.append('events[%d].frame is outside 0..durationFrames-1' % k)
        if ev.get('do') == 'spawnTarget':
            has_target = True
        _event(e, k, ev, has_target)
        if ev.get('do') == 'removeTarget':
            has_target = False
        if ev.get('do') in RIG_EVENTS:
            if ev['frame'] in rig_frames:
                e.append('events[%d]: one rig command per frame (frame %d has two)' % (k, ev['frame']))
            rig_frames.add(ev['frame'])
    if 'order' in doc and not (_int(doc['order']) and 0 <= doc['order'] <= 100):
        e.append('order must be an int 0..100 (the place in its game; default 50)')
    if 'measure' in doc and not isinstance(doc['measure'], dict):
        e.append('measure must be an object')
    covers = doc.get('covers')
    if not isinstance(covers, list) or not covers or not all(isinstance(c, str) and UNIT_RE.match(c) for c in covers):
        e.append('covers must be a non-empty list of unit ids')
    return e


def load(path):
    with open(path, 'rb') as f:
        raw = f.read()
    doc = json.loads(raw.decode('utf-8'))
    problems = validate(doc)
    stem = os.path.basename(path)
    if stem.endswith('.combat.json') and isinstance(doc.get('id'), str) and \
            doc['id'] != 'combat.' + stem[:-len('.combat.json')]:
        problems.append('id %s does not match the file name %s' % (doc.get('id'), stem))
    if problems:
        raise ScenarioError('%s: %s' % (path, '; '.join(problems)))
    doc['_path'] = path
    doc['_sha256'] = hashlib.sha256(raw).hexdigest()
    return doc


def events_by_frame(sc):
    """{frame: [events]} in file order."""
    out = {}
    for ev in sc['events']:
        out.setdefault(ev['frame'], []).append(ev)
    return out


def held_inputs(sc):
    """The local input bytes per frame (held until an event changes them): a list of dicts, one per frame."""
    state = {}
    out = []
    by = events_by_frame(sc)
    for f in range(sc['durationFrames']):
        for ev in by.get(f, []):
            if ev.get('do') == 'buttons':
                state.update(ev['buttons'])
        out.append(dict(state))
    return out
