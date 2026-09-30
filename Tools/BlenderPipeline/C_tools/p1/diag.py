import bpy, sys, json, math
from mathutils import Vector
blend, clip = sys.argv[1], sys.argv[2]; bones = sys.argv[3].split(",")
bpy.ops.wm.open_mainfile(filepath=blend)
arm = bpy.data.objects["PLAYER_Survivor_Rig"]; act = bpy.data.actions[clip]
ad = arm.animation_data; ad.action = act
if ad.action_slot is None: ad.action_slot = act.slots[0]
scn = bpy.context.scene; N = int(act.frame_range[1]); prev = {}
fr = {}
for sd in "LR":
    R = arm.data.bones[f"Foot_{sd}"].matrix_local; ank = R.translation
    fr[sd] = R.inverted() @ Vector((ank.x, ank.y + 0.035, 0.0))
for f in range(N + 1):
    scn.frame_set(f); row = [f]
    for b in bones:
        q = arm.pose.bones[b].matrix.to_quaternion()
        if b in prev: a = prev[b].rotation_difference(q).angle; row.append(round(math.degrees(min(a, math.tau - a)) * 30))
        prev[b] = q
    for sd in "LR":
        M = arm.pose.bones[f"Foot_{sd}"].matrix; h = M @ fr[sd]; ball = arm.pose.bones[f"Toe_{sd}"].head
        row.append((sd, round(h.x,3), round(h.y,3), round(h.z*1000), round(ball.x,3), round(ball.y,3), round(ball.z*1000)))
    row.append(round(arm.pose.bones["Pelvis"].head.z, 3))
    print("ROW", row)
