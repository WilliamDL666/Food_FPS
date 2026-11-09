# Blender (5.x) headless script: ingredients, kitchen facilities and unlockable hats for 霓城封锁 (food FPS, V18).
# Run:  blender.exe -b -P Tools/blender_kitchen.py -- <kitchen_out_dir> <hats_out_dir>
# Ingredients ~0.4 m, facilities ~1–2 m, hats ~0.8 wide (rig units), origin at the bottom centre, Z up.
import bpy, math, random, sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fallguys_style

args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = args[0] if args else os.getcwd(); HATS = args[1] if len(args) > 1 else OUT
os.makedirs(OUT, exist_ok=True); os.makedirs(HATS, exist_ok=True)
def lin(c): return tuple((x / 12.92) if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)
_m = {}
def mat(k, rgb, rough=.35, emit=0):
    if k in _m: return _m[k]
    m = bpy.data.materials.new(k); m.use_nodes = True; b = m.node_tree.nodes.get('Principled BSDF')
    b.inputs['Base Color'].default_value = (*lin(rgb), 1); b.inputs['Roughness'].default_value = rough
    if emit: b.inputs['Emission Color'].default_value = (*lin(rgb), 1); b.inputs['Emission Strength'].default_value = emit
    _m[k] = m; return m
R = math.radians
def fin(o, m, bev=0, sub=0):
    o.data.materials.clear(); o.data.materials.append(m)
    if bev: md = o.modifiers.new('b', 'BEVEL'); md.width = bev; md.segments = 4; md.limit_method = 'NONE'
    if sub: md = o.modifiers.new('s', 'SUBSURF'); md.levels = md.render_levels = sub
    for p in o.data.polygons: p.use_smooth = True
    o.parent = ROOT; return o
def box(loc, s, m, rnd=.5, rot=(0, 0, 0), sub=1):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=[R(a) for a in rot]); o = bpy.context.object; o.scale = s
    bpy.ops.object.transform_apply(scale=True); return fin(o, m, min(s) * rnd * .5, sub)
def cyl(loc, r, d, m, r2=None, rot=(0, 0, 0), v=32, bev=.3):
    if r2 is None: bpy.ops.mesh.primitive_cylinder_add(vertices=v, radius=r, depth=d, location=loc, rotation=[R(a) for a in rot])
    else: bpy.ops.mesh.primitive_cone_add(vertices=v, radius1=r, radius2=r2, depth=d, location=loc, rotation=[R(a) for a in rot])
    return fin(bpy.context.object, m, min(r, d) * bev)
def sph(loc, s, m, seg=24):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=seg // 2, radius=1, location=loc); o = bpy.context.object; o.scale = s
    bpy.ops.object.transform_apply(scale=True); return fin(o, m)
def tor(loc, Rr, r, m, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_torus_add(major_radius=Rr, minor_radius=r, major_segments=40, minor_segments=14, location=loc, rotation=[R(a) for a in rot]); return fin(bpy.context.object, m)
def eyes(x, y, z, s=1.0):
    for dx in (-.03 * s, .03 * s): sph((x + dx, y, z), (.012 * s, .008 * s, .018 * s), mat('black', (.04, .04, .05), .2))

# ------------------------------------------------------------------ ingredients (~0.4 m)
def milk():
    box((0, 0, .17), (.2, .2, .32), mat('carton', (1, 1, 1)), rnd=.3)
    bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=.14, radius2=.02, depth=.1, location=(0, 0, .38), rotation=(0, 0, R(45))); fin(bpy.context.object, mat('carton', (1, 1, 1)), .01)
    box((0, -.101, .17), (.16, .01, .14), mat('milkblue', (.35, .65, 1)), rnd=.5, sub=0); eyes(0, -.11, .2)
def tea():
    for i in range(5): sph((math.cos(i * 1.3) * .08, math.sin(i * 1.3) * .08, .08 + i * .03), (.09, .045, .02), mat('leaf', (.3, .75, .3), .4))
    cyl((0, 0, .05), .12, .08, mat('teacup', (.95, .95, .9)), r2=.09)
def boba(): [sph((math.cos(i * 2.1) * .07 * (i % 3), math.sin(i * 2.1) * .07 * (i % 3), .06 + i * .035), (.06, .06, .06), mat('boba', (.15, .08, .06), .12)) for i in range(7)]
def flour():
    sph((0, 0, .16), (.16, .14, .17), mat('sack', (.96, .9, .78), .7)); tor((0, 0, .3), .06, .02, mat('string', (.8, .6, .35)))
    box((0, -.135, .15), (.12, .01, .08), mat('label', (1, .5, .4)), rnd=.5, sub=0)
def egg(): sph((0, 0, .15), (.12, .12, .16), mat('egg', (1, .97, .9), .3)); eyes(0, -.115, .17)
def chili():
    bpy.ops.mesh.primitive_cone_add(vertices=24, radius1=.08, radius2=.01, depth=.36, location=(0, 0, .12), rotation=(R(70), 0, 0)); fin(bpy.context.object, mat('chili', (.95, .1, .05), .25), .02, 1)
    cyl((0, .17, .19), .03, .06, mat('leaf', (.3, .75, .3), .4), rot=(R(70), 0, 0)); eyes(0, -.02, .16, .8)
def hotpotbase():
    box((0, 0, .1), (.28, .2, .18), mat('base', (.85, .2, .1), .4), rnd=.4)
    box((0, 0, .2), (.3, .22, .02), mat('wrap', (1, .85, .2), .3), rnd=.5, sub=0)
    for i in range(6): sph((-.1 + i * .04, -.101, .1), (.012, .005, .012), mat('chiliseed', (1, .9, .3)))
def bread():
    box((0, 0, .1), (.32, .2, .18), mat('crust', (.85, .55, .25), .5), rnd=.8)
    for i in range(3): box((-.09 + i * .09, 0, .19), (.03, .16, .015), mat('cut', (1, .85, .6)), rnd=.5, sub=0)
def cheese():
    bpy.ops.mesh.primitive_cylinder_add(vertices=3, radius=.2, depth=.16, location=(0, 0, .08)); fin(bpy.context.object, mat('cheese', (1, .8, .2), .4), .015)
    for p in ((.03, -.05, .16), (-.06, .04, .16), (.07, .07, .16)): sph(p, (.025, .025, .012), mat('hole', (.85, .6, .1)))
def ice():
    box((0, 0, .13), (.24, .24, .24), mat('ice', (.75, .92, 1), .05), rnd=.4); eyes(0, -.125, .15)
def sugar(): [box((x, y, .06 + z), (.11, .11, .11), mat('sugar', (1, 1, 1), .6), rnd=.3) for x, y, z in ((-.06, 0, 0), (.06, 0, 0), (0, .02, .11))]
def rice():
    cyl((0, 0, .08), .15, .14, mat('bowl', (.4, .6, 1), .3), r2=.1)
    sph((0, 0, .16), (.14, .14, .08), mat('rice', (.99, .99, .97), .6))
def nori():
    box((0, 0, .15), (.26, .02, .28), mat('nori', (.1, .16, .12), .5), rnd=.3, rot=(0, 0, 10))
    box((0, .03, .15), (.26, .02, .28), mat('nori', (.1, .16, .12), .5), rnd=.3, rot=(0, 0, -8))

# ------------------------------------------------------------------ kitchen facilities
def milkteamachine():
    box((0, 0, .6), (.9, .6, 1.2), mat('mach', (1, .62, .78)), rnd=.25)
    box((0, -.31, .75), (.6, .02, .4), mat('screen', (.4, 1, .9), .2, 1.2), rnd=.4, sub=0)
    cyl((0, -.25, .22), .14, .2, mat('cup', (.97, .97, 1)), r2=.17)
    cyl((0, -.25, .2), .145, .14, mat('tea', (.78, .55, .36)), r2=.165)
    cyl((0, -.15, 1.32), .08, .25, mat('strawp', (1, .38, .58)))
    sph((0, 0, 1.28), (.42, .28, .12), mat('mach', (1, .62, .78)))
def oven():
    box((0, 0, .55), (1.0, .7, 1.1), mat('oven', (.95, .35, .3)), rnd=.25)
    box((0, -.36, .55), (.7, .02, .55), mat('window', (1, .6, .2), .2, 1.6), rnd=.4, sub=0)
    cyl((0, -.37, .92), .04, .6, mat('handle', (.9, .9, .95), .2), rot=(0, R(90), 0))
    for x in (-.3, 0, .3): cyl((x, -.36, .98), .05, .04, mat('knob', (1, .9, .4)), rot=(R(90), 0, 0))
def freezer():
    box((0, 0, .7), (.9, .7, 1.4), mat('freezer', (.7, .9, 1), .2), rnd=.25)
    box((0, -.36, .95), (.8, .02, .02), mat('seal', (.5, .7, .9)), rnd=.5, sub=0)
    for i in range(6): sph((random.uniform(-.35, .35), random.uniform(-.3, .3), 1.42), (.06, .06, .04), mat('frost', (1, 1, 1), .5))
    eyes(0, -.37, 1.1, 2)
def hotpotstation():
    box((0, 0, .38), (1.1, 1.1, .76), mat('table', (.75, .5, .32), .5), rnd=.15)
    cyl((0, 0, .9), .42, .3, mat('pot', (.62, .64, .7), .25), bev=.1)
    cyl((0, 0, 1.04), .39, .04, mat('redsoup', (.9, .2, .1), .15), bev=.3)
    for i in range(8): sph((math.cos(i) * .25, math.sin(i * 1.7) * .25, 1.07), (.05, .05, .04), mat('meat', (.75, .42, .35)))
    tor((0, 0, 1.05), .42, .03, mat('pot', (.62, .64, .7), .25))
def crate():
    box((0, 0, .35), (.8, .8, .7), mat('box', (1, .75, .3), .5), rnd=.2)
    box((0, 0, .35), (.82, .12, .72), mat('tape', (1, .35, .45)), rnd=.3, sub=0)
    box((0, 0, .35), (.12, .82, .72), mat('tape', (1, .35, .45)), rnd=.3, sub=0)
    sph((0, 0, .75), (.15, .15, .08), mat('bow', (1, .35, .45)))
def turntable():
    cyl((0, 0, .12), 2.3, .24, mat('lazy', (.95, .92, .85), .25), v=64, bev=.2)
    tor((0, 0, .24), 2.25, .05, mat('rim', (1, .55, .7)))
    for i in range(6): cyl((math.cos(i * 1.047) * 1.5, math.sin(i * 1.047) * 1.5, .26), .32, .04, mat('plate', (1, 1, 1), .2), v=40)
def mentos():
    cyl((0, 0, .7), .28, 1.4, mat('cola', (.35, .15, .08), .1), r2=.22)
    cyl((0, 0, 1.5), .12, .25, mat('cola', (.35, .15, .08), .1)); cyl((0, 0, 1.66), .13, .06, mat('capred', (.95, .15, .15)))
    cyl((0, 0, .8), .285, .3, mat('label', (.95, .15, .15)), r2=.255)
    for i in range(10): cyl((math.cos(i) * .55, math.sin(i * 1.3) * .55, .05 + (i % 3) * .07), .12, .06, mat('mentos', (1, 1, 1), .4))
def plate():
    cyl((0, 0, .02), .35, .03, mat('plate', (1, 1, 1), .2), v=40)
    for i in range(7): sph((random.uniform(-.2, .2), random.uniform(-.2, .2), .05), (.04, .03, .02), mat('crumb', [(1, .6, .7), (.85, .55, .25), (1, .9, .3)][i % 3]))

# ------------------------------------------------------------------ unlockable hats
def takoyaki():
    cyl((0, 0, .05), .42, .08, mat('boat', (.85, .7, .45)))
    for x, y in ((-.15, -.1), (.15, -.1), (0, .14)):
        sph((x, y, .2), (.15, .15, .14), mat('tako', (.88, .58, .3), .5))
        sph((x, y, .33), (.1, .1, .02), mat('sauce', (.35, .18, .08), .2))
        box((x, y, .36), (.12, .02, .01), mat('mayo', (1, .98, .9)), rnd=.5, sub=0)
def xiaolongbao():
    cyl((0, 0, .08), .45, .16, mat('steamer', (.86, .72, .52), .5))
    sph((0, 0, .3), (.3, .3, .2), mat('bao', (.99, .96, .9), .5))
    for i in range(12): a = i / 12 * 6.283; sph((math.cos(a) * .12, math.sin(a) * .12, .46), (.035, .035, .06), mat('bao', (.99, .96, .9), .5))
def pizza():
    bpy.ops.mesh.primitive_cylinder_add(vertices=3, radius=.45, depth=.06, location=(0, 0, .05)); fin(bpy.context.object, mat('cheese', (1, .8, .2), .4), .01)
    bpy.ops.mesh.primitive_cylinder_add(vertices=3, radius=.48, depth=.04, location=(0, 0, .02)); fin(bpy.context.object, mat('crust', (.85, .55, .25), .5), .01)
    for p in ((.05, -.05), (-.12, .08), (.15, .12), (-.02, .2)): cyl((p[0], p[1], .09), .06, .02, mat('pepperoni', (.85, .2, .15)))

ITEMS = {'Milk': milk, 'Tea': tea, 'Boba': boba, 'Flour': flour, 'Egg': egg, 'Chili': chili, 'HotpotBase': hotpotbase, 'Bread': bread,
         'Cheese': cheese, 'Ice': ice, 'Sugar': sugar, 'Rice': rice, 'Nori': nori,
         'MilkTeaMachine': milkteamachine, 'Oven': oven, 'Freezer': freezer, 'HotpotStation': hotpotstation, 'Crate': crate,
         'Turntable': turntable, 'Mentos': mentos, 'Plate': plate}
HATS_ = {'Takoyaki': takoyaki, 'Xiaolongbao': xiaolongbao, 'Pizza': pizza}
random.seed(4)
for name, fn in list(ITEMS.items()) + [('Hat_' + k, v) for k, v in HATS_.items()]:
    bpy.ops.wm.read_factory_settings(use_empty=True); _m.clear()
    ROOT = bpy.data.objects.new(name, None); bpy.context.collection.objects.link(ROOT)
    fn(); fallguys_style.apply(ROOT, bevel_mul=1.2, s_mul=1.05)
    p = os.path.join(HATS if name.startswith('Hat_') else OUT, name + '.glb')
    bpy.ops.export_scene.gltf(filepath=p, export_format='GLB', export_apply=True, export_yup=True); print('EXPORTED', name, os.path.getsize(p))
print('KITCHEN_DONE')
