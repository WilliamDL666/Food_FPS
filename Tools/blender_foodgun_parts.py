# Blender (5.x) headless: 给外部素材包（Tripo 整块网格）的 3 把美食枪恢复可动件，供 FoodGunAnim（Assets/Scripts/FoodFx.cs）驱动。
#   SMG7  TeaLevel      奶茶液面：localScale.y 随剩余子弹缩放 → 轴心在液面底部，无旋转（Blender Z = Unity 本地 Y）
#   DMR3  Haw0..Haw4    糖葫芦：SetActive 逐颗隐藏，Haw0 在最前（枪口方向）先消失
#   LMG2  Spin          寿司转鼓：绕 Unity 本地 Z（= 枪管轴，Blender -Y）旋转 → 轴心在枪管轴上，无旋转
# 坐标：握把在原点，枪管沿 Blender -Y，上方 +Z，米。保留 AnimRoot / <名>_Body / Muzzle / Grip 和内嵌动画；
# 原贴图字节原样移植回输出 GLB（不经 Blender 重新压缩），尺寸不变（≤1024）。
#
# 运行（输入即输出，覆盖同一路径；已拆过的会跳过，可重复运行）：
#   blender.exe -b -P Tools/blender_foodgun_parts.py -- <FoodGuns 目录> [--src <拆件前 glb 目录>] [--only SMG7,DMR3]
#     --src   从未拆件的备份重新生成（例如 E:\Claude工作区\备份\FoodGuns_V21_拆件前）
#   blender.exe -b -P Tools/blender_foodgun_parts.py -- --preview <输出前缀> <FoodGuns 目录>
#     渲染对比图：每把枪 满弹 / 半弹 / 空弹（LMG2 为 静止 / 转 20° / 转 40°）
import bpy, bmesh, sys, os, math, json, struct
import numpy as np
from mathutils import Vector, Matrix

ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
GUNS = ('SMG7', 'DMR3', 'LMG2')
R = math.radians


# ------------------------------------------------------------------ helpers
def srgb2lin(c): return tuple((x / 12.92) if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)

def mix(a, b, t): return tuple(x * (1 - t) + y * t for x, y in zip(a, b))

_mats = {}
def mat(key, srgb, rough=0.35, metal=0.0):
    """固定前缀 FoodGun_，与旧程序化枪一致；导出前 strip_suffix() 再去掉可能的 .001。"""
    if key in _mats: return _mats[key]
    m = bpy.data.materials.new('FoodGun_' + key); m.use_nodes = True
    b = m.node_tree.nodes.get('Principled BSDF')
    b.inputs['Base Color'].default_value = (*srgb2lin(srgb), 1)
    b.inputs['Roughness'].default_value = rough; b.inputs['Metallic'].default_value = metal
    _mats[key] = m; return m

def link(o, root):
    bpy.context.view_layer.update()
    mw = o.matrix_world.copy(); o.parent = root; o.matrix_world = mw
    return o

def finish(o, m, root, bevel=0.0, seg=3, sub=0):
    o.data.materials.clear(); o.data.materials.append(m)
    if bevel > 0:
        md = o.modifiers.new('bev', 'BEVEL'); md.width = bevel; md.segments = seg; md.limit_method = 'NONE'
    if sub:
        md = o.modifiers.new('sub', 'SUBSURF'); md.levels = md.render_levels = sub
    for p in o.data.polygons: p.use_smooth = True
    return link(o, root)

def rbox(name, lo, hi, m, root, bevel=0.003, sub=0):
    """圆角盒：lo / hi 为世界坐标的对角点。"""
    c = [(a + b) / 2 for a, b in zip(lo, hi)]; s = [b - a for a, b in zip(lo, hi)]
    bpy.ops.mesh.primitive_cube_add(size=1, location=c); o = bpy.context.object; o.name = name; o.scale = s
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(o, m, root, bevel=min(bevel, min(s) * .49), seg=4, sub=sub)

def sph(name, loc, s, m, root, seg=16):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=seg // 2, radius=1, location=loc); o = bpy.context.object
    o.name = name; o.scale = s; bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(o, m, root)

def cyl_y(name, y0, y1, x, z, r, m, root, v=16, bevel=0.0):
    """沿 Y 的圆柱（y0 → y1）。"""
    bpy.ops.mesh.primitive_cylinder_add(vertices=v, radius=r, depth=abs(y1 - y0), location=(x, (y0 + y1) / 2, z), rotation=(R(90), 0, 0))
    o = bpy.context.object; o.name = name
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return finish(o, m, root, bevel=bevel)

def bake(o):
    """把修改器应用掉（合并静态小件前用）。"""
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    for md in list(o.modifiers): bpy.ops.object.modifier_apply(modifier=md.name)
    return o

def join(objs, name):
    for o in objs: bake(o)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]; bpy.ops.object.join()
    o = bpy.context.object; o.name = name; o.data.name = name; return o

def origin_to(o, point):
    bpy.context.scene.cursor.location = point
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')

def separate(body, pred, name, close=None, all_verts=False):
    """把 body 中满足 pred(面心世界坐标, 面心贴图色 sRGB) 的面拆成独立物体 name；返回 (新物体, 面数)。
    close(面心)：可选的"补漏"区域——区域内未选中、但有 >=2 个相邻面已选中的面也一起拆走（去掉零碎残片）。
    all_verts=True：改为要求面的每个顶点都满足 pred(顶点, 色)；否则 pred 第三个参数是该面的顶点列表。"""
    mw = body.matrix_world; cols = face_colors(body)
    bpy.ops.object.select_all(action='DESELECT'); body.select_set(True); bpy.context.view_layer.objects.active = body
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_mode(type='FACE')
    bm = bmesh.from_edit_mesh(body.data); bm.faces.ensure_lookup_table()
    sel = set()
    for f in bm.faces:
        f.select_set(False)
        if all_verts:
            if all(pred(mw @ v.co, cols[f.index]) for v in f.verts): sel.add(f)
        elif pred(mw @ f.calc_center_median(), cols[f.index], [mw @ v.co for v in f.verts]): sel.add(f)
    if close:
        for _ in range(4):
            add = [f for f in bm.faces if f not in sel and close(mw @ f.calc_center_median())
                   and sum(1 for e in f.edges for g in e.link_faces if g is not f and g in sel) >= 2]
            if not add: break
            sel.update(add)
    for f in sel: f.select_set(True)
    n = len(sel)
    bmesh.update_edit_mesh(body.data)
    if n == 0: bpy.ops.object.mode_set(mode='OBJECT'); raise RuntimeError('separate %s: no faces matched' % name)
    bpy.ops.mesh.separate(type='SELECTED'); bpy.ops.object.mode_set(mode='OBJECT')
    new = [o for o in bpy.context.selected_objects if o != body][0]
    new.name = name; new.data.name = name
    return new, n

def verts_world(o):
    mw = o.matrix_world; return [mw @ v.co for v in o.data.vertices]

def bbox_mid(pts):
    return Vector([(min(p[i] for p in pts) + max(p[i] for p in pts)) / 2 for i in range(3)])

_tex = {}
def face_colors(body):
    """每个面在 baseColor 贴图上的颜色（面 UV 中心处的像素，sRGB 0-1），按 polygon 索引。"""
    m = body.data.materials[0]
    img = next(n.image for n in m.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image and 'basecolor' in n.image.name.lower())
    if img.name not in _tex:
        a = np.empty(img.size[0] * img.size[1] * 4, dtype=np.float32); img.pixels.foreach_get(a)
        _tex[img.name] = (a.reshape(img.size[1], img.size[0], 4), img.size[0], img.size[1])
    px, w, h = _tex[img.name]
    me = body.data; uv = np.empty(len(me.loops) * 2, dtype=np.float32); me.uv_layers.active.data.foreach_get('uv', uv); uv = uv.reshape(-1, 2)
    st = np.empty(len(me.polygons), dtype=np.int32); me.polygons.foreach_get('loop_start', st)
    tot = np.empty(len(me.polygons), dtype=np.int32); me.polygons.foreach_get('loop_total', tot)
    idx = np.repeat(np.arange(len(st)), tot)
    cu = np.bincount(idx, uv[:, 0]) / tot; cv = np.bincount(idx, uv[:, 1]) / tot
    return px[((cv % 1) * h).astype(int) % h, ((cu % 1) * w).astype(int) % w, :3]

def is_red(c): return c[0] > .45 and c[0] > c[1] * 1.8 and c[0] > c[2] * 1.8
def is_reddish(c): return c[0] > .5 and c[0] > c[1] * 1.45 and c[0] > c[2] * 1.45      # 山楂边缘偏粉的高光
def is_orange(c): return c[0] > .6 and .3 < c[1] < .8 and c[2] < .45 and c[0] - c[2] > .35

def tex_color(body, pred, pick='median'):
    """在 body 的 baseColor 贴图上，对面心满足 pred 的面按 UV 取色（sRGB）。用来让新零件和原贴图同色系。"""
    fc = face_colors(body); mw = body.matrix_world
    cols = [fc[p.index] for p in body.data.polygons if pred(mw @ p.center)]
    if not cols: raise RuntimeError('tex_color: no faces')
    c = np.array(cols)
    if pick == 'dark': c = c[np.argsort(c.sum(1))[: max(1, len(c) // 5)]]       # 取最暗的 20%（珍珠、海苔）
    if pick == 'light': c = c[np.argsort(c.sum(1))[-max(1, len(c) // 5):]]      # 取最亮的 20%（奶盖、米饭）
    return tuple(float(x) for x in np.median(c, axis=0))


# ------------------------------------------------------------------ SMG-7 珍珠奶茶：液面
# 原模型奶茶画在机匣两侧一块内凹的"窗口"上（|x|≈0.026，画上去的珍珠凸到 0.030，前后壁 |x|≈0.031），y∈[-0.20,-0.105]，z∈[0.047,0.100]，
# 上面盖着圆形杯盖。做法：在内凹窗口里嵌一块"空杯"玻璃 + 一块稍厚的奶茶块 TeaLevel（轴心在底部），
# 珍珠固定沉在杯底，顶部一圈奶盖。新件贴着前后壁（|x|≤0.031），原网格不删面，所以没有破洞。
def smg7(root, body):
    Y0, Y1, Z0, Z1 = -0.2015, -0.1035, 0.047, 0.1005
    win = lambda p: abs(p.x) > 0.022 and -0.195 < p.y < -0.11 and 0.05 < p.z < 0.095
    tea_c = tex_color(body, win)
    boba_c = tex_color(body, win, 'dark')
    cream_c = tex_color(body, lambda p: abs(p.x) > 0.02 and -0.19 < p.y < -0.12 and 0.088 < p.z < 0.098, 'light')
    print('  SMG7 colours tea', [round(x, 3) for x in tea_c], 'boba', [round(x, 3) for x in boba_c], 'cream', [round(x, 3) for x in cream_c])
    # 采样只当参考：窗口里奶茶 / 珍珠 / 奶油混在一起，取中值偏暗偏灰，所以往原模型弹匣里的蜜桃奶茶色上拉
    tea_c = mix(tea_c, (0.96, 0.66, 0.42), .7)
    glass = mat('cupglass', (0.86, 0.93, 0.97), rough=.06)        # 空杯：偏冷的浅蓝白，和机身奶油色、奶茶色都拉开
    tea = mat('tea', tea_c, rough=.1)
    foam = mat('teafoam', mix(tea_c, (1.0, 0.95, 0.86), .6), rough=.25)
    boba = mat('boba', mix(boba_c, (0.25, 0.12, 0.07), .6), rough=.12)
    cream = mat('cream', (1.0, 0.96, 0.9), rough=.3)
    glint = mat('glint', (1.0, 1.0, 1.0), rough=.05)
    parts = [rbox('TeaGlass', (-0.0300, Y0, Z0), (0.0300, Y1, Z1), glass, root, bevel=.004)]
    # 奶盖：顶部一圈 + 几滴垂下来的奶油
    parts.append(rbox('TeaCream', (-0.0312, Y0 + .0008, 0.0925), (0.0312, Y1 - .0008, Z1 + .0005), cream, root, bevel=.004))
    for sx in (-1, 1):
        for y, d in ((-0.191, .0055), (-0.172, .0075), (-0.150, .005), (-0.131, .007), (-0.114, .0045)):
            parts.append(sph('drip', (sx * 0.0310, y, 0.0925 - d * .35), (.0018, .0045, d), cream, root, seg=12))
    # 珍珠沉底（两侧各 5 + 3 颗，空杯时仍在）
    for sx in (-1, 1):
        for y in (-0.192, -0.1735, -0.155, -0.1365, -0.118):
            parts.append(sph('boba', (sx * 0.0305, y, 0.0545), (.0058, .0058, .0058), boba, root, seg=14))
        for y in (-0.183, -0.146, -0.127):
            parts.append(sph('boba', (sx * 0.0302, y, 0.0655), (.0055, .0055, .0055), boba, root, seg=14))
        # 杯壁高光（斜条）
        g = rbox('glint', (sx * 0.0315 - .0004, -0.1215, 0.060), (sx * 0.0315 + .0004, -0.117, 0.088), glint, root, bevel=.0015)
        g.rotation_euler = (R(-14), 0, 0); parts.append(g)
    join(parts, 'TeaCup')
    # TeaLevel：x 比空杯厚 0.0007，盖住空杯；轴心在底部（z=Z0+.0005），FoodGunAnim 只缩放本地 Y（= Blender Z）
    # 液面顶上一条浅色奶沫线，随液面一起降
    t = join([rbox('TeaLevel', (-0.0307, Y0 + .0008, Z0 + .0005), (0.0307, Y1 - .0008, 0.0925), tea, root, bevel=.0035),
              rbox('TeaFoam', (-0.0310, Y0 + .0012, 0.0885), (0.0310, Y1 - .0012, 0.0925), foam, root, bevel=.0016)], 'TeaLevel')
    origin_to(t, (0, (Y0 + Y1) / 2, Z0 + .0005))
    return {'TeaLevel': t.name}


# ------------------------------------------------------------------ DMR-3 冰糖葫芦：5 颗山楂
# 原模型 5 颗山楂穿在竹签上（签轴 z≈0.1345），每颗坐在一根小立柱上（柱顶 z≈0.12）。
# 做法：按段（每颗占相邻间距的一半）+ 颜色（红色 / 柱顶以上）把每颗山楂从整块网格里拆出来 → Haw0（最前）..Haw4；
# 拆掉后竹签和柱顶会露洞，所以补一根贯穿的细竹签（藏在原竹签里，只在空位露出）和 5 个柱顶圆帽。
def dmr3(root, body):
    V = verts_world(body)
    SK_Z, SK_X = 0.1345, 0.0
    guesses = (-0.3985, -0.357, -0.315, -0.273, -0.231)
    skewer_c = tex_color(body, lambda p: abs(p.x - SK_X) < .008 and abs(p.z - SK_Z) < .008 and (-0.207 < p.y < -0.200 or -0.436 < p.y < -0.423), 'light')
    post_c = tex_color(body, lambda p: abs(p.x) < .006 and 0.104 < p.z < 0.116 and any(abs(p.y - g) < .007 for g in guesses), 'light')
    print('  DMR3 colours skewer', [round(x, 3) for x in skewer_c], 'post', [round(x, 3) for x in post_c])
    post_c = mix(post_c, skewer_c, .6)                               # 柱子采样会混进底下的棕色导轨，往竹签的奶油色拉
    skewer = mat('skewer', skewer_c, rough=.45); post = mat('hawpost', post_c, rough=.35)
    out = {}
    for i, g in enumerate(guesses):
        pts = [p for p in V if abs(p.y - g) < .019 and abs(p.x) < .034 and 0.121 < p.z < 0.16 and math.hypot(p.x - SK_X, p.z - SK_Z) > .0075]
        c = bbox_mid(pts)
        HALF = .0212                                                                      # 相邻两颗间距 0.042，各占一半
        lo = -HALF if i > 0 else -.028; hi = HALF if i < len(guesses) - 1 else .028       # 两头那颗往外多留一点（签头的红色）
        zc = max(p.z for p in pts) - .0205                                                 # 球心：从顶点往下一个半径
        def mine(p, col, c=c, lo=lo, hi=hi):
            if not (lo < p.y - c.y < hi) or abs(p.x) > .04 or p.z < .104: return False
            # 两颗之间露出的竹签留在机身（但贴着竹签的红色面跟山楂走，否则吃掉后签上留红点）
            if math.hypot(p.x - SK_X, p.z - SK_Z) < .0072 and abs(p.y - c.y) > .0135 and not is_reddish(col): return False
            return True
        # 这一段里：柱顶以上的所有面（含糖霜白点）+ 球内的红色面（被立柱挡住的山楂底部）；
        # 导轨顶面在山楂底下也被画成了偏红，球外的红色不动，否则导轨上会开洞
        inside = lambda p, col, vs, mine=mine, c=c, zc=zc: mine(p, col) and (p.z > .1205 or (p.z > .114 and is_reddish(col)) or (math.hypot(p.x - c.x, p.y - c.y, p.z - zc) < .025 and is_red(col)))
        h, n = separate(body, inside, 'Haw%d' % i, close=lambda p, c=c, zc=zc: math.hypot(p.x - c.x, p.y - c.y, p.z - zc) < .025 and p.z > .112)
        origin_to(h, c); out['Haw%d' % i] = '%d faces @ y=%.3f' % (n, c.y)
        # 柱顶圆帽：量柱顶截面
        pp = [p for p in V if abs(p.y - c.y) < .013 and abs(p.x) < .013 and 0.108 < p.z < 0.1185]
        if pp:
            pc = bbox_mid(pp); ex = max(p.x for p in pp) - min(p.x for p in pp); ey = max(p.y for p in pp) - min(p.y for p in pp)
            sph('HawPostCap%d' % i, (pc.x, pc.y, 0.1188), (ex / 2 * 1.22, ey / 2 * 1.22, .0048), post, root, seg=16)   # 稍宽，盖住柱顶边角残留的红点
    # 贯穿竹签：比原签细一点，平时藏在原签和山楂里，山楂吃掉后露出连续的签子
    ys = [p.y for p in V if abs(p.x - SK_X) < .006 and abs(p.z - SK_Z) < .006]
    cyl_y('HawSkewer', min(ys) + .006, max(ys) - .002, SK_X, SK_Z, .0052, skewer, root, v=14)
    return out


# ------------------------------------------------------------------ LMG-2 回转寿司：转鼓
# 原模型枪口后方是 6 卷寿司围着枪管的转鼓，外面套红色框架 + 橙色框边。寿司卷外缘 r≈0.061，框边从 r≈0.060 开始，
# 两者在同一圈里交错，只按半径切会在转动时露出碎片。所以 Spin 取：
#   转鼓内部（面心 r < R_IN，含中心枪管段和卷与卷之间的深色填充） + 6 卷寿司各自圆柱内的面（卷心由米饭色正面实测），
#   内部的面要求所有顶点 r < R_MAX（细长三角不会从框架里带出来）；卷上的面要求顶点都在该卷圆内。轴心在实测枪管轴上。
# 转鼓后端补一块深色背板（随 Spin 一起转），挡住寿司卷根部切口和机身上留下的洞。
Y_F, Y_B, R_IN, R_ROLL, R_MAX = -0.302, -0.2445, 0.0575, 0.0236, 0.066
def lmg2(root, body):
    V = verts_world(body)
    bar = [p for p in V if -0.306 < p.y < -0.297 and math.hypot(p.x + .003, p.z - .055) < .016]
    a = bbox_mid(bar); ax, az = a.x, a.z
    # 实测：枪管截面中心约 (-0.002, 0.0537)，与正视图 6 卷寿司的环心基本重合，转动不晃
    print('  LMG2 barrel axis x=%.4f z=%.4f' % (ax, az))
    # 卷心：从正视图读出的 6 个大致圆心做种子，用正面（朝 -Y）米饭色的面做几轮 k-means 校准
    fc = face_colors(body); mw = body.matrix_world; n3 = mw.to_3x3()
    rice = []
    for f in body.data.polygons:
        c = mw @ f.center; r = math.hypot(c.x - ax, c.z - az)
        if -0.300 < c.y < -0.270 and .02 < r < .066 and min(fc[f.index]) > .70 and (n3 @ f.normal).normalized().y < -.3:
            rice.append(Vector((c.x, c.z)))
    seeds = [Vector(v) for v in ((-0.003, 0.098), (0.0385, 0.080), (0.0426, 0.0335), (0.004, 0.013), (-0.037, 0.028), (-0.040, 0.069))]
    rolls = list(seeds)
    for _ in range(6):
        groups = [[] for _ in rolls]
        for q in rice:
            k = min(range(6), key=lambda i: (q - rolls[i]).length)
            if (q - rolls[k]).length < .024: groups[k].append(q)
        rolls = [sum(g, Vector((0, 0))) / len(g) if g else rolls[i] for i, g in enumerate(groups)]
    print('  LMG2 roll centres', [(round(v.x, 4), round(v.y, 4), round(math.hypot(v.x - ax, v.y - az), 4)) for v in rolls])
    # 有的卷米饭圈一侧和框架熔在一起，k-means 会偏；种子和校准值都用（任一圆内都算这卷）
    rolls = rolls + seeds
    nori_c = tex_color(body, lambda p: Y_F < p.y < Y_B and .03 < math.hypot(p.x - ax, p.z - az) < .06, 'dark')
    print('  LMG2 colour nori', [round(x, 3) for x in nori_c])
    def inside(p, col, vs):
        if not (Y_F < p.y < Y_B): return False
        for rc in rolls:      # 寿司卷：以卷心为准（Tripo 的 6 卷并不正好在一个圆上，最外一卷外缘到 r≈0.068）
            d = math.hypot(p.x - rc.x, p.z - rc.y)
            if d > .013 and is_orange(col): continue      # 卷的外圈只有米饭白 / 海苔黑，橙色只在卷芯；外圈的橙色是框边
            if d < R_ROLL and all(math.hypot(v.x - rc.x, v.z - rc.y) < R_ROLL + .004 for v in vs): return True
        if any(math.hypot(v.x - ax, v.z - az) > R_MAX for v in vs): return False
        return math.hypot(p.x - ax, p.z - az) < R_IN
    s, n = separate(body, inside, 'Spin')
    back = cyl_y('SpinBack', Y_B - .0022, Y_B - .0002, ax, az, R_MAX - .002, mat('nori', mix(nori_c, (0.08, 0.06, 0.05), .3), rough=.5), root, v=48)
    bake(back)
    bpy.ops.object.select_all(action='DESELECT'); back.select_set(True); s.select_set(True)
    bpy.context.view_layer.objects.active = s; bpy.ops.object.join()
    s = bpy.context.object; s.name = 'Spin'; s.data.name = 'Spin'
    origin_to(s, (ax, (Y_F + Y_B) / 2, az))
    return {'Spin': '%d faces, axis x=%.4f z=%.4f, %d rolls' % (n, ax, az, len(rolls))}


# ------------------------------------------------------------------ GLB plumbing
def read_glb(path):
    d = open(path, 'rb').read()
    jl = struct.unpack('<I', d[12:16])[0]; j = json.loads(d[20:20 + jl])
    bo = 20 + jl; bl = struct.unpack('<I', d[bo:bo + 4])[0]
    return j, d[bo + 8:bo + 8 + bl]

def glb_images(path):
    j, b = read_glb(path); out = {}
    for im in j.get('images', []):
        bv = j['bufferViews'][im['bufferView']]; s = bv.get('byteOffset', 0)
        out[im.get('name')] = (b[s:s + bv['byteLength']], im['mimeType'])
    return out

def transplant_images(path, src_imgs):
    """把 Blender 重新编码的贴图换回原始字节（同名替换），避免二次压缩、保持 ≤1024。"""
    j, b = read_glb(path)
    by_bv = {im['bufferView']: im for im in j.get('images', []) if 'bufferView' in im}
    out = bytearray(); swapped = []
    for i, bv in enumerate(j['bufferViews']):
        s = bv.get('byteOffset', 0); data = b[s:s + bv['byteLength']]
        im = by_bv.get(i)
        if im is not None and im.get('name') in src_imgs:
            data, mime = src_imgs[im['name']]; im['mimeType'] = mime; swapped.append(im['name'])
        while len(out) % 4: out += b'\0'
        bv['byteOffset'] = len(out); bv['byteLength'] = len(data); out += data
    while len(out) % 4: out += b'\0'
    j['buffers'][0]['byteLength'] = len(out)
    js = json.dumps(j, separators=(',', ':')).encode()
    while len(js) % 4: js += b' '
    total = 12 + 8 + len(js) + 8 + len(out)
    open(path, 'wb').write(struct.pack('<III', 0x46546C67, 2, total) + struct.pack('<I', len(js)) + b'JSON' + js + struct.pack('<I', len(out)) + b'BIN\0' + bytes(out))
    return swapped, [im.get('name') for im in j.get('images', [])]

def strip_suffix():
    """V18 教训：Blender 会给重名材质 / 物体加 .001，游戏按名字找节点和材质，导出前去掉。"""
    for coll in (bpy.data.materials, bpy.data.objects):
        for x in list(coll):
            base = x.name.split('.')[0] if '.' in x.name and x.name.rsplit('.', 1)[1].isdigit() else None
            if base and base not in coll: x.name = base


def process(name, src, dst):
    bpy.ops.wm.read_factory_settings(use_empty=True); _mats.clear()
    bpy.ops.import_scene.gltf(filepath=src)
    names = {o.name for o in bpy.data.objects}
    if {'TeaLevel', 'Spin', 'Haw0'} & names:
        print('SKIP', name, '(already has movable parts; use --src <pristine dir> to rebuild)'); return
    root = bpy.data.objects['AnimRoot']; body = bpy.data.objects[name + '_Body']
    keep = {n: bpy.data.objects[n].matrix_world.translation.copy() for n in ('Muzzle', 'Grip')}
    bb0 = [round(x, 4) for x in bbox_all()]
    info = {'SMG7': smg7, 'DMR3': dmr3, 'LMG2': lmg2}[name](root, body)
    strip_suffix()
    for n, p in keep.items(): assert (bpy.data.objects[n].matrix_world.translation - p).length < 1e-6, n
    bb1 = [round(x, 4) for x in bbox_all()]
    src_imgs = glb_images(src)
    bpy.ops.object.select_all(action='DESELECT')
    bpy.ops.export_scene.gltf(filepath=dst, export_format='GLB', export_yup=True, export_apply=True, export_animations=True,
                              export_image_format='AUTO', export_jpeg_quality=95)
    swapped, all_imgs = transplant_images(dst, src_imgs)
    print('DONE', name, os.path.getsize(src) // 1024, 'K ->', os.path.getsize(dst) // 1024, 'K', info)
    print('   bbox before', bb0, 'after', bb1)
    print('   images', all_imgs, 'restored', swapped)

def bbox_all():
    pts = [p for o in bpy.data.objects if o.type == 'MESH' for p in (o.matrix_world @ Vector(c) for c in o.bound_box)]
    return [min(p[i] for p in pts) for i in range(3)] + [max(p[i] for p in pts) for i in range(3)]


# ------------------------------------------------------------------ preview（满弹 / 半弹 / 空弹 对比）
# 每把枪出两张：<out>_<枪>_whole.png（整枪 3/4 视角）和 <out>_<枪>_close.png（可动件特写），每张从左到右三个状态。
WHOLE = {'SMG7': (0, -0.12, 0.04), 'DMR3': (0, -0.2, 0.06), 'LMG2': (0, -0.1, 0.035)}
FOCUS = {'SMG7': ((0, -0.15, 0.07), 0.30), 'DMR3': ((0, -0.31, 0.13), 0.30), 'LMG2': ((0, -0.27, 0.055), 0.26)}
def preview(out, d):
    states = [('full', 1.0, 0), ('half', .5, 20), ('empty', 0.0, 40)]
    for g in GUNS:
        for mode in ('whole', 'close'):
            bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene
            (fx, fy, fz), osc = FOCUS[g]
            if mode == 'whole': fx, fy, fz = WHOLE[g]
            gap = {'SMG7': .55, 'DMR3': .95, 'LMG2': .66}[g] if mode == 'whole' else osc * 1.15
            for si, (_, frac, ang) in enumerate(states):
                before = set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=os.path.join(d, g + '.glb'))
                new = [o for o in bpy.data.objects if o not in before]
                hold = bpy.data.objects.new('hold', None); sc.collection.objects.link(hold)
                for o in new:
                    o.animation_data_clear()                                          # 内嵌 Idle 片段会改 AnimRoot，预览里不要
                    n = o.name.split('.')[0]
                    if n == 'TeaLevel': o.scale.z = max(.04, frac)                      # = FoodGunAnim 的 tea 逻辑
                    if n.startswith('Haw') and n[3:].isdigit():                        # = FoodGunAnim 的 haws 逻辑
                        o.hide_render = not (int(n[3:]) >= 5 - math.ceil(frac * 5))
                    if n == 'Spin': o.rotation_mode = 'XYZ'; o.rotation_euler.y = R(ang)   # Unity 本地 Z = Blender -Y（导入是四元数模式，先切 XYZ）
                    if o.parent is None: o.parent = hold
                # 绕焦点转到 3/4 视角（看枪的左侧 -X，和第一人称看到的一面相同）
                hold.matrix_world = Matrix.Translation((si * gap, 0, 0)) @ Matrix.Rotation(R(-35), 4, 'Z') @ Matrix.Rotation(R(-14), 4, 'Y') @ Matrix.Translation((-fx, -fy, -fz))
            sc.render.engine = 'BLENDER_EEVEE' if 'BLENDER_EEVEE' in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items.keys() else 'BLENDER_EEVEE_NEXT'
            sc.render.resolution_x, sc.render.resolution_y = 1800, 640
            w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
            w.node_tree.nodes['Background'].inputs[0].default_value = (0.75, 0.85, 1.0, 1)
            sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sc.collection.objects.link(sun); sun.data.energy = 3.5; sun.rotation_euler = (R(50), 0, R(-150))
            cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
            cam.data.type = 'ORTHO'; cam.data.ortho_scale = 3 * gap * 1.02; cam.data.clip_start = .01
            cam.location = (gap, -3, 0.9); cam.rotation_euler = (R(73), 0, 0)
            f = '%s_%s_%s.png' % (out, g, mode); sc.render.filepath = f; bpy.ops.render.render(write_still=True); print('PREVIEW_DONE', f)


if ARGS and ARGS[0] == '--preview':
    preview(ARGS[1], ARGS[2])
else:
    if not ARGS: raise SystemExit('usage: blender -b -P blender_foodgun_parts.py -- <FoodGuns dir> [--src <dir>] [--only A,B]')
    out_dir = ARGS[0]
    src_dir = ARGS[ARGS.index('--src') + 1] if '--src' in ARGS else out_dir
    only = ARGS[ARGS.index('--only') + 1].split(',') if '--only' in ARGS else GUNS
    for g in GUNS:
        if g in only: process(g, os.path.join(src_dir, g + '.glb'), os.path.join(out_dir, g + '.glb'))
    print('FOODGUN_PARTS_DONE')
