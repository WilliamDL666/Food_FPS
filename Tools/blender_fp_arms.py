# Blender (5.x) headless script: first-person arms for 霓城封锁 (V22 proposal, 3 looks to choose from).
# Run:  blender.exe -b -P Tools/blender_fp_arms.py -- <glb_out_dir> [preview_dir] [variant ...]
#   e.g. blender.exe -b -P Tools/blender_fp_arms.py -- Assets/Resources/Toon/FPArms "E:/Claude工作区/预览/FPArms"
#
# Space = the first-person gun pivot (the GameObject DessertGuns.Create returns), NOT the 0.62-scaled gun model:
#   metres, barrel along -Y, up +Z, pistol grip at the origin (same convention as blender_food_guns.py).
#   Put the glb under the gun pivot at identity and the hands sit on a 0.62-scaled AR-9 (grip + parts.foregrip).
# Hierarchy per arm (R = right / L = left; R is on the viewer's right = Blender -X = Unity +X):
#   Arm_R   (empty at the shoulder end of the upper-arm stub)
#     UpperArm_R (mesh)            short stub, mostly off-screen
#     Forearm_R  (mesh, origin = elbow)
#       Hand_R   (empty, origin = wrist; local -Y = fingers, local X = axis through the fist)
#         Glove_R (mesh)
#         Palm_R  (empty, the point that should sit on the grip / handguard)
# Materials (fixed names, no .001 suffix): FPArm_Skin / FPArm_Sleeve → team colour, FPArm_Cuff → hero tint,
#   FPArm_Glove (white), FPArm_Chef (chef-jacket white), FPArm_Line (glove stitching).
import bpy, bmesh, math, sys, os, re
from mathutils import Vector, Matrix
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fallguys_style

HERE = os.path.dirname(os.path.abspath(__file__))
args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = os.path.abspath(args[0]) if args else os.path.join(HERE, '..', 'Assets', 'Resources', 'Toon', 'FPArms')
PREVIEW = os.path.abspath(args[1]) if len(args) > 1 and args[1] != '-' else None
ONLY = args[2:] or ['Sweater']                  # V22: user picked 丙 甜心针织袖; pass names to rebuild the others
os.makedirs(OUT, exist_ok=True)
if PREVIEW: os.makedirs(PREVIEW, exist_ok=True)
GUN = os.path.join(HERE, '..', 'Assets', 'Resources', 'Toon', 'FoodGuns', os.environ.get('FPARMS_GUN', 'SMG7') + '.glb')

TEAM_BLUE = (.15, .62, 1.0)          # Config.Blue  (FPArm_Skin / FPArm_Sleeve default; recoloured in game)
TEAM_RED = (1.0, .22, .3)            # Config.Red   (used for the red-team preview only)
HERO_TINT = (1.0, .55, .2)           # 猎隼 tint    (FPArm_Cuff default; recoloured in game)

def lin(c): return tuple((x / 12.92) if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)
def U(x, y, z): return Vector((-x, -z, y))          # Unity (pivot space) → Blender
R = math.radians

_m = {}
def mat(name, rgb, rough=.35):
    if name in _m: return _m[name]
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes.get('Principled BSDF')
    b.inputs['Base Color'].default_value = (*lin(rgb), 1); b.inputs['Roughness'].default_value = rough
    _m[name] = m; return m

def new_obj(name, me):
    o = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(o); return o
def empty(name, mw, parent=None):
    e = bpy.data.objects.new(name, None); e.empty_display_size = .015; bpy.context.collection.objects.link(e)
    place(e, mw, parent); return e
def place(o, mw, parent):
    """set world matrix mw with a clean local transform under parent (no parent-inverse, glTF-friendly)"""
    o.parent = parent; o.matrix_parent_inverse = Matrix.Identity(4)
    o.matrix_basis = (parent.matrix_world.inverted() @ mw) if parent else mw
    bpy.context.view_layer.update()

def basis(d):
    d = d.normalized(); a = Vector((0, 0, 1)) if abs(d.z) < .9 else Vector((1, 0, 0))
    u = d.cross(a).normalized(); v = d.cross(u).normalized(); return d, u, v

def lathe(bm, m_index, origin, direction, prof, seg=16, rfun=None):
    """surface of revolution around origin + s*direction. prof = [(s, r), ...] with s increasing; r == 0 → pole.
    rfun(s, ang) → radius multiplier (folds, ribs)."""
    d, u, v = basis(direction); rings = []
    for s, r in prof:
        c = origin + d * s
        if r <= 1e-6: rings.append([bm.verts.new(c)]); continue
        ring = []
        for i in range(seg):
            a = i / seg * 2 * math.pi; k = rfun(s, a) if rfun else 1.0
            ring.append(bm.verts.new(c + (u * math.cos(a) + v * math.sin(a)) * r * k))
        rings.append(ring)
    for r0, r1 in zip(rings, rings[1:]):
        if len(r0) == 1 and len(r1) == 1: continue
        if len(r0) == 1:
            for i in range(seg): f = bm.faces.new((r0[0], r1[i], r1[(i + 1) % seg])); f.material_index = m_index
        elif len(r1) == 1:
            for i in range(seg): f = bm.faces.new((r0[(i + 1) % seg], r0[i], r1[0])); f.material_index = m_index
        else:
            for i in range(seg):
                f = bm.faces.new((r0[i], r1[i], r1[(i + 1) % seg], r0[(i + 1) % seg])); f.material_index = m_index

def capsule_prof(length, r0, r1, n_cap=4, n_mid=4, cap0=True, cap1=True):
    p = []
    if cap0:
        for i in range(n_cap): t = (math.pi / 2) * (1 - i / n_cap); p.append((-r0 * math.sin(t), r0 * math.cos(t)))
    for i in range(n_mid + 1): t = i / n_mid; p.append((length * t, r0 + (r1 - r0) * t))
    if cap1:
        for i in range(1, n_cap + 1): t = (math.pi / 2) * (i / n_cap); p.append((length + r1 * math.sin(t), r1 * math.cos(t)))
    return p

def ellipsoid(bm, m_index, center, radii, seg=16, rings=8, rot=None):
    rot = rot or Matrix.Identity(3); verts = []
    top = bm.verts.new(center + rot @ Vector((0, 0, radii[2]))); bot = bm.verts.new(center + rot @ Vector((0, 0, -radii[2])))
    for j in range(1, rings):
        t = math.pi * j / rings; row = []
        for i in range(seg):
            a = 2 * math.pi * i / seg
            row.append(bm.verts.new(center + rot @ Vector((radii[0] * math.sin(t) * math.cos(a), radii[1] * math.sin(t) * math.sin(a), radii[2] * math.cos(t)))))
        verts.append(row)
    for i in range(seg):
        bm.faces.new((top, verts[0][i], verts[0][(i + 1) % seg])).material_index = m_index
        bm.faces.new((bot, verts[-1][(i + 1) % seg], verts[-1][i])).material_index = m_index
    for r0, r1 in zip(verts, verts[1:]):
        for i in range(seg): bm.faces.new((r0[i], r1[i], r1[(i + 1) % seg], r0[(i + 1) % seg])).material_index = m_index

def torus(bm, m_index, center, axis, R0, r, seg=24, sides=8, rfun=None):
    d, u, v = basis(axis); grid = []
    for i in range(seg):
        a = 2 * math.pi * i / seg; radial = u * math.cos(a) + v * math.sin(a); row = []
        k = rfun(a) if rfun else 1.0
        for j in range(sides):
            b = 2 * math.pi * j / sides
            row.append(bm.verts.new(center + radial * (R0 + r * k * math.cos(b)) + d * (r * k * math.sin(b))))
        grid.append(row)
    for i in range(seg):
        for j in range(sides):
            a0, a1 = grid[i], grid[(i + 1) % seg]
            bm.faces.new((a0[j], a1[j], a1[(j + 1) % sides], a0[(j + 1) % sides])).material_index = m_index

def mesh_obj(name, build, mats, origin_world, parent=None, sub=0):
    """build(bm) creates geometry in world space; the object origin is moved to origin_world."""
    bm = bmesh.new(); build(bm)
    for vv in bm.verts: vv.co -= origin_world
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for m in mats: me.materials.append(m)
    for p in me.polygons: p.use_smooth = True
    o = new_obj(name, me); place(o, Matrix.Translation(origin_world), parent)
    if sub: md = o.modifiers.new('sub', 'SUBSURF'); md.levels = md.render_levels = sub
    return o

# ------------------------------------------------------------------ glove (local hand space: wrist at origin, fingers -Y, fist axis X, back of hand +Z)
GS = 1.1                                        # glove scale (user V22: 1.1 x the old Glove.glb look, ~7 cm wide)
K = 1.05                                        # arm thickness multiplier (user V22: ~0.8 x the first proposal, which was 1.3)
PALM_LOCAL = Vector((0, -.05, -.014)) * GS     # where the grip / handguard passes through the fist
def glove_geo(bm, gi, li, cuff_i, cuff_style):
    ellipsoid(bm, gi, Vector((0, -.04, .002)), (.033, .034, .025), 16, 8)                        # back of hand / palm
    for k, x in enumerate((-.0225, -.0075, .0075, .0225)):                                       # four curled fingers
        rr = .0098 if k in (1, 2) else .0088
        lathe(bm, gi, Vector((x, -.064, .016)), Vector((0, -.15, -1)), capsule_prof(.024, rr, rr * .95, 3, 2), 10)
        ellipsoid(bm, gi, Vector((x, -.056, -.022)), (rr * .95, rr * 1.15, rr * .85), 10, 5)     # fingertips tucked under
    lathe(bm, gi, Vector((.029, -.026, -.004)), Vector((.25, -1, -.55)), capsule_prof(.026, .0125, .0105, 3, 2), 10)   # thumb
    for x in (-.011, 0, .011):                                                                   # Mickey stitching on the back
        lathe(bm, li, Vector((x, -.022, .0262)), Vector((0, -1, .12)), capsule_prof(.024, .0022, .0022, 2, 1), 6)
    lathe(bm, gi, Vector((0, -.014, 0)), Vector((0, 1, 0)), [(0, .0245), (.012, .025), (.018, .0245)], 18)   # wrist of the glove
    if cuff_style == 'ring':                                                                     # slim colour ring on the glove cuff
        torus(bm, cuff_i, Vector((0, .003, 0)), Vector((0, 1, 0)), .0245, .0075, 22, 8)
    elif cuff_style == 'roll':                                                                   # fat rolled white cuff
        torus(bm, cuff_i, Vector((0, .004, 0)), Vector((0, 1, 0)), .025, .0105, 22, 8)
    bmesh.ops.scale(bm, vec=(GS, GS, GS), verts=bm.verts)

def hand_frame(fwd_u, axis_u):
    fwd = U(*fwd_u).normalized(); x = U(*axis_u)
    x = (x - fwd * x.dot(fwd)).normalized(); y = -fwd; z = x.cross(y)
    m = Matrix((x, y, z)).transposed(); return m

# ------------------------------------------------------------------ arm layout (pivot space, measured on a 0.62-scaled food gun)
#   right fist on the pistol grip, left fist under the handguard near parts.foregrip = (0, -.02, muzzleZ*.55)*.62
ARMS = {
    'R': dict(palm=U(0, -.018, .0),      hand_fwd=(-.65, .1, .72), hand_axis=(0, 1, -.21), fore_dir=(.62, -.55, -.56), fore_len=.26, up_dir=(.15, .75, -.65)),
    'L': dict(palm=U(-.004, -.022, .13), hand_fwd=(.45, .8, .35),  hand_axis=(0, 0, 1),    fore_dir=(-.42, -.72, -.55), fore_len=.30, up_dir=(-.35, .55, -.75)),
}
WR = .0245 * GS                                 # glove wrist radius -> forearm starts here

def build_arm(root, sd, v, M):
    a = ARMS[sd]
    rot = hand_frame(a['hand_fwd'], a['hand_axis'])
    wrist = a['palm'] - rot @ PALM_LOCAL
    fore_d = U(*a['fore_dir']).normalized()
    elbow = wrist + fore_d * a['fore_len']
    up_d = U(*a['up_dir']).normalized()
    shoulder = elbow + up_d * .12
    arm = empty('Arm_' + sd, Matrix.Translation(shoulder), root)
    v['upper'](sd, arm, elbow, shoulder, M)
    fore = v['fore'](sd, arm, wrist, elbow, M)
    hw = Matrix.Translation(wrist) @ rot.to_4x4()
    hand = empty('Hand_' + sd, hw, fore)
    mats = [M['glove'], M['line']] + ([M[v['glove_cuff_mat']]] if v['glove_cuff_mat'] != 'glove' else [])
    ci = 2 if v['glove_cuff_mat'] != 'glove' else 0
    g = mesh_obj('Glove_' + sd, lambda bm: glove_geo(bm, 0, 1, ci, v['glove_cuff']), mats, Vector((0, 0, 0)), None)
    place(g, hw, hand)
    empty('Palm_' + sd, hw @ Matrix.Translation(PALM_LOCAL), hand)
    return arm

def stub(sd, arm, elbow, shoulder, M, key, r0, r1, rfun=None):
    mesh_obj('UpperArm_' + sd, lambda bm: lathe(bm, 0, elbow, shoulder - elbow, capsule_prof((shoulder - elbow).length, r0, r1, 4, 2), 16, rfun), [M[key]], shoulder, arm)

# ---------------- look A: 豆子软胶臂 (bean jelly arm = same team colour as the body; hero-colour ring on the glove cuff)
def a_upper(sd, arm, elbow, shoulder, M): stub(sd, arm, elbow, shoulder, M, 'skin', .034 * K, .036 * K)
def a_fore(sd, arm, wrist, elbow, M):
    L = (elbow - wrist).length
    prof = capsule_prof(L, WR * .98, .034 * K, 4, 6)                                       # round wrist end hides the wrist bend
    bulge = lambda s, a: 1.0 + .07 * math.sin(math.pi * min(1, max(0, s / L)))               # soft "inflated" belly
    return mesh_obj('Forearm_' + sd, lambda bm: lathe(bm, 0, wrist, elbow - wrist, prof, 16, bulge), [M['skin']], elbow, arm)

# ---------------- look B: 卷袖厨师服 (rolled-up chef-jacket sleeve over a bare bean forearm, hero-colour piping + buttons)
def b_upper(sd, arm, elbow, shoulder, M): stub(sd, arm, elbow, shoulder, M, 'chef', .045 * K, .047 * K, lambda s, a: 1 + .05 * math.sin(3 * a + s * 40))
def b_fore(sd, arm, wrist, elbow, M):
    L = (elbow - wrist).length; d = (elbow - wrist).normalized(); roll_s = L * .26
    def geo(bm):
        lathe(bm, 0, wrist, d, [(-WR * .98 * math.cos(R(22.5 * i)), WR * .98 * math.sin(R(22.5 * i))) for i in range(4)] + [(roll_s * t, WR * .98 + (.03 * K - WR * .98) * t) for t in (0, .25, .5, .75, 1.0)] + [(roll_s + .01, .03 * K)], 16)
        # the rolled sleeve: two fat fabric rolls + piping, then the loose sleeve up to the elbow
        torus(bm, 1, wrist + d * (roll_s + .010 * K), d, .034 * K, .0135 * K, 22, 8, lambda a: 1 + .08 * math.sin(2 * a + .7) + .05 * math.sin(5 * a))
        torus(bm, 1, wrist + d * (roll_s + .034 * K), d, .036 * K, .0135 * K, 22, 8, lambda a: 1 + .08 * math.sin(2 * a + 2.3) + .05 * math.sin(4 * a + 1))
        torus(bm, 2, wrist + d * (roll_s + .022 * K), d, .0425 * K, .0045 * K, 26, 6)
        lathe(bm, 1, wrist, d, [(roll_s + .034 * K, .036 * K), (roll_s + .055 * K, .041 * K), (L, .045 * K), (L + .02, .045 * K)], 16,
              lambda s, a: 1 + .05 * math.sin(3 * a + s * 40))
        _, u, vv = basis(d)
        for ang in (1.55, 2.25):                                                                 # two macaron buttons on the roll
            n = (u * math.cos(ang) + vv * math.sin(ang)).normalized()
            ellipsoid(bm, 2, wrist + d * (roll_s + .022 * K) + n * .049 * K, (.008 * K, .008 * K, .005 * K), 10, 5, n.to_track_quat('Z', 'Y').to_matrix())
    return mesh_obj('Forearm_' + sd, geo, [M['skin'], M['chef'], M['cuff']], elbow, arm)

# ---------------- look C: 甜心针织袖 (team-colour knit sleeve over the whole forearm, hero-colour ribbed cuff + varsity stripes)
def c_upper(sd, arm, elbow, shoulder, M): stub(sd, arm, elbow, shoulder, M, 'sleeve', .043 * K, .045 * K)
def c_fore(sd, arm, wrist, elbow, M):
    L = (elbow - wrist).length; d = (elbow - wrist).normalized()
    def geo(bm):
        rib = lambda s, a: 1 + .07 * max(0.0, math.cos(12 * a)) ** 2                              # knit rib ridges
        lathe(bm, 1, wrist, d, [(-.004, 0), (-.004, WR * .95), (.0, WR * 1.1), (.014 * K, .032 * K), (.026 * K, .031 * K), (.032 * K, .028 * K)], 24, rib)
        fold = lambda s, a: 1 + .045 * math.sin(2 * a + s * 55) * min(1, s / .05)               # soft sleeve folds
        prof = [(.03 * K, .027 * K), (.042 * K, .034 * K), (.07, .038 * K), (.11, .040 * K), (.15, .041 * K), (.19, .042 * K), (L, .043 * K), (L + .02, .043 * K)]
        lathe(bm, 0, wrist, d, prof, 16, fold)
        for s0 in (L * .3, L * .3 + .026):                                                       # two varsity stripes
            lathe(bm, 1, wrist, d, [(s0, .0415 * K), (s0 + .002, .0438 * K), (s0 + .011, .0438 * K), (s0 + .013, .0415 * K)], 16, lambda s, a: 1 + .045 * math.sin(2 * a + s * 55))
    return mesh_obj('Forearm_' + sd, geo, [M['sleeve'], M['cuff']], elbow, arm)

VARIANTS = {
    'Bean':    dict(title='甲 豆子软胶臂', upper=a_upper, fore=a_fore, glove_cuff='ring', glove_cuff_mat='cuff'),
    'Chef':    dict(title='乙 卷袖厨师服', upper=b_upper, fore=b_fore, glove_cuff='roll', glove_cuff_mat='glove'),
    'Sweater': dict(title='丙 甜心针织袖', upper=c_upper, fore=c_fore, glove_cuff='roll', glove_cuff_mat='glove'),
}

def materials(team=TEAM_BLUE):
    return {'skin': mat('FPArm_Skin', team, .28), 'sleeve': mat('FPArm_Sleeve', team, .55), 'cuff': mat('FPArm_Cuff', HERO_TINT, .4),
            'glove': mat('FPArm_Glove', (1, 1, 1), .4), 'chef': mat('FPArm_Chef', (1.0, .98, .95), .6), 'line': mat('FPArm_Line', (.12, .12, .14), .5)}

def strip_suffixes():
    """V18 bug: Blender appends .001 to duplicate names and the game looks materials up by name."""
    for m in bpy.data.materials:
        base = re.sub(r'\.\d{3}$', '', m.name)
        if base != m.name:
            other = bpy.data.materials.get(base)
            if other is not None and other != m: other.name = base + '_old'
            m.name = base
    for o in bpy.data.objects:
        base = re.sub(r'\.\d{3}$', '', o.name)
        if base != o.name: o.name = base

def tri_count(objs):
    dg = bpy.context.evaluated_depsgraph_get(); n = 0
    for o in objs:
        if o.type != 'MESH': continue
        e = o.evaluated_get(dg); me = e.to_mesh(); me.calc_loop_triangles(); n += len(me.loop_triangles); e.to_mesh_clear()
    return n

def build_variant(name):
    bpy.ops.wm.read_factory_settings(use_empty=True); _m.clear()
    v = VARIANTS[name]; M = materials()
    root = bpy.data.objects.new('FPArms_' + name, None); bpy.context.collection.objects.link(root)
    arms = [build_arm(root, sd, v, M) for sd in ('R', 'L')]
    fallguys_style.apply(root, bevel_mul=1.0, s_mul=1.0)
    for o in bpy.data.objects:
        if o.type == 'MESH':
            for p in o.data.polygons: p.use_smooth = True
    strip_suffixes()
    for a in arms:
        print('FPARMS_TRIS', name, a.name, tri_count([a] + list(a.children_recursive)))
    bad = [m.name for m in bpy.data.materials if re.search(r'\.\d{3}$', m.name)]
    if bad: raise RuntimeError('material suffix left: %s' % bad)
    path = os.path.join(OUT, 'FPArms_' + name + '.glb')
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', export_apply=True, export_yup=True)
    print('EXPORTED', path, os.path.getsize(path))
    return path

# ------------------------------------------------------------------ first-person preview (mimics Fighter.BuildViewModel)
#   viewPivot = camera-local (.2, -.2, .42) at hip; ADS: viewAds = -sightLocal + (0, 0, .26), sightLocal = (0, top*.62, .05*.62)
#   gun model scaled 0.62 under the pivot; FOV 78 (vertical) at hip, 78 / adsZoom (~1.35) in ADS; renderers behind z < -.07 hidden.
def recolor(team):
    for m in bpy.data.materials:
        if m.name in ('FPArm_Skin', 'FPArm_Sleeve') and m.use_nodes:
            b = m.node_tree.nodes.get('Principled BSDF')
            if b: b.inputs['Base Color'].default_value = (*lin(team), 1)

def preview(name, arms_path, team=TEAM_BLUE, tag=''):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    before = set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=GUN)
    gun = [o for o in bpy.data.objects if o not in before]
    groot = [o for o in gun if o.parent is None][0]; groot.scale = (.62, .62, .62); bpy.context.view_layer.update()
    top = 0.0
    for o in gun:
        if o.type != 'MESH': continue
        cs = [o.matrix_world @ Vector(c) for c in o.bound_box]
        cy = sum(c.y for c in cs) / 8 / .62
        if cy > .07: o.hide_render = True                                         # first-person: stock hidden
        top = max(top, max(c.z for c in cs) / .62)
    top = min(max(top * .8, .04), .12)
    before = set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=arms_path)
    arms = [o for o in bpy.data.objects if o not in before]
    mats = sorted({s.material.name for o in arms if o.type == 'MESH' for s in o.material_slots if s.material})
    print('FPARMS_GLB', name, 'nodes:', sorted(o.name for o in arms), 'materials:', mats)
    if team != TEAM_BLUE: recolor(team)

    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_EEVEE' if 'BLENDER_EEVEE' in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items.keys() else 'BLENDER_EEVEE_NEXT'
    sc.render.resolution_x, sc.render.resolution_y = 1600, 900
    sc.view_settings.view_transform = 'Standard'
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
    bg = w.node_tree.nodes['Background']; bg.inputs[0].default_value = (*lin((.86, .78, .95)), 1); bg.inputs[1].default_value = 1.1
    # simple candy street so the arms read against something: floor + two walls
    def plane(loc, rot, size, rgb):
        bpy.ops.mesh.primitive_plane_add(size=size, location=loc, rotation=rot); p = bpy.context.object
        m = bpy.data.materials.new('bg'); m.use_nodes = True; m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (*lin(rgb), 1)
        m.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = .7; p.data.materials.append(m)
    plane((0, -6, -1.55), (0, 0, 0), 30, (.78, .72, .86))
    plane((3.5, -6, 1), (0, R(90), 0), 30, (1.0, .72, .62))
    plane((-3.5, -6, 1), (0, R(90), 0), 30, (.62, .74, 1.0))
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sc.collection.objects.link(sun)
    sun.data.energy = 3.2; sun.rotation_euler = (R(40), R(-15), R(25))
    fill = bpy.data.objects.new('fill', bpy.data.lights.new('fill', 'AREA')); sc.collection.objects.link(fill)
    fill.data.energy = 6; fill.data.size = 1; fill.location = (.3, .7, .5); fill.rotation_euler = (R(-60), 0, R(200))
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.sensor_fit = 'VERTICAL'; cam.data.clip_start = .02; cam.rotation_euler = (R(90), 0, R(180))
    # ADS: Fighter.FoodAim hides the gun (and anything parented to it) once adsK > .85 and draws the food scope instead,
    # so the last visible ADS frame is around adsK = .7 → pos = Lerp(hip, ads, SmoothStep(.7)), fov = Lerp(78, 78/zoom, .7)
    hip, ads, k = Vector((.2, -.2, .42)), Vector((0, -top * .62, .26 - .05 * .62)), .7
    cpos = -hip.lerp(ads, k * k * (3 - 2 * k))
    shots = [('hip', U(-.2, .2, -.42), 78.0), ('ads', U(*cpos), 78.0 + (78.0 / 1.35 - 78.0) * k)]
    if os.environ.get('FPARMS_DEBUG'):                                              # orthographic side / top views for layout checks
        shots += [('dbg_side', None, (R(90), 0, R(90)), Vector((1.5, -.05, -.05))), ('dbg_top', None, (0, 0, 0), Vector((0, -.05, 1.5)))]
    out = []
    for shot in shots:
        key = shot[0]
        if key.startswith('dbg'):
            cam.data.type = 'ORTHO'; cam.data.ortho_scale = .75; cam.rotation_euler = shot[2]; cam.location = shot[3]
            sc.render.filepath = os.path.join(PREVIEW, 'FPArms_%s%s_%s.png' % (name, tag, key)); bpy.ops.render.render(write_still=True)
            cam.data.type = 'PERSP'; cam.rotation_euler = (R(90), 0, R(180)); continue
        _, pos, fov = shot
        cam.location = pos; cam.data.angle_y = R(fov)
        p = os.path.join(PREVIEW, 'FPArms_%s%s_%s.png' % (name, tag, key)); sc.render.filepath = p
        bpy.ops.render.render(write_still=True); out.append(p); print('PREVIEW', p)
    return out

for name in VARIANTS:
    if ONLY and name not in ONLY: continue
    path = build_variant(name)
    if PREVIEW:
        preview(name, path)
        preview(name, path, TEAM_RED, '_red')
print('FPARMS_DONE')
