"""Fit the same credited fabric sleeves/gloves used by pistols to rifle handling.

The weapon keeps its authored mechanism tracks. Hands follow contact targets
with fixed limb lengths; bone-axis rotations are calibrated in palm space.
"""
from copy import deepcopy
from pathlib import Path
import math
import shutil
import numpy as np

from .sidearm_models import read, values, append, world_matrices, matrix
from .pistol_arms import tracks, pose, curve, decompose, rotation, quaternion, sample
from .reload_motion import solve_arm


def palm(world, wrist, knuckle, thumb):
    x = world[knuckle][:3, 3] - world[wrist][:3, 3]
    x /= np.linalg.norm(x)
    y = world[thumb][:3, 3] - world[wrist][:3, 3]
    y -= x * (x @ y); y /= np.linalg.norm(y)
    return np.column_stack([x, y, np.cross(x, y)])


def assemble(source, blob, folder):
    base = Path(__file__).resolve().parents[1] / 'assets/sidearms/export'
    doc, data = read(base / 'arms_base.glb')
    authored = tracks(doc, data, doc['animations'][0])
    # Keep the pistol's camera frame, finger shape, cloth mesh and UVs intact.
    c, s = math.cos(.15), math.sin(.15)
    camera = np.array([[-s, 0, c, .36-.171*c], [0, 1, 0, -.205],
                       [-c, 0, -s, .16+.171*s], [0, 0, 0, 1]])
    roots = doc['scenes'][doc.get('scene', 0)]['nodes']
    frame = len(doc['nodes'])
    doc['nodes'].append({'name': 'MercRifleFrame', 'matrix': camera.T.reshape(-1).tolist(), 'children': roots})
    doc['scenes'][doc.get('scene', 0)]['nodes'] = [frame]
    rest, parents = world_matrices(doc)
    neutral = pose(doc, authored, 8.8)
    local = [np.linalg.inv(neutral[parents[i]]) @ m if i in parents else m for i, m in enumerate(neutral)]
    skin = doc['skins'][0]
    binds = values(doc, data, skin['inverseBindMatrices']).tolist()
    mesh_world = rest[83]
    doc['materials'][0]['name'] = 'OpenScctRifles.Arms'
    for img in doc['images']:
        src = base / img['uri']; target = folder / 'textures' / ('shared_' + src.name)
        shutil.copy2(src, target); img['uri'] = 'textures/' + target.name
    material_offset, texture_offset, image_offset = len(doc['materials']), len(doc['textures']), len(doc['images'])
    def remap(value):
        if not isinstance(value, dict): return
        for key, child in value.items():
            if key.endswith('Texture') and isinstance(child, dict) and 'index' in child: child['index'] += texture_offset
            else: remap(child)
    for material in deepcopy(source['materials']): remap(material); doc['materials'].append(material)
    for texture in deepcopy(source['textures']): texture['source'] += image_offset; doc['textures'].append(texture)
    doc['images'].extend(deepcopy(source['images']))
    source_skin = source['skins'][0]
    source_binds = values(source, blob, source_skin['inverseBindMatrices']).reshape(-1, 4, 4).transpose(0, 2, 1)
    held = pose(source, tracks(source, blob, next(a for a in source['animations'] if a['name'] == 'FirstWaitStFd2')), 0)
    # Only rigid weapon parts enter the target skin. The M4 hand meshes are discarded.
    meshes = [(i, n) for i, n in enumerate(source['nodes']) if n.get('name', '').endswith('_parts') and 'mesh' in n]
    used = set()
    for _, n in meshes:
        for part in source['meshes'][n['mesh']]['primitives']:
            used.update(values(source, blob, part['attributes']['JOINTS_0'])[:, 0].astype(int))
    for name in ('FX', 'FX_DOUILLE'):
        used.add(source_skin['joints'].index(next(i for i, n in enumerate(source['nodes']) if n.get('name') == name)))
    mapping = {}
    for joint in sorted(used):
        old = source_skin['joints'][joint]; new = len(doc['nodes']); mapping[old] = new
        t, q, sc = decompose(np.linalg.inv(rest[6]) @ held[old])
        doc['nodes'].append({'name': source['nodes'][old]['name'], 'translation': t.tolist(), 'rotation': q.tolist(), 'scale': sc.tolist()})
        doc['nodes'][6]['children'].append(new)
        skin['joints'].append(new)
        binds.append((np.linalg.inv(held[old]) @ mesh_world).T.reshape(-1).tolist())
    for _, n in meshes:
        primitives = []
        for part in source['meshes'][n['mesh']]['primitives']:
            attrs = part['attributes']; joints = values(source, blob, attrs['JOINTS_0']).astype(int)
            xyz = values(source, blob, attrs['POSITION']); normals = values(source, blob, attrs['NORMAL'])
            positions, directions = xyz.copy(), normals.copy()
            for joint in np.unique(joints[:, 0]):
                old = source_skin['joints'][joint]
                transform = np.linalg.inv(mesh_world) @ held[old] @ source_binds[joint]
                selected = joints[:, 0] == joint
                positions[selected] = (transform[:3, :3] @ xyz[selected].T).T + transform[:3, 3]
                directions[selected] = (np.linalg.inv(transform[:3, :3]).T @ normals[selected].T).T
            directions /= np.maximum(1e-12, np.linalg.norm(directions, axis=1, keepdims=True))
            new_joints = np.zeros_like(joints)
            for joint in np.unique(joints[:, 0]): new_joints[joints[:, 0] == joint, 0] = skin['joints'].index(mapping[source_skin['joints'][joint]])
            primitives.append({'attributes': {
                'POSITION': append(doc, data, positions, 'VEC3'), 'NORMAL': append(doc, data, directions, 'VEC3'),
                'TEXCOORD_0': append(doc, data, values(source, blob, attrs['TEXCOORD_0']), 'VEC2'),
                'JOINTS_0': append(doc, data, new_joints, 'VEC4', 5123),
                'WEIGHTS_0': append(doc, data, values(source, blob, attrs['WEIGHTS_0']), 'VEC4')},
                'indices': append(doc, data, values(source, blob, part['indices']), 'SCALAR', 5125),
                'material': material_offset + part['material']})
        mesh = len(doc['meshes']); doc['meshes'].append({'name': n['name'], 'primitives': primitives})
        doc['nodes'].append({'name': n['name'], 'mesh': mesh, 'skin': 0})
        doc['nodes'][5]['children'].append(len(doc['nodes'])-1)
    skin['inverseBindMatrices'] = append(doc, data, binds, 'MAT4')
    _, parents = world_matrices(doc)
    chains = [(7, 8, 10, 39, 48, 40, 19, 11), (32, 33, 35, 16, 25, 17, 44, 36)]
    calibration = {}
    for upper, lower, wrist, old, knuckle, thumb, own_knuckle, own_thumb in chains:
        calibration[wrist] = palm(held, old, knuckle, thumb) @ palm(neutral, wrist, own_knuckle, own_thumb).T @ neutral[wrist][:3, :3]
    # Shoulder anchors remain behind the visible forearms, on their own side of the camera.
    for chain, anchor in [((7,8,9,10), [-.16,-.30,-.20]), ((32,33,34,35), [-.30,-.20,.30])]:
        offset = np.array(anchor)-neutral[chain[0]][:3,3]
        for node in chain: neutral[node][:3,3] += offset
    main = next(i for i,n in enumerate(source['nodes']) if n.get('name','').startswith('Main_j_'))
    doc['animations'] = []
    for clip in source['animations']:
        compiled = tracks(source, blob, clip); duration = max(float(t[-1]) for _, _, t, _ in compiled)
        times = np.linspace(0, duration, max(2, math.ceil(duration*30)+1))
        frames = {n: [] for n in skin['joints']}
        for time in times:
            old_world = pose(source, compiled, time); desired = {}
            if clip['name'] == 'FirstReloStFd2':
                # Preserve magazine/bolt contact, but reduce the source's wide
                # presentation swing so the wrists stay in a comfortable reach.
                raw = old_world[main]; target = held[main].copy()
                q = quaternion(rotation(raw) @ rotation(held[main]).T)
                q = sample(np.array([0.,1.]),np.array([[0.,0,0,1],q]),.55,True)
                target[:3,:3] = matrix({'rotation':q})[:3,:3] @ held[main][:3,:3]
                envelope = math.sin(time/max(duration,1e-9)*math.pi)**2
                target[:3,3] += (raw[:3,3]-held[main][:3,3])*.6 + np.array([.08,-.02,-.025])*envelope
                correction = target @ np.linalg.inv(raw)
                old_world = [correction@m for m in old_world]
            for upper, lower, wrist, old, *_ in chains:
                orientation = rotation(old_world[old]) @ rotation(held[old]).T @ calibration[wrist]
                desired.update(solve_arm(neutral, (upper, lower, wrist), old_world[old][:3, 3], orientation, twist_node=lower+1))
            for old, new in mapping.items(): desired[new] = old_world[old]
            def current(node):
                if node in desired: return desired[node]
                parent = parents.get(node)
                value = current(parent) @ local[node] if node < len(local) and parent is not None else neutral[node] if node < len(neutral) else np.eye(4)
                desired[node] = value; return value
            for node in frames:
                parent = parents.get(node)
                frames[node].append(decompose(np.linalg.inv(current(parent)) @ current(node) if parent is not None else current(node)))
        animation = {'name': clip['name'], 'channels': [], 'samplers': []}
        for node, poses in frames.items():
            for i, path in enumerate(('translation', 'rotation', 'scale')): curve(doc, data, animation, node, path, times, [p[i] for p in poses])
        doc['animations'].append(animation)
    doc['extras'] = deepcopy(source['extras'])
    return doc, data
