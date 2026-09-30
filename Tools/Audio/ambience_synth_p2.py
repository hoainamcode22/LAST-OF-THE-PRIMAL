"""PRIMAL FRONTIER ambience synthesizer, Phase 2 additions (WORLD agent). Original sounds only.

usage: python3 ambience_synth_p2.py <Assets/_Project/Audio/Ambience dir>
  writes AMB_Flies_Loop.wav: a mono 3D loop of carrion flies buzzing around a carcass (Bone Valley). Seamless loop,
  16-bit 44.1 kHz. Uses the helpers of ambience_synth.py (same folder). Nothing else in the project had a fly sound:
  every other Phase 2 zone sound reuses existing clips.
"""
import os, sys, zlib
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ambience_synth import SR, n_, t_, bp, lp, hp, slow_env, loop_xf, rms_norm, save, XF

def fly(d, rng):
    """one fly: a buzzing wing tone (~170-260 Hz, rich harmonics), pitch and loudness wander as it circles and lands"""
    tt = t_(d); f0 = rng.uniform(170, 260)
    wob = slow_env(d, rng, rate=1.6, lo=-1, hi=1)                   # circling: pitch up when it comes closer
    f = f0 * (1 + 0.07 * wob + 0.012 * np.sin(2 * np.pi * rng.uniform(5, 9) * tt))
    ph = 2 * np.pi * np.cumsum(f) / SR
    tone = sum((0.9 ** k) * np.sin((k + 1) * ph + rng.uniform(0, 6)) for k in range(9))
    tone = bp(tone, 150, 4200)
    near = np.clip(0.55 + 0.45 * wob, 0, 1) ** 1.6                   # louder when near (same wobble)
    # it lands now and then (silence), then takes off again
    rest = slow_env(d, rng, rate=0.35, lo=-0.6, hi=1.0)
    on = np.clip(rest * 3.0, 0, 1)
    return tone * near * on

def flies(rng):
    d = 14 + XF; n = n_(d); out = np.zeros(n)
    for _ in range(5): out += fly(d, rng) * rng.uniform(0.4, 1.0)
    out += bp(rng.standard_normal(n), 2500, 7000) * 0.004           # air
    return rms_norm(loop_xf(out, XF), -24)

if __name__ == "__main__":
    if len(sys.argv) < 2: print(__doc__); sys.exit(1)
    rng = np.random.default_rng(zlib.crc32(b"Flies") & 0xffff)
    save(os.path.join(sys.argv[1], "AMB_Flies_Loop.wav"), flies(rng))
