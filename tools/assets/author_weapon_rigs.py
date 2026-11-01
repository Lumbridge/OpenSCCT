"""Create editable weapon reload workspaces without changing game skeletons.

blender -b --python tools/assets/author_weapon_rigs.py -- --repo PATH
Outputs ignored local/weapons/<weapon>/<weapon>-authoring.blend. Existing files
are not overwritten unless --replace is supplied. Controls are FK, with the
authored reload selected; originals remain Action assets for reuse.
"""
import argparse
import json
from pathlib import Path
import shutil
import sys

import bpy

sys.path.insert(0, str(Path(__file__).parent))
from author_characters import collection, move_to, shape
from validate_glb import read_glb


MODELS = {
    'rifle': ('SWeaponFirst/1DEF_01.glb', 'FirstReloStFd2'),
    'uzi': ('SWeaponFirst/1DEF_Uzi.glb', 'FirstReloStFd2U'),
    'shotgun': ('SWeaponFirst/1DEF_shotgun.glb', 'FirstReloStFd2S'),
    **{name: (f'OpenScctSidearms/{name}_first.glb', 'FirstReloStFd2')
       for name in ('suppressed', 'unsuppressed', 'automatic', 'desert_eagle', 'magnum44')},
}


def build(repo, name, replace):
    relative, clip = MODELS[name]
    folder = repo/'local/weapons'/name
    blend = folder/(name+'-authoring.blend')
    if blend.exists() and not replace:
        print('Keeping edited workspace: '+str(blend), flush=True)
        return
    source = repo/'.content/shared/chars'/relative
    doc, _ = read_glb(source)
    folder.mkdir(parents=True, exist_ok=True)
    snapshot = folder/'reference.glb'
    shutil.copy2(source, snapshot)
    uris = {i['uri'] for i in doc.get('images', []) if i.get('uri')}
    for material in doc.get('materials', []):
        uris.update(v for v in material.get('extras', {}).values()
                    if isinstance(v, str) and v.startswith('textures/'))
    for uri in uris:
        src = (source.parent/uri).resolve()
        dst = (folder/uri).resolve()
        if not src.is_relative_to(source.parent.resolve()) or not dst.is_relative_to(folder.resolve()):
            raise ValueError('Unsafe texture path '+uri)
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dst)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = 30
    bpy.ops.import_scene.gltf(filepath=str(snapshot), import_pack_images=False,
                             bone_heuristic='BLENDER', disable_bone_shape=True)
    deform = max((o for o in bpy.context.scene.objects if o.type == 'ARMATURE'), key=lambda o: len(o.data.bones))
    deform_slot = 'OB'+deform.name
    imported_objects = {'OB'+o.name: o for o in bpy.context.scene.objects}
    deform.name = name+'_EXPORT'
    actions = list(bpy.data.actions)
    for action in actions:
        action.use_fake_user = True
        action.asset_mark()
    # A duplicate with identical rest matrices can use every imported action
    # directly. Pose-space constraints isolate editing controls from skin weights.
    rig = deform.copy()
    rig.data = deform.data.copy()
    rig.name = name+'_CONTROLS'
    controls = collection(name+' | Animation controls')
    controls.objects.link(rig)
    rig.animation_data_clear()
    deform.animation_data_clear()
    rig.show_in_front = True
    rig['controls_active'] = 1.0
    rig.id_properties_ui('controls_active').update(min=0, max=1,
        description='Controls drive the skin; disable to play an original action on the export rig')
    widgets = collection(name+' | Widgets (do not export)')
    ring = shape(name+'_control', .018, widgets)
    groups = {n: rig.data.collections.new(n) for n in ('Hands and arms', 'Fingers', 'Weapon mechanisms', 'Other bones')}
    for bone in rig.data.bones:
        for existing in list(bone.collections):
            existing.unassign(bone)
        lower = bone.name.lower()
        group = ('Fingers' if any(t in lower for t in ('finger', 'thumb', 'point', 'middle', 'ring', 'pink'))
                 else 'Hands and arms' if any(t in lower for t in ('arm', 'hand', 'wrist', 'elbow', 'clavicle'))
                 else 'Weapon mechanisms' if any(t in lower for t in ('corps', 'chargeur', 'pompe', 'pistol', 'base', 'mag', 'slide', 'round', 'case', 'trigger', 'hammer'))
                 else 'Other bones')
        groups[group].assign(bone)
        bone.use_deform = False
        rig.pose.bones[bone.name].custom_shape = ring
        target = deform.pose.bones[bone.name]
        constraint = target.constraints.new('COPY_TRANSFORMS')
        constraint.name = 'Authoring control'
        constraint.target = rig
        constraint.subtarget = bone.name
        constraint.owner_space = constraint.target_space = 'POSE'
        driver = constraint.driver_add('influence').driver
        driver.expression = 'enabled'
        var = driver.variables.new()
        var.name = 'enabled'
        var.targets[0].id = rig
        var.targets[0].data_path = '["controls_active"]'
    groups['Other bones'].is_visible = False
    rig.animation_data_create()
    action = next(a for a in actions if a.name == clip or a.name.startswith(clip+'.'))
    rig.animation_data.action = action
    if action.slots:
        rig.animation_data.action_slot = next(s for s in action.slots if s.identifier == deform_slot)
        # glTF can import a reload as one multi-slot action: the skin, scene
        # root and detached mechanism objects must all play the same clip.
        for slot in action.slots:
            obj = imported_objects.get(slot.identifier)
            if obj is None or obj == deform:
                continue
            obj.animation_data_clear()
            obj.animation_data_create()
            obj.animation_data.action = action
            obj.animation_data.action_slot = slot
    scene = bpy.context.scene
    scene.frame_start = round(action.frame_range[0])
    scene.frame_end = round(action.frame_range[1])
    scene['openscct_source'] = str(snapshot)
    scene['openscct_export_rig'] = deform.name
    scene['openscct_control_rig'] = rig.name
    scene['openscct_mesh'] = source.stem
    scene['openscct_reload_clip'] = clip
    start, end = action.frame_range
    markers = {'Ready': 0, 'Return to aim': 1}
    if name == 'shotgun':
        markers.update({f'Shell {i+1}': t/3 for i, t in enumerate((.788, 1.110, 1.441, 1.919, 2.279))})
        markers['Early-exit target'] = 2.6/3
    elif name == 'magnum44':
        markers.update({'Open cylinder': .12, 'Eject cases': .28, 'Seat rounds': .58, 'Close cylinder': .82})
    elif name in ('suppressed', 'unsuppressed', 'automatic', 'desert_eagle'):
        markers.update({'Magazine release': .14, 'Magazine seated': .56, 'Slide release': .66})
    for label, fraction in markers.items():
        scene.timeline_markers.new(label, frame=round(start+(end-start)*fraction))
    scene.frame_set(scene.frame_start)
    bpy.ops.object.select_all(action='DESELECT')
    deform.hide_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode='POSE')
    text = bpy.data.texts.new('START HERE')
    text.write(f'''{name}: editable first-person weapon and hands

The control skeleton is separate from the skin/export skeleton. Pose the visible
FK controls or edit their curves. Bone collections separate hands, fingers and
weapon mechanisms. The existing geometry, UVs, materials and bone names remain
compatible with OpenSCCT. This is an authoring upgrade, not a newly sculpted gun.

The selected {clip} contains the reworked reload. Other actions are reusable
assets. Markers show handling cues; do not move the shell notify / end timings.
Textures are beside this file under textures/. Save your edits before rebuilding.

export_character.py exports the model with the reference clips by default.
Use --clip {clip} to bake edited control motion into a staged single-clip GLB.
Single-clip exports are previews, not drop-in replacements for the complete gun.
''')
    bpy.ops.wm.save_as_mainfile(filepath=str(blend))
    print(json.dumps({'weapon': name, 'blend': str(blend), 'controls': len(rig.data.bones),
                      'reload': clip, 'frames': [scene.frame_start, scene.frame_end]}), flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', type=Path, required=True)
    parser.add_argument('--weapon', choices=list(MODELS))
    parser.add_argument('--replace', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    for name in ([args.weapon] if args.weapon else MODELS):
        build(args.repo.resolve(), name, args.replace)
