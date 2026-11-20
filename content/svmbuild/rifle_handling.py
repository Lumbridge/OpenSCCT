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


def split_m4_backup_sight(reference, folder):
    """Give the M4's folding aperture a draw section so a mounted handle can replace it."""
    import copy
    world, blob = read(reference)
    receiver = next(mesh for mesh in world['meshes'] if mesh['name'] == 'm4a1s')
    for part in receiver['primitives']:
        positions = values(world, blob, part['attributes']['POSITION'])
        faces = values(world, blob, part['indices']).reshape(-1, 3)
        # This artist mesh has a disconnected rear leaf. Its corners fit wholly inside
        # these bounds, including duplicate vertices along UV and shading seams.
        inside = np.all((positions >= [-.0172, .1354, -.0059]) &
                        (positions <= [-.0085, .1680, .0056]), axis=1)
        leaf = inside[faces].all(axis=1)
        if leaf.any():
            if not np.array_equal(leaf, inside[faces].any(axis=1)):
                raise ValueError("M4 rear sight intersects receiver triangles")
            break
    else:
        raise ValueError("Could not locate the folding sight on the M4 receiver")

    uv = values(world, blob, part['attributes']['TEXCOORD_0'])
    material_name = 'OpenScctRifles.M4BackupSight'
    for suffix, mesh_name in (('', 'm4a1s'), ('_first', 'm4a1s_parts')):
        path = folder / ('m4a1s' + suffix + '.glb')
        doc, data = read(path)
        if any(material['name'] == material_name for material in doc['materials']):
            continue
        mesh = next(mesh for mesh in doc['meshes'] if mesh['name'] == mesh_name)
        # The two rigs share UVs and triangle order. Only the first-person vertex positions differ.
        for primitive in mesh['primitives']:
            indices = values(doc, data, primitive['indices']).reshape(-1, 3)
            if (np.array_equal(indices, faces) and
                    np.array_equal(values(doc, data, primitive['attributes']['TEXCOORD_0']), uv)):
                break
        else:
            raise ValueError(f"No matching receiver topology in {path.name}")
        material = copy.deepcopy(doc['materials'][primitive['material']])
        material['name'] = material_name
        sight = dict(primitive, material=len(doc['materials']))
        doc['materials'].append(material)
        component = doc['accessors'][primitive['indices']]['componentType']
        primitive['indices'] = append(doc, data, indices[~leaf].reshape(-1, 1), 'SCALAR', component, 34963)
        sight['indices'] = append(doc, data, indices[leaf].reshape(-1, 1), 'SCALAR', component, 34963)
        mesh['primitives'].append(sight)
        pending = path.with_suffix('.writing')
        write(pending, doc, data)
        pending.replace(path)


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
    if doc.get('extras', {}).get('rifle_handling') == 2:
        if profile == 'ak47':
            from .ak_reload import author as author_ak_reload
            author_ak_reload(doc, data, _part_points)
        from .mine_handling import author as author_mines
        author_mines(doc, data, profile)
        write(path, doc, data)
        return
    from .rifle_contacts import author
    # Retain the M4 and FAMAS handling; only the reported AK/Galil contacts are edited.
    author(doc, data, profile, _part_points)
    if profile == 'ak47':
        from .ak_reload import author as author_ak_reload
        author_ak_reload(doc, data, _part_points)
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
    from .mine_handling import author as author_mines
    author_mines(doc, data, profile)
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
