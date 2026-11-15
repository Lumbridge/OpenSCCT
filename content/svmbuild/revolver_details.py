"""Solid, hollow revolver cases with bevelled rims and recessed primers."""
import math
import numpy as np
from PIL import Image

from .sidearm_models import append


def materials(doc, folder):
    result = []
    for name, rgba in [('CaseBrass', (172, 124, 48, 255)), ('CasePrimer', (141, 130, 109, 255))]:
        Image.new('RGBA', (4, 4), rgba).save(folder / 'textures' / (name+'.png'))
        image = len(doc['images']); doc['images'].append({'uri': 'textures/'+name+'.png'})
        texture = len(doc['textures']); doc['textures'].append({'source': image})
        result.append(len(doc['materials']))
        doc['materials'].append({'name': 'OpenScctSidearms.'+name,
            'pbrMetallicRoughness': {'baseColorTexture': {'index': texture},
                                   'metallicFactor': .75, 'roughnessFactor': .32}})
    return result


def lathe(doc, data, profile, centre, transform, joint, material, segments=32):
    """Closed revolved cross section, including the head, bevels and inner mouth."""
    points, normals, uv, indices = [], [], [], []
    normal_transform = np.linalg.inv(transform[:3, :3]).T
    for section, ((x0, r0), (x1, r1)) in enumerate(zip(profile, profile[1:])):
        start = len(points)
        for x, radius in ((x0, r0), (x1, r1)):
            for step in range(segments+1):
                angle = step*math.tau/segments; c, s = math.cos(angle), math.sin(angle)
                point = np.array([x, centre[1]+radius*c, centre[2]+radius*s])
                normal = normal_transform @ np.array([r0-r1, (x1-x0)*c, (x1-x0)*s])
                normal /= max(1e-12, np.linalg.norm(normal))
                points.append(transform[:3, :3] @ point+transform[:3, 3]); normals.append(normal)
                uv.append([step/segments, section/(len(profile)-1)])
        for step in range(segments):
            a = start+step; b = a+1; c = a+segments+1; d = c+1
            indices.extend([a, b, c, b, d, c])
    return {'attributes': {'POSITION': append(doc, data, points, 'VEC3', target=34962),
        'NORMAL': append(doc, data, normals, 'VEC3', target=34962),
        'TEXCOORD_0': append(doc, data, uv, 'VEC2', target=34962),
        'JOINTS_0': append(doc, data, [[joint, 0, 0, 0]]*len(points), 'VEC4', 5123, 34962),
        'WEIGHTS_0': append(doc, data, [[1, 0, 0, 0]]*len(points), 'VEC4', target=34962)},
        'indices': append(doc, data, np.array(indices).reshape(-1, 1), 'SCALAR', 5125, 34963),
        'material': material}


def case_mesh(doc, data, source_points, neutral_to_mesh, joint, material_ids):
    low, high = source_points.min(0), source_points.max(0)
    centre = (low+high)*.5
    radius = min(high[1]-low[1], high[2]-low[2])*.46
    head, mouth = low[0], .1191
    # The original model's cut triangles retained mostly end caps. Build a
    # complete wall, recessed primer, rim, extraction groove and hollow mouth.
    profile = [(head, .0025), (head, radius*1.08), (head+.0006, radius*1.12),
               (head+.0016, radius*1.12), (head+.0020, radius*.89),
               (head+.0029, radius*.89), (head+.0033, radius),
               (mouth-.0005, radius*.97), (mouth, radius*.94),
               (mouth, radius*.78), (mouth-.0005, radius*.76),
               (head+.003, radius*.76), (head+.003, 0)]
    primer = [(head+.0001, 0), (head+.0001, .00235),
              (head, .0025), (head+.0004, .0025)]
    return [lathe(doc, data, profile, centre, neutral_to_mesh, joint, material_ids[0]),
            lathe(doc, data, primer, centre, neutral_to_mesh, joint, material_ids[1])]
