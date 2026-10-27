"""Scenario files (svm.scenario/1): what to play in the original and in the rewrite, frame by frame.

A scenario names a map and a team, where to put the player (a spot found in the map data), a
short setup (writes, stance, view), how many frames to settle, then frame-indexed inputs and the
recording length. The same file drives the original recorder (record.py) and, later, the sim
runner of tests/OpenScct.DiffTest. The format is described in tests/scenarios/README.md and checked by
tests/scenarios/svm.scenario-1.schema.json; validate() here applies the same rules without a
schema library.
"""
from __future__ import annotations

import hashlib
import json
import os
import re

FORMAT = 'svm.scenario/1'

# Input bytes of the local controller (CPF_Input properties) a scenario may hold. They are held:
# written every frame with the scenario's value until an event changes it.
INPUT_BYTES = ('BT_Up', 'BT_Down', 'BT_Left', 'BT_Right', 'bCrouch', 'bRoll', 'bUsing', 'bFireMode',
               'bUseBackToWall')
TEAMS = ('spy', 'merc')
TARGETS = ('pawn', 'controller')
# Fields a setup `write` may touch. Physics is never written (the recorder refuses it).
WRITABLE = {
    'pawn': ('Health',),
    'controller': ('bFastSpeed',),
}
SETUP_OPS = ('teleport', 'write', 'stance', 'view', 'fastSpeed', 'wait', 'waitStill', 'leaveGE')
# Comparison profiles. traversal: GE moves (ledges, pipes, zip lines, roll, back to wall, crawl); the recorder
# also writes the traversal extras (trace `tv`, `rm`, `ach`).
PROFILES = ('locomotion', 'traversal')
UNIT_RE = re.compile(r'^(uc|nat|eng|rep|def|fmt|rl):\S+$')
ID_RE = re.compile(r'^[a-z0-9]+(\.[a-z0-9][a-z0-9-]*)+$')


class ScenarioError(ValueError):
    pass


def _is_int(v):
    return isinstance(v, int) and not isinstance(v, bool)


def _is_num(v):
    return isinstance(v, (int, float)) and not isinstance(v, bool)


def _vec(v, n=3):
    return isinstance(v, list) and len(v) == n and all(_is_num(x) for x in v)


def validate(doc):
    """A list of problems (empty when the scenario is valid)."""
    e = []
    if not isinstance(doc, dict):
        return ['the scenario is not an object']
    if doc.get('format') != FORMAT:
        e.append('format must be %s' % FORMAT)
    if not isinstance(doc.get('id'), str) or not ID_RE.match(doc['id']):
        e.append('id must be dotted lower-case words, e.g. movement.spy-run')
    for key in ('title', 'map'):
        if not isinstance(doc.get(key), str) or not doc.get(key):
            e.append('%s is required' % key)
    if doc.get('mode') not in ('playlevel',):
        e.append('mode must be playlevel')
    players = doc.get('players')
    if not isinstance(players, list) or len(players) != 1:
        e.append('players must list exactly one player (v1 records the local player only)')
    else:
        for p in players:
            if not isinstance(p, dict) or p.get('team') not in TEAMS:
                e.append('players[].team must be spy or merc')
    spawn = doc.get('spawn')
    if not isinstance(spawn, dict):
        e.append('spawn is required')
    else:
        if not _vec(spawn.get('floor')):
            e.append('spawn.floor must be [x, y, floorZ]')
        if not _is_int(spawn.get('yaw')):
            e.append('spawn.yaw must be an int (rotator units)')
        if 'clearance' in spawn and not _is_num(spawn['clearance']):
            e.append('spawn.clearance must be a number')
    for key in ('settleFrames', 'durationFrames', 'repeats'):
        if not _is_int(doc.get(key)) or doc[key] < (0 if key == 'settleFrames' else 1):
            e.append('%s must be a positive int' % key)
    setup = doc.get('setup', [])
    if not isinstance(setup, list):
        e.append('setup must be a list')
        setup = []
    for i, op in enumerate(setup):
        e += ['setup[%d]: %s' % (i, m) for m in _check_setup(op)]
    inputs = doc.get('inputs')
    if not isinstance(inputs, list):
        e.append('inputs must be a list')
        inputs = []
    duration = doc.get('durationFrames') if _is_int(doc.get('durationFrames')) else 0
    last = -1
    for i, ev in enumerate(inputs):
        msgs = _check_input(ev, duration)
        e += ['inputs[%d]: %s' % (i, m) for m in msgs]
        if not msgs:
            if ev['frame'] < last:
                e.append('inputs[%d]: frames must not go backwards' % i)
            last = ev['frame']
    covers = doc.get('covers')
    if not isinstance(covers, list) or not covers or not all(isinstance(u, str) and UNIT_RE.match(u) for u in covers):
        e.append('covers must list unit ids (uc:, nat:, eng:, rep:, def:, fmt:, rl:)')
    if doc.get('profile') not in (None,) + PROFILES:
        e.append('profile must be one of %s' % ', '.join(PROFILES))
    measure = doc.get('measure', {})
    if not isinstance(measure, dict):
        e.append('measure must be an object')
    return e


def _check_setup(op):
    if not isinstance(op, dict) or op.get('op') not in SETUP_OPS:
        return ['op must be one of %s' % ', '.join(SETUP_OPS)]
    kind = op['op']
    e = []
    if kind == 'write':
        target, field = op.get('target'), op.get('field')
        if target not in TARGETS:
            e.append('write.target must be pawn or controller')
        elif field not in WRITABLE[target]:
            e.append('write.field %s is not writable on the %s (allowed: %s)' % (field, target,
                                                                                 ', '.join(WRITABLE[target])))
        if not _is_num(op.get('value')):
            e.append('write.value must be a number')
    elif kind == 'stance':
        if not isinstance(op.get('crouched'), bool):
            e.append('stance.crouched must be true or false')
    elif kind == 'view':
        if not _is_int(op.get('pitch')) or not _is_int(op.get('yaw')):
            e.append('view.pitch and view.yaw must be ints')
    elif kind == 'fastSpeed':
        if not isinstance(op.get('value'), bool):
            e.append('fastSpeed.value must be true or false')
    elif kind == 'wait':
        if not _is_int(op.get('frames')) or op['frames'] < 1:
            e.append('wait.frames must be a positive int')
    elif kind in ('waitStill', 'leaveGE'):
        if not _is_int(op.get('maxFrames')) or op['maxFrames'] < 1:
            e.append('%s.maxFrames must be a positive int' % kind)
    return e


def _check_input(ev, duration):
    if not isinstance(ev, dict) or not _is_int(ev.get('frame')) or ev['frame'] < 0:
        return ['frame must be an int >= 0']
    e = []
    if duration and ev['frame'] >= duration:
        e.append('frame %d is past durationFrames' % ev['frame'])
    keys = set(ev) - {'frame', 'note'}
    if not keys:
        e.append('an input event needs buttons, jump, view or fastSpeed')
    for k in keys:
        if k == 'buttons':
            b = ev['buttons']
            if not isinstance(b, dict) or not b:
                e.append('buttons must be a non-empty object')
            else:
                for name, v in b.items():
                    if name not in INPUT_BYTES:
                        e.append('unknown button %s' % name)
                    if not _is_int(v) or not 0 <= v <= 255:
                        e.append('button %s value must be 0..255' % name)
        elif k == 'jump':
            if ev['jump'] is not True:
                e.append('jump must be true')
        elif k == 'view':
            v = ev['view']
            if not isinstance(v, dict) or not _is_int(v.get('pitch')) or not _is_int(v.get('yaw')):
                e.append('view must be {pitch, yaw} ints')
        elif k == 'fastSpeed':
            if not isinstance(ev['fastSpeed'], bool):
                e.append('fastSpeed must be true or false')
        else:
            e.append('unknown key %s' % k)
    return e


def load(path):
    """The scenario at `path` with `_path` and `_sha256` (of the file bytes) added. Raises
    ScenarioError when it does not validate."""
    with open(path, 'rb') as f:
        raw = f.read()
    doc = json.loads(raw.decode('utf-8'))
    problems = validate(doc)
    if problems:
        raise ScenarioError('%s: %s' % (path, '; '.join(problems)))
    doc['_path'] = os.path.abspath(path)
    doc['_sha256'] = hashlib.sha256(raw).hexdigest()
    return doc


def team(doc):
    return doc['players'][0]['team']


def profile(doc):
    return doc.get('profile') or 'locomotion'


def timeline(doc):
    """Per frame 0..durationFrames-1, the input state the recorder writes before that frame's tick:
    {'buttons': {name: value} (held), 'jump': bool (this frame only), 'view': (pitch, yaw) or None
    (this frame only), 'fastSpeed': bool or None (this frame only)}."""
    events = {}
    for ev in doc['inputs']:
        events.setdefault(ev['frame'], []).append(ev)
    held = {}
    out = []
    for f in range(doc['durationFrames']):
        jump, view, fast = False, None, None
        for ev in events.get(f, []):
            for name, v in ev.get('buttons', {}).items():
                held[name] = v
            jump = jump or bool(ev.get('jump'))
            if 'view' in ev:
                view = (ev['view']['pitch'], ev['view']['yaw'])
            if 'fastSpeed' in ev:
                fast = ev['fastSpeed']
        out.append({'buttons': dict(held), 'jump': jump, 'view': view, 'fastSpeed': fast})
    return out
