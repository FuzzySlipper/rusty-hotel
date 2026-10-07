# Normalizes the retained TRELLIS.2 prop meshes into runtime GLBs (no generation call).
#   blender -b --python art/props-02/prepare.py -- [piece ...]
# For each piece in pieces.json: turn it by "turn" quarter turns about up (its front to the fixture frame's +z), scale it
# uniformly so it fits inside "fit" ([width, height, depth] in metres, fixture x, y, z), stand it on the ground with its
# origin at the centre of its base, weld and drop degenerate faces, downsize textures to "textureSize" as JPEG and soften
# metal (the Hotel lights props without a reflected environment), then export content/models/props/<piece>.glb.
# Blender is Z-up: fixture (x, y, z) is Blender (x, -z, y). Saves the prepared <piece>.blend master beside this script
# (its textures as JPEG files under textures/).
import bpy, bmesh, json, math, os, sys
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SET = json.load(open(os.path.join(HERE, "pieces.json")))
only = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []

def prepare(piece, spec):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=os.path.join(HERE, "native", piece + ".glb"))
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
    bm = bmesh.new(); bm.from_mesh(obj.data)
    bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(-90 * spec.get("turn", 0)), 3, "Z"))
    lo = Vector([min(v.co[i] for v in bm.verts) for i in range(3)])
    hi = Vector([max(v.co[i] for v in bm.verts) for i in range(3)])
    width, height, depth = spec["fit"]
    scale = min(width / (hi.x - lo.x), depth / (hi.y - lo.y), height / (hi.z - lo.z))
    base = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
    for v in bm.verts: v.co = (v.co - base) * scale
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=1e-6)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.calc_area() < 1e-10], context="FACES_ONLY")
    bm.to_mesh(obj.data); bm.free()
    obj.data.update()
    triangles = len(obj.data.polygons)
    # A piece reconstructed at a higher budget (a retry) names its own.
    budget = spec.get("triangleBudget", SET["triangleBudget"])
    if triangles > budget: raise SystemExit(f"{piece}: {triangles} triangles exceed {budget}")
    for image in bpy.data.images:
        if image.size[0] > SET["textureSize"]: image.scale(SET["textureSize"], SET["textureSize"])
    soften_metal(obj, SET["metallicScale"])
    size = [round(max(v.co[i] for v in obj.data.vertices) - min(v.co[i] for v in obj.data.vertices), 3) for i in range(3)]
    print(f"PREPARED {piece} triangles={triangles} size(x,depth,height)={size}")
    out = os.path.join(ROOT, "content", "models", "props", piece + ".glb")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", export_yup=True, export_apply=True, export_image_format="JPEG")
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

# Scale the metallic channel (blue) of each metal-rough texture, as for the held items.
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

for piece, spec in SET["pieces"].items():
    if not only or piece in only: prepare(piece, spec)
