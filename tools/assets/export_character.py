"""Export an authoring character without its control rig, retaining game metadata.

blender -b --python-exit-code 1 --python export_character.py -- authoring.blend output.glb
Default: preserve all original action clips. --clip NAME bakes the current control
animation over the scene frame range into a single new clip (staging only).
"""
import argparse
import copy
import json
from pathlib import Path
import shutil
import sys

import bpy

sys.path.insert(0, str(Path(__file__).parent))
from validate_glb import read_glb, write_glb, validate


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('blend', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--clip')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    source_blend, output = args.blend.resolve(), args.output.resolve()
    bpy.ops.wm.open_mainfile(filepath=str(source_blend))
    scene = bpy.context.scene
    source = Path(scene['openscct_source'])
    if output == source or output == source_blend or output.suffix != '.glb':
        raise ValueError('Export to a separate staged .glb, not the source')
    reference, reference_chunks = read_glb(source)
    deform = bpy.data.objects[scene['openscct_export_rig']]
    controls = bpy.data.objects[scene['openscct_control_rig']]
    meshes = [o for o in scene.objects if o.type == 'MESH' and o.find_armature() == deform]
    bpy.ops.object.select_all(action='DESELECT')
    deform.hide_set(False)
    deform.select_set(True)
    bpy.context.view_layer.objects.active = deform
    if args.clip:
        controls['controls_active'] = 1.0
        controls.update_tag()
        bpy.context.view_layer.update()
        old_actions = list(bpy.data.actions)
        bpy.ops.object.mode_set(mode='POSE')
        bpy.ops.pose.select_all(action='SELECT')
        bpy.ops.nla.bake(frame_start=scene.frame_start, frame_end=scene.frame_end,
                        only_selected=True, visual_keying=True, clear_constraints=True,
                        use_current_action=False, bake_types={'POSE'})
        bpy.ops.object.mode_set(mode='OBJECT')
        action = deform.animation_data.action
        action.name = args.clip
        for old in old_actions:
            if old != action:
                bpy.data.actions.remove(old)
    else:
        controls['controls_active'] = 0.0
        controls.update_tag()
        deform.animation_data_create()
        deform.animation_data.action = next(iter(bpy.data.actions))
    for bone in deform.pose.bones:
        for constraint in list(bone.constraints):
            bone.constraints.remove(constraint)
    for mesh in meshes:
        mesh.hide_set(False)
        mesh.select_set(True)
    output.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=str(output), export_format='GLB', use_selection=True,
                             export_animations=bool(args.clip), export_animation_mode='ACTIONS',
                             export_force_sampling=True, export_extras=True,
                             export_anim_slide_to_zero=True)
    document, chunks = read_glb(output)
    document['extras'] = reference.get('extras', {})
    if args.clip:
        document.get('extras', {}).get('svmap', {}).pop('animation', None)
    materials = {m['name']: m for m in reference['materials']}
    for material in document.get('materials', []):
        if material.get('name') in materials:
            material['extras'] = materials[material['name']].get('extras', {})
    texture_folder = output.parent / 'textures'
    texture_folder.mkdir(exist_ok=True)
    for path in (source_blend.parent / 'textures').glob('*.png'):
        shutil.copy2(path, texture_folder / path.name)
    # OpenSCCT's material compiler reads image URIs rather than embedded buffers.
    binary = next(data for kind, data in chunks if kind == b'BIN\x00')
    for index, image in enumerate(document.get('images', [])):
        if 'bufferView' not in image:
            continue
        view = document['bufferViews'][image.pop('bufferView')]
        mime = image.pop('mimeType', 'image/png')
        extension = '.jpg' if mime == 'image/jpeg' else '.png'
        name = Path(image.get('name', f'image_{index}')).stem + extension
        name = name.replace(':', '_').replace('/', '_').replace('\\', '_')
        offset = view.get('byteOffset', 0)
        (texture_folder / name).write_bytes(binary[offset:offset + view['byteLength']])
        image['uri'] = 'textures/' + name
    # Authoring-only metadata is not needed by the game.
    for node in document.get('nodes', []):
        node.pop('extras', None)
    if not args.clip:
        # Preserve original timing/notifies' matching render tracks exactly.
        # Blender resampling drops static clips and rounds some clip endpoints.
        original_binary = next(data for kind, data in reference_chunks if kind == b'BIN\x00')
        binary_offset = len(binary)
        view_offset = len(document.get('bufferViews', []))
        accessor_offset = len(document.get('accessors', []))
        for view in reference.get('bufferViews', []):
            view = copy.deepcopy(view)
            view['buffer'] = 0
            view['byteOffset'] = view.get('byteOffset', 0) + binary_offset
            document.setdefault('bufferViews', []).append(view)
        for accessor in reference.get('accessors', []):
            accessor = copy.deepcopy(accessor)
            if 'sparse' in accessor:
                raise ValueError('Sparse reference accessors need explicit remapping')
            if 'bufferView' in accessor:
                accessor['bufferView'] += view_offset
            document.setdefault('accessors', []).append(accessor)
        node_indices = {node.get('name'): i for i, node in enumerate(document['nodes'])}
        animations = copy.deepcopy(reference['animations'])
        for animation in animations:
            for sampler in animation['samplers']:
                sampler['input'] += accessor_offset
                sampler['output'] += accessor_offset
            for channel in animation['channels']:
                target = channel['target']
                target['node'] = node_indices[reference['nodes'][target['node']]['name']]
        document['animations'] = animations
        document['buffers'][0]['byteLength'] = len(binary) + len(original_binary)
        chunks = [(b'BIN\x00', binary + original_binary)]
    write_glb(output, document, chunks)
    bones = [reference['nodes'][i]['name'] for skin in reference['skins'] for i in skin['joints']]
    expected = {args.clip: None} if args.clip else {a['name']: None for a in reference['animations']}
    report = validate(output, {'required_bones': bones, 'required_clips': expected,
                              'allow_static_clips': list(expected) if not args.clip else []})
    report['source_blend'] = str(source_blend)
    report['game_material_metadata_preserved'] = True
    report['authoring_bones_exported'] = [b for b in report['bones'] if b.startswith(('CTRL_', 'OUT_', 'IK_', 'POLE_'))]
    report['passed'] &= not report['authoring_bones_exported']
    output.with_suffix('.report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'passed': report['passed'], 'bones': len(report['bones']),
                      'clips': len(report['clips_seconds']), 'errors': report['errors']}))
    if not report['passed']:
        raise ValueError('Character export validation failed')


if __name__ == '__main__':
    main()
