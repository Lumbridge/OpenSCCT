"""Author an original compact tube optic at real-world scale; no third-party model is used."""
from pathlib import Path
import math
import sys
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parent / 'export'
bpy.ops.wm.read_factory_settings(use_empty=True)


def material(name, rgba, metal=0., rough=.6):
    image = bpy.data.images.new(name, width=8, height=8, alpha=True)
    image.pixels = list(rgba) * 64
    image.filepath_raw = str(ROOT / 'textures' / (name + '.png')); image.file_format = 'PNG'; image.save()
    mat = bpy.data.materials.new(name); mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    texture = mat.node_tree.nodes.new('ShaderNodeTexImage'); texture.image = image
    mat.node_tree.links.new(texture.outputs['Color'], bsdf.inputs['Base Color'])
    bsdf.inputs['Metallic'].default_value = metal; bsdf.inputs['Roughness'].default_value = rough
    bsdf.inputs['Alpha'].default_value = rgba[3]
    return mat


black = material('OpticAnodized', (.11, .13, .12, 1), .6, .72)
rubber = material('OpticRubber', (.014, .016, .017, 1), 0, .85)
steel = material('OpticFasteners', (.11, .12, .13, 1), .8, .4)
glass = material('OpticLens', (.11, .28, .30, .09), .1, .15)
red = material('OpticReticle', (1, .008, .002, 1), 0, .4)
red.node_tree.nodes['Principled BSDF'].inputs['Emission Color'].default_value = (1, .005, 0, 1)
red.node_tree.nodes['Principled BSDF'].inputs['Emission Strength'].default_value = 1


def finish(obj, mat, bevel=0.):
    obj.data.materials.append(mat)
    if bevel:
        edge = obj.modifiers.new('Machined edge radii', 'BEVEL'); edge.width = bevel; edge.segments = 3
        obj.modifiers.new('Weighted normals', 'WEIGHTED_NORMAL')
    for polygon in obj.data.polygons: polygon.use_smooth = True
    return obj


def box(name, centre, scale, mat=black, bevel=.001):
    bpy.ops.mesh.primitive_cube_add(size=1, location=centre)
    obj = bpy.context.object; obj.name = name; obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(obj, mat, bevel)


def cylinder(name, centre, radius, depth, axis, mat=black):
    bpy.ops.mesh.primitive_cylinder_add(vertices=48, radius=radius, depth=depth, location=centre)
    obj = bpy.context.object; obj.name = name
    obj.rotation_euler = Vector(axis).to_track_quat('Z', 'Y').to_euler()
    return finish(obj, mat, .0006)


def ring(x, major, minor, mat):
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, major_segments=64, minor_segments=12,
        location=(x, 0, 0), rotation=(0, math.pi/2, 0))
    return finish(bpy.context.object, mat)


# The housing is hollow all the way through. Rear/forward rims, seals and a stepped objective look machined.
vertices, faces = [], []
for x, radius in [(-.035, .018), (-.031, .019), (.026, .019), (.033, .0175), (.033, .0145), (-.035, .0145)]:
    vertices.extend((x, math.cos(i*math.tau/64)*radius, math.sin(i*math.tau/64)*radius) for i in range(64))
for row in range(6):
    for i in range(64): faces.append((row*64+i, row*64+(i+1)%64, ((row+1)%6)*64+(i+1)%64, ((row+1)%6)*64+i))
mesh = bpy.data.meshes.new('Hollow machined housing'); mesh.from_pydata(vertices, [], faces); mesh.update()
body = bpy.data.objects.new('Tube optic housing', mesh); bpy.context.scene.collection.objects.link(body); finish(body, black)
for x in [-.033, .030]: ring(x, .017, .001, rubber)
for x in [-.027, .023]: ring(x, .0191, .0007, black)
box('Integral saddle', (0, 0, -.020), (.050, .026, .010))
box('Receiver riser', (0, 0, -.0315), (.045, .025, .017), bevel=.0015)
box('Low rail mount', (0, 0, -.0445), (.059, .031, .009), bevel=.0018)
box('Rail clamp jaw', (0, -.019, -.0445), (.043, .007, .010), steel, .0008)
box('Recoil lug', (.012, 0, -.051), (.006, .021, .006), steel, .0005)
for x in [-.016, .016]:
    cylinder('Mount clamp screw', (x, -.024, -.0445), .004, .005, (0, 1, 0), steel)
    box('Screw driver slot', (x, -.027, -.0445), (.004, .0005, .0008), rubber, .0001)
cylinder('Battery housing', (.004, -.022, 0), .0105, .013, (0, 1, 0))
cylinder('Battery cap', (.004, -.030, 0), .011, .0045, (0, 1, 0), rubber)
for i in range(24):
    a = i*math.tau/24
    rib = box('Battery cap knurl', (.004+math.cos(a)*.0106, -.031, math.sin(a)*.0106), (.0013, .003, .0011), black, .0002)
    rib.rotation_euler[1] = -a
cylinder('Elevation turret', (-.012, 0, .021), .006, .008, (0, 0, 1))
cylinder('Elevation cap', (-.012, 0, .026), .0067, .0035, (0, 0, 1), rubber)
box('Elevation coin slot', (-.012, 0, .028), (.007, .001, .0005), steel, .0001)
cylinder('Windage turret', (-.012, .020, 0), .0055, .007, (0, 1, 0))
cylinder('Windage cap', (-.012, .024, 0), .0061, .003, (0, 1, 0), rubber)
# Thin tinted optical faces stay transparent; the reticle is tiny and centred, not a painted crosshair.
for x in [-.030, .027]: cylinder('Coated optical lens', (x, 0, 0), .0143, .0004, (1, 0, 0), glass)
cylinder('Collimated red point', (.0267, 0, 0), .0004, .00005, (1, 0, 0), red)
for obj in bpy.context.scene.objects:
    if obj.type == 'MESH':
        bpy.context.view_layer.objects.active = obj; obj.select_set(True)
        for mod in list(obj.modifiers): bpy.ops.object.modifier_apply(modifier=mod.name)
        obj.select_set(False)
sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from svmbuild.sidearm_models import read, write

def export(name):
    bpy.ops.export_scene.gltf(filepath=str(ROOT / name), export_format='GLB', export_animations=False)
    doc, blob = read(ROOT / name)
    for image in doc['images']:
        image.pop('bufferView', None); image.pop('mimeType', None)
        image['uri'] = 'textures/' + image['name'] + '.png'
    for mat in doc['materials']:
        mat['name'] = 'OpenScctRifles.' + mat['name']
        mat['doubleSided'] = True
        if 'Lens' in mat['name']: mat['alphaMode'] = 'BLEND'
        if 'Reticle' in mat['name']:
            mat['extras'] = {'scct_self_illum': 'textures/OpticReticle.png'}
            mat.setdefault('extensions', {})['KHR_materials_unlit'] = {}
            if 'KHR_materials_unlit' not in doc.setdefault('extensionsUsed', []): doc['extensionsUsed'].append('KHR_materials_unlit')
    write(ROOT / name, doc, blob)

export('micro_red_dot.glb')
