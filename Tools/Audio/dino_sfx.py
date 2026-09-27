"""PRIMAL FRONTIER - original synthesized creature voices (no samples). Writes 16-bit mono WAV."""
import numpy as np, os, sys
from scipy import signal
from scipy.io import wavfile
SR = 44100
rng = np.random.default_rng(3)
OUT = sys.argv[1] if len(sys.argv) > 1 else "sfx"
os.makedirs(OUT, exist_ok=True)

def env(n, a=0.05, r=0.4, shape=1.0):
    t = np.linspace(0, 1, n); e = np.minimum(1, t / max(a, 1e-3)) * np.clip((1 - t) / max(r, 1e-3), 0, 1) ** shape
    return e

def voice(dur, f0, f1, harm=12, formants=((500, 1.0), (1200, 0.5), (2600, 0.2)), noise=0.3, rough=0.2, vib=4.0, seed=0):
    n = int(dur * SR); t = np.arange(n) / SR
    r = np.random.default_rng(seed)
    f = np.geomspace(f0, f1, n) * (1 + 0.02 * np.sin(2 * np.pi * vib * t) + rough * 0.08 * signal.lfilter([1], [1, -0.995], r.standard_normal(n)) / 20)
    ph = 2 * np.pi * np.cumsum(f) / SR
    src = sum(np.sin(k * ph) / k ** 0.9 for k in range(1, harm + 1))
    src = src * (1 + rough * r.standard_normal(n) * 0.5)                       # growl / roughness
    src += noise * r.standard_normal(n)
    out = np.zeros(n)
    for fc, g in formants:
        b, a = signal.iirpeak(min(fc, SR / 2 - 100) / (SR / 2), Q=4); out += g * signal.lfilter(b, a, src)
    return out

def save(name, x, lp=None, hp=40, reverb=0.0, gain=0.9):
    if lp: b, a = signal.butter(4, lp / (SR / 2)); x = signal.lfilter(b, a, x)
    if hp: b, a = signal.butter(2, hp / (SR / 2), 'high'); x = signal.lfilter(b, a, x)
    if reverb > 0:
        ir = rng.standard_normal(int(SR * 1.8)) * np.exp(-np.linspace(0, 7, int(SR * 1.8))); ir /= np.abs(ir).sum() / 12
        x = x + reverb * signal.fftconvolve(x, ir)[:len(x) + 0]
    x = x / (np.abs(x).max() + 1e-9) * gain
    fade = int(0.02 * SR); x[:fade] *= np.linspace(0, 1, fade); x[-fade:] *= np.linspace(1, 0, fade)
    wavfile.write(os.path.join(OUT, name + ".wav"), SR, (x * 32767).astype(np.int16))

# species voices: (base f0 range, formants, roughness)
S = {
    "Triceratops":     dict(f=(70, 55), form=((300, 1.0), (700, 0.5), (1600, 0.15)), rough=0.5, noise=0.25, call_dur=1.4),
    "Parasaurolophus": dict(f=(140, 105), form=((420, 1.0), (900, 0.8), (2100, 0.2)), rough=0.05, noise=0.08, call_dur=2.2),
    "Ankylosaurus":    dict(f=(60, 48), form=((260, 1.0), (620, 0.4), (1400, 0.1)), rough=0.6, noise=0.3, call_dur=1.0),
    "Velociraptor":    dict(f=(900, 1300), form=((1500, 1.0), (3200, 0.5), (5200, 0.2)), rough=0.3, noise=0.35, call_dur=0.45),
    "Carnotaurus":     dict(f=(95, 70), form=((350, 1.0), (900, 0.6), (2200, 0.25)), rough=0.8, noise=0.45, call_dur=1.8),
    "Spinosaurus":     dict(f=(80, 60), form=((320, 1.0), (800, 0.5), (1900, 0.3)), rough=0.7, noise=0.6, call_dur=1.8),
    "Apex":            dict(f=(55, 38), form=((220, 1.0), (560, 0.7), (1500, 0.3)), rough=1.0, noise=0.5, call_dur=2.8),
    "Pteranodon":      dict(f=(600, 480), form=((1100, 1.0), (2600, 0.4), (4000, 0.1)), rough=0.3, noise=0.2, call_dur=0.6),
    "Mosasaurus":      dict(f=(50, 40), form=((200, 1.0), (500, 0.4), (1200, 0.1)), rough=0.5, noise=0.4, call_dur=1.6),
}
for name, p in S.items():
    f0, f1 = p["f"]
    for i in range(2):
        d = p["call_dur"] * (0.9 + 0.2 * i)
        x = voice(d, f0 * (1 + 0.05 * i), f1, formants=p["form"], noise=p["noise"], rough=p["rough"] * 0.6, seed=10 + i) * env(int(d * SR), 0.12, 0.5)
        save(f"SFX_{name}_Call_{i + 1:02d}", x, lp=6000 if f0 < 300 else 9000)
    rd = p["call_dur"] * 1.4
    for i in range(2):
        x = voice(rd, f0 * 1.25, f1 * 0.85, formants=p["form"], noise=p["noise"] * 1.5, rough=p["rough"] * 1.4 + 0.2, vib=6, seed=20 + i) * env(int(rd * SR), 0.18, 0.35, 1.5)
        save(f"SFX_{name}_Roar_{i + 1:02d}", x, lp=7000, reverb=0.25)
    for i in range(2):
        hd = 0.45
        x = voice(hd, f0 * 1.8, f0 * 1.2, formants=p["form"], noise=p["noise"], rough=p["rough"], seed=30 + i) * env(int(hd * SR), 0.02, 0.8)
        save(f"SFX_{name}_Hurt_{i + 1:02d}", x, lp=8000)
    dd = p["call_dur"] * 1.6
    x = voice(dd, f0 * 1.1, f0 * 0.55, formants=p["form"], noise=p["noise"] * 0.8, rough=p["rough"], vib=2, seed=40) * env(int(dd * SR), 0.1, 0.9, 2)
    save(f"SFX_{name}_Death_01", x, lp=5000, reverb=0.2)
# distant roar for the predator warning: the apex voice far away (low-passed, long reverb)
x = voice(3.2, 60, 38, formants=S["Apex"]["form"], noise=0.5, rough=1.0, vib=5, seed=77) * env(int(3.2 * SR), 0.25, 0.4, 1.5)
save("SFX_RoarDistant_01", x, lp=900, reverb=1.2, gain=0.8)
x = voice(2.6, 75, 50, formants=S["Carnotaurus"]["form"], noise=0.5, rough=0.9, vib=5, seed=78) * env(int(2.6 * SR), 0.2, 0.4, 1.5)
save("SFX_RoarDistant_02", x, lp=1100, reverb=1.2, gain=0.8)
print(len(os.listdir(OUT)))
