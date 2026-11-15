"""Individual AK and Galil contacts. M4 and FAMAS authored handling is left intact."""
import math
import numpy as np
from .pistol_arms import tracks, pose, turn, curve, decompose
from .sidearm_models import world_matrices
from .reload_motion import solve_arm, smooth
from .reload_choreography import mix

FINGERS = ((11, 12, 13, 14), (15, 16, 17, 18), (19, 20, 21, 22), (24, 25, 26, 27), (28, 29, 30, 31))


def _close(hand, chain, target):
    # Small joint rotations retain segment lengths and avoid snapping the glove into a fist.
    for _ in range(24):
        if np.linalg.norm(hand[chain[-1]][:3, 3]-target) < .0008: break
        for offset in (2, 1, 0):
            joint = chain[offset]; origin = hand[joint][:3, 3]
            start = hand[chain[-1]][:3, 3]-origin; end = target-origin
            cosine = np.clip(start @ end / max(1e-12, np.linalg.norm(start)*np.linalg.norm(end)), -1, 1)
            angle = min(.10, math.acos(cosine))
            axis = np.cross(start, end)
            if np.linalg.norm(axis) < 1e-9: continue
            axis /= np.linalg.norm(axis)
            x, y, z = axis; k = np.array([[0, -z, y], [z, 0, -x], [-y, x, 0]])
            rotation = np.eye(3)+math.sin(angle)*k+(1-math.cos(angle))*(k @ k)
            for node in chain[offset:]:
                hand[node][:3, :3] = rotation @ hand[node][:3, :3]
                hand[node][:3, 3] = origin+rotation @ (hand[node][:3, 3]-origin)


def _hand_frame(held):
    middle = np.mean([held[n][:3, 3] for n in (15, 19, 24, 28)], axis=0)
    row = held[15][:3, 3]-held[28][:3, 3]; row /= np.linalg.norm(row)
    across = middle-held[10][:3, 3]; across -= row*(across @ row); across /= np.linalg.norm(across)
    return middle, np.column_stack([row, across, np.cross(row, across)])


def _support(held, profile):
    hand = [m.copy() for m in held]
    # AK: palm under the wooden fore-end; Galil: a more rearward grip below the deeper handguard.
    angle, shift, cy, cz, ry, rz = {
        'ak47': (-.30, [.065, 0, -.025], -.145, .138, .037, .030),
        'galil': (-.18, [-.020, -.012, -.018], -.157, .145, .043, .034),
    }[profile]
    c, s = math.cos(angle), math.sin(angle)
    rotation = np.array([[1, 0, 0], [0, c, -s], [0, s, c]])
    wrist = held[10][:3, 3]
    delta = np.eye(4); delta[:3, :3] = rotation
    delta[:3, 3] = wrist+shift-rotation @ wrist
    for n in range(10, 32): hand[n] = delta @ held[n]
    for chain in FINGERS[1:]:
        tip = hand[chain[-1]][:3, 3].copy()
        tip[1] = np.clip(tip[1], cy-ry*.75, cy+ry*.65)
        tip[2] = cz+rz*math.sqrt(max(0, 1-((tip[1]-cy)/ry)**2))
        _close(hand, chain, tip)
    thumb = hand[14][:3, 3].copy()
    thumb[1] = -.135 if profile == 'ak47' else -.143
    thumb[2] = .109 if profile == 'ak47' else .113
    _close(hand, FINGERS[0], thumb)
    return hand


def _mag_grasp(held, centre, half_width, profile):
    middle, basis = _hand_frame(held)
    target_basis = np.column_stack([[0., 1, 0], [1., 0, 0], [0., 0, -1]])
    rotation = target_basis @ basis.T
    grasp = np.eye(4); grasp[:3, :3] = rotation
    offset = [.010, -.027, -.029] if profile == 'ak47' else [.006, -.019, -.027]
    grasp[:3, 3] = centre+offset-rotation @ middle
    hand = [m.copy() for m in held]
    for n in range(10, 32): hand[n] = grasp @ held[n]
    for chain in FINGERS[1:]:
        tip = hand[chain[-1]][:3, 3].copy()
        tip[0] = centre[0]+half_width-.004
        tip[2] = centre[2]+.016
        _close(hand, chain, tip)
    thumb = hand[14][:3, 3].copy()
    thumb[0] = centre[0]-.010; thumb[2] = centre[2]-.018
    _close(hand, FINGERS[0], thumb)
    return hand


def _blend_hand(a, b, amount, parents):
    result = {10: mix(a[10], b[10], amount)}
    for n in range(11, 32):
        parent = parents[n]
        result[n] = result[parent] @ mix(np.linalg.inv(a[parent]) @ a[n], np.linalg.inv(b[parent]) @ b[n], amount)
    return result


def _place_hand(reference, hand, pole):
    wrist = hand[10]
    result = solve_arm(reference, (7, 8, 10), wrist[:3, 3], wrist[:3, :3],
                       pole_offset=np.array([0, pole, 0]), twist_node=9)
    correction = result[10] @ np.linalg.inv(wrist)
    result.update({n: correction @ hand[n] for n in range(11, 32)})
    return result


def author(doc, data, profile, part_points):
    if profile not in ('ak47', 'galil'): return
    _, parents = world_matrices(doc)
    held = pose(doc, tracks(doc, data, next(a for a in doc['animations'] if a['name'] == 'FirstWaitStFd2')), 0)
    support = _support(held, profile)
    points = part_points(doc, data, held, 87)
    centre = (points.min(0)+points.max(0))*.5
    grasp = _mag_grasp(held, centre, (points.max(0)[0]-points.min(0)[0])*.5, profile)
    for animation in doc['animations']:
        reload = animation['name'] == 'FirstReloStFd2'
        # AK reloads are authored independently in ak_reload.py. These contact
        # corrections apply only to its held poses and the Galil's existing clip.
        if reload and profile == 'ak47': continue
        original = tracks(doc, data, animation)
        duration = max(float(t[-1]) for _, _, t, _ in original)
        nodes = set(range(7, 32)); frames = {n: [] for n in nodes}
        times = np.linspace(0, duration, max(2, math.ceil(duration*30)+1))
        for time in times:
            f = time/duration; old = pose(doc, original, time)
            gun_follow = old[86] @ np.linalg.inv(held[86])
            base_hand = [gun_follow @ m for m in support]
            if reload:
                follow = old[87] @ np.linalg.inv(held[87])
                contact_hand = [follow @ m for m in grasp]
                blend = smooth([0, .09, .22, .69, .87, 1], np.array([0, 0, 1, 1, 0, 0]), f)
                hand = _blend_hand(base_hand, contact_hand, blend, parents)
            else:
                hand = {n: base_hand[n] for n in range(10, 32)}
            desired = _place_hand(old, hand, -.055 if profile == 'ak47' else -.025)
            for n in nodes:
                parent = parents.get(n)
                transform = desired.get(n, old[n])
                parent_transform = desired.get(parent, old[parent]) if parent is not None else np.eye(4)
                frames[n].append(np.linalg.inv(parent_transform) @ transform)
        animation['channels'] = [c for c in animation['channels'] if c['target']['node'] not in nodes]
        for n, poses in frames.items():
            for i, channel in enumerate(('translation', 'rotation', 'scale')):
                curve(doc, data, animation, n, channel, times, [decompose(m)[i] for m in poses])
