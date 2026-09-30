# CHAR review sheets: workbench renders of clip frames from several views, composed into one PNG per sheet.
import bpy, os, math
import numpy as np
from mathutils import Vector

RIG = "PLAYER_Survivor_Rig"; LOD0 = "PLAYER_Survivor_LOD0"
DIRS = {"front": (0, -1, 0.08), "back": (0, 1, 0.08), "left": (1, 0, 0.05), "right": (-1, 0, 0.05), "back_high": (0.25, 1.0, 0.85),
        "front34": (0.7, -1, 0.25), "top": (0, -0.05, 1), "back34": (-0.6, 1.0, 0.35), "fright34": (-0.7, -1, 0.2)}

def _setup():
    scn = bpy.context.scene
    scn.render.engine = 'BLENDER_WORKBENCH'
    sh = scn.display.shading
    sh.light = 'STUDIO'; sh.color_type = 'MATERIAL'; sh.show_shadows = False; sh.show_cavity = True
    scn.render.film_transparent = False
    if scn.world is None: scn.world = bpy.data.worlds.new("C_World")
    scn.world.color = (0.86, 0.86, 0.84)
    scn.view_settings.view_transform = 'Standard'
    scn.render.image_settings.file_format = 'PNG'; scn.render.resolution_percentage = 100
    keep = {LOD0, "_Ground"}
    for o in scn.objects:
        if o.type in ('MESH', 'CURVE', 'FONT', 'LIGHT'): o.hide_render = o.name not in keep
    lod = bpy.data.objects[LOD0]
    for m in lod.modifiers: m.show_viewport = True; m.show_render = True
    g = bpy.data.objects.get("_Ground")
    if g: g.hide_render = False
    cam = bpy.data.objects.get("C_Cam")
    if not cam:
        cd = bpy.data.cameras.new("C_Cam"); cd.type = 'ORTHO'
        cam = bpy.data.objects.new("C_Cam", cd); scn.collection.objects.link(cam)
    scn.camera = cam
    return scn, cam

def _load(p):
    im = bpy.data.images.load(p); w, h = im.size
    a = np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4); bpy.data.images.remove(im); return a

def _grid(tiles, cols, out):
    th, tw = tiles[0].shape[:2]; pad = 3
    rows = (len(tiles) + cols - 1) // cols
    H = rows * (th + pad) + pad; Wd = cols * (tw + pad) + pad
    canvas = np.ones((H, Wd, 4), np.float32) * 0.25; canvas[..., 3] = 1
    for i, im in enumerate(tiles):
        rr, cc = divmod(i, cols)
        y0 = H - pad - (rr + 1) * (th + pad) + pad; x0 = pad + cc * (tw + pad)
        canvas[y0:y0 + th, x0:x0 + tw] = im
    img = bpy.data.images.new(os.path.basename(out), Wd, H); img.pixels.foreach_set(canvas.ravel())
    img.filepath_raw = out; img.file_format = 'PNG'; img.save(); bpy.data.images.remove(img)

def sheets(specs, outdir):
    os.makedirs(outdir, exist_ok=True); tmp = os.path.join(outdir, "_tiles"); os.makedirs(tmp, exist_ok=True)
    scn, cam = _setup()
    arm = bpy.data.objects[RIG]; ad = arm.animation_data or arm.animation_data_create()
    txt_data = bpy.data.curves.get("C_Label") or bpy.data.curves.new("C_Label", 'FONT')
    txt = bpy.data.objects.get("C_Label") or bpy.data.objects.new("C_Label", txt_data)
    if txt.name not in scn.collection.objects: scn.collection.objects.link(txt)
    txt.parent = cam; txt.hide_render = False
    mat = bpy.data.materials.get("C_LabelMat") or bpy.data.materials.new("C_LabelMat"); mat.diffuse_color = (0.05, 0.05, 0.05, 1)
    if not txt_data.materials: txt_data.materials.append(mat)
    made = []
    for sp in specs:
        tiles = []
        views = sp.get("views", ["back_high", "left"]); res = sp.get("res", (240, 320)); scale = sp.get("scale", 2.1)
        target = Vector(sp.get("target", (0, 0, 0.95)))
        for v in views:
            for (clip, f) in sp["frames"]:
                act = bpy.data.actions.get(clip)
                if not act: continue
                ad.action = act
                try:
                    if ad.action_slot is None and len(act.slots): ad.action_slot = act.slots[0]
                except Exception: pass
                scn.frame_set(f); bpy.context.view_layer.update()
                scn.render.resolution_x, scn.render.resolution_y = res
                cam.data.ortho_scale = scale
                d = Vector(DIRS[v]).normalized()
                cam.location = target + d * 5.0
                cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
                txt_data.body = f"{clip} {f}"
                txt.scale = (scale * 0.05,) * 3
                if res[0] >= res[1]: w, h = scale, scale * res[1] / res[0]
                else: h, w = scale, scale * res[0] / res[1]
                txt.location = (-w / 2 + 0.02 * scale, h / 2 - 0.06 * scale, -1.0)
                p = os.path.join(tmp, f"{sp['name']}_{v}_{clip}_{f:03d}.png")
                scn.render.filepath = p
                bpy.ops.render.render(write_still=True)
                tiles.append(_load(p))
        if tiles:
            out = os.path.join(outdir, sp["name"] + ".png")
            _grid(tiles, sp.get("cols", len(sp["frames"])), out); made.append(out)
    ad.action = None
    return made
