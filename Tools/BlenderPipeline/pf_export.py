# PRIMAL FRONTIER - export Blender kit to the Unity project (FBX per asset, textures, terrain data, placement, materials).
import bpy, os, json, shutil, math
from mathutils import Matrix, Vector
import pf_common as C

UA = os.path.join(C.UNITY, "Assets", "_Project")
MODELS = os.path.join(UA, "Art", "Models")
TEXOUT = os.path.join(UA, "Art", "Textures")
TERROUT = os.path.join(UA, "Art", "Terrain")
DATAOUT = os.path.join(UA, "Data", "World")

def category(name):
    if name.startswith(("ENV_Rock", "ENV_Cliff", "ENV_Cave")) or name == "RES_Stone_01": return "Rocks" if not name.startswith("RES") else "Resources"
    if name.startswith(("ENV_Tree", "ENV_Fern", "ENV_Bush", "ENV_Grass", "ENV_FallenLog")): return "Vegetation"
    if name.startswith("PROP_Ship_") and not name.startswith(("PROP_Ship_Crate", "PROP_Ship_Barrel", "PROP_Ship_Debris")): return "Shipwreck"
    if name.startswith("PROP_"): return "Props"
    if name.startswith("RES_"): return "Resources"
    if name.startswith("ENV_") and "Water" in name: return "Water"
    return "Misc"

def masters():
    """Asset name -> list of objects to export (LOD chain or single)."""
    out = {}
    for cname in ["02_Forest", "03_Rocks", "04_Shipwreck", "05_Props", "06_Resources"]:
        c = bpy.data.collections.get(cname)
        if not c: continue
        for o in c.objects:       # direct children only (masters), scatter lives in SC_* sub collections
            if o.type != 'MESH' or o.name.startswith("_"): continue
            if o.name.endswith("_LOD0") or o.name.endswith("_LOD1"):
                out.setdefault(o.name[:-5], []).append(o)
            else:
                out[o.name] = [o]
    return out

FBX_KW = dict(axis_forward='-Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
              bake_space_transform=True, object_types={'MESH', 'EMPTY'}, mesh_smooth_type='FACE',
              use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False, path_mode='STRIP',
              use_tspace=True, use_selection=True, use_custom_props=False)

def export_asset(name, objs, outdir):
    os.makedirs(outdir, exist_ok=True)
    saved = [(o, o.matrix_world.copy(), o.parent) for o in objs]
    root = None
    try:
        if len(objs) > 1:
            root = bpy.data.objects.new(name, None)
            bpy.context.scene.collection.objects.link(root)
            for o in objs:
                o.parent = None; o.matrix_world = Matrix.Identity(4); o.parent = root
        else:
            objs[0].matrix_world = Matrix.Identity(4)
        bpy.ops.object.select_all(action='DESELECT')
        for o in objs: o.select_set(True)
        if root: root.select_set(True)
        bpy.context.view_layer.objects.active = objs[0]
        path = os.path.join(outdir, name + ".fbx")
        bpy.ops.export_scene.fbx(filepath=path, **FBX_KW)
        return path
    finally:
        for o, mw, par in saved:
            o.parent = par; o.matrix_world = mw
        if root: bpy.data.objects.remove(root, do_unlink=True)

def export_water(outdir):
    os.makedirs(outdir, exist_ok=True)
    res = []
    for n in ["ENV_Pond_Water", "ENV_Stream_Water"]:
        o = bpy.data.objects.get(n)
        if not o: continue
        o.name = n + "_SRC"
        tmp = o.copy(); tmp.data = o.data.copy(); tmp.name = n + "_EXPORT"
        bpy.context.scene.collection.objects.link(tmp)
        tmp.data.transform(tmp.matrix_world); tmp.matrix_world = Matrix.Identity(4)
        tmp.name = n; tmp.data.name = n
        # UVs for water shader (world XY / 10 m)
        me = tmp.data
        if not me.uv_layers: me.uv_layers.new(name="UVMap")
        for lp in me.loops:
            v = me.vertices[lp.vertex_index].co
            me.uv_layers.active.data[lp.index].uv = (v.x / 10.0, v.y / 10.0)
        bpy.ops.object.select_all(action='DESELECT'); tmp.select_set(True); bpy.context.view_layer.objects.active = tmp
        bpy.ops.export_scene.fbx(filepath=os.path.join(outdir, n + ".fbx"), **FBX_KW)
        bpy.data.objects.remove(tmp, do_unlink=True); o.name = n
        res.append(n)
    return res

def material_table():
    table = {}
    for m in bpy.data.materials:
        if not m.name.startswith("M_") or not m.use_nodes: continue
        if m.name in ("M_Terrain_Preview", "M_Ocean_Preview", "M_FreshWater_Preview") or m.name.startswith("M_Blockout") or m.name == "M_LineupGround":
            continue
        nt = m.node_tree
        entry = {"tint": [1, 1, 1], "alpha_clip": False, "double_sided": not m.use_backface_culling if m.name.startswith(("M_Foliage", "M_SailCloth")) else False}
        tex = None
        for n in nt.nodes:
            if n.bl_idname == "ShaderNodeTexImage" and n.image and n.image.name.endswith("_D.png"):
                tex = n.image.name[2:-6]
            if n.bl_idname == "ShaderNodeMix" and n.blend_type == 'MULTIPLY' and not n.inputs[7].is_linked:
                entry["tint"] = [round(c, 3) for c in n.inputs[7].default_value[:3]]
            if n.bl_idname == "ShaderNodeMapping":
                entry["tiling"] = round(n.inputs["Scale"].default_value[0], 3)
        bs = nt.nodes.get("Principled BSDF")
        if bs and bs.inputs["Alpha"].is_linked: entry["alpha_clip"] = True
        if tex:
            entry["texture"] = tex
        elif bs:
            entry["color"] = [round(c, 3) for c in bs.inputs["Base Color"].default_value[:3]]
            entry["smoothness"] = round(1 - bs.inputs["Roughness"].default_value, 3)
            entry["metallic"] = round(bs.inputs["Metallic"].default_value, 3)
        if m.name == "M_SailCloth": entry["double_sided"] = True
        if m.name == "M_Foliage": entry["double_sided"] = True; entry["alpha_clip"] = True
        table[m.name] = entry
    return table

def markers(meta):
    import numpy as np, terrain_gen as T
    H = np.load(os.path.join(C.ROOT, "export", "terrain", "terrain_height.npy"))
    L = meta["layout"]; out = []
    for k, v in L["zones"].items():
        if "pos" in v:
            out.append({"name": "ZONE_" + k, "group": "Zones", "x": v["pos"][0], "y": v["pos"][1], "z": round(T.sample(H, *v["pos"]), 3), "r": v.get("r", 0), "yaw": v.get("yaw", 0)})
    for k, v in L["habitats"].items():
        out.append({"name": "HAB_" + k, "group": "Habitats", "x": v["pos"][0], "y": v["pos"][1], "z": round(T.sample(H, *v["pos"]), 3), "r": v["r"], "yaw": 0})
    for k, v in L["resources"].items():
        out.append({"name": "RESAREA_" + k, "group": "ResourceAreas", "x": v["pos"][0], "y": v["pos"][1], "z": round(T.sample(H, *v["pos"]), 3), "r": v["r"], "yaw": 0})
    out.append({"name": "WATER_Pond", "group": "Water", "x": L["zones"]["Pond"]["pos"][0], "y": L["zones"]["Pond"]["pos"][1], "z": round(meta["pond_water_level"], 3), "r": L["zones"]["Pond"]["r"], "yaw": 0})
    return {"markers": out}

def run(textures=True, terrain=True, models=True):
    report = {"models": {}, "textures": 0, "terrain": [], "data": []}
    if models:
        for name, objs in sorted(masters().items()):
            p = export_asset(name, objs, os.path.join(MODELS, category(name)))
            report["models"][name] = {"file": os.path.relpath(p, C.UNITY), "tris": sum(C.tri_count(o) for o in objs if not o.name.endswith("_LOD1")), "lods": len(objs)}
        report["water"] = export_water(os.path.join(MODELS, "Water"))
    if textures:
        os.makedirs(TEXOUT, exist_ok=True)
        for f in os.listdir(C.TEX):
            if f.startswith("T_") and f.endswith(".png"):
                shutil.copy2(os.path.join(C.TEX, f), os.path.join(TEXOUT, f)); report["textures"] += 1
    if terrain:
        os.makedirs(TERROUT, exist_ok=True); os.makedirs(DATAOUT, exist_ok=True)
        src = os.path.join(C.ROOT, "export", "terrain")
        for f in ["ENV_Island_Height_1025.r16", "ENV_Island_Splat0.png", "ENV_Island_Splat1.png", "ENV_Detail_Fern.png", "ENV_Detail_Grass.png", "ENV_Detail_Bush.png"]:
            dst = os.path.join(TERROUT, f.replace(".r16", ".bytes"))       # .bytes so Unity imports it as a TextAsset
            shutil.copy2(os.path.join(src, f), dst); report["terrain"].append(os.path.basename(dst))
        lay = json.load(open(os.path.join(src, "layout.json")))
        lay.pop("stream_profile", None)
        json.dump(lay, open(os.path.join(DATAOUT, "layout.json"), "w"), indent=1)
        shutil.copy2(os.path.join(C.ROOT, "export", "placement.json"), os.path.join(DATAOUT, "placement.json"))
        mats = []
        for name, e in sorted(material_table().items()):
            mats.append({"name": name, "texture": e.get("texture", ""), "tint": e.get("tint", [1, 1, 1]), "alphaClip": e.get("alpha_clip", False),
                         "doubleSided": e.get("double_sided", False), "color": e.get("color", [1, 1, 1]), "smoothness": e.get("smoothness", 0.2),
                         "metallic": e.get("metallic", 0.0), "tiling": e.get("tiling", 1.0)})
        json.dump({"materials": mats}, open(os.path.join(DATAOUT, "materials.json"), "w"), indent=1)
        json.dump(markers(lay), open(os.path.join(DATAOUT, "markers.json"), "w"), indent=1)
        report["data"] = ["layout.json", "placement.json", "materials.json", "markers.json"]
    json.dump(report, open(os.path.join(C.ROOT, "export", "export_report.json"), "w"), indent=1)
    return report
