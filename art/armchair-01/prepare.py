# Normalizes the retained Tripo armchair into the runtime GLB and renders inspection views (no provider call).
#   blender -b --python art/armchair-01/prepare.py
# Floor pivot at the seat's footprint centre, overall height 0.82 m, front facing +Z in glTF terms (-Y in Blender).
import bpy, bmesh, math, os
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
HEIGHT = 0.82

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=os.path.join(HERE, "native-01.glb"))
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
bpy.ops.object.select_all(action="DESELECT")
for o in meshes: o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1: bpy.ops.object.join()
chair = bpy.context.view_layer.objects.active
bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
for o in [o for o in bpy.context.scene.objects if o != chair]: bpy.data.objects.remove(o)
# Bake the world transform into the mesh, then normalize the mesh data itself.
chair.data.transform(chair.matrix_world)
chair.matrix_world = Matrix.Identity(4)
bm = bmesh.new(); bm.from_mesh(chair.data)
# Tripo's seat faces +X here; turn it to face -Y (glTF +Z, the fixture convention for "out into the room").
bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(-90), 3, "Z"))
low = Vector((min(v.co.x for v in bm.verts), min(v.co.y for v in bm.verts), min(v.co.z for v in bm.verts)))
high = Vector((max(v.co.x for v in bm.verts), max(v.co.y for v in bm.verts), max(v.co.z for v in bm.verts)))
scale = HEIGHT / (high.z - low.z)
bmesh.ops.translate(bm, verts=bm.verts, vec=(-(low.x + high.x) / 2, -(low.y + high.y) / 2, -low.z))
bmesh.ops.scale(bm, verts=bm.verts, vec=(scale, scale, scale))
# The Engine refuses zero-area triangles.
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=1e-7)
bmesh.ops.triangulate(bm, faces=bm.faces)
bm.to_mesh(chair.data); bm.free()
chair.data.update()
chair.matrix_world = Matrix.Identity(4)
bpy.context.view_layer.update()
print("ARMCHAIR objects", [(o.name, o.type, o.parent.name if o.parent else None) for o in bpy.context.scene.objects], tuple(round(x, 3) for x in chair.matrix_world.to_scale()))
dims = chair.dimensions
print(f"ARMCHAIR triangles={len(chair.data.polygons)} size={dims.x:.3f}x{dims.y:.3f}x{dims.z:.3f}")
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "armchair.blend"))
bpy.ops.export_scene.gltf(filepath=os.path.join(ROOT, "content", "models", "furniture", "armchair.glb"), export_format="GLB", export_yup=True, export_apply=True)

# Inspection renders into ignored .runtime/mesh-review/.
out = os.path.join(ROOT, ".runtime", "mesh-review", "armchair-01")
os.makedirs(out, exist_ok=True)
scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "BLENDER_EEVEE"
scene.render.resolution_x = scene.render.resolution_y = 640
world = bpy.data.worlds.new("w"); world.color = (0.5, 0.5, 0.5); scene.world = world
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); scene.collection.objects.link(sun)
sun.rotation_euler = (math.radians(50), 0, math.radians(30)); sun.data.energy = 3
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scene.collection.objects.link(cam); scene.camera = cam
for name, angle in [("front", -90), ("three-quarter", -45), ("side", 0), ("back", 90)]:
    a = math.radians(angle)
    cam.location = (2.4 * math.cos(a), 2.4 * math.sin(a), 1.2)
    direction = Vector((0, 0, 0.4)) - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(out, f"{name}.png")
    bpy.ops.render.render(write_still=True)
