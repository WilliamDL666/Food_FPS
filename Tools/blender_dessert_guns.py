# Blender (5.x) headless script: builds the 7 dessert-style guns for 霓城封锁 and exports one .glb each.
# Run:  blender.exe -b -P Tools/blender_dessert_guns.py -- <output_dir>
# Convention: barrel along -Y, up +Z, pistol grip at the origin, units = metres.
# Each gun has empties "Muzzle" and "Grip" so the game can orient it.
import bpy, bmesh, math, random, sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fallguys_style
from mathutils import Vector

OUT = sys.argv[sys.argv.index('--') + 1] if '--' in sys.argv else os.path.join(os.getcwd(), 'dessert_out')
os.makedirs(OUT, exist_ok=True)

PALETTE = {
    'choco': (0.36, 0.20, 0.12), 'darkchoco': (0.20, 0.11, 0.07), 'wafer': (0.93, 0.74, 0.45), 'biscuit': (0.85, 0.60, 0.32),
    'cream': (1.0, 0.97, 0.90), 'pink': (1.0, 0.60, 0.75), 'mint': (0.55, 0.93, 0.80), 'lemon': (1.0, 0.88, 0.42),
    'berry': (0.68, 0.32, 0.86), 'candyred': (0.93, 0.15, 0.22), 'white': (0.98, 0.98, 1.0), 'sky': (0.52, 0.80, 1.0),
    'cherry': (0.82, 0.03, 0.10), 'flame': (1.0, 0.68, 0.22), 'icing': (1.0, 0.45, 0.68),
}
SPRINKLES = [(1, .35, .5), (.4, .8, 1), (1, .9, .3), (.5, 1, .6), (.8, .5, 1), (1, 1, 1)]

def srgb_to_lin(c): return tuple((x / 12.92) if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)
_mats = {}
def mat(key, rgb=None, rough=0.42, emit=0.0):
    if key in _mats: return _mats[key]
    rgb = rgb or PALETTE[key]
    m = bpy.data.materials.new('Dessert_' + key); m.use_nodes = True
    b = m.node_tree.nodes.get('Principled BSDF')
    b.inputs['Base Color'].default_value = (*srgb_to_lin(rgb), 1)
    b.inputs['Roughness'].default_value = rough
    if emit > 0:
        b.inputs['Emission Color'].default_value = (*srgb_to_lin(rgb), 1); b.inputs['Emission Strength'].default_value = emit
    _mats[key] = m; return m

def finish(o, m, bevel=0.0, subdiv=0, smooth=True):
    o.data.materials.clear(); o.data.materials.append(m)
    if bevel > 0:
        md = o.modifiers.new('bevel', 'BEVEL'); md.width = bevel; md.segments = 3; md.limit_method = 'NONE'
    if subdiv > 0:
        md = o.modifiers.new('sub', 'SUBSURF'); md.levels = subdiv; md.render_levels = subdiv
    if smooth:
        for p in o.data.polygons: p.use_smooth = True
    o.parent = ROOT
    return o

def rbox(loc, size, m, round_=0.35, subdiv=1, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=[math.radians(a) for a in rot])
    o = bpy.context.object; o.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(o, m, bevel=min(size) * round_ * 0.5, subdiv=subdiv)

def cyl(loc, r, depth, m, axis='Y', round_=0.3, verts=32, rot=None):
    r_ = {'X': (0, math.pi / 2, 0), 'Y': (math.pi / 2, 0, 0), 'Z': (0, 0, 0)}[axis] if rot is None else [math.radians(a) for a in rot]
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, location=loc, rotation=r_)
    return finish(bpy.context.object, m, bevel=min(r, depth) * round_)

def sphere(loc, scale, m, seg=24):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=seg // 2, radius=1, location=loc)
    o = bpy.context.object; o.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(o, m)

def torus(loc, R, r, m, rot=(0, 0, 0), seg=40, minor_seg=16):
    bpy.ops.mesh.primitive_torus_add(major_radius=R, minor_radius=r, major_segments=seg, minor_segments=minor_seg, location=loc, rotation=[math.radians(a) for a in rot])
    return finish(bpy.context.object, m)

def cone(loc, r1, r2, depth, m, rot=(0, 0, 0), verts=32):
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r1, radius2=r2, depth=depth, location=loc, rotation=[math.radians(a) for a in rot])
    return finish(bpy.context.object, m, bevel=min(r1, depth) * 0.08)

def sprinkles(center, half, count, seed, on_surface=None):
    rnd = random.Random(seed)
    for i in range(count):
        p = Vector(center) + Vector(((rnd.random() * 2 - 1) * half[0], (rnd.random() * 2 - 1) * half[1], (rnd.random() * 2 - 1) * half[2]))
        bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.0045, depth=0.016, location=p,
                                            rotation=(rnd.random() * math.pi, rnd.random() * math.pi, 0))
        finish(bpy.context.object, mat('spr%d' % (i % 6), SPRINKLES[i % 6], rough=0.25), bevel=0.002)

def frosting(base, R, layers, m, axis='Y', step=0.022, shrink=0.72):
    # stacked, slightly twisted tori = piped frosting swirl, pointing along +axis
    d = {'Y': Vector((0, -1, 0)), 'Z': Vector((0, 0, 1))}[axis]
    rot = (90, 0, 0) if axis == 'Y' else (0, 0, 0)
    r = R
    for i in range(layers):
        torus(Vector(base) + d * (i * step), r, r * 0.42, m, rot=rot, seg=32, minor_seg=12)
        r *= shrink
    sphere(Vector(base) + d * (layers * step), (r * .9, r * .9, r * .9), m)

def stripes(start_y, length, r, m1, m2, bands, z=0.0):
    seg = length / bands
    for i in range(bands):
        cyl((0, start_y - seg * (i + 0.5), z), r, seg * 1.02, m1 if i % 2 == 0 else m2, round_=0.08)

def grip(m, z=-0.05, y=0.01):
    rbox((0, y, z), (0.032, 0.045, 0.1), m, round_=0.6, rot=(-12, 0, 0))

def empties(muzzle_y, muzzle_z):
    for name, loc in (('Muzzle', (0, muzzle_y, muzzle_z)), ('Grip', (0, 0, 0))):
        e = bpy.data.objects.new(name, None); e.location = loc; bpy.context.collection.objects.link(e); e.parent = ROOT

# ------------------------------------------------------------------ guns
def lollipop():
    for y in (-0.03, -0.075):
        cyl((0, y, 0.02), 0.042, 0.022, mat('pink'), round_=0.45)            # macaron shells
    cyl((0, -0.053, 0.02), 0.037, 0.016, mat('cream'), round_=0.4)           # filling
    stripes(-0.1, 0.12, 0.008, mat('candyred'), mat('white'), 6, z=0.02)
    torus((0, -0.24, 0.02), 0.026, 0.012, mat('mint'), rot=(90, 0, 0))     # swirl lollipop
    torus((0, -0.24, 0.02), 0.014, 0.008, mat('lemon'), rot=(90, 0, 0))
    sphere((0, -0.24, 0.02), (0.008, 0.008, 0.008), mat('pink'))
    grip(mat('biscuit'), z=-0.035, y=-0.05); empties(-0.27, 0.02)

def donut():
    rbox((0, -0.08, 0.025), (0.06, 0.24, 0.06), mat('choco'), round_=0.3)
    for i in range(4): rbox((0, 0.015 - i * 0.062, 0.058), (0.05, 0.05, 0.01), mat('darkchoco'), round_=0.5)
    for i in range(6): cyl((0, -0.22 - i * 0.025, 0.03), 0.015, 0.024, mat('biscuit' if i % 2 == 0 else 'wafer'), verts=10, round_=0.15)   # churro ridges
    torus((0, -0.1, -0.05), 0.04, 0.021, mat('biscuit'), rot=(0, 90, 0))        # donut drum
    torus((0.006, -0.1, -0.05), 0.04, 0.017, mat('icing'), rot=(0, 90, 0))      # glaze
    sprinkles((0.024, -0.1, -0.05), (0.004, 0.045, 0.045), 18, 3)
    rbox((0, 0.09, 0.012), (0.04, 0.1, 0.05), mat('wafer'), round_=0.4)
    grip(mat('darkchoco')); empties(-0.38, 0.03)

def icecream():
    rbox((0, -0.1, 0.025), (0.055, 0.3, 0.055), mat('wafer'), round_=0.25)
    for i in range(5): rbox((0, 0.03 - i * 0.06, 0.025), (0.058, 0.007, 0.058), mat('biscuit'), round_=0.3)
    rbox((0, -0.36, 0.028), (0.045, 0.2, 0.045), mat('sky'), round_=0.9)                 # popsicle barrel
    rbox((0, -0.32, 0.05), (0.047, 0.12, 0.008), mat('pink'), round_=0.9)                 # dipped coating
    cone((0, -0.14, -0.09), 0.033, 0.004, 0.11, mat('wafer'), rot=(180, 0, 0))            # cone magazine (tip down)
    sphere((0, -0.14, -0.03), (0.036, 0.036, 0.03), mat('mint'))                           # scoop
    sphere((0, -0.14, -0.004), (0.011, 0.011, 0.011), mat('cherry'))
    sprinkles((0, -0.14, -0.02), (0.028, 0.028, 0.012), 10, 5)
    rbox((0, 0.13, 0.01), (0.045, 0.14, 0.07), mat('biscuit'), round_=0.35)
    cyl((0, -0.08, 0.066), 0.012, 0.05, mat('pink'), round_=0.4)                          # macaron sight
    grip(mat('biscuit')); empties(-0.47, 0.028)

def cake():
    layers = ['pink', 'cream', 'choco', 'cream', 'lemon']
    for i, k in enumerate(layers): cyl((0, -0.02 - i * 0.05, 0.03), 0.065, 0.05, mat(k), round_=0.12 if k == 'cream' else 0.25)
    for i in range(12):
        a = i / 12 * math.tau
        sphere((math.cos(a) * 0.066, -0.25, 0.03 + math.sin(a) * 0.066), (0.016, 0.016, 0.016), mat('white'))   # cream piping ring
    stripes(-0.26, 0.26, 0.012, mat('mint'), mat('white'), 8, z=0.03)                         # candle barrel
    sphere((0, -0.54, 0.03), (0.011, 0.018, 0.011), mat('flame', rough=0.3, emit=3.0))
    cyl((0, -0.12, -0.07), 0.045, 0.035, mat('berry'), axis='Z', round_=0.2)                  # cupcake drum
    frosting((0, -0.12, -0.045), 0.04, 3, mat('pink'), axis='Z', step=0.014)
    sprinkles((0, -0.12, 0.098), (0.035, 0.1, 0.004), 20, 7)
    rbox((0, 0.1, 0.02), (0.05, 0.14, 0.08), mat('biscuit'), round_=0.35)
    grip(mat('choco')); empties(-0.55, 0.03)

def candycane():
    rbox((0, -0.08, 0.02), (0.05, 0.26, 0.06), mat('biscuit'), round_=0.3)
    stripes(-0.2, 0.6, 0.014, mat('candyred'), mat('white'), 16, z=0.03)
    torus((0, 0.085, -0.005), 0.05, 0.016, mat('candyred'), rot=(0, 90, 0))                # the cane hook as stock, touching the body
    cyl((0, -0.1, 0.1), 0.025, 0.2, mat('pink'), round_=0.25)                               # swiss-roll scope
    for y in (0.0, -0.2): torus((0, y, 0.1), 0.017, 0.004, mat('cream'), rot=(90, 0, 0), seg=24)
    rbox((0, -0.1, 0.066), (0.015, 0.015, 0.03), mat('darkchoco'), round_=0.5)
    grip(mat('biscuit')); empties(-0.82, 0.03)

def cupcake():
    rbox((0, -0.08, 0.02), (0.06, 0.24, 0.06), mat('biscuit'), round_=0.3)
    for i in range(3): cyl((0, -0.02 - i * 0.07, 0.023), 0.032, 0.008, mat('choco'), round_=0.3)
    cone((0, -0.27, 0.03), 0.02, 0.075, 0.14, mat('lemon'), rot=(90, 0, 0), verts=24)       # pleated liner flaring forward
    frosting((0, -0.35, 0.03), 0.07, 4, mat('pink'), axis='Y', step=0.02)
    sphere((0, -0.43, 0.06), (0.018, 0.018, 0.018), mat('cherry'))
    sprinkles((0, -0.37, 0.03), (0.05, 0.02, 0.05), 18, 9)
    rbox((0, 0.1, 0.01), (0.045, 0.13, 0.07), mat('wafer'), round_=0.35)
    grip(mat('choco')); empties(-0.44, 0.03)

def chocobar():
    for x in range(2):
        for zz in range(5):
            rbox((0, 0.02 - zz * 0.055, 0.005 + x * 0.042), (0.058, 0.05, 0.038), mat('choco' if (zz % 2 == x) else 'darkchoco'), round_=0.35)
    for i in (-1, 0, 1):
        cyl((i * 0.016, -0.38, 0.03), 0.006, 0.26, mat('biscuit'), round_=0.4, verts=12)   # pocky sticks
        cyl((i * 0.016, -0.42, 0.03), 0.0075, 0.18, mat('pink' if i == 0 else 'choco'), round_=0.4, verts=12)
    rbox((0, -0.12, -0.05), (0.035, 0.05, 0.08), mat('wafer'), round_=0.4, rot=(10, 0, 0))
    rbox((0, 0.12, 0.01), (0.045, 0.13, 0.07), mat('wafer'), round_=0.35)
    grip(mat('darkchoco')); empties(-0.52, 0.03)

GUNS = {'P9': lollipop, 'SMG7': donut, 'AR9': icecream, 'LMG2': cake, 'DMR3': candycane, 'FS12': cupcake, 'BR5': chocobar}
for name, build in GUNS.items():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    _mats.clear()
    ROOT = bpy.data.objects.new('Dessert_' + name, None); bpy.context.collection.objects.link(ROOT)
    build()
    path = os.path.join(OUT, name + '.glb')
    fallguys_style.apply(ROOT)
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', export_apply=True, export_yup=True, use_selection=False)
    print('EXPORTED', name, os.path.getsize(path))
print('DESSERT_DONE')
