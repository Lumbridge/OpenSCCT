"""Open an editable stock-derived Spy viewmodel in a local Blender workspace."""

import argparse
import math
from pathlib import Path
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, str(Path(__file__).parent))
from author_characters import collection, move_to, shape


def import_rig(path, name, target):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(path), import_pack_images=False,
                             bone_heuristic="BLENDER", disable_bone_shape=True)
    imported = sorted(set(bpy.data.objects) - before, key=lambda obj: obj.name)
    rig = next(obj for obj in imported if obj.type == "ARMATURE")
    rig.name = name
    for obj in imported:
        move_to(obj, target)
    roots = [obj for obj in imported if obj.parent is None]
    return rig, roots


def empty(name, coll, position, parent=None, size=0.035):
    obj = bpy.data.objects.new(name, None)
    coll.objects.link(obj)
    obj.empty_display_type = "SPHERE"
    obj.empty_display_size = size
    obj.parent = parent
    obj.location = position
    return obj


def fk_controls(deform, controls, widgets):
    finger_pose = {bone.name: bone.matrix_basis.copy() for bone in deform.pose.bones if "Finger" in bone.name}
    wrist_pose = {side: deform.matrix_world @ deform.pose.bones[f"B {side} Hand"].matrix
                  for side in ("L", "R")}
    rig = deform.copy()
    rig.data = deform.data.copy()
    rig.name = "SpyHands_CONTROLS"
    rig.show_in_front = True
    controls.objects.link(rig)
    deform.animation_data_clear()
    rig.animation_data_clear()
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for side in ("L", "R"):
        for joint, child in (("UpperArm", "ForeArm"), ("ForeArm", "Hand")):
            control = rig.data.edit_bones[f"B {side} {joint}"]
            control.tail = rig.data.edit_bones[f"B {side} {child}"].head
    for source in deform.data.bones:
        output = rig.data.edit_bones.new("OUT_" + source.name)
        output.length = source.length
        output.matrix = source.matrix_local
        output.parent = rig.data.edit_bones[source.name]
    bpy.ops.object.mode_set(mode="OBJECT")

    ring = shape("Spy hand FK control", 0.018, widgets)
    for bone in rig.pose.bones:
        bone.matrix_basis = finger_pose.get(bone.name, Matrix.Identity(4))
        bone.bone.use_deform = False
        is_arm = bone.name.startswith(("B L ", "B R "))
        if is_arm and any(part in bone.name for part in ("Arm", "Hand", "Finger", "Clavicle")):
            bone.custom_shape = ring
        else:
            bone.bone.hide = True
    for bone in deform.pose.bones:
        constraint = bone.constraints.new("COPY_TRANSFORMS")
        constraint.name = "Follow authoring controls"
        constraint.target = rig
        constraint.subtarget = "OUT_" + bone.name
        constraint.owner_space = constraint.target_space = "POSE"
    return rig, wrist_pose


def blend_control(constraint, rig, key):
    driver = constraint.driver_add("influence").driver
    driver.expression = "blend"
    variable = driver.variables.new()
    variable.name = "blend"
    variable.targets[0].id = rig
    variable.targets[0].data_path = f'["{key}"]'


def hand_ik(rig, controls, side, reference):
    right = side == "R"
    wrist = empty(f"{side}_WRIST_IK", controls, (0.33, -0.165, 0.495) if right else (0.32, -0.09, 0.455))
    wrist.rotation_euler = reference.to_euler()
    pole = empty(f"{side}_ELBOW_POLE", controls, (0.12, -0.30 if right else 0.25, 0.23))
    shoulder = rig.matrix_world @ rig.pose.bones[f"B {side} UpperArm"].head
    forearm = rig.pose.bones[f"B {side} ForeArm"]
    constraint = forearm.constraints.new("IK")
    constraint.name = f"{side} hand contact"
    constraint.target = wrist
    constraint.pole_target = pole
    constraint.chain_count = 2
    constraint.use_stretch = False
    axis = (wrist.location - shoulder).normalized()
    pole_direction = pole.location - shoulder
    pole_direction -= axis * pole_direction.dot(axis)
    best = (float("-inf"), 0.0)
    for index in range(36):
        angle = index * math.tau / 36 - math.pi
        constraint.pole_angle = angle
        bpy.context.view_layer.update()
        elbow = rig.matrix_world @ forearm.head
        score = (elbow - shoulder).dot(pole_direction)
        best = max(best, (score, angle))
    constraint.pole_angle = best[1]

    key = f"ik_{side}_hand"
    rig[key] = 1.0
    rig.id_properties_ui(key).update(min=0.0, max=1.0, description="Blend between FK and the wrist target")
    blend_control(constraint, rig, key)
    rotation = rig.pose.bones[f"B {side} Hand"].constraints.new("COPY_ROTATION")
    rotation.target = wrist
    blend_control(rotation, rig, key)


def build(chars, output):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.fps = 30
    model = collection("Spy FPV | converted meshes")
    controls = collection("Spy FPV | authoring controls")
    widgets = collection("Spy FPV | hidden control shapes")
    arms, roots = import_rig(chars / "SPerso/ATT_01_fp.glb", "SpyHands_EXPORT", model)
    for root in roots:
        root.matrix_world = Matrix.Rotation(math.pi / 2, 4, "Z") @ root.matrix_world
    actions = list(bpy.data.actions)
    for action in actions:
        action.use_fake_user = True
        action.asset_mark()
    ready = next(action for action in actions if action.name.split(".")[0].lower() == "waitstspfd2")
    arms.animation_data.action = ready
    scene.frame_start = round(ready.frame_range[0])
    scene.frame_end = round(ready.frame_range[1])
    scene.frame_set(scene.frame_start)
    rig, wrists = fk_controls(arms, controls, widgets)
    for side in ("L", "R"):
        hand_ik(rig, controls, side, wrists[side])

    shocker, roots = import_rig(chars / "SWeaponFirst/p190_fp.glb", "Shocker_EXPORT", model)
    for action in set(bpy.data.actions) - set(actions):
        action.use_fake_user = True
        action.asset_mark()
    shocker.animation_data_clear()
    grip = empty("P190_GRIP", controls, (0, 0, 0))
    grip.parent = rig
    grip.parent_type = "BONE"
    grip.parent_bone = "B R Hand"
    bpy.context.view_layer.update()
    wrist = rig.matrix_world @ rig.pose.bones["B R Hand"].head
    grip.matrix_world = Matrix.Translation(wrist)
    for root in roots:
        rest = root.matrix_world.copy()
        root.parent = grip
        root.matrix_basis = rest
    empty("P190_TRIGGER_CONTACT", controls, (0.055, 0.025, 0.047), grip, 0.018)
    empty("P190_SUPPORT_CONTACT", controls, (0.038, 0.060, -0.037), grip, 0.024)
    empty("INTERACTION_CONTACT", controls, (0.56, 0.0, 0.43))
    camera_data = bpy.data.cameras.new("Spy viewmodel preview")
    camera = bpy.data.objects.new("FPV_CAMERA", camera_data)
    controls.objects.link(camera)
    camera.location = (0.02, 0.0, 0.66)
    camera.rotation_euler = Vector((1.0, 0.0, 0.0)).to_track_quat("-Z", "Y").to_euler()
    camera_data.lens = 24
    camera_data.clip_start = 0.02
    scene.camera = camera

    instructions = bpy.data.texts.new("START HERE")
    instructions.write(
        "Spy FPV authoring reference\n\n"
        "Converted models keep the original UVs, bind poses and surface names.\n"
        "The controls start in a camera-ready stance with the stock aimed finger pose.\n"
        "Use each hand's IK slider, wrist and elbow targets for contact. P190_GRIP adjusts\n"
        "the weapon in the right hand; the trigger, support and interaction targets mark\n"
        "contact positions. The P190's original actions and two effect joints stay available.\n"
        "This rig is for local authoring. The chars pack includes the arms and P190 for the drawn stance.\n"
    )
    bpy.ops.file.pack_all()
    bpy.ops.object.select_all(action="DESELECT")
    arms.hide_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")
    output.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(output))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, required=True)
    parser.add_argument("--chars", type=Path)
    parser.add_argument("--replace", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    repo = args.repo.resolve()
    output = repo / "local/spy-viewmodel/spy-viewmodel.blend"
    if output.exists() and not args.replace:
        print(f"Keeping edited workspace: {output}")
    else:
        build(args.chars.resolve() if args.chars else repo / ".content/shared/chars", output)
