import bpy, subprocess, os, json
W = r"E:\Model game khủng long\renders\characters\C_work"
def launch(tag, job, blend="c_work.blend"):
    job["out"] = os.path.join(W, f"res_{tag}.json")
    jp = os.path.join(W, f"job_{tag}.json"); json.dump(job, open(jp, "w"), indent=1)
    lf = open(os.path.join(W, f"log_{tag}.txt"), "w")
    b = blend if os.path.isabs(blend) else os.path.join(W, blend)
    return subprocess.Popen([bpy.app.binary_path, "-b", b, "--python", os.path.join(W, "c_run.py"), "--", jp], stdout=lf, stderr=subprocess.STDOUT, cwd=W).pid
