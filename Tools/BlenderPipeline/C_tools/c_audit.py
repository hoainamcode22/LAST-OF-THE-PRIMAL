# CHAR audit: per-clip measurements with the Phase-B audit code (renders/characters/audit/_work/aud_anim.analyse) plus the
# acceptance numbers of CHARACTER_ANIMATION_PLAN B1-B7 and the punch / action checks. Read only (never saves).
import bpy, sys, os, math
AUD = r"E:\Model game khủng long\renders\characters\audit\_work"
_saved = list(sys.path)
sys.path.insert(0, AUD)
import aud_anim as AA
sys.path[:] = _saved + ([AUD] if AUD not in _saved else [])
from mathutils import Vector
import numpy as np

RIG = "PLAYER_Survivor_Rig"
AA.SPEED.update({"Walk": 1.35, "Run": 3.8, "Sprint": 6.2, "Run_Backward": 2.4})

def r(x, n=3):
    return None if x is None else round(float(x), n)

def frames_of(act):
    return int(round(act.frame_range[1]))

def sample(act):
    scn = bpy.context.scene; arm = bpy.data.objects[RIG]
    ad = arm.animation_data or arm.animation_data_create(); ad.action = act
    try:
        if ad.action_slot is None and len(act.slots): ad.action_slot = act.slots[0]
    except Exception: pass
    names = [b.name for b in arm.data.bones]
    F = []
    for f in range(frames_of(act) + 1):
        scn.frame_set(f)
        M = {n: arm.pose.bones[n].matrix.copy() for n in names}
        Q = {n: arm.pose.bones[n].matrix_basis.to_quaternion() for n in names}
        F.append((M, Q, arm.pose.bones["Pelvis"].location.copy()))
    return F, names

def audit(clips, meta):
    arm = bpy.data.objects[RIG]
    for o in bpy.data.objects:
        if o.type == 'MESH':
            for m in o.modifiers: m.show_viewport = False
    B = arm.data.bones
    R = {b.name: b.matrix_local.copy() for b in B}
    fr = {}; rest_z = {}
    for sd in ("L", "R"):
        ank = R[f"Foot_{sd}"].translation.copy()
        fr[sd] = dict(heel_local=R[f"Foot_{sd}"].inverted() @ Vector((ank.x, ank.y + 0.035, 0.0)))
        rest_z[sd] = dict(heel=0.0, ball=R[f"Toe_{sd}"].translation.z, toe=(R[f"Toe_{sd}"] @ Vector((0, B[f"Toe_{sd}"].length, 0))).z)
    out = {}
    for name in clips:
        act = bpy.data.actions.get(name)
        if not act: out[name] = "missing"; continue
        F, names = sample(act)
        N = frames_of(act); loop = bool(act.get("pf_loop", False))
        c = AA.analyse(name, F, R, B, names, loop, N, fr, rest_z)
        ser = c.pop("_series")
        s = {"frames": N, "loop": loop, "seam_deg": c.get("seam_pose_max_deg"), "peak_ang_speed": c.get("ang_vel_max_deg_s", [])[:2],
             "spike_ratio": c.get("ang_acc_spike_ratio"), "acc_p95": c.get("ang_acc_deg_s2", {}).get("p95"), "static_frames": len(c.get("static_frames", []))}
        bd = c["body"]
        s["pel_yaw_rng"] = r(bd["pel_yaw"]["range"], 1); s["pel_obl_rng"] = r(bd["pel_obl"]["range"], 1); s["pel_tilt_rng"] = r(bd["pel_tilt"]["range"], 1)
        s["chest_yaw_rng"] = r(bd["ch_yaw"]["range"], 1); s["sh_line_yaw_rng"] = r(bd["sh_yaw"]["range"], 1); s["head_yaw_rng"] = r(bd["head_yaw"]["range"], 1)
        s["head_pitch_rng"] = r(bd["head_pitch"]["range"], 1)
        s["hip_sh_corr"] = c.get("hip_vs_shoulder_corr")
        s["pel_z"] = [bd["pel_z"]["min"], bd["pel_z"]["max"]]; s["bob_cm"] = r((bd["pel_z"]["max"] - bd["pel_z"]["min"]) * 100, 1)
        s["pel_lat_cm"] = r((bd["pel_x"]["max"] - bd["pel_x"]["min"]) * 100, 1)
        pz = ser["body"]["pel_z"]
        span = N if (loop and N > 1) else N + 1
        s["pel_z_min_frames"] = [i for i in range(span) if pz[i] <= min(pz[:span]) + 0.002]
        s["pel_z_max_frames"] = [i for i in range(span) if pz[i] >= max(pz[:span]) - 0.002]
        mt = (meta.get(name) or {}).get("extra") or {}
        if "mid_stance" in mt and loop:
            ms = mt["mid_stance"]; zmin = s["pel_z_min_frames"]
            def cyc(a, b): d = abs(a - b) % N; return min(d, N - d)
            s["pel_min_to_midstance_frames"] = r(min(min(cyc(z, ms["L"]), cyc(z, ms["R"])) for z in zmin), 1)
        arms = {}
        for sd in ("L", "R"):
            A = c["arms"][sd]; S = ser["arms"][sd]
            hz = [x for x in S["hand_z_sh"] if x is not None]; hf = [x for x in S["hand_fwd"] if x is not None]
            hl = [x for x in S["hand_lat"] if x is not None]
            arms[sd] = dict(elbow=[r(A["elbow"]["min"], 1), r(A["elbow"]["max"], 1)], abd=[r(A["abd_sag"]["min"], 1), r(A["abd_sag"]["max"], 1)],
                            flex=[r(A["flex"]["min"], 1), r(A["flex"]["max"], 1)],
                            wrist_below_sh_front=r(-max(hz)), wrist_z_rng=r(max(hz) - min(hz)), wrist_fwd_rng=r(max(hf) - min(hf)),
                            hand_lat_min=r(min(hl)), wrist_roll_step=A["wrist_roll"].get("max_step"), fist_roll_rng=r(A["fist_roll"]["range"], 1) if A.get("fist_roll") else None,
                            twist_rng=[r(A["twistbone"]["min"], 1), r(A["twistbone"]["max"], 1)], wrist_swing_max=r(A["wrist_swing"]["max"], 1))
        s["arms"] = arms
        feet = {}
        for sd in ("L", "R"):
            fd = c["feet"][sd]
            feet[sd] = dict(ball_slide=fd.get("ball_slide_m_s", {}).get("max"), heel_slide=fd.get("heel_slide_m_s", {}).get("max"),
                            toe_slide=fd.get("toe_slide_m_s", {}).get("max"), ball_grounded=fd.get("ball_grounded_frames"),
                            ball_min_mm=fd.get("ball_min_z_above_rest_mm"), heel_min_mm=fd.get("heel_min_z_above_rest_mm"))
        s["feet"] = feet
        # series kept for plotting / specific checks (rounded)
        s["series"] = dict(pel_z=[r(x) for x in pz], pel_yaw=[r(x, 1) for x in ser["body"]["pel_yaw"]], sh_yaw=[r(x, 1) for x in ser["body"]["sh_yaw"]],
                           head_yaw=[r(x, 1) for x in ser["body"]["head_yaw"]],
                           elbowL=[r(x, 1) for x in ser["arms"]["L"]["elbow"]], handzL=[r(x) for x in ser["arms"]["L"]["hand_z_sh"]],
                           handfwdL=[r(x) for x in ser["arms"]["L"]["hand_fwd"]], handzR=[r(x) for x in ser["arms"]["R"]["hand_z_sh"]],
                           handfwdR=[r(x) for x in ser["arms"]["R"]["hand_fwd"]], elbowR=[r(x, 1) for x in ser["arms"]["R"]["elbow"]],
                           speed=c.get("max_speed_per_frame_deg_s"))
        s["extra"] = extra_checks(name, F, names, mt)
        out[name] = s
    bpy.data.objects[RIG].animation_data.action = None
    return out

def extra_checks(name, F, names, mt=None):
    """fist / hand world data for punches and hand-to-target actions"""
    e = {}
    fist = {}
    for sd in ("L", "R"):
        P = [M[f"Hand_{sd}"].translation for (M, Q, PL) in F]
        head = [M["Head"].translation for (M, Q, PL) in F]
        sp = [(P[i + 1] - P[i]).length * 30 for i in range(len(P) - 1)]
        fwd = [-(p.y) for p in P]
        dh = [(P[i] - (head[i] + Vector((0, -0.06, -0.02)))).length for i in range(len(P))]
        fist[sd] = dict(speed_max=r(max(sp) if sp else 0, 2), speed_max_frame=int(np.argmax(sp)) if sp else 0,
                        fwd_max=r(max(fwd)), fwd_max_frame=int(np.argmax(fwd)), to_face_min=r(min(dh)), to_face_max=r(max(dh)),
                        z=[r(p.z) for p in P][::2], fwd=[r(x) for x in fwd][::2], to_face=[r(x) for x in dh][::2])
    e["hands"] = fist
    # per-frame foot slides above 0.08 m/s (ball / heel within 8 mm of the ground), belt removed for the gait loops
    arm = bpy.data.objects[RIG]; B = arm.data.bones
    sp = AA.SPEED.get(name); mv = AA.MOVE.get(name, (0, 0))
    belt = Vector((-mv[0] * sp, -mv[1] * sp, 0)) if sp else Vector((0, 0, 0))
    # clips authored in a moving / turning capsule frame: points are taken to the world with the capsule path from the meta
    mt = mt or {}
    n = len(F); caps = None
    rs = mt.get("root_speed"); ry = mt.get("root_yaw"); tr = mt.get("turn_deg_s")
    if isinstance(rs, list) or isinstance(ry, list) or tr:
        import math
        vs = rs if isinstance(rs, list) else [0.0] * n
        ys = ry if isinstance(ry, list) else [(tr or 0.0) * f / 30.0 for f in range(n)]
        pos = Vector((0, 0, 0)); caps = []
        for f in range(n):
            caps.append((pos.copy(), ys[f]))
            if f + 1 < n:
                v = 0.5 * (vs[f] + vs[f + 1]); y = math.radians(0.5 * (ys[f] + ys[f + 1]))
                pos = pos + Vector((math.sin(y), -math.cos(y), 0)) * (v / 30.0)
        belt = Vector((0, 0, 0))
    def W_(i, p):
        if caps is None: return p
        from mathutils import Matrix
        c, y = caps[i]; return c + Matrix.Rotation(math.radians(y), 3, 'Z') @ p
    import math
    sl = {}
    for sd in ("L", "R"):
        R0 = B[f"Toe_{sd}"].matrix_local.translation.z
        ball = [W_(i, M[f"Toe_{sd}"].translation.copy()) for i, (M, Q, PL) in enumerate(F)]
        hl = B[f"Foot_{sd}"].matrix_local.inverted() @ Vector((B[f"Foot_{sd}"].matrix_local.translation.x, B[f"Foot_{sd}"].matrix_local.translation.y + 0.035, 0.0))
        heel = [W_(i, M[f"Foot_{sd}"] @ hl) for i, (M, Q, PL) in enumerate(F)]
        out = []
        for nm, P, z0 in (("ball", ball, R0), ("heel", heel, 0.0)):
            for i in range(len(P) - 1):
                if P[i].z - z0 < 0.008 and P[i + 1].z - z0 < 0.008:
                    v = (P[i + 1] - P[i]) * 30 - belt
                    v.z = 0
                    if v.length > 0.08: out.append((nm, i, r(v.length)))
        sl[sd] = out
    e["slides"] = sl
    return e
