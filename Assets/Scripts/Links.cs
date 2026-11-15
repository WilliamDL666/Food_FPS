using System.Collections.Generic;
using UnityEngine;

namespace NeonSiege
{
    // ================================================================ V22 第三期：干员 × 地图元素联动（设计稿_V22_地图重构与干员地图联动 §4）
    // 6 个主联动，每个干员 1 个。原技能本体不变，联动是"在特定地图元素上的第二种用法"。
    // 四条硬约束：有时限（≤ 35 s，回合清空）、至少 2 种反制、同一元素同一时间只有一种联动（先到先得）、不永久改变地图。
    // 顺序和 Role 一一对应：猎隼 / 铆钉 / 铁幕 / 夜鸦 / 焚芯 / 幻频
    public enum LinkKind { ColaBounce, WaferPatch, JellyCurtain, MustardPot, CakeBreach, CandySteam }

    public static class Links
    {
        public static readonly Color Gold = new Color(1f, .8f, .25f);
        public static readonly string[] Ids = { "cola", "patch", "curtain", "pot", "breach", "steam" };
        public static readonly int[] Total = new int[6];   // 本次运行累计触发（测试读）
        public static LinkKind For(Role r) { return (LinkKind)(int)r; }
        public static Role RoleOf(LinkKind k) { return (Role)(int)k; }

        // ---------------------------------------------------------------- 文案（气泡 / 预览 / H 页 / 提示卡 / 播报共用，测试直接比字符串）
        public static string Name(LinkKind k) { switch (k) { case LinkKind.ColaBounce: return "可乐弹射"; case LinkKind.WaferPatch: return "嵌入补洞"; case LinkKind.JellyCurtain: return "果冻门帘"; case LinkKind.MustardPot: return "芥末辣锅"; case LinkKind.CakeBreach: return "开墙"; default: return "棉花糖蒸汽"; } }
        public static string ElementName(LinkKind k) { switch (k) { case LinkKind.ColaBounce: return "果冻弹床"; case LinkKind.WaferPatch: return "破口"; case LinkKind.JellyCurtain: return "门框"; case LinkKind.MustardPot: return "火锅"; case LinkKind.CakeBreach: return "蛋糕墙"; default: return "蒸汽"; } }
        // 世界气泡里的联动行（≤ 18 字，只给能联动的英雄看）
        public static string Bubble(LinkKind k)
        {
            switch (k)
            {
                case LinkKind.ColaBounce: return "★ 冲刺踩弹床 → 可乐弹射";
                case LinkKind.WaferPatch: return "★ 威化墙贴上去 → 嵌入补洞";
                case LinkKind.JellyCurtain: return "★ 布丁盾嵌门框 → 果冻门帘";
                case LinkKind.MustardPot: return "★ 签子射进锅 → 芥末辣锅";
                case LinkKind.CakeBreach: return "★ 纸杯粘墙上 → 炸出门洞";
                default: return "★ 迷雾罩蒸汽 → 不透明烟";
            }
        }
        public static string Bubble(Role r, LinkKind element) { return For(r) == element ? Bubble(element) : null; }
        // 预览 / 组合提示上的结果字
        public static string Result(LinkKind k)
        {
            switch (k)
            {
                case LinkKind.ColaBounce: return "可乐弹射 · 飞远 ×" + Config.DashPadBoost.ToString("0.##");
                case LinkKind.WaferPatch: return "嵌入补洞 · " + Config.CoverPatchLife.ToString("0") + " 秒";
                case LinkKind.JellyCurtain: return "果冻门帘 · " + Config.ShieldDockLife.ToString("0") + " 秒";
                case LinkKind.MustardPot: return "芥末辣锅 · " + Config.MustardPotLife.ToString("0") + " 秒";
                case LinkKind.CakeBreach: return "开墙 · 炸出门洞";
                default: return "棉花糖蒸汽 · 挡视线";
            }
        }
        public static string Combo(Role r) { var k = For(r); return ElementName(k) + " + " + Config.Hero(r).skill.Name + " → " + Name(k); }
        public static string HelpRule(Role r)
        {
            switch (r)
            {
                case Role.Falcon: return "汽水冲刺途中（或冲完 " + Config.DashPadWindow.ToString("0.##") + " 秒内）踩到果冻弹床：沿冲刺方向弹出去，水平距离 ×" + Config.DashPadBoost.ToString("0.##") + "；落地冒一圈可乐泡沫（25 m 能听到）";
                case Role.Rivet: return "预览贴近被打开的蛋糕墙破口 / 墙洞（" + Config.CoverPatchSnap.ToString("0.#") + " m 内）会变金色吸附：威化墙嵌进破口，耐久不变，存在 " + Config.CoverPatchLife.ToString("0") + " 秒（部署 " + Config.CoverPatchDeploy.ToString("0.0") + " 秒）。每人同时 1 块；开局就有的入口仍然不能封";
                case Role.Ironwall: return "面向 " + Config.ShieldDockRange.ToString("0") + " m 内开着的门框（≤ " + Config.ShieldDockMaxWidth.ToString("0") + " m 宽：蒸笼门、连廊卷帘门、B 侧门）按 F：布丁盾嵌进门框变成果冻门帘，你可以走开。承伤 " + (Config.ShieldHp * Config.ShieldDockHpMult).ToString("0") + "、" + Config.ShieldDockLife.ToString("0") + " 秒；子弹手雷都挡，人能穿过但速度 ×" + Config.ShieldDockPassSlow.ToString("0.0#");
                case Role.Raven: return "芥末签射进火锅 / 关东煮锅：不扫描，改成 " + Config.MustardPotRadius.ToString("0") + " m 黄绿辣雾 " + Config.MustardPotLife.ToString("0") + " 秒，雾里的敌人一直透墙可见（不需要视线），离开后再亮 " + Config.MustardPotLinger.ToString("0") + " 秒";
                case Role.Ember: return "纸杯蛋糕直接粘在蛋糕墙上：爆炸时落点正下方必定炸出 ≥ " + (Config.BlastWallDoorHalf * 2).ToString("0.0") + " × " + Config.BlastWallDoorHeight.ToString("0.0") + " m 的门洞（伤害和焦糖火不变）";
                default: return "棉花糖迷雾罩住蒸汽源（火锅 / 关东煮热气、正在喷的蒸汽阀）：蒸汽中心 " + Config.CandySteamRadius.ToString("0") + " m 变成不透明的棉花糖蒸汽，" + Config.CandySteamLife.ToString("0") + " 秒（不超过迷雾剩余时间）";
            }
        }
        public static string HelpCounter(Role r)
        {
            switch (r)
            {
                case Role.Falcon: return "落点固定可以预瞄；空中不能变向；开罐声 40 m";
                case Role.Rivet: return "纸杯蛋糕 ×2 + 持续火力；部署 0.9 秒能打断；30 秒后自己消失";
                case Role.Ironwall: return "纸杯蛋糕 ×2；直接挤过去（会变慢）；绕别的门；11 秒后化掉";
                case Role.Raven: return "离开火锅 6 m；棉花糖迷雾里不会被标记；冰淇淋打火锅散热气；一枪打掉签子";
                case Role.Ember: return "铆钉嵌入补洞、BR-5 奶油补墙；听滋滋声离开红圈";
                default: return "冰淇淋打火锅散热气（一起散）；提前拧掉蒸汽阀；绕到雾外";
            }
        }
        public static string TipBody(Role r) { return Combo(r) + "：" + Result(For(r)) + "。看到金色 ★ 气泡、技能图标变金就能联动，H 帮助里有详细规则"; }

        // ---------------------------------------------------------------- 工具
        public static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
        public static float SegFlatDist(Vector3 a, Vector3 b, Vector3 p)
        {
            a.y = b.y = p.y = 0; Vector3 ab = b - a;
            float t = ab.sqrMagnitude < 1e-6f ? 0 : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(a + ab * t, p);
        }

        // ---------------------------------------------------------------- 破口（铆钉 / 焚芯）
        public class Breach { public Object key; public BreakWall wall; public Destructible crate; public Vector3 center; public Quaternion rot; public float width, height; }
        static int brFrame = -1; static readonly List<Breach> brCache = new List<Breach>();
        // 当前地图上所有"被打出来的、能过人的"破口：打碎的墙洞木箱 + 蛋糕墙上连续 ≥ 1 m 的贯穿洞
        public static List<Breach> Breaches()
        {
            if (brFrame == Time.frameCount) return brCache;
            brFrame = Time.frameCount; brCache.Clear();
            if (Arena.I == null) return brCache;
            foreach (var d in Arena.I.destructibles)
                if (d != null && !d.Alive && d.name.Contains("墙洞"))
                    brCache.Add(new Breach { key = d, crate = d, center = d.transform.position, rot = d.transform.rotation, width = 1.95f, height = 2.3f });
            foreach (var w in Arena.I.breakWalls) { if (w == null) continue; var b = w.Opening(); if (b != null) brCache.Add(b); }
            return brCache;
        }
        public static DeployedCover PatchOf(Breach b)
        {
            foreach (var c in DeployedCover.Live) if (c != null && c.patched && c.patchKey == b.key) return c;
            return null;
        }
        static bool FighterIn(Breach b)
        {
            foreach (var h in Physics.OverlapBox(b.center + Vector3.up * b.height / 2, new Vector3(b.width / 2, b.height / 2, .35f), b.rot, ~0, QueryTriggerInteraction.Ignore))
                if (h.GetComponentInParent<Fighter>() != null) return true;
            return false;
        }
        // desired 2 m 内有破口、且铆钉本人离破口 ≤ CoverPatchReach → 吸附。busy = 已被占用（已补 / 有门帘 / 有人站在洞里）
        public static bool PatchTarget(Fighter f, Vector3 desired, out Breach br, out bool busy)
        {
            br = null; busy = false; float bd = Config.CoverPatchSnap;
            foreach (var b in Breaches())
            {
                float d = Flat(b.center, desired); if (d > bd) continue;
                if (Flat(b.center, f.transform.position) > Config.CoverPatchReach || Mathf.Abs(b.center.y - f.transform.position.y) > 1.2f) continue;
                bd = d; br = b;
            }
            if (br == null) return false;
            busy = PatchOf(br) != null || ShieldWall.CurtainAt(br.center + Vector3.up) != null || FighterIn(br);
            return true;
        }
        public static bool SpawnPatch(Fighter f, Breach b)
        {
            foreach (var c in DeployedCover.Live.ToArray()) if (c != null && c.patched && c.owner == f) Object.Destroy(c.gameObject);   // 每个铆钉同时只有 1 块嵌入墙
            var go = new GameObject((Skills.Food ? "嵌入补洞的威化墙 " : "嵌入补洞的掩体 ") + Config.TeamName(f.team));
            go.transform.position = b.center; go.transform.rotation = b.rot;
            go.AddComponent<DeployedCover>().InitPatch(f, b);
            Skills.spawned.Add(go);
            if (Skills.Food) { Sfx.Play("food_squish", b.center, .9f, .85f, 1f, 40f); Sfx.Play("food_popcorn", b.center, .7f, .7f, 1f, 30f); }
            Trigger(LinkKind.WaferPatch, f, b.center + Vector3.up * 1.2f, (b.crate != null ? "墙洞" : "蛋糕墙破口") + "嵌进一块金边威化 · " + Config.CoverPatchLife.ToString("0") + " 秒");
            return true;
        }

        // ---------------------------------------------------------------- 门框（铁幕）
        public class Frame { public Object key; public Vector3 center; public Quaternion rot; public float width, height; public bool open; public string name; }
        static int frFrame = -1; static readonly List<Frame> frCache = new List<Frame>();
        public static List<Frame> Frames()
        {
            if (frFrame == Time.frameCount) return frCache;
            frFrame = Time.frameCount; frCache.Clear();
            if (Arena.I == null) return frCache;
            foreach (var s in Arena.I.shutters)
                if (s != null && s.Width <= Config.ShieldDockMaxWidth + .01f)
                    frCache.Add(new Frame { key = s, center = s.transform.position, rot = s.transform.rotation, width = s.Width, height = s.Height, open = s.open && !s.Moving, name = s.gate ? "蒸笼门" : "卷帘门" });
            foreach (var d in Arena.I.doors)
                if (d != null) frCache.Add(new Frame { key = d, center = d.transform.position, rot = d.transform.rotation, width = 4f, height = 3.2f, open = d.open, name = "侧门" });
            return frCache;
        }
        public static ShieldWall CurtainOf(Frame fr) { foreach (var s in ShieldWall.Docked) if (s != null && s.dockKey == fr.key) return s; return null; }
        // 5 m 内、在面前（55° 内，贴着门框时不看朝向）、开着、没被占用的门框
        public static Frame DockFrame(Fighter f, out Frame busy)
        {
            busy = null; Frame best = null; float bd = float.MaxValue;
            Vector3 pos = f.transform.position, fwd = Quaternion.Euler(0, f.yaw, 0) * Vector3.forward;
            foreach (var fr in Frames())
            {
                Vector3 to = fr.center - pos; float dy = to.y; to.y = 0; float d = to.magnitude;
                if (d > Config.ShieldDockRange || Mathf.Abs(dy) > 1.5f) continue;
                if (d > 1.2f && Vector3.Angle(fwd, to) > 55f) continue;
                if (!fr.open || CurtainOf(fr) != null) { busy = fr; continue; }
                if (d < bd) { bd = d; best = fr; }
            }
            return best;
        }
        public static void SpawnCurtain(Fighter f, Frame fr)
        {
            var go = new GameObject((Skills.Food ? "果冻门帘 " : "门框阵列 ") + Config.TeamName(f.team));
            go.transform.position = fr.center; go.transform.rotation = fr.rot;
            go.AddComponent<ShieldWall>().InitDock(f, fr);
            Skills.spawned.Add(go);
            Sfx.Play("food_boing", fr.center, 1f, .5f, 1f, 45f);
            Trigger(LinkKind.JellyCurtain, f, fr.center + Vector3.up * 1.5f, fr.name + "挂上果冻门帘 · 子弹打不穿、人能慢慢挤过去");
        }

        // ---------------------------------------------------------------- 火锅（夜鸦）
        public static HotpotHazard PotAt(Vector3 p)
        {
            foreach (var h in HotpotHazard.All)
            {
                if (h == null) continue; Vector3 c = h.transform.position;
                if (Flat(p, c) <= 2f && p.y - c.y <= 2.6f && p.y - c.y >= -.5f) return h;
            }
            return null;
        }
        public static Drone PotDrone(HotpotHazard h) { foreach (var d in Drone.Live) if (d != null && d.pot == h) return d; return null; }
        public static bool PotBusy(HotpotHazard h) { return PotDrone(h) != null; }
        // p 点被"team 的敌方"放的棉花糖迷雾罩住
        public static bool EnemyNoiseCovers(Vector3 p, int team)
        {
            foreach (var n in NoiseField.Live)
                if (n != null && n.owner != null && n.owner.team != team && Flat(p, n.transform.position) <= Config.NoiseRadius) return true;
            return false;
        }
        public static GameObject MustardFog(Vector3 at)
        {
            var go = new GameObject("芥末辣锅"); go.transform.position = at;
            float R = Config.MustardPotRadius;
            // 地面黄绿辣雾（LINK-4）
            var ps = new GameObject("辣雾").AddComponent<ParticleSystem>(); ps.transform.SetParent(go.transform, false); ps.transform.localPosition = Vector3.up * .4f;
            var main = ps.main; main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3f); main.startSpeed = new ParticleSystem.MinMaxCurve(.2f, .7f); main.startSize = new ParticleSystem.MinMaxCurve(1.4f, 2.8f);
            main.startColor = new Color(.75f, 1f, .25f, .42f); main.maxParticles = 260; main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = -.02f;
            var em = ps.emission; em.rateOverTime = 70;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = R * .85f; sh.rotation = new Vector3(-90, 0, 0);
            var col = ps.colorOverLifetime; col.enabled = true; var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(.8f, 1f, .3f), 0), new GradientColorKey(new Color(.55f, .9f, .2f), 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .2f), new GradientAlphaKey(.7f, .7f), new GradientAlphaKey(0, 1) }); col.color = g;
            ps.GetComponent<ParticleSystemRenderer>().material = Fx.Smoke;
            // 锅里冒起来的芥末烟柱
            var plume = new GameObject("芥末烟柱").AddComponent<ParticleSystem>(); plume.transform.SetParent(go.transform, false); plume.transform.localPosition = Vector3.up * 1.1f; plume.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var pm = plume.main; pm.startLifetime = 2.2f; pm.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.4f); pm.startSize = new ParticleSystem.MinMaxCurve(.8f, 1.6f); pm.startColor = new Color(.7f, 1f, .2f, .6f); pm.maxParticles = 120; pm.simulationSpace = ParticleSystemSimulationSpace.World;
            var pe = plume.emission; pe.rateOverTime = 30; var psh = plume.shape; psh.shapeType = ParticleSystemShapeType.Cone; psh.angle = 12; psh.radius = .6f;
            plume.GetComponent<ParticleSystemRenderer>().material = Fx.Smoke;
            var ring = new GameObject("辣雾范围").AddComponent<LineRenderer>(); ring.transform.SetParent(go.transform, false);
            ring.material = Fx.Additive; ring.startWidth = ring.endWidth = .12f; ring.startColor = ring.endColor = Skills.Mustard; ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Skills.Circle(ring, Skills.GroundAt(at + Vector3.up), R);
            var l = new GameObject("辣雾光").AddComponent<Light>(); l.transform.SetParent(go.transform, false); l.transform.localPosition = Vector3.up * 1.5f; l.color = Skills.Mustard; l.range = R * 1.6f; l.intensity = 2.2f;
            Skills.spawned.Add(go);
            return go;
        }

        // ---------------------------------------------------------------- 蒸汽源（幻频）
        public struct Steam { public Object key; public Vector3 center; public string name; }
        public static List<Steam> SteamSources()
        {
            var l = new List<Steam>();
            if (SteamCloud.Suppressed) return l;   // 冰淇淋散了热气：所有蒸汽都失效
            foreach (var h in HotpotHazard.All) if (h != null) l.Add(new Steam { key = h, center = h.SteamCenter, name = h.name.Contains("关东煮") ? "关东煮热气" : "火锅热气" });
            foreach (var v in SteamValve.Valves) if (v != null && v.Active) l.Add(new Steam { key = v, center = v.CloudCenter, name = v.transform.position.x < 5f ? "后厨蒸汽阀" : "蒸汽阀" });
            return l;
        }
        public static bool SteamNear(Vector3 p, float r, out Steam s)
        {
            s = default; float bd = r;
            foreach (var st in SteamSources()) { float d = Flat(st.center, p); if (d <= bd && Mathf.Abs(st.center.y - p.y) < 5f) { bd = d; s = st; } }
            return s.key != null;
        }
        // 每 0.25 s：迷雾罩住的每个蒸汽源做一次棉花糖蒸汽（同一源同时只有一团）
        public static void CandyCheck(NoiseField n)
        {
            if (n == null || n.owner == null) return;
            float until = n.EndsAt; if (until - Time.time < .5f) return;
            foreach (var s in SteamSources())
            {
                if (n.candyDone.Contains(s.key)) continue;
                if (Flat(s.center, n.transform.position) > Config.NoiseRadius + .5f) continue;
                n.candyDone.Add(s.key);
                if (CandySteam.Active(s.key)) continue;
                CandySteam.Spawn(s.key, s.center, Mathf.Min(Time.time + Config.CandySteamLife, until));
                Trigger(LinkKind.CandySteam, n.owner, s.center, s.name + "被棉花糖罩住 · 变成看不穿的棉花糖蒸汽");
            }
        }

        // ---------------------------------------------------------------- 可乐弹射（猎隼）
        public static float LastColaDist = -1;
        public static Vector3 ColaLaunch(Fighter f, JumpPad pad, Vector3 v)
        {
            bool wasDashing = f.Dashing;
            var dr = f.GetComponent<DashRunner>(); if (dr != null) Object.Destroy(dr);
            if (wasDashing) { f.dashUntil = Time.time; Skills.DashFinished(f); }
            Vector3 h = new Vector3(v.x, 0, v.z), dir = f.lastDashDir; dir.y = 0;
            if (dir.sqrMagnitude < .01f) dir = h.normalized; dir.Normalize();
            Vector3 nv = dir * h.magnitude * Config.DashPadBoost + Vector3.up * v.y;   // 水平方向改成冲刺方向，水平速度 ×DashPadBoost（滞空时间不变 → 水平距离 ×DashPadBoost）
            var cf = f.GetComponent<ColaFlight>() ?? f.gameObject.AddComponent<ColaFlight>(); cf.Begin(f.transform.position);
            Fx.Burst(pad.transform.position + Vector3.up * .3f, Vector3.up, Skills.Cola, 40, 8f, .14f, .7f, .3f);
            Trigger(LinkKind.ColaBounce, f, pad.transform.position + Vector3.up * .5f, "沿冲刺方向弹出去 · 水平 ×" + Config.DashPadBoost.ToString("0.##"));
            return nv;
        }

        // ---------------------------------------------------------------- 触发：遥测 + 金色闪光 + 专属音 + 播报
        public static float lastAt = -9; public static LinkKind lastKind; public static Fighter lastBy;
        public static void Trigger(LinkKind k, Fighter by, Vector3 at, string detail)
        {
            Total[(int)k]++; lastAt = Time.time; lastKind = k; lastBy = by;
            var m = Match.I;
            if (m != null)
            {
                m.OnLink(k);
                m.Feed("★ 联动 · " + (by != null ? Config.TeamName(by.team) + " " + by.Hero.name : "") + "「" + Name(k) + "」" + (string.IsNullOrEmpty(detail) ? "" : " · " + detail), Gold);
                if (m.autoplay) Debug.Log("NEON_LINK r" + m.round + " " + Ids[(int)k] + " " + (by != null ? by.Hero.name : "-") + " at=" + at.ToString("F0"));
                if (by != null) foreach (var b in m.fighters) if (b.team != by.team && b.alive && Vector3.Distance(b.transform.position, at) < 40f && b.TryGetComponent(out Bot bb)) bb.Hear(by, at, 1f);
            }
            Fx.Shockwave(Skills.GroundAt(at + Vector3.up * .5f), 4.5f, Gold, .6f);
            Fx.Burst(at, Vector3.up, Gold, 36, 4.5f, .1f, .8f, .2f);
            Fx.Burst(at, Vector3.up, new Color(1f, .95f, .7f), 14, 2.5f, .18f, .5f, -.1f);
            Fx.Flash(at, Gold, 5f, 10f, .5f);
            Sound(k, at);
        }
        public static string LastSound = "";
        static void Sound(LinkKind k, Vector3 at)
        {
            // 每种联动一个专属音：同一个"叮"（铃声，音高按联动区分）+ 一层美食音
            float[] bell = { 1.5f, 1.15f, .9f, 1.35f, .75f, 1.7f };
            Sfx.Play("impactBell_heavy", at, .55f, bell[(int)k], 1f, 45f);
            switch (k)
            {
                case LinkKind.ColaBounce: Sfx.Play("food_fizz", at, 1f, .8f, 1f, 40f); Sfx.Play("food_boing", at, .9f, 1.4f, 1f, 35f); break;
                case LinkKind.WaferPatch: Sfx.Play("food_popcorn", at, .9f, .6f, 1f, 35f); Sfx.Play("confirmation", at, .6f, 1.3f, 1f, 30f); break;
                case LinkKind.JellyCurtain: Sfx.Play("food_boing", at, 1f, .45f, 1f, 40f); Sfx.Play("food_cream", at, .8f, .9f, 1f, 30f); break;
                case LinkKind.MustardPot: Sfx.Play("food_fizz", at, .9f, .5f, 1f, 40f); Sfx.Play("maximize", at, .7f, .7f, 1f, 40f); break;
                case LinkKind.CakeBreach: Sfx.Play("food_confetti", at, 1f, .8f, 1f, 50f); Sfx.Play("impactPlate_heavy", at, .8f, .7f, 1f, 45f); break;
                default: Sfx.Play("food_squish", at, 1f, .5f, 1f, 40f); Sfx.Play("engineCircular", at, .5f, .6f, 1f, 35f); break;
            }
            LastSound = "impactBell_heavy@" + bell[(int)k];
        }

        // ---------------------------------------------------------------- 可读性：玩家现在瞄着的东西能不能联动（技能图标变金 + 组合提示）
        public struct Aim { public bool any, ok; public LinkKind kind; public Vector3 at; public string text; }
        public static Aim AimFor(Fighter f)
        {
            var a = new Aim();
            if (f == null || !f.alive) return a;
            a.kind = For(f.role);
            Vector3 pos = f.transform.position, fwd = Quaternion.Euler(0, f.yaw, 0) * Vector3.forward;
            switch (f.role)
            {
                case Role.Falcon:
                    foreach (var pad in JumpPad.All)
                    {
                        if (pad == null) continue;
                        Vector3 to = pad.transform.position - pos; float dy = to.y; to.y = 0; float d = to.magnitude;
                        if (d < 1f || d > Config.DashDistance + 1.5f || Mathf.Abs(dy) > 1f || Vector3.Angle(fwd, to) > 30f) continue;
                        a.any = a.ok = true; a.at = pad.transform.position; break;
                    }
                    break;
                case Role.Rivet:
                    if (PatchTarget(f, Skills.CoverDesired(f), out var br, out bool busy)) { a.any = true; a.ok = !busy; a.at = br.center; }
                    break;
                case Role.Ironwall:
                {
                    var fr = DockFrame(f, out var bf);
                    if (fr != null) { a.any = a.ok = true; a.at = fr.center; }
                    else if (bf != null && bf.open) { a.any = true; a.at = bf.center; }
                    break;
                }
                case Role.Raven:
                {
                    var pts = new List<Vector3>();
                    Vector3 hp = Skills.Simulate(f.Eye + f.AimDir * .5f, Skills.ArrowVelocity(f), pts, Config.ArrowGravity, out _);
                    var pot = PotAt(hp); if (pot != null) { a.any = true; a.ok = !PotBusy(pot); a.at = pot.transform.position; }
                    break;
                }
                case Role.Ember:
                {
                    var pts = new List<Vector3>();
                    Vector3 hp = Skills.Simulate(f.Eye + f.AimDir * .5f, Skills.ThrowVelocity(f, 15f), pts, 1f, out Collider c);
                    if (c != null && c.GetComponent<WallPiece>() != null) { a.any = a.ok = true; a.at = hp; }
                    break;
                }
                default:
                    if (SteamNear(Skills.GroundPoint(f, 22f), Config.NoiseRadius + .5f, out var st)) { a.any = a.ok = true; a.at = st.center; }
                    break;
            }
            a.text = !a.any ? null : a.ok ? Result(a.kind) : "已被占用 · 同一处同时只能有一个联动";
            return a;
        }

        // ---------------------------------------------------------------- 世界里的联动点（气泡 / 小地图星标 / 首次提示卡）
        public class Element { public Object key; public Vector3 pos; public float h; public string label; public bool interact; }
        static int elFrame = -1; static Role elRole; static readonly List<Element> elCache = new List<Element>();
        public static List<Element> ElementsFor(Role r)
        {
            if (elFrame == Time.frameCount && elRole == r) return elCache;
            elFrame = Time.frameCount; elRole = r; elCache.Clear();
            string line = Bubble(For(r));
            switch (r)
            {
                case Role.Falcon: foreach (var p in JumpPad.All) if (p != null) elCache.Add(new Element { key = p, pos = p.transform.position, h = 1.8f, label = line }); break;
                case Role.Rivet: foreach (var b in Breaches()) if (PatchOf(b) == null) elCache.Add(new Element { key = b.key, pos = b.center, h = b.height + .7f, label = line }); break;
                case Role.Ironwall: foreach (var fr in Frames()) elCache.Add(new Element { key = fr.key, pos = fr.center, h = fr.height + .6f, label = line, interact = fr.key is Interactable }); break;
                case Role.Raven: foreach (var h in HotpotHazard.All) if (h != null) elCache.Add(new Element { key = h, pos = h.transform.position, h = 3.4f, label = line }); break;
                case Role.Ember: if (Arena.I != null) foreach (var w in Arena.I.breakWalls) if (w != null) elCache.Add(new Element { key = w, pos = w.transform.position, h = w.Height + .6f, label = line }); break;
                default:
                    foreach (var h in HotpotHazard.All) if (h != null) elCache.Add(new Element { key = h, pos = h.transform.position, h = 3.4f, label = line });
                    foreach (var v in SteamValve.Valves) if (v != null) elCache.Add(new Element { key = v, pos = v.transform.position, h = 2.3f, label = line, interact = true });
                    break;
            }
            return elCache;
        }
        // 已有的交互气泡（卷帘门 / 蒸笼门 / 蒸汽阀）后面追加一行联动
        public static string InteractLine(Fighter p, Interactable it)
        {
            if (p == null) return null;
            if (p.role == Role.Ironwall && it is Shutter s && s.Width <= Config.ShieldDockMaxWidth + .01f) return Bubble(LinkKind.JellyCurtain);
            if (p.role == Role.Phantom && it is SteamValve) return Bubble(LinkKind.CandySteam);
            return null;
        }
    }

    // ================================================================ 棉花糖蒸汽：迷雾 ∩ 蒸汽源 → 不透明的一团（LINK-6），挡视线（SteamCloud 限时云），冰淇淋吹散
    public class CandySteam : MonoBehaviour
    {
        public static readonly List<CandySteam> Live = new List<CandySteam>();
        public Object key; public float until; public Vector3 center; int cloudId = -1; readonly List<Transform> puffs = new List<Transform>(); readonly List<Vector3> puffBase = new List<Vector3>(); float born;
        public static bool Active(Object k) { foreach (var c in Live) if (c != null && c.key == k) return true; return false; }
        public static CandySteam Spawn(Object key, Vector3 c, float until)
        {
            var go = new GameObject("棉花糖蒸汽"); go.transform.position = c;
            var cs = go.AddComponent<CandySteam>(); cs.key = key; cs.until = until; cs.center = c; cs.born = Time.time;
            cs.cloudId = SteamCloud.AddTimed(c, Config.CandySteamRadius / .8f, until);   // SteamCloud.Blocks 按半径 ×0.8 判定 → 实际挡视线半径 = CandySteamRadius
            cs.Build(); Skills.spawned.Add(go);
            return cs;
        }
        void OnEnable() { Live.Add(this); }
        void OnDisable() { Live.Remove(this); }
        void OnDestroy() { SteamCloud.ExpireTimed(cloudId); }
        void Build()
        {
            float R = Config.CandySteamRadius;
            Color pink = new Color(1f, .72f, .9f), white = new Color(1f, .96f, .98f);
            var rnd = new System.Random(7);
            for (int i = 0; i < 11; i++)
            {
                GameObject puff = Arena.Model("Toon/Kitchen/Cloud_" + (1 + i % 3));
                if (puff == null) { puff = GameObject.CreatePrimitive(PrimitiveType.Sphere); }
                foreach (var c in puff.GetComponentsInChildren<Collider>()) Destroy(c);
                Arena.Fit(puff, new Vector3(R * .95f, 0, 0));
                var holder = new GameObject("棉花糖团").transform; holder.SetParent(transform, false);
                puff.transform.SetParent(holder, false);
                float a = (float)rnd.NextDouble() * Mathf.PI * 2, rr = i == 0 ? 0 : (float)(.35 + rnd.NextDouble() * .5) * R;
                Vector3 off = new Vector3(Mathf.Cos(a) * rr, -1.7f + (float)rnd.NextDouble() * 2.8f, Mathf.Sin(a) * rr);
                holder.localPosition = off; holder.localRotation = Quaternion.Euler(0, (float)rnd.NextDouble() * 360f, 0);
                Color col = Color.Lerp(pink, white, (float)rnd.NextDouble());
                foreach (var r in puff.GetComponentsInChildren<Renderer>()) { r.material.color = col; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
                puffs.Add(holder); puffBase.Add(off);
            }
            var ps = new GameObject("棉花糖烟").AddComponent<ParticleSystem>(); ps.transform.SetParent(transform, false);
            var main = ps.main; main.startLifetime = 2.2f; main.startSpeed = new ParticleSystem.MinMaxCurve(.2f, .8f); main.startSize = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
            main.startColor = new Color(1f, .8f, .94f, .9f); main.maxParticles = 160; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 45; var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = R * .8f;
            ps.GetComponent<ParticleSystemRenderer>().material = Fx.Smoke;
            transform.localScale = Vector3.one * .2f;
        }
        void Update()
        {
            if (SteamCloud.Suppressed) { if (Match.I != null) Match.I.Feed("冰淇淋吹散了热气 · 棉花糖蒸汽一起散了", new Color(.7f, .95f, 1f)); Destroy(gameObject); return; }   // 反制
            float age = Time.time - born, left = until - Time.time;
            float k = Mathf.Min(Mathf.Clamp01(age / .35f), Mathf.Clamp01(left / .5f));
            transform.localScale = Vector3.one * Mathf.Lerp(.2f, 1f, Mathf.SmoothStep(0, 1, k));
            for (int i = 0; i < puffs.Count; i++) if (puffs[i] != null) puffs[i].localPosition = puffBase[i] + new Vector3(0, Mathf.Sin(Time.time * 1.3f + i) * .15f, 0);
            if (left <= 0) Destroy(gameObject);
        }
    }

    // ================================================================ 可乐弹射的飞行：可乐尾迹 + 落地泡沫（25 m 能听到）
    public class ColaFlight : MonoBehaviour
    {
        Fighter f; Vector3 from; float started, trailAt;
        public void Begin(Vector3 p) { f = GetComponent<Fighter>(); from = p; started = Time.time; enabled = true; }
        void Update()
        {
            if (f == null || !f.alive) { enabled = false; return; }
            if (Time.time >= trailAt) { trailAt = Time.time + .04f; Fx.Burst(f.transform.position + Vector3.up * .8f, Vector3.down, Time.frameCount % 3 == 0 ? Links.Gold : Skills.Cola, 4, 1.5f, .12f, .45f, .2f); }
            if (Time.time - started < .25f || !f.grounded) return;
            Links.LastColaDist = Links.Flat(f.transform.position, from);
            Fx.Shockwave(f.transform.position, 4.5f, Skills.Cola, .5f);
            Fx.Burst(f.transform.position + Vector3.up * .2f, Vector3.up, Skills.Bubble, 40, 4f, .12f, .7f, -.1f);
            Fx.Burst(f.transform.position + Vector3.up * .2f, Vector3.up, Skills.Cola, 30, 3f, .16f, .8f, .2f);
            Sfx.Play("food_fizz", f.transform.position, 1f, .7f, 1f, 25f); Sfx.Play("food_splat", f.transform.position, .7f, 1.2f, 1f, 25f);
            enabled = false;
        }
    }

    // ================================================================ 团队标记（最小版）：中键 = 敌人 / 这里 / 这儿能联动；AI 队友会看过去或靠拢
    public enum PingKind { Enemy, Here, Link }
    public class Ping
    {
        public PingKind kind; public LinkKind link; public Vector3 pos; public int team; public Fighter owner, target, responder; public float born; public string label, line;
        public float Age { get { return Time.time - born; } }
        public float Life { get { return kind == PingKind.Enemy ? 4f : Config.PingLife; } }
    }
    public static class Pings
    {
        public static readonly List<Ping> Live = new List<Ping>();
        public static int Total;
        static readonly Dictionary<Fighter, float> cd = new Dictionary<Fighter, float>();
        public static void Clear() { Live.Clear(); cd.Clear(); }
        public static void Tick() { Live.RemoveAll(p => p.Age > p.Life); }
        public static bool SameTeam(Fighter a, Fighter b) { return a != null && b != null && (Match.FFA ? a == b : a.team == b.team); }
        public static Color ColorOf(PingKind k) { return k == PingKind.Enemy ? new Color(1f, .3f, .3f) : k == PingKind.Link ? Links.Gold : new Color(.45f, .9f, 1f); }
        public static string Line(PingKind k, LinkKind l)
        {
            if (k == PingKind.Enemy) return "敌人在这！小心番茄酱";
            if (k == PingKind.Here) return "往这儿走～";
            switch (l)
            {
                case LinkKind.ColaBounce: return "这个弹床能可乐弹射！";
                case LinkKind.WaferPatch: return "这个破口能补！";
                case LinkKind.JellyCurtain: return "这个门框能挂布丁！";
                case LinkKind.MustardPot: return "火锅能射芥末签！";
                case LinkKind.CakeBreach: return "这面墙能炸！";
                default: return "这儿有蒸汽，能做棉花糖烟！";
            }
        }
        // 玩家中键：先找准星附近 4.5° 内看得见的敌人 → 再看准星指着的东西是不是联动点 → 否则标地面
        public static Ping FromAim(Fighter f)
        {
            if (f == null || !f.alive) return null;
            if (cd.TryGetValue(f, out float t) && Time.time < t) return null;
            cd[f] = Time.time + Config.PingCooldown;
            Fighter best = null; float ba = 4.5f;
            foreach (var e in Match.I.fighters)
            {
                if (!e.alive || SameTeam(e, f) || e == f) continue;
                Vector3 to = e.Chest - f.Eye; if (to.magnitude > 90f) continue;
                float ang = Vector3.Angle(f.AimDir, to);
                if (ang < ba && Skills.LineClear(f.Eye, e.Chest, e)) { ba = ang; best = e; }
            }
            if (best != null) return Create(f, PingKind.Enemy, best.Chest, best);
            Vector3 hp = f.Eye + f.AimDir * 40f; Collider hc = null; float hd = float.MaxValue;
            foreach (var h in Physics.RaycastAll(f.Eye, f.AimDir, 120f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(f.transform) || h.distance >= hd) continue;
                hd = h.distance; hp = h.point; hc = h.collider;
            }
            if (ElementAt(hp, hc, out LinkKind lk, out Vector3 ep)) return Create(f, PingKind.Link, ep, null, lk);
            return Create(f, PingKind.Here, hp);
        }
        public static bool ElementAt(Vector3 p, Collider c, out LinkKind k, out Vector3 at)
        {
            k = LinkKind.CakeBreach; at = p;
            foreach (var b in Links.Breaches()) if (Links.Flat(b.center, p) < 2.5f && Mathf.Abs(p.y - b.center.y) < 3f) { k = LinkKind.WaferPatch; at = b.center + Vector3.up; return true; }
            if (c != null && c.GetComponent<WallPiece>() != null) { k = LinkKind.CakeBreach; return true; }
            foreach (var h in HotpotHazard.All) if (h != null && Links.Flat(h.transform.position, p) < 3f && p.y - h.transform.position.y < 4f) { k = LinkKind.MustardPot; at = h.transform.position + Vector3.up; return true; }
            foreach (var v in SteamValve.Valves) if (v != null && Links.Flat(v.transform.position, p) < 2.2f) { k = LinkKind.CandySteam; at = v.transform.position + Vector3.up; return true; }
            foreach (var pad in JumpPad.All) if (pad != null && Links.Flat(pad.transform.position, p) < 2f && Mathf.Abs(p.y - pad.transform.position.y) < 2f) { k = LinkKind.ColaBounce; at = pad.transform.position + Vector3.up * .3f; return true; }
            foreach (var fr in Links.Frames()) if (Links.Flat(fr.center, p) < 3f && p.y - fr.center.y < fr.height + 1f) { k = LinkKind.JellyCurtain; at = fr.center + Vector3.up * 1.5f; return true; }
            return false;
        }
        public static Ping Create(Fighter f, PingKind kind, Vector3 pos, Fighter target = null, LinkKind link = LinkKind.ColaBounce)
        {
            var m = Match.I; if (m == null || f == null) return null;
            int mine = 0; Ping oldest = null;
            foreach (var q in Live) if (q.owner == f) { mine++; if (oldest == null) oldest = q; }
            if (mine >= 2 && oldest != null) Live.Remove(oldest);   // 每人同时最多 2 个
            var p = new Ping { kind = kind, link = link, pos = pos, team = f.team, owner = f, target = target, born = Time.time };
            p.line = Line(kind, link);
            p.label = kind == PingKind.Enemy ? "敌人" : kind == PingKind.Here ? "这里" : "★ " + Links.ElementName(link);
            Live.Add(p); Total++; m.OnPing();
            var pl = m.player;
            if (pl != null && SameTeam(pl, f))
            {
                m.Feed((f.human ? "你" : f.Hero.name) + "：" + p.line, ColorOf(kind));
                Sfx.Ui(kind == PingKind.Enemy ? "maximize" : "click", .45f, kind == PingKind.Link ? 1.5f : 1.15f);
            }
            // AI 队友响应：都看过去；"这里 / 这儿能联动"时离得最近的那个 AI 走过去
            Fighter mover = null; float md = 60f;
            if (kind != PingKind.Enemy)
                foreach (var b in m.fighters)
                {
                    if (b == f || !b.alive || b.human || !SameTeam(b, f) || b.bombBusy || m.BombCarrier == b) continue;
                    float d = Vector3.Distance(b.transform.position, pos); if (d < md) { md = d; mover = b; }
                }
            foreach (var b in m.fighters)
                if (b != f && b.alive && SameTeam(b, f) && b.TryGetComponent(out Bot bb)) bb.OnPing(p, b == mover);
            p.responder = mover;
            if (m.autoplay) Debug.Log("NEON_PING r" + m.round + " " + f.Hero.name + " " + kind + " at=" + pos.ToString("F0"));
            return p;
        }
    }

    // ================================================================ 敌方大招预警：横幅 + 全图专属音 + 影响到你时的状态条 + 蜡烛轰炸落点圈。生效时间不变（不加前摇）
    public static class UltWarn
    {
        public class Status { public string text; public float until; public Color color; public System.Func<bool> live; }
        public static readonly List<Status> Statuses = new List<Status>();
        public static float LastAt = -9; public static Role LastRole; public static string LastSound = "", LastBanner = "";
        public static int Warned;
        public static void Clear() { Statuses.Clear(); }
        public static string Counter(Role r)
        {
            switch (r)
            {
                case Role.Falcon: return "往侧面躲开正面";
                case Role.Rivet: return "饭团墙能翻越";
                case Role.Ironwall: return "拖 8 秒或把人逼出罩子";
                case Role.Raven: return "5 秒内别硬拼，换位";
                case Role.Ember: return "马上离开红色落点圈";
                default: return "走出 11 米范围";
            }
        }
        public static void OnUlt(Fighter f, Vector3 ground)
        {
            if (f.role == Role.Ember) CandleRing.Spawn(ground);   // 落点圈：双方都看得到（时间不变：第一根蜡烛仍在 0.3 s 后落下）
            var m = Match.I; var p = m != null ? m.player : null;
            if (p == null || f == p || Pings.SameTeam(f, p)) return;
            LastAt = Time.time; LastRole = f.role; Warned++;
            LastBanner = "敌方大招 · " + f.Hero.name + "「" + FoodState.UltName(f.role) + "」";
            if (m.ui != null) m.ui.UltBanner(LastBanner, FoodGuide.UltEffect(f.role) + " · " + Counter(f.role), new Color(1f, .4f, .35f));
            PlaySound(f.role);
            if (!p.alive) return;
            float d = Vector3.Distance(p.transform.position, f.transform.position);
            switch (f.role)
            {
                case Role.Raven: Add("被回转寿司雷达扫到 · 全图透视 5 秒", 5f, Skills.Mustard, null); break;
                case Role.Phantom: if (Vector3.Distance(p.transform.position, ground) < 11f) Add("糖霜风暴 · 看不清、变慢 6 秒", 6f, new Color(1f, .6f, .9f), null); break;
                case Role.Ember: Add("蜡烛轰炸落点 · 快离开红圈", 2.6f, new Color(1f, .35f, .3f), () => CandleRing.Covers(p.transform.position)); break;
                case Role.Falcon:
                    if (d < 25f && Vector3.Angle(Quaternion.Euler(0, f.yaw, 0) * Vector3.forward, p.transform.position - f.transform.position) < 35f) Add("双层汉堡冲撞冲过来了 · 往侧面躲", 1.4f, new Color(1f, .6f, .25f), null);
                    break;
            }
        }
        static void Add(string t, float secs, Color c, System.Func<bool> live) { Statuses.Add(new Status { text = t, until = Time.time + secs, color = c, live = live }); }
        static void PlaySound(Role r)
        {
            // 6 个专属音（全图、2D）：同一个低音"咚"打头 + 每个英雄一层
            Sfx.Ui("lowFrequency_explosion", .45f, 1.6f);
            switch (r)
            {
                case Role.Falcon: Sfx.Ui("food_boing", .6f, .6f); LastSound = "food_boing"; break;
                case Role.Rivet: Sfx.Ui("impactPlate_heavy", .55f, .7f); LastSound = "impactPlate_heavy"; break;
                case Role.Ironwall: Sfx.Ui("forceField", .55f, .6f); LastSound = "forceField"; break;
                case Role.Raven: Sfx.Ui("food_sushi", .6f, .8f); LastSound = "food_sushi"; break;
                case Role.Ember: Sfx.Ui("food_confetti", .6f, .7f); LastSound = "food_confetti"; break;
                default: Sfx.Ui("glitch", .55f, .6f); LastSound = "glitch"; break;
            }
        }
    }

    // 生日蜡烛轰炸落点圈：半径 = 落点散布 4.5 m + 单根爆炸 3.5 m = 8 m，存在到轰炸结束（约 2.3 s）
    public class CandleRing : MonoBehaviour
    {
        public static readonly List<CandleRing> Live = new List<CandleRing>();
        public const float Radius = 8f, Life = 2.5f;
        float born; LineRenderer ring, inner; Material disk;
        void OnEnable() { Live.Add(this); }
        void OnDisable() { Live.Remove(this); }
        public static bool Covers(Vector3 p) { foreach (var c in Live) if (c != null && Links.Flat(c.transform.position, p) <= Radius) return true; return false; }
        public static CandleRing Spawn(Vector3 at)
        {
            var go = new GameObject("蜡烛轰炸落点圈"); go.transform.position = Skills.GroundAt(at + Vector3.up);
            var c = go.AddComponent<CandleRing>(); c.born = Time.time;
            c.ring = new GameObject("外圈").AddComponent<LineRenderer>(); c.ring.transform.SetParent(go.transform, false);
            c.ring.material = Fx.Additive; c.ring.startWidth = c.ring.endWidth = .22f; c.ring.startColor = c.ring.endColor = new Color(1f, .25f, .2f); c.ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Skills.Circle(c.ring, go.transform.position, Radius);
            c.inner = new GameObject("内圈").AddComponent<LineRenderer>(); c.inner.transform.SetParent(go.transform, false);
            c.inner.material = Fx.Additive; c.inner.startWidth = c.inner.endWidth = .1f; c.inner.startColor = c.inner.endColor = new Color(1f, .5f, .3f); c.inner.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Skills.Circle(c.inner, go.transform.position, 4.5f);
            var d = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(d.GetComponent<Collider>()); d.name = "落点区";
            d.transform.SetParent(go.transform, false); d.transform.localPosition = Vector3.up * .05f; d.transform.localScale = new Vector3(Radius * 2, .005f, Radius * 2);
            c.disk = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, .2f, .15f, .18f) };
            d.GetComponent<Renderer>().sharedMaterial = c.disk; d.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Skills.spawned.Add(go);
            return c;
        }
        void Update()
        {
            float age = Time.time - born;
            float a = .5f + .5f * Mathf.Sin(age * 18f);
            ring.startColor = ring.endColor = new Color(1f, .25f, .2f, .6f + .4f * a);
            disk.color = new Color(1f, .2f, .15f, .12f + .1f * a);
            if (age > Life) Destroy(gameObject);
        }
    }

    // ================================================================ 联动 / 标记 / 大招预警的 HUD
    public class LinkHud : MonoBehaviour
    {
        public static Links.Aim Aim;
        public static string LastResultText = "";
        static GUIStyle st, big, small; static Font font;

        void Update()
        {
            Pings.Tick();
            UltWarn.Statuses.RemoveAll(s => Time.time > s.until);
            var m = Match.I; var p = m != null ? m.player : null;
            if (p == null || !p.alive || m.phase != Phase.Live || p.skillCharges <= 0) { Aim = default; return; }
            Aim = Links.AimFor(p);
            // 首次提示卡：走近自己能联动的元素（12 m）
            if (FoodTips.AutoEnabled)
                foreach (var e in Links.ElementsFor(p.role))
                    if (Links.Flat(e.pos, p.transform.position) < FoodGuide.DetailRange) { FoodTips.Trigger("link_self"); break; }
        }

        static void Styles()
        {
            if (st != null) return;
            font = Resources.Load<Font>("NotoSansSC");
            st = new GUIStyle(GUI.skin.label) { font = font, fontSize = 16, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            big = new GUIStyle(st) { fontSize = 26, fontStyle = FontStyle.Bold };
            small = new GUIStyle(st) { fontSize = 13 };
        }
        static void Fill(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = o; }
        static void Txt(Rect r, string t, GUIStyle s, Color c)
        {
            var o = s.normal.textColor;
            s.normal.textColor = new Color(0, 0, 0, .7f * c.a); GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), t, s);
            s.normal.textColor = c; GUI.Label(r, t, s); s.normal.textColor = o;
        }

        void OnGUI()
        {
            var m = Match.I; if (m == null) return;
            if (m.ui != null && !m.ui.Started && !m.autoplay && !m.testMode) return;
            if (m.ui != null && m.ui.HudOverlayOpen) return;
            var p = m.player; if (p == null) return;
            Styles();
            GUI.depth = -4;
            float s = Mathf.Clamp(Screen.height / 1080f, .7f, 1.6f); GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            float W = Screen.width / s, H = Screen.height / s;
            var cam = Camera.main;
            // ---- 标记（世界图标）
            if (cam != null)
                foreach (var pg in Pings.Live)
                {
                    if (!Pings.SameTeam(pg.owner, p)) continue;
                    Vector3 sp = cam.WorldToScreenPoint(pg.pos + Vector3.up * .6f);
                    float x = sp.x / s, y = (Screen.height - sp.y) / s; bool behind = sp.z < 0;
                    if (behind) { x = W - x; y = H - 80; }
                    x = Mathf.Clamp(x, 40, W - 40); y = Mathf.Clamp(y, 120, H - 180);
                    Color c = Pings.ColorOf(pg.kind); float a = Mathf.Clamp01((pg.Life - pg.Age) / .5f);
                    float pulse = 1f + .12f * Mathf.Sin(Time.time * 8f), sz = 30 * pulse;
                    Icons.Draw(new Rect(x - sz / 2, y - sz / 2, sz, sz), Icons.Diamond, new Color(0, 0, 0, .55f * a));
                    Icons.Draw(new Rect(x - sz / 2 + 3, y - sz / 2 + 3, sz - 6, sz - 6), Icons.Diamond, new Color(c.r, c.g, c.b, a));
                    float dist = Vector3.Distance(cam.transform.position, pg.pos);
                    string who = pg.owner == p ? "你" : pg.owner.Hero.name;
                    Txt(new Rect(x - 90, y - sz / 2 - 24, 180, 22), pg.label + " · " + who, small, new Color(c.r, c.g, c.b, a));
                    Txt(new Rect(x - 60, y + sz / 2, 120, 18), Mathf.RoundToInt(dist) + "m", small, new Color(1, 1, 1, .85f * a));
                }
            if (!p.alive) return;
            // ---- 状态条（敌方大招 / 敌方联动影响到你）
            float sy = 196;
            var list = new List<UltWarn.Status>();
            foreach (var su in UltWarn.Statuses) if (su.live == null || su.live()) list.Add(su);
            foreach (var d in Drone.Live)
                if (d != null && d.PotMode && d.owner != null && !Pings.SameTeam(d.owner, p) && Links.Flat(d.pot.transform.position, p.transform.position) <= Config.MustardPotRadius)
                { list.Add(new UltWarn.Status { text = "被芥末辣锅呛到 · 离开火锅 " + Config.MustardPotRadius.ToString("0") + " 米", until = Time.time + d.PotLeft, color = Skills.Mustard }); break; }
            foreach (var su in list)
            {
                Rect r = new Rect(W / 2 - 230, sy, 460, 34);
                Fill(r, new Color(.1f, .03f, .05f, .82f)); Fill(new Rect(r.x, r.y, 6, r.height), su.color);
                float left = Mathf.Max(0, su.until - Time.time);
                Fill(new Rect(r.x, r.yMax - 3, r.width * Mathf.Clamp01(left / 6f), 3), su.color);
                Txt(new Rect(r.x + 10, r.y, r.width - 20, r.height), "⚠ " + su.text, st, Color.Lerp(su.color, Color.white, .45f));
                sy += 40;
            }
            // ---- 联动触发：准星上方金色结果字（只给触发的人）
            if (Links.lastBy == p && Time.time - Links.lastAt < 1.8f)
            {
                float k = Time.time - Links.lastAt, a = Mathf.Clamp01((1.8f - k) / .4f), sc = 1f + .25f * Mathf.Clamp01(1f - k / .2f);
                LastResultText = "★ 联动！" + Links.Name(Links.lastKind);
                var bs = new GUIStyle(big) { fontSize = Mathf.RoundToInt(28 * sc) };
                Txt(new Rect(W / 2 - 300, H / 2 - 150, 600, 44), LastResultText, bs, new Color(Links.Gold.r, Links.Gold.g, Links.Gold.b, a));
            }
            // ---- 预览上写结果（铆钉 / 夜鸦 / 焚芯 / 幻频按住 F 时）
            if (cam != null && Skills.PreviewState(p, out Vector3 pp, out bool plink, out bool plegal) && plink)
            {
                Vector3 sp = cam.WorldToScreenPoint(pp + Vector3.up * 1.2f);
                if (sp.z > 0)
                {
                    float x = sp.x / s, y = (Screen.height - sp.y) / s;
                    string t = plegal ? Links.Result(Links.For(p.role)) : "已被占用";
                    Fill(new Rect(x - 90, y - 15, 180, 30), new Color(.15f, .08f, .02f, .8f));
                    Txt(new Rect(x - 90, y - 15, 180, 30), "★ " + t, st, plegal ? Links.Gold : new Color(1f, .4f, .35f));
                }
            }
            // ---- 瞄准可联动元素：准星旁金色星 + 底部组合提示（元素 + 技能 → 结果）（LINK-7）
            if (Aim.any && m.CanAct)
            {
                Color c = Aim.ok ? Links.Gold : new Color(1f, .4f, .35f);
                float pulse = .75f + .25f * Mathf.Sin(Time.time * 7f);
                Txt(new Rect(W / 2 + 14, H / 2 - 40, 40, 30), "★", new GUIStyle(big) { fontSize = 22 }, new Color(c.r, c.g, c.b, pulse));
                string combo = Links.ElementName(Aim.kind) + "  +  " + p.Hero.skill.Name + "  →  " + (Aim.ok ? Links.Name(Aim.kind) : "已被占用");
                float tw = combo.Length * 17 + 70;
                Rect r = new Rect(W / 2 - tw / 2, H - 132, tw, 44);
                Fill(r, new Color(.18f, .1f, .02f, .86f));
                Fill(new Rect(r.x, r.y, r.width, 2), c); Fill(new Rect(r.x, r.yMax - 2, r.width, 2), c);
                Icons.Draw(new Rect(r.x + 8, r.y + 4, 36, 36), Icons.Skill(p.role), c);
                Txt(new Rect(r.x + 44, r.y, r.width - 50, r.height), combo, new GUIStyle(st) { fontSize = 18, fontStyle = FontStyle.Bold }, c);
                Txt(new Rect(W / 2 - 200, r.yMax + 2, 400, 20), Aim.text, small, new Color(1f, .92f, .7f, .9f));
            }
        }

        // 小地图：自己英雄能联动的点位（金色星形）+ 队友标记
        public static void MapDraw(System.Func<Vector3, Vector2> map, bool big)
        {
            var m = Match.I; var p = m != null ? m.player : null; if (p == null) return;
            Styles();
            var gs = new GUIStyle(st) { fontSize = big ? 18 : 12 };
            foreach (var e in Links.ElementsFor(p.role))
            {
                Vector2 a = map(e.pos);
                Txt(new Rect(a.x - 10, a.y - 10, 20, 20), "★", gs, new Color(Links.Gold.r, Links.Gold.g, Links.Gold.b, .9f));
            }
            foreach (var pg in Pings.Live)
            {
                if (!Pings.SameTeam(pg.owner, p)) continue;
                Vector2 a = map(pg.pos); Color c = Pings.ColorOf(pg.kind); float r = (big ? 9 : 6) * (1f + .25f * Mathf.Sin(Time.time * 8f));
                Icons.Draw(new Rect(a.x - r, a.y - r, r * 2, r * 2), Icons.Diamond, c);
            }
        }
    }
}
