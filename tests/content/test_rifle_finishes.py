from pathlib import Path

import numpy as np
import pytest
from PIL import Image

from svmbuild.fitted import SurfaceMesh
from svmbuild.rifle_finishes import FINISHES, PANELS, panel_colour, projection
from svmbuild.rifles import SOURCE
from svmbuild.sidearm_models import read


def test_finishes_are_limited_to_the_four_approved_models():
    assert set(PANELS) == {'m4a1s', 'ak47', 'galil', 'famas'}
    assert len(set(FINISHES)) == len(FINISHES) == 16


@pytest.mark.parametrize('rifle', PANELS)
def test_fitted_panels_are_shared_by_first_and_third_person_and_preserve_other_materials(rifle):
    mesh = SurfaceMesh(SOURCE / (rifle + '.glb'))
    first, _ = read(SOURCE / (rifle + '_first.glb'))
    first_albedos = set()
    for mat in first['materials']:
        colour = mat.get('pbrMetallicRoughness', {}).get('baseColorTexture')
        if colour:
            texture = first['textures'][colour['index']]
            first_albedos.add(Path(first['images'][texture['source']]['uri']).stem)
    assert PANELS[rifle].keys() <= mesh.tris.keys() & first_albedos
    for stem, bounds in PANELS[rifle].items():
        with Image.open(SOURCE / 'textures' / (stem + '.png')) as image:
            base = np.asarray(image.convert('RGBA').resize((128, 128), Image.Resampling.LANCZOS))
        mask, u, v = projection(mesh, stem, bounds, 128, 128)
        red, coverage = panel_colour(base, mask, u, v, 'red_tiger')
        blue, _ = panel_colour(base, mask, u, v, 'blue_tiger')
        assert np.count_nonzero(coverage) > 30, stem
        assert np.count_nonzero(coverage == 0) > base.shape[0] * base.shape[1] // 10, stem
        assert np.any(red[coverage > 0] != blue[coverage > 0]), stem
        np.testing.assert_array_equal(red[coverage == 0], base[coverage == 0])
        np.testing.assert_array_equal(red[..., 3], base[..., 3])
        red_channel, green, blue_channel = base[..., :3].astype(float).transpose(2, 0, 1)
        wood_or_brass = (red_channel > green * 1.13 + 2) & (green > blue_channel * 1.07 + 1)
        assert not coverage[wood_or_brass].any(), stem
