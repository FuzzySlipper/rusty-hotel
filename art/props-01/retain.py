# Re-encodes a downloaded prop mesh for retention: geometry, UVs and materials as downloaded, textures at 1024 JPEG
# (the size prepare.py downsizes to anyway), so the retained native stays small. The download's own hash is recorded in
# provenance.json before it is replaced.
#   blender -b --python art/props-01/retain.py -- <download.glb> <art/props-01/native/piece.glb>
import bpy, sys
src, out = sys.argv[sys.argv.index("--") + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
for image in bpy.data.images:
    if image.size[0] > 1024: image.scale(1024, 1024)
bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", export_yup=True, export_apply=False, export_image_format="JPEG")
