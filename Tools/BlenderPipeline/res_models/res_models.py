# PRIMAL FRONTIER - resource node models (RES agent, Phase 3.5): original, simple, low poly.
#   RES_Branch_01 / RES_Branch_02: fallen dry branches (bark tubes with forks and twigs), lying on the ground.
# Run in a background Blender:  blender -b --factory-startup --python res_models.py
# Output: Assets/_Project/Art/Models/Resources/RES_Branch_0N.fbx (material "M_Bark" binds to the project's M_Bark by name)
#         Tools/BlenderPipeline/res_models/RES_Branches.blend (source)
import bpy, bmesh, math, os, random
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Models", "Resources")
FBX_KW = dict(axis_forward='-Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
              bake_space_transform=True, object_types={'MESH'}, mesh_smooth_type='FACE', use_mesh_modifiers=True,
              add_leaf_bones=False, bake_anim=False, path_mode='STRIP', use_tspace=True, use_selection=True, use_custom_props=False)

def tube(bm, uv, pts, radii, segs, cap_start=False, v_tile=0.6):
    rings = []; length = 0.0
    t0 = (pts[1] - pts[0]).normalized()
    ref = Vector((0, 0, 1)) if abs(t0.z) < 0.9 else Vector((1, 0, 0))
    n = t0.cross(ref).normalized()
    for i, p in enumerate(pts):
        if i: length += (p - pts[i - 1]).length
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        n = (n - t * n.dot(t)).normalized(); b = t.cross(n)
        ring = []
        for j in range(segs + 1):
            a = j / segs * 2 * math.pi
            v = bm.verts.new(p + (n * math.cos(a) + b * math.sin(a)) * radii[i])
            ring.append((v, (j / segs, length / v_tile)))
        rings.append(ring)
    for i in range(len(rings) - 1):
        for j in range(segs):
            a, b2, c, d = rings[i][j], rings[i][j + 1], rings[i + 1][j + 1], rings[i + 1][j]
            f = bm.faces.new([a[0], b2[0], c[0], d[0]]); f.smooth = True
            for loop, u in zip(f.loops, [a[1], b2[1], c[1], d[1]]): loop[uv].uv = u
    # tip: close to a point
    tip = bm.verts.new(pts[-1] + (pts[-1] - pts[-2]).normalized() * radii[-1] * 1.5)
    last = rings[-1]
    for j in range(segs):
        f = bm.faces.new([last[j][0], last[j + 1][0], tip]); f.smooth = True
        for loop, u in zip(f.loops, [last[j][1], last[j + 1][1], (last[j][1][0], last[j][1][1] + 0.1)]): loop[uv].uv = u
    if cap_start:   # the broken-off thick end: a flat cut showing the wood
        first = rings[0][:-1]
        f = bm.faces.new([v for v, _ in reversed(first)])
        for k, loop in enumerate(f.loops):
            a = k / segs * 2 * math.pi; loop[uv].uv = (0.5 + 0.2 * math.cos(a), 0.5 + 0.2 * math.sin(a))
    return rings

def curve(start, direction, length, n, bend, rng, drop=0.0):
    pts = []; d = direction.normalized()
    side = Vector((0, 0, 1)).cross(d).normalized() if abs(d.z) < 0.95 else Vector((1, 0, 0))
    for i in range(n):
        t = i / (n - 1)
        p = start + d * (length * t) + side * (bend * math.sin(t * math.pi)) + Vector((0, 0, -drop * t))
        p += Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-0.3, 0.3))) * 0.008 * (i > 0 and i < n - 1)
        pts.append(p)
    return pts

def branch(name, seed, L, r0, forks):
    rng = random.Random(seed)
    bm = bmesh.new(); uv = bm.loops.layers.uv.verify()
    main = curve(Vector((-L / 2, 0, r0)), Vector((1, 0, 0.0)), L, 7, 0.09, rng, drop=r0 * 0.55)
    radii = [r0 * (1 - 0.62 * i / 6) for i in range(7)]
    tube(bm, uv, main, radii, 7, cap_start=True)
    for (t, ang, flen, fr) in forks:
        i = int(t * 6); p = main[i].lerp(main[min(i + 1, 6)], t * 6 - i)
        d = Vector((math.cos(math.radians(ang)), math.sin(math.radians(ang)), 0.06))
        z0 = radii[i] * 0.9
        fpts = curve(Vector((p.x, p.y, z0)), d, flen, 4, 0.04 * rng.choice((-1, 1)), rng, drop=max(0.0, z0 - fr * 0.5))
        tube(bm, uv, fpts, [fr, fr * 0.75, fr * 0.5, fr * 0.3], 5)
        # a twig off the fork
        q = fpts[2]; d2 = Vector((math.cos(math.radians(ang + 40)), math.sin(math.radians(ang + 40)), 0.15))
        tube(bm, uv, curve(q, d2, flen * 0.35, 3, 0.0, rng), [fr * 0.45, fr * 0.3, fr * 0.18], 4)
    # a snapped side stub
    q = main[4]; tube(bm, uv, [q, q + Vector((0.02, -0.05, 0.05))], [r0 * 0.3, r0 * 0.25], 5)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    mat = bpy.data.materials.get("M_Bark") or bpy.data.materials.new("M_Bark")
    mat.diffuse_color = (0.33, 0.24, 0.16, 1.0)
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    # rest on the ground: lowest point at z = 0
    zmin = min(v.co.z for v in me.vertices)
    for v in me.vertices: v.co.z -= zmin
    return ob

def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    os.makedirs(OUT, exist_ok=True)
    res = {}
    specs = [
        ("RES_Branch_01", 11, 1.35, 0.055, [(0.40, 38, 0.52, 0.030), (0.66, -34, 0.40, 0.024)]),
        ("RES_Branch_02", 23, 1.10, 0.048, [(0.55, 30, 0.45, 0.026)]),
    ]
    for name, seed, L, r0, forks in specs:
        ob = branch(name, seed, L, r0, forks)
        bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
        path = os.path.join(OUT, name + ".fbx")
        bpy.ops.export_scene.fbx(filepath=path, **FBX_KW)
        dims = ob.dimensions
        res[name] = {"path": path, "verts": len(ob.data.vertices), "faces": len(ob.data.polygons), "dims": [round(dims.x, 3), round(dims.y, 3), round(dims.z, 3)]}
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(os.path.dirname(os.path.abspath(__file__)), "RES_Branches.blend"))
    print("RESULT", res)

main()
