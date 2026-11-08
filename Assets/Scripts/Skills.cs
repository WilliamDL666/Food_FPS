using System.Collections.Generic;
using UnityEngine;

namespace NeonSiege
{
    // GDD 第 6 章：六个主动技能的规则实现。每个技能都有：启动声光提示、次数限制、明确反制。
    public static class Skills
    {
        class Preview { public LineRenderer arc, ring; public GameObject holo; public Vector3 point; public bool legal; }
        static readonly Dictionary<Fighter, Preview> previews = new Dictionary<Fighter, Preview>();
        public static readonly List<GameObject> spawned = new List<GameObject>();
        static readonly Dictionary<Fighter, float> dashEndedAt = new Dictionary<Fighter, float>();

        public static bool NeedsPreview(Role r) { return r == Role.Rivet || r == Role.Raven || r == Role.Ember || r == Role.Phantom; }
        public static bool Previewing(Fighter f) { return previews.ContainsKey(f); }

        public static bool CanUse(Fighter f)
        {
            return f.alive && f.skillCharges > 0 && Match.I.CanAct && !f.Dashing && f.usingInteract == null && !f.bombBusy;
        }

        public static void ClearRound()
        {
            foreach (var o in spawned) if (o != null) Object.Destroy(o);
            spawned.Clear();
            foreach (var p in previews.Values) DestroyPreview(p);
            previews.Clear(); dashEndedAt.Clear();
        }

        // ---------------------------------------------------------------- human input
        public static void HumanInput(Fighter f, bool canAct)
        {
            bool down = Input.GetKeyDown(KeyCode.F), up = Input.GetKeyUp(KeyCode.F);
            if (previews.TryGetValue(f, out var pv))
            {
                if (!CanUse(f) || Input.GetMouseButtonDown(1)) { Cancel(f); return; }
                UpdatePreview(f, pv);
                if (up)
                {
                    bool legal = pv.legal; Vector3 point = pv.point;
                    Cancel(f);
                    if (legal) Cast(f, point);
                    else { Sfx.Ui("error", .4f); Match.I.ui.Toast("此处不能部署（不能封住棱核入口或离地过高）"); }
                }
                return;
            }
            if (!down) return;
            if (!CanUse(f))
            {
                if (f.alive && f.skillCharges <= 0) Match.I.ui.Toast("技能充能中 · " + Mathf.CeilToInt(Mathf.Max(0, f.skillRechargeAt - Time.time)) + " 秒");
                else if (!Match.I.CanAct) Match.I.ui.Toast("准备阶段不能使用技能");
                Sfx.Ui("error", .3f); return;
            }
            if (NeedsPreview(f.role)) { var p = new Preview(); previews[f] = p; UpdatePreview(f, p); }
            else Cast(f, f.transform.position + f.AimDir * 5);
        }

        public static void Cancel(Fighter f)
        {
            if (previews.TryGetValue(f, out var p)) { DestroyPreview(p); previews.Remove(f); }
        }

        static void DestroyPreview(Preview p)
        {
            if (p.arc != null) Object.Destroy(p.arc.gameObject);
            if (p.ring != null) Object.Destroy(p.ring.gameObject);
            if (p.holo != null) Object.Destroy(p.holo);
        }

        static LineRenderer Line(string name, Color c, float width)
        {
            var lr = new GameObject(name).AddComponent<LineRenderer>();
            lr.material = Fx.Additive; lr.startColor = lr.endColor = c; lr.startWidth = lr.endWidth = width;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return lr;
        }

        public static void Circle(LineRenderer lr, Vector3 center, float r)
        {
            lr.loop = true; lr.positionCount = 48;
            for (int i = 0; i < 48; i++)
            {
                float a = i / 48f * Mathf.PI * 2;
                lr.SetPosition(i, center + new Vector3(Mathf.Cos(a) * r, .08f, Mathf.Sin(a) * r));
            }
        }

        static void UpdatePreview(Fighter f, Preview p)
        {
            Color ok = new Color(.3f, 1f, .6f), bad = new Color(1f, .3f, .25f);
            switch (f.role)
            {
                case Role.Rivet:
                    p.legal = CoverPlacement(f, out p.point, out Quaternion rot);
                    if (p.holo == null)
                    {
                        p.holo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        Object.Destroy(p.holo.GetComponent<Collider>());
                        p.holo.transform.localScale = new Vector3(Config.CoverWidth, Config.CoverHeight, .35f);
                        p.holo.GetComponent<Renderer>().material = new Material(Resources.Load<Material>("Materials/Holo") ?? Fx.Additive);
                    }
                    p.holo.transform.position = p.point + Vector3.up * .8f; p.holo.transform.rotation = rot;
                    p.holo.GetComponent<Renderer>().material.SetColor("_Color", p.legal ? ok : bad);
                    break;
                case Role.Raven:
                case Role.Ember:
                    if (p.arc == null) { p.arc = Line("投掷预览", ok, .03f); p.ring = Line("落点预览", ok, .05f); }
                    bool arrow = f.role == Role.Raven;
                    Vector3 v = arrow ? ArrowVelocity(f) : ThrowVelocity(f, 15f);
                    var pts = new List<Vector3>();
                    p.point = Simulate(f.Eye + f.AimDir * .5f, v, pts, arrow ? Config.ArrowGravity : 1f);
                    p.arc.positionCount = pts.Count; p.arc.SetPositions(pts.ToArray());
                    Circle(p.ring, p.point, f.role == Role.Raven ? Config.DroneRadius : Config.BlastRadius);
                    p.legal = true;
                    break;
                case Role.Phantom:
                    if (p.ring == null) p.ring = Line("噪幕预览", new Color(.9f, .4f, 1f), .06f);
                    p.point = GroundPoint(f, 22f);
                    Circle(p.ring, p.point, Config.NoiseRadius);
                    p.legal = true;
                    break;
            }
        }

        // ---------------------------------------------------------------- casting
        public static bool Cast(Fighter f, Vector3 target)
        {
            if (!CanUse(f)) return false;
            switch (f.role)
            {
                case Role.Falcon: StartDash(f, target); break;
                case Role.Rivet:
                    if (!f.human) { if (!CoverPlacementAt(f, target, out target, out _)) return false; }
                    SpawnCover(f, target, Quaternion.Euler(0, f.yaw, 0));
                    break;
                case Role.Ironwall: SpawnShield(f); break;
                case Role.Raven: ShootArrow(f, f.human ? ArrowVelocity(f) : ArrowVelocityTo(f.Eye, target)); break;
                case Role.Ember: Throw(f, f.human ? ThrowVelocity(f, 15f) : VelocityTo(f.Eye, target, 32f), false); break;
                case Role.Phantom: SpawnNoise(f, f.human ? target : GroundAt(target)); break;
            }
            f.skillCharges--; f.stats.skillUses++;
            if (f.skillRechargeAt <= Time.time) f.skillRechargeAt = Time.time + f.Hero.skill.cooldown;
            Match.I.SkillCue(f);
            return true;
        }

        // 猎隼 · 推进喷射：0.2 s 前摇（喷焰+音效）→ 0.18 s 内位移 7 m，CharacterController 扫掠防穿模
        static void StartDash(Fighter f, Vector3 target)
        {
            Vector3 dir;
            if (f.human)
            {
                float x = Input.GetAxisRaw("Horizontal"), z = Input.GetAxisRaw("Vertical");
                dir = Quaternion.Euler(0, f.yaw, 0) * new Vector3(x, 0, z);
                if (dir.sqrMagnitude < .01f) dir = Quaternion.Euler(0, f.yaw, 0) * Vector3.forward;
            }
            else { dir = target - f.transform.position; dir.y = 0; }
            dir.Normalize();
            f.dashUntil = Time.time + Config.DashWindup + Config.DashTime;
            var runner = f.gameObject.AddComponent<DashRunner>();
            runner.owner = f; runner.dir = dir;
            Sfx.Play("thrusterFire", f.transform.position, 1f, 1.1f, 1f, 40f);
            Fx.Burst(f.transform.position + Vector3.up * .9f - dir * .4f, -dir, new Color(.4f, .7f, 1f), 30, 4, .12f, .4f, 0);
        }

        public static void DashFinished(Fighter f)
        {
            dashEndedAt[f] = Time.time; f.aimRecoveryUntil = Time.time + Config.DashAimRecovery;
            f.guardUntil = Time.time + Config.DashGuardTime; f.ammo = f.W.magazine;
            Fx.Shockwave(f.transform.position, 4f, new Color(.4f, .8f, 1f), .4f);
            Fx.Sparks(f.transform.position + Vector3.up * .1f, Vector3.up, new Color(.5f, .85f, 1f), 30);
            Sfx.Play("forceField", f.transform.position, .7f, 1.6f, 1f, 30f);
            Fx.Burst(f.Chest, Vector3.up, new Color(.4f, .8f, 1f), 25, 3, .08f, .5f, 0);
        }

        // 铆钉 · 战术掩体：贴地、合法区域、不能封入口
        public static bool CoverPlacement(Fighter f, out Vector3 point, out Quaternion rot)
        {
            rot = Quaternion.Euler(0, f.yaw, 0);
            Vector3 aimPoint = f.Eye + f.AimDir * 3.4f;
            if (Physics.Raycast(f.Eye, f.AimDir, out RaycastHit hit, 4.5f, ~0, QueryTriggerInteraction.Ignore) && hit.normal.y > .6f) aimPoint = hit.point;
            Vector3 flat = f.transform.position + rot * Vector3.forward * Mathf.Clamp(Vector3.Distance(new Vector3(aimPoint.x, 0, aimPoint.z), new Vector3(f.transform.position.x, 0, f.transform.position.z)), 2.2f, 4.5f);
            return CoverPlacementAt(f, flat, out point, out _);
        }

        public static bool CoverPlacementAt(Fighter f, Vector3 desired, out Vector3 point, out Quaternion rot)
        {
            rot = Quaternion.Euler(0, f.yaw, 0);
            point = desired;
            if (!Physics.Raycast(desired + Vector3.up * 2f, Vector3.down, out RaycastHit ground, 4f, ~0, QueryTriggerInteraction.Ignore)) return false;
            point = ground.point;
            if (ground.normal.y < .85f || Mathf.Abs(point.y - f.transform.position.y) > .7f) return false;
            if (!Arena.I.IsBuildLegal(point)) return false;
            var hits = Physics.OverlapBox(point + Vector3.up * .95f, new Vector3(Config.CoverWidth / 2 - .05f, .75f, .2f), rot, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits) if (h.GetComponentInParent<Fighter>() != null || !h.isTrigger) return false;
            return true;
        }

        static void SpawnCover(Fighter f, Vector3 point, Quaternion rot)
        {
            var go = new GameObject("战术掩体 " + Config.TeamName(f.team));
            go.transform.position = point; go.transform.rotation = rot;
            go.AddComponent<DeployedCover>().Init(f);
            spawned.Add(go);
            Sfx.Play("forceField", point, .9f, 1.1f, 1f, 40f);
        }

        // 铁幕 · 防护阵列
        static void SpawnShield(Fighter f)
        {
            var go = new GameObject("防护阵列 " + Config.TeamName(f.team));
            Quaternion rot = Quaternion.Euler(0, f.yaw, 0);
            go.transform.position = f.transform.position + rot * Vector3.forward * 1.7f;
            go.transform.rotation = rot;
            go.AddComponent<ShieldWall>().Init(f);
            f.shieldActiveUntil = Time.time + Config.ShieldLife;
            spawned.Add(go);
            Sfx.Play("forceField", go.transform.position, 1f, .7f, 1f, 45f);
        }

        // 夜鸦 / 焚芯 投掷物
        public static Vector3 ThrowVelocity(Fighter f, float speed)
        {
            Vector3 d = f.AimDir; d = (d + Vector3.up * .18f).normalized;
            return d * speed;
        }

        // 夜鸦侦测箭：高速、轻微下坠的直线弹道
        public static Vector3 ArrowVelocity(Fighter f) { return (f.AimDir + Vector3.up * .04f).normalized * Config.ArrowSpeed; }
        public static Vector3 ArrowVelocityTo(Vector3 from, Vector3 to)
        {
            to += Vector3.up * .3f;
            float t = Mathf.Max(.05f, Vector3.Distance(from, to) / Config.ArrowSpeed);
            return (to - from) / t - .5f * Physics.gravity * Config.ArrowGravity * t;
        }

        public static void ShootArrow(Fighter f, Vector3 velocity)
        {
            var go = new GameObject("侦测箭");
            go.transform.position = f.Eye + f.AimDir * .6f;
            go.transform.rotation = Quaternion.LookRotation(velocity);
            var dark = Resources.Load<Material>("Materials/GunMetal") ?? Arena.Glow(new Color(.2f, .2f, .22f), 0f);
            var glow = Arena.Glow(new Color(.55f, .45f, 1f), 3f);
            Part(go.transform, PrimitiveType.Cylinder, new Vector3(0, 0, -.25f), new Vector3(90, 0, 0), new Vector3(.025f, .4f, .025f), dark);      // shaft (0.8 m)
            Part(go.transform, PrimitiveType.Cube, new Vector3(0, 0, .2f), new Vector3(0, 0, 45), new Vector3(.05f, .05f, .14f), dark);          // head
            Part(go.transform, PrimitiveType.Cylinder, new Vector3(0, 0, .08f), new Vector3(90, 0, 0), new Vector3(.06f, .05f, .06f), glow);    // sensor ring
            for (int i = 0; i < 3; i++)
            {
                var fl = Part(go.transform, PrimitiveType.Cube, Vector3.zero, Vector3.zero, new Vector3(.004f, .06f, .14f), glow);
                fl.localRotation = Quaternion.Euler(0, 0, i * 120); fl.localPosition = fl.localRotation * new Vector3(0, .035f, -.58f);
            }
            var proj = go.AddComponent<Arrow>(); proj.owner = f; proj.velocity = velocity;
            spawned.Add(go);
            Sfx.Play("impactPunch_medium", go.transform.position, .7f, 1.8f, f.human ? .3f : 1f, 25f);
            Sfx.Play("thrusterFire", go.transform.position, .25f, 2.4f, f.human ? .3f : 1f, 20f);
        }

        static Transform Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 euler, Vector3 scale, Material m)
        {
            var g = GameObject.CreatePrimitive(type); Object.Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localEulerAngles = euler; g.transform.localScale = scale;
            var r = g.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g.transform;
        }

        public static Vector3 VelocityTo(Vector3 from, Vector3 to, float angleDeg)
        {
            Vector3 flat = to - from; float h = flat.y; flat.y = 0;
            float dist = flat.magnitude, g = 9.81f, a = angleDeg * Mathf.Deg2Rad;
            float denom = 2 * Mathf.Cos(a) * Mathf.Cos(a) * (dist * Mathf.Tan(a) - h);
            float v = denom > .01f ? Mathf.Sqrt(g * dist * dist / denom) : 12f;
            v = Mathf.Clamp(v, 4f, 22f);
            return (flat.normalized * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a)) * v;
        }

        static Vector3 Simulate(Vector3 pos, Vector3 v, List<Vector3> pts, float gravityScale = 1f)
        {
            pts.Add(pos);
            for (int i = 0; i < 90; i++)
            {
                Vector3 next = pos + v * .04f; v += Physics.gravity * gravityScale * .04f;
                if (Physics.Linecast(pos, next, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)) { pts.Add(hit.point); return hit.point; }
                pts.Add(next); pos = next;
            }
            return pos;
        }

        static void Throw(Fighter f, Vector3 velocity, bool drone)
        {
            var go = GameObject.CreatePrimitive(drone ? PrimitiveType.Cylinder : PrimitiveType.Sphere);
            go.name = drone ? "侦测飞盘" : "定向爆破装置";
            go.transform.localScale = drone ? new Vector3(.38f, .04f, .38f) : Vector3.one * .26f;
            go.transform.position = f.Eye + f.AimDir * .6f;
            Object.Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = Arena.Glow(drone ? new Color(.55f, .45f, 1f) : new Color(1f, .3f, .15f), 3f);
            var proj = go.AddComponent<Thrown>();
            proj.owner = f; proj.velocity = velocity; proj.drone = drone;
            spawned.Add(go);
            Sfx.Play("click", go.transform.position, .8f, .8f, 1f, 25f);
        }

        // 焚芯爆破后的燃烧区：持续伤害敌人，迫使其离开点位
    public class FireZone : MonoBehaviour
    {
        public Fighter owner; float born, tick; Light l;
        void Start()
        {
            born = Time.time;
            var go = new GameObject("火焰"); go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.startLifetime = new ParticleSystem.MinMaxCurve(.5f, 1.1f); main.startSpeed = new ParticleSystem.MinMaxCurve(.8f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(.3f, .9f); main.startColor = new Color(1f, .45f, .1f); main.maxParticles = 300; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 110;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = Config.FireRadius; sh.rotation = new Vector3(-90, 0, 0);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(new Color(1f, .9f, .5f), 0), new GradientColorKey(new Color(1f, .2f, .05f), 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) }); col.color = g;
            go.GetComponent<ParticleSystemRenderer>().material = Fx.Additive;
            l = new GameObject("火光").AddComponent<Light>(); l.transform.SetParent(transform, false); l.transform.localPosition = Vector3.up;
            l.color = new Color(1f, .5f, .15f); l.range = 9; l.intensity = 3;
            var ring = new GameObject("燃烧边界").AddComponent<LineRenderer>(); ring.transform.SetParent(transform);
            ring.material = Fx.Additive; ring.startWidth = ring.endWidth = .08f; ring.startColor = ring.endColor = new Color(1f, .4f, .1f);
            Skills.Circle(ring, transform.position, Config.FireRadius);
            Sfx.Play("thrusterFire", transform.position, .8f, .6f, 1f, 35f);
        }
        void Update()
        {
            float age = Time.time - born;
            l.intensity = 2.5f + Mathf.PerlinNoise(Time.time * 8, 0) * 1.5f;
            if (age > Config.FireDuration) { Destroy(gameObject); return; }
            if (Time.time < tick) return;
            tick = Time.time + .25f;
            foreach (var f in Match.I.fighters)
            {
                if (!f.alive || owner == null || f.team == owner.team) continue;
                Vector3 d = f.transform.position - transform.position; d.y = 0;
                if (d.magnitude <= Config.FireRadius) { f.ReceiveDamage(Config.FireDps * .25f, owner, transform.position, false, true); f.revealedUntil = Mathf.Max(f.revealedUntil, Time.time + .3f); }
            }
        }
    }

    public class Glyph : MonoBehaviour
    {
        static readonly string chars = "01霓城封锁ERR#@%&数据干扰▓░";
        TextMesh tm; float next; Vector3 basePos;
        void Start() { tm = GetComponent<TextMesh>(); basePos = transform.localPosition; }
        void Update()
        {
            var cam = Camera.main; if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
            transform.localPosition = basePos + Vector3.up * Mathf.Sin(Time.time * 2 + basePos.x) * .2f;
            if (Time.time < next) return;
            next = Time.time + Random.Range(.05f, .25f);
            int n = Random.Range(2, 6); var s = ""; for (int i = 0; i < n; i++) s += chars[Random.Range(0, chars.Length)];
            tm.text = s;
        }
    }

    // 幻频 · 信号噪幕
        static void SpawnNoise(Fighter f, Vector3 point)
        {
            var go = new GameObject("信号噪幕 " + Config.TeamName(f.team));
            go.transform.position = point;
            go.AddComponent<NoiseField>().Init(f);
            spawned.Add(go);
            Sfx.Play("glitch", point, 1f, .8f, 1f, 45f);
        }

        public static Vector3 GroundPoint(Fighter f, float maxDist)
        {
            Vector3 p = f.Eye + f.AimDir * maxDist;
            if (Physics.Raycast(f.Eye, f.AimDir, out RaycastHit hit, maxDist, ~0, QueryTriggerInteraction.Ignore)) p = hit.point - f.AimDir * .3f;
            return GroundAt(p);
        }

        public static Vector3 GroundAt(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * .5f, Vector3.down, out RaycastHit g, 30f, ~0, QueryTriggerInteraction.Ignore)) return g.point;
            p.y = 0; return p;
        }

        // ---------------------------------------------------------------- effectiveness tracking (赛后反馈 / 复盘指标)
        public static void NotifyDamage(Fighter source, Fighter victim, float dealt)
        {
            if (source.role == Role.Falcon && dashEndedAt.TryGetValue(source, out float t) && Time.time - t < 2.5f)
            {
                source.stats.skillEffective++; dashEndedAt.Remove(source);
            }
        }

        public static void NotifyKill(Fighter killer, Fighter victim) { }

        public static bool LineClear(Vector3 a, Vector3 b, Fighter target)
        {
            if (SteamCloud.Blocks(a, b)) return false;   // V17：火锅热气挡视线
            var hits = Physics.RaycastAll(a, (b - a).normalized, Vector3.Distance(a, b), ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h.collider.GetComponentInParent<Fighter>() != null) continue;
                if (h.collider.GetComponentInParent<ShieldWall>() != null) continue;
                if (h.collider.GetComponentInParent<Drone>() != null) continue;
                return false;
            }
            return true;
        }
    }

    public class DashRunner : MonoBehaviour
    {
        public Fighter owner; public Vector3 dir; float t, nextGhost; Light glow;
        void Start()
        {
            glow = new GameObject("喷射光").AddComponent<Light>(); glow.transform.SetParent(owner.transform, false);
            glow.transform.localPosition = new Vector3(0, 1f, -.5f); glow.color = new Color(.4f, .7f, 1f); glow.range = 6; glow.intensity = 4;
            Fx.Shockwave(owner.transform.position, 2.5f, new Color(.4f, .7f, 1f), .3f);
        }
        void OnDestroy() { if (glow != null) Destroy(glow.gameObject); }
        void Update()
        {
            t += Time.deltaTime;
            if (owner == null || !owner.alive) { Destroy(this); return; }
            Fx.Burst(owner.transform.position + Vector3.up * 1.1f - owner.transform.forward * .35f, -dir + Vector3.down * .3f, new Color(.4f, .75f, 1f), 4, 5, .12f, .25f, 0);
            if (t < Config.DashWindup) return;
            if (Time.time >= nextGhost) { nextGhost = Time.time + .035f; Ghost.Spawn(owner, new Color(.3f, .7f, 1f)); }
            float k = Mathf.Min(Time.deltaTime, Config.DashWindup + Config.DashTime - (t - Time.deltaTime));
            if (k > 0) owner.ApplyDash(dir * (Config.DashDistance / Config.DashTime) * k);
            if (t >= Config.DashWindup + Config.DashTime) { Skills.DashFinished(owner); Destroy(this); }
        }
    }

    // Afterimage: bake the avatar's skinned meshes into a fading hologram.
    public class Ghost : MonoBehaviour
    {
        float t; Material m; Color c;
        public static void Spawn(Fighter f, Color c)
        {
            if (f.avatar == null) return;
            var go = new GameObject("残影");
            var g = go.AddComponent<Ghost>(); g.c = c;
            g.m = new Material(Resources.Load<Material>("Materials/Holo") ?? Fx.Additive); g.m.SetColor("_Color", c);
            foreach (var smr in f.avatar.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = new Mesh(); smr.BakeMesh(mesh);
                var part = new GameObject("part"); part.transform.SetParent(go.transform, false);
                part.transform.position = smr.transform.position; part.transform.rotation = smr.transform.rotation;
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = part.AddComponent<MeshRenderer>(); var mats = new Material[mesh.subMeshCount]; for (int i = 0; i < mats.Length; i++) mats[i] = g.m; r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
        void Update()
        {
            t += Time.deltaTime;
            m.SetColor("_Color", c * Mathf.Max(0, 1 - t / .35f));
            if (t > .35f) { foreach (var mf in GetComponentsInChildren<MeshFilter>()) Destroy(mf.sharedMesh); Destroy(gameObject); }
        }
    }

    public class DeployedCover : MonoBehaviour, IDamageable
    {
        public Fighter owner; public float hp = Config.CoverHp, absorbed;
        float born; Transform visual; BoxCollider col; UnityEngine.AI.NavMeshObstacle obstacle; bool counted;
        public void Init(Fighter f)
        {
            owner = f; born = Time.time;
            col = gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(Config.CoverWidth, Config.CoverHeight, .35f); col.center = new Vector3(0, Config.CoverHeight / 2, 0);
            visual = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            Destroy(visual.GetComponent<Collider>());
            visual.SetParent(transform, false); visual.localScale = new Vector3(Config.CoverWidth, .05f, .35f);
            visual.GetComponent<Renderer>().sharedMaterial = Resources.Load<Material>("Materials/MetalPlate");
            var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(strip.GetComponent<Collider>());
            strip.transform.SetParent(visual, false); strip.transform.localPosition = new Vector3(0, .35f, 0); strip.transform.localScale = new Vector3(1.01f, .06f, 1.05f);
            strip.GetComponent<Renderer>().sharedMaterial = Arena.Glow(Config.TeamColor(f.team), 3f);
            obstacle = gameObject.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box; obstacle.size = col.size; obstacle.center = col.center; obstacle.carving = true;
        }
        Transform holoFrame; LineRenderer beam;
        void Update()
        {
            float age = Time.time - born;
            float k = Mathf.Clamp01(age / Config.CoverDeploy);
            if (age < Config.CoverDeploy)
            {
                if (holoFrame == null)
                {
                    holoFrame = GameObject.CreatePrimitive(PrimitiveType.Cube).transform; Destroy(holoFrame.GetComponent<Collider>());
                    holoFrame.SetParent(transform, false); holoFrame.localScale = new Vector3(Config.CoverWidth + .1f, Config.CoverHeight + .1f, .45f); holoFrame.localPosition = new Vector3(0, Config.CoverHeight / 2, 0);
                    var hm = new Material(Resources.Load<Material>("Materials/Hex") ?? Fx.Additive); hm.SetColor("_Color", Config.TeamColor(owner.team) * 1.6f);
                    holoFrame.GetComponent<Renderer>().sharedMaterial = hm;
                }
                if (beam == null)
                {
                    beam = new GameObject("施工光束").AddComponent<LineRenderer>(); beam.transform.SetParent(transform);
                    beam.material = Fx.Additive; beam.startWidth = .06f; beam.endWidth = .02f; beam.positionCount = 2;
                    beam.startColor = new Color(1f, .85f, .3f); beam.endColor = Config.TeamColor(owner.team);
                }
                Vector3 from = owner.avatar != null ? owner.avatar.MuzzlePosition : owner.Eye;
                beam.SetPosition(0, from); beam.SetPosition(1, transform.position + Vector3.up * Config.CoverHeight * k + transform.right * Mathf.Sin(Time.time * 20) * 1.2f);
                if (Random.value < .7f) Fx.Sparks(transform.position + transform.right * Random.Range(-1.4f, 1.4f) + Vector3.up * visual.localScale.y, Vector3.up, new Color(1f, .8f, .4f), 3);
            }
            else if (holoFrame != null) { if (beam != null) Destroy(beam.gameObject); Destroy(holoFrame.gameObject); holoFrame = null; Fx.Burst(transform.position + Vector3.up * .8f, Vector3.up, Config.TeamColor(owner.team), 25, 3, .08f, .5f, 0); }
            visual.localScale = new Vector3(Config.CoverWidth, Mathf.Max(.05f, Config.CoverHeight * k), .35f);
            visual.localPosition = new Vector3(0, visual.localScale.y / 2, 0);
            col.size = new Vector3(Config.CoverWidth, Mathf.Max(.1f, Config.CoverHeight * k), .35f); col.center = new Vector3(0, col.size.y / 2, 0);
            if (age > Config.CoverDeploy + Config.CoverLife) Destroy(gameObject);
        }
        public bool TakeDamage(float amount, Fighter source, bool explosive)
        {
            hp -= amount; if (source == null || source.team != owner.team) absorbed += amount;
            if (!counted && absorbed >= 60) { counted = true; owner.stats.skillEffective++; }
            if (hp <= 0)
            {
                Fx.Burst(transform.position + Vector3.up * .8f, Vector3.up, new Color(.6f, .8f, 1f), 30, 5, .12f, .7f, 1);
                Sfx.Play("impactMetal", transform.position, .9f, .8f);
                if (source != null && source.team != owner.team) source.stats.coverDestroyed++;
                Destroy(gameObject);
            }
            return true;
        }
    }

    public class ShieldWall : MonoBehaviour, IDamageable
    {
        public Fighter owner; float hp = Config.ShieldHp, absorbed, born; bool counted; Material mat;
        public void Init(Fighter f)
        {
            owner = f; born = Time.time;
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "阵列面板"; panel.transform.SetParent(transform, false);
            panel.transform.localPosition = new Vector3(0, Config.ShieldHeight / 2, 0); panel.transform.localScale = new Vector3(Config.ShieldWidth, Config.ShieldHeight, .12f);
            mat = new Material(Resources.Load<Material>("Materials/Hex") ?? Resources.Load<Material>("Materials/Holo") ?? Fx.Additive);
            mat.SetColor("_Color", Config.TeamColor(f.team) * 1.3f);
            panel.GetComponent<Renderer>().sharedMaterial = mat;
            panel.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var col = panel.GetComponent<Collider>();
            foreach (var other in Match.I.fighters) if (other.cc != null) Physics.IgnoreCollision(col, other.cc);
            var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(frame.GetComponent<Collider>());
            frame.transform.SetParent(transform, false); frame.transform.localPosition = new Vector3(0, .05f, 0); frame.transform.localScale = new Vector3(Config.ShieldWidth + .1f, .1f, .3f);
            frame.GetComponent<Renderer>().sharedMaterial = Arena.Glow(Config.TeamColor(f.team), 3f);
            var l = new GameObject("阵列光").AddComponent<Light>(); l.transform.SetParent(transform, false); l.transform.localPosition = new Vector3(0, 1.2f, -.4f);
            l.color = Config.TeamColor(f.team); l.range = 7; l.intensity = 2.5f;
            Fx.Burst(transform.position + Vector3.up * 1.2f, transform.forward, Config.TeamColor(f.team), 40, 4, .1f, .5f, 0);
            Fx.Shockwave(transform.position, 3f, Config.TeamColor(f.team), .35f);
        }
        void Update()
        {
            float age = Time.time - born;
            mat.SetColor("_Color", Config.TeamColor(owner.team) * (1.1f + .3f * Mathf.Sin(Time.time * 10f)) * Mathf.Clamp01(hp / Config.ShieldHp * .7f + .3f));
            if (age > Config.ShieldLife || !owner.alive) { owner.shieldActiveUntil = 0; Destroy(gameObject); }
        }
        public bool TakeDamage(float amount, Fighter source, bool explosive)
        {
            if (source != null && source.team == owner.team) return true;   // blocks friendly fire too, without damage
            hp -= amount; absorbed += amount;
            mat.SetColor("_Color", Color.white * 3f);
            if (source != null) { mat.SetVector("_HitPos", transform.position + Vector3.up * 1.4f + (source.transform.position - transform.position).normalized * .1f); mat.SetFloat("_HitTime", Time.timeSinceLevelLoad); }
            Sfx.Play("impactMetal_light", transform.position + Vector3.up, .5f, 1.8f, 1f, 30f);
            if (!counted && absorbed >= 60) { counted = true; owner.stats.skillEffective++; }
            if (hp <= 0)
            {
                Fx.Burst(transform.position + Vector3.up * 1.2f, transform.forward, Config.TeamColor(owner.team), 40, 5, .1f, .6f, 0);
                Sfx.Play("forceField", transform.position, 1f, .5f);
                owner.shieldActiveUntil = 0; Destroy(gameObject);
            }
            return true;
        }
    }

    public class Thrown : MonoBehaviour
    {
        public Fighter owner; public Vector3 velocity; public bool drone; float life;
        void Start()
        {
            var tr = gameObject.AddComponent<TrailRenderer>(); tr.time = .35f; tr.startWidth = .12f; tr.endWidth = 0; tr.material = Fx.Additive;
            Color c = drone ? new Color(.6f, .45f, 1f) : new Color(1f, .35f, .1f);
            tr.startColor = c; tr.endColor = new Color(c.r, c.g, c.b, 0); tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var l = new GameObject("投掷光").AddComponent<Light>(); l.transform.SetParent(transform, false); l.color = c; l.range = 4; l.intensity = 2;
        }
        void Update()
        {
            life += Time.deltaTime;
            Vector3 next = transform.position + velocity * Time.deltaTime;
            velocity += Physics.gravity * Time.deltaTime;
            if (Physics.Linecast(transform.position, next, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(owner.transform) || life > 4f)
            {
                Vector3 p = hit.collider != null ? hit.point + hit.normal * .05f : transform.position;
                if (drone) { var d = gameObject.AddComponent<Drone>(); d.Init(owner, p); }
                else { var c = gameObject.AddComponent<BlastCharge>(); c.Init(owner, p); }
                Destroy(this);
                return;
            }
            transform.position = next;
            transform.Rotate(0, 720 * Time.deltaTime, 0);
        }
    }

    public class Arrow : MonoBehaviour
    {
        public Fighter owner; public Vector3 velocity; float life;
        void Start()
        {
            var tr = gameObject.AddComponent<TrailRenderer>(); tr.time = .25f; tr.startWidth = .05f; tr.endWidth = 0; tr.material = Fx.Additive;
            Color c = new Color(.6f, .45f, 1f); tr.startColor = c; tr.endColor = new Color(c.r, c.g, c.b, 0); tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var l = new GameObject("箭光").AddComponent<Light>(); l.transform.SetParent(transform, false); l.color = c; l.range = 3; l.intensity = 1.5f;
        }
        void Update()
        {
            life += Time.deltaTime;
            Vector3 step = velocity * Time.deltaTime;
            velocity += Physics.gravity * Config.ArrowGravity * Time.deltaTime;
            bool hitSomething = false; RaycastHit hit = default;
            foreach (var h in Physics.RaycastAll(transform.position, step, step.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (owner != null && h.collider.transform.IsChildOf(owner.transform)) continue;
                if (h.collider.GetComponentInParent<Fighter>() != null) continue; // 箭穿过人体，只钉在环境上
                if (!hitSomething || h.distance < hit.distance) { hit = h; hitSomething = true; }
            }
            if (hitSomething || life > 3f)
            {
                Vector3 n = hitSomething ? hit.normal : Vector3.up;
                Vector3 p = hitSomething ? hit.point - transform.forward * .12f + n * .02f : transform.position;   // tip buried in the surface
                transform.position = p;
                Fx.Sparks(p, n, new Color(.7f, .6f, 1f), 10);
                Sfx.Play("impactMetal", p, .7f, 1.6f, 1f, 30f);
                var d = gameObject.AddComponent<Drone>(); d.Init(owner, p, n);
                Destroy(this);
                return;
            }
            transform.position += step;
            if (velocity.sqrMagnitude > .01f) transform.rotation = Quaternion.LookRotation(velocity);
        }
    }

    // 夜鸦 · 侦测箭（命中后成为扫描信标）：落地 → 1.3 s → 单次扫描（需视线）→ 寿命 4 s；可被一枪击毁
    public class Drone : MonoBehaviour, IDamageable
    {
        public Fighter owner; float landed; public float Landed { get { return landed; } } bool scanned, scanned2; LineRenderer pulse; Light lamp; float nextBeep; Vector3 normal = Vector3.up;
        public void Init(Fighter f, Vector3 p) { Init(f, p, Vector3.up); }
        public void Init(Fighter f, Vector3 p, Vector3 n)
        {
            owner = f; landed = Time.time; transform.position = p; normal = n;
            var col = gameObject.AddComponent<SphereCollider>(); col.radius = transform.localScale.x < .9f ? .8f : .35f; // legacy scaled test primitive vs arrow
            lamp = new GameObject("信标灯").AddComponent<Light>(); lamp.transform.SetParent(transform, false);
            lamp.transform.position = p + n * .3f; lamp.type = LightType.Point; lamp.color = new Color(.6f, .45f, 1f); lamp.range = 5; lamp.intensity = 2;
            Sfx.Play("computerNoise", p, .8f, 1.3f, 1f, 35f);
        }
        void Update()
        {
            float age = Time.time - landed;
            if (Time.time >= nextBeep) { nextBeep = Time.time + .45f; Sfx.Play("tick", transform.position, .8f, 1.6f, 1f, 30f); lamp.intensity = 4; }
            lamp.intensity = Mathf.MoveTowards(lamp.intensity, 1, Time.deltaTime * 10);
            if (!scanned && age >= Config.DroneScanDelay) Scan();
            if (scanned && !scanned2 && age >= Config.DroneScanDelay + Config.DroneSecondPulse) { scanned2 = true; scanned = false; Scan(); scanned = true; }
            if (pulse != null)
            {
                float k = Mathf.Clamp01((age - Config.DroneScanDelay) / .5f);
                Skills.Circle(pulse, transform.position, Config.DroneRadius * k);
                var c = pulse.startColor; c.a = 1 - k * .7f; pulse.startColor = pulse.endColor = c;
            }
            if (age >= Config.DroneLife) Destroy(gameObject);
        }
        void Scan()
        {
            scanned = true; int found = 0;
            Sfx.Play("maximize", transform.position, 1f, .8f, 1f, 45f);
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(sphere.GetComponent<Collider>());
            sphere.name = "扫描球"; sphere.transform.position = transform.position;
            var sm = new Material(Resources.Load<Material>("Materials/Holo") ?? Fx.Additive); sm.SetColor("_Color", new Color(.6f, .45f, 1f) * 1.2f);
            sphere.GetComponent<Renderer>().sharedMaterial = sm; sphere.AddComponent<ScanSphere>().radius = Config.DroneRadius;
            Fx.Flash(transform.position + Vector3.up, new Color(.6f, .45f, 1f), 6f, 14f, .5f);
            pulse = new GameObject("扫描脉冲").AddComponent<LineRenderer>();
            pulse.transform.SetParent(transform); pulse.material = Fx.Additive; pulse.startWidth = pulse.endWidth = .08f;
            pulse.startColor = pulse.endColor = new Color(.7f, .55f, 1f);
            foreach (var e in Match.I.fighters)
            {
                if (!e.alive || e.team == owner.team) continue;
                if (Vector3.Distance(e.Chest, transform.position) > Config.DroneRadius) continue;
                if (!Skills.LineClear(transform.position + normal * .3f, e.Chest, e)) continue;
                if (Time.time < e.hiddenFromScanUntil) { NoiseField.CreditBlock(e); continue; }
                e.revealedUntil = Time.time + Config.DroneRevealTime; found++;
            }
            if (found > 0) { owner.stats.skillEffective++; Match.I.Feed(Config.TeamName(owner.team) + " 夜鸦 扫描到 " + found + " 名敌人"); }
        }
        public bool TakeDamage(float amount, Fighter source, bool explosive)
        {
            if (source != null && source.team == owner.team) return false;
            Fx.Burst(transform.position, Vector3.up, new Color(.6f, .5f, 1f), 20, 4, .06f, .5f);
            Sfx.Play("impactMetal", transform.position, .8f, 1.5f);
            if (Match.I != null) Match.I.Feed("侦测箭信标被击毁");
            Destroy(gameObject);
            return true;
        }
    }

    public class ScanSphere : MonoBehaviour
    {
        public float radius = 9; float t; Material m;
        void Start() { m = GetComponent<Renderer>().sharedMaterial; }
        void Update()
        {
            t += Time.deltaTime; float k = t / .6f;
            if (k >= 1) { Destroy(gameObject); return; }
            transform.localScale = Vector3.one * radius * 2 * Mathf.Sqrt(k);
            m.SetColor("_Color", new Color(.6f, .45f, 1f) * 1.4f * (1 - k));
        }
    }

    // 焚芯 · 定向爆破：0.9 s 声光预警 → 角色伤害上限 35，对轻型掩体 ×2
    public class BlastCharge : MonoBehaviour
    {
        public Fighter owner; float armed; LineRenderer ring; Light lamp; float nextBeep; GameObject fill;
        void OnDestroy() { if (fill != null) Destroy(fill); }
        public void Init(Fighter f, Vector3 p)
        {
            owner = f; armed = Time.time; transform.position = p;
            ring = new GameObject("爆破预警圈").AddComponent<LineRenderer>(); ring.transform.SetParent(transform);
            ring.material = Fx.Additive; ring.startWidth = ring.endWidth = .1f; ring.startColor = ring.endColor = new Color(1f, .2f, .1f);
            Skills.Circle(ring, Skills.GroundAt(p), Config.BlastRadius);
            lamp = new GameObject("预警灯").AddComponent<Light>(); lamp.transform.SetParent(transform, false);
            lamp.type = LightType.Point; lamp.color = new Color(1f, .2f, .1f); lamp.range = 8; lamp.intensity = 3;
            Match.I.Feed("⚠ " + Config.TeamName(f.team) + " 焚芯 定向爆破预警！");
            fill = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(fill.GetComponent<Collider>());
            fill.transform.position = Skills.GroundAt(p) + Vector3.up * .04f; fill.transform.localScale = new Vector3(.1f, .005f, .1f);
            var fm = new Material(Resources.Load<Material>("Materials/Holo") ?? Fx.Additive); fm.SetColor("_Color", new Color(1f, .2f, .1f) * .8f); fm.SetFloat("_Lines", 0);
            fill.GetComponent<Renderer>().sharedMaterial = fm;
        }
        void Update()
        {
            float age = Time.time - armed;
            if (Time.time >= nextBeep) { nextBeep = Time.time + .15f; Sfx.Play("error", transform.position, 1f, 1.6f, 1f, 35f); }
            bool on = Mathf.Repeat(age * 7f, 1f) < .5f;
            lamp.intensity = on ? 5 : .5f; ring.enabled = on || age > .6f;
            float fk = Mathf.Clamp01(age / Config.BlastWarning) * Config.BlastRadius * 2;
            fill.transform.localScale = new Vector3(fk, .005f, fk);
            if (age >= Config.BlastWarning) Explode();
        }
        void Explode()
        {
            Vector3 p = transform.position;
            Fx.Explosion(p, Config.BlastRadius);
            bool effective = false;
            foreach (var e in Match.I.fighters)
            {
                if (!e.alive || e.team == owner.team) continue;
                float d = Vector3.Distance(e.Chest, p);
                if (d > Config.BlastRadius || !Skills.LineClear(p + Vector3.up * .2f, e.Chest, e)) continue;
                float dmg = Config.BlastMaxDamage * Mathf.Lerp(1f, .45f, d / Config.BlastRadius);
                e.ReceiveDamage(dmg, owner, p, false, true); effective = true;
            }
            foreach (var c in Physics.OverlapSphere(p, Config.BlastRadius))
            {
                var piece = c.GetComponent<WallPiece>();
                if (piece != null) { if (Vector3.Distance(c.transform.position, p) < 2.3f) { piece.TakeDamage(999, owner, true); effective = true; } continue; }
                var dest = c.GetComponentInParent<Destructible>();
                if (dest != null && dest.Alive) { dest.TakeDamage(Config.BlastCoverDamage * Config.BlastCoverMultiplier, owner, true); effective = true; continue; }
                var cover = c.GetComponentInParent<DeployedCover>();
                if (cover != null && cover.owner.team != owner.team) { cover.TakeDamage(Config.BlastCoverDamage * Config.BlastCoverMultiplier, owner, true); effective = true; continue; }
                var shield = c.GetComponentInParent<ShieldWall>();
                if (shield != null && shield.owner.team != owner.team) { shield.TakeDamage(Config.BlastCoverDamage * Config.BlastCoverMultiplier, owner, true); effective = true; }
            }
            if (effective) owner.stats.skillEffective++;
            var fire = new GameObject("燃烧区"); fire.transform.position = Skills.GroundAt(p);
            fire.AddComponent<FireZone>().owner = owner; Skills.spawned.Add(fire);
            Destroy(gameObject);
        }
    }

    // 焚芯爆破后的燃烧区：持续伤害敌人，迫使其离开点位
    public class FireZone : MonoBehaviour
    {
        public Fighter owner; float born, tick; Light l;
        void Start()
        {
            born = Time.time;
            var go = new GameObject("火焰"); go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.startLifetime = new ParticleSystem.MinMaxCurve(.5f, 1.1f); main.startSpeed = new ParticleSystem.MinMaxCurve(.8f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(.3f, .9f); main.startColor = new Color(1f, .45f, .1f); main.maxParticles = 300; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 110;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = Config.FireRadius; sh.rotation = new Vector3(-90, 0, 0);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(new Color(1f, .9f, .5f), 0), new GradientColorKey(new Color(1f, .2f, .05f), 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) }); col.color = g;
            go.GetComponent<ParticleSystemRenderer>().material = Fx.Additive;
            l = new GameObject("火光").AddComponent<Light>(); l.transform.SetParent(transform, false); l.transform.localPosition = Vector3.up;
            l.color = new Color(1f, .5f, .15f); l.range = 9; l.intensity = 3;
            var ring = new GameObject("燃烧边界").AddComponent<LineRenderer>(); ring.transform.SetParent(transform);
            ring.material = Fx.Additive; ring.startWidth = ring.endWidth = .08f; ring.startColor = ring.endColor = new Color(1f, .4f, .1f);
            Skills.Circle(ring, transform.position, Config.FireRadius);
            Sfx.Play("thrusterFire", transform.position, .8f, .6f, 1f, 35f);
        }
        void Update()
        {
            float age = Time.time - born;
            l.intensity = 2.5f + Mathf.PerlinNoise(Time.time * 8, 0) * 1.5f;
            if (age > Config.FireDuration) { Destroy(gameObject); return; }
            if (Time.time < tick) return;
            tick = Time.time + .25f;
            foreach (var f in Match.I.fighters)
            {
                if (!f.alive || owner == null || f.team == owner.team) continue;
                Vector3 d = f.transform.position - transform.position; d.y = 0;
                if (d.magnitude <= Config.FireRadius) { f.ReceiveDamage(Config.FireDps * .25f, owner, transform.position, false, true); f.revealedUntil = Mathf.Max(f.revealedUntil, Time.time + .3f); }
            }
        }
    }

    // 幻频 · 信号噪幕：区域内我方不可被侦测；区域内敌方小地图失效；不影响移动、开火、脚步
    public class NoiseField : MonoBehaviour
    {
        public Fighter owner; float born; Material mat; bool counted; float jamTime;
        static readonly Dictionary<Fighter, Fighter> protector = new Dictionary<Fighter, Fighter>();
        public void Init(Fighter f)
        {
            owner = f; born = Time.time;
            var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(dome.GetComponent<Collider>());
            dome.name = "噪幕穹顶"; dome.transform.SetParent(transform, false);
            dome.transform.localScale = new Vector3(Config.NoiseRadius * 2, Config.NoiseRadius * .9f, Config.NoiseRadius * 2);
            mat = new Material(Resources.Load<Material>("Materials/Holo") ?? Fx.Additive);
            mat.SetColor("_Color", new Color(.7f, .25f, 1f) * .6f);
            dome.GetComponent<Renderer>().sharedMaterial = mat;
            dome.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var ring = new GameObject("噪幕边界").AddComponent<LineRenderer>(); ring.transform.SetParent(transform);
            ring.material = Fx.Additive; ring.startWidth = ring.endWidth = .1f; ring.startColor = ring.endColor = new Color(.85f, .35f, 1f);
            Skills.Circle(ring, transform.position, Config.NoiseRadius);
            var font = Resources.Load<Font>("NotoSansSC");
            for (int i = 0; i < 14; i++)
            {
                var g = new GameObject("乱码"); g.transform.SetParent(transform, false);
                Vector2 rp = Random.insideUnitCircle * Config.NoiseRadius * .85f;
                g.transform.localPosition = new Vector3(rp.x, Random.Range(.6f, 3.5f), rp.y);
                var tm = g.AddComponent<TextMesh>(); tm.font = font; tm.fontSize = 64; tm.characterSize = Random.Range(.03f, .07f); tm.anchor = TextAnchor.MiddleCenter;
                tm.color = new Color(.85f, .4f, 1f, .85f); if (font != null) g.GetComponent<MeshRenderer>().sharedMaterial = Arena.WorldText(font);
                g.AddComponent<Skills.Glyph>();
            }
        }
        public static void CreditBlock(Fighter hidden)
        {
            if (protector.TryGetValue(hidden, out var p) && p != null) { p.stats.skillEffective++; protector.Remove(hidden); }
        }
        void Update()
        {
            float age = Time.time - born;
            mat.SetColor("_Color", new Color(.7f, .25f, 1f) * (.45f + .2f * Mathf.PerlinNoise(Time.time * 8f, 0)));
            bool anyJammed = false;
            foreach (var f in Match.I.fighters)
            {
                if (!f.alive) continue;
                if (new Vector2(f.transform.position.x - transform.position.x, f.transform.position.z - transform.position.z).magnitude > Config.NoiseRadius) continue;
                if (f.team == owner.team) { f.hiddenFromScanUntil = Time.time + .15f; if (f.revealedUntil > Time.time) { f.revealedUntil = 0; CreditOnce(); } protector[f] = owner; }
                else { f.jammedUntil = Time.time + .15f; anyJammed = true; }
            }
            if (anyJammed) { jamTime += Time.deltaTime; if (jamTime >= 1f) CreditOnce(); }
            if (Random.value < .3f) Fx.Burst(transform.position + new Vector3(Random.Range(-5f, 5f), Random.Range(.3f, 2.5f), Random.Range(-5f, 5f)), Vector3.up, new Color(.8f, .4f, 1f), 3, 1, .06f, .25f, 0);
            if (age >= Config.NoiseLife) Destroy(gameObject);
        }
        void CreditOnce() { if (!counted) { counted = true; owner.stats.skillEffective++; } }
    }
}
