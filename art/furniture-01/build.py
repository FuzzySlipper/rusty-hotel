# Builds the hotel's furniture as GLB from parameters: bevelled case goods and upholstery with seeded, tileable wood,
# marble and vinyl textures. No source images, prompts or provider calls.
#   blender -b --python art/furniture-01/build.py
# Writes content/models/furniture/<piece>.glb and art/furniture-01/<piece>.blend, plus the shared textures in
# art/furniture-01/textures/. Pieces are authored in the fixture frame (x across, y up, z out toward the front) and
# fitted to the footprints in content/scene/fixtures.json; Blender is Z-up, so a fixture point (x, y, z) is (x, -z, y).
import bpy, bmesh, math, os
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "content", "models", "furniture")
TEXTURES = os.path.join(HERE, "textures")
SIZE = 512

# ---- Textures -------------------------------------------------------------------------------------------------------

def periodic_noise(rng, scale):
    field = rng.standard_normal((SIZE, SIZE))
    f = np.fft.fftfreq(SIZE)
    kernel = np.exp(-2 * (np.pi * scale) ** 2 * (f[None, :] ** 2 + f[:, None] ** 2))
    out = np.real(np.fft.ifft2(np.fft.fft2(field) * kernel))
    return out / np.abs(out).max()

def save(name, rgb):
    pixels = np.clip(rgb, 0, 1)
    image = bpy.data.images.new(name, SIZE, SIZE, alpha=False)
    rgba = np.concatenate([pixels, np.ones((SIZE, SIZE, 1))], axis=-1)[::-1].reshape(-1)
    image.pixels = rgba.astype(np.float32)
    image.filepath_raw = os.path.join(TEXTURES, name + ".png")
    image.file_format = "PNG"
    image.save()
    return image

def wood(name, dark, light, seed, rings):
    # Grain runs along u: warped rings across v, with fine pores.
    rng = np.random.default_rng(seed)
    u = np.arange(SIZE)[None, :] / SIZE
    v = np.arange(SIZE)[:, None] / SIZE
    warp = periodic_noise(rng, 18) * 0.35 + periodic_noise(rng, 6) * 0.05
    grain = 0.5 + 0.5 * np.sin(2 * np.pi * (v * rings + warp * 2.0) + 0 * u)
    grain = grain ** 2.2
    pores = periodic_noise(rng, 0.8) * 0.12
    # Soft figure: the rings only shade the wood a little, so it reads as veneer, not stripes.
    t = np.clip(0.35 + grain * 0.3 + 0.2 * (0.5 + periodic_noise(rng, 30) * 0.5) + pores, 0, 1)[..., None]
    return save(name, np.array(dark)[None, None, :] * (1 - t) + np.array(light)[None, None, :] * t)

def marble(name, seed):
    rng = np.random.default_rng(seed)
    veins = np.abs(np.sin(2 * np.pi * (periodic_noise(rng, 22) * 1.6 + periodic_noise(rng, 5) * 0.2)))
    t = (1 - veins ** 0.18)[..., None]
    base = np.array([0.78, 0.75, 0.68])[None, None, :] * (0.94 + 0.06 * periodic_noise(rng, 12)[..., None])
    return save(name, base * (1 - t * 0.55) + np.array([0.34, 0.36, 0.34])[None, None, :] * t * 0.55)

def vinyl(name, colour, seed):
    rng = np.random.default_rng(seed)
    t = (0.5 + 0.5 * periodic_noise(rng, 1.2))[..., None] * 0.08 + (0.5 + 0.5 * periodic_noise(rng, 40))[..., None] * 0.1
    return save(name, np.array(colour)[None, None, :] * (0.9 + t))

# ---- Geometry -------------------------------------------------------------------------------------------------------

def material(name, image=None, colour=(0.5, 0.5, 0.5), roughness=0.6, metallic=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    if image is not None:
        tex = m.node_tree.nodes.new("ShaderNodeTexImage")
        tex.image = image
        m.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    else:
        bsdf.inputs["Base Color"].default_value = (*colour, 1)
    return m

def to_blender(x, y, z):
    return (x, -z, y)

def block(name, lo, hi, mat, bevel=0.006, taper=1.0):
    """A box from fixture-frame corner lo to hi, edges bevelled; taper narrows its top (for legs)."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        x, y, z = v.co.x + 0.5, v.co.y + 0.5, v.co.z + 0.5  # 0..1 in fixture x, z, y order below
        fx = lo[0] + (hi[0] - lo[0]) * x
        fz = lo[2] + (hi[2] - lo[2]) * y
        fy = lo[1] + (hi[1] - lo[1]) * z
        if taper != 1.0 and z < 0.5:
            cx, cz = (lo[0] + hi[0]) / 2, (lo[2] + hi[2]) / 2
            fx, fz = cx + (fx - cx) * taper, cz + (fz - cz) * taper
        v.co = to_blender(fx, fy, fz)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh); bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    if bevel > 0:
        mod = obj.modifiers.new("bevel", "BEVEL")
        mod.width = min(bevel, 0.45 * min(abs(hi[i] - lo[i]) for i in range(3)))
        mod.segments = 3
        mod.limit_method = "ANGLE"
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    # World-metre box projection keeps grain scale the same on every piece.
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.cube_project(cube_size=1.0, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    obj.select_set(False)
    for p in obj.data.polygons: p.use_smooth = True
    return obj

def knob(name, at, mat, r=0.018):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=r, segments=16, ring_count=8, location=to_blender(*at))
    obj = bpy.context.active_object
    obj.name = name
    obj.data.materials.append(mat)
    bpy.ops.object.shade_smooth()
    return obj

def export(piece):
    os.makedirs(OUT, exist_ok=True)
    for o in bpy.context.scene.objects:
        o.select_set(o.type == "MESH")
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, piece + ".glb"), export_format="GLB", export_yup=True,
                              export_apply=True, use_selection=True, export_image_format="AUTO")
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, piece + ".blend"))

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)

def materials():
    return {
        "walnut": material("walnut", bpy.data.images.load(os.path.join(TEXTURES, "walnut.png")), roughness=0.5),
        "oak": material("oak", bpy.data.images.load(os.path.join(TEXTURES, "oak.png")), roughness=0.55),
        "teak": material("teak", bpy.data.images.load(os.path.join(TEXTURES, "teak.png")), roughness=0.5),
        "marble": material("marble", bpy.data.images.load(os.path.join(TEXTURES, "marble.png")), roughness=0.25),
        "vinyl": material("vinyl", bpy.data.images.load(os.path.join(TEXTURES, "mustard-vinyl.png")), roughness=0.4),
        "brass": material("brass", colour=(0.36, 0.25, 0.09), roughness=0.35, metallic=0.85),
        "plinth": material("plinth", colour=(0.05, 0.035, 0.025), roughness=0.6),
    }

# ---- Pieces (fixture frame, metres) ---------------------------------------------------------------------------------

def side_table(m):
    block("top", (-0.55, 0.78, -0.45), (0.55, 0.85, 0.45), m["walnut"], 0.012)
    for x in (-0.5, 0.42):
        block("end", (x, 0.0, -0.36), (x + 0.08, 0.78, 0.36), m["walnut"], 0.01)
    block("shelf", (-0.42, 0.12, -0.32), (0.42, 0.15, 0.32), m["walnut"], 0.006)

def writing_desk(m):
    block("top", (-0.675, 0.78, -1.05), (0.675, 0.83, 1.05), m["walnut"], 0.012)
    block("apron", (-0.6, 0.68, -0.97), (0.6, 0.78, 0.97), m["walnut"], 0.006)
    for x in (-0.605, 0.535):
        for z in (-0.97, 0.88):
            block("leg", (x, 0.0, z), (x + 0.08, 0.78, z + 0.08), m["walnut"], 0.006, taper=0.6)
    block("drawer line", (0.6, 0.705, -0.4), (0.605, 0.755, 0.4), m["walnut"], 0.002)
    knob("pull", (0.615, 0.73, 0.0), m["brass"], 0.014)

def cartridge_table(m):
    block("plinth", (-0.4, 0.0, -0.29), (0.37, 0.06, 0.29), m["plinth"], 0.004)
    block("body", (-0.42, 0.06, -0.31), (0.39, 0.82, 0.31), m["walnut"], 0.008)
    block("top", (-0.43, 0.82, -0.32), (0.4, 0.84, 0.32), m["walnut"], 0.006)
    for x0, x1 in ((-0.39, -0.03), (-0.01, 0.36)):
        block("door", (x0, 0.12, 0.31), (x1, 0.76, 0.325), m["walnut"], 0.004)
    knob("pull", (-0.05, 0.45, 0.335), m["brass"])
    knob("pull", (0.01, 0.45, 0.335), m["brass"])

def log_desk(m):
    block("top", (-0.6, 0.83, -0.4), (0.6, 0.87, 0.4), m["oak"], 0.01)
    for x in (-0.58, 0.52):
        block("side", (x, 0.0, -0.38), (x + 0.06, 0.83, 0.38), m["oak"], 0.008)
    block("modesty", (-0.52, 0.3, -0.36), (0.52, 0.8, -0.33), m["oak"], 0.004)

def reel_desk(m):
    block("top", (-0.55, 0.81, -0.8), (0.55, 0.87, 0.8), m["teak"], 0.012)
    for z in (-0.74, 0.66):
        block("end", (-0.5, 0.0, z), (0.45, 0.81, z + 0.08), m["teak"], 0.01)
    block("stretcher", (-0.06, 0.18, -0.66), (0.02, 0.26, 0.66), m["teak"], 0.006)

def guest_bench(m):
    block("plinth", (-0.975, 0.0, -0.225), (0.975, 0.3, 0.205), m["plinth"], 0.01)
    for i in range(3):
        x0 = -1.075 + i * 2.15 / 3
        block("cushion", (x0 + 0.005, 0.3, -0.275), (x0 + 2.15 / 3 - 0.005, 0.55, 0.275), m["vinyl"], 0.045)

def low_cabinet(m):
    for x in (-0.33, 0.27):
        for z in (-0.84, 0.78):
            block("leg", (x, 0.0, z), (x + 0.06, 0.12, z + 0.06), m["walnut"], 0.004, taper=0.6)
    block("body", (-0.375, 0.12, -0.9), (0.375, 0.83, 0.9), m["walnut"], 0.008)
    block("top", (-0.385, 0.83, -0.91), (0.385, 0.85, 0.91), m["walnut"], 0.006)
    for z0, z1 in ((-0.86, -0.01), (0.01, 0.86)):
        block("door", (0.375, 0.17, z0), (0.39, 0.79, z1), m["walnut"], 0.004)
    for z in (-0.05, 0.05):
        block("pull", (0.39, 0.42, z - 0.008), (0.402, 0.56, z + 0.008), m["brass"], 0.003)

def luggage_table(m):
    for x in (-0.55, 0.49):
        for z in (-0.4, 0.34):
            block("leg", (x, 0.0, z), (x + 0.06, 0.74, z + 0.06), m["walnut"], 0.005)
            block("cap", (x - 0.004, 0.74, z - 0.004), (x + 0.064, 0.8, z + 0.064), m["brass"], 0.004)
    for z in (-0.38, 0.32):
        block("rail", (-0.5, 0.66, z), (0.5, 0.74, z + 0.06), m["walnut"], 0.004)
    for i in range(6):
        z0 = -0.36 + i * 0.125
        block("slat", (-0.53, 0.74, z0), (0.53, 0.78, z0 + 0.09), m["walnut"], 0.006)

def bell_pedestal(m):
    block("foot", (-0.32, 0.0, -0.28), (0.32, 0.08, 0.28), m["walnut"], 0.01)
    block("step", (-0.28, 0.08, -0.24), (0.28, 0.13, 0.24), m["walnut"], 0.008)
    block("shaft", (-0.22, 0.13, -0.18), (0.22, 0.66, 0.18), m["walnut"], 0.01)
    block("cap", (-0.28, 0.66, -0.24), (0.28, 0.72, 0.24), m["walnut"], 0.01)
    block("marble", (-0.25, 0.72, -0.22), (0.25, 0.78, 0.22), m["marble"], 0.006)

PIECES = {
    "side-table": side_table, "writing-desk": writing_desk, "cartridge-table": cartridge_table, "log-desk": log_desk,
    "reel-desk": reel_desk, "guest-bench": guest_bench, "low-cabinet": low_cabinet, "luggage-table": luggage_table,
    "bell-pedestal": bell_pedestal,
}

os.makedirs(TEXTURES, exist_ok=True)
reset()
wood("walnut", (0.12, 0.065, 0.035), (0.30, 0.17, 0.09), 21, 28)
wood("oak", (0.32, 0.22, 0.12), (0.55, 0.42, 0.26), 22, 34)
wood("teak", (0.22, 0.12, 0.06), (0.42, 0.25, 0.12), 23, 30)
marble("marble", 24)
vinyl("mustard-vinyl", (0.55, 0.38, 0.09), 25)
for piece, build in PIECES.items():
    reset()
    build(materials())
    export(piece)
    print("FURNITURE", piece, sum(len(o.evaluated_get(bpy.context.evaluated_depsgraph_get()).data.loop_triangles) for o in bpy.context.scene.objects if o.type == "MESH"))
