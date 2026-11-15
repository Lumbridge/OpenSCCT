"""AK-specific magazine exchange and right-hand charging-handle animation.

Authored from the held pose and the actual magazine/handle geometry. No source
reload keys are sampled. The two magazines have separate trajectories; hands
follow their contact frames, with fixed-length arms and distributed forearm roll.
"""
import math
import numpy as np

from .sidearm_models import world_matrices
from .pistol_arms import tracks, pose, curve, decompose
from .reload_motion import solve_arm, smooth
from .reload_choreography import rotation, path, mix

LEFT = ((11, 12, 13, 14), (15, 16, 17, 18), (19, 20, 21, 22), (24, 25, 26, 27), (28, 29, 30, 31))
RIGHT = ((36, 37, 38, 39), (40, 41, 42, 43), (44, 45, 46, 47), (49, 50, 51, 52), (53, 54, 55, 56))
VERSION = 1
EVENTS = {'Release magazine': .11, 'Old magazine clear': .29, 'Retrieve fresh magazine': .40,
          'Front lug engaged': .54, 'Magazine locked': .60, 'Support hand restored': .70,
          'Pull charging handle': .80, 'Release charging handle': .85, 'Ready': 1.0}


def delta(pivot, roll=0., pitch=0., yaw=0., move=(0, 0, 0)):
    r = rotation([0, 1, 0], yaw) @ rotation([0, 0, 1], pitch) @ rotation([1, 0, 0], roll)
    m = np.eye(4); m[:3, :3] = r
    m[:3, 3] = pivot - r @ pivot + np.asarray(move)
    return m


def close_finger(hand, chain, target):
    for _ in range(20):
        if np.linalg.norm(hand[chain[-1]][:3, 3] - target) < .001: break
        for start in (2, 1, 0):
            n = chain[start]; origin = hand[n][:3, 3]
            a, b = hand[chain[-1]][:3, 3] - origin, target - origin
            axis = np.cross(a, b)
            if np.linalg.norm(axis) < 1e-9: continue
            angle = min(.06, math.acos(np.clip(a @ b / max(1e-9, np.linalg.norm(a)*np.linalg.norm(b)), -1, 1)))
            r = rotation(axis, angle)
            for child in chain[start:]:
                hand[child][:3, :3] = r @ hand[child][:3, :3]
                hand[child][:3, 3] = origin + r @ (hand[child][:3, 3] - origin)


def magazine_hand(held, centre):
    hand = [m.copy() for m in held]
    middle = np.mean([held[n][:3, 3] for n in (15, 19, 24, 28)], axis=0)
    row = held[15][:3, 3] - held[28][:3, 3]; row /= np.linalg.norm(row)
    across = middle-held[10][:3, 3]; across -= row*(across @ row); across /= np.linalg.norm(across)
    source = np.column_stack([row, across, np.cross(row, across)])
    up = np.array([.24, .9707729, 0]); forward = np.array([.9707729, -.24, 0])
    target = np.column_stack([up, forward, np.cross(up, forward)])
    r = target @ source.T
    placement = np.eye(4); placement[:3, :3] = r
    placement[:3, 3] = centre + [.004, -.002, -.033] - r @ middle
    for n in range(10, 32): hand[n] = placement @ held[n]
    for chain, height in zip(LEFT[1:], [.034, .010, -.014, -.034]):
        close_finger(hand, chain, centre + np.array([.012 + height*.24, height, .016]))
    close_finger(hand, LEFT[0], centre + np.array([-.018, .043, -.015]))
    return hand


def handle_hand(held, contact):
    hand = [m.copy() for m in held]
    # The right palm approaches from the exposed side of the receiver. The
    # index/middle fingers hook the handle; the thumb opposes without crossing it.
    shift = contact - (held[43][:3, 3]*.45 + held[47][:3, 3]*.55)
    for n in range(35, 57): hand[n][:3, 3] += shift
    for chain, x in zip(RIGHT[1:], [.013, -.005, -.022, -.037]):
        close_finger(hand, chain, contact + np.array([x, -.002, .001]))
    close_finger(hand, RIGHT[0], contact + np.array([.025, -.015, .015]))
    return hand


def blend_hand(a, b, amount, first, last, parents):
    out = {first: mix(a[first], b[first], amount)}
    for n in range(first+1, last):
        p = parents[n]
        out[n] = out[p] @ mix(np.linalg.inv(a[p]) @ a[n], np.linalg.inv(b[p]) @ b[n], amount)
    return out


def arm(held, hand, right=False):
    upper, elbow, twist, wrist, end = (32, 33, 34, 35, 57) if right else (7, 8, 9, 10, 32)
    solved = solve_arm(held, (upper, elbow, wrist), hand[wrist][:3, 3], hand[wrist][:3, :3],
                       pole_offset=np.array([0, -.025, .035 if right else -.035]) * min(1., np.linalg.norm(hand[wrist][:3, 3]-held[wrist][:3, 3])/.15), twist_node=twist)
    correction = solved[wrist] @ np.linalg.inv(hand[wrist])
    solved.update({n: correction @ hand[n] for n in range(wrist+1, end)})
    return solved


def author(doc, data, part_points):
    if doc.get('extras', {}).get('ak_reload') == VERSION: return
    held = pose(doc, tracks(doc, data, next(a for a in doc['animations'] if a['name'] == 'FirstWaitStFd2')), 0)
    _, parents = world_matrices(doc)
    mag = part_points(doc, data, held, 87)
    centre = (mag.min(0) + mag.max(0))*.5
    handle = part_points(doc, data, held, 89)
    contact = handle[handle[:, 2] > handle[:, 2].max()-.004].mean(0)
    grasp = magazine_hand(held, centre)
    hook = handle_hand(held, contact)
    pivot = np.array([.415, -.166, centre[2]])
    def mag_pose(rock=0., move=(0, 0, 0)):
        return delta(pivot, yaw=0., pitch=rock, move=move)
    old_keys = [0, .11, .20, .29, .37, .44, 1]
    old_poses = [mag_pose(), mag_pose(), mag_pose(.32), mag_pose(.36, [-.045, -.135, -.012]),
                 mag_pose(.52, [-.12, -.37, -.05]), mag_pose(.70, [-.19, -.65, -.08]), mag_pose(.70, [-.19, -.65, -.08])]
    fresh_keys = [0, .39, .44, .51, .54, .60, 1]
    fresh_poses = [mag_pose(.32, [-.09, -.52, -.065]), mag_pose(.32, [-.09, -.52, -.065]),
                   mag_pose(.32, [-.07, -.32, -.045]), mag_pose(.32, [-.023, -.065, -.006]),
                   mag_pose(.32), mag_pose(), mag_pose()]
    duration = 2.8
    times = np.linspace(0, duration, 169)
    animated = set(doc['skins'][0]['joints'])
    frames = {n: [] for n in animated}
    scales = {87: [], 88: []}
    reach_errors = []
    for time in times:
        f = time/duration
        weight = smooth([0, .08, .19, .60, .72, .86, .98, 1], np.array([0, .65, 1, 1, .85, .60, 0, 0]), f)
        draw = smooth([0, .19, .65, .73, .88, .98, 1], np.array([0, .16, .16, .055, .055, 0, 0]), f)
        gun = delta(held[35][:3, 3], roll=.80*weight, pitch=.10*weight, yaw=.20*weight,
                    move=[draw, .10*weight, .035*weight])
        # Present the arms and weapon together farther from the camera. Moving
        # only the receiver makes the support targets exceed the arm's reach.
        base = [m.copy() for m in held]
        for m in base: m[0, 3] += draw
        desired = {n: base[n].copy() for n in animated}
        for n in animated:
            if n >= 86: desired[n] = gun @ held[n]
        old = path(old_keys, old_poses, f)
        fresh = path(fresh_keys, fresh_poses, f)
        # New magazine uses its own bind frame, aligned to the occupied magwell.
        desired[87] = gun @ (np.eye(4) if f >= .95 else old) @ held[87]
        desired[88] = gun @ fresh @ held[87]
        scales[87].append(1. if f < .39 or f >= .96 else .00001)
        scales[88].append(1. if .395 <= f < .96 else .00001)
        support = [gun @ m for m in held]
        # Brace the rear of the wooden fore-end while the firing hand operates
        # the handle, keeping the support elbow comfortably bent.
        for n in range(10, 32): support[n][:3, 3] -= gun[:3, 0]*.055*weight
        magazine = old if f < .395 else fresh
        contact_hand = [gun @ magazine @ m for m in grasp]
        grab = smooth([0, .025, .105, .605, .70, 1], np.array([0, 0, 1, 1, 0, 0]), f)
        left = blend_hand(support, contact_hand, grab, 10, 32, parents)
        solved = arm(base, left)
        reach_errors.append(float(np.linalg.norm(solved[10][:3, 3]-left[10][:3, 3])))
        desired.update(solved)
        rack = smooth([0, .765, .80, .815, .855, 1], np.array([0, 0, .065, .065, 0, 0]), f)
        desired[89][:3, 3] -= gun[:3, 0]*rack
        hook_delta = gun.copy(); hook_delta[:3, 3] -= gun[:3, 0]*rack
        right_hook = [hook_delta @ m for m in hook]
        reach = smooth([0, .705, .765, .855, .945, 1], np.array([0, 0, 1, 1, 0, 0]), f)
        right = blend_hand(support, right_hook, reach, 35, 57, parents)
        desired.update(arm(base, right, right=True))
        for n in animated:
            p = parents.get(n)
            parent = desired.get(p, held[p]) if p is not None else np.eye(4)
            frames[n].append(np.linalg.inv(parent) @ desired[n])
    if max(reach_errors) > .002:
        raise ValueError(f'AK reload exceeds arm reach by {max(reach_errors):.4f} m')
    animation = {'name': 'FirstReloStFd2', 'channels': [], 'samplers': []}
    for n, poses in frames.items():
        keys = [decompose(m) for m in poses]
        for i, channel in enumerate(('translation', 'rotation', 'scale')):
            values = [k[i] for k in keys]
            if channel == 'rotation':
                for j in range(1, len(values)):
                    if values[j-1] @ values[j] < 0: values[j] = -values[j]
            if channel == 'scale' and n in scales:
                values = [v*s for v, s in zip(values, scales[n])]
            curve(doc, data, animation, n, channel, times, values)
            if channel == 'scale' and n in scales: animation['samplers'][-1]['interpolation'] = 'STEP'
    doc['animations'] = [a for a in doc['animations'] if a['name'] != animation['name']] + [animation]
    doc.setdefault('extras', {})['ak_reload'] = VERSION
    doc['extras']['ak_reload_events'] = EVENTS
    doc['extras']['ak_reload_max_hand_reach_error_m'] = max(reach_errors)
