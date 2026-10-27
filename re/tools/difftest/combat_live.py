"""Both players of a combat rig game, seen frame by frame: one stable sample per frame of the local player (pawn,
controller, weapon), the rig's target (pawn, controller, weapon), the rig's grenade and the bullet impacts the rig
collected, with the level clock.

Sampling is livegame.py's: spin on LevelInfo.TimeSeconds, wait a short delay after it moves, read every block,
check the clock did not move during the read (torn: dropped) and read the moving fields again (different: the tick
was still running, read again). Field offsets come from the game's reflection data, per class; a field a class does
not have is left out and listed in the trace header.

Memory writes here are only the local controller's input bytes (as livegame.py); everything else the rig does
through its command channel (combat_rig.py).
"""
from __future__ import annotations

import struct
import time

import livegame

PAWN_FIELDS = {
    'Location': 'vec', 'Velocity': 'vec', 'Acceleration': 'vec', 'Rotation': 'rot', 'Physics': 'byte', 'Health': 'int',
    'bIsCrouched': 'bool', 'CollisionHeight': 'float', 'EyeHeight': 'float', 'WalkBob': 'vec', 'MeshInstance': 'ptr',
    'Base': 'ptr', 'Controller': 'ptr',
    'MyWeapon': 'ptr', 'WeaponLoaded': 'byte', 'bStickyShocking': 'bool', 'bStickyShockedInTheHead': 'bool',
    'm_AttackTarget': 'ptr', 'm_AttackedBy': 'ptr', 'bCanBeGrabbed': 'bool', 'bLyingOnGround': 'bool',
    'bCanBeTerminated': 'bool', 'bSleeping': 'bool', 'iLastWakeTime': 'int', 'sAction': 'int', 'IK_Type': 'byte',
    # spy only
    'EnergyLevel': 'float', 'HealthRefill': 'float', 'NbPointsRefillable': 'int', 'BloodDropStartTime': 'float',
}
PC_FIELDS = {
    'Rotation': 'rot', 'MyDesiredRotation': 'rot', 'LocalMyDesiredRotation': 'rot', 'iDontUpdatePadInput': 'int',
    'iTimeToStopStickyShock': 'int', 'iTimeElecStickyShock': 'int', 'TimeShock': 'float', 'TimeShockForGrab': 'float',
    'RealTimeInvulSticky': 'float', 'fSpawnProtectionEnd': 'float', 'bTazered': 'bool', 'bFire': 'byte',
    'bAltFire': 'byte', 'bUsing': 'byte', 'bFireMode': 'byte', 'bReload': 'byte', 'bCrouch': 'byte', 'BT_Up': 'byte',
    'BT_Down': 'byte', 'BT_Left': 'byte', 'BT_Right': 'byte', 'bInSmoke': 'bool', 'btoCought': 'bool',
    'fSmoke_FallDate': 'float', 'fSmoke_EndOfCoughDate': 'float', 'fLastTimeWakeUp': 'float', 'fTimeWhenHit': 'float',
    'fLastElbowAttackTime': 'float', 'fLastChargingTime': 'float', 'fLastBerserkTime': 'float',
    'fGrabMatchStartedTime': 'float', 'GrabReleaseMethode': 'byte', 'fLyingOnGroundDuration': 'float',
    'aForward': 'float', 'aStrafe': 'float', 'Pawn': 'ptr', 'myHUD': 'ptr',
}
HUD_FIELDS = {'bPerturbation': 'bool', 'HudPerturbationLength': 'float', 'TimetobeginPerturb': 'float',
              'bProjection': 'bool', 'bNeigeDisp': 'bool'}
WEAPON_FIELDS = {
    'ClipAmmo': 'int', 'Ammo': 'int', 'ROFMode': 'byte', 'BulletInARow': 'int', 'BulletsToFire': 'int',
    'DispersionActuelle': 'float', 'AccurateRotation': 'rot', 'TimeAccReset': 'float', 'LastShot': 'float',
    'Location': 'vec', 'Rotation': 'rot',
}
RIG_FIELDS = {
    'Seq': 'int', 'Ack': 'int', 'Status': 'int', 'NumImpacts': 'int', 'Grenade': 'ptr', 'GrenadeSpawnedAt': 'float',
    'GrenadeGoneAt': 'float', 'bGrenadeRigFuse': 'bool', 'GrenadeLastLocation': 'vec', 'Target': 'ptr',
    'Dummy': 'ptr', 'StartSpotLocation': 'vec',
}
LEVEL_FIELDS = {'TimeSeconds': 'float', 'm_dT': 'float', 'TimeDilation': 'float'}
LAST_DAMAGES = 10
DAMAGE_STRIDE = 16           # DamageInfos: DamageType byte (padded to 4), Damage, PillTag, Time ints
IMPACT_SLOTS = 32


class CaseLayout(livegame.Layout):
    """livegame.Layout with case-insensitive names: a field takes the spelling of a name the game already had."""

    def __init__(self, game, obj, fields):
        props = game.props(game.class_of(obj))
        lower = {k.lower(): k for k in props}
        mapped = {}
        for name, kind in fields.items():
            mapped[lower.get(name.lower(), name)] = kind
        livegame.Layout.__init__(self, game, obj, mapped)
        self.rename = {lower.get(n.lower(), n): n for n in fields}
        self.offsets = {self.rename.get(k, k): v for k, v in self.offsets.items()}

    def decode(self, block):
        out = livegame.Layout.decode(self, block)
        return {self.rename.get(k, k): v for k, v in out.items()}


class Reader:
    """Layouts per class for one role (pawn, controller, weapon), so a respawned target is read the same way."""

    def __init__(self, game, fields):
        self.game = game
        self.fields = fields
        self.by_class = {}

    def layout(self, obj):
        cls = self.game.class_of(obj)
        if cls not in self.by_class:
            self.by_class[cls] = CaseLayout(self.game, obj, self.fields)
        return self.by_class[cls]

    def read(self, p, obj):
        if not obj:
            return None
        lay = self.layout(obj)
        if not lay.fields:
            return {}
        raw = p.read(obj + lay.lo, lay.hi - lay.lo)
        if len(raw) != lay.hi - lay.lo:
            return None
        return lay.decode(raw)

    def header(self):
        return {self.game.r.obj_name(c): {'offsets': l.offsets, 'missing': l.missing} for c, l in self.by_class.items()}


class CombatGame:
    """The local player and the rig's target in one running game."""

    def __init__(self, session, rig, delay=0.002, settle_check=0.0004):
        self.session = session
        self.rig = rig
        self.g = g = rig.g
        self.p = g.p
        self.pc, self.pawn = session.player()
        if not self.pawn:
            raise RuntimeError('the local player has no pawn')
        self.delay = delay
        self.settle_check = settle_check
        self.pawns = Reader(g, PAWN_FIELDS)
        self.pcs = Reader(g, PC_FIELDS)
        self.weapons = Reader(g, WEAPON_FIELDS)
        self.huds = Reader(g, HUD_FIELDS)
        self.rig_layout = CaseLayout(g, rig.obj, RIG_FIELDS)
        lvl_off = g.props(g.class_of(self.pawn))['Level'][0]
        self.level = self.p.u32(self.pawn + lvl_off) or 0
        self.level_layout = livegame.Layout(g, self.level, LEVEL_FIELDS)
        self.ts_addr = self.level + self.level_layout.offsets['TimeSeconds']
        props = rig.props
        self.impacts_off = props[rig.key('Impacts')][0]
        self.impact_starts_off = props[rig.key('ImpactStarts')][0]
        self.impact_times_off = props[rig.key('ImpactTimes')][0]
        self.seen_impacts = None
        self._names = {}
        self._objnames = {}
        self.pcs.layout(self.pc)
        self.pawns.layout(self.pawn)
        self.local_classes = (g.r.class_name(self.pawn), g.r.class_name(self.pc))
        if not self.local_classes[0].startswith('SPawn') or not self.local_classes[1].endswith('PlayerController'):
            raise RuntimeError('the local player reads as %s / %s: the name table does not match this game'
                               % self.local_classes)

    # -- raw ------------------------------------------------------------------------------------------------------

    def u32(self, a):
        b = self.p.read(a, 4)
        return struct.unpack('<I', b)[0] if len(b) == 4 else 0

    def time_seconds(self):
        b = self.p.read(self.ts_addr, 4)
        return struct.unpack('<f', b)[0] if len(b) == 4 else None

    def name(self, idx):
        if idx not in self._names:
            self._names[idx] = self.g.r.name(idx)
        return self._names[idx]

    def obj_name(self, ptr):
        if not ptr:
            return None
        if ptr not in self._objnames:
            try:
                self._objnames[ptr] = self.g.r.obj_name(ptr)
            except Exception:  # noqa: BLE001 - a stale pointer reads as no name
                self._objnames[ptr] = None
        return self._objnames[ptr]

    def class_name(self, ptr):
        return self.g.r.class_name(ptr) if ptr else None

    def state_node(self, obj):
        frame = self.u32(obj + livegame.OBJ_STATEFRAME) if obj else 0
        return self.u32(frame + livegame.STATEFRAME_NODE) if frame else 0

    def channels(self, mi):
        if not mi:
            return []
        head = self.p.read(mi + livegame.MI_CHANNELS, 8)
        if len(head) != 8:
            return []
        data, num = struct.unpack('<Ii', head)
        if not data or num <= 0 or num > 64:
            return []
        raw = self.p.read(data, min(num, 24) * livegame.CH_STRIDE)
        out = []
        for c in range(len(raw) // livegame.CH_STRIDE):
            e = raw[c * livegame.CH_STRIDE:(c + 1) * livegame.CH_STRIDE]
            seq, rate, frame = struct.unpack_from('<iff', e, livegame.CH_SEQ)
            alpha = struct.unpack_from('<f', e, livegame.CH_ALPHA)[0]
            if seq <= 0 or (c > 0 and alpha <= 0.0):
                continue
            out.append([c, seq, frame, rate, 1.0 if c == 0 else alpha])
        return out

    def last_damages(self, pc):
        lay = self.pcs.layout(pc)
        props = self.g.props(self.g.class_of(pc))
        if 'LastDamages' not in props:
            return None
        raw = self.p.read(pc + props['LastDamages'][0], LAST_DAMAGES * DAMAGE_STRIDE)
        if len(raw) != LAST_DAMAGES * DAMAGE_STRIDE:
            return None
        del lay
        return [[raw[i * DAMAGE_STRIDE]] + list(struct.unpack_from('<iii', raw, i * DAMAGE_STRIDE + 4))
                for i in range(LAST_DAMAGES)]

    # -- one side -------------------------------------------------------------------------------------------------

    def side(self, pawn, pc):
        """Raw blocks of one player: pawn, controller, weapon, damage history, animation channels, state nodes."""
        out = {'pawn': self.pawns.read(self.p, pawn) if pawn else None,
               'pc': self.pcs.read(self.p, pc) if pc else None}
        weapon = (out['pawn'] or {}).get('MyWeapon') or 0
        out['weapon'] = self.weapons.read(self.p, weapon) if weapon else None
        hud = (out['pc'] or {}).get('myHUD') or 0
        out['hud'] = self.huds.read(self.p, hud) if hud else None
        out['hist'] = self.last_damages(pc) if pc else None
        out['anim'] = self.channels((out['pawn'] or {}).get('MeshInstance'))
        out['nodes'] = [self.state_node(pawn), self.state_node(pc), self.state_node(weapon)]
        out['ptrs'] = [pawn, pc, weapon]
        return out

    def _read(self):
        lb = self.p.read(self.level + self.level_layout.lo, self.level_layout.hi - self.level_layout.lo)
        if len(lb) != self.level_layout.hi - self.level_layout.lo:
            return None
        rb = self.p.read(self.rig.obj + self.rig_layout.lo, self.rig_layout.hi - self.rig_layout.lo)
        rig = self.rig_layout.decode(rb) if len(rb) == self.rig_layout.hi - self.rig_layout.lo else None
        if rig is None:
            return None
        pc, pawn = self.pc, self.u32(self.pc + self.pcs.layout(self.pc).offsets['Pawn'])
        local = self.side(pawn, pc)
        tpawn, tpc = rig.get('Target') or 0, rig.get('Dummy') or 0
        target = self.side(tpawn, tpc) if (tpawn or tpc) else None
        gren = rig.get('Grenade') or 0
        grenade = None
        if gren:
            b = self.p.read(gren + self.g.off_loc, 12)
            v = self.p.read(gren + self.g.off_vel, 12)
            if len(b) == 12 and len(v) == 12:
                grenade = {'loc': list(struct.unpack('<3f', b)), 'vel': list(struct.unpack('<3f', v))}
        level = self.level_layout.decode(lb)
        key = repr((local, target, grenade, rig))
        return level, rig, local, target, grenade, key

    def new_impacts(self, count):
        """Impacts the rig recorded since the last call: [x, y, z, time, start x, start y, start z]."""
        if self.seen_impacts is None:
            self.seen_impacts = count
            return []
        out = []
        first = max(self.seen_impacts, count - IMPACT_SLOTS)
        for n in range(first, count):
            k = n % IMPACT_SLOTS
            loc = struct.unpack('<3f', self.p.read(self.rig.obj + self.impacts_off + 12 * k, 12))
            start = struct.unpack('<3f', self.p.read(self.rig.obj + self.impact_starts_off + 12 * k, 12))
            t = struct.unpack('<f', self.p.read(self.rig.obj + self.impact_times_off + 4 * k, 4))[0]
            out.append(list(loc) + [t] + list(start))
        self.seen_impacts = count
        return out

    def sample(self, frame_time, started):
        clock = time.perf_counter
        while clock() - started < self.delay:
            pass
        tries = 0
        while True:
            tries += 1
            if self.time_seconds() != frame_time:
                return None
            first = self._read()
            if first is None or self.time_seconds() != frame_time:
                return None
            t = clock()
            while clock() - t < self.settle_check:
                pass
            second = self._read()
            if second is None or self.time_seconds() != frame_time:
                return None
            if first[-1] == second[-1] or tries >= 25:
                level, rig, local, target, grenade, _ = second
                return {'level': level, 'rig': rig, 'local': local, 'target': target, 'grenade': grenade,
                        'tries': tries, 'stable': first[-1] == second[-1], 'readAt': clock() - started}

    def wait_frame(self, prev, timeout=2.0):
        clock = time.perf_counter
        end = clock() + timeout
        while True:
            t = self.time_seconds()
            if t != prev:
                return t, clock()
            if clock() > end:
                raise TimeoutError('the level clock did not move for %.1fs (paused or closed?)' % timeout)

    def next_sample(self, prev):
        torn = 0
        t, at = self.wait_frame(prev)
        while True:
            s = self.sample(t, at)
            if s is not None:
                s['t'] = t
                s['torn'] = torn
                return s
            torn += 1
            t, at = self.wait_frame(t)

    # -- local input ------------------------------------------------------------------------------------------------

    def write_input(self, name, value):
        lay = self.pcs.layout(self.pc)
        off = lay.offsets.get(name)
        if off is None:
            props = self.g.props(self.g.class_of(self.pc))
            lower = {k.lower(): k for k in props}
            off = props[lower[name.lower()]][0]
        self.p.write(self.pc + off, bytes([int(value) & 0xFF]))

    def offsets(self):
        return {'pawn': self.pawns.header(), 'controller': self.pcs.header(), 'weapon': self.weapons.header(),
                'hud': self.huds.header(),
                'rig': {'offsets': self.rig_layout.offsets, 'missing': self.rig_layout.missing},
                'level': self.level_layout.offsets, 'stateFrame': [livegame.OBJ_STATEFRAME, livegame.STATEFRAME_NODE],
                'lastDamages': {'count': LAST_DAMAGES, 'stride': DAMAGE_STRIDE},
                'animChannels': {'array': livegame.MI_CHANNELS, 'stride': livegame.CH_STRIDE}}
