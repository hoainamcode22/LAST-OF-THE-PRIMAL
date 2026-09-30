"""PRIMAL FRONTIER ambience synthesizer (WORLD agent). Original sounds only, made from noise, oscillators and filters.

usage: python3 ambience_synth.py <Assets/_Project/Audio/Ambience dir> [name ...]
  writes AMB_<Name>_Loop.wav (seamless loops: 2D beds stereo, 3D emitters mono) and OneShots/AMB_<Kind>_<n>.wav
  (mono one-shots: primitive bird calls, distant dinosaur calls, small night creatures, thunder). 16-bit 44.1 kHz.
  Every clip has its own seed: regenerating one never changes another. A name list regenerates only those clips.

The island is prehistoric (Cretaceous-like): no songbird melodies, no owls, no modern sounds. Life here is insects
(cicadas, crickets), frogs, simple bird calls (whistles, croaks, rattles, trills), distant dinosaur calls (low resonant
honks, closed-mouth booms, bellows), wind in giant ferns and conifers, water, rain, surf, lava.
"""
import os, sys
import numpy as np
from scipy import signal
from scipy.io import wavfile

SR = 44100
RNG = np.random.default_rng(1)


# ---------------------------------------------------------------- basics
def n_(d): return int(SR * d)
def t_(d): return np.arange(n_(d)) / SR
def white(d, rng): return rng.standard_normal(n_(d))
def pink(d, rng):
    """1/f noise (Voss-McCartney style via FFT shaping)"""
    n = n_(d); x = rng.standard_normal(n); X = np.fft.rfft(x); f = np.fft.rfftfreq(n, 1 / SR); f[0] = 1
    X /= np.sqrt(f); y = np.fft.irfft(X, n); return y / (np.std(y) + 1e-9)
def brown(d, rng):
    y = np.cumsum(rng.standard_normal(n_(d))); y = hp(y, 20); return y / (np.std(y) + 1e-9)
def bp(x, lo, hi, order=2): b, a = signal.butter(order, [lo / (SR / 2), min(hi, SR / 2 - 200) / (SR / 2)], 'band'); return signal.lfilter(b, a, x)
def lp(x, f, order=2): b, a = signal.butter(order, min(f, SR / 2 - 200) / (SR / 2), 'low'); return signal.lfilter(b, a, x)
def hp(x, f, order=2): b, a = signal.butter(order, f / (SR / 2), 'high'); return signal.lfilter(b, a, x)
def reson(x, f, q):
    w = 2 * np.pi * f / SR; r = np.exp(-w / (2 * q)); b = [1 - r]; a = [1, -2 * r * np.cos(w), r * r]; return signal.lfilter(b, a, x)
def slow_env(d, rng, rate=0.15, lo=0.0, hi=1.0, seed_pts=None):
    """smooth random envelope (cubic interpolation of random points every 1/rate s)"""
    n = n_(d); k = max(4, int(d * rate) + 4); pts = rng.uniform(lo, hi, k)
    xs = np.linspace(0, n, k); from scipy.interpolate import CubicSpline
    cs = CubicSpline(xs, pts, bc_type='natural'); return np.clip(cs(np.arange(n)), lo, hi)
def adsr(n, a, r, shape=2.0):
    x = np.ones(n); na = max(1, int(SR * a)); nr = max(1, int(SR * r))
    x[:na] = np.linspace(0, 1, na) ** 1.5
    if nr < n: x[-nr:] *= np.linspace(1, 0, nr) ** shape
    return x
def pan(x, p, itd=True):
    """constant-power pan p in -1..1, with a small interaural delay"""
    a = (p + 1) * np.pi / 4; l, r = np.cos(a) * x, np.sin(a) * x
    if itd:
        dly = int(abs(p) * 0.0006 * SR)
        if dly > 0:
            if p > 0: l = np.concatenate([np.zeros(dly), l[:-dly]])
            else: r = np.concatenate([np.zeros(dly), r[:-dly]])
    return np.stack([l, r])
def add_at(buf, x, pos):
    """add x into buf at pos (wraps around: loop-safe)"""
    n = buf.shape[-1]; L = x.shape[-1]
    idx = (np.arange(L) + pos) % n
    if buf.ndim == 1: np.add.at(buf, idx, x)
    else:
        for c in range(buf.shape[0]): np.add.at(buf[c], idx, x[c] if x.ndim > 1 else x)
def ir(d, rng, decay, lo=200, hi=6000, stereo=False, pre=0.0):
    """synthetic room / forest impulse response: decaying filtered noise"""
    n = n_(d); tt = np.arange(n) / SR
    ch = []
    for _ in range(2 if stereo else 1):
        x = bp(rng.standard_normal(n), lo, hi) * np.exp(-tt / decay)
        if pre > 0: x[:n_(pre)] = 0
        x[0] = 1.0 if not stereo else 0.7
        ch.append(x / np.sqrt(np.sum(x ** 2)))
    return np.stack(ch) if stereo else ch[0]
def verb(x, rng, d=1.2, decay=0.35, wet=0.35, lo=200, hi=5000):
    r = ir(d, rng, decay, lo, hi); y = signal.fftconvolve(x, r)[:len(x) + n_(d)]
    xx = np.pad(x, (0, len(y) - len(x))); return xx * (1 - wet) + y * wet
def loop_xf(x, xf):
    """seamless loop: the extra tail (xf s) is cross-faded (equal power) into the head"""
    n = n_(xf); w = np.linspace(0, np.pi / 2, n)
    if x.ndim == 1:
        head, body, tail = x[:n], x[:-n].copy(), x[-n:]
        body[:n] = head * np.sin(w) + tail * np.cos(w); return body
    out = []
    for c in range(x.shape[0]): out.append(loop_xf(x[c], xf))
    return np.stack(out)
def rms_norm(x, db, peak_db=-2.0):
    r = np.sqrt(np.mean(x ** 2)) + 1e-12; y = x * (10 ** (db / 20) / r)
    pk = np.max(np.abs(y)); lim = 10 ** (peak_db / 20)
    if pk > lim:        # soft limit the few peaks above the ceiling
        y = np.tanh(y / lim) * lim
    return y
def fade(x, a=0.004, r=0.03):
    n = x.shape[-1]; e = np.ones(n); na, nr = n_(a), n_(r)
    e[:na] = np.linspace(0, 1, na); e[-nr:] *= np.linspace(1, 0, nr); return x * e
def dc_block(x):
    b, a = signal.butter(1, 18 / (SR / 2), 'high')
    y = signal.lfilter(b, a, np.concatenate([x, x]))      # settle on a first pass, keep the second (periodic signals stay periodic)
    return y[len(x):]
def save(path, x):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    y = np.clip(x, -1, 1)
    # no DC: a gentle high-pass run over the loop twice around (so the loop point stays seamless)
    if y.ndim == 1: y = dc_block(y)
    else: y = np.stack([dc_block(c) for c in y])
    if y.ndim == 2: y = y.T
    wavfile.write(path, SR, (y * 32767).astype(np.int16))
    print(f"{os.path.basename(path):34s} {'stereo' if y.ndim == 2 else 'mono  '} {y.shape[0] / SR:5.1f} s  rms {20 * np.log10(np.sqrt(np.mean(y.astype(float) ** 2)) + 1e-12):6.1f} dB")


# ---------------------------------------------------------------- sound atoms
def chirp_tone(d, f0, f1, rng, harm=(1.0,), vib=0.0, vib_rate=0.0, curve=1.0):
    tt = t_(d); k = (tt / d) ** curve; f = f0 + (f1 - f0) * k
    if vib: f = f * (1 + vib * np.sin(2 * np.pi * vib_rate * tt + rng.uniform(0, 6)))
    ph = 2 * np.pi * np.cumsum(f) / SR
    return sum(a * np.sin((i + 1) * ph) for i, a in enumerate(harm))
def drop(rng, f=None, d=None):
    """one rain drop on a leaf: a tiny click + a short damped ping"""
    d = d or rng.uniform(0.006, 0.02); n = n_(d); tt = np.arange(n) / SR
    click = rng.standard_normal(n) * np.exp(-tt / 0.0015)
    f = f or rng.uniform(1800, 6500)
    ping = np.sin(2 * np.pi * f * tt) * np.exp(-tt / rng.uniform(0.002, 0.006)) * rng.uniform(0.2, 0.7)
    return hp(click, 1200, 1) * 0.8 + ping
def bubble(rng, f0=None, d=None):
    """a water bubble: a sine whose pitch rises quickly as it decays (Minnaert-like)"""
    d = d or rng.uniform(0.012, 0.05); tt = t_(d); f0 = f0 or rng.uniform(400, 1600)
    f = f0 * (1 + 0.9 * tt / d); ph = 2 * np.pi * np.cumsum(f) / SR
    return np.sin(ph) * np.exp(-tt / (d * 0.35)) * (1 - np.exp(-tt / 0.0015))
def grains(buf, rng, rate, maker, gain=1.0, stereo_spread=0.0, env=None):
    """scatter 'rate' per second events made by maker() into buf (mono or stereo), optional density envelope"""
    n = buf.shape[-1]; d = n / SR; count = rng.poisson(rate * d)
    pos = rng.integers(0, n, count)
    if env is not None:
        keep = rng.uniform(0, 1, count) < env[pos % len(env)] / max(1e-6, env.max()); pos = pos[keep]
    for p in pos:
        g = maker(rng) * gain * rng.uniform(0.3, 1.0)
        if buf.ndim == 2: add_at(buf, pan(g, rng.uniform(-stereo_spread, stereo_spread), False), p)
        else: add_at(buf, g, p)
    return buf


# ---------------------------------------------------------------- 2D beds (stereo loops)
XF = 2.0
def forest_day(rng):
    d = 32 + XF; n = n_(d); out = np.zeros((2, n))
    gust = slow_env(d, rng, 0.18, 0.15, 1.0) ** 1.5
    for c in range(2):
        g = np.roll(gust, int(c * 0.25 * SR))
        leaves = bp(pink(d, rng), 1400, 7500) * (0.25 + 0.75 * g) * 0.35
        flutter = np.zeros(n); grains(flutter, rng, 900, lambda r: hp(r.standard_normal(n_(0.004)) * np.hanning(n_(0.004)), 1800, 1), 0.5, env=g)
        air = lp(pink(d, rng), 380) * 0.18
        murmur = bp(pink(d, rng), 180, 700) * 0.06 * (0.6 + 0.4 * g)
        out[c] = leaves + flutter * 0.5 + air + murmur
    # a big trunk creaking now and then (giant conifers / tree ferns in the wind)
    for _ in range(3):
        dd = rng.uniform(0.5, 1.2); tt = t_(dd); f = rng.uniform(150, 260) * (1 + 0.15 * np.sin(2 * np.pi * rng.uniform(1, 3) * tt))
        car = signal.sawtooth(2 * np.pi * np.cumsum(f) / SR, 0.3) * np.abs(bp(rng.standard_normal(len(tt)), 50, 400))
        cr = bp(car, 200, 1800) * np.sin(np.linspace(0, np.pi, len(tt))) ** 2 * 0.25
        add_at(out, pan(cr, rng.uniform(-0.8, 0.8)), rng.integers(0, n))
    return rms_norm(loop_xf(out, XF), -26)

def insects_day(rng):
    d = 24 + XF; n = n_(d); out = np.zeros((2, n)); tt = t_(d)
    # cicadas: a buzz band pulsed at 100-180 Hz, each swelling and fading over several seconds
    for i in range(4):
        fc = rng.uniform(3800, 6200); pr = rng.uniform(95, 180)
        pulse = (0.5 + 0.5 * np.sign(np.sin(2 * np.pi * pr * tt + rng.uniform(0, 6)))) * (0.6 + 0.4 * np.sin(2 * np.pi * pr * 2 * tt))
        band = bp(white(d, rng), fc * 0.85, fc * 1.15, 3) * pulse
        sw = slow_env(d, rng, 0.25, 0.0, 1.0) ** 2.5
        far = 0.35 if i >= 2 else 1.0
        x = band * sw * far
        if i >= 2: x = lp(x, 4500)
        out += pan(x, rng.uniform(-0.9, 0.9))
    # flies / small bees drifting past: a soft wing hum
    for _ in range(5):
        dd = rng.uniform(1.2, 3.0); t2 = t_(dd); f = rng.uniform(170, 260) * (1 + 0.05 * np.sin(2 * np.pi * rng.uniform(3, 7) * t2))
        hum = chirp_tone(dd, f[0], f[-1], rng, (1, 0.5, 0.25)) * np.sin(np.linspace(0, np.pi, len(t2))) ** 2 * 0.045
        p0 = rng.uniform(-1, 1); p = np.linspace(p0, -p0, len(t2))
        a = (p + 1) * np.pi / 4
        add_at(out, np.stack([np.cos(a) * hum, np.sin(a) * hum]), rng.integers(0, n))
    out += np.stack([lp(pink(d, rng), 300), lp(pink(d, rng), 300)]) * 0.02
    return rms_norm(loop_xf(out, XF), -28)

def night_insects(rng):
    d = 32 + XF; n = n_(d); out = np.zeros((2, n)); tt = t_(d)
    # field crickets: 3-4 syllable chirps, each individual with its own pitch / rhythm / place
    for i in range(12):
        f = rng.uniform(3600, 5200); syl = rng.uniform(0.012, 0.02); per = syl * rng.uniform(1.7, 2.3); nsy = rng.integers(2, 5)
        every = rng.uniform(0.35, 1.1); level = rng.uniform(0.25, 1.0) * (0.35 if i > 7 else 1.0)
        chirp = np.zeros(n_(per * nsy + 0.01))
        for s in range(nsy):
            a = int(s * per * SR); ln = n_(syl); w = np.hanning(ln)
            chirp[a:a + ln] += np.sin(2 * np.pi * f * np.arange(ln) / SR) * w
        x = np.zeros(n); p = rng.uniform(0, every)
        while p < d:
            add_at(x, chirp * rng.uniform(0.7, 1.0), int(p * SR)); p += every * rng.uniform(0.9, 1.1)
        x *= level * (0.6 + 0.4 * slow_env(d, rng, 0.1, 0, 1))
        if i > 7: x = lp(x, 3500)
        out += pan(x, rng.uniform(-1, 1))
    # tree crickets: a soft continuous trill far away
    for _ in range(3):
        f = rng.uniform(2200, 3000); tr = rng.uniform(40, 60)
        x = np.sin(2 * np.pi * f * tt) * (0.5 + 0.5 * np.sin(2 * np.pi * tr * tt)) ** 2 * 0.035 * slow_env(d, rng, 0.12, 0.0, 1) ** 2
        out += pan(lp(x, 3000), rng.uniform(-1, 1))
    out += np.stack([lp(pink(d, rng), 250), lp(pink(d, rng), 250)]) * 0.05
    return rms_norm(loop_xf(out, XF), -27)

def wind(rng, strong=False):
    d = (24 if strong else 28) + XF; n = n_(d); out = np.zeros((2, n))
    gust = slow_env(d, rng, 0.35 if strong else 0.16, 0.2 if strong else 0.1, 1.0) ** (1.2 if strong else 1.6)
    for c in range(2):
        g = np.roll(gust, int(c * 0.35 * SR))
        body = bp(pink(d, rng), 60, 900 if strong else 700) * (0.3 + 0.7 * g)
        hiss = bp(pink(d, rng), 1500, 7000) * g * (0.35 if strong else 0.22)
        x = body + hiss
        if strong:
            # howl through rock and trunks: resonances that move with the gusts
            src = pink(d, rng)
            for fc in (rng.uniform(280, 380), rng.uniform(520, 700)):
                fm = fc * (0.85 + 0.3 * g)
                y = np.zeros(n); blk = n_(0.05)
                for s in range(0, n, blk):
                    seg = src[s:s + blk]; y[s:s + len(seg)] = reson(seg, fm[s], 18)
                x += y * g * 0.5
            x += lp(brown(d, rng), 90) * 0.5 * g
        out[c] = x
    return rms_norm(loop_xf(out, XF), -22 if strong else -27)

def rain(rng, heavy=False):
    d = 20 + XF; n = n_(d); out = np.zeros((2, n))
    for c in range(2):
        hiss = bp(pink(d, rng), 500 if heavy else 900, 11000) * (0.5 if heavy else 0.18)
        low = lp(pink(d, rng), 220) * (0.35 if heavy else 0.08)
        x = hiss + low
        grains(x, rng, 3200 if heavy else 700, drop, 0.35 if heavy else 0.45)
        # heavier drops on big leaves and on the ground
        grains(x, rng, 300 if heavy else 60, lambda r: lp(drop(r, r.uniform(500, 1500), r.uniform(0.02, 0.05)), 3000), 0.5)
        out[c] = x
    if heavy: out *= 0.8 + 0.2 * slow_env(d, rng, 0.2, 0.3, 1.0)
    return rms_norm(loop_xf(out, XF), -21 if heavy else -27)

def ocean_surf(rng):
    d = 28 + XF; n = n_(d); out = np.zeros((2, n))
    bed = np.stack([lp(pink(d, rng), 400), lp(pink(d, rng), 400)]) * 0.25 + np.stack([bp(pink(d, rng), 1800, 9000), bp(pink(d, rng), 1800, 9000)]) * 0.025
    out += bed
    p = 0.0
    while p < d:
        per = rng.uniform(6.5, 10.5); crash_d = rng.uniform(3.5, 6.0)
        tt = t_(crash_d); k = tt / crash_d
        env_ = np.where(k < 0.25, (k / 0.25) ** 2, np.exp(-(k - 0.25) * 3.2))
        body = lp(pink(crash_d, rng), rng.uniform(900, 1500)) * env_
        foam = bp(pink(crash_d, rng), 1500, 9000) * np.clip(env_ - 0.1, 0, 1) ** 1.5 * 0.5
        wash = np.zeros(len(tt)); grains(wash, rng, 250, lambda r: hp(r.standard_normal(n_(0.003)) * np.hanning(n_(0.003)), 2500, 1), 0.25)
        wash *= np.clip((k - 0.4) / 0.6, 0, 1) * np.exp(-(k - 0.4) * 2)
        w = (body + foam + wash) * np.clip((1 - k) / 0.3, 0, 1) ** 2          # every part dies out before the buffer ends
        add_at(out, pan(w, rng.uniform(-0.5, 0.5)), int(p * SR))
        p += per
    return rms_norm(loop_xf(out, XF), -23)

def cave_room(rng):
    d = 20 + XF; n = n_(d); out = np.zeros((2, n))
    for c in range(2): out[c] = lp(brown(d, rng), 140) * 0.3 + bp(pink(d, rng), 200, 600) * 0.02
    # water drips with the cave's echo
    for _ in range(int(d * 0.9)):
        f = rng.uniform(900, 2600); dd = 0.25; tt = t_(dd)
        pl = np.sin(2 * np.pi * f * (1 + 0.3 * tt / dd) * tt) * np.exp(-tt / 0.03) + bp(rng.standard_normal(len(tt)), 2000, 6000) * np.exp(-tt / 0.004) * 0.3
        e = np.zeros(n_(1.2)); e[:len(pl)] += pl
        for k, dl in enumerate((0.07, 0.13, 0.21, 0.34, 0.52)):
            s = n_(dl); e[s:s + len(pl)] += lp(pl, 3000 - k * 400) * 0.45 ** (k + 1)
        add_at(out, pan(e * rng.uniform(0.2, 0.6), rng.uniform(-0.9, 0.9)), rng.integers(0, n))
    return rms_norm(loop_xf(out, XF), -31)


# ---------------------------------------------------------------- 3D emitters (mono loops)
def river(rng, stream=False):
    d = (20 if stream else 24) + XF; n = n_(d)
    rush = bp(pink(d, rng), 250 if not stream else 500, 3500 if not stream else 5000) * (0.5 if not stream else 0.2)
    am = lp(np.abs(white(d, rng)), 9) * 3
    x = rush * (0.45 + 0.55 * np.clip(am, 0, 2))
    grains(x, rng, 220 if not stream else 260, lambda r: bubble(r, r.uniform(300, 1300) if not stream else r.uniform(700, 2600)), 0.45 if not stream else 0.5)
    x += lp(brown(d, rng), 150) * (0.35 if not stream else 0.05)
    # small splashes where the water breaks over rocks
    grains(x, rng, 6, lambda r: bp(r.standard_normal(n_(0.08)), 800, 6000) * np.exp(-np.arange(n_(0.08)) / SR / 0.02), 0.4)
    return rms_norm(loop_xf(x, XF), -20 if not stream else -23)

def waterfall(rng):
    d = 20 + XF
    roar = pink(d, rng) * 0.6 + lp(brown(d, rng), 110) * 1.1 + bp(pink(d, rng), 2000, 9000) * 0.35
    roar *= 0.85 + 0.15 * slow_env(d, rng, 0.5, 0, 1)
    grains(roar, rng, 60, lambda r: bubble(r, r.uniform(250, 900), r.uniform(0.02, 0.06)), 0.3)
    return rms_norm(loop_xf(roar, XF), -16)

def frog_croak(rng, f0=None, dur=None):
    """low pulsed croak: a harmonic buzz gated 20-40 times a second, one resonance"""
    dur = dur or rng.uniform(0.18, 0.5); tt = t_(dur); f0 = f0 or rng.uniform(140, 300)
    gate = np.clip(np.sin(2 * np.pi * rng.uniform(20, 40) * tt), 0, 1) ** 1.5         # soft pulses, no clicks
    src = signal.sawtooth(2 * np.pi * f0 * tt, 0.6) * gate
    y = reson(src, f0 * rng.uniform(2.5, 4), 6) + reson(src, rng.uniform(900, 1400), 5) * 0.4
    return y * adsr(len(tt), 0.01, dur * 0.3)
def frog_peep(rng):
    dur = rng.uniform(0.05, 0.12); f = rng.uniform(2200, 3300)
    return chirp_tone(dur, f * 0.9, f * 1.05, rng, (1, 0.2)) * np.sin(np.linspace(0, np.pi, n_(dur))) ** 1.5
def wetland(rng, night=False):
    d = 24 + XF; n = n_(d); x = np.zeros(n); tt = t_(d)
    lap = lp(pink(d, rng), 600) * (0.25 + 0.2 * np.sin(2 * np.pi * tt / rng.uniform(2.5, 4)) ** 2) * 0.4
    x += lap + bp(pink(d, rng), 1500, 6000) * 0.03 * slow_env(d, rng, 0.2, 0.2, 1)       # reeds in a breeze
    grains(x, rng, 1.2, lambda r: bubble(r, r.uniform(250, 600), r.uniform(0.03, 0.06)), 0.4)  # a plop now and then
    if not night:
        # a few frogs by day, dragonfly-like wing whirr passing, insect buzz
        for _ in range(4): add_at(x, frog_croak(rng) * 0.35, rng.integers(0, n))
        for _ in range(3):
            dd = rng.uniform(0.8, 1.6); wh = bp(white(dd, rng), 80, 400) * (0.5 + 0.5 * np.sin(2 * np.pi * 30 * t_(dd))) * np.sin(np.linspace(0, np.pi, n_(dd))) ** 2 * 0.3
            add_at(x, wh, rng.integers(0, n))
        x += bp(white(d, rng), 4000, 6000) * 0.02 * slow_env(d, rng, 0.2, 0, 1)
        return rms_norm(loop_xf(x, XF), -25)
    # night chorus: croakers in rhythm, peepers, a trilling toad
    for i in range(6):
        f0 = rng.uniform(130, 280); every = rng.uniform(0.8, 2.2); p = rng.uniform(0, every); lvl = rng.uniform(0.3, 0.9)
        dur = rng.uniform(0.2, 0.45)
        while p < d: add_at(x, frog_croak(rng, f0 * rng.uniform(0.97, 1.03), dur) * lvl, int(p * SR)); p += every * rng.uniform(0.85, 1.2)
    for i in range(5):
        every = rng.uniform(0.4, 1.0); p = rng.uniform(0, every); lvl = rng.uniform(0.15, 0.5)
        while p < d: add_at(x, frog_peep(rng) * lvl, int(p * SR)); p += every * rng.uniform(0.8, 1.25)
    for _ in range(3):
        dd = rng.uniform(2.0, 4.0); t2 = t_(dd); f = rng.uniform(1000, 1500)
        tr = np.sin(2 * np.pi * f * t2) * (0.5 + 0.5 * np.sin(2 * np.pi * rng.uniform(25, 40) * t2)) ** 3 * adsr(len(t2), 0.1, 0.4) * 0.25
        add_at(x, tr, rng.integers(0, n))
    return rms_norm(loop_xf(x, XF), -22)

def pond(rng):
    d = 20 + XF; tt = t_(d)
    x = lp(pink(d, rng), 500) * (0.3 + 0.25 * np.sin(2 * np.pi * tt / 3.1) ** 2) * 0.35
    grains(x, rng, 3, lambda r: bubble(r, r.uniform(300, 900), r.uniform(0.02, 0.05)), 0.35)
    grains(x, rng, 2, lambda r: lp(drop(r, r.uniform(600, 1400), 0.04), 2500), 0.3)
    return rms_norm(loop_xf(x, XF), -28)

def lava(rng):
    d = 20 + XF; n = n_(d)
    rumble = lp(brown(d, rng), 80) * 0.9 + lp(pink(d, rng), 300) * 0.2
    hiss = hp(pink(d, rng), 3000) * 0.05 * slow_env(d, rng, 0.3, 0.2, 1)
    x = rumble + hiss
    # thick bubbles bursting and rock cracking in the heat
    grains(x, rng, 4, lambda r: lp(bubble(r, r.uniform(60, 180), r.uniform(0.08, 0.2)), 800), 1.2)
    grains(x, rng, 25, lambda r: hp(r.standard_normal(n_(0.004)) * np.exp(-np.arange(n_(0.004)) / SR / 0.0008), 1500, 1), 0.4)
    for _ in range(3):   # gas puffing out of a crack
        dd = rng.uniform(0.6, 1.4); pf = bp(white(dd, rng), 400, 3000) * np.sin(np.linspace(0, np.pi, n_(dd))) ** 3 * 0.25
        add_at(x, pf, rng.integers(0, n))
    return rms_norm(loop_xf(x, XF), -22)


# ---------------------------------------------------------------- one-shots
def bird(rng, kind):
    """simple calls of early birds: whistles, croaks, rattles, trills (no songbird melodies)"""
    if kind == 0:     # two-note whistle, down
        a = chirp_tone(0.22, 2900, 2500, rng, (1, 0.12)) * np.sin(np.linspace(0, np.pi, n_(0.22))) ** 1.3
        b = chirp_tone(0.3, 2300, 1700, rng, (1, 0.12), 0.01, 25) * np.sin(np.linspace(0, np.pi, n_(0.3))) ** 1.3
        x = np.concatenate([a, np.zeros(n_(0.07)), b])
    elif kind == 1:   # harsh croak "kraak"
        dd = 0.35; tt = t_(dd); f0 = rng.uniform(420, 560) * (1 - 0.15 * tt / dd)
        src = signal.sawtooth(2 * np.pi * np.cumsum(f0) / SR, 0.2) + white(dd, rng) * 0.3
        x = (reson(src, 1300, 4) + reson(src, 2400, 6) * 0.6) * adsr(len(tt), 0.01, 0.12)
        x = np.concatenate([x, np.zeros(n_(0.12)), x[: n_(0.25)] * 0.7])
    elif kind == 2:   # dry rattle
        x = np.zeros(n_(0.6))
        for i in range(14):
            c = bp(rng.standard_normal(n_(0.012)), 1800, 5200) * np.exp(-np.arange(n_(0.012)) / SR / 0.003)
            s = n_(i * 0.036); x[s:s + len(c)] += c * (1 - i / 16)
    elif kind == 3:   # low soft hoot pair (a ground bird)
        seg = lambda f: chirp_tone(0.28, f, f * 0.92, rng, (1, 0.25, 0.08)) * np.sin(np.linspace(0, np.pi, n_(0.28))) ** 2
        x = np.concatenate([seg(rng.uniform(480, 560)), np.zeros(n_(0.18)), seg(rng.uniform(430, 500))])
    elif kind == 4:   # fast trill
        dd = 0.7; tt = t_(dd); f = rng.uniform(3000, 3600)
        x = np.sin(2 * np.pi * f * tt + 3 * np.sin(2 * np.pi * 11 * tt)) * (0.5 + 0.5 * np.sin(2 * np.pi * 22 * tt)) ** 2 * adsr(len(tt), 0.05, 0.2)
    elif kind == 5:   # rising whoop
        dd = 0.45; x = chirp_tone(dd, 900, 1900, rng, (1, 0.35, 0.1), curve=1.6) * adsr(n_(dd), 0.04, 0.12)
    elif kind == 6:   # chatter: short chips
        x = np.zeros(n_(0.8))
        for i in range(int(rng.integers(4, 7))):
            f = rng.uniform(2600, 3800); c = chirp_tone(0.05, f, f * 0.8, rng) * np.hanning(n_(0.05))
            s = n_(i * rng.uniform(0.09, 0.13)); x[s:s + len(c)] += c
    else:             # thin descending whistle, long
        dd = 0.8; x = chirp_tone(dd, 3600, 2100, rng, (1, 0.1), 0.02, 6, 0.7) * adsr(n_(dd), 0.03, 0.4)
    x = verb(fade(x), rng, 0.8, 0.16, 0.25, 400, 7000)
    return rms_norm(fade(x, 0.002, 0.2), -22, -3)

def distant_call(rng, kind):
    """dinosaur calls far away: low resonant honks, closed-mouth booms, bellows. Low-passed, long outdoor tail."""
    if kind in (0, 3):     # hadrosaur-like resonant honk (kind 3: two animals answering)
        def honk(f0, dd):
            tt = t_(dd); f = f0 * (1 + 0.04 * np.sin(2 * np.pi * 4.5 * tt)) * (1 + 0.06 * np.sin(np.pi * tt / dd))
            src = signal.sawtooth(2 * np.pi * np.cumsum(f) / SR, 0.5)
            y = reson(src, f0 * 2, 8) + reson(src, f0 * 3.1, 9) * 0.7 + reson(src, 480, 5) * 0.4
            return y * adsr(len(tt), 0.25, dd * 0.45)
        x = honk(rng.uniform(70, 95), rng.uniform(1.6, 2.4))
        if kind == 3: x = np.concatenate([x, np.zeros(n_(0.7)), honk(rng.uniform(95, 120), rng.uniform(1.2, 1.8)) * 0.8])
    elif kind == 1:        # closed-mouth boom (low, felt more than heard)
        dd = 2.2; tt = t_(dd); f = rng.uniform(34, 46) * (1 - 0.08 * tt / dd)
        x = (np.sin(2 * np.pi * np.cumsum(f) / SR) + 0.5 * np.sin(4 * np.pi * np.cumsum(f) / SR) + 0.2 * np.sin(6 * np.pi * np.cumsum(f) / SR)) * adsr(len(tt), 0.3, 1.0)
        x = np.concatenate([x, np.zeros(n_(0.5)), x * 0.8])
    elif kind == 2:        # bellow / roar far away
        dd = 2.0; tt = t_(dd); f = rng.uniform(85, 110) * (1 - 0.3 * (tt / dd) ** 1.5)
        src = signal.sawtooth(2 * np.pi * np.cumsum(f) / SR, 0.4) * (1 + 0.4 * bp(white(dd, rng), 20, 80))
        x = (reson(src, 350, 3) + reson(src, 800, 4) * 0.6 + bp(white(dd, rng), 200, 1200) * 0.3) * adsr(len(tt), 0.15, 0.9)
    elif kind == 4:        # short low bark of a predator
        dd = 0.6; tt = t_(dd); f = rng.uniform(120, 150) * (1 - 0.35 * tt / dd)
        src = signal.sawtooth(2 * np.pi * np.cumsum(f) / SR, 0.3) + white(dd, rng) * 0.4
        x = (reson(src, 500, 3) + reson(src, 1100, 5) * 0.5) * adsr(len(tt), 0.02, 0.35)
        x = np.concatenate([x, np.zeros(n_(0.35)), x * 0.75])
    else:                  # long low moan of a very large animal
        dd = 3.2; tt = t_(dd); f = rng.uniform(48, 60) * (1 + 0.1 * np.sin(np.pi * tt / dd))
        src = signal.sawtooth(2 * np.pi * np.cumsum(f) / SR, 0.5)
        x = (reson(src, f[0] * 3, 6) + reson(src, 260, 4) * 0.6) * adsr(len(tt), 0.5, 1.4)
    x = lp(x, 1400, 3)                                   # air absorbs the highs over distance
    x = verb(fade(x, 0.05, 0.3), rng, 3.0, 0.9, 0.55, 80, 1500)
    return rms_norm(fade(x, 0.02, 0.6), -24, -4)

def critter(rng, kind):
    if kind == 0:     # rustle in the litter
        dd = rng.uniform(0.5, 0.9); x = np.zeros(n_(dd))
        grains(x, rng, 90, lambda r: bp(r.standard_normal(n_(0.006)), 1500, 7000) * np.hanning(n_(0.006)), 0.6)
        x *= np.sin(np.linspace(0, np.pi, len(x)))
    elif kind == 1:   # tiny chitter of a small mammal
        x = np.zeros(n_(0.45))
        for i in range(6):
            f = rng.uniform(4200, 6500); c = chirp_tone(0.025, f, f * 1.2, rng) * np.hanning(n_(0.025)); s = n_(i * 0.055); x[s:s + len(c)] += c
    elif kind == 2:   # a single frog peep far off
        x = np.concatenate([frog_peep(rng), np.zeros(n_(0.25)), frog_peep(rng) * 0.8])
    else:             # soft twig snap
        dd = 0.12; x = bp(white(dd, rng), 1200, 5000) * np.exp(-t_(dd) / 0.01) + bp(white(dd, rng), 300, 1200) * np.exp(-t_(dd) / 0.03) * 0.4
    x = verb(fade(x), rng, 0.8, 0.2, 0.2, 400, 6000)
    return rms_norm(fade(x, 0.002, 0.1), -30, -6)

def thunder(rng, near):
    if near:
        dd = 7.5; tt = t_(dd)
        crack = bp(white(0.5, rng), 800, 9000) * np.exp(-t_(0.5) / 0.06) * 1.2
        tear = np.zeros(n_(1.2)); grains(tear, rng, 600, lambda r: hp(r.standard_normal(n_(0.004)) * np.hanning(n_(0.004)), 1500, 1), 0.7)
        tear *= np.exp(-t_(1.2) / 0.3)
        boom = lp(brown(dd, rng), 140, 3) * np.exp(-tt / 1.4) * (1 - np.exp(-tt / 0.03)) * 2.5
        roll = lp(pink(dd, rng), 400) * slow_env(dd, rng, 2.5, 0.2, 1) * np.exp(-tt / 2.2) * 0.7
        x = boom + roll; x[:len(crack)] += crack; x[:len(tear)] += tear
    else:
        dd = rng.uniform(6.5, 9.0); tt = t_(dd)
        env_ = slow_env(dd, rng, 2.0, 0.1, 1.0) * np.clip(tt / 0.8, 0, 1) * np.exp(-tt / (dd * 0.4))
        x = (lp(brown(dd, rng), 110, 3) * 1.6 + lp(pink(dd, rng), 280) * 0.6) * env_
    x = verb(fade(x, 0.001 if near else 0.3, 1.2), rng, 3.5, 1.2, 0.35, 40, 3000)
    return rms_norm(fade(x, 0.001, 1.5), -18 if near else -22, -1)


# ---------------------------------------------------------------- table
LOOPS = {
    "ForestDay": forest_day, "InsectsDay": insects_day, "NightInsects": night_insects,
    "WindSoft": lambda r: wind(r, False), "WindStrong": lambda r: wind(r, True),
    "RainLight": lambda r: rain(r, False), "RainHeavy": lambda r: rain(r, True),
    "OceanSurf": ocean_surf, "CaveRoom": cave_room,
    "River": lambda r: river(r, False), "Stream": lambda r: river(r, True), "Waterfall": waterfall,
    "WetlandDay": lambda r: wetland(r, False), "WetlandNight": lambda r: wetland(r, True), "Pond": pond, "Lava": lava,
}
SHOTS = {"Bird": (8, bird), "DistantCall": (6, distant_call), "Critter": (4, critter),
         "ThunderNear": (2, lambda r, k: thunder(r, True)), "ThunderFar": (3, lambda r, k: thunder(r, False))}

def main(out, only=None):
    import zlib
    for i, (name, fn) in enumerate(LOOPS.items()):
        if only and name not in only: continue
        rng = np.random.default_rng(zlib.crc32(name.encode()) & 0xffff)
        save(os.path.join(out, f"AMB_{name}_Loop.wav"), fn(rng))
    for name, (count, fn) in SHOTS.items():
        if only and name not in only: continue
        for k in range(count):
            rng = np.random.default_rng((zlib.crc32(name.encode()) + 97 * k) & 0xffff)
            save(os.path.join(out, "OneShots", f"AMB_{name}_{k + 1}.wav"), fn(rng, k))

if __name__ == "__main__":
    if len(sys.argv) < 2: print(__doc__); sys.exit(1)
    main(sys.argv[1], set(sys.argv[2:]) or None)
