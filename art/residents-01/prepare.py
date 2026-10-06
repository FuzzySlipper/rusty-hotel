# Prepares the residents' runtime GLBs from the retained Tripo downloads (no provider call).
#   blender -b --python art/residents-01/prepare.py -- [resident ...]
# For each resident in pieces.json: imports the rigged Tripo model with its retargeted clips (native/<resident>-<clip>.glb,
# each a whole rigged model carrying one clip), keeps one armature and mesh, and gathers every clip onto it under its
# Hotel name. Clips Tripo did not supply are authored here as keyed actions built from the idle clip's first pose, with
# bones turned and moved in armature space ("authored" in pieces.json: per key, a time in seconds, bone rotations as
# axis and degrees, and bone moves in model units). The model is scaled to the resident's height with its feet at the origin, textures are written
# as 1024 JPEG, and content/models/residents/<resident>.glb is exported with every clip. The prepared .blend master is
# saved beside this script with its textures as JPEG files under textures/.
import bpy, json, math, os, sys
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SET = json.load(open(os.path.join(HERE, "pieces.json")))
only = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
FPS = SET["fps"]

def imported(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    return [o for o in bpy.data.objects if o not in before]

def prepare(name, spec):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = FPS
    # The first clip's file supplies the kept armature and mesh; the others supply only their actions.
    first, *others = spec["clips"].items()
    objects = imported(os.path.join(HERE, "native", f"{name}-{first[1]}.glb"))
    arm = next(o for o in objects if o.type == "ARMATURE")
    def take_action(clip):
        action = arm.animation_data.action if arm.animation_data else None
        action.name = clip; action.use_fake_user = True
        return action
    actions = {first[0]: take_action(first[0])}
    for clip, source in others:
        extra = imported(os.path.join(HERE, "native", f"{name}-{source}.glb"))
        other = next(o for o in extra if o.type == "ARMATURE")
        action = other.animation_data.action
        action.name = clip; action.use_fake_user = True
        actions[clip] = action
        for o in extra: bpy.data.objects.remove(o, do_unlink=True)
    for clip, keys in spec.get("authored", {}).items():
        actions[clip] = author(arm, actions["idle"], clip, keys)
    # Scale to the resident's height with the feet at the origin, measured on the idle pose.
    arm.animation_data.action = actions["idle"]
    bpy.context.scene.frame_set(0)
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    depsgraph = bpy.context.evaluated_depsgraph_get()
    zs = [ (o.evaluated_get(depsgraph).matrix_world @ v.co).z for o in meshes for v in o.evaluated_get(depsgraph).to_mesh().vertices]
    low, high = min(zs), max(zs)
    scale = spec["height"] / (high - low)
    arm.scale = [s * scale for s in arm.scale]
    arm.location.z -= low * scale
    for image in bpy.data.images:
        if image.size[0] > SET["textureSize"]: image.scale(SET["textureSize"], SET["textureSize"])
    out = os.path.join(ROOT, "content", "models", "residents", name + ".glb")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", export_yup=True, export_image_format="JPEG",
        export_animation_mode="ACTIONS", export_force_sampling=True, export_frame_range=False)
    textures = os.path.join(HERE, "textures"); os.makedirs(textures, exist_ok=True)
    for i, image in enumerate([im for im in bpy.data.images if im.size[0] > 0]):
        path = os.path.join(textures, f"{name}-{i}.jpg")
        image.file_format = "JPEG"; image.save(filepath=path, quality=90)
        if image.packed_file is not None: image.unpack(method="REMOVE")
        image.filepath = bpy.path.relpath(path, start=HERE); image.source = "FILE"
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, name + ".blend"), compress=True, relative_remap=True)
    print(f"PREPARED {name} clips={sorted(actions)} scale={scale:.3f} triangles={sum(len(o.data.polygons) for o in meshes)}")

def author(arm, idle, clip, keys):
    """A keyed action: at each key, the idle clip's first pose with the named bones turned about armature-space axes and moved."""
    action = bpy.data.actions.new(clip); action.use_fake_user = True
    bones = arm.pose.bones
    for key in keys:
        arm.animation_data.action = idle
        bpy.context.scene.frame_set(0)
        bpy.context.view_layer.update()
        base = {b.name: b.matrix_basis.copy() for b in bones}
        arm.animation_data.action = action
        for b in bones: b.matrix_basis = base[b.name]
        bpy.context.view_layer.update()
        # Parents before children, so a child's turn applies on its parent's new pose.
        for bone_name, (axis, degrees) in sorted(key["turn"].items(), key=lambda t: len(bones[t[0]].parent_recursive)):
            b = bones[bone_name]
            head = b.matrix.to_translation()
            turn = Matrix.Translation(head) @ Matrix.Rotation(math.radians(degrees), 4, Vector(axis)) @ Matrix.Translation(-head)
            b.matrix = turn @ b.matrix
            bpy.context.view_layer.update()
        for bone_name, offset in key.get("move", {}).items():
            b = bones[bone_name]
            b.matrix = Matrix.Translation(Vector(offset)) @ b.matrix
            bpy.context.view_layer.update()
        frame = round(key["at"] * FPS)
        for b in bones:
            b.rotation_mode = "QUATERNION"
            b.keyframe_insert("rotation_quaternion", frame=frame)
            b.keyframe_insert("location", frame=frame)
    return action

for name, spec in SET["pieces"].items():
    if not only or name in only: prepare(name, spec)
