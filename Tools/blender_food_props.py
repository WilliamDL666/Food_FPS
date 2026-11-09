# Blender (5.x) headless script: food-themed map props for 霓城封锁 (milk tea, hot pot, cakes, snacks...).
# Run:  blender.exe -b -P Tools/blender_food_props.py -- <output_dir>
# Convention: Z up, base on the ground at the origin, real metres (the game scales them to fit).
import bpy, math, random, sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fallguys_style
from mathutils import Vector

OUT = sys.argv[sys.argv.index('--') + 1] if '--' in sys.argv else os.path.join(os.getcwd(), 'food_out')
os.makedirs(OUT, exist_ok=True)
C = {
    'tea': (0.80, 0.58, 0.40), 'cup': (0.95, 0.97, 1.0), 'lid': (1.0, 0.75, 0.85), 'boba': (0.18, 0.10, 0.08), 'straw': (1.0, 0.4, 0.55),
    'pot': (0.62, 0.64, 0.70), 'potdark': (0.30, 0.31, 0.35), 'redsoup': (0.88, 0.18, 0.10), 'whitesoup': (0.98, 0.93, 0.80),
    'chili': (0.95, 0.10, 0.05), 'meat': (0.75, 0.42, 0.35), 'tofu': (0.98, 0.95, 0.85), 'green': (0.35, 0.80, 0.30),
    'chopstick': (0.72, 0.50, 0.28), 'sponge': (0.98, 0.85, 0.60), 'cream': (1.0, 0.97, 0.92), 'pink': (1.0, 0.62, 0.76),
    'choco': (0.36, 0.20, 0.12), 'straw_berry': (0.90, 0.08, 0.15), 'leaf': (0.30, 0.75, 0.30), 'candle': (0.55, 0.85, 1.0),
    'flame': (1.0, 0.70, 0.25), 'dough': (0.90, 0.66, 0.38), 'icing': (1.0, 0.48, 0.70), 'bun': (0.88, 0.58, 0.28),
    'patty': (0.38, 0.22, 0.14), 'cheese': (1.0, 0.80, 0.20), 'lettuce': (0.45, 0.85, 0.30), 'tomato': (0.92, 0.22, 0.18),
    'rice': (0.98, 0.98, 0.95), 'salmon': (1.0, 0.55, 0.35), 'nori': (0.12, 0.18, 0.12), 'dumpling': (0.98, 0.95, 0.88),
    'mint': (0.55, 0.93, 0.80), 'lemon': (1.0, 0.88, 0.42), 'berry': (0.68, 0.32, 0.86), 'wafer': (0.93, 0.74, 0.45),
    'sky': (0.52, 0.80, 1.0), 'white': (0.98, 0.98, 1.0), 'red': (0.93, 0.15, 0.22), 'bowl': (0.95, 0.35, 0.30), 'broth': (0.96, 0.82, 0.55),
    'noodle': (1.0, 0.92, 0.62), 'egg': (1.0, 0.75, 0.20), 'eggwhite': (0.99, 0.98, 0.95), 'skewer': (0.85, 0.65, 0.40), 'stickcup': (0.85, 0.20, 0.20),
}
SPR = [(1, .35, .5), (.4, .8, 1), (1, .9, .3), (.5, 1, .6), (.8, .5, 1), (1, 1, 1)]
def lin(c): return tuple((x / 12.92) if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)
_m = {}
def mat(key, rgb=None, rough=0.45, emit=0.0, alpha=1.0):
    k = key + ('_a' if alpha < 1 else '')
    if k in _m: return _m[k]
    rgb = rgb or C[key]; m = bpy.data.materials.new('Food_' + k); m.use_nodes = True
    b = m.node_tree.nodes.get('Principled BSDF')
    b.inputs['Base Color'].default_value = (*lin(rgb), 1); b.inputs['Roughness'].default_value = rough
    if emit: b.inputs['Emission Color'].default_value = (*lin(rgb), 1); b.inputs['Emission Strength'].default_value = emit
    if alpha < 1:
        b.inputs['Alpha'].default_value = alpha
        try: m.surface_render_method = 'BLENDED'
        except Exception: m.blend_method = 'BLEND'
    _m[k] = m; return m

def fin(o, m, bevel=0.0, sub=0):
    o.data.materials.clear(); o.data.materials.append(m)
    if bevel: md = o.modifiers.new('b', 'BEVEL'); md.width = bevel; md.segments = 3
    if sub: md = o.modifiers.new('s', 'SUBSURF'); md.levels = sub; md.render_levels = sub
    for p in o.data.polygons: p.use_smooth = True
    o.parent = ROOT; return o
R = math.radians
def cyl(loc, r, d, m, r2=None, rot=(0, 0, 0), v=40, bev=0.15):
    if r2 is None: bpy.ops.mesh.primitive_cylinder_add(vertices=v, radius=r, depth=d, location=loc, rotation=[R(a) for a in rot])
    else: bpy.ops.mesh.primitive_cone_add(vertices=v, radius1=r, radius2=r2, depth=d, location=loc, rotation=[R(a) for a in rot])
    return fin(bpy.context.object, m, bevel=min(r, d) * bev)
def sph(loc, s, m, seg=24):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=seg // 2, radius=1, location=loc); o = bpy.context.object; o.scale = s
    bpy.ops.object.transform_apply(scale=True); return fin(o, m)
def box(loc, s, m, rnd=0.3, rot=(0, 0, 0), sub=1):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=[R(a) for a in rot]); o = bpy.context.object; o.scale = s
    bpy.ops.object.transform_apply(scale=True); return fin(o, m, bevel=min(s) * rnd * .5, sub=sub)
def tor(loc, Rr, r, m, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_torus_add(major_radius=Rr, minor_radius=r, major_segments=40, minor_segments=16, location=loc, rotation=[R(a) for a in rot])
    return fin(bpy.context.object, m)
def sprinkles(center, half, n, seed, size=1.0):
    rnd = random.Random(seed)
    for i in range(n):
        p = Vector(center) + Vector(((rnd.random() * 2 - 1) * half[0], (rnd.random() * 2 - 1) * half[1], (rnd.random() * 2 - 1) * half[2]))
        bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.02 * size, depth=0.07 * size, location=p, rotation=(rnd.random() * 3.14, rnd.random() * 3.14, 0))
        fin(bpy.context.object, mat('s%d' % (i % 6), SPR[i % 6], rough=.25), bevel=0.008 * size)
def frosting(base, r0, layers, m, step):
    r = r0
    for i in range(layers): tor((base[0], base[1], base[2] + i * step), r, r * .42, m); r *= .72
    sph((base[0], base[1], base[2] + layers * step), (r, r, r), m)

# ------------------------------------------------------------------ props
def milktea():          # ~2.4 m giant bubble tea (scaled up in game for landmarks)
    cyl((0, 0, .85), .62, 1.7, mat('tea', rough=.3), r2=.75, v=48, bev=.03)          # milk tea body (opaque, game-friendly)
    cyl((0, 0, 1.85), .755, .3, mat('cup', rough=.15), r2=.78, v=48, bev=.05)         # clear-ish cup top above the tea
    rnd = random.Random(2)
    for i in range(30):                                                                 # boba pressed against the cup wall
        a = rnd.random() * 6.283; z = .1 + rnd.random() * .45; r = .62 + .13 * (z / 1.7)
        sph((math.cos(a) * r, math.sin(a) * r, z), (.1, .1, .1), mat('boba', rough=.2))
    cyl((0, 0, 1.15), .73, .22, mat('pink'), r2=.75, v=48, bev=.2)                    # label band
    sph((0, 0, 2.0), (.8, .8, .28), mat('lid', rough=.3))                             # dome lid
    cyl((.22, 0, 2.5), .07, 1.6, mat('straw', rough=.3), rot=(0, 12, 0), v=20, bev=.3)
    tor((0, 0, 2.0), .79, .04, mat('lid'))

def hotpot():           # yuanyang hot pot, 3 m across, ~1.3 m tall
    cyl((0, 0, .5), 1.5, 1.0, mat('pot', rough=.25), v=56, bev=.05)
    tor((0, 0, 1.0), 1.5, .07, mat('potdark', rough=.3))
    cyl((0, 0, .92), 1.42, .1, mat('redsoup', rough=.15), v=56, bev=.2)
    box((0, 0, .98), (.12, 2.8, .12), mat('pot'), rnd=.6)                            # divider
    bpy.ops.mesh.primitive_cylinder_add(vertices=56, radius=1.36, depth=.12, location=(0, 0, .935))
    o = bpy.context.object; o.scale = (.5, 1, 1); bpy.ops.object.transform_apply(scale=True); o.location.x = -.7
    fin(o, mat('whitesoup', rough=.2))                                                 # white broth half
    rnd = random.Random(4)
    for i in range(14):
        x = .2 + rnd.random() * 1.0; y = (rnd.random() * 2 - 1) * 1.0
        if x * x + y * y > 1.6: continue
        sph((x, y, 1.0), (.08, .08, .06), mat('chili', rough=.2)) if i % 3 else sph((x, y, 1.0), (.16, .16, .14), mat('meat'))
    for i in range(6):
        x = -(.3 + rnd.random() * .8); y = (rnd.random() * 2 - 1) * .9
        box((x, y, 1.02), (.25, .25, .18), mat('tofu', rough=.6), rnd=.4) if i % 2 else sph((x, y, 1.0), (.17, .17, .15), mat('meat'))
        sph((x + .2, y - .1, 1.0), (.18, .06, .03), mat('green'))
    for dx in (-.08, .08):
        cyl((.5 + dx, .4, 1.6), .035, 1.6, mat('chopstick'), rot=(0, 28, 20), v=10, bev=.3)
    for a in (0, 180): cyl((math.cos(R(a)) * 1.62, math.sin(R(a)) * 1.62, .85), .1, .35, mat('potdark'), rot=(90, 0, a), v=16)

def bigcake():          # 3-tier birthday cake, ~3 m
    tiers = [(1.65, .9, 'pink'), (1.05, .75, 'cream'), (.5, .65, 'pink')]   # wide ledges so players can climb tier by tier
    z = 0
    for i, (r, h, k) in enumerate(tiers):
        cyl((0, 0, z + h / 2), r, h, mat(k), v=56, bev=.12)
        for j in range(18):                                                             # chocolate drips
            a = j / 18 * 6.283
            sph((math.cos(a) * r, math.sin(a) * r, z + h - .12 - (j % 3) * .06), (.13, .13, .22 + (j % 3) * .05), mat('choco', rough=.25))
        tor((0, 0, z + h), r * .98, .09, mat('cream'))
        for j in range(10):
            a = j / 10 * 6.283
            sph((math.cos(a) * r * .8, math.sin(a) * r * .8, z + h + .12), (.13, .13, .13), mat('straw_berry', rough=.25))
        z += h
    for j in range(5):
        a = j / 5 * 6.283
        cyl((math.cos(a) * .3, math.sin(a) * .3, z + .3), .05, .6, mat('candle'), v=12)
        sph((math.cos(a) * .3, math.sin(a) * .3, z + .66), (.05, .05, .09), mat('flame', emit=4))

def cakeslice():        # triangular wedge cover, ~1.1 m tall
    bpy.ops.mesh.primitive_cylinder_add(vertices=3, radius=1.0, depth=1.0, location=(0, 0, .5))
    o = bpy.context.object; o.scale = (1.2, .8, 1); bpy.ops.object.transform_apply(scale=True)
    fin(o, mat('sponge', rough=.7), bevel=.06)
    for zz, k in ((.33, 'pink'), (.66, 'cream')):
        bpy.ops.mesh.primitive_cylinder_add(vertices=3, radius=1.02, depth=.08, location=(0, 0, zz))
        o = bpy.context.object; o.scale = (1.2, .8, 1); bpy.ops.object.transform_apply(scale=True); fin(o, mat(k), bevel=.02)
    bpy.ops.mesh.primitive_cylinder_add(vertices=3, radius=1.03, depth=.1, location=(0, 0, 1.03))
    o = bpy.context.object; o.scale = (1.2, .8, 1); bpy.ops.object.transform_apply(scale=True); fin(o, mat('cream'), bevel=.04)
    sph((.2, 0, 1.2), (.18, .18, .16), mat('straw_berry', rough=.25)); sph((.25, .05, 1.36), (.05, .08, .03), mat('leaf'))

def donut():            # giant standing donut, ~1.8 m
    tor((0, 0, 1.0), .62, .36, mat('dough'), rot=(90, 0, 0))
    tor((0, -.05, 1.0), .62, .3, mat('icing', rough=.25), rot=(90, 0, 0))
    sprinkles((0, -.32, 1.0), (.85, .06, .85), 40, 5, size=1.6)

def burger():           # ~1.4 m
    sph((0, 0, .25), (.85, .85, .28), mat('bun')); cyl((0, 0, .5), .9, .22, mat('patty', rough=.7), v=40, bev=.4)
    box((0, 0, .65), (1.6, 1.6, .05), mat('cheese'), rnd=.6, rot=(0, 0, 45), sub=0)
    cyl((0, 0, .72), .95, .08, mat('lettuce'), v=24, bev=.4); cyl((0, 0, .82), .82, .1, mat('tomato'), v=32, bev=.4)
    sph((0, 0, .95), (.88, .88, .5), mat('bun'))
    rnd = random.Random(7)
    for i in range(20): a = rnd.random() * 6.28; rr = rnd.random() * .6; sph((math.cos(a) * rr, math.sin(a) * rr, 1.35 - rr * .25), (.04, .025, .02), mat('cream'))

def sushi():            # salmon nigiri, ~0.8 m tall
    box((0, 0, .3), (1.4, .8, .6), mat('rice', rough=.8), rnd=.9)
    box((0, 0, .66), (1.55, .9, .18), mat('salmon', rough=.3), rnd=.8)
    for x in (-.4, 0, .4): box((x, 0, .7), (.06, .92, .19), mat('white', rough=.3), rnd=.9, sub=0)
    box((0, 0, .45), (.25, .84, .7), mat('nori', rough=.6), rnd=.4, sub=0)

def dumpling():         # ~0.9 m
    sph((0, 0, .4), (.75, .5, .42), mat('dumpling', rough=.6))
    for i in range(7): sph((-.5 + i * .17, 0, .78), (.08, .1, .14), mat('dumpling', rough=.6))

def macarons():         # stack of 3, ~1.1 m
    cols = ['pink', 'mint', 'lemon']
    for i, k in enumerate(cols):
        z = .18 + i * .34
        cyl((0, 0, z - .07), .5, .13, mat(k), v=40, bev=.5); cyl((0, 0, z + .07), .5, .13, mat(k), v=40, bev=.5)
        cyl((0, 0, z), .44, .07, mat('cream'), v=40, bev=.4)

def icecream():         # ~2.4 m cone with two scoops
    cyl((0, 0, .75), .05, 1.5, mat('wafer', rough=.7), r2=.55, v=32, bev=.02)
    sph((0, 0, 1.65), (.58, .58, .5), mat('mint')); sph((0, 0, 2.15), (.48, .48, .42), mat('pink'))
    sph((0, 0, 2.6), (.12, .12, .12), mat('straw_berry', rough=.2)); sprinkles((0, 0, 2.2), (.4, .4, .2), 24, 9, size=1.2)

def popsicle():         # ~2 m
    box((0, 0, 1.25), (.7, .25, 1.4), mat('sky'), rnd=.9)
    box((0, 0, 1.6), (.72, .27, .7), mat('pink'), rnd=.9)
    box((0, 0, .3), (.14, .06, .7), mat('wafer', rough=.8), rnd=.6)

def lollipoplamp():     # street lamp, ~4.5 m
    cyl((0, 0, 1.9), .08, 3.8, mat('white', rough=.4), v=16)
    for i in range(10): cyl((0, 0, .2 + i * .38), .085, .16, mat('red'), v=16, bev=.1)
    tor((0, 0, 4.1), .45, .16, mat('pink'), rot=(90, 0, 0)); tor((0, 0, 4.1), .24, .12, mat('mint'), rot=(90, 0, 0))
    sph((0, 0, 4.1), (.14, .1, .14), mat('lemon', emit=2.5))

def skewers():          # chuanchuan bucket, ~1.6 m
    cyl((0, 0, .35), .38, .7, mat('stickcup'), r2=.45, v=32, bev=.1)
    rnd = random.Random(11)
    for i in range(9):
        a = i / 9 * 6.283; x, y = math.cos(a) * .2, math.sin(a) * .2; tilt = (rnd.random() - .5) * 20
        cyl((x, y, 1.0), .015, 1.4, mat('skewer'), rot=(tilt, tilt * .6, 0), v=8)
        for k in range(3): sph((x, y, 1.0 + k * .16), (.07, .07, .06), mat(['meat', 'tofu', 'green', 'chili'][(i + k) % 4]))

def ramen():            # ~1.2 m bowl
    cyl((0, 0, .4), .4, .8, mat('bowl', rough=.3), r2=.85, v=48, bev=.04)
    cyl((0, 0, .76), .8, .06, mat('broth', rough=.15), v=48)
    for i in range(6): tor((-.15 + (i % 3) * .1, -.1, .8), .25, .02, mat('noodle'), rot=(90, 0, i * 30))
    sph((.35, .2, .8), (.16, .12, .08), mat('eggwhite')); sph((.35, .2, .84), (.08, .06, .04), mat('egg'))
    cyl((-.3, .3, .8), .14, .04, mat('white'), v=24); box((.0, .45, .8), (.4, .25, .03), mat('patty'), rnd=.5)
    for dx in (-.06, .06): cyl((.3 + dx, -.3, 1.1), .02, 1.0, mat('chopstick'), rot=(0, 35, 0), v=8)

def candyfence():       # ~1 m tall, 2 m wide
    for x in (-.9, 0, .9):
        cyl((x, 0, .5), .07, 1.0, mat('white'), v=16)
        for i in range(4): cyl((x, 0, .1 + i * .25), .072, .1, mat('red'), v=16, bev=.1)
        tor((x + .12, 0, 1.0), .12, .07, mat('red'), rot=(90, 0, 0))
    for z in (.35, .75): cyl((0, 0, z), .045, 1.9, mat('pink'), rot=(0, 90, 0), v=12)


# ---- climbable cover blocks: 6.0 (X) x 2.44 (Y) x 2.0 (Z) — flat walkable top, decorations only around the rim
L, W, H = 6.0, 2.44, 2.0
def rim_dollops(m, z, n=9, size=.2):
    for i in range(n):
        x = -L / 2 + .35 + i * (L - .7) / (n - 1)
        for y in (-W / 2 + .12, W / 2 - .12): sph((x, y, z), (size, size, size * .8), m)

def chocoblock():       # giant chocolate bar, 2 x 6 squares
    box((0, 0, H / 2 - .1), (L, W, H - .2), mat('choco', rough=.3), rnd=.12)
    for i in range(6):
        for j in range(2):
            box((-L / 2 + .5 + i * 1.0, -W / 4 + j * W / 2, H - .1), (.86, W / 2 - .14, .22), mat('choco', rough=.3), rnd=.5)
    box((-L / 2 + 1.2, 0, H / 2 - .1), (2.4, W + .06, H - .1), mat('wrapper', (.95, .2, .35), rough=.35), rnd=.08)   # half-peeled wrapper
    box((-L / 2 + 1.2, 0, H / 2 - .1), (.25, W + .1, H - .05), mat('foil', (1, .85, .3), rough=.2), rnd=.3)

def spongeblock():      # strawberry cream sponge slab
    for k, (z, hh, key) in enumerate(((.33, .66, 'sponge'), (.7, .1, 'cream'), (1.05, .6, 'sponge'), (1.42, .12, 'pink'), (1.72, .5, 'sponge'))):
        box((0, 0, z), (L, W, hh), mat(key, rough=.5 if key == 'sponge' else .3), rnd=.15 if key == 'sponge' else .6)
    box((0, 0, H - .04), (L + .06, W + .06, .1), mat('cream'), rnd=.8)
    rim_dollops(mat('cream'), H + .08)
    for i in range(5): sph((-2.4 + i * 1.2, -W / 2 - .02, .7), (.16, .1, .14), mat('straw_berry', rough=.2))

def waferblock():       # pink strawberry wafer
    for i in range(7):
        z = .14 + i * .29
        box((0, 0, z), (L, W, .2), mat('wafer', rough=.5), rnd=.3)
        if i < 6: box((0, 0, z + .145), (L - .1, W - .1, .09), mat('pink' if i % 2 == 0 else 'cream'), rnd=.6)
    for x in range(-2, 3):
        box((x * 1.1, 0, H - .02), (.06, W + .02, .05), mat('biscuitline', (.8, .55, .3), rough=.6), rnd=.5, sub=0)

def jellyblock():       # wobbly rainbow jelly (Fall Guys favourite)
    cols = [(.98, .3, .45), (1, .62, .2), (1, .9, .3), (.35, .85, .5), (.35, .65, 1)]
    for i, c in enumerate(cols):
        box((-L / 2 + .6 + i * 1.2, 0, H / 2), (1.18, W, H), mat('jelly%d' % i, c, rough=.12), rnd=.35)
    rim_dollops(mat('cream'), H + .06, n=7, size=.22)
    for i in range(5): sph((-L / 2 + .6 + i * 1.2, 0, H + .06), (.13, .13, .13), mat('cherry', (.85, .05, .12), rough=.15))

def pancakes():         # climbable pancake stack: 3 steps of ~0.75 m, radius 2.4 → 1.3
    z = 0
    for i, (r, h) in enumerate(((2.4, .75), (1.65, .75), (.95, .75))):
        cyl((0, 0, z + h / 2), r, h, mat('pancake', (.86, .56, .3)), v=48, bev=.35)
        cyl((0, 0, z + h - .02), r * .97, .08, mat('syrup', (.75, .38, .08), rough=.1), v=48, bev=.5)
        for j in range(10):
            a = j / 10 * 6.283 + i
            sph((math.cos(a) * r * .99, math.sin(a) * r * .99, z + h - .14), (.14, .14, .24), mat('syrup', (.75, .38, .08), rough=.1))
        z += h
    box((0, 0, z + .1), (.45, .45, .2), mat('butter', (1, .9, .45), rough=.3), rnd=.5)
    for j in range(6):
        a = j / 6 * 6.283
        sph((math.cos(a) * .7, math.sin(a) * .7, z + .1), (.1, .1, .09), mat('berry', rough=.2))

def kawaii(x, y, z, s=1.0):   # V19：大美食都加上一张可爱的脸（朝 -Y）
    for dx in (-.11 * s, .11 * s):
        sph((x + dx, y, z), (.05 * s, .03 * s, .075 * s), mat('eyeK', (.05, .04, .05), .15))
        sph((x + dx + .015 * s, y - .02 * s, z + .025 * s), (.016 * s, .01 * s, .02 * s), mat('eyeW', (1, 1, 1), .2))
        sph((x + dx * 1.7, y + .01 * s, z - .09 * s), (.05 * s, .015 * s, .03 * s), mat('blushK', (1, .55, .6), .5))
    sph((x, y, z - .08 * s), (.035 * s, .015 * s, .02 * s), mat('eyeK', (.05, .04, .05), .15))
FACES = {'MilkTea': (0, -.74, .75, 1.6), 'HotPot': (0, -1.52, .55, 2.0), 'BigCake': (0, -1.62, .45, 2.2), 'CakeSlice': (0, -.82, .55, 1.3), 'Donut': (0, -.68, 1.0, 1.3),
         'Burger': (0, -.86, .6, 1.4), 'Sushi': (0, -.47, .3, 1.0), 'Dumpling': (0, -.5, .42, 1.0), 'Macarons': (0, -.52, .52, .9), 'IceCream': (0, -.56, 1.65, 1.2),
         'Popsicle': (0, -.14, 1.35, .9), 'Skewers': (0, -.42, .35, .8), 'Ramen': (0, -.62, .4, 1.1), 'ChocoBlock': (0, -1.23, 1.0, 2.0), 'SpongeBlock': (0, -1.23, 1.0, 2.0),
         'WaferBlock': (0, -1.23, 1.0, 2.0), 'JellyBlock': (0, -1.23, 1.0, 2.0), 'Pancakes': (0, -2.42, .4, 2.0)}

PROPS = {'MilkTea': milktea, 'HotPot': hotpot, 'BigCake': bigcake, 'CakeSlice': cakeslice, 'Donut': donut, 'Burger': burger,
         'Sushi': sushi, 'Dumpling': dumpling, 'Macarons': macarons, 'IceCream': icecream, 'Popsicle': popsicle,
         'LollipopLamp': lollipoplamp, 'Skewers': skewers, 'Ramen': ramen, 'CandyFence': candyfence,
         'ChocoBlock': chocoblock, 'SpongeBlock': spongeblock, 'WaferBlock': waferblock, 'JellyBlock': jellyblock, 'Pancakes': pancakes}
for name, build in PROPS.items():
    bpy.ops.wm.read_factory_settings(use_empty=True); _m.clear()
    ROOT = bpy.data.objects.new('Food_' + name, None); bpy.context.collection.objects.link(ROOT)
    build()
    if name in FACES: kawaii(*FACES[name])
    fallguys_style.apply(ROOT)
    path = os.path.join(OUT, name + '.glb')
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', export_apply=True, export_yup=True)
    print('EXPORTED', name, os.path.getsize(path))
print('FOOD_DONE')
