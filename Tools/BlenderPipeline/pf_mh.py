"""Blender side of the MakeHuman-based survivor body."""
import bpy, json
from mathutils import Vector
WORK = r"E:\Model game khủng long\characters\work\mh"

def import_body(stem, name, replace=True):
    if replace and name in bpy.data.objects:
        bpy.data.objects.remove(bpy.data.objects[name], do_unlink=True)
    before = set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=f"{WORK}\\{stem}.obj", forward_axis='Y', up_axis='Z', use_split_groups=False,
                          import_vertex_groups=True)
    ob = [o for o in bpy.data.objects if o not in before][0]
    ob.name = name; ob.data.name = name
    for p in ob.data.polygons: p.use_smooth = True
    return ob

def joints(stem):
    return json.load(open(f"{WORK}\\{stem}_joints.json"))["joints"]

def preview_skin():
    m = bpy.data.materials.get("_skinprev") or bpy.data.materials.new("_skinprev")
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (0.52, 0.33, 0.23, 1)
    b.inputs["Roughness"].default_value = 0.5
    try:
        b.inputs["Subsurface Weight"].default_value = 0.15
        b.inputs["Subsurface Radius"].default_value = (0.9, 0.35, 0.2)
        b.inputs["Subsurface Scale"].default_value = 0.01
    except Exception: pass
    return m

def assign_groups(ob, stem):
    """vertex groups from <stem>_groups.json (the OBJ importer keeps vertex order); verifies a few positions"""
    G = json.load(open(f"{WORK}\\{stem}_groups.json"))
    vs = []
    with open(f"{WORK}\\{stem}.obj") as f:
        for ln in f:
            if ln.startswith('v '):
                vs.append(tuple(float(x) for x in ln.split()[1:4]))
    for i in (0, 5000, 13379, len(vs) - 1):
        co = ob.data.vertices[i].co
        assert abs(co.x - vs[i][0]) + abs(co.y - vs[i][1]) + abs(co.z - vs[i][2]) < 1e-4, f"vertex order changed at {i}"
    for g, idx in G.items():
        vg = ob.vertex_groups.get(g) or ob.vertex_groups.new(name=g)
        vg.add(idx, 1.0, 'REPLACE')
    return G

def split_off(ob, group_names, delete=True):
    """delete (or return centre/radius of) helper geometry by group"""
    import bmesh
    info = {}
    bm = bmesh.new(); bm.from_mesh(ob.data)
    dl = bm.verts.layers.deform.verify()
    idx = {ob.vertex_groups[g].index: g for g in group_names if g in ob.vertex_groups}
    kill = []
    pts = {g: [] for g in group_names}
    for v in bm.verts:
        for gi, g in idx.items():
            if gi in v[dl]:
                pts[g].append(v.co.copy()); kill.append(v); break
    for g, P in pts.items():
        if P:
            c = sum(P, Vector()) / len(P)
            info[g] = dict(center=c, radius=max((p - c).length for p in P))
    if delete:
        bmesh.ops.delete(bm, geom=list(set(kill)), context='VERTS')
    bm.to_mesh(ob.data); bm.free()
    for g in group_names:
        if g in ob.vertex_groups: ob.vertex_groups.remove(ob.vertex_groups[g])
    return info
