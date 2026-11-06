"""Build close-up Spy render meshes from converted content without changing the world meshes."""

from pathlib import Path

import numpy as np

from .sidearm_models import append, values
from .writers.glb import ARRAY_BUFFER, ELEMENT_ARRAY_BUFFER, pack_glb, read_glb


ARMS = "SPerso/ATT_01"
SHOCKER = "SWeaponFirst/p190"
ARM_CLIPS = frozenset(name.lower() for name in (
    "WaitStAlFd0", "WaitCrAlFd0", "WaitStSpFd2", "WaitCrSpFd2",
    "DrawStSpBg2", "DrawStSpEd2", "DrawCrSpBg2", "DrawCrSpEd2",
))
# The stock FX is ahead of the upper electrode. Put the sight source at the
# front of the lower emitter, in metres in the animated FX frame (+X forward).
LASER_OFFSET = (-0.016, -0.027, 0.0)


def arm_joints(names, side):
    parts = ("UpperArm", "ForeArm", "Hand", "Finger")
    return [index for index, name in enumerate(names)
            if any(name.startswith(f"B {side} {part}") for part in parts)
            or name == f"Bip01 {side} UpperArm_RollBone"]


def bind_sleeves(attributes, sides, clavicles):
    """Keep the sleeve cut attached when the first-person shoulders move independently of the spine."""
    joints = attributes["JOINTS_0"]
    weights = attributes["WEIGHTS_0"]
    arm_weights = [np.where(np.isin(joints, side), weights, 0.).sum(axis=1) for side in sides]
    right = arm_weights[1] > arm_weights[0]
    for arm, clavicle, selected in zip(sides, clavicles, (~right, right)):
        outside = (weights > 0) & ~np.isin(joints, [clavicle, *arm])
        for vertex in np.flatnonzero(selected & outside.any(axis=1)):
            slots = np.flatnonzero(outside[vertex])
            existing = np.flatnonzero((joints[vertex] == clavicle) & (weights[vertex] > 0))
            target = existing[0] if existing.size else slots[0]
            amount = weights[vertex, slots].sum()
            joints[vertex, slots] = 0
            weights[vertex, slots] = 0
            joints[vertex, target] = clavicle
            weights[vertex, target] += amount


def midpoint_skin(joints, weights, endpoints):
    """Interpolate a skin edge and keep the four strongest joint influences."""
    result_joints = np.zeros((len(endpoints), 4), dtype=joints.dtype)
    result_weights = np.zeros((len(endpoints), 4), dtype=weights.dtype)
    for index, (first, second) in enumerate(endpoints):
        influences = {}
        for vertex in (first, second):
            for joint, weight in zip(joints[vertex], weights[vertex]):
                if weight > 0:
                    influences[joint] = influences.get(joint, 0.) + weight
        strongest = sorted(influences, key=lambda joint: (-influences[joint], joint))[:4]
        normalized = np.array([influences[joint] for joint in strongest])
        result_joints[index, :len(strongest)] = strongest
        result_weights[index, :len(strongest)] = normalized / normalized.sum()
    return result_joints, result_weights


def refined_gloves(attributes, triangles, glove_joints):
    """Subdivide once while keeping wedges split and physical seam edges together."""
    positions = attributes["POSITION"]
    normals = attributes["NORMAL"]
    joints = attributes["JOINTS_0"]
    weights = attributes["WEIGHTS_0"]
    corners = np.concatenate((triangles[:, [0, 1]], triangles[:, [1, 2]], triangles[:, [2, 0]]))
    edges, midpoint = np.unique(np.sort(corners, axis=1), axis=0, return_inverse=True)
    _, welded, wedge = np.unique(np.round(positions, 6), axis=0, return_index=True, return_inverse=True)
    edge_ids = np.sort(wedge[edges], axis=1)
    physical, physical_id = np.unique(edge_ids, axis=0, return_inverse=True)
    endpoints = welded[physical]

    # Use the same position and skin for both UV/normal wedges of a physical edge.
    end_positions = positions[endpoints]
    mid_positions = end_positions.mean(axis=1)
    end_normals = np.zeros((len(welded), 3), dtype=normals.dtype)
    np.add.at(end_normals, wedge, normals)
    end_normals /= np.maximum(np.linalg.norm(end_normals, axis=1, keepdims=True), 1e-6)
    edge_normals = end_normals[physical]
    distance = ((mid_positions[:, None] - end_positions) * edge_normals).sum(axis=2)
    rounding = -(distance[..., None] * edge_normals).mean(axis=1)
    rounding *= np.minimum(1., .002 / np.maximum(np.linalg.norm(rounding, axis=1, keepdims=True), 1e-6))
    glove_influence = np.where(np.isin(joints, glove_joints), weights, 0.).sum(axis=1)
    glove_edges = (glove_influence[endpoints] >= .75).all(axis=1)
    boundary = np.bincount(physical_id[midpoint], minlength=len(physical)) != 2
    mid_positions[glove_edges & ~boundary] += rounding[glove_edges & ~boundary]
    mid_joints, mid_weights = midpoint_skin(joints, weights, endpoints)

    midpoint_attributes = {name: attr[edges].mean(axis=1) for name, attr in attributes.items()
                           if name not in {"POSITION", "JOINTS_0", "WEIGHTS_0"}}
    midpoint_attributes["POSITION"] = mid_positions[physical_id]
    midpoint_attributes["JOINTS_0"] = mid_joints[physical_id]
    midpoint_attributes["WEIGHTS_0"] = mid_weights[physical_id]
    mid_normals = midpoint_attributes["NORMAL"]
    mid_normals /= np.maximum(np.linalg.norm(mid_normals, axis=1, keepdims=True), 1e-6)

    a, b, c = triangles.T
    ab, bc, ca = (midpoint.reshape(3, -1) + len(positions))
    refined = np.stack((a, ab, ca, ab, b, bc, ca, bc, c, ab, bc, ca), axis=1).reshape(-1, 3)
    return ({name: np.concatenate((attr, midpoint_attributes[name])) for name, attr in attributes.items()},
            refined)


def arms_only(document, data):
    """Take the two sleeve/hand islands by skin membership, preserving authored seams."""
    joints = document["skins"][0]["joints"]
    names = [document["nodes"][index]["name"] for index in joints]
    left, right = (arm_joints(names, side) for side in ("L", "R"))
    if not left or not right:
        raise ValueError("Spy viewmodel has no stock arm joints")

    clavicles = [names.index(f"B {side} Clavicle") for side in ("L", "R")]
    arm_count = np.zeros(2, dtype=int)
    gloves = [index for index in left + right if "Hand" in names[index] or "Finger" in names[index]]
    for mesh in document["meshes"]:
        primitives = []
        for primitive in mesh["primitives"]:
            attributes = primitive["attributes"]
            triangles = values(document, data, primitive["indices"]).reshape(-1, 3)
            weights = values(document, data, attributes["WEIGHTS_0"])
            bone = values(document, data, attributes["JOINTS_0"])
            sides = []
            for side in (left, right):
                influence = np.where(np.isin(bone, side), weights, 0).sum(axis=1)
                sides.append(np.all(influence[triangles] >= 0.5, axis=1))
            arm_count += [np.count_nonzero(mask) for mask in sides]
            selected = triangles[sides[0] | sides[1]]
            if selected.size == 0:
                continue

            used, indices = np.unique(selected.ravel(), return_inverse=True)
            skin = {name: values(document, data, index)[used] for name, index in attributes.items()}
            bind_sleeves(skin, (left, right), clavicles)
            skin, indices = refined_gloves(skin, indices.reshape(-1, 3), gloves)
            cropped = {}
            for name, index in attributes.items():
                accessor = document["accessors"][index]
                cropped[name] = append(document, data, skin[name],
                                       accessor["type"], accessor["componentType"], ARRAY_BUFFER)
            index_type = document["accessors"][primitive["indices"]]["componentType"]
            primitives.append({
                **primitive,
                "attributes": cropped,
                "indices": append(document, data, indices.ravel(), "SCALAR", index_type, ELEMENT_ARRAY_BUFFER),
            })
        mesh["primitives"] = primitives
    if np.any(arm_count == 0):
        raise ValueError("Spy viewmodel crop lost an arm")

    document["animations"] = [clip for clip in document.get("animations", [])
                              if clip["name"].lower() in ARM_CLIPS]


def add_laser(document, data):
    """Add a separate visual beam socket without touching the stock FX or mechanism tracks."""
    skin = document["skins"][0]
    joints = skin["joints"]
    fx_index, fx = next((slot, node) for slot, node in enumerate(joints)
                        if document["nodes"][node]["name"] == "FX")
    laser = len(document["nodes"])
    document["nodes"][fx].setdefault("children", []).append(laser)
    document["nodes"].append({"name": "Laser", "translation": list(LASER_OFFSET)})
    joints.append(laser)

    bindings = values(document, data, skin["inverseBindMatrices"])
    # glTF matrices are column major. Inverse bind for FX * local is
    # inverse(local) * inverse(FX); a translation only changes its last column.
    laser_binding = bindings[fx_index].copy()
    laser_binding[12:15] -= LASER_OFFSET
    skin["inverseBindMatrices"] = append(document, data, np.vstack((bindings, laser_binding)), "MAT4")


def compact(document, data):
    """Drop unused geometry and animation buffers while preserving the selected data verbatim."""
    references = []
    for mesh in document["meshes"]:
        for primitive in mesh["primitives"]:
            references.append((primitive, "indices"))
            references.extend((primitive["attributes"], name) for name in primitive["attributes"])
    for skin in document["skins"]:
        references.append((skin, "inverseBindMatrices"))
    for clip in document["animations"]:
        for sampler in clip["samplers"]:
            references.extend(((sampler, "input"), (sampler, "output")))

    source_accessors = document["accessors"]
    accessor_indices = sorted({owner[key] for owner, key in references})
    accessor_map = {old: new for new, old in enumerate(accessor_indices)}
    for owner, key in references:
        owner[key] = accessor_map[owner[key]]
    document["accessors"] = accessors = [source_accessors[index] for index in accessor_indices]

    # Converted character textures are external. Preserve a local authored
    # character's embedded images too when copying the referenced buffer views.
    view_references = [*accessors, *(image for image in document.get("images", []) if "bufferView" in image)]
    view_indices = sorted({entry["bufferView"] for entry in view_references})
    view_map = {old: new for new, old in enumerate(view_indices)}
    source_views = document["bufferViews"]
    packed = bytearray()
    document["bufferViews"] = views = []
    for index in view_indices:
        view = source_views[index]
        start = view.get("byteOffset", 0)
        packed.extend(b"\0" * (-len(packed) % 4))
        views.append({**view, "byteOffset": len(packed)})
        packed.extend(data[start:start + view["byteLength"]])
    for entry in view_references:
        entry["bufferView"] = view_map[entry["bufferView"]]
    return packed


def install_models(chars_dir: Path) -> list[str]:
    """Generate the drawn-stance arms and P190 alongside their stock textures."""
    outputs = []
    for name, build in ((ARMS, arms_only), (SHOCKER, add_laser)):
        path = chars_dir / (name + ".glb")
        if not path.is_file():
            continue
        document, source = read_glb(path.read_bytes())
        data = bytearray(source)
        build(document, data)
        metadata = document["extras"]["svmap"]
        metadata["mesh"] = name.replace("/", ".") + "_fp"
        metadata["bones"] = len(document["skins"][0]["joints"])
        output = path.with_name(path.stem + "_fp.glb")
        outputs.append((output, pack_glb(document, compact(document, data))))

    for path, payload in outputs:
        path.write_bytes(payload)
    return [path.relative_to(chars_dir).as_posix() for path, _ in outputs]
