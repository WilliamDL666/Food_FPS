using System.Collections.Generic;
using UnityEngine;

namespace NeonSiege
{
    // GDD 第 6 章：六个主动技能的规则实现。每个技能都有：启动声光提示、次数限制、明确反制。
    public static class Skills
    {
        class Preview { public LineRenderer arc, ring; public GameObject holo; public Vector3 point; public bool legal, link; public Links.Breach patch; }
        static readonly Dictionary<Fighter, Preview> previews = new Dictionary<Fighter, Preview>();
        public static readonly List<GameObject> spawned = new List<GameObject>();
        static readonly Dictionary<Fighter, float> dashEndedAt = new Dictionary<Fighter, float>();
        static readonly Dictionary<Fighter, float> dashEndTime = new Dictionary<Fighter, float>();   // V22 第三期：可乐弹射的"冲刺结束后宽限"
        // V22 第三期：冲刺中（含前摇）或冲刺结束 DashPadWindow 秒内
        public static bool InDashWindow(Fighter f) { return f.Dashing || dashEndTime.TryGetValue(f, out float t) && Time.time - t <= Config.DashPadWindow; }
        // V22 第三期：给 HUD 读预览状态（落点、是否联动、是否合法）
        public static bool PreviewState(Fighter f, out Vector3 point, out bool link, out bool legal)
        {
            point = Vector3.zero; link = legal = false;
            if (!previews.TryGetValue(f, out var p)) return false;
            point = p.point; link = p.link; legal = p.legal; return true;
        }

        // V21：美食模式下技能只换皮（名字 / 模型 / 颜色 / 音效），规则和数值不变（设计稿_V21_美食技能与可发现性 §2.1）
        public static bool Food { get { return ToonAssets.Available; } }
        public static readonly Color Cola = new Color(.62f, .36f, .16f), Bubble = new Color(.85f, .95f, 1f), Wafer = new Color(.93f, .74f, .45f), Caramel = new Color(1f, .78f, .38f),
            Mustard = new Color(.82f, 1f, .2f), Marsh = new Color(1f, .62f, .92f);

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
            previews.Clear(); dashEndedAt.Clear(); dashEndTime.Clear();
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
                    else { Sfx.Ui("error", .4f); Match.I.ui.Toast(pv.patch != null ? "这个破口已经被占用了（同一处同时只能有一个联动）" : "此处不能部署（不能封住棱核入口或离地过高）"); }
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

        // 截图 / 测试用：不读键盘，直接显示并刷新一帧技能预览
        public static void DebugPreview(Fighter f) { if (!previews.TryGetValue(f, out var p)) { p = new Preview(); previews[f] = p; } UpdatePreview(f, p); }
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
                {
                    Quaternion rot; Vector3 size = new Vector3(Config.CoverWidth, Config.CoverHeight, .35f);
                    // V22 第三期：预览贴近被打开的蛋糕墙破口 / 墙洞（2 m 内）→ 吸附、变金色 = 嵌入补洞
                    if (Links.PatchTarget(f, CoverDesired(f), out var br, out bool busy))
                    { p.patch = br; p.link = true; p.legal = !busy; p.point = br.center; rot = br.rot; size = new Vector3(br.width, br.height, .45f); }
                    else if (LinkPlus.RampTarget(f, CoverDesired(f), out var rs, out bool rbusy))
                    {
                        // V23 d：对准台边 → 金色斜放的坡道预览
                        p.patch = null; p.link = true; p.legal = !rbusy; p.point = rs.basePos;
                        float L = Vector3.Distance(rs.top, rs.basePos) + .5f; rot = Quaternion.LookRotation(rs.top - rs.basePos, Vector3.up); size = new Vector3(2.6f, .25f, L);
                        if (p.holo == null) { p.holo = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.Destroy(p.holo.GetComponent<Collider>()); p.holo.GetComponent<Renderer>().material = new Material(Resources.Load<Material>("Materials/Holo") ?? Fx.Additive); }
                        p.holo.transform.localScale = size; p.holo.transform.position = (rs.top + rs.basePos) / 2f; p.holo.transform.rotation = rot;
                        p.holo.GetComponent<Renderer>().material.SetColor("_Color", p.legal ? Links.Gold * 1.3f : bad);
                        break;
                    }
                    else { p.patch = null; p.link = false; p.legal = CoverPlacement(f, out p.point, out rot); }
                    if (p.holo == null)
                    {
                        p.holo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        Object.Destroy(p.holo.GetComponent<Collider>());
                        p.holo.GetComponent<Renderer>().material = new Material(Resources.Load<Material>("Materials/Holo") ?? Fx.Additive);
                    }
                    p.holo.transform.localScale = size;
                    p.holo.transform.position = p.point + Vector3.up * size.y / 2f; p.holo.transform.rotation = rot;
                    p.holo.GetComponent<Renderer>().material.SetColor("_Color", !p.legal ? bad : p.link ? Links.Gold * 1.3f : ok);
                    break;
                }
                case Role.Raven:
                case Role.Ember:
                    if (p.arc == null) { p.arc = Line("投掷预览", ok, .03f); p.ring = Line("落点预览", ok, .05f); }
                    bool arrow = f.role == Role.Raven;
                    Vector3 v = arrow ? ArrowVelocity(f) : ThrowVelocity(f, 15f);
                    var pts = new List<Vector3>();
                    p.point = Simulate(f.Eye + f.AimDir * .5f, v, pts, arrow ? Config.ArrowGravity : 1f, out Collider hitCol);
                    p.arc.positionCount = pts.Count; p.arc.SetPositions(pts.ToArray());
                    // V22 第三期：签子会扎进火锅 / 纸杯会粘上蛋糕墙 → 弹道和落点圈变金色
                    Component potHit = null;
                    if (arrow) { potHit = Links.PotAt(p.point); p.link = potHit != null && !Links.PotBusy(potHit); }
                    else p.link = hitCol != null && hitCol.GetComponent<WallPiece>() != null || LinkPlus.SauceAt(p.point, .8f) >= 0;   // V23 d：纸杯炸酱料也算联动
                    Circle(p.ring, p.point, !p.link ? (f.role == Role.Raven ? Config.DroneRadius : Config.BlastRadius) : arrow ? Links.PotRadius(potHit) : Config.BlastWallDoorHalf + .3f);
                    p.arc.startColor = p.arc.endColor = p.ring.startColor = p.ring.endColor = p.link ? Links.Gold : ok;
                    p.legal = true;
                    break;
                case Role.Phantom:
                    if (p.ring == null) p.ring = Line("噪幕预览", new Color(.9f, .4f, 1f), .06f);
                    p.point = GroundPoint(f, 22f);
                    Circle(p.ring, p.point, Config.NoiseRadius);
                    p.link = Links.SteamNear(p.point, Config.NoiseRadius + .5f, out _);   // V22 第三期：迷雾能罩住蒸汽源 → 金色
                    if (!p.link && Arena.I != null && f.team != Match.I.DefendingTeam) foreach (var cam in Arena.I.cams) if (cam != null && cam.Working && Links.Flat(cam.transform.position, p.point) <= Config.NoiseRadius + .5f) p.link = true;   // V23 d：罩住守方监控
                    p.ring.startColor = p.ring.endColor = p.link ? Links.Gold : new Color(.9f, .4f, 1f);
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
                    // V22 第三期：落点 2 m 内有被打开的破口 → 嵌入补洞（例外：被打出来的破口可以封，原有入口仍不能封）
                    if (Links.PatchTarget(f, target, out var br, out bool busy)) { if (busy || !Links.SpawnPatch(f, br)) return false; }
                    else if (LinkPlus.RampTarget(f, target, out var rs, out bool rbusy)) { if (rbusy || !LinkPlus.SpawnRamp(f, rs)) return false; }   // V23 d 副联动：威化坡道
                    else
                    {
                        if (!f.human) { if (!CoverPlacementAt(f, target, out target, out _)) return false; }
                        SpawnCover(f, target, Quaternion.Euler(0, f.yaw, 0));
                    }
                    break;
                case Role.Ironwall:
                {
                    // V22 第三期：5 m 内、面前有 ≤ 5 m 宽的门框（开着）→ 果冻门帘；否则原来的布丁盾
                    var fr = Links.DockFrame(f, out _);
                    if (fr != null) Links.SpawnCurtain(f, fr); else SpawnShield(f);
                    break;
                }
                case Role.Raven: ShootArrow(f, f.human ? ArrowVelocity(f) : ArrowVelocityTo(f.Eye, target)); break;
                case Role.Ember: Throw(f, f.human ? ThrowVelocity(f, 15f) : VelocityTo(f.Eye, target, 32f), false); break;
                case Role.Phantom: SpawnNoise(f, f.human ? target : GroundAt(target)); break;
            }
            SkillHand.Play(f, f.role);   // V23：第一人称左手动作（时长 ≤ 现有锁定时间，不新增不能开火的时间）
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
            f.lastDashDir = dir;
            f.dashUntil = Time.time + Config.DashWindup + Config.DashTime;
            var runner = f.gameObject.AddComponent<DashRunner>();
            runner.owner = f; runner.dir = dir;
            if (Food)
            {
                // 汽水冲刺：开罐"嘶"一声（音量和可听距离与原喷射声相同：1.0 / 40 m，反制靠听）
                // V23：前摇 = 摇罐（开罐声由 SkillHand 在前摇 60% 处播）；气泡喷射声在 DashRunner 进入位移时播（1.0 / 40 m，可听距离不变）
                Sfx.Play("food_can_shake", f.transform.position, .9f, 1f, 1f, 40f);
                Fx.Burst(f.transform.position + Vector3.up * .9f - dir * .4f, -dir, Cola, 30, 4, .12f, .4f, 0);
                Fx.Burst(f.transform.position + Vector3.up * 1.1f, Vector3.up, Bubble, 14, 2, .07f, .4f, -.2f);
            }
            else
            {
                Sfx.Play("thrusterFire", f.transform.position, 1f, 1.1f, 1f, 40f);
                Fx.Burst(f.transform.position + Vector3.up * .9f - dir * .4f, -dir, new Color(.4f, .7f, 1f), 30, 4, .12f, .4f, 0);
            }
        }

        public static void DashFinished(Fighter f)
        {
            dashEndedAt[f] = Time.time; dashEndTime[f] = Time.time; f.aimRecoveryUntil = Time.time + Config.DashAimRecovery;
            f.guardUntil = Time.time + Config.DashGuardTime; f.ammo = f.W.magazine;
            if (Food)
            {
                // 落地打个饱嗝：脚下炸开一圈白色气泡
                Fx.Shockwave(f.transform.position, 4f, Bubble, .4f);
                Fx.Burst(f.transform.position + Vector3.up * .2f, Vector3.up, Bubble, 30, 3.5f, .1f, .6f, -.1f);
                Sfx.Play("food_burp", f.transform.position, .9f, 1f, 1f, 30f);   // V23：落地打个饱嗝（签名音）
                Fx.Burst(f.Chest, Vector3.up, Cola, 18, 3, .08f, .5f, 0);
                // V23：脚下一圈可乐泡沫水渍，存在时间 = 减伤时间（敌人看地面就知道他还有减伤）
                if (Physics.Raycast(f.transform.position + Vector3.up * .5f, Vector3.down, out RaycastHit gh, 2f, ~0, QueryTriggerInteraction.Ignore))
                { var q = FoodFx.Splat(gh.point, gh.normal, new Color(.75f, .5f, .3f), 1.6f); if (q != null) Object.Destroy(q, Config.DashGuardTime); }
                return;
            }
            Fx.Shockwave(f.transform.position, 4f, new Color(.4f, .8f, 1f), .4f);
            Fx.Sparks(f.transform.position + Vector3.up * .1f, Vector3.up, new Color(.5f, .85f, 1f), 30);
            Sfx.Play("forceField", f.transform.position, .7f, 1.6f, 1f, 30f);
            Fx.Burst(f.Chest, Vector3.up, new Color(.4f, .8f, 1f), 25, 3, .08f, .5f, 0);
        }

        // 铆钉 · 战术掩体：贴地、合法区域、不能封入口
        public static bool CoverPlacement(Fighter f, out Vector3 point, out Quaternion rot)
        {
            rot = Quaternion.Euler(0, f.yaw, 0);
            return CoverPlacementAt(f, CoverDesired(f), out point, out _);
        }
        // 预览想放的位置（贴地之前）：准星前方 2.2–4.5 m
        public static Vector3 CoverDesired(Fighter f)
        {
            Quaternion rot = Quaternion.Euler(0, f.yaw, 0);
            Vector3 aimPoint = f.Eye + f.AimDir * 3.4f;
            if (Physics.Raycast(f.Eye, f.AimDir, out RaycastHit hit, 4.5f, ~0, QueryTriggerInteraction.Ignore) && hit.normal.y > .6f) aimPoint = hit.point;
            return f.transform.position + rot * Vector3.forward * Mathf.Clamp(Vector3.Distance(new Vector3(aimPoint.x, 0, aimPoint.z), new Vector3(f.transform.position.x, 0, f.transform.position.z)), 2.2f, 4.5f);
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
            var go = new GameObject((Food ? "威化饼干墙 " : "战术掩体 ") + Config.TeamName(f.team));
            go.transform.position = point; go.transform.rotation = rot;
            go.AddComponent<DeployedCover>().Init(f);
            spawned.Add(go);
            if (Food) Sfx.Play("food_squish", point, .7f, 1f, 1f, 40f);   // 挤奶油；V23：每叠一层一声"咔嚓"在 DeployedCover.Update 里播（3 声 = 3 层 = 0.6 s）
            else Sfx.Play("forceField", point, .9f, 1.1f, 1f, 40f);
        }

        // 铁幕 · 防护阵列
        static void SpawnShield(Fighter f)
        {
            var go = new GameObject((Food ? "焦糖布丁盾 " : "防护阵列 ") + Config.TeamName(f.team));
            Quaternion rot = Quaternion.Euler(0, f.yaw, 0);
            go.transform.position = f.transform.position + rot * Vector3.forward * 1.7f;
            go.transform.rotation = rot;
            go.AddComponent<ShieldWall>().Init(f);
            f.shieldActiveUntil = Time.time + Config.ShieldLife;
            spawned.Add(go);
            if (Food) Sfx.Play("food_jelly_boing", go.transform.position, 1f, 1f, 1f, 45f);   // V23：低沉"啵嘤"，45 m 不变
            else Sfx.Play("forceField", go.transform.position, 1f, .7f, 1f, 45f);
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
            var go = new GameObject(Food ? "芥末探签" : "侦测箭");
            go.transform.position = f.Eye + f.AimDir * .6f;
            go.transform.rotation = Quaternion.LookRotation(velocity);
            // 美食模式：竹签（糖葫芦签）+ 签头一团绿芥末；签子长度和碰撞球（0.35 m）不变
            var dark = Food ? Arena.Glow(new Color(.86f, .7f, .45f), 0f) : Resources.Load<Material>("Materials/GunMetal") ?? Arena.Glow(new Color(.2f, .2f, .22f), 0f);
            var glow = Arena.Glow(Food ? Mustard : new Color(.55f, .45f, 1f), 3f);
            if (Food) Part(go.transform, PrimitiveType.Sphere, new Vector3(0, 0, .16f), Vector3.zero, new Vector3(.09f, .09f, .12f), glow);   // 芥末团
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
            Sfx.Play(Food ? "food_squish" : "thrusterFire", go.transform.position, .25f, 2.4f, f.human ? .3f : 1f, 20f);   // V23：抹一下芥末
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

        static Vector3 Simulate(Vector3 pos, Vector3 v, List<Vector3> pts, float gravityScale = 1f) { return Simulate(pos, v, pts, gravityScale, out _); }
        public static Vector3 Simulate(Vector3 pos, Vector3 v, List<Vector3> pts, float gravityScale, out Collider col)
        {
            col = null;
            pts.Add(pos);
            for (int i = 0; i < 90; i++)
            {
                Vector3 next = pos + v * .04f; v += Physics.gravity * gravityScale * .04f;
                if (Physics.Linecast(pos, next, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)) { pts.Add(hit.point); col = hit.collider; return hit.point; }
                pts.Add(next); pos = next;
            }
            return pos;
        }

        static void Throw(Fighter f, Vector3 velocity, bool drone)
        {
            var go = GameObject.CreatePrimitive(drone ? PrimitiveType.Cylinder : PrimitiveType.Sphere);
            go.name = drone ? "侦测飞盘" : Food ? "点火纸杯蛋糕" : "定向爆破装置";
            go.transform.localScale = drone ? new Vector3(.38f, .04f, .38f) : Vector3.one * .26f;
            go.transform.position = f.Eye + f.AimDir * .6f;
            Object.Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = Arena.Glow(drone ? new Color(.55f, .45f, 1f) : new Color(1f, .3f, .15f), 3f);
            if (!drone && Food) CupcakeLook(go.transform);
            var proj = go.AddComponent<Thrown>();
            proj.owner = f; proj.velocity = velocity; proj.drone = drone;
            spawned.Add(go);
            Sfx.Play("click", go.transform.position, .8f, .8f, 1f, 25f);   // 打火机"咔"
            if (!drone && Food) Fx.Burst(go.transform.position, Vector3.up, new Color(1f, .75f, .3f), 6, 1.5f, .04f, .3f, 0);   // V23：点火的火星
        }

        // V21：点火纸杯蛋糕的外观（直径约 0.3 m，插一根烟花蜡烛）；投掷体和判定不变
        public static void CupcakeLook(Transform t)
        {
            var cup = Arena.Model("Toon/Hats/Hat_Cupcake");
            if (cup == null) return;
            var own = t.GetComponent<Renderer>(); if (own != null) own.enabled = false;
            foreach (var c in cup.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            Arena.Fit(cup, new Vector3(.3f, 0, 0));
            var holder = new GameObject("纸杯").transform; holder.SetParent(t, false);
            Vector3 ls = t.lossyScale; holder.localScale = new Vector3(1f / Mathf.Max(.01f, ls.x), 1f / Mathf.Max(.01f, ls.y), 1f / Mathf.Max(.01f, ls.z));
            cup.transform.SetParent(holder, false); cup.transform.localPosition = new Vector3(0, -.12f, 0);
            Part(holder, PrimitiveType.Cylinder, new Vector3(0, .13f, 0), Vector3.zero, new Vector3(.025f, .07f, .025f), Arena.Glow(new Color(1f, .4f, .6f), 1f)).name = "蜡烛";   // 烟花蜡烛（V23：引信期间越烧越短）
            Part(holder, PrimitiveType.Sphere, new Vector3(0, .22f, 0), Vector3.zero, Vector3.one * .045f, Arena.Glow(new Color(1f, .7f, .2f), 6f)).name = "火苗";             // 火苗
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
                if (d.magnitude <= Config.FireRadius) { DamageCtx.Set(DamageKind.Fire, Skills.Food ? "焦糖火" : "燃烧区"); f.ReceiveDamage(Config.FireDps * .25f, owner, transform.position, false, true); f.revealedUntil = Mathf.Max(f.revealedUntil, Time.time + .3f); f.fireRevealUntil = Mathf.Max(f.fireRevealUntil, Time.time + .3f); }   // V22：单独记"火里暴露"，警告文案不再借用芥末
            }
        }
    }

    public class Glyph : MonoBehaviour
    {
        static readonly string chars = "01霓城封锁ERR#@%&数据干扰▓░", sweet = "甜黏糖？…甜糖黏";
        TextMesh tm; float next; Vector3 basePos;
        void Start() { tm = GetComponent<TextMesh>(); basePos = transform.localPosition; }
        void Update()
        {
            var cam = Camera.main; if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
            transform.localPosition = basePos + Vector3.up * Mathf.Sin(Time.time * 2 + basePos.x) * .2f;
            if (Time.time < next) return;
            next = Time.time + Random.Range(.05f, .25f);
            string pool = Food ? sweet : chars;   // V21：棉花糖迷雾里飘的是"甜 黏 糖 ？"
            int n = Random.Range(Food ? 1 : 2, Food ? 3 : 6); var s = ""; for (int i = 0; i < n; i++) s += pool[Random.Range(0, pool.Length)];
            tm.text = s;
        }
    }

    // 幻频 · 信号噪幕
        static void SpawnNoise(Fighter f, Vector3 point)
        {
            var go = new GameObject((Food ? "棉花糖迷雾 " : "信号噪幕 ") + Config.TeamName(f.team));
            go.transform.position = point;
            go.AddComponent<NoiseField>().Init(f);
            spawned.Add(go);
            if (Food)
            {
                Sfx.Play("food_marsh_puff", point, 1f, 1f, 1f, 45f); Sfx.Play("food_poprock_loop", point, .7f, 1f, 1f, 45f);   // V23："噗"地膨出 + 跳跳糖噼啪（45 m 不变）
                Fx.Tracer(f.Eye + f.transform.right * -.25f + Vector3.down * .2f, point + Vector3.up * .5f, Marsh, .05f);   // 左手甩出去的一条糖丝
            }
            else Sfx.Play("glitch", point, 1f, .8f, 1f, 45f);
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
                if (Food) SkillPop.Show(source, "续杯！", Caramel);   // V23：结果字 = 遥测有效判定
            }
        }

        public static void NotifyKill(Fighter killer, Fighter victim) { LinkPlus.OnKill(victim); }   // V23 d：雾里的击杀遥测

        // V23：汽水冲刺的可乐尾迹（棕色 + 白色气泡，0.7 s 消散）
        public static TrailRenderer ColaTrail(Fighter f)
        {
            var go = new GameObject("可乐尾迹"); go.transform.SetParent(f.transform, false); go.transform.localPosition = new Vector3(0, 1f, -.3f);
            var tr = go.AddComponent<TrailRenderer>(); tr.time = .7f; tr.startWidth = .55f; tr.endWidth = .05f; tr.material = FoodFx.TrailMaterial;
            tr.startColor = new Color(Cola.r, Cola.g, Cola.b, .85f); tr.endColor = new Color(Bubble.r, Bubble.g, Bubble.b, 0f); tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return tr;
        }
        // V23：芥末味的"波浪形烟环"（不是正圆）
        public static void WavyCircle(LineRenderer lr, Vector3 center, float r, float phase)
        {
            lr.loop = true; lr.positionCount = 64;
            for (int i = 0; i < 64; i++)
            {
                float a = i / 64f * Mathf.PI * 2, rr = r * (1f + .09f * Mathf.Sin(a * 6f + phase * 5f) + .05f * Mathf.Sin(a * 11f - phase * 3f));
                lr.SetPosition(i, center + new Vector3(Mathf.Cos(a) * rr, .12f + .1f * Mathf.Sin(a * 4f + phase * 4f), Mathf.Sin(a) * rr));
            }
        }

        // V23 a-1：原 LineClear 同时管"看不看得见"和"炸不炸得到"，第一行就查蒸汽 → 站在热气里的人手雷炸不到。拆成两个：
        //   SightClear = 视线（蒸汽 / 棉花糖蒸汽等遮挡 + 实体几何），给 AI 视野、监控、芥末扫描、标记、头像用；
        //   BlastClear = 爆炸（只看实体几何），给手雷、纸杯蛋糕、爆炸桶用 —— 往蒸汽里扔雷是最自然的反制，不能被雾挡掉。
        public static bool SightClear(Vector3 a, Vector3 b, Fighter target)
        {
            if (SteamCloud.Blocks(a, b)) return false;   // V17：火锅热气挡视线
            return BlastClear(a, b, target);
        }
        public static bool BlastClear(Vector3 a, Vector3 b, Fighter target)
        {
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
        public Fighter owner; public Vector3 dir; float t, nextGhost; Light glow; bool fizzed, canOpened; TrailRenderer trail;
        void Start()
        {
            glow = new GameObject("喷射光").AddComponent<Light>(); glow.transform.SetParent(owner.transform, false);
            glow.transform.localPosition = new Vector3(0, 1f, -.5f); glow.color = Skills.Food ? Skills.Caramel : new Color(.4f, .7f, 1f); glow.range = 6; glow.intensity = 4;
            Fx.Shockwave(owner.transform.position, 2.5f, Skills.Food ? Skills.Bubble : new Color(.4f, .7f, 1f), .3f);
        }
        void OnDestroy() { if (glow != null) Destroy(glow.gameObject); if (trail != null) { trail.transform.SetParent(null, true); trail.emitting = false; Destroy(trail.gameObject, trail.time + .1f); } }
        void Update()
        {
            t += Time.deltaTime;
            if (owner == null || !owner.alive) { Destroy(this); return; }
            Fx.Burst(owner.transform.position + Vector3.up * 1.1f - owner.transform.forward * .35f, -dir + Vector3.down * .3f, Skills.Food ? Skills.Cola : new Color(.4f, .75f, 1f), 4, 5, .12f, .25f, 0);   // 背后喷可乐泡沫
            // V24：电脑没有第一人称左手（SkillHand 只给玩家播开罐声），在前摇 60% 处补一声同样的"啵"开罐（替代原来叠加的 thrusterFire）
            if (Skills.Food && !owner.human && !canOpened && t >= Config.DashWindup * .6f) { canOpened = true; Sfx.Play("food_can_open", owner.transform.position, .8f, 1f, 1f, 20f); }
            if (t < Config.DashWindup) return;
            if (!fizzed) { fizzed = true; if (Skills.Food) { Sfx.Play("food_fizz", owner.transform.position, 1f, 1.1f, 1f, 40f); if (trail == null) trail = Skills.ColaTrail(owner); } }   // V23：进入位移才喷气泡；身后拉可乐尾迹
            if (Skills.Food && owner.human) SkillPop.EdgeBubbles = 1f;   // 屏幕边缘一圈焦糖色气泡流
            V24.DashTrail(owner);   // V24：身后留一条可乐气泡带 2 s，队友踩上去 ×1.1
            if (Time.time >= nextGhost) { nextGhost = Time.time + .035f; Ghost.Spawn(owner, Skills.Food ? new Color(.6f, .75f, .9f) : new Color(.3f, .7f, 1f)); }   // 半透明气泡色残影
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
        public Fighter owner; public float hp = Config.CoverHp * (ToonAssets.Available ? 1.3f : 1f), absorbed;   // 铆钉被动：掩体更耐打
        float born; Transform visual; BoxCollider col; UnityEngine.AI.NavMeshObstacle obstacle; bool counted;
        // V22 第三期：嵌入补洞 —— 形状贴合破口、耐久不变、存在 CoverPatchLife 秒、部署 CoverPatchDeploy 秒
        public float w = Config.CoverWidth, h = Config.CoverHeight, life = Config.CoverLife, deploy = Config.CoverDeploy, thick = .35f;
        public bool patched; public Object patchKey;
        public static readonly List<DeployedCover> Live = new List<DeployedCover>();
        void OnEnable() { Live.Add(this); }
        void OnDisable() { Live.Remove(this); }
        public float Age { get { return Time.time - born; } }
        public void InitPatch(Fighter f, Links.Breach b)
        {
            patched = true; patchKey = b.key; w = b.width + .06f; h = b.height + .02f; thick = .45f; life = Config.CoverPatchLife; deploy = Config.CoverPatchDeploy;
            Init(f);
            var gold = Arena.Glow(Links.Gold, 3.2f);   // 金边威化：破口四周一圈金色描边（LINK-2）
            foreach (float sx in new[] { -1f, 1f }) Trim(new Vector3(sx * (w / 2 + .03f), h / 2, 0), new Vector3(.09f, h + .06f, thick + .08f), gold);
            Trim(new Vector3(0, h + .03f, 0), new Vector3(w + .12f, .09f, thick + .08f), gold);
            Trim(new Vector3(0, .03f, 0), new Vector3(w + .12f, .06f, thick + .08f), gold);
        }
        void Trim(Vector3 lp, Vector3 s, Material m)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(c.GetComponent<Collider>()); c.name = "金边";
            c.transform.SetParent(transform, false); c.transform.localPosition = lp; c.transform.localScale = s;
            var r = c.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        public void Init(Fighter f)
        {
            owner = f; born = Time.time;
            col = gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(w, h, thick); col.center = new Vector3(0, h / 2, 0);
            visual = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            Destroy(visual.GetComponent<Collider>());
            visual.SetParent(transform, false); visual.localScale = new Vector3(w, .05f, thick);
            visual.GetComponent<Renderer>().sharedMaterial = Resources.Load<Material>("Materials/MetalPlate");
            if (Skills.Food)
            {
                // V21：威化饼干墙 —— 用地图同款威化方块模型撑满掩体体积（碰撞体和尺寸不变），夹心奶油条是队伍颜色
                var wafer = Arena.Model("Toon/Food/WaferBlock");
                if (wafer != null)
                {
                    foreach (var c in wafer.GetComponentsInChildren<Collider>()) Destroy(c);
                    Arena.Fit(wafer, Vector3.one); wafer.name = "威化";
                    wafer.transform.SetParent(visual, false); wafer.transform.localPosition = new Vector3(0, -.5f, 0);
                    visual.GetComponent<Renderer>().enabled = false;
                }
                else visual.GetComponent<Renderer>().sharedMaterial = Arena.Glow(Skills.Wafer, 0f);
            }
            var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(strip.GetComponent<Collider>());
            strip.transform.SetParent(visual, false); strip.transform.localPosition = new Vector3(0, .35f, 0); strip.transform.localScale = new Vector3(1.01f, .06f, 1.05f);
            strip.GetComponent<Renderer>().sharedMaterial = Arena.Glow(Config.TeamColor(f.team), 3f);
            obstacle = gameObject.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box; obstacle.size = col.size; obstacle.center = col.center; obstacle.carving = true;
        }
        Transform holoFrame; LineRenderer beam; int crackStage, layersDone;
        public float MaxHp { get { return Config.CoverHp * (ToonAssets.Available ? 1.3f : 1f); } }
        void Update()
        {
            float age = Time.time - born;
            float k = Mathf.Clamp01(age / deploy);
            if (Skills.Food)
            {
                // V23：威化分 3 层从下往上叠（每层 = 部署时间 / 3，0.6 s 部署 → 每层 0.2 s），每叠一层一声"咔嚓"，最后一层落地加一声低频"咚"
                int layers = Mathf.Min(3, Mathf.FloorToInt(k * 3f + .0001f));
                if (age >= deploy) layers = 3;
                while (layersDone < layers)
                {
                    layersDone++;
                    Sfx.Play("food_wafer_crunch", transform.position + Vector3.up * h * layersDone / 3f, .9f, 1f + layersDone * .05f, 1f, 40f);
                    Fx.Burst(transform.position + Vector3.up * h * layersDone / 3f, Vector3.up, Skills.Wafer, 8, 1.5f, .05f, .4f, 1f);
                    if (layersDone == 3) Sfx.Play("food_rice_thud", transform.position, .6f, 1.5f, 1f, 35f);
                }
                k = layers / 3f + (layers < 3 ? Mathf.SmoothStep(0, 1, Mathf.Repeat(k * 3f, 1f)) / 3f * .35f : 0f);   // 一层一层"顿"地长高
            }
            if (age < deploy)
            {
                if (holoFrame == null)
                {
                    holoFrame = GameObject.CreatePrimitive(PrimitiveType.Cube).transform; Destroy(holoFrame.GetComponent<Collider>());
                    holoFrame.SetParent(transform, false); holoFrame.localScale = new Vector3(w + .1f, h + .1f, thick + .1f); holoFrame.localPosition = new Vector3(0, h / 2, 0);
                    var hm = new Material(Resources.Load<Material>("Materials/Hex") ?? Fx.Additive); hm.SetColor("_Color", Skills.Food ? Skills.Wafer * 1.2f : Config.TeamColor(owner.team) * 1.6f);
                    holoFrame.GetComponent<Renderer>().sharedMaterial = hm;
                }
                if (beam == null)
                {
                    beam = new GameObject("施工光束").AddComponent<LineRenderer>(); beam.transform.SetParent(transform);
                    beam.material = Fx.Additive; beam.startWidth = .06f; beam.endWidth = .02f; beam.positionCount = 2;
                    beam.startColor = new Color(1f, .85f, .3f); beam.endColor = Config.TeamColor(owner.team);
                    if (Skills.Food) { beam.startWidth = .12f; beam.endWidth = .08f; beam.startColor = new Color(1f, .97f, .92f); }   // 爆米花机口挤出的奶油
                }
                Vector3 from = owner.avatar != null ? owner.avatar.MuzzlePosition : owner.Eye;
                beam.SetPosition(0, from); beam.SetPosition(1, transform.position + Vector3.up * h * k + transform.right * Mathf.Sin(Time.time * 20) * w * .33f);
                if (Skills.Food) { if (Random.value < .5f) Fx.Burst(transform.position + transform.right * Random.Range(-w * .4f, w * .4f) + Vector3.up * visual.localScale.y, Vector3.up, patched ? Links.Gold : new Color(1f, .97f, .92f), 3, 1.2f, .06f, .4f, .6f); }
                else if (Random.value < .7f) Fx.Sparks(transform.position + transform.right * Random.Range(-w * .4f, w * .4f) + Vector3.up * visual.localScale.y, Vector3.up, new Color(1f, .8f, .4f), 3);
            }
            else if (holoFrame != null) { if (beam != null) Destroy(beam.gameObject); Destroy(holoFrame.gameObject); holoFrame = null; Fx.Burst(transform.position + Vector3.up * .8f, Vector3.up, patched ? Links.Gold : Config.TeamColor(owner.team), 25, 3, .08f, .5f, 0); }
            visual.localScale = new Vector3(w, Mathf.Max(.05f, h * k), thick);
            visual.localPosition = new Vector3(0, visual.localScale.y / 2, 0);
            col.size = new Vector3(w, Mathf.Max(.1f, h * k), thick); col.center = new Vector3(0, col.size.y / 2, 0);
            if (age > deploy + life) Destroy(gameObject);
        }
        void Cracks(int stage)
        {
            var dark = Arena.Glow(new Color(.45f, .3f, .15f), 0f);
            for (int i = 0; i < 2 + 2 * stage; i++)   // V24：越往后裂纹越多、越长，还会分叉
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(c.GetComponent<Collider>()); c.name = "威化裂缝";
                c.transform.SetParent(transform, false);
                float side = i % 2 == 0 ? 1f : -1f, len = Random.Range(.25f, .55f) * (1f + stage * .25f), rot = Random.Range(-60f, 60f);
                Vector3 lp = new Vector3(Random.Range(-w * .4f, w * .4f), Random.Range(.25f, h - .2f), side * (thick / 2 + .006f));
                c.transform.localPosition = lp;
                c.transform.localRotation = Quaternion.Euler(0, 0, rot); c.transform.localScale = new Vector3(len, .025f + .006f * stage, .01f);
                var r = c.GetComponent<Renderer>(); r.sharedMaterial = dark; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (stage >= 2)
                {
                    var b = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(b.GetComponent<Collider>()); b.name = "威化裂缝分叉"; b.transform.SetParent(transform, false);
                    b.transform.localPosition = lp + Quaternion.Euler(0, 0, rot) * Vector3.right * len * .3f; b.transform.localRotation = Quaternion.Euler(0, 0, rot + Random.Range(35f, 70f) * (Random.value < .5f ? 1 : -1));
                    b.transform.localScale = new Vector3(len * .45f, .02f, .01f); var br = b.GetComponent<Renderer>(); br.sharedMaterial = dark; br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
        }
        public bool TakeDamage(float amount, Fighter source, bool explosive)
        {
            hp -= amount; if (source == null || source.team != owner.team) absorbed += amount;
            if (!counted && absorbed >= 60) { counted = true; owner.stats.skillEffective++; if (Skills.Food) SkillPop.Show(owner, "威化挡住了！", Skills.Wafer); }
            if (Skills.Food && hp > 0)
            {
                // 耐久掉到 2/3、1/3 时掉饼干渣、颜色变暗（裂纹感）
                int stage = Mathf.Clamp(Mathf.FloorToInt((1f - hp / MaxHp) * 5f), 0, 4);   // V24：每掉 20% 多一层裂纹（原来只有 2/3、1/3 两档）
                if (stage > crackStage)
                {
                    crackStage = stage;
                    Fx.Burst(transform.position + Vector3.up * .9f, Vector3.up, Skills.Wafer, 24, 3, .07f, .6f, 1.2f);
                    foreach (var r in visual.GetComponentsInChildren<Renderer>()) if (r.gameObject != visual.gameObject && r.material.HasProperty("_Color") && r.name != "Cube") r.material.color *= .82f;
                    Cracks(stage);   // V23：2/3、1/3 出现真的裂缝
                }
                Sfx.Play("food_wafer_crunch", transform.position + Vector3.up, .35f, 1.4f + Random.value * .2f, 1f, 20f);   // V23：被打的小脆响
                Fx.Burst(transform.position + Vector3.up * Random.Range(.4f, h), Vector3.up, Skills.Wafer, 3, 2f, .04f, .4f, 1.2f);
            }
            if (hp <= 0)
            {
                Fx.Burst(transform.position + Vector3.up * .8f, Vector3.up, Skills.Food ? Skills.Wafer : new Color(.6f, .8f, 1f), 30, 5, .12f, .7f, 1);
                if (Skills.Food) { Sfx.Play("food_wafer_shatter", transform.position, 1f, 1f, 1f, 40f); Fx.Debris(transform.position + Vector3.up * h * .5f, Skills.Wafer, 14); }   // V23：整面墙"哗啦"散成饼干块
                else Sfx.Play("impactMetal", transform.position, .9f, .8f);
                if (source != null && source.team != owner.team) source.stats.coverDestroyed++;
                Destroy(gameObject);
            }
            return true;
        }
    }

    public class ShieldWall : MonoBehaviour, IDamageable
    {
        public Fighter owner; float hp = Config.ShieldHp, absorbed, born; bool counted; Material mat;
        Transform jiggle; Vector3 jiggleBase; float jt = 9f;
        // V22 第三期：果冻门帘 —— 嵌进门框，不跟人走（铁幕可以离开）；承伤 ×ShieldDockHpMult、持续 ShieldDockLife；双方子弹手雷都挡，人能慢慢穿过（×ShieldDockPassSlow）
        public bool docked; public Object dockKey; float w = Config.ShieldWidth, hgt = Config.ShieldHeight, life = Config.ShieldLife, hpMax = Config.ShieldHp; float wobbleAt, dripAt;
        // V23：布丁"啵"地立起 —— 0 → 0.1 s 从 60% 涨到 110%，0.1 → 0.3 s 回落到 100%
        public static float PopCurve(float age)
        {
            if (age >= .3f) return 1f;
            if (age < .1f) return Mathf.Lerp(.6f, 1.1f, age / .1f);
            return Mathf.Lerp(1.1f, 1f, Mathf.SmoothStep(0, 1, (age - .1f) / .2f));
        }
        public static readonly List<ShieldWall> Docked = new List<ShieldWall>();
        public float Hp { get { return hp; } }
        public float HpMax { get { return hpMax; } }
        public float Width { get { return w; } }
        void OnDisable() { Docked.Remove(this); }
        public void InitDock(Fighter f, Links.Frame fr)
        {
            docked = true; dockKey = fr.key; w = fr.width + .15f; hgt = Mathf.Min(fr.height, 3.6f); life = Config.ShieldDockLife; hpMax = hp = Config.ShieldHp * Config.ShieldDockHpMult;
            Init(f); Docked.Add(this);
            // 门帘外观（LINK-3）：几条半透明布丁色"帘子"垂下来 + 顶上焦糖横梁 + 两侧金边
            var drape = new Material(Resources.Load<Material>("Materials/Hex") ?? Resources.Load<Material>("Materials/Holo") ?? Fx.Additive); drape.SetColor("_Color", Skills.Caramel * .9f);
            int strips = Mathf.Max(3, Mathf.RoundToInt(w / .9f));
            for (int i = 0; i < strips; i++)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(s.GetComponent<Collider>()); s.name = "果冻帘";
                s.transform.SetParent(transform, false); float x = -w / 2 + (i + .5f) * w / strips;
                s.transform.localPosition = new Vector3(x, hgt / 2, (i % 2 == 0 ? .09f : -.09f)); s.transform.localScale = new Vector3(w / strips * .92f, hgt * .98f, .05f);
                var r = s.GetComponent<Renderer>(); r.sharedMaterial = drape; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var gold = Arena.Glow(Links.Gold, 3f);
            foreach (float sx in new[] { -1f, 1f })
            {
                var e = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(e.GetComponent<Collider>()); e.name = "门帘金边";
                e.transform.SetParent(transform, false); e.transform.localPosition = new Vector3(sx * w / 2, hgt / 2, 0); e.transform.localScale = new Vector3(.08f, hgt, .3f);
                e.GetComponent<Renderer>().sharedMaterial = gold;
            }
        }
        // 站在门帘里（门框局部空间 |x| ≤ 宽/2、|z| ≤ 0.7）
        public static ShieldWall CurtainAt(Vector3 p)
        {
            foreach (var s in Docked)
            {
                if (s == null) continue;
                Vector3 l = s.transform.InverseTransformPoint(p);
                if (Mathf.Abs(l.x) <= s.w / 2 + .2f && Mathf.Abs(l.z) <= .7f && l.y > -.6f && l.y < s.hgt) return s;
            }
            return null;
        }
        public static bool InCurtain(Vector3 p)
        {
            var s = CurtainAt(p); if (s == null) return false;
            if (Time.time >= s.wobbleAt) { s.wobbleAt = Time.time + .45f; s.jt = 0; if (Skills.Food) Sfx.Play("food_squish", p + Vector3.up, .35f, 1.3f, 1f, 18f); }
            return true;
        }
        // 美食模式：布丁黄偏队伍色；写实模式：队伍色
        Color Tint { get { return Skills.Food ? Color.Lerp(Skills.Caramel, Config.TeamColor(owner.team), .3f) : Config.TeamColor(owner.team); } }
        public void Init(Fighter f)
        {
            owner = f; born = Time.time;
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "阵列面板"; panel.transform.SetParent(transform, false);
            panel.transform.localPosition = new Vector3(0, hgt / 2, 0); panel.transform.localScale = new Vector3(w, hgt, .12f);
            mat = new Material(Resources.Load<Material>("Materials/Hex") ?? Resources.Load<Material>("Materials/Holo") ?? Fx.Additive);
            mat.SetColor("_Color", Tint * 1.3f);
            panel.GetComponent<Renderer>().sharedMaterial = mat;
            if (Skills.Food)
            {
                // V21：焦糖布丁盾 —— 面板沿用原来的半透明材质（双方都能隔着盾看到人，透明度不变），只换成布丁色 + 顶上一层焦糖
                panel.name = "布丁面板"; jiggle = panel.transform; jiggleBase = panel.transform.localScale;
                var top = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(top.GetComponent<Collider>()); top.name = "焦糖层";
                top.transform.SetParent(transform, false); top.transform.localPosition = new Vector3(0, hgt + .04f, 0); top.transform.localScale = new Vector3(w, docked ? .22f : .1f, docked ? .4f : .16f);
                top.GetComponent<Renderer>().sharedMaterial = Arena.Glow(new Color(.55f, .28f, .08f), .6f);
            }
            panel.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var col = panel.GetComponent<Collider>();
            foreach (var other in Match.I.fighters) if (other.cc != null) Physics.IgnoreCollision(col, other.cc);
            var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(frame.GetComponent<Collider>());
            frame.transform.SetParent(transform, false); frame.transform.localPosition = new Vector3(0, .05f, 0); frame.transform.localScale = new Vector3(w + .1f, .1f, .3f);
            frame.GetComponent<Renderer>().sharedMaterial = Arena.Glow(Config.TeamColor(f.team), 3f);
            var l = new GameObject("阵列光").AddComponent<Light>(); l.transform.SetParent(transform, false); l.transform.localPosition = new Vector3(0, 1.2f, -.4f);
            l.color = Config.TeamColor(f.team); l.range = 7; l.intensity = 2.5f;
            Fx.Burst(transform.position + Vector3.up * 1.2f, transform.forward, Config.TeamColor(f.team), 40, 4, .1f, .5f, 0);
            Fx.Shockwave(transform.position, 3f, Config.TeamColor(f.team), .35f);
        }
        void Update()
        {
            float age = Time.time - born;
            mat.SetColor("_Color", Tint * (1.1f + .3f * Mathf.Sin(Time.time * 10f)) * Mathf.Clamp01(hp / hpMax * .7f + .3f));   // 血越少越淡（布丁化开）
            if (jiggle != null)
            {
                jt += Time.deltaTime; float jw = Mathf.Exp(-jt * 6f) * Mathf.Sin(jt * 28f);
                float pop = PopCurve(age);   // V23：0.3 s 的弹性出场（盾第 1 帧就生效，只是形状在弹）
                jiggle.localScale = new Vector3(jiggleBase.x * (1 + .03f * jw) * (2f - pop) * .5f + jiggleBase.x * .5f, jiggleBase.y * (1 - .05f * jw) * pop, jiggleBase.z);
                jiggle.localPosition = new Vector3(0, jiggle.localScale.y / 2f, 0);
                if (hp < hpMax * .5f && Time.time >= dripAt) { dripAt = Time.time + Mathf.Lerp(.15f, .5f, hp / (hpMax * .5f)); Fx.Burst(transform.position + transform.right * Random.Range(-w * .45f, w * .45f) + Vector3.up * .15f, Vector3.down, new Color(.75f, .45f, .12f), 2, .5f, .07f, .6f, 1.5f); }   // 血量 < 50%：底部滴焦糖
            }
            if (docked && Match.I != null)   // V23 d：果冻粘身 —— 穿过门帘的敌人透墙可见 JellyStickTime 秒
                foreach (var e in Match.I.fighters) if (e.alive && e.team != owner.team && CurtainAt(e.transform.position) == this) LinkPlus.JellyStick(e, this);
            if (age > life || !docked && !owner.alive) { if (!docked) owner.shieldActiveUntil = 0; Destroy(gameObject); }
        }
        public bool TakeDamage(float amount, Fighter source, bool explosive)
        {
            if (source != null && source.team == owner.team) return true;   // blocks friendly fire too, without damage
            hp -= amount; absorbed += amount;
            mat.SetColor("_Color", Color.white * 3f);
            if (source != null) { mat.SetVector("_HitPos", transform.position + Vector3.up * 1.4f + (source.transform.position - transform.position).normalized * .1f); mat.SetFloat("_HitTime", Time.timeSinceLevelLoad); }
            if (Skills.Food)
            {
                // V23：软"噗"，血越少音高越低（听得出盾快化了）；被打处一圈凹坑波纹
                jt = 0; Sfx.Play("food_squish", transform.position + Vector3.up, .4f, Mathf.Lerp(.8f, 1.6f, Mathf.Clamp01(hp / hpMax)), 1f, 30f);
                if (source != null) { Vector3 hitAt = transform.position + Vector3.up * 1.2f + (source.transform.position - transform.position).normalized * .1f; Fx.Burst(hitAt, (source.transform.position - transform.position).normalized, Skills.Caramel, 5, 1.2f, .06f, .3f, .3f); }
                if (absorbed >= 60) SkillPop.Show(owner, "布丁吃下 " + Mathf.RoundToInt(absorbed) + " 伤害", Skills.Caramel);
            }
            else Sfx.Play("impactMetal_light", transform.position + Vector3.up, .5f, 1.8f, 1f, 30f);
            if (!counted && absorbed >= 60) { counted = true; owner.stats.skillEffective++; }
            if (hp <= 0)
            {
                Fx.Burst(transform.position + Vector3.up * 1.2f, transform.forward, Tint, 40, 5, .1f, .6f, 0);
                if (Skills.Food)
                {
                    Sfx.Play("food_splat", transform.position, 1f, .7f);   // 湿的"啪叽"
                    var puddle = FoodFx.Splat(Skills.GroundAt(transform.position) + Vector3.up * .02f, Vector3.up, Skills.Caramel, Mathf.Max(1.6f, w * .9f));   // 瘫成一滩布丁，留 1.8 s
                    if (puddle != null) Destroy(puddle, 1.8f);
                }
                else Sfx.Play("forceField", transform.position, 1f, .5f);
                if (!docked) owner.shieldActiveUntil = 0;
                Destroy(gameObject);
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
                else { var c = gameObject.AddComponent<BlastCharge>(); c.Init(owner, p, hit.collider != null ? hit.collider.GetComponent<WallPiece>() : null); }   // V22 第三期：粘在蛋糕墙上 = 开墙
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
            Color c = Skills.Food ? Skills.Mustard : new Color(.6f, .45f, 1f); tr.startColor = c; tr.endColor = new Color(c.r, c.g, c.b, 0); tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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
                if (Skills.Food) { Fx.Burst(p, n, Skills.Mustard, 14, 2.4f, .06f, .4f, .5f); Sfx.Play("food_bamboo_thunk", p, .8f, 1f, 1f, 30f); gameObject.AddComponent<Quiver>(); }   // V23：竹签扎木头 + 溅一小团芥末 + 签尾抖 3 下
                else { Fx.Sparks(p, n, new Color(.7f, .6f, 1f), 10); Sfx.Play("impactMetal", p, .7f, 1.6f, 1f, 30f); }
                var d = gameObject.AddComponent<Drone>();
                var pot = hitSomething ? Links.PotAt(p) : null;   // V22 第三期：签子扎进火锅 / 关东煮锅 → 芥末辣锅（同一口锅同时只有一个）
                if (pot != null && !Links.PotBusy(pot)) d.InitPot(owner, p, n, pot); else d.Init(owner, p, n);
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
        // V22 第三期：芥末辣锅模式 —— 不做两次扫描，改为火锅周围 MustardPotRadius 内持续 MustardPotLife 秒的黄绿辣雾；雾里的敌人一直透墙可见（不需要视线），离开后再可见 MustardPotLinger 秒
        public Component pot; float potTick, coughAt; int potFound; readonly HashSet<Fighter> potSeen = new HashSet<Fighter>(); GameObject fog;   // V23 d：pot = 火锅 / 关东煮锅 / 回血炉
        public bool PotMode { get { return pot != null; } }
        public float PotLeft { get { return pot != null ? Mathf.Max(0, Config.MustardPotLife - (Time.time - landed)) : 0; } }
        public float PotRadiusNow { get { return pot == null ? 0 : Links.PotRadius(pot) * Mathf.Clamp01((Time.time - landed) / Config.MustardSpread); } }   // V23 d：1.75 s 扩散到最大
        public static readonly List<Drone> Live = new List<Drone>();   // V21：敌方产物气泡用
        void OnEnable() { Live.Add(this); }
        void OnDisable() { Live.Remove(this); }
        static Color ScanColor { get { return Skills.Food ? Skills.Mustard : new Color(.6f, .45f, 1f); } }
        public void Init(Fighter f, Vector3 p) { Init(f, p, Vector3.up); }
        public void Init(Fighter f, Vector3 p, Vector3 n)
        {
            owner = f; landed = Time.time; transform.position = p; normal = n;
            var col = gameObject.AddComponent<SphereCollider>(); col.radius = transform.localScale.x < .9f ? .8f : .35f; // legacy scaled test primitive vs arrow
            lamp = new GameObject("信标灯").AddComponent<Light>(); lamp.transform.SetParent(transform, false);
            lamp.transform.position = p + n * .3f; lamp.type = LightType.Point; lamp.color = ScanColor; lamp.range = 5; lamp.intensity = 2;
            Sfx.Play(Skills.Food ? "food_squish" : "computerNoise", p, .8f, 1.3f, 1f, 35f);
        }
        public void InitPot(Fighter f, Vector3 p, Vector3 n, Component h)
        {
            Init(f, p, n); pot = h;
            fog = Links.MustardFog(h.transform.position, Links.PotRadius(h));
            if (Skills.Food) V24.MustardMushroom(h.transform.position, Links.PotRadius(h));   // V24：锅里"轰"地冲起绿色蘑菇云
            lamp.range = 9; lamp.intensity = 4;
            Links.Trigger(LinkKind.MustardPot, f, h.transform.position + Vector3.up, (h is KitchenStation ? "回血炉 " : "") + Links.PotRadius(h).ToString("0") + " m 芥末呛雾 " + Config.MustardPotLife.ToString("0") + " 秒 · 雾里的人透视 + 变慢");
        }
        void OnDestroy() { if (fog != null) Destroy(fog); }
        void PotUpdate(float age)
        {
            if (age >= Config.MustardPotLife || owner == null) { Destroy(gameObject); return; }
            if (SteamCloud.SuppressedAt(pot.transform.position)) { if (Match.I != null) Match.I.Feed("冰淇淋吹散了热气 · 芥末辣锅一起散了", new Color(.7f, .95f, 1f)); Destroy(gameObject); return; }   // 反制：冰淇淋形态打火锅
            if (Time.time < potTick) return;
            potTick = Time.time + .2f;
            Vector3 c = pot.transform.position;
            bool sugared = Links.EnemyNoiseCovers(c, owner.team);   // 优先级：棉花糖迷雾罩住火锅 → 辣雾失效（糖味盖芥末）
            bool cough = Time.time >= coughAt;
            foreach (var e in Match.I.fighters)
            {
                if (!e.alive || e.team == owner.team) continue;
                Vector3 d = e.transform.position - c; if (Mathf.Abs(d.y) > 3.5f) continue; d.y = 0;
                if (d.magnitude > PotRadiusNow) continue;
                if (sugared || Links.EnemyNoiseCovers(e.transform.position, owner.team) || Time.time < e.hiddenFromScanUntil) { NoiseField.CreditBlock(e); continue; }   // 糖味盖芥末
                e.revealedUntil = Mathf.Max(e.revealedUntil, Time.time + Config.MustardPotLinger + .2f);   // 不需要视线：芥末味穿透蒸汽
                e.mustardUntil = Mathf.Max(e.mustardUntil, Time.time + .3f);                               // V23 d：减速 ×MustardSlow、冲刺耗体力 ×MustardSprintDrain
                if (potSeen.Add(e)) { potFound++; if (potFound == 1) owner.stats.skillEffective++; }
                if (cough) Sfx.Play("food_squish", e.Chest, .55f, 1.9f, 1f, 15f);   // 咳嗽声 15 m
                if (e.human) FoodTips.Trigger("enemy_pot");
            }
            if (cough) coughAt = Time.time + 1.2f;
        }
        void Update()
        {
            float age = Time.time - landed;
            if (pot != null) { if (Time.time >= nextBeep) { nextBeep = Time.time + .45f; lamp.intensity = 6; } lamp.intensity = Mathf.MoveTowards(lamp.intensity, 3, Time.deltaTime * 10); PotUpdate(age); return; }
            if (Time.time >= nextBeep) { nextBeep = Time.time + .45f; Sfx.Play("tick", transform.position, .8f, 1.6f, 1f, 30f); lamp.intensity = 4; }
            lamp.intensity = Mathf.MoveTowards(lamp.intensity, 1, Time.deltaTime * 10);
            if (!scanned && age >= Config.DroneScanDelay) Scan();
            if (scanned && !scanned2 && age >= Config.DroneScanDelay + Config.DroneSecondPulse) { scanned2 = true; scanned = false; Scan(); scanned = true; }
            if (pulse != null)
            {
                float k = Mathf.Clamp01((age - Config.DroneScanDelay) / .5f);
                if (Skills.Food) Skills.WavyCircle(pulse, transform.position, Config.DroneRadius * k, age); else Skills.Circle(pulse, transform.position, Config.DroneRadius * k);   // V23：芥末味的波浪烟环
                var c = pulse.startColor; c.a = 1 - k * .7f; pulse.startColor = pulse.endColor = c;
            }
            if (age >= Config.DroneLife) Destroy(gameObject);
        }
        void Scan()
        {
            scanned = true; int found = 0;
            if (Skills.Food) Sfx.Play("food_sneeze", transform.position, 1f, 1f, 1f, 45f);   // V23：吸鼻子 → 啊嚏！（45 m 不变）
            else Sfx.Play("maximize", transform.position, 1f, .8f, 1f, 45f);
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(sphere.GetComponent<Collider>());
            sphere.name = Skills.Food ? "芥末气味波" : "扫描球"; sphere.transform.position = transform.position;
            var sm = new Material(Resources.Load<Material>("Materials/Holo") ?? Fx.Additive); sm.SetColor("_Color", ScanColor * 1.2f);
            var ss = sphere.AddComponent<ScanSphere>(); ss.radius = Config.DroneRadius; ss.color = ScanColor;
            sphere.GetComponent<Renderer>().sharedMaterial = sm;
            Fx.Flash(transform.position + Vector3.up, ScanColor, 6f, 14f, .5f);
            pulse = new GameObject("扫描脉冲").AddComponent<LineRenderer>();
            pulse.transform.SetParent(transform); pulse.material = Fx.Additive; pulse.startWidth = pulse.endWidth = .08f;
            pulse.startColor = pulse.endColor = Skills.Food ? Skills.Mustard : new Color(.7f, .55f, 1f);
            foreach (var e in Match.I.fighters)
            {
                if (!e.alive || e.team == owner.team) continue;
                if (Vector3.Distance(e.Chest, transform.position) > Config.DroneRadius) continue;
                if (!Skills.SightClear(transform.position + normal * .3f, e.Chest, e)) continue;
                if (Time.time < e.hiddenFromScanUntil) { NoiseField.CreditBlock(e); continue; }
                e.revealedUntil = Time.time + Config.DroneRevealTime; found++;
                if (Skills.Food)
                {
                    Fx.Burst(e.Chest + Vector3.up * .9f, Vector3.up, Skills.Mustard, 12, 2f, .06f, .5f, -.2f);   // 被呛到
                    Fx.Burst(e.Eye, Vector3.down, new Color(.6f, .85f, 1f), 6, 1f, .04f, .5f, 1.2f);            // 小眼泪
                    SkillPop.World(e.Eye + Vector3.up * .7f, "呛！", Skills.Mustard, e.transform);
                }
                if (e.human) FoodTips.Trigger("enemy_mustard");
            }
            if (found > 0 && Skills.Food) SkillPop.Show(owner, "呛到 " + found + " 人！", Skills.Mustard);
            if (found > 0) { owner.stats.skillEffective++; Match.I.Feed(Skills.Food ? Config.TeamName(owner.team) + " 夜鸦 的芥末签呛到了 " + found + " 名敌人" : Config.TeamName(owner.team) + " 夜鸦 扫描到 " + found + " 名敌人"); }
        }
        public bool TakeDamage(float amount, Fighter source, bool explosive)
        {
            if (source != null && source.team == owner.team) return false;
            Fx.Burst(transform.position, Vector3.up, ScanColor, 20, 4, .06f, .5f);
            Sfx.Play(Skills.Food ? "food_bonk" : "impactMetal", transform.position, .8f, 1.5f);
            if (Match.I != null) Match.I.Feed(pot != null ? "芥末签被打断了 · 芥末辣锅散了" : Skills.Food ? "芥末签被打断了" : "侦测箭信标被击毁");
            Destroy(gameObject);
            return true;
        }
    }

    // V23：签子钉入后尾部抖 3 下（0.36 s）
    public class Quiver : MonoBehaviour
    {
        float t; Quaternion baseRot;
        void Start() { baseRot = transform.rotation; }
        void Update()
        {
            t += Time.deltaTime; float k = t / .36f;
            if (k >= 1f) { transform.rotation = baseRot; Destroy(this); return; }
            transform.rotation = baseRot * Quaternion.Euler(Mathf.Sin(k * Mathf.PI * 6f) * 9f * (1 - k), 0, 0);
        }
    }

    public class ScanSphere : MonoBehaviour
    {
        public float radius = 9; public Color color = new Color(.6f, .45f, 1f); float t; Material m;
        void Start() { m = GetComponent<Renderer>().sharedMaterial; }
        void Update()
        {
            t += Time.deltaTime; float k = t / .6f;
            if (k >= 1) { Destroy(gameObject); return; }
            transform.localScale = Vector3.one * radius * 2 * Mathf.Sqrt(k);
            m.SetColor("_Color", color * 1.4f * (1 - k));
        }
    }

    // 焚芯 · 定向爆破：0.9 s 声光预警 → 角色伤害上限 35，对轻型掩体 ×2
    public class BlastCharge : MonoBehaviour
    {
        public Fighter owner; float armed; LineRenderer ring; Light lamp; float nextBeep; GameObject fill; bool sang;
        public WallPiece stuck;   // V22 第三期：粘在蛋糕墙上 → 开墙
        public static readonly List<BlastCharge> Live = new List<BlastCharge>();   // V21：敌方产物气泡用
        void OnEnable() { Live.Add(this); }
        void OnDisable() { Live.Remove(this); }
        void OnDestroy() { if (fill != null) Destroy(fill); }
        public void Init(Fighter f, Vector3 p) { Init(f, p, null); }
        public void Init(Fighter f, Vector3 p, WallPiece wall)
        {
            owner = f; armed = Time.time; transform.position = p; stuck = wall != null && wall.wall != null ? wall : null;
            if (stuck != null) { Fx.Burst(p, -stuck.transform.forward, Links.Gold, 16, 2f, .06f, .5f, 0); Sfx.Play("food_squish", p, .8f, .8f, 1f, 30f); }
            ring = new GameObject("爆破预警圈").AddComponent<LineRenderer>(); ring.transform.SetParent(transform);
            ring.material = Fx.Additive; ring.startWidth = ring.endWidth = .1f; ring.startColor = ring.endColor = new Color(1f, .2f, .1f);
            Skills.Circle(ring, Skills.GroundAt(p), Config.BlastRadius);
            lamp = new GameObject("预警灯").AddComponent<Light>(); lamp.transform.SetParent(transform, false);
            lamp.type = LightType.Point; lamp.color = new Color(1f, .2f, .1f); lamp.range = 8; lamp.intensity = 3;
            // V21：播报只写"纸杯"，不写"蛋糕爆炸"，免得和爆破目标生日蛋糕混淆；红色预警圈不变
            Match.I.Feed(Skills.Food ? "⚠ " + Config.TeamName(f.team) + " 焚芯 的点火纸杯 · 听到滋滋声快跑！" : "⚠ " + Config.TeamName(f.team) + " 焚芯 定向爆破预警！");
            if (Skills.Food && transform.Find("纸杯") == null) Skills.CupcakeLook(transform);
            var hp0 = Match.I.player; if (hp0 != null && hp0.team != f.team && Vector3.Distance(hp0.transform.position, p) < Config.BlastRadius + 3f) FoodTips.Trigger("enemy_cupcake");
            fill = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(fill.GetComponent<Collider>());
            fill.transform.position = Skills.GroundAt(p) + Vector3.up * .04f; fill.transform.localScale = new Vector3(.1f, .005f, .1f);
            var fm = new Material(Resources.Load<Material>("Materials/Holo") ?? Fx.Additive); fm.SetColor("_Color", new Color(1f, .2f, .1f) * .8f); fm.SetFloat("_Lines", 0);
            fill.GetComponent<Renderer>().sharedMaterial = fm;
        }
        void Update()
        {
            float age = Time.time - armed;
            // 引信"滋—滋—"：节奏 0.15 s、可听距离 35 m 与原蜂鸣相同
            if (Time.time >= nextBeep) { nextBeep = Time.time + .15f; Sfx.Play(Skills.Food ? "food_fuse" : "error", transform.position, 1f, Skills.Food ? 1f : 1.6f, 1f, 35f); }   // V23：新合成引信"滋—"，节奏 0.15 s / 35 m 不变
            if (Skills.Food) Burn(age / Config.BlastWarning);
            if (Skills.Food && !sang && age >= Config.BlastWarning - V24.BirthdaySongAt) { sang = true; V24.BirthdaySong(transform.position); }   // V24：快炸时唱一句生日歌（预警）
            bool on = Mathf.Repeat(age * 7f, 1f) < .5f;
            lamp.intensity = on ? 5 : .5f; ring.enabled = on || age > .6f;
            float fk = Mathf.Clamp01(age / Config.BlastWarning) * Config.BlastRadius * 2;
            fill.transform.localScale = new Vector3(fk, .005f, fk);
            if (age >= Config.BlastWarning) Explode();
        }
        Transform candle, flame; Vector3 candleS, candleP, flameP;
        // V23：蜡烛长度 = 剩余引信时间（0.9 s 烧完）
        void Burn(float k)
        {
            if (candle == null) { foreach (var t in GetComponentsInChildren<Transform>()) { if (t.name == "蜡烛") candle = t; if (t.name == "火苗") flame = t; } if (candle == null) return; candleS = candle.localScale; candleP = candle.localPosition; if (flame != null) flameP = flame.localPosition; }
            float left = Mathf.Clamp01(1f - k) * .85f + .15f;
            candle.localScale = new Vector3(candleS.x, candleS.y * left, candleS.z);
            candle.localPosition = candleP - new Vector3(0, candleS.y * (1 - left), 0);
            if (flame != null) { flame.localPosition = flameP - new Vector3(0, candleS.y * 2f * (1 - left), 0); flame.localScale = Vector3.one * .045f * (1f + .25f * Mathf.Sin(Time.time * 40f)); }
            if (Random.value < .4f && flame != null) Fx.Burst(flame.position, Vector3.up, new Color(1f, .7f, .25f), 1, .8f, .02f, .25f, -.2f);
        }
        public static string LastResult = "";
        void Explode()
        {
            Vector3 p = transform.position;
            bool hadHole = stuck != null && stuck.wall != null && stuck.wall.Opening() != null;   // V22 第三期：墙上已经有洞时再炸不算"开墙"联动（只按原规则炸）
            Fx.Explosion(p, Config.BlastRadius);
            if (Skills.Food)
            {
                // 火光 + 奶油 + 彩纸
                for (int i = 0; i < 6; i++) Fx.Burst(p + Vector3.up * .4f, Vector3.up, FoodFx.Confetti[i], 16, 7f, .08f, 1f, .8f);
                Fx.Burst(p + Vector3.up * .3f, Vector3.up, new Color(1f, .97f, .92f), 24, 5f, .12f, .7f, 1f);
                Sfx.Play("food_confetti", p, 1f, .9f, 1f, 50f);
                // V23：一圈奶油冲击波（白环）+ 奶油"噗" + 地面 / 墙面奶油贴花（≈ 6 s）
                Fx.Shockwave(p, Config.BlastRadius, new Color(1f, .97f, .92f), .5f);
                Sfx.Play("food_cream", p, 1f, .8f, 1f, 50f);
                for (int i = 0; i < 8; i++)
                {
                    Vector3 dir = Quaternion.Euler(0, i * 45f, 0) * Vector3.forward + Vector3.down * .35f;
                    if (Physics.Raycast(p + Vector3.up * .3f, dir, out RaycastHit ch, Config.BlastRadius, ~0, QueryTriggerInteraction.Ignore) && ch.collider.GetComponentInParent<Fighter>() == null)
                    { var q = FoodFx.Splat(ch.point, ch.normal, new Color(1f, .97f, .92f), Random.Range(.5f, .9f)); if (q != null) Destroy(q, 6f); }
                }
            }
            bool effective = false, hitPerson = false, hitCover = false;
            foreach (var e in Match.I.fighters)
            {
                if (!e.alive || e.team == owner.team) continue;
                float d = Vector3.Distance(e.Chest, p);
                if (d > Config.BlastRadius || !Skills.BlastClear(p + Vector3.up * .2f, e.Chest, e)) continue;   // V23：爆炸只看实体几何，蒸汽挡不住
                float dmg = Config.BlastMaxDamage * Mathf.Lerp(1f, .45f, d / Config.BlastRadius);
                DamageCtx.Set(DamageKind.Skill, owner.Hero.skill.Name); e.ReceiveDamage(dmg, owner, p, false, true); effective = true; hitPerson = true;
            }
            foreach (var c in Physics.OverlapSphere(p, Config.BlastRadius))
            {
                var piece = c.GetComponent<WallPiece>();
                if (piece != null) { if (Vector3.Distance(c.transform.position, p) < 2.3f) { piece.TakeDamage(999, owner, true); effective = true; hitCover = true; } continue; }
                var dest = c.GetComponentInParent<Destructible>();
                if (dest != null && dest.Alive) { dest.TakeDamage(Config.BlastCoverDamage * Config.BlastCoverMultiplier, owner, true); effective = true; hitCover = true; continue; }
                var cover = c.GetComponentInParent<DeployedCover>();
                if (cover != null && cover.owner.team != owner.team) { cover.TakeDamage(Config.BlastCoverDamage * Config.BlastCoverMultiplier, owner, true); effective = true; hitCover = true; continue; }
                var shield = c.GetComponentInParent<ShieldWall>();
                if (shield != null && shield.owner.team != owner.team) { shield.TakeDamage(Config.BlastCoverDamage * Config.BlastCoverMultiplier, owner, true); effective = true; hitCover = true; continue; }
                var ramp = c.GetComponentInParent<WaferRamp>();
                if (ramp != null && ramp.owner != null && ramp.owner.team != owner.team) { ramp.TakeDamage(Config.BlastCoverDamage * Config.BlastCoverMultiplier, owner, true); effective = true; hitCover = true; }
            }
            LinkPlus.OnBlast(p);
            if (Skills.Food) V24.FireAt(p, owner);   // V24：芥末雾碰到火 = 辣油爆燃
            bool sauceFire = LinkPlus.SauceIgnite(owner, p);   // V23 d 副联动：点酱（黄油着火时代替焦糖火）
            if (sauceFire) effective = true;
            if (stuck != null && stuck.wall != null && !hadHole)
            {
                // V22 第三期：开墙 —— 原 2.3 m 炸碎范围之外，再把落点正下方 ±BlastWallDoorHalf、地面到 max(BlastWallDoorHeight, 落点高) 的墙块全部炸开 → 必定是能过人的门洞
                var w = stuck.wall; Vector3 lp = w.transform.InverseTransformPoint(p);
                foreach (var q in w.pieces)
                {
                    if (!q.Alive) continue;
                    Vector3 ql = q.transform.localPosition;
                    if (Mathf.Abs(ql.x - lp.x) <= Config.BlastWallDoorHalf && ql.y - q.transform.localScale.y * .5f < Mathf.Max(Config.BlastWallDoorHeight, lp.y + .3f)) q.TakeDamage(999, owner, true);   // V23 d：底边低于门高的块都炸开 → 门洞 ≥ 3.8 × 2.6 m
                }
                effective = true;
                w.Invalidate(); var hole = w.Opening();
                if (!hadHole && hole != null)
                {
                    Links.Trigger(LinkKind.CakeBreach, owner, hole.center + Vector3.up * 1.2f, "蛋糕墙炸出一个两人并排的大门（" + hole.width.ToString("0.0") + " m 宽）· 蛋糕粉尘 " + Config.CakeDustLife.ToString("0.#") + " 秒");
                    SteamArea.Spawn(SteamKind.Dust, w, hole.center + Vector3.up * 1.2f, Config.CakeDustRadius, Config.CakeDustLife, owner);   // V23 d：炸开瞬间洞口扬起蛋糕粉尘（同一套视线判定）
                    if (Skills.Food) V24.CakeRubble(hole, owner);   // V24：碎蛋糕块留地上当临时掩体 20 s + 奶油滑地 4 s
                }
            }
            if (effective) owner.stats.skillEffective++;
            if (Skills.Food && (hitPerson || hitCover)) { LastResult = hitPerson ? "生日快乐！" : "烤酥了！"; SkillPop.Show(owner, LastResult, hitPerson ? new Color(1f, .5f, .65f) : Skills.Caramel); }   // V23：命中人 / 命中掩体
            if (!sauceFire) { var fire = new GameObject(Skills.Food ? "焦糖火" : "燃烧区"); fire.transform.position = Skills.GroundAt(p); fire.AddComponent<FireZone>().owner = owner; Skills.spawned.Add(fire); }
            Destroy(gameObject);
        }
    }

    // 焚芯爆破后的燃烧区：持续伤害敌人，迫使其离开点位
    public class FireZone : MonoBehaviour
    {
        public Fighter owner; float born, tick, bubbleAt; Light l; readonly List<Transform> bubbles = new List<Transform>();
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
            if (Skills.Food)
            {
                // V23：焦糖地面 —— 琥珀色、表面鼓泡、边缘结焦（不再是通用橙色地面火）
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .7f, .25f), new Color(.85f, .45f, .1f)); main.startSize = new ParticleSystem.MinMaxCurve(.15f, .45f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(.2f, .7f); em.rateOverTime = 70;
                g.SetKeys(new[] { new GradientColorKey(new Color(1f, .8f, .35f), 0), new GradientColorKey(new Color(.6f, .3f, .05f), 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) }); col.color = g;
                var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(disc.GetComponent<Collider>()); disc.name = "焦糖地面";
                disc.transform.SetParent(transform, false); disc.transform.localPosition = Vector3.up * .03f; disc.transform.localScale = new Vector3(Config.FireRadius * 2, .01f, Config.FireRadius * 2);
                var dm = new Material(Shader.Find("Sprites/Default")) { color = new Color(.85f, .5f, .12f, .55f) }; disc.GetComponent<Renderer>().sharedMaterial = dm; disc.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var edge = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(edge.GetComponent<Collider>()); edge.name = "焦边";
                edge.transform.SetParent(transform, false); edge.transform.localPosition = Vector3.up * .02f; edge.transform.localScale = new Vector3(Config.FireRadius * 2.12f, .008f, Config.FireRadius * 2.12f);
                edge.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = new Color(.3f, .15f, .05f, .7f) };
                for (int i = 0; i < 9; i++) { var b = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(b.GetComponent<Collider>()); b.name = "焦糖泡"; b.transform.SetParent(transform, false); Vector2 rp = Random.insideUnitCircle * Config.FireRadius * .8f; b.transform.localPosition = new Vector3(rp.x, .04f, rp.y); b.transform.localScale = Vector3.zero; b.GetComponent<Renderer>().sharedMaterial = Arena.Glow(new Color(1f, .72f, .3f), .8f); bubbles.Add(b.transform); }
            }
            l = new GameObject("火光").AddComponent<Light>(); l.transform.SetParent(transform, false); l.transform.localPosition = Vector3.up;
            l.color = new Color(1f, .5f, .15f); l.range = 9; l.intensity = 3;
            var ring = new GameObject("燃烧边界").AddComponent<LineRenderer>(); ring.transform.SetParent(transform);
            ring.material = Fx.Additive; ring.startWidth = ring.endWidth = .08f; ring.startColor = ring.endColor = Skills.Food ? new Color(1f, .65f, .2f) : new Color(1f, .4f, .1f);
            Skills.Circle(ring, transform.position, Config.FireRadius);
            Sfx.Play(Skills.Food ? "food_caramel_bubble" : "thrusterFire", transform.position, .8f, Skills.Food ? 1f : .6f, 1f, 35f);
        }
        void Update()
        {
            float age = Time.time - born;
            l.intensity = 2.5f + Mathf.PerlinNoise(Time.time * 8, 0) * 1.5f;
            if (age > Config.FireDuration) { Destroy(gameObject); return; }
            for (int i = 0; i < bubbles.Count; i++) { float bk = Mathf.Repeat(Time.time * .9f + i * .37f, 1f); bubbles[i].localScale = Vector3.one * (bk < .85f ? bk * .35f : 0f); }   // 鼓泡 → 啵地破
            if (Skills.Food && Time.time >= bubbleAt) { bubbleAt = Time.time + .6f; Sfx.Play("food_caramel_bubble", transform.position, .55f, Random.Range(.9f, 1.1f), 1f, 20f); }   // 咕嘟咕嘟（20 m）
            if (Time.time < tick) return;
            tick = Time.time + .25f;
            foreach (var f in Match.I.fighters)
            {
                if (!f.alive || owner == null || f.team == owner.team) continue;
                Vector3 d = f.transform.position - transform.position; d.y = 0;
                if (d.magnitude <= Config.FireRadius) { DamageCtx.Set(DamageKind.Fire, Skills.Food ? "焦糖火" : "燃烧区"); f.ReceiveDamage(Config.FireDps * .25f, owner, transform.position, false, true); f.revealedUntil = Mathf.Max(f.revealedUntil, Time.time + .3f); f.fireRevealUntil = Mathf.Max(f.fireRevealUntil, Time.time + .3f); }   // V22：单独记"火里暴露"，警告文案不再借用芥末
            }
        }
    }

    // 幻频 · 信号噪幕：区域内我方不可被侦测；区域内敌方小地图失效；不影响移动、开火、脚步
    public class NoiseField : MonoBehaviour
    {
        public Fighter owner; float born; Material mat; bool counted; float jamTime, popAt; Transform dome; Vector3 domeScale;
        public readonly HashSet<Object> candyDone = new HashSet<Object>(); float candyAt;   // V22 第三期：本团迷雾已经和哪些蒸汽源做过棉花糖蒸汽
        public float EndsAt { get { return born + Config.NoiseLife; } }
        static readonly Dictionary<Fighter, Fighter> protector = new Dictionary<Fighter, Fighter>();
        public static readonly List<NoiseField> Live = new List<NoiseField>();
        void OnEnable() { Live.Add(this); }
        void OnDisable() { Live.Remove(this); }
        static Color Base { get { return Skills.Food ? new Color(1f, .55f, .9f) : new Color(.7f, .25f, 1f); } }
        // V21：小地图按干扰来源显示文字 —— 敌方棉花糖迷雾罩住了这个人
        public static bool Covers(Fighter f)
        {
            foreach (var n in Live)
                if (n != null && n.owner != null && n.owner.team != f.team && new Vector2(f.transform.position.x - n.transform.position.x, f.transform.position.z - n.transform.position.z).magnitude <= Config.NoiseRadius) return true;
            return false;
        }
        public void Init(Fighter f)
        {
            owner = f; born = Time.time;
            var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(dome.GetComponent<Collider>());
            dome.name = Skills.Food ? "棉花糖云" : "噪幕穹顶"; dome.transform.SetParent(transform, false);
            dome.transform.localScale = new Vector3(Config.NoiseRadius * 2, Config.NoiseRadius * .9f, Config.NoiseRadius * 2);
            this.dome = dome.transform; domeScale = dome.transform.localScale; popAt = Time.time + .3f;
            // 半透明材质不变（不挡视线是规则的一部分），只换成粉紫色棉花糖
            mat = new Material(Resources.Load<Material>("Materials/Holo") ?? Fx.Additive);
            mat.SetColor("_Color", Base * .6f);
            dome.GetComponent<Renderer>().sharedMaterial = mat;
            dome.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var ring = new GameObject("噪幕边界").AddComponent<LineRenderer>(); ring.transform.SetParent(transform);
            ring.material = Fx.Additive; ring.startWidth = ring.endWidth = .1f; ring.startColor = ring.endColor = Skills.Food ? Skills.Marsh : new Color(.85f, .35f, 1f);
            Skills.Circle(ring, transform.position, Config.NoiseRadius);
            var font = Resources.Load<Font>("NotoSansSC");
            for (int i = 0; i < 14; i++)
            {
                var g = new GameObject("乱码"); g.transform.SetParent(transform, false);
                Vector2 rp = Random.insideUnitCircle * Config.NoiseRadius * .85f;
                g.transform.localPosition = new Vector3(rp.x, Random.Range(.6f, 3.5f), rp.y);
                var tm = g.AddComponent<TextMesh>(); tm.font = font; tm.fontSize = 64; tm.characterSize = Random.Range(.03f, .07f); tm.anchor = TextAnchor.MiddleCenter;
                tm.color = Skills.Food ? new Color(1f, .75f, .95f, .9f) : new Color(.85f, .4f, 1f, .85f); if (font != null) g.GetComponent<MeshRenderer>().sharedMaterial = Arena.WorldText(font);
                g.AddComponent<Skills.Glyph>();
            }
        }
        public static void CreditBlock(Fighter hidden)
        {
            if (protector.TryGetValue(hidden, out var p) && p != null) { p.stats.skillEffective++; protector.Remove(hidden); if (Skills.Food) SkillPop.Show(p, "糊住了！", Skills.Marsh); }
        }
        void Update()
        {
            float age = Time.time - born;
            mat.SetColor("_Color", Base * (.45f + .2f * Mathf.PerlinNoise(Time.time * 8f, 0)));
            bool anyJammed = false;
            foreach (var f in Match.I.fighters)
            {
                if (!f.alive) continue;
                if (new Vector2(f.transform.position.x - transform.position.x, f.transform.position.z - transform.position.z).magnitude > Config.NoiseRadius) continue;
                if (f.team == owner.team) { f.hiddenFromScanUntil = Time.time + .15f; if (f.revealedUntil > Time.time) { f.revealedUntil = 0; CreditOnce(); } protector[f] = owner; }
                else { f.jammedUntil = Time.time + .15f; anyJammed = true; if (f.human) { FoodTips.Trigger("enemy_marsh"); if (Skills.Food) SkillPop.EdgeSugar = 1f; } }   // V23：雾里敌人屏幕四角糊一层糖丝
            }
            if (anyJammed) { jamTime += Time.deltaTime; if (jamTime >= 1f) CreditOnce(); }
            if (Time.time >= candyAt) { candyAt = Time.time + .25f; Links.CandyCheck(this); LinkPlus.GumCheck(this); }   // V23 d：迷雾罩住守方监控 → 糊住   // V22 第三期：迷雾 ∩ 蒸汽源 → 棉花糖蒸汽
            if (Random.value < .3f) Fx.Burst(transform.position + new Vector3(Random.Range(-5f, 5f), Random.Range(.3f, 2.5f), Random.Range(-5f, 5f)), Vector3.up, Skills.Food ? FoodFx.Confetti[Random.Range(0, 6)] : new Color(.8f, .4f, 1f), 3, 1, .06f, .25f, 0);   // 飘糖粒
            if (Skills.Food && Time.time >= popAt) { popAt = Time.time + .7f; Sfx.Play("food_poprock_loop", transform.position, .45f, Random.Range(.95f, 1.05f), 1f, 45f); }   // V23：跳跳糖噼啪循环（45 m）
            if (Skills.Food && age < .35f && dome != null) dome.localScale = domeScale * (.4f + .6f * Mathf.Sin(Mathf.Clamp01(age / .35f) * Mathf.PI * .5f) + .12f * Mathf.Sin(age / .35f * Mathf.PI));   // "噗"地膨出
            if (age >= Config.NoiseLife) Destroy(gameObject);
        }
        void CreditOnce() { if (!counted) { counted = true; owner.stats.skillEffective++; if (Skills.Food) SkillPop.Show(owner, "糊住了！", Skills.Marsh); } }
    }
}
