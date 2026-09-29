# preview render of the resource models (workbench), for review only
import bpy, os, math
from mathutils import Vector
d = os.path.dirname(os.path.abspath(__file__))
bpy.ops.wm.open_mainfile(filepath=os.path.join(d, "RES_Branches.blend"))
sc = bpy.context.scene
obs = [o for o in sc.objects if o.type == 'MESH']
for i, o in enumerate(obs): o.location = (0, i * 0.9 - 0.45, 0)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam)
cam.location = (1.3, -1.6, 1.2); cam.rotation_euler = (math.radians(58), 0, math.radians(38)); sc.camera = cam
sc.render.engine = 'BLENDER_WORKBENCH'; sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'MATERIAL'
sc.render.resolution_x, sc.render.resolution_y = 800, 500
sc.render.filepath = os.path.join(d, "preview_branches.png")
bpy.ops.render.render(write_still=True)
print("RESULT ok")
