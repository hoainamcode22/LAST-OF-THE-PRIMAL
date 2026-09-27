"""PLAYER_Survivor v2: merge the skinned parts into LOD meshes (one skinned mesh per LOD, shared materials) and export."""
import bpy, bmesh
import pf_ops

LOD0_PARTS = ["PLR2_Body", "PLR2_Eyes", "PLR2_Mouth", "PLR2_Lashes", "PLR2_Brows", "PLR2_Beard", "PLR2_Hair", "PLR2_HairTie",
              "PLR2_Kilt", "PLR2_Belt", "PLR2_Pelt", "PLR2_PeltFur", "PLR2_PeltFurTop", "PLR2_Strap", "PLR2_Wrap_l", "PLR2_Wrap_r", "PLR2_Necklace"]
MAT_ORDER = ["M_Player_Skin", "M_Player_Eye", "M_Player_Hair", "M_Player_Cloth"]

def _dup(o, name):
    c = o.copy(); c.data = o.data.copy(); c.name = name; c.data.name = name
    bpy.context.scene.collection.objects.link(c)
    return c

def _decimate_keep_weights(o, ratio):
    """decimate with vertex groups / UVs kept (armature modifier removed while evaluating); drops shape keys"""
    arm_mod = [(m.object) for m in o.modifiers if m.type == 'ARMATURE']
    for m in list(o.modifiers): o.modifiers.remove(m)
    if o.data.shape_keys: o.shape_key_clear()
    if ratio < 0.999:
        d = o.modifiers.new("Dec", 'DECIMATE'); d.ratio = ratio; d.use_collapse_triangulate = True
        if abs(o.location.x) < 1e-6: d.use_symmetry = True; d.symmetry_axis = 'X'
        dg = bpy.context.evaluated_depsgraph_get()
        me = bpy.data.meshes.new_from_object(o.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
        old = o.data; o.modifiers.clear(); o.data = me; bpy.data.meshes.remove(old)
    for a in arm_mod:
        md = o.modifiers.new("Armature", 'ARMATURE'); md.object = a

def _join(objs, name, arm):
    for o in objs:
        for m in list(o.modifiers):
            if m.type != 'ARMATURE': o.modifiers.remove(m)
    # objects without shape keys get a basis so the join keeps the others' keys
    if any(o.data.shape_keys for o in objs):
        for o in objs:
            if not o.data.shape_keys: o.shape_key_add(name="Basis", from_mix=False)
    pf_ops.run(bpy.ops.object.join, objs, active=objs[0])
    j = objs[0]; j.name = name; j.data.name = name
    # material slot order: skin, eye, hair, cloth
    mats = [bpy.data.materials[n] for n in MAT_ORDER]
    remap = {}
    for i, m in enumerate(j.data.materials): remap[i] = MAT_ORDER.index(m.name) if m and m.name in MAT_ORDER else 0
    idx = [remap[p.material_index] for p in j.data.polygons]
    j.data.materials.clear()
    for m in mats: j.data.materials.append(m)
    for p, i in zip(j.data.polygons, idx): p.material_index = i
    for m in list(j.modifiers): j.modifiers.remove(m)
    md = j.modifiers.new("Armature", 'ARMATURE'); md.object = arm
    j.parent = arm; j.matrix_parent_inverse = arm.matrix_world.inverted()
    # max 4 influences, normalized (Unity Standard skin weights)
    pf_ops.run(bpy.ops.object.vertex_group_limit_total, [j], active=j, limit=4)
    pf_ops.run(bpy.ops.object.vertex_group_normalize_all, [j], active=j, lock_active=False)
    return j

def build_lods(arm, hair_lod_fn=None):
    for n in ("PLAYER_Survivor_LOD0", "PLAYER_Survivor_LOD1", "PLAYER_Survivor_LOD2"):
        if n in bpy.data.objects: bpy.data.objects.remove(bpy.data.objects[n], do_unlink=True)
    src = {n: bpy.data.objects[n] for n in LOD0_PARTS if n in bpy.data.objects}
    lod0 = _join([_dup(o, o.name + "_L0") for o in src.values()], "PLAYER_Survivor_LOD0", arm)
    # LOD1: decimated body/gear, fewer wider hair cards, no lashes / brow cards / beard fringe / mouth
    l1 = []
    for n, r in (("PLR2_Body", 0.36), ("PLR2_Kilt", 0.5), ("PLR2_Belt", 0.35), ("PLR2_Pelt", 0.6), ("PLR2_Strap", 0.5), ("PLR2_Necklace", 0.4),
                 ("PLR2_Wrap_l", 0.6), ("PLR2_Wrap_r", 0.6), ("PLR2_Eyes", 0.3), ("PLR2_HairTie", 0.4)):
        if n in src:
            c = _dup(src[n], n + "_L1"); _decimate_keep_weights(c, r); l1.append(c)
    if hair_lod_fn:
        l1 += [h for h in hair_lod_fn(1) if h]
    lod1 = _join(l1, "PLAYER_Survivor_LOD1", arm)
    # LOD2
    l2 = []
    for n, r in (("PLR2_Body", 0.13), ("PLR2_Kilt", 0.25), ("PLR2_Pelt", 0.35), ("PLR2_Belt", 0.15)):
        if n in src:
            c = _dup(src[n], n + "_L2"); _decimate_keep_weights(c, r); l2.append(c)
    if hair_lod_fn:
        l2 += [h for h in hair_lod_fn(2) if h]
    lod2 = _join(l2, "PLAYER_Survivor_LOD2", arm)
    for o in src.values(): o.hide_set(True); o.hide_render = True
    tris = lambda o: sum(len(p.vertices) - 2 for p in o.data.polygons)
    return {o.name: tris(o) for o in (lod0, lod1, lod2)}
