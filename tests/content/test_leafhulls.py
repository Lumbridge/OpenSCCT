"""UModel LeafHulls: the collision bound of a BSP node decoded to a convex region (synthetic models only)."""

from __future__ import annotations

import numpy as np
from svmbuild.sources import modeltail as M


def _node(plane, back=-1, cbound=-1) -> M.ModelNode:
    return M.ModelNode(plane=plane, zone_mask=0, flags=0, vert_pool=0, surf=-1, back=back, front=-1, coplanar=-1,
                       collision_bound=cbound, render_bound=-1, zone_back=0, zone_front=0, num_verts=0, leaf=(0, 0))


def _box_ints(lo, hi) -> list:
    return list(np.array([*lo, *hi], np.float32).view(np.int32))


def _model(leaf_hulls: list) -> M.Model:
    # A unit cube's floor (z >= 0 is solid behind the plane facing down) and the four walls around it.
    nodes = [
        _node((0.0, 0.0, -1.0, 0.0), cbound=0),
        _node((1.0, 0.0, 0.0, 1.0)),
        _node((1.0, 0.0, 0.0, 0.0)),
        _node((0.0, 1.0, 0.0, 1.0)),
        _node((0.0, -1.0, 0.0, 0.0)),
    ]
    return M.Model("Model0", np.zeros((0, 3), np.float32), np.zeros((0, 3), np.float32), nodes, [],
                   np.zeros((0, 2), np.int64), leaf_hulls=np.array(leaf_hulls, np.int32))


def test_a_hull_lists_its_node_planes_oriented_out_of_the_region():
    run = [0, 1, 2 | M.LEAF_HULL_FLIPPED, 3, 4, -1, *_box_ints((0, 0, 0), (1, 1, 262144))]
    hull = M.leaf_hull(_model(run + [7, 7]), 0)
    assert hull is not None
    assert hull.nodes == [0, 1, 2, 3, 4]
    assert hull.flipped == [False, False, True, False, False]
    assert hull.planes[2] == (-1.0, -0.0, -0.0, -0.0)
    assert hull.box_min == (0.0, 0.0, 0.0) and hull.box_max == (1.0, 1.0, 262144.0)
    centre = np.array([0.5, 0.5, 0.5])
    assert all(np.dot(p[:3], centre) - p[3] < 0 for p in hull.planes)


def test_runs_follow_each_other():
    first = [0, 1, -1, *_box_ints((0, 0, 0), (1, 1, 1))]
    second = [3, 4 | M.LEAF_HULL_FLIPPED, -1, *_box_ints((-1, -1, -1), (2, 2, 2))]
    model = _model(first + second)
    assert M.leaf_hull(model, 0).nodes == [0, 1]
    hull = M.leaf_hull(model, len(first))
    assert hull.nodes == [3, 4] and hull.flipped == [False, True] and hull.box_min == (-1.0, -1.0, -1.0)


def test_runs_that_do_not_fit_the_layout_are_refused():
    box = _box_ints((0, 0, 0), (1, 1, 1))
    assert M.leaf_hull(_model([0, 1, -1, *box]), 99) is None             # start outside the array
    assert M.leaf_hull(_model([0, 1, -1, *box[:5]]), 0) is None          # box cut short
    assert M.leaf_hull(_model([0, 9, -1, *box]), 0) is None              # no such node
    assert M.leaf_hull(_model([0, -5, -1, *box]), 0) is None             # a negative entry other than the end
    assert M.leaf_hull(_model([-1, *box]), 0) is None                    # no planes
    assert M.leaf_hull(_model([0, -1, *_box_ints((1, 0, 0), (0, 1, 1))]), 0) is None  # inverted box
    model = _model([0, -1, *box])
    model.leaf_hulls = None
    assert M.leaf_hull(model, 0) is None
