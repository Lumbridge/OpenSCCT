"""Mine retrieval, held loop and wall-placement motions for custom viewmodels."""
import math
import numpy as np

from .sidearm_models import read, write, world_matrices
from .pistol_arms import tracks, pose, curve, decompose, turn
from .reload_motion import solve_arm, smooth
from .reload_choreography import path, mix

CLIPS = {'FirstMineStBg2': .4, 'FirstMineStNt2': 2/3, 'FirstMineStEd2': 14/15}
OFFSETS = {'ak47': [0, -.115, .075], 'm4a1s': [.015, -.12, .070],
           'galil': [.020, -.11, .080], 'famas': [.025, -.100, .090]}


def author(doc, data, profile):
    if doc.get('extras', {}).get('mine_handling') == 2: return
    idle = next(a for a in doc['animations'] if a['name'] == 'FirstWaitStFd2')
    held = pose(doc, tracks(doc, data, idle), 0)
    _, parents = world_matrices(doc)
    names = {n.get('name'): i for i, n in enumerate(doc['nodes'])}
    gun = names.get('Main_j_049', names.get('pistol'))
    if gun is None: raise ValueError('Missing custom weapon root')
    # The mounted pistol parent is an animated transform, although it is not
    # weighted by any vertices. Bake it too so its children follow the hand once.
    joints = set(doc['skins'][0]['joints']) | {gun}
    hand_local = {n: np.linalg.inv(held[10]) @ held[n] for n in range(11, 32)}
    pointing = [m.copy() for m in held]
    # A relaxed closed hand with the index extended along the placement direction.
    for joint, child in ((15, 16), (16, 17), (17, 18)):
        start = pointing[joint][:3, 3]
        swing = turn(pointing[child][:3, 3]-start, np.array([1., 0, 0]))
        for n in range(joint, 19):
            pointing[n][:3, :3] = swing @ pointing[n][:3, :3]
            pointing[n][:3, 3] = start+swing @ (pointing[n][:3, 3]-start)
    point_local = {n: np.linalg.inv(held[10]) @ pointing[n] for n in range(11, 32)}
    pocket = held[10].copy(); pocket[:3, 3] = [.15, -.43, -.08]
    reach = held[10].copy(); reach[:3, 3] = [.32, -.12, -.035]
    press = held[10].copy(); press[:3, 3] = [.39, -.10, -.035]
    retract = held[10].copy(); retract[:3, 3] = [.24, -.22, -.10]
    if profile not in OFFSETS:
        # The pistol arm rig starts farther behind the camera than the rifle
        # rig. Its placement gesture must use its own reachable wrist targets.
        pocket[:3, 3] = [.10, -.40, -.04]
        reach[:3, 3] = [.24, -.16, .015]
        press[:3, 3] = [.28, -.14, .015]
        retract[:3, 3] = [.18, -.24, -.01]
    offset = np.asarray(OFFSETS.get(profile, [.010, -.085, .065]))
    doc['animations'] = [a for a in doc['animations'] if a['name'] not in CLIPS]
    reach_errors = []
    for name, duration in CLIPS.items():
        times = np.linspace(0, duration, math.ceil(duration*60)+1)
        frames = {n: [] for n in joints}
        for time in times:
            f = time/duration
            if name == 'FirstMineStBg2':
                lowered = smooth([0, .25, 1], np.array([0, .35, 1]), f)
                hand = mix(held[10], pocket, f)
                finger = 0.
            elif name == 'FirstMineStNt2':
                lowered = 1.
                hand = pocket.copy(); hand[1, 3] += math.sin(f*math.tau)*.002
                finger = 0.
            else:
                lowered = 1-smooth([0, .62, 1], np.array([0, 0, 1]), f)
                hand = path([0, .18, .36, .53, .64, .80, 1],
                            [pocket, retract, reach, press, press, retract, held[10]], f)
                finger = smooth([0, .16, .35, .67, .90, 1], np.array([0, 0, 1, 1, 0, 0]), f)
            delta = np.eye(4); delta[:3, 3] = offset*lowered
            desired = {n: held[n].copy() for n in joints}
            # The gun retains its own firing-hand relationship, with both
            # shoulders fixed. Different receiver lengths get their own clearance.
            for n in joints:
                if n >= gun: desired[n] = delta @ held[n]
            right_target = (delta @ held[35])[:3, 3]
            right = solve_arm(held, (32, 33, 35), right_target, held[35][:3, :3], twist_node=34)
            reach_errors.append(float(np.linalg.norm(right[35][:3, 3]-right_target)))
            desired.update(right)
            for n in range(36, 57): desired[n] = delta @ held[n]
            left = solve_arm(held, (7, 8, 10), hand[:3, 3], hand[:3, :3], twist_node=9)
            reach_errors.append(float(np.linalg.norm(left[10][:3, 3]-hand[:3, 3])))
            desired.update(left)
            for n in range(11, 32): desired[n] = desired[10] @ mix(hand_local[n], point_local[n], finger)
            for n in joints:
                parent = parents.get(n)
                parent_pose = desired.get(parent, held[parent]) if parent is not None else np.eye(4)
                frames[n].append(decompose(np.linalg.inv(parent_pose) @ desired[n]))
        animation = {'name': name, 'channels': [], 'samplers': []}
        for n, poses in frames.items():
            for i, channel in enumerate(('translation', 'rotation', 'scale')):
                keys = [p[i] for p in poses]
                if channel == 'rotation':
                    for j in range(1, len(keys)):
                        if keys[j-1] @ keys[j] < 0: keys[j] = -keys[j]
                curve(doc, data, animation, n, channel, times, keys)
        doc['animations'].append(animation)
    if max(reach_errors) > .002:
        raise ValueError(f'{profile}: mine gesture exceeds arm reach by {max(reach_errors):.4f} m')
    doc.setdefault('extras', {})['mine_handling'] = 2


def install(path, profile):
    doc, data = read(path)
    author(doc, data, profile)
    write(path, doc, data)
