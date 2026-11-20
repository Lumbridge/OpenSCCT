"""Render converted weapon icons or posed view-model reviews with background Blender.

blender --background --disable-autoexec --python tools/assets/render_weapon_review.py -- manifest.json
The manifest names a content GLB, output PNG, and optional clip/fraction or ADS view.
"""
import json
import math
import os
from pathlib import Path
import sys
import bpy
from mathutils import Vector

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / 'content'))
from svmbuild.sidearm_models import read, write
from svmbuild.pistol_arms import tracks, sample, pose, decompose


def render(request):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    source = Path(request['source']); output = Path(request['output'])
    output.parent.mkdir(parents=True, exist_ok=True)
    camera_position = Vector((0, 0, 0))
    camera_forward = Vector((1, 0, 0))
    import_path = source
    if request.get('clip') or request.get('ads'):
        doc, binary = read(source)
        animation = next(a for a in doc['animations'] if a['name'] == request.get('clip', 'FirstWaitStFd2'))
        compiled = tracks(doc, binary, animation)
        duration = max(float(t[-1]) for _, _, t, _ in compiled)
        time = duration * request.get('fraction', 0.)
        if request.get('ads'):
            world = pose(doc, compiled, time)
            sight = next(i for i, n in enumerate(doc['nodes']) if n.get('name') == 'ADS')
            p = world[sight][:3, 3]; direction = world[sight][:3, 0]; direction /= (direction @ direction)**.5
            camera_forward = Vector((float(direction[0]), float(-direction[2]), float(direction[1])))
            camera_position = Vector((float(p[0]), float(-p[2]), float(p[1]))) - camera_forward*request.get('eye_relief', {'m4a1s': .18, 'ak47': .11, 'galil': .12, 'famas': .13}[source.stem.split('_')[0]])
        for node, channel, times, value in compiled:
            target = doc['nodes'][node]
            if 'matrix' in target:
                import numpy as np
                t, q, s = decompose(np.asarray(target.pop('matrix')).reshape(4, 4).T)
                target.update(translation=t.tolist(), rotation=q.tolist(), scale=s.tolist())
            target[channel] = sample(times, value, time, channel == 'rotation').tolist()
        doc['animations'] = []
        for image in doc.get('images', []):
            if 'uri' in image and not image['uri'].startswith('data:'):
                image['uri'] = os.path.relpath(source.parent / image['uri'], output.parent).replace('\\', '/')
        import_path = output.with_suffix('.glb')
        write(import_path, doc, binary)
    bpy.ops.import_scene.gltf(filepath=str(import_path))
    scene = bpy.context.scene
    if request.get('target'):
        bpy.ops.mesh.primitive_plane_add(size=8, location=(4, 0, 0), rotation=(0, math.pi/2, 0))
        board = bpy.context.object
        mat = bpy.data.materials.new('Sight visibility target'); mat.use_nodes = True
        nodes = mat.node_tree.nodes; checker = nodes.new('ShaderNodeTexChecker')
        checker.inputs['Color1'].default_value = (.65, .7, .75, 1)
        checker.inputs['Color2'].default_value = (.08, .10, .12, 1)
        checker.inputs['Scale'].default_value = 32
        coordinates = nodes.new('ShaderNodeTexCoord')
        mat.node_tree.links.new(coordinates.outputs['UV'], checker.inputs['Vector'])
        shader = nodes.get('Principled BSDF')
        mat.node_tree.links.new(checker.outputs['Color'], shader.inputs['Base Color'])
        mat.node_tree.links.new(checker.outputs['Color'], shader.inputs['Emission Color'])
        shader.inputs['Emission Strength'].default_value = .7
        board.data.materials.append(mat)
    bpy.context.view_layer.update()
    dependency = bpy.context.evaluated_depsgraph_get()
    points = []
    for obj in scene.objects:
        # glTF armatures may add a hidden unit-size bone display mesh to the scene.
        if obj.type != 'MESH' or not obj.visible_get() or obj.hide_render or (request.get('target') and obj == board): continue
        evaluated = obj.evaluated_get(dependency); mesh = evaluated.to_mesh()
        points.extend(evaluated.matrix_world @ v.co for v in mesh.vertices)
        evaluated.to_mesh_clear()
    low = Vector([min(p[i] for p in points) for i in range(3)])
    high = Vector([max(p[i] for p in points) for i in range(3)])
    centre = (low+high)*.5; length = max(high-low)
    lens = bpy.data.cameras.new('WeaponCamera'); camera = bpy.data.objects.new('WeaponCamera', lens)
    scene.collection.objects.link(camera); scene.camera = camera
    if request.get('icon'):
        camera.location = centre + Vector((0, -2, .1))*length
        camera.rotation_euler = (centre-camera.location).to_track_quat('-Z', 'Y').to_euler()
        lens.type = 'ORTHO'
        rotation = camera.rotation_euler.to_matrix(); right, up = rotation @ Vector((1, 0, 0)), rotation @ Vector((0, 1, 0))
        width = max(p.dot(right) for p in points) - min(p.dot(right) for p in points)
        height = max(p.dot(up) for p in points) - min(p.dot(up) for p in points)
        lens.ortho_scale = max(width, height*2.5)*1.1
    else:
        camera.location = camera_position
        camera.rotation_euler = camera_forward.to_track_quat('-Z', 'Y').to_euler()
        lens.sensor_fit = 'VERTICAL'; lens.angle = math.radians(48 if request.get('ads') else 60)
        lens.clip_start = .005
    for position, energy, size in [((.4, -.7, 1.1), 100, .9), ((-.3, .6, .5), 65, .8), ((-.4, -.2, .1), 35, .7)]:
        light = bpy.data.lights.new('Softbox', 'AREA'); light.energy = energy*length*length*.25
        light.shape = 'DISK'; light.size = size*length
        obj = bpy.data.objects.new('Softbox', light); scene.collection.objects.link(obj)
        obj.location = centre+Vector(position)*length
        obj.rotation_euler = (centre-obj.location).to_track_quat('-Z', 'Y').to_euler()
    scene.world = bpy.data.worlds.new('Studio'); scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value = (.05, .065, .08, 1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value = .4
    scene.render.engine = 'CYCLES'; scene.cycles.samples = 32
    # Denoising erases a subpixel aiming dot as an isolated firefly.
    scene.cycles.use_denoising = False
    scene.view_settings.view_transform = 'Standard'
    scene.render.resolution_x = 800; scene.render.resolution_y = 320 if request.get('icon') else 450
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = request.get('icon', False)
    scene.render.image_settings.file_format = 'PNG'; scene.render.filepath = str(output)
    bpy.ops.render.render(write_still=True)
    print('WEAPON_RENDER ' + str(output), flush=True)


manifest = Path(sys.argv[sys.argv.index('--')+1])
for request in json.loads(manifest.read_text(encoding='utf-8')):
    render(request)
