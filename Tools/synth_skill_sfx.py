# V23：技能 / 大招美食化音效（原创、程序合成，无第三方素材）。做法和 synth_food_sfx.py 一样。
# Run:  python Tools/synth_skill_sfx.py <out_dir>   → writes food_<name>_00N.wav（每个 3 个变体）
# 命名规则：Sfx.Load 用最后一个下划线之前的部分做分组名，所以 food_can_shake_000 → 分组 "food_can_shake"。
import sys, os, wave
import numpy as np

SR = 44100
OUT = sys.argv[1] if len(sys.argv) > 1 else 'skill_sfx'
os.makedirs(OUT, exist_ok=True)

def t(sec): return np.arange(int(SR * sec)) / SR
def env(n, attack=.004, decay=.1):
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
def tone(f, sec, vib=0, vibhz=0):
    x = t(sec); return np.sin(2 * np.pi * np.cumsum(f + vib * np.sin(2 * np.pi * vibhz * x)) / SR)
def save(name, i, y):
    y = y / (np.max(np.abs(y)) + 1e-9) * .89
    fade = min(len(y), 400); y[-fade:] *= np.linspace(1, 0, fade)
    with wave.open(os.path.join(OUT, f'food_{name}_{i:03d}.wav'), 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((y * 32767).astype(np.int16).tobytes())

# ---------------- 技能
def can_shake(r, p):      # 摇罐：两下液体晃动 + 罐壁轻响
    y = np.zeros(int(SR * .18))
    for k, at in enumerate((.0, .08)):
        i = int(at * SR); L = int(SR * .07)
        slosh = lowpass(noise(.07, r), 900 * p) * env(L, .003, .02)
        tink = tone(2400 * p * (1 + .1 * k), .03) * env(int(SR * .03), .0005, .006) * .3
        y[i:i + L] += slosh; y[i:i + len(tink)] += tink
    return y
def can_open(r, p):       # 开罐："啵"的拉环 + 嘶的气
    pop = mix(noise(.004, r), sweep(900 * p, 300 * p, .04) * env(int(SR * .04), .001, .012))
    hiss = highpass(noise(.16, r), 3500) * env(int(SR * .16), .005, .05) * .6
    return mix(pop, pad(hiss, .012))
def burp(r, p):           # 饱嗝：低频共振峰 + 可爱上扬尾音
    x = t(.42)
    f = 95 * p + 20 * np.sin(2 * np.pi * 11 * x)
    base = np.sin(2 * np.pi * np.cumsum(f) / SR)
    form = bandpass(base * (1 + .6 * np.sign(np.sin(2 * np.pi * 27 * x))), 150, 900)
    tail = sweep(220 * p, 420 * p, .12) * env(int(SR * .12), .005, .05) * .3
    return mix(form * env(len(x), .01, .16), pad(tail, .3))
def wafer_crunch(r, p):   # 威化咔嚓：高频脆响
    y = bandpass(noise(.12, r), 1800 * p, 9000) * env(int(SR * .12), .0008, .02)
    for _ in range(5):
        i = int(r.uniform(0, .06) * SR); L = int(SR * .004); y[i:i + L] += noise(.004, r) * .7
    return y
def wafer_shatter(r, p):  # 威化整面碎：多层脆响叠加
    y = np.zeros(int(SR * .5))
    for _ in range(18):
        i = int(r.uniform(0, .35) * SR); L = int(SR * .05)
        y[i:i + L] += bandpass(noise(.05, r), 1500, 8000) * env(L, .0008, .012) * r.uniform(.4, 1)
    thud = sweep(160 * p, 60 * p, .18) * env(int(SR * .18), .002, .06) * .8
    return mix(y, thud)
def jelly_boing(r, p):    # 布丁出场：低通 boing
    x = t(.42)
    b = np.sin(2 * np.pi * np.cumsum(120 * p + 260 * p * (x / .42) + 30 * np.sin(2 * np.pi * 14 * x)) / SR) * env(len(x), .006, .15)
    return lowpass(b, 1400)
def bamboo_thunk(r, p):   # 竹签扎木头：闷响
    x = t(.15)
    k = (np.sin(2 * np.pi * 180 * p * x) + .5 * np.sin(2 * np.pi * 420 * p * x)) * env(len(x), .001, .03)
    return mix(lowpass(noise(.012, r), 2500) * .7, k)
def sneeze(r, p):         # 吸鼻子 → 啊嚏！
    inh = bandpass(noise(.22, r), 800, 4000) * (np.linspace(.2, 1, int(SR * .22)) ** 2) * .5
    ah = tone(330 * p, .12, 20, 8) * env(int(SR * .12), .01, .06) * .5
    choo = mix(bandpass(noise(.2, r), 2500, 9000) * env(int(SR * .2), .002, .05), sweep(500 * p, 180 * p, .12) * env(int(SR * .12), .002, .04) * .5)
    return mix(inh, pad(ah, .24), pad(choo, .34))
def fuse(r, p):           # 蜡烛引信滋—：短、比 fizz 尖
    y = bandpass(noise(.08, r), 4000 * p, 11000) * env(int(SR * .08), .002, .03)
    return mix(y, tone(5200 * p, .05) * env(int(SR * .05), .001, .015) * .25)
def caramel_bubble(r, p): # 焦糖咕嘟（循环素材，0.6 s）
    y = np.zeros(int(SR * .6))
    for _ in range(5):
        i = int(r.uniform(0, .5) * SR); L = int(SR * .08)
        y[i:i + L] += sweep(r.uniform(180, 320) * p, r.uniform(90, 140) * p, .08, 2) * env(L, .002, .025) * r.uniform(.5, 1)
    return mix(y, lowpass(noise(.6, r), 500) * .12)
def marsh_puff(r, p):     # 棉花糖"噗"地膨出
    air = lowpass(noise(.3, r), 1200 * p) * env(int(SR * .3), .01, .08)
    low = sweep(140 * p, 70 * p, .2) * env(int(SR * .2), .004, .07) * .6
    return mix(air, low)
def poprock_loop(r, p):   # 跳跳糖噼啪（循环素材，0.7 s）
    y = np.zeros(int(SR * .7))
    for _ in range(34):
        i = int(r.uniform(0, .66) * SR); L = int(SR * .005)
        y[i:i + L] += bandpass(noise(.005, r), 2500 * p, 10000) * r.uniform(.3, 1)
    return y
# ---------------- 大招
def burger_chomp(r, p):   # 双层汉堡冲撞：大口"啊呜"咬合 + 面包软压
    bite = mix(lowpass(noise(.12, r), 900 * p) * env(int(SR * .12), .003, .04), sweep(260 * p, 90 * p, .14) * env(int(SR * .14), .002, .05))
    chomp = bandpass(noise(.06, r), 1200, 6000) * env(int(SR * .06), .001, .015) * .7
    return mix(bite, pad(chomp, .12))
def rice_thud(r, p):      # 饭团城墙落地：沉闷"咚" + 米粒散落
    thud = sweep(110 * p, 40 * p, .3) * env(int(SR * .3), .003, .1)
    grains = np.zeros(int(SR * .4))
    for _ in range(20):
        i = int(r.uniform(.05, .35) * SR); L = int(SR * .006); grains[i:i + L] += bandpass(noise(.006, r), 3000, 9000) * .3
    return mix(thud, lowpass(noise(.08, r), 600) * .6, grains)
def jelly_dome(r, p):     # 果冻罩：大一号的 Q 弹 + 晶莹"叮"
    x = t(.6)
    b = lowpass(np.sin(2 * np.pi * np.cumsum(80 * p + 200 * p * (x / .6) + 25 * np.sin(2 * np.pi * 9 * x)) / SR) * env(len(x), .01, .25), 1200)
    ding = (tone(1568 * p, .5) + .5 * tone(2349 * p, .5)) * env(int(SR * .5), .003, .2) * .25
    return mix(b, pad(ding, .1))
def sushi_radar(r, p):    # 回转寿司雷达：转盘木响 + 扫描"叮—"
    knock = (np.sin(2 * np.pi * 230 * p * t(.1)) + .5 * np.sin(2 * np.pi * 610 * p * t(.1))) * env(int(SR * .1), .001, .025)
    x = t(.7); ping = np.sin(2 * np.pi * np.cumsum(880 * p + 440 * p * (x / .7)) / SR) * env(len(x), .005, .3) * .5
    return mix(knock, pad(knock * .7, .14), pad(ping, .2))
def candle_whoosh(r, p):  # 生日蜡烛轰炸：呼呼的火苗 + 吹气
    wind = bandpass(noise(.5, r), 300 * p, 2500 * p) * np.sin(np.pi * np.linspace(0, 1, int(SR * .5))) ** .6
    flick = np.zeros(int(SR * .5))
    for _ in range(8):
        i = int(r.uniform(0, .42) * SR); L = int(SR * .03); flick[i:i + L] += bandpass(noise(.03, r), 2000, 7000) * env(L, .001, .01) * .5
    return mix(wind, flick)
def frost_wind(r, p):     # 糖霜风暴：甜软的风 + 糖粒沙沙 + 高处的铃
    wind = lowpass(noise(1.0, r), 700 * p) * np.sin(np.pi * np.linspace(0, 1, int(SR * 1.0))) ** .5
    sand = highpass(noise(1.0, r), 6000) * np.sin(np.pi * np.linspace(0, 1, int(SR * 1.0))) * .25
    x = t(1.0); bell = sum(np.sin(2 * np.pi * f * p * x) * env(len(x), .005, .35) for f in (2093, 2637, 3136)) * .12
    return mix(wind, sand, pad(bell, .15))

SOUNDS = {'can_shake': can_shake, 'can_open': can_open, 'burp': burp, 'wafer_crunch': wafer_crunch, 'wafer_shatter': wafer_shatter,
          'jelly_boing': jelly_boing, 'bamboo_thunk': bamboo_thunk, 'sneeze': sneeze, 'fuse': fuse, 'caramel_bubble': caramel_bubble,
          'marsh_puff': marsh_puff, 'poprock_loop': poprock_loop,
          'burger_chomp': burger_chomp, 'rice_thud': rice_thud, 'jelly_dome': jelly_dome, 'sushi_radar': sushi_radar, 'candle_whoosh': candle_whoosh, 'frost_wind': frost_wind}
for name, fn in SOUNDS.items():
    for i in range(3):
        rng = np.random.default_rng((sum(map(ord, name)) * 7 + i) & 0xffff)
        save(name, i, fn(rng, [1.0, 1.07, .94][i]))
print('SFX_DONE', len(SOUNDS) * 3)
