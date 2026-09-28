# Pose preview for clip authoring: weapon proxies on the hands (same grip frame as PlayerHierarchy.TryComputeGrip in
# Unity: blade / shaft along the knuckle line towards the index finger, edge towards the knuckles) + quick renders.
import bpy, bmesh, math, os
from mathutils import Vector, Matrix
import pf_look
from pf_anim import HumanRig
import pf_clips_human as CH

RIG = "PLAYER_Survivor_Rig"
PROXY_COLL = "_Proxies"

def grip_matrix(arm, sd):
    """armature-space rest grip frame of a hand: Y = blade (towards the index side), Z = edge (towards the knuckles)"""
    b = arm.data.bones
    hand = b[f"Hand_{sd}"].head_local; mid = b[f"Middle_01_{sd}"].head_local
    idx = b[f"Index_01_{sd}"].head_local; lit = b[f"Pinky_01_{sd}"].head_local
    across = (idx - lit).normalized()
    fingers = mid - hand
    up = (fingers - across * fingers.dot(across)).normalized()
    x = across.cross(up).normalized()
    m = Matrix((x, across, up)).transposed().to_4x4()
    m.translation = hand.lerp(mid, 0.75)
    return m

def _box(bm, x0, x1, y0, y1, z0, z1):
    r = bmesh.ops.create_cube(bm, size=1.0)
    for v in r["verts"]:
        v.co = Vector((x0 if v.co.x < 0 else x1, y0 if v.co.y < 0 else y1, z0 if v.co.z < 0 else z1))

def _proxy(name, build, color):
    o = bpy.data.objects.get(name)
    if o: bpy.data.objects.remove(o, do_unlink=True)
    me = bpy.data.meshes.new(name); bm = bmesh.new(); build(bm); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me)
    col = bpy.data.collections.get(PROXY_COLL) or bpy.data.collections.new(PROXY_COLL)
    if col.name not in [c.name for c in bpy.context.scene.collection.children]: bpy.context.scene.collection.children.link(col)
    col.objects.link(o)
    m = bpy.data.materials.get("M_Proxy_" + name) or bpy.data.materials.new("M_Proxy_" + name)
    m.use_nodes = True; m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = color
    me.materials.append(m)
    return o

def attach(arm, o, sd, local=Matrix()):
    """parent to the hand bone keeping the rest grip frame (armature at rest while parenting)"""
    pp = arm.data.pose_position; arm.data.pose_position = 'REST'; bpy.context.view_layer.update()
    o.parent = arm; o.parent_type = 'BONE'; o.parent_bone = f"Hand_{sd}"
    bpy.context.view_layer.update()
    o.matrix_world = arm.matrix_world @ grip_matrix(arm, sd) @ local
    arm.data.pose_position = pp; bpy.context.view_layer.update()

def proxies(arm, which=("sword",)):
    for n in ("_PX_Sword", "_PX_Spear", "_PX_Bow"):
        o = bpy.data.objects.get(n)
        if o: bpy.data.objects.remove(o, do_unlink=True)
    out = []
    if "sword" in which:
        real = bpy.data.objects.get("WEAPON_FlintSword")
        o = _proxy("_PX_Sword", lambda bm: (_box(bm, -0.014, 0.014, -0.11, 0.11, -0.016, 0.016), _box(bm, -0.01, 0.01, 0.11, 0.78, -0.035, 0.035),
                                             _box(bm, -0.02, 0.02, 0.1, 0.13, -0.06, 0.06)), (0.55, 0.4, 0.25, 1))
        attach(arm, o, "R"); out.append(o.name)
    if "spear" in which:
        o = _proxy("_PX_Spear", lambda bm: (_box(bm, -0.015, 0.015, -0.75, 1.15, -0.015, 0.015), _box(bm, -0.006, 0.006, 1.15, 1.35, -0.03, 0.03)), (0.5, 0.35, 0.2, 1))
        attach(arm, o, "R"); out.append(o.name)
    if "bow" in which:
        def bow(bm):
            n = 10
            for k in range(n):
                a0 = -0.62 + 1.24 * k / n; a1 = -0.62 + 1.24 * (k + 1) / n
                y0, y1 = 0.62 * math.sin(a0), 0.62 * math.sin(a1)
                z0, z1 = 0.62 * (math.cos(a0) - 1), 0.62 * (math.cos(a1) - 1)
                _box(bm, -0.014, 0.014, min(y0, y1), max(y0, y1), min(z0, z1) - 0.012, max(z0, z1) + 0.012)
            _box(bm, -0.003, 0.003, -0.36, 0.36, -0.122, -0.116)   # string on the palm side (-Z), belly towards the knuckles
        o = _proxy("_PX_Bow", bow, (0.45, 0.3, 0.15, 1))
        attach(arm, o, "L"); out.append(o.name)
    return out

def render_poses(poses, subdir="pose_preview", views=("three4", "side"), size=(300, 380), dist=3.3):
    """poses: list of (name, fn(rig) -> feet dict or None). Renders each pose, returns file list"""
    arm = bpy.data.objects[RIG]
    ad = arm.animation_data; keep = ad.action if ad else None
    if ad: ad.action = None
    rig = HumanRig(arm); pf_look.studio()
    hid = {}
    for n in ("PLAYER_Survivor_LOD1", "PLAYER_Survivor_LOD2"):
        o = bpy.data.objects.get(n)
        if o: hid[n] = o.hide_render; o.hide_render = True
    out = []
    try:
        for name, fn in poses:
            rig.reset(); feet = fn(rig)
            if feet:
                for sd, tg in feet.items(): rig.solve_leg(sd, *tg)
            bpy.context.view_layer.update()
            out += pf_look.shoot(Vector((0, 0, 1.05)), dist, 1.35, views=views, size=size, prefix=name, lens=45, look_z=1.0, subdir=subdir)
    finally:
        rig.reset()
        if ad: ad.action = keep
        for n, v in hid.items(): bpy.data.objects[n].hide_render = v
    return out

def sheet(files, path, cols=6, cell=(300, 380)):
    """contact sheet with Blender's image API (no PIL in Blender)"""
    import numpy as np
    W, H = cell; rows = (len(files) + cols - 1) // cols
    canvas = np.zeros((rows * H, cols * W, 4), dtype=np.float32); canvas[..., 3] = 1
    for i, f in enumerate(files):
        img = bpy.data.images.load(f, check_existing=False)
        w, h = img.size
        px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
        r, c = divmod(i, cols)
        hh, ww = min(h, H), min(w, W)
        canvas[(rows - 1 - r) * H:(rows - 1 - r) * H + hh, c * W:c * W + ww] = px[:hh, :ww]
        bpy.data.images.remove(img)
    out = bpy.data.images.new("_sheet", cols * W, rows * H, alpha=True)
    out.pixels = canvas.ravel(); out.filepath_raw = path; out.file_format = 'PNG'; out.save()
    bpy.data.images.remove(out)
    return path

def preview_clips(names, prop, sheet_path, views=("front", "three4"), size=(240, 300), cols=8):
    """solve and render the key poses of weapon clips with a weapon proxy"""
    import pf_clips_weapons as W
    arm = bpy.data.objects[RIG]
    ad = arm.animation_data; keep = ad.action if ad else None
    if ad: ad.action = None
    rig = HumanRig(arm)
    try:
        kp = W.key_poses(rig, names)
    finally:
        if ad: ad.action = keep
    proxies(arm, (prop,))
    files = render_poses([(lbl, (lambda r, d=pose: CH.apply(r, d))) for lbl, pose, info in kp], subdir="pose_keys", views=views, size=size)
    sheet(files, sheet_path, cols=cols, cell=size)
    return {lbl: info for lbl, pose, info in kp}

def filmstrips(specs, sheet_path, size=(200, 250), step=3, view="three4", cols=12, dist=3.4):
    """specs: [(action name, prop or None)] -> one row per clip (frames 0..end every `step`)"""
    arm = bpy.data.objects[RIG]
    ad = arm.animation_data; keep = ad.action
    pf_look.studio()
    hid = {}
    for n in ("PLAYER_Survivor_LOD1", "PLAYER_Survivor_LOD2"):
        o = bpy.data.objects.get(n)
        if o: hid[n] = o.hide_render; o.hide_render = True
    files = []
    try:
        for name, prop in specs:
            proxies(arm, (prop,) if prop else ())
            act = bpy.data.actions[name]; n = int(act.get("pf_frames", act.frame_range[1]))
            frames = list(range(0, n + 1, step))[:cols]
            row = pf_look.shoot_action(arm, name, frames, Vector((0, 0, 1.0)), dist, 1.3, view=view, size=size, lens=45, subdir="film")
            files += row + [None] * (cols - len(row))
    finally:
        ad.action = keep
        for n, v in hid.items(): bpy.data.objects[n].hide_render = v
        proxies(arm, ())
    import numpy as np
    W, H = size; rows = len(files) // cols
    canvas = np.zeros((rows * H, cols * W, 4), dtype=np.float32); canvas[..., 3] = 1
    for i, f in enumerate(files):
        if not f: continue
        img = bpy.data.images.load(f, check_existing=False); w, h = img.size
        px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
        r, c = divmod(i, cols)
        canvas[(rows - 1 - r) * H:(rows - 1 - r) * H + min(h, H), c * W:c * W + min(w, W)] = px[:min(h, H), :min(w, W)]
        bpy.data.images.remove(img)
    out = bpy.data.images.new("_film", cols * W, rows * H, alpha=True)
    out.pixels = canvas.ravel(); out.filepath_raw = sheet_path; out.file_format = 'PNG'; out.save(); bpy.data.images.remove(out)
    return sheet_path
