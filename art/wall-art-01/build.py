# Builds the wall art as GLB models from the retained source images (no generation call).
#   blender -b --python art/wall-art-01/build.py -- [piece ...]
# For each piece in pieces.json: a canvas plane carrying its image (UVs exact, the image's own aspect at "width"), set
# in front of a frame or a backing board, centred at "centre" in the wall fixture's frame (x across, y up from the
# floor, z out from the wall). A frame is a moulded border "border" wide and "depth" deep in a wood or gilt finish; a
# board is a flat backing with brass pins at its top corners. Textures are written as 1536 JPEG at most. Writes
# content/models/wall-art/<piece>.glb and saves the editable <piece>.blend master (its image under textures/).
# Blender is Z-up: fixture (x, y, z) is Blender (x, -z, y).
import bpy, bmesh, json, math, os, sys
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SET = json.load(open(os.path.join(HERE, "pieces.json")))
only = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []

def material(name, colour, roughness, metallic=0.0, image=None):
    m = bpy.data.materials.new(name); m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*colour, 1)
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    if image is not None:
        tex = m.node_tree.nodes.new("ShaderNodeTexImage"); tex.image = image
        m.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    return m

def box(name, lo, hi, mat):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1)
    for v in bm.verts: v.co = Vector([lo[i] + (v.co[i] + 0.5) * (hi[i] - lo[i]) for i in range(3)])
    mesh = bpy.data.meshes.new(name); bm.to_mesh(mesh); bm.free()
    obj = bpy.data.objects.new(name, mesh); obj.data.materials.append(mat)
    bpy.context.scene.collection.objects.link(obj)
    return obj

def build(piece, spec):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    image = bpy.data.images.load(os.path.join(HERE, f"source-{piece}.png"))
    longest = SET["textureSize"]
    if max(image.size) > longest:
        w, h = image.size; s = longest / max(w, h); image.scale(int(w * s), int(h * s))
    aspect = image.size[1] / image.size[0]
    width = spec["width"]; height = width * aspect
    cx, cy = spec["centre"]
    # Fixture (x, y, z) to Blender (x, -z, y): the wall is at Blender y = 0, the room toward -y.
    front = -spec["front"]
    canvas_mesh = bpy.data.meshes.new("canvas")
    canvas_mesh.from_pydata([(cx - width / 2, front, cy - height / 2), (cx + width / 2, front, cy - height / 2),
        (cx + width / 2, front, cy + height / 2), (cx - width / 2, front, cy + height / 2)], [], [(0, 1, 2, 3)])
    uv = canvas_mesh.uv_layers.new(name="UVMap")
    for loop, coords in zip(uv.data, [(0, 0), (1, 0), (1, 1), (0, 1)]): loop.uv = coords
    canvas = bpy.data.objects.new("canvas", canvas_mesh)
    canvas.data.materials.append(material("canvas", (1, 1, 1), spec.get("canvasRoughness", 0.8), image=image))
    bpy.context.scene.collection.objects.link(canvas)
    finish = SET["finishes"][spec["finish"]]
    trim = material(spec["finish"], finish["colour"], finish["roughness"], finish.get("metallic", 0.0))
    b, d = spec["border"], spec["depth"]
    back = front + d  # toward the wall
    if spec["kind"] == "frame":
        x0, x1, y0, y1 = cx - width / 2 - b, cx + width / 2 + b, cy - height / 2 - b, cy + height / 2 + b
        lip = front - spec.get("lip", 0.012)
        for name, lo, hi in [("top", (x0, lip, y1 - b), (x1, back, y1)), ("bottom", (x0, lip, y0), (x1, back, y0 + b)),
                             ("left", (x0, lip, y0 + b), (x0 + b, back, y1 - b)), ("right", (x1 - b, lip, y0 + b), (x1, back, y1 - b))]:
            box(name, lo, hi, trim)
        box("backing", (x0 + b, front + 0.003, y0 + b), (x1 - b, back, y1 - b), trim)
    else:
        box("board", (cx - width / 2 - b, front + 0.002, cy - height / 2 - b), (cx + width / 2 + b, back, cy + height / 2 + b), trim)
        brass = material("brass", (0.72, 0.55, 0.25), 0.35, 0.3)
        for px in (cx - width / 2 + 0.03, cx + width / 2 - 0.03):
            pz = cy + height / 2 - 0.03
            box("pin", (px - 0.008, front - 0.008, pz - 0.008), (px + 0.008, front, pz + 0.008), brass)
    out = os.path.join(ROOT, "content", "models", "wall-art", piece + ".glb")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", export_yup=True, export_image_format="JPEG")
    textures = os.path.join(HERE, "textures"); os.makedirs(textures, exist_ok=True)
    path = os.path.join(textures, piece + ".jpg")
    image.file_format = "JPEG"; image.save(filepath=path, quality=90)
    image.filepath = bpy.path.relpath(path, start=HERE); image.source = "FILE"
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, piece + ".blend"), compress=True, relative_remap=True)
    print(f"BUILT {piece} canvas={width:.3f}x{height:.3f}")

for piece, spec in SET["pieces"].items():
    if not only or piece in only: build(piece, spec)
