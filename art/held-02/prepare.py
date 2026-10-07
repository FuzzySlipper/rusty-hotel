# Normalizes the retained TRELLIS.2 held-item meshes into runtime GLBs (no generation call).
#   blender -b --python art/held-02/prepare.py -- [piece ...]
# For each piece in pieces.json: level it first if it lies diagonally ("level"), turn it so its business end ("forward", a native axis) points along the hand's
# forward and its top stays up, scale it so its length along forward is "length" metres, move its origin to the grip
# ("grip": the fraction of its bounds back-to-front, left-to-right, bottom-to-top), weld and drop degenerate faces,
# downsize textures to "textureSize" (JPEG) and soften metal, then export content/models/held/<piece>.glb and save the prepared
# .blend master beside this script (its textures as JPEG files under textures/).
# Blender is Z-up and the export is Y-up: the hand's forward (-z in hand space) is Blender +y.
import bpy, bmesh, json, math, os, sys
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SET = json.load(open(os.path.join(HERE, "pieces.json")))
only = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
# A native axis turned onto Blender +y about up (z).
TURNS = {"+y": 0, "-x": -90, "-y": 180, "+x": 90}

def prepare(piece, spec):
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
    bm = bmesh.new(); bm.from_mesh(obj.data)
    if spec.get("level"):
        # A piece reconstructed lying diagonally in its source's picture plane (x up to z): turn it about y so its long
        # axis there lies along x, keeping the end that pointed toward +x.
        cx = sum(v.co.x for v in bm.verts) / len(bm.verts); cz = sum(v.co.z for v in bm.verts) / len(bm.verts)
        sxx = sum((v.co.x - cx) ** 2 for v in bm.verts); szz = sum((v.co.z - cz) ** 2 for v in bm.verts)
        sxz = sum((v.co.x - cx) * (v.co.z - cz) for v in bm.verts)
        angle = 0.5 * math.atan2(2 * sxz, sxx - szz)
        bmesh.ops.rotate(bm, verts=bm.verts, cent=(cx, 0, cz), matrix=Matrix.Rotation(angle, 3, "Y"))
    bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(TURNS[spec["forward"]]), 3, "Z"))
    lo = Vector([min(v.co[i] for v in bm.verts) for i in range(3)])
    hi = Vector([max(v.co[i] for v in bm.verts) for i in range(3)])
    scale = spec["length"] / (hi.y - lo.y)
    # Grip fractions: back-to-front is Blender y, left-to-right x, bottom-to-top z.
    u, v, w = spec["grip"]
    grip = Vector((lo.x + v * (hi.x - lo.x), lo.y + u * (hi.y - lo.y), lo.z + w * (hi.z - lo.z)))
    for vert in bm.verts: vert.co = (vert.co - grip) * scale
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=1e-6)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.calc_area() < 1e-10], context="FACES_ONLY")
    bm.to_mesh(obj.data); bm.free()
    obj.data.update()
    triangles = len(obj.data.polygons)
    if triangles > SET["triangleBudget"]: raise SystemExit(f"{piece}: {triangles} triangles exceed the budget {SET['triangleBudget']}")
    for image in bpy.data.images:
        if image.size[0] > SET["textureSize"]: image.scale(SET["textureSize"], SET["textureSize"])
    soften_metal(obj, SET["metallicScale"])
    size = [round(max(v.co[i] for v in obj.data.vertices) - min(v.co[i] for v in obj.data.vertices), 3) for i in range(3)]
    print(f"PREPARED {piece} triangles={triangles} size={size} scale={scale:.4f}")
    out = os.path.join(ROOT, "content", "models", "held", piece + ".glb")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", export_yup=True, export_apply=True, export_image_format="JPEG")
    save_master(piece)

# The editable master: the prepared object in a .blend whose textures are saved beside it as JPEG files under
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

for piece, spec in SET["pieces"].items():
    if not only or piece in only: prepare(piece, spec)
