using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace NeonSiege
{
    // ================================================================ V23 d：联动放大（设计稿_V23 §4 推荐档 + §5 副联动；拍板 4 乙 / 5 甲）
    // 整片蒸汽（蒸汽阀 / 关口火锅"开大火" / 棉花糖蒸汽 / 开墙蛋糕粉尘）、芥末呛雾地图层、威化坡道、点酱、糊监控、可乐炸罐、果冻粘身、入口保底。
    // 新增物件（开大火旋钮、坡道点 / 酱料点标记）都建在 Arena 层级之外、ArenaLightmaps.Apply 之后（同 FoodScenery），不影响烘焙匹配。
    public enum SteamKind { Valve, Candy, Dust }

    // ---------------------------------------------------------------- 整片蒸汽：一个竖直圆柱（半径 R、离地 0–4.6 m）
    // 视线规则（SightClear 经 SteamCloud.Blocks 调到这里，玩家和 AI 同一判定）：
    //   ① 任一端在雾里：两点距离 > SteamInsideSight（2.5 m）就看不见；② 两端都在雾外：线段穿过圆柱就看不见（判定半径 = 显示半径，不再 ×0.8）。
    // 子弹不受影响（照样能盲射）；爆炸走 BlastClear，不受影响（往雾里扔雷是标准反制）。
    public class SteamArea : MonoBehaviour
    {
        public static readonly List<SteamArea> Live = new List<SteamArea>();
        public const float Height = 4.6f;
        public SteamKind kind; public Object source; public Fighter by, candyOwner; public int team = -1;
        public float R, born, until, total, closeAt = -1, baseY, candyUntil = -1; public Vector3 center;
        readonly List<Transform> puffs = new List<Transform>(); readonly List<Vector3> puffBase = new List<Vector3>(); readonly List<Renderer> rends = new List<Renderer>();
        ParticleSystem ps; bool tintedCandy;
        void OnEnable() { Live.Add(this); }
        void OnDisable() { Live.Remove(this); }

        public bool Blind { get { return kind != SteamKind.Dust; } }
        bool viewInside;
        // 镜头进了这片雾：关掉这片雾自己的雾团模型（贴脸的不透明雾团会把 2.5 m 内也挡死），里面的样子交给近距离雾 + 泛白
        public void SetInsideView(bool on)
        {
            if (viewInside == on) return; viewInside = on;
            foreach (var r in rends) if (r != null) r.enabled = !on;
            if (ps != null) { var pr = ps.GetComponent<ParticleSystemRenderer>(); if (pr != null) pr.enabled = !on; }
        }
        public bool Alive { get { return Time.time < until; } }
        public bool Candy { get { return kind == SteamKind.Candy || Time.time < candyUntil; } }
        public static int BlindCount { get { int n = 0; foreach (var a in Live) if (a != null && a.Blind && a.Alive) n++; return n; } }
        public static bool CanSpawnBlind { get { return BlindCount < Config.SteamAreaMax; } }
        // 当前生效的判定半径：1 s 膨开（从 15% 起）、关阀后 1.75 s 收掉；粉尘最后 0.5 s 收到一半
        public float Radius
        {
            get
            {
                if (Time.time >= until) return 0f;
                float age = Time.time - born, bloom = kind == SteamKind.Dust ? .12f : Config.SteamBloom;
                float k = Mathf.SmoothStep(.15f, 1f, Mathf.Clamp01(age / bloom));
                if (closeAt >= 0) k *= Mathf.Clamp01((until - Time.time) / Config.SteamCloseFade);
                else if (kind == SteamKind.Dust) k *= .5f + .5f * Mathf.Clamp01((until - Time.time) / .5f);
                return R * k;
            }
        }
        public bool Inside(Vector3 p, float pad = 0f)
        {
            float r = Radius; if (r <= .05f) return false;
            if (p.y < baseY - .5f || p.y > baseY + Height) return false;
            return Links.Flat(p, center) <= r + pad;
        }
        bool Crosses(Vector3 a, Vector3 b, float r)
        {
            float dx = b.x - a.x, dz = b.z - a.z, fx = a.x - center.x, fz = a.z - center.z;
            float A = dx * dx + dz * dz, B = 2 * (fx * dx + fz * dz), C = fx * fx + fz * fz - r * r;
            float t0, t1;
            if (A < 1e-6f) { if (C > 0) return false; t0 = 0; t1 = 1; }
            else
            {
                float disc = B * B - 4 * A * C; if (disc < 0) return false;
                float sq = Mathf.Sqrt(disc); t0 = Mathf.Max(0, (-B - sq) / (2 * A)); t1 = Mathf.Min(1, (-B + sq) / (2 * A));
                if (t0 > t1) return false;
            }
            float ya = a.y + (b.y - a.y) * t0, yb = a.y + (b.y - a.y) * t1;
            return Mathf.Max(ya, yb) >= baseY - .3f && Mathf.Min(ya, yb) <= baseY + Height;
        }
        public static bool BlocksSight(Vector3 a, Vector3 b)
        {
            float lim = Config.SteamInsideSight * Config.SteamInsideSight;
            for (int i = 0; i < Live.Count; i++)
            {
                var x = Live[i]; if (x == null || !x.Alive) continue;
                float r = x.Radius; if (r < .3f) continue;
                if (x.Inside(a) || x.Inside(b)) { if ((a - b).sqrMagnitude > lim) return true; continue; }
                if (x.Crosses(a, b, r)) return true;
            }
            return false;
        }
        public static SteamArea Of(Object src) { foreach (var a in Live) if (a != null && a.Alive && a.source == src) return a; return null; }
        public static SteamArea NearBlind(Vector3 p, float r) { foreach (var a in Live) if (a != null && a.Blind && a.Alive && a.closeAt < 0 && Links.Flat(a.center, p) <= r) return a; return null; }
        public static SteamArea BlindAt(Vector3 p) { foreach (var a in Live) if (a != null && a.Blind && a.Alive && a.Inside(p)) return a; return null; }
        public static void EndAll() { foreach (var a in Live.ToArray()) if (a != null) { a.until = 0; Destroy(a.gameObject); } }

        public static SteamArea Spawn(SteamKind k, Object src, Vector3 c, float r, float life, Fighter by)
        {
            if (k != SteamKind.Dust && !CanSpawnBlind) return null;
            var go = new GameObject(k == SteamKind.Dust ? "蛋糕粉尘" : k == SteamKind.Candy ? "整片棉花糖蒸汽" : "整片蒸汽");
            var a = go.AddComponent<SteamArea>();
            a.kind = k; a.source = src; a.by = by; a.team = by != null ? by.team : -1; a.R = r; a.born = Time.time; a.until = Time.time + life; a.total = life; a.center = c;
            a.baseY = c.y - (k == SteamKind.Dust ? 1.2f : 1.8f);   // 中心都是"源 + 1.8 m"（阀门 / 锅 / 回血炉）或"洞口 + 1.2 m"（粉尘）；不用向下打射线，锅沿会被当成地面
            go.transform.position = new Vector3(c.x, a.baseY, c.z);
            a.Build(k == SteamKind.Dust ? new Color(.95f, .85f, .7f) : k == SteamKind.Candy ? new Color(1f, .74f, .92f) : new Color(.97f, .98f, 1f));
            Skills.spawned.Add(go);
            LinkPlus.OnAreaSpawned(a);
            return a;
        }
        public void End()
        {
            if (closeAt >= 0) return;
            closeAt = Time.time; until = Mathf.Min(until, Time.time + Config.SteamCloseFade);
            if (ps != null) { var em = ps.emission; em.enabled = false; }
        }
        public void SetCandy(Fighter owner, float untilT)
        {
            candyOwner = owner; candyUntil = untilT;
            if (kind != SteamKind.Valve) kind = SteamKind.Candy;
            until = Mathf.Max(until, untilT); total = Mathf.Max(total, until - born);
            Tint(true);
        }
        void Tint(bool candy)
        {
            tintedCandy = candy;
            Color pink = new Color(1f, .7f, .9f), white = new Color(1f, .96f, .98f), steam = new Color(.97f, .98f, 1f);
            for (int i = 0; i < rends.Count; i++) if (rends[i] != null) rends[i].material.color = candy ? Color.Lerp(pink, white, (i * .37f) % 1f) : steam;
            if (ps != null) { var main = ps.main; main.startColor = candy ? new Color(1f, .78f, .94f, .85f) : new Color(1f, 1f, 1f, .8f); }
        }
        void Build(Color col)
        {
            bool dust = kind == SteamKind.Dust;
            int n = dust ? 7 : Mathf.Clamp(Mathf.RoundToInt(R * 2.4f), 8, 24);
            float size = dust ? 1.9f : Mathf.Clamp(R * .62f, 2.6f, 5.2f);
            var rnd = new System.Random(Mathf.RoundToInt(center.x * 7 + center.z * 13));
            for (int layer = 0; layer < (dust ? 1 : 2); layer++)
            {
                int m = layer == 0 ? n : n / 2;
                for (int i = 0; i < m; i++)
                {
                    GameObject puff = Arena.Model("Toon/Kitchen/Cloud_" + (1 + (i + layer) % 3));
                    if (puff == null) puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    foreach (var c in puff.GetComponentsInChildren<Collider>()) Destroy(c);
                    Arena.Fit(puff, new Vector3(size * (layer == 0 ? 1f : .85f), 0, 0));
                    var holder = new GameObject("雾团").transform; holder.SetParent(transform, false);
                    puff.transform.SetParent(holder, false);
                    float rr = R * (layer == 0 ? .8f : .55f) * Mathf.Sqrt((i + .5f) / m), ang = i * 2.39996f + layer;
                    Vector3 off = new Vector3(Mathf.Cos(ang) * rr, (layer == 0 ? .5f : 2.3f) + (float)rnd.NextDouble() * (dust ? .8f : 1.3f), Mathf.Sin(ang) * rr);
                    holder.localPosition = off; holder.localRotation = Quaternion.Euler(0, (float)rnd.NextDouble() * 360f, 0);
                    foreach (var r in puff.GetComponentsInChildren<Renderer>()) { r.material.color = col; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rends.Add(r); }
                    puffs.Add(holder); puffBase.Add(off);
                }
            }
            ps = new GameObject("雾").AddComponent<ParticleSystem>(); ps.transform.SetParent(transform, false); ps.transform.localPosition = Vector3.up * (dust ? .6f : 1f); ps.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var main = ps.main; main.startLifetime = new ParticleSystem.MinMaxCurve(2.4f, 3.4f); main.startSpeed = new ParticleSystem.MinMaxCurve(.2f, .7f);
            main.startSize = dust ? new ParticleSystem.MinMaxCurve(1.2f, 2.4f) : new ParticleSystem.MinMaxCurve(3f, 5.5f);
            main.startColor = new Color(col.r, col.g, col.b, .8f); main.maxParticles = dust ? 120 : 450; main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = dust ? .05f : -.01f;
            var em = ps.emission; em.rateOverTime = dust ? 50 : R * 7f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = R * .85f;
            ps.GetComponent<ParticleSystemRenderer>().material = Fx.Smoke;
            if (dust) { Fx.Burst(center, Vector3.up, col, 40, 4f, .14f, .9f, .4f, Fx.Smoke); Fx.Burst(center, Vector3.up, new Color(.75f, .5f, .3f), 30, 5f, .05f, .8f, 1.2f); }
            transform.localScale = Vector3.one * .25f;
        }
        void Update()
        {
            float age = Time.time - born, left = until - Time.time;
            if (left <= -.05f) { Destroy(gameObject); return; }
            if (closeAt < 0 && kind != SteamKind.Dust && SteamCloud.SuppressedAt(center))
            {
                End();
                if (Match.I != null) Match.I.Feed(Candy ? "冰淇淋吹散了热气 · 棉花糖蒸汽一起散了" : "冰淇淋吹散了整片白雾", new Color(.7f, .95f, 1f));
            }
            float bloom = kind == SteamKind.Dust ? .15f : Config.SteamBloom;
            float k = Mathf.SmoothStep(.25f, 1f, Mathf.Clamp01(age / bloom));
            if (closeAt >= 0) k *= Mathf.Clamp01(left / Config.SteamCloseFade);
            else if (left < 1.2f) k *= .78f + .22f * Mathf.Clamp01(left / 1.2f);   // 最后 1.2 s 变薄
            transform.localScale = Vector3.one * Mathf.Max(.02f, k);
            for (int i = 0; i < puffs.Count; i++) if (puffs[i] != null) puffs[i].localPosition = puffBase[i] + new Vector3(0, Mathf.Sin(Time.time * 1.1f + i) * .12f, 0);
            if (tintedCandy && kind == SteamKind.Valve && Time.time >= candyUntil) Tint(false);
            if (Time.time < candyUntil || kind == SteamKind.Candy)
            {
                // 棉花糖蒸汽：里面的敌人黏脚（= 迷雾原效果 ×NoiseSlow）
                if (candyOwner != null && Match.I != null)
                    foreach (var f in Match.I.fighters)
                        if (f.alive && f.team != candyOwner.team && Inside(f.transform.position)) f.jammedUntil = Mathf.Max(f.jammedUntil, Time.time + .15f);
            }
            if (Blind && Match.I != null && Match.I.Cur != null) Match.I.Cur.steamArea += Time.deltaTime;
        }
    }

    // ---------------------------------------------------------------- 关口火锅"开大火"旋钮：同 SteamValve 规则（长按 0.8 s，整片 9 m / 10 s / 冷却 40 s，进雾关阀）
    public class HotpotKnob : SteamValve
    {
        public HotpotHazard pot;
        public override Vector3 CloudCenter { get { return pot != null ? pot.SteamCenter : base.CloudCenter; } }
        public override string Noun { get { return "大火"; } }
        public override string OpenVerb { get { return "开"; } }
        public override string MarkerLabel(Fighter p)
        {
            if (Active) return "火锅开着大火 · 走进去按住 G 关火";
            if (CooldownLeft > 0) return "火锅旋钮 · 还要 " + Mathf.CeilToInt(CooldownLeft) + " 秒";
            if (!SteamArea.CanSpawnBlind) return "火锅旋钮 · 全图已有 2 片白雾";
            return "G 开大火 · 整片蒸汽";
        }
        public void BuildKnob(HotpotHazard h)
        {
            pot = h; openHold = holdTime = Config.KnobHold; range = 2.2f; areaRadius = Config.SteamAreaPot;
            Material M(Color c, float g) { var m = new Material(Shader.Find("Standard")); m.color = c; m.SetFloat("_Glossiness", g); return m; }
            var red = M(new Color(.9f, .22f, .2f), .5f); var gold = M(new Color(1f, .78f, .3f), .6f); var dark = M(new Color(.35f, .2f, .15f), .3f);
            GameObject Part(PrimitiveType t, Vector3 lp, Vector3 s, Material m, Transform parent = null)
            {
                var o = GameObject.CreatePrimitive(t); Object.Destroy(o.GetComponent<Collider>()); o.transform.SetParent(parent != null ? parent : transform, false);
                o.transform.localPosition = lp; o.transform.localScale = s; var r = o.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; return o;
            }
            Part(PrimitiveType.Cylinder, new Vector3(0, .45f, 0), new Vector3(.16f, .45f, .16f), dark);           // 燃气杆
            Part(PrimitiveType.Cube, new Vector3(0, .95f, 0), new Vector3(.42f, .3f, .2f), gold);                 // 面板
            wheel = new GameObject("火力旋钮").transform; wheel.SetParent(transform, false); wheel.localPosition = new Vector3(0, .95f, .14f); wheel.localRotation = Quaternion.Euler(90, 0, 0);
            Part(PrimitiveType.Cylinder, Vector3.zero, new Vector3(.24f, .05f, .24f), red, wheel);
            Part(PrimitiveType.Cube, new Vector3(0, .06f, .07f), new Vector3(.04f, .04f, .12f), gold, wheel);      // 指针
            var flame = Part(PrimitiveType.Sphere, new Vector3(0, 1.25f, 0), new Vector3(.16f, .22f, .16f), Arena.Glow(new Color(1f, .5f, .15f), 4f)); flame.name = "小火苗";
            var font = Resources.Load<Font>("NotoSansSC");
            var t = new GameObject("开大火字").AddComponent<TextMesh>(); t.transform.SetParent(transform, false); t.transform.localPosition = new Vector3(0, 1.55f, 0);
            t.text = "开大火"; t.anchor = TextAnchor.MiddleCenter; t.fontSize = 64; t.characterSize = .05f; t.color = new Color(1f, .55f, .3f); t.font = font;
            if (font != null) t.GetComponent<MeshRenderer>().sharedMaterial = Arena.WorldText(font);
            t.gameObject.AddComponent<FaceCam>();
            MakeJet(transform.InverseTransformPoint(h.transform.position + Vector3.up * 1.2f));
            lamp = new GameObject("火光").AddComponent<Light>(); lamp.transform.SetParent(transform, false); lamp.transform.localPosition = new Vector3(0, 1.3f, 0);
            lamp.color = new Color(1f, .5f, .2f); lamp.range = 3f; lamp.intensity = 1f;
        }
    }
    public class FaceCam : MonoBehaviour { void LateUpdate() { var c = Camera.main; if (c != null) transform.rotation = Quaternion.LookRotation(transform.position - c.transform.position); } }

    // ---------------------------------------------------------------- 威化坡道（铆钉副联动）
    public class WaferRamp : MonoBehaviour, IDamageable
    {
        public static readonly List<WaferRamp> Live = new List<WaferRamp>();
        public Fighter owner; public LinkPlus.RampSpot spot; float born, hp; Transform vis; int layers; OffMeshLink link;
        public float Hp { get { return hp; } }
        public float Length { get; private set; }
        void OnEnable() { Live.Add(this); }
        void OnDisable() { Live.Remove(this); if (spot != null && spot.ramp == this) spot.ramp = null; }
        public void Init(Fighter f, LinkPlus.RampSpot s)
        {
            owner = f; spot = s; born = Time.time; hp = Config.CoverHp * (ToonAssets.Available ? 1.3f : 1f); s.ramp = this;
            Vector3 top = s.top, bot = s.basePos; Length = Vector3.Distance(top, bot) + .5f;
            transform.position = (top + bot) / 2f; transform.rotation = Quaternion.LookRotation(top - bot, Vector3.up);
            var col = gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(2.6f, .25f, Length); col.center = new Vector3(0, -.125f, 0);
            vis = new GameObject("威化坡道").transform; vis.SetParent(transform, false);
            for (int i = 0; i < 3; i++)
            {
                GameObject w = ToonAssets.Available ? Arena.Model("Toon/Food/WaferBlock") : null;
                if (w == null) { w = GameObject.CreatePrimitive(PrimitiveType.Cube); w.GetComponent<Renderer>().sharedMaterial = Arena.Glow(Skills.Wafer, 0f); }
                foreach (var c in w.GetComponentsInChildren<Collider>()) Destroy(c);
                Arena.Fit(w, Vector3.one);   // 先在世界原点缩成 1×1×1（同 DeployedCover 的做法），再放进按尺寸缩放的格子里
                var cell = new GameObject("威化段").transform; cell.SetParent(vis, false);
                cell.localScale = new Vector3(2.6f, .25f, Length / 3f + .02f); cell.localPosition = new Vector3(0, -.125f, -Length / 2f + Length / 3f * (i + .5f));
                w.transform.SetParent(cell, false); w.transform.localPosition = w.GetComponent<Renderer>() != null && w.GetComponent<MeshFilter>() != null && w.GetComponent<MeshFilter>().sharedMesh.name == "Cube" ? Vector3.zero : new Vector3(0, -.5f, 0); w.transform.localRotation = Quaternion.identity;
            }
            var gold = Arena.Glow(Links.Gold, 3f);
            foreach (float sx in new[] { -1f, 1f })
            {
                var e = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(e.GetComponent<Collider>()); e.name = "金边蜂蜜"; e.transform.SetParent(vis, false);
                e.transform.localPosition = new Vector3(sx * 1.32f, 0, 0); e.transform.localScale = new Vector3(.08f, .3f, Length); e.GetComponent<Renderer>().sharedMaterial = gold;
            }
            var strip = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(strip.GetComponent<Collider>()); strip.transform.SetParent(vis, false);
            strip.transform.localPosition = new Vector3(0, .01f, 0); strip.transform.localScale = new Vector3(.25f, .02f, Length * .9f); strip.GetComponent<Renderer>().sharedMaterial = Arena.Glow(Config.TeamColor(f.team), 2.5f);
            vis.localScale = new Vector3(1, 1, .1f);
            // AI 也能走：坡底 ↔ 坡顶一条导航连接
            var a = new GameObject("坡底").transform; a.SetParent(transform, false); a.position = bot - (top - bot).normalized * .3f;
            var z = new GameObject("坡顶").transform; z.SetParent(transform, false); z.position = top + new Vector3(top.x - bot.x, 0, top.z - bot.z).normalized * .6f;
            link = gameObject.AddComponent<OffMeshLink>(); link.startTransform = a; link.endTransform = z; link.biDirectional = true; link.costOverride = 1.5f;
            Skills.spawned.Add(gameObject);
        }
        static Bounds GetBounds(GameObject g)
        {
            var rs = g.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
            Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            b.center = g.transform.InverseTransformPoint(b.center); return b;
        }
        void Update()
        {
            float age = Time.time - born, k = Mathf.Clamp01(age / Config.CoverDeploy);
            int l = Mathf.Min(3, Mathf.FloorToInt(k * 3f + .0001f));
            while (layers < l) { layers++; Sfx.Play(Skills.Food ? "food_wafer_crunch" : "forceField", transform.position, .9f, 1f + layers * .05f, 1f, 40f); Fx.Burst(transform.position, Vector3.up, Links.Gold, 8, 1.5f, .05f, .4f, 1f); }
            if (vis != null) { vis.localScale = new Vector3(1, 1, Mathf.Max(.1f, k)); vis.localPosition = new Vector3(0, 0, -Length / 2f * (1 - k)); }
            if (age > Config.CoverDeploy + Config.WaferRampLife) Destroy(gameObject);
        }
        public bool TakeDamage(float amount, Fighter source, bool explosive)
        {
            if (source != null && owner != null && source.team == owner.team) return true;
            hp -= amount;
            if (Skills.Food) Sfx.Play("food_wafer_crunch", transform.position, .35f, 1.4f + Random.value * .2f, 1f, 20f);
            if (hp <= 0)
            {
                if (Skills.Food) { Sfx.Play("food_wafer_shatter", transform.position, 1f, 1f, 1f, 40f); Fx.Debris(transform.position, Skills.Wafer, 14); }
                Destroy(gameObject);
            }
            return true;
        }
    }

    // ---------------------------------------------------------------- 果冻粘身：穿过门帘的敌人身上粘果冻（透视 2.5 s + "噗叽"声）
    public class JellyStuck : MonoBehaviour
    {
        public float until; float fxAt; Fighter f;
        void Start() { f = GetComponent<Fighter>(); }
        void Update()
        {
            if (f == null || !f.alive || Time.time > until) { Destroy(this); return; }
            if (Time.time >= fxAt)
            {
                fxAt = Time.time + .4f;
                Fx.Burst(f.Chest, Vector3.down, Skills.Caramel, 4, 1f, .07f, .5f, 1.2f);
                if (Skills.Food && f.velocity.sqrMagnitude > 1f) Sfx.Play("food_squish", f.transform.position, .45f, 1.5f, 1f, 18f);
            }
        }
    }

    // ---------------------------------------------------------------- 点酱：黄油跑道着火（替代焦糖火，不叠加）
    public class SauceFire : MonoBehaviour
    {
        public static readonly List<SauceFire> Live = new List<SauceFire>();
        public int zone; public Fighter owner; float born, tick; Light l; Material disc;
        void OnEnable() { Live.Add(this); }
        void OnDisable() { Live.Remove(this); }
        public static bool Burning(int zone) { foreach (var s in Live) if (s != null && s.zone == zone) return true; return false; }
        public void Init(int z, Fighter by)
        {
            zone = z; owner = by; born = Time.time;
            SauceZone.Shape(z, out Vector3 c, out float rx, out float rz, out float rot);
            transform.position = new Vector3(c.x, .03f, c.z); transform.rotation = Quaternion.Euler(0, rot, 0);
            var ps = new GameObject("黄油火").AddComponent<ParticleSystem>(); ps.transform.SetParent(transform, false); ps.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var main = ps.main; main.startLifetime = new ParticleSystem.MinMaxCurve(.5f, 1.1f); main.startSpeed = new ParticleSystem.MinMaxCurve(.8f, 2.4f); main.startSize = new ParticleSystem.MinMaxCurve(.35f, 1f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .8f, .3f), new Color(1f, .45f, .1f)); main.maxParticles = 600; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 40 + rx * rz * 8f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 1f; sh.scale = new Vector3(rx, rz, 1f);
            ps.GetComponent<ParticleSystemRenderer>().material = Fx.Additive;
            var d = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(d.GetComponent<Collider>()); d.name = "着火的黄油"; d.transform.SetParent(transform, false);
            d.transform.localScale = new Vector3(rx * 2.05f, .006f, rz * 2.05f);
            disc = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, .55f, .15f, .45f) }; d.GetComponent<Renderer>().sharedMaterial = disc; d.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            l = new GameObject("火光").AddComponent<Light>(); l.transform.SetParent(transform, false); l.transform.localPosition = Vector3.up * 1.2f; l.color = new Color(1f, .55f, .2f); l.range = Mathf.Max(rx, rz) * 2.5f; l.intensity = 3f;
            Sfx.Play(Skills.Food ? "food_caramel_bubble" : "thrusterFire", transform.position, 1f, .8f, 1f, 35f);
            Skills.spawned.Add(gameObject);
        }
        void Update()
        {
            float age = Time.time - born;
            if (age > Config.SauceButterFire) { Destroy(gameObject); return; }
            if (l != null) l.intensity = 2.5f + Mathf.PerlinNoise(Time.time * 8, 0) * 1.5f;
            if (disc != null) disc.color = new Color(1f, .55f, .15f, .35f + .15f * Mathf.Sin(Time.time * 12f));
            if (Time.time < tick || Match.I == null) return;
            tick = Time.time + .25f;
            foreach (var f in Match.I.fighters)
            {
                if (!f.alive || owner == null || f.team == owner.team || !SauceZone.Inside(zone, f.transform.position)) continue;
                DamageCtx.Set(DamageKind.Fire, Skills.Food ? "黄油火" : "燃烧区"); f.ReceiveDamage(Config.FireDps * .25f, owner, transform.position, false, true);
                f.revealedUntil = Mathf.Max(f.revealedUntil, Time.time + .3f); f.fireRevealUntil = Mathf.Max(f.fireRevealUntil, Time.time + .3f);
            }
        }
    }

    // ---------------------------------------------------------------- 芥末呛雾的扩散（1.75 s 从锅口扩到最大半径）
    public class MustardGrow : MonoBehaviour
    {
        public float R, born; public ParticleSystem ps; public LineRenderer ring; public Vector3 ground;
        void Update()
        {
            float k = Mathf.Clamp01((Time.time - born) / Config.MustardSpread), r = Mathf.Max(.6f, R * k);
            if (ps != null) { var sh = ps.shape; sh.radius = r * .85f; var em = ps.emission; em.rateOverTime = 30 + r * r * 1.1f; }
            if (ring != null) Skills.Circle(ring, ground, r);
            if (k >= 1f && ring != null) enabled = false;
        }
    }

    // ---------------------------------------------------------------- 每帧：雾里的镜头（泛白 + 近距离雾）、监控糊糖、首次提示
    public class LinkTicker : MonoBehaviour
    {
        public static LinkTicker I; public static SteamArea CamInside; public static float White;
        bool saved, fog; FogMode mode; Color col; float dens, fs, fe; static Texture2D vignette;
        readonly Dictionary<SecurityCam, GameObject> blobs = new Dictionary<SecurityCam, GameObject>();
        public static void Ensure() { if (I == null) I = new GameObject("V23 联动放大").AddComponent<LinkTicker>(); }
        void LateUpdate()
        {
            Camera cam = null; foreach (var c in Camera.allCameras) if (c.targetTexture == null && (cam == null || c.depth > cam.depth)) cam = c;   // 正在出画面的相机（截图外景相机也算）
            if (cam == null) cam = Camera.main; SteamArea inside = null;
            if (cam != null) foreach (var a in SteamArea.Live) if (a != null && a.Alive && a.Inside(cam.transform.position, -.2f)) { inside = a; break; }
            foreach (var a in SteamArea.Live) if (a != null) a.SetInsideView(a == inside);
            CamInside = inside;
            White = Mathf.MoveTowards(White, inside != null ? 1f : 0f, Time.deltaTime * 3f);
            if (inside != null)
            {
                if (!saved) { saved = true; fog = RenderSettings.fog; mode = RenderSettings.fogMode; col = RenderSettings.fogColor; dens = RenderSettings.fogDensity; fs = RenderSettings.fogStartDistance; fe = RenderSettings.fogEndDistance; }
                RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogStartDistance = .3f; RenderSettings.fogEndDistance = Config.SteamInsideSight + 1.5f;
                RenderSettings.fogColor = inside.kind == SteamKind.Dust ? new Color(.92f, .84f, .72f) : inside.Candy ? new Color(1f, .82f, .93f) : new Color(.93f, .95f, .97f);
                var m = Match.I; if (m != null && m.player != null && inside.Blind) FoodTips.Trigger("steam_inside");
            }
            else if (saved) { saved = false; RenderSettings.fog = fog; RenderSettings.fogMode = mode; RenderSettings.fogColor = col; RenderSettings.fogDensity = dens; RenderSettings.fogStartDistance = fs; RenderSettings.fogEndDistance = fe; }
            // 监控被棉花糖糊住：镜头上糊一团粉色
            if (Arena.I != null)
                foreach (var c in Arena.I.cams)
                {
                    if (c == null) continue; bool g = LinkPlus.Gummed(c);
                    blobs.TryGetValue(c, out var b);
                    if (g && b == null)
                    {
                        b = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(b.GetComponent<Collider>()); b.name = "糊住监控的棉花糖";
                        b.transform.SetParent(c.transform, false); b.transform.localPosition = new Vector3(0, -.1f, .55f); b.transform.localScale = new Vector3(.55f, .45f, .55f);
                        b.GetComponent<Renderer>().sharedMaterial = Arena.Glow(new Color(1f, .7f, .9f), .8f); blobs[c] = b;
                    }
                    else if (!g && b != null) { Destroy(b); blobs.Remove(c); }
                }
            // 首次提示：走近自己英雄的副联动点
            var mm = Match.I; var p = mm != null ? mm.player : null;
            if (p != null && p.alive && FoodTips.AutoEnabled && mm.phase == Phase.Live && LinkPlus.SideFor(p.role) >= 0 && Time.frameCount % 15 == 0)
                foreach (var e in Links.ElementsFor(p.role)) if (e.side && Links.Flat(e.pos, p.transform.position) < FoodGuide.DetailRange) { FoodTips.Trigger("link_side"); break; }
        }
        void OnGUI()
        {
            GUI.depth = 20;   // 在 HUD（GameUI = 0）下面
            if (White <= .01f || Event.current.type != EventType.Repaint) return;
            if (vignette == null)
            {
                vignette = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) { float dx = (x - 31.5f) / 32f, dy = (y - 31.5f) / 32f, d = Mathf.Sqrt(dx * dx + dy * dy); vignette.SetPixel(x, y, new Color(1, 1, 1, Mathf.Lerp(.08f, .45f, Mathf.Clamp01((d - .25f) / .75f)))); }
                vignette.Apply();
            }
            var a = CamInside; Color c = a == null ? Color.white : a.kind == SteamKind.Dust ? new Color(.95f, .88f, .78f) : a.Candy ? new Color(1f, .85f, .95f) : new Color(.96f, .97f, 1f);
            var o = GUI.color; GUI.color = new Color(c.r, c.g, c.b, White);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), vignette, ScaleMode.StretchToFill);   // 中心约 10% 白、边缘 55%（中心透明度 ≈ 90%）
            GUI.color = o;
        }
    }

    // ================================================================ 规则 / 文案 / 遥测
    public static class LinkPlus
    {
        public static GameObject Root;
        public class RampSpot { public GameObject key; public Vector3 top, basePos, dir; public float h; public WaferRamp ramp; }
        public static readonly List<RampSpot> Ramps = new List<RampSpot>();
        public static readonly List<HotpotKnob> Knobs = new List<HotpotKnob>();
        public static readonly List<GameObject> SauceMarks = new List<GameObject>();
        public static int BlindMaxEver, ColaPushed, JellyStuckCount, ValveClosed;
        public static readonly List<Fighter> LastColaHits = new List<Fighter>();
        public static float LastColaBoost = 1f;
        static readonly Dictionary<SecurityCam, NoiseField> gum = new Dictionary<SecurityCam, NoiseField>();

        public static int SideFor(Role r) { return r == Role.Rivet ? (int)LinkKind.WaferRamp : r == Role.Ember ? (int)LinkKind.SauceIgnite : r == Role.Phantom ? (int)LinkKind.CamGum : -1; }
        public static string SideHelp(Role r)
        {
            switch (r)
            {
                case Role.Rivet: return "威化坡道：威化墙预览对准 " + (Config.RampMinH + .1f).ToString("0.0") + "–" + (Config.RampMaxH - .1f).ToString("0.0") + " m 高的平台边（金色）会自动斜放成约 " + Config.WaferRampAngle.ToString("0") + "° 的坡道，队友能直接走上去，存在 " + Config.WaferRampLife.ToString("0") + " 秒、耐久不变。反制：打碎它";
                case Role.Ember: return "点酱：纸杯蛋糕炸在酱料地面上 → 巧克力酱变热巧克力 " + Config.SauceChocoHot.ToString("0") + " 秒（烫脚），黄油整条跑道着火 " + Config.SauceButterFire.ToString("0.#") + " 秒（代替焦糖火，不叠加）。反制：绕开酱料";
                case Role.Phantom: return "糊监控：棉花糖迷雾罩住守方监控 → 迷雾期间监控失效。反制：守方换位、听脚步";
            }
            return null;
        }

        // ---------------------------------------------------------------- 布置（Match 里 FoodScenery.Dress 之后调用）
        public static void Dress(Arena arena)
        {
            Ramps.Clear(); Knobs.Clear(); SauceMarks.Clear(); gum.Clear(); BlindMaxEver = 0;
            if (Root != null) Arena.Kill(Root);
            if (arena == null) return;
            Root = new GameObject("V23 d 联动物件（不在烘焙匹配范围内）");
            LinkTicker.Ensure();
            Physics.SyncTransforms();
            try { PlaceKnobs(); } catch (System.Exception e) { Debug.LogWarning("NEON_LINKD knobs failed: " + e); }
            try { FindRamps(); } catch (System.Exception e) { Debug.LogWarning("NEON_LINKD ramps failed: " + e); }
            for (int i = 0; i < SauceZone.Count; i++) { var g = new GameObject("酱料点 " + i); g.transform.SetParent(Root.transform, false); g.transform.position = SauceZone.Center(i); SauceMarks.Add(g); }
            var sb = new System.Text.StringBuilder();
            foreach (var r in Ramps) sb.Append("(" + r.top.x.ToString("0.0") + "," + r.top.z.ToString("0.0") + " h=" + r.h.ToString("0.00") + ") ");
            Debug.Log("NEON_LINKD knobs=" + Knobs.Count + " ramps=" + Ramps.Count + " [" + sb.ToString().Trim() + "] sauce=" + SauceZone.Count + " cams=" + arena.cams.Count + " healPots=" + HealPots().Count);
        }
        public static List<KitchenStation> HealPots()
        {
            var l = new List<KitchenStation>();
            foreach (var it in Interactable.All) if (it is KitchenStation k && k.kind == KitchenStation.Kind.HotpotHeal) l.Add(k);
            return l;
        }
        static bool Clear(Vector3 p, float r, float h)
        {
            foreach (var c in Physics.OverlapCapsule(p + Vector3.up * (r + .05f), p + Vector3.up * h, r, ~0, QueryTriggerInteraction.Ignore))
                if (c.GetComponentInParent<Fighter>() == null) return false;
            return true;
        }
        static void PlaceKnobs()
        {
            foreach (var h in HotpotHazard.All)
            {
                if (h == null) continue;
                Vector3 c = h.transform.position, best = Vector3.zero; bool found = false;
                // 优先放在锅的东侧（守方一侧），一圈 12 个方向找一个不穿模、NavMesh 上的点
                for (int i = 0; i < 12 && !found; i++)
                    for (float d = 2.1f; d <= 3.1f && !found; d += .5f)
                    {
                        Vector3 dir = Quaternion.Euler(0, (i % 2 == 0 ? 1 : -1) * (i + 1) / 2 * 30f, 0) * Vector3.right;
                        Vector3 p = c + dir * d; p.y = c.y;
                        if (!NavMesh.SamplePosition(p, out NavMeshHit nh, .6f, NavMesh.AllAreas) || Mathf.Abs(nh.position.y - c.y) > .4f) continue;
                        if (!Clear(nh.position, .3f, 1.5f)) continue;
                        best = nh.position; found = true;
                    }
                if (!found) { Debug.LogWarning("NEON_LINKD knob skipped " + h.name); continue; }
                var go = new GameObject("开大火旋钮 · " + h.name); go.transform.SetParent(Root.transform, false); go.transform.position = best;
                Vector3 face = c - best; face.y = 0; go.transform.rotation = Quaternion.LookRotation(-face.normalized);
                var k = go.AddComponent<HotpotKnob>(); k.BuildKnob(h); Knobs.Add(k);
            }
        }
        static bool Static(Collider c)
        {
            return c != null && !c.isTrigger && c.GetComponentInParent<Fighter>() == null && c.GetComponentInParent<BreakWall>() == null && c.GetComponentInParent<Destructible>() == null
                && c.GetComponentInParent<Shutter>() == null && c.GetComponentInParent<DeployedCover>() == null && c.GetComponentInParent<Interactable>() == null && !(c is CharacterController);
        }
        // 扫全图找"约 2.2–2.6 m 高、上面能站人、旁边地面能放下 30° 坡"的平台边（最多 5 个，互相隔 12 m，离包点近的优先）
        static void FindRamps()
        {
            var cands = new List<RampSpot>();
            float tan = Mathf.Tan(Config.WaferRampAngle * Mathf.Deg2Rad);
            Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
            for (float x = -Arena.HalfX + 2; x <= Arena.HalfX - 2; x += .75f)
                for (float z = -Arena.HalfZ + 2; z <= Arena.HalfZ - 2; z += .75f)
                {
                    if (!Physics.Raycast(new Vector3(x, Config.RampMaxH + .5f, z), Vector3.down, out RaycastHit hit, 2f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    float h = hit.point.y; if (h < Config.RampMinH || h > Config.RampMaxH || hit.normal.y < .95f || !Static(hit.collider)) continue;
                    if (!NavMesh.SamplePosition(hit.point, out NavMeshHit tn, .5f, NavMesh.AllAreas) || Mathf.Abs(tn.position.y - h) > .3f) continue;
                    if (!Clear(hit.point, .35f, 1.8f)) continue;
                    foreach (var d in dirs)
                    {
                        // 往外走到边
                        float e = 0; bool edge = false;
                        for (float s = .25f; s <= 1.25f; s += .25f)
                        {
                            Vector3 q = hit.point + d * s;
                            if (Physics.Raycast(q + Vector3.up * .6f, Vector3.down, out RaycastHit qh, .9f, ~0, QueryTriggerInteraction.Ignore) && Mathf.Abs(qh.point.y - h) < .12f) { e = s; continue; }
                            edge = true; break;
                        }
                        if (!edge || e < .25f) continue;
                        Vector3 top = hit.point + d * e; top.y = h;
                        // 边两侧 ±1 m 也要是同高的台面（坡宽 2.6 m）
                        Vector3 side = Vector3.Cross(Vector3.up, d);
                        bool wide = true;
                        foreach (float sd in new[] { -1f, 1f })
                            if (!Physics.Raycast(top - d * .2f + side * sd + Vector3.up * .6f, Vector3.down, out RaycastHit sh, .9f, ~0, QueryTriggerInteraction.Ignore) || Mathf.Abs(sh.point.y - h) > .12f) wide = false;
                        if (!wide) continue;
                        float run = h / tan;
                        Vector3 b = top + d * run;
                        if (!Physics.Raycast(new Vector3(b.x, 1.2f, b.z), Vector3.down, out RaycastHit bh, 2f, ~0, QueryTriggerInteraction.Ignore) || bh.point.y > .3f || bh.normal.y < .9f) continue;
                        b.y = bh.point.y;
                        if (!NavMesh.SamplePosition(b, out NavMeshHit bn, .6f, NavMesh.AllAreas) || Mathf.Abs(bn.position.y - b.y) > .3f) continue;
                        if (Arena.I != null && !Arena.I.IsBuildLegal(b)) continue;
                        // 坡面上方要空（两侧各探一次）
                        bool free = true;
                        for (float t = .15f; t <= .8f && free; t += .2f)
                        {
                            Vector3 sp = Vector3.Lerp(b, top, t) + Vector3.up * .95f;
                            foreach (float sd in new[] { -.9f, 0f, .9f }) if (Physics.CheckSphere(sp + side * sd, .5f, ~0, QueryTriggerInteraction.Ignore)) { free = false; break; }
                        }
                        if (!free) continue;
                        cands.Add(new RampSpot { top = top, basePos = b, dir = d, h = h });
                    }
                }
            cands.Sort((p, q) => SiteDist(p.top).CompareTo(SiteDist(q.top)));
            foreach (var c in cands)
            {
                bool far = true; foreach (var r in Ramps) if (Links.Flat(r.top, c.top) < 12f) far = false;
                if (!far) continue;
                c.key = new GameObject("威化坡道点"); c.key.transform.SetParent(Root.transform, false); c.key.transform.position = Vector3.Lerp(c.basePos, c.top, .5f);
                Ramps.Add(c);
                if (Ramps.Count >= 5) break;
            }
        }
        static float SiteDist(Vector3 p) { return Mathf.Min(Links.Flat(p, Arena.SiteA), Links.Flat(p, Arena.SiteB)); }

        // ---------------------------------------------------------------- 威化坡道
        public static bool RampTarget(Fighter f, Vector3 desired, out RampSpot spot, out bool busy)
        {
            spot = null; busy = false; float bd = 2.2f;
            foreach (var r in Ramps)
            {
                float d = Links.SegFlatDist(r.basePos, r.top, desired); if (d > bd) continue;
                if (Links.Flat(r.basePos, f.transform.position) > 8f || Mathf.Abs(f.transform.position.y - r.basePos.y) > 1f) continue;
                bd = d; spot = r;
            }
            if (spot == null) return false;
            busy = spot.ramp != null;
            if (!busy) foreach (var c in Physics.OverlapBox(Vector3.Lerp(spot.basePos, spot.top, .5f) + Vector3.up * .6f, new Vector3(1.2f, .6f, 1.2f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) if (c.GetComponentInParent<Fighter>() != null && c.GetComponentInParent<Fighter>() != f) busy = true;
            return true;
        }
        public static bool SpawnRamp(Fighter f, RampSpot s)
        {
            if (s == null || s.ramp != null) return false;
            foreach (var r in WaferRamp.Live.ToArray()) if (r != null && r.owner == f) Object.Destroy(r.gameObject);   // 每个铆钉同时 1 条
            var go = new GameObject((Skills.Food ? "威化坡道 " : "斜坡掩体 ") + Config.TeamName(f.team));
            go.AddComponent<WaferRamp>().Init(f, s);
            if (Skills.Food) Sfx.Play("food_squish", s.basePos, .8f, .9f, 1f, 40f);
            Links.Trigger(LinkKind.WaferRamp, f, s.top + Vector3.up * .8f, "威化斜放成坡道 · 队友能走上 " + s.h.ToString("0.0") + " m 的台子 · " + Config.WaferRampLife.ToString("0") + " 秒");
            return true;
        }

        // ---------------------------------------------------------------- 点酱
        public static int SauceAt(Vector3 p, float pad = 1.2f)
        {
            for (int i = 0; i < SauceZone.Count; i++) if (SauceZone.Near(i, p, pad)) return i;
            return -1;
        }
        // 纸杯蛋糕爆炸点在酱料上（或 1.2 m 内）→ 巧克力变热巧克力 / 黄油整条着火。返回 true = 黄油着火已经代替了焦糖火
        public static bool SauceIgnite(Fighter owner, Vector3 p)
        {
            if (owner == null) return false;
            Vector3 g = Skills.GroundAt(p + Vector3.up * .3f); if (p.y - g.y > 1.6f) return false;
            int i = SauceAt(new Vector3(p.x, g.y, p.z)); if (i < 0) return false;
            Vector3 c = SauceZone.Center(i);
            if (SauceZone.KindOf(i) == SauceZone.Chocolate)
            {
                if (SauceZone.HotIndex(i)) return false;
                SauceZone.MarkHot(i, Config.SauceChocoHot);
                Fx.Burst(c + Vector3.up * .2f, Vector3.up, new Color(.55f, .28f, .12f), 30, 3f, .1f, .8f, -.2f);
                Links.Trigger(LinkKind.SauceIgnite, owner, c + Vector3.up * .5f, "巧克力酱变成热巧克力 " + Config.SauceChocoHot.ToString("0") + " 秒 · 踩上去会烫");
                return false;
            }
            if (SauceFire.Burning(i)) return true;
            new GameObject(Skills.Food ? "着火的黄油跑道" : "燃烧跑道").AddComponent<SauceFire>().Init(i, owner);
            Links.Trigger(LinkKind.SauceIgnite, owner, c + Vector3.up * .5f, "整条黄油跑道着火 " + Config.SauceButterFire.ToString("0.#") + " 秒");
            return true;
        }

        // ---------------------------------------------------------------- 糊监控
        public static bool Gummed(SecurityCam c) { return c != null && gum.TryGetValue(c, out var n) && n != null && Time.time < n.EndsAt; }
        public static void GumCheck(NoiseField n)
        {
            if (n == null || n.owner == null || Arena.I == null || Match.I == null) return;
            if (n.owner.team == Match.I.DefendingTeam) return;   // 监控是守方的
            foreach (var c in Arena.I.cams)
            {
                if (c == null || !c.Working || Gummed(c)) continue;
                if (Links.Flat(c.transform.position, n.transform.position) > Config.NoiseRadius + .5f) continue;
                gum[c] = n;
                Links.Trigger(LinkKind.CamGum, n.owner, c.transform.position, "守方监控被棉花糖糊住 · 迷雾散掉前看不见");
            }
        }

        // ---------------------------------------------------------------- 可乐弹射：落点在 NavMesh 上（否则把倍率往下截到最远可落点）+ 落地可乐炸罐
        public static float ColaBoost(Vector3 start, Vector3 dir, float hSpeed, float vy)
        {
            for (float b = Config.DashPadBoost; b >= 1f - 1e-3f; b -= .05f)
                if (LandsOnNav(start, dir * hSpeed * b + Vector3.up * vy)) return b;
            return 1f;
        }
        public static bool LandsOnNav(Vector3 start, Vector3 v)
        {
            Vector3 pos = start + Vector3.up * .3f;
            for (int i = 0; i < 160; i++)
            {
                Vector3 next = pos + v * .025f; v += Vector3.down * Tune.Gravity * .025f;
                if (Physics.Linecast(pos, next, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<Fighter>() == null && hit.collider.GetComponentInParent<JumpPad>() == null)
                {
                    if (hit.normal.y < .6f) return false;
                    return NavMesh.SamplePosition(hit.point, out NavMeshHit nh, 1.2f, NavMesh.AllAreas) && Mathf.Abs(nh.position.y - hit.point.y) < .7f;
                }
                pos = next;
                if (pos.y < -5f) return false;
            }
            return false;
        }
        public static void ColaImpact(Fighter f, Vector3 at)
        {
            LastColaHits.Clear();
            if (Match.I == null || f == null) return;
            foreach (var e in Match.I.fighters)
            {
                if (!e.alive || e == f || Pings.SameTeam(e, f)) continue;
                Vector3 d = e.transform.position - at; if (Mathf.Abs(d.y) > 2f) continue; d.y = 0;
                if (d.magnitude > Config.ColaImpactRadius) continue;
                Vector3 dir = d.sqrMagnitude < .01f ? Quaternion.Euler(0, Random.Range(0, 360f), 0) * Vector3.forward : d.normalized;
                e.Launch(dir * Config.ColaImpactPush + Vector3.up * 3.2f);   // 推开，不掉血
                e.aimRecoveryUntil = Mathf.Max(e.aimRecoveryUntil, Time.time + Config.ColaImpactShake);   // 准星晃（散布 +3°）
                if (e.human) Fx.shake = Mathf.Max(Fx.shake, .45f);
                LastColaHits.Add(e); ColaPushed++;
                Fx.Burst(e.Chest, Vector3.up, Skills.Cola, 14, 3f, .1f, .6f, .4f);
            }
            Fx.Shockwave(at + Vector3.up * .05f, Config.ColaImpactRadius, Skills.Cola, .45f);
            Fx.Burst(at + Vector3.up * .3f, Vector3.up, Skills.Cola, 50, 6f, .14f, .8f, .5f);
            Sfx.Play("food_can_open", at, 1f, .8f, 1f, 35f);
            if (LastColaHits.Count > 0 && Skills.Food) SkillPop.Show(f, "可乐炸罐！冲散 " + LastColaHits.Count + " 人", Skills.Cola);
        }

        // ---------------------------------------------------------------- 果冻粘身
        public static void JellyStick(Fighter e, ShieldWall curtain)
        {
            if (e == null || !e.alive || curtain == null) return;
            var js = e.GetComponent<JellyStuck>();
            bool fresh = js == null;
            if (js == null) { js = e.gameObject.AddComponent<JellyStuck>(); JellyStuckCount++; }
            js.until = Time.time + Config.JellyStickTime;
            e.revealedUntil = Mathf.Max(e.revealedUntil, Time.time + Config.JellyStickTime);
            if (fresh)
            {
                if (Skills.Food) SkillPop.World(e.Eye + Vector3.up * .7f, "粘身！", Skills.Caramel, e.transform);
                if (e.human) FoodTips.Trigger("enemy_jelly");
            }
        }

        // ---------------------------------------------------------------- 入口保底（硬约束）：每个包点至少 2 个常规入口不能被联动产物占住
        public static readonly Vector3[][] SiteEntrances = {
            new[] { new Vector3(24, 0, 40), new Vector3(20, 0, 17.5f), new Vector3(29, 0, 15) },
            new[] { new Vector3(24, 0, -39.5f), new Vector3(20, 0, -17.5f), new Vector3(46, 0, -16.5f) } };
        public static readonly string[][] EntranceNames = { new[] { "A 长街口", "A 连廊", "A 中庭侧口" }, new[] { "B 长街口", "B 连廊", "B 东侧口" } };
        const float EntranceSnap = 3.5f;
        public static Links.Frame FrameAt(Vector3 e) { foreach (var fr in Links.Frames()) if (Links.Flat(fr.center, e) <= EntranceSnap) return fr; return null; }
        public static bool EntranceSealable(Vector3 e) { return FrameAt(e) != null; }
        public static bool EntranceOccupied(Vector3 e) { var fr = FrameAt(e); return fr != null && Links.CurtainOf(fr) != null; }
        public static int OpenEntrances(int site, Object assume = null)
        {
            int n = 0;
            foreach (var e in SiteEntrances[site]) { var fr = FrameAt(e); if (fr == null || Links.CurtainOf(fr) == null && (assume == null || fr.key != assume)) n++; }
            return n;
        }
        public static bool CurtainAllowed(Links.Frame fr)
        {
            for (int s = 0; s < 2; s++)
                foreach (var e in SiteEntrances[s])
                    if (Links.Flat(fr.center, e) <= EntranceSnap && OpenEntrances(s, fr.key) < 2) return false;
            return true;
        }

        // ---------------------------------------------------------------- 地图图层（MapUI.DynamicAreas 读，双方都画）
        public struct MapArea { public Vector3 c; public float r, left, total; public Color col; }
        public static List<MapArea> MapAreas()
        {
            var l = new List<MapArea>();
            foreach (var a in SteamArea.Live)
            {
                if (a == null || !a.Alive) continue;
                Color col = a.kind == SteamKind.Dust ? new Color(.95f, .85f, .7f, .5f) : a.Candy ? new Color(1f, .65f, .9f, .5f) : new Color(1f, 1f, 1f, .55f);
                l.Add(new MapArea { c = a.center, r = Mathf.Max(a.Radius, a.R * .3f), left = a.until - Time.time, total = a.total, col = col });
            }
            foreach (var d in Drone.Live)
                if (d != null && d.PotMode) l.Add(new MapArea { c = d.pot.transform.position, r = Mathf.Max(1f, d.PotRadiusNow), left = d.PotLeft, total = Config.MustardPotLife, col = new Color(.75f, .9f, .25f, .4f) });
            return l;
        }

        // ---------------------------------------------------------------- 遥测
        public static void OnAreaSpawned(SteamArea a)
        {
            var m = Match.I; if (m == null) return;
            int n = SteamArea.BlindCount; BlindMaxEver = Mathf.Max(BlindMaxEver, n);
            if (m.Cur != null) m.Cur.blindMax = Mathf.Max(m.Cur.blindMax, n);
            if (m.autoplay && a.Blind) Debug.Log("NEON_STEAM r" + m.round + " " + a.kind + " r=" + a.R + " by=" + (a.by != null ? a.by.Hero.name : "-") + " blind=" + n);
            var p = m.player; if (p != null && a.Blind && Links.Flat(p.transform.position, a.center) < 30f) FoodTips.Trigger("steam_area");
        }
        public static void OnSteamOpened(SteamValve v, Fighter f) { var m = Match.I; if (m != null && m.Cur != null && v is HotpotKnob) m.Cur.knob++; }
        public static void OnValveClosed(SteamValve v, Fighter f)
        {
            ValveClosed++;
            var m = Match.I; if (m == null) return;
            if (m.Cur != null) m.Cur.valveClose++;
            m.Feed((f != null ? Config.TeamName(f.team) + " " + f.Hero.name + " " : "") + "钻进白雾关掉了" + v.Noun + " · " + Config.SteamCloseFade.ToString("0.#") + " 秒内散开", new Color(.85f, .9f, 1f));
            if (m.autoplay) Debug.Log("NEON_VALVE_CLOSE r" + m.round + " " + (f != null ? f.Hero.name : "-") + " " + v.Noun);
        }
        public static void OnBlast(Vector3 p) { var m = Match.I; if (m != null && m.Cur != null && SteamArea.BlindAt(p + Vector3.up * .3f) != null) m.Cur.nadeSteam++; }
        public static void OnKill(Fighter victim) { var m = Match.I; if (m != null && m.Cur != null && victim != null && SteamArea.BlindAt(victim.Chest) != null) m.Cur.killSteam++; }
    }
}
