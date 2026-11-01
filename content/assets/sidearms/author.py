"""Adapt the credited CC-BY pistol sources to OpenSCCT's neutral weapon rig.

Run: blender -b --python content/assets/sidearms/author.py -- --repo <checkout>
Original glTFs and license files remain in sources/. No converted retail arms are release inputs.
"""
from pathlib import Path
import argparse
import json
import math
import os
import shutil
import struct
import sys
import bpy
from mathutils import Matrix, Vector

parser=argparse.ArgumentParser()
parser.add_argument('--repo',required=True)
parser.add_argument('--only',help='Author a single pistol variant.')
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
root=Path(args.repo).resolve()/'content/assets/sidearms'
out=root/'export';out.mkdir(exist_ok=True);(out/'textures').mkdir(exist_ok=True)
(out/'CREDITS.txt').write_text((root/'CREDITS.txt').read_text(encoding='utf-8'),encoding='utf-8')

# Sources use different units and face directions. Result: Blender +X forward, +Z up, metres.
# Grip centre is (.018, 0, -.054), shared with the merc-arm assembly.
ADAPT={
    'suppressed':(.01,1,(.088,0,-.029)),
    'automatic':(1,1,(.074,0,-.028)),
    'magnum44':(.001,-1,(.158,0,-.029)),
    'desert_eagle':(.01,1,(.128,0,-.214)),
    'unsuppressed':(.01,1,(.088,0,-.029)),
}


def mechanism(kind,obj):
    name=obj.name.lower()
    if kind=='magnum44':
        if name.startswith('bullet_lp'):
            return 'round'+str(int(name.split('.')[1].split('_')[0]) if '.' in name else 0)
        return 'pompe' if any(t in name for t in ['cylinder','extractor','carane']) else 'corps'
    if kind=='desert_eagle':
        if any(t in name for t in ['magazine','baseinside']):return 'chargeur'
        if any(t in name for t in ['slide_low','safety_low','bolt','rearsight','slidepart','sideswitch']):return 'pompe'
    return 'corps'


def group_bone(kind,name):
    name=name.lower()
    if 'slide_' in name:return 'pompe'
    if any(t in name for t in ['magazine','follower','bulletsmag','g18_mag_']):return 'chargeur'
    return 'corps'


def solid_material(name,color,metal=.0,rough=.4):
    material=bpy.data.materials.new(name);material.use_nodes=True
    bsdf=material.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value=(*color,1);bsdf.inputs['Metallic'].default_value=metal;bsdf.inputs['Roughness'].default_value=rough
    image=bpy.data.images.new(name.replace('.','_'),width=4,height=4);image.pixels=list((*color,1))*16
    tex=material.node_tree.nodes.new('ShaderNodeTexImage');tex.image=image
    material.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
    return material


def cylinder(name,at,radius,length,mat,bone='corps',vertices=48):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices,radius=radius,depth=length,location=at,rotation=(0,math.pi/2,0))
    obj=bpy.context.object;obj.name=name;obj['mechanism']=bone;obj.data.materials.append(mat)
    bevel=obj.modifiers.new('Machined edges','BEVEL');bevel.width=.0007;bevel.segments=3
    bpy.context.view_layer.objects.active=obj;bpy.ops.object.modifier_apply(modifier=bevel.name)
    return obj


def accessories(kind):
    if kind=='suppressed':
        metal=solid_material('OpenScctSidearms.suppressor',(.055,.06,.065),.75,.3)
        parts=[cylinder('Suppressor body',(.278,0,.023),.018,.15,metal)]
        for x in [.209,.216,.337,.344]:parts.append(cylinder('Suppressor collar',(x,0,.023),.0185,.003,metal))
        bpy.ops.mesh.primitive_torus_add(major_radius=.0105,minor_radius=.0065,major_segments=48,minor_segments=12,location=(.354,0,.023),rotation=(0,math.pi/2,0))
        cap=bpy.context.object;cap.name='Suppressor muzzle';cap['mechanism']='corps';cap.data.materials.append(metal);parts.append(cap)
        return parts
    if kind=='magnum44':
        metal=solid_material('OpenScctSidearms.loader',(.08,.085,.09),.5,.32)
        # The six textured cartridges from the source gun move with this loader in the
        # first-person assembly. Do not manufacture a second set of cylindrical placeholders.
        return [cylinder('Speedloader plate',(.070,0,.0114934),.022,.012,metal,'chargeur'),
                cylinder('Speedloader knob',(.057,0,.0114934),.011,.014,metal,'chargeur',32)]
    return []


def rig_mesh(obj,rig,bones):
    bpy.context.view_layer.update()
    obj.data.transform(obj.matrix_world)
    obj.parent=rig;obj.matrix_parent_inverse=Matrix.Identity(4);obj.matrix_basis=Matrix.Identity(4);obj.vertex_groups.clear()
    groups={name:obj.vertex_groups.new(name=name) for name in dict.fromkeys(['corps','pompe','chargeur',*bones])}
    indices={name:[] for name in groups}
    for i,bone in enumerate(bones):indices[bone].append(i)
    for name,vertices in indices.items():
        if vertices:groups[name].add(vertices,1,'REPLACE')
    modifier=obj.modifiers.new('OpenSCCT weapon rig','ARMATURE');modifier.object=rig


def export(path,parts,rig,kind):
    bpy.ops.object.select_all(action='DESELECT');copies=[]
    for part in parts:
        clone=part.copy();clone.data=part.data.copy();bpy.context.collection.objects.link(clone);copies.append(clone);clone.select_set(True)
    bpy.context.view_layer.objects.active=copies[0];bpy.ops.object.join();joined=bpy.context.object;rig.select_set(True)
    separate=path.with_suffix('.gltf')
    bpy.ops.export_scene.gltf(filepath=str(separate),export_format='GLTF_SEPARATE',export_texture_dir='textures',use_selection=True,
        export_animations=False,export_def_bones=False,export_all_influences=False)
    doc=json.loads(separate.read_text());binary=(path.parent/doc['buffers'][0]['uri']).read_bytes();doc['buffers']=[{'byteLength':len(binary)}]
    doc['extras']={'svmap':{'mesh':'OpenScctSidearms.'+kind},'license':'CC-BY-4.0; see CREDITS.txt'}
    encoded=json.dumps(doc,sort_keys=True,separators=(',',':')).encode();encoded+=b' '*(-len(encoded)%4);binary+=b'\0'*(-len(binary)%4)
    path.write_bytes(struct.pack('<III',0x46546c67,2,28+len(encoded)+len(binary))+struct.pack('<II',len(encoded),0x4e4f534a)+encoded+struct.pack('<II',len(binary),0x004e4942)+binary)
    separate.with_suffix('.bin').unlink();separate.unlink();bpy.data.objects.remove(joined,do_unlink=True)


for kind,(scale,direction,offset) in ADAPT.items():
    if args.only and kind!=args.only:continue
    bpy.ops.wm.read_factory_settings(use_empty=True);bpy.context.preferences.filepaths.save_version=0
    source_kind='suppressed' if kind=='unsuppressed' else kind
    bpy.ops.import_scene.gltf(filepath=str(root/'sources'/source_kind/'scene.gltf'))
    bpy.context.view_layer.update()
    graph=bpy.context.evaluated_depsgraph_get()
    parts=[]
    transform=Matrix(((0,-direction*scale,0,offset[0]),(direction*scale,0,0,offset[1]),(0,0,scale,offset[2]),(0,0,0,1)))
    for obj in list(bpy.context.scene.objects):
        if obj.type!='MESH' or not obj.data.materials:continue
        if kind=='desert_eagle' and obj.name.startswith(('BulletCase','Bullet_low')):continue
        if kind=='automatic' and obj.name=='Object_16':continue
        names=[group.name for group in obj.vertex_groups];weights=[]
        for vertex in obj.data.vertices:
            best=max(vertex.groups,key=lambda group:group.weight,default=None)
            weights.append(group_bone(kind,names[best.group]) if best else mechanism(kind,obj))
        evaluated=obj.evaluated_get(graph)
        mesh=bpy.data.meshes.new_from_object(evaluated,preserve_all_data_layers=True,depsgraph=graph)
        mesh.transform(transform@evaluated.matrix_world)
        obj.data=mesh;obj.animation_data_clear();obj.parent=None;obj.matrix_parent_inverse=Matrix.Identity(4);obj.matrix_world=Matrix.Identity(4);obj.modifiers.clear()
        obj['weights']=weights;parts.append(obj)
    for obj in list(bpy.context.scene.objects):
        if obj not in parts:bpy.data.objects.remove(obj,do_unlink=True)
    for material in {m for obj in parts for m in obj.data.materials}:material.name='OpenScctSidearms.'+kind+'_'+material.name
    used_images={n.image for material in bpy.data.materials if material.use_nodes for n in material.node_tree.nodes if n.type=='TEX_IMAGE' and n.image}
    for image in used_images:
        if image.packed_file:image.unpack(method='REMOVE')
        if max(image.size)>2048:
            width,height=image.size;ratio=2048/max(width,height);image.scale(round(width*ratio),round(height*ratio))
        image.name='OpenScctSidearms_'+kind+'_'+Path(image.name).stem
        image.filepath_raw=str(out/'textures'/(image.name+'.png'));image.file_format='PNG';image.save()
        image.pack()
    loader=accessories(kind);parts+=loader
    bpy.ops.object.armature_add();rig=bpy.context.object;rig.name='SidearmRig'
    bpy.ops.object.mode_set(mode='EDIT');bone=rig.data.edit_bones[0];bone.name='corps';bone.head=(0,0,0);bone.tail=(.1,0,0)
    for name,point in [('pompe',(.1,0,.02)),('chargeur',(.018,0,-.054)),('FX',(.36 if kind=='suppressed' else .203 if kind=='unsuppressed' else .338 if kind=='magnum44' else .24 if kind=='desert_eagle' else .176,0,.023)),('FX_DOUILLE',(.08,-.02,.03))]:
        child=rig.data.edit_bones.new(name);child.head=point;child.tail=Vector(point)+Vector((.02,0,0));child.parent=bone
    if kind=='magnum44':
        for i in range(6):
            child=rig.data.edit_bones.new('round'+str(i));child.head=(.07822,0,.0114934)
            child.tail=(.1191,0,.0114934);child.parent=rig.data.edit_bones['pompe']
    bpy.ops.object.mode_set(mode='OBJECT')
    for obj in parts:rig_mesh(obj,rig,obj.get('weights',[obj.get('mechanism','corps')]*len(obj.data.vertices)))
    for obj in loader:obj.hide_render=kind=='magnum44'
    target=Vector((.12,0,-.015));bpy.ops.object.camera_add(location=(.43,-.62,.30))
    camera=bpy.context.object;camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO';camera.data.ortho_scale=.47
    scene=bpy.context.scene;scene.camera=camera;scene.render.engine='CYCLES';scene.cycles.samples=32
    scene.render.resolution_x=768;scene.render.resolution_y=576;scene.render.resolution_percentage=100
    scene.world=bpy.data.worlds.new('Studio');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs['Color'].default_value=(.22,.22,.22,1)
    for at,power in [((.15,-.3,.5),50),((-.2,.2,.3),35)]:
        bpy.ops.object.light_add(type='AREA',location=at);light=bpy.context.object;light.data.energy=power;light.data.size=.45
        light.rotation_euler=(target-light.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(root/(kind+'-preview.png'));bpy.ops.render.render(write_still=True)
    scene.render.resolution_x=256;scene.render.resolution_y=256;scene.render.film_transparent=True
    scene.render.filepath=str(out/'textures'/(kind+'_icon.png'));bpy.ops.render.render(write_still=True)
    bpy.ops.file.pack_all()
    temporary=root/(kind+'-'+str(os.getpid())+'.blend')
    bpy.ops.wm.save_as_mainfile(filepath=str(temporary),compress=True)
    temporary.replace(root/(kind+'.blend'))
    if kind=='magnum44':export(out/'magnum44_loader.glb',loader,rig,kind)
    export(out/(kind+'.glb'),[obj for obj in parts if kind!='magnum44' or obj not in loader],rig,kind)
print('Adapted credited CC-BY pistols:',out)

if args.only:sys.exit(0)

# Preserve the authored, fully finger-rigged arms and their animation source independently of the retail skeleton.
arms_source=root/'sources'/'arms'
arms=json.loads((arms_source/'scene.gltf').read_text())
arms_data=(arms_source/'scene.bin').read_bytes()
for node in arms['nodes']:
    if 'mesh' in node:
        if node['mesh']==12:node['mesh']=0
        else:node.pop('mesh',None)
arms['meshes']=[arms['meshes'][12]]
for primitive in arms['meshes'][0]['primitives']:primitive['material']=0
material=arms['materials'][1];material['name']='SWeaponTex.1DEF_01'
arms['materials']=[material]
for key in ['baseColorTexture','metallicRoughnessTexture']:
    material['pbrMetallicRoughness'][key]['index']-=3
material['normalTexture']['index']-=3;material['occlusionTexture']['index']-=3
arms['textures']=arms['textures'][3:]
used_images=[]
for texture in arms['textures']:
    image=arms['images'][texture['source']].copy()
    old_uri=image['uri'];image['uri']='textures/OpenScctSidearms_arms_'+Path(old_uri).name
    shutil.copy2(arms_source/old_uri,out/image['uri'])
    texture['source']=len(used_images);used_images.append(image)
arms['images']=used_images
arms['buffers']=[{'byteLength':len(arms_data)}]
encoded=json.dumps(arms,sort_keys=True,separators=(',',':')).encode();encoded+=b' '*(-len(encoded)%4)
arms_data+=b'\0'*(-len(arms_data)%4)
(out/'arms_base.glb').write_bytes(struct.pack('<III',0x46546c67,2,28+len(encoded)+len(arms_data))+struct.pack('<II',len(encoded),0x4e4f534a)+encoded+struct.pack('<II',len(arms_data),0x004e4942)+arms_data)
