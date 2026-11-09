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
            if (f.human) Fx.AddNumber(f.Chest + Vector3.up * .4f, Heal, false, false, false);
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
        public static bool SlickAt(Vector3 p) { var z = Find(p); return z != null && Time.time < z.slickUntil; }
    }

    // 火锅热气：挡住视线（AI 也看不穿），子弹可以穿过
    public static class SteamCloud
    {
        static readonly List<Vector4> clouds = new List<Vector4>();   // xyz 中心, w 半径
        public static void Clear() { clouds.Clear(); suppressedUntil = 0; particles.Clear(); }
        static float suppressedUntil; public static readonly List<ParticleSystem> particles = new List<ParticleSystem>();
        public static bool Suppressed { get { return Time.time < suppressedUntil; } }
        // V18：冰淇淋给火锅降温，热气散开一段时间
        public static bool Suppress(float secs)
        {
            bool was = Suppressed; suppressedUntil = Time.time + secs;
            foreach (var ps in particles) if (ps != null) { var em = ps.emission; em.enabled = false; }
            return !was;
        }
        public static void Tick() { if (!Suppressed && suppressedUntil > 0) { suppressedUntil = 0; foreach (var ps in particles) if (ps != null) { var em = ps.emission; em.enabled = true; } } }
        public static void Add(Vector3 c, float r) { clouds.Add(new Vector4(c.x, c.y, c.z, r)); }
        public static bool Blocks(Vector3 a, Vector3 b)
        {
            Tick(); if (Suppressed) return false;
            foreach (var c in clouds)
            {
                Vector3 cc = c; Vector3 ab = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(cc - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                if (Vector3.Distance(a + ab * t, cc) < c.w * .8f) return true;
            }
            return false;
        }
    }

    // 站进火锅里会被烫
    public class HotpotHazard : MonoBehaviour
    {
        public float radius = 1.25f, height = .6f, dps = 12f;
        readonly Dictionary<Fighter, float> tick = new Dictionary<Fighter, float>();
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
                f.ReceiveDamage(dps * .25f, null, transform.position, false);
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
