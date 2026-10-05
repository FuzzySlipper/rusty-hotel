# Builds the hotel's light fittings as GLB from parameters, with no source images or provider calls.
#   blender -b --python art/fittings-01/build.py
# Writes content/models/{sconce,ceiling-lamp,pendant,service-lamp}.glb (runtime) and a .blend master for each beside
# this script. Blender is Z-up; the glTF exporter turns +Z into +Y and -Y into +Z, so a wall fitting is modelled
# with the wall at y = 0, standing out along -Y, and a ceiling fitting hangs down from z = 0.
import bpy, bmesh, math, os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "content", "models")

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)

def material(name, color, roughness, metallic=0.0, emission=None, strength=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    m.use_backface_culling = False  # exported as doubleSided: open glass is seen from inside and out
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    if emission:
        bsdf.inputs["Emission Color"].default_value = (*emission, 1)
        bsdf.inputs["Emission Strength"].default_value = strength
    return m

def finish(obj, mat, smooth=True):
    obj.data.materials.append(mat)
    if smooth:
        for p in obj.data.polygons: p.use_smooth = True
    return obj

def lathe(name, profile, segments, mat, cap=False):
    """Revolves [radius, z] points about Z."""
    bm = bmesh.new()
    rings = []
    for r, z in profile:
        ring = [bm.verts.new((r * math.cos(2 * math.pi * i / segments), r * math.sin(2 * math.pi * i / segments), z)) for i in range(segments)]
        rings.append(ring)
    for a, b in zip(rings, rings[1:]):
        for i in range(segments):
            j = (i + 1) % segments
            bm.faces.new((a[i], a[j], b[j], b[i]))
    if cap:
        bm.faces.new(list(reversed(rings[0])))
    # Points on the axis collapse a ring to one vertex: weld it and drop the zero-area faces the Engine refuses.
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=1e-6)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh); bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return finish(obj, mat)

def export(path):
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", export_yup=True, export_apply=True)

def sconce(brass, glass):
    # Oval backplate on the wall, centred 1.95 m up.
    plate = lathe("backplate", [(0.0, 0.0), (0.075, 0.0), (0.082, 0.004), (0.085, 0.012), (0.07, 0.018), (0.0, 0.018)], 40, brass)
    plate.rotation_euler = (math.pi / 2, 0, 0)
    plate.scale = (1.0, 1.8, 1.0)
    plate.location = (0, 0, 1.95)
    # A short arm out to the shade's collar.
    arm = lathe("arm", [(0.012, 0.0), (0.012, 0.12)], 16, brass)
    arm.rotation_euler = (math.pi / 2, 0, 0)
    arm.location = (0, -0.015, 1.9)
    # Amber glass cylinder between brass rims, open at both ends.
    shade = lathe("shade", [(0.074, 0.0), (0.08, 0.004), (0.08, 0.2), (0.074, 0.204)], 48, glass)
    shade.location = (0, -0.18, 1.82)
    rim_low = lathe("rim-low", [(0.07, -0.006), (0.086, -0.006), (0.088, 0.008), (0.07, 0.008)], 48, brass)
    rim_low.location = (0, -0.18, 1.82)
    rim_high = lathe("rim-high", [(0.07, -0.008), (0.088, -0.008), (0.086, 0.006), (0.07, 0.006)], 48, brass)
    rim_high.location = (0, -0.18, 2.024)
    collar = lathe("collar", [(0.0, 0.0), (0.03, 0.0), (0.03, 0.02), (0.0, 0.02)], 24, brass, cap=True)
    collar.location = (0, -0.18, 1.88)

def ceiling_lamp(brass, glass, plaster):
    # A stepped plaster rose on the ceiling, a brass canopy and a frosted glass dome below.
    lathe("rose", [(0.0, 0.0), (0.28, 0.0), (0.28, -0.008), (0.24, -0.012), (0.22, -0.02), (0.16, -0.024), (0.14, -0.034), (0.0, -0.036)], 64, plaster)
    lathe("canopy", [(0.0, -0.03), (0.07, -0.03), (0.075, -0.045), (0.06, -0.06), (0.0, -0.06)], 40, brass)
    dome = [(0.2 * math.sin(math.radians(a)), -0.06 - 0.13 * (1 - math.cos(math.radians(a)))) for a in range(90, -1, -6)]
    lathe("dome", [(0.205, -0.06)] + dome[::-1][::-1], 64, glass)
    lathe("finial", [(0.0, -0.19), (0.018, -0.19), (0.012, -0.205), (0.0, -0.21)], 16, brass)

def pendant(brass, diffuser, cord):
    # A cord drop from a small canopy to a spun-brass dome, glowing through a diffuser at its rim.
    lathe("canopy", [(0.0, 0.0), (0.05, 0.0), (0.05, -0.02), (0.0, -0.025)], 32, brass)
    lathe("cord", [(0.005, -0.02), (0.005, -0.14)], 12, cord)
    shell = [(0.24 * math.sin(math.radians(a)), -0.14 - 0.18 * (1 - math.cos(math.radians(a)))) for a in range(6, 91, 6)]
    lathe("shade", [(0.014, -0.135)] + shell + [(0.235, -0.322)], 64, brass)
    lathe("diffuser", [(0.0, -0.3), (0.225, -0.3), (0.0, -0.3)][:2] + [(0.0, -0.301)], 64, diffuser)

def service_lamp(enamel, bulb, steel):
    # A conduit drop to a green enamel cone with a bare bulb under it.
    lathe("canopy", [(0.0, 0.0), (0.045, 0.0), (0.045, -0.02), (0.0, -0.02)], 24, steel)
    lathe("rod", [(0.011, -0.02), (0.011, -0.18)], 12, steel)
    lathe("shade", [(0.04, -0.18), (0.06, -0.2), (0.16, -0.29), (0.165, -0.3)], 48, enamel)
    lathe("bulb", [(0.0, -0.255)] + [(0.035 * math.sin(math.radians(a)), -0.29 + 0.035 * math.cos(math.radians(a))) for a in range(15, 180, 15)] + [(0.0, -0.325)], 24, bulb)

os.makedirs(OUT, exist_ok=True)
reset()
brass = material("brass", (0.36, 0.25, 0.09), 0.38, 0.85)
amber = material("amber-glass", (0.55, 0.3, 0.1), 0.3, 0.0, (1.0, 0.55, 0.22), 0.55)
sconce(brass, amber)
export(os.path.join(OUT, "sconce.glb"))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(os.path.dirname(__file__), "sconce.blend"))
reset()
brass = material("brass", (0.36, 0.25, 0.09), 0.38, 0.85)
frost = material("frosted-glass", (0.85, 0.8, 0.7), 0.6, 0.0, (1.0, 0.84, 0.6), 0.6)
plaster = material("plaster", (0.42, 0.4, 0.33), 0.95)
ceiling_lamp(brass, frost, plaster)
export(os.path.join(OUT, "ceiling-lamp.glb"))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(os.path.dirname(__file__), "ceiling-lamp.blend"))
reset()
brass = material("brass", (0.36, 0.25, 0.09), 0.38, 0.85)
diffuser = material("diffuser", (0.9, 0.82, 0.65), 0.7, 0.0, (1.0, 0.82, 0.55), 0.7)
cord = material("cord", (0.05, 0.04, 0.035), 0.8)
pendant(brass, diffuser, cord)
export(os.path.join(OUT, "pendant.glb"))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(os.path.dirname(__file__), "pendant.blend"))
reset()
enamel = material("enamel", (0.16, 0.26, 0.22), 0.45, 0.1)
bulb = material("bulb", (0.95, 0.95, 0.9), 0.2, 0.0, (0.85, 0.95, 0.9), 0.9)
steel = material("steel", (0.25, 0.25, 0.24), 0.5, 0.8)
service_lamp(enamel, bulb, steel)
export(os.path.join(OUT, "service-lamp.glb"))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(os.path.dirname(__file__), "service-lamp.blend"))
