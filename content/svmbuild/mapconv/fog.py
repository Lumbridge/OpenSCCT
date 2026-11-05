"""Portal geometry for Enhanced's zone fog; source geometry remains local content."""
from __future__ import annotations

from itertools import combinations
import math
import numpy as np


def portals(model) -> list[dict]:
    result = []
    for index, node in enumerate(model.nodes):
        if not 3 <= node.numverts <= 64 or not all(node.izone) or node.izone[0] == node.izone[1]:
            continue
        if node.ivertpool < 0 or node.ivertpool + node.numverts > len(model.verts):
            continue
        indices = [int(model.verts[node.ivertpool + k, 0]) for k in range(node.numverts)]
        if any(i < 0 or i >= len(model.points) for i in indices):
            continue
        points = [model.points[i] for i in indices]
        result.append({"node": index, "plane": [float(x) for x in node.plane],
                       "back": int(node.izone[0]), "front": int(node.izone[1]),
                       "vertices": [[float(x) for x in point] for point in points]})
    return result


def source_portals(src) -> list[dict]:
    from scctpkg.bsp import read_model

    ref = src.header.world_model
    return portals(read_model(src.pkg, src.pkg.exports[ref - 1])) if ref else []


def convex_vertices(planes) -> np.ndarray:
    """B5 0x1017b000: every accepted plane triplet, including repeated corners."""
    planes = np.asarray(planes, dtype=np.float64)
    points = []
    for triple in combinations(range(len(planes)), 3):
        rows = planes[list(triple)]
        determinant = np.linalg.det(rows[:, :3])
        if abs(determinant) < 1e-6:
            continue
        point = np.linalg.solve(rows[:, :3], rows[:, 3])
        if np.all(planes[:, :3] @ point <= planes[:, 3] + .05):
            points.append(point)
    return np.asarray(points, dtype=np.float64).reshape(-1, 3)


def brush_pieces(model, matrix: np.ndarray, offset: np.ndarray) -> list[np.ndarray]:
    """World-space outward planes of the inside BSP leaves (native 0x10179ec0)."""
    if model.root_outside is None or not 1 <= len(model.nodes) <= 4096 or abs(np.linalg.det(matrix)) < 1e-9:
        raise ValueError("fog brush has no readable RootOutside/geometry transform")
    inverse = np.linalg.inv(matrix)
    planes = []
    for node in model.nodes:
        normal = inverse @ np.asarray(node.plane[:3])
        length = np.linalg.norm(normal)
        if length < 1e-9:
            raise ValueError("fog brush has a degenerate plane")
        planes.append(np.r_[normal, node.plane[3] + np.dot(normal, offset)] / length)
    result = []
    pending = [(0, model.root_outside, [])]
    visited = 0
    while pending and visited < 8192:
        index, outside, path = pending.pop()
        visited += 1
        node = model.nodes[index]
        structural = node.num_verts != 0 and not node.flags & 0x21
        for front in (False, True):
            child = node.front if front else node.back
            child_outside = outside or structural if front else outside and not structural
            child_path = path + [-planes[index] if front else planes[index]]
            if child < 0:
                if not child_outside:
                    result.append(np.asarray(child_path))
            elif child < len(model.nodes):
                pending.append((child, child_outside, child_path))
    if not result:
        raise ValueError("fog brush has no inside leaves")
    return result


def point_region(model, point) -> int:
    """The native fog query returns 255 for solid leaves, otherwise their zone number."""
    if model.root_outside is None or not model.nodes:
        return -1
    outside, index = model.root_outside, 0
    for _ in range(4096):
        node = model.nodes[index]
        front = np.dot(point, node.plane[:3]) >= node.plane[3]
        structural = node.num_verts != 0 and not node.flags & 0x21
        outside = outside or structural if front else outside and not structural
        child = node.front if front else node.back
        if child < 0:
            return (node.zone_front if front else node.zone_back) if outside else 255
        if child >= len(model.nodes):
            return -1
        index = child
    return -1


def inside_pieces(pieces, point) -> bool:
    return any(np.all(p[:, :3] @ point <= p[:, 3]) for p in pieces)


def brush_contains(model, inverse: np.ndarray, offset: np.ndarray, point) -> bool:
    if model.root_outside is None or not model.nodes:
        return False
    local = (np.asarray(point) - offset) @ inverse
    outside, index = model.root_outside, 0
    for _ in range(len(model.nodes) + 1):
        node = model.nodes[index]
        front = np.dot(local, node.plane[:3]) >= node.plane[3]
        structural = node.num_verts != 0 and not node.flags & 0x21
        outside = outside or structural if front else outside and not structural
        child = node.front if front else node.back
        if child < 0:
            return not outside
        if child >= len(model.nodes):
            return False
        index = child
    return False


def fallback_box(origin, inside) -> list[np.ndarray]:
    """B5 0x1017a910: locate an inside seed, flood 256-unit samples, bisect its six axial extents."""
    seed = None
    for shell in range(3):
        for n in range(125):
            cell = np.array([n % 5 - 2, n // 5 % 5 - 2, n // 25 - 2])
            if max(abs(cell)) != shell:
                continue
            point = np.asarray(origin) + cell * 128.0
            if inside(point):
                seed = point
                break
        if seed is not None:
            break
    if seed is None:
        return []
    cells = [(0, 0, 0)]
    visited = {(0, 0, 0)}
    low_cell, high_cell = np.zeros(3, dtype=int), np.zeros(3, dtype=int)
    cursor = 0
    while cursor < len(cells) and len(cells) <= 200000:
        cell = cells[cursor]
        cursor += 1
        for axis in range(3):
            for sign in (-1, 1):
                neighbor = list(cell)
                neighbor[axis] += sign
                key = tuple(neighbor)
                if abs(neighbor[axis]) >= 1024 or key in visited:
                    continue
                visited.add(key)
                if inside(seed + np.array(neighbor) * 256.0):
                    cells.append(key)
                    low_cell = np.minimum(low_cell, neighbor)
                    high_cell = np.maximum(high_cell, neighbor)
    planes = []
    for axis in range(3):
        for sign in (-1, 1):
            low, high = 0.0, ((-low_cell[axis] if sign < 0 else high_cell[axis]) + 1) * 256.0
            for _ in range(16):
                middle = (low + high) * .5
                point = seed.copy()
                point[axis] += sign * middle
                if inside(point):
                    low = middle
                else:
                    high = middle
            normal = np.zeros(3)
            normal[axis] = sign
            planes.append(np.r_[normal, sign * seed[axis] + low])
    return [np.asarray(planes)]


def fog_floor(pieces, vertices, world_zone) -> float | None:
    """Native 0x1017c0d0: lowest open sample over all convex pieces."""
    nonempty = [v for v in vertices if len(v)]
    if not nonempty:
        return None
    points = np.concatenate(nonempty)
    low, high = points.min(axis=0), points.max(axis=0)
    step = max(16.0, max(high[0] - low[0], high[1] - low[1]) / 48.0)
    found = math.inf
    x = low[0] + step * .5
    while x < high[0]:
        y = low[1] + step * .5
        while y < high[1]:
            z = low[2] + 1.0
            while z < min(high[2], found):
                point = np.array([x, y, z])
                if inside_pieces(pieces, point) and 0 <= world_zone(point) < 255:
                    found = z
                    break
                z += 8.0
            y += step
        x += step
    return None if not math.isfinite(found) else found


def volume_entries(pieces, color, distance: float, height: float, fade: float, zone: int, world_zone,
                   *, inside=None) -> list[dict]:
    """Native volume packing; invalid pieces reject the entire actor, matching 0x1017c4b0 rollback."""
    vertices = [convex_vertices(p) for p in pieces]
    floor = fog_floor(pieces, vertices, world_zone)
    if floor is None:
        return []
    inside = inside or (lambda p: inside_pieces(pieces, p))
    all_points = np.concatenate([v for v in vertices if len(v)])
    extinction = 3.0 / distance
    diagonal = float(np.linalg.norm(all_points.max(axis=0) - all_points.min(axis=0)))
    cap = floor + height * max(0, math.ceil(math.log2(diagonal * extinction * 1024))) if height > 0 and diagonal > 0 else math.inf
    result = []
    for planes, points in zip(pieces, vertices):
        if len(points) < 4:
            continue
        center = points.mean(axis=0)
        if not inside(center):
            raise ValueError("fog piece center is outside its actor")
        low, high = points.min(axis=0), points.max(axis=0)
        opened, closed = [], []
        for plane in planes:
            face = points[np.abs(points @ plane[:3] - plane[3]) < .1]
            if len(face) < 3:
                continue
            middle = face.mean(axis=0)
            samples = [middle + (vertex - middle) * .6 + plane[:3] * 4 for vertex in face]
            samples.append(middle + plane[:3] * 4)
            count = sum(not inside(p) and 0 <= world_zone(p) < 255 for p in samples)
            (opened if count * 2 > len(samples) else closed).append(plane.tolist())
        if len(opened) + len(closed) > 24:
            raise ValueError("fog piece has more than 24 faces")
        if low[2] >= cap:
            continue
        if cap < high[2]:
            clipped = convex_vertices(np.vstack([planes, [0, 0, 1, cap]]))
            if len(clipped) >= 4:
                low, high = clipped.min(axis=0), clipped.max(axis=0)
        result.append({"color": list(color), "extinction": extinction, "floor": floor,
                       "heightFalloff": height, "edgeFade": fade, "zone": zone,
                       "planes": opened + closed, "openPlanes": len(opened),
                       "min": low.tolist(), "max": high.tolist()})
    return result


def source_volumes(src) -> list[dict]:
    from ..sources import modeltail
    from ..sources.typedprops import Ref
    from .collision import rotation_matrix, _vec, _rot

    actors = [a for a in src.actors if a.cls.lower() == "sbase.sfogvolume" and a.category != "skipped"]
    if not actors:
        return []
    world_ref = src.header.world_model
    if not world_ref:
        return []
    world = modeltail.read_model(src.pkg, src.pkg.exports[world_ref - 1])
    world_zone = lambda point: point_region(world, point)
    result = []
    for actor in actors:
        distance = float(src.value(actor, "FogDistance", 1500.0))
        if distance <= 0:
            continue
        brush = src.value(actor, "Brush")
        if not isinstance(brush, Ref) or brush.ref <= 0 or not src.own(brush):
            src.notes.add("fog-volume", f"{actor.name}: no readable brush")
            continue
        try:
            model = modeltail.read_model(src.pkg, src.pkg.exports[brush.ref - 1])
            scale = np.array(_vec(src.value(actor, "DrawScale3D"), (1, 1, 1))) * float(src.value(actor, "DrawScale", 1))
            matrix = np.diag(scale) @ rotation_matrix(_rot(src.value(actor, "Rotation")))
            location = np.array(_vec(src.value(actor, "Location")))
            offset = location - np.array(_vec(src.value(actor, "PrePivot"))) @ matrix
            inverse = np.linalg.inv(matrix)
            inside = lambda point: brush_contains(model, inverse, offset, point)
            try:
                pieces = brush_pieces(model, matrix, offset)
            except ValueError:
                pieces = fallback_box(location, inside)
                if not pieces:
                    raise ValueError("fog volume has no inside near its origin")
                src.notes.add("fog-volume-box", f"{actor.name}: unreadable convex brush uses sampled box")
            zone = world_zone(location) if src.value(actor, "bLimitToZone", False) else -1
            if not 0 <= zone < 255:
                zone = -1
            color = src.value(actor, "FogColor", {"R": 128, "G": 128, "B": 128})
            rgb = [float(color.get(c, 128)) / 255 for c in ("R", "G", "B")]
            entries = volume_entries(pieces, rgb, distance, float(src.value(actor, "HeightFalloff", 0)),
                                     float(src.value(actor, "EdgeFade", 64)), zone, world_zone, inside=inside)
            if len(result) + len(entries) > 24:
                raise ValueError("level exceeds 24 fog volume pieces")
            result.extend(entries)
        except (ValueError, IndexError, np.linalg.LinAlgError) as error:
            src.notes.add("fog-volume", f"{actor.name}: {error}")
    return result
