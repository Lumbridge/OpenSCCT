"""Bake a Mixamo clip onto a local OpenSCCT reference skeleton for visual review.

blender -b --python-exit-code 1 --python retarget_mixamo.py -- manifest.json
Retains target geometry/proportions, creates an in-place clip, and preserves
the reference GLB placement metadata. Does not replace simulation animation data.
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, str(Path(__file__).parent))
from validate_glb import read_glb, write_glb, validate


BODY_MAP = {
    'Hips': 'B Pelvis', 'Spine': 'B Spine', 'Spine1': 'B Spine1',
    'Spine2': 'B Spine2', 'Neck': 'B Neck', 'Head': 'B Head',
}
for side, short in [('Left', 'L'), ('Right', 'R')]:
    for source, target in [('Shoulder', 'Clavicle'), ('Arm', 'UpperArm'),
                           ('ForeArm', 'ForeArm'), ('Hand', 'Hand'),
                           ('UpLeg', 'Thigh'), ('Leg', 'Calf'),
                           ('Foot', 'Foot'), ('ToeBase', 'Toe0')]:
        BODY_MAP[side + source] = f'B {short} {target}'


def anatomical_frame(arm, hips, head, left, right):
    def point(name):
        return arm.matrix_world @ arm.data.bones[name].head_local
    up = (point(head) - point(hips)).normalized()
    lateral = (point(right) - point(left)).normalized()
    forward = up.cross(lateral).normalized()
    lateral = forward.cross(up).normalized()
    return Matrix((lateral, forward, up)).transposed().to_quaternion()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('manifest', type=Path)
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    manifest = args.manifest.resolve()
    spec = json.loads(manifest.read_text(encoding='utf-8'))
    source, target_path, output = [(manifest.parent / spec[k]).resolve()
                                   for k in ('source', 'target', 'output')]
    if output in (source, target_path) or output.suffix != '.glb':
        raise ValueError('Output must be a separate staged GLB')
    for field in ('source_url', 'author', 'license'):
        if not spec.get(field):
            raise ValueError(f'Missing provenance: {field}')
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(source), use_anim=True)
    source_objects = set(bpy.context.scene.objects)
    arms = [o for o in source_objects if o.type == 'ARMATURE']
    if len(arms) != 1 or not arms[0].animation_data or not arms[0].animation_data.action:
        raise ValueError('Expected one animated source armature')
    src = arms[0]
    source_action = src.animation_data.action
    first, last = map(int, source_action.frame_range)
    bpy.ops.import_scene.gltf(filepath=str(target_path), import_pack_images=True)
    targets = set(bpy.context.scene.objects) - source_objects
    arms = [o for o in targets if o.type == 'ARMATURE']
    if len(arms) != 1:
        raise ValueError('Expected one reference armature')
    target = arms[0]
    shapes = {b.custom_shape for b in target.pose.bones if b.custom_shape}
    targets -= shapes
    for shape in shapes:
        shape.hide_render = True
    for obj in targets:
        obj.animation_data_clear()
    for action in list(bpy.data.actions):
        if action != source_action:
            bpy.data.actions.remove(action)
    for bone in target.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)
    mapping = spec.get('bone_map') or {f'mixamorig:{s}': t for s, t in BODY_MAP.items()}
    for s, t in mapping.items():
        if s not in src.data.bones or t not in target.data.bones:
            raise ValueError(f'Missing bone mapping: {s} -> {t}')
    src_frame = anatomical_frame(src, 'mixamorig:Hips', 'mixamorig:Head',
                                'mixamorig:LeftUpLeg', 'mixamorig:RightUpLeg')
    dst_frame = anatomical_frame(target, 'B Pelvis', 'B Head', 'B L Thigh', 'B R Thigh')
    alignment = dst_frame @ src_frame.inverted()
    src_rest = {s: (src.matrix_world @ src.data.bones[s].matrix_local).to_quaternion() for s in mapping}
    dst_rest = {t: (target.matrix_world @ target.data.bones[t].matrix_local).to_quaternion() for t in mapping.values()}
    src_height = ((src.matrix_world @ src.data.bones['mixamorig:Head'].head_local) -
                  (src.matrix_world @ src.data.bones['mixamorig:Hips'].head_local)).length
    dst_height = ((target.matrix_world @ target.data.bones['B Head'].head_local) -
                  (target.matrix_world @ target.data.bones['B Pelvis'].head_local)).length
    ratio = dst_height / src_height
    target.animation_data_create()
    action = bpy.data.actions.new(spec['clip_name'])
    target.animation_data.action = action
    scene = bpy.context.scene
    scene.render.fps = spec.get('fps', 30)
    scene.frame_start, scene.frame_end = first, last
    reverse = {t: s for s, t in mapping.items()}
    pelvis_start = None
    for frame in range(first, last + 1):
        scene.frame_set(frame)
        bpy.context.view_layer.update()
        posed = {s: src.matrix_world @ src.pose.bones[s].matrix for s in mapping}
        pelvis = posed['mixamorig:Hips'].translation
        if pelvis_start is None:
            pelvis_start = pelvis.copy()
        for bone in target.pose.bones:
            if bone.name not in reverse:
                bone.matrix_basis = Matrix.Identity(4)
                continue
            s = reverse[bone.name]
            # Transfer the source's world-space rotational change from bind
            # pose; the target's own rest transforms retain its proportions.
            delta = alignment @ posed[s].to_quaternion() @ src_rest[s].inverted() @ alignment.inverted()
            desired = target.matrix_world.to_quaternion().inverted() @ delta @ dst_rest[bone.name]
            rest = bone.bone.matrix_local
            parent_rest = bone.parent.bone.matrix_local if bone.parent else Matrix.Identity(4)
            parent_pose = bone.parent.matrix if bone.parent else Matrix.Identity(4)
            translation = (parent_pose @ parent_rest.inverted() @ rest).translation
            if bone.name == 'B Pelvis':
                offset = alignment @ (pelvis - pelvis_start) * ratio
                # Pilot is deliberately in-place: retain vertical bob only.
                translation += target.matrix_world.inverted().to_3x3() @ Vector((0, 0, offset.z))
            bone.matrix = Matrix.LocRotScale(translation, desired, Vector((1, 1, 1)))
            bone.rotation_mode = 'QUATERNION'
            bone.keyframe_insert('rotation_quaternion', frame=frame, group=bone.name)
            bone.keyframe_insert('location', frame=frame, group=bone.name)
            bpy.context.view_layer.update()
    for obj in source_objects:
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.actions.remove(source_action)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in targets:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = target
    scene.frame_set(first)
    output.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(output.with_suffix('.blend')))
    bpy.ops.export_scene.gltf(filepath=str(output), export_format='GLB', use_selection=True,
                             export_animations=True, export_animation_mode='ACTIONS',
                             export_anim_slide_to_zero=True,
                             export_force_sampling=True, export_extras=True)
    document, chunks = read_glb(output)
    reference, _ = read_glb(target_path)
    document['extras'] = reference.get('extras', {})
    # The original simulation clips are NOT part of this staged preview.
    document.get('extras', {}).get('svmap', {}).pop('animation', None)
    write_glb(output, document, chunks)
    required = [reference['nodes'][i]['name'] for skin in reference['skins'] for i in skin['joints']]
    report = validate(output, {'required_bones': required,
                              'required_clips': {spec['clip_name']: (last - first) / scene.render.fps}})
    report.update({'mapped_bones': mapping, 'in_place': True, 'status': 'visual-review-only',
                   'unmapped_target_bones': sorted(set(required) - set(mapping.values())),
                   'source_url': spec['source_url'], 'license': spec['license'], 'author': spec['author'],
                   'source_sha256': hashlib.sha256(source.read_bytes()).hexdigest(),
                   'target_sha256': hashlib.sha256(target_path.read_bytes()).hexdigest()})
    output.with_suffix('.report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({k: report[k] for k in ('passed', 'triangles', 'clips_seconds', 'errors', 'unmapped_target_bones')}))
    if not report['passed']:
        raise ValueError('Retarget export failed validation')


if __name__ == '__main__':
    main()
