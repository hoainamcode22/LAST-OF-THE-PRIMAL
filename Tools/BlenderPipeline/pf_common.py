# PRIMAL FRONTIER - shared Blender helpers
import bpy, bmesh, os, math
from mathutils import Vector, Matrix

ROOT = r"E:\Model game khủng long"
TEX = os.path.join(ROOT, "textures")
UNITY = r"E:\LAST OF THE PRIMAL"
COLLS = ["00_Blockout", "01_Terrain", "02_Forest", "03_Rocks", "04_Shipwreck", "05_Props", "06_Resources",
         "07_Dinosaurs", "08_Player", "09_Weapons", "10_VFX", "11_Lighting"]

def coll(name, parent=None):
    c = bpy.data.collections.get(name)
    if c is None:
        c = bpy.data.collections.new(name)
    par = parent if parent else bpy.context.scene.collection
    if isinstance(par, str):
        par = coll(par)
    if c.name not in [x.name for x in par.children]:
        par.children.link(c)
    return c

def link(obj, cname):
    c = coll(cname) if isinstance(cname, str) else cname
    for uc in list(obj.users_collection):
        uc.objects.unlink(obj)
    c.objects.link(obj)
    return obj

def remove(name):
    o = bpy.data.objects.get(name)
    if o:
        bpy.data.objects.remove(o, do_unlink=True)

def clear_collection(cname):
    c = bpy.data.collections.get(cname)
    if not c: return
    for o in list(c.all_objects):
        bpy.data.objects.remove(o, do_unlink=True)

def load_img(fname, noncolor=False):
    path = os.path.join(TEX, fname)
    img = bpy.data.images.get(fname)
    if img is None:
        img = bpy.data.images.load(path, check_existing=True)
    else:
        img.reload()
    img.colorspace_settings.name = "Non-Color" if noncolor else "sRGB"
    return img

def pbr(name, tex, tint=(1, 1, 1), alpha=False, uv_scale=1.0, normal_strength=1.0, rough_override=None):
    """Material M_<name> using T_<tex>_D/N/M. Blender preview only; Unity materials are rebuilt from the same textures."""
    mname = "M_" + name
    m = bpy.data.materials.get(mname) or bpy.data.materials.new(mname)
    m.use_nodes = True
    nt = m.node_tree; nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial"); out.location = (600, 0)
    bs = nt.nodes.new("ShaderNodeBsdfPrincipled"); bs.location = (300, 0)
    nt.links.new(bs.outputs[0], out.inputs[0])
    uv = nt.nodes.new("ShaderNodeTexCoord"); uv.location = (-900, 0)
    mp = nt.nodes.new("ShaderNodeMapping"); mp.location = (-700, 0)
    mp.inputs["Scale"].default_value = (uv_scale, uv_scale, uv_scale)
    nt.links.new(uv.outputs["UV"], mp.inputs[0])
    d = nt.nodes.new("ShaderNodeTexImage"); d.location = (-400, 250); d.image = load_img(f"T_{tex}_D.png")
    nt.links.new(mp.outputs[0], d.inputs[0])
    if tint != (1, 1, 1):
        mix = nt.nodes.new("ShaderNodeMix"); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'; mix.location = (0, 300)
        mix.inputs[0].default_value = 1.0
        nt.links.new(d.outputs[0], mix.inputs[6]); mix.inputs[7].default_value = (*tint, 1)
        nt.links.new(mix.outputs[2], bs.inputs["Base Color"])
    else:
        nt.links.new(d.outputs[0], bs.inputs["Base Color"])
    npath = os.path.join(TEX, f"T_{tex}_N.png")
    if os.path.exists(npath):
        n = nt.nodes.new("ShaderNodeTexImage"); n.location = (-400, -50); n.image = load_img(f"T_{tex}_N.png", True)
        nm = nt.nodes.new("ShaderNodeNormalMap"); nm.location = (0, -50); nm.inputs[0].default_value = normal_strength
        nt.links.new(mp.outputs[0], n.inputs[0]); nt.links.new(n.outputs[0], nm.inputs[1]); nt.links.new(nm.outputs[0], bs.inputs["Normal"])
    mpath = os.path.join(TEX, f"T_{tex}_M.png")
    if rough_override is not None:
        bs.inputs["Roughness"].default_value = rough_override
    elif os.path.exists(mpath):
        mm = nt.nodes.new("ShaderNodeTexImage"); mm.location = (-400, -350); mm.image = load_img(f"T_{tex}_M.png", True)
        inv = nt.nodes.new("ShaderNodeMath"); inv.operation = 'SUBTRACT'; inv.inputs[0].default_value = 1.0; inv.location = (0, -350)
        nt.links.new(mp.outputs[0], mm.inputs[0]); nt.links.new(mm.outputs["Alpha"], inv.inputs[1]); nt.links.new(inv.outputs[0], bs.inputs["Roughness"])
    else:
        bs.inputs["Roughness"].default_value = 0.85
    if alpha:
        nt.links.new(d.outputs["Alpha"], bs.inputs["Alpha"])
        try: m.surface_render_method = 'DITHERED'
        except Exception: pass
        try: m.blend_method = 'CLIP'
        except Exception: pass
        m.use_backface_culling = False
    m.diffuse_color = (*[c * t for c, t in zip((0.5, 0.5, 0.5), tint)], 1)
    return m

def simple(name, rgb, rough=0.8, metal=0.0, emission=None):
    mname = "M_" + name
    m = bpy.data.materials.get(mname) or bpy.data.materials.new(mname)
    m.use_nodes = True
    bs = m.node_tree.nodes.get("Principled BSDF")
    bs.inputs["Base Color"].default_value = (*rgb, 1); bs.inputs["Roughness"].default_value = rough; bs.inputs["Metallic"].default_value = metal
    if emission:
        bs.inputs["Emission Color"].default_value = (*emission, 1); bs.inputs["Emission Strength"].default_value = 5
    m.diffuse_color = (*rgb, 1)
    return m

def mesh_obj(name, bm, cname, mats=()):
    me = bpy.data.meshes.get(name)
    if me: 
        o = bpy.data.objects.get(name)
        if o: bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.meshes.remove(me)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me)
    for m in mats: me.materials.append(m)
    link(o, cname)
    return o

def select_only(objs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]

def apply_all(o, modifiers=True):
    select_only([o])
    if modifiers:
        for md in list(o.modifiers):
            try: bpy.ops.object.modifier_apply(modifier=md.name)
            except Exception as e: print("modifier apply fail", o.name, md.name, e)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

def smart_uv(o, angle=66, margin=0.02, scale_to_bounds=False):
    select_only([o])
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(angle), island_margin=margin, scale_to_bounds=scale_to_bounds)
    bpy.ops.object.mode_set(mode='OBJECT')

def cube_uv_world(o, size=2.0):
    """Box-project UVs in object space so tiling textures keep real-world texel density (size = meters per tile)."""
    me = o.data
    if not me.uv_layers: me.uv_layers.new(name="UVMap")
    uvl = me.uv_layers.active.data
    for poly in me.polygons:
        n = poly.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for li in poly.loop_indices:
            v = me.vertices[me.loops[li].vertex_index].co
            if ax == 0: uv = (v.y, v.z)
            elif ax == 1: uv = (v.x, v.z)
            else: uv = (v.x, v.y)
            uvl[li].uv = (uv[0] / size, uv[1] / size)

def tri_count(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)

def set_origin_base(o):
    """Origin at bottom-center of bounds."""
    me = o.data
    xs = [v.co.x for v in me.vertices]; ys = [v.co.y for v in me.vertices]; zs = [v.co.z for v in me.vertices]
    c = Vector(((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, min(zs)))
    me.transform(Matrix.Translation(-c))
    o.location = o.location + c
