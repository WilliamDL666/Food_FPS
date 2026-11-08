# Synthesises the cartoon food sound set for 霓城封锁 (original, procedurally generated — no third-party audio).
# Run:  python Tools/synth_food_sfx.py <out_dir>      → writes food_<name>_00N.wav (3 variants each)
import sys, os, wave
import numpy as np

SR = 44100
OUT = sys.argv[1] if len(sys.argv) > 1 else 'food_sfx'
os.makedirs(OUT, exist_ok=True)

def t(sec): return np.arange(int(SR * sec)) / SR
def env(n, attack=.004, decay=.1, total=None):
    x = np.arange(n) / SR
    a = np.clip(x / max(attack, 1e-4), 0, 1)
    return a * np.exp(-np.maximum(x - attack, 0) / decay)
def sweep(f0, f1, sec, curve=3.0):
    x = t(sec); k = (x / sec) ** (1 / curve)
    f = f0 + (f1 - f0) * k
    return np.sin(2 * np.pi * np.cumsum(f) / SR)
def noise(sec, rng): return rng.uniform(-1, 1, int(SR * sec))
def lowpass(x, cut):
    a = np.exp(-2 * np.pi * cut / SR); y = np.zeros_like(x); s = 0.0
    for i, v in enumerate(x): s = (1 - a) * v + a * s; y[i] = s
    return y
def highpass(x, cut): return x - lowpass(x, cut)
def bandpass(x, lo, hi): return highpass(lowpass(x, hi), lo)
def mix(*parts):
    n = max(len(p) for p in parts); y = np.zeros(n)
    for p in parts: y[:len(p)] += p
    return y
def pad(x, sec): return np.concatenate([np.zeros(int(SR * sec)), x])
def save(name, i, y):
    y = y / (np.max(np.abs(y)) + 1e-9) * .89
    fade = min(len(y), 400); y[-fade:] *= np.linspace(1, 0, fade)
    with wave.open(os.path.join(OUT, f'food_{name}_{i:03d}.wav'), 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((y * 32767).astype(np.int16).tobytes())

def ketchup(r, p):   # squeeze-bottle squirt
    n = bandpass(noise(.16, r), 300 * p, 2400 * p) * env(int(SR * .16), .006, .05)
    thump = sweep(140 * p, 60 * p, .1) * env(int(SR * .1), .002, .03)
    blurp = sweep(260 * p, 520 * p, .06) * env(int(SR * .06), .002, .02) * .5
    return mix(n * .8, thump, pad(blurp, .03))
def boba(r, p):      # plop
    s = sweep(620 * p, 150 * p, .09, 2) * env(int(SR * .09), .001, .03)
    click = noise(.004, r) * .4
    return mix(click, s)
def popcorn(r, p):   # pop
    click = noise(.003, r)
    res = np.sin(2 * np.pi * 1150 * p * t(.06)) * env(int(SR * .06), .0005, .012)
    body = sweep(420 * p, 180 * p, .05) * env(int(SR * .05), .001, .015) * .7
    return mix(click, res * .8, body)
def sushi(r, p):     # wooden knock
    x = t(.12)
    k = (np.sin(2 * np.pi * 230 * p * x) + .6 * np.sin(2 * np.pi * 610 * p * x) + .3 * np.sin(2 * np.pi * 1490 * p * x)) * env(len(x), .001, .022)
    return mix(lowpass(noise(.01, r), 3000) * .6, k)
def haw(r, p):       # rubber-band twang + thwip
    x = t(.3)
    tw = np.sin(2 * np.pi * (150 * p + 18 * np.sin(2 * np.pi * 9 * x)) * x) * env(len(x), .002, .09)
    thwip = bandpass(noise(.08, r), 1500, 7000) * env(int(SR * .08), .002, .02)
    pop = sweep(900 * p, 300 * p, .04) * env(int(SR * .04), .001, .012)
    return mix(thwip * .7, pop * .8, tw * .6)
def dumpling(r, p):  # whomp + boing tail
    whomp = lowpass(noise(.3, r), 700) * env(int(SR * .3), .004, .07)
    low = sweep(95 * p, 45 * p, .25) * env(int(SR * .25), .002, .08)
    x = t(.35); boing = np.sin(2 * np.pi * np.cumsum(280 * p + 200 * p * (x / .35) + 25 * np.sin(2 * np.pi * 14 * x)) / SR) * env(len(x), .01, .1) * .35
    return mix(whomp * 1.2, low, pad(boing, .05))
def cream(r, p):     # pshh + soft plop
    air = highpass(noise(.2, r), 2500) * env(int(SR * .2), .01, .06)
    plop = sweep(380 * p, 160 * p, .07) * env(int(SR * .07), .001, .025) * .6
    return mix(air * .7, pad(plop, .02))
def splat(r, p):     # wet splat on a wall
    n = lowpass(noise(.18, r), 1800 * p) * env(int(SR * .18), .002, .04)
    chop = (r.uniform(0, 1, len(n)) > .35) * .6 + .4
    thump = sweep(110 * p, 55 * p, .08) * env(int(SR * .08), .001, .025)
    return mix(n * chop, thump * .7)
def squish(r, p):    # hit on a player
    x = t(.14)
    s = np.sin(2 * np.pi * np.cumsum(200 * p - 110 * p * (x / .14) + 30 * np.sin(2 * np.pi * 30 * x)) / SR) * env(len(x), .002, .04)
    return mix(s, lowpass(noise(.08, r), 1200) * env(int(SR * .08), .002, .02) * .6)
def confetti(r, p):  # party popper + crackles + chime
    pop = mix(noise(.004, r), sweep(800 * p, 200 * p, .05) * env(int(SR * .05), .001, .015))
    cr = np.zeros(int(SR * .5))
    for _ in range(26):
        i = int(r.uniform(.02, .45) * SR); L = int(SR * .006)
        cr[i:i + L] += bandpass(noise(.006, r), 2000, 9000) * r.uniform(.2, .6)
    x = t(.5); chime = sum(np.sin(2 * np.pi * f * p * x) * env(len(x), .005, .18) for f in (1046, 1318, 1568)) * .25
    return mix(pop, cr, pad(chime, .04))
def bonk(r, p):      # frying-pan bonk
    x = t(.7)
    parts = [(523, 1, .22), (1309, .6, .15), (2153, .45, .1), (3410, .3, .06)]
    ring = sum(a * np.sin(2 * np.pi * f * p * x) * env(len(x), .001, d) for f, a, d in parts)
    return mix(lowpass(noise(.01, r), 4000), ring)
def fizz(r, p):      # shaken soda burst
    n = highpass(noise(1.0, r), 3000) * env(int(SR * 1.0), .01, .35)
    b = np.zeros(int(SR * 1.0))
    for _ in range(40):
        i = int(r.uniform(0, .9) * SR); f = r.uniform(700, 1800) * p; L = int(SR * .02)
        b[i:i + L] += np.sin(2 * np.pi * np.linspace(f, f * 1.6, L) * np.arange(L) / SR) * np.exp(-np.arange(L) / (SR * .006)) * .5
    boom = sweep(120, 40, .35) * env(int(SR * .35), .002, .1)
    return mix(boom, n * .6, b)
def boing(r, p):     # jelly bounce
    x = t(.4)
    return np.sin(2 * np.pi * np.cumsum(180 * p + 380 * p * (x / .4) + 40 * np.sin(2 * np.pi * 16 * x)) / SR) * env(len(x), .005, .14)

SOUNDS = {'ketchup': ketchup, 'boba': boba, 'popcorn': popcorn, 'sushi': sushi, 'haw': haw, 'dumpling': dumpling, 'cream': cream,
          'splat': splat, 'squish': squish, 'confetti': confetti, 'bonk': bonk, 'fizz': fizz, 'boing': boing}
for name, fn in SOUNDS.items():
    for i in range(3):
        rng = np.random.default_rng(hash((name, i)) & 0xffff)
        save(name, i, fn(rng, [1.0, 1.07, .94][i]))
print('SFX_DONE', len(SOUNDS) * 3)
