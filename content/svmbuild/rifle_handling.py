"""Visible magazine contacts and camera-aligned sights for the custom rifle rigs."""
import math
import numpy as np
from PIL import Image
from .sidearm_models import read, write, values, append, world_matrices, matrix
from .pistol_arms import tracks, pose, curve, decompose, turn
from .reload_motion import solve_arm, smooth


def _curve_pose(doc, data, animation, node, times, poses):
    for i, channel in enumerate(('translation', 'rotation', 'scale')):
        curve(doc, data, animation, node, channel, times, [decompose(p)[i] for p in poses])


def _material(doc, folder, name, color, luminous=False):
    file = name + '.png'
    Image.new('RGBA', (4, 4), color).save(folder / 'textures' / file)
    image = len(doc.setdefault('images', [])); doc['images'].append({'uri': 'textures/' + file})
    texture = len(doc.setdefault('textures', [])); doc['textures'].append({'source': image})
    material = len(doc.setdefault('materials', []))
    entry = {'name': 'OpenScctRifles.' + name, 'doubleSided': True,
             'pbrMetallicRoughness': {'baseColorTexture': {'index': texture}, 'metallicFactor': .3, 'roughnessFactor': .6}}
    if luminous: entry['extras'] = {'scct_self_illum': 'textures/' + file}
    doc['materials'].append(entry)
    return material


def _box(doc, data, low, high, transform, joint, material):
    low, high = np.asarray(low), np.asarray(high)
    points = np.array([[high[k] if i & (1 << k) else low[k] for k in range(3)] for i in range(8)])
    faces = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
    xyz, normals, uv, indices = [], [], [], []
    for face in faces:
        p = points[list(face)]; n = np.cross(p[1]-p[0], p[2]-p[0]); n /= np.linalg.norm(n)
        start = len(xyz); xyz.extend((transform[:3, :3] @ p.T).T + transform[:3, 3])
        normal = np.linalg.inv(transform[:3, :3]).T @ n; normal /= np.linalg.norm(normal)
        normals.extend([normal] * 4); uv.extend([[0, 0], [1, 0], [1, 1], [0, 1]])
        indices.extend([start, start+1, start+2, start, start+2, start+3])
    return {'attributes': {'POSITION': append(doc, data, xyz, 'VEC3'), 'NORMAL': append(doc, data, normals, 'VEC3'),
        'TEXCOORD_0': append(doc, data, uv, 'VEC2'), 'JOINTS_0': append(doc, data, [[joint, 0, 0, 0]] * len(xyz), 'VEC4', 5123),
        'WEIGHTS_0': append(doc, data, [[1, 0, 0, 0]] * len(xyz), 'VEC4')},
        'indices': append(doc, data, np.array(indices).reshape(-1, 1), 'SCALAR', 5125), 'material': material}


def _mesh(doc, name, primitives):
    mesh = len(doc['meshes']); doc['meshes'].append({'name': name, 'primitives': primitives})
    node = len(doc['nodes']); doc['nodes'].append({'name': name, 'mesh': mesh, 'skin': 0})
    doc['scenes'][doc.get('scene', 0)]['nodes'].append(node)


def _joint(doc, data, name, parent, world):
    rest, _ = world_matrices(doc)
    local = np.linalg.inv(rest[parent]) @ world
    node = len(doc['nodes']); doc['nodes'].append({'name': name, 'matrix': local.T.reshape(-1).tolist()})
    doc['nodes'][parent].setdefault('children', []).append(node)
    skin = doc['skins'][0]; skin['joints'].append(node)
    binds = values(doc, data, skin['inverseBindMatrices']).tolist()
    binds.append(np.linalg.inv(world).T.reshape(-1).tolist())
    skin['inverseBindMatrices'] = append(doc, data, binds, 'MAT4')
    return node, len(skin['joints'])-1


def _part_points(doc, data, world, node):
    skin = doc['skins'][0]; joint = skin['joints'].index(node)
    bind = values(doc, data, skin['inverseBindMatrices'])[joint].reshape(4, 4).T
    result = []
    for mesh in doc['meshes']:
        for primitive in mesh['primitives']:
            attrs = primitive['attributes']
            if 'JOINTS_0' not in attrs: continue
            selected = values(doc, data, attrs['JOINTS_0'])[:, 0] == joint
            p = values(doc, data, attrs['POSITION'])[selected]
            if len(p):
                m = world[node] @ bind
                result.extend((m[:3, :3] @ p.T).T + m[:3, 3])
    return np.asarray(result)


def _append_optic(doc, data, folder, joint, placement, asset="micro_red_dot.glb"):
    from copy import deepcopy
    source, blob = read(folder / asset)
    for extension in source.get('extensionsUsed', []):
        if extension not in doc.setdefault('extensionsUsed', []): doc['extensionsUsed'].append(extension)
    world, _ = world_matrices(source)
    image_offset, texture_offset, material_offset = len(doc['images']), len(doc['textures']), len(doc['materials'])
    doc['images'].extend(deepcopy(source['images']))
    for texture in deepcopy(source['textures']):
        # The receiving rig has its own sampler table; runtime materials choose filtering themselves.
        texture.pop('sampler', None)
        texture['source'] += image_offset; doc['textures'].append(texture)
    def remap(value):
        if not isinstance(value, dict): return
        for key, child in value.items():
            if key.endswith('Texture') and isinstance(child, dict) and 'index' in child: child['index'] += texture_offset
            else: remap(child)
    for material in deepcopy(source['materials']):
        remap(material); doc['materials'].append(material)
    primitives = []
    for i, node in enumerate(source['nodes']):
        if 'mesh' not in node: continue
        transform = placement @ world[i]
        for primitive in source['meshes'][node['mesh']]['primitives']:
            own = primitive['attributes']; xyz = values(source, blob, own['POSITION'])
            normals = values(source, blob, own['NORMAL'])
            xyz = (transform[:3, :3] @ xyz.T).T + transform[:3, 3]
            normals = (np.linalg.inv(transform[:3, :3]).T @ normals.T).T
            normals /= np.maximum(1e-12, np.linalg.norm(normals, axis=1, keepdims=True))
            attrs = {'POSITION': append(doc, data, xyz, 'VEC3'), 'NORMAL': append(doc, data, normals, 'VEC3'),
                     'JOINTS_0': append(doc, data, [[joint, 0, 0, 0]]*len(xyz), 'VEC4', 5123),
                     'WEIGHTS_0': append(doc, data, [[1, 0, 0, 0]]*len(xyz), 'VEC4')}
            if 'TEXCOORD_0' in own: attrs['TEXCOORD_0'] = append(doc, data, values(source, blob, own['TEXCOORD_0']), 'VEC2')
            primitives.append({'attributes': attrs, 'material': primitive['material']+material_offset,
                'indices': append(doc, data, values(source, blob, primitive['indices']), 'SCALAR', 5125)})
    _mesh(doc, 'MicroRedDot', primitives)


def _support_grip(held, profile):
    """Cup the fore-end with the palm below it and four fixed-length fingers around its far side."""
    knuckles = (15, 19, 24, 28)
    midpoint = np.mean([held[n][:3, 3] for n in knuckles], axis=0)
    along = held[15][:3, 3] - held[28][:3, 3]; along /= np.linalg.norm(along)
    across = midpoint - held[10][:3, 3]
    across -= along * (across @ along); across /= np.linalg.norm(across)
    # An oblique underhand grip lets the forearm approach along the palm instead of kinking the wrist.
    basis = np.column_stack([along, across, np.cross(along, across)])
    rotation = np.column_stack([[1., 0, 0], [0., 0, 1.], [0., -1., 0]]) @ basis.T
    angle = {'ak47': .45, 'm4a1s': .30, 'galil': .34, 'famas': .12}[profile]
    c, s = math.cos(angle), math.sin(angle)
    rotation = np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]]) @ rotation
    x, y, z, ry, rz = {
        'ak47': (.490, -.144, .138, .038, .031),
        'm4a1s': (.435, -.134, .138, .039, .037),
        'galil': (.460, -.154, .145, .044, .035),
        'famas': (.330, -.127, .137, .039, .034),
    }[profile]
    target = np.array([x, y-ry, z-.003])
    placement = np.eye(4); placement[:3, :3] = rotation
    placement[:3, 3] = target - rotation @ midpoint
    fitted = [m.copy() for m in held]
    for n in range(10, 32): fitted[n] = placement @ held[n]
    for chain in ((15, 16, 17, 18), (19, 20, 21, 22), (24, 25, 26, 27), (28, 29, 30, 31)):
        theta = 0.
        for node, child in zip(chain, chain[1:]):
            start = fitted[node][:3, 3]
            length = np.linalg.norm(held[child][:3, 3]-held[node][:3, 3])
            child_x = (placement @ held[child])[:3, 3][0]
            def point(angle): return np.array([child_x, y-ry*math.cos(angle), z+rz*math.sin(angle)])
            # The first segment meets the guard from the slightly staggered MCP row.
            low = theta; high = theta+.02
            while high < theta+math.pi and np.linalg.norm(point(high)-start) < length: high += .02
            for _ in range(28):
                mid = (low+high)*.5
                if np.linalg.norm(point(mid)-start) < length: low = mid
                else: high = mid
            theta = (low+high)*.5
            end = point(theta)
            before = (placement @ held[child])[:3, 3] - (placement @ held[node])[:3, 3]
            fitted[node][:3, :3] = turn(before, end-start) @ (placement @ held[node])[:3, :3]
            fitted[child][:3, 3] = end
        fitted[chain[-1]][:3, :3] = fitted[chain[-2]][:3, :3] @ np.linalg.inv(held[chain[-2]][:3, :3]) @ held[chain[-1]][:3, :3]
    # Move the shoulder anchor slightly inward while retaining the original two limb lengths.
    arm = [m.copy() for m in held]
    for n in (7, 8, 9, 10): arm[n][:3, 3] += [.10, .02, .04]
    fitted_arm = _support_arm(arm, fitted)
    for n, transform in fitted_arm.items(): fitted[n] = transform
    return fitted


def _support_arm(reference, grip):
    wrist = grip[10]
    palm = np.mean([grip[n][:3, 3] for n in (15, 19, 24, 28)], axis=0)-wrist[:3, 3]
    palm /= np.linalg.norm(palm)
    length = np.linalg.norm(reference[10][:3, 3]-reference[8][:3, 3])
    elbow = wrist[:3, 3]-palm*length
    return solve_arm(reference, (7, 8, 10), wrist[:3, 3], wrist[:3, :3],
                     pole_offset=elbow-reference[8][:3, 3], twist_node=9)


def author_rifle(path, profile):
    doc, data = read(path)
    if doc.get('extras', {}).get('rifle_handling') == 1: return
    names = {n.get('name'): i for i, n in enumerate(doc['nodes'])}
    gun = next(i for n, i in names.items() if n and n.startswith('Main_j_'))
    magazine = next(i for n, i in names.items() if n and n.startswith('Mag1_j_'))
    bolt = next((i for n, i in names.items() if n and n.startswith('Bolt_j_')), None)
    held_clip = next(a for a in doc['animations'] if a['name'] == 'FirstWaitStFd2')
    original_held = pose(doc, tracks(doc, data, held_clip), 0)
    held = _support_grip(original_held, profile)
    _, parents = world_matrices(doc)
    local = [np.linalg.inv(held[parents[i]]) @ m if i in parents else m for i, m in enumerate(held)]
    for animation in doc['animations']:
        original = tracks(doc, data, animation)
        duration = max(float(t[-1]) for _, _, t, _ in original)
        reload = animation['name'] == 'FirstReloStFd2'
        nodes = set(doc['skins'][0]['joints']) if reload else set(range(7, 32))
        frames = {n: [] for n in nodes}
        times = np.linspace(0, duration, max(2, math.ceil(duration*30)+1))
        magazine_points = _part_points(doc, data, held, magazine)
        centre = (magazine_points.min(0) + magazine_points.max(0)) * .5
        mag_contact = np.linalg.inv(held[magazine]) @ np.r_[centre + [0, 0, -.005], 1]
        finger = (held[18][:3, 3] + held[22][:3, 3]) * .5 - held[10][:3, 3]
        hand_turn = turn(finger, np.array([0., 0., 1.]))
        for time in times:
            f = time / duration
            old = pose(doc, original, time)
            if not reload:
                follow = old[gun] @ np.linalg.inv(held[gun])
                base = [m.copy() for m in old]
                for n in (7, 8, 9, 10): base[n][:3, 3] += held[7][:3, 3]-original_held[7][:3, 3]
                desired = _support_arm(base, [follow @ m for m in held])
                desired.update({n: follow @ held[n] for n in range(11, 32)})
            else:
                envelope = smooth([0, .12, .24, .76, .93, 1], np.array([0, .6, 1, 1, .3, 0]), f)
                angle = -.23 * envelope; c, s = math.cos(angle), math.sin(angle)
                delta = np.eye(4); delta[:3, :3] = [[1, 0, 0], [0, c, -s], [0, s, c]]
                pivot = held[gun][:3, 3]
                delta[:3, 3] = pivot - delta[:3, :3] @ pivot + np.array([.100, .120, -.045]) * envelope
                # Fingers inherit the solved wrists; rigid mechanisms inherit the same gun frame.
                desired = {n: delta @ held[n] for n in doc['skins'][0]['joints'] if n >= gun}
                gun_pose = delta @ held[gun]
                mag_pose = delta @ held[magazine]
                travel = smooth([0, .18, .33, .50, .68, .76, 1], np.array([0, 0, .055, .055, .025, 0, 0]), f)
                mag_pose[:3, 3] += np.array([-.012 * envelope, -travel, -.015 * envelope])
                desired[magazine] = mag_pose
                contact = (mag_pose @ mag_contact)[:3]
                orientation = delta[:3, :3] @ hand_turn @ held[10][:3, :3]
                wrist = contact - delta[:3, :3] @ hand_turn @ finger
                grip = delta @ held[10]
                blend = smooth([0, .10, .22, .73, .91, 1], np.array([0, 0, 1, 1, 0, 0]), f)
                target = grip[:3, 3] * (1-blend) + wrist * blend
                from .reload_choreography import mix
                hand = grip.copy(); hand[:3, :3] = orientation
                hand = mix(grip, hand, blend)
                base = held
                if blend < .01:
                    desired.update(_support_arm(base, [delta @ m for m in held]))
                else:
                    desired.update(solve_arm(base, (7, 8, 10), target, hand[:3, :3], twist_node=9))
                right = delta @ held[35]
                desired.update(solve_arm(base, (32, 33, 35), right[:3, 3], right[:3, :3], twist_node=34))
                if bolt is not None:
                    rack = smooth([0, .77, .82, .86, .91, 1], np.array([0, 0, .025, .025, 0, 0]), f)
                    desired[bolt] = delta @ held[bolt]; desired[bolt][:3, 3] -= np.array([rack, 0, 0])
            def current(n):
                if n in desired: return desired[n]
                parent = parents.get(n)
                return current(parent) @ local[n] if parent is not None else local[n]
            for node in nodes:
                parent = parents.get(node)
                frames[node].append(np.linalg.inv(current(parent)) @ current(node) if parent is not None else current(node))
        animation['channels'] = [c for c in animation['channels'] if c['target']['node'] not in nodes]
        for node, poses in frames.items(): _curve_pose(doc, data, animation, node, times, poses)

    # Calibrate a real sight line on the weapon. Both the marker and sight geometry follow its gun bone.
    fx = original_held[names['FX']][:3, 3]
    centres = {'ak47': [.420, -.108, fx[2]], 'galil': [.213, -.093, fx[2]],
               'famas': [.080, -.003, fx[2]], 'm4a1s': [.349, -.035, fx[2]]}
    centre = np.array(centres[profile])
    sight = np.eye(4); sight[:3, 3] = centre
    if profile != 'm4a1s':

        front = np.array({'galil': [.646, -.0875, fx[2]], 'ak47': [.736, -.109, fx[2]], 'famas': [.377, -.004, fx[2]]}[profile])
        sight[:3, :3] = turn(np.array([1., 0., 0.]), (front-centre)/np.linalg.norm(front-centre))
    rest, _ = world_matrices(doc)
    local_sight = np.linalg.inv(original_held[gun]) @ sight
    marker_world = rest[gun] @ local_sight
    _joint(doc, data, 'ADS', gun, marker_world)
    main_joint = doc['skins'][0]['joints'].index(gun)
    bind = values(doc, data, doc['skins'][0]['inverseBindMatrices'])[main_joint].reshape(4, 4).T
    to_mesh = np.linalg.inv(original_held[gun] @ bind)
    if profile == 'm4a1s':
        _append_optic(doc, data, path.parent, main_joint, to_mesh @ sight)
    elif profile == 'famas':
        _append_optic(doc, data, path.parent, main_joint, to_mesh @ sight, 'famas_irons.glb')
    doc.setdefault('extras', {})['rifle_handling'] = 1
    write(path, doc, data)


def world_sights(path, first_path, profile):
    if profile not in ('m4a1s', 'famas'): return
    doc, data = read(path); first, blob = read(first_path)
    held = pose(first, tracks(first, blob, next(a for a in first['animations'] if a['name'] == 'FirstWaitStFd2')), 0)
    names = {n.get('name'): i for i, n in enumerate(first['nodes'])}
    world, _ = world_matrices(doc)
    muzzle = next(i for i, n in enumerate(doc['nodes']) if n.get('name') == 'FX')
    sight = world[muzzle] @ np.linalg.inv(held[names['FX']]) @ held[names['ADS']]
    _append_optic(doc, data, path.parent, 0, sight,
                  'micro_red_dot.glb' if profile == 'm4a1s' else 'famas_irons.glb')
    write(path, doc, data)


def revolver_yoke(path):
    """Keep a visible steel crane between the frame and the cylinder throughout its swing."""
    doc, data = read(path)
    if doc.get('extras', {}).get('revolver_yoke') == 1: return
    names = {n.get('name'): i for i, n in enumerate(doc['nodes'])}
    frame, cylinder = names['base'], names['slide']
    idle = pose(doc, tracks(doc, data, next(a for a in doc['animations'] if a['name'] == 'FirstWaitStFd2')), 0)
    points = _part_points(doc, data, idle, cylinder)
    low, high = points.min(0), points.max(0)
    bearing = np.array([high[0]-.009, low[1]+.009, (low[2]+high[2])*.5])
    pivot = bearing + np.array([.006, -.017, .006])
    frame_point = np.linalg.inv(idle[frame]) @ np.r_[pivot, 1]
    cylinder_point = np.linalg.inv(idle[cylinder]) @ np.r_[bearing, 1]
    rest, _ = world_matrices(doc)
    node, joint = _joint(doc, data, 'CylinderCrane', frame, np.eye(4))
    material = _material(doc, path.parent, 'CylinderCrane', (86, 89, 92, 255))
    _mesh(doc, 'CylinderCraneMesh', [_box(doc, data, [0, -.0025, -.0025], [1, .0025, .0025], np.eye(4), joint, material)])
    for animation in doc['animations']:
        source = tracks(doc, data, animation); duration = max(float(t[-1]) for _, _, t, _ in source)
        times = np.linspace(0, duration, max(2, math.ceil(duration*30)+1)); frames = []
        for time in times:
            world = pose(doc, source, time)
            start, end = (world[frame] @ frame_point)[:3], (world[cylinder] @ cylinder_point)[:3]
            direction = end-start; length = max(.001, np.linalg.norm(direction))
            basis = turn(np.array([1., 0, 0]), direction/length); basis[:, 0] *= length
            crane = np.eye(4); crane[:3, :3] = basis; crane[:3, 3] = start
            frames.append(np.linalg.inv(world[frame]) @ crane)
        _curve_pose(doc, data, animation, node, times, frames)
    doc.setdefault('extras', {})['revolver_yoke'] = 1
    write(path, doc, data)
