using System.Collections.Generic;
using UnityEngine;

namespace NeonSiege
{
    // ---------------------------------------------------------------- V17 第二阶段：地图上的美食玩法

    // 能吃的补给：走过去按住 G 吃掉，回 25 生命；吃掉后 25 秒刷新，新回合也会刷新
    public class Snack : Interactable
    {
        public const float Heal = 25f;
        public static float Respawn { get { return Match.Mode == GameMode.Grow ? 8f : 25f; } }
        Transform model; float eatenAt = -999; bool eaten; Light glow;
        public bool Eaten { get { return eaten; } }

        public void Build(string food)
        {
            holdTime = .5f; range = 1.9f;
            var plate = GameObject.CreatePrimitive(PrimitiveType.Cylinder); plate.name = "餐盘";
            Arena.Kill(plate.GetComponent<Collider>());
            plate.transform.SetParent(transform, false); plate.transform.localPosition = new Vector3(0, .03f, 0); plate.transform.localScale = new Vector3(.9f, .03f, .9f);
            var pm = new Material(Shader.Find("Standard")); pm.color = new Color(1f, .97f, .95f); pm.SetFloat("_Glossiness", .7f);
            plate.GetComponent<Renderer>().sharedMaterial = pm;
            var o = Arena.Model("Toon/Food/" + food);
            if (o != null)
            {
                foreach (var c in o.GetComponentsInChildren<Collider>()) Arena.Kill(c);
                Arena.Fit(o, new Vector3(0, .55f, 0));
                model = new GameObject("零食").transform; model.SetParent(transform, false); model.localPosition = new Vector3(0, .18f, 0);
                o.transform.SetParent(model, false);
                var sp = model.gameObject.AddComponent<Spin>(); sp.speed = new Vector3(0, 60, 0); sp.bob = .06f;
            }
            glow = new GameObject("零食灯").AddComponent<Light>(); glow.transform.SetParent(transform, false); glow.transform.localPosition = Vector3.up * .9f;
            glow.color = new Color(1f, .8f, .6f); glow.range = 3.5f; glow.intensity = 1.2f;
        }

        void Update()
        {
            if (eaten && Time.time - eatenAt >= Respawn) Show(true);
        }

        void Show(bool on)
        {
            eaten = !on;
            if (model != null) model.gameObject.SetActive(on);
            if (glow != null) glow.enabled = on;
            if (on) Fx.Burst(transform.position + Vector3.up * .4f, Vector3.up, new Color(1f, .8f, .9f), 12, 2f, .05f, .5f, -.2f);
        }

        public override bool Usable(Fighter f) { return !eaten && f.alive && f.hp < Config.MaxHp; }
        public override string Prompt(Fighter f) { return "按住 G 吃掉（+" + Heal + " 生命）"; }
        public override Vector3 Anchor { get { return transform.position + Vector3.up * .5f; } }
        protected override void OnStart(Fighter f) { Sfx.Play("food_squish", transform.position, .5f, 1.3f, 1f, 15f); }
        protected override void Complete(Fighter f)
        {
            float heal = Heal * (ToonAssets.Available && f.role == Role.Falcon ? 2f : 1f);   // 猎隼被动：吃零食回血翻倍
            f.hp = Mathf.Min(Config.MaxHp, f.hp + heal);
            if (ToonAssets.Available) { f.Food.snacks++; f.Food.Grow(1); }
            eatenAt = Time.time; Show(false);
            Fx.Burst(f.Chest, Vector3.up, new Color(1f, .6f, .75f), 18, 2.2f, .06f, .6f, .4f);
            Sfx.Play("food_popcorn", transform.position, .8f, .8f, 1f, 20f);
            Sfx.Play("confirmation", transform.position, .4f, 1.4f, 1f, 15f);
            if (f.human) Fx.AddNumber(f.Chest + Vector3.up * .4f, heal, false, false, false);   // V21：飘字显示实际回血（猎隼 50）
        }
        public override void ResetRound() { base.ResetRound(); Show(true); }
    }

    // 巧克力酱（减速）与黄油（打滑）地面
    public static class SauceZone
    {
        public const int None = 0, Chocolate = 1, Butter = 2;
        class Z { public Vector3 c; public float rx, rz, rot; public int kind; public float hotUntil, slickUntil; }
        static readonly List<Z> zones = new List<Z>();
        public static void Clear() { zones.Clear(); }
        public static void Add(Vector3 c, float rx, float rz, float rotY, int kind) { zones.Add(new Z { c = c, rx = rx, rz = rz, rot = rotY, kind = kind }); }
        static Z Find(Vector3 p)
        {
            if (p.y > .35f) return null;
            foreach (var z in zones)
            {
                Vector3 d = Quaternion.Euler(0, -z.rot, 0) * (p - z.c);
                if ((d.x * d.x) / (z.rx * z.rx) + (d.z * d.z) / (z.rz * z.rz) <= 1f) return z;
            }
            return null;
        }
        public static int At(Vector3 p) { var z = Find(p); return z == null ? None : z.kind; }
        // V18：食物反应 —— 黄油被番茄酱弄得更滑；巧克力被辣椒加热变成热巧克力
        public static void Mark(Vector3 p, float secs, bool hot)
        {
            var z = Find(p); if (z == null) return;
            if (hot) { bool was = Time.time < z.hotUntil; z.hotUntil = Time.time + secs; if (!was && Match.I != null) Match.I.Feed("辣椒 + 巧克力酱 = 热巧克力！踩上去会烫", new Color(1f, .5f, .3f)); }
            else { bool was = Time.time < z.slickUntil; z.slickUntil = Time.time + secs; if (!was && Match.I != null) Match.I.Feed("番茄酱 + 黄油 = 更滑了！", new Color(1f, .6f, .4f)); }
        }
        public static bool HotAt(Vector3 p) { var z = Find(p); return z != null && Time.time < z.hotUntil; }
        // V23 d 副联动"点酱"：给联动 / 测试读酱料块的几何
        public static int Count { get { return zones.Count; } }
        public static Vector3 Center(int i) { return zones[i].c; }
        public static int KindOf(int i) { return zones[i].kind; }
        public static float Size(int i) { return Mathf.Max(zones[i].rx, zones[i].rz); }
        // p（地面高度）离第 i 块酱料的边 ≤ pad 米
        public static bool Near(int i, Vector3 p, float pad)
        {
            var z = zones[i]; if (p.y > 1.2f) return false;
            Vector3 d = Quaternion.Euler(0, -z.rot, 0) * (p - z.c);
            float ax = z.rx + pad, az = z.rz + pad;
            return (d.x * d.x) / (ax * ax) + (d.z * d.z) / (az * az) <= 1f;
        }
        public static bool Inside(int i, Vector3 p) { return p.y <= .6f && Near(i, new Vector3(p.x, 0, p.z), 0f); }
        public static void Shape(int i, out Vector3 c, out float rx, out float rz, out float rot) { var z = zones[i]; c = z.c; rx = z.rx; rz = z.rz; rot = z.rot; }
        public static void MarkHot(int i, float secs) { var z = zones[i]; z.hotUntil = Mathf.Max(z.hotUntil, Time.time + secs); }
        public static bool HotIndex(int i) { return Time.time < zones[i].hotUntil; }
        public static bool SlickAt(Vector3 p) { var z = Find(p); return z != null && Time.time < z.slickUntil; }
    }

    // 火锅热气：挡住视线（AI 也看不穿），子弹可以穿过
    public static class SteamCloud
    {
        static readonly List<Vector4> clouds = new List<Vector4>();   // xyz 中心, w 半径
        // V22 第二期：蒸汽阀喷出的限时白雾（到时自动失效，回合重置清空）
        static readonly List<Vector4> timed = new List<Vector4>(); static readonly List<float> timedUntil = new List<float>();
        public static int AddTimed(Vector3 c, float r, float until) { timed.Add(new Vector4(c.x, c.y, c.z, r)); timedUntil.Add(until); return timed.Count - 1; }
        public static void ExpireTimed(int id) { if (id >= 0 && id < timedUntil.Count) timedUntil[id] = 0; }   // V22 第三期：棉花糖蒸汽被冰淇淋吹散 / 提前结束
        public static void ClearTimed() { timed.Clear(); timedUntil.Clear(); SteamArea.EndAll(); }   // V23 d：整片蒸汽也一起清
        public static int TimedActive { get { int n = 0; foreach (var u in timedUntil) if (Time.time < u) n++; return n; } }
        public static void Clear() { clouds.Clear(); sups.Clear(); anyParticleOff = false; particles.Clear(); ClearTimed(); }
        public static readonly List<ParticleSystem> particles = new List<ParticleSystem>();
        // V23 a-1：冰淇淋"散热气"从全图改成局部 —— 只散被打的那口锅周围 SuppressRadius 米（设计稿 §4.0 隐患 2：10–14 m，取 12）。
        // 每个散雾是一个球（中心 / 半径 / 到期时间）；蒸汽云的中心落在任何一个有效球里就算被散。旧的 Suppress(secs) 保留为"全图"版（测试 / 兼容用）。
        public const float SuppressRadius = 12f;
        class Sup { public Vector3 c; public float r, until; }
        static readonly List<Sup> sups = new List<Sup>(); static bool anyParticleOff;
        public static bool Suppressed { get { foreach (var s in sups) if (Time.time < s.until) return true; return false; } }   // 场上有任何散雾在生效
        public static bool SuppressedAt(Vector3 p)
        {
            foreach (var s in sups) if (Time.time < s.until && (s.r <= 0 || Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(s.c.x, 0, s.c.z)) <= s.r)) return true;
            return false;
        }
        // V18：冰淇淋给火锅降温，热气散开一段时间（全图版：r ≤ 0 = 不限范围）
        public static bool Suppress(float secs) { return Suppress(Vector3.zero, 0f, secs); }
        public static bool Suppress(Vector3 center, float radius, float secs)
        {
            bool was = SuppressedAt(center);
            Sup hit = null; foreach (var s in sups) if (s.r == radius && Vector3.Distance(s.c, center) < .5f) hit = s;
            if (hit == null) sups.Add(hit = new Sup { c = center, r = radius });
            hit.until = Time.time + secs;
            foreach (var ps in particles) if (ps != null && SuppressedAt(ps.transform.position)) { var em = ps.emission; em.enabled = false; anyParticleOff = true; }
            return !was;
        }
        public static void Tick()
        {
            for (int i = sups.Count - 1; i >= 0; i--) if (Time.time >= sups[i].until) sups.RemoveAt(i);
            if (anyParticleOff) { anyParticleOff = false; foreach (var ps in particles) if (ps != null) { bool off = SuppressedAt(ps.transform.position); var em = ps.emission; em.enabled = !off; anyParticleOff |= off; } }
        }
        public static void Add(Vector3 c, float r) { clouds.Add(new Vector4(c.x, c.y, c.z, r)); }
        public static bool Blocks(Vector3 a, Vector3 b)
        {
            Tick();
            if (SteamArea.BlocksSight(a, b)) return true;   // V23 d：整片蒸汽 / 棉花糖蒸汽 / 蛋糕粉尘（外面看不进，里面只看 2.5 m）
            foreach (var c in clouds) if (!SuppressedAt(c) && Hits(c, a, b)) return true;
            for (int i = 0; i < timed.Count; i++) if (Time.time < timedUntil[i] && !SuppressedAt(timed[i]) && Hits(timed[i], a, b)) return true;
            return false;
        }
        static bool Hits(Vector4 c, Vector3 a, Vector3 b)
        {
            Vector3 cc = c; Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(cc - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
            return Vector3.Distance(a + ab * t, cc) < c.w * .8f;
        }
    }

    // ---------------------------------------------------------------- V22 第二期：蒸汽阀（中庭 ×2、南后厨 ×1）
    // V23 d：整片蒸汽 —— 长按 G 0.5 s → 中庭 9 m / 后厨 7 m 整片白雾 10 s（外面看不进、里面只看清 2.5 m，AI 同一判定；子弹照样穿过、爆炸照样炸到），冷却 40 s。
    // 喷着的时候任何人走进雾里长按 G 0.5 s 关阀（反制），1.75 s 内散开。全图同时最多 2 片盲区，满了就拧不开。
    // 外观：奶白茶壶 + 红色阀门轮（MAP-A2），喷气时阀门轮转、壶嘴冒蒸汽。
    public class SteamValve : Interactable
    {
        public const float Duration = 10f, Cooldown = 40f, Radius = 9f, HoldSecs = .5f;   // Radius = 中庭阀门（后厨见 areaRadius）
        public static readonly List<SteamValve> Valves = new List<SteamValve>();
        protected float activeUntil = -1, readyAt, firedAt = -9; protected ParticleSystem ps; protected Transform wheel; protected Light lamp;
        public float areaRadius = 9f, openHold = HoldSecs; public SteamArea area; public Fighter openedBy;
        public bool Active { get { return area != null && Time.time < area.until && area.closeAt < 0; } }
        public bool Closable { get { return Active && Time.time - firedAt > 1.2f; } }   // 刚拧开 1.2 s 内不能关（防止一直按着 G 自己又关掉）
        public float CooldownLeft { get { return Mathf.Max(0, readyAt - Time.time); } }
        public virtual Vector3 CloudCenter { get { return transform.position + Vector3.up * 1.8f; } }
        public virtual string Noun { get { return transform.position.x < 5f ? "后厨蒸汽阀" : "蒸汽阀"; } }
        public virtual string OpenVerb { get { return "拧开"; } }
        protected override void OnEnable() { base.OnEnable(); Valves.Add(this); }
        protected override void OnDisable() { base.OnDisable(); Valves.Remove(this); }

        public void Build()
        {
            holdTime = HoldSecs; range = 2.2f;
            areaRadius = transform.position.x < 5f ? Config.SteamAreaKitchen : Config.SteamAreaMid;
            Material M(Color c, float g) { var m = new Material(Shader.Find("Standard")); m.color = c; m.SetFloat("_Glossiness", g); return m; }
            var cream = M(new Color(1f, .95f, .9f), .6f); var pink = M(new Color(1f, .6f, .7f), .5f); var red = M(new Color(.9f, .25f, .35f), .5f); var leaf = M(new Color(.45f, .75f, .55f), .4f);
            GameObject Part(PrimitiveType t, Vector3 lp, Vector3 s, Material m, Transform parent = null)
            {
                var o = GameObject.CreatePrimitive(t); Arena.Kill(o.GetComponent<Collider>()); o.transform.SetParent(parent != null ? parent : transform, false);
                o.transform.localPosition = lp; o.transform.localScale = s; o.GetComponent<Renderer>().sharedMaterial = m; return o;
            }
            Part(PrimitiveType.Cylinder, new Vector3(0, .08f, 0), new Vector3(1.3f, .08f, 1.3f), pink);                    // 底座
            Part(PrimitiveType.Sphere, new Vector3(0, .62f, 0), new Vector3(1.05f, .95f, 1.05f), cream);                   // 壶身
            Part(PrimitiveType.Cylinder, new Vector3(0, 1.08f, 0), new Vector3(.45f, .08f, .45f), cream);                  // 壶盖
            Part(PrimitiveType.Sphere, new Vector3(0, .62f, .5f), new Vector3(.32f, .38f, .05f), leaf);                    // 叶子标
            var spout = Part(PrimitiveType.Cylinder, new Vector3(.62f, .78f, 0), new Vector3(.16f, .28f, .16f), cream); spout.transform.localRotation = Quaternion.Euler(0, 0, -55);
            Part(PrimitiveType.Cylinder, new Vector3(0, 1.22f, 0), new Vector3(.07f, .14f, .07f), red);                    // 阀杆
            wheel = new GameObject("阀门轮").transform; wheel.SetParent(transform, false); wheel.localPosition = new Vector3(0, 1.38f, 0);
            Part(PrimitiveType.Cylinder, Vector3.zero, new Vector3(.62f, .03f, .62f), red, wheel);
            for (int i = 0; i < 2; i++) { var sp = Part(PrimitiveType.Cube, Vector3.zero, new Vector3(.62f, .05f, .07f), red, wheel); sp.transform.localRotation = Quaternion.Euler(0, i * 90, 0); }
            var cap = gameObject.AddComponent<CapsuleCollider>(); cap.radius = .55f; cap.height = 1.45f; cap.center = new Vector3(0, .72f, 0);
            MakeJet(new Vector3(.75f, 1f, 0));
            lamp = new GameObject("蒸汽阀灯").AddComponent<Light>(); lamp.transform.SetParent(transform, false); lamp.transform.localPosition = new Vector3(0, 1.6f, 0);
            lamp.color = new Color(1f, .55f, .6f); lamp.range = 3f; lamp.intensity = 1f;
        }
        protected void MakeJet(Vector3 local)
        {
            var go = new GameObject("蒸汽"); go.transform.SetParent(transform, false); go.transform.localPosition = local;
            ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.startLifetime = 2.6f; main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f); main.startSize = new ParticleSystem.MinMaxCurve(2f, 4f);
            main.startColor = new Color(1f, 1f, 1f, .75f); main.maxParticles = 220; main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = -.05f;
            var em = ps.emission; em.rateOverTime = 40; em.enabled = false;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = .6f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .15f), new GradientAlphaKey(.8f, .7f), new GradientAlphaKey(0, 1) }); col.color = g;
            go.GetComponent<ParticleSystemRenderer>().material = Resources.Load<Material>("Materials/FxSmoke") ?? Fx.Additive;
        }

        public override bool Usable(Fighter f)
        {
            if (f == null || !f.alive) return false;
            holdTime = Active ? Config.SteamCloseHold : openHold;
            if (Active) return Time.time - firedAt > (f == openedBy ? 3f : 1.2f);   // 拧开的人 3 s 内不会因为一直按着 G 又关掉
            return Time.time >= readyAt && SteamArea.CanSpawnBlind;
        }
        public override string Prompt(Fighter f)
        {
            if (Active) return "按住 G 关掉" + Noun + "（白雾 " + Config.SteamCloseFade.ToString("0.#") + " 秒内散开）";
            return "按住 G " + OpenVerb + Noun + "（整片白雾 " + Duration.ToString("0") + " 秒：外面看不进，里面只看清 " + Config.SteamInsideSight.ToString("0.#") + " 米）";
        }
        // 世界气泡（FoodMarkers）
        public virtual string MarkerLabel(Fighter p)
        {
            if (Active) return "整片白雾 · 走进去按住 G 关掉";
            if (CooldownLeft > 0) return Noun + " · 还要 " + Mathf.CeilToInt(CooldownLeft) + " 秒";
            if (!SteamArea.CanSpawnBlind) return Noun + " · 全图已有 2 片白雾";
            return "G " + OpenVerb + Noun + " · 整片白雾";
        }
        public override Vector3 Anchor { get { return transform.position + Vector3.up * 1f; } }
        protected override void OnStart(Fighter f) { Sfx.Play("switch", transform.position, .6f, 1.3f, 1f, 20f); }
        protected override void Complete(Fighter f) { if (Active) Close(f); else Fire(f); }
        public void Fire(Fighter f)
        {
            if (Active) return;
            var a = SteamArea.Spawn(SteamKind.Valve, this, CloudCenter, areaRadius, Duration, f);
            if (a == null) { if (f != null && f.human && Match.I != null && Match.I.ui != null) Match.I.ui.Toast("全图已经有 2 片整片蒸汽了 · 等一片散掉"); return; }
            area = a; openedBy = f; firedAt = Time.time;
            activeUntil = a.until; readyAt = Time.time + Cooldown;
            if (ps != null) { var em = ps.emission; em.enabled = true; }
            Sfx.Play("thrusterFire", transform.position, .9f, .6f, 1f, 40f);
            Sfx.Play("food_fizz", transform.position, .8f, .55f, 1f, 45f);
            if (Match.I != null) { Match.I.OnValve(); Match.I.NoiseAt(f, transform.position); if (f != null) Match.I.Feed(Config.TeamName(f.team) + " " + f.Hero.name + " " + OpenVerb + "了" + Noun + " · 整片白雾 " + Duration.ToString("0") + " 秒", new Color(.85f, .9f, 1f)); }
            LinkPlus.OnSteamOpened(this, f);
        }
        public void Close(Fighter f)
        {
            if (area == null) return;
            area.End(); activeUntil = Time.time;
            Sfx.Play("switch", transform.position, .8f, .8f, 1f, 30f); Sfx.Play("food_squish", transform.position, .6f, .6f, 1f, 25f);
            LinkPlus.OnValveClosed(this, f);
        }
        protected virtual void Update()
        {
            bool on = Active;
            if (wheel != null && on) wheel.Rotate(0, 360f * Time.deltaTime, 0, Space.Self);
            if (ps != null && !on) { var em = ps.emission; if (em.enabled) em.enabled = false; }
            if (lamp != null) lamp.intensity = on ? 2.2f : CooldownLeft > 0 ? .3f : 1f;
        }
        public override void ResetRound()
        {
            base.ResetRound(); activeUntil = -1; readyAt = 0; firedAt = -9; openedBy = null;
            if (area != null) Object.Destroy(area.gameObject); area = null;
            if (ps != null) { var em = ps.emission; em.enabled = false; ps.Clear(); }
        }
    }

    // 站进火锅里会被烫
    public class HotpotHazard : MonoBehaviour
    {
        public float radius = 1.25f, height = .6f, dps = 12f;
        readonly Dictionary<Fighter, float> tick = new Dictionary<Fighter, float>();
        public static readonly List<HotpotHazard> All = new List<HotpotHazard>();   // V22 第三期：芥末辣锅 / 棉花糖蒸汽的联动点
        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }
        public Vector3 SteamCenter { get { return transform.position + Vector3.up * 1.8f; } }
        void Update()
        {
            if (Match.I == null) return;
            foreach (var f in Match.I.fighters)
            {
                if (!f.alive) continue;
                Vector3 d = f.transform.position - transform.position;
                if (d.y < height || d.y > height + 1f || new Vector2(d.x, d.z).magnitude > radius) continue;
                if (tick.TryGetValue(f, out float t) && Time.time < t) continue;
                tick[f] = Time.time + .25f;
                DamageCtx.Set(DamageKind.Env, "火锅烫伤"); f.ReceiveDamage(dps * .25f, null, transform.position, false);
                Fx.Burst(f.transform.position + Vector3.up * .3f, Vector3.up, new Color(1f, .35f, .2f), 6, 2f, .06f, .4f, -.2f);
                if (f.human) { Fx.AddNumber(f.Chest, dps * .25f, false, false, false); Sfx.Ui("food_squish", .4f, 1.4f); }
            }
        }
    }

    // 美食大乱斗：阵亡掉落手里的美食枪，走过去就能捡（20 秒后消失）
    public class WeaponDrop : MonoBehaviour
    {
        public WeaponSpec w; float born; Transform spin;
        public static readonly List<WeaponDrop> Live = new List<WeaponDrop>();
        public static void Spawn(WeaponSpec w, Vector3 at)
        {
            if (w == null || w == Config.Pistol) return;
            var go = new GameObject("掉落的美食枪 " + w.id); go.transform.position = Skills.GroundAt(at) + Vector3.up * .05f;
            var d = go.AddComponent<WeaponDrop>(); d.w = w; d.born = Time.time;
            d.spin = new GameObject("旋转").transform; d.spin.SetParent(go.transform, false); d.spin.localPosition = Vector3.up * .6f;
            var gun = DessertGuns.Create(w, new GunParts(), false); gun.SetParent(d.spin, false); gun.localScale = Vector3.one * 2.2f;
            var sp = d.spin.gameObject.AddComponent<Spin>(); sp.speed = new Vector3(0, 120, 0); sp.bob = .12f;
            var l = new GameObject("光").AddComponent<Light>(); l.transform.SetParent(go.transform, false); l.transform.localPosition = Vector3.up; l.color = FoodFx.For(w).splat; l.range = 3; l.intensity = 2;
            Live.Add(d);
        }
        void OnDestroy() { Live.Remove(this); }
        void Update()
        {
            if (Time.time - born > 20f || Match.I == null || Match.I.phase == Phase.MatchEnd) { Destroy(gameObject); return; }
            if (Time.time - born < .6f) return;
            foreach (var f in Match.I.fighters)
            {
                if (!f.alive || Vector3.Distance(f.transform.position, transform.position) > 1.4f) continue;
                if (f.W == w && f.ammo >= w.magazine) continue;
                f.Equip(w);
                Sfx.Play("food_popcorn", transform.position, .8f, 1.2f, 1f, 20f);
                if (f.human) Match.I.Feed("捡起了 " + w.id + "「" + w.name + "」", FoodFx.For(w).splat);
                Destroy(gameObject); return;
            }
        }
    }
}
