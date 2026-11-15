using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace NeonSiege
{
    public interface IDamageable
    {
        // Returns true when the hit was absorbed (bullet stops).
        bool TakeDamage(float amount, Fighter source, bool explosive);
    }

    // Anything the player can hold E on. Arena keeps a list; Fighter queries the nearest usable one.
    public abstract class Interactable : MonoBehaviour
    {
        public static readonly List<Interactable> All = new List<Interactable>();
        public float holdTime = 1f, range = 2.2f;
        public float progress; public Fighter user;
        protected virtual void OnEnable() { All.Add(this); }
        protected virtual void OnDisable() { All.Remove(this); }
        public abstract bool Usable(Fighter f);
        public abstract string Prompt(Fighter f);
        protected abstract void Complete(Fighter f);
        public virtual void ResetRound() { progress = 0; user = null; }
        public virtual Vector3 Anchor { get { return transform.position; } }

        public bool Hold(Fighter f, float dt)
        {
            if (!Usable(f)) return false;
            if (user != f) { user = f; progress = 0; OnStart(f); }
            progress += dt;
            if (progress >= holdTime) { progress = 0; user = null; Complete(f); return true; }
            return false;
        }
        protected virtual void OnStart(Fighter f) { Sfx.Play("computerNoise", transform.position, .6f, 1f, 1f, 25f); }
        public void Release(Fighter f) { if (user == f) { user = null; progress = 0; } }

        public static Interactable Nearest(Fighter f)
        {
            Interactable best = null; float bd = float.MaxValue;
            foreach (var i in All)
            {
                if (i == null || !i.isActiveAndEnabled || !i.Usable(f)) continue;
                float d = Vector3.Distance(i.Anchor, f.transform.position + Vector3.up * .9f);
                if (d < i.range && d < bd) { bd = d; best = i; }
            }
            return best;
        }

        protected static GameObject Cube(Transform parent, Vector3 lp, Vector3 size, Material m, bool collider = false)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            if (!collider) Arena.Kill(c.GetComponent<Collider>());
            c.transform.SetParent(parent, false); c.transform.localPosition = lp; c.transform.localScale = size;
            c.GetComponent<Renderer>().sharedMaterial = m;
            return c;
        }
    }

    // ---------------------------------------------------------------- 卷帘门：E 切换开关（2 s，有噪音），双方都可用
    // V22 第二期：检查站铁门 → 蒸笼门（gate=true）：默认开着（三层蒸笼升到门楣上方），守方长按 G 2.0 s 盖上（巨响），
    // 任何人长按 G 1.2 s 掀开；关着时有碰撞 + 寻路障碍（NavMeshObstacle 挖洞），开着时都关掉，NavMesh 同步。
    public class Shutter : Interactable
    {
        public bool open; float t = 1; Transform slab, lever; NavMeshObstacle obstacle; BoxCollider col; float width, height;
        public bool gate;
        public const float GateCloseHold = 2.0f, GateOpenHold = 1.2f, GateMoveTime = 1.6f;
        public string Kind { get { return gate ? "蒸笼门" : "卷帘门"; } }
        public float Width { get { return width; } }
        public float Height { get { return height; } }
        public bool Moving { get { return t < 1f; } }   // V22 第三期：门框联动只在完全开着时生效
        // 爆破模式只有守方能盖上蒸笼门；其他模式谁都能关
        public static bool CanClose(Fighter f) { return Match.I == null || Match.Mode != GameMode.Bomb || f == null || f.team == Match.I.DefendingTeam; }
        public void BuildGate(float w, float h, Material frame)
        {
            gate = true; width = w; height = h; holdTime = GateOpenHold; range = 3f;
            col = gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(w, h, .5f); col.center = new Vector3(0, h / 2, 0);
            obstacle = gameObject.AddComponent<NavMeshObstacle>(); obstacle.shape = NavMeshObstacleShape.Box; obstacle.size = col.size; obstacle.center = col.center; obstacle.carving = true;
            Material M(Color c, float g) { var m = new Material(Shader.Find("Standard")); m.color = c; m.SetFloat("_Glossiness", g); return m; }
            var bamboo = M(new Color(.9f, .72f, .45f), .3f); var rim = M(new Color(.7f, .5f, .28f), .25f); var bun = M(new Color(1f, .96f, .9f), .45f);
            var red = M(new Color(.85f, .25f, .3f), .45f); var pillar = M(new Color(.95f, .82f, .6f), .3f);
            slab = new GameObject("蒸笼门扇").transform; slab.SetParent(transform, false);
            float tier = h / 3f;
            for (int i = 0; i < 3; i++)
            {
                // 压扁的竹蒸笼：椭圆柱（宽 = 门宽，厚 0.9 m），上下各一圈深色竹圈，正面一个小包子图案
                var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Arena.Kill(c.GetComponent<Collider>()); c.name = "蒸笼";
                c.transform.SetParent(slab, false); c.transform.localPosition = new Vector3(0, tier * (i + .5f), 0); c.transform.localScale = new Vector3(w, tier * .47f, .9f);
                c.GetComponent<Renderer>().sharedMaterial = bamboo;
                foreach (float yy in new[] { .06f, tier - .06f })
                {
                    var r = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Arena.Kill(r.GetComponent<Collider>()); r.name = "蒸笼竹圈";
                    r.transform.SetParent(slab, false); r.transform.localPosition = new Vector3(0, tier * i + yy, 0); r.transform.localScale = new Vector3(w + .06f, .06f, .96f);
                    r.GetComponent<Renderer>().sharedMaterial = rim;
                }
                foreach (float side in new[] { 1f, -1f })
                {
                    var b = GameObject.CreatePrimitive(PrimitiveType.Sphere); Arena.Kill(b.GetComponent<Collider>()); b.name = "包子图案";
                    b.transform.SetParent(slab, false); b.transform.localPosition = new Vector3(0, tier * (i + .5f), side * .43f); b.transform.localScale = new Vector3(.55f, .4f, .12f);
                    b.GetComponent<Renderer>().sharedMaterial = bun;
                }
            }
            Cube(slab, new Vector3(0, h + .12f, 0), new Vector3(1.2f, .14f, .14f), rim);   // 提手
            // 两侧立柱（只有外观，墙本身有碰撞）
            foreach (float s in new[] { 1f, -1f }) Cube(transform, new Vector3(s * (w / 2 + .3f), (h + .9f) / 2, 0), new Vector3(.55f, h + .9f, 1f), pillar);
            // 筷子拉杆：开着时竖起，关上时倒下（MAP-A5）
            Cube(transform, new Vector3(w / 2 + .3f, 1.1f, .62f), new Vector3(.5f, .6f, .25f), red);
            lever = new GameObject("筷子拉杆").transform; lever.SetParent(transform, false); lever.localPosition = new Vector3(w / 2 + .3f, 1.35f, .78f);
            foreach (float dx in new[] { -.07f, .07f }) Cube(lever, new Vector3(dx, .7f, 0), new Vector3(.07f, 1.4f, .07f), red);
            Cube(transform, new Vector3(w / 2 + .3f, h + 1.0f, .55f), new Vector3(.3f, .2f, .1f), Arena.Glow(new Color(1f, .7f, .3f), 3f));
            open = true; t = 1; Apply(1);
        }
        public void Build(float w, float h, Material frame)
        {
            width = w; height = h; holdTime = 1.6f; range = 2.6f;
            col = gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(w, h, .25f); col.center = new Vector3(0, h / 2, 0);
            obstacle = gameObject.AddComponent<NavMeshObstacle>(); obstacle.shape = NavMeshObstacleShape.Box; obstacle.size = col.size; obstacle.center = col.center; obstacle.carving = true;
            var model = Arena.Piece("PH/rollershutter_door/rollershutter_door", "rollershutter_door_graffiti");
            if (model != null)
            {
                Arena.Fit(model, new Vector3(w, h, .25f));
                slab = new GameObject("卷帘").transform; slab.SetParent(transform, false);
                model.transform.SetParent(slab, false);
            }
            else { slab = Cube(transform, new Vector3(0, h / 2, 0), new Vector3(w, h, .2f), frame).transform; }
            Cube(transform, new Vector3(0, h + .25f, 0), new Vector3(w + .4f, .5f, .6f), frame);
            Cube(transform, new Vector3(-w / 2 - .12f, h / 2, 0), new Vector3(.24f, h, .4f), frame);
            Cube(transform, new Vector3(w / 2 + .12f, h / 2, 0), new Vector3(.24f, h, .4f), frame);
            Cube(transform, new Vector3(0, h + .04f, .32f), new Vector3(w * .6f, .06f, .04f), Arena.Glow(new Color(1f, .55f, .1f), 2.5f));
        }
        protected override void OnStart(Fighter f)
        {
            if (gate) Sfx.Play("food_dumpling", transform.position, .35f, .8f, 1f, 15f);   // 蒸笼门：轻轻的竹子摩擦声，不再是电脑噪音
            else base.OnStart(f);
        }
        public override bool Usable(Fighter f)
        {
            if (t < 1f) return false;
            if (!gate) return true;
            holdTime = open ? GateCloseHold : GateOpenHold;
            return !open || CanClose(f);
        }
        public override string Prompt(Fighter f)
        {
            if (gate) return open ? "按住 G 盖上蒸笼门（2 秒 · 巨响，全图可闻）" : "按住 G 掀开蒸笼门（1.2 秒）";
            return open ? "按住 G 放下卷帘门" : "按住 G 升起卷帘门（会发出噪音）";
        }
        public override Vector3 Anchor { get { return transform.position + Vector3.up * .9f; } }
        protected override void Complete(Fighter f) { SetOpen(!open); f.stats.doorsOpened++; if (Match.I != null) Match.I.Feed(Config.TeamName(f.team) + " " + f.Hero.name + (gate ? (open ? " 掀开了" : " 盖上了") : (open ? " 升起了" : " 放下了")) + name); }
        public void SetOpen(bool o)
        {
            open = o; t = 0;
            if (Match.I != null && Match.I.phase == Phase.Live) Debug.Log("NEON_DOOR r" + Match.I.round + " " + name + " open=" + o);
            if (gate && Match.I != null) Match.I.OnGate(o);
            // V22：蒸笼门不再用电子 / 金属音，改成竹蒸笼的木头闷响 + 蒸汽嘶声；盖上仍然远处可闻（守方关门是情报），但音量降低
            if (gate) { Sfx.Play("food_bonk", transform.position, o ? .45f : .65f, o ? .8f : .6f, 1f, o ? 40f : 90f); Sfx.Play("food_fizz", transform.position, .35f, .7f, 1f, o ? 25f : 45f); }
            else Sfx.Play("engineCircular", transform.position, 1f, .7f, 1f, 50f);
            if (gate) Fx.Burst(transform.position + Vector3.up * (height + .5f), Vector3.up, new Color(1f, 1f, 1f, .7f), 24, 2.5f, .5f, 1.4f, -.2f, Fx.Smoke);
            if (Match.I != null) Match.I.NoiseAt(null, transform.position);
        }
        public override void ResetRound() { base.ResetRound(); open = gate; t = 1; Apply(open ? 1 : 0); }   // 蒸笼门每回合开着，卷帘门关着
        void Apply(float k)
        {
            if (slab != null && gate) slab.localPosition = new Vector3(0, k * (height + .9f), 0);   // 蒸笼整体升到门楣上方
            else if (slab != null) { slab.localPosition = new Vector3(0, k * (height - .3f), 0); slab.localScale = new Vector3(1, Mathf.Max(.1f, 1 - k * .88f), 1); }
            if (lever != null) lever.localRotation = Quaternion.Euler(0, 0, (1 - k) * -75f);
            bool blocking = k < .6f;
            col.enabled = blocking; obstacle.enabled = blocking;
        }
        void Update()
        {
            if (t >= 1f) return;
            t = Mathf.Min(1f, t + Time.deltaTime / (gate ? GateMoveTime : 1.8f));
            Apply(open ? t : 1 - t);
        }
    }

    // ---------------------------------------------------------------- 门禁 + 终端
    public class Door : MonoBehaviour
    {
        public bool open;
        Transform left, right; NavMeshObstacle obstacle; Light beacon; float t;
        public void Build(Material plate, Material glow)
        {
            left = Panel(plate, glow, -1); right = Panel(plate, glow, 1);
            obstacle = gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box; obstacle.size = new Vector3(4, 3, .8f); obstacle.center = new Vector3(0, 1.5f, 0); obstacle.carving = true;
            beacon = new GameObject("警示灯").AddComponent<Light>(); beacon.transform.SetParent(transform, false);
            beacon.transform.localPosition = new Vector3(0, 3.9f, .7f); beacon.type = LightType.Point; beacon.color = new Color(1f, .55f, .1f); beacon.range = 9; beacon.intensity = 0;
        }
        Transform Panel(Material plate, Material glow, int side)
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
            p.name = "门板"; p.transform.SetParent(transform, false);
            p.transform.localPosition = new Vector3(side, 1.6f, 0); p.transform.localScale = new Vector3(2, 3.2f, .3f);
            p.GetComponent<Renderer>().sharedMaterial = plate;
            var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Arena.Kill(strip.GetComponent<Collider>()); strip.transform.SetParent(p.transform, false);
            strip.transform.localScale = new Vector3(.9f, .06f, 1.15f);
            strip.GetComponent<Renderer>().sharedMaterial = glow;
            return p.transform;
        }
        public void Open()
        {
            if (open) return;
            open = true; t = 0; obstacle.enabled = false;
            Sfx.Play("doorOpen", transform.position, 1f, .8f, 1f, 60f);
            Sfx.Play("forceField", transform.position, .6f, .6f, 1f, 60f);
        }
        public void Close()
        {
            open = false; t = 0; obstacle.enabled = true;
            left.localPosition = new Vector3(-1, 1.6f, 0); right.localPosition = new Vector3(1, 1.6f, 0);
            beacon.intensity = 0;
        }
        void Update()
        {
            if (!open) return;
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / 1f));
            left.localPosition = new Vector3(-1 - k * 1.95f, 1.6f, 0); right.localPosition = new Vector3(1 + k * 1.95f, 1.6f, 0);
            beacon.intensity = t < 4f ? (Mathf.Repeat(t * 2.5f, 1f) < .5f ? 3.5f : .3f) : .8f;
        }
    }

    public class Terminal : Interactable
    {
        public Door door; Renderer screen; Material idle, used;
        public void Build(Material plate, Material glow)
        {
            holdTime = Config.TerminalHoldTime;
            Cube(transform, new Vector3(0, .65f, 0), new Vector3(.8f, 1.3f, .45f), plate, true);
            screen = Cube(transform, new Vector3(0, 1.05f, .235f), new Vector3(.6f, .4f, .02f), glow).GetComponent<Renderer>();
            idle = glow; used = Arena.Glow(new Color(.2f, 1f, .5f), 2.5f);
            var l = new GameObject("终端灯").AddComponent<Light>(); l.transform.SetParent(transform, false);
            l.transform.localPosition = new Vector3(0, 1.2f, .8f); l.color = new Color(1f, .6f, .2f); l.range = 4; l.intensity = 1.2f;
        }
        public override bool Usable(Fighter f) { return !door.open; }
        public override string Prompt(Fighter f) { return "按住 G 启动门禁（会触发警报）"; }
        public override Vector3 Anchor { get { return transform.position + Vector3.up; } }
        protected override void Complete(Fighter f)
        {
            door.Open(); screen.sharedMaterial = used;
            if (Match.I != null) { Match.I.Feed(Config.TeamName(f.team) + " " + f.Hero.name + " 打开了" + door.name); Match.I.NoiseAt(f, transform.position); }
            f.stats.doorsOpened++;
        }
        public override void ResetRound() { base.ResetRound(); if (screen != null) screen.sharedMaterial = idle; }
    }

    // ---------------------------------------------------------------- 轻型可破坏掩体（黄色警示灯条）
    public class Destructible : MonoBehaviour, IDamageable
    {
        public float hp;
        GameObject visual; BoxCollider col; NavMeshObstacle obstacle; Vector3 size;
        public bool Alive { get { return hp > 0; } }
        public void Build(Vector3 s)
        {
            size = s;
            col = gameObject.AddComponent<BoxCollider>(); col.size = s; col.center = new Vector3(0, s.y / 2, 0);
            obstacle = gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box; obstacle.size = s; obstacle.center = col.center; obstacle.carving = true;
            visual = new GameObject("visual"); visual.transform.SetParent(transform, false);
            for (int i = 0; i < 2; i++)
            {
                var crate = ToonAssets.Available ? Arena.Model("Toon/Env/Crate") : Arena.Model("PH/wooden_crate_01/wooden_crate_01");
                if (crate == null) break;
                Arena.Fit(crate, new Vector3(s.x * .98f, s.y * .5f, s.z));
                crate.transform.SetParent(visual.transform, false);
                crate.transform.localPosition += new Vector3(0, i * s.y * .5f, 0);
                crate.transform.localRotation = Quaternion.Euler(0, i * 4f, 0);
            }
            for (int side = -1; side <= 1; side += 2)
                Interactable_Stripe(visual.transform, new Vector3(0, s.y * .52f, side * (s.z / 2 + .01f)), new Vector3(s.x * .96f, .07f, .02f));
            Restore();
        }
        static void Interactable_Stripe(Transform p, Vector3 lp, Vector3 size)
        {
            var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Arena.Kill(stripe.GetComponent<Collider>());
            stripe.transform.SetParent(p, false); stripe.transform.localPosition = lp; stripe.transform.localScale = size;
            stripe.GetComponent<Renderer>().sharedMaterial = Arena.Glow(new Color(1f, .78f, .1f), 1.4f);
        }
        public void Restore() { hp = Config.LightCoverHp; visual.SetActive(true); col.enabled = true; obstacle.enabled = true; }
        public bool TakeDamage(float amount, Fighter source, bool explosive)
        {
            if (hp <= 0) return false;
            hp -= amount;
            Fx.Burst(transform.position + Vector3.up * size.y * .6f, Vector3.up, new Color(.8f, .55f, .25f), 6, 3, .08f, .4f, 1);
            if (hp <= 0)
            {
                visual.SetActive(false); col.enabled = false; obstacle.enabled = false;
                Fx.Debris(transform.position + Vector3.up * .6f, new Color(.45f, .3f, .18f), 18);
                Sfx.Play("impactGeneric_light", transform.position, 1f, .7f);
                if (Match.I != null) Match.I.Feed("轻型掩体被摧毁");
                if (source != null) source.stats.coverDestroyed++;
            }
            return true;
        }
    }

    // ---------------------------------------------------------------- 爆炸桶：被击中后 0.5 s 引信，爆炸伤害所有人
    public class ExplosiveBarrel : MonoBehaviour, IDamageable
    {
        float hp = 30, fuseAt = -1; GameObject visual; Collider col; Fighter lastHitter; Light warn;
        public void Build()
        {
            visual = Arena.Model("PH/Barrel_01/Barrel_01") ?? GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Arena.Fit(visual, new Vector3(.62f, .95f, .62f));
            visual.transform.SetParent(transform, false);
            foreach (var r in visual.GetComponentsInChildren<Renderer>()) { var m = r.material; m.color = new Color(1f, .35f, .25f); }
            var band = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Arena.Kill(band.GetComponent<Collider>()); band.transform.SetParent(transform, false);
            band.transform.localPosition = new Vector3(0, .62f, 0); band.transform.localScale = new Vector3(.64f, .03f, .64f);
            band.GetComponent<Renderer>().sharedMaterial = Arena.Glow(new Color(1f, .4f, .1f), 2.5f);
            var c = gameObject.AddComponent<CapsuleCollider>(); c.radius = .31f; c.height = .95f; c.center = new Vector3(0, .475f, 0); col = c;
            warn = new GameObject("引信").AddComponent<Light>(); warn.transform.SetParent(transform, false); warn.transform.localPosition = Vector3.up * 1.1f;
            warn.color = new Color(1f, .4f, .1f); warn.range = 4; warn.intensity = 0;
        }
        public void ResetRound() { hp = 30; fuseAt = -1; visual.SetActive(true); col.enabled = true; warn.intensity = 0; }
        public bool TakeDamage(float amount, Fighter source, bool explosive)
        {
            if (hp <= 0 && fuseAt < 0) return false;
            if (source != null) lastHitter = source;
            hp -= amount;
            if (hp <= 0 && fuseAt < 0) { fuseAt = Time.time + (explosive ? .15f : .55f); Sfx.Play("thrusterFire", transform.position, .8f, 1.6f, 1f, 30f); }
            return true;
        }
        void Update()
        {
            if (fuseAt < 0) return;
            warn.intensity = Mathf.Repeat(Time.time * 12, 1) < .5f ? 5 : 0;
            if (Random.value < .6f) Fx.Burst(transform.position + Vector3.up * 1f, Vector3.up, new Color(1f, .6f, .2f), 2, 3, .05f, .3f, .5f);
            if (Time.time < fuseAt) return;
            fuseAt = -1; warn.intensity = 0;
            visual.SetActive(false); col.enabled = false;
            Vector3 p = transform.position + Vector3.up * .5f;
            Fx.Explosion(p, 4.5f);
            if (Match.I != null)
            {
                foreach (var f in Match.I.fighters)
                {
                    if (!f.alive) continue;
                    float d = Vector3.Distance(f.Chest, p);
                    if (d < 5f && Skills.LineClear(p, f.Chest, f)) { DamageCtx.Set(DamageKind.Env, "爆炸桶"); f.ReceiveDamage(70f * (1 - d / 6f), lastHitter, p, false, true); }
                }
                Match.I.NoiseAt(lastHitter, p);
            }
            foreach (var c in Physics.OverlapSphere(p, 4.5f))
            {
                if (c.transform.IsChildOf(transform)) continue;
                var dmg = c.GetComponentInParent<IDamageable>();
                if (dmg != null && !(dmg is Fighter)) dmg.TakeDamage(80, lastHitter, true);
            }
        }
    }

    // ---------------------------------------------------------------- 可打碎玻璃：挡第一发子弹后碎裂，打开视线
    public class GlassPanel : MonoBehaviour, IDamageable
    {
        Renderer r; Collider col; Vector3 size;
        public void Build(Vector3 s, Color tint)
        {
            size = s;
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.transform.SetParent(transform, false); g.transform.localScale = s; g.transform.localPosition = new Vector3(0, s.y / 2, 0);
            Arena.Kill(g.GetComponent<Collider>());
            r = g.GetComponent<Renderer>();
            var m = new Material(Resources.Load<Material>("Materials/Holo") ?? Fx.Additive);
            m.SetColor("_Color", tint * .35f); m.SetFloat("_Lines", 0);
            r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var bc = gameObject.AddComponent<BoxCollider>(); bc.size = s; bc.center = new Vector3(0, s.y / 2, 0); col = bc;
        }
        public void ResetRound() { r.enabled = true; col.enabled = true; }
        public bool TakeDamage(float amount, Fighter source, bool explosive)
        {
            if (!col.enabled) return false;
            r.enabled = false; col.enabled = false;
            Fx.Burst(transform.position + Vector3.up * size.y / 2, transform.forward, new Color(.7f, .9f, 1f), 50, 5, .07f, 1.2f, 1.5f);
            Sfx.Play("impactGlass_heavy", transform.position, 1f, 1f, 1f, 40f);
            return false;   // bullets keep flying through the broken pane
        }
    }

    // ---------------------------------------------------------------- 电闸：E 拉闸，关闭该区域灯光 20 s
    public class PowerBox : Interactable
    {
        public string zone; public float offUntil;
        public readonly List<Light> lights = new List<Light>();
        public readonly List<Renderer> neon = new List<Renderer>();
        readonly Dictionary<Light, float> baseIntensity = new Dictionary<Light, float>();
        Light led;
        public void Build()
        {
            holdTime = 1.2f;
            var model = Arena.Model("PH/power_box_01/power_box_01");
            if (model != null) { Arena.Fit(model, new Vector3(.7f, .76f, .6f)); model.transform.SetParent(transform, false); model.transform.localPosition += Vector3.up * 1.1f; }
            Cube(transform, new Vector3(0, .55f, 0), new Vector3(.15f, 1.1f, .15f), Resources.Load<Material>("Materials/MetalPlate"), true);
            led = new GameObject("电闸指示灯").AddComponent<Light>(); led.transform.SetParent(transform, false); led.transform.localPosition = new Vector3(0, 1.9f, .5f);
            led.range = 3; led.intensity = 1.5f; led.color = new Color(.3f, 1f, .4f);
        }
        public void Register(Light l) { lights.Add(l); baseIntensity[l] = l.intensity; }
        public override bool Usable(Fighter f) { return Time.time >= offUntil; }
        public override string Prompt(Fighter f) { return "按住 G 拉下电闸：" + zone + "断电 20 秒"; }
        public override Vector3 Anchor { get { return transform.position + Vector3.up * 1.2f; } }
        protected override void Complete(Fighter f)
        {
            offUntil = Time.time + 20f;
            Sfx.Play("lowFrequency_explosion", transform.position, .5f, 2f, 1f, 60f);
            Sfx.Play("glitch", transform.position, 1f, .6f, 1f, 60f);
            if (Match.I != null) Match.I.Feed("⚡ " + Config.TeamName(f.team) + " " + f.Hero.name + " 切断了" + zone + "的电源", new Color(1f, .9f, .4f));
            f.stats.doorsOpened++;
        }
        public override void ResetRound() { base.ResetRound(); offUntil = 0; }
        public bool Dark { get { return Time.time < offUntil; } }
        void Update()
        {
            bool dark = Dark;
            float flick = dark && offUntil - Time.time < 1.5f ? (Random.value < .5f ? 1 : 0) : (dark ? 0 : 1);
            foreach (var l in lights) if (l != null) l.intensity = baseIntensity[l] * flick;
            foreach (var r in neon) if (r != null) r.enabled = flick > 0;
            led.color = dark ? new Color(1f, .2f, .1f) : new Color(.3f, 1f, .4f);
        }
    }

    // ---------------------------------------------------------------- 医疗站：每回合一次，+50 生命 / +25 护甲
    public class MedStation : Interactable
    {
        bool used; Light glow; Renderer screen;
        public bool Used { get { return used; } }
        public void Build()
        {
            holdTime = 1.0f;
            Cube(transform, new Vector3(0, 1.0f, 0), new Vector3(.9f, 2.0f, .5f), Resources.Load<Material>("Materials/MetalPlate"), true);
            var box = Arena.Model("PH/medical_box/medical_box");
            if (box != null) { Arena.Fit(box, new Vector3(.53f, .12f, .35f)); box.transform.SetParent(transform, false); box.transform.localPosition += new Vector3(0, 1.0f, .3f); box.transform.localRotation = Quaternion.Euler(-90, 0, 0); }
            screen = Cube(transform, new Vector3(0, 1.65f, .26f), new Vector3(.6f, .35f, .02f), Arena.Glow(new Color(.2f, 1f, .5f), 2.2f)).GetComponent<Renderer>();
            Cube(transform, new Vector3(0, 1.65f, .27f), new Vector3(.08f, .25f, .02f), Arena.Glow(Color.white, 3f));
            Cube(transform, new Vector3(0, 1.65f, .27f), new Vector3(.25f, .08f, .02f), Arena.Glow(Color.white, 3f));
            glow = new GameObject("医疗站灯").AddComponent<Light>(); glow.transform.SetParent(transform, false); glow.transform.localPosition = new Vector3(0, 1.7f, .9f);
            glow.color = new Color(.3f, 1f, .5f); glow.range = 5; glow.intensity = 1.6f;
        }
        public override bool Usable(Fighter f) { return !used && f.alive && (f.hp < Config.MaxHp || f.armor < Config.MaxArmor); }
        public override string Prompt(Fighter f) { return "按住 G 使用医疗站（+50 生命 +25 护甲，本回合仅一次）"; }
        public override Vector3 Anchor { get { return transform.position + Vector3.up; } }
        protected override void Complete(Fighter f)
        {
            used = true; f.hp = Mathf.Min(Config.MaxHp, f.hp + 50); f.armor = Mathf.Min(Config.MaxArmor, f.armor + 25);
            glow.color = new Color(.4f, .4f, .45f); screen.enabled = false;
            Fx.Burst(f.Chest, Vector3.up, new Color(.3f, 1f, .5f), 30, 2, .1f, .8f, -.3f);
            Sfx.Play("confirmation", transform.position, .8f, 1.2f, 1f, 20f);
        }
        public override void ResetRound() { base.ResetRound(); used = false; if (glow != null) { glow.color = new Color(.3f, 1f, .5f); screen.enabled = true; } }
    }

    // ---------------------------------------------------------------- 赛博弹射板
    public class JumpPad : MonoBehaviour
    {
        public Vector3 launch = new Vector3(0, 13.5f, 4f);
        public bool worldLaunch;
        // Ballistic launch that lands on a platform (gravity 17, apex 1.2 m above the landing height).
        public void AimAt(Vector3 landing, float over = 1.2f)
        {
            const float g = 17f;
            float h = landing.y - transform.position.y;
            float vy = Mathf.Sqrt(2 * g * (h + over));
            float t = vy / g + Mathf.Sqrt(2 * over / g);
            Vector3 flat = landing - transform.position; flat.y = 0;
            launch = flat / t + Vector3.up * vy; worldLaunch = true;
            transform.rotation = Quaternion.LookRotation(flat.sqrMagnitude > .01f ? flat : Vector3.forward);
        }
        readonly Dictionary<Fighter, float> cooldown = new Dictionary<Fighter, float>();
        readonly Dictionary<Fighter, Vector3> prevPos = new Dictionary<Fighter, Vector3>();
        public static readonly List<JumpPad> All = new List<JumpPad>();   // V22 第三期：可乐弹射联动点
        public static bool TestAllowBots;   // 测试 / 截图用：让脚本控制的 AI 也能踩弹射板
        public static Fighter LastLaunched; public static Vector3 LastLaunchPos; public static float LastLaunchAt = -9; public static bool LastWasCola;
        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }
        public Vector3 WorldLaunch { get { return worldLaunch ? launch : transform.TransformDirection(launch); } }
        Transform ring;
        public void Build(Color c)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            b.transform.SetParent(transform, false); b.transform.localScale = new Vector3(1.8f, .06f, 1.8f); b.transform.localPosition = Vector3.up * .06f;
            b.GetComponent<Renderer>().sharedMaterial = Resources.Load<Material>("Materials/MetalPlate");
            Arena.Kill(b.GetComponent<Collider>());
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Arena.Kill(g.GetComponent<Collider>());
            g.transform.SetParent(transform, false); g.transform.localScale = new Vector3(1.4f, .02f, 1.4f); g.transform.localPosition = Vector3.up * .13f;
            g.GetComponent<Renderer>().sharedMaterial = Arena.Glow(c, 3f);
            ring = g.transform;
            var l = new GameObject("弹射板灯").AddComponent<Light>(); l.transform.SetParent(transform, false); l.transform.localPosition = Vector3.up * .8f;
            l.color = c; l.range = 5; l.intensity = 2;
            var arrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Arena.Kill(arrow.GetComponent<Collider>()); arrow.transform.SetParent(transform, false);
            arrow.transform.localPosition = new Vector3(0, .15f, .5f); arrow.transform.localScale = new Vector3(.15f, .02f, .7f);
            arrow.GetComponent<Renderer>().sharedMaterial = Arena.Glow(Color.white, 2f);
            if (ToonAssets.Available)   // V17：弹射板换成果冻弹床
            {
                var jelly = Arena.Model("Toon/Food/JellyBlock");
                if (jelly != null)
                {
                    foreach (var jc in jelly.GetComponentsInChildren<Collider>()) Arena.Kill(jc);
                    Arena.Fit(jelly, new Vector3(1.8f, .4f, 1.8f));
                    jellyRoot = new GameObject("果冻弹床").transform; jellyRoot.SetParent(transform, false);
                    jelly.transform.SetParent(jellyRoot, false);
                    b.GetComponent<Renderer>().enabled = false; g.GetComponent<Renderer>().enabled = false; arrow.GetComponent<Renderer>().enabled = false;
                }
            }
        }
        Transform jellyRoot; float wobbleT = 9f;
        void Update()
        {
            ring.localScale = new Vector3(1.4f, .02f, 1.4f) * (1f + .08f * Mathf.Sin(Time.time * 6f));
            if (jellyRoot != null)
            {
                wobbleT += Time.deltaTime; float w = Mathf.Exp(-wobbleT * 5f) * Mathf.Cos(wobbleT * 22f) + .04f * Mathf.Sin(Time.time * 4f);
                jellyRoot.localScale = new Vector3(1f + .25f * w, 1f - .45f * w, 1f + .25f * w);
            }
            if (Match.I == null) return;
            foreach (var f in Match.I.fighters)
            {
                Vector3 fp = f.transform.position;
                bool hadPrev = prevPos.TryGetValue(f, out Vector3 pp); prevPos[f] = fp;
                if (!f.alive) continue;
                // V22 第三期：猎隼汽水冲刺中（或结束 DashPadWindow 秒内）踩到弹射板 → 可乐弹射。冲刺 50 m/s 一帧可能跨过弹床，按上一帧到这一帧的线段判定
                bool cola = f.role == Role.Falcon && Skills.InDashWindow(f);
                if (cola)
                {
                    if (Links.SegFlatDist(hadPrev ? pp : fp, fp, transform.position) > 1.15f || Mathf.Abs(fp.y - transform.position.y) > .9f) continue;
                }
                else
                {
                    if (!f.grounded) continue;
                    Vector3 d = fp - transform.position; d.y = 0;
                    if (d.magnitude > .9f || Mathf.Abs(fp.y - transform.position.y) > .5f) continue;
                }
                if (cooldown.TryGetValue(f, out float t) && Time.time < t) continue;
                // AI only uses a pad when it is heading for that sniper nest (otherwise it gets stranded on rooftops)
                if (!f.human && !TestAllowBots && (!f.TryGetComponent(out Bot bot) || !bot.WantsPad || cola)) continue;
                if (Match.I.autoplay) Debug.Log("NEON_PAD r" + Match.I.round + " " + name + " " + f.Hero.name + (cola ? " cola" : ""));
                cooldown[f] = Time.time + 1f;
                Vector3 v = WorldLaunch;
                if (cola) v = Links.ColaLaunch(f, this, v);
                LastLaunched = f; LastLaunchPos = f.transform.position; LastLaunchAt = Time.time; LastWasCola = cola;
                f.Launch(v);
                Fx.Burst(transform.position + Vector3.up * .2f, Vector3.up, new Color(.4f, .9f, 1f), 30, 6, .1f, .5f, 0);
                if (jellyRoot != null) { wobbleT = 0; Sfx.Play("food_boing", transform.position, 1f, Random.Range(.95f, 1.05f), 1f, 35f); }
                else Sfx.Play("thrusterFire", transform.position, 1f, 1.4f, 1f, 35f);
            }
        }
    }

    // ---------------------------------------------------------------- 守方监控摄像头：视野内的攻方被短暂标记；一枪可毁
    public class SecurityCam : MonoBehaviour, IDamageable
    {
        public float yawCenter; bool broken;
        public bool Working { get { return !broken; } } Transform head; Light lamp; LineRenderer cone;
        public void Build()
        {
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pole.transform.SetParent(transform, false); pole.transform.localScale = new Vector3(.08f, .08f, .5f); pole.transform.localPosition = new Vector3(0, 0, .25f);
            pole.GetComponent<Renderer>().sharedMaterial = Resources.Load<Material>("Materials/MetalPlate");
            Arena.Kill(pole.GetComponent<Collider>());
            head = new GameObject("镜头").transform; head.SetParent(transform, false); head.localPosition = new Vector3(0, -.1f, .5f);
            var m = Arena.Model("PH/security_camera_01/security_camera_01");
            if (m != null) { Arena.Fit(m, new Vector3(.2f, .32f, .6f)); m.transform.SetParent(head, false); m.transform.localRotation = Quaternion.Euler(0, 180, 0); }
            var col = head.gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(.3f, .35f, .6f);
            lamp = new GameObject("录像灯").AddComponent<Light>(); lamp.transform.SetParent(head, false); lamp.transform.localPosition = new Vector3(0, 0, .4f);
            lamp.color = new Color(1f, .1f, .1f); lamp.range = 2; lamp.intensity = 2;
        }
        public void ResetRound() { broken = false; head.gameObject.SetActive(true); }
        public bool TakeDamage(float amount, Fighter source, bool explosive)
        {
            if (broken) return false;
            if (source != null && Match.I != null && source.team == Match.I.DefendingTeam) return false;
            broken = true; head.gameObject.SetActive(false);
            Fx.Burst(head.position, Vector3.down, new Color(1f, .8f, .4f), 25, 4, .05f, .6f, 1);
            Sfx.Play("impactMetal", head.position, 1f, 1.4f);
            if (Match.I != null) Match.I.Feed("守方监控被摧毁");
            return true;
        }
        void Update()
        {
            if (broken || Match.I == null) return;
            float yaw = yawCenter + Mathf.Sin(Time.time * .5f) * 40f;
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            head.localRotation = Quaternion.Euler(18, 0, 0);
            lamp.intensity = Mathf.Repeat(Time.time, 1) < .5f ? 2.5f : .3f;
            if (!Match.I.CanAct) return;
            Vector3 eye = head.position;
            foreach (var f in Match.I.fighters)
            {
                if (!f.alive || f.team == Match.I.DefendingTeam) continue;
                Vector3 to = f.Chest - eye;
                if (to.magnitude > 28f || Vector3.Angle(head.forward, to) > 35f) continue;
                if (Time.time < f.hiddenFromScanUntil) continue;
                if (Skills.LineClear(eye, f.Chest, f)) f.revealedUntil = Mathf.Max(f.revealedUntil, Time.time + .6f);
            }
        }
    }
}

namespace NeonSiege
{
    // V13：可破坏墙（彩虹六号式）—— 墙由 0.5 m 方块拼成。子弹逐块打穿，手雷/爆破装置直接炸开一个洞。
    // 每 1 m 宽一列配一个寻路障碍：这一列下面 2 m 都被打通后，AI 才会从洞里穿过去。
    public class BreakWall : MonoBehaviour
    {
        public const float Cell = .5f, PieceHp = 70f;
        public readonly List<WallPiece> pieces = new List<WallPiece>();
        public Material[] layers;   // V17：按行换材质 = 一层层的蛋糕
        readonly List<NavMeshObstacle> columns = new List<NavMeshObstacle>();
        int nx, ny; float thick, cw = Cell, chh = Cell;
        public float Width { get; private set; }
        public float Height { get; private set; }
        public int Broken { get { int n = 0; foreach (var p in pieces) if (!p.Alive) n++; return n; } }

        public void Build(float width, float height, float thickness, Material mat)
        {
            thick = thickness;
            nx = Mathf.Max(1, Mathf.RoundToInt(width / Cell)); ny = Mathf.Max(1, Mathf.RoundToInt(height / Cell));
            cw = width / nx; chh = height / ny; Width = width; Height = height;
            for (int iy = 0; iy < ny; iy++)
                for (int ix = 0; ix < nx; ix++)
                {
                    var c = GameObject.CreatePrimitive(PrimitiveType.Cube); c.name = "墙块";
                    c.transform.SetParent(transform, false);
                    c.transform.localPosition = new Vector3(-width / 2 + cw * (ix + .5f), chh * (iy + .5f), 0);
                    c.transform.localScale = new Vector3(cw * 1.002f, chh * 1.002f, thickness);
                    c.GetComponent<Renderer>().sharedMaterial = layers != null ? layers[iy % layers.Length] : mat;
                    var wp = c.AddComponent<WallPiece>(); wp.wall = this; wp.ix = ix; wp.iy = iy; wp.hp = PieceHp;
                    pieces.Add(wp);
                }
            int cols = Mathf.Max(1, Mathf.RoundToInt(width));
            for (int i = 0; i < cols; i++)
            {
                var o = new GameObject("墙列寻路障碍"); o.transform.SetParent(transform, false);
                o.transform.localPosition = new Vector3(-width / 2 + (i + .5f) * width / cols, height / 2, 0);
                var ob = o.AddComponent<NavMeshObstacle>(); ob.shape = NavMeshObstacleShape.Box; ob.size = new Vector3(width / cols, height, thickness + .1f); ob.carving = true;
                columns.Add(ob);
            }
            // warning stencil so players know the wall can be breached
            for (int side = -1; side <= 1; side += 2)
            {
                var t = new GameObject("可破坏标识").AddComponent<TextMesh>(); t.transform.SetParent(transform, false);
                t.transform.localPosition = new Vector3(0, Mathf.Min(height - .3f, 2.4f), side * (thickness / 2 + .02f));
                t.transform.localRotation = Quaternion.Euler(0, side > 0 ? 180 : 0, 0);
                var font = Resources.Load<Font>("NotoSansSC"); t.font = font; if (font != null) t.GetComponent<MeshRenderer>().sharedMaterial = Arena.WorldText(font);
                t.text = layers != null ? "蛋糕墙 · 可以打穿 / 炸开\n奶油裱花（BR-5）打它是补墙" : "▧ 薄墙 · 可击穿 / 炸开"; t.anchor = TextAnchor.MiddleCenter; t.fontSize = 64; t.characterSize = .04f; t.color = new Color(1f, .75f, .3f, .8f);   // V21：补上 BR-5 补墙说明
            }
        }

        public void Invalidate() { openFrame = -1; }   // V22 第三期：墙块碎 / 补回时，破口缓存作废
        public void OnPieceBroken(WallPiece p)
        {
            openFrame = -1;
            // an unsupported piece above a hole falls too (no floating plaster)
            foreach (var q in pieces) if (q.Alive && q.ix == p.ix && q.iy == p.iy + 1 && Random.value < .5f) { q.Break(null); break; }
            int cols = columns.Count;
            for (int c = 0; c < cols; c++)
            {
                if (!columns[c].enabled) continue;
                float x0 = (float)c / cols * nx, x1 = (float)(c + 1) / cols * nx; bool open = true;
                foreach (var q in pieces)
                    if (q.ix >= Mathf.FloorToInt(x0) && q.ix < Mathf.CeilToInt(x1) && q.iy * Cell < 2f && q.Alive) { open = false; break; }
                if (open) columns[c].enabled = false;
            }
        }

        // V22 第三期：当前最大的"能过人的破口"（离地 2 m 以内整列都碎了、连续 ≥ 1 m 宽）。开局完整的墙返回 null（铆钉只能补被打出来的洞）
        int openFrame = -1; Links.Breach openCache;
        public Links.Breach Opening()
        {
            if (openFrame == Time.frameCount) return openCache;
            openFrame = Time.frameCount; openCache = null;
            var open = new bool[nx];
            for (int ix = 0; ix < nx; ix++) open[ix] = true;
            foreach (var q in pieces) if (q.Alive && q.iy * chh < 1.95f) open[q.ix] = false;
            int bestS = -1, bestN = 0, s = 0, n = 0;
            for (int ix = 0; ix < nx; ix++) { if (open[ix]) { if (n == 0) s = ix; n++; if (n > bestN) { bestN = n; bestS = s; } } else n = 0; }
            if (bestN * cw < .95f) return null;
            float top = Height;
            foreach (var q in pieces) if (q.Alive && q.ix >= bestS && q.ix < bestS + bestN && q.iy * chh >= 1.95f) top = Mathf.Min(top, q.iy * chh);
            float lx = -Width / 2 + cw * (bestS + bestN / 2f);
            openCache = new Links.Breach { key = this, wall = this, center = transform.TransformPoint(new Vector3(lx, 0, 0)), rot = transform.rotation, width = bestN * cw, height = Mathf.Max(2f, top) };
            return openCache;
        }

        // V18：奶油补墙 —— 补回一块和它相邻的、已经被打掉的蛋糕块
        public void RepairNear(WallPiece src)
        {
            foreach (var q in pieces)
                if (!q.Alive && Mathf.Abs(q.ix - src.ix) + Mathf.Abs(q.iy - src.iy) <= 2)
                {
                    q.Restore(); Fx.Burst(q.transform.position, Vector3.up, new Color(1f, .7f, .85f), 10, 2f, .06f, .5f, .4f);
                    Sfx.Play("food_squish", q.transform.position, .5f, 1.3f, 1f, 20f); break;
                }
        }

        public void ResetRound()
        {
            openFrame = -1;
            foreach (var p in pieces) p.Restore();
            foreach (var c in columns) c.enabled = true;
        }
    }

    public class WallPiece : MonoBehaviour, IDamageable
    {
        public BreakWall wall; public int ix, iy; public float hp;
        public bool Alive { get { return hp > 0; } }
        public bool TakeDamage(float amount, Fighter source, bool explosive)
        {
            if (!Alive) return false;
            hp -= amount;
            Fx.Burst(transform.position, -transform.forward, wall != null && wall.layers != null ? GetComponent<Renderer>().sharedMaterial.color : new Color(.7f, .66f, .6f), 5, 2f, .04f, .3f, .9f);
            if (hp <= 0) { Break(source); return true; }
            Sfx.Play(wall != null && wall.layers != null ? "food_squish" : "hit_concrete", transform.position, .35f, Random.Range(.95f, 1.15f), 1f, 25f);
            return true;
        }
        public void Break(Fighter source)
        {
            hp = 0;
            GetComponent<Renderer>().enabled = false; GetComponent<Collider>().enabled = false;
            Fx.Burst(transform.position, Vector3.up, new Color(.6f, .57f, .52f, .9f), 14, 3f, .08f, .9f, 1.2f, Fx.Smoke);
            for (int i = 0; i < 2; i++)
            {
                var chunk = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(chunk.GetComponent<Collider>());
                chunk.transform.position = transform.position + Random.insideUnitSphere * .15f; chunk.transform.localScale = Vector3.one * Random.Range(.08f, .18f);
                chunk.GetComponent<Renderer>().sharedMaterial = GetComponent<Renderer>().sharedMaterial;
                var b = chunk.AddComponent<Ballistic>(); b.velocity = (Random.insideUnitSphere + Vector3.up * .5f) * 3f; b.spin = 400; b.bounce = true; b.life = 2.5f;
            }
            Sfx.Play(wall != null && wall.layers != null ? "food_splat" : "hit_concrete", transform.position, .7f, .7f, 1f, 35f);
            if (wall != null) wall.OnPieceBroken(this);
        }
        public void Restore() { hp = BreakWall.PieceHp; GetComponent<Renderer>().enabled = true; GetComponent<Collider>().enabled = true; if (wall != null) wall.Invalidate(); }
    }
}
