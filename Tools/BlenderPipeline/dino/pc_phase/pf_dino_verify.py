"""
DINO agent check (2026-09-30): the clips kept from phase 2 must come out of the generator unchanged. Rebuilds them on the
work copy's rig and compares bone positions frame by frame with the FBX that is in the Unity project today (backup copy).
Run: blender --background <copy of DINO_Work.blend> --python pf_dino_verify.py -- <out.json> <backup dir> <sid,sid>
"""
import bpy, sys, os, json, math
sys.path.insert(0, r"E:\Model game khủng long\scripts")
from mathutils import Matrix, Vector
import pf_dino_anim as A, dino_specs

KEPT = ["Idle", "Idle_Variation", "Walk", "Run", "Look", "Alert", "Roar", "Attack", "Heavy_Attack", "Hurt", "Death"]
argv = sys.argv[sys.argv.index("--") + 1:]
out, bdir, sids = argv[0], argv[1], argv[2].split(",")
res = {}
sc = bpy.context.scene
for sid in sids:
    Name = sid.capitalize(); r = {}
    arm = bpy.data.objects[f"DINO_{sid}_Rig"]
    A.build_clips(arm, dict(dino_specs.SPECS[sid]))
    before = set(bpy.data.objects); acts_before = set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=os.path.join(bdir, Name, f"DINO_{Name}.fbx"), automatic_bone_orientation=False)
    imp = [o for o in bpy.data.objects if o not in before and o.type == 'ARMATURE'][0]
    acts = {a.name.split("|")[-1]: a for a in bpy.data.actions if a not in acts_before}
    r["_imported_actions"] = len(acts)
    bones = [b for b in ("Head", "Toe_L", "Toe_R", "Jaw", "Tail_03", "Hand_L") if b in arm.pose.bones and b in imp.pose.bones]
    R = Matrix.Rotation(math.pi, 4, 'Z')
    for clip in KEPT:
        a_new = bpy.data.actions.get(clip); a_old = acts.get(clip)
        if not a_new or not a_old: r[clip] = "missing"; continue
        f0, f1 = [int(x) for x in a_new.frame_range]
        worst = 0.0
        for f in sorted({f0, (f0 + f1) // 2, f1, f0 + (f1 - f0) // 3}):
            arm.animation_data.action = a_new; imp.animation_data.action = a_old
            sc.frame_set(f)
            for b in bones:
                pn = arm.matrix_world @ arm.pose.bones[b].head
                po = R @ (imp.matrix_world @ imp.pose.bones[b].head)
                worst = max(worst, (pn - po).length)
        r[clip] = round(worst * 100, 2)          # cm
    r["_imported_scale"] = list(imp.scale)
    res[sid] = r
    for o in [o for o in bpy.data.objects if o not in before]: bpy.data.objects.remove(o, do_unlink=True)
    for a in [a for a in bpy.data.actions if a not in acts_before]: bpy.data.actions.remove(a)
json.dump(res, open(out, "w"), indent=1)
print("VERIFY DONE")
