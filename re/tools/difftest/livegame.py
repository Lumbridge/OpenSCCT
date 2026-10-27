"""The running original, seen frame by frame: field offsets from the game's own reflection data,
one stable sample per frame, and input writes on the local controller.

Frames: LevelInfo.TimeSeconds moves on once per frame, at the start of the level tick. A sample
of frame k is taken after TimeSeconds changes to k's value and the tick has finished: the reader
waits a short delay, reads the blocks, re-reads TimeSeconds (a change means the next frame began
during the read: the sample is torn and dropped), and reads the moving fields a second time (a
difference means the tick was still running: try again). Inputs for frame k+1 are written right
after the sample of frame k, while the game renders or sleeps, so the tick of k+1 sees them; when
TimeSeconds has already moved by the time the write lands, the frame is marked late.

Memory access goes through the RE+ driver (game_tools.Session / scctdrive.Game), which opens only
a game running from the sandbox. Reads cover the pawn, its controller, the level info and the
mesh instance's animation channels. Writes cover the controller's input bytes, bPressedJump,
bFastSpeed, the view rotation, and (setup only) the pawn's Location, Rotation and Health. Physics
is never written.
"""
from __future__ import annotations

import ctypes
import math
import struct
import time

# UObject: +0x0C StateFrame (FStateFrame*), whose +0x18 is the current UState.
OBJ_STATEFRAME = 0x0C
STATEFRAME_NODE = 0x18
# Mesh instance animation channels (MeshAnimChannel, 144 bytes each, TArray at MeshInstance+0x100):
# +8 sequence name, +12 rate (per second, over the frame count), +16 frame (0..1), +80 blend alpha
# (channel 0 is the base pose), +100 tween weight while the frame is negative.
MI_CHANNELS = 0x100
CH_STRIDE = 144
CH_SEQ, CH_RATE, CH_FRAME, CH_ALPHA, CH_TWEEN = 8, 12, 16, 80, 100

PAWN_FIELDS = {
    'Location': 'vec', 'Velocity': 'vec', 'Acceleration': 'vec', 'Rotation': 'rot', 'Physics': 'byte',
    'Health': 'int', 'bIsCrouched': 'bool', 'GroundSpeed': 'float', 'CollisionHeight': 'float',
    'nJumpNumber': 'int', 'bIsRolling': 'bool', 'MeshInstance': 'ptr', 'Level': 'ptr', 'Base': 'ptr',
}
PC_FIELDS = {
    'Rotation': 'rot', 'aForward': 'float', 'aStrafe': 'float', 'fNormePadMove': 'float', 'iMove': 'int',
    'fZMaxFalling': 'float', 'bFastSpeed': 'bool', 'bCrouchWithRoll': 'bool', 'bPressedJump': 'bool',
    'BT_Up': 'byte', 'BT_Down': 'byte', 'BT_Left': 'byte', 'BT_Right': 'byte', 'bCrouch': 'byte',
    'bRoll': 'byte', 'bUsing': 'byte', 'bFireMode': 'byte', 'bUseBackToWall': 'byte',
    'MyDesiredRotation': 'rot', 'LocalMyDesiredRotation': 'rot', 'Pawn': 'ptr',
}
LEVEL_FIELDS = {'TimeSeconds': 'float', 'm_dT': 'float', 'TimeDilation': 'float'}

# Traversal extras, read only when a scenario asks for them (profile traversal or record --traversal): the GE
# the pawn holds (ZipP1/ZipP2 and the candidates NewZipP1/NewZipP2), the move flags the GE states and natives
# use, and the controller's edge detectors and timers. Names missing from a build are listed in the header.
TRAVERSAL_PAWN_FIELDS = {
    'ZipP1': 'vec', 'ZipP2': 'vec', 'NewZipP1': 'vec', 'NewZipP2': 'vec', 'LastZipP': 'vec', 'm_geoNormal': 'vec',
    'vBackFeetNormal': 'vec', 'PrePivot': 'vec', 'CollisionRadius': 'float', 'bCanStop': 'bool',
    'bIsZipSpeed': 'bool', 'bIsLeftPart': 'bool', 'TypeLedge': 'byte', 'BTW': 'byte', 'IK_Type': 'byte',
    'AnimAction': 'name', 'bIsWalking': 'bool',
}
TRAVERSAL_PC_FIELDS = {
    'bCanStop': 'bool', 'iOldMove': 'int', 'iRealMove': 'int', 'bTestBackFeetGrab': 'bool', 'bFindGE': 'bool',
    'bBlockControlForJump': 'bool', 'bRollAction': 'bool', 'bWantsToMove': 'bool', 'vWallNormal': 'vec',
    'm_LastSpecialMoveTime': 'float', 'm_LastRollingTime': 'float', 'aForwardRoulade': 'float',
    'aStrafeRoulade': 'float', 'fGEZLocation': 'float', 'bToggleBTW': 'byte', 'bToggleUsing': 'byte',
    'oldbUsing': 'byte', 'oldbUseBackToWall': 'byte',
}
# Mesh instance root-motion lock (measured on MissE, Bridge traversal note 2026-09-28): +0x184 lock mode (0 off,
# 1 or 3), then four transforms of 12 floats (position, three rotation rows): the root bone key and world
# transform at the lock (+0x1F0, +0x220) and the current ones (+0x250, +0x280).
MI_LOCK_MODE = 0x184
MI_LOCK_KEY, MI_LOCK_WORLD, MI_ROOT_KEY, MI_ROOT_WORLD = 0x1F0, 0x220, 0x250, 0x280
MI_LOCK_SPAN = MI_ROOT_WORLD + 48 - MI_LOCK_MODE
CH_TWEEN_RATE = 24
ANIM_CHANNELS = 16

_winmm = None


def fine_timer(on=True):
    """1 ms sleep granularity on Windows while recording (the default is 15.6 ms)."""
    global _winmm
    try:
        _winmm = _winmm or ctypes.WinDLL('winmm')
        (_winmm.timeBeginPeriod if on else _winmm.timeEndPeriod)(1)
    except (OSError, AttributeError):   # not Windows
        pass


class Layout:
    """Offsets of the fields of one object, read from the game's reflection data."""

    def __init__(self, game, obj, fields):
        props = game.props(game.class_of(obj))
        self.fields = {}
        missing = []
        for name, kind in fields.items():
            p = props.get(name)
            if not p:
                missing.append(name)
                continue
            off, pcls, es, ad, flags, mask = p
            self.fields[name] = (off, kind, mask or 1)
        self.missing = missing
        offs = [o for o, _, _ in self.fields.values()]
        self.lo = min(offs) if offs else 0
        self.hi = max(offs) + 12 if offs else 0
        self.offsets = {n: o for n, (o, _, _) in self.fields.items()}

    def decode(self, block):
        out = {}
        for name, (off, kind, mask) in self.fields.items():
            at = off - self.lo
            if kind == 'vec':
                out[name] = list(struct.unpack_from('<3f', block, at))
            elif kind == 'rot':
                out[name] = list(struct.unpack_from('<3i', block, at))
            elif kind == 'float':
                out[name] = struct.unpack_from('<f', block, at)[0]
            elif kind in ('int', 'name'):
                out[name] = struct.unpack_from('<i', block, at)[0]
            elif kind == 'ptr':
                out[name] = struct.unpack_from('<I', block, at)[0]
            elif kind == 'byte':
                out[name] = block[at]
            elif kind == 'bool':
                out[name] = int(bool(struct.unpack_from('<I', block, at)[0] & mask))
        return out


class LiveGame:
    def __init__(self, session, delay=0.002, settle_check=0.0004):
        self.session = session
        self.g = session.attach()
        self.pc, self.pawn = session.player()
        if not self.pawn:
            raise RuntimeError('the local player has no pawn')
        self.p = self.g.p
        pawn_class, pc_class = self.g.r.class_name(self.pawn), self.g.r.class_name(self.pc)
        if not pawn_class.startswith('SPawn') or not pc_class.endswith('PlayerController'):
            raise RuntimeError('the local player reads as %s / %s: the name table does not match this game'
                               % (pawn_class, pc_class))
        self.delay = delay
        self.settle_check = settle_check
        self.pawn_layout = Layout(self.g, self.pawn, PAWN_FIELDS)
        self.pc_layout = Layout(self.g, self.pc, PC_FIELDS)
        self.level = self._u32(self.pawn + self.pawn_layout.offsets['Level'])
        self.level_layout = Layout(self.g, self.level, LEVEL_FIELDS)
        self.ts_addr = self.level + self.level_layout.offsets['TimeSeconds']
        self.pawn_class = self.g.r.class_name(self.pawn)
        self.pc_class = self.g.r.class_name(self.pc)
        self._names = {}
        self._objects = {}
        self.traversal = False
        self.ges = []

    def enable_traversal(self, on=True, ges=()):
        """Read the traversal extras with every sample while on. `ges` is the map's GE list
        ([{'index', 'kind', 'points'}]) used to name the GE the pawn holds."""
        if on and not hasattr(self, 'tpawn_layout'):
            self.tpawn_layout = Layout(self.g, self.pawn, TRAVERSAL_PAWN_FIELDS)
            self.tpc_layout = Layout(self.g, self.pc, TRAVERSAL_PC_FIELDS)
        self.traversal = bool(on)
        self.ges = list(ges)

    def traversal_offsets(self):
        if not self.traversal:
            return None
        return {'pawn': self.tpawn_layout.offsets, 'controller': self.tpc_layout.offsets,
                'missing': {'pawn': self.tpawn_layout.missing, 'controller': self.tpc_layout.missing},
                'rootLock': {'mode': MI_LOCK_MODE, 'lockKey': MI_LOCK_KEY, 'lockWorld': MI_LOCK_WORLD,
                             'rootKey': MI_ROOT_KEY, 'rootWorld': MI_ROOT_WORLD},
                'animChannels': {'count': ANIM_CHANNELS, 'tweenRate': CH_TWEEN_RATE}}

    # -- raw access ---------------------------------------------------------------------------------

    def _u32(self, a):
        b = self.p.read(a, 4)
        return struct.unpack('<I', b)[0] if len(b) == 4 else 0

    def time_seconds(self):
        b = self.p.read(self.ts_addr, 4)
        return struct.unpack('<f', b)[0] if len(b) == 4 else None

    def name(self, idx):
        if idx not in self._names:
            self._names[idx] = self.g.r.name(idx)
        return self._names[idx]

    def state_name(self, obj):
        frame = self._u32(obj + OBJ_STATEFRAME)
        node = self._u32(frame + STATEFRAME_NODE) if frame else 0
        if not node:
            return None
        return self.g.r.obj_name(node)

    def offsets(self):
        return {'pawn': self.pawn_layout.offsets, 'controller': self.pc_layout.offsets,
                'level': self.level_layout.offsets, 'missing': {'pawn': self.pawn_layout.missing,
                                                                'controller': self.pc_layout.missing,
                                                                'level': self.level_layout.missing},
                'stateFrame': [OBJ_STATEFRAME, STATEFRAME_NODE],
                'animChannels': {'array': MI_CHANNELS, 'stride': CH_STRIDE, 'seq': CH_SEQ, 'rate': CH_RATE,
                                 'frame': CH_FRAME, 'alpha': CH_ALPHA, 'tween': CH_TWEEN}}

    # -- one sample ---------------------------------------------------------------------------------

    def _blocks(self):
        pl, cl, ll = self.pawn_layout, self.pc_layout, self.level_layout
        pb = self.p.read(self.pawn + pl.lo, pl.hi - pl.lo)
        cb = self.p.read(self.pc + cl.lo, cl.hi - cl.lo)
        lb = self.p.read(self.level + ll.lo, ll.hi - ll.lo)
        if len(pb) != pl.hi - pl.lo or len(cb) != cl.hi - cl.lo or len(lb) != ll.hi - ll.lo:
            return None
        return pb, cb, lb

    def _channels(self, mi):
        if not mi:
            return [], b''
        head = self.p.read(mi + MI_CHANNELS, 8)
        if len(head) != 8:
            return [], b''
        data, num = struct.unpack('<Ii', head)
        if not data or num <= 0 or num > 64:
            return [], b''
        raw = self.p.read(data, num * CH_STRIDE)
        out = []
        for c in range(len(raw) // CH_STRIDE):
            e = raw[c * CH_STRIDE:(c + 1) * CH_STRIDE]
            seq, rate, frame = struct.unpack_from('<iff', e, CH_SEQ)
            alpha = struct.unpack_from('<f', e, CH_ALPHA)[0]
            if seq <= 0 or (c > 0 and alpha <= 0.0):
                continue
            ch = [c, self.name(seq), frame, rate, 1.0 if c == 0 else alpha]
            if c == 0:
                ch.append(struct.unpack_from('<f', e, CH_TWEEN)[0])
            out.append(ch)
        return out, raw

    def _read(self):
        blocks = self._blocks()
        if blocks is None:
            return None
        pb, cb, lb = blocks
        pawn = self.pawn_layout.decode(pb)
        pc = self.pc_layout.decode(cb)
        level = self.level_layout.decode(lb)
        anim, raw = self._channels(pawn.get('MeshInstance'))
        pstate = self._u32(self.pawn + OBJ_STATEFRAME)
        cstate = self._u32(self.pc + OBJ_STATEFRAME)
        extra = self._traversal_read(pawn.get('MeshInstance'), raw) if self.traversal else None
        # Only the decoded fields: others in the same blocks change while the frame renders
        # (LastRenderTime and the like).
        key = (repr(sorted(pawn.items())), repr(sorted(pc.items())), repr(anim),
               self._u32(pstate + STATEFRAME_NODE) if pstate else 0,
               self._u32(cstate + STATEFRAME_NODE) if cstate else 0,
               repr(extra) if extra is not None else None)
        return pawn, pc, level, anim, key, extra

    # -- traversal extras ---------------------------------------------------------------------------

    def _block(self, obj, layout):
        if not layout.fields:
            return {}
        b = self.p.read(obj + layout.lo, layout.hi - layout.lo)
        if len(b) != layout.hi - layout.lo:
            return None
        return layout.decode(b)

    def object_name(self, ptr):
        if not ptr:
            return None
        if ptr not in self._objects:
            try:
                self._objects[ptr] = self.g.r.obj_name(ptr)
            except Exception:  # noqa: BLE001 - a stale pointer reads as no name
                self._objects[ptr] = None
        return self._objects[ptr]

    def _traversal_read(self, mi, raw_channels):
        tp = self._block(self.pawn, self.tpawn_layout)
        tc = self._block(self.pc, self.tpc_layout)
        if tp is None or tc is None:
            return None
        if 'AnimAction' in tp:
            tp['AnimAction'] = self.name(tp['AnimAction']) if tp['AnimAction'] > 0 else None
        return {'pawn': tp, 'pc': tc, 'rootMotion': self._root_lock(mi), 'channels': self._all_channels(raw_channels)}

    def _root_lock(self, mi):
        if not mi:
            return None
        raw = self.p.read(mi + MI_LOCK_MODE, MI_LOCK_SPAN)
        if len(raw) != MI_LOCK_SPAN:
            return None
        return decode_root_lock(raw)

    def _all_channels(self, raw):
        """Channels 0..15 that have a sequence, whatever their blend alpha: [channel, sequence, frame, rate,
        alpha, tween weight, tween rate]."""
        out = []
        for c in range(min(ANIM_CHANNELS, len(raw) // CH_STRIDE)):
            e = raw[c * CH_STRIDE:(c + 1) * CH_STRIDE]
            seq, rate, frame = struct.unpack_from('<iff', e, CH_SEQ)
            if seq <= 0:
                continue
            alpha = struct.unpack_from('<f', e, CH_ALPHA)[0]
            tween = struct.unpack_from('<f', e, CH_TWEEN)[0]
            tween_rate = struct.unpack_from('<f', e, CH_TWEEN_RATE)[0]
            out.append([c, self.name(seq), frame, rate, alpha, tween, tween_rate])
        return out

    def sample(self, frame_time, started):
        """A stable sample of the frame whose TimeSeconds is frame_time (detected at perf_counter
        `started`), or None when the next frame began first."""
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
            if first[4] == second[4] or tries >= 25:
                pawn, pc, level, anim, key, extra = second
                out = {'pawn': pawn, 'pc': pc, 'level': level, 'anim': anim, 'tries': tries,
                       'ps': self.g.r.obj_name(key[3]) if key[3] else None,
                       'cs': self.g.r.obj_name(key[4]) if key[4] else None,
                       'stable': first[4] == second[4], 'readAt': clock() - started}
                if extra is not None:
                    out['traversal'] = extra
                return out

    def wait_frame(self, prev, timeout=2.0):
        """Spin until TimeSeconds differs from prev; returns (new value, perf_counter at detection)."""
        clock = time.perf_counter
        end = clock() + timeout
        read = self.time_seconds
        while True:
            t = read()
            if t != prev:
                return t, clock()
            if clock() > end:
                raise TimeoutError('the level clock did not move for %.1fs (paused or closed?)' % timeout)

    def next_sample(self, prev):
        """The next frame's stable sample: (sample, frames skipped while torn)."""
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

    def states(self):
        return self.state_name(self.pawn), self.state_name(self.pc)

    # -- writes -------------------------------------------------------------------------------------

    def write_byte(self, name, value):
        off = self.pc_layout.offsets[name]
        self.p.write(self.pc + off, bytes([int(value) & 0xFF]))

    def write_bool(self, obj, layout, name, value):
        off, _, mask = layout.fields[name]
        cur = self._u32(obj + off)
        self.p.write(obj + off, struct.pack('<I', (cur | mask) if value else (cur & ~mask & 0xFFFFFFFF)))

    def press_jump(self):
        self.write_bool(self.pc, self.pc_layout, 'bPressedJump', True)

    def set_fast_speed(self, value):
        self.write_bool(self.pc, self.pc_layout, 'bFastSpeed', bool(value))

    def set_view(self, pitch, yaw):
        """The controller's view rotation; a merc's controller copies MyDesiredRotation and
        LocalMyDesiredRotation over its Rotation each tick, so those are written too."""
        data = struct.pack('<3i', int(pitch), int(yaw) & 0xFFFF, 0)
        for name in ('Rotation', 'MyDesiredRotation', 'LocalMyDesiredRotation'):
            if name in self.pc_layout.offsets:
                self.p.write(self.pc + self.pc_layout.offsets[name], data)

    def teleport(self, location, yaw=None):
        self.p.write(self.pawn + self.pawn_layout.offsets['Location'], struct.pack('<3f', *location))
        if yaw is not None:
            rot = self.p.read(self.pawn + self.pawn_layout.offsets['Rotation'], 12)
            p, _, r = struct.unpack('<3i', rot)
            self.p.write(self.pawn + self.pawn_layout.offsets['Rotation'], struct.pack('<3i', p, int(yaw) & 0xFFFF, r))

    def physics(self):
        b = self.p.read(self.pawn + self.pawn_layout.offsets['Physics'], 1)
        return b[0] if len(b) == 1 else None

    def write_int(self, obj, layout, name, value):
        self.p.write(obj + layout.offsets[name], struct.pack('<i', int(value)))

    def write_setup(self, target, field, value):
        if field == 'Physics':
            raise ValueError('Physics is never written')
        if target == 'pawn' and field == 'Health':
            self.write_int(self.pawn, self.pawn_layout, 'Health', value)
        elif target == 'controller' and field == 'bFastSpeed':
            self.set_fast_speed(value)
        else:
            raise ValueError('%s.%s is not a setup field' % (target, field))


def horizontal_speed(v):
    return math.hypot(v[0], v[1])


def _xform(raw, off):
    v = struct.unpack_from('<12f', raw, off - MI_LOCK_MODE)
    return list(v[:3]), [list(v[3:6]), list(v[6:9]), list(v[9:12])]


def _row_mul(v, m):
    """Row vector v times the 3x3 matrix m (FMatrix row convention)."""
    return [v[0] * m[0][j] + v[1] * m[1][j] + v[2] * m[2][j] for j in range(3)]


def decode_root_lock(raw):
    """The root-motion lock of a mesh instance from the bytes at MeshInstance + MI_LOCK_MODE: the mode, the
    current root bone key and world positions, and while locked the mesh origin and yaw the lock holds:
    MeshToWorld = Key^T x World, origin = World.pos - Key.pos x MeshToWorld (the move's start Location and
    yaw, RotOrigin included)."""
    mode = struct.unpack_from('<i', raw, 0)[0]
    key_pos, _ = _xform(raw, MI_ROOT_KEY)
    world_pos, _ = _xform(raw, MI_ROOT_WORLD)
    out = {'mode': mode, 'rootKey': key_pos, 'rootWorld': world_pos}
    if mode:
        lk_pos, lk = _xform(raw, MI_LOCK_KEY)
        lw_pos, lw = _xform(raw, MI_LOCK_WORLD)
        kt = [[lk[j][i] for j in range(3)] for i in range(3)]
        m = [_row_mul(kt[i], lw) for i in range(3)]
        moved = _row_mul(lk_pos, m)
        out['origin'] = [lw_pos[i] - moved[i] for i in range(3)]
        out['yaw'] = int(round(math.atan2(m[0][1], m[0][0]) * 32768.0 / math.pi)) & 0xFFFF
    return out


def ge_index(ges, p1, p2, tol=2.0):
    """The index of the GE whose first two points are p1 and p2 (either order, within tol), or None."""
    if not p1 or not p2 or not ges:
        return None
    for g in ges:
        a, b = g['points'][0], g['points'][1]
        for x, y in ((a, b), (b, a)):
            if all(abs(u - v) <= tol for u, v in zip(x, p1)) and all(abs(u - v) <= tol for u, v in zip(y, p2)):
                return g['index']
    return None
