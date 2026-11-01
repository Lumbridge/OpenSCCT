"""Blender batch export: blender -b --python export_asset.py -- manifest.json.

All manifest paths are relative to the manifest. Outputs are staged GLBs, never
automatically installed into gameplay packs. Source files are not overwritten.
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys

import bpy

sys.path.insert(0, str(Path(__file__).parent))
from validate_glb import validate


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('manifest', type=Path)
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    manifest = args.manifest.resolve()
    spec = json.loads(manifest.read_text(encoding='utf-8'))
    source = (manifest.parent / spec['source']).resolve()
    output = (manifest.parent / spec['output']).resolve()
    if output == source or output.suffix.lower() != '.glb':
        raise ValueError('Output must be a separate .glb file')
    for field in ('source_url', 'author', 'license'):
        if not spec.get(field):
            raise ValueError(f'Missing provenance: {field}')
    # Run in a fresh background Blender process, never the live editing scene.
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if source.suffix.lower() == '.blend':
        bpy.ops.wm.open_mainfile(filepath=str(source))
    elif source.suffix.lower() == '.fbx':
        bpy.ops.import_scene.fbx(filepath=str(source), use_anim=True)
    elif source.suffix.lower() in ('.gltf', '.glb'):
        bpy.ops.import_scene.gltf(filepath=str(source))
    else:
        raise ValueError('Supported inputs: .blend, .fbx, .gltf, .glb')
    objects = [o for o in bpy.context.scene.objects if o.type in ('MESH', 'ARMATURE', 'EMPTY')]
    requested = spec.get('objects')
    if requested:
        roots = [bpy.data.objects.get(name) for name in requested]
        if any(o is None for o in roots):
            raise ValueError('A requested object is missing')
        objects = list({child for o in roots for child in [o, *o.children_recursive]})
    for obj in objects:
        if obj.type == 'MESH' and spec.get('triangle_target'):
            if obj.find_armature():
                raise ValueError('Automatic decimation is limited to static props')
            obj.data.calc_loop_triangles()
            count = len(obj.data.loop_triangles)
            if count > spec['triangle_target']:
                modifier = obj.modifiers.new('AssetPipelineDecimate', 'DECIMATE')
                modifier.ratio = spec['triangle_target'] / count
        if obj.type == 'ARMATURE' and obj.animation_data:
            for track in obj.animation_data.nla_tracks:
                track.mute = True
    for action in bpy.data.actions:
        if action.name in spec.get('clip_names', {}):
            action.name = spec['clip_names'][action.name]
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = next((o for o in objects if o.type == 'MESH'), None)
    output.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=str(output), export_format='GLB', use_selection=True,
                             export_apply=True,
                             export_animations=True, export_animation_mode='ACTIONS',
                             export_anim_slide_to_zero=True,
                             export_force_sampling=True, export_extras=True)
    report = validate(output, spec.get('validation'))
    report['provenance'] = {k: spec[k] for k in ('source_url', 'author', 'license')}
    report['source_sha256'] = hashlib.sha256(source.read_bytes()).hexdigest()
    report['output_sha256'] = hashlib.sha256(output.read_bytes()).hexdigest()
    report['blender_version'] = bpy.app.version_string
    output.with_suffix('.report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    output.with_suffix('.credits.txt').write_text(
        '\n'.join(f'{k}: {v}' for k, v in report['provenance'].items()) + '\n', encoding='utf-8')
    print(json.dumps(report, indent=2))
    if not report['passed']:
        raise ValueError('Export failed validation; see report')


if __name__ == '__main__':
    main()
