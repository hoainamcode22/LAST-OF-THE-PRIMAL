# Review renders for characters: studio light + cameras around a target, EEVEE, PNGs + contact sheet.
import bpy, math, os
from mathutils import Vector
import pf_ops

RENDERS = r"E:\Model game khủng long\renders\characters"

def studio(ground=True):
    sc = bpy.context.scene
    names = [e.identifier for e in sc.render.bl_rna.properties['engine'].enum_items]
    sc.render.engine = 'BLENDER_EEVEE' if 'BLENDER_EEVEE' in names else 'BLENDER_EEVEE_NEXT'
    try: sc.view_settings.view_transform = 'AgX'
    except Exception: pass
    sc.view_settings.exposure = 0.0
    w = bpy.data.worlds.get("W_Studio") or bpy.data.worlds.new("W_Studio")
    sc.world = w; w.use_nodes = True
    bg = w.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.42, 0.44, 0.47, 1); bg.inputs[1].default_value = 0.6
    col = bpy.data.collections.get("_Studio") or bpy.data.collections.new("_Studio")
    if col.name not in [c.name for c in sc.collection.children]: sc.collection.children.link(col)
    def light(name, typ, energy, rot, color=(1, 1, 1), size=1.0):
        o = bpy.data.objects.get(name)
        if not o:
            ld = bpy.data.lights.new(name, typ); o = bpy.data.objects.new(name, ld); col.objects.link(o)
        o.data.energy = energy; o.data.color = color; o.rotation_euler = rot
        if typ == 'SUN': o.data.angle = math.radians(size)
        return o
    light("L_Key", 'SUN', 3.2, (math.radians(50), 0, math.radians(-35)), (1.0, 0.95, 0.88), 3)
    light("L_Fill", 'SUN', 0.9, (math.radians(65), 0, math.radians(140)), (0.8, 0.88, 1.0), 10)
    light("L_Rim", 'SUN', 1.6, (math.radians(-60), 0, math.radians(10)), (1, 1, 1), 5)
    if ground and not bpy.data.objects.get("_Ground"):
        me = bpy.data.meshes.new("_Ground")
        import bmesh
        bm = bmesh.new(); bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=30); bm.to_mesh(me)
        g = bpy.data.objects.new("_Ground", me); col.objects.link(g)
        m = bpy.data.materials.get("M_StudioGround") or bpy.data.materials.new("M_StudioGround")
        m.use_nodes = True; m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.3, 0.29, 0.27, 1)
        m.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.9
        me.materials.append(m)
    return col

def cam(name="_Cam"):
    o = bpy.data.objects.get(name)
    if not o:
        cd = bpy.data.cameras.new(name); o = bpy.data.objects.new(name, cd)
        bpy.data.collections["_Studio"].objects.link(o)
    return o

def shoot(target, dist, height, views=("front", "side", "back", "three4"), size=(720, 900), prefix="shot", lens=50, look_z=None, subdir=""):
    """target: Vector look-at. Character faces -Y. Returns list of paths."""
    sc = bpy.context.scene
    sc.render.resolution_x, sc.render.resolution_y = size
    try: sc.eevee.taa_render_samples = 16
    except Exception: pass
    c = cam(); c.data.lens = lens; c.data.clip_end = 500; sc.camera = c
    ang = {"front": 0, "three4": 35, "side": 90, "back": 180, "left": -90, "three4b": 145}
    out = []
    d = os.path.join(RENDERS, subdir); os.makedirs(d, exist_ok=True)
    lz = target.z if look_z is None else look_z
    for v in views:
        a = math.radians(ang[v])
        # camera on the -Y side for "front" (character faces -Y), rotating towards the character's left (+X) for side
        pos = Vector((target.x + math.sin(a) * dist, target.y - math.cos(a) * dist, height))
        c.location = pos
        dirv = Vector((target.x, target.y, lz)) - pos
        c.rotation_euler = dirv.to_track_quat('-Z', 'Y').to_euler()
        p = os.path.join(d, f"{prefix}_{v}.png")
        sc.render.filepath = p
        bpy.ops.render.render(write_still=True)
        out.append(p)
    return out

def shoot_action(arm, action_name, frames, target, dist, height, view="three4", size=(360, 450), lens=45, subdir="anim", prefix=None):
    sc = bpy.context.scene
    arm.animation_data.action = bpy.data.actions[action_name]
    try:
        slot = bpy.data.actions[action_name].slots[0]
        arm.animation_data.action_slot = slot
    except Exception:
        pass
    out = []
    for f in frames:
        sc.frame_set(f)
        out += shoot(target, dist, height, views=(view,), size=size, prefix=f"{prefix or action_name}_{f:03d}", lens=lens, subdir=subdir)
    return out
