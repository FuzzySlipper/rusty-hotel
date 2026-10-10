# Prepares the first-person arms from the Concierge's prepared master (art/residents-02/concierge.blend): no new source.
#   blender -b art/residents-02/concierge.blend --python art/arms-01/prepare.py
# Keeps the vertices skinned mostly to the arms (upper arm, forearm, hand and their twist bones, both sides) and the whole
# Tripo skeleton, turns the model to face -Z with its right shoulder (R_Upperarm) at the origin, and exports
# content/models/held/arms.glb with the idle clip alone (the game poses the arms by two-bone IK on the held item's grips). Prints
# the arm joints' rest positions in glTF space (+Y up, -Z forward) for content/combat/arms.json, and writes them to
# art/arms-01/rig.json.
import bpy, json, math, os
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
ARM = ("Upperarm", "Forearm", "Hand")

arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
mesh = next(o for o in bpy.data.objects if o.type == "MESH" and any(m.type == "ARMATURE" for m in o.modifiers))
for o in [o for o in bpy.data.objects if o not in (arm, mesh)]:
    bpy.data.objects.remove(o, do_unlink=True)
# The Engine admits a skinned model with its clips: the idle stays (the arm chains are posed by IK over it).
for action in list(bpy.data.actions):
    if action.name != "idle": bpy.data.actions.remove(action)

# Keep a vertex when the bone with its largest weight is part of an arm.
names = {g.index: g.name for g in mesh.vertex_groups}
def keep(v):
    weights = [(g.weight, names[g.group]) for g in v.groups]
    return bool(weights) and any(part in max(weights)[1] for part in ARM)
bpy.context.view_layer.objects.active = mesh
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="DESELECT")
bpy.ops.object.mode_set(mode="OBJECT")
for v in mesh.data.vertices:
    v.select = not keep(v)
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.delete(type="VERT")
bpy.ops.object.mode_set(mode="OBJECT")

# Face -Z (Blender +Y) from +X, with the right shoulder at the origin.
turn = Matrix.Rotation(math.radians(90), 4, "Z")
shoulder = turn @ (arm.matrix_world @ arm.data.bones["R_Upperarm"].head_local)
arm.matrix_world = Matrix.Translation(-shoulder) @ turn @ arm.matrix_world
bpy.context.view_layer.update()

def gltf(point):  # Blender (x, y, z) is glTF (x, z, -y).
    return [round(point.x, 4), round(point.z, 4), round(-point.y, 4)]
rig = {b: gltf(arm.matrix_world @ arm.data.bones[b].head_local) for b in
       ("R_Upperarm", "R_Forearm", "R_Hand", "L_Upperarm", "L_Forearm", "L_Hand")}
print("ARMS", json.dumps(rig), "vertices", len(mesh.data.vertices))
json.dump(rig, open(os.path.join(HERE, "rig.json"), "w"), indent=2)

out = os.path.join(ROOT, "content", "models", "held", "arms.glb")
bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", export_yup=True, export_image_format="JPEG", export_animation_mode="ACTIONS", export_force_sampling=True)
print("EXPORTED", out)
