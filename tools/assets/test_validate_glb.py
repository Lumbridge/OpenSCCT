import json
from pathlib import Path
import tempfile
import unittest

from validate_glb import validate, read_glb, write_glb


class ValidationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name) / 'test.glb'
        self.document = {
            'asset': {'version': '2.0'}, 'nodes': [{'name': 'hips'}],
            'skins': [{'joints': [0]}],
            'accessors': [{'count': 6}, {'count': 2, 'min': [0], 'max': [1]}],
            'meshes': [{'primitives': [{'indices': 0}]}],
            'animations': [{'name': 'walk', 'samplers': [{'input': 1}],
                            'channels': [{'target': {'node': 0}}]}],
        }

    def run_check(self, contract=None):
        write_glb(self.path, self.document, [])
        return validate(self.path, contract)

    def test_missing_bone_and_clip_rejected(self):
        report = self.run_check({'required_bones': ['head'], 'required_clips': {'idle': None}})
        self.assertFalse(report['passed'])
        self.assertEqual(len(report['errors']), 2)

    def test_duration_and_budget_rejected(self):
        report = self.run_check({'max_triangles': 1, 'required_clips': {'walk': 2}})
        self.assertEqual(len(report['errors']), 2)

    def test_missing_texture_rejected(self):
        self.document['images'] = [{'uri': 'textures/missing.png'}]
        self.assertFalse(self.run_check()['passed'])

    def test_reference_contract_passes(self):
        self.assertTrue(self.run_check({'required_bones': ['hips'], 'required_clips': {'walk': 1}})['passed'])

    def test_truncated_glb_rejected(self):
        self.run_check()
        self.path.write_bytes(self.path.read_bytes()[:-4])
        with self.assertRaises(ValueError):
            read_glb(self.path)


if __name__ == '__main__':
    unittest.main()
