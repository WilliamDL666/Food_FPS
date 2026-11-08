# Blender (5.x) headless script: Fall Guys–style jelly-bean character on the Quaternius Toon Shooter rig.
# Keeps the skeleton, all 17 animations and the hand-held gun nodes, replaces the soldier body with a bean.
# Also exports six food hats (one per hero).
# Run:  blender.exe -b -P Tools/blender_bean_character.py -- <chars_out_dir> <hats_out_dir>
import bpy, bmesh, math, sys, os
from mathutils import Vector
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fallguys_style

args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = args[0] if args else os.getcwd()
HATS = args[1] if len(args) > 1 else OUT
SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Assets', 'Resources', 'Toon', 'Chars', 'Character_Soldier.gltf')
os.makedirs(OUT, exist_ok=True); os.makedirs(HATS, exist_ok=True)

def lin(c): return tuple((x / 12.92) if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)
_m = {}
def mat(name, rgb, rough=.35):
    if name in _m: return _m[name]
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes.get('Principled BSDF'); b.inputs['Base Color'].default_value = (*lin(rgb), 1); b.inputs['Roughness'].default_value = rough
    _m[name] = m; return m

def smooth(o, sub=1):
    for p in o.data.polygons: p.use_smooth = True
    if sub:
        md = o.modifiers.new('sub', 'SUBSURF'); md.levels = md.render_levels = sub
def sphere(name, loc, scale, m, seg=32):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=seg // 2, radius=1, location=loc)
    o = bpy.context.object; o.name = name; o.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    o.data.materials.append(m); smooth(o, 0); return o
def capsule(name, a, b, r, m):
    a, b = Vector(a), Vector(b); d = b - a
    bpy.ops.mesh.primitive_cylinder_add(vertices=20, radius=r, depth=d.length, location=(a + b) / 2)
    o = bpy.context.object; o.name = name
    o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d.normalized())
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    caps = []
    for end in (a, b):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=20, ring_count=10, radius=r, location=end); caps.append(bpy.context.object)
    o = join([o] + caps, name)
    o.data.materials.clear(); o.data.materials.append(m); smooth(o, 0); return o

def join(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]; bpy.ops.object.join()
    o = bpy.context.object; o.name = name; return o

def weight(o, arm, fn):
    """fn(world_co) -> {bone: w}"""
    for v in o.data.vertices:
        ws = fn(o.matrix_world @ v.co); tot = sum(ws.values()) or 1
        for bn, w in ws.items():
            g = o.vertex_groups.get(bn) or o.vertex_groups.new(name=bn)
            g.add([v.index], w / tot, 'REPLACE')
    o.parent = arm
    md = o.modifiers.new('Armature', 'ARMATURE'); md.object = arm

def ramp(z, z0, z1): return max(0.0, min(1.0, (z - z0) / (z1 - z0)))

# ------------------------------------------------------------------ bean character
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
for n in ('Body', 'Head', 'ShoulderPad.L', 'ShoulderPad.R', 'Icosphere'):
    o = bpy.data.objects.get(n)
    if o: bpy.data.objects.remove(o, do_unlink=True)
bpy.context.view_layer.update()

MAIN = mat('Character_Main', (.4, .6, 1.0), .3)       # team colour (recoloured in game)
FACE = mat('Face', (1.0, .97, .93), .35)
BLACK = mat('Black', (.04, .04, .05), .2)
GLOVE = mat('Glove', (1.0, 1.0, 1.0), .4)
PANTS = mat('Pants', (.3, .3, .4), .4)
SHOE = mat('Shoes', (.15, .15, .18), .35)
BLUSH = mat('Blush', (1.0, .6, .65), .5)

body = sphere('Body', (0, 0, 1.12), (.44, .39, .8), MAIN, 48)
face = sphere('Face', (0, -.3, 1.43), (.29, .1, .22), FACE, 32)
eyes = [sphere('Eye', (x, -.385, 1.47), (.04, .025, .058), BLACK, 16) for x in (-.09, .09)]
shine = [sphere('Shine', (x + .012, -.405, 1.49), (.012, .006, .016), FACE, 12) for x in (-.09, .09)]
blush = [sphere('Blush', (x, -.37, 1.38), (.045, .015, .025), BLUSH, 12) for x in (-.17, .17)]
smile = sphere('Smile', (0, -.392, 1.37), (.05, .012, .014), BLACK, 16)
bodyparts = join([body, face] + eyes + shine + blush + [smile], 'Body')
weight(bodyparts, arm, lambda c: {'Hips': 1 - ramp(c.z, .7, 1.0), 'Abdomen': max(0, 1 - abs(c.z - 1.0) / .25), 'Torso': max(0, 1 - abs(c.z - 1.3) / .25), 'Head': ramp(c.z, 1.4, 1.65)})
smooth(bodyparts, 1)

cap = sphere('Head', (0, 0, 1.12), (.452, .402, .812), MAIN, 48)      # hero-coloured crown that follows the bean's curve ("Head" → hero colour in game)
bm = bmesh.new(); bm.from_mesh(cap.data)
bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < 1.66 - 1.12], context='VERTS'); bm.to_mesh(cap.data); bm.free()
weight(cap, arm, lambda c: {'Head': 1})

arms = []
for s in (1, -1):
    sd = 'L' if s > 0 else 'R'
    a = capsule('Arm.' + sd, (s * .3, .02, 1.39), (s * .93, .02, 1.395), .085, MAIN)
    weight(a, arm, lambda c, sd=sd: {'UpperArm.' + sd: 1 - ramp(abs(c.x), .47, .6), 'LowerArm.' + sd: ramp(abs(c.x), .47, .6)})
    h = sphere('Hand.' + sd, (s * 1.03, .0, 1.395), (.12, .11, .11), GLOVE, 24)
    th = sphere('Thumb.' + sd, (s * 1.0, -.1, 1.41), (.05, .05, .045), GLOVE, 16)
    hand = join([h, th], 'Hand.' + sd); weight(hand, arm, lambda c, sd=sd: {'LowerArm.' + sd: 1})
    leg = capsule('Leg.' + sd, (s * .18, .02, .55), (s * .187, .02, .17), .1, PANTS)
    weight(leg, arm, lambda c, sd=sd: {'UpperLeg.' + sd: ramp(c.z, .45, .6), 'LowerLeg.' + sd: 1 - ramp(c.z, .45, .6)})
    shoe = sphere('Shoe.' + sd, (s * .188, -.05, .07), (.12, .17, .085), SHOE, 24)
    weight(shoe, arm, lambda c, sd=sd: {'Foot.' + sd: 1})
    for o in (a, hand, leg, shoe): smooth(o, 1)

fallguys_style.apply(None, bevel_mul=1.0, s_mul=1.0)
path = os.path.join(OUT, 'Character_Bean.glb')
bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', export_yup=True, export_apply=True, export_animations=True, export_animation_mode='ACTIONS', export_skins=True)
print('EXPORTED', path, os.path.getsize(path))

# ------------------------------------------------------------------ food hats (origin = bottom centre, ~0.8 wide in rig units)
def hat_scene(name):
    bpy.ops.wm.read_factory_settings(use_empty=True); _m.clear()
    root = bpy.data.objects.new('Hat_' + name, None); bpy.context.collection.objects.link(root); return root
def part(o, root, sub=1):
    o.parent = root; smooth(o, sub); return o
def cyl(loc, r, d, m, r2=None):
    if r2 is None: bpy.ops.mesh.primitive_cylinder_add(vertices=40, radius=r, depth=d, location=loc)
    else: bpy.ops.mesh.primitive_cone_add(vertices=40, radius1=r, radius2=r2, depth=d, location=loc)
    o = bpy.context.object; o.data.materials.append(m)
    md = o.modifiers.new('b', 'BEVEL'); md.width = min(r, d) * .35; md.segments = 4; return o
def tor(loc, R, r, m):
    bpy.ops.mesh.primitive_torus_add(major_radius=R, minor_radius=r, location=loc); o = bpy.context.object; o.data.materials.append(m); return o
def hat_export(root, name):
    fallguys_style.apply(root, bevel_mul=1.2, s_mul=1.05)
    p = os.path.join(HATS, 'Hat_' + name + '.glb')
    bpy.ops.export_scene.gltf(filepath=p, export_format='GLB', export_apply=True, export_yup=True); print('EXPORTED', p, os.path.getsize(p))

def burger():
    r = hat_scene('Burger')
    part(sphere('bun', (0, 0, .06), (.36, .36, .1), mat('bun', (.9, .58, .28)), 32), r)
    part(cyl((0, 0, .15), .4, .07, mat('patty', (.4, .22, .13), .6)), r)
    part(cyl((0, 0, .2), .42, .02, mat('cheese', (1, .8, .2)), ), r)
    part(cyl((0, 0, .22), .4, .03, mat('lettuce', (.45, .85, .3))), r)
    part(sphere('top', (0, 0, .25), (.38, .38, .2), mat('bun', (.9, .58, .28)), 32), r)
    for i in range(10):
        a = i * 2.4; part(sphere('seed', (math.cos(a) * .2 * (i % 3 + 1) / 3, math.sin(a) * .2 * (i % 3 + 1) / 3, .44 - .02 * (i % 3)), (.025, .015, .01), mat('seed', (1, .96, .85)), 8), r, 0)
    hat_export(r, 'Burger')
def onigiri():
    r = hat_scene('Onigiri')
    bpy.ops.mesh.primitive_cone_add(vertices=3, radius1=.42, radius2=.05, depth=.5, location=(0, 0, .25)); o = bpy.context.object; o.data.materials.append(mat('rice', (.99, .99, .97), .6))
    md = o.modifiers.new('b', 'BEVEL'); md.width = .12; md.segments = 6; part(o, r, 2)
    part(cyl((0, -.0, .08), .26, .16, mat('nori', (.1, .16, .12), .5)), r)
    hat_export(r, 'Onigiri')
def pudding():
    r = hat_scene('Pudding')
    part(cyl((0, 0, .16), .36, .32, mat('custard', (1, .85, .4), .25), r2=.3), r)
    part(cyl((0, 0, .34), .3, .06, mat('caramel', (.6, .3, .08), .15)), r)
    for i in range(8): a = i / 8 * 6.283; part(sphere('drip', (math.cos(a) * .31, math.sin(a) * .31, .29), (.05, .05, .08), mat('caramel', (.6, .3, .08), .15), 12), r, 0)
    part(sphere('cream', (0, 0, .41), (.1, .1, .08), mat('cream', (1, .98, .95)), 16), r)
    part(sphere('cherry', (0, 0, .5), (.06, .06, .06), mat('cherry', (.85, .05, .12), .15), 16), r)
    hat_export(r, 'Pudding')
def sushi():
    r = hat_scene('Sushi')
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, .1)); o = bpy.context.object; o.scale = (.62, .36, .2); bpy.ops.object.transform_apply(scale=True)
    o.data.materials.append(mat('rice', (.99, .99, .97), .6)); md = o.modifiers.new('b', 'BEVEL'); md.width = .07; md.segments = 5; part(o, r, 1)
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, .24)); o = bpy.context.object; o.scale = (.7, .42, .1); bpy.ops.object.transform_apply(scale=True)
    o.data.materials.append(mat('salmon', (1, .52, .32), .25)); md = o.modifiers.new('b', 'BEVEL'); md.width = .04; md.segments = 5; part(o, r, 1)
    for x in (-.18, 0, .18):
        bpy.ops.mesh.primitive_cube_add(size=1, location=(x, 0, .25)); o = bpy.context.object; o.scale = (.03, .43, .1); bpy.ops.object.transform_apply(scale=True)
        o.data.materials.append(mat('stripe', (1, .9, .85), .3)); part(o, r, 0)
    hat_export(r, 'Sushi')
def cupcake():
    r = hat_scene('Cupcake')
    part(cyl((0, 0, .1), .3, .2, mat('liner', (.95, .3, .45), .4), r2=.36), r)
    for i in range(3): part(tor((0, 0, .24 + i * .08), .3 * (.75 ** i), .1 * (.75 ** i), mat('frost', (.55, .9, 1.0), .3)), r)
    part(cyl((0, 0, .55), .03, .22, mat('candle', (1, .9, .3))), r)
    part(sphere('flame', (0, 0, .7), (.035, .035, .06), mat('flame', (1, .6, .15), .2), 12), r, 0)
    hat_export(r, 'Cupcake')
def donut():
    r = hat_scene('Donut')
    part(tor((0, 0, .12), .26, .14, mat('dough', (.92, .66, .38), .5)), r)
    part(tor((0, 0, .17), .26, .12, mat('icing', (.75, .4, 1.0), .25)), r)
    for i in range(14): a = i * 0.45; part(sphere('spr', (math.cos(a) * (.26 + .06 * math.sin(i)), math.sin(a) * (.26 + .06 * math.sin(i)), .28), (.03, .01, .01), mat('spr%d' % (i % 4), [(1, .35, .5), (.4, .8, 1), (1, .9, .3), (.5, 1, .6)][i % 4], .25), 8), r, 0)
    hat_export(r, 'Donut')
for fn in (burger, onigiri, pudding, sushi, cupcake, donut): fn()
print('BEAN_DONE')
