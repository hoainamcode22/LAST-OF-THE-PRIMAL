"""Render item icons / review shots for single objects (transparent background, framed from the bounds)."""
import bpy, math, os
from mathutils import Vector

def render_object(obj, path, size=256, yaw=35, pitch=25, transparent=True, margin=1.25, lens=50, roll=0.0):
    sc = bpy.context.scene
    vis = {o.name: (o.hide_render) for o in bpy.data.objects if o.type == 'MESH'}
    for o in bpy.data.objects:
        if o.type == 'MESH': o.hide_render = o is not obj
    ws = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    c = sum(ws, Vector()) / 8; r = max((w - c).length for w in ws)
    cam = bpy.data.objects.get("_IconCam") or bpy.data.objects.new("_IconCam", bpy.data.cameras.new("_IconCam"))
    if cam.name not in sc.collection.objects: sc.collection.objects.link(cam)
    cam.data.lens = lens; cam.data.clip_start = 0.001; cam.data.clip_end = 100
    fov = 2 * math.atan(18 / lens); dist = r * margin / math.sin(fov / 2)
    a = math.radians(yaw); p = math.radians(pitch)
    cam.location = c + Vector((math.sin(a) * math.cos(p), -math.cos(a) * math.cos(p), math.sin(p))) * dist
    q = (c - cam.location).to_track_quat('-Z', 'Y')
    if roll:
        from mathutils import Quaternion
        q = q @ Quaternion((0, 0, 1), math.radians(roll))
    cam.rotation_euler = q.to_euler()
    sc.camera = cam
    sc.render.resolution_x = sc.render.resolution_y = size; sc.render.resolution_percentage = 100
    sc.render.film_transparent = transparent
    sc.render.image_settings.file_format = 'PNG'; sc.render.image_settings.color_mode = 'RGBA' if transparent else 'RGB'
    sc.render.filepath = path; bpy.ops.render.render(write_still=True)
    for o in bpy.data.objects:
        if o.name in vis: o.hide_render = vis[o.name]
    sc.render.film_transparent = False
    return path
