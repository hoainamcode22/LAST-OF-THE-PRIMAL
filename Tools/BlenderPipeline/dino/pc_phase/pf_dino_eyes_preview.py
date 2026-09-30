"""
PRIMAL FRONTIER - DINO agent (2026-09-30): Blender preview of the creature eyes that PrimalCreatureEyes builds in Unity
(same numbers: eye centre / radius from the Blender build, size factor, push-out, eyeball with the iris texture, skin
eyelid shell with an almond opening, catchlight). Nothing here is exported; it renders review images only.
Run: blender --background <copy of DINO_Work.blend> --python pf_dino_eyes_preview.py -- <out.json> <outdir>
"""
import bpy, bmesh, sys, os, json, math
import numpy as np
from mathutils import Vector, Matrix, Quaternion
from mathutils.kdtree import KDTree

# same table as PrimalCreatureEyes.Specs (Blender space: x = creature's left, -y forward, z up)
SPECS = {
    "triceratops":     dict(x=0.30, y=-3.2713, z=1.8238, r=0.055, iris=(0.78, 0.50, 0.16), slit=False, k=1.25),
    "parasaurolophus": dict(x=0.14, y=-2.9331, z=3.3136, r=0.040, iris=(0.85, 0.62, 0.20), slit=False, k=1.3),
    "ankylosaurus":    dict(x=0.27, y=-2.6120, z=1.0177, r=0.035, iris=(0.72, 0.64, 0.22), slit=False, k=1.35),
    "velociraptor":    dict(x=0.035, y=-0.6735, z=0.9200, r=0.013, iris=(1.00, 0.72, 0.14), slit=True, k=1.5),
    "carnotaurus":     dict(x=0.15, y=-2.9179, z=3.1914, r=0.035, iris=(1.00, 0.50, 0.10), slit=True, k=1.35),
    "spinosaurus":     dict(x=0.13, y=-4.6654, z=4.0661, r=0.040, iris=(0.95, 0.82, 0.25), slit=True, k=1.3),
}
OUT_K = 0.12
LID = dict(a=62.0, up_herb=40.0, up_pred=30.0, lo=50.0)
RINGS = [(0.95, 0.5), (1.0, 0.535), (1.15, 0.54), (1.35, 0.535), (1.6, 0.525), (1.9, 0.51)]
SEG = 24


def ss(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t)


def eye_texture(name, iris, slit, N=128):
    """numpy port of PrimalCreatureEyes.EyeTexture (+ iris colour baked in, + catchlight)"""
    ys, xs = np.mgrid[0:N, 0:N]
    u = (xs + 0.5) / N * 2 - 1; w = (ys + 0.5) / N * 2 - 1; r = np.sqrt(u * u + w * w); ang = np.arctan2(w, u)
    fib = 0.9 + 0.1 * np.sin(ang * 37 + np.sin(ang * 11) * 2) + 0.06 * np.sin(ang * 83)
    g = fib * (1.12 + (0.9 - 1.12) * np.clip(r, 0, 1))
    g = g * (1 + (0.22 - 1) * ss(0.72, 0.98, r))
    if slit: pupil = (u / 0.1) ** 2 + (w / 0.72) ** 2 < 1
    else: pupil = r < 0.26
    if not slit: g = 0.02 + (g - 0.02) * ss(0.26, 0.3, r)
    g = np.where(pupil, 0.02, g); g = np.where(r > 1, 0.12, g)
    rgb = np.stack([g * iris[0], g * iris[1], g * iris[2]], -1)
    rgb = np.where(pupil[..., None], 0.015, rgb)
    # catchlight: small soft highlight up and slightly forward of the pupil
    cl = np.exp(-(((u - 0.0) ** 2 + (w - 0.42) ** 2) / (2 * 0.075 ** 2)))
    rgb = rgb + cl[..., None] * 0.9
    rgb = np.clip(rgb, 0, 1)
    img = bpy.data.images.new(name, N, N)
    px = np.concatenate([rgb, np.ones((N, N, 1))], -1).astype(np.float32)
    img.pixels.foreach_set(px.ravel())
    return img


def eye_material(sid, img):
    m = bpy.data.materials.new(f"PV_Eye_{sid}"); m.use_nodes = True
    nt = m.node_tree; b = nt.nodes.get("Principled BSDF")
    t = nt.nodes.new("ShaderNodeTexImage"); t.image = img
    nt.links.new(t.outputs[0], b.inputs["Base Color"])
    b.inputs["Roughness"].default_value = 0.08
    try:
        nt.links.new(t.outputs[0], b.inputs["Emission Color"]); b.inputs["Emission Strength"].default_value = 0.16
    except Exception: pass
    return m


def eyeball(name, coll):
    """sphere radius 0.5 facing +Z with the front-projected UVs of the Unity mesh"""
    bm = bmesh.new(); lat, lon = 12, 20
    verts = []
    for i in range(lat + 1):
        a = math.pi * i / lat
        row = []
        for j in range(lon):
            b = 2 * math.pi * j / lon
            row.append(bm.verts.new((math.sin(a) * math.cos(b) * 0.5, math.sin(a) * math.sin(b) * 0.5, math.cos(a) * 0.5)))
        verts.append(row)
    uvl = bm.loops.layers.uv.new()
    for i in range(lat):
        for j in range(lon):
            q = [verts[i][j], verts[i][(j + 1) % lon], verts[i + 1][(j + 1) % lon], verts[i + 1][j]]
            try: f = bm.faces.new(q)
            except ValueError: continue
            for l in f.loops: l[uvl].uv = (l.vert.co.x + 0.5, l.vert.co.y + 0.5)
            f.smooth = True
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); coll.objects.link(o)
    return o


def lid_points(pred):
    up = LID["up_pred"] if pred else LID["up_herb"]
    pts = []
    for (u, rad) in RINGS:
        ring = []
        for j in range(SEG):
            ph = 2 * math.pi * j / SEG
            b = up if math.sin(ph) > 0 else LID["lo"]
            tmax = 1.0 / math.sqrt((math.cos(ph) / LID["a"]) ** 2 + (math.sin(ph) / b) ** 2)
            th = math.radians(min(u * tmax, 155.0))
            ring.append(Vector((rad * math.sin(th) * math.cos(ph), rad * math.sin(th) * math.sin(ph), rad * math.cos(th))))
        pts.append(ring)
    return pts


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    out, outdir = argv[0], argv[1]; os.makedirs(outdir, exist_ok=True)
    sc = bpy.context.scene; res = {}
    coll = bpy.data.collections.new("PV_Eyes"); sc.collection.children.link(coll)
    for sid, s in SPECS.items():
        arm = bpy.data.objects[f"DINO_{sid}_Rig"]; arm.data.pose_position = 'REST'
        lod0 = bpy.data.objects[f"DINO_{sid}_LOD0"]
        pred = s["slit"]
        head = arm.data.bones["Head"].head_local
        # like the Unity tool: the centre of the modelled eyeball vertices near the build position
        me = lod0.data; n = len(me.vertices)
        co = np.empty(n * 3, np.float32); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
        part = np.zeros(n, np.int32)
        if "part" in me.attributes: me.attributes["part"].data.foreach_get("value", part)
        cs = []
        for sx in (1, -1):
            c0 = np.array((sx * s["x"], s["y"], s["z"]), np.float32)
            d = np.linalg.norm(co - c0, axis=1)
            sel = (d < s["r"] * 4) & (part == 3)
            cs.append(Vector(co[sel].mean(0)) if sel.sum() >= 6 else Vector(c0))
        mid = (cs[0] + cs[1]) / 2; fwd = (mid - head).normalized()
        img = eye_texture(f"PV_T_{sid}", s["iris"], s["slit"]); em = eye_material(sid, img)
        skin = lod0.data.materials[0]
        img_d = next((nd.image for nd in skin.node_tree.nodes if nd.type == 'TEX_IMAGE' and nd.image and nd.image.name.endswith("_D.png")), None)
        # skin vertices (material 0) for the lid UVs
        me = lod0.data; uv = me.uv_layers.active.data
        kd = KDTree(len(me.vertices)); vuv = {}
        for p in me.polygons:
            if p.material_index != 0: continue
            for li in p.loop_indices:
                vi = me.loops[li].vertex_index
                if vi not in vuv: vuv[vi] = tuple(uv[li].uv); kd.insert(me.vertices[vi].co, vi)
        kd.balance()
        # lid colour: average skin texel around the eye, a little darker (as PrimalCreatureEyes.LidMaterial)
        avg = np.array([0.35, 0.3, 0.24])
        if img_d:
            W, H = img_d.size; px = np.empty(W * H * 4, np.float32); img_d.pixels.foreach_get(px); px = px.reshape(H, W, 4)
            cols = []
            for (co_, vi, d) in kd.find_range(cs[0], s["r"] * 3 * s["k"]):
                u_, v_ = vuv[vi]; cols.append(px[min(H - 1, int(v_ * H)) % H, min(W - 1, int(u_ * W)) % W, :3])
            if cols: avg = np.mean(cols, 0)
        avg = np.where(avg <= 0.04045, avg / 12.92, ((avg + 0.055) / 1.055) ** 2.4) * 0.85      # texel values are sRGB
        lidm = bpy.data.materials.new(f"PV_Lid_{sid}"); lidm.use_nodes = True; lidm.use_backface_culling = False
        bs = lidm.node_tree.nodes.get("Principled BSDF"); bs.inputs["Base Color"].default_value = (*avg.tolist(), 1); bs.inputs["Roughness"].default_value = 0.65
        info_avg = [round(float(x), 3) for x in avg]
        info = {"lid_rgb": info_avg}
        for side, c in zip(("L", "R"), cs):
            lat = (c - mid).normalized()
            outw = (lat + fwd * 0.55 + Vector((0, 0, 0.1))).normalized()
            dia = s["r"] * s["k"] * 2
            pos = c + outw * s["r"] * OUT_K
            rot = outw.to_track_quat('Z', 'Y')
            M = Matrix.Translation(pos) @ rot.to_matrix().to_4x4() @ Matrix.Scale(dia, 4)
            e = eyeball(f"PV_Eye_{sid}_{side}", coll); e.matrix_world = M; e.data.materials.append(em)
            # lid shell
            pts = lid_points(pred)
            bm = bmesh.new(); V = [[bm.verts.new(M @ p) for p in ring] for ring in pts]
            for k in range(len(V) - 1):
                for j in range(SEG):
                    q = [V[k][j], V[k][(j + 1) % SEG], V[k + 1][(j + 1) % SEG], V[k + 1][j]]
                    f = bm.faces.new(q); f.smooth = True
            lme = bpy.data.meshes.new(f"PV_Lid_{sid}_{side}"); bm.to_mesh(lme); bm.free()
            lo = bpy.data.objects.new(lme.name, lme); coll.objects.link(lo); lme.materials.append(lidm)
            info[side] = dict(pos=[round(x, 3) for x in pos], dia=round(dia, 4))
        res[sid] = info
    # renders: face close-up (with / without the new parts) and a play-distance view
    sc.render.engine = 'BLENDER_EEVEE'; sc.render.resolution_x, sc.render.resolution_y = 800, 500
    try: sc.eevee.taa_render_samples = 24
    except Exception: pass
    cam = bpy.data.objects["DinoCam"]; sc.camera = cam
    sun = bpy.data.objects.get("ShowSun")
    if sun: sun.rotation_euler = (math.radians(50), 0, math.radians(35)); sun.data.energy = 4.0
    w = sc.world or bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
    bg = w.node_tree.nodes.get("Background")
    if bg: bg.inputs[0].default_value = (0.55, 0.62, 0.72, 1); bg.inputs[1].default_value = 0.8
    shots = []
    for sid, s in SPECS.items():
        keep = {f"DINO_{sid}_LOD0", "ShowGround", "DinoCam", "ShowSun"} | {o.name for o in coll.objects if f"_{sid}_" in o.name}
        for o in bpy.data.objects: o.hide_render = o.name not in keep
        mid = Vector((0, s["y"], s["z"]))
        L = {"velociraptor": 2.1, "carnotaurus": 8, "spinosaurus": 13, "triceratops": 8, "parasaurolophus": 9, "ankylosaurus": 6.5}[sid]
        for tag, dist, az, el, lens in (("face", s["r"] * 26 + 0.25, 55, 10, 60), ("play10m", 10.0, 50, 8, 50), ("play20m", 20.0, 50, 8, 50)):
            d = Vector((math.cos(math.radians(el)) * math.sin(math.radians(az)), -math.cos(math.radians(el)) * math.cos(math.radians(az)), math.sin(math.radians(el))))
            tgt = mid if tag == "face" else Vector((0, s["y"] * 0.4, s["z"] * 0.7))
            cam.location = tgt + d * dist; cam.rotation_euler = (tgt - cam.location).to_track_quat('-Z', 'Y').to_euler(); cam.data.lens = lens
            p = os.path.join(outdir, f"eyes_{sid}_{tag}.png"); sc.render.filepath = p
            bpy.ops.render.render(write_still=True); shots.append(p)
    res["shots"] = shots
    json.dump(res, open(out, "w"), indent=1)
    print("EYES DONE")


main()
