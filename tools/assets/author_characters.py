"""Build editable spy/merc authoring files from locally converted character GLBs.

blender -b --python-exit-code 1 --python tools/assets/author_characters.py -- --repo PATH
The original mesh, UVs and export skeleton are preserved. A separate control rig
provides FK, calibrated limb IK and world-space output bones for the export rig.
"""
import argparse
import json
import math
from pathlib import Path
import shutil
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).parent))
from validate_glb import read_glb


def collection(name):
    result = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(result)
    return result


def move_to(obj, coll):
    for old in list(obj.users_collection):
        old.objects.unlink(obj)
    coll.objects.link(obj)


def shape(name, radius, coll):
    vertices = [(radius * math.cos(i * math.tau / 24), 0,
                 radius * math.sin(i * math.tau / 24)) for i in range(24)]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [(i, (i + 1) % 24) for i in range(24)], [])
    obj = bpy.data.objects.new(name, mesh)
    coll.objects.link(obj)
    obj.hide_render = True
    obj.hide_set(True)
    return obj


def build(repo, mesh_name, label):
    source = repo / '.content/shared/chars/SPerso' / (mesh_name + '.glb')
    folder = repo / 'local/characters' / label.lower()
    folder.mkdir(parents=True, exist_ok=True)
    backup = repo / 'local/characters/backups/before-authoring/SPerso' / (mesh_name + '.glb')
    snapshot = folder / 'reference.glb'
    if not snapshot.exists():
        shutil.copy2(backup if backup.exists() else source, snapshot)
    reference, _ = read_glb(source)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(source), import_pack_images=False,
                             bone_heuristic='BLENDER', disable_bone_shape=True)
    deform = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    deform.name = label + '_EXPORT'
    model_coll = collection(label + ' | Model + export skeleton')
    controls_coll = collection(label + ' | Animation controls')
    widgets = collection(label + ' | Control shapes (do not export)')
    for obj in [deform, *meshes]:
        move_to(obj, model_coll)
    # Save the reference actions as reusable motion clips, with no active
    # animation or NLA track overriding the authoring rest pose.
    actions = list(bpy.data.actions)
    for action in actions:
        action.use_fake_user = True
        action.asset_mark()
    deform.animation_data_clear()
    for bone in deform.pose.bones:
        bone.matrix_basis.identity()
    bpy.context.view_layer.update()
    rig_data = bpy.data.armatures.new(label + '_Controls')
    rig = bpy.data.objects.new(label + '_CONTROLS', rig_data)
    controls_coll.objects.link(rig)
    rig.matrix_world = deform.matrix_world.copy()
    rig.show_in_front = True
    rig['controls_active'] = 1.0
    rig.id_properties_ui('controls_active').update(min=0, max=1,
        description='1: author with controls; 0: play the original clips on the export rig')
    rig['asset_role'] = label
    rig['source_mesh'] = str(snapshot)
    rig['export_object'] = deform.name
    ring = shape(label + '_FK_shape', .055, widgets)
    target_shape = shape(label + '_IK_shape', .10, widgets)
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    root = rig_data.edit_bones.new('CTRL_world')
    root.head, root.tail = (0, 0, 0), (0, .2, 0)
    root.use_deform = False
    # Mechanism controls point towards the next anatomical joint. Output bones
    # preserve the game's exact bind orientation regardless of control axes.
    child_override = {'B Pelvis': 'B Spine', 'B Spine': 'B Spine1', 'B Spine1': 'B Spine2',
                      'B Spine2': 'B Neck', 'B Neck': 'B Head'}
    for side in ('L', 'R'):
        for parent, child in [('Clavicle', 'UpperArm'), ('UpperArm', 'ForeArm'),
                              ('ForeArm', 'Hand'), ('Thigh', 'Calf'), ('Calf', 'Foot'), ('Foot', 'Toe0')]:
            child_override[f'B {side} {parent}'] = f'B {side} {child}'
    for bone in deform.data.bones:
        ctrl = rig_data.edit_bones.new('CTRL_' + bone.name)
        ctrl.length = max(bone.length, .04)
        ctrl.matrix = bone.matrix_local.copy()
        child = child_override.get(bone.name)
        if child:
            ctrl.tail = deform.data.bones[child].head_local
        ctrl.use_deform = False
        if bone.parent:
            ctrl.parent = rig_data.edit_bones['CTRL_' + bone.parent.name]
        else:
            ctrl.parent = root
        out = rig_data.edit_bones.new('OUT_' + bone.name)
        out.length = max(bone.length, .03)
        out.matrix = bone.matrix_local.copy()
        out.parent = ctrl
        out.use_deform = False
    limb_specs = []
    for side in ('L', 'R'):
        for kind, first, middle, end in [('hand', 'UpperArm', 'ForeArm', 'Hand'),
                                        ('foot', 'Thigh', 'Calf', 'Foot')]:
            names = [f'B {side} {part}' for part in (first, middle, end)]
            a, b, c = [deform.data.bones[n].head_local.copy() for n in names]
            direction = (c - a).normalized()
            bend = b - (a + direction * (b - a).dot(direction))
            if bend.length < .005:
                bend = Vector((0, -1 if kind == 'foot' else 1, 0))
                bend -= direction * bend.dot(direction)
            pole_location = b + bend.normalized() * .45
            target = rig_data.edit_bones.new(f'IK_{side}_{kind}')
            target.length = .15
            target.matrix = rig_data.edit_bones['CTRL_' + names[2]].matrix.copy()
            target.parent = root
            target.use_deform = False
            pole = rig_data.edit_bones.new(f'POLE_{side}_{kind}')
            pole.head, pole.tail = pole_location, pole_location + Vector((0, .12, 0))
            pole.parent = root
            pole.use_deform = False
            limb_specs.append((side, kind, names, b))
    bpy.ops.object.mode_set(mode='OBJECT')
    output_bones = rig_data.collections.new('Output mechanism (hidden)')
    fk_bones = rig_data.collections.new('FK controls')
    ik_bones = rig_data.collections.new('IK targets + poles')
    for bone in rig_data.bones:
        if bone.name.startswith('OUT_'):
            output_bones.assign(bone)
        else:
            (ik_bones if bone.name.startswith(('IK_', 'POLE_')) else fk_bones).assign(bone)
            pose = rig.pose.bones[bone.name]
            pose.custom_shape = target_shape if bone.name.startswith('IK_') else ring
            pose.use_custom_shape_bone_size = False
            pose.color.palette = 'THEME04' if ' L ' in bone.name or '_L_' in bone.name else 'THEME03'
    output_bones.is_visible = False
    # Calibrate each pole angle against the reference's actual bend plane.
    for side, kind, names, expected_elbow in limb_specs:
        prop = f'ik_{side}_{kind}'
        rig[prop] = 0.0
        rig.id_properties_ui(prop).update(min=0, max=1, description='0: FK, 1: IK')
        ik = rig.pose.bones['CTRL_' + names[1]].constraints.new('IK')
        ik.name = f'{side} {kind} IK'
        ik.target, ik.subtarget = rig, f'IK_{side}_{kind}'
        ik.pole_target, ik.pole_subtarget = rig, f'POLE_{side}_{kind}'
        ik.chain_count, ik.use_stretch = 2, False
        best = (float('inf'), 0)
        for index in range(72):
            angle = index * math.tau / 72 - math.pi
            ik.pole_angle = angle
            bpy.context.view_layer.update()
            actual = rig.pose.bones['CTRL_' + names[1]].head
            error = (actual - expected_elbow).length
            if error < best[0]:
                best = error, angle
        ik.pole_angle = best[1]
        driver = ik.driver_add('influence').driver
        driver.expression = 'value'
        variable = driver.variables.new()
        variable.name, variable.type = 'value', 'SINGLE_PROP'
        variable.targets[0].id = rig
        variable.targets[0].data_path = f'["{prop}"]'
        rotation = rig.pose.bones['CTRL_' + names[2]].constraints.new('COPY_ROTATION')
        rotation.target, rotation.subtarget = rig, f'IK_{side}_{kind}'
        rotation.target_space = rotation.owner_space = 'POSE'
        driver = rotation.driver_add('influence').driver
        driver.expression = 'value'
        variable = driver.variables.new()
        variable.name, variable.type = 'value', 'SINGLE_PROP'
        variable.targets[0].id = rig
        variable.targets[0].data_path = f'["{prop}"]'
    for bone in deform.pose.bones:
        constraint = bone.constraints.new('COPY_TRANSFORMS')
        constraint.name = 'Authoring control rig'
        constraint.target, constraint.subtarget = rig, 'OUT_' + bone.name
        constraint.target_space = constraint.owner_space = 'WORLD'
        driver = constraint.driver_add('influence').driver
        driver.expression = 'active'
        variable = driver.variables.new()
        variable.name, variable.type = 'active', 'SINGLE_PROP'
        variable.targets[0].id = rig
        variable.targets[0].data_path = '["controls_active"]'
    texture_folder = folder / 'textures'
    texture_folder.mkdir(exist_ok=True)
    # Keep local editable copies, including the vision/specular auxiliary maps.
    for texture in (source.parent / 'textures').glob('SPersoTextures_*.png'):
        shutil.copy2(texture, texture_folder / texture.name)
    for image in bpy.data.images:
        path = Path(bpy.path.abspath(image.filepath)) if image.filepath else None
        if path and (texture_folder / path.name).is_file():
            image.filepath = str(texture_folder / path.name)
            image.reload()
            image.filepath = '//textures/' + path.name
    for mesh in meshes:
        mesh['authoring_notes'] = 'Original silhouette/UV layout retained. Weight groups bind to EXPORT bones.'
        detail = mesh.modifiers.new('Optional sculpt subdivision (off for export)', 'SUBSURF')
        detail.levels = detail.render_levels = 1
        detail.show_viewport = detail.show_render = False
    report = {'character': label, 'mesh': mesh_name, 'source': str(source),
              'export_bones': len(deform.data.bones), 'controls': len(rig.data.bones),
              'preserved_clips': len(actions), 'materials': [m.name for m in bpy.data.materials],
              'rest_error_m': 0.0, 'rest_angle_radians': 0.0, 'ik_tests': {}}
    bpy.context.view_layer.update()
    for bone in deform.pose.bones:
        error = (bone.matrix.translation - bone.bone.matrix_local.translation).length
        report['rest_error_m'] = max(report['rest_error_m'], error)
        angle = bone.matrix.to_quaternion().rotation_difference(bone.bone.matrix_local.to_quaternion()).angle
        report['rest_angle_radians'] = max(report['rest_angle_radians'], min(angle, abs(math.tau - angle)))
    # Prove all four IK targets move the expected effector and remain finite.
    for side, kind, names, _ in limb_specs:
        prop = f'ik_{side}_{kind}'
        rig[prop] = 1.0
        rig.update_tag()
        bpy.context.view_layer.update()
        controller = rig.pose.bones[f'IK_{side}_{kind}']
        old = controller.matrix.copy()
        moved = old.copy()
        shoulder = rig.pose.bones['CTRL_' + names[0]].head
        moved.translation += (shoulder - old.translation).normalized() * .04
        controller.matrix = moved
        bpy.context.view_layer.update()
        target_pos = controller.matrix.translation
        end_pos = rig.pose.bones['CTRL_' + names[2]].matrix.translation
        report['ik_tests'][prop] = (target_pos - end_pos).length
        controller.matrix = old
        rig[prop] = 0.0
        rig.update_tag()
        bpy.context.view_layer.update()
    report['passed'] = (report['rest_error_m'] < .0001 and report['rest_angle_radians'] < .002
                        and all(v < .025 for v in report['ik_tests'].values()))
    notes = bpy.data.texts.new('START HERE - ' + label)
    notes.write(f'''{label}: OpenSCCT editable character base

Model/UVs are derived from your existing locally converted {mesh_name}.
The familiar silhouette is preserved; this is an authoring upgrade, not a new sculpt.

Select {rig.name}, Pose Mode. CTRL_* = FK; IK_* = limb targets; POLE_* = bend direction.
Object custom properties ik_L_hand, ik_R_hand, ik_L_foot, ik_R_foot switch FK/IK.
controls_active=1 drives the export skeleton. Set 0 to preview existing actions on {deform.name}.
All {len(actions)} source clips are retained as Action assets; select them in the Action Editor.
IK switches do not auto-snap: align targets before switching within an existing pose.

Paint/replace the local images under textures/. Original UVs/material identifiers remain intact.
The disabled subdivision modifier is an authoring option, not an automatic quality upgrade.
Do not export CONTROLS or shape widgets. Use export_character.py for gameplay-compatible export.
''')
    deform.hide_set(True)
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.context.scene['openscct_source'] = str(snapshot)
    bpy.context.scene['openscct_export_rig'] = deform.name
    bpy.context.scene['openscct_control_rig'] = rig.name
    bpy.context.scene['openscct_mesh'] = mesh_name
    bpy.context.scene.render.fps = 30
    bpy.context.scene.frame_end = 60
    blend = folder / (label.lower() + '-authoring.blend')
    bpy.ops.wm.save_as_mainfile(filepath=str(blend))
    (folder / 'rig-report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report))
    if not report['passed']:
        raise ValueError(f'{label} rig validation failed')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', type=Path, required=True)
    parser.add_argument('--character', choices=['spy', 'merc', 'both'], default='both')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    for mesh, label in [('ATT_01', 'Spy'), ('DEF_01', 'Merc')]:
        if args.character in ('both', label.lower()):
            build(args.repo.resolve(), mesh, label)
