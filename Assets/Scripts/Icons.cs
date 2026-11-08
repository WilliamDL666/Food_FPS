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
        public static Texture2D Skill(Role r)
        {
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

        public static void Draw(Rect r, Texture2D t, Color c)
        {
            var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, t, ScaleMode.ScaleToFit); GUI.color = o;
        }
    }
}
