"""The two-pawn combat rig: a map script compiled into a sandbox copy of a stock map, and its driver in a running
Play Level game.

Build (editor only, no game): the stock sandbox editor cannot compile map scripts (its RE+ plugin predates the
editor command channel's `script.compile`), so the map is made in a private editor-only copy of the project sandbox
(DEFAULT_COMPILE_SANDBOX, F:\\SCCT-Automation\\svm-combat\\Sandbox, with a plugin build that has the command):
the stock map is opened from MapsEd, the rig classes (rig/SvmRigController.uc, rig/SvmCombatRig.uc) are compiled
into it (MyLevel), one SvmCombatRig is placed, and the map is saved under a new name and copied into the project
sandbox's MapsEd. The stock map file is never written. Play Level then runs from the project sandbox as for every
other recording.

Run: `Rig` finds the placed SvmCombatRig in the game and sends it commands through its fields (arguments, Cmd,
then Seq + 1; the rig runs the command in its next Tick and copies Seq to Ack). The rig spawns the target player
(a second SPlayerController and pawn made as the game makes a player) and drives the local player's weapon the
way its key aliases do (`Button bFire | Function Fire`).
"""
from __future__ import annotations

import hashlib
import os
import shutil
import struct
import subprocess
import time

HERE = os.path.dirname(os.path.abspath(__file__))
RIG_DIR = os.path.join(HERE, 'rig')
RIG_CLASSES = (('SvmRigController', 'SPlayerController'), ('SvmCombatRig', 'Info'))
DEFAULT_COMPILE_SANDBOX = r'F:\SCCT-Automation\svm-combat\Sandbox'
# A plugin build with script.compile (RE+ private, feature/aerod 6d13c7a and later), read from a sandbox that has it.
DEFAULT_COMPILER_DLL = r'F:\SCCT-Automation\routecheck\Sandbox\System\Reloaded.Editor.dll'
DEFAULT_BASE_MAP = 'SteeE'
DEFAULT_RIG_MAP = 'SteeECombat'

# Commands (SvmCombatRig.RunCommand).
CMD_SPAWN, CMD_PLACE, CMD_HEALTH, CMD_AIM, CMD_FIRE, CMD_FIRE_UP, CMD_ALT, CMD_ALT_UP = 1, 2, 3, 4, 5, 6, 7, 8
CMD_BONE, CMD_GRENADE, CMD_ROF, CMD_REMOVE, CMD_CLEAR_HISTORY, CMD_TELEPORT, CMD_CROUCH = 9, 10, 11, 12, 13, 14, 15
CMD_FLOOR, CMD_DUMMY_ROLE, CMD_DUMMY_FIRE, CMD_LOCAL_HEALTH, CMD_LOCAL_BONE, CMD_DUMMY_CROUCH = 16, 17, 18, 19, 20, 21
CMD_REFILL, CMD_DUMMY_WAITING = 22, 23
# Bone selectors of SvmCombatRig.BoneName.
BONES = {'head': 1, 'spine2': 2, 'spine1': 3, 'pelvis': 4, 'l_upperarm': 5, 'l_forearm': 6, 'l_hand': 7,
         'l_thigh': 8, 'l_calf': 9, 'l_foot': 10, 'r_upperarm': 11, 'r_forearm': 12, 'r_hand': 13, 'r_thigh': 14,
         'r_calf': 15, 'r_foot': 16, 'spine': 17, 'neck': 18}
TEAM = {'spy': 0, 'merc': 1}
RIG_FIELDS = ('Seq', 'Ack', 'Cmd', 'Status', 'I0', 'F0', 'V0', 'Target', 'Dummy', 'Shooter', 'Grenade', 'FrameTime',
              'NumImpacts', 'Impacts', 'ImpactTimes', 'StartSpotLocation', 'GrenadeGoneAt', 'bGrenadeRigFuse')


def sha256(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for block in iter(lambda: f.read(1 << 20), b''):
            h.update(block)
    return h.hexdigest()


def rig_source(name):
    with open(os.path.join(RIG_DIR, name + '.uc'), encoding='utf-8') as f:
        return f.read()


def rig_sources_sha256():
    h = hashlib.sha256()
    for name, _ in RIG_CLASSES:
        h.update(rig_source(name).encode('utf-8'))
    return h.hexdigest()


# -- build ------------------------------------------------------------------------------------------------------

def ensure_compile_sandbox(play_root, compile_root, dll, log):
    """A private editor-only copy of the project sandbox with a plugin that compiles map scripts. Made once (a
    mirror of the project sandbox; robocopy, junctions not followed); the plugin is refreshed when it differs."""
    system = os.path.join(compile_root, 'System')
    if not os.path.isfile(os.path.join(system, 'ChaosTheory_Editor.exe')):
        os.makedirs(compile_root, exist_ok=True)
        log('copying %s to %s (editor-only compile sandbox)' % (play_root, compile_root))
        code = subprocess.run(['robocopy', play_root, compile_root, '/MIR', '/XJ', '/NFL', '/NDL', '/NJH', '/NJS',
                               '/NP', '/R:1', '/W:1'], capture_output=True).returncode
        if code >= 8:
            raise SystemExit('copying the sandbox failed (robocopy %d)' % code)
    target = os.path.join(system, 'Reloaded.Editor.dll')
    if not os.path.isfile(dll):
        raise SystemExit('no editor plugin with script.compile at %s' % dll)
    if not os.path.isfile(target) or sha256(target) != sha256(dll):
        shutil.copyfile(dll, target)
        log('editor plugin set to', dll)
    return {'root': compile_root, 'plugin': sha256(target)}


def build_map(tools, sb, play_root, compile_root=DEFAULT_COMPILE_SANDBOX, dll=DEFAULT_COMPILER_DLL,
              base=DEFAULT_BASE_MAP, name=DEFAULT_RIG_MAP, log=None):
    """Compile the rig into a copy of `base` and install it as MapsEd/<name>.sdc in the project sandbox."""
    log = log or sb.log
    info = {'base': base, 'name': name, 'rigSha256': rig_sources_sha256()}
    info['compileSandbox'] = ensure_compile_sandbox(play_root, compile_root, dll, log)
    base_path = os.path.join(compile_root, 'Packages', 'MapsEd', base + '.sdc')
    stock = os.path.join(play_root, 'Packages', 'MapsEd', base + '.sdc')
    if sha256(base_path) != sha256(stock):
        shutil.copyfile(stock, base_path)
    info['baseSha256'] = sha256(stock)
    out = os.path.join(compile_root, 'Packages', 'MapsEd', name + '.sdc')
    with sb.Sandbox(tools, compile_root, desktop='SVMCombatEditor', fps=None) as box:
        box.load_map(base)
        info['compiled'] = []
        for cls, parent in RIG_CLASSES:
            reply = box.live.raw('script.compile', {'name': cls, 'parent': parent, 'source': rig_source(cls)})
            if not reply.get('ok'):
                raise SystemExit('script.compile %s: %s' % (cls, reply.get('error')))
            result = reply.get('result') or {}
            problems = [line for line in result.get('log', []) if 'rror' in str(line)]
            log('compiled', cls, 'log lines', len(result.get('log', [])), problems[:5])
            info['compiled'].append({'class': cls, 'parent': parent, 'errors': problems})
        reply = box.live.raw('exec', {'command': 'ACTOR ADD CLASS=MyLevel.SvmCombatRig'})
        found = box.live.raw('actors.find', {'queries': [{'class': 'SvmCombatRig', 'fields': ['name', 'location']}]})
        actors = ((found.get('result') or {}).get('results') or [{}])[0].get('actors', [])
        if len(actors) != 1:
            raise SystemExit('the rig actor was not placed once: %s / %s' % (reply, found))
        info['rigActor'] = actors[0]
        reply = box.live.raw('map.save', {'path': out, 'answers': [{'text': 'larger than the file in memory',
                                                                    'press': 'Yes'}]})
        if not reply.get('ok'):
            raise SystemExit('map.save: %s' % reply.get('error'))
    installed = os.path.join(play_root, 'Packages', 'MapsEd', name + '.sdc')
    shutil.copyfile(out, installed)
    info['map'] = {'path': installed, 'sha256': sha256(installed)}
    log('rig map installed', info['map'])
    return info


# -- run --------------------------------------------------------------------------------------------------------

class RigError(RuntimeError):
    pass


def _ptr(value):
    if not value or value == 'None':
        return 0
    return int(str(value).split(':')[0], 16)


class Rig:
    """The placed SvmCombatRig of a running game (game_tools.Session) and its command channel."""

    def __init__(self, session, log=print):
        self.session = session
        self.g = g = session.attach()
        self.log = log
        g.r.load_objects()
        rigs = [o for o in g.r.objects if o and g.r.class_name(o) == 'SvmCombatRig']
        if not rigs:
            # Names the level registered after the driver's scan.
            g.r.names = {}
            g.r.scan_names()
            g.r.name_cache = {}
            rigs = [o for o in g.r.objects if o and g.r.class_name(o) == 'SvmCombatRig']
        if len(rigs) != 1:
            raise RigError('%d SvmCombatRig actors in the game' % len(rigs))
        self.obj = rigs[0]
        self.props = g.props(g.class_of(self.obj))
        if any(k not in self.props for k in RIG_FIELDS):
            # Field names the level registered after the driver's scan read as '#<index>': scan again.
            g.r.names = {}
            g.r.scan_names()
            g.r.name_cache = {}
            g._props = {}
            self.props = g.props(g.class_of(self.obj))
        # Names are case-insensitive in the engine: a field takes the spelling of a name the game already had
        # (Shooter reads as 'shooter', I1 as 'i1').
        self.keys = {k.lower(): k for k in self.props}
        missing = [k for k in RIG_FIELDS if k.lower() not in self.keys]
        if missing:
            raise RigError('the rig has no %s (fields %s)' % (missing, sorted(self.props)[-40:]))

    def key(self, name):
        return self.keys[name.lower()]

    def get(self, name):
        return self.g.get(self.obj, self.key(name))

    def vec(self, name):
        off = self.props[self.key(name)][0]
        return list(struct.unpack('<3f', self.g.p.read(self.obj + off, 12)))

    def ptr(self, name):
        return _ptr(self.get(name))

    def send(self, code, **args):
        """Write a command; returns its sequence number (wait() for it)."""
        for k, v in args.items():
            self.g.set(self.obj, self.key(k), v)
        self.g.set(self.obj, self.key('Cmd'), int(code))
        seq = int(self.get('Seq')) + 1
        self.g.set(self.obj, self.key('Seq'), seq)
        return seq

    def done(self, seq):
        return int(self.get('Ack')) >= seq

    def wait(self, seq, timeout=3.0):
        end = time.time() + timeout
        while time.time() < end:
            ack = int(self.get('Ack'))
            if ack == seq:
                return int(self.get('Status'))
            if ack > seq:
                return 1
            time.sleep(0.002)
        raise RigError('command %d not run within %.1fs' % (seq, timeout))

    def cmd(self, code, timeout=3.0, **args):
        status = self.wait(self.send(code, **args), timeout)
        if status < 0:
            raise RigError('command %d refused: status %d' % (code, status))
        return status
