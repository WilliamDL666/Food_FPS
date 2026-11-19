using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace NeonSiege
{
    // ================================================================ V25：奶茶机 / 变身表现 / 监控 + 干员加料 + 数据工具（Docs/优化意见_奶茶机_变身表现_监控.md + 用户 11-16 清单）
    // 拍板默认按推荐：奶茶机 = 甲（加料奶茶）；变身 = 全部做；监控 = 看得见 + 守方看监控 + 幻频接管；门洞保留现在的值；趴下造型保持甲（现状）。
    // 看监控用 5 键（C 是蹲下、V 是近战）。和 V24 一样：原文件只加一行 V25.Xxx(...) 钩子。
    public static class V25
    {
        // ---------------------------------------------------------------- 数值
        public static float BobaTime = 20f, BobaSlow = .9f, BobaSlowTime = 1.5f, BobaReload = .7f; public static int MaxGrenades = 2;   // 奶茶机 甲
        public static float HijackTime = 9f, CamViewMax = 10f, CamViewCooldown = 15f, CamSpotDot = 1.5f;                                 // 监控：接管 8–10 s；每次看最多 10 s、冷却 15 s
        public static float DomeBulletSlow = .15f, RadarTime = 5f;                                                                     // 果冻罩里敌方子弹 ×0.15；寿司雷达 5 s（= 规则时长）
        public static int PearlMax = 80;

        // ---------------------------------------------------------------- 遥测 / 测试读
        public static int Bobas, BobaSlows, FormShells, FormBreaks, Spots, Hijacks, HijackBreaks, CamViews, Pearls, PushOuts, CakeShadows, Radars;
        static void Log(string s) { if (Match.I != null && Match.I.autoplay) Debug.Log("NEON_V25 r" + Match.I.round + " " + s); }

        // ---------------------------------------------------------------- 布置
        public static void Dress(Arena arena)
        {
            V25Hud.Ensure(); V26Hud.Ensure();
            if (arena == null) return;
            // 恢复 2 个回血炉（V22 地图重构时注释掉了；现在按"贴墙 + 不压别的交互物"重新找位置）
            int heal = 0;
            foreach (var near in new[] { new Vector3(53, 0, -14), new Vector3(-36, 0, 33.6f) })
            {
                if (!V24.FindWallSpot(near, 5f, out Vector3 p, out Vector3 face)) { Debug.LogWarning("NEON_V25 heal skip " + near); continue; }
                var g = new GameObject("厨房 HotpotHeal（V25 恢复）"); g.transform.SetParent(V24.Root.transform, false); g.transform.position = p; g.transform.rotation = Quaternion.LookRotation(face);
                g.AddComponent<KitchenStation>().Build(KitchenStation.Kind.HotpotHeal); heal++;
            }
            foreach (var c in arena.cams) if (c != null) c.gameObject.AddComponent<CamFan>().cam = c;
            Debug.Log("NEON_V25 healPots+" + heal + " camFans=" + arena.cams.Count);
        }

        // ---------------------------------------------------------------- 奶茶机 甲：加料奶茶
        static readonly Dictionary<Fighter, float> bobaUntil = new Dictionary<Fighter, float>(), slowUntil = new Dictionary<Fighter, float>();
        public static void DrinkBoba(Fighter f)
        {
            if (f == null) return;
            Bobas++; bobaUntil[f] = Time.time + BobaTime;
            f.grenades = Mathf.Min(f.grenades + 1, MaxGrenades);
            Sfx.Play("food_boba", f.transform.position, .9f, .8f, f.human ? 0f : 1f, 15f); Fx.Burst(f.Chest, Vector3.up, new Color(.3f, .17f, .1f), 16, 2f, .08f, .6f, .6f);
            if (Skills.Food) SkillPop.Show(f, "加料奶茶！珍珠弹 " + BobaTime.ToString("0") + " 秒", new Color(.85f, .62f, .42f));
            if (f.human && Match.I != null) Match.I.Feed("加料奶茶：" + BobaTime.ToString("0") + " 秒珍珠弹（打中减速 10%）· 换弹快 30% · 手雷 " + f.grenades, new Color(.85f, .62f, .42f));
        }
        public static bool Boba(Fighter f) { return f != null && bobaUntil.TryGetValue(f, out float t) && Time.time < t && f.alive; }
        public static float BobaLeft(Fighter f) { return f != null && bobaUntil.TryGetValue(f, out float t) ? Mathf.Max(0, t - Time.time) : 0; }
        public static float ReloadMul(Fighter f) { return Boba(f) ? BobaReload : 1f; }
        public static void OnGunHit(Fighter a, Fighter v, Vector3 at)
        {
            if (!Boba(a) || v == null || !v.alive) return;
            if (!slowUntil.TryGetValue(v, out float t) || Time.time > t) BobaSlows++;
            slowUntil[v] = Time.time + BobaSlowTime;
            Fx.Burst(at, Vector3.up, new Color(.22f, .12f, .08f), 4, 2f, .1f, .6f, 1.5f);
        }
        public static float SpeedMul(Fighter f) { return f != null && slowUntil.TryGetValue(f, out float t) && Time.time < t ? BobaSlow : 1f; }

        // ---------------------------------------------------------------- 变身表现
        public static string FormSound(FoodForm fm)
        {
            switch (fm)
            {
                case FoodForm.Cake: return "food_confetti"; case FoodForm.MilkTea: return "food_boba"; case FoodForm.Spicy: return "food_caramel_bubble";
                case FoodForm.Burger: return "food_burger_chomp"; case FoodForm.IceCream: return "food_frost_wind"; case FoodForm.Onigiri: return "food_rice_thud";
                default: return "food_squish";
            }
        }
        public static void OnBecome(Fighter f, FoodForm fm)
        {
            if (f == null || fm == FoodForm.None || !Skills.Food) return;
            Sfx.Play(FormSound(fm), f.transform.position, 1f, 1f, f.human ? 0f : 1f, 35f);
            FormShell.Attach(f, fm); FormShells++;
            if (f.human)
            {
                V25Hud.FormPop(Ingredients.FormName(fm) + "！" + Ingredients.FormEffect(fm), Ingredients.FormColor(fm));
                f.ViewNudge(2f, 6f, .25f);   // 镜头一缩一弹
                FpGarnish.Attach(f, fm);
                V26.OnBecome(f, fm);   // V26：第一人称枪换皮肤
            }
        }
        public static void OnFormEnd(Fighter f, FoodForm fm, bool announce)
        {
            if (f == null || fm == FoodForm.None) return;
            var shell = f.GetComponent<FormShell>(); if (shell != null) { shell.Break(announce); FormBreaks++; }
            var g = f.GetComponent<FpGarnish>(); if (g != null) Object.Destroy(g);
            V26.OnFormEnd(f);
            if (f.human && announce && f.alive) V25Hud.FormPop("变身结束", new Color(.85f, .88f, .95f));
        }

        // ---------------------------------------------------------------- 监控：看得见 / 接管
        static readonly Dictionary<SecurityCam, int> hijackTeam = new Dictionary<SecurityCam, int>();
        static readonly Dictionary<SecurityCam, float> hijackUntil = new Dictionary<SecurityCam, float>();
        static readonly Dictionary<Fighter, float> spottedAt = new Dictionary<Fighter, float>();
        public static void Hijack(SecurityCam c, Fighter by)
        {
            if (c == null || by == null) return;
            hijackTeam[c] = by.team; hijackUntil[c] = Time.time + HijackTime; Hijacks++;
            if (Match.I != null) Match.I.Feed(Config.TeamName(by.team) + " " + by.Hero.name + " 用糖丝接管了守方监控 " + HijackTime.ToString("0") + " 秒 · 反过来偷看守方" + (by.human ? "（按 5 看）" : ""), new Color(1f, .6f, .9f));
            Log("hijack " + by.Hero.name);
        }
        public static int HijackedBy(SecurityCam c) { return c != null && hijackUntil.TryGetValue(c, out float t) && Time.time < t && c.Working ? hijackTeam[c] : -1; }
        public static float HijackLeft(SecurityCam c) { return c != null && hijackUntil.TryGetValue(c, out float t) ? Mathf.Max(0, t - Time.time) : 0; }
        public static void EndHijack(SecurityCam c, Fighter by)
        {
            hijackUntil.Remove(c); hijackTeam.Remove(c); HijackBreaks++;
            if (c.Head != null) { Fx.Burst(c.Head.position, Vector3.down, new Color(1f, .6f, .9f), 30, 3f, .06f, .7f, .8f); Sfx.Play("food_marsh_puff", c.Head.position, 1f, 1.2f, 1f, 30f); }
            if (Match.I != null) Match.I.Feed((by != null ? by.Hero.name + " " : "") + "打掉了监控上的糖丝 · 监控回到守方手里", new Color(.6f, .85f, 1f));
        }
        // 被接管的监控：扫守方，给攻方透视（和平时扫攻方同一套范围 / 角度 / 视线）
        public static void HijackScan(SecurityCam c, Transform head)
        {
            var m = Match.I; if (m == null || head == null || !m.CanAct) return;
            int team = HijackedBy(c);
            if (Random.value < .1f) Fx.Burst(head.position, Vector3.down, new Color(1f, .6f, .9f), 2, .6f, .05f, .9f, .2f);   // 一直冒粉色糖丝
            foreach (var f in m.fighters)
            {
                if (!f.alive || f.team == team) continue;
                Vector3 to = f.Chest - head.position;
                if (to.magnitude > 28f || Vector3.Angle(head.forward, to) > 35f || Time.time < f.hiddenFromScanUntil) continue;
                if (Skills.SightClear(head.position, f.Chest, f)) { f.revealedUntil = Mathf.Max(f.revealedUntil, Time.time + .6f); CamSpotted(f, c); }
            }
        }
        public static void CamSpotted(Fighter f, SecurityCam c)
        {
            if (f == null) return;
            if (!spottedAt.TryGetValue(f, out float t) || Time.time - t > 1f) Spots++;
            spottedAt[f] = Time.time;
            if (f.human) V25Hud.Spotted();
        }
        public static bool SpottedRecently(Fighter f, float secs) { return f != null && spottedAt.TryGetValue(f, out float t) && Time.time - t < secs; }

        // ---------------------------------------------------------------- 干员加料
        static readonly Dictionary<ShieldWall, int> pearlCount = new Dictionary<ShieldWall, int>();
        public static void CurtainPearl(ShieldWall s, Fighter src)
        {
            if (s == null || src == null) return;
            pearlCount.TryGetValue(s, out int n); if (n >= PearlMax) return; pearlCount[s] = n + 1; Pearls++;
            Vector3 o = src.Eye, d = src.AimDir, p0 = s.transform.position, nrm = s.transform.forward;
            float den = Vector3.Dot(d, nrm); Vector3 hit = p0 + Vector3.up * s.Height * .5f;
            if (Mathf.Abs(den) > 1e-3f) { float t = Vector3.Dot(p0 - o, nrm) / den; if (t > 0) hit = o + d * t; }
            Vector3 l = s.transform.InverseTransformPoint(hit);
            l.x = Mathf.Clamp(l.x, -s.Width * .45f, s.Width * .45f); l.y = Mathf.Clamp(l.y, .2f, s.Height - .2f); l.z = Random.Range(-.05f, .05f);
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere); Object.Destroy(g.GetComponent<Collider>()); g.name = "陷进门帘的珍珠";
            g.transform.SetParent(s.transform, false); g.transform.localPosition = l;
            string id = src.W != null ? src.W.id : "";
            bool boba = id == "SMG-7", pop = id == "AR-9";
            g.transform.localScale = Vector3.one * (pop ? .13f : boba ? .1f : .08f);
            g.GetComponent<Renderer>().sharedMaterial = Arena.Glow(boba ? new Color(.22f, .12f, .08f) : pop ? new Color(1f, .95f, .75f) : FoodFx.For(src.W).splat, .4f);
            g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            g.AddComponent<Bob>().amp = .03f;
        }
        public static void CoverPushOut(Transform t, float w, float h, float thick)
        {
            var m = Match.I; if (m == null || t == null) return;
            foreach (var f in m.fighters)
            {
                if (!f.alive || f.cc == null) continue;
                Vector3 l = t.InverseTransformPoint(f.transform.position + Vector3.up * .9f);
                float hz = thick / 2f + .45f;
                if (Mathf.Abs(l.x) > w / 2f + .35f || l.y < -.9f || l.y > h + .9f || Mathf.Abs(l.z) > hz) continue;
                float side = l.z >= 0 ? 1f : -1f, need = hz - Mathf.Abs(l.z) + .1f;
                f.cc.enabled = false; f.transform.position += t.forward * side * need; f.cc.enabled = true;
                Fx.Burst(f.transform.position + Vector3.up * .5f, t.forward * side, Skills.Wafer, 6, 1.5f, .05f, .4f, 1f);
                PushOuts++;
            }
        }
        public static float BulletSlow(Bullet b)
        {
            if (b == null || b.owner == null || JellyDome.Live.Count == 0) return 1f;
            Vector3 p = b.transform.position;
            foreach (var d in JellyDome.Live)
                if (d != null && d.Owner != null && d.Owner.team != b.owner.team && (p - d.transform.position).sqrMagnitude < 25f) return DomeBulletSlow;
            return 1f;
        }
        public static void CakeShadow(Vector3 at)
        {
            var go = new GameObject("巨型蛋糕的影子"); go.transform.position = Skills.GroundAt(at + Vector3.up); go.AddComponent<CakeShadowFx>(); Skills.spawned.Add(go); CakeShadows++;
        }
        static readonly float[] radarUntil = new float[8];
        public static void OnUlt(Fighter f)
        {
            if (f == null || f.role != Role.Raven || f.team < 0 || f.team >= radarUntil.Length) return;
            radarUntil[f.team] = Time.time + RadarTime; Radars++;
        }
        public static bool RadarFor(int team) { return team >= 0 && team < radarUntil.Length && Time.time < radarUntil[team]; }

        // ---------------------------------------------------------------- 击杀表 / 回合战报
        public class KillRow { public int round; public string killer, victim, weapon, part; public float dist, ttk; public bool head; public int killerTeam = -1, victimTeam; }
        public static readonly List<KillRow> Kills = new List<KillRow>();
        static int lastRound = -1;
        public static string PartName(HitPart p) { switch (p) { case HitPart.Head: return "头"; case HitPart.Chest: return "胸"; case HitPart.Stomach: return "腹"; case HitPart.Arm: return "手臂"; default: return "腿"; } }
        // V26：没有命中部位的击杀（爆炸 / 火 / 近战 / 技能 / 环境）按伤害类型写名字
        public static string KindPart(DamageKind k, string cause)
        {
            switch (k)
            {
                case DamageKind.Bomb: case DamageKind.Grenade: return "爆炸";
                case DamageKind.Fire: return "烫伤";
                case DamageKind.Melee: return "近战";
                case DamageKind.Skill: case DamageKind.Ult: return "技能";
                case DamageKind.Food: return "食物";
                case DamageKind.Env: return "环境";
                default: return !string.IsNullOrEmpty(cause) && cause.Contains("爆") ? "爆炸" : "身体";
            }
        }
        public static void OnKill(FeedItem it, Fighter victim, Fighter killer, bool head)
        {
            var m = Match.I; if (m == null || victim == null) return;
            if (m.round < lastRound) Kills.Clear();   // 新的一场
            lastRound = m.round;
            var row = new KillRow { round = m.round, killer = killer != null ? killer.Hero.name : "环境", victim = victim.Hero.name, head = head, killerTeam = killer != null ? killer.team : -1, victimTeam = victim.team };
            row.weapon = it != null && it.gun != null ? it.gun.id : it != null && !string.IsNullOrEmpty(it.cause) ? it.cause : "-";
            row.part = KindPart(it != null ? it.kind : DamageKind.Env, row.weapon); row.dist = killer != null ? Vector3.Distance(killer.transform.position, victim.transform.position) : 0;
            var log = victim.damageLog;
            if (log.Count > 0)
            {
                var last = log[log.Count - 1];
                row.part = last.hasPart ? PartName(last.part) : KindPart(last.kind, last.weapon);   // V26：非子弹伤害也写清楚，不留 "-"
                if (last.distance > .1f) row.dist = last.distance;
                float first = last.time;
                for (int i = log.Count - 1; i >= 0; i--)
                {
                    var r = log[i]; if (r.source != killer || first - r.time > 3f) break;
                    first = r.time;
                }
                row.ttk = Mathf.Max(0, last.time - first);
            }
            Kills.Add(row);
            if (m.autoplay) Debug.Log("NEON_KILL r=" + row.round + " killer=" + row.killer + " kteam=" + row.killerTeam + " victim=" + row.victim + " weapon=" + row.weapon + " part=" + row.part + " dist=" + row.dist.ToString("0.0") + " ttk=" + row.ttk.ToString("0.00") + " head=" + (head ? 1 : 0));
        }
        public static void OnRoundEnd(int winner, string reason)
        {
            var m = Match.I; if (m == null) return;
            if (m.autoplay) Debug.Log("NEON_ROUNDRESULT r=" + m.round + " att=" + m.AttackingTeam + " winner=" + winner + " reason=" + reason);
            V25Hud.ShowReport(m.round);
        }
        public static void AppendKillTable(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("kills,round,killer,victim,weapon,part,distance_m,ttk_s,headshot");
            foreach (var k in Kills) sb.AppendLine(string.Join(",", "kill", k.round, k.killer, k.victim, k.weapon, k.part, k.dist.ToString("0.0"), k.ttk.ToString("0.00"), k.head ? 1 : 0));
        }

        // ---------------------------------------------------------------- 地图叠加层（MapUI 在画完人之后调）
        public static void MapOverlay(MapView v, Fighter p, bool big)
        {
            var m = Match.I; if (m == null || p == null) return;
            float pulse = .5f + .5f * Mathf.Sin(Time.time * 8f);
            foreach (var f in m.fighters)
            {
                if (!f.alive) continue;
                bool ally = f.team == p.team;
                Vector2 a = v.Map(f.transform.position); if (!v.Inside(a, 4)) continue;
                // 守方：被监控扫到的攻方 → 红点（1.5 s）
                if (!ally && p.team == m.DefendingTeam && SpottedRecently(f, CamSpotDot)) { MapGL.Disc(a, (big ? 9f : 6f) + 2f * pulse, new Color(1f, .15f, .15f, .85f)); MapGL.Ring(a, (big ? 13f : 9f) + 3f * pulse, 1.5f, new Color(1f, .3f, .3f, .9f)); }
                // 变身中：菜色光圈（自己 / 队友一直画；敌人只在透视时画）
                if (ToonAssets.Available && f.Food.Transformed && (ally || f.Revealed)) MapGL.Ring(a, big ? 15f : 11f, 2.5f, Ingredients.FormColor(f.Food.form));
            }
            // 寿司雷达：小地图边缘一圈回转寿司传送带；被扫到的敌人画成一盘寿司
            if (RadarFor(p.team) && ToonAssets.Available)
            {
                if (v.clipRadius > 0)
                {
                    MapGL.Ring(v.center, v.clipRadius - 4f, 7f, new Color(.25f, .18f, .12f, .85f));
                    for (int i = 0; i < 14; i++)
                    {
                        Vector2 q = CombatHud.ArcPoint(v.center, v.clipRadius - 4f, i * (360f / 14f) + Time.time * 60f);
                        MapGL.Disc(q, 4.2f, i % 2 == 0 ? new Color(1f, .3f, .35f) : new Color(.3f, .55f, 1f), 14);
                        MapGL.Disc(q, 2.2f, new Color(1f, .97f, .9f), 10);
                    }
                }
                foreach (var e in m.fighters)
                {
                    if (!e.alive || e.team == p.team) continue;
                    Vector2 a = v.Clamp(v.Map(e.transform.position), 8);
                    float r = big ? 11f : 7f;
                    MapGL.Disc(a, r, new Color(1f, .3f, .35f, .95f), 16); MapGL.Disc(a, r * .6f, new Color(1f, .97f, .9f), 12); MapGL.Disc(a + new Vector2(0, -r * .15f), r * .35f, new Color(1f, .5f, .25f), 10);
                }
            }
        }
    }

    // 轻轻上下浮（门帘里的珍珠）
    public class Bob : MonoBehaviour
    {
        public float amp = .03f; Vector3 b; float ph;
        void Start() { b = transform.localPosition; ph = Random.value * 6f; }
        void Update() { transform.localPosition = b + Vector3.up * Mathf.Sin(Time.time * 2.5f + ph) * amp; }
    }

    // ================================================================ 变身外壳（别人看）：汉堡上下两片面包、奶茶套成杯子、冰淇淋甜筒……；结束时"啪"地碎掉
    public class FormShell : MonoBehaviour
    {
        Fighter f; FoodForm fm; Transform root; float born;
        public static void Attach(Fighter f, FoodForm fm)
        {
            var old = f.GetComponent<FormShell>(); if (old != null) { old.Clear(); Destroy(old); }
            var s = f.gameObject.AddComponent<FormShell>(); s.f = f; s.fm = fm; s.born = Time.time; s.Build();
        }
        Transform P(PrimitiveType t, Vector3 lp, Vector3 sc, Color c, float glow = .5f, float alpha = 1f)
        {
            var g = GameObject.CreatePrimitive(t); Destroy(g.GetComponent<Collider>()); g.transform.SetParent(root, false); g.transform.localPosition = lp; g.transform.localScale = sc;
            var r = g.GetComponent<Renderer>();
            r.sharedMaterial = alpha < 1f ? new Material(Shader.Find("Sprites/Default")) { color = new Color(c.r, c.g, c.b, alpha), renderQueue = 3050 } : Arena.Glow(c, glow);
            r.shadowCastingMode = f.human ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.Off;   // 自己的第一人称不挡视线
            return g.transform;
        }
        void Build()
        {
            root = new GameObject("变身外壳").transform; root.position = f.transform.position;
            Color c = Ingredients.FormColor(fm);
            switch (fm)
            {
                case FoodForm.Burger:
                    P(PrimitiveType.Sphere, new Vector3(0, 2.05f, 0), new Vector3(1.3f, .55f, 1.3f), UltFx.BunHot, .8f); P(PrimitiveType.Sphere, new Vector3(0, .12f, 0), new Vector3(1.3f, .4f, 1.3f), UltFx.BunHot, .8f);
                    P(PrimitiveType.Cylinder, new Vector3(0, 1.82f, 0), new Vector3(1.45f, .03f, 1.45f), new Color(.35f, .95f, .3f), 1.2f); P(PrimitiveType.Cube, new Vector3(0, 1.86f, 0), new Vector3(1.05f, .05f, 1.05f), new Color(1f, .85f, .15f), 1.2f).localRotation = Quaternion.Euler(0, 45, 0);
                    break;
                case FoodForm.MilkTea:
                    P(PrimitiveType.Cylinder, new Vector3(0, .95f, 0), new Vector3(1.05f, 1f, 1.05f), new Color(.85f, .65f, .45f), 0, .38f);
                    P(PrimitiveType.Cylinder, new Vector3(0, 1.98f, 0), new Vector3(1.15f, .04f, 1.15f), new Color(1f, .95f, .9f), .6f);
                    P(PrimitiveType.Cylinder, new Vector3(.25f, 2.4f, 0), new Vector3(.08f, .5f, .08f), new Color(1f, .4f, .6f), 1f);
                    for (int i = 0; i < 6; i++) P(PrimitiveType.Sphere, new Vector3(Random.Range(-.35f, .35f), .2f, Random.Range(-.35f, .35f)), Vector3.one * .16f, new Color(.2f, .12f, .08f), .2f);
                    break;
                case FoodForm.IceCream:
                    P(PrimitiveType.Cylinder, new Vector3(0, .55f, 0), new Vector3(.75f, .55f, .75f), new Color(.85f, .62f, .35f), .5f);
                    P(PrimitiveType.Sphere, new Vector3(0, 1.75f, 0), new Vector3(1.25f, 1.05f, 1.25f), new Color(.75f, .93f, 1f), 0, .45f);
                    break;
                case FoodForm.Cake:
                    P(PrimitiveType.Cylinder, new Vector3(0, .5f, 0), new Vector3(1.4f, .5f, 1.4f), new Color(1f, .7f, .85f), 0, .45f);
                    P(PrimitiveType.Cylinder, new Vector3(0, 1.4f, 0), new Vector3(1.1f, .4f, 1.1f), new Color(1f, .95f, .92f), 0, .45f);
                    P(PrimitiveType.Cylinder, new Vector3(0, 2.05f, 0), new Vector3(.08f, .25f, .08f), new Color(1f, .8f, .3f), 2f);
                    break;
                case FoodForm.Onigiri:
                    P(PrimitiveType.Sphere, new Vector3(0, 1f, 0), new Vector3(1.25f, 1.45f, .9f), new Color(1f, .98f, .92f), 0, .4f);
                    P(PrimitiveType.Cube, new Vector3(0, .45f, 0), new Vector3(.9f, .55f, .95f), new Color(.1f, .3f, .15f), .2f);
                    break;
                case FoodForm.Spicy:
                    P(PrimitiveType.Cylinder, new Vector3(0, .35f, 0), new Vector3(1.35f, .35f, 1.35f), new Color(.85f, .2f, .1f), .6f);
                    for (int i = 0; i < 5; i++) P(PrimitiveType.Capsule, new Vector3(Mathf.Cos(i * 1.26f) * .55f, .85f, Mathf.Sin(i * 1.26f) * .55f), new Vector3(.1f, .2f, .1f), new Color(1f, .15f, .1f), 1.5f);
                    break;
                default:
                    P(PrimitiveType.Sphere, new Vector3(0, 1f, 0), new Vector3(1.3f, 1.8f, 1.3f), new Color(.25f, .15f, .2f), 0, .45f);
                    break;
            }
            root.localScale = Vector3.one * .3f;
        }
        void LateUpdate()
        {
            if (root == null) return;
            if (f == null || !f.alive) { Clear(); Destroy(this); return; }
            float age = Time.time - born, pop = age < .25f ? Mathf.Lerp(.3f, 1.12f, age / .25f) : Mathf.Lerp(1.12f, 1f, Mathf.Clamp01((age - .25f) / .15f));
            float k = f.avatar != null ? f.avatar.Body.localScale.y : 1f;
            root.position = f.transform.position; root.rotation = Quaternion.Euler(0, f.yaw, 0);
            root.localScale = Vector3.one * pop * Mathf.Max(.6f, k) * (1f + .02f * Mathf.Sin(Time.time * 6f));
        }
        public void Break(bool loud)
        {
            if (root != null)
            {
                Vector3 c = root.position + Vector3.up * 1.1f; Color col = Ingredients.FormColor(fm);
                Fx.Burst(c, Vector3.up, col, 30, 5f, .12f, .7f, 1.2f); Fx.Burst(c, Vector3.up, Color.white, 12, 3f, .06f, .5f, .8f);
                if (loud) Sfx.Play("food_wafer_shatter", c, .7f, 1.4f, f != null && f.human ? 0f : 1f, 30f);
            }
            Clear(); Destroy(this);
        }
        void Clear() { if (root != null) Destroy(root.gameObject); root = null; }
        void OnDestroy() { Clear(); }
    }

    // 第一人称：手边冒对应菜色的糖粒 / 冰淇淋结霜
    public class FpGarnish : MonoBehaviour
    {
        ParticleSystem ps;
        public static void Attach(Fighter f, FoodForm fm)
        {
            if (f.cam == null) return;
            var old = f.GetComponent<FpGarnish>(); if (old != null) Destroy(old);
            var g = f.gameObject.AddComponent<FpGarnish>();
            var go = new GameObject("变身手边装饰"); go.transform.SetParent(f.cam.transform, false); go.transform.localPosition = new Vector3(.22f, -.2f, .55f);
            var ps = go.AddComponent<ParticleSystem>(); var main = ps.main; main.startLifetime = new ParticleSystem.MinMaxCurve(.4f, .8f); main.startSpeed = new ParticleSystem.MinMaxCurve(.05f, .25f);
            main.startSize = new ParticleSystem.MinMaxCurve(.006f, .016f); Color c = fm == FoodForm.IceCream ? new Color(.85f, .97f, 1f) : Ingredients.FormColor(fm);
            main.startColor = new ParticleSystem.MinMaxGradient(c, Color.white); main.maxParticles = 80; main.simulationSpace = ParticleSystemSimulationSpace.Local; main.gravityModifier = fm == FoodForm.IceCream ? .02f : .1f;
            var em = ps.emission; em.rateOverTime = 30; var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(.2f, .06f, .35f);
            var r = go.GetComponent<ParticleSystemRenderer>(); r.material = Fx.Additive; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            g.ps = ps;
        }
        void OnDestroy() { if (ps != null) Destroy(ps.gameObject); }
    }

    // ================================================================ 监控扇形（地上画出它扫的范围；被接管变粉；坏了 / 被糊住不画）
    public class CamFan : MonoBehaviour
    {
        public SecurityCam cam; Transform fan; Material mat; MeshFilter mf;
        public Transform Fan { get { return fan; } } public Color Col { get { return mat != null ? mat.color : Color.clear; } }   // 测试 / 截图读
        const float Range = 20f, Half = 35f; const int Seg = 18;
        void Start()
        {
            fan = new GameObject("监控扇形").transform;
            mf = fan.gameObject.AddComponent<MeshFilter>(); var mr = fan.gameObject.AddComponent<MeshRenderer>();
            mat = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, .2f, .2f, .14f), renderQueue = 3001 }; mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            var mesh = new Mesh { name = "扇形" }; var vs = new Vector3[Seg + 2]; var tri = new int[Seg * 3];
            vs[0] = Vector3.zero;
            for (int i = 0; i <= Seg; i++) { float a = (-Half + 2 * Half * i / Seg) * Mathf.Deg2Rad; vs[i + 1] = new Vector3(Mathf.Sin(a) * Range, 0, Mathf.Cos(a) * Range); }
            for (int i = 0; i < Seg; i++) { tri[i * 3] = 0; tri[i * 3 + 1] = i + 1; tri[i * 3 + 2] = i + 2; }
            mesh.vertices = vs; mesh.triangles = tri; mesh.RecalculateNormals(); mf.sharedMesh = mesh;
            // 描边：地面是粉紫色，只靠半透明填充看不出来（截图验证过）→ 加一圈不透明的亮色边
            edge = fan.gameObject.AddComponent<LineRenderer>(); edge.useWorldSpace = false; edge.loop = true; edge.positionCount = vs.Length;
            for (int i = 0; i < vs.Length; i++) edge.SetPosition(i, vs[i] + Vector3.up * .02f);
            edge.startWidth = edge.endWidth = .14f; edgeMat = new Material(Shader.Find("Sprites/Default")) { renderQueue = 3002 }; edge.sharedMaterial = edgeMat;
            edge.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; edge.receiveShadows = false;   // 默认朝向镜头，任何角度都看得见
            // V26：扫描线 —— 一条亮线在扇形里来回扫（雷达那样），远处也看得出"这里有监控在看"
            var sg = new GameObject("扫描线"); sg.transform.SetParent(fan, false);
            sweep = sg.AddComponent<LineRenderer>(); sweep.useWorldSpace = false; sweep.positionCount = 2; sweep.startWidth = .22f; sweep.endWidth = .1f;
            sweep.sharedMaterial = edgeMat; sweep.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; sweep.receiveShadows = false;
        }
        LineRenderer edge, sweep; Material edgeMat;
        public float SweepAngle { get; private set; }
        void OnDestroy() { if (fan != null) Destroy(fan.gameObject); if (mat != null) Destroy(mat); if (edgeMat != null) Destroy(edgeMat); }
        void LateUpdate()
        {
            if (fan == null || cam == null) return;
            bool hij = V25.HijackedBy(cam) >= 0, show = cam.Working && (hij || !LinkPlus.Gummed(cam)) && Skills.Food;
            fan.gameObject.SetActive(show); if (!show) return;
            Vector3 p = cam.transform.position;
            if (Physics.Raycast(p, Vector3.down, out RaycastHit g, 12f, ~0, QueryTriggerInteraction.Ignore)) p = g.point + Vector3.up * .04f; else p.y = .04f;
            fan.position = p; fan.rotation = Quaternion.Euler(0, cam.transform.eulerAngles.y, 0);
            float k = .5f + .5f * Mathf.Sin(Time.time * 3f);
            mat.color = hij ? new Color(.7f, .1f, .9f, .42f + .1f * k) : new Color(1f, .08f, .05f, .4f + .1f * k);   // V26：填充 .3 → .4（还是偏淡）   // 截图验证：粉色 .28 画在粉紫地面上看不出 → 守方红更深、被接管改深紫红
            if (edgeMat != null) edgeMat.color = hij ? new Color(.75f, .15f, 1f, .75f + .2f * k) : new Color(1f, .12f, .08f, .75f + .2f * k);
            if (sweep != null)
            {
                SweepAngle = Mathf.Sin(Time.time * 1.6f) * Half * .95f; float a = SweepAngle * Mathf.Deg2Rad;
                sweep.SetPosition(0, Vector3.up * .03f); sweep.SetPosition(1, new Vector3(Mathf.Sin(a) * Range, .03f, Mathf.Cos(a) * Range));
            }
        }
    }

    // ================================================================ 巨型蛋糕的影子：地上一团越来越大的影子 + 天上一个半透明大蛋糕慢慢落下再淡出
    public class CakeShadowFx : MonoBehaviour
    {
        float born; Transform disc, cake; Material dm; readonly List<Material> cms = new List<Material>();
        void Start()
        {
            born = Time.time;
            var d = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(d.GetComponent<Collider>()); d.name = "影子"; d.transform.SetParent(transform, false); d.transform.localPosition = Vector3.up * .05f; disc = d.transform;
            dm = new Material(Shader.Find("Sprites/Default")) { color = new Color(.15f, .05f, .08f, 0f), renderQueue = 3001 }; var r = d.GetComponent<Renderer>(); r.sharedMaterial = dm; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var m = Arena.Model("Toon/Food/BigCake");
            if (m != null)
            {
                foreach (var c in m.GetComponentsInChildren<Collider>()) Destroy(c);
                Arena.Fit(m, new Vector3(7f, 0, 7f)); cake = new GameObject("天上的蛋糕").transform; cake.SetParent(transform, false); m.transform.SetParent(cake, false); m.transform.localPosition = Vector3.zero;
                foreach (var rr in m.GetComponentsInChildren<Renderer>())
                {
                    var mm = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, .7f, .85f, 0f), renderQueue = 3002 }; rr.sharedMaterial = mm; cms.Add(mm);
                    rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                cake.localPosition = Vector3.up * 16f;
            }
            Sfx.Play("lowFrequency_explosion", transform.position + Vector3.up * 10f, .6f, .7f, 1f, 60f);
        }
        void Update()
        {
            float age = Time.time - born, k = Mathf.Clamp01(age / .6f), fade = Mathf.Clamp01((2.6f - age) / .6f);
            if (disc != null) disc.localScale = new Vector3(7f * Mathf.Lerp(.3f, 1f, k), .004f, 7f * Mathf.Lerp(.3f, 1f, k));
            if (dm != null) dm.color = new Color(.15f, .05f, .08f, .45f * k * fade);
            if (cake != null) { cake.localPosition = Vector3.up * Mathf.Lerp(16f, 11f, k); cake.Rotate(0, 20f * Time.deltaTime, 0); foreach (var mm in cms) mm.color = new Color(1f, .7f, .85f, .35f * k * fade); }
            if (age > 2.7f) Destroy(gameObject);
        }
        void OnDestroy() { if (dm != null) Destroy(dm); foreach (var mm in cms) if (mm != null) Destroy(mm); }
    }

    // ================================================================ 看监控（5 键；V 是近战）：守方看自家监控；攻方看被幻频接管的监控。每次最多 10 s，冷却 15 s
    public static class CamView
    {
        public static bool Active { get { return active; } }
        static bool active; static Camera vc; static int idx; static float until, readyAt, startedAt; static readonly List<SecurityCam> list = new List<SecurityCam>();
        public static int Index { get { return idx; } } public static int Count { get { return list.Count; } }
        public static float Left { get { return Mathf.Max(0, until - Time.time); } } public static float CooldownLeft { get { return Mathf.Max(0, readyAt - Time.time); } }
        public static SecurityCam Current { get { return active && idx < list.Count ? list[idx] : null; } }
        public static List<SecurityCam> Available(Fighter p)
        {
            var l = new List<SecurityCam>(); var m = Match.I; if (m == null || p == null || Arena.I == null) return l;
            foreach (var c in Arena.I.cams)
            {
                if (c == null || !c.Working) continue;
                int h = V25.HijackedBy(c);
                if (p.team == m.DefendingTeam ? h < 0 : h == p.team) l.Add(c);
            }
            return l;
        }
        public static void Tick()
        {
            var m = Match.I; var p = m != null ? m.player : null;
            if (active)
            {
                bool stop = p == null || !p.alive || m.phase != Phase.Live || Time.time >= until || p.lastDamageAt > startedAt || Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Escape);
                list.RemoveAll(x => x == null || !x.Working || (p != null && (p.team == m.DefendingTeam ? V25.HijackedBy(x) >= 0 : V25.HijackedBy(x) != p.team)));
                if (stop || list.Count == 0) { Stop(); return; }
                if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetMouseButtonDown(0)) { idx = (idx + 1) % list.Count; Sfx.Ui("click", .4f, 1.4f); }
                if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetMouseButtonDown(1)) { idx = (idx + list.Count - 1) % list.Count; Sfx.Ui("click", .4f, 1.2f); }
                idx = Mathf.Clamp(idx, 0, list.Count - 1);
                var c = list[idx]; var head = c.Head != null ? c.Head : c.transform;
                vc.transform.position = head.position + head.forward * .35f; vc.transform.rotation = head.rotation;
                return;
            }
            if (p == null || !p.alive || m.phase != Phase.Live || m.ui == null || m.ui.Blocking || p.scripted || !Input.GetKeyDown(KeyCode.Alpha5)) return;
            if (Time.time < readyAt) { Sfx.Ui("error", .3f, 1.2f); return; }
            list.Clear(); list.AddRange(Available(p));
            if (list.Count == 0) { Sfx.Ui("error", .3f, 1f); return; }
            Start(p);
        }
        // 截图 / 测试用：不按键直接打开监控画面（和按 5 走同一套可用监控规则）；Close() 收回（不进冷却）
        public static bool Open(Fighter p) { list.Clear(); list.AddRange(Available(p)); if (list.Count == 0) return false; Start(p); return true; }
        public static void Close() { if (!active) return; Stop(); readyAt = 0; }
        static void Start(Fighter p)
        {
            if (vc == null) { vc = new GameObject("监控画面相机").AddComponent<Camera>(); vc.fieldOfView = 68f; vc.nearClipPlane = .1f; }
            var main = Camera.main; vc.depth = (main != null ? main.depth : 0) + 10; vc.enabled = true; vc.gameObject.SetActive(true);
            active = true; idx = 0; startedAt = Time.time; until = Time.time + V25.CamViewMax; V25.CamViews++;
            Sfx.Ui("computerNoise", .4f, 1.3f);
        }
        static void Stop()
        {
            active = false; readyAt = Time.time + V25.CamViewCooldown;
            if (vc != null) vc.gameObject.SetActive(false);
            Sfx.Ui("click", .4f, .8f);
        }
    }

    // ================================================================ V25 HUD：变身大字 / 四角菜色框 / 倒计时、珍珠弹、被监控看到、棉花糖准星、看监控画面、回合战报
    public class V25Hud : MonoBehaviour
    {
        public static V25Hud I;
        public static void Ensure() { if (I == null) I = new GameObject("V25 HUD").AddComponent<V25Hud>(); }
        static string popText; static Color popCol; static float popAt = -9, spotAt = -9, beepAt = -9, reportUntil = -9; static int reportRound;
        static GUIStyle big, small, mid; static Texture2D soft;
        public static void FormPop(string t, Color c) { popText = t; popCol = c; popAt = Time.time; }
        public static void Spotted() { spotAt = Time.time; if (Time.time - beepAt > 1f) { beepAt = Time.time; Sfx.Ui("error", .35f, 1.8f); } }
        public static void ForceReport(int round, float secs) { reportRound = round; reportUntil = Time.time + secs; }   // 截图用（自动对局 / 测试模式下 ShowReport 不弹）
        public static void ShowReport(int round)
        {
            var m = Match.I; if (m == null || Match.Mode != GameMode.Bomb || m.autoplay || m.testMode) return;
            reportRound = round; reportUntil = Time.time + Mathf.Max(3f, Config.RoundEndPause - .2f);
        }
        Vector2 marshOff;
        void Update() { CamView.Tick(); }
        static void Styles()
        {
            if (big != null) return;
            var font = Resources.Load<Font>("NotoSansSC");
            big = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold }; if (font != null) big.font = font;
            mid = new GUIStyle(big) { fontSize = 18 }; small = new GUIStyle(big) { fontSize = 14, fontStyle = FontStyle.Normal };
            soft = new Texture2D(48, 48, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 48; y++) for (int x = 0; x < 48; x++) { float u = (x + .5f) / 24f - 1, v = (y + .5f) / 24f - 1; soft.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01((1 - Mathf.Sqrt(u * u + v * v)) * 2.5f))); }
            soft.Apply();
        }
        static void Shadowed(Rect r, string s, GUIStyle st, Color c)
        {
            var o = GUI.color; GUI.color = new Color(.1f, .05f, .08f, c.a * .85f); GUI.Label(new Rect(r.x + 2, r.y + 3, r.width, r.height), s, st); GUI.color = c; GUI.Label(r, s, st); GUI.color = o;
        }
        static void Box(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = o; }
        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            var m = Match.I; var p = m != null ? m.player : null; if (m == null || p == null) return;
            GUI.depth = 14; Styles();
            float W = Screen.width, H = Screen.height, cx = W / 2, cy = H / 2; var o = GUI.color;
            // 看监控
            if (CamView.Active)
            {
                Box(new Rect(0, 0, W, 4), new Color(1f, .2f, .2f, .8f)); Box(new Rect(0, H - 4, W, 4), new Color(1f, .2f, .2f, .8f));
                for (float y = 0; y < H; y += 6) Box(new Rect(0, y, W, 1), new Color(0, 0, 0, .12f));
                bool hij = CamView.Current != null && V25.HijackedBy(CamView.Current) >= 0;
                Shadowed(new Rect(0, 86, W, 30), (Mathf.Repeat(Time.time, 1f) < .5f ? "● " : "   ") + (hij ? "糖丝接管的监控 " : "监控 ") + (CamView.Index + 1) + " / " + CamView.Count, mid, hij ? new Color(1f, .6f, .9f) : new Color(1f, .35f, .35f));   // 顶部比分栏下方居中（左上角会压住小地图）
                Shadowed(new Rect(0, H * .8f, W, 30), "← → 或 左右键 切换 · 5 退出 · 剩 " + Mathf.CeilToInt(CamView.Left) + " 秒（挨打会自动退出）", small, Color.white);
                GUI.color = o; return;
            }
            if (!p.alive) { Report(m, W, H); GUI.color = o; return; }
            // 变身：四角菜色框 + 准星旁倒计时
            var fs = ToonAssets.Available ? p.Food : null;
            if (fs != null && fs.Transformed)
            {
                Color c = Ingredients.FormColor(fs.form); float a = .55f + .2f * Mathf.Sin(Time.time * 4f), L = Mathf.Min(W, H) * .12f, T = 8f;
                Color cc = new Color(c.r, c.g, c.b, a);
                Box(new Rect(0, 0, L, T), cc); Box(new Rect(0, 0, T, L), cc); Box(new Rect(W - L, 0, L, T), cc); Box(new Rect(W - T, 0, T, L), cc);
                Box(new Rect(0, H - T, L, T), cc); Box(new Rect(0, H - L, T, L), cc); Box(new Rect(W - L, H - T, L, T), cc); Box(new Rect(W - T, H - L, T, L), cc);
                Shadowed(new Rect(cx + 34, cy - 12, 160, 24), Ingredients.FormName(fs.form) + " " + Mathf.CeilToInt(fs.FormLeft) + "s", new GUIStyle(small) { alignment = TextAnchor.MiddleLeft }, Color.Lerp(c, Color.white, .3f));
            }
            if (V25.Boba(p)) Shadowed(new Rect(cx + 34, cy + 10, 160, 22), "珍珠弹 " + Mathf.CeilToInt(V25.BobaLeft(p)) + "s", new GUIStyle(small) { alignment = TextAnchor.MiddleLeft, fontSize = 13 }, new Color(.95f, .78f, .58f));
            // 变身那一下的大字（1.4 s：弹出 → 停 → 淡出）
            float age = Time.time - popAt;
            if (age < 1.4f && !string.IsNullOrEmpty(popText))
            {
                float pop = age < .15f ? Mathf.Lerp(.5f, 1.15f, age / .15f) : age < .3f ? Mathf.Lerp(1.15f, 1f, (age - .15f) / .15f) : 1f, a = Mathf.Clamp01((1.4f - age) / .35f);
                big.fontSize = Mathf.RoundToInt(H * .05f * pop);
                GUI.color = new Color(popCol.r, popCol.g, popCol.b, .35f * a); GUI.DrawTexture(new Rect(cx - H * .06f * pop, cy - H * .2f - H * .06f * pop, H * .12f * pop, H * .12f * pop), soft);
                Shadowed(new Rect(0, cy - H * .2f, W, H * .08f), popText, big, new Color(Mathf.Lerp(popCol.r, 1, .3f), Mathf.Lerp(popCol.g, 1, .3f), Mathf.Lerp(popCol.b, 1, .3f), a));
            }
            // 被监控看到：屏幕上方红眼睛 + 字
            float sa = Time.time - spotAt;
            if (sa < 1f)
            {
                float a = Mathf.Clamp01((1f - sa) / .3f), ey = 128;   // 放在顶部比分栏下面（70 会压住"第 N 回合"）
                GUI.color = new Color(1f, 1f, 1f, .9f * a); GUI.DrawTexture(new Rect(cx - 30, ey - 14, 60, 28), soft);
                GUI.color = new Color(1f, .1f, .1f, a); GUI.DrawTexture(new Rect(cx - 11, ey - 11, 22, 22), soft);
                GUI.color = new Color(.2f, 0, 0, a); GUI.DrawTexture(new Rect(cx - 4, ey - 4, 8, 8), soft);
                Shadowed(new Rect(0, ey + 14, W, 26), "被监控看到！", mid, new Color(1f, .35f, .35f, a));
            }
            // 被接管的监控提示（攻方）
            if (m.phase == Phase.Live && p.team != m.DefendingTeam && CamView.Available(p).Count > 0 && CamView.CooldownLeft <= 0)
                Shadowed(new Rect(0, H * .78f, W, 22), "监控被糖丝接管了 · 按 5 偷看守方", small, new Color(1f, .65f, .9f));
            else if (m.phase == Phase.Live && p.team == m.DefendingTeam && Match.Mode == GameMode.Bomb && CamView.CooldownLeft <= 0 && CamView.Available(p).Count > 0 && Time.time - m.liveStartedAt < 8f)
                Shadowed(new Rect(0, H * .78f, W, 22), "按 5 看监控画面（最多 " + V25.CamViewMax.ToString("0") + " 秒）", small, new Color(.75f, .9f, 1f));
            // 棉花糖准星：被糖黏住时准星变成一团软绵绵的棉花糖（跟手有拖影）
            if (Time.time < p.jammedUntil && ToonAssets.Available)
            {
                Vector2 target = new Vector2(-Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 14f;
                marshOff = Vector2.Lerp(marshOff, target, Time.deltaTime * 6f);
                float s = 34f + 4f * Mathf.Sin(Time.time * 5f);
                GUI.color = new Color(1f, .75f, .92f, .35f); GUI.DrawTexture(new Rect(cx - s / 2 + marshOff.x * 1.6f, cy - s / 2 + marshOff.y * 1.6f, s, s), soft);
                GUI.color = new Color(1f, .82f, .95f, .55f); GUI.DrawTexture(new Rect(cx - s / 2 + marshOff.x, cy - s / 2 + marshOff.y, s, s), soft);
                GUI.color = new Color(1f, .95f, .98f, .7f); GUI.DrawTexture(new Rect(cx - s * .3f, cy - s * .3f, s * .6f, s * .6f), soft);
            }
            Report(m, W, H);
            GUI.color = o;
        }
        // 回合战报：谁杀了谁、用什么枪、打在哪、多远
        void Report(Match m, float W, float H)
        {
            if (Time.time > reportUntil || m.phase != Phase.RoundEnd) return;
            var rows = new List<V25.KillRow>(); foreach (var k in V25.Kills) if (k.round == reportRound) rows.Add(k);
            float w = 420, h = 44 + Mathf.Max(1, rows.Count) * 22, x = 20, y = H * .36f;   // 放左边（右边会和首次提示卡叠在一起）
            Box(new Rect(x, y, w, h), new Color(.06f, .04f, .08f, .78f)); Box(new Rect(x, y, w, 3), new Color(1f, .7f, .85f, .9f));
            Shadowed(new Rect(x, y + 6, w, 26), "第 " + reportRound + " 回合战报", mid, new Color(1f, .85f, .92f));
            var st = new GUIStyle(small) { alignment = TextAnchor.MiddleLeft, fontSize = 13 };
            if (rows.Count == 0) Shadowed(new Rect(x + 14, y + 36, w - 20, 22), "这回合没人倒下", st, new Color(.85f, .85f, .9f));
            for (int i = 0; i < rows.Count; i++)
            {
                var k = rows[i]; Color kc = k.killerTeam >= 0 ? Config.TeamColor(k.killerTeam) : Color.white;
                string line = k.killer + " ▶ " + k.victim + "   " + k.weapon + " · " + k.part + (k.head ? "（爆头）" : "") + " · " + k.dist.ToString("0") + " m" + (k.ttk > .05f ? " · " + k.ttk.ToString("0.0") + " s" : "");
                Shadowed(new Rect(x + 14, y + 36 + i * 22, w - 20, 22), line, st, Color.Lerp(kc, Color.white, .45f));
            }
        }
    }
}
