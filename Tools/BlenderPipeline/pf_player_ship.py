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
