# CHAR P1 runner (container bpy 5.2.1): python run.py job.json
import sys, os, json, time, traceback
job = json.load(open(sys.argv[1]))
import bpy
W = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(W, "scripts"))
bpy.ops.wm.open_mainfile(filepath=job.get("blend", os.path.join(W, "c_work.blend")))
res = {"job": job, "t": {}}
def T(k, t0): res["t"][k] = round(time.time() - t0, 1)
try:
    import pf_anim, pf_clips_human as CH
    import pf_player_ship as SHIP
    SHIP.STAGING = os.path.join(W, "staging_new3"); SHIP.UNITY_META = os.path.join(W, "PLAYER_Survivor_anim.json")
    for code in job.get("pre", []): exec(code)
    which = job.get("which")
    if which:
        t0 = time.time(); B, dt = SHIP.bake(which)
        res["baked"] = sorted(B.meta.keys()); T("bake", t0)
        SHIP.store_meta(B)
        res["meta"] = {k: {kk: v for kk, v in m.items() if kk != "fps"} for k, m in B.meta.items()}
        res["char_report"] = getattr(B, "char_report", None)
    for code in job.get("post", []): exec(code)
    if job.get("audit"):
        import c_audit; t0 = time.time()
        res["audit"] = c_audit.audit(job["audit"], res.get("meta", {})); T("audit", t0)
    if job.get("sheets"):
        import c_render; t0 = time.time()
        res["sheets"] = c_render.sheets(job["sheets"], os.path.join(W, "sheets")); T("render", t0)
    if job.get("save_as"):
        t0 = time.time(); arm = bpy.data.objects["PLAYER_Survivor_Rig"]; arm.animation_data.action = None
        bpy.context.preferences.filepaths.save_version = 0
        bpy.ops.wm.save_as_mainfile(filepath=job["save_as"], copy=False); res["saved"] = job["save_as"]; T("save", t0)
    if job.get("export_staging"):
        t0 = time.time(); res["export"] = SHIP.export_staging(); T("export", t0)
    res["_ok"] = True
except Exception:
    res["_ok"] = False; res["error"] = traceback.format_exc()
out = job.get("out", os.path.join(W, "res.json"))
json.dump(res, open(out, "w"), indent=1, default=lambda o: float(o) if hasattr(o, "__float__") else str(o))
print("DONE", out, res["_ok"])
