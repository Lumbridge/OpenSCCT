"""Bake textured side renders from locally converted weapons, without a Blender dependency."""
import numpy as np
from PIL import Image
from .sidearm_models import read, values, world_matrices


def render(path, output):
    doc, blob = read(path); world, _ = world_matrices(doc)
    parts = []
    for node, item in enumerate(doc['nodes']):
        if 'mesh' not in item: continue
        skin = doc['skins'][item['skin']] if 'skin' in item else None
        if skin:
            binds = values(doc, blob, skin['inverseBindMatrices']).reshape(-1, 4, 4).transpose(0, 2, 1)
            matrices = np.array([world[j] @ bind for j, bind in zip(skin['joints'], binds)])
        for part in doc['meshes'][item['mesh']]['primitives']:
            attrs = part['attributes']; points = values(doc, blob, attrs['POSITION'])
            if skin and 'JOINTS_0' in attrs:
                joints = values(doc, blob, attrs['JOINTS_0']).astype(int)
                weights = values(doc, blob, attrs['WEIGHTS_0'])
                transformed = np.zeros_like(points)
                for column in range(4):
                    m = matrices[joints[:, column]]
                    transformed += ((m[:, :3, :3] @ points[:, :, None])[:, :, 0]+m[:, :3, 3])*weights[:, column, None]
                points = transformed
            else:
                m = world[node]; points = (m[:3, :3] @ points.T).T+m[:3, 3]
            material = doc['materials'][part['material']].get('pbrMetallicRoughness', {})
            texture = np.full((1, 1, 4), 255, np.uint8)
            if 'baseColorTexture' in material:
                image = doc['images'][doc['textures'][material['baseColorTexture']['index']]['source']]
                if 'uri' in image: texture = np.asarray(Image.open(path.parent/image['uri']).convert('RGBA'))
            uv = values(doc, blob, attrs['TEXCOORD_0']) if 'TEXCOORD_0' in attrs else np.zeros((len(points), 2))
            indices = values(doc, blob, part['indices']).reshape(-1, 3).astype(int)
            parts.append((points, uv, indices, texture))
    if not parts: raise ValueError(f'No weapon geometry: {path}')
    # A slight elevation shows the top rail while keeping the recognizable side silhouette.
    right, up, depth = np.array([1., 0, 0]), np.array([0, .998, -.063]), np.array([0, .063, .998])
    all_points = np.concatenate([p[0] for p in parts])
    projected = np.column_stack([all_points @ right, all_points @ up])
    low, high = projected.min(0), projected.max(0); centre = (low+high)*.5
    width, height = 800, 320
    scale = min((width-56)/(high[0]-low[0]), (height-40)/(high[1]-low[1]))
    pixels = np.zeros((height, width, 4), np.uint8); zbuffer = np.full((height, width), -np.inf)
    light = np.array([-.3, .7, 1.]); light /= np.linalg.norm(light)
    for points, uv, indices, texture in parts:
        screen = np.column_stack([(points @ right-centre[0])*scale+width*.5,
                                  -(points @ up-centre[1])*scale+height*.5])
        z = points @ depth
        for triangle in indices:
            p = screen[triangle]
            left, top = np.maximum(0, np.floor(p.min(0)).astype(int))
            right_edge, bottom = np.minimum([width-1, height-1], np.ceil(p.max(0)).astype(int))
            if right_edge < left or bottom < top: continue
            a, b, c = p; den = (b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
            if abs(den) < 1e-8: continue
            yy, xx = np.mgrid[top:bottom+1, left:right_edge+1]; xx = xx+.5; yy = yy+.5
            first = ((b[1]-c[1])*(xx-c[0])+(c[0]-b[0])*(yy-c[1]))/den
            second = ((c[1]-a[1])*(xx-c[0])+(a[0]-c[0])*(yy-c[1]))/den
            third = 1-first-second
            bary = np.stack([first, second, third], axis=-1)
            distance = bary @ z[triangle]
            old_z = zbuffer[top:bottom+1, left:right_edge+1]
            visible = (bary.min(axis=-1) >= -1e-6) & (distance > old_z)
            if not visible.any(): continue
            tex = bary @ uv[triangle]
            tx = np.clip((tex[..., 0]*texture.shape[1]).astype(int), 0, texture.shape[1]-1)
            ty = np.clip((tex[..., 1]*texture.shape[0]).astype(int), 0, texture.shape[0]-1)
            colour = texture[ty, tx].copy()
            normal = np.cross(points[triangle[1]]-points[triangle[0]], points[triangle[2]]-points[triangle[0]])
            normal /= max(1e-12, np.linalg.norm(normal))
            colour[..., :3] = np.clip(colour[..., :3]*(.58+.42*abs(normal @ light))+18, 0, 255)
            visible &= colour[..., 3] > 32
            pixels[top:bottom+1, left:right_edge+1][visible] = colour[visible]
            old_z[visible] = distance[visible]
    output.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(pixels).save(output)


def install(chars):
    names = ('M160', 'Hk81', 'BM401')
    for name in names:
        render(chars/'SWeaponFirst'/(name+'.glb'), chars/'SWeaponFirst/textures'/(name+'_icon.png'))
    return len(names)
