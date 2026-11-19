using System.Collections.Generic;
using UnityEngine;

namespace NeonSiege
{
    // ================================================================ V26：上版遗留 + 说好没做完的
    //   GunSkin     —— 变身时第一人称的枪换皮肤（汉堡夹生菜、冰淇淋结霜、奶茶插吸管……）
    //   V26Hud      —— 敌人名字旁的菜图标 + 准星对准变身敌人时的打法提示
    //   CandyEye    —— 监控做成糖果眼睛，打坏时糖壳碎裂
    // 原文件只加一行 V26.Xxx(...) 钩子。
    public static class V26
    {
        public static int GunSkins, CandyEyes, CandyBreaks, HintShows;
        public const float HintRange = 60f;
        public static bool Debug;
        public const float AimCone = 4f;

        public static void OnBecome(Fighter f, FoodForm fm) { if (f != null && f.human && Skills.Food) { GunSkin.Attach(f, fm); GunSkins++; } }
        public static void OnFormEnd(Fighter f) { if (f == null) return; var g = f.GetComponent<GunSkin>(); if (g != null) Object.Destroy(g); }

        // 准星对准变身敌人时的一句打法
        public static string Tip(FoodForm fm)
        {
            switch (fm)
            {
                case FoodForm.Cake: return "巨型蛋糕：护甲厚，瞄头打，别被它撞";
                case FoodForm.MilkTea: return "奶茶杯：跑得快，打提前量";
                case FoodForm.Spicy: return "麻辣火锅：别贴脸，热浪会烫人";
                case FoodForm.Burger: return "汉堡：正面难打，绕后！";
                case FoodForm.IceCream: return "冰淇淋：别连吃 3 枪，会被冻住";
                case FoodForm.Onigiri: return "饭团：冲过来会滚撞，侧跳躲开";
                case FoodForm.Dark: return "黑暗料理：它自己掉血，拖时间";
                default: return "";
            }
        }

        // 屏幕中心对准的敌人（只算看得见的：被墙挡了就不算）
        public static Fighter Aimed(Fighter p)
        {
            if (p == null) return null;
            bool camOk = p.cam != null && Vector3.Distance(p.cam.transform.position, p.Head) < 1.5f;
            return Aimed(p, camOk ? p.cam.transform.position : p.Head, camOk ? p.cam.transform.forward : Quaternion.Euler(p.pitch, p.yaw, 0) * Vector3.forward);
        }
        public static Fighter Aimed(Fighter p, Vector3 o, Vector3 d)
        {
            if (p == null || Match.I == null) return null;
            var hits = Physics.RaycastAll(o, d, HintRange, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            if (Debug) { var sb = new System.Text.StringBuilder("NEON_V26AIM o=" + o.ToString("0.0") + " fwd=" + d.ToString("0.00") + " player=" + p.transform.position.ToString("0.0") + " hits:"); foreach (var h in hits) sb.Append(" " + h.collider.name + "@" + h.distance.ToString("0.0") + (h.collider.GetComponentInParent<Fighter>() != null ? "(F)" : "")); UnityEngine.Debug.Log(sb.ToString()); }
            foreach (var h in hits)
            {
                var f = h.collider.GetComponentInParent<Fighter>();
                if (f == p) continue;
                if (f == null) break;   // 先打到墙 / 掩体
                if (f.alive && f.team != p.team) return f;
                break;
            }
            // 没正好压在身上：准星 4° 以内、看得见的敌人也算"对准"（提示不需要像开枪一样精确）
            Fighter best = null; float bestA = AimCone;
            foreach (var f in Match.I.fighters)
            {
                if (f == null || f == p || !f.alive || f.team == p.team) continue;
                Vector3 to = f.Chest - o; if (to.magnitude > HintRange) continue;
                float a = Vector3.Angle(d, to); if (a >= bestA) continue;
                if (!Skills.SightClear(o, f.Chest, f)) continue;
                best = f; bestA = a;
            }
            return best;
        }
        public static bool AimHint(Fighter p, out Fighter target, out string tip) { return AimHint(p, null, out target, out tip); }
        public static bool AimHint(Fighter p, Vector3? dir, out Fighter target, out string tip)
        {
            target = null; tip = null;
            if (!Skills.Food || p == null || !p.alive) return false;
            var f = dir.HasValue ? Aimed(p, p.Head, dir.Value) : Aimed(p); if (f == null) return false;
            var fs = f.Food; if (fs == null || !fs.Transformed) return false;
            target = f; tip = Tip(fs.form); return !string.IsNullOrEmpty(tip);
        }

        // ---------------------------------------------------------------- 监控 → 糖果眼睛
        public static void CandyEye(SecurityCam cam, Transform head)
        {
            if (!Skills.Food || cam == null || head == null || head.GetComponent<CandyEye>() != null) return;
            head.gameObject.AddComponent<CandyEye>().Build(cam); CandyEyes++;
        }
        public static void CandyBreak(Vector3 at)
        {
            if (!Skills.Food) return;
            Fx.Burst(at, Vector3.up, new Color(1f, .55f, .8f), 30, 5f, .07f, .8f, 1.3f);   // 粉色糖壳
            Fx.Burst(at, Vector3.up, Color.white, 20, 4f, .06f, .7f, 1.3f);                // 白色糖壳
            Fx.Burst(at, Vector3.down, new Color(.9f, .1f, .15f), 10, 2.5f, .05f, .6f, 1f);  // 红色"眼珠"糖芯
            Sfx.Play("food_wafer_shatter", at, .9f, 1.5f, 1f, 35f);
            CandyBreaks++;
        }

        // ---------------------------------------------------------------- 地图细节（MAP-A3 北烤串巷 / MAP-A5 蒸笼门）—— 全部建在 Arena 层级外，不影响烘焙光照匹配
        public static int GateDecor, AlleyDecor, Crates;
        public static void DressMap(Arena arena)
        {
            if (arena == null || !Skills.Food) return;
            GateDecor = AlleyDecor = Crates = 0;
            if (Arena.V26Root == null) Arena.V26Root = new GameObject("V26 地图").transform;
            foreach (var s in arena.shutters) if (s != null && s.gate) { SteamerGate(s); GateDecor++; }
            KebabAlley();
        }
        static ParticleSystem Puff(Transform parent, Vector3 lp, float rate, float size, Color c)
        {
            var go = new GameObject("V26 蒸汽"); go.transform.SetParent(parent, false); go.transform.localPosition = lp;
            var ps = go.AddComponent<ParticleSystem>(); var main = ps.main; main.startLifetime = 2.2f; main.startSpeed = new ParticleSystem.MinMaxCurve(.6f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * .6f, size); main.startColor = c; main.maxParticles = 40; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = rate; var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = .25f; sh.rotation = new Vector3(-90, 0, 0);
            var col = ps.colorOverLifetime; col.enabled = true; var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .2f), new GradientAlphaKey(0, 1) }); col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.material = Resources.Load<Material>("Materials/FxSmoke") ?? UltFx.SoftMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return ps;
        }
        static void SteamerGate(Shutter s)
        {
            float w = s.Width, h = s.Height, tier = h / 3f, px = w / 2 + .3f;
            var root = new GameObject("V26 蒸笼门细节").transform; root.SetParent(Arena.V26Root, false); root.position = s.transform.position; root.rotation = s.transform.rotation;
            // 门扇上：竹篾竖条 + 每层一圈红绳（跟着门扇升降）
            if (s.Slab != null)
            {
                var slabDecor = new GameObject("V26 门扇竹篾").transform; slabDecor.SetParent(Arena.V26Root, false);
                slabDecor.gameObject.AddComponent<Follow>().target = s.Slab;
                var slat = Arena.Glow(new Color(.62f, .43f, .22f), .4f); var rope = Arena.Glow(new Color(.95f, .2f, .25f), 1.3f);
                for (int i = 0; i < 3; i++)
                {
                    float y = tier * (i + .5f);
                    for (int k = -4; k <= 4; k++)
                    {
                        if (k == 0) continue;   // 正中是包子图案
                        float x = k / 4.6f * w / 2, z = .45f * Mathf.Sqrt(Mathf.Max(0, 1 - (2 * x / w) * (2 * x / w))) + .01f;
                        foreach (float sd in new[] { 1f, -1f }) Prim(slabDecor, PrimitiveType.Cube, new Vector3(x, y, sd * z), new Vector3(.05f, tier * .7f, .02f), slat);
                    }
                    foreach (float sd in new[] { 1f, -1f }) Prim(slabDecor, PrimitiveType.Cube, new Vector3(0, y + tier * .32f, sd * .46f), new Vector3(w * .7f, .06f, .02f), rope);
                }
                // 门开着时门扇顶上冒一大团热气（MAP-A5 "baskets lifted, steam puffing"）
                var big = Puff(slabDecor, new Vector3(0, h + .3f, 0), 10, 1.8f, new Color(1f, 1f, 1f, .16f));
                big.gameObject.AddComponent<GateSteam>().gate = s;
            }
            // 立柱：红灯笼 ×4（两面各一对）+ 柱顶叠两层小蒸笼 + 一个包子
            var red = Arena.Glow(new Color(1f, .18f, .15f), 2.2f); var gold = Arena.Glow(new Color(1f, .78f, .3f), 1.6f);
            var bamboo = Arena.Glow(new Color(.9f, .72f, .45f), .6f); var bun = Arena.Glow(new Color(1f, .96f, .9f), 1.2f);
            foreach (float sx in new[] { 1f, -1f })
            {
                foreach (float sz in new[] { 1f, -1f })
                {
                    Vector3 lp = new Vector3(sx * px, h + .1f, sz * .75f);
                    Prim(root, PrimitiveType.Cube, lp + Vector3.up * .45f, new Vector3(.03f, .3f, .03f), gold);
                    Prim(root, PrimitiveType.Sphere, lp, new Vector3(.42f, .5f, .42f), red);
                    Prim(root, PrimitiveType.Cylinder, lp + Vector3.up * .26f, new Vector3(.22f, .03f, .22f), gold);
                    Prim(root, PrimitiveType.Cylinder, lp - Vector3.up * .26f, new Vector3(.22f, .03f, .22f), gold);
                    Prim(root, PrimitiveType.Cube, lp - Vector3.up * .42f, new Vector3(.04f, .18f, .04f), gold);   // 流苏
                }
                for (int k = 0; k < 2; k++) Prim(root, PrimitiveType.Cylinder, new Vector3(sx * px, h + .9f + .16f + k * .3f, 0), new Vector3(.75f, .14f, .75f), bamboo);
                Prim(root, PrimitiveType.Sphere, new Vector3(sx * px, h + .9f + .72f, 0), new Vector3(.4f, .3f, .4f), bun);
                Puff(root, new Vector3(sx * px, h + .9f + .85f, 0), 5, .9f, new Color(1f, 1f, 1f, .18f));   // 柱顶小蒸笼一直冒热气
            }
            // 门楣牌匾（两面）
            var font = Resources.Load<Font>("NotoSansSC");
            foreach (float sd in new[] { 1f, -1f })
            {
                Prim(root, PrimitiveType.Cube, new Vector3(0, h + 1.55f, sd * .56f), new Vector3(2.2f, .6f, .06f), red);
                Prim(root, PrimitiveType.Cube, new Vector3(0, h + 1.55f, sd * .55f), new Vector3(2.34f, .72f, .04f), gold);
                if (font == null) continue;
                var t = new GameObject("V26 牌匾字").AddComponent<TextMesh>(); t.transform.SetParent(root, false); t.transform.localPosition = new Vector3(0, h + 1.55f, sd * .6f);
                t.transform.localRotation = Quaternion.Euler(0, sd > 0 ? 180 : 0, 0);
                t.text = "蒸 笼 门"; t.anchor = TextAnchor.MiddleCenter; t.fontSize = 64; t.characterSize = .05f; t.color = new Color(1f, .9f, .55f); t.font = font;
                t.GetComponent<MeshRenderer>().sharedMaterial = Arena.WorldText(font);
            }
        }
        static void KebabAlley()
        {
            float x0 = Arena.LinkX0, x1 = Arena.LinkX1;
            var root = new GameObject("V26 北烤串巷细节").transform; root.SetParent(Arena.V26Root, false);
            // 灯泡串：每条灯笼线上挂 7 个小灯泡（暖白 / 橙交替）
            var b1 = Arena.Glow(new Color(1f, .9f, .6f), 3f); var b2 = Arena.Glow(new Color(1f, .55f, .3f), 3f);
            for (int i = 0; i < 4; i++) for (int k = 0; k < 7; k++)
            {
                float x = Mathf.Lerp(x0 + .4f, x1 - .4f, k / 6f), sag = Mathf.Sin(k / 6f * Mathf.PI) * .25f;
                Prim(root, PrimitiveType.Sphere, new Vector3(x, 5.32f - sag, 12.5f + i * 4.4f), Vector3.one * .14f, (i + k) % 2 == 0 ? b1 : b2);
            }
            // 烤炉台上：一排烤串（竹签 + 三块肉丸）+ 冒烟
            var stick = Arena.Glow(new Color(.85f, .7f, .45f), .4f); var meat = Arena.Glow(new Color(.75f, .32f, .18f), .9f); var pepper = Arena.Glow(new Color(.3f, .8f, .25f), 1f);
            for (int i = 0; i < 6; i++)
            {
                float x = Mathf.Lerp(x0 + .4f, -2.2f, i / 5f);
                Prim(root, PrimitiveType.Cylinder, new Vector3(x, 2.22f, 12.9f), new Vector3(.025f, .4f, .025f), stick, Quaternion.Euler(90, 0, 0));
                for (int k = 0; k < 3; k++) Prim(root, PrimitiveType.Sphere, new Vector3(x, 2.24f, 12.65f + k * .2f), Vector3.one * .13f, k == 1 ? pepper : meat);
            }
            Puff(root, new Vector3(-3.4f, 2.3f, 12.9f), 6, 1.2f, new Color(.85f, .82f, .8f, .14f));
            // 两摞彩色糖屑木箱（MAP-A3 "crates of sprinkles as cover"）：贴东墙放，有碰撞 + 寻路挖洞
            SprinkleCrate(root, new Vector3(x1 - .5f, 0, 19.6f));
            SprinkleCrate(root, new Vector3(x1 - .5f, 0, 14.4f));
            AlleyDecor = root.childCount;
        }
        static void SprinkleCrate(Transform root, Vector3 p)
        {
            var g = new GameObject("V26 糖屑木箱"); g.transform.SetParent(root, false); g.transform.position = p;
            var col = g.AddComponent<BoxCollider>(); col.size = new Vector3(.9f, 1.3f, .9f); col.center = new Vector3(0, .65f, 0);
            var ob = g.AddComponent<UnityEngine.AI.NavMeshObstacle>(); ob.shape = UnityEngine.AI.NavMeshObstacleShape.Box; ob.size = col.size + new Vector3(.1f, 0, .1f); ob.center = col.center; ob.carving = true;
            var wood = Arena.Glow(new Color(.85f, .62f, .4f), .5f); var dark = Arena.Glow(new Color(.6f, .4f, .25f), .3f);
            Prim(g.transform, PrimitiveType.Cube, new Vector3(0, .4f, 0), new Vector3(.9f, .8f, .9f), wood);
            Prim(g.transform, PrimitiveType.Cube, new Vector3(.05f, 1.05f, -.05f), new Vector3(.6f, .5f, .6f), wood, Quaternion.Euler(0, 15, 0));
            foreach (float y in new[] { .1f, .7f }) Prim(g.transform, PrimitiveType.Cube, new Vector3(0, y, 0), new Vector3(.92f, .06f, .92f), dark);
            Color[] cs = { new Color(1f, .3f, .5f), new Color(.3f, .8f, 1f), new Color(1f, .9f, .3f), new Color(.5f, 1f, .5f) };
            var rnd = new System.Random(Mathf.RoundToInt(p.z * 10));
            for (int i = 0; i < 14; i++) Prim(g.transform, PrimitiveType.Cube, new Vector3((float)rnd.NextDouble() * .5f - .25f, 1.31f, (float)rnd.NextDouble() * .5f - .25f), new Vector3(.12f, .03f, .03f), Arena.Glow(cs[i % 4], 1.5f), Quaternion.Euler(0, (float)rnd.NextDouble() * 180, 0));
            Crates++;
        }

        // ---------------------------------------------------------------- 威化坡道落点复核（V23 d 的 5 个点）
        // "不该上的高点"规则：① 离地图边 ≥ 3 m；② 上面是能站 3 个人的平台（往里 0.6–2.2 m、左右 ±1 m 的 3×3 个点 ≥ 8 个同高），
        // 1 m 深的高柜台台面不算；③ 离两边出生点 ≥ 18 m；④ 站上去看不到对方出生点（防止开局架出生点）
        public static bool RampOk(Vector3 top, Vector3 d, float h, out string why)
        {
            why = "";
            if (Mathf.Abs(top.x) > Arena.HalfX - 3 || Mathf.Abs(top.z) > Arena.HalfZ - 3) { why = "贴地图边"; return false; }
            Vector3 side = Vector3.Cross(Vector3.up, d); int n = 0;
            for (int i = 0; i < 3; i++) for (int j = -1; j <= 1; j++)
            {
                Vector3 q = top - d * (.6f + i * .8f) + side * j;
                if (Physics.Raycast(new Vector3(q.x, h + .6f, q.z), Vector3.down, out RaycastHit hh, .9f, ~0, QueryTriggerInteraction.Ignore) && Mathf.Abs(hh.point.y - h) < .15f) n++;
            }
            if (n < 8) { why = "台面太窄（" + n + "/9，像柜台台面）"; return false; }
            var ar = Arena.I; if (ar == null) return true;
            Vector3 eye = new Vector3(top.x, h + 1.6f, top.z) - d * .5f;
            foreach (bool att in new[] { true, false })
            {
                Vector3 sp = ar.SpawnPoint(att, 0);
                float fd = Vector2.Distance(new Vector2(top.x, top.z), new Vector2(sp.x, sp.z));
                if (fd < 18f) { why = "离" + (att ? "攻方" : "守方") + "出生点 " + fd.ToString("0") + " m"; return false; }
                if (fd < 80f && !Physics.Linecast(eye, sp + Vector3.up * 1.4f, ~0, QueryTriggerInteraction.Ignore)) { why = "能直接看到" + (att ? "攻方" : "守方") + "出生点"; return false; }
            }
            return true;
        }
        public static string RampZone(Vector3 p) { return Match.ContactZone(p); }

        // 小工具：不带碰撞的发光方块 / 球
        public static Transform Prim(Transform p, PrimitiveType t, Vector3 lp, Vector3 s, Material m, Quaternion? rot = null)
        {
            var o = GameObject.CreatePrimitive(t); Object.Destroy(o.GetComponent<Collider>()); o.transform.SetParent(p, false);
            o.transform.localPosition = lp; o.transform.localScale = s; if (rot.HasValue) o.transform.localRotation = rot.Value;
            var r = o.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return o.transform;
        }
    }

    // ================================================================ 第一人称枪皮肤（只给玩家自己看；换枪后自动重贴）
    public class GunSkin : MonoBehaviour
    {
        Fighter f; FoodForm fm; Transform root, gun; float born; Material frost;
        public Transform Root { get { return root; } }
        public FoodForm Form { get { return fm; } }
        public int Pieces { get { return root != null ? root.childCount : 0; } }
        public static void Attach(Fighter f, FoodForm fm)
        {
            var old = f.GetComponent<GunSkin>(); if (old != null) DestroyImmediate(old);
            var g = f.gameObject.AddComponent<GunSkin>(); g.f = f; g.fm = fm; g.born = Time.time; g.Rebuild();
        }
        // 枪在 viewGun 自己空间里的包围盒（去掉手臂、枪口火光、粒子 / 线）
        static Bounds LocalBounds(Transform gun, Transform arms)
        {
            bool any = false; var b = new Bounds();
            foreach (var r in gun.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                if (arms != null && r.transform.IsChildOf(arms)) continue;
                if (r.transform.name.StartsWith("V26")) continue;
                bool limb = false; for (var t = r.transform; t != null && t != gun; t = t.parent) { string n = t.name.ToLower(); if (n.Contains("arm") || n.Contains("hand") || n.Contains("sleeve") || n.Contains("glove") || n.Contains("手") || n.Contains("袖")) { limb = true; break; } }
                if (limb) continue;   // 截图验证：手 / 袖子算进来，冰淇淋霜罩成了一大块
                var wb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var lp = gun.InverseTransformPoint(c);
                    if (!any) { b = new Bounds(lp, Vector3.zero); any = true; } else b.Encapsulate(lp);
                }
            }
            if (!any) b = new Bounds(new Vector3(0, .05f, .15f), new Vector3(.08f, .12f, .45f));
            return b;
        }
        void Rebuild()
        {
            if (root != null) Destroy(root.gameObject);
            gun = f.ViewGun; if (gun == null) return;
            var arms = f.ViewParts != null ? f.ViewParts.arms : null;
            var b = LocalBounds(gun, arms);
            root = new GameObject("V26 枪皮肤 " + Ingredients.FormName(fm)).transform; root.SetParent(gun, false);
            float L = Mathf.Max(.15f, b.size.z), Wd = Mathf.Max(.04f, b.size.x), Hh = Mathf.Max(.05f, b.size.y);
            float top = b.max.y, cz = b.center.z, cx = b.center.x, u = L / 10f;   // u = 枪长的 1/10，所有装饰按它缩放
            Vector3 S(float x, float y, float z) { return new Vector3(x, y, z) * u; }
            switch (fm)
            {
                case FoodForm.Burger:   // 汉堡：枪身上夹两片波浪生菜 + 一片芝士 + 顶上撒芝麻
                {
                    var green = Arena.Glow(new Color(.35f, .85f, .25f), 1.1f); var cheese = Arena.Glow(new Color(1f, .78f, .15f), 1.2f); var sesame = Arena.Glow(new Color(1f, .95f, .8f), 1f);
                    for (int i = 0; i < 6; i++)
                    {
                        float z = cz - L * .3f + i * L * .1f;
                        V26.Prim(root, PrimitiveType.Cube, new Vector3(cx, top - u * .05f + Mathf.Sin(i * 1.7f) * u * .04f, z), new Vector3(Wd * 1.08f, u * .05f, u * .8f), green, Quaternion.Euler(Mathf.Sin(i * 2.3f) * 10f, 0, Mathf.Cos(i * 1.9f) * 6f));   // 截图验证：1.35 倍宽太大，像木板
                    }
                    V26.Prim(root, PrimitiveType.Cube, new Vector3(cx - Wd * .52f, top - Hh * .2f, cz), new Vector3(u * .04f, Hh * .3f, L * .2f), cheese, Quaternion.Euler(0, 0, 8));
                    for (int i = 0; i < 10; i++) V26.Prim(root, PrimitiveType.Sphere, new Vector3(cx + Random.Range(-Wd * .4f, Wd * .4f), top + u * .18f, cz + Random.Range(-L * .3f, L * .3f)), S(.12f, .07f, .2f), sesame);
                    break;
                }
                case FoodForm.IceCream:   // 冰淇淋：整把枪罩一层半透明的霜 + 顶上冒冰晶
                {
                    // 截图验证：整把罩一个半透明盒子太假 → 改成顶上一层白霜块 + 冰晶
                    frost = new Material(Shader.Find("Sprites/Default")) { color = new Color(.85f, .97f, 1f, .55f), renderQueue = 3003 };
                    for (int i = 0; i < 8; i++) V26.Prim(root, PrimitiveType.Sphere, new Vector3(cx + Random.Range(-Wd * .25f, Wd * .25f), top, cz - L * .38f + i * L * .1f), new Vector3(Wd * .8f, u * .15f, u * 1.1f), frost);
                    var ice = Arena.Glow(new Color(.75f, .95f, 1f), 1.8f);
                    for (int i = 0; i < 7; i++)
                        V26.Prim(root, PrimitiveType.Cube, new Vector3(cx + Random.Range(-Wd * .35f, Wd * .35f), top + u * .1f, cz + Random.Range(-L * .4f, L * .4f)), S(.18f, .45f + Random.value * .3f, .18f), ice, Quaternion.Euler(Random.Range(-25f, 25f), 45, Random.Range(-25f, 25f)));
                    var pink = Arena.Glow(new Color(1f, .7f, .85f), 1.2f);
                    V26.Prim(root, PrimitiveType.Sphere, new Vector3(cx, top + u * .25f, cz - L * .35f), S(1.1f, .7f, 1.1f), pink);   // 枪尾顶一球粉色冰淇淋
                    break;
                }
                case FoodForm.MilkTea:   // 奶茶：枪尾插一根粗吸管 + 侧面粘一排珍珠
                {
                    var straw = Arena.Glow(new Color(1f, .45f, .65f), 1.3f); var pearl = Arena.Glow(new Color(.3f, .17f, .1f), .5f);
                    V26.Prim(root, PrimitiveType.Cylinder, new Vector3(cx, top + u * 1.3f, cz - L * .3f), S(.35f, 1.5f, .35f), straw, Quaternion.Euler(-18, 0, 0));
                    for (int i = 0; i < 6; i++) V26.Prim(root, PrimitiveType.Sphere, new Vector3(cx + Wd * .55f, b.center.y - Hh * .1f + (i % 2) * u * .3f, cz - L * .25f + i * L * .1f), S(.38f, .38f, .38f), pearl);
                    break;
                }
                case FoodForm.Cake:   // 蛋糕：顶上一排奶油花 + 一根小蜡烛
                {
                    var cream = Arena.Glow(new Color(1f, .97f, .92f), 1.3f); var berry = Arena.Glow(new Color(1f, .2f, .3f), 1.3f); var candle = Arena.Glow(new Color(.5f, .8f, 1f), 1.2f); var flame = Arena.Glow(new Color(1f, .8f, .3f), 3f);
                    for (int i = 0; i < 5; i++) V26.Prim(root, PrimitiveType.Sphere, new Vector3(cx, top + u * .15f, cz - L * .3f + i * L * .15f), S(.55f, .4f, .55f), cream);
                    V26.Prim(root, PrimitiveType.Sphere, new Vector3(cx, top + u * .45f, cz), S(.3f, .3f, .3f), berry);
                    V26.Prim(root, PrimitiveType.Cylinder, new Vector3(cx, top + u * .7f, cz - L * .3f), S(.12f, .5f, .12f), candle);
                    V26.Prim(root, PrimitiveType.Sphere, new Vector3(cx, top + u * 1.3f, cz - L * .3f), S(.15f, .25f, .15f), flame);
                    break;
                }
                case FoodForm.Spicy:   // 火锅：侧面绑两根红辣椒
                {
                    var chili = Arena.Glow(new Color(1f, .15f, .08f), 1.8f); var stem = Arena.Glow(new Color(.3f, .7f, .2f), 1f);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float x = cx + s * Wd * .6f;
                        V26.Prim(root, PrimitiveType.Capsule, new Vector3(x, b.center.y, cz), S(.35f, L * .32f / u, .35f), chili, Quaternion.Euler(90, 0, 0));
                        V26.Prim(root, PrimitiveType.Cylinder, new Vector3(x, b.center.y, cz - L * .35f), S(.1f, .2f, .1f), stem, Quaternion.Euler(90, 0, 0));
                    }
                    break;
                }
                case FoodForm.Onigiri:   // 饭团：中间裹一圈海苔 + 撒米粒
                {
                    var nori = Arena.Glow(new Color(.08f, .2f, .12f), .3f); var rice = Arena.Glow(new Color(1f, 1f, .97f), 1.2f);
                    V26.Prim(root, PrimitiveType.Cube, new Vector3(cx, b.center.y, cz), new Vector3(Wd * 1.12f, Hh * 1.05f, L * .18f), nori);
                    for (int i = 0; i < 14; i++) V26.Prim(root, PrimitiveType.Sphere, new Vector3(cx + Random.Range(-Wd * .45f, Wd * .45f), top + u * .05f, cz + Random.Range(-L * .4f, L * .4f)), S(.1f, .08f, .18f), rice, Quaternion.Euler(0, Random.Range(0, 180f), 0));
                    break;
                }
                default:   // 黑暗料理：一坨坨往下滴的绿色黏液
                {
                    var goo = Arena.Glow(new Color(.35f, .75f, .2f), 1.6f);
                    for (int i = 0; i < 6; i++) V26.Prim(root, PrimitiveType.Sphere, new Vector3(cx + Random.Range(-Wd * .4f, Wd * .4f), b.min.y + Random.Range(0f, Hh * .5f), cz + Random.Range(-L * .4f, L * .4f)), S(.3f, .5f, .3f), goo);
                    break;
                }
            }
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.gameObject.layer = gun.gameObject.layer;
        }
        void LateUpdate()
        {
            if (f == null || !f.alive || f.Food == null || !f.Food.Transformed) { Destroy(this); return; }
            if (f.ViewGun != gun) Rebuild();   // 换枪 / 重建手臂后重贴
            if (root == null) return;
            float age = Time.time - born, pop = age < .2f ? Mathf.Lerp(.2f, 1.1f, age / .2f) : Mathf.Lerp(1.1f, 1f, Mathf.Clamp01((age - .2f) / .15f));
            root.localScale = Vector3.one * pop;
            if (frost != null) { var c = frost.color; c.a = .5f + .1f * Mathf.Sin(Time.time * 3f); frost.color = c; }
        }
        void OnDestroy() { if (root != null) Destroy(root.gameObject); if (frost != null) Destroy(frost); }
    }

    // ================================================================ 糖果眼睛监控：白色糖球眼 + 红色虹膜（被接管变粉）+ 后面一个糖纸拧结
    public class CandyEye : MonoBehaviour
    {
        SecurityCam cam; Material iris; Transform eye;
        public Transform Eye { get { return eye; } }
        public Color IrisColor { get { return iris != null ? iris.color : Color.clear; } }
        public void Build(SecurityCam c)
        {
            cam = c;
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;   // 藏起原来的普通摄像头
            // 糖果眼睛建在 Arena 层级外（监控在 Arena 里，多出渲染器会让烘焙光照对不上），每帧跟着镜头
            if (Arena.V26Root == null) Arena.V26Root = new GameObject("V26 地图").transform;
            var holder = new GameObject("V26 糖果眼睛").transform; holder.SetParent(Arena.V26Root, false);
            var fol = holder.gameObject.AddComponent<Follow>(); fol.target = transform; fol.mirrorActive = true;
            eye = new GameObject("眼球").transform; eye.SetParent(holder, false); eye.localPosition = new Vector3(0, 0, .05f);
            var white = Arena.Glow(new Color(1f, .97f, .97f), 1.4f);
            V26.Prim(eye, PrimitiveType.Sphere, Vector3.zero, Vector3.one * .42f, white);
            // 糖果螺旋条纹（三圈粉色斜条）
            var stripe = Arena.Glow(new Color(1f, .4f, .7f), 1.5f);
            for (int i = 0; i < 3; i++) V26.Prim(eye, PrimitiveType.Cylinder, Vector3.zero, new Vector3(.43f, .012f, .43f), stripe, Quaternion.Euler(0, i * 60f, 70f));
            iris = new Material(Arena.Glow(new Color(1f, .12f, .12f), 2f));
            V26.Prim(eye, PrimitiveType.Cylinder, new Vector3(0, 0, .19f), new Vector3(.2f, .015f, .2f), iris, Quaternion.Euler(90, 0, 0));
            V26.Prim(eye, PrimitiveType.Cylinder, new Vector3(0, 0, .205f), new Vector3(.09f, .01f, .09f), Arena.Glow(new Color(.05f, .02f, .04f), .1f), Quaternion.Euler(90, 0, 0));
            V26.Prim(eye, PrimitiveType.Sphere, new Vector3(.04f, .04f, .2f), Vector3.one * .03f, Arena.Glow(Color.white, 3f));   // 高光
            // 后面：糖纸拧结（两片三角）
            var wrap = Arena.Glow(new Color(.55f, .85f, 1f), 1.2f);
            V26.Prim(eye, PrimitiveType.Cube, new Vector3(.09f, 0, -.26f), new Vector3(.16f, .2f, .02f), wrap, Quaternion.Euler(0, 60, 45));
            V26.Prim(eye, PrimitiveType.Cube, new Vector3(-.09f, 0, -.26f), new Vector3(.16f, .2f, .02f), wrap, Quaternion.Euler(0, -60, 45));
        }
        void Update()
        {
            if (iris == null || cam == null) return;
            bool hij = V25.HijackedBy(cam) >= 0, gum = LinkPlus.Gummed(cam);
            Color c = hij ? new Color(1f, .45f, .9f) : gum ? new Color(.5f, .3f, .4f) : new Color(1f, .12f, .12f);
            float k = gum ? .3f : .7f + .3f * Mathf.Sin(Time.time * (hij ? 12f : 4f));
            iris.color = c * .3f * k; iris.SetColor("_EmissionColor", c * k * 1.5f);
            if (eye != null) eye.localScale = Vector3.one * (1f + .04f * Mathf.Sin(Time.time * 2f));   // 眼睛轻轻"呼吸"
        }
        void OnDestroy() { if (iris != null) Destroy(iris); if (eye != null && eye.parent != null) Destroy(eye.parent.gameObject); }
    }

    // 跟随另一个物体（Arena 层级外的装饰跟着门扇动）
    public class Follow : MonoBehaviour
    {
        public Transform target; public bool mirrorActive;
        void LateUpdate()
        {
            if (target == null) return;
            transform.position = target.position; transform.rotation = target.rotation;
            if (mirrorActive) { bool on = target.gameObject.activeInHierarchy; for (int i = 0; i < transform.childCount; i++) if (transform.GetChild(i).gameObject.activeSelf != on) transform.GetChild(i).gameObject.SetActive(on); }
        }
    }
    // 蒸笼门门扇上的大团热气：只有门开着（门扇升起、不在动）时冒
    public class GateSteam : MonoBehaviour
    {
        public Shutter gate; ParticleSystem ps;
        void Start() { ps = GetComponent<ParticleSystem>(); }
        void LateUpdate() { if (gate == null || ps == null) return; var em = ps.emission; em.enabled = gate.open && !gate.Moving; }
    }

    // ================================================================ HUD：敌人头上的菜图标 + 准星打法提示
    public class V26Hud : MonoBehaviour
    {
        public static V26Hud I;
        public static void Ensure() { if (I == null) I = new GameObject("V26 HUD").AddComponent<V26Hud>(); }
        static GUIStyle st, icon; static Texture2D disc;
        public static string LastTip; public static float LastTipAt = -9;
        static void Styles()
        {
            if (st != null) return;
            var font = Resources.Load<Font>("NotoSansSC");
            st = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 15, fontStyle = FontStyle.Bold }; if (font != null) st.font = font;
            icon = new GUIStyle(st) { fontSize = 13 };
            disc = new Texture2D(32, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) { float u = (x + .5f) / 16f - 1, v = (y + .5f) / 16f - 1; disc.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01((1 - Mathf.Sqrt(u * u + v * v)) * 12f))); }
            disc.Apply();
        }
        static void Shadowed(Rect r, string s, GUIStyle g, Color c)
        {
            var o = GUI.color; GUI.color = new Color(.1f, .05f, .08f, c.a * .85f); GUI.Label(new Rect(r.x + 1.5f, r.y + 2, r.width, r.height), s, g); GUI.color = c; GUI.Label(r, s, g); GUI.color = o;
        }
        public static void FoodIcon(Rect r, FoodForm fm, float a)
        {
            Styles(); var o = GUI.color; Color c = Ingredients.FormColor(fm);
            GUI.color = new Color(.1f, .05f, .08f, .8f * a); GUI.DrawTexture(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), disc);
            GUI.color = new Color(c.r, c.g, c.b, a); GUI.DrawTexture(r, disc);
            string n = Ingredients.FormName(fm); string ch = fm == FoodForm.Cake ? "糕" : fm == FoodForm.Spicy ? "辣" : fm == FoodForm.Dark ? "暗" : n.Length > 0 ? n.Substring(0, 1) : "?";
            GUI.color = new Color(.12f, .05f, .08f, a); GUI.Label(r, ch, icon); GUI.color = o;
        }
        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !Skills.Food) return;
            var m = Match.I; var p = m != null ? m.player : null; if (m == null || p == null || !p.alive || CamView.Active) return;
            var cam = Camera.main; if (cam == null) return;
            GUI.depth = 13; Styles();
            // 已暴露的变身敌人：名字上方画菜图标（GameUI 画名字在 头 + 0.5 m 处、文字在上方 38 px）
            if (!(Time.time < p.jammedUntil))
                foreach (var f in m.fighters)
                {
                    if (!f.alive || f.team == p.team || !f.Revealed) continue;
                    var fs = f.Food; if (fs == null || !fs.Transformed) continue;
                    Vector3 sp = cam.WorldToScreenPoint(f.Head + Vector3.up * .5f); if (sp.z < 0) continue;
                    float x = sp.x, y = Screen.height - sp.y;
                    FoodIcon(new Rect(x - 12, y - 66, 24, 24), fs.form, 1f);
                }
            // 准星对准变身敌人 → 准星下方一句打法
            if (V26.AimHint(p, out Fighter t, out string tip))
            {
                if (LastTip != tip || Time.time - LastTipAt > 1f) V26.HintShows++;
                LastTip = tip; LastTipAt = Time.time;
                float cx = Screen.width / 2f, cy = Screen.height / 2f; Color c = Ingredients.FormColor(t.Food.form);
                FoodIcon(new Rect(cx - 150, cy + 40, 22, 22), t.Food.form, 1f);
                Shadowed(new Rect(cx - 124, cy + 38, 300, 26), tip, new GUIStyle(st) { alignment = TextAnchor.MiddleLeft }, Color.Lerp(c, Color.white, .35f));
            }
        }
    }
}
