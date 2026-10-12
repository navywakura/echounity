"""Original ECHO-5 mechanical arachnid. Run with Blender 4.5 LTS:

blender --background --factory-startup --python Tools/build_arachnid.py -- --render

Exports an editable .blend, FBX, glTF and a measured geometry receipt. All model
objects belong to ECHO5_Arachnid and descend from Chassis_Root. Studio objects
are in a separate collection and are excluded from both game exports.
"""
import argparse
import json
import math
from pathlib import Path
import sys

import bpy
import bmesh
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / "ArtSource/ECHO5_Arachnid"
EXPORT = ROOT / "Assets/EchoArachnid/Models"
args = argparse.ArgumentParser()
args.add_argument("--render", action="store_true")
options = args.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
ART.mkdir(parents=True, exist_ok=True)
EXPORT.mkdir(parents=True, exist_ok=True)

# This runs in a fresh scene: never clear a user's loaded project implicitly.
for obj in list(bpy.data.objects):
    bpy.data.objects.remove(obj, do_unlink=True)
for col in list(bpy.data.collections):
    bpy.data.collections.remove(col)
scene = bpy.context.scene
scene.unit_settings.system = "METRIC"
scene.unit_settings.scale_length = 1.0
model = bpy.data.collections.new("ECHO5_Arachnid")
scene.collection.children.link(model)


def linear(hex_color):
    rgb = [int(hex_color[i:i+2], 16) / 255 for i in (0, 2, 4)]
    return tuple(v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4 for v in rgb) + (1,)


def material(name, color, metallic, roughness, emission=0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    rgba = linear(color)
    mat.diffuse_color = rgba
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = rgba
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    if emission:
        bsdf.inputs["Emission Color"].default_value = rgba
        bsdf.inputs["Emission Strength"].default_value = emission
    return mat


dark = material("Mat_Chassis_Dark", "1E2024", .8, .35)
joint = material("Mat_Mechanical_Joints", "56616C", .85, .26)
eye = material("Mat_ECHO_Eye_Emission", "00E5FF", .25, .16, 5.0)
bronze = material("Mat_Copper_Trim", "C38152", .78, .3)
rubber = material("Mat_Grip_Rubber", "12181C", .05, .6)
panel = material("Mat_Armor_Panels", "34414A", .8, .3)
glass = material("Mat_Optical_Glass", "061B22", .55, .14)


def organize(obj, name, parent=None):
    obj.name = name
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    model.objects.link(obj)
    if parent:
        matrix = obj.matrix_world.copy()
        obj.parent = parent
        obj.matrix_world = matrix
    return obj


def empty(name, position, parent=None):
    obj = bpy.data.objects.new(name, None)
    model.objects.link(obj)
    obj.location = position
    obj.empty_display_type = "PLAIN_AXES"
    obj.empty_display_size = .025
    bpy.context.view_layer.update()
    if parent:
        matrix = obj.matrix_world.copy()
        obj.parent = parent
        obj.matrix_world = matrix
    return obj


root = empty("Chassis_Root", (0, 0, 0))
root["asset"] = "ECHO-5 / original mechanical arachnid"
root["forward_axis_blender"] = "-Y"
root["ground_m"] = 0.0
root["license"] = "CC0-1.0; original geometry generated for this project"
body = empty("Body_Pivot", (0, 0, 0), root)


def finish(obj, mat, bevel=0):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(mat)
    if bevel:
        modifier = obj.modifiers.new("Machined edge bevel", "BEVEL")
        modifier.width = bevel
        modifier.segments = 2
        modifier.affect = "EDGES"
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(obj.data)
    bm.free()
    for face in obj.data.polygons:
        face.use_smooth = True
    # Blender 4.1+ replaces the old auto_smooth checkbox with sharp edges by angle.
    obj.data.set_sharp_from_angle(angle=math.radians(35))
    obj.data.update()
    return obj


def box(name, p, size, mat, parent=body, bevel=.006, rotation=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=p)
    obj = bpy.context.object
    obj.scale = size
    obj.rotation_euler = rotation
    finish(obj, mat, bevel)
    return organize(obj, name, parent)


def ball(name, p, scale, mat, parent=body, subdivisions=2):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdivisions, radius=1, location=p)
    obj = bpy.context.object
    obj.scale = (scale,) * 3 if isinstance(scale, (float, int)) else scale
    finish(obj, mat)
    return organize(obj, name, parent)


def rod(name, a, b, radius, mat, parent=body, radius2=None, vertices=10):
    a, b = Vector(a), Vector(b)
    bpy.ops.mesh.primitive_cone_add(vertices=vertices, radius1=radius,
                                    radius2=radius if radius2 is None else radius2,
                                    depth=(b-a).length, location=(a+b)*.5)
    obj = bpy.context.object
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = (b-a).to_track_quat("Z", "Y")
    finish(obj, mat, min(.002, radius * .12))
    return organize(obj, name, parent)


def beam(name, a, b, width, thickness, mat, parent):
    a, b = Vector(a), Vector(b)
    bpy.ops.mesh.primitive_cube_add(size=1, location=(a+b)*.5)
    obj = bpy.context.object
    obj.scale = (width, thickness, (b-a).length)
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = (b-a).to_track_quat("Z", "Y")
    finish(obj, mat, .004)
    return organize(obj, name, parent)


def armor_slice(name, center, width, length, height, parent=body):
    # Closed barrel cross-section: planar facets, a raised central spine and a flat base.
    outline = [(-.5, 0), (-.5, .37), (-.35, .82), (0, 1), (.35, .82), (.5, .37), (.5, 0)]
    verts = [(center[0]+x*width, center[1]+side*length/2, center[2]+z*height)
             for side in (-1, 1) for x, z in outline]
    n = len(outline)
    faces = [tuple(reversed(range(n))), tuple(range(n, 2*n))]
    faces += [(i, (i+1)%n, (i+1)%n+n, i+n) for i in range(n)]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    model.objects.link(obj)
    finish(obj, panel, .005)
    organize(obj, name, parent)
    return obj


# Low, forward-oriented armored chassis and a larger segmented abdomen.
box("Cefalothorax_Underframe", (0, -.105, .235), (.25, .31, .075), dark, bevel=.022)
box("Thorax_Armored_Prism", (0, -.115, .295), (.31, .30, .105), dark, bevel=.035)
box("Thorax_Copper_Seam", (0, -.12, .33), (.19, .27, .015), bronze, bevel=.009)
box("Thorax_Top_Shield", (0, -.116, .35), (.225, .255, .04), panel, bevel=.026)
box("Abdomen_Underframe", (0, .172, .225), (.29, .37, .07), rubber, bevel=.035)
for i, (y, w, h) in enumerate(((.045, .31, .127), (.13, .36, .146),
                               (.22, .35, .128), (.305, .26, .09))):
    armor_slice(f"Abdomen_Armor_{i:02d}", (0, y, .25), w, .077, h)
    box(f"Dorsal_Copper_Link_{i:02d}", (0, y, .252+h), (.018, .060, .010), bronze, bevel=.003)
    for side in (-1, 1):
        for j in range(3):
            box(f"Vent_{i}_{side}_{j}", (side*w*.42, y-.024+j*.024, .286),
                (.018, .011, .034), rubber, bevel=.002)
for side in (-1, 1):
    rod(f"Hydraulic_Spine_{side}", (side*.122, -.07, .217), (side*.13, .30, .23), .010, joint)
    for y in (-.21, -.04, .07, .27):
        rod(f"Armor_Bolt_{side}_{y}", (side*.08, y, .354), (side*.08, y, .36), .008, bronze, vertices=6)

head = empty("Head_Pivot", (0, -.23, .295), body)
box("Optical_Faceplate", (0, -.279, .285), (.265, .062, .115), dark, head, .018)
box("Optical_Visor", (0, -.314, .285), (.238, .014, .087), glass, head, .008)
for i, (x, z, radius) in enumerate(((-.071, .293, .027), (.071, .293, .027),
                                   (-.025, .263, .018), (.025, .263, .018))):
    rod(f"Eye_{i}_Bezel", (x, -.318, z), (x, -.346, z), radius*1.25, joint, head, vertices=16)
    rod(f"Eye_{i}_Copper_Rim", (x, -.343, z), (x, -.350, z), radius*1.1, bronze, head, vertices=16)
    lens = ball(f"Eye_{i}_Emission", (x, -.351, z), (radius, radius*.32, radius), eye, head, 3)
    lens["unity_dynamic_emission"] = True
for side in (-1, 1):
    antenna = empty(f"Antenna_{'L' if side<0 else 'R'}_Pivot", (side*.10, -.21, .34), head)
    a = (side*.10, -.21, .34); b = (side*.145, -.327, .414); c = (side*.178, -.414, .429)
    ball(f"Antenna_{side}_Socket", a, .021, bronze, antenna)
    rod(f"Antenna_{side}_Base", a, b, .009, joint, antenna)
    rod(f"Antenna_{side}_Whisker", b, c, .006, dark, antenna, radius2=.003)
    ball(f"Antenna_{side}_ProximityTip", c, .010, eye, antenna)

leg_records = []
for side, label in ((-1, "L"), (1, "R")):
    for i, y in enumerate((-.19, -.055, .10, .245)):
        spread = (-.14, -.052, .055, .145)[i]
        p0 = Vector((side*.127, y, .235)); p1 = Vector((side*.223, y+spread*.32, .252))
        p2 = Vector((side*(.350 if i in (0, 3) else .375), y+spread, .369))
        p3 = Vector((side*(.470 if i in (0, 3) else .503), y+spread*1.35, .031))
        prefix = f"Leg_{label}{i}"
        coxa = empty(prefix+"_Coxa", p0, root)
        femur = empty(prefix+"_Femur", p1, coxa)
        tibia = empty(prefix+"_Tibia", p2, femur)
        foot = empty(prefix+"_Foot", p3, tibia)
        ball(prefix+"_HipBearing", p0, .031, joint, coxa)
        beam(prefix+"_CoxaArmor", p0, p1, .047, .043, dark, coxa)
        ball(prefix+"_FemurBearing", p1, .029, bronze, femur)
        beam(prefix+"_FemurArmor", p1, p2, .050, .038, panel, femur)
        offset = Vector((0, .019, .003))
        rod(prefix+"_FemurHydraulic", p1+offset, p2+offset, .007, joint, femur)
        ball(prefix+"_KneeBearing", p2, .027, joint, tibia)
        rod(prefix+"_KneePin", p2+Vector((0, -.033, 0)), p2+Vector((0, .033, 0)), .016, bronze, tibia, vertices=12)
        beam(prefix+"_TibiaArmor", p2, p3, .029, .031, dark, tibia)
        rod(prefix+"_TibiaPiston", p2+offset, p3+offset, .006, joint, tibia)
        ball(prefix+"_Ankle", p3, .020, joint, foot)
        box(prefix+"_GroundPad", (p3.x, p3.y, .012), (.058, .067, .024), rubber, foot, .005)
        box(prefix+"_ToeCap", (p3.x, p3.y-.008, .030), (.043, .043, .016), bronze, foot, .004)
        leg_records.append({'id':prefix,'coxa':list(p0),'femur':list(p1),'tibia':list(p2),
                            'foot':list(p3),'upper_length':(p2-p1).length,'lower_length':(p3-p2).length})

# Merge only surfaces attached to the same rigid pivot, retaining separate eye lenses.
groups = {}
for obj in list(model.objects):
    if obj.type == "MESH" and not obj.name.startswith("Eye_"):
        groups.setdefault(obj.parent, []).append(obj)
for parent, objects in groups.items():
    if len(objects) < 2: continue
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects: obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    bpy.context.object.name = parent.name+"_Surfaces"

bpy.context.view_layer.update()
meshes = [o for o in model.objects if o.type == "MESH"]
vertices, triangles, loose, nonmanifold, negative_volume = 0, 0, 0, 0, []
points = []
for obj in meshes:
    mesh = obj.data; mesh.calc_loop_triangles()
    vertices += len(mesh.vertices); triangles += len(mesh.loop_triangles)
    points.extend(obj.matrix_world @ v.co for v in mesh.vertices)
    bm = bmesh.new(); bm.from_mesh(mesh)
    loose += sum(not v.link_edges for v in bm.verts)
    nonmanifold += sum(not e.is_manifold for e in bm.edges)
    if bm.calc_volume(signed=True) < -1e-8: negative_volume.append(obj.name)
    bm.free()
    assert obj.parent is not None
    assert obj.matrix_world.determinant() > 0
minimum = [min(p[i] for p in points) for i in range(3)]
maximum = [max(p[i] for p in points) for i in range(3)]
assert abs(minimum[2]) < 1e-5, minimum
assert loose == nonmanifold == 0 and not negative_volume
assert triangles < 65000, triangles
receipt = dict(schema="echo.arachnid.geometry.v1",blender=bpy.app.version_string,
               root="Chassis_Root",collection=model.name,units="metres",legs=8,eyes=4,
               meshes=len(meshes),vertices=vertices,triangles=triangles,
               dimensions_m=[maximum[i]-minimum[i] for i in range(3)],bounds_min=minimum,bounds_max=maximum,
               loose_vertices=loose,nonmanifold_edges=nonmanifold,inverted_meshes=negative_volume,
               rig="Rigid pivot hierarchy; three leg segments, foot pivot and head pivot",
               animation="Procedural IK in Unity; geometry is not an inference model",limbs=leg_records)
(ART/"geometry-report.json").write_text(json.dumps(receipt,indent=2)+'\n')

bpy.ops.object.select_all(action="DESELECT")
for obj in model.objects: obj.select_set(True)
bpy.context.view_layer.objects.active=root
bpy.ops.export_scene.fbx(filepath=str(EXPORT/"ECHO5_Arachnid.fbx"),use_selection=True,
    object_types={"EMPTY","MESH"},apply_unit_scale=True,global_scale=1.0,
    axis_forward="-Z",axis_up="Y",use_mesh_modifiers=True,add_leaf_bones=False,
    bake_anim=False,path_mode="AUTO",mesh_smooth_type="EDGE")
bpy.ops.export_scene.gltf(filepath=str(ART/"ECHO5_Arachnid.glb"),export_format="GLB",
    use_selection=True,export_yup=True,export_apply=True)

studio=bpy.data.collections.new("Preview_Studio");scene.collection.children.link(studio)
def studio_link(obj):
    for c in list(obj.users_collection): c.objects.unlink(obj)
    studio.objects.link(obj);return obj
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.004))
floor=studio_link(bpy.context.object);floor.name="Studio floor — not exported"
floor.data.materials.append(material("Studio_Ground","BAC5CE",.05,.6))
def light(name,position,energy,size,color):
    data=bpy.data.lights.new(name,"AREA");data.energy=energy;data.shape="DISK";data.size=size;data.color=color
    obj=bpy.data.objects.new(name,data);studio.objects.link(obj);obj.location=position
    obj.rotation_euler=(Vector((0,0,.2))-obj.location).to_track_quat("-Z","Y").to_euler()
light("Softbox key",(1.5,-2.0,2.7),170,2.1,(.79,.89,1))
light("Copper rim",(-1.5,.9,1.9),210,1.3,(1,.65,.36))
light("Top fill",(.2,1.5,2.4),125,1.6,(.50,.80,1))
camdata=bpy.data.cameras.new("Asset presentation");cam=bpy.data.objects.new("Asset presentation",camdata)
studio.objects.link(cam);cam.location=(1.2,-1.65,1.12)
cam.rotation_euler=(Vector((0,0,.18))-cam.location).to_track_quat("-Z","Y").to_euler()
camdata.type="ORTHO";camdata.ortho_scale=1.54;scene.camera=cam
scene.world.color=(.12,.12,.12)
scene.render.engine="CYCLES";scene.cycles.samples=32;scene.cycles.use_denoising=True
scene.render.threads_mode="FIXED";scene.render.threads=2
scene.render.resolution_x=1500;scene.render.resolution_y=1150;scene.render.resolution_percentage=100
scene.render.image_settings.file_format="PNG";scene.render.filepath=str(ART/"ECHO5_Arachnid_preview.png")
scene.view_settings.view_transform="AgX";scene.render.film_transparent=False
bpy.ops.object.select_all(action="DESELECT");root.select_set(True);bpy.context.view_layer.objects.active=root
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=="VIEW_3D":
            area.spaces.active.shading.type="MATERIAL"
            area.spaces.active.region_3d.view_distance=1.8
            area.spaces.active.region_3d.view_location=(0,0,.2)
            area.spaces.active.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
bpy.ops.wm.save_as_mainfile(filepath=str(ART/"ECHO5_Arachnid.blend"))
print('ECHO_ARACHNID_GEOMETRY '+json.dumps(receipt),flush=True)
if options.render: bpy.ops.render.render(write_still=True)
