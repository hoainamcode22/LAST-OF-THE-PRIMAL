# Launch Unity batch jobs from Blender's Python (Windows side) and read logs.
import subprocess, os, tempfile, bpy
EXE = r"E:\game\6000.3.10f1\Editor\Unity.exe"
PROJ = r"E:\LAST OF THE PRIMAL"

def start(method, tag, extra=()):
    log = os.path.join(tempfile.gettempdir(), f"lotp_{tag}.log")
    if os.path.exists(log): os.remove(log)
    p = subprocess.Popen([EXE, "-batchmode", "-quit", "-projectPath", PROJ, "-executeMethod", method, "-logFile", log, *extra], creationflags=0x08000000)
    bpy.app.driver_namespace["unity_" + tag] = p.pid
    return p.pid, log

def status(tag, keys=("[Primal", "error CS", "Exception", "FAIL")):
    log = os.path.join(tempfile.gettempdir(), f"lotp_{tag}.log")
    pid = bpy.app.driver_namespace.get("unity_" + tag)
    alive = pid is not None and str(pid) in subprocess.run(f'tasklist /FI "PID eq {pid}"', shell=True, capture_output=True, text=True).stdout
    s = open(log, encoding="utf-8", errors="ignore").read() if os.path.exists(log) else ""
    lines = s.splitlines()
    key = [l[:400] for l in lines if any(k in l for k in keys)]
    code = None
    for l in lines[::-1]:
        if "Application will terminate with return code" in l or "return code" in l:
            code = l[-40:]; break
    return dict(alive=alive, size=len(s), key=key[:120], exit=code, tail=[l[:200] for l in lines[-5:]])
