# PRIMAL FRONTIER phase 2 assets: climbable fruit tree (original design: leaning fern-palm with fruit bunches under
# the crown), fruit bunch prop, fruit icon. Exported to the Unity project with a small meta json for the builder.
import bpy, bmesh, math, random, os, json
from mathutils import Vector, Matrix
import pf_common as C
import pf_veg as V

UA = os.path.join(C.UNITY, "Assets", "_Project")

def fruit_tree(name="ENV_FruitTree_01", seed=11, lod=0):
    rng = random.Random(seed); B = V.Builder()
    H = 6.2
    lean = Vector((0.10, 0.04, 0))
    trunk = V._curve_pts(Vector((0, 0, -0.25)), Vector((0, 0, 1)), H, 12 if lod == 0 else 6, lean=lean, wobble=0.05, seed=seed)
    radii = [0.24 * (1 + 0.55 * math.exp(-max(0.0, p.z) / 0.45)) * (1 - 0.35 * i / len(trunk)) for i, p in enumerate(trunk)]
    B.tube(trunk, radii, segs=12 if lod == 0 else 7, u_rep=2, v_tile=0.9, jitter=0.12, seed=seed, cap=True)
    # leaf-scar rings (low bumps) make the trunk readable as climbable
    top = trunk[-1]
    n = 26 if lod == 0 else 12
    for j in range(n):
        a = j / n * 6.283 + rng.uniform(-0.1, 0.1)
        pitch = rng.uniform(0.15, 1.2)
        d = Vector((math.cos(a), math.sin(a), pitch)).normalized()
        B.card(top, d, Vector((-math.sin(a), math.cos(a), 0)), rng.uniform(2.6, 3.4), 1.7, V.Q["fern"], bend=0.35, segs=3 if lod == 0 else 2, center=top)
    for j in range(6 if lod == 0 else 3):
        a = j / 6 * 6.283 + 0.4
        d = Vector((math.cos(a), math.sin(a), 0.9)).normalized()
        B.card(top - Vector((0, 0, 0.2)), d, Vector((-math.sin(a), math.cos(a), 0)), 2.0, 1.8, V.Q["broadleaf"], bend=0.25, segs=2, center=top)
    o = B.finish(name if lod == 0 else name.replace("_01", "_01") + "", "20_Phase2", (0, 0, 0), V.mats())
    # climb meta (Unity space conversion happens in the builder: Blender (x, y, z) -> Unity (x, z, y) with -X)
    below = top - Vector((0, 0, 0.55))
    fruits = []
    for k in range(3):
        a = k / 3 * 6.283 + 0.9
        fruits.append(list(below + Vector((math.cos(a) * 0.38, math.sin(a) * 0.38, -0.1 * k))))
    radius_mid = radii[len(radii) // 2]
    meta = dict(height=H, climb_start=[0, 0, 0], climb_end=list(trunk[-1] - Vector((0, 0, 2.2))), trunk_radius=radius_mid, fruits=fruits)
    return o, meta

def fruit_bunch(name="PROP_FruitBunch"):
    bm = bmesh.new()
    rng = random.Random(5)
    # stem
    stem = bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=0.018, radius2=0.012, depth=0.22)
    bmesh.ops.translate(bm, verts=stem["verts"], vec=Vector((0, 0, -0.11)))
    for v in stem["verts"]: v.tag = True
    for k in range(7):
        a = k / 7 * 6.283 + rng.uniform(-0.2, 0.2); r = 0.075 if k < 5 else 0.03
        c = Vector((math.cos(a) * r, math.sin(a) * r, -0.2 - (0.07 if k >= 5 else 0) - rng.uniform(0, 0.04)))
        s = bmesh.ops.create_icosphere(bm, subdivisions=2, radius=0.055)
        sc = Vector((1.0, 1.0, 1.3)) * rng.uniform(0.85, 1.1)
        for v in s["verts"]: v.co = Vector((v.co.x * sc.x, v.co.y * sc.y, v.co.z * sc.z)) + c
    for f in bm.faces:
        f.material_index = 1 if all(v.tag for v in f.verts) else 0
        f.smooth = True
    fruit = C.simple("Fruit", (0.95, 0.52, 0.12), rough=0.45)
    stemm = C.simple("FruitStem", (0.28, 0.2, 0.1), rough=0.9)
    o = C.mesh_obj(name, bm, "20_Phase2", [fruit, stemm])
    return o

def render_icon(obj, path, size=256):
    sc = bpy.context.scene
    names = [e.identifier for e in sc.render.bl_rna.properties['engine'].enum_items]
    sc.render.engine = 'BLENDER_EEVEE' if 'BLENDER_EEVEE' in names else 'BLENDER_EEVEE_NEXT'
    sc.render.film_transparent = True
    sc.render.resolution_x = sc.render.resolution_y = size
    cam = bpy.data.objects.get("_IconCam") or bpy.data.objects.new("_IconCam", bpy.data.cameras.new("_IconCam"))
    if cam.name not in sc.collection.objects: sc.collection.objects.link(cam)
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = 0.42
    c = obj.matrix_world @ Vector((0, 0, -0.17))
    cam.location = c + Vector((0.6, -0.8, 0.35)); cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sun = bpy.data.objects.get("_IconSun") or bpy.data.objects.new("_IconSun", bpy.data.lights.new("_IconSun", 'SUN'))
    if sun.name not in sc.collection.objects: sc.collection.objects.link(sun)
    sun.data.energy = 4; sun.rotation_euler = (math.radians(45), 0, math.radians(30))
    sc.camera = cam
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)

def export(objs, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    import pf_export
    bpy.ops.export_scene.fbx(filepath=path, **pf_export.FBX_KW)

def build_all():
    C.coll("20_Phase2")
    tree, meta = fruit_tree()
    lod1, _ = fruit_tree("ENV_FruitTree_01_LOD1", lod=1)
    tree.name = "ENV_FruitTree_01_LOD0"; tree.data.name = tree.name
    bunch = fruit_bunch()
    render_icon(bunch, os.path.join(UA, "Art", "Icons", "ICON_Fruit.png"))
    # LOD chain in one FBX (the world builder convention: NAME_LOD0 / NAME_LOD1 under a root)
    root = bpy.data.objects.get("ENV_FruitTree_01") or bpy.data.objects.new("ENV_FruitTree_01", None)
    if root.name not in bpy.context.scene.collection.objects: bpy.context.scene.collection.objects.link(root)
    for o in (tree, lod1): o.parent = root
    bpy.ops.object.select_all(action='DESELECT')
    root.select_set(True); tree.select_set(True); lod1.select_set(True)
    bpy.context.view_layer.objects.active = tree
    import pf_export
    p1 = os.path.join(UA, "Art", "Models", "Vegetation", "ENV_FruitTree_01.fbx")
    bpy.ops.export_scene.fbx(filepath=p1, **pf_export.FBX_KW)
    p2 = os.path.join(UA, "Art", "Models", "Props", "PROP_FruitBunch.fbx")
    export([bunch], p2)
    os.makedirs(os.path.join(UA, "Data", "World"), exist_ok=True)
    json.dump(meta, open(os.path.join(UA, "Data", "World", "fruit_tree.json"), "w"), indent=1)
    return dict(tree=p1, bunch=p2, meta=meta, tris=[C.tri_count(tree), C.tri_count(lod1), C.tri_count(bunch)])
