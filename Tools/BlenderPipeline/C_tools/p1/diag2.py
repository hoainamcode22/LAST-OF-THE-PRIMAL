import bpy, sys, math
sys.path.insert(0, sys.argv[3])
blend, clip = sys.argv[1], sys.argv[2]
bpy.ops.wm.open_mainfile(filepath=blend)
from pf_clips_human import GROUND_RADII
arm = bpy.data.objects["PLAYER_Survivor_Rig"]; act = bpy.data.actions[clip]
ad = arm.animation_data; ad.action = act
if ad.action_slot is None: ad.action_slot = act.slots[0]
scn = bpy.context.scene
for f in range(int(act.frame_range[1]) + 1):
    scn.frame_set(f); lows = []
    for b, r in GROUND_RADII.items():
        pb = arm.pose.bones[b]; h = pb.head; t = pb.tail
        lows.append((round(min(h.z + (t.z - h.z) * k / 4 - r for k in range(5)), 3), b))
    lows.sort(); print("ROW", f, lows[:3], "hands", round(arm.pose.bones["Hand_L"].head.z,3), round(arm.pose.bones["Hand_R"].head.z,3))
