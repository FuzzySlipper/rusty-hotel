# Splits the retained TRELLIS.2 spirit meshes into a body and two wings and writes runtime GLBs (no generation call).
#   blender -b --python art/spirits-01/prepare.py -- [piece ...]
# Each native moth faces the source camera (-y) with its wings spread along x. For each piece in pieces.json: turn it
# to face Blender +y (the runtime -z), scale it to "width" across the wings, and centre it. A face whose centre lies
# beyond the hinge ("hinge": a fraction of the half span, x) belongs to that side's wing, unless it lies in one of the
# "keep" boxes ([half-span fraction, height fraction]: within that span and above that height, as the head and the
# antennae are, faces stay with the body). Wings set behind the body ("wingsBehind": a fraction of the depth from the back)
# are taken whole: any face in that back slab off the centre line belongs to its side's wing. Each
# wing is moved so its hinge (at "hingeHeight", a fraction of the height) is its origin; the hinges, in the body's
# frame, are printed for content/spirits/<piece>.json. Metal is softened as for the held items (metallicScale).
# Writes content/models/spirits/<piece>-{body,left,right}.glb.
# Saves the prepared <piece>.blend master (body and wings) beside this script, its textures as JPEG files under textures/.
import bpy, bmesh, json, math, os, sys
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SET = json.load(open(os.path.join(HERE, "pieces.json")))
only = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []

def load(spec):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=os.path.join(HERE, spec["native"]))
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes: o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1: bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
    for o in [o for o in bpy.context.scene.objects if o != obj]: bpy.data.objects.remove(o)
    obj.data.transform(obj.matrix_world)
    obj.matrix_world = Matrix.Identity(4)
    return obj

def prepare(piece, spec):
    obj = load(spec)
    for image in bpy.data.images:
        if image.size[0] > SET["textureSize"]: image.scale(SET["textureSize"], SET["textureSize"])
    soften_metal(obj, SET["metallicScale"])
    glow(obj, SET["glow"])
    bm = bmesh.new(); bm.from_mesh(obj.data)
    bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.pi, 3, "Z"))
    lo = Vector([min(v.co[i] for v in bm.verts) for i in range(3)])
    hi = Vector([max(v.co[i] for v in bm.verts) for i in range(3)])
    centre = (lo + hi) / 2
    scale = spec["width"] / (hi.x - lo.x)
    for v in bm.verts: v.co = (v.co - centre) * scale
    half, height = spec["width"] / 2, (hi.z - lo.z) * scale
    bottom = -height / 2
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bm.to_mesh(obj.data); bm.free()
    obj.data.update()
    hinge_x = spec["hinge"] * half
    back, depth_span = min(v.co.y for v in obj.data.vertices), (hi.y - lo.y) * scale
    behind = spec.get("wingsBehind")
    def side_of(c):
        if behind is not None and c.y < back + behind * depth_span and abs(c.x) > 0.02 * half: return -1 if c.x < 0 else 1
        if abs(c.x) <= hinge_x: return 0
        if any(abs(c.x) < kx * half and c.z > bottom + kz * height for kx, kz in spec["keep"]): return 0
        return -1 if c.x < 0 else 1
    sides = [side_of(p.center) for p in obj.data.polygons]
    hinge_z = bottom + spec["hingeHeight"] * height
    hinges = {}
    out = os.path.join(ROOT, "content", "models", "spirits")
    os.makedirs(out, exist_ok=True)
    for side, name in [(0, "body"), (-1, "left"), (1, "right")]:
        part = obj.copy(); part.data = obj.data.copy(); bpy.context.scene.collection.objects.link(part)
        bm = bmesh.new(); bm.from_mesh(part.data)
        bm.faces.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[f for f, s in zip(bm.faces, sides) if s != side], context="FACES")
        # A wing's origin is its hinge: the body's edge at the wing root, halfway through the wing's depth.
        if side != 0:
            depth = sum(v.co.y for v in bm.verts) / max(1, len(bm.verts))
            hinge = Vector((side * hinge_x, depth, hinge_z))
            for v in bm.verts: v.co -= hinge
            # Blender (x, y, z) is runtime (x, z, -y).
            hinges[name] = [round(hinge.x, 4), round(hinge.z, 4), round(-hinge.y, 4)]
        bm.to_mesh(part.data); bm.free()
        bpy.ops.object.select_all(action="DESELECT"); part.select_set(True)
        bpy.context.view_layer.objects.active = part
        triangles = len(part.data.polygons)
        if triangles > SET["triangleBudget"]: raise SystemExit(f"{piece}-{name}: {triangles} triangles exceed {SET['triangleBudget']}")
        bpy.ops.export_scene.gltf(filepath=os.path.join(out, f"{piece}-{name}.glb"), export_format="GLB", export_yup=True,
            export_apply=True, use_selection=True, export_image_format=SET["imageFormat"])
        print(f"PREPARED {piece}-{name} triangles={triangles}")
        part.name = name
    print(f"HINGES {piece} {json.dumps([hinges['left'], hinges['right']])}")
    # The master keeps the three parts at their origins; the hinges place the wings.
    bpy.data.objects.remove(obj)
    save_master(piece)

# The editable master: the prepared objects in a .blend whose textures are saved beside it as JPEG files under
# textures/, not packed, so the master stays small.
def save_master(piece):
    textures = os.path.join(HERE, "textures")
    os.makedirs(textures, exist_ok=True)
    for i, image in enumerate([im for im in bpy.data.images if im.size[0] > 0]):
        path = os.path.join(textures, f"{piece}-{i}.jpg")
        image.file_format = "JPEG"
        image.save(filepath=path, quality=90)
        if image.packed_file is not None: image.unpack(method="REMOVE")
        image.filepath = bpy.path.relpath(path, start=HERE)
        image.source = "FILE"
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, piece + ".blend"), compress=True, relative_remap=True)


# The Hotel lights props with point lights and ambient only, no reflected environment, so fully metallic surfaces
# read black. Scale the metallic channel (blue) of each metal-rough texture so their base colour shows.
def soften_metal(obj, factor):
    import numpy as np
    for slot in obj.material_slots:
        tree = slot.material.node_tree
        for node in tree.nodes:
            if node.type != "BSDF_PRINCIPLED": continue
            link = next((l for l in tree.links if l.to_socket == node.inputs["Metallic"]), None)
            if link is None:
                node.inputs["Metallic"].default_value *= factor
                continue
            source = link.from_node
            while source.type != "TEX_IMAGE":
                source = next(l.from_node for l in tree.links if l.to_node == source)
            image = source.image
            pixels = np.array(image.pixels[:], dtype=np.float32).reshape(-1, image.channels)
            pixels[:, 2] *= factor
            image.pixels.foreach_set(pixels.ravel())
            image.update()

# Spirits glow faintly with their own colours ("glow", the glTF emissive strength from the base colour texture), so
# they read in the Hotel's dim rooms as something other than furniture.
def glow(obj, strength):
    for slot in obj.material_slots:
        tree = slot.material.node_tree
        for node in tree.nodes:
            if node.type != "BSDF_PRINCIPLED": continue
            link = next((l for l in tree.links if l.to_socket == node.inputs["Base Color"]), None)
            if link is None: continue
            tree.links.new(link.from_socket, node.inputs["Emission Color"])
            node.inputs["Emission Strength"].default_value = strength

for piece, spec in SET["pieces"].items():
    if not only or piece in only: prepare(piece, spec)
