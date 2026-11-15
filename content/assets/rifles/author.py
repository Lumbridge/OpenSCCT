"""Fit freely licensed realistic rifles to BarcodeGames' animated first-person rig.

Run with --sources pointing at the four Sketchfab GLB downloads in sources.json.
Only the external models enter export/. Converted SCCT contextual tracks are
retargeted locally by svmbuild.rifles during the user's content build.
"""
from copy import deepcopy
from pathlib import Path
import argparse
import io
import json
import math
import sys
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1]))
from svmbuild.sidearm_models import read, write, values, append, world_matrices
from svmbuild.pistol_arms import tracks, pose, curve, decompose, turn, slice_clip

OUT = ROOT / 'export'
AXIS = np.array([[0., 0, 1], [0, 1, 0], [-1, 0, 0]])
GRIP = np.array([-.057, 1.37, .065])
FILES = {'m4a1s': 'm4_-_fps_weapon_animations_pack_v.1.glb', 'ak47': 'ak47.glb',
         'galil': 'imi_galil_weapon.glb', 'famas': 'famas.glb'}


def geometry(doc, data, world, node, primitive, raw=False):
    """Bake one mesh into source world space, respecting normalized skin weights."""
    attributes = primitive['attributes']
    xyz = values(doc, data, attributes['POSITION'])
    normals = values(doc, data, attributes['NORMAL'])
    n = doc['nodes'][node]
    if not raw and 'skin' in n and 'JOINTS_0' in attributes:
        skin = doc['skins'][n['skin']]
        binds = values(doc, data, skin['inverseBindMatrices']).reshape(-1, 4, 4).transpose(0, 2, 1)
        matrices = np.array([world[j] @ bind for j, bind in zip(skin['joints'], binds)])
        joints = values(doc, data, attributes['JOINTS_0'])
        weights = values(doc, data, attributes['WEIGHTS_0']).astype(float)
        weights /= np.maximum(1e-12, weights.sum(axis=1, keepdims=True))
        output = np.zeros_like(xyz); normal_output = np.zeros_like(normals)
        for column in range(4):
            m = matrices[joints[:, column]]
            output += ((m[:, :3, :3] @ xyz[:, :, None])[:, :, 0] + m[:, :3, 3]) * weights[:, column, None]
            normal_output += (np.linalg.inv(m[:, :3, :3]).transpose(0, 2, 1) @ normals[:, :, None])[:, :, 0] * weights[:, column, None]
        xyz, normals = output, normal_output
    elif not raw:
        m = world[node]
        xyz = (m[:3, :3] @ xyz.T).T + m[:3, 3]
        normals = (np.linalg.inv(m[:3, :3]).T @ normals.T).T
    normals /= np.maximum(1e-12, np.linalg.norm(normals, axis=1, keepdims=True))
    return xyz, normals


def material_bank(doc, data, prefix):
    """External PNGs are required by the game's faithful material loader."""
    images = []
    for i, image in enumerate(doc.get('images', [])):
        if 'bufferView' in image:
            view = doc['bufferViews'][image['bufferView']];offset = view.get('byteOffset', 0)
            image = Image.open(io.BytesIO(data[offset:offset + view['byteLength']])).convert('RGBA')
        else:
            raise ValueError('Source must be a self-contained GLB')
        # Galil has seven material sets; 1K per part avoids a 300+ MB GPU texture
        # footprint for one rifle. Other sources retain their single/three-set 2K art.
        limit=1024 if prefix=='galil' else 2048
        image.thumbnail((limit, limit), Image.Resampling.LANCZOS)
        path = f'textures/{prefix}_{i}.png';image.save(OUT / path, optimize=True)
        images.append({'uri': path})
    materials = deepcopy(doc.get('materials', []))
    for i, material in enumerate(materials):
        material['name'] = f'OpenScctRifles.{prefix}_{i}'
        # Sketchfab's older specular/glossiness workflow is not consumed by
        # the faithful renderer. Convert its diffuse image and factor explicitly.
        legacy = material.pop('extensions', {}).get('KHR_materials_pbrSpecularGlossiness')
        if legacy:
            material['pbrMetallicRoughness'] = {
                'baseColorTexture': legacy.get('diffuseTexture', {}),
                'baseColorFactor': legacy.get('diffuseFactor', [1,1,1,1]),
                'metallicFactor': 0, 'roughnessFactor': .85}
        # Base colour factors are baked: the faithful renderer reads the image.
        pbr = material.get('pbrMetallicRoughness', {})
        # The downloaded FAMAS marks its polymer grip/stock as almost fully
        # metallic. Restore matte polymer and restrained phosphate steel.
        if prefix=='famas':pbr.update(metallicFactor=.6 if i==2 else .18,roughnessFactor=.9)
        elif prefix=='galil':pbr.update(metallicFactor=.45,roughnessFactor=.9)
        factor = np.array(pbr.pop('baseColorFactor', [1, 1, 1, 1]))
        if 'baseColorTexture' in pbr and np.any(factor != 1):
            source = doc['textures'][pbr['baseColorTexture']['index']]['source']
            image_path = OUT / images[source]['uri']
            pixels = np.asarray(Image.open(image_path).convert('RGBA')).astype(float)
            Image.fromarray(np.clip(pixels * factor, 0, 255).astype('uint8')).save(image_path, optimize=True)
        if 'metallicRoughnessTexture' in pbr:
            source = doc['textures'][pbr['metallicRoughnessTexture']['index']]['source']
            pixels = np.asarray(Image.open(OUT / images[source]['uri']).convert('RGB')) / 255.
            mask = (.1 + .9 * pixels[:, :, 2]*pbr.get('metallicFactor',1)) * (1 - .8 * pixels[:, :, 1])
            path = f'textures/{prefix}_{i}_spec.png'
            spec = np.full((*mask.shape,4),255,dtype='uint8');spec[:,:,3] = np.rint(mask*255).astype('uint8')
            # The game's reflection mask is the ALPHA channel, not luminance.
            Image.fromarray(spec).save(OUT / path, optimize=True)
            material['extras'] = {'scct_spec_mask': path, 'scct_env_color': [.006, .007, .008]}
    textures = deepcopy(doc.get('textures', []))
    for texture in textures: texture.pop('sampler', None)
    return materials, textures, images


def add_materials(target, bank):
    materials, textures, images = deepcopy(bank)
    texture_offset = len(target['textures']);image_offset = len(target['images']);offset = len(target['materials'])
    def remap(value):
        if not isinstance(value, dict): return
        for key, child in value.items():
            if key.endswith('Texture') and isinstance(child, dict) and 'index' in child: child['index'] += texture_offset
            else: remap(child)
    for material in materials: remap(material)
    for texture in textures: texture['source'] += image_offset
    target['materials'].extend(materials);target['textures'].extend(textures);target['images'].extend(images)
    return offset


def gun_parts(name, doc, data, ready):
    """Return neutral +X-forward parts, with their moving joint and triangle subset."""
    parts = []
    if name == 'm4a1s':
        transform = np.eye(4);transform[:3, :3] = AXIS;transform[:3, 3] = -AXIS @ GRIP
    elif name == 'ak47':
        transform = np.eye(4);transform[:3, :3] = np.array([[0, 0, -1], [0, 1, 0], [1, 0, 0]]) * .093
        transform[:3, 3] = -transform[:3, :3] @ np.array([0, -.15, 1.9])
    elif name == 'galil':
        transform = np.diag([.103, .103, .103, 1]);transform[:3, 3] = [.1854, -.103, 0]
    else:
        transform = np.eye(4);transform[:3, :3] = AXIS * .166;transform[:3, 3] = -transform[:3, :3] @ np.array([0, .45, 3.95])
    for i, node in enumerate(doc['nodes']):
        if 'mesh' not in node: continue
        if name == 'm4a1s' and i not in [63, 73, 76, 79, 81, 83]: continue
        for primitive in doc['meshes'][node['mesh']]['primitives']:
            xyz, normals = geometry(doc, data, ready, i, primitive, raw=name == 'ak47')
            xyz = (transform[:3, :3] @ xyz.T).T + transform[:3, 3]
            normals = (np.linalg.inv(transform[:3, :3]).T @ normals.T).T
            normals /= np.maximum(1e-12, np.linalg.norm(normals, axis=1, keepdims=True))
            uv = values(doc, data, primitive['attributes']['TEXCOORD_0'])
            triangles = values(doc, data, primitive['indices']).reshape(-1, 3)
            groups = np.full(len(xyz), 60)
            if name == 'm4a1s': groups[:] = {63:61, 73:71, 76:74, 79:77}.get(i, 60)
            elif name == 'galil' and 'magazine' in node.get('name', ''): groups[:] = 61
            elif name == 'famas' and i == 6: groups[:] = 61
            elif name == 'ak47':
                joints = values(doc, data, primitive['attributes']['JOINTS_0'])
                weights = values(doc, data, primitive['attributes']['WEIGHTS_0'])
                dominant = joints[np.arange(len(joints)), weights.argmax(axis=1)]
                groups[:] = np.array([{1:60, 2:-1, 4:61, 6:71, 8:-1, 10:-1}.get(int(j), 60) for j in dominant])
            for joint in np.unique(groups):
                if joint < 0: continue
                selected = triangles[np.all(groups[triangles] == joint, axis=1)]
                if not len(selected): continue
                used, indices = np.unique(selected, return_inverse=True)
                parts.append((int(joint), primitive.get('material', 0), xyz[used], normals[used], uv[used], indices.reshape(-1, 1)))
    return parts


def suppressor(material):
    # Original steel suppressor: closed annular end cap with a genuine bore,
    # shallow retention rings, and 48 sides to keep the first-person silhouette round.
    profile = [(.550,.013),(.566,.019),(.572,.020),(.586,.020),(.588,.021),(.591,.021),
               (.594,.020),(.694,.020),(.697,.021),(.700,.021),(.703,.020),(.728,.020),(.732,.018),(.732,.0045),(.718,.0045)]
    points=[];normals=[];uv=[];indices=[]
    for k,(x,radius) in enumerate(profile):
        for i in range(49):
            angle=i*2*math.pi/48;points.append([x,.096+radius*math.cos(angle),radius*math.sin(angle)])
            normals.append([0,math.cos(angle),math.sin(angle)]);uv.append([i/48,k/(len(profile)-1)])
    for k in range(len(profile)-1):
        for i in range(48):
            a=k*49+i;b=a+49;indices.extend([[a,b,a+1],[a+1,b,b+1]])
    return (60,material,np.array(points),np.array(normals),np.array(uv),np.array(indices).reshape(-1,1))


def primitive(doc,data,part,transform=None,joint=None):
    _,material,xyz,normals,uv,indices=part
    if transform is not None:
        xyz=(transform[:3,:3]@xyz.T).T+transform[:3,3]
        normals=(np.linalg.inv(transform[:3,:3]).T@normals.T).T
        normals/=np.maximum(1e-12,np.linalg.norm(normals,axis=1,keepdims=True))
    attributes={'POSITION':append(doc,data,xyz,'VEC3',target=34962),
        'NORMAL':append(doc,data,normals,'VEC3',target=34962),'TEXCOORD_0':append(doc,data,uv,'VEC2',target=34962)}
    if joint is not None:
        attributes['JOINTS_0']=append(doc,data,np.tile([joint,0,0,0],(len(xyz),1)),'VEC4',5123,34962)
        attributes['WEIGHTS_0']=append(doc,data,np.tile([1,0,0,0],(len(xyz),1)),'VEC4',target=34962)
    return {'attributes':attributes,'indices':append(doc,data,indices,'SCALAR',5125,34963),'material':material}


def compact(doc,data):
    """Discard orphaned source buffers, cameras, images and removed weapon meshes."""
    out=deepcopy(doc);blob=bytearray();out['accessors']=[];out['bufferViews']=[]
    mapping={}
    def accessor(index):
        if index not in mapping:
            a=doc['accessors'][index]
            mapping[index]=append(out,blob,values(doc,data,index),a['type'],a['componentType'])
            if a.get('normalized'):out['accessors'][mapping[index]]['normalized']=True
        return mapping[index]
    meshmap={};out['meshes']=[]
    for node in out['nodes']:
        node.pop('camera',None)
        if 'mesh' not in node:continue
        index=node['mesh']
        if index not in meshmap:
            mesh=deepcopy(doc['meshes'][index])
            for part in mesh['primitives']:
                part['attributes']={key:accessor(a) for key,a in part['attributes'].items()}
                part['indices']=accessor(part['indices'])
            meshmap[index]=len(out['meshes']);out['meshes'].append(mesh)
        node['mesh']=meshmap[index]
    for skin in out.get('skins',[]):skin['inverseBindMatrices']=accessor(skin['inverseBindMatrices'])
    for animation in out.get('animations',[]):
        for sampler in animation['samplers']:
            sampler['input']=accessor(sampler['input']);sampler['output']=accessor(sampler['output'])
    used=sorted({p['material'] for m in out['meshes'] for p in m['primitives']});materials=out['materials']
    out['materials']=[materials[i] for i in used]
    for mesh in out['meshes']:
        for part in mesh['primitives']:part['material']=used.index(part['material'])
    used_textures=set()
    def visit(value,remap=None):
        if not isinstance(value,dict):return
        for key,child in value.items():
            if key.endswith('Texture') and isinstance(child,dict) and 'index' in child:
                if remap is None:used_textures.add(child['index'])
                else:child['index']=remap.index(child['index'])
            else:visit(child,remap)
    for material in out['materials']:visit(material)
    used_textures=sorted(used_textures);out['textures']=[out['textures'][i] for i in used_textures]
    for material in out['materials']:visit(material,used_textures)
    used_images=sorted({t['source'] for t in out['textures']});out['images']=[out['images'][i] for i in used_images]
    for texture in out['textures']:texture['source']=used_images.index(texture['source'])
    out.pop('cameras',None);out.pop('extensionsUsed',None);out.pop('extensionsRequired',None)
    return out,blob


def fit_support(doc,data,name,idle,parents):
    """Two-bone IK keeps the support hand on each foregrip and its relocated magazine."""
    # Neutral rifle frame -> M4 source world.
    placement=np.eye(4);placement[:3,:3]=AXIS.T;placement[:3,3]=GRIP
    shift={'m4a1s':[0,0,0],'ak47':[-.035,-.025,0],'galil':[.025,-.01,0],'famas':[-.055,-.025,0]}[name]
    magazine={'m4a1s':[0,0,0],'ak47':[-.055,-.015,0],'galil':[-.005,-.025,0],'famas':[-.215,-.015,0]}[name]
    for animation in doc['animations']:
        source=tracks(doc,data,animation);duration=max(float(t[-1]) for _,_,t,_ in source)
        reload=animation['name']=='FirstReloStFd2';times=np.linspace(0,duration,max(2,round(duration*30)+1));frames={n:[] for n in [37,38,39]}
        for time in times:
            world=pose(doc,source,time);fraction=time/duration
            handling=min(np.clip((fraction-.06)/.08,0,1),np.clip((.92-fraction)/.12,0,1)) if reload else 0
            offset=placement[:3,:3]@((1-handling)*np.array(shift)+handling*np.array(magazine))
            # Keep the offset in the moving gun frame, including draw and reload tilt.
            rigid=world[60][:3,:3]@np.linalg.inv(idle[60][:3,:3]);target=world[39][:3,3]+rigid@offset
            shoulder=world[37][:3,3];elbow=world[38][:3,3];wrist=world[39][:3,3]
            a=np.linalg.norm(elbow-shoulder);b=np.linalg.norm(wrist-elbow);direction=target-shoulder
            length=np.clip(np.linalg.norm(direction),abs(a-b)+1e-6,a+b-1e-6);direction/=max(1e-10,np.linalg.norm(direction));target=shoulder+direction*length
            pole=elbow-shoulder;pole-=direction*(pole@direction);pole/=max(1e-10,np.linalg.norm(pole));along=(a*a-b*b+length*length)/(2*length)
            new_elbow=shoulder+direction*along+pole*math.sqrt(max(0,a*a-along*along))
            desired={n:world[n].copy() for n in frames};desired[37][:3,:3]=turn(elbow-shoulder,new_elbow-shoulder)@world[37][:3,:3]
            desired[38][:3,:3]=turn(wrist-elbow,target-new_elbow)@world[38][:3,:3];desired[38][:3,3]=new_elbow;desired[39][:3,3]=target
            for n in frames:frames[n].append(decompose(np.linalg.inv(desired.get(parents[n],world[parents[n]]))@desired[n]))
        animation['channels']=[c for c in animation['channels'] if c['target']['node'] not in frames]
        for n,poses in frames.items():
            for k,path in enumerate(['translation','rotation','scale']):curve(doc,data,animation,n,path,times,[p[k] for p in poses])


def build(name,source,rig,rig_data,bank):
    own,own_data=source
    ready=pose(own,tracks(own,own_data,next(a for a in own['animations'] if a['name']=='Draw')),10) if name=='m4a1s' else world_matrices(own)[0]
    parts=gun_parts(name,own,own_data,ready)
    if name=='m4a1s':
        suppressor_material=len(bank[0]);bank=deepcopy(bank)
        image=Image.new('RGB',(128,128));pixels=np.asarray(image).copy();rng=np.random.default_rng(7);noise=rng.integers(-3,4,(128,128,1));pixels[:]=np.clip(np.array([32,35,38])+noise,0,255);Image.fromarray(pixels).save(OUT/'textures/suppressor.png')
        bank[2].append({'uri':'textures/suppressor.png'});bank[1].append({'source':len(bank[2])-1})
        bank[0].append({'name':'OpenScctRifles.Suppressor','pbrMetallicRoughness':{'baseColorTexture':{'index':len(bank[1])-1},'metallicFactor':.85,'roughnessFactor':.48}})
        parts.append(suppressor(suppressor_material))
    # World weapon: gun-only skin, hand attachment origin at the pistol grip.
    world={'asset':{'version':'2.0'},'scene':0,'scenes':[{'nodes':[0,3]}],
        'nodes':[{'name':'corps','children':[1,2]},{'name':'FX','translation':[max(p[2][:,0].max() for p in parts),.096,0]},
                 {'name':'FX_DOUILLE','translation':[.10,.09,.025]},{'name':name,'mesh':0,'skin':0}],
        'skins':[{'joints':[0,1,2],'skeleton':0}], 'meshes':[], 'bufferViews':[], 'accessors':[], 'images':[], 'textures':[], 'materials':[],
        'extras':{'svmap':{'mesh':'OpenScctRifles.'+name},'license':'CC-BY-4.0; see CREDITS.txt'}}
    blob=bytearray();add_materials(world,bank)
    world['skins'][0]['inverseBindMatrices']=append(world,blob,[np.eye(4).T.reshape(-1),np.eye(4).T.reshape(-1),np.eye(4).T.reshape(-1)],'MAT4')
    world['meshes']=[{'name':name,'primitives':[primitive(world,blob,p,joint=0) for p in parts]}]
    world['animations']=[{'name':'idle','samplers':[],'channels':[]}];curve(world,blob,world['animations'][0],0,'translation',[0,1],[[0,0,0],[0,0,0]])
    world,blob=compact(world,blob);write(OUT/(name+'.glb'),world,blob)
    # Preserve the original authored hands and all their skin weights.
    doc=deepcopy(rig);data=bytearray(rig_data);base_bank=material_bank(rig,rig_data,'hands')
    doc['materials'],doc['textures'],doc['images']=base_bank
    for i,node in enumerate(doc['nodes']):
        if 'mesh' in node and i not in [7,8]:node.pop('mesh');node.pop('skin',None)
    doc['materials'][0]['name']='OpenScctRifles.Arms';doc['materials'][1]['name']='OpenScctRifles.Gloves'
    offset=add_materials(doc,bank)
    idle=pose(doc,tracks(doc,data,next(a for a in doc['animations'] if a['name']=='Draw')),10)
    rest,parents=world_matrices(doc);mesh_world=rest[7]
    # The source rigid gun parts were not skinned. Their unused skin entries
    # contain identity IBMs; establish actual mesh-space binds before replacement.
    binds=values(doc,data,doc['skins'][0]['inverseBindMatrices']).copy()
    for joint in [60,61,66,71,74,77]:
        binds[doc['skins'][0]['joints'].index(joint)]=(np.linalg.inv(rest[joint])@mesh_world).T.reshape(-1)
    doc['skins'][0]['inverseBindMatrices']=append(doc,data,binds,'MAT4')
    placement=np.eye(4);placement[:3,:3]=AXIS.T;placement[:3,3]=GRIP
    primitives=[]
    for part in parts:
        joint=part[0];p=list(part);p[1]+=offset
        transform=np.linalg.inv(mesh_world)@rest[joint]@np.linalg.inv(idle[joint])@placement
        primitives.append(primitive(doc,data,p,transform,doc['skins'][0]['joints'].index(joint)))
        if joint==61:
            # A separate replacement magazine follows the original spare-magazine joint.
            transform=np.linalg.inv(mesh_world)@rest[66]@np.linalg.inv(idle[61])@placement
            primitives.append(primitive(doc,data,p,transform,doc['skins'][0]['joints'].index(66)))
    doc['meshes'].append({'name':name+'_parts','primitives':primitives});node=len(doc['nodes'])
    doc['nodes'].append({'name':name+'_parts','mesh':len(doc['meshes'])-1,'skin':0});doc['nodes'][parents[7]].setdefault('children',[]).append(node)
    sources={a['name']:tracks(doc,data,a) for a in doc['animations']}
    doc['animations']=[slice_clip(doc,data,sources[source],alias,start,end) for source,alias,start,end in
        [('Draw','FirstDrawStEd2',269,300),('Holster','FirstHolsterStFd2',251,268),('Fire','FirstShooStFd2',226,250),('Reload','FirstReloStFd2',0,110),('Draw','FirstWaitStFd2',300,330)]]
    fit_support(doc,data,name,idle,parents)
    # FX axes point down the bore (+X in game space), independently of the rig's grip rotation.
    skin=doc['skins'][0];binds=values(doc,data,skin['inverseBindMatrices']).tolist()
    for label,position in [('FX',[max(p[2][:,0].max() for p in parts),.096,0]),('FX_DOUILLE',[.10,.09,.025])]:
        desired=placement.copy();desired[:3,:3]=placement[:3,:3]@np.eye(3);desired[:3,3]=(placement@np.r_[position,1])[:3]
        local=np.linalg.inv(idle[60])@desired;t,q,s=decompose(local);n=len(doc['nodes']);doc['nodes'].append({'name':label,'translation':t.tolist(),'rotation':q.tolist(),'scale':s.tolist()});doc['nodes'][60].setdefault('children',[]).append(n);skin['joints'].append(n)
        binds.append((np.linalg.inv(rest[60]@local)@mesh_world).T.reshape(-1).tolist())
    skin['inverseBindMatrices']=append(doc,data,binds,'MAT4')
    # The original camera is eye-height; offset the rifle down/right for hip fire.
    camera=np.eye(4);camera[:3,:3]=AXIS;camera[:3,3]=-AXIS@np.array([.0109,1.5393,-.1006])+np.array([0,-.06,.07])
    roots=doc['scenes'][doc.get('scene',0)]['nodes'];frame=len(doc['nodes']);doc['nodes'].append({'name':'MercRifleFrame','matrix':camera.T.reshape(-1).tolist(),'children':roots});doc['scenes'][doc.get('scene',0)]['nodes']=[frame]
    doc['extras']={'svmap':{'mesh':'OpenScctRifles.'+name+'_first'},'license':'CC-BY-4.0; see CREDITS.txt'}
    from svmbuild.rifle_arms import assemble
    doc,data=assemble(doc,data,OUT)
    doc,data=compact(doc,data);write(OUT/(name+'_first.glb'),doc,data)
    return {'triangles':sum(len(p[5])//3 for p in parts),'muzzle':max(p[2][:,0].max() for p in parts)}


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--sources',type=Path,required=True);args=parser.parse_args()
    (OUT/'textures').mkdir(parents=True,exist_ok=True)
    rig,rig_data=read(args.sources/FILES['m4a1s']);stats={}
    for name,file in FILES.items():
        source=read(args.sources/file);bank=material_bank(*source,name);stats[name]=build(name,source,rig,rig_data,bank)
    used=set()
    for path in OUT.glob('*.glb'):
        doc,_=read(path);used.update(i['uri'] for i in doc['images'])
        for material in doc['materials']:used.update(v for v in material.get('extras',{}).values() if isinstance(v,str) and v.startswith('textures/'))
    for path in (OUT/'textures').glob('*.png'):
        if path.relative_to(OUT).as_posix() not in used and not path.name.endswith('_icon.png'):path.unlink()
    import shutil
    shutil.copy2(ROOT/'CREDITS.txt',OUT/'CREDITS.txt')
    (OUT/'models.json').write_text(json.dumps(stats,indent=2)+'\n')
    print(json.dumps(stats))


if __name__=='__main__':main()
