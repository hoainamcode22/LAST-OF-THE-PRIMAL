"""PRIMAL FRONTIER - original sound effects synthesized from scratch (numpy / scipy). No samples, no third-party audio.
usage: python3 sfx_synth.py <out_dir>   -> SFX_<Id>_<n>.wav (mono 16-bit 44.1 kHz) and AMB_<Name>_Loop.wav"""
import os, sys, numpy as np
from scipy import signal
from scipy.io import wavfile

SR = 44100
rng = np.random.default_rng(1234)

def t(d): return np.arange(int(SR * d)) / SR
def noise(d): return rng.standard_normal(int(SR * d))
def env(n, a=0.002, d=0.1, shape=4.0):
    x = np.arange(n) / SR
    e = np.minimum(1.0, x / max(a, 1e-4)) * np.exp(-np.maximum(0, x - a) * shape / max(d, 1e-4))
    return e
def bp(x, lo, hi, order=2): b, a = signal.butter(order, [lo / (SR / 2), min(hi, SR / 2 - 100) / (SR / 2)], 'band'); return signal.lfilter(b, a, x)
def lp(x, f, order=2): b, a = signal.butter(order, f / (SR / 2), 'low'); return signal.lfilter(b, a, x)
def hp(x, f, order=2): b, a = signal.butter(order, f / (SR / 2), 'high'); return signal.lfilter(b, a, x)
def reson(x, f, q):
    w = 2 * np.pi * f / SR; r = np.exp(-w / (2 * q)); b = [1 - r]; a = [1, -2 * r * np.cos(w), r * r]; return signal.lfilter(b, a, x)
def mode(d, f, decay, amp=1.0, ph=0.0): x = t(d); return amp * np.sin(2 * np.pi * f * x + ph) * np.exp(-x * decay)
def pad(x, d): n = int(SR * d); return np.pad(x, (0, max(0, n - len(x))))[:n] if len(x) < n else x
def mix(*xs):
    n = max(len(x) for x in xs); out = np.zeros(n)
    for x in xs: out[:len(x)] += x
    return out
def norm(x, peak=0.8):
    m = np.max(np.abs(x)) + 1e-9; x = x / m * peak
    fade = min(len(x), int(SR * 0.004)); x[-fade:] *= np.linspace(1, 0, fade)
    return x
def save(out, name, x, peak=0.8):
    x = norm(np.asarray(x, np.float64), peak)
    wavfile.write(os.path.join(out, name + ".wav"), SR, (x * 32767).astype(np.int16))

def grains(d, rate, lo, hi, gdur=0.004):
    """crunchy granular texture (sand, crumbs, leaves)"""
    n = int(SR * d); out = np.zeros(n)
    k = rng.poisson(rate * d)
    for i in range(k):
        p = rng.integers(0, max(1, n - int(SR * gdur)))
        g = noise(gdur) * np.hanning(int(SR * gdur)) * rng.uniform(0.3, 1.0)
        out[p:p + len(g)] += g
    return bp(out, lo, hi)

def glottal(d, f0a, f0b, jitter=0.02):
    """pulse train with a falling pitch (voice source)"""
    x = t(d); f0 = np.linspace(f0a, f0b, len(x)) * (1 + jitter * lp(rng.standard_normal(len(x)), 30) * 5)
    ph = np.cumsum(f0) / SR; src = (ph % 1.0) ** 3 - 0.25
    return src

def formants(x, fs, qs=(8, 10, 12), gains=(1.0, 0.6, 0.3)):
    return sum(g * reson(x, f, q) for f, q, g in zip(fs, qs, gains))

# ---------------------------------------------------------------- effects
def foot_sand(): return mix(grains(0.16, 900, 700, 5000) * env(int(SR * 0.16), 0.01, 0.06, 3), lp(noise(0.08), 400) * env(int(SR * 0.08), 0.003, 0.03) * 0.6)
def foot_dirt(): return mix(lp(noise(0.12), 500) * env(int(SR * 0.12), 0.002, 0.03, 4) * 1.2, bp(noise(0.05), 900, 3000) * env(int(SR * 0.05), 0.001, 0.015) * 0.4, grains(0.1, 300, 1000, 4000) * env(int(SR * 0.1), 0.005, 0.04) * 0.5)
def foot_rock(): return mix(hp(noise(0.03), 2000) * env(int(SR * 0.03), 0.0005, 0.008) * 0.7, lp(noise(0.08), 350) * env(int(SR * 0.08), 0.001, 0.02) * 0.8, grains(0.06, 400, 2500, 7000) * 0.3)
def foot_mud():
    n = noise(0.22); f = np.linspace(300, 1100, len(n)); y = np.zeros(len(n)); state = np.zeros(2)
    y = sum(reson(n[i::4], 0, 1)[:0].sum() for i in range(0)) if False else bp(n, 250, 1200) * np.sin(np.linspace(0, np.pi, len(n))) ** 2
    return mix(y * 1.2, lp(noise(0.1), 200) * env(int(SR * 0.1), 0.004, 0.04) * 0.8)
def foot_water():
    base = bp(noise(0.3), 800, 5000) * env(int(SR * 0.3), 0.004, 0.09, 3)
    bub = sum(pad(mode(0.06, f, 40) * np.linspace(1, 0, int(SR * 0.06)), 0.3) for f in rng.uniform(400, 1200, 3)) * 0.3
    return mix(base, bub)
def land(): return mix(lp(noise(0.35), 250) * env(int(SR * 0.35), 0.003, 0.12, 3) * 1.4, grains(0.3, 600, 800, 4000) * env(int(SR * 0.3), 0.01, 0.1) * 0.6)
def hit_flesh(): return mix(mode(0.18, rng.uniform(80, 110), 28, 1.2), bp(noise(0.05), 900, 3200) * env(int(SR * 0.05), 0.0008, 0.015) * 0.7, lp(noise(0.12), 600) * env(int(SR * 0.12), 0.002, 0.04) * 0.6)
def hit_heavy(): return mix(mode(0.35, rng.uniform(55, 70), 14, 1.4), bp(noise(0.12), 500, 2500) * env(int(SR * 0.12), 0.001, 0.05) * 0.8, grains(0.2, 500, 1500, 5000) * env(int(SR * 0.2), 0.005, 0.08) * 0.5)
def grunt(d=0.28, f0=(135, 105), vowel=(650, 1100, 2500)):
    src = glottal(d, *f0); v = formants(src, vowel) * env(len(src), 0.02, d * 0.5, 2.5)
    br = bp(noise(d), 500, 3000) * env(len(src), 0.01, d * 0.6, 2) * 0.15
    return mix(v, br)
def death(): return mix(grunt(0.7, (125, 70), (600, 1000, 2400)), pad(bp(noise(0.9), 300, 2500) * env(int(SR * 0.9), 0.1, 0.5, 2) * 0.25, 0.9))
def wood_chop():
    f = rng.uniform(0.9, 1.1)
    return mix(bp(noise(0.15), 700 * f, 2400 * f) * env(int(SR * 0.15), 0.0008, 0.04) * 0.9, mode(0.25, 190 * f, 18, 0.8), mode(0.25, 430 * f, 25, 0.5), mode(0.2, 760 * f, 35, 0.25),
               hp(noise(0.02), 3000) * env(int(SR * 0.02), 0.0003, 0.004) * 0.6)
def stone_hit():
    f = rng.uniform(0.9, 1.15)
    return mix(hp(noise(0.02), 2500) * env(int(SR * 0.02), 0.0002, 0.003), mode(0.3, 1850 * f, 30, 0.5), mode(0.3, 2950 * f, 38, 0.35), mode(0.25, 4300 * f, 45, 0.2),
               lp(noise(0.06), 500) * env(int(SR * 0.06), 0.001, 0.02) * 0.5)
def leaf_rustle(): return grains(0.45, 1800, 1500, 8000) * np.sin(np.linspace(0, np.pi, int(SR * 0.45))) ** 1.5
def pickup(): return mix(grains(0.18, 700, 1200, 6000) * env(int(SR * 0.18), 0.02, 0.07), pad(lp(noise(0.03), 800) * env(int(SR * 0.03), 0.001, 0.01) * 0.5, 0.18))
def craft(): return mix(*[pad(np.concatenate([np.zeros(int(SR * s)), stone_hit()[:int(SR * 0.08)] * 0.4 + bp(noise(0.08), 1500, 5000) * env(int(SR * 0.08), 0.01, 0.03) * 0.3]), 0.5) for s in (0.0, 0.18)])
def knap(): return mix(stone_hit(), hp(noise(0.05), 4000) * env(int(SR * 0.05), 0.0002, 0.01) * 0.5)
def build(): x = wood_chop(); return mix(x, mode(0.3, 120, 12, 0.8))
def eat(): return mix(grains(0.25, 2200, 1800, 6000) * env(int(SR * 0.25), 0.005, 0.08, 2.5), lp(noise(0.12), 300) * env(int(SR * 0.12), 0.01, 0.05) * 0.5)
def drink():
    out = []
    for i in range(2):
        g = t(0.09); f = np.linspace(170, 330, len(g)); ph = np.cumsum(f) / SR
        out.append(np.sin(2 * np.pi * ph) * np.sin(np.linspace(0, np.pi, len(g))) ** 2); out.append(np.zeros(int(SR * 0.12)))
    return mix(np.concatenate(out) * 0.8, pad(bp(noise(0.3), 300, 1500) * 0.08, 0.42))
def splash(): return mix(bp(noise(0.5), 600, 6000) * env(int(SR * 0.5), 0.005, 0.18, 3), lp(noise(0.2), 300) * env(int(SR * 0.2), 0.003, 0.06) * 0.6)
def fire_ignite():
    n = noise(0.9); x = np.zeros(len(n)); fc = np.geomspace(200, 3000, len(n))
    x = lp(n, 1200) * np.sin(np.linspace(0, np.pi, len(n))) ** 1.2
    return mix(x, crackle(0.9, 40) * 0.7)
def crackle(d, rate):
    n = int(SR * d); out = np.zeros(n)
    for i in range(rng.poisson(rate * d)):
        p = rng.integers(0, max(1, n - 400)); c = hp(noise(0.006), 1500) * env(int(SR * 0.006), 0.0002, 0.002) * rng.uniform(0.2, 1)
        out[p:p + len(c)] += c
    return out
def fire_ext(): return mix(hp(noise(1.4), 2500) * env(int(SR * 1.4), 0.02, 0.6, 2.5), crackle(1.0, 30) * 0.4)
def sizzle(): return mix(hp(noise(2.0), 3000) * 0.25 * (0.7 + 0.3 * np.sin(np.linspace(0, 12, int(SR * 2)))), crackle(2.0, 60) * 0.6)
def whoosh(d=0.32):
    n = noise(d); c = np.sin(np.linspace(0, np.pi, len(n))) ** 2
    lo = bp(n, 300, 1400); hi = bp(n, 1200, 4000)
    k = np.linspace(0, 1, len(n))
    return (lo * (1 - k) + hi * k) * c
def spear_impact(): return mix(mode(0.2, 140, 20, 1.0), bp(noise(0.08), 800, 3000) * env(int(SR * 0.08), 0.001, 0.02) * 0.8, hp(noise(0.02), 3000) * env(int(SR * 0.02), 0.0003, 0.004) * 0.5)
def bow_draw():
    d = 0.8; n = noise(d); f = np.linspace(700, 1300, len(n))
    y = reson(bp(n, 400, 3000), 900, 20) * 0.6 + bp(n, 2000, 6000) * 0.05
    return y * np.sin(np.linspace(0, np.pi / 2, len(n)))
def ks(f, d, damp=0.996):
    n = int(SR * d); N = int(SR / f); buf = rng.uniform(-1, 1, N); out = np.zeros(n)
    for i in range(n):
        out[i] = buf[i % N]; buf[i % N] = damp * 0.5 * (buf[i % N] + buf[(i + 1) % N])
    return out
def bow_release(): return mix(ks(105, 0.5) * 0.8, pad(whoosh(0.25) * 0.5, 0.5))
def arrow_impact(): return mix(mode(0.15, 220, 30, 0.8), bp(noise(0.05), 1000, 3500) * env(int(SR * 0.05), 0.0005, 0.012) * 0.7, ks(180, 0.2, 0.99) * 0.25)
def marimba(freqs, gap=0.12, d=0.6):
    out = np.zeros(int(SR * (d + gap * len(freqs))))
    for i, f in enumerate(freqs):
        x = mode(d, f, 7, 1.0) + mode(d, f * 4, 18, 0.25); s = int(SR * gap * i); out[s:s + len(x)] += x
    return out
def ui_click(): return mix(mode(0.05, 1200, 90, 0.6), lp(noise(0.02), 2000) * env(int(SR * 0.02), 0.0005, 0.005) * 0.4)
def dino_step(heavy=False):
    f = 32 if heavy else 45
    return mix(mode(0.9 if heavy else 0.6, f, 5 if heavy else 8, 1.6), lp(noise(0.6), 180) * env(int(SR * 0.6), 0.004, 0.25, 3) * 1.2, grains(0.5, 500, 600, 3000) * env(int(SR * 0.5), 0.02, 0.2) * 0.4)

# ---------------------------------------------------------------- ambience loops (seamless crossfade)
def loopify(x, xf=1.5):
    n = int(SR * xf); a = x[:n].copy(); x = x[n:].copy(); w = np.linspace(0, 1, n)
    x[-n:] = x[-n:] * (1 - w) + a * w
    return x
def ocean(d=24):
    n = noise(d + 2); base = lp(n, 500) * 0.7 + bp(n, 800, 3000) * 0.25
    tt = t(d + 2); sw = 0.55 + 0.45 * np.sin(2 * np.pi * tt / 7.3) * np.sin(2 * np.pi * tt / 11.1 + 1)
    foam = hp(noise(d + 2), 2500) * 0.08 * np.clip(np.sin(2 * np.pi * tt / 7.3 - 0.8), 0, 1) ** 3
    return loopify(base * sw + foam)
def wind(d=24):
    n = noise(d + 2); tt = t(d + 2); g = 0.5 + 0.5 * np.sin(2 * np.pi * tt / 9.0) * np.sin(2 * np.pi * tt / 5.3)
    return loopify(bp(n, 150, 900) * (0.4 + 0.6 * g) + bp(n, 900, 2500) * 0.15 * g)
def chirp_bird(d=0.25):
    x = t(d); f = 3200 + 900 * np.sin(2 * np.pi * 14 * x) + np.linspace(600, -400, len(x))
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * np.sin(np.linspace(0, np.pi, len(x))) ** 2
def forest(d=26):
    total = int(SR * (d + 2)); out = lp(noise(d + 2), 1200) * 0.05
    out += grains(d + 2, 300, 2000, 8000) * 0.05
    tt = t(d + 2); cic = np.sin(2 * np.pi * 4800 * tt) * (0.5 + 0.5 * np.sign(np.sin(2 * np.pi * 42 * tt))) * np.clip(np.sin(2 * np.pi * tt / 13), 0, 1) ** 2
    out += cic * 0.02
    for i in range(10):
        s = rng.integers(0, total - SR); b = np.concatenate([chirp_bird(rng.uniform(0.12, 0.3)) for _ in range(rng.integers(2, 5))]) * rng.uniform(0.04, 0.1)
        out[s:s + len(b)] += b[:total - s]
    return loopify(out)
def night(d=24):
    tt = t(d + 2); out = bp(noise(d + 2), 150, 600) * 0.08
    for k, f in enumerate((4200, 4700, 3900)):
        ph = rng.uniform(0, 1); pulse = (np.sin(2 * np.pi * (2.2 + 0.3 * k) * tt + ph) > 0.6).astype(float)
        out += np.sin(2 * np.pi * f * tt) * pulse * (np.sin(2 * np.pi * 60 * tt) > 0) * 0.02
    return loopify(out)
def rain(d=18): return loopify(bp(noise(d + 2), 800, 7000) * 0.3 + grains(d + 2, 3000, 2000, 9000) * 0.2 + lp(noise(d + 2), 300) * 0.15)
def fire_loop(d=12): return loopify(lp(noise(d + 2), 250) * 0.3 + crackle(d + 2, 25) * 0.9 + hp(noise(d + 2), 3000) * 0.02)
def storm(d=20):
    base = wind(d) * 1.5 + rain(d)[:int(SR * d) - int(SR * 1.5)][:len(wind(d))] if False else None
    w = wind(d); r = rain(d + 6)[:len(w)]; o = ocean(d + 4)[:len(w)]
    return w * 1.3 + r * 0.8 + o * 1.0
def thunder():
    d = 5.0; n = noise(d); x = lp(n, 120) * env(len(n), 0.05, 2.0, 2) * 1.5 + lp(n, 400) * env(len(n), 0.01, 0.6, 3) * 0.6
    return x + crackle(1.0, 80)[:len(x)] * 0.0 if False else x
def ship_creak():
    d = 1.4; n = bp(noise(d), 100, 2000); tt = t(d); f = 180 + 60 * np.sin(2 * np.pi * 0.7 * tt)
    y = np.zeros(len(n)); ph = 0.0
    carrier = np.sin(2 * np.pi * np.cumsum(f) / SR) * (1 + 0.5 * np.sign(np.sin(2 * np.pi * np.cumsum(f * 0.5) / SR)))
    return carrier * np.abs(n) * np.sin(np.linspace(0, np.pi, len(n))) ** 2 * 0.8
def wood_break(): return mix(mode(0.6, 90, 8, 1.4), bp(noise(0.6), 400, 3000) * env(int(SR * 0.6), 0.002, 0.25, 3), crackle(0.8, 120) * 1.2)
def breath(inhale=False):
    d = 0.9 if inhale else 1.1; n = noise(d); s = formants(bp(n, 300, 3000), (500, 1500, 2600), (4, 5, 6), (1, 0.5, 0.3))
    e = np.sin(np.linspace(0, np.pi, len(n))) ** (0.8 if inhale else 1.5)
    return s * e

def main(out):
    os.makedirs(out, exist_ok=True)
    amb = os.path.join(out, "..", "Ambience"); os.makedirs(amb, exist_ok=True)
    table = [("FootSand", foot_sand, 4), ("FootDirt", foot_dirt, 4), ("FootRock", foot_rock, 4), ("FootMud", foot_mud, 3), ("FootWater", foot_water, 3), ("Land", land, 2),
             ("HitFlesh", hit_flesh, 3), ("HitHeavy", hit_heavy, 2), ("HurtGrunt", lambda: grunt(rng.uniform(0.22, 0.32), (rng.uniform(125, 150), rng.uniform(95, 110))), 3), ("Death", death, 1),
             ("WoodChop", wood_chop, 4), ("StoneHit", stone_hit, 4), ("LeafRustle", leaf_rustle, 3), ("Pickup", pickup, 2),
             ("Craft", craft, 3), ("CraftKnap", knap, 3), ("Build", build, 3), ("Eat", eat, 3), ("Drink", drink, 2), ("WaterSplash", splash, 2),
             ("FireIgnite", fire_ignite, 1), ("FireExtinguish", fire_ext, 1), ("Sizzle", sizzle, 1),
             ("SpearWhoosh", whoosh, 3), ("SpearImpact", spear_impact, 2), ("BowDraw", bow_draw, 1), ("BowRelease", bow_release, 2), ("ArrowImpact", arrow_impact, 2),
             ("UiClick", ui_click, 1), ("UiObjective", lambda: marimba([220, 330]), 1), ("UiRecipe", lambda: marimba([262, 330, 392]), 1),
             ("DinoStep", lambda: dino_step(False), 3), ("DinoStepHeavy", lambda: dino_step(True), 2),
             ("Thunder", thunder, 2), ("ShipCreak", ship_creak, 2), ("WoodBreak", wood_break, 1), ("BreathIn", lambda: breath(True), 1), ("BreathOut", breath, 2)]
    for name, fn, n in table:
        for i in range(n): save(out, f"SFX_{name}_{i + 1}", fn())
    for name, fn in (("Ocean", ocean), ("Wind", wind), ("Forest", forest), ("Night", night), ("Rain", rain), ("Fire", fire_loop), ("Storm", storm)):
        save(amb, f"AMB_{name}_Loop", fn(), 0.6)
    print("done", len(os.listdir(out)), "sfx")

if __name__ == "__main__": main(sys.argv[1])
