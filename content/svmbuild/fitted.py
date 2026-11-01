"""Geometry inputs and material boundaries for cosmetic baking (never atlas-wide paint)."""
from pathlib import Path
from functools import lru_cache

import numpy as np

from . import skeleton
from .sidearm_models import read, values, world_matrices


def body_group(name):
    if name.startswith(('L_', 'R_')):
        side = 0 if name.startswith('L_') else 1
        return (skeleton.ARM_L if any(k in name for k in ('arm_', 'elbow_', 'forearm_'))
                else skeleton.HAND_L) + side
    return skeleton._group_of(name)


class SurfaceMesh:
    """All primitives in an authored GLB, in scene space, including separate vertex buffers.

    Retail Mesh deliberately assumes one shared vertex buffer. Authored sidearms do not.
    Keeping this reader separate preserves retail bind-pose/skeleton semantics.
    """
    def __init__(self, path):
        doc, data = read(Path(path))
        worlds, _ = world_matrices(doc)
        positions, normals, uvs, groups = [], [], [], []
        self.tris, self.joints = {}, {}
        offset = 0
        for ni, node in enumerate(doc['nodes']):
            if 'mesh' not in node:
                continue
            world = worlds[ni]
            skin = doc.get('skins', [])[node['skin']] if 'skin' in node else None
            names = [doc['nodes'][j].get('name', '') for j in skin['joints']] if skin else []
            for pr in doc['meshes'][node['mesh']]['primitives']:
                if pr.get('mode', 4) != 4:
                    raise ValueError('Cosmetic surfaces must be triangles')
                mat = doc['materials'][pr['material']]
                ti = mat.get('pbrMetallicRoughness', {}).get('baseColorTexture', {}).get('index')
                if ti is None:
                    continue
                stem = Path(doc['images'][doc['textures'][ti]['source']]['uri']).stem
                a = pr['attributes']
                p = values(doc, data, a['POSITION'])
                n = values(doc, data, a['NORMAL']) @ np.linalg.inv(world[:3, :3])
                n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)
                positions.append(p @ world[:3, :3].T + world[:3, 3])
                normals.append(n)
                uvs.append(values(doc, data, a['TEXCOORD_0']))
                g = np.zeros((len(p), skeleton._GROUPS), np.float32)
                if skin and 'JOINTS_0' in a:
                    js = values(doc, data, a['JOINTS_0']).astype(int)
                    ws = values(doc, data, a['WEIGHTS_0'])
                    for k in range(js.shape[1]):
                        ids = np.array([body_group(names[j]) for j in js[:, k]])
                        np.add.at(g, (np.arange(len(p)), ids), ws[:, k])
                else:
                    g[:, skeleton.TORSO] = 1
                g /= np.maximum(g.sum(1, keepdims=True), 1e-12)
                groups.append(g)
                indices = values(doc, data, pr['indices']).reshape(-1, 3) if 'indices' in pr else np.arange(len(p)).reshape(-1, 3)
                self.tris.setdefault(stem, []).append(indices.astype(np.int64) + offset)
                offset += len(p)
        if not positions:
            raise ValueError(f'No textured surfaces in {path}')
        self.pos = np.concatenate(positions).astype(np.float32)
        self.nrm = np.concatenate(normals).astype(np.float32)
        self.uv = np.concatenate(uvs).astype(np.float32)
        self.groups = np.concatenate(groups)


ARMS = 'OpenScctSidearms_arms_arms_baseColor'

# Grip/trigger-frame boundaries for the retail meshes. Unlike separate-shell
# classification these also protect grips welded into the receiver's vertex shell.
_LOWER_FRAME = {'SWeaponTex_p190': .34, 'SWeaponTex_M160': .36,
                'SWeaponTex_Hk81': .35, 'SWeaponTex_HK_81_chargeur': .35,
                'SWeaponTex_shotgun': .24}


@lru_cache(maxsize=2)
def load_mesh(path, authored, stamp, size):
    """Bound cache to two models; stat keys invalidate edited authoring inputs."""
    return SurfaceMesh(path) if authored else skeleton.Mesh(Path(path))


def sidearm(stem):
    return stem.startswith('OpenScctSidearms_') and stem != ARMS


def part_regions(mesh, stem, h, w):
    layout = skeleton.bake(mesh, stem, h, w)
    if layout is None:raise ValueError(f'{stem} is absent from its mesh')
    return layout


def secondary_parts(mesh, layout):
    parts={group:layout['cover'] & (layout['group']==group) for group in
           (skeleton.HEAD,skeleton.HAND_L,skeleton.HAND_R,skeleton.FOOT_L,skeleton.FOOT_R)}
    for side,foot,leg in (('L',skeleton.FOOT_L,skeleton.LEG_L),('R',skeleton.FOOT_R,skeleton.LEG_R)):
        ankle=mesh.joints.get(f'B {side} Foot')
        knee=mesh.joints.get(f'B {side} Calf')
        if ankle is not None and knee is not None:
            # Boot shafts move with the calf, not the foot bone. Include them
            # explicitly, otherwise only toe caps receive the footwear finish.
            top=ankle[1]+.72*(knee[1]-ankle[1])
            parts[foot] |= layout['cover'] & (layout['group']==leg) & (layout['P'][...,1]<top)
    return parts


def coordinated_parts(colours, retail, mesh, stem, pattern, *, return_regions=False):
    """Fit the actual motif independently to headwear, footwear, gloves and hardware.

    Each region gets its own local frame/scale, preserving the base material detail.
    This is neither a solid-colour substitute nor a pattern drawn across a UV atlas.
    """
    from . import cosmetics, gunskin
    if pattern == 'skeleton':
        return (colours,np.zeros(retail.shape[:2],bool)) if return_regions else colours
    h,w=retail.shape[:2];layout=part_regions(mesh,stem,h,w)
    shape,palette=cosmetics.PATTERNS[pattern]
    out=colours.copy();p=layout['P'];cover=layout['cover']
    suit=stem.startswith('SPersoTextures_') or stem in (ARMS,'SWeaponTex_1DEF_01')
    regions=[]
    if suit:
        for group,region in secondary_parts(mesh,layout).items():
            if not region.any():continue
            points=p[region];lo=points.min(0);span=np.maximum(np.ptp(points,axis=0),1e-5)
            q=(p-lo)/span
            if group==skeleton.HEAD:
                # The spy's fabric head covering participates in the skin; merc
                # skin is a separate atlas, only included in the skeleton finish.
                stripe=(abs(q[...,0]-.5)<.10)&(q[...,1]>.66)
            elif group in (skeleton.FOOT_L,skeleton.FOOT_R):
                stripe=((q[...,1]>.18)&(q[...,1]<.27))|((q[...,2]>.7)&(layout['N'][...,1]>.35))
            else:
                stripe=(q[...,1]>.65)&(abs(q[...,0]-.5)<.32)
            regions.append((region,stripe,group))
    else:
        _,forward,up,sleeves=gunskin.GUN_MESH.get(stem,('',gunskin._X,gunskin._Y,False))
        gun=gunskin._fitted_gun(mesh,stem,h,w,forward,up,sleeves)
        region=cover & np.isin(gun.role,[gunskin.SMALL,gunskin.HANG])
        if sidearm(stem):region |= cover&(p[...,1]<-.025)&(p[...,0]<mesh.pos[:,0].min()+np.ptp(mesh.pos[:,0])*.40)
        if stem in _LOWER_FRAME:
            region |= cover&(p[...,1]<mesh.pos[:,1].min()+np.ptp(mesh.pos[:,1])*_LOWER_FRAME[stem])
        if region.any():
            yy=p[...,1];lo=yy[region].min();span=max(float(np.ptp(yy[region])),1e-5)
            stripe=((yy-lo)/span>.28)&((yy-lo)/span<.36)
            for piece in np.unique(gun.piece[region]):
                regions.append((region&(gun.piece==piece),stripe,None))
    touched=np.zeros((h,w),bool)
    lum=retail[...,:3].astype(np.float32) @ np.array([.3,.59,.11],np.float32)/255
    for region,stripe,group in regions:
        points=p[region];lo=points.min(0);span=np.maximum(np.ptp(points,axis=0),1e-5)
        q=(points-lo)/span
        normal=layout['N'][region]
        local=q[None,...]*.6
        inside=np.ones(local.shape[:2],bool)
        if shape=='galaxy':
            motif=gunskin.solid_galaxy(pattern,palette,local,inside)[0]
        elif shape=='inferno':
            distance=q[:,1]
            if group==skeleton.HAND_L:distance=q[:,0]
            if group==skeleton.HAND_R:distance=1-q[:,0]
            motif=gunskin.solid_inferno(pattern,palette,local,inside,distance[None,:]*.6,.6,
                                      np.full(inside.shape,group not in (skeleton.HAND_L,skeleton.HAND_R)))[0]
        else:
            tile=gunskin._tile(pattern,shape,tuple(palette))
            # The sides, front/back and top each sample a local surface plane.
            # Blend at corners, rather than stretching a face's drawing round it.
            u,v=q[:,0],1-q[:,1]
            if group in (skeleton.HAND_L,skeleton.HAND_R):u,v=q[:,2],q[:,0]
            views=(gunskin._sample(tile,u,v),gunskin._sample(tile,q[:,2],1-q[:,1]),
                   gunskin._sample(tile,q[:,0],q[:,2]))
            weights=abs(normal[:,[2,0,1]])**8
            weights/=np.maximum(weights.sum(1,keepdims=True),1e-6)
            motif=sum(view*weights[:,k:k+1] for k,view in enumerate(views))
            if shape in cosmetics._GILDED:
                motif=cosmetics._gold_ramp(lum[region],palette,motif[:,0],motif[:,1],
                    cosmetics.MASTERCRAFT_DEPTH if shape=='mastercraft' else 1)
        # Rubber/fabric keeps its own moulded/weave detail even when the main
        # finish is gold or lacquer (which intentionally uses different shading).
        detail=np.clip(.55+.45*(lum[region]+.015)/(float(lum[region].mean())+.015),.4,1.5)
        out[region]=motif*detail[:,None]*(.85+.15*stripe[region,None])
        touched |= region
    out=np.clip(out,0,1)
    return (out,touched) if return_regions else out


def refinish_parts(job):
    """Rebuild secondary regions in an existing staged bake without resampling its main motif."""
    from PIL import Image
    from . import cosmetics
    src,dst,pattern,stem,size,path=job
    stat=Path(path).stat();mesh=load_mesh(path,stem.startswith('OpenScctSidearms_'),stat.st_mtime_ns,stat.st_size)
    with Image.open(src) as image:
        retail=np.array(image.convert('RGBA').resize(size,Image.Resampling.LANCZOS))
    with Image.open(dst) as image:result=np.array(image.convert('RGBA'))
    if result.shape!=retail.shape:raise ValueError(f'Wrong size for secondary finish: {dst}')
    rgb=retail[...,:3].astype(np.float32)/255
    camo,touched=coordinated_parts(np.zeros_like(rgb),retail,mesh,stem,pattern,return_regions=True)
    shape=cosmetics.PATTERNS[pattern][0]
    strength=cosmetics.FINISH_STRENGTH if shape in cosmetics._FINISHES else cosmetics.CAMO_STRENGTH
    allowed=coverage(retail,mesh,stem,pattern)[...,None]
    composed=rgb+(rgb*(1-strength)+camo*strength-rgb)*allowed
    result[touched,:3]=np.clip(composed[touched]*255+.5,0,255).astype(np.uint8)
    Image.fromarray(result).save(dst,compress_level=6)
    return dst


def coverage(retail, mesh, stem, pattern, *, lighting=False):
    """Preserve functional materials; white coverage means the finish may replace colour.

    Semantic atlas exclusions use normalized coordinates from the original atlases,
    not screen-space rectangles. Geometry groups additionally protect gloves/boots.
    """
    h, w = retail.shape[:2]
    layout = skeleton.bake(mesh, stem, h, w)
    if layout is None:
        raise ValueError(f'{stem} is absent from its cosmetic mesh')
    mask = layout['cover'].astype(np.float32)
    if pattern == 'skeleton':
        return mask
    rgb = retail[..., :3].astype(np.float32) / 255
    lum = rgb @ np.array([.3, .59, .11])
    # Keep recessed seams and original hardware shading, rather than flooding black cavities.
    if stem == ARMS or sidearm(stem):
        # Authored PBR bases are much darker than the baked retail atlases. A
        # retail brightness threshold reduces their cloth/slide to coloured
        # speckles. Use geometry for material separation and keep only true
        # black cavities; paint() still retains their local shading.
        mask *= np.clip(lum / .004, 0, 1)
    else:
        mask *= np.clip((lum - .018) / .065, 0, 1)
    if sidearm(stem):
        p = layout['P']
        length = np.ptp(mesh.pos[:, 0])
        grip = (p[..., 1] < -.025) & (p[..., 0] < mesh.pos[:, 0].min() + length * .40)
        mask[grip & layout['cover']] = 0 if lighting else 1
    if stem in _LOWER_FRAME:
        y0, y1 = mesh.pos[:, 1].min(), mesh.pos[:, 1].max()
        lower=layout['cover'] & (layout['P'][..., 1] < y0 + (y1-y0)*_LOWER_FRAME[stem])
        mask[lower] = 0 if lighting else 1
    suit = stem.startswith('SPersoTextures_') or stem == ARMS or stem == 'SWeaponTex_1DEF_01'
    if suit:
        secondary = np.logical_or.reduce(list(secondary_parts(mesh,layout).values()))
        # These parts receive muted, purpose-designed accents; their rubber and
        # fabric do not receive metal sheen. Actual lights are preserved below.
        mask[secondary & layout['cover']] = 0 if lighting else 1
        # Emissive lenses, displays and identification marks retain their authored colours.
        mask[(rgb.max(2) - rgb.min(2) > .22) & (rgb.max(2) > .4)] = 0
        v, u = np.mgrid[:h, :w] / np.array([h, w])[:, None, None]
        exclusions = {
            'SPersoTextures_Shadw_agent': [(0.27, .49, 1, .64), (.32, .64, 1, 1)],
            'SPersoTextures_Shadw_agent_2': [(.78, 0, 1, 1)],
            'SPersoTextures_DEF_01_A_Body': [(.54, .15, 1, .44), (0, .43, .22, .8),
                                          (.55, .55, .68, .79), (.78, .56, 1, .81), (0, .93, 1, 1)],
        }
        for x0, y0, x1, y1 in exclusions.get(stem, []):
            mask[(u >= x0) & (u < x1) & (v >= y0) & (v < y1) & ~secondary] = 0
    else:
        from . import gunskin
        _, forward, up, sleeves = gunskin.GUN_MESH.get(stem, ('', gunskin._X, gunskin._Y, False))
        gun = gunskin._fitted_gun(mesh, stem, h, w, forward, up, sleeves)
        hardware=layout['cover'] & np.isin(gun.role, [gunskin.SMALL, gunskin.HANG])
        mask[hardware] = 0 if lighting else 1
        mask[gun.end & (gun.x > gun.x1 - .015 * gun.length)] = 0
    return mask
