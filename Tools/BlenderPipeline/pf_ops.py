# Context helpers so bpy.ops work from the MCP execution context.
import bpy

def view3d_ctx():
    wm = bpy.context.window_manager
    for w in wm.windows:
        for a in w.screen.areas:
            if a.type == 'VIEW_3D':
                r = [r for r in a.regions if r.type == 'WINDOW'][0]
                return dict(window=w, area=a, region=r, screen=w.screen, scene=bpy.context.scene, view_layer=bpy.context.view_layer)
    w = wm.windows[0]
    return dict(window=w, screen=w.screen, scene=bpy.context.scene, view_layer=bpy.context.view_layer)

def run(op, objs, active=None, **kw):
    """Run bpy.ops callable `op` with the given selection/active object."""
    active = active or objs[0]
    vl = bpy.context.view_layer
    vl.update()
    for o in bpy.context.scene.objects:
        if o is not None and o.name in vl.objects:
            o.select_set(False)
    for o in objs:
        o.select_set(True)
    vl.objects.active = active
    c = view3d_ctx()
    c.update(active_object=active, object=active, selected_objects=list(objs), selected_editable_objects=list(objs))
    with bpy.context.temp_override(**c):
        return op(**kw)

def mode_set(obj, mode):
    run(bpy.ops.object.mode_set, [obj], mode=mode)


def evaluated_mesh_copy(obj, name):
    dg = bpy.context.evaluated_depsgraph_get()
    return bpy.data.meshes.new_from_object(obj.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)

def import_ply(path, name):
    import bmesh
    old = bpy.data.objects.get(name)
    if old: bpy.data.objects.remove(old)
    before = set(bpy.data.objects.keys())
    with bpy.context.temp_override(**view3d_ctx()):
        bpy.ops.wm.ply_import(filepath=path)
    new = [bpy.data.objects[n] for n in set(bpy.data.objects.keys()) - before][0]
    new.name = name; new.data.name = name
    bm = bmesh.new(); bm.from_mesh(new.data); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(new.data); bm.free()
    for p in new.data.polygons: p.use_smooth = True
    return new
