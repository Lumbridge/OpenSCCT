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


def _famas_sights(doc, data, folder, joint, placement):
    """Complete the game's F1 model: thin apertures and blade in its existing handle.

    Visual reference: MAT 1057 (1986), figures 5/6. The imported model has the
    sight channel but omits these parts. No replacement handle or accessory rail.
    https://www.kalasnyikov.hu/dokumentumok/famas.pdf
    """
    material = _material(doc, folder, 'FamasSightSteel', (66, 69, 68, 255))
    primitives = []
    for x, bore in ((.074, .0028), (.080, .0022)):
        points, normals, uv, indices = [], [], [], []
        rings = [(x-.0007, .006), (x+.0007, .006), (x+.0007, bore), (x-.0007, bore)]
        def point(row, n):
            xx, radius = rings[row % 4]; angle = n*math.tau/48
            if row % 4 < 2:
                radius = min(.006/max(.001,abs(math.sin(angle))), .007/max(.001,abs(math.cos(angle))))
            return [xx, -.022+math.cos(angle)*radius, .1365+math.sin(angle)*radius]
        for row in range(4):
            for n in range(48):
                face = np.array([point(row,n),point(row,n+1),point(row+1,n+1),point(row+1,n)])
                normal = np.cross(face[1]-face[0],face[2]-face[0]); normal /= np.linalg.norm(normal)
                start = len(points)
                points.extend((placement[:3,:3] @ face.T).T+placement[:3,3])
                normal = np.linalg.inv(placement[:3,:3]).T @ normal; normal /= np.linalg.norm(normal)
                normals.extend([normal]*4); uv.extend([[0,0],[1,0],[1,1],[0,1]])
                indices.extend([start,start+1,start+2,start,start+2,start+3])
        primitives.append({'attributes': {'POSITION': append(doc,data,points,'VEC3'),
            'NORMAL': append(doc,data,normals,'VEC3'), 'TEXCOORD_0': append(doc,data,uv,'VEC2'),
            'JOINTS_0': append(doc,data,[[joint,0,0,0]]*len(points),'VEC4',5123),
            'WEIGHTS_0': append(doc,data,[[1,0,0,0]]*len(points),'VEC4')},
            'indices': append(doc,data,np.array(indices).reshape(-1,1),'SCALAR',5125),'material':material})
        primitives.append(_box(doc,data,[x-.001,-.033,.1335],[x+.001,-.026,.1395],placement,joint,material))
    primitives.append(_box(doc,data,[.366,-.042,.1358],[.369,-.022,.1372],placement,joint,material))
    _mesh(doc, 'FamasNativeSightParts', primitives)


def author_rifle(path, profile):
    doc, data = read(path)
    if doc.get('extras', {}).get('rifle_handling') == 2: return
    from .rifle_contacts import author
    # Retain the M4 and FAMAS handling; only the reported AK/Galil contacts are edited.
    author(doc, data, profile, _part_points)
    names = {n.get('name'): i for i, n in enumerate(doc['nodes'])}
    gun = names['Main_j_049']
    held = pose(doc, tracks(doc, data, next(a for a in doc['animations'] if a['name'] == 'FirstWaitStFd2')), 0)
    z = held[names['FX']][2, 3]
    rear, front = {
        'm4a1s': ([.285, -.053, z], [.60, -.053, z]),
        'ak47': ([.420, -.102, z], [.736, -.106, z]),
        'galil': ([.213, -.093, z], [.646, -.0875, z]),
        # Native sight channel along the top of the carry handle.
        'famas': ([.074, -.022, .1365], [.3675, -.022, .1365]),
    }[profile]
    sight = np.eye(4); sight[:3, 3] = rear
    sight[:3, :3] = turn(np.array([1., 0, 0]), np.asarray(front)-rear)
    rest, _ = world_matrices(doc)
    _joint(doc, data, 'ADS', gun, rest[gun] @ np.linalg.inv(held[gun]) @ sight)
    if profile == 'm4a1s':
        joint = doc['skins'][0]['joints'].index(gun)
        bind = values(doc, data, doc['skins'][0]['inverseBindMatrices'])[joint].reshape(4, 4).T
        _append_optic(doc, data, path.parent, joint, np.linalg.inv(held[gun] @ bind) @ sight)
    if profile == 'famas':
        joint = doc['skins'][0]['joints'].index(gun)
        bind = values(doc, data, doc['skins'][0]['inverseBindMatrices'])[joint].reshape(4, 4).T
        _famas_sights(doc, data, path.parent, joint, np.linalg.inv(held[gun] @ bind))
    doc.setdefault('extras', {})['rifle_handling'] = 2
    write(path, doc, data)


def world_sights(path, first_path, profile):
    if profile not in ('m4a1s', 'famas'): return
    doc, data = read(path); first, blob = read(first_path)
    held = pose(first, tracks(first, blob, next(a for a in first['animations'] if a['name'] == 'FirstWaitStFd2')), 0)
    names = {n.get('name'): i for i, n in enumerate(first['nodes'])}
    world, _ = world_matrices(doc)
    muzzle = next(i for i, n in enumerate(doc['nodes']) if n.get('name') == 'FX')
    sight = world[muzzle] @ np.linalg.inv(held[names['FX']]) @ held[names['ADS']]
    if profile == 'm4a1s': _append_optic(doc, data, path.parent, 0, sight)
    else: _famas_sights(doc, data, path.parent, 0, world[muzzle] @ np.linalg.inv(held[names['FX']]))
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
