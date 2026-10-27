"""Tests of the golden tools on synthetic pictures (no game, no Godot).

    python -I -B -m pytest tools/golden
"""
from __future__ import annotations

import json
import os
import sys
import tempfile
import unittest

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import score  # noqa: E402


def picture(seed, size=(96, 64), shift=0, gain=1.0):
    rng = np.random.default_rng(seed)
    base = rng.integers(0, 256, size=(size[1], size[0] + 8, 3)).astype(np.float64)
    base = (base[:, :-8] + base[:, 1:-7]) / 2.0          # some structure
    img = np.roll(base, shift, axis=1) * gain
    return Image.fromarray(np.clip(img, 0, 255).astype(np.uint8))


class ScoreTests(unittest.TestCase):
    def test_identical_pictures_score_one(self):
        with tempfile.TemporaryDirectory() as d:
            p = os.path.join(d, 'a.png')
            picture(1).save(p)
            la = score.luma(p)
            self.assertAlmostEqual(score.ssim(la, la), 1.0, places=6)

    def test_shift_and_gain_lower_the_score(self):
        with tempfile.TemporaryDirectory() as d:
            paths = {}
            for name, img in (('ref', picture(2)), ('shift', picture(2, shift=2)), ('dark', picture(2, gain=0.5))):
                paths[name] = os.path.join(d, name + '.png')
                img.save(paths[name])
            ref = score.luma(paths['ref'])
            self.assertLess(score.ssim(ref, score.luma(paths['shift'])), 0.9)
            self.assertLess(score.ssim(ref, score.luma(paths['dark'])), 0.95)

    def test_score_folder_reports_gates(self):
        with tempfile.TemporaryDirectory() as d:
            game, svm = os.path.join(d, 'game'), os.path.join(d, 'svm')
            os.makedirs(game)
            os.makedirs(svm)
            for i in range(3):
                picture(10 + i).save(os.path.join(game, 'p%d.png' % i))
                picture(10 + i).save(os.path.join(svm, 'p%d.png' % i))
            picture(20).save(os.path.join(game, 'only-game.png'))
            rows, summary = score.score(game, svm)
            self.assertEqual(len(rows), 3)
            self.assertEqual(summary['poses'], 3)
            self.assertAlmostEqual(summary['ssimMedian'], 1.0, places=4)
            self.assertTrue(all(summary['gates'].values()))

    def test_size_mismatch_is_reported(self):
        with tempfile.TemporaryDirectory() as d:
            game, svm = os.path.join(d, 'game'), os.path.join(d, 'svm')
            os.makedirs(game)
            os.makedirs(svm)
            picture(3).save(os.path.join(game, 'p.png'))
            picture(3, size=(48, 32)).save(os.path.join(svm, 'p.png'))
            rows, summary = score.score(game, svm)
            self.assertIn('error', rows[0])
            self.assertEqual(summary, {})


    def test_binning_hides_per_pixel_grain(self):
        rng = np.random.default_rng(5)
        base = np.kron(rng.integers(0, 256, size=(16, 24)).astype(np.float64), np.ones((4, 4)))
        grainy = base * rng.uniform(0.64, 1.36, size=base.shape)
        self.assertLess(score.ssim(base, grainy), 0.9)
        self.assertGreater(score.ssim(score.binned(base, 4), score.binned(grainy, 4)), score.ssim(base, grainy))
        self.assertEqual(score.binned(base, 4).shape, (16, 24))
        self.assertIs(score.binned(base, 1), base)


class CaptureToolTests(unittest.TestCase):
    def test_vision_modes_pick_their_team_and_pointers_parse(self):
        sys.path.insert(0, os.path.join(HERE, '..', '..', 're', 'tools', 'difftest'))
        try:
            import capture_game  # noqa: PLC0415
        except ImportError as error:  # the RE helpers need their own dependencies
            self.skipTest(str(error))
        self.assertEqual(capture_game.VISION_TEAM[36], 'spy')
        self.assertEqual(capture_game.VISION_TEAM[38], 'merc')
        self.assertEqual(capture_game.pointer(0x1234), 0x1234)
        self.assertEqual(capture_game.pointer('0x1a2b: SGoggle'), 0x1A2B)
        self.assertEqual(capture_game.pointer('None'), 0)


class PoseFileTests(unittest.TestCase):
    def test_pose_files_are_well_formed(self):
        folder = os.path.join(HERE, 'poses')
        for name in sorted(os.listdir(folder)):
            with open(os.path.join(folder, name), encoding='utf-8') as f:
                doc = json.load(f)
            self.assertEqual(doc['format'], 'svm.golden-poses/1', name)
            ids = [p['id'] for p in doc['poses']]
            self.assertEqual(len(ids), len(set(ids)), name)
            for p in doc['poses']:
                self.assertTrue(('pawn' in p) != ('floor' in p), p['id'])
                self.assertEqual(len(p.get('pawn') or p.get('floor')), 3, p['id'])
                self.assertIsInstance(p['yaw'], int, p['id'])


if __name__ == '__main__':
    unittest.main()
