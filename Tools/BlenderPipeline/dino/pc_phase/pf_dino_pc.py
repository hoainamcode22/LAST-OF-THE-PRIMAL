"""
PRIMAL FRONTIER - DINO agent driver (2026-09-30): rebuild a species' clips on the rig already in the work copy of
DINO_Work.blend (no re-mesh, no re-skin), check them, render review poses, export FBX + anim json to the Unity project.
Run in background Blender:  blender --background <copy of DINO_Work.blend> --python pf_dino_pc.py -- <out.json> <sid,sid|all> [export] [render] [eyes]
"""
import bpy, sys, os, json, math, time, traceback
sys.path.insert(0, r"E:\Model game khủng long\scripts")
from mathutils import Vector, Matrix
import numpy as np
import pf_dino_anim as A, pf_dino_anim_pc as P, dino_specs, pf_char_export

UNITY = r"E:\LAST OF THE PRIMAL\Assets\Art\Characters\Dinosaurs"
SHOTS = r"E:\LAST OF THE PRIMAL\Documentation\Screenshots\PCPhase\Dino"
LAND = ["parasaurolophus", "triceratops", "ankylosaurus", "velociraptor", "carnotaurus", "spinosaurus", "apex"]


def species_objects(sid):
    arm = bpy.data.objects[f"DINO_{sid}_Rig"]
    lods = sorted([o for o in bpy.data.objects if o.type == 'MESH' and o.name.startswith(f"DINO_{sid}_LOD")], key=lambda o: o.name)
    return arm, lods


def belly_z(arm, lod0):
    """lowest skin point under the trunk between hips and shoulders, near the mid line and clear of the legs / ground"""
    b = arm.data.bones
    y0, y1 = sorted((b["Pelvis"].head_local.y, b["Chest"].head_local.y))
    hipx = min(abs(bb.head_local.x) for bb in b if bb.name.startswith("Thigh_"))
    hz = min(bb.head_local.z for bb in b if bb.name.startswith("Thigh_"))
    n = len(lod0.data.vertices); co = np.empty(n * 3, np.float32); lod0.data.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
    sel = (co[:, 1] > y0) & (co[:, 1] < y1) & (np.abs(co[:, 0]) < 0.45 * hipx) & (co[:, 2] > 0.3 * hz)
    return float(co[sel, 2].min()) if sel.any() else 0.55 * hz


def build(sid):
    sp = dict(dino_specs.SPECS[sid])
    arm, lods = species_objects(sid)
    sp["_belly"] = belly_z(arm, lods[0])
    base = A.build_clips(arm, sp)
    new, log = P.build_pc_clips(arm, sp, sid)
    by = {m["name"]: m for m in base}
    for m in new: by[m["name"]] = m
    names = [m["name"] for m in base] + [m["name"] for m in new if m["name"] not in {b["name"] for b in base}]
    return [by[n] for n in names], log, sp


def pose_positions(arm, act, bones, frames):
    arm.animation_data.action = act
    out = {b: [] for b in bones}
    sc = bpy.context.scene
    for f in range(1, frames + 2):
        sc.frame_set(f)
        for b in bones: out[b].append(arm.pose.bones[b].head.copy())
    return out


def check(sid, arm, metas, sp):
    """foot slide for the locomotion clips, feet fixed in the world for the turn clips, loop gap, ground contact"""
    rep = {}
    toes = [n for n in ("Toe_L", "Toe_R") if n in arm.pose.bones]
    L = sp["length"]
    for m in metas:
        act = bpy.data.actions.get(m["name"])
        if not act: rep[m["name"]] = "MISSING ACTION"; continue
        n = m["frames"]
        pos = pose_positions(arm, act, toes + ["Head", "Pelvis"], n)
        info = {}
        if m["loop"]:
            gap = max((pos[b][0] - pos[b][-1]).length for b in pos)
            info["loopGap_cm"] = round(gap * 100, 2)
        if m.get("speed", 0) > 0.01 and m.get("move") == "forward":
            sl = []
            for b in toes:
                p = pos[b]; zf = min(q.z for q in p) + 0.004 * L
                for i in range(n):
                    if p[i].z < zf and p[i + 1].z < zf:
                        vy = (p[i + 1].y - p[i].y) * A.FPS
                        sl.append(abs(vy - m["speed"]))
            info["footSlide_mps"] = round(sum(sl) / max(1, len(sl)), 3); info["speed"] = m["speed"]
        if m.get("move") == "turn":
            side = 1 if m["name"].endswith("Left") else -1
            rate = math.radians(P.TURN_RATE.get(sid, 45)) * side
            sl = []
            for b in toes:
                p = pos[b]; zf = min(q.z for q in p) + 0.004 * L
                for i in range(n):
                    if p[i].z < zf and p[i + 1].z < zf:
                        w0 = Matrix.Rotation(rate * i / A.FPS, 3, 'Z') @ p[i]; w1 = Matrix.Rotation(rate * (i + 1) / A.FPS, 3, 'Z') @ p[i + 1]
                        sl.append((w1 - w0).length * A.FPS)
            info["worldFootSlide_mps"] = round(sum(sl) / max(1, len(sl)), 3)
        zmin = min(min(p.z for p in pos[b]) for b in toes) if toes else 0
        info["toe_zmin"] = round(zmin, 3)
        info["head_z"] = [round(min(p.z for p in pos["Head"]), 2), round(max(p.z for p in pos["Head"]), 2)]
        rep[m["name"]] = info
    return rep


def export(sid, arm, lods, metas):
    Name = sid.capitalize(); dest = os.path.join(UNITY, Name)
    names = [m["name"] for m in metas]
    meta = dict(character=Name, rig="Generic", fps=30, clips=[dict(name=m["name"], frames=m["frames"], loop=m["loop"], speed=m.get("speed", 0.0),
                notes=m.get("notes", ""), move=m.get("move", ""), events=m.get("events", [])) for m in metas])
    keep = set(names)
    for a in list(bpy.data.actions):
        if a.name not in keep: bpy.data.actions.remove(a)
    res = pf_char_export.export(arm, lods, dest, f"DINO_{Name}", names, [], meta, f"DINO_{Name}_anim.json",
                                use_mesh_modifiers=False, face_unity_forward=True)
    return dict(fbx=res["fbx"], bytes=res["bytes"], takes=res["takes_found"], clips=len(names))


# ------------------------------------------------------------------ review renders
def setup_render(sid, lods):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_EEVEE'
    sc.render.resolution_x, sc.render.resolution_y, sc.render.resolution_percentage = 800, 500, 100
    try: sc.eevee.taa_render_samples = 16
    except Exception: pass
    sc.render.film_transparent = False
    sc.render.image_settings.file_format = 'PNG'
    keep = set(o.name for o in lods[:1]) | {"DinoCam", "ShowSun", "ShowGround"}
    for o in bpy.data.objects:
        if o.type in ('MESH', 'CURVE', 'EMPTY', 'LIGHT', 'CAMERA'): o.hide_render = o.name not in keep
    w = sc.world or bpy.data.worlds.new("W"); sc.world = w
    w.use_nodes = True
    bg = w.node_tree.nodes.get("Background")
    if bg: bg.inputs[0].default_value = (0.55, 0.62, 0.72, 1); bg.inputs[1].default_value = 0.8
    cam = bpy.data.objects["DinoCam"]; sc.camera = cam
    sun = bpy.data.objects.get("ShowSun")
    if sun: sun.rotation_euler = (math.radians(50), 0, math.radians(35)); sun.data.energy = 4.0
    g = bpy.data.objects.get("ShowGround")
    if g: g.scale = (60, 60, 1); g.location = (0, 0, 0)
    return cam


def aim(cam, target, dist, az, el, lens=50):
    t = Vector(target)
    d = Vector((math.cos(math.radians(el)) * math.sin(math.radians(az)), -math.cos(math.radians(el)) * math.cos(math.radians(az)), math.sin(math.radians(el))))
    cam.location = t + d * dist
    cam.rotation_euler = (t - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam.data.lens = lens; cam.data.clip_end = 500


def render(sid, arm, lods, shots, tag, outdir):
    cam = setup_render(sid, lods)
    L = dino_specs.SPECS[sid]["length"]
    done = []
    sc = bpy.context.scene
    for clip, frac, view in shots:
        act = bpy.data.actions.get(clip)
        if not act: continue
        arm.animation_data.action = act
        f0, f1 = act.frame_range
        sc.frame_set(int(round(f0 + (f1 - f0) * frac)))
        bpy.context.view_layer.update()
        head = arm.matrix_world @ arm.pose.bones["Head"].head
        pel = arm.matrix_world @ arm.pose.bones["Pelvis"].head
        if view == "face":
            aim(cam, head + Vector((0, -0.25 * L / 8, 0)), 0.45 * L / 2 + 0.6, 60, 8, 60)
        elif view == "side":
            c = (head + pel) * 0.5; c.z *= 0.6
            aim(cam, c, L * 1.25 + 1.0, 90, 8, 40)
        elif view == "rear":
            c = pel.copy(); c.z *= 0.6
            aim(cam, c, L * 1.2 + 1.0, 150, 12, 40)
        else:   # front three-quarter
            c = (head * 0.4 + pel * 0.6); c.z *= 0.6
            aim(cam, c, L * 1.2 + 1.0, 40, 12, 40)
        path = os.path.join(outdir, f"{tag}_{sid}_{clip}_{int(frac * 100):02d}_{view}.png")
        sc.render.filepath = path
        bpy.ops.render.render(write_still=True)
        done.append(path)
    return done


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    out = argv[0]; sids = LAND if argv[1] == "all" else argv[1].split(",")
    flags = set(argv[2:])
    res = {"started": time.strftime("%H:%M:%S"), "flags": sorted(flags)}
    for sid in sids:
        t0 = time.time(); r = {}
        try:
            metas, log, sp = build(sid)
            arm, lods = species_objects(sid)
            r["belly"] = sp["_belly"]; r["log"] = log
            r["clips"] = [(m["name"], m["frames"], m["loop"], m.get("speed", 0), len(m.get("events", []))) for m in metas]
            r["check"] = check(sid, arm, metas, sp)
            if "render" in flags:
                shots = json.loads(os.environ.get("PF_SHOTS", "null") or "null") or DEFAULT_SHOTS(sid)
                outdir = os.environ.get("PF_SHOT_DIR", SHOTS); os.makedirs(outdir, exist_ok=True)
                r["renders"] = render(sid, arm, lods, shots, os.environ.get("PF_TAG", "D"), outdir)
            if "export" in flags:
                r["export"] = export(sid, arm, lods, metas)
        except Exception:
            r["error"] = traceback.format_exc()
        r["seconds"] = round(time.time() - t0, 1)
        res[sid] = r
        json.dump(res, open(out, "w"), indent=1, default=str)
    res["finished"] = time.strftime("%H:%M:%S")
    json.dump(res, open(out, "w"), indent=1, default=str)
    print("PF_DINO_PC DONE")


def DEFAULT_SHOTS(sid):
    s = [("Drink", 0.2, "side"), ("Eat", 0.3, "q34"), ("Rest_Down", 0.6, "q34"), ("Rest_Loop", 0.0, "q34"), ("Rest_Loop", 0.0, "side"), ("Rest_Up", 0.5, "side"),
         ("Call", 0.4, "q34"), ("Stop", 0.2, "side"), ("Turn_Left", 0.3, "q34"), ("Idle", 0.0, "face")]
    if sid in P.HERBIVORES: s += [("Flee", 0.25, "side"), ("Defend", 0.3, "q34"), ("Charge", 0.25, "side"), ("Attack", 0.45, "q34")]
    else: s += [("Chase", 0.25, "side"), ("Bite", 0.42, "q34"), ("Recover", 0.3, "q34")]
    return s


if __name__ == "__main__":
    main()
