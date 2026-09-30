import bpy, sys, json, math
fbx, out, full = sys.argv[-3], sys.argv[-2], set(sys.argv[-1].split(","))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx, use_anim=True, ignore_leaf_bones=False)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
ad = arm.animation_data or arm.animation_data_create()
scn = bpy.context.scene
meshes = [o.name for o in bpy.data.objects if o.type == 'MESH']
acts = {a.name.split("|")[-1]: a for a in bpy.data.actions if a.name.startswith(arm.name + "|") or ("|" not in a.name)}
res = {"armature": arm.name, "meshes": meshes, "bones": len(arm.data.bones), "clips": {}}
for n, a in sorted(acts.items()):
    ad.action = a
    try:
        if ad.action_slot is None and len(a.slots): ad.action_slot = a.slots[0]
    except Exception: pass
    f0, f1 = int(round(a.frame_range[0])), int(round(a.frame_range[1]))
    N = f1 - f0
    frames = list(range(N + 1)) if n in full else sorted(set([0, N // 3, (2 * N) // 3, N]))
    S = {}
    for f in frames:
        scn.frame_set(f0 + f)
        M = arm.matrix_world
        S[f] = {"q": {b.name: [round(x, 6) for x in b.matrix_basis.to_quaternion()] for b in arm.pose.bones},
                "loc": [round(x, 6) for x in arm.pose.bones["Pelvis"].matrix_basis.translation],
                "w": {b: [round(x, 5) for x in (M @ arm.pose.bones[b].head)] for b in ("Pelvis", "Spine", "Chest", "Head", "UpperArm_L", "UpperArm_R", "Hand_L", "Hand_R", "Foot_L", "Foot_R", "Toe_L", "Toe_R")},
                "wt": {b: [round(x, 5) for x in (M @ arm.pose.bones[b].tail)] for b in ("Toe_L", "Toe_R")}}
    res["clips"][n] = {"range": [f0, f1], "frames": N, "s": S}
json.dump(res, open(out, "w"))
print("DONE", len(res["clips"]))
