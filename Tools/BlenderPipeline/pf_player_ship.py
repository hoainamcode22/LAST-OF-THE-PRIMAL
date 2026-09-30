# PRIMAL FRONTIER - bake every player clip and export the player (LOD0/1/2 + rig + clips + meta) to Unity.
import bpy, os, json, time
import pf_anim, pf_clips_human as CH, pf_char_export as EX

RIG = "PLAYER_Survivor_Rig"
LODS = ("PLAYER_Survivor_LOD0", "PLAYER_Survivor_LOD1", "PLAYER_Survivor_LOD2")
DEST = r"E:\LAST OF THE PRIMAL\Assets\Art\Characters\Player"

def bake(which=None):
    arm = bpy.data.objects[RIG]
    for n in ("_PX_Sword", "_PX_Spear", "_PX_Bow"):
        o = bpy.data.objects.get(n)
        if o: bpy.data.objects.remove(o, do_unlink=True)
    rig = pf_anim.HumanRig(arm)
    t = time.time()
    B = CH.build_all(rig, which)
    arm.animation_data.action = None
    rig.reset()
    return B, round(time.time() - t, 1)

def meta_from(B, old=None):
    clips = []
    names = list(B.meta.keys())
    if old:   # keep clips that were not rebuilt this time
        for c in old.get("clips", []):
            if c["name"] not in B.meta and bpy.data.actions.get(c["name"]): clips.append(c)
    for n in names:
        m = B.meta[n]
        clips.append(dict(name=n, frames=m["frames"], loop=m["loop"], speed=m["speed"], notes=m["notes"],
                          events=[dict(frame=f, function=fn, param=pa) for f, fn, pa in m["events"]]))
    return dict(character="Player", rig=RIG, fps=30, clips=clips)

def export(meta):
    arm = bpy.data.objects[RIG]
    meshes = [bpy.data.objects[n] for n in LODS]
    actions = [c["name"] for c in meta["clips"]]
    stray = [a.name for a in bpy.data.actions if a.name not in actions]
    return EX.export(arm, meshes, DEST, "PLAYER_Survivor", actions, [], meta, "PLAYER_Survivor_anim.json", face_unity_forward=True), stray


# ------------------------------------------------------------------ CHAR phase (2026-09-30): staging export for U
# Every batch writes E:\Model game khủng long\export\staging\PLAYER_Survivor.fbx (all player clips, same FBX settings as
# export()) and clips_manifest.json (the builder's clip schema + fps, root speed / distance and extra curves). Clip meta is
# kept on each action (custom property pf_meta) so later batches still know the events of clips baked earlier; clips never
# rebaked keep the entry of the anim json that Unity uses today (read only).
STAGING = r"E:\Model game khủng long\export\staging"
UNITY_META = DEST + r"\Animations\PLAYER_Survivor_anim.json"

def store_meta(B):
    """write the baked clips' meta onto their actions (kept in the .blend)"""
    for n, m in B.meta.items():
        a = bpy.data.actions.get(n)
        if a: a["pf_meta"] = json.dumps({k: v for k, v in m.items()})

def _entry(name, m):
    ev = [dict(frame=int(f), function=fn, param=pa) for f, fn, pa in m.get("events", [])]
    ex = m.get("extra") or {}
    frames = int(m["frames"]); speed = float(m.get("speed", 0.0) or 0.0)
    e = dict(name=name, frames=frames, fps=30, loop=bool(m["loop"]), speed=speed, notes=m.get("notes", ""), events=ev)
    e["root_speed"] = ex.get("root_speed", speed)
    e["root_distance"] = ex.get("root_distance", ex.get("root_distance_per_cycle", round(speed * frames / 30.0, 3)))
    for k in ("root_yaw", "turn_deg_s", "starts_in", "ends_in", "mid_stance", "contact", "hold_frames"):
        if k in ex: e[k] = ex[k]
    return e

def manifest():
    old = {}
    if os.path.exists(UNITY_META):
        for c in json.load(open(UNITY_META)).get("clips", []):
            old[c["name"]] = dict(frames=c["frames"], loop=c["loop"], speed=c.get("speed", 0.0), notes=c.get("notes", ""),
                                  events=[(e["frame"], e["function"], e["param"]) for e in c.get("events", [])])
    clips = []
    for a in sorted(bpy.data.actions, key=lambda a: a.name):
        if a.name.startswith("_"): continue
        if "pf_meta" in a: m = json.loads(a["pf_meta"])
        elif a.name in old: m = old[a.name]
        else: m = dict(frames=int(round(a.frame_range[1])), loop=bool(a.get("pf_loop", False)), speed=0.0, notes="", events=[])
        clips.append(_entry(a.name, m))
    return dict(character="Player", rig=RIG, fps=30, clips=clips)

def export_staging(extra=None):
    meta = manifest()
    arm = bpy.data.objects[RIG]
    meshes = [bpy.data.objects[n] for n in LODS]
    actions = [c["name"] for c in meta["clips"]]
    r = EX.export(arm, meshes, STAGING, "PLAYER_Survivor", actions, [], meta, "clips_manifest.json", face_unity_forward=True, flat=True)
    r["clips"] = len(actions)
    return r
