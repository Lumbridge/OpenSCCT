"""Render menu icons from the exported world rifles using background Blender.

blender --background --disable-autoexec --python content/assets/rifles/render.py
"""
from pathlib import Path
import json
import struct
import bpy
from mathutils import Vector

ROOT=Path(__file__).resolve().parent/'export'
for name in ('m4a1s','ak47','galil','famas'):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(ROOT/(name+'.glb')))
    bpy.context.view_layer.update()
    scene=bpy.context.scene
    # Blender's imported skin can still report the placeholder [-1,+1] bounds
    # before its first evaluated draw. The authored world meshes are at identity;
    # use their actual glTF accessor bounds to frame the catalogue consistently.
    raw=(ROOT/(name+'.glb')).read_bytes();size=struct.unpack_from('<I',raw,12)[0]
    document=json.loads(raw[20:20+size]);points=[]
    for mesh in document['meshes']:
        for primitive in mesh['primitives']:
            bounds=document['accessors'][primitive['attributes']['POSITION']]
            for point in (bounds['min'],bounds['max']):points.append(Vector((point[0],-point[2],point[1])))
    low=Vector([min(v[i] for v in points) for i in range(3)])
    high=Vector([max(v[i] for v in points) for i in range(3)])
    centre=(low+high)/2;length=max(high-low)
    lens=bpy.data.cameras.new('Catalogue');camera=bpy.data.objects.new('Catalogue',lens)
    bpy.context.collection.objects.link(camera);scene.camera=camera
    camera.location=centre+Vector((.02,-1,.13))*length*1.8
    camera.rotation_euler=(centre-camera.location).to_track_quat('-Z','Y').to_euler()
    lens.type='ORTHO';lens.ortho_scale=length*1.1
    for position,power,size in [((.4,-.6,.8),160,.9),((-.2,.6,.5),90,.7),((-.2,-.1,-.2),25,.6)]:
        light=bpy.data.lights.new('Softbox','AREA');light.energy=power*length**2*.25
        light.shape='DISK';light.size=size*length;node=bpy.data.objects.new('Softbox',light)
        bpy.context.collection.objects.link(node);node.location=centre+Vector(position)*length
        node.rotation_euler=(centre-node.location).to_track_quat('-Z','Y').to_euler()
    scene.world=bpy.data.worlds.new('Studio');scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.04,.055,.07,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value=.25
    scene.render.engine='CYCLES';scene.cycles.samples=32
    scene.render.resolution_x=800;scene.render.resolution_y=320;scene.render.resolution_percentage=100
    scene.render.film_transparent=True;scene.render.image_settings.file_format='PNG'
    scene.render.filepath=str(ROOT/'textures'/(name+'_icon.png'));bpy.ops.render.render(write_still=True)
