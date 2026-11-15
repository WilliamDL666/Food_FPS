using System.Collections.Generic;
using UnityEngine;

namespace NeonSiege
{
    // Procedurally painted UI icons (no external sprite sheets needed): hexagon frames, skull kill
    // icon, headshot icon, per-hero skill glyphs, arrows. Generated once, cached, anti-aliased.
    public static class Icons
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        delegate float Shape(float x, float y);   // signed distance-ish: <0 inside

        static Texture2D Paint(string key, int n, Shape s, float soft = 1.5f)
        {
            if (cache.TryGetValue(key, out var t) && t != null) return t;
            t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + .5f) / n * 2 - 1, v = (y + .5f) / n * 2 - 1;
                    float d = s(u, v) * n * .5f;
                    px[y * n + x] = new Color(1, 1, 1, Mathf.Clamp01(.5f - d / soft));
                }
            t.SetPixels(px); t.Apply(true);
            cache[key] = t;
            return t;
        }

        static float Circle(float x, float y, float cx, float cy, float r) { return Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r; }
        static float Box(float x, float y, float cx, float cy, float hx, float hy) { float dx = Mathf.Abs(x - cx) - hx, dy = Mathf.Abs(y - cy) - hy; return Mathf.Max(dx, dy); }
        static float Hex(float x, float y, float r)
        {
            x = Mathf.Abs(x); y = Mathf.Abs(y);
            return Mathf.Max(x * .866f + y * .5f, y) - r;
        }
        static float Union(float a, float b) { return Mathf.Min(a, b); }
        static float Cut(float a, float b) { return Mathf.Max(a, -b); }

        public static Texture2D HexFill { get { return Paint("hexfill", 128, (x, y) => Hex(y, x, .92f)); } }
        public static Texture2D HexRing { get { return Paint("hexring", 128, (x, y) => Mathf.Abs(Hex(y, x, .86f)) - .06f); } }
        public static Texture2D Ring { get { return Paint("ring", 128, (x, y) => Mathf.Abs(Circle(x, y, 0, 0, .85f)) - .07f); } }

        // Skull: cranium + jaw with eye sockets and nose cut out.
        public static Texture2D Skull
        {
            get
            {
                return Paint("skull", 128, (x, y) =>
                {
                    float head = Union(Circle(x, y, 0, .12f, .62f), Box(x, y, 0, -.42f, .36f, .22f));
                    float eyes = Union(Circle(x, y, -.25f, .08f, .17f), Circle(x, y, .25f, .08f, .17f));
                    float nose = Box(x, y, 0, -.18f, .06f, .09f);
                    float teeth = Union(Box(x, y, -.13f, -.52f, .025f, .12f), Box(x, y, .13f, -.52f, .025f, .12f));
                    return Cut(Cut(Cut(head, eyes), nose), teeth);
                });
            }
        }

        // Headshot: head circle with crosshair through it.
        public static Texture2D Headshot
        {
            get
            {
                return Paint("headshot", 96, (x, y) =>
                {
                    float ring = Mathf.Abs(Circle(x, y, 0, 0, .55f)) - .09f;
                    float dot = Circle(x, y, 0, 0, .16f);
                    float cross = Union(Box(x, y, 0, .8f, .05f, .18f), Union(Box(x, y, 0, -.8f, .05f, .18f), Union(Box(x, y, .8f, 0, .18f, .05f), Box(x, y, -.8f, 0, .18f, .05f))));
                    return Union(Union(ring, dot), cross);
                });
            }
        }

        public static Texture2D Arrow
        {
            get
            {
                return Paint("arrow", 96, (x, y) =>
                {
                    float shaft = Box(x, y, -.25f, 0, .45f, .14f);
                    float head = Mathf.Max(Mathf.Max(x - .75f, -x - .1f + Mathf.Abs(y) * 0f), Mathf.Abs(y) - (.75f - x) * .9f);
                    return Union(shaft, Mathf.Max(head, -(x - .05f)));
                });
            }
        }

        public static Texture2D Diamond { get { return Paint("diamond", 64, (x, y) => Mathf.Abs(x) + Mathf.Abs(y) - .8f); } }

        public static Texture2D Chevron
        {
            get
            {
                return Paint("chevron", 64, (x, y) =>
                {
                    float a = Mathf.Abs(y - (.3f - Mathf.Abs(x) * .9f)) - .14f;
                    return Mathf.Max(a, Mathf.Abs(x) - .85f);
                });
            }
        }

        // Ability glyphs, one per hero (drawn inside a hexagon by the UI).
        // V23：美食模式用美食图标（Tools/make_skill_icons.py 生成 → Resources/Icons/SkillFood_<Role>.png）：汽水罐、威化、布丁、芥末竹签、带蜡烛纸杯、棉花糖棒
        public static Texture2D FoodSkill(Role r)
        {
            string key = "foodsk_" + r;
            if (cache.TryGetValue(key, out var t) && t != null) return t;
            t = Resources.Load<Texture2D>("Icons/SkillFood_" + r);
            if (t != null) cache[key] = t;
            return t;
        }
        public static Texture2D Skill(Role r)
        {
            if (ToonAssets.Available) { var ft = FoodSkill(r); if (ft != null) return ft; }
            switch (r)
            {
                case Role.Falcon:   // speed chevrons
                    return Paint("sk0", 128, (x, y) =>
                    {
                        float c1 = Mathf.Max(Mathf.Abs(x + .25f - (.45f - Mathf.Abs(y) * .9f)) - .1f, Mathf.Abs(y) - .55f);
                        float c2 = Mathf.Max(Mathf.Abs(x - .25f - (.45f - Mathf.Abs(y) * .9f)) - .1f, Mathf.Abs(y) - .55f);
                        return Union(c1, c2);
                    });
                case Role.Rivet:    // brick wall
                    return Paint("sk1", 128, (x, y) =>
                    {
                        float wall = Box(x, y, 0, -.05f, .62f, .48f);
                        float gaps = Union(Box(x, y, 0, .12f, .7f, .025f), Box(x, y, 0, -.22f, .7f, .025f));
                        gaps = Union(gaps, Union(Box(x, y, -.2f, .3f, .025f, .16f), Box(x, y, .25f, -.05f, .025f, .16f)));
                        gaps = Union(gaps, Box(x, y, -.2f, -.38f, .025f, .16f));
                        return Cut(wall, gaps);
                    });
                case Role.Ironwall: // shield
                    return Paint("sk2", 128, (x, y) =>
                    {
                        float top = Box(x, y, 0, .25f, .55f, .3f);
                        float bottom = Mathf.Max(Mathf.Abs(x) - (.55f - (.0f - y) * .9f), Box(x, y, 0, -.25f, .55f, .35f));
                        float shield = Union(top, bottom);
                        return Cut(shield, Mathf.Abs(Union(top, bottom) + .14f) - .045f);
                    });
                case Role.Raven:    // eye
                    return Paint("sk3", 128, (x, y) =>
                    {
                        float eye = Mathf.Max(Circle(x, y, 0, -.55f, .85f), Circle(x, y, 0, .55f, .85f));
                        float iris = Circle(x, y, 0, 0, .2f);
                        return Union(Cut(eye, Circle(x, y, 0, 0, .3f)), iris);
                    });
                case Role.Ember:    // burst star
                    return Paint("sk4", 128, (x, y) =>
                    {
                        float a = Mathf.Atan2(y, x), r = Mathf.Sqrt(x * x + y * y);
                        float star = r - (.45f + .25f * Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 4)), 3));
                        return Cut(star, Circle(x, y, 0, 0, .18f));
                    });
                default:            // signal waves
                    return Paint("sk5", 128, (x, y) =>
                    {
                        float r = Mathf.Sqrt(x * x + (y + .5f) * (y + .5f));
                        float waves = Union(Mathf.Abs(r - .35f) - .06f, Union(Mathf.Abs(r - .62f) - .06f, Mathf.Abs(r - .89f) - .06f));
                        float wedge = Mathf.Abs(x) - (y + .5f) * .8f;
                        return Union(Mathf.Max(waves, wedge), Circle(x, y, 0, -.5f, .12f));
                    });
            }
        }

        // ---- V22：白色豆子姿态剪影（参考图 UI-4）+ 体力锁。和其它图标一样程序生成，不用外部图片
        static float Ellipse(float x, float y, float cx, float cy, float rx, float ry, float rotDeg = 0)
        {
            float a = rotDeg * Mathf.Deg2Rad, dx = x - cx, dy = y - cy;
            float u = dx * Mathf.Cos(a) + dy * Mathf.Sin(a), v = -dx * Mathf.Sin(a) + dy * Mathf.Cos(a);
            return (Mathf.Sqrt(u * u / (rx * rx) + v * v / (ry * ry)) - 1f) * Mathf.Min(rx, ry);
        }
        static float Smooth(float a, float b, float k) { float h = Mathf.Clamp01(.5f + .5f * (b - a) / k); return Mathf.Lerp(b, a, h) - k * h * (1 - h); }
        public static Texture2D BeanStand
        {
            get
            {
                return Paint("bean_stand", 128, (x, y) =>
                {
                    float body = Union(Ellipse(x, y, 0, .2f, .4f, .48f), Box(x, y, 0, -.22f, .4f, .3f));
                    float arms = Union(Ellipse(x, y, -.47f, -.12f, .11f, .2f, 25), Ellipse(x, y, .47f, -.12f, .11f, .2f, -25));
                    float feet = Union(Ellipse(x, y, -.2f, -.62f, .13f, .16f), Ellipse(x, y, .2f, -.62f, .13f, .16f));
                    return Smooth(Smooth(body, arms, .06f), feet, .05f);
                });
            }
        }
        public static Texture2D BeanCrouch
        {
            get
            {
                return Paint("bean_crouch", 128, (x, y) =>
                {
                    float body = Ellipse(x, y, .08f, .1f, .42f, .4f, -15);
                    float legs = Union(Ellipse(x, y, -.32f, -.38f, .14f, .2f, 30), Ellipse(x, y, .22f, -.42f, .14f, .2f, -20));
                    float feet = Union(Ellipse(x, y, -.48f, -.62f, .2f, .09f), Ellipse(x, y, .42f, -.62f, .2f, .09f));
                    float arm = Ellipse(x, y, .5f, -.25f, .1f, .17f, -30);
                    return Smooth(Smooth(Smooth(body, legs, .07f), feet, .05f), arm, .05f);
                });
            }
        }
        public static Texture2D BeanProne
        {
            get
            {
                return Paint("bean_prone", 128, (x, y) =>
                {
                    float body = Smooth(Ellipse(x, y, -.05f, -.35f, .58f, .25f), Ellipse(x, y, .12f, -.2f, .36f, .3f), .1f);
                    float hands = Ellipse(x, y, -.72f, -.46f, .17f, .1f);
                    float feet = Ellipse(x, y, .74f, -.5f, .17f, .09f);
                    return Smooth(Smooth(body, hands, .06f), feet, .06f);
                });
            }
        }
        public static Texture2D BeanSlide
        {
            get
            {
                return Paint("bean_slide", 128, (x, y) =>
                {
                    float body = Smooth(Ellipse(x, y, .02f, -.32f, .52f, .24f, 15), Ellipse(x, y, .38f, -.06f, .3f, .3f), .1f);
                    float foot = Ellipse(x, y, -.6f, -.52f, .2f, .1f, 10), hand = Ellipse(x, y, .72f, -.4f, .14f, .09f);
                    float lines = Union(Box(x, y, -.42f, .32f, .26f, .045f), Box(x, y, -.56f, .14f, .2f, .045f));
                    return Union(Smooth(Smooth(body, foot, .06f), hand, .05f), lines);
                });
            }
        }
        public static Texture2D Lock
        {
            get
            {
                return Paint("lock", 64, (x, y) =>
                {
                    float shackle = Mathf.Max(Mathf.Abs(Circle(x, y, 0, .18f, .36f)) - .11f, -(y - .12f));
                    shackle = Union(shackle, Union(Box(x, y, -.36f, .05f, .11f, .14f), Box(x, y, .36f, .05f, .11f, .14f)));
                    float body = Box(x, y, 0, -.33f, .58f, .42f);
                    return Cut(Union(shackle, body), Union(Circle(x, y, 0, -.26f, .12f), Box(x, y, 0, -.45f, .05f, .14f)));
                });
            }
        }

        public static void Draw(Rect r, Texture2D t, Color c)
        {
            var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, t, ScaleMode.ScaleToFit); GUI.color = o;
        }
    }
}
