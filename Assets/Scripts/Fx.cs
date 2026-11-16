using System.Collections.Generic;
using UnityEngine;

namespace NeonSiege
{
    // Kenney CC0 sound packs, grouped by file prefix (laserSmall_000..004 -> "laserSmall").
    public static class Sfx
    {
        static readonly Dictionary<string, List<AudioClip>> groups = new Dictionary<string, List<AudioClip>>();
        static readonly List<AudioSource> pool = new List<AudioSource>();
        static Transform root;
        public static float LastPlayedAt;
        public static string LastPlayed = "";
        public static readonly System.Collections.Generic.Dictionary<string, float> PlayedAt = new System.Collections.Generic.Dictionary<string, float>();   // V24：每组音效最后一次播放时间（测试判定用）
        public static bool PlayedSince(string group, float t) { return PlayedAt.TryGetValue(group, out float at) && at >= t; }

        static void Load()
        {
            if (groups.Count > 0) return;
            foreach (var clip in Resources.LoadAll<AudioClip>("Audio"))
            {
                int cut = clip.name.LastIndexOf('_');
                string key = cut > 0 ? clip.name.Substring(0, cut) : clip.name;
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<AudioClip>();
                list.Add(clip);
            }
        }

        public static void Play(string group, Vector3 pos, float volume = 1f, float pitch = 1f, float spatial = 1f, float maxDistance = 45f)
        {
            Load();
            LastPlayed = group; LastPlayedAt = Time.time; PlayedAt[group] = Time.time;
            if (!groups.TryGetValue(group, out var list) || list.Count == 0) return;
            if (root == null) root = new GameObject("Sfx Pool").transform;
            AudioSource src = null;
            foreach (var s in pool) if (s != null && !s.isPlaying) { src = s; break; }
            if (src == null)
            {
                if (pool.Count >= 64) { src = pool[0]; pool.RemoveAt(0); pool.Add(src); }
                else
                {
                    src = new GameObject("sfx").AddComponent<AudioSource>();
                    src.transform.SetParent(root);
                    src.rolloffMode = AudioRolloffMode.Linear; src.dopplerLevel = 0;
                    pool.Add(src);
                }
            }
            src.transform.position = pos;
            src.spatialBlend = spatial; src.maxDistance = maxDistance; src.minDistance = 2f;
            // V9：远处变闷、隔墙更闷（低通 + 衰减），近处保持清脆
            var lp = src.GetComponent<AudioLowPassFilter>(); if (lp == null) lp = src.gameObject.AddComponent<AudioLowPassFilter>();
            float cutoff = 22000f;
            if (spatial > .5f && Camera.main != null)
            {
                Vector3 ear = Camera.main.transform.position;
                float d = Vector3.Distance(ear, pos);
                cutoff = Mathf.Lerp(22000f, 3500f, Mathf.Clamp01(d / Mathf.Max(20f, maxDistance)));
                if (d > 4f && Occluded(ear, pos)) { cutoff = Mathf.Min(cutoff, 1000f); volume *= .6f; }
            }
            lp.cutoffFrequency = cutoff;
            src.pitch = pitch * Random.Range(.95f, 1.05f);
            src.volume = volume;
            src.clip = list[Random.Range(0, list.Count)];
            src.Play();
        }

        static bool Occluded(Vector3 ear, Vector3 pos)
        {
            foreach (var h in Physics.RaycastAll(ear, pos - ear, Vector3.Distance(ear, pos) - .5f, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider.GetComponentInParent<Fighter>() == null && h.collider.bounds.size.y > 1.6f) return true;
            return false;
        }

        public static bool Has(string group) { Load(); return groups.TryGetValue(group, out var l) && l.Count > 0; }   // V23：测试读音效分组是否加载
        public static int Variants(string group) { Load(); return groups.TryGetValue(group, out var l) ? l.Count : 0; }

        public static void Ui(string group, float volume = .5f, float pitch = 1f)
        {
            Play(group, Camera.main != null ? Camera.main.transform.position : Vector3.zero, volume, pitch, 0f);
        }
    }

    public class DamageNumber { public Vector3 pos; public float amount, born, last; public bool head, armor, kill; public Fighter target; }

    public static class Fx
    {
        static Material additive, smoke;
        public static readonly List<DamageNumber> numbers = new List<DamageNumber>();
        public static float shake;   // camera shake magnitude, decays in Fighter.UpdateView

        public static Material Additive
        {
            get
            {
                if (additive == null) additive = Resources.Load<Material>("Materials/FxAdditive");
                if (additive == null) additive = new Material(Shader.Find("Sprites/Default"));
                return additive;
            }
        }
        public static Material Smoke
        {
            get
            {
                if (smoke == null) smoke = Resources.Load<Material>("Materials/FxSmoke");
                return smoke != null ? smoke : Additive;
            }
        }

        // Hits on the same target within 0.7 s stack into one growing number that follows the target.
        public static void AddNumber(Vector3 p, float amount, bool head, bool armor, bool kill, Fighter target)
        {
            foreach (var n in numbers)
                if (n.target == target && target != null && Time.time - n.last < .7f)
                { n.amount += amount; n.last = Time.time; n.head |= head; n.kill |= kill; n.armor = armor; n.born = Time.time - .05f; return; }
            numbers.Add(new DamageNumber { pos = p, amount = amount, head = head, armor = armor, kill = kill, born = Time.time, last = Time.time, target = target });
            if (numbers.Count > 24) numbers.RemoveAt(0);
        }
        public static void AddNumber(Vector3 p, float amount, bool head, bool armor, bool kill)
        {
            numbers.Add(new DamageNumber { pos = p + Random.insideUnitSphere * .2f, amount = amount, head = head, armor = armor, kill = kill, born = Time.time });
            if (numbers.Count > 24) numbers.RemoveAt(0);
        }

        public static void Shake(Vector3 at, float strength, float radius)
        {
            var cam = Camera.main; if (cam == null) return;
            float d = Vector3.Distance(cam.transform.position, at);
            if (d < radius) shake = Mathf.Max(shake, strength * (1 - d / radius));
        }

        public static void Tracer(Vector3 from, Vector3 to, Color color, float width = .025f)
        {
            var go = new GameObject("tracer");
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2; lr.SetPosition(0, from); lr.SetPosition(1, to);
            lr.material = Additive; lr.startWidth = width; lr.endWidth = width * .5f;
            lr.startColor = color; lr.endColor = new Color(color.r, color.g, color.b, 0f);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<FadeLine>();
        }

        public static ParticleSystem Burst(Vector3 pos, Vector3 normal, Color color, int count, float speed, float size, float life, float gravity = .6f, Material mat = null)
        {
            var go = new GameObject("burst");
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(normal.sqrMagnitude > .01f ? normal : Vector3.up);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = .1f; main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * .5f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * .4f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * .5f, size);
            main.startColor = color; main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission; emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0, (short)count) });
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 35; shape.radius = .05f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(color, .3f) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.material = mat != null ? mat : Additive;
            ps.Play();
            Object.Destroy(go, life + .3f);
            return ps;
        }

        // Sparks that streak (stretched billboards) — used for metal impacts and muzzle sparks.
        public static void Sparks(Vector3 pos, Vector3 normal, Color c, int count)
        {
            var ps = Burst(pos, normal, c, count, 7f, .035f, .35f, 1.4f);
            var r = ps.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = .06f; r.lengthScale = 1.5f;
        }

        public static void Debris(Vector3 pos, Color c, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var d = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(d.GetComponent<Collider>());
                d.transform.position = pos + Random.insideUnitSphere * .4f;
                d.transform.localScale = Vector3.one * Random.Range(.05f, .18f);
                d.transform.rotation = Random.rotation;
                var m = d.GetComponent<Renderer>(); m.material.color = c * Random.Range(.6f, 1.1f);
                var b = d.AddComponent<Ballistic>(); b.velocity = (Random.insideUnitSphere + Vector3.up) * Random.Range(3f, 7f); b.life = Random.Range(1.2f, 2f);
            }
        }

        public static void BulletHole(Vector3 pos, Vector3 normal, bool glow)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(q.GetComponent<Collider>());
            q.transform.position = pos + normal * .01f;
            q.transform.rotation = Quaternion.LookRotation(-normal) * Quaternion.Euler(0, 0, Random.Range(0, 360));
            q.transform.localScale = Vector3.one * Random.Range(.06f, .09f);
            var r = q.GetComponent<Renderer>();
            r.sharedMaterial = glow ? HoleGlow : HoleDark; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            q.AddComponent<Decal>();
        }
        static Material holeDark, holeGlow;
        static Material HoleDark { get { if (holeDark == null) { holeDark = new Material(Shader.Find("Sprites/Default")); holeDark.color = new Color(.02f, .02f, .02f, .85f); } return holeDark; } }
        static Material HoleGlow { get { if (holeGlow == null) { holeGlow = new Material(Additive); holeGlow.SetColor("_TintColor", new Color(1f, .5f, .2f, .6f)); } return holeGlow; } }

        public static void Shell(Vector3 pos, Vector3 right, Vector3 up)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(s.GetComponent<Collider>());
            s.transform.position = pos; s.transform.localScale = new Vector3(.012f, .022f, .012f);
            s.transform.rotation = Random.rotation;
            var r = s.GetComponent<Renderer>(); r.sharedMaterial = ToonAssets.Available ? DessertGuns.M("spr" + Random.Range(0, 6), DessertGuns.Candy(Random.Range(0, 6)), .8f) : ShellMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var b = s.AddComponent<Ballistic>(); b.velocity = right * Random.Range(2.2f, 3.2f) + up * Random.Range(1.5f, 2.5f); b.spin = 1500; b.life = 1.2f; b.bounce = true;
        }
        static Material shellMat;
        static Material ShellMat { get { if (shellMat == null) { shellMat = new Material(Shader.Find("Standard")); shellMat.color = new Color(.85f, .62f, .25f); shellMat.SetFloat("_Metallic", .9f); shellMat.SetFloat("_Glossiness", .75f); } return shellMat; } }

        public static void Flash(Vector3 pos, Color color, float intensity, float range, float time)
        {
            var go = new GameObject("flash");
            go.transform.position = pos;
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = color; l.intensity = intensity; l.range = range;
            l.shadows = LightShadows.None;
            go.AddComponent<FadeLight>().time = time;
        }

        public static void Shockwave(Vector3 pos, float radius, Color c, float time = .45f)
        {
            var go = new GameObject("冲击波");
            go.transform.position = pos + Vector3.up * .1f;
            var lr = go.AddComponent<LineRenderer>(); lr.material = Additive; lr.loop = true; lr.positionCount = 48; lr.useWorldSpace = false;
            lr.startColor = lr.endColor = c; lr.startWidth = lr.endWidth = .35f;
            var w = go.AddComponent<Wave>(); w.radius = radius; w.time = time;
        }

        public static void Explosion(Vector3 pos, float radius)
        {
            Burst(pos, Vector3.up, new Color(1f, .5f, .15f), 80, radius * 3.2f, .5f, .7f, .2f);
            Burst(pos, Vector3.up, new Color(1f, .9f, .6f), 20, radius * 1.4f, 1.6f, .25f, 0f);
            Burst(pos, Vector3.up, ToonAssets.Available ? new Color(1f, .93f, .84f, .55f) : new Color(.18f, .16f, .15f, .7f), 30, radius * 1.1f, 2.2f, 2.2f, -.08f, ToonAssets.Available ? UltFx.SoftMat : Smoke);   // V23 打磨：美食模式不出黑烟（汽水罐爆炸原来一柱黑烟盖住可乐喷泉），换成奶油色
            Sparks(pos, Vector3.up, new Color(1f, .7f, .3f), 40);
            Shockwave(pos, radius * 1.6f, new Color(1f, .6f, .3f));
            Debris(pos, new Color(.3f, .28f, .26f), 10);
            Flash(pos + Vector3.up, new Color(1f, .5f, .2f), 10f, radius * 4, .4f);
            Shake(pos, .9f, radius * 6f);
            Sfx.Play("boom", pos, 1f, Random.Range(.92f, 1.05f), 1f, 110f);
            Sfx.Play("boom_far", pos, .7f, 1f, 1f, 400f);
        }
    }

    public class Ballistic : MonoBehaviour
    {
        public Vector3 velocity; public float spin, life = 1.5f; public bool bounce; float t;
        void Update()
        {
            t += Time.deltaTime;
            velocity += Physics.gravity * Time.deltaTime;
            Vector3 next = transform.position + velocity * Time.deltaTime;
            if (Physics.Linecast(transform.position, next, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<Fighter>() == null)
            {
                next = hit.point + hit.normal * .02f;
                velocity = bounce ? Vector3.Reflect(velocity, hit.normal) * .35f : Vector3.zero;
                if (bounce && velocity.magnitude > .8f) Sfx.Play("impactMetal_light", hit.point, .08f, 2.2f, 1f, 8f);
            }
            transform.position = next;
            if (spin > 0) transform.Rotate(spin * Time.deltaTime, spin * .3f * Time.deltaTime, 0);
            if (t > life) { transform.localScale *= .9f; if (t > life + .4f) Destroy(gameObject); }
        }
    }

    public class Decal : MonoBehaviour
    {
        static readonly Queue<GameObject> live = new Queue<GameObject>();
        void Start() { live.Enqueue(gameObject); while (live.Count > 80) { var o = live.Dequeue(); if (o != null) Destroy(o); } Destroy(gameObject, 20f); }
    }

    public class Wave : MonoBehaviour
    {
        public float radius = 5, time = .45f; float t; LineRenderer lr;
        void Start() { lr = GetComponent<LineRenderer>(); }
        void Update()
        {
            t += Time.deltaTime; float k = t / time;
            if (k >= 1) { Destroy(gameObject); return; }
            float r = radius * Mathf.Sqrt(k);
            for (int i = 0; i < 48; i++) { float a = i / 48f * Mathf.PI * 2; lr.SetPosition(i, new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r)); }
            Color c = lr.startColor; c.a = 1 - k; lr.startColor = lr.endColor = c; lr.startWidth = lr.endWidth = .4f * (1 - k) + .05f;
        }
    }

    public class FadeLine : MonoBehaviour
    {
        LineRenderer lr; float t; const float Life = .07f;
        void Start() { lr = GetComponent<LineRenderer>(); }
        void Update()
        {
            t += Time.deltaTime;
            if (t >= Life) { Destroy(gameObject); return; }
            Color c = lr.startColor; c.a = 1f - t / Life; lr.startColor = c;
        }
    }

    public class FadeLight : MonoBehaviour
    {
        public float time = .1f; Light l; float start, t;
        void Start() { l = GetComponent<Light>(); start = l.intensity; }
        void Update()
        {
            t += Time.deltaTime;
            if (t >= time) { Destroy(gameObject); return; }
            l.intensity = start * (1 - t / time);
        }
    }

    public class Spin : MonoBehaviour
    {
        public Vector3 speed = new Vector3(0, 40, 0); public float bob; float y0;
        void Start() { y0 = transform.localPosition.y; }
        void Update()
        {
            transform.Rotate(speed * Time.deltaTime, Space.World);
            if (bob > 0) { var p = transform.localPosition; p.y = y0 + Mathf.Sin(Time.time * 2f) * bob; transform.localPosition = p; }
        }
    }

    public class Flicker : MonoBehaviour
    {
        public float baseIntensity = 2f; Light l; float seed;
        void Start() { l = GetComponent<Light>(); seed = Random.value * 10; }
        void Update() { if (l.intensity > 0 || baseIntensity > 0) l.intensity = baseIntensity * (Mathf.PerlinNoise(seed, Time.time * 3f) > .12f ? 1f : .25f); }
    }

    // ---------------------------------------------------------------- 可见弹丸：有飞行时间，逐帧射线检测，命中后结算伤害
    public class Bullet : MonoBehaviour
    {
        public Fighter owner; public WeaponSpec w; public Vector3 dir; public float speed, traveled, maxRange = 140f; public Color color;
        public Vector3 origin;
        public float dmgMul = 1f; public int pierces; public static Bullet Current;   // V20：穿墙后伤害打折
        LineRenderer streak; TrailRenderer trail; bool done, flybyPlayed;

        public static void Fire(Fighter owner, Vector3 origin, Vector3 visualStart, Vector3 dir, Color color)
        {
            var go = new GameObject("弹丸");
            go.transform.position = origin;
            var b = go.AddComponent<Bullet>();
            b.owner = owner; b.w = owner.W; b.dir = dir; b.speed = owner.W.bulletSpeed; b.color = color; b.origin = origin;
            b.streak = go.AddComponent<LineRenderer>();
            b.streak.material = Fx.Additive; b.streak.positionCount = 2; b.streak.useWorldSpace = true;
            float width = owner.W.pellets > 1 ? .02f : .045f;
            b.streak.startWidth = width; b.streak.endWidth = width * .3f;
            b.streak.startColor = Color.Lerp(color, Color.white, .5f); b.streak.endColor = new Color(color.r, color.g, color.b, 0);
            b.streak.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            b.visualOffset = visualStart - origin;
            if (ToonAssets.Available)   // V17：子弹就是食物本身，拖一条酱汁色的尾巴
            {
                var th = FoodFx.For(owner.W);
                b.food = FoodFx.AmmoModel(owner.W);
                if (b.food != null) { b.food.transform.position = visualStart; b.food.transform.rotation = Quaternion.LookRotation(dir); b.spinAxis = Random.onUnitSphere; }
                b.streak.material = FoodFx.TrailMaterial;
                float w2 = owner.W.pellets > 1 ? .025f : .05f;
                b.streak.startWidth = w2; b.streak.endWidth = w2 * .2f;
                b.streak.startColor = new Color(th.splat.r, th.splat.g, th.splat.b, .75f); b.streak.endColor = new Color(th.splat.r, th.splat.g, th.splat.b, 0);
            }
        }
        Vector3 visualOffset; GameObject food; Vector3 spinAxis;

        void Update()
        {
            if (done) return;
            // V20：子弹下坠
            if (w != null && w.dropScale > 0) { Vector3 vel = dir * speed + Vector3.down * 9.81f * w.dropScale * Time.deltaTime; speed = vel.magnitude; dir = vel / speed; }
            float step = speed * Time.deltaTime;
            Vector3 pos = transform.position;
            var hits = Physics.RaycastAll(pos, dir, step, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (owner != null && h.collider.transform.IsChildOf(owner.transform)) continue;
                Current = this;
                bool stop = owner != null && owner.Resolve(h, dir, traveled + h.distance, w);
                Current = null;
                if (stop) { End(h.point); return; }
            }
            traveled += step;
            transform.position = pos + dir * step;
            // V13：子弹从玩家耳边擦过时的"嗖"声（只对敌方子弹）
            if (!flybyPlayed && Match.I != null && Match.I.player != null && owner != null && owner.team != Match.I.player.team && Camera.main != null)
            {
                Vector3 ear = Camera.main.transform.position, a = pos, b = pos + dir * step;
                Vector3 ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(ear - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                float miss = Vector3.Distance(ear, a + ab * t);
                if (miss < 2.2f && t > 0f && t < 1f && traveled > 6f) { flybyPlayed = true; Sfx.Play("flyby", a + ab * t, .7f, Random.Range(.9f, 1.15f), 1f, 12f); }
            }
            // visual: offset from the muzzle converges onto the true ray over the first 8 m
            float k = Mathf.Clamp01(traveled / 8f);
            Vector3 head = transform.position + visualOffset * (1 - k);
            streak.SetPosition(0, head); streak.SetPosition(1, head - dir * Mathf.Min(traveled, food != null ? 1.4f : 2.8f));
            if (food != null) { food.transform.position = head; food.transform.Rotate(spinAxis, 900f * Time.deltaTime, Space.World); }
            if (traveled > maxRange) End(transform.position);
        }

        void OnDestroy() { if (food != null) Destroy(food); }

        void End(Vector3 at)
        {
            done = true;
            if (food != null) Destroy(food);
            streak.SetPosition(0, at); streak.SetPosition(1, at - dir * .6f);
            Destroy(gameObject, .03f);
        }
    }
}
