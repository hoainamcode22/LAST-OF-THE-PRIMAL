import subprocess, os
D = r"E:\Model game khủng long\characters\dino_pc\D_20260930"
S = r"E:\Model game khủng long\scripts"
def launch(sids, flags, out, tag, shots=None, shotdir=None):
    env = dict(os.environ); env["PF_TAG"] = tag
    env["PF_SHOT_DIR"] = shotdir or os.path.join(D, "renders")
    if shots: env["PF_SHOTS"] = shots
    f = open(os.path.join(D, "logs", out + ".log"), "w", encoding="utf-8")
    p = subprocess.Popen([r"E:\Blender\blender.exe", "--background", "--factory-startup", os.path.join(D, "DINO_Work_D.blend"), "--python", os.path.join(S, "pf_dino_pc.py"), "--", os.path.join(D, "logs", out + ".json"), sids, *flags],
                         stdout=f, stderr=subprocess.STDOUT, cwd=D, env=env, creationflags=0x08000000)
    return p.pid
