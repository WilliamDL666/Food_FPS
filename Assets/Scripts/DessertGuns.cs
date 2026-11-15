using System.Collections.Generic;
using UnityEngine;

namespace NeonSiege
{
    // V14：甜品风武器 —— 全部用代码建模（基础几何体 + 程序生成的甜甜圈/甜筒网格），不依赖外部模型。
    // 坐标约定：枪管朝 +Z，握把在原点，单位米。
    public static class DessertGuns
    {
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        public static Material M(string key, Color c, float gloss = .55f, float emit = 0f)
        {
            if (mats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Shader.Find("Standard")) { name = "甜品_" + key, color = c };
            m.SetFloat("_Glossiness", gloss); m.SetFloat("_Metallic", 0f);
            if (emit > 0) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * emit); }
            mats[key] = m; return m;
        }
        // palette
        static Material Choco => M("choco", new Color(.36f, .2f, .12f), .6f);
        static Material DarkChoco => M("darkchoco", new Color(.2f, .11f, .07f), .65f);
        static Material Wafer => M("wafer", new Color(.93f, .74f, .45f), .2f);
        static Material Biscuit => M("biscuit", new Color(.85f, .6f, .32f), .15f);
        static Material Cream => M("cream", new Color(1f, .97f, .9f), .45f);
        static Material Pink => M("pink", new Color(1f, .62f, .76f), .6f);
        static Material Mint => M("mint", new Color(.6f, .95f, .82f), .6f);
        static Material Lemon => M("lemon", new Color(1f, .9f, .45f), .6f);
        static Material Berry => M("berry", new Color(.7f, .3f, .85f), .6f);
        static Material CandyRed => M("candyred", new Color(.95f, .18f, .25f), .75f);
        static Material White => M("white", new Color(.98f, .98f, 1f), .7f);
        static Material Sky => M("sky", new Color(.55f, .82f, 1f), .6f);
        static Material Cherry => M("cherry", new Color(.85f, .05f, .12f), .9f);
        static Material Flame => M("flame", new Color(1f, .7f, .25f), .2f, 2.5f);
        static readonly Color[] SprinkleColors = { new Color(1f, .35f, .5f), new Color(.4f, .8f, 1f), new Color(1f, .9f, .3f), new Color(.5f, 1f, .6f), new Color(.8f, .5f, 1f), Color.white };
        public static Color Candy(int i) { return SprinkleColors[(i % SprinkleColors.Length + SprinkleColors.Length) % SprinkleColors.Length]; }

        static Transform P(Transform parent, PrimitiveType t, Vector3 pos, Vector3 scale, Material m, Vector3 euler = default)
        {
            var g = GameObject.CreatePrimitive(t); Object.Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localEulerAngles = euler; g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = m; return g.transform;
        }
        static Transform MeshPart(Transform parent, Mesh mesh, Vector3 pos, Vector3 euler, Vector3 scale, Material m, string name)
        {
            var g = new GameObject(name); g.transform.SetParent(parent, false);
            g.transform.localPosition = pos; g.transform.localEulerAngles = euler; g.transform.localScale = scale;
            g.AddComponent<MeshFilter>().sharedMesh = mesh; g.AddComponent<MeshRenderer>().sharedMaterial = m; return g.transform;
        }

        // ---------------- procedural meshes
        static Mesh torus, cone, swirl;
        static Mesh Torus { get { return torus != null ? torus : torus = BuildTorus(1f, .42f, 28, 14); } }   // donut, ring in XZ plane
        static Mesh Cone { get { return cone != null ? cone : cone = BuildCone(24); } }                      // apex at +Y, base radius 1 at y=0
        static Mesh BuildTorus(float R, float r, int seg, int sides)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var tri = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2; Vector3 c = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                for (int j = 0; j <= sides; j++)
                {
                    float b = j / (float)sides * Mathf.PI * 2; Vector3 d = c * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    v.Add(c * R + d * r); n.Add(d);
                }
            }
            for (int i = 0; i < seg; i++) for (int j = 0; j < sides; j++)
                {
                    int a = i * (sides + 1) + j, b = a + sides + 1;
                    tri.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
                }
            var m = new Mesh { name = "donut" }; m.SetVertices(v); m.SetNormals(n); m.SetTriangles(tri, 0); m.RecalculateBounds(); return m;
        }
        static Mesh BuildCone(int seg)
        {
            var v = new List<Vector3>(); var tri = new List<int>();
            for (int i = 0; i < seg; i++)
            {
                float a0 = i / (float)seg * Mathf.PI * 2, a1 = (i + 1) / (float)seg * Mathf.PI * 2;
                Vector3 p0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), p1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                int k = v.Count; v.Add(p0); v.Add(Vector3.up); v.Add(p1); tri.AddRange(new[] { k, k + 1, k + 2 });
                k = v.Count; v.Add(p1); v.Add(Vector3.zero); v.Add(p0); tri.AddRange(new[] { k, k + 1, k + 2 });
            }
            var m = new Mesh { name = "cone" }; m.SetVertices(v); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }

        static void Sprinkles(Transform parent, Vector3 center, Vector3 half, int count, int seed)
        {
            var rnd = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                Vector3 p = center + new Vector3(((float)rnd.NextDouble() * 2 - 1) * half.x, ((float)rnd.NextDouble() * 2 - 1) * half.y, ((float)rnd.NextDouble() * 2 - 1) * half.z);
                P(parent, PrimitiveType.Capsule, p, new Vector3(.006f, .012f, .006f), M("spr" + (i % 6), Candy(i), .8f), new Vector3((float)rnd.NextDouble() * 360, (float)rnd.NextDouble() * 360, 0));
            }
        }
        // red/white (or any two colours) striped stick along +Z
        static void Striped(Transform t, Vector3 start, float len, float radius, Material a, Material b, int bands)
        {
            float seg = len / bands;
            for (int i = 0; i < bands; i++)
                P(t, PrimitiveType.Cylinder, start + new Vector3(0, 0, seg * (i + .5f)), new Vector3(radius * 2, seg / 2, radius * 2), i % 2 == 0 ? a : b, new Vector3(90, 0, 0));
        }

        // ---------------- the guns
        public static Transform Create(WeaponSpec w, GunParts parts, bool firstPerson)
        {
            var pivot = new GameObject("甜品武器 " + w.id).transform;
            var t = new GameObject("model").transform; t.SetParent(pivot, false);
            float muzzleZ = .4f, top = .06f, muzzleY = .03f;
            Vector3 gripLocal = Vector3.zero;   // V22：枪 glb 里 Grip 节点（未缩放的模型空间），第一人称手臂右手对准它
            // V17：美食枪（整把枪就是一种食物）优先，其次是 V15 的甜品枪
            var glb = Resources.Load<GameObject>("Toon/FoodGuns/" + GlbName(w.id)) ?? Resources.Load<GameObject>("Toon/Dessert/" + GlbName(w.id));
            FoodGunAnim anim = null;
            if (glb != null)
            {
                var g = Object.Instantiate(glb, t, false); g.name = "Blender美食枪";
                foreach (var c in g.GetComponentsInChildren<Collider>()) Object.Destroy(c);
                // V21：外部素材包的枪自带整枪 Fire/Reload/Idle 片段，交给 FoodGunAnim 统一控制，不让它自动播放
                foreach (var a in g.GetComponentsInChildren<Animation>()) { a.Stop(); a.enabled = false; }
                foreach (var a in g.GetComponentsInChildren<Animator>()) a.enabled = false;
                var mz = FindDeep(g.transform, "Muzzle");
                if (mz != null) { var ml = t.InverseTransformPoint(mz.position); muzzleZ = ml.z; muzzleY = ml.y; }
                var gp = FindDeep(g.transform, "Grip"); if (gp != null) gripLocal = t.InverseTransformPoint(gp.position);
                anim = pivot.gameObject.AddComponent<FoodGunAnim>();
                anim.tea = FindDeep(g.transform, "TeaLevel"); anim.spin = FindDeep(g.transform, "Spin");
                if (FindDeep(g.transform, "Haw0") != null) { anim.haws = new Transform[5]; for (int i = 0; i < 5; i++) anim.haws[i] = FindDeep(g.transform, "Haw" + i); }
                var b = new Bounds(); bool any = false;
                foreach (var r in g.GetComponentsInChildren<Renderer>())
                {
                    var lb = r.bounds; var top3 = t.InverseTransformPoint(lb.max);
                    if (!any) { b = new Bounds(top3, Vector3.zero); any = true; } else b.Encapsulate(top3);
                }
                if (any) top = Mathf.Clamp(b.max.y * .8f, .04f, .12f);
                if (firstPerson)   // 第一人称：枪托贴着镜头会挡住视野，隐藏握把后方的部件
                    foreach (var r in g.GetComponentsInChildren<Renderer>())
                        if (t.InverseTransformPoint(r.bounds.center).z < -.07f) r.enabled = false;
            }
            else switch (w.id)
            {
                case "SMG-7": muzzleZ = Donut(t); top = .07f; break;
                case "AR-9": muzzleZ = IceCream(t); top = .065f; break;
                case "LMG-2": muzzleZ = Cake(t); top = .1f; break;
                case "DMR-3": muzzleZ = CandyCane(t); top = .1f; break;
                case "FS-12": muzzleZ = Cupcake(t); top = .08f; break;
                case "BR-5": muzzleZ = ChocoBar(t); top = .065f; break;
                default: muzzleZ = Lollipop(t); top = .05f; break;
            }
            float scale = firstPerson ? .62f : 1f;
            t.localScale = Vector3.one * scale;
            parts.muzzle = new GameObject("枪口").transform; parts.muzzle.SetParent(pivot, false); parts.muzzle.localPosition = new Vector3(0, muzzleY * scale, muzzleZ * scale);
            parts.eject = new GameObject("抛壳口").transform; parts.eject.SetParent(pivot, false); parts.eject.localPosition = new Vector3(.03f, .04f, .08f) * scale;
            parts.laser = parts.muzzle;
            parts.sightLocal = new Vector3(0, top * scale, .05f * scale);
            parts.foregrip = new Vector3(0, -.02f, muzzleZ * .55f) * scale;
            foreach (var r in pivot.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = firstPerson ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            var fpArms = firstPerson && glb != null ? Resources.Load<GameObject>("Toon/FPArms/FPArms_Sweater") : null;
            if (fpArms != null) AttachArms(fpArms, pivot, parts, w.id, gripLocal * scale);   // V22：甜心针织袖手臂（Tools/blender_fp_arms.py）
            else if (firstPerson)
            {
                var glove = Resources.Load<GameObject>("Toon/FoodGuns/Glove");
                foreach (var (pos, sc) in new[] { (new Vector3(.005f, -.05f, -.01f), new Vector3(.08f, .08f, .09f)), (parts.foregrip + new Vector3(-.01f, -.03f, 0), new Vector3(.075f, .075f, .085f)) })
                {
                    if (glove != null)   // V17：白色卡通手套，替换原来的深色"蛋形手"
                    {
                        var h = Object.Instantiate(glove, pivot, false); h.name = "卡通手套";
                        h.transform.localPosition = pos + new Vector3(0, .005f, .012f); h.transform.localRotation = Quaternion.Euler(-55, 0, 0); h.transform.localScale = Vector3.one * .9f;
                        foreach (var r in h.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    }
                    else { var h = P(pivot, PrimitiveType.Sphere, pos, sc, ToonAssets.Mitt); h.name = "手套"; }
                }
            }
            return pivot;
        }

        // ---------------- V22：第一人称手臂
        // FPArms_Sweater.glb 建在"枪 pivot 空间"（未缩放，握把在原点）：Arm_R/L → Forearm → Hand → Glove，Palm_R/L 是拳心。
        // 右手：Palm_R 对准 Grip 节点 + 默认偏移；左手：Palm_L 对准 parts.foregrip + 默认偏移（手枪改成托在握把左下）。
        // 每把枪的微调写在 HandTune 里（米，pivot 空间：x 右 / y 上 / z 前）。
        static readonly Vector3 RightFromGrip = new Vector3(0f, -.03f, -.004f), LeftFromForegrip = new Vector3(-.004f, -.01f, .012f), LeftPistol = new Vector3(-.03f, -.055f, .01f);
        static readonly Dictionary<string, (Vector3 r, Vector3 l)> HandTune = new Dictionary<string, (Vector3 r, Vector3 l)>
        {
            { "P-9", (Vector3.zero, new Vector3(0f, .01f, 0f)) },   // 左手托得太低，上调 1 cm { "SMG-7", (Vector3.zero, Vector3.zero) }, { "AR-9", (Vector3.zero, Vector3.zero) },
            { "LMG-2", (Vector3.zero, Vector3.zero) }, { "DMR-3", (Vector3.zero, Vector3.zero) }, { "FS-12", (Vector3.zero, Vector3.zero) }, { "BR-5", (Vector3.zero, Vector3.zero) },
        };
        static void AttachArms(GameObject pf, Transform pivot, GunParts parts, string id, Vector3 grip)
        {
            var a = Object.Instantiate(pf, pivot, false); a.name = "第一人称手臂";
            a.transform.localPosition = Vector3.zero; a.transform.localRotation = Quaternion.identity; a.transform.localScale = Vector3.one;
            foreach (var c in a.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            foreach (var r in a.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            HandTune.TryGetValue(id, out var tune);
            Vector3 rT = grip + RightFromGrip + tune.r;
            Vector3 lT = (id == "P-9" ? grip + LeftPistol : parts.foregrip + LeftFromForegrip) + tune.l;
            Snap(FindDeep(a.transform, "Arm_R"), FindDeep(a.transform, "Palm_R"), pivot, rT);
            var armL = FindDeep(a.transform, "Arm_L");
            Snap(armL, FindDeep(a.transform, "Palm_L"), pivot, lT);
            parts.arms = a.transform; parts.armL = armL; parts.palmLTarget = lT;
            if (armL != null) parts.armLRest = pivot.InverseTransformPoint(armL.position);
        }
        static void Snap(Transform arm, Transform palm, Transform pivot, Vector3 target)
        {
            if (arm == null || palm == null) return;
            arm.position += pivot.TransformPoint(target) - palm.position;
        }

        static string GlbName(string id) => id switch { "SMG-7" => "SMG7", "AR-9" => "AR9", "LMG-2" => "LMG2", "DMR-3" => "DMR3", "FS-12" => "FS12", "BR-5" => "BR5", _ => "P9" };
        static Transform FindDeep(Transform t, string n)
        {
            if (t.name == n) return t;
            foreach (Transform c in t) { var f = FindDeep(c, n); if (f != null) return f; }
            return null;
        }

        static void Grip(Transform t, Material m) { P(t, PrimitiveType.Cube, new Vector3(0, -.05f, -.01f), new Vector3(.032f, .1f, .045f), m, new Vector3(-12, 0, 0)); }

        // 棒棒糖手枪
        static float Lollipop(Transform t)
        {
            P(t, PrimitiveType.Cylinder, new Vector3(0, .02f, .03f), new Vector3(.08f, .022f, .08f), Pink, new Vector3(90, 0, 0));        // macaron shell
            P(t, PrimitiveType.Cylinder, new Vector3(0, .02f, .075f), new Vector3(.08f, .022f, .08f), Pink, new Vector3(90, 0, 0));
            P(t, PrimitiveType.Cylinder, new Vector3(0, .02f, .053f), new Vector3(.072f, .012f, .072f), Cream, new Vector3(90, 0, 0));    // filling
            Striped(t, new Vector3(0, .02f, .1f), .12f, .008f, CandyRed, White, 6);
            MeshPart(t, Torus, new Vector3(0, .02f, .235f), new Vector3(90, 0, 0), Vector3.one * .03f, Mint, "旋涡棒棒糖");
            P(t, PrimitiveType.Sphere, new Vector3(0, .02f, .235f), new Vector3(.04f, .04f, .02f), Lemon);
            Grip(t, Biscuit);
            return .25f;
        }
        // 甜甜圈冲锋枪
        static float Donut(Transform t)
        {
            P(t, PrimitiveType.Cube, new Vector3(0, .025f, .08f), new Vector3(.06f, .06f, .24f), Choco);
            for (int i = 0; i < 4; i++) P(t, PrimitiveType.Cube, new Vector3(0, .057f, -.015f + i * .062f), new Vector3(.05f, .008f, .05f), DarkChoco);   // bar squares
            for (int i = 0; i < 6; i++) P(t, PrimitiveType.Cylinder, new Vector3(0, .03f, .22f + i * .025f), new Vector3(.028f, .0125f, .028f), i % 2 == 0 ? Biscuit : Wafer, new Vector3(90, 0, 0));   // churro barrel
            MeshPart(t, Torus, new Vector3(0, -.05f, .1f), new Vector3(0, 0, 90), new Vector3(.045f, .06f, .045f), Biscuit, "甜甜圈弹鼓");
            MeshPart(t, Torus, new Vector3(.012f, -.05f, .1f), new Vector3(0, 0, 90), new Vector3(.046f, .03f, .046f), Pink, "糖霜");
            Sprinkles(t, new Vector3(.03f, -.05f, .1f), new Vector3(.005f, .045f, .045f), 14, 3);
            P(t, PrimitiveType.Cube, new Vector3(0, .01f, -.09f), new Vector3(.04f, .05f, .1f), Wafer);                                      // wafer stock
            Grip(t, DarkChoco);
            return .37f;
        }
        // 冰淇淋步枪
        static float IceCream(Transform t)
        {
            P(t, PrimitiveType.Cube, new Vector3(0, .025f, .1f), new Vector3(.055f, .055f, .3f), Wafer);
            for (int i = 0; i < 5; i++) P(t, PrimitiveType.Cube, new Vector3(0, .025f, -.03f + i * .06f), new Vector3(.058f, .058f, .006f), Biscuit);   // wafer ridges
            P(t, PrimitiveType.Cube, new Vector3(0, .028f, .36f), new Vector3(.045f, .045f, .2f), Sky);                                             // popsicle barrel
            P(t, PrimitiveType.Sphere, new Vector3(0, .028f, .46f), new Vector3(.045f, .045f, .04f), Sky);
            P(t, PrimitiveType.Cube, new Vector3(0, .052f, .33f), new Vector3(.047f, .006f, .12f), Pink);                                           // dip
            MeshPart(t, Cone, new Vector3(0, -.035f, .14f), new Vector3(180, 0, 0), new Vector3(.032f, .1f, .032f), Wafer, "甜筒弹匣");         // cone magazine (apex down)
            P(t, PrimitiveType.Sphere, new Vector3(0, -.03f, .14f), new Vector3(.068f, .05f, .068f), Mint);                                         // scoop
            P(t, PrimitiveType.Sphere, new Vector3(0, .015f, .14f), new Vector3(.02f, .02f, .02f), Cherry);
            Sprinkles(t, new Vector3(0, -.01f, .14f), new Vector3(.03f, .015f, .03f), 8, 5);
            P(t, PrimitiveType.Cube, new Vector3(0, .01f, -.13f), new Vector3(.045f, .07f, .14f), Biscuit);
            P(t, PrimitiveType.Cylinder, new Vector3(0, .065f, .08f), new Vector3(.022f, .05f, .022f), Pink, new Vector3(90, 0, 0));                // macaron sight
            Grip(t, Biscuit);
            return .48f;
        }
        // 生日蛋糕机枪
        static float Cake(Transform t)
        {
            Material[] layers = { Pink, Cream, Choco, Cream, Lemon };
            for (int i = 0; i < 5; i++) P(t, PrimitiveType.Cylinder, new Vector3(0, .03f, .02f + i * .05f), new Vector3(.13f, .025f, .13f), layers[i], new Vector3(90, 0, 0));
            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f * Mathf.PI * 2;
                P(t, PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * .065f, .03f + Mathf.Sin(a) * .065f, .25f), Vector3.one * .03f, White);   // cream ring
            }
            Striped(t, new Vector3(0, .03f, .26f), .26f, .012f, Mint, White, 8);                                                         // candle barrel
            P(t, PrimitiveType.Sphere, new Vector3(0, .03f, .54f), new Vector3(.02f, .02f, .035f), Flame);
            P(t, PrimitiveType.Cylinder, new Vector3(0, -.07f, .12f), new Vector3(.09f, .035f, .09f), Berry);                             // cupcake drum
            P(t, PrimitiveType.Sphere, new Vector3(0, -.03f, .12f), new Vector3(.1f, .05f, .1f), Pink);
            Sprinkles(t, new Vector3(0, .1f, .12f), new Vector3(.04f, .005f, .1f), 16, 7);
            P(t, PrimitiveType.Cube, new Vector3(0, .02f, -.1f), new Vector3(.05f, .08f, .14f), Biscuit);
            Grip(t, Choco);
            return .55f;
        }
        // 拐杖糖狙击枪
        static float CandyCane(Transform t)
        {
            P(t, PrimitiveType.Cube, new Vector3(0, .02f, .08f), new Vector3(.05f, .06f, .26f), Biscuit);
            Striped(t, new Vector3(0, .03f, .2f), .6f, .014f, CandyRed, White, 14);
            MeshPart(t, Torus, new Vector3(0, .03f, -.17f), new Vector3(0, 90, 0), new Vector3(.06f, .08f, .06f), CandyRed, "拐杖糖托");        // the cane hook as stock
            P(t, PrimitiveType.Cylinder, new Vector3(0, .1f, .1f), new Vector3(.05f, .1f, .05f), Pink, new Vector3(90, 0, 0));                  // swiss-roll scope
            P(t, PrimitiveType.Cylinder, new Vector3(0, .1f, .2f), new Vector3(.03f, .002f, .03f), Cream, new Vector3(90, 0, 0));
            P(t, PrimitiveType.Cylinder, new Vector3(0, .1f, 0f), new Vector3(.03f, .002f, .03f), Cream, new Vector3(90, 0, 0));
            P(t, PrimitiveType.Cube, new Vector3(0, .065f, .1f), new Vector3(.015f, .03f, .015f), DarkChoco);
            Grip(t, Biscuit);
            return .82f;
        }
        // 纸杯蛋糕霰弹炮
        static float Cupcake(Transform t)
        {
            P(t, PrimitiveType.Cube, new Vector3(0, .02f, .08f), new Vector3(.06f, .06f, .24f), Biscuit);
            for (int i = 0; i < 3; i++) P(t, PrimitiveType.Cylinder, new Vector3(0, .023f, .02f + i * .07f), new Vector3(.064f, .004f, .064f), Choco, new Vector3(90, 0, 0));   // cookie layers
            MeshPart(t, Cone, new Vector3(0, .03f, .34f), new Vector3(-90, 0, 0), new Vector3(.075f, .16f, .075f), Lemon, "纸杯");       // liner flaring toward the muzzle
            P(t, PrimitiveType.Cylinder, new Vector3(0, .03f, .34f), new Vector3(.12f, .012f, .12f), Lemon, new Vector3(90, 0, 0));
            for (int i = 0; i < 3; i++) P(t, PrimitiveType.Sphere, new Vector3(0, .03f, .36f + i * .03f), Vector3.one * (.12f - i * .03f), Pink);   // frosting swirl
            P(t, PrimitiveType.Sphere, new Vector3(0, .07f, .42f), Vector3.one * .035f, Cherry);
            Sprinkles(t, new Vector3(0, .03f, .38f), new Vector3(.05f, .05f, .02f), 14, 9);
            P(t, PrimitiveType.Cube, new Vector3(0, .01f, -.1f), new Vector3(.045f, .07f, .13f), Wafer);
            Grip(t, Choco);
            return .44f;
        }
        // 巧克力棒点射步枪
        static float ChocoBar(Transform t)
        {
            for (int x = 0; x < 2; x++) for (int z = 0; z < 5; z++)
                    P(t, PrimitiveType.Cube, new Vector3(0, .005f + x * .042f, -.02f + z * .055f), new Vector3(.058f, .038f, .05f), z % 2 == x ? Choco : DarkChoco);
            for (int i = -1; i <= 1; i++) P(t, PrimitiveType.Cylinder, new Vector3(i * .016f, .03f, .38f), new Vector3(.012f, .13f, .012f), Biscuit, new Vector3(90, 0, 0));   // pocky sticks
            for (int i = -1; i <= 1; i++) P(t, PrimitiveType.Cylinder, new Vector3(i * .016f, .03f, .42f), new Vector3(.014f, .09f, .014f), i == 0 ? Pink : Choco, new Vector3(90, 0, 0));
            P(t, PrimitiveType.Cube, new Vector3(0, -.05f, .12f), new Vector3(.035f, .08f, .05f), Wafer, new Vector3(10, 0, 0));
            P(t, PrimitiveType.Cube, new Vector3(0, .01f, -.12f), new Vector3(.045f, .07f, .13f), Wafer);
            Grip(t, DarkChoco);
            return .52f;
        }
    }
}
