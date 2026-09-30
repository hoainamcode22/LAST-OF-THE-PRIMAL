# CHAR runner (background Blender): bake clips, audit, render review sheets, optionally save / export to staging.
# usage: blender -b <blend> --python c_run.py -- <job.json>
import bpy, sys, os, json, time, traceback
W = os.path.dirname(os.path.abspath(__file__))
job = json.load(open(sys.argv[sys.argv.index("--") + 1]))
SCRIPTS = job.get("scripts", os.path.join(W, "scripts_dev"))
sys.path.insert(0, SCRIPTS)
sys.path.insert(1, r"E:\Model game khủng long\scripts")
sys.path.insert(2, W)
res = {"job": job, "t": {}}
def T(k, t0): res["t"][k] = round(time.time() - t0, 1)
try:
    import pf_anim, pf_clips_human as CH
    import pf_player_ship as SHIP
    t0 = time.time()
    which = job.get("which")
    if which:
        B, dt = SHIP.bake(which)
        res["baked"] = sorted(B.meta.keys()); T("bake", t0)
        SHIP.store_meta(B)
        res["meta"] = {k: {kk: v for kk, v in m.items() if kk != "fps"} for k, m in B.meta.items()}
        res["char_report"] = getattr(B, "char_report", None)
        res["weapon_report"] = getattr(B, "weapon_report", None)
    import c_audit
    if job.get("audit"):
        t0 = time.time()
        res["audit"] = c_audit.audit(job["audit"], res.get("meta", {}))
        T("audit", t0)
    if job.get("sheets"):
        import c_render
        t0 = time.time()
        res["sheets"] = c_render.sheets(job["sheets"], os.path.join(W, "sheets"))
        T("render", t0)
    if job.get("save_as"):
        t0 = time.time()
        arm = bpy.data.objects["PLAYER_Survivor_Rig"]
        arm.animation_data.action = None
        bpy.context.preferences.filepaths.save_version = 0      # never rotate the .blend1 backups
        bpy.ops.wm.save_as_mainfile(filepath=job["save_as"], copy=False)
        res["saved"] = job["save_as"]; T("save", t0)
    if job.get("export_staging"):
        t0 = time.time()
        res["export"] = SHIP.export_staging(job.get("manifest_extra", {}))
        T("export", t0)
    res["_ok"] = True
except Exception:
    res["_ok"] = False; res["error"] = traceback.format_exc()
out = job.get("out", os.path.join(W, "c_result.json"))
json.dump(res, open(out, "w"), indent=1, default=lambda o: float(o) if hasattr(o, "__float__") else str(o))
open(out + ".done", "w").write("done")
