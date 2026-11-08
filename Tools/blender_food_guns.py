# Blender (5.x) headless script: "food FPS" weapon set for 霓城封锁 —— each gun IS a food, and fires that food.
# Run:  blender.exe -b -P Tools/blender_food_guns.py -- <output_dir>
# Convention (same as the old dessert guns): barrel along -Y, up +Z, pistol grip at the origin, metres.
# Empties: "Muzzle", "Grip".  Special animated nodes: "TeaLevel" (SMG), "Spin" (LMG), "Haw0".."Haw4" (DMR).
import bpy, math, random, sys, os
from mathutils import Vector
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fallguys_style

OUT = sys.argv[sys.argv.index('--') + 1] if '--' in sys.argv else os.path.join(os.getcwd(), 'foodguns_out')
os.makedirs(OUT, exist_ok=True)
P = {
    'ketchup': (0.92, 0.12, 0.10), 'cap': (1.0, 0.98, 0.94), 'mustard': (1.0, 0.80, 0.15), 'leaf': (0.30, 0.78, 0.30),
    'tea': (0.78, 0.55, 0.36), 'cup': (0.97, 0.97, 1.0), 'pink': (1.0, 0.62, 0.78), 'boba': (0.16, 0.09, 0.07), 'strawp': (1.0, 0.38, 0.58),
    'red': (0.93, 0.16, 0.20), 'white': (1.0, 1.0, 1.0), 'corn': (1.0, 0.95, 0.80), 'butter': (1.0, 0.86, 0.32), 'gold': (1.0, 0.75, 0.22),
    'wood': (0.80, 0.62, 0.45), 'darkwood': (0.50, 0.33, 0.22), 'nori': (0.10, 0.17, 0.12), 'rice': (0.99, 0.99, 0.96), 'salmon': (1.0, 0.52, 0.32),
    'egg': (1.0, 0.82, 0.25), 'lacquer': (0.15, 0.12, 0.14), 'lemon': (1.0, 0.90, 0.25), 'lemonpale': (1.0, 0.97, 0.70), 'haw': (0.88, 0.06, 0.12),
    'bamboo': (0.72, 0.82, 0.50), 'bamboodark': (0.55, 0.62, 0.28), 'steamer': (0.86, 0.72, 0.52), 'dumpling': (0.99, 0.96, 0.88),
    'bag': (1.0, 0.92, 0.95), 'cream': (1.0, 0.55, 0.75), 'silver': (0.82, 0.85, 0.90), 'mint': (0.45, 0.92, 0.78),
    'pan': (0.30, 0.78, 0.86), 'paninner': (0.22, 0.24, 0.30), 'yolk': (1.0, 0.72, 0.10), 'soda': (0.25, 0.80, 0.95), 'black': (0.05, 0.05, 0.06),
    'blush': (1.0, 0.55, 0.60), 'glove': (1.0, 1.0, 1.0),
}
def lin(c): return tuple((x / 12.92) if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)
_m = {}
def mat(k, rough=0.4, metal=0.0):
    if k in _m: return _m[k]
    m = bpy.data.materials.new('FoodGun_' + k); m.use_nodes = True
    b = m.node_tree.nodes.get('Principled BSDF')
    b.inputs['Base Color'].default_value = (*lin(P[k]), 1); b.inputs['Roughness'].default_value = rough; b.inputs['Metallic'].default_value = metal
    _m[k] = m; return m
R = math.radians
def fin(o, m, bevel=0.0, sub=0, parent=None):
    o.data.materials.clear(); o.data.materials.append(m)
    if bevel > 0: md = o.modifiers.new('b', 'BEVEL'); md.width = bevel; md.segments = 4; md.limit_method = 'NONE'
    if sub: md = o.modifiers.new('s', 'SUBSURF'); md.levels = md.render_levels = sub
    for p in o.data.polygons: p.use_smooth = True
    o.parent = parent or ROOT; return o
def box(loc, s, m, rnd=.5, rot=(0, 0, 0), sub=1):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=[R(a) for a in rot]); o = bpy.context.object; o.scale = s
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True); return fin(o, m, min(s) * rnd * .5, sub)
AX = {'X': (0, 90, 0), 'Y': (90, 0, 0), 'Z': (0, 0, 0)}
def cyl(loc, r, d, m, axis='Y', bev=.25, v=32):
    bpy.ops.mesh.primitive_cylinder_add(vertices=v, radius=r, depth=d, location=loc, rotation=[R(a) for a in AX[axis]])
    return fin(bpy.context.object, m, min(r, d) * bev)
def cone(loc, r1, r2, d, m, axis='Y', v=32):   # with axis='Y' the narrow end (r2) points forward (-Y)
    bpy.ops.mesh.primitive_cone_add(vertices=v, radius1=r1, radius2=r2, depth=d, location=loc, rotation=[R(a) for a in AX[axis]])
    return fin(bpy.context.object, m, min(r1, d) * .12)
def sph(loc, s, m, seg=24):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=seg // 2, radius=1, location=loc); o = bpy.context.object; o.scale = s
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True); return fin(o, m)
def tor(loc, Rr, r, m, axis='Z'):
    bpy.ops.mesh.primitive_torus_add(major_radius=Rr, minor_radius=r, major_segments=40, minor_segments=14, location=loc, rotation=[R(a) for a in AX[axis]])
    return fin(bpy.context.object, m)
def empty(name, loc, parent=None):
    e = bpy.data.objects.new(name, None); e.location = loc; bpy.context.collection.objects.link(e); e.parent = parent or ROOT; return e
def adopt(objs, parent):
    bpy.context.view_layer.update()
    inv = parent.matrix_world.inverted()
    for o in objs: mw = o.matrix_world.copy(); o.parent = parent; o.matrix_world = mw
def origin_to(o, point):
    bpy.context.scene.cursor.location = point
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
def grip(m, y=0.0, z=-0.045):
    box((0, y, z), (.036, .05, .1), m, rnd=.7, rot=(-12, 0, 0))
def guard(m, y=-.035):
    tor((0, y, -.005), .022, .006, m, axis='X'); box((0, y + .002, -.008), (.008, .01, .022), m, rnd=.8, sub=0)
def face(x, y, z, scale=1.0, side=1):
    for dy in (-.014, .014):
        sph((x, y + dy * scale, z + .004 * scale), (.003 * scale, .006 * scale, .008 * scale), mat('black', .2))
        sph((x - .001 * side, y + dy * 1.7 * scale, z - .008 * scale), (.002 * scale, .007 * scale, .004 * scale), mat('blush', .5))
    sph((x, y, z - .006 * scale), (.002 * scale, .006 * scale, .003 * scale), mat('black', .2))
def muzzle(y, z): empty('Muzzle', (0, y, z)); empty('Grip', (0, 0, 0))

# ------------------------------------------------------------------ guns
def ketchup():        # P-9 番茄酱瓶
    cyl((0, -.07, .055), .048, .16, mat('ketchup', .25), bev=.4)
    sph((0, .01, .055), (.048, .03, .048), mat('ketchup', .25))
    cone((0, -.165, .055), .048, .022, .04, mat('ketchup', .25))
    cyl((0, -.192, .055), .025, .03, mat('cap', .3), bev=.3)
    cone((0, -.222, .055), .017, .005, .035, mat('cap', .3))
    sph((0, -.236, .048), (.006, .006, .009), mat('ketchup', .15))                 # drip at the nozzle
    cyl((0, -.075, .055), .0505, .07, mat('mustard', .35), bev=.1)                 # label
    sph((.05, -.075, .056), (.006, .016, .016), mat('ketchup', .3)); sph((.053, -.075, .072), (.004, .008, .004), mat('leaf'))
    face(.051, -.045, .06, .8)
    grip(mat('ketchup', .3)); guard(mat('cap'))
    muzzle(-.245, .055)

def milktea():        # SMG-7 珍珠奶茶枪
    box((0, -.06, .055), (.075, .2, .08), mat('pink', .35), rnd=.8)
    for i in range(8):                                                                 # thick striped straw barrel
        cyl((0, -.17 - i * .02, .068), .016, .0205, mat('strawp' if i % 2 == 0 else 'white', .3), bev=.05)
    cyl((0, -.33, .068), .0165, .005, mat('strawp', .3), bev=.2)
    cyl((0, -.13, -.055), .05, .12, mat('cup', .2), axis='Z', bev=.2)              # cup magazine (white = empty part)
    tea = cyl((0, -.13, -.055), .0515, .11, mat('tea', .3), axis='Z', bev=.15); tea.name = 'TeaLevel'
    origin_to(tea, (0, -.13, -.11))
    for i in range(10):
        a = i / 10 * 6.283
        sph((math.cos(a) * .05, -.13 + math.sin(a) * .05, -.1), (.011, .011, .011), mat('boba', .15))
    tor((0, -.13, .006), .05, .009, mat('pink', .3))
    face(.053, -.13, -.045, 1.1)
    grip(mat('boba', .3), y=.02); guard(mat('pink'), y=-.015)
    box((0, .07, .05), (.06, .06, .07), mat('pink', .35), rnd=.9)
    muzzle(-.34, .068)

def popcorn():        # AR-9 爆米花机
    box((0, -.08, .05), (.085, .26, .1), mat('red', .3), rnd=.5)
    for y in (-.17, -.11, -.05, .01):
        for x in (-.043, .043): box((x, y, .05), (.006, .025, .096), mat('white', .3), rnd=.6, sub=0)
    for x in (-.035, .035):
        for y in (-.19, .02): cyl((x, y, .13), .005, .06, mat('gold', .25, .3), axis='Z', bev=.3)
    box((0, -.085, .165), (.09, .24, .02), mat('red', .3), rnd=.8)                  # roof (sight rail)
    rnd = random.Random(3)
    for i in range(22):
        p = (rnd.uniform(-.03, .03), rnd.uniform(-.18, .01), rnd.uniform(.105, .14))
        sph(p, (.013, .013, .011), mat('corn' if i % 3 else 'butter', .55))
    for i in range(4):                                                                 # striped bucket barrel
        y = -.22 - i * .03; r1 = .028 + i * .007
        cone((0, y, .05), r1, r1 + .007, .031, mat('red' if i % 2 == 0 else 'white', .3))
    for i in range(5):
        sph((rnd.uniform(-.03, .03), -.335, .05 + rnd.uniform(-.03, .03)), (.013, .012, .012), mat('corn', .55))
    box((0, -.13, -.035), (.045, .065, .09), mat('butter', .35), rnd=.6, rot=(12, 0, 0))
    grip(mat('butter', .35)); guard(mat('white'))
    box((0, .12, .045), (.06, .1, .08), mat('butter', .35), rnd=.7)
    muzzle(-.35, .05)

def sushi():          # LMG-2 回转寿司加特林
    box((0, -.06, .05), (.11, .3, .1), mat('wood', .5), rnd=.4)
    for y in (-.17, .05): box((0, y, -.005), (.1, .03, .03), mat('darkwood', .5), rnd=.6)
    spin = empty('Spin', (0, -.3, .06))
    rolls = []
    for i in range(6):
        a = i / 6 * 6.283; x, z = math.cos(a) * .036, .06 + math.sin(a) * .036
        rolls.append(cyl((x, -.3, z), .02, .16, mat('nori', .5), bev=.15))
        rolls.append(cyl((x, -.381, z), .017, .006, mat('rice', .6), bev=.3))
        rolls.append(cyl((x, -.384, z), .007, .004, mat('salmon', .3), bev=.3))
    rolls.append(cyl((0, -.3, .06), .012, .17, mat('bamboo', .4), bev=.2))
    rolls.append(tor((0, -.24, .06), .06, .01, mat('darkwood', .4), axis='Y'))
    adopt(rolls, spin)
    box((0, -.08, .115), (.04, .07, .025), mat('rice', .6), rnd=.9)                   # nigiri sight
    box((0, -.08, .133), (.046, .078, .014), mat('salmon', .25), rnd=.9)
    for y in (-.1, -.08, -.06): box((0, y, .14), (.047, .004, .006), mat('white', .3), rnd=.9, sub=0)
    box((.085, -.05, .02), (.06, .12, .08), mat('lacquer', .25), rnd=.4)              # bento ammo box
    box((.085, -.05, .065), (.064, .124, .014), mat('red', .25), rnd=.6)
    sph((.118, -.08, .03), (.004, .016, .016), mat('rice', .6)); sph((.118, -.03, .03), (.004, .014, .014), mat('egg', .4))
    grip(mat('darkwood', .45)); guard(mat('darkwood'))
    box((0, .15, .05), (.07, .1, .08), mat('wood', .5), rnd=.6)
    muzzle(-.39, .06)

def haw():            # DMR-3 冰糖葫芦狙击枪
    box((0, -.05, .05), (.06, .28, .075), mat('lemon', .35), rnd=.6)
    cyl((0, -.405, .05), .014, .43, mat('bamboo', .4), bev=.3)
    for i in range(4): tor((0, -.24 - i * .1, .05), .0145, .004, mat('bamboodark', .4), axis='Y')
    cyl((0, -.4, .095), .004, .42, mat('wood', .5), bev=.3)                           # skewer
    for i in range(5):
        h = sph((0, -.53 + i * .07, .095), (.023, .022, .023), mat('haw', .08)); h.name = 'Haw%d' % i
        sph((0, -.553 + i * .07, .095), (.006, .004, .006), mat('darkwood', .5))
    for x in (-.012, .012): box((x, -.22, .075), (.004, .006, .04), mat('wood'), rnd=.8, sub=0)
    cyl((0, -.05, .125), .02, .13, mat('lemon', .3), bev=.2)                          # lemon-slice scope
    for y in (-.118, .018):
        cyl((0, y, .125), .032, .008, mat('lemon', .3), bev=.3)
        cyl((0, y - .002 if y < 0 else y + .002, .125), .027, .006, mat('lemonpale', .4), bev=.3)
        for k in range(6):
            a = k / 6 * 180
            box((0, y - .006 if y < 0 else y + .006, .125), (.003, .002, .05), mat('white', .4), rnd=.5, rot=(0, a, 0), sub=0)
    box((0, -.05, .1), (.02, .03, .02), mat('lemon'), rnd=.6)
    grip(mat('wood', .45)); guard(mat('wood'))
    box((0, .17, .045), (.05, .14, .075), mat('bamboo', .4), rnd=.6)
    muzzle(-.63, .05)

def dumpling():       # FS-12 饺子霰弹枪
    box((0, -.04, .04), (.08, .2, .08), mat('steamer', .55), rnd=.5)
    cyl((0, -.11, .115), .06, .05, mat('steamer', .55), axis='Z', bev=.15)           # bamboo steamer
    for z in (.092, .138): tor((0, -.11, z), .06, .005, mat('wood', .5))
    sph((0, -.11, .14), (.058, .058, .02), mat('steamer', .55))
    sph((0, -.11, .162), (.026, .018, .015), mat('dumpling', .5))
    for i in range(5): sph((0, -.124 + i * .007, .176), (.004, .004, .006), mat('dumpling', .5))
    cyl((0, -.245, .045), .035, .17, mat('bamboo', .45), bev=.2)
    for y in (-.19, -.27): tor((0, y, .045), .0355, .005, mat('bamboodark', .45), axis='Y')
    tor((0, -.33, .045), .033, .007, mat('bamboodark', .45), axis='Y')
    sph((0, -.21, -.005), (.034, .06, .026), mat('dumpling', .5))                    # dumpling pump
    for i in range(7): sph((0, -.24 + i * .01, .016), (.006, .005, .008), mat('dumpling', .5))
    face(.033, -.21, -.002, .8)
    grip(mat('darkwood', .45)); guard(mat('darkwood'))
    box((0, .1, .04), (.06, .08, .07), mat('wood', .5), rnd=.6)
    muzzle(-.335, .045)

def cream():          # BR-5 奶油裱花枪
    cone((0, -.08, .06), .056, .017, .23, mat('bag', .5))
    tor((0, .035, .06), .05, .012, mat('cream', .3), axis='Y')
    sph((0, .055, .06), (.03, .025, .03), mat('cream', .3))
    tor((0, -.03, .06), .047, .008, mat('mint', .35), axis='Y')                       # hand strap
    for i in range(3): tor((0, -.03, .12 + i * .013), .02 * (.75 ** i), .009 * (.75 ** i), mat('cream', .3))
    sph((0, -.03, .155), (.008, .008, .009), mat('haw', .1))
    cone((0, -.215, .06), .019, .006, .05, mat('silver', .2, .6), v=8)               # star nozzle
    for k in range(6):
        a = k / 6 * 6.283; box((math.cos(a) * .013, -.21, .06 + math.sin(a) * .013), (.004, .04, .004), mat('silver', .2, .6), rnd=.6, sub=0)
    sph((0, -.248, .06), (.008, .012, .008), mat('cream', .3))
    face(.045, -.06, .062, .9)
    grip(mat('mint', .35)); guard(mat('mint'))
    muzzle(-.26, .06)

# ------------------------------------------------------------------ melee / grenade / gloves / ammo
def pan():            # 平底锅（握把在原点，锅朝 -Y）
    cyl((0, -.1, 0), .016, .2, mat('darkwood', .45), bev=.4)
    sph((0, .005, 0), (.02, .02, .02), mat('pink', .35))
    cyl((0, -.33, 0), .13, .03, mat('pan', .25), axis='Z', bev=.3)
    tor((0, -.33, .015), .128, .013, mat('pan', .25))
    cyl((0, -.33, .016), .118, .004, mat('paninner', .35), axis='Z', bev=.3)
    sph((0, -.33, .022), (.075, .062, .008), mat('white', .3)); sph((.01, -.335, .03), (.027, .027, .016), mat('yolk', .15))
    empty('Head', (0, -.33, 0)); empty('Grip', (0, 0, 0))

def soda():           # 汽水罐手雷
    cyl((0, 0, 0), .033, .12, mat('soda', .2), axis='Z', bev=.15)
    cyl((0, 0, 0), .0335, .05, mat('lemon', .3), axis='Z', bev=.1)
    for i in range(6): a = i / 6 * 6.283; sph((math.cos(a) * .034, math.sin(a) * .034, (i % 3 - 1) * .012), (.004, .004, .004), mat('white', .3))
    cyl((0, 0, .062), .028, .006, mat('silver', .2, .7), axis='Z', bev=.3)
    tor((.008, 0, .067), .009, .002, mat('silver', .2, .7))
    face(.034, 0, -.03, .7)

def glove():          # 白色卡通手套（拳头）
    box((0, 0, 0), (.07, .06, .065), mat('glove', .45), rnd=.9, sub=2)
    for i in range(4): box((-.026 + i * .0175, -.033, -.005), (.018, .028, .05), mat('glove', .45), rnd=.95, sub=2)
    box((.04, -.012, .012), (.02, .04, .022), mat('glove', .45), rnd=.95, sub=2, rot=(0, 0, -25))
    tor((0, .035, 0), .036, .014, mat('glove', .45), axis='Y')
    for k in range(3): box((-.012 + k * .012, -.004, .033), (.003, .03, .002), mat('black', .5), rnd=.5, sub=0)

def ammo_ketchup(): sph((0, 0, 0), (.4, .55, .4), mat('ketchup', .15)); cone((0, .55, 0), .3, .02, .5, mat('ketchup', .15), axis='Y')
def ammo_boba(): sph((0, 0, 0), (.5, .5, .5), mat('boba', .1))
def ammo_popcorn():
    for p in ((0, 0, 0), (.25, .1, .15), (-.22, .05, .2), (.05, -.2, .25), (.1, .2, -.2)): sph(p, (.3, .3, .28), mat('corn' if p[0] >= 0 else 'butter', .55))
def ammo_sushi(): cyl((0, 0, 0), .45, .6, mat('nori', .5), bev=.15); cyl((0, -.3, 0), .38, .04, mat('rice', .6), bev=.3); cyl((0, -.32, 0), .16, .03, mat('salmon', .3), bev=.3)
def ammo_haw(): sph((0, 0, 0), (.5, .48, .5), mat('haw', .06)); sph((0, -.5, 0), (.12, .06, .12), mat('darkwood', .5))
def ammo_dumpling():
    sph((0, 0, 0), (.4, .6, .3), mat('dumpling', .5))
    for i in range(6): sph((0, -.3 + i * .12, .25), (.07, .06, .1), mat('dumpling', .5))
def ammo_cream():
    for i in range(3): tor((0, 0, i * .18), .35 * (.72 ** i), .16 * (.72 ** i), mat('cream', .3))
    sph((0, 0, .55), (.1, .1, .12), mat('cream', .3))

ITEMS = {'P9': ketchup, 'SMG7': milktea, 'AR9': popcorn, 'LMG2': sushi, 'DMR3': haw, 'FS12': dumpling, 'BR5': cream,
         'Pan': pan, 'Soda': soda, 'Glove': glove,
         'Ammo_Ketchup': ammo_ketchup, 'Ammo_Boba': ammo_boba, 'Ammo_Popcorn': ammo_popcorn, 'Ammo_Sushi': ammo_sushi,
         'Ammo_Haw': ammo_haw, 'Ammo_Dumpling': ammo_dumpling, 'Ammo_Cream': ammo_cream}
only = sys.argv[sys.argv.index('--') + 2:] if '--' in sys.argv else []
for name, build in ITEMS.items():
    if only and name not in only: continue
    bpy.ops.wm.read_factory_settings(use_empty=True); _m.clear()
    ROOT = bpy.data.objects.new('FoodGun_' + name, None); bpy.context.collection.objects.link(ROOT)
    build()
    fallguys_style.apply(ROOT, bevel_mul=1.4, s_mul=.92)
    path = os.path.join(OUT, name + '.glb')
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', export_apply=True, export_yup=True)
    print('EXPORTED', name, os.path.getsize(path))
print('FOODGUNS_DONE')
