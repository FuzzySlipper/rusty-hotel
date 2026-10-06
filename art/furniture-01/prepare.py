# Normalizes the retained Tripo furniture downloads into runtime GLBs (no provider call).
#   blender -b --python art/furniture-01/prepare.py -- [piece ...]
# For each piece in pieces.json that names a "native" download: turn it by "turn" quarter turns about up so its front
# faces the fixture frame's +z, fit it to the fixture's footprint and top height (from "fit"), weld duplicates and drop
# degenerate faces, then export content/models/furniture/<piece>.glb and render inspection views into ignored
# .runtime/mesh-review/furniture/. Blender is Z-up: fixture (x, y, z) is Blender (x, -z, y).
import bpy, bmesh, json, math, os, sys
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
PIECES = json.load(open(os.path.join(HERE, "pieces.json")))
only = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []

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
    bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(-90 * spec["turn"]), 3, "Z"))
    lo = Vector([min(v.co[i] for v in bm.verts) for i in range(3)])
    hi = Vector([max(v.co[i] for v in bm.verts) for i in range(3)])
    # Fit: fixture x width, fixture z depth (Blender y), and height. A piece with a "surface" height fits its main
    # working surface (the largest upward-facing area) to it, so finds and props rest on the top they belong on;
    # otherwise its whole height fits the footprint's.
    (x0, y0, z0), (x1, y1, z1) = spec["fit"]
    sx, sy = (x1 - x0) / (hi.x - lo.x), (z1 - z0) / (hi.y - lo.y)
    if "surface" in spec:
        bins = {}
        for f in bm.faces:
            if f.normal.z > 0.9 and f.calc_center_median().z > lo.z + 0.2 * (hi.z - lo.z):
                key = round(f.calc_center_median().z - lo.z, 2)
                bins[key] = bins.get(key, 0) + f.calc_area()
        top = max(bins, key=bins.get)
        sz = (spec["surface"] - y0) / top
    else:
        sz = (y1 - y0) / (hi.z - lo.z)
    for v in bm.verts:
        p = v.co - lo
        v.co = Vector((x0 + p.x * sx, -z1 + p.y * sy, y0 + p.z * sz))
    # The Engine refuses zero-area triangles, and the fit's uneven scale can flatten slivers into them: weld, dissolve,
    # triangulate, then drop any face still below a square tenth of a millimetre.
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=1e-5)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.calc_area() < 1e-8], context="FACES_ONLY")
    bm.to_mesh(obj.data); bm.free()
    obj.data.update()
    print(f"PREPARED {piece} triangles={len(obj.data.polygons)} scale={sx:.2f},{sy:.2f},{sz:.2f}")
    out = os.path.join(ROOT, "content", "models", "furniture", piece + ".glb")
    bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", export_yup=True, export_apply=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, piece + ".blend"))
    review(piece, obj)

def review(piece, obj):
    out = os.path.join(ROOT, ".runtime", "mesh-review", "furniture")
    os.makedirs(out, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "BLENDER_EEVEE"
    scene.render.resolution_x = scene.render.resolution_y = 480
    world = bpy.data.worlds.new("w"); world.color = (0.5, 0.5, 0.5); scene.world = world
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); scene.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(50), 0, math.radians(30)); sun.data.energy = 3
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scene.collection.objects.link(cam); scene.camera = cam
    size = max(obj.dimensions) * 1.9
    # "front" looks along -fixture z, from in front of the piece (Blender -y).
    for name, angle in [("front", -90), ("quarter", -45)]:
        a = math.radians(angle)
        cam.location = (size * math.cos(a), size * math.sin(a), size * 0.55)
        cam.rotation_euler = (Vector((0, 0, obj.dimensions.z * 0.45)) - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(out, f"{piece}-{name}.png")
        bpy.ops.render.render(write_still=True)

for piece, spec in PIECES.items():
    if spec.get("native") and (not only or piece in only):
        prepare(piece, spec)
