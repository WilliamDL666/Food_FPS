# V23：6 个技能的美食图标（程序绘制，原创，无外部素材）。
# 白色 + 灰阶明暗的剪影：UI 用 GUI.color 着色（就绪 = 英雄色、联动可用 = 金色、充能中 = 灰），所以图标本身不带颜色。
# Run: python Tools/make_skill_icons.py  → Assets/Resources/Icons/SkillFood_<Role>.png（128×128，4× 超采样）
import os
from PIL import Image, ImageDraw, ImageFilter

OUT = os.path.join(os.path.dirname(__file__), '..', 'Assets', 'Resources', 'Icons')
os.makedirs(OUT, exist_ok=True)
N, S = 128, 4
W = N * S

def canvas(): return Image.new('LA', (W, W), (255, 0))
def k(*v): return [int(x * S) for x in v]
WHITE, LIGHT, MID, DARK = (255, 255), (232, 255), (190, 255), (140, 255)

def finish(img, name):
    # 外圈 3 px 深色描边（在任何底色上都看得清）
    a = img.getchannel('A')
    grown = a.filter(ImageFilter.MaxFilter(4 * S + 1))
    outline = Image.new('LA', (W, W), (70, 0)); outline.putalpha(grown)
    base = Image.new('LA', (W, W), (255, 0)); base.alpha_composite(outline); base.alpha_composite(img)
    base = base.resize((N, N), Image.LANCZOS)
    base.convert('RGBA').save(os.path.join(OUT, f'SkillFood_{name}.png'))

def soda():   # 汽水罐（拉环 + 气泡）
    im = canvas(); d = ImageDraw.Draw(im)
    d.rounded_rectangle(k(38, 26, 90, 112), radius=10 * S, fill=WHITE)
    d.rectangle(k(38, 26, 90, 34), fill=MID); d.rectangle(k(38, 104, 90, 112), fill=MID)
    d.rounded_rectangle(k(46, 50, 82, 84), radius=6 * S, fill=MID)            # 标签
    d.ellipse(k(56, 58, 72, 76), fill=WHITE)
    d.rounded_rectangle(k(56, 14, 76, 24), radius=4 * S, outline=WHITE, width=3 * S)   # 拉环
    for x, y, r in ((98, 30, 6), (106, 48, 4), (96, 60, 3)): d.ellipse(k(x - r, y - r, x + r, y + r), outline=WHITE, width=2 * S)
    return im
def wafer():  # 三层威化
    im = canvas(); d = ImageDraw.Draw(im)
    for i, y in enumerate((30, 58, 86)):
        d.rounded_rectangle(k(18, y, 110, y + 22), radius=4 * S, fill=WHITE)
        for x in range(26, 110, 14): d.line(k(x, y + 3, x, y + 19), fill=MID, width=2 * S)
        d.line(k(20, y + 11, 108, y + 11), fill=MID, width=2 * S)
        if i < 2: d.rectangle(k(20, y + 22, 108, y + 28), fill=LIGHT)                  # 奶油夹心
    return im
def pudding():  # 焦糖布丁 + 焦糖滴
    im = canvas(); d = ImageDraw.Draw(im)
    d.polygon(k(30, 106, 98, 106, 86, 40, 42, 40), fill=WHITE)
    d.ellipse(k(42, 32, 86, 48), fill=DARK)
    for x, l in ((48, 16), (62, 24), (76, 12)): d.rounded_rectangle(k(x - 4, 42, x + 4, 42 + l), radius=4 * S, fill=DARK)
    d.rounded_rectangle(k(20, 104, 108, 114), radius=5 * S, fill=MID)                   # 盘子
    d.ellipse(k(50, 60, 60, 76), fill=LIGHT)
    return im
def skewer():  # 芥末竹签（签头一团芥末 + 气味波）
    im = canvas(); d = ImageDraw.Draw(im)
    d.line(k(18, 112, 96, 34), fill=WHITE, width=6 * S)
    d.polygon(k(92, 26, 110, 18, 102, 38), fill=WHITE)                                  # 签尖
    for x, y, r in ((74, 54, 15), (86, 44, 11), (64, 64, 10)): d.ellipse(k(x - r, y - r, x + r, y + r), fill=LIGHT)   # 一团芥末
    d.ellipse(k(70, 46, 80, 56), fill=WHITE)
    for r in (22, 32): d.arc(k(76 - r, 52 - r, 76 + r, 52 + r), 200, 280, fill=WHITE, width=3 * S)   # 气味波
    for i in range(2): d.polygon(k(22 + i * 9, 100 - i * 9, 12 + i * 9, 98 - i * 9, 20 + i * 9, 108 - i * 9), fill=MID)   # 尾羽
    return im
def cupcake():  # 纸杯 + 蜡烛 + 火苗
    im = canvas(); d = ImageDraw.Draw(im)
    d.polygon(k(30, 66, 98, 66, 88, 112, 40, 112), fill=WHITE)
    for x in range(40, 92, 10): d.line(k(x, 70, x + 2 * (1 if x > 64 else -1), 110), fill=MID, width=2 * S)
    d.ellipse(k(24, 46, 104, 78), fill=LIGHT); d.ellipse(k(38, 34, 90, 62), fill=WHITE)
    d.rounded_rectangle(k(59, 12, 69, 40), radius=3 * S, fill=MID)
    d.ellipse(k(57, 2, 71, 14), fill=WHITE); d.polygon(k(57, 8, 71, 8, 64, -6), fill=WHITE)   # 水滴形火苗
    return im
def marsh():  # 棉花糖棒 + 拉丝
    im = canvas(); d = ImageDraw.Draw(im)
    d.line(k(34, 112, 62, 64), fill=MID, width=6 * S)
    for x, y, r in ((70, 44, 26), (54, 36, 18), (88, 34, 18), (76, 22, 16)): d.ellipse(k(x - r, y - r, x + r, y + r), fill=WHITE)
    d.ellipse(k(60, 30, 72, 40), fill=LIGHT)
    d.arc(k(76, 54, 124, 110), 200, 300, fill=WHITE, width=2 * S); d.arc(k(84, 60, 120, 100), 190, 290, fill=LIGHT, width=2 * S)
    return im

for name, fn in (('Falcon', soda), ('Rivet', wafer), ('Ironwall', pudding), ('Raven', skewer), ('Ember', cupcake), ('Phantom', marsh)):
    finish(fn(), name)
print('ICONS_DONE', OUT)
