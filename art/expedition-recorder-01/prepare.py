"""Offline Blender conversion and inspection; retains original FBX and uses no runtime dependency."""
import bpy, json, math
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parent
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(ROOT/'native-01.fbx'))
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
coords=[o.matrix_world @ v.co for o in meshes for v in o.data.vertices]
lo=Vector(tuple(min(p[a] for p in coords) for a in range(3))); hi=Vector(tuple(max(p[a] for p in coords) for a in range(3)))
# Blender Z-up. Normalize the longest horizontal dimension to a portable 58cm.
scale=.58/max(hi.x-lo.x,hi.y-lo.y)
offset=Vector(((hi.x+lo.x)/2,(hi.y+lo.y)/2,lo.z))
for o in meshes:
 mat=o.matrix_world.copy()
 for v in o.data.vertices:v.co=(mat @ v.co-offset)*scale
 o.matrix_world.identity()
 for mat in o.data.materials:
  if mat and mat.use_nodes:
   for n in mat.node_tree.nodes:
    if n.type=='BSDF_PRINCIPLED':
     n.inputs['Roughness'].default_value=.82
     n.inputs['Metallic'].default_value=.08
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:o.select_set(True)
triangles=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in meshes)
report={'blender':bpy.app.version_string,'source':'native-01.fbx','sourceBoundsBlender':[list(lo),list(hi)],'scale':scale,'triangles':triangles,'objects':len(meshes),'vertices':sum(len(o.data.vertices) for o in meshes),'images':[{'name':i.name,'size':list(i.size),'packed':bool(i.packed_file)} for i in bpy.data.images],'edit':'Center on floor, longest horizontal extent 0.58m; roughness .82 and metallic .08 defaults; no remesh/decimation.'}
(ROOT/'mesh-01.json').write_text(json.dumps(report,indent=2)+'\n')
assert triangles<=12000,triangles
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'recorder.blend'))
bpy.ops.export_scene.gltf(filepath=str(ROOT.parent.parent/'content/models/recorder.glb'),export_format='GLB',use_selection=True)
# Neutral multi-view renders of the actual model, not the source image.
scene=bpy.context.scene
scene.render.engine='CYCLES';scene.cycles.samples=24
scene.render.resolution_x=640;scene.render.resolution_y=640;scene.render.resolution_percentage=100
scene.world.color=(.35,.35,.35)
scene.view_settings.view_transform='Standard'
for location,power,size in [((1,-1,2),100,2),((-1,0,1),65,2),((0,2,1),80,2)]:
 bpy.ops.object.light_add(type='AREA',location=location);lamp=bpy.context.object;lamp.data.energy=power;lamp.data.shape='DISK';lamp.data.size=size;lamp.rotation_euler=(Vector((0,0,.1))-lamp.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add();camera=bpy.context.object;scene.camera=camera;camera.data.type='ORTHO';camera.data.ortho_scale=.88
for name,location in [('front',(0,-1,.48)),('back',(0,1,.48)),('side',(1,0,.48)),('three-quarter',(1,-1,.7)),('rear',(-1,0,.48)),('underside',(.4,-.4,-1))]:
 camera.location=location;camera.rotation_euler=(Vector((0,0,.1))-camera.location).to_track_quat('-Z','Y').to_euler()
 scene.render.filepath=str(ROOT.parent.parent/'.runtime/mesh-review'/f'{name}.png');bpy.ops.render.render(write_still=True)
