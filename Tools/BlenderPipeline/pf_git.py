"""Windows git helper (run from Blender's Python on the host)."""
import subprocess
G = r"C:\Program Files\Git\bin\git.exe"; R = r"E:\LAST OF THE PRIMAL"
TRAILER = "\n\nCo-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>\nClaude-Session: https://claude.ai/code/session_011XFXLDMGrfwvsgLt6sXquu"

def g(*a):
    p = subprocess.run([G, "-C", R] + list(a), capture_output=True, text=True, encoding="utf-8", errors="replace")
    return p.returncode, (p.stdout + p.stderr)[-2000:]

def commit(title, body=""):
    g("add", "-A")
    msg = title + ("\n\n" + body if body else "") + TRAILER
    rc, out = g("-c", "user.name=hoainamcode22", "-c", "user.email=hoainamcode22@users.noreply.github.com", "commit", "-m", msg)
    return rc, g("log", "--oneline", "-1")[1]
