"""Tests of the combat recorder's pure parts on synthetic data (no game): the scenario format, the spec formulas the
measurements compare with, and the event extraction.

    python -I -B -m pytest re/tools/difftest/combat_test.py -q
"""
from __future__ import annotations

import copy
import gzip
import json
import math
import os
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import combat_measure as cm  # noqa: E402
import combat_scenario as cs  # noqa: E402
import combat_suite  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))

DOC = {
    'format': 'svm.combat/1', 'id': 'combat.test-case', 'title': 'a test', 'map': 'SteeE', 'mode': 'playlevel',
    'local': {'team': 'merc', 'floor': [0.0, 0.0, 0.0], 'yaw': 16384},
    'target': {'team': 'spy', 'floor': [0.0, 300.0, 0.0], 'yaw': 49152, 'crouched': False},
    'settleFrames': 10, 'durationFrames': 100,
    'events': [{'frame': 0, 'do': 'rof', 'mode': 'single'},
               {'frame': 2, 'do': 'aim', 'bone': 'spine2', 'to': 'pelvis'},
               {'frame': 4, 'do': 'fire'}, {'frame': 5, 'do': 'fireUp'},
               {'frame': 5, 'do': 'buttons', 'buttons': {'BT_Up': 1}},
               {'frame': 50, 'do': 'buttons', 'buttons': {'BT_Up': 0}}],
    'repeats': 1, 'measure': {'hits': {}}, 'covers': ['uc:SBase.SWeaponDefense:TraceFire'],
}


class ScenarioFormat(unittest.TestCase):
    def test_valid(self):
        self.assertEqual(cs.validate(copy.deepcopy(DOC)), [])

    def test_one_rig_command_per_frame(self):
        doc = copy.deepcopy(DOC)
        doc['events'].insert(3, {'frame': 4, 'do': 'heal', 'hp': 100})
        self.assertTrue(any('one rig command per frame' in p for p in cs.validate(doc)))

    def test_target_events_need_a_target(self):
        doc = copy.deepcopy(DOC)
        doc['target'] = None
        doc['events'].append({'frame': 60, 'do': 'heal', 'hp': 100})
        self.assertTrue(any('needs a target' in p for p in cs.validate(doc)))
        doc['events'].insert(0, {'frame': 0, 'do': 'note', 'note': 'x'})
        doc['events'] = [e for e in doc['events'] if e['do'] != 'rof']
        doc['events'].insert(1, {'frame': 0, 'do': 'spawnTarget', 'team': 'spy', 'floor': [0, 1, 2], 'yaw': 0})
        self.assertFalse(any('needs a target' in p for p in cs.validate(doc)))

    def test_bad_fields(self):
        doc = copy.deepcopy(DOC)
        doc['events'][1]['bone'] = 'tail'
        doc['events'].append({'frame': 70, 'do': 'grenade', 'kind': 'frag', 'at': [0, 0, 0], 'fromLocal': [0, 0, 0]})
        doc['events'].append({'frame': 80, 'do': 'buttons', 'buttons': {'bJump': 1}})
        doc['events'].append({'frame': 200, 'do': 'fire'})
        problems = cs.validate(doc)
        for text in ('bone must be', 'needs one of at', 'buttons must map', 'outside 0..durationFrames'):
            self.assertTrue(any(text in p for p in problems), (text, problems))

    def test_held_inputs(self):
        held = cs.held_inputs(DOC)
        self.assertEqual(held[4], {})
        self.assertEqual(held[5], {'BT_Up': 1})
        self.assertEqual(held[49], {'BT_Up': 1})
        self.assertEqual(held[50], {'BT_Up': 0})

    def test_tracked_files(self):
        folder = os.path.join(REPO, 'tests', 'scenarios', 'combat')
        names = sorted(n for n in os.listdir(folder) if n.endswith('.combat.json'))
        self.assertGreaterEqual(len(names), 30)
        for name in names:
            sc = cs.load(os.path.join(folder, name))
            self.assertEqual(sc['id'], 'combat.' + name[:-len('.combat.json')])

    def test_generator_matches_the_files(self):
        self.assertEqual(combat_suite.write(check_only=True), [])


@unittest.skipIf(cm.np is None, 'numpy is needed for the float32 formulas')
class SpecFormulas(unittest.TestCase):
    def test_rifle_damage_table(self):
        # hit-zones-and-weapons.md rule 8: torso values and the zone scaling.
        for d, dmg in ((0, 25), (300, 25), (600, 21), (900, 18), (1199, 15), (1200, 14), (1500, 12), (1800, 9),
                       (5000, 9)):
            self.assertEqual(cm.rifle_damage(d), dmg, d)
        self.assertEqual([cm.rifle_damage(250, p) for p in (1, 2, 3, 5)], [75, 25, 10, 5])
        self.assertEqual([cm.rifle_damage(1500, p) for p in (1, 9, 4, 15)], [36, 12, 4, 2])

    def test_rifle_steps(self):
        # The first integer distance of each value (rule 8).
        firsts = {}
        for d in range(0, 1901):
            firsts.setdefault(cm.rifle_damage(d), d)
        self.assertEqual(firsts, {25: 0, 24: 301, 23: 391, 22: 481, 21: 571, 20: 661, 19: 751, 18: 840, 17: 930,
                                  16: 1020, 15: 1110, 14: 1200, 13: 1320, 12: 1440, 11: 1560, 10: 1680, 9: 1800})

    def test_accuracy(self):
        self.assertAlmostEqual(cm.accuracy(0, 1), 0.25)
        self.assertAlmostEqual(cm.accuracy(0, 3), 0.45)
        self.assertAlmostEqual(cm.accuracy(346.5, 1), 0.7)
        self.assertAlmostEqual(cm.accuracy(116.9, 1), 116.9 / 600 + 0.25)

    def test_frag(self):
        self.assertEqual(cm.frag_expect(100, 'spy'), ('SDamage_Explosion', 200))
        self.assertEqual(cm.frag_expect(100, 'spy', protected=True), ('SDamage_Explosion', 0))
        self.assertEqual(cm.frag_expect(450, 'spy'), ('SDTBase', 33))
        self.assertEqual(cm.frag_expect(600, 'spy'), (None, 0))


class Extraction(unittest.TestCase):
    @staticmethod
    def side(hp=100, cs_='s_PlayerWalkingSt2H', clip=25, hist=None, pad=0, mdr=(0, 16384, 0), bir=0, disp=0.6):
        return {'loc': [0.0, 0.0, 88.5], 'vel': [0.0, 0.0, 0.0], 'hp': hp, 'cs': cs_, 'ws': 's_Selected', 'ps': 'P',
                'pad': pad, 'sticky': 0, 'eye': 55.5, 'bob': [0.0, 0.0, 0.0], 'mdr': list(mdr),
                'hist': hist or [[0, 0, 0, 0]] * 10,
                'w': {'clip': clip, 'bir': bir, 'disp': disp, 'acc': [0, 16384, 0]}}

    def frames(self):
        h0 = [[0, 0, 0, 0]] * 10
        h1 = [[5, 25, 2, 12]] + h0[:9]
        fr = []
        fr.append({'f': 0, 't': 10.0, 'L': self.side(), 'T': self.side(hist=h0), 'imp': [], 'sent': [[1, 'fire']],
                   'gs': [0, 0, 0, [0, 0, 0]]})
        fr.append({'f': 1, 't': 10.0167, 'L': self.side(clip=24, bir=1, disp=0.25, mdr=(100, 16400, 0)),
                   'T': self.side(hist=h0), 'imp': [[0.0, 250.0, 120.0, 10.0167, 0, 0, 0]], 'sent': [],
                   'gs': [0, 0, 0, [0, 0, 0]]})
        fr.append({'f': 2, 't': 10.0333, 'L': self.side(clip=24, bir=1, disp=0.25, mdr=(100, 16400, 0)),
                   'T': self.side(hp=75, hist=h1, pad=1), 'imp': [], 'sent': [], 'gs': [0, 0, 0, [0, 0, 0]]})
        return fr

    def test_new_history(self):
        h0 = [[0, 0, 0, 0]] * 10
        h1 = [[5, 25, 2, 12]] + h0[:9]
        h2 = [[5, 10, 3, 13]] + h1[:9]
        self.assertEqual(cm.new_history(h0, h1), [[5, 25, 2, 12]])
        self.assertEqual(cm.new_history(h0, h2), [[5, 25, 2, 12], [5, 10, 3, 13]])
        self.assertEqual(cm.new_history(h2, h2), [])

    def test_events_and_hits(self):
        fr = self.frames()
        ev = cm.events(fr)
        kinds = [(e['kind'], e.get('who')) for e in ev]
        self.assertIn(('shot', 'L'), kinds)
        self.assertIn(('damage', 'T'), kinds)
        self.assertIn(('padFreeze', 'T'), kinds)
        hits = cm.measure_hits(fr, ev)
        self.assertEqual(hits['n'], 1)
        h = hits['hits'][0]
        self.assertEqual((h['damage'], h['pill'], h['zone']), (25, 2, 'torso L'))
        self.assertAlmostEqual(h['distance'], math.hypot(250.0, 120.0 - 144.0), places=2)
        if cm.np is not None:
            self.assertTrue(h['match'])
        shots = cm.measure_shots(fr, ev)
        self.assertEqual(shots['shots'][0]['kickPitch'], 100)
        self.assertEqual(shots['shots'][0]['kickYaw'], 16)
        self.assertAlmostEqual(shots['shots'][0]['expectDisp'], 0.25)

    def test_spread_offset(self):
        # An impact straight down the aim line sits at u = 0.5 on both axes; one A * 500 to the right at 15000
        # along the aim sits at u = 1.
        eye = [0.0, 0.0, 0.0]
        rot = [0, 0, 0]
        mid = cm.spread_offset(eye, rot, [1500.0, 0.0, 0.0], 0.25)
        self.assertAlmostEqual(mid['uY'], 0.5)
        self.assertAlmostEqual(mid['uZ'], 0.5)
        edge = cm.spread_offset(eye, rot, [1500.0, 12.5, 0.0], 0.25)
        self.assertAlmostEqual(edge['uY'], 1.0, places=3)

    def test_measure_folder(self):
        header = {'format': cm.TRACE_FORMAT, 'run': 1, 'scenario': {'doc': dict(DOC, measure={'hits': {}, 'shots': {},
                                                                                            'states': {}})}}
        with tempfile.TemporaryDirectory() as tmp:
            folder = os.path.join(tmp, 'combat.test-case')
            os.makedirs(folder)
            with gzip.open(os.path.join(folder, 'run1.jsonl.gz'), 'wt', encoding='utf-8') as f:
                f.write(json.dumps(header) + '\n')
                for fr in self.frames():
                    f.write(json.dumps(fr) + '\n')
                f.write(json.dumps({'end': True, 'frames': 3}) + '\n')
            r = cm.measure_folder(folder)
            self.assertEqual(r['runs'][0]['hits']['n'], 1)
            self.assertTrue(os.path.isfile(os.path.join(folder, 'measure.json')))
            path = cm.measure_all(tmp)
            with open(path, encoding='utf-8') as f:
                text = f.read()
            self.assertIn('combat.test-case', text)


if __name__ == '__main__':
    unittest.main()
