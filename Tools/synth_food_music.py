# Original procedural background music for 霓城封锁 (food FPS): an upbeat "food street" loop + a hot-pot bubbling loop.
# Run:  python Tools/synth_food_music.py <out_dir>
import sys, os, wave
import numpy as np

SR = 22050
OUT = sys.argv[1] if len(sys.argv) > 1 else 'music'
os.makedirs(OUT, exist_ok=True)
BPM = 118; BEAT = 60 / BPM; BARS = 16; N = int(SR * BEAT * 4 * BARS)
rng = np.random.default_rng(7)
def note(m): return 440 * 2 ** ((m - 69) / 12)
def env(n, a=.005, d=.25, s=.0):
    t = np.arange(n) / SR; e = np.minimum(t / a, 1) * (s + (1 - s) * np.exp(-t / d)); return e
def tone(f, dur, kind='tri', a=.005, d=.25, s=0.):
    n = int(SR * dur); t = np.arange(n) / SR
    if kind == 'tri': w = 2 * np.abs(2 * ((t * f) % 1) - 1) - 1
    elif kind == 'sq': w = np.sign(np.sin(2 * np.pi * f * t)) * .6
    elif kind == 'bell': w = np.sin(2 * np.pi * f * t) + .4 * np.sin(2 * np.pi * f * 2.76 * t) * np.exp(-t * 6)
    else: w = np.sin(2 * np.pi * f * t)
    return w * env(n, a, d, s)
def add(buf, x, at):
    i = int(at * SR); j = min(len(buf), i + len(x)); buf[i:j] += x[:j - i]

music = np.zeros(N)
chords = [(60, 64, 67), (57, 60, 64), (53, 57, 60), (55, 59, 62)]          # C  Am  F  G
scale = [72, 74, 76, 79, 81, 84]                                             # C major pentatonic
for bar in range(BARS):
    ch = chords[bar % 4]; t0 = bar * 4 * BEAT
    for b in range(4):                                                        # bouncy bass: root on beats, fifth on offbeats
        add(music, tone(note(ch[0] - 24), BEAT * .45, 'tri', d=.12) * .5, t0 + b * BEAT)
        add(music, tone(note(ch[0] - 17), BEAT * .3, 'tri', d=.08) * .3, t0 + b * BEAT + BEAT / 2)
    for b in range(8):                                                        # plucky chord stabs on the off-beats
        if b % 2 == 1:
            for m in ch: add(music, tone(note(m), BEAT * .3, 'sq', d=.06) * .07, t0 + b * BEAT / 2)
    if bar >= 2:                                                              # melody: pentatonic hops, bell timbre
        pos = 0.0
        while pos < 4 * BEAT - .01:
            dur = BEAT * (.5 if rng.random() < .7 else 1)
            if rng.random() < .82: add(music, tone(note(rng.choice(scale)), dur * .95, 'bell', d=.18) * .16, t0 + pos)
            pos += dur
    for b in range(4):                                                        # soft kick + shaker
        k = np.sin(2 * np.pi * np.cumsum(np.linspace(140, 45, int(SR * .12))) / SR) * env(int(SR * .12), .001, .05)
        add(music, k * .5, t0 + b * BEAT)
        for h in range(2):
            hn = rng.uniform(-1, 1, int(SR * .03)); hn = hn - np.convolve(hn, np.ones(8) / 8, 'same')
            add(music, hn * env(len(hn), .001, .01) * .12, t0 + b * BEAT + h * BEAT / 2 + BEAT / 4)
music /= np.max(np.abs(music)) + 1e-9
fade = int(SR * .02); music[:fade] *= np.linspace(0, 1, fade); music[-fade:] *= np.linspace(1, 0, fade)

def save(name, x):
    with wave.open(os.path.join(OUT, name), 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes((x * .85 * 32767).astype(np.int16).tobytes())
save('food_street.wav', music)

# hot-pot bubbling loop (6 s)
L = int(SR * 6); bub = np.zeros(L)
for _ in range(140):
    f = rng.uniform(250, 900); d = rng.uniform(.02, .06); n = int(SR * d); t = np.arange(n) / SR
    s = np.sin(2 * np.pi * (f + f * 1.5 * t / d) * t) * np.exp(-t / (d * .4))
    i = int(rng.uniform(0, L - n)); bub[i:i + n] += s * rng.uniform(.2, .6)
noise = rng.uniform(-1, 1, L); low = np.convolve(noise, np.ones(40) / 40, 'same') * .3
bub = bub + low; bub /= np.max(np.abs(bub)) + 1e-9
save('hotpot_bubble.wav', bub)
print('MUSIC_DONE', len(music) / SR, 's')
