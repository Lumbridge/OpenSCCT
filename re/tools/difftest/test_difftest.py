"""Tests of the difftest recorder on synthetic data (no game needed).

    python -I -B -m unittest discover -s re/tools/difftest -p "test_*.py"
"""
from __future__ import annotations

import copy
import glob
import json
import os
import struct
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import livegame  # noqa: E402
import record  # noqa: E402
import scenario as scn  # noqa: E402
import trace as tr  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))

BASE = {
    'format': 'svm.scenario/1', 'id': 'movement.synthetic', 'title': 'synthetic', 'map': 'TestBox',
    'mode': 'playlevel', 'players': [{'team': 'spy'}],
    'spawn': {'floor': [0.0, 0.0, 0.0], 'yaw': 16384},
    'setup': [{'op': 'teleport'}, {'op': 'stance', 'crouched': False}, {'op': 'fastSpeed', 'value': True},
              {'op': 'write', 'target': 'pawn', 'field': 'Health', 'value': 100}, {'op': 'waitStill', 'maxFrames': 60}],
    'settleFrames': 10, 'durationFrames': 20,
    'inputs': [{'frame': 0, 'buttons': {'BT_Up': 1}}, {'frame': 5, 'jump': True},
               {'frame': 8, 'view': {'pitch': 0, 'yaw': 0}}, {'frame': 10, 'buttons': {'BT_Up': 0}}],
    'repeats': 3, 'profile': 'locomotion', 'covers': ['uc:SBase.SPlayerController:s_PlayerJump.PlayerMove'],
}


class ScenarioTests(unittest.TestCase):
    def test_valid(self):
        self.assertEqual(scn.validate(BASE), [])

    def test_problems(self):
        bad = copy.deepcopy(BASE)
        bad['setup'].append({'op': 'write', 'target': 'pawn', 'field': 'Physics', 'value': 2})
        bad['inputs'].append({'frame': 12, 'buttons': {'BT_Jump': 1}})
        bad['inputs'].append({'frame': 3, 'buttons': {'BT_Left': 1}})
        bad['inputs'].append({'frame': 99, 'jump': True})
        bad['covers'] = ['SPlayerController.Jump']
        problems = scn.validate(bad)
        text = ' | '.join(problems)
        self.assertIn('Physics is not writable', text)
        self.assertIn('unknown button BT_Jump', text)
        self.assertIn('frames must not go backwards', text)
        self.assertIn('past durationFrames', text)
        self.assertIn('covers must list unit ids', text)

    def test_timeline(self):
        t = scn.timeline(BASE)
        self.assertEqual(len(t), 20)
        self.assertEqual(t[0]['buttons'], {'BT_Up': 1})
        self.assertEqual(t[4]['buttons'], {'BT_Up': 1})
        self.assertTrue(t[5]['jump'])
        self.assertFalse(t[6]['jump'])
        self.assertEqual(t[8]['view'], (0, 0))
        self.assertIsNone(t[9]['view'])
        self.assertEqual(t[10]['buttons'], {'BT_Up': 0})
        self.assertEqual(record.Runner.inputs_of(t[5]), {'BT_Up': 1, 'jump': 1})
        self.assertEqual(record.Runner.inputs_of(t[12]), {})

    def test_tracked_scenarios_are_valid(self):
        files = glob.glob(os.path.join(REPO, 'tests', 'scenarios', '**', '*.scenario.json'), recursive=True)
        self.assertGreaterEqual(len(files), 10)
        ids = set()
        for path in files:
            doc = scn.load(path)
            self.assertNotIn(doc['id'], ids)
            ids.add(doc['id'])
            self.assertEqual(len(doc['_sha256']), 64)


def walk(run, offset=0.0, release=12, dts=None):
    frames = [{'f': -1, 't': 10.0, 'dt': 1 / 60, 'loc': [0.0, offset, 0.0], 'vel': [0.0, 0.0, 0.0], 'phys': 1,
               'ps': 'P', 'cs': 's_PlayerWalkingSt0H'}]
    t, y, v = 10.0, offset, 0.0
    for f in range(20):
        dt = dts[f] if dts else 1 / 60
        t += dt
        v = min(423.5, v + 3300 * dt) if f < release else 0.0
        y += v * dt
        frames.append({'f': f, 't': t, 'dt': dt, 'loc': [0.0, y, 0.0], 'vel': [0.0, v, 0.0], 'phys': 1, 'ps': 'P',
                       'cs': 's_PlayerWalkingSt0H'})
    return frames


class TraceTests(unittest.TestCase):
    def test_f32_text_round_trips(self):
        for value in (0.1, -2031.0835, 423.5, 1e-7, 16384.0, -1055.0, 0.016655866):
            f = struct.unpack('<f', struct.pack('<f', value))[0]
            self.assertEqual(struct.pack('<f', tr.f32(f)), struct.pack('<f', f))
            self.assertLessEqual(len(repr(tr.f32(f))), 16)

    def test_write_read(self):
        with tempfile.TemporaryDirectory() as d:
            path = os.path.join(d, 'run1.jsonl.gz')
            w = tr.TraceWriter(path, {'scenario': {'id': 'movement.synthetic'}, 'run': 1})
            for fr in walk(1):
                w.frame(fr)
            w.close({'torn': 0})
            header, frames, footer = tr.read(path)
            self.assertEqual(header['format'], tr.FORMAT)
            self.assertEqual(len(frames), 21)
            self.assertEqual(footer['frames'], 21)
            self.assertTrue(footer['end'])

    def test_metrics(self):
        m = tr.metrics(walk(1), walk(2))
        self.assertAlmostEqual(m['locRms'], 0.0)
        m = tr.metrics(walk(1), walk(2, offset=1.5))
        self.assertAlmostEqual(m['locRms'], 1.5, places=6)
        self.assertAlmostEqual(m['locEnd'], 1.5, places=6)
        self.assertAlmostEqual(m['velRms'], 0.0, places=6)
        m = tr.metrics(walk(1), walk(2, release=13))
        self.assertAlmostEqual(m['locEnd'], 423.5 / 60, places=3)

    def test_envelope_picks_the_medoid(self):
        env = tr.envelope({'run1': walk(1), 'run2': walk(2, offset=2.5), 'run3': walk(3, offset=5.0)})
        self.assertEqual(env['reference'], 'run2')
        self.assertAlmostEqual(env['envelope']['locRms'], 5.0, places=3)
        self.assertTrue(env['states']['controller']['same'])


class FakeProcess:
    """A tiny address space with a clock that moves on after a set number of reads."""

    def __init__(self):
        self.mem = bytearray(0x4000)
        self.reads = 0
        self.tick_after = None
        self.clock_at = None

    def read(self, a, n):
        self.reads += 1
        if self.tick_after is not None and self.reads >= self.tick_after:
            t = struct.unpack_from('<f', self.mem, self.clock_at)[0]
            struct.pack_into('<f', self.mem, self.clock_at, t + 1 / 60)
            self.tick_after = None
        return bytes(self.mem[a:a + n])

    def write(self, a, data):
        self.mem[a:a + len(data)] = data
        return True


class FakeReader:
    def __init__(self, names=None):
        self.names = names or {0x400: 'SPawnAttaque_1Mesh', 0x800: 'SPlayerController', 0x3000: 'LevelInfo'}

    def class_name(self, obj):
        return self.names.get(obj, 'State')

    def name(self, idx):
        return 'Seq%d' % idx

    def obj_name(self, obj):
        return 'S%x' % obj


class FakeGame:
    PROPS = {
        0x100: {'Location': (0x10, 'StructProperty', 12, 1, 0, 0), 'Velocity': (0x20, 'StructProperty', 12, 1, 0, 0),
                'Rotation': (0x30, 'StructProperty', 12, 1, 0, 0), 'Physics': (0x40, 'ByteProperty', 1, 1, 0, 0),
                'Health': (0x44, 'IntProperty', 4, 1, 0, 0), 'bIsCrouched': (0x48, 'BoolProperty', 4, 1, 0, 0x20),
                'CollisionHeight': (0x4C, 'FloatProperty', 4, 1, 0, 0), 'Level': (0x50, 'ObjectProperty', 4, 1, 0, 0),
                'MeshInstance': (0x54, 'ObjectProperty', 4, 1, 0, 0)},
        0x1000: {'Rotation': (0x30, 'StructProperty', 12, 1, 0, 0), 'BT_Up': (0x40, 'ByteProperty', 1, 1, 4, 0),
                 'bCrouch': (0x41, 'ByteProperty', 1, 1, 4, 0), 'bPressedJump': (0x44, 'BoolProperty', 4, 1, 0, 4),
                 'bFastSpeed': (0x48, 'BoolProperty', 4, 1, 0, 0x20), 'aForward': (0x4C, 'FloatProperty', 4, 1, 4, 0)},
        0x2000: {'TimeSeconds': (0x10, 'FloatProperty', 4, 1, 0, 0), 'm_dT': (0x14, 'FloatProperty', 4, 1, 0, 0)},
    }

    def __init__(self, p):
        self.p = p
        self.r = FakeReader()

    def class_of(self, obj):
        return {0x400: 0x100, 0x800: 0x1000, 0x3000: 0x2000}[obj]

    def props(self, cls):
        return self.PROPS[cls]


class FakeSession:
    def __init__(self, game):
        self.game = game

    def attach(self):
        return self.game

    def player(self):
        return 0x800, 0x400


def fake_live():
    p = FakeProcess()
    g = FakeGame(p)
    struct.pack_into('<I', p.mem, 0x400 + 0x50, 0x3000)        # pawn.Level
    struct.pack_into('<3f', p.mem, 0x400 + 0x10, 1.0, 2.0, 3.0)
    struct.pack_into('<i', p.mem, 0x400 + 0x44, 100)
    struct.pack_into('<I', p.mem, 0x400 + 0x48, 0x20)
    struct.pack_into('<f', p.mem, 0x3000 + 0x10, 5.0)           # TimeSeconds
    struct.pack_into('<f', p.mem, 0x3000 + 0x14, 1 / 60)
    live = livegame.LiveGame(FakeSession(g), delay=0.0, settle_check=0.0)
    p.clock_at = 0x3000 + 0x10
    return p, live


class LiveGameTests(unittest.TestCase):
    def test_layout_and_sample(self):
        p, live = fake_live()
        s = live.sample(5.0, 0.0)
        self.assertIsNotNone(s)
        self.assertEqual(s['pawn']['Location'], [1.0, 2.0, 3.0])
        self.assertEqual(s['pawn']['Health'], 100)
        self.assertEqual(s['pawn']['bIsCrouched'], 1)
        self.assertEqual(s['level']['TimeSeconds'], 5.0)
        self.assertTrue(s['stable'])

    def test_a_read_that_crosses_a_frame_is_dropped(self):
        p, live = fake_live()
        p.reads = 0
        p.tick_after = 3                    # the clock moves during the first block reads
        self.assertIsNone(live.sample(5.0, 0.0))

    def test_next_sample_counts_torn_frames(self):
        p, live = fake_live()
        p.reads = 0
        p.tick_after = 4
        s = live.next_sample(4.0)           # 5.0 is new; the read of it tears; the next frame is taken
        self.assertEqual(s['torn'], 1)
        self.assertAlmostEqual(s['t'], 5.0 + 1 / 60, places=5)

    def test_a_wrong_name_table_is_refused(self):
        p = FakeProcess()
        g = FakeGame(p)
        g.r = FakeReader({0x400: 'FindAttClosestTarget', 0x800: 'StaticMeshInstance6781'})
        struct.pack_into('<I', p.mem, 0x400 + 0x50, 0x3000)
        with self.assertRaises(RuntimeError):
            livegame.LiveGame(FakeSession(g))

    def test_stale_name_caches_are_dropped(self):
        import sandbox
        import time
        folder = os.path.join(tempfile.gettempdir(), 'scct-automation', 'out')
        os.makedirs(folder, exist_ok=True)
        pid = 4000000 + os.getpid() % 1000
        path = os.path.join(folder, '_drive_names_%d.pkl' % pid)
        with open(path, 'wb') as f:
            f.write(b'stale')
        old = time.time() - 3600
        os.utime(path, (old, old))
        try:
            self.assertEqual(sandbox.drop_stale_name_caches([pid], time.time() - 60), [path])
            self.assertFalse(os.path.exists(path))
        finally:
            if os.path.exists(path):
                os.remove(path)

    def test_writes(self):
        p, live = fake_live()
        live.write_byte('BT_Up', 1)
        self.assertEqual(p.mem[0x800 + 0x40], 1)
        live.press_jump()
        self.assertEqual(struct.unpack_from('<I', p.mem, 0x800 + 0x44)[0] & 4, 4)
        live.set_fast_speed(True)
        live.set_fast_speed(False)
        self.assertEqual(struct.unpack_from('<I', p.mem, 0x800 + 0x48)[0] & 0x20, 0)
        live.set_view(-100, 70000)
        self.assertEqual(struct.unpack_from('<3i', p.mem, 0x800 + 0x30), (-100, 70000 & 0xFFFF, 0))
        live.teleport([10.0, 20.0, 30.0], 16384)
        self.assertEqual(struct.unpack_from('<3f', p.mem, 0x400 + 0x10), (10.0, 20.0, 30.0))
        self.assertEqual(struct.unpack_from('<3i', p.mem, 0x400 + 0x30)[1], 16384)
        with self.assertRaises(ValueError):
            live.write_setup('pawn', 'Physics', 2)


class TraversalScenarioTests(unittest.TestCase):
    def test_traversal_profile_and_leave_ge(self):
        doc = copy.deepcopy(BASE)
        doc['profile'] = 'traversal'
        doc['setup'].insert(0, {'op': 'leaveGE', 'maxFrames': 240})
        self.assertEqual(scn.validate(doc), [])
        self.assertEqual(scn.profile(doc), 'traversal')
        self.assertEqual(scn.profile(BASE), 'locomotion')
        doc['setup'][0] = {'op': 'leaveGE'}
        doc['profile'] = 'parkour'
        text = ' | '.join(scn.validate(doc))
        self.assertIn('leaveGE.maxFrames must be a positive int', text)
        self.assertIn('profile must be one of locomotion, traversal', text)

    def test_tracked_traversal_scenarios(self):
        files = glob.glob(os.path.join(REPO, 'tests', 'scenarios', 'traversal', '*.scenario.json'))
        self.assertGreaterEqual(len(files), 12)
        for path in files:
            doc = scn.load(path)
            self.assertEqual(scn.profile(doc), 'traversal', path)
            self.assertEqual(doc['setup'][0]['op'], 'leaveGE', path)


def lock_bytes(mode, lock_key, lock_world, root_key=(0.0, 0.0, 0.0), root_world=(0.0, 0.0, 0.0)):
    """MeshInstance bytes from +0x184: the mode, then the four transforms at their offsets (the current ones with
    identity rows)."""
    raw = bytearray(livegame.MI_LOCK_SPAN)
    struct.pack_into('<i', raw, 0, mode)
    ident = (1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0)
    for off, x in ((livegame.MI_LOCK_KEY, lock_key), (livegame.MI_LOCK_WORLD, lock_world),
                   (livegame.MI_ROOT_KEY, (root_key, ident)), (livegame.MI_ROOT_WORLD, (root_world, ident))):
        pos, rows = x
        struct.pack_into('<12f', raw, off - livegame.MI_LOCK_MODE, *(tuple(pos) + tuple(rows)))
    return bytes(raw)


class TraversalExtrasTests(unittest.TestCase):
    IDENT = (1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0)
    YAW90 = (0.0, 1.0, 0.0, -1.0, 0.0, 0.0, 0.0, 0.0, 1.0)     # rows: X to +Y, Y to -X

    def test_root_lock_origin_and_yaw(self):
        raw = lock_bytes(3, ((10.0, 0.0, 0.0), self.IDENT), ((100.0, 200.0, 300.0), self.YAW90),
                         root_world=(1.0, 2.0, 3.0))
        rm = livegame.decode_root_lock(raw)
        self.assertEqual(rm['mode'], 3)
        for a, b in zip(rm['origin'], (100.0, 190.0, 300.0)):
            self.assertAlmostEqual(a, b, places=4)
        self.assertEqual(rm['yaw'], 16384)
        self.assertEqual(rm['rootWorld'], [1.0, 2.0, 3.0])

    def test_root_lock_key_rotation_is_undone(self):
        # The key carries the same quarter turn as the world: the mesh itself is not turned.
        raw = lock_bytes(1, ((0.0, 10.0, 0.0), self.YAW90), ((5.0, 5.0, 0.0), self.YAW90))
        rm = livegame.decode_root_lock(raw)
        self.assertEqual(rm['yaw'], 0)
        for a, b in zip(rm['origin'], (5.0, -5.0, 0.0)):
            self.assertAlmostEqual(a, b, places=4)

    def test_unlocked_has_no_origin(self):
        rm = livegame.decode_root_lock(lock_bytes(0, ((0, 0, 0), self.IDENT), ((0, 0, 0), self.IDENT)))
        self.assertEqual(rm['mode'], 0)
        self.assertNotIn('origin', rm)

    def test_ge_index(self):
        ges = [{'index': 7, 'kind': 'LG', 'points': [[0.0, 0.0, 10.0], [100.0, 0.0, 10.0]]},
               {'index': 9, 'kind': 'PIPE', 'points': [[5.0, 5.0, 0.0], [5.0, 5.0, 300.0], [5.0, 15.0, 0.0]]}]
        self.assertEqual(livegame.ge_index(ges, [100.5, 0.0, 10.0], [0.0, -1.0, 10.0]), 7)
        self.assertEqual(livegame.ge_index(ges, [5.0, 5.0, 0.0], [5.0, 5.0, 300.0]), 9)
        self.assertIsNone(livegame.ge_index(ges, [0.0, 0.0, 10.0], [50.0, 0.0, 10.0]))
        self.assertIsNone(livegame.ge_index([], [0.0, 0.0, 10.0], [100.0, 0.0, 10.0]))

    def test_sample_carries_the_extras(self):
        p = FakeProcess()
        g = FakeGameTv(p)
        struct.pack_into('<I', p.mem, 0x400 + 0x50, 0x3000)
        struct.pack_into('<3f', p.mem, 0x400 + 0x60, 0.0, 0.0, 10.0)        # ZipP1
        struct.pack_into('<3f', p.mem, 0x400 + 0x6C, 100.0, 0.0, 10.0)      # ZipP2
        struct.pack_into('<i', p.mem, 0x800 + 0x60, 12)                      # iOldMove
        struct.pack_into('<f', p.mem, 0x3000 + 0x10, 5.0)
        p.clock_at = 0x3000 + 0x10
        live = livegame.LiveGame(FakeSession(g), delay=0.0, settle_check=0.0)
        self.assertNotIn('traversal', live.sample(5.0, 0.0))
        live.enable_traversal(True, [{'index': 7, 'kind': 'LG', 'points': [[0.0, 0.0, 10.0], [100.0, 0.0, 10.0]]}])
        s = live.sample(5.0, 0.0)
        tv = s['traversal']
        self.assertEqual(tv['pawn']['ZipP2'], [100.0, 0.0, 10.0])
        self.assertEqual(tv['pc']['iOldMove'], 12)
        self.assertIsNone(tv['rootMotion'])                                  # no mesh instance
        self.assertIn('ZipP1', live.traversal_offsets()['pawn'])
        self.assertIn('bFindGE', live.traversal_offsets()['missing']['controller'])
        live.enable_traversal(False)
        self.assertNotIn('traversal', live.sample(5.0, 0.0))

    def test_frame_record_writes_tv(self):
        class Game:
            ges = [{'index': 7, 'kind': 'LG', 'points': [[0.0, 0.0, 10.0], [100.0, 0.0, 10.0]]}]

            @staticmethod
            def object_name(ptr):
                return 'LevelInfo0' if ptr else None

        runner = record.Runner.__new__(record.Runner)
        runner.game = Game()
        s = {'t': 1.0, 'wallAt': 0.5, 'tries': 1, 'stable': True, 'ps': 'P', 'cs': 'OnLedge', 'anim': [],
             'level': {'m_dT': 1 / 60},
             'pawn': {'Location': [0, 0, 0], 'Velocity': [0, 0, 0], 'Rotation': [0, 0, 0], 'Physics': 9, 'Base': 0x10},
             'pc': {'Rotation': [0, 0, 0]},
             'traversal': {'pawn': {'ZipP1': [100.0, 0.0, 10.0], 'ZipP2': [0.0, 0.0, 10.0]}, 'pc': {'iOldMove': 4},
                           'rootMotion': {'mode': 0}, 'channels': [[0, 'LedgStAlNt0', 0.5, 1.0, 0.0, 0.8, 0.0]]}}
        rec = runner.frame_record(3, s, {}, False, 0, 0.0)
        self.assertEqual(rec['tv']['ge'], 7)
        self.assertIsNone(rec['tv']['newGe'])
        self.assertEqual(rec['tv']['base'], 'LevelInfo0')
        self.assertEqual(rec['rm'], {'mode': 0})
        self.assertEqual(rec['ach'][0][1], 'LedgStAlNt0')


class FakeGameTv(FakeGame):
    PROPS = copy.deepcopy(FakeGame.PROPS)
    PROPS[0x100].update({'ZipP1': (0x60, 'StructProperty', 12, 1, 0, 0), 'ZipP2': (0x6C, 'StructProperty', 12, 1, 0, 0)})
    PROPS[0x1000].update({'iOldMove': (0x60, 'IntProperty', 4, 1, 0, 0)})


def tframe(f, cs, phys, y, anim=None, ge=None, lock=0):
    return {'f': f, 't': 10.0 + f / 60.0, 'loc': [0.0, y, 0.0], 'cs': cs, 'phys': phys,
            'anim': [[0, anim or cs + 'Anim', 0.0, 1.0, 1.0]], 'tv': {'ge': ge}, 'rm': {'mode': lock}}


class PhasesTests(unittest.TestCase):
    def run_frames(self, shift=0):
        out = [tframe(-1, 'Walk', 1, 0.0)]
        for f in range(30):
            if f < 10 + shift:
                out.append(tframe(f, 'Walk', 1, f * 5.0))
            elif f < 20:
                out.append(tframe(f, 'GrabLedge', 9, 50.0, ge=104))
            else:
                out.append(tframe(f, 'TopLedge', 5, 50.0 + (f - 20) * 2.0, anim='LedgStAlUp0', ge=104, lock=3))
        return out

    def test_phases(self):
        import phases
        ps = phases.phases(self.run_frames())
        self.assertEqual([(p['cs'], p['phys']) for p in ps], [('Walk', 1), ('GrabLedge', 9), ('TopLedge', 5)])
        self.assertEqual((ps[0]['f0'], ps[0]['f1']), (0, 9))
        self.assertAlmostEqual(ps[0]['seconds'], 10 / 60.0)
        self.assertAlmostEqual(ps[0]['dxy'], 50.0)             # to the next phase's first sample
        self.assertEqual(ps[1]['ge'], [104])
        self.assertEqual(ps[2]['lock'], [3])
        self.assertEqual(ps[2]['anim0'], ['LedgStAlUp0'])
        self.assertAlmostEqual(ps[2]['seconds'], 9 / 60.0)      # the last phase ends at its last frame

    def test_compare(self):
        import phases
        same = phases.compare({'run1': phases.phases(self.run_frames()), 'run2': phases.phases(self.run_frames(1))})
        self.assertTrue(same['same'])
        self.assertEqual(same['spread'][1]['f0'], [10, 11])
        other = self.run_frames()
        other[15]['cs'] = 'OnLedge'
        diff = phases.compare({'run1': phases.phases(self.run_frames()), 'run2': phases.phases(other)})
        self.assertFalse(diff['same'])
        self.assertNotIn('spread', diff)


class GeSpotTests(unittest.TestCase):
    BOX = [{'index': 0, 'kind': 'LG', 'id': 0, 'points': [[0.0, 0.0, 10.0], [100.0, 0.0, 10.0]]},
           {'index': 1, 'kind': 'LG', 'id': 0, 'points': [[100.0, 0.0, 10.0], [100.0, 100.0, 10.0]]},
           {'index': 2, 'kind': 'LG', 'id': 0, 'points': [[100.0, 100.0, 10.0], [0.0, 100.0, 10.0]]},
           {'index': 3, 'kind': 'LG', 'id': 0, 'points': [[0.0, 100.0, 10.0], [0.0, 0.0, 10.0]]},
           {'index': 4, 'kind': 'LG', 'id': 0, 'points': [[100.0, 100.0, 10.0], [200.0, 100.0, 10.0]]}]

    def test_frames_and_yaws(self):
        import gespots
        mid, u, length, n = gespots.lg_frame(self.BOX[0]['points'])
        self.assertEqual(mid, [50.0, 0.0, 10.0])
        self.assertEqual((u, length), ((1.0, 0.0), 100.0))
        self.assertEqual((n[0] + 0.0, n[1]), (0.0, 1.0))
        self.assertEqual(gespots.yaw_of(0.0, 1.0), 16384)
        self.assertEqual(gespots.yaw_of(0.0, -1.0), 49152)
        self.assertEqual(gespots.yaw_of(-1.0, 0.0), 32768)

    def test_corners(self):
        import gespots
        corners = gespots.shared_corners(self.BOX)
        self.assertEqual(sorted(corners[(0, 1)]), [(1, 0)])
        self.assertEqual(sorted(corners[(2, 0)]), [(1, 1), (4, 0)])
        # Ledge 0 has the box on +Y, so its floor side is -Y; ledge 1 leaves the shared corner toward +Y, back
        # over the box: seen from the floor side the wall under ledge 0 ends there (an outside corner).
        v = gespots.corner_kind(self.BOX[0], 1, self.BOX[1], 0)
        self.assertLess(v[0] * 0.0 + v[1] * -1.0, 0)
        self.assertEqual(gespots.corner_kind(self.BOX[2], 0, self.BOX[4], 0), 'straight')

    def test_line_point_and_clusters(self):
        import gespots
        self.assertEqual(gespots.line_point([0.0, 0.0, 100.0], [100.0, 0.0, 0.0], 25.0), [25.0, 0.0, 75.0])
        cells = [(0, 0, 0.0), (1, 0, 0.0), (1, 1, 2.0), (5, 5, 0.0), (5, 6, 40.0)]
        groups = gespots.clusters(cells, 32)
        self.assertEqual(groups[0], [(0, 0, 0.0), (1, 0, 0.0), (1, 1, 2.0)])
        self.assertEqual(sorted(len(g) for g in groups), [1, 1, 3])

    def test_jump_grab_selection(self):
        import gespots
        low = {'ge': 0, 'sign': -1, 'top': False, 'length': 100.0, 'normal': [0.0, -1.0], 'drop': 264.0,
               'floorZ': -254.0, 'flat': True, 'active': True, 'corners': [],
               'wall': {'hit': True, 'fromLine': 36.0, 'headroom': 164.0, 'contact': [50.0, -36.0, -164.5]}}
        top = {'ge': 0, 'sign': 1, 'top': True, 'room': {'stand': True, 'crouch': True}}
        grabs = gespots.jump_grabs([low, top])
        self.assertEqual(len(grabs), 1)
        self.assertEqual(grabs[0]['facingYaw'], 16384)
        self.assertEqual(grabs[0]['start100'], [50.0, -136.0, -254.0])
        self.assertTrue(grabs[0]['pullUp']['stand'])
        self.assertEqual(gespots.jump_grabs([dict(low, drop=400.0), top]), [])


class MovesetSpotTests(unittest.TestCase):
    """movespots.py parts that need no editor: steps and stairs, slopes, barrier and wall jump selection."""

    GRID = (0.0, 0.0, 32.0, 20, 20)

    def test_steps_and_stairs(self):
        import movespots
        # Three treads rising 16 each along +X from x 2, then a flat top; a 40 block at y 10.
        fmap, region = {}, set()
        for i in range(12):
            for j in range(8):
                z = 16.0 * max(0, min(3, i - 1))
                fmap[(i, j)] = [(z, 1.0)]
                region.add((i, j, z))
        fmap[(5, 10)] = [(40.0, 1.0)]
        fmap[(4, 10)] = [(0.0, 1.0)]
        region |= {(5, 10, 40.0), (4, 10, 0.0)}
        steps, stairs = movespots.step_edges(fmap, self.GRID, region)
        rises = {(g['rise'], g['upYaw']) for g in steps}
        self.assertIn((16.0, 0), rises)
        self.assertIn((40.0, 0), rises)
        self.assertTrue(all(g['topWalkable'] for g in steps if g['rise'] == 16.0))
        self.assertEqual(stairs[0]['rises'], [16.0, 16.0, 16.0])
        self.assertEqual(stairs[0]['upYaw'], 0)
        self.assertEqual(stairs[0]['topZ'], 48.0)

    def test_slope_patch_gradient(self):
        import movespots
        nz = 0.8922
        fmap, region = {}, set()
        for i in range(10):
            for j in range(4):
                z = 16.2 * i
                fmap[(i, j)] = [(z, nz)]
                region.add((i, j, z))
        out = movespots.slope_patches(fmap, self.GRID, region)
        self.assertEqual(len(out), 1)
        self.assertEqual(out[0]['upYaw'], 0)
        self.assertAlmostEqual(out[0]['grade'], 16.2 / 32.0, places=4)
        self.assertAlmostEqual(out[0]['degrees'], 26.85, delta=0.06)
        self.assertEqual(out[0]['low'][2], 0.0)

    def test_wall_jump_window_and_kick(self):
        import movespots
        self.assertEqual(movespots.WALLJUMP_WINDOW, (130.0, 335.0))
        # The kick a ledge drop gives (s_FirstWallJump.AnimEnd, before the clip moves the pawn).
        for drop, kick in ((409.3, 'up'), (264.7, 'up'), (250.0, 'upSmall'), (230.0, 'waist')):
            d = drop - movespots.SPY_LOC_OVER_FLOOR
            self.assertEqual('up' if d > 175 else ('upSmall' if d > 150 else 'waist'), kick)


class MovesetScenarioTests(unittest.TestCase):
    def test_tracked_moveset_scenarios(self):
        files = sorted(glob.glob(os.path.join(REPO, 'tests', 'scenarios', 'moveset', '*.scenario.json')))
        self.assertGreaterEqual(len(files), 19)
        with open(os.path.join(REPO, 're', 'inventory', 'inventory.json'), encoding='utf-8') as f:
            units = {u['id'] for u in json.load(f)['units']}
        ids = set()
        for path in files:
            doc = scn.load(path)
            self.assertTrue(doc['id'].startswith('moveset.'), path)
            self.assertNotIn(doc['id'], ids)
            ids.add(doc['id'])
            self.assertEqual(doc['setup'][0]['op'], 'leaveGE', path)
            self.assertEqual(doc['repeats'], 3, path)
            missing = [c for c in doc['covers'] if c not in units]
            self.assertEqual(missing, [], path)


if __name__ == '__main__':
    unittest.main()
