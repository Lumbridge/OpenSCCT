"""Synthetic fog portal export preserves all nonzero zone boundaries, regardless of surface flags."""
from types import SimpleNamespace
import numpy as np
from svmbuild.sources.env import import_formats


def test_portal_geometry_keeps_zone_orientation():
    import_formats()
    from scctpkg.bsp import PF_PORTAL
    from svmbuild.mapconv.fog import portals

    node = SimpleNamespace(numverts=4, izone=(2, 7), isurf=0, ivertpool=0, plane=(1, 0, 0, 20))
    model = SimpleNamespace(nodes=[node], surfs=[SimpleNamespace(polyflags=PF_PORTAL)],
        points=np.array([[20, -10, -10], [20, 10, -10], [20, 10, 10], [20, -10, 10]]),
        verts=np.array([[0, 0], [1, 0], [2, 0], [3, 0]]))
    result = portals(model)
    assert len(result) == 1
    assert result[0]["plane"] == [1, 0, 0, 20]
    assert (result[0]["back"], result[0]["front"]) == (2, 7)
    assert result[0]["vertices"] == model.points.tolist()
    model.surfs[0].polyflags = 0
    assert portals(model) == result
    model.surfs[0].polyflags = PF_PORTAL
    node.izone = (2, 2)
    assert portals(model) == []
    node.izone = (0, 2)
    assert portals(model) == []
