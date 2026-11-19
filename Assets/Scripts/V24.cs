using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace NeonSiege
{
    // ================================================================ V24 体验强化（Docs/修改意见_V24_体验强化.md）
    // 用户拍板：全部按推荐 —— 悬浮能开枪但散布变大、蒸汽加风向（每回合随机）、死亡变成一盘菜、4 个新机关全做。
    // 这里集中放 V24 新增的规则 / 表现；原有文件只加一行钩子（V24.Xxx(...)），方便对照和回滚。
    // 数值：C# 默认值是唯一来源（同 Config），区间取初版中值，最后靠试玩定。
    public static class V24
    {
        // ---------------------------------------------------------------- 数值
        // 可乐喷泉：托到 8 m（7–9）→ 顶上悬浮 2 s（1.5–2.5）→ 羽毛缓降（重力 ×0.35，最快 2.6 m/s 下落）；喷泉 4.5 s（4–5）内再走进去会被再次托起
        public static float ColaLiftHeight = 8f, ColaHover = 2f, ColaFeatherGrav = .35f, ColaFeatherMaxFall = 2.6f, ColaFountainLife = 4.5f, ColaRelift = 2.6f;
        public static float ColaPuddleRadius = 3.2f, ColaPuddleLife = 7f, ColaPuddleAccel = .4f;   // 喷完留一滩可乐：6–8 s，加速度 ×0.4（打滑）
        public static float HoverSpread = 2f;                                                     // 悬浮时能开枪，散布 +2°；全图可见
        public static int ColaShots = 4; public static float ColaShotWindow = 6f;                 // 可乐瓶打 3 枪摇晃、第 4 枪喷（冷却同手雷触发 15 s）
        // 整片蒸汽：风向（每回合随机），雾团往下风处飘 1 m/s，最多飘 8 m（防止整片雾飘出关口）
        public static float WindSpeed = 1f, WindMaxDrift = 8f; public static Vector3 Wind;
        // 芥末呛雾：每 2–3 s 一个大喷嚏（全图 40 m 可听、暴露 1 s、准星抖）；碰到火 = 辣油爆燃（6 m、最多 35 伤害，雾随之消失）
        public static float SneezeMin = 2f, SneezeMax = 3f, SneezeHear = 40f, SpicyOilRadius = 6f, SpicyOilDamage = 35f;
        // 其他联动
        public static float JellySlow = .85f;                                                    // 果冻挂身：移速 ×0.85（3 s = Config.JellyStickTime）
        public static float CakeRubbleLife = 20f, CreamSlickLife = 4f, CreamSlickAccel = .35f;    // 开墙：碎蛋糕块齐腰掩体 20 s + 奶油滑地 4 s
        public static float CandyTrailTime = 3f, FootprintLife = 6f;                              // 粘糖丝：3 s 内留粉色脚印（脚印 6 s 淡出）
        // 新机关
        public static int HotpotShots = 7; public static float HotpotShotWindow = 4f, HotpotWaveRadius = 4f, HotpotWaveLife = 3f, HotpotWaveDps = 10f, HotpotWaveAccel = .45f, HotpotRefill = 30f;
        public static float FreezerHp = 90f, IceRadius = 6f, IceLife = 10f, IceAccel = .05f, IceSpeed = 1.12f;   // V24 收尾：×0.15 走上去几乎不滑 → ×0.05（5 m/s 要滑约 2.5 s 才停）+ 冰上最高速度 ×1.12
        public static float PopcornHp = 60f, PopcornRadius = 6f, PopcornLife = 6f;
        public static float DonutHold = 1f, DonutSpeed = 8f, DonutRange = 45f, DonutPush = 9f, DonutDamage = 20f, DonutRadius = 1.3f;
        // 技能 / 大招
        public static float BubbleTrailLife = 2f, BubbleTrailBoost = 1.1f, BubbleTrailWidth = 1.3f;
        public static float BurgerClampTime = .6f, FrostSnowLife = 5f, UltCamTime = .4f, UltCamBack = 2.8f, UltCamUp = .9f;
        public static float BirthdaySongAt = .5f;                                                 // 引信剩 0.5 s 唱生日歌（预警，时长不变）

        // ---------------------------------------------------------------- 遥测（测试 / 试玩读）
        public static int Lifts, Hovers, ColaBottleBursts, Sneezes, SpicyOils, HotpotWaves, IceSheets, PopcornClouds, DonutRolls, DonutHits, Rubbles, Footprints, BubbleBoosts, Clamps, BiteMarks, Songs, UltCams, Dishes, Toasts;
        public static string LastKillLine = "";
        static void Log(string s) { if (Match.I != null && Match.I.autoplay) Debug.Log("NEON_V24 r" + Match.I.round + " " + s); }

        // ---------------------------------------------------------------- 布置
        public static GameObject Root;
        public static readonly List<ShootProp> Props = new List<ShootProp>();
        public static readonly List<GiantDonut> Donuts = new List<GiantDonut>();
        public static void Dress(Arena arena)
        {
            Props.Clear(); Donuts.Clear();
            if (Root != null) Arena.Kill(Root);
            if (arena == null) return;
            Root = new GameObject("V24 新机关（不在烘焙匹配范围内）");
            V24Ticker.Ensure();
            if (!ToonAssets.Available) return;
            Physics.SyncTransforms();
            try
            {
                // 冰淇淋冰柜：中庭原冰柜（拿冰护甲）旁边 + 两个冰柜仓门口
                Place(ShootProp.Kind.Freezer, new Vector3(16, 0, 12.4f), 6f);
                Place(ShootProp.Kind.Freezer, new Vector3(7f, 0, 34f), 5f);
                Place(ShootProp.Kind.Freezer, new Vector3(10.5f, 0, -34f), 5f);
                // 爆米花机：夜市主街 + 中庭南侧
                Place(ShootProp.Kind.Popcorn, new Vector3(-20, 0, 4.5f), 6f);
                Place(ShootProp.Kind.Popcorn, new Vector3(16, 0, -2f), 6f);
                // 巨型甜甜圈：南北长街西段，推倒后往东滚过整条街
                PlaceDonut(new Vector3(-46, 0, 40f), Vector3.right);
                PlaceDonut(new Vector3(-46, 0, -40f), Vector3.right);
            }
            catch (System.Exception e) { Debug.LogWarning("NEON_V24 dress failed: " + e); }
            try { V25.Dress(arena); } catch (System.Exception e) { Debug.LogWarning("NEON_V25 dress failed: " + e); }
            Debug.Log("NEON_V24 props=" + Props.Count + " donuts=" + Donuts.Count);
        }
        // 在 near 附近 r 内找一个能站人、背后 1.4 m 内有墙的点（贴墙放，不挡路中间）
        public static bool FindWallSpot(Vector3 near, float r, out Vector3 spot, out Vector3 face)
        {
            spot = near; face = Vector3.forward;
            Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
            for (float d = 0; d <= r; d += .75f)
                for (int i = 0; i < (d < .1f ? 1 : 12); i++)
                {
                    Vector3 p = near + Quaternion.Euler(0, i * 30f, 0) * Vector3.forward * d;
                    if (!NavMesh.SamplePosition(p, out NavMeshHit nh, .5f, NavMesh.AllAreas) || nh.position.y > .5f) continue;
                    if (!Clear(nh.position, .75f, 1.6f)) continue;
                    bool crowded = false;   // 别压在已有的交互物 / 厨房台 / 别的新道具上
                    foreach (var it in Interactable.All) if (it != null && Links.Flat(it.transform.position, nh.position) < 1.8f) crowded = true;
                    foreach (var sp in Props) if (sp != null && Links.Flat(sp.transform.position, nh.position) < 3f) crowded = true;
                    if (crowded) continue;
                    foreach (var w in dirs)
                        if (Physics.Raycast(nh.position + Vector3.up * 1f, w, out RaycastHit wh, 1.4f, ~0, QueryTriggerInteraction.Ignore) && wh.collider.GetComponentInParent<Fighter>() == null && Mathf.Abs(wh.normal.y) < .3f)
                        {
                            spot = wh.point + wh.normal * .75f; spot.y = nh.position.y; face = wh.normal; face.y = 0; face.Normalize();
                            if (Arena.I != null && !Arena.I.IsBuildLegal(spot)) continue;
                            return true;
                        }
                }
            return false;
        }
        static bool Clear(Vector3 p, float r, float h)
        {
            foreach (var c in Physics.OverlapCapsule(p + Vector3.up * (r + .05f), p + Vector3.up * h, r, ~0, QueryTriggerInteraction.Ignore))
                if (c.GetComponentInParent<Fighter>() == null) return false;
            return true;
        }
        static void Place(ShootProp.Kind k, Vector3 near, float r)
        {
            if (!FindWallSpot(near, r, out Vector3 p, out Vector3 face)) { Debug.LogWarning("NEON_V24 skip " + k + " near " + near); return; }
            var go = new GameObject(k == ShootProp.Kind.Freezer ? "冰淇淋冰柜（可射爆）" : "爆米花机（可射爆）");
            go.transform.SetParent(Root.transform, false); go.transform.position = p; go.transform.rotation = Quaternion.LookRotation(face);
            var sp = go.AddComponent<ShootProp>(); sp.Build(k); Props.Add(sp);
        }
        static void PlaceDonut(Vector3 near, Vector3 dir)
        {
            if (!NavMesh.SamplePosition(near, out NavMeshHit nh, 4f, NavMesh.AllAreas)) { Debug.LogWarning("NEON_V24 skip donut " + near); return; }
            Vector3 p = nh.position;
            // 贴一侧墙站着（左右 4 m 内最近的墙），滚动方向沿街
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            foreach (float s in new[] { 1f, -1f })
                if (Physics.Raycast(p + Vector3.up * 1.3f, side * s, out RaycastHit wh, 4.5f, ~0, QueryTriggerInteraction.Ignore) && wh.collider.GetComponentInParent<Fighter>() == null)
                { p = wh.point - side * s * (DonutRadius + .4f); p.y = nh.position.y; break; }
            var go = new GameObject("巨型甜甜圈（可推倒）"); go.transform.SetParent(Root.transform, false); go.transform.position = p;
            var d = go.AddComponent<GiantDonut>(); d.Build(dir); Donuts.Add(d);
        }

        public static void NewRound()
        {
            float a = Random.Range(0f, 360f);
            Wind = Quaternion.Euler(0, a, 0) * Vector3.forward * WindSpeed;
            if (Match.I != null && Match.I.testMode) Wind = Vector3.zero;   // 自动化测试里不吹风（几何断言要稳定）；V24 测试单独开风
            colaHits.Clear(); potHits.Clear(); potReadyAt.Clear(); sneezeAt.Clear(); candyUntil.Clear(); printFrom.Clear(); bitten.Clear();
            ultCamUntil = -9; bigText = null;
            foreach (var p in Props) if (p != null) p.ResetRound();
            Log("wind=" + WindName(Wind));
        }
        public static string WindName(Vector3 w)
        {
            if (w.sqrMagnitude < .01f) return "无风";
            float ang = Mathf.Repeat(Mathf.Atan2(w.x, w.z) * Mathf.Rad2Deg, 360f);
            string[] n = { "北", "东北", "东", "东南", "南", "西南", "西", "西北" };
            return "往" + n[Mathf.RoundToInt(ang / 45f) % 8] + "吹";
        }

        // ---------------------------------------------------------------- 3C 钩子（Fighter.Move / CurrentSpread 调）
        public static float SpeedMul(Fighter f)
        {
            float k = 1f;
            if (f.GetComponent<JellyStuck>() != null) k *= JellySlow;
            k *= V25.SpeedMul(f);   // V25：被珍珠弹打中 ×0.9
            if (BubbleTrail.Boosts(f)) k *= BubbleTrailBoost;
            if (SlickZone.AccelAt(f.transform.position) <= IceAccel + .001f) k *= IceSpeed;   // 冰面上越滑越快
            return k;
        }
        public static float AccelMul(Fighter f)
        {
            // V24 收尾：不再要求 grounded（刚放上去 / 下台阶那几帧 isGrounded 会抖，测试和实机都偶尔判成"没站在冰上"）；区域本身只有 1.2 m 高
            return SlickZone.AccelAt(f.transform.position);
        }
        // 代替 Move 里的 "verticalSpeed -= Gravity * dt"：可乐托举 / 悬浮 / 羽毛缓降
        public static float Vertical(Fighter f, float vs)
        {
            float dt = Time.deltaTime;
            if (!ColaLift.Active.TryGetValue(f, out var lift) || lift == null) return vs - Tune.Gravity * dt;
            return lift.Vertical(vs, dt);
        }
        public static float SpreadAdd(Fighter f) { return ColaLift.Floating(f) ? HoverSpread : 0f; }

        // ---------------------------------------------------------------- 可乐喷泉
        public static void FountainLift(Vector3 c, float radius)
        {
            if (Match.I == null) return;
            foreach (var f in Match.I.fighters)
            {
                if (!f.alive) continue;
                Vector3 d = f.transform.position - c; if (d.y < -1f || d.y > 2.5f) continue; d.y = 0;
                if (d.magnitude > radius) continue;
                ColaLift.Lift(f, c);
            }
        }
        // 喷泉持续期间（ColaFountain.Update 调）：走进水柱 2.6 m 内的人再被托起（正在上升 / 悬浮的不重复托）
        public static void FountainTick(Vector3 c)
        {
            if (Match.I == null) return;
            foreach (var f in Match.I.fighters)
            {
                if (!f.alive) continue;
                if (ColaLift.Active.TryGetValue(f, out var l) && l != null && l.phase != ColaLift.Phase.Feather) continue;
                Vector3 d = f.transform.position - c; if (d.y < -1f || d.y > ColaLiftHeight + 1f) continue; d.y = 0;
                if (d.magnitude > ColaRelift) continue;
                ColaLift.Lift(f, c);
            }
        }
        public static void ColaPuddle(Vector3 ground)
        {
            SlickZone.Spawn("可乐水洼", ground, ColaPuddleRadius, ColaPuddleLife, ColaPuddleAccel, 0, null, new Color(.55f, .3f, .12f, .55f), "food_squish");
        }
        static readonly Dictionary<MentosPile, List<float>> colaHits = new Dictionary<MentosPile, List<float>>();
        static readonly Dictionary<HotpotHazard, List<float>> potHits = new Dictionary<HotpotHazard, List<float>>();
        static readonly Dictionary<HotpotHazard, float> potReadyAt = new Dictionary<HotpotHazard, float>();
        static int Count(List<float> l, float window) { l.RemoveAll(t => Time.time - t > window); return l.Count; }

        // 子弹打到场景（FoodReact.OnImpact 开头调）：可乐瓶 / 火锅
        public static void OnBulletImpact(Fighter attacker, RaycastHit h, WeaponSpec w)
        {
            if (attacker == null || h.collider == null) return;
            var pile = h.collider.GetComponentInParent<MentosPile>();
            if (pile != null)
            {
                if (!pile.Ready) return;
                if (!colaHits.TryGetValue(pile, out var l)) colaHits[pile] = l = new List<float>();
                l.Add(Time.time); int n = Count(l, ColaShotWindow);
                Vector3 c = pile.transform.position + Vector3.up * .85f;
                Sfx.Play("food_can_shake", c, .6f + .1f * n, 1f + .08f * n, 1f, 30f);
                Fx.Burst(c + Vector3.up * .8f, Vector3.up, Color.white, 6 + n * 4, 2f + n, .07f, .4f, .4f);
                var wob = pile.GetComponent<Wobble>(); if (wob == null) wob = pile.gameObject.AddComponent<Wobble>(); wob.Kick(.06f * n);
                if (n >= ColaShots)
                {
                    l.Clear(); ColaBottleBursts++;
                    MentosPile.TryFountain(c, attacker, pile);
                    Log("cola_bottle " + attacker.Hero.name);
                }
                return;
            }
            if (FoodReact.IsHotpot(h.collider))
            {
                HotpotHazard pot = null; float bd = 3.5f;
                foreach (var hz in HotpotHazard.All) if (hz != null) { float d = Links.Flat(hz.transform.position, h.point); if (d < bd) { bd = d; pot = hz; } }
                if (pot == null) return;
                if (potReadyAt.TryGetValue(pot, out float ready) && Time.time < ready) return;
                if (!potHits.TryGetValue(pot, out var l)) potHits[pot] = l = new List<float>();
                l.Add(Time.time); int n = Count(l, HotpotShotWindow);
                Vector3 c = pot.transform.position + Vector3.up * .8f;
                Fx.Burst(c, Vector3.up, new Color(1f, .3f, .12f), 4 + n * 2, 2f + n * .3f, .07f, .5f, 1f);
                if (n >= HotpotShots - 2) Sfx.Play("food_caramel_bubble", c, .5f + .07f * n, .8f, 1f, 25f);   // 快满了：咕嘟咕嘟（预警）
                if (n >= HotpotShots) { l.Clear(); potReadyAt[pot] = Time.time + HotpotRefill; HotpotWave(pot, attacker); }
            }
        }
        public static bool PotRefilling(HotpotHazard h) { return h != null && potReadyAt.TryGetValue(h, out float t) && Time.time < t; }
        static void HotpotWave(HotpotHazard pot, Fighter by)
        {
            Vector3 g = Skills.GroundAt(pot.transform.position + Vector3.up * .5f);
            SlickZone.Spawn("溢出的红油", g, HotpotWaveRadius, HotpotWaveLife, HotpotWaveAccel, HotpotWaveDps, by, new Color(.95f, .25f, .08f, .6f), "food_splat", true);
            Fx.Shockwave(g + Vector3.up * .05f, HotpotWaveRadius, new Color(1f, .3f, .1f), .6f);
            for (int i = 0; i < 3; i++) Fx.Burst(pot.transform.position + Vector3.up, Vector3.up, i == 1 ? new Color(1f, .75f, .2f) : new Color(.95f, .2f, .08f), 40, 6f, .12f, .8f, 1.2f);
            for (int i = 0; i < 10; i++)
            {
                Vector2 rr = Random.insideUnitCircle * HotpotWaveRadius;
                if (Physics.Raycast(g + new Vector3(rr.x, 2f, rr.y), Vector3.down, out RaycastHit gh, 4f, ~0, QueryTriggerInteraction.Ignore)) { var q = FoodFx.Splat(gh.point, gh.normal, new Color(.9f, .2f, .06f), Random.Range(.8f, 1.6f)); if (q != null) Object.Destroy(q, HotpotWaveLife + 2f); }
            }
            Sfx.Play("food_splat", g, 1f, .6f, 1f, 45f); Sfx.Play("food_caramel_bubble", g, 1f, .7f, 1f, 45f);
            SkillPop.World(pot.transform.position + Vector3.up * 2.6f, "火锅溢锅！", new Color(1f, .4f, .2f), null, 1.6f);
            if (Match.I != null) { Match.I.Feed((by != null ? by.Hero.name + " " : "") + "把火锅打溢了 · 红油烫脚又打滑 " + HotpotWaveLife.ToString("0") + " 秒", new Color(1f, .45f, .25f)); Match.I.NoiseAt(by, g); }
            HotpotWaves++; Log("hotpot_wave " + (by != null ? by.Hero.name : "-"));
        }

        // ---------------------------------------------------------------- 芥末呛雾
        static readonly Dictionary<Fighter, float> sneezeAt = new Dictionary<Fighter, float>();
        public static void MustardMushroom(Vector3 pot, float R)
        {
            // 从锅里"轰"地冲起一朵 6 m 高的绿色蘑菇云，再塌下来铺开（规则半径不变，由 Drone.PotRadiusNow 扩散）
            var go = new GameObject("芥末蘑菇云"); go.transform.position = pot; go.AddComponent<Mushroom>().R = R; Skills.spawned.Add(go);
            Sfx.Play("lowFrequency_explosion", pot, .7f, 1.5f, 1f, 60f); Sfx.Play("food_fizz", pot, 1f, .6f, 1f, 50f);
            Fx.Shockwave(pot + Vector3.up * .1f, R * .5f, Skills.Mustard, .5f);
        }
        static void SneezeTick()
        {
            var m = Match.I; if (m == null || m.phase != Phase.Live) return;
            foreach (var f in m.fighters)
            {
                if (!f.alive || Time.time >= f.mustardUntil) continue;
                if (!sneezeAt.TryGetValue(f, out float t)) { sneezeAt[f] = Time.time + Random.Range(.6f, 1.4f); continue; }
                if (Time.time < t) continue;
                sneezeAt[f] = Time.time + Random.Range(SneezeMin, SneezeMax);
                Sneeze(f);
            }
            foreach (var f in m.fighters) if (!f.alive || Time.time >= f.mustardUntil + 1f) sneezeAt.Remove(f);
        }
        public static void Sneeze(Fighter f)
        {
            Sneezes++;
            Sfx.Play("food_sneeze", f.Head, 1f, Random.Range(.95f, 1.12f), 1f, SneezeHear);
            Fx.Burst(f.Head + f.AimDir * .3f, f.AimDir, new Color(.85f, 1f, .5f), 18, 4f, .06f, .4f, .8f);
            if (Skills.Food) SkillPop.World(f.Head + Vector3.up * .6f, "阿嚏！", Skills.Mustard, f.transform, 1f);
            f.revealedUntil = Mathf.Max(f.revealedUntil, Time.time + 1f);
            f.aimRecoveryUntil = Mathf.Max(f.aimRecoveryUntil, Time.time + .25f);   // 喷嚏瞬间准星抖一下（散布 +3° 0.25 s）
            if (f.human) { f.pitch -= 2.5f; Fx.shake = Mathf.Max(Fx.shake, .35f); Tears = 1f; }
            foreach (var e in Match.I.fighters)
                if (e.alive && e.team != f.team && Vector3.Distance(e.transform.position, f.transform.position) < SneezeHear && e.TryGetComponent(out Bot b)) b.Hear(f, f.transform.position, 1.2f);
        }
        public static float Tears;   // 屏幕边缘泪光（V24Hud 画）
        // 芥末雾 + 火（纸杯蛋糕爆炸 / 焦糖火 / 黄油火 / 蜡烛）= 辣油爆燃：一次性火焰伤害，雾随之消失
        public static bool FireAt(Vector3 p, Fighter owner)
        {
            foreach (var d in Drone.Live.ToArray())
            {
                if (d == null || !d.PotMode || d.pot == null) continue;
                Vector3 c = d.pot.transform.position;
                if (Links.Flat(c, p) > d.PotRadiusNow || Mathf.Abs(p.y - c.y) > 3.5f) continue;
                SpicyOil(d, p, owner);
                return true;
            }
            return false;
        }
        static void SpicyOil(Drone d, Vector3 p, Fighter owner)
        {
            SpicyOils++;
            Vector3 c = d.pot.transform.position; Vector3 mid = Vector3.Lerp(c, p, .5f) + Vector3.up * .8f;
            Fx.Explosion(mid, SpicyOilRadius * .6f);
            for (int i = 0; i < 3; i++) Fx.Burst(mid, Vector3.up, i == 0 ? new Color(1f, .35f, .05f) : i == 1 ? new Color(1f, .8f, .2f) : Skills.Mustard, 60, 9f, .16f, .9f, .3f, Fx.Additive);
            Fx.Flash(mid + Vector3.up, new Color(1f, .5f, .1f), 9f, 22f, .7f); Fx.Shake(mid, 1f, 35f);
            Sfx.Play("explosionCrunch", mid, 1f, .8f, 1f, 70f); Sfx.Play("food_caramel_bubble", mid, 1f, 1.3f, 1f, 50f);
            SkillPop.World(mid + Vector3.up * 2f, "辣油爆燃！", new Color(1f, .45f, .1f), null, 1.6f);
            if (Match.I != null)
            {
                foreach (var e in Match.I.fighters)
                {
                    if (!e.alive || owner != null && e.team == owner.team && e != owner) continue;
                    float dd = Vector3.Distance(e.Chest, mid); if (dd > SpicyOilRadius || !Skills.BlastClear(mid, e.Chest, e)) continue;
                    DamageCtx.Set(DamageKind.Fire, "辣油爆燃"); e.ReceiveDamage(SpicyOilDamage * Mathf.Lerp(1f, .4f, dd / SpicyOilRadius), owner, mid, false, true);
                }
                Match.I.Feed("芥末雾碰到火 · 辣油爆燃！", new Color(1f, .5f, .15f));
                Match.I.NoiseAt(owner, mid);
            }
            Object.Destroy(d.gameObject);   // 雾随之消失（Drone.OnDestroy 收掉芥末雾）
            Log("spicy_oil " + (owner != null ? owner.Hero.name : "-"));
        }

        // ---------------------------------------------------------------- 其他联动
        public static void CakeRubble(Links.Breach hole, Fighter owner)
        {
            if (hole == null) return;
            Vector3 n = hole.rot * Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, n).normalized;
            float s = Vector3.Dot(n, owner != null ? owner.transform.position - hole.center : n) >= 0 ? 1f : -1f;   // 碎块落在炸的人这一侧的对面（被炸进去）
            Vector3 g = Skills.GroundAt(hole.center + Vector3.up * .5f);
            int made = 0;
            // V24 修复：原来中间那块正对洞口 1.8 m（side 偏移 0 / ±1.5 m 都在门洞宽度内），NavMesh 被切开，穿洞路线从 6.5 m 绕到 10–12 m（M50 / M91）。
            // 改成散在洞口两侧（门洞宽度外 0.9 m），门洞正前方留出两人并排的通道
            float flank = hole.width * .5f + .9f, third = Random.value < .5f ? 1f : -1f;
            for (int i = 0; i < 3; i++)
            {
                float sideK = i == 0 ? -flank : i == 1 ? flank : third * (flank + .4f), depth = i < 2 ? 1.4f : 2.9f;
                Vector3 p = g - n * s * depth + side * sideK + new Vector3(Random.Range(-.15f, .15f), 0, Random.Range(-.15f, .15f));
                if (!NavMesh.SamplePosition(p, out NavMeshHit nh, 1f, NavMesh.AllAreas) || !Clear(nh.position, .45f, 1.1f)) continue;
                var go = new GameObject("碎蛋糕块（掩体）"); go.transform.position = nh.position; go.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360f), 0);
                go.AddComponent<CakeChunk>().Build(); Skills.spawned.Add(go); made++;
            }
            SlickZone.Spawn("奶油滑地", g, 2.6f, CreamSlickLife, CreamSlickAccel, 0, owner, new Color(1f, .96f, .9f, .6f), "food_cream");
            if (made > 0) { Rubbles++; Log("cake_rubble " + made); }
        }
        static readonly Dictionary<Fighter, float> candyUntil = new Dictionary<Fighter, float>();
        static readonly Dictionary<Fighter, Vector3> printFrom = new Dictionary<Fighter, Vector3>();
        static readonly Dictionary<Fighter, bool> printLeft = new Dictionary<Fighter, bool>();
        static void FootprintTick()
        {
            var m = Match.I; if (m == null || m.phase != Phase.Live) return;
            foreach (var f in m.fighters)
            {
                if (!f.alive || !f.grounded) continue;
                // 被糖黏住（迷雾 / 棉花糖蒸汽 / 糖霜风暴）→ 3 s 内留粉色脚印；踩过糖霜雪地 → 白色脚印
                if (Time.time < f.jammedUntil) candyUntil[f] = Time.time + CandyTrailTime;
                bool candy = candyUntil.TryGetValue(f, out float cu) && Time.time < cu, snow = SnowField.At(f.transform.position);
                if (!candy && !snow) { printFrom.Remove(f); continue; }
                Vector3 pos = f.transform.position;
                if (!printFrom.TryGetValue(f, out Vector3 last)) { printFrom[f] = pos; continue; }
                if (Links.Flat(last, pos) < .65f) continue;
                printFrom[f] = pos;
                bool left = printLeft.TryGetValue(f, out bool pl) && pl; printLeft[f] = !left;
                Vector3 dir = pos - last; dir.y = 0;
                Footprint.Spawn(pos, dir, left, snow ? new Color(.55f, .65f, .9f, .75f) : new Color(1f, .45f, .8f, .85f), snow ? FrostSnowLife + 2f : FootprintLife);
                Footprints++;
            }
        }
        public static void FrostSnow(Vector3 c, float r)
        {
            var go = new GameObject("糖霜雪地"); go.transform.position = Skills.GroundAt(c + Vector3.up); go.AddComponent<SnowField>().Init(r, FrostSnowLife); Skills.spawned.Add(go);
            Log("frost_snow");
        }

        // ---------------------------------------------------------------- 技能 / 大招
        public static void DashTrail(Fighter f) { BubbleTrail.Add(f); }
        public static void BurgerClamp(Fighter victim, Fighter by)
        {
            if (victim == null || !victim.alive) return;
            Clamps++;
            victim.Freeze(BurgerClampTime);
            var go = new GameObject("被夹进汉堡"); go.AddComponent<BurgerClampFx>().Init(victim); Skills.spawned.Add(go);
            if (victim.human) Fx.shake = Mathf.Max(Fx.shake, .5f);
        }
        static readonly HashSet<Fighter> bitten = new HashSet<Fighter>();
        static void BiteTick()
        {
            var m = Match.I; if (m == null || m.phase != Phase.Live || !Skills.Food) return;
            foreach (var f in m.fighters)
            {
                if (!f.alive) continue;
                bool ram = f.Food.UltRamming;
                if (!ram) { bitten.Remove(f); continue; }
                if (bitten.Contains(f)) continue;
                Vector3 fwd = new Vector3(f.velocity.x, 0, f.velocity.z); if (fwd.sqrMagnitude < 4f) fwd = Quaternion.Euler(0, f.yaw, 0) * Vector3.forward;
                if (!Physics.Raycast(f.Chest, fwd.normalized, out RaycastHit h, 1.4f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (h.collider.GetComponentInParent<Fighter>() != null || Mathf.Abs(h.normal.y) > .5f) continue;
                bitten.Add(f);
                var q = BiteMark(h.point, h.normal); if (q != null) Skills.spawned.Add(q);
                Sfx.Play("food_burger_chomp", h.point, 1f, .8f, 1f, 40f); Fx.Burst(h.point, h.normal, UltFx.Bun, 24, 4f, .08f, .6f, 1f);
                BiteMarks++; Log("bite_mark " + f.Hero.name);
            }
        }
        static Texture2D biteTex;
        static GameObject BiteMark(Vector3 p, Vector3 n)
        {
            if (biteTex == null)
            {
                const int N = 128; biteTex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "汉堡咬痕" };
                var px = new Color[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float u = (x + .5f) / N * 2 - 1, v = (y + .5f) / N * 2 - 1, r = Mathf.Sqrt(u * u + v * v), a = Mathf.Atan2(v, u);
                        float edge = .78f + .1f * Mathf.Cos(a * 7f);                                    // 一圈牙印的扇贝边
                        float inside = Mathf.Clamp01((edge - r) * 30f), rim = Mathf.Clamp01(1f - Mathf.Abs(r - edge + .06f) * 14f);
                        float k = Mathf.Max(inside * .85f, rim);
                        px[y * N + x] = new Color(.35f + .4f * rim, .18f + .25f * rim, .08f + .1f * rim, k);
                    }
                biteTex.SetPixels(px); biteTex.Apply();
            }
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); q.name = "汉堡咬痕"; Object.DestroyImmediate(q.GetComponent<Collider>());
            q.transform.position = p + n * .02f; q.transform.rotation = Quaternion.LookRotation(-n); q.transform.localScale = Vector3.one * 1.6f;
            var r2 = q.GetComponent<Renderer>(); r2.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = biteTex, renderQueue = 3001 }; r2.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            q.AddComponent<SplatPop>();
            return q;
        }
        // 纸杯蛋糕：引信剩 0.5 s 唱一句生日歌（程序合成的音盒旋律，不含歌词；35 m 可听 = 引信声距离）
        static AudioClip song;
        public static void BirthdaySong(Vector3 p)
        {
            Songs++;
            if (song == null) song = Synth.Melody("生日歌", new[] { 67, 67, 69, 67, 72, 71 }, new[] { .06f, .06f, .12f, .12f, .12f, .2f }, .9f, 1);
            Synth.PlayAt(song, p, .9f, 35f);
        }
        // 大招：施放者 0.4 s 镜头拉出再拉回；其他人屏幕中央弹一行美食大字
        static float ultCamUntil = -9; static Fighter ultCamWho;
        static string bigText; static Color bigCol; static float bigAt;
        public static string BigText { get { return Time.time - bigAt < 1.6f ? bigText : null; } }
        public static float BigAge { get { return Time.time - bigAt; } }
        public static Color BigColor { get { return bigCol; } }
        public static void OnUlt(Fighter f, Vector3 ground)
        {
            if (f == null || Match.I == null) return;
            var p = Match.I.player;
            V25.OnUlt(f);   // V25：寿司雷达（小地图边缘转一圈回转寿司）
            if (f.human && f == p) { ultCamUntil = Time.time + UltCamTime; ultCamWho = f; UltCams++; f.ShowSelf(UltCamTime); }
            else if (p != null) { bigText = UltShout(f.role); bigCol = f.Hero.tint; bigAt = Time.time; }
        }
        public static string UltShout(Role r)
        {
            switch (r)
            {
                case Role.Falcon: return "双层汉堡冲过来了！";
                case Role.Rivet: return "饭团城墙落地！";
                case Role.Ironwall: return "果冻罩 · Q 弹！";
                case Role.Raven: return "回转寿司开转！";
                case Role.Ember: return "生日蜡烛来了！";
                default: return "糖霜风暴来了！";
            }
        }
        public static Vector3 UltCamOffset(Fighter f)
        {
            if (f != ultCamWho || Time.time >= ultCamUntil) return Vector3.zero;
            float k = 1f - (ultCamUntil - Time.time) / UltCamTime;       // 0 → 1
            float w = Mathf.Sin(k * Mathf.PI);                            // 拉出再拉回
            Vector3 off = new Vector3(.4f * w, UltCamUp * w, -UltCamBack * w);
            // 身后有墙就拉得短一点（不穿墙）
            var cam = f.cam; if (cam == null || cam.transform.parent == null || off.sqrMagnitude < 1e-4f) return off;
            Vector3 world = cam.transform.parent.TransformVector(off); float len = world.magnitude, best = len;
            foreach (var h in Physics.SphereCastAll(f.Eye, .25f, world / len, len, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider.GetComponentInParent<Fighter>() != f && h.distance < best) best = Mathf.Max(0, h.distance - .1f);
            return off * (best / len);
        }
        public static bool UltCamActive(Fighter f) { return f == ultCamWho && Time.time < ultCamUntil; }

        // ---------------------------------------------------------------- 打击感 / 死亡 / 胜利
        public static void HitFood(Vector3 point, Vector3 back, WeaponSpec w, bool head, Fighter victim)
        {
            string id = w != null ? w.id : "";
            switch (id)
            {
                case "SMG-7": Fx.Burst(point, back + Vector3.up * .3f, new Color(.22f, .12f, .08f), 5, 3f, .09f, .7f, 1.6f); break;             // 珍珠
                case "AR-9": Fx.Burst(point, back + Vector3.up * .5f, new Color(1f, .95f, .75f), 6, 3.5f, .1f, .7f, 1.2f); break;               // 爆米花
                case "LMG-2": Fx.Burst(point, back, new Color(1f, .97f, .9f), 6, 2.5f, .06f, .5f, 1.4f); Fx.Burst(point, back, new Color(1f, .5f, .3f), 3, 2f, .1f, .5f, 1.4f); break;   // 米粒 + 鱼片
                case "DMR-3": Fx.Burst(point, back, new Color(.95f, .1f, .15f), 4, 3f, .12f, .6f, 1.4f); break;                                // 山楂
                case "FS-12": Fx.Burst(point, back, new Color(1f, .95f, .82f), 5, 2.5f, .1f, .6f, 1.4f); break;                                // 饺子皮
                case "BR-5": Fx.Burst(point, back, new Color(1f, .62f, .82f), 8, 2f, .1f, .5f, .9f); break;                                    // 奶油
                default:   // 番茄酱：地上溅一摊
                    if (victim != null && Physics.Raycast(victim.transform.position + Vector3.up * .3f, Vector3.down, out RaycastHit g, 1.5f, ~0, QueryTriggerInteraction.Ignore) && Random.value < .5f)
                    { var q = FoodFx.Splat(g.point, g.normal, new Color(.95f, .15f, .12f), .5f); if (q != null) Object.Destroy(q, 6f); }
                    break;
            }
            if (head && victim != null)
            {
                Sfx.Play("food_bonk", point, .6f, 1.6f, 1f, 25f);   // 爆头"啵"
                SkillPop.World(victim.Head + Vector3.up * .45f, "★ ★", new Color(1f, .88f, .25f), victim.transform, .7f);
            }
        }
        public static string Verb(WeaponSpec w, DamageKind k, bool head)
        {
            if (head) return "一口闷了";
            if (w != null)
                switch (w.id)
                {
                    case "SMG-7": return "吸干了"; case "AR-9": return "崩飞了"; case "LMG-2": return "卷走了"; case "DMR-3": return "串起了";
                    case "FS-12": return "包圆了"; case "BR-5": return "抹平了"; default: return "蘸走了";
                }
            switch (k)
            {
                case DamageKind.Fire: return "烤焦了";
                case DamageKind.Grenade: return "炸开花了";
                case DamageKind.Ult: return "吃干抹净了";
                case DamageKind.Skill: return "做成了点心";
                case DamageKind.Bomb: return "端上了桌";
                default: return "煮熟了";
            }
        }
        public static string KillLine(FeedItem it)
        {
            if (it == null || it.victim == null) return "";
            string who = it.killer != null ? it.killer.Hero.name : "厨房";
            string with = it.gun != null ? "用" + it.gun.name + " " : !string.IsNullOrEmpty(it.cause) ? "用" + it.cause + " " : "";
            LastKillLine = who + " " + with + Verb(it.gun, it.kind, it.head) + " " + it.victim.Hero.name;
            return LastKillLine;
        }
        public static void OnDish() { Dishes++; }
        public static void Celebrate(int winner)
        {
            var m = Match.I; if (m == null || winner < 0 || !Skills.Food) return;
            Fighter mvp = null; float best = -1;
            foreach (var f in m.fighters)
            {
                if (f.team != winner) continue;
                float score = f.stats.kills * 100f + f.stats.damage;
                if (score > best) { best = score; mvp = f; }
            }
            foreach (var f in m.fighters)
            {
                if (f.team != winner || !f.alive) continue;
                var go = new GameObject("举杯庆祝 " + f.Hero.name); go.AddComponent<Cheers>().Init(f, f == mvp, false); Skills.spawned.Add(go);
                Toasts++;
            }
            if (mvp != null && !mvp.alive) { var go = new GameObject("主厨帽 " + mvp.Hero.name); go.AddComponent<Cheers>().Init(mvp, true, true); Skills.spawned.Add(go); }
            if (mvp != null) m.Feed("本回合 MVP：" + mvp.Hero.name + " · 戴上主厨帽！", Config.TeamColor(winner));
            Log("toast winner=" + winner + " mvp=" + (mvp != null ? mvp.Hero.name : "-"));
        }

        // ---------------------------------------------------------------- 地图 UI
        public static List<LinkPlus.MapArea> MapAreas()
        {
            var l = new List<LinkPlus.MapArea>();
            foreach (var z in SlickZone.Live) if (z != null && z.Alive) l.Add(new LinkPlus.MapArea { c = z.center, r = z.radius, left = z.until - Time.time, total = z.life, col = new Color(z.color.r, z.color.g, z.color.b, .45f) });
            return l;
        }
        // 每 10 s 闪一次（1.2 s），交火时（3 s 内开过枪 / 挨过打）不闪；k = 0 → 1 的扩散进度
        public static bool LinkFlash(Fighter p, out float k)
        {
            k = 0; if (p == null) return false;
            if (Time.time - p.lastShotAt < 3f || Time.time - p.lastDamageAt < 3f) return false;
            float t = Mathf.Repeat(Time.time, 10f);
            if (t > 1.2f) return false;
            k = t / 1.2f; return true;
        }

        // ---------------------------------------------------------------- 每帧
        public static void Tick()
        {
            SneezeTick(); FootprintTick(); BiteTick();
            Tears = Mathf.MoveTowards(Tears, 0, Time.deltaTime * .8f);
        }
    }

    // ================================================================ 可乐托举 / 悬浮 / 羽毛缓降
    public class ColaLift : MonoBehaviour
    {
        public enum Phase { Rise, Hover, Feather }
        public static readonly Dictionary<Fighter, ColaLift> Active = new Dictionary<Fighter, ColaLift>();
        public Phase phase; Fighter f; float topY, hoverUntil, born; ParticleSystem foam; bool counted;
        public static bool Floating(Fighter f) { return Active.TryGetValue(f, out var l) && l != null && (l.phase == Phase.Hover || l.phase == Phase.Feather && !f.grounded); }
        public static void Lift(Fighter f, Vector3 fountain)
        {
            if (f == null || !f.alive) return;
            if (!Active.TryGetValue(f, out var l) || l == null) { l = f.gameObject.AddComponent<ColaLift>(); l.f = f; Active[f] = l; }
            l.Begin(fountain);
        }
        void Begin(Vector3 fountain)
        {
            f = f != null ? f : GetComponent<Fighter>();
            phase = Phase.Rise; born = Time.time; counted = false;
            topY = fountain.y + V24.ColaLiftHeight;
            float h = Mathf.Max(1f, topY - f.transform.position.y);
            float vy = Mathf.Sqrt(2f * Tune.Gravity * h);
            Vector3 away = f.transform.position - fountain; away.y = 0;
            Vector3 drift = away.sqrMagnitude > .04f ? away.normalized * 1.2f : Quaternion.Euler(0, Random.Range(0, 360f), 0) * Vector3.forward * .8f;
            f.Launch(Vector3.up * vy + drift);
            V24.Lifts++;
            if (foam == null)
            {
                var g = new GameObject("可乐泡沫托"); g.transform.SetParent(transform, false); g.transform.localPosition = Vector3.zero;
                foam = g.AddComponent<ParticleSystem>(); var main = foam.main; main.startLifetime = new ParticleSystem.MinMaxCurve(.6f, 1f); main.startSpeed = new ParticleSystem.MinMaxCurve(.5f, 1.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(.2f, .5f); main.startColor = new ParticleSystem.MinMaxGradient(Color.white, new Color(1f, .8f, .5f)); main.maxParticles = 200; main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = .3f;
                var em = foam.emission; em.rateOverTime = 40; var sh = foam.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = .5f; sh.rotation = new Vector3(90, 0, 0);
                foam.GetComponent<ParticleSystemRenderer>().material = UltFx.SoftMat;
            }
            if (f.human) Sfx.Ui("food_fizz", .5f, .8f);
        }
        public float Vertical(float vs, float dt)
        {
            float y = f.transform.position.y;
            switch (phase)
            {
                case Phase.Rise:
                    vs -= Tune.Gravity * dt;
                    if (vs <= .6f || y >= topY - .2f) { phase = Phase.Hover; hoverUntil = Time.time + V24.ColaHover; if (!counted) { counted = true; V24.Hovers++; } }
                    return vs;
                case Phase.Hover:
                {
                    float target = topY + Mathf.Sin((Time.time - born) * 2.2f) * .18f;   // 泡沫托着慢慢晃
                    if (Time.time >= hoverUntil) phase = Phase.Feather;
                    return Mathf.Clamp((target - y) * 4f, -2.5f, 2.5f);
                }
                default:
                    vs -= Tune.Gravity * V24.ColaFeatherGrav * dt;
                    return Mathf.Max(vs, -V24.ColaFeatherMaxFall);
            }
        }
        void Update()
        {
            if (f == null || !f.alive || Match.I == null || Match.I.phase == NeonSiege.Phase.Prep) { End(); return; }
            if (phase != Phase.Rise) f.revealedUntil = Mathf.Max(f.revealedUntil, Time.time + .3f);   // 悬浮的人全图看得到
            if (foam != null) { var em = foam.emission; em.rateOverTime = phase == Phase.Hover ? 60 : phase == Phase.Feather ? 15 : 30; }
            if (phase == Phase.Feather && f.grounded && Time.time - born > .5f) End();
            if (Time.time - born > 12f) End();
        }
        void End()
        {
            if (f != null && Active.TryGetValue(f, out var l) && l == this) Active.Remove(f);
            if (foam != null) { foam.transform.SetParent(null, true); var em = foam.emission; em.enabled = false; Destroy(foam.gameObject, 1.2f); }
            Destroy(this);
        }
    }

    // 被打的东西晃一晃（可乐瓶）
    public class Wobble : MonoBehaviour
    {
        float amp, t; Quaternion rest; bool init;
        public void Kick(float a) { if (!init) { rest = transform.localRotation; init = true; } amp = Mathf.Max(amp, a); t = 0; }
        void Update()
        {
            if (!init) return;
            t += Time.deltaTime; float k = amp * Mathf.Exp(-t * 4f);
            transform.localRotation = rest * Quaternion.Euler(Mathf.Sin(t * 28f) * k * 57f, 0, Mathf.Cos(t * 23f) * k * 57f);
            if (k < .002f) { transform.localRotation = rest; amp = 0; }
        }
    }

    // ================================================================ 地面区域：可乐水洼 / 冰面 / 红油 / 奶油滑地（加速度变小 = 打滑；可选持续伤害）
    public class SlickZone : MonoBehaviour
    {
        public static readonly List<SlickZone> Live = new List<SlickZone>();
        public Vector3 center; public float radius, until, life, accel, dps; public Fighter owner; public Color color; public string step; public bool hot;
        float born, tick, stepAt; Material mat; Transform disc;
        public bool Alive { get { return Time.time < until; } }
        void OnEnable() { Live.Add(this); }
        void OnDisable() { Live.Remove(this); }
        public static SlickZone Spawn(string name, Vector3 c, float r, float lifeSecs, float accelMul, float dpsAmt, Fighter by, Color col, string stepSound, bool hotOil = false)
        {
            var go = new GameObject(name); go.transform.position = c;
            var z = go.AddComponent<SlickZone>(); z.center = c; z.radius = r; z.life = lifeSecs; z.until = Time.time + lifeSecs; z.accel = accelMul; z.dps = dpsAmt; z.owner = by; z.color = col; z.step = stepSound; z.hot = hotOil; z.born = Time.time;
            z.Build(); Skills.spawned.Add(go);
            return z;
        }
        public bool Inside(Vector3 p) { return Alive && Mathf.Abs(p.y - center.y) < 1.2f && Links.Flat(p, center) <= radius * Grow; }
        float Grow { get { return hot ? Mathf.Clamp01((Time.time - born) / .5f) : 1f; } }   // 红油像浪一样 0.5 s 溢开
        public static float AccelAt(Vector3 p)
        {
            float k = 1f;
            foreach (var z in Live) if (z != null && z.Inside(p)) k = Mathf.Min(k, z.accel);
            return k;
        }
        void Build()
        {
            var d = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(d.GetComponent<Collider>()); d.name = "地面"; d.transform.SetParent(transform, false);
            d.transform.localPosition = Vector3.up * .025f; d.transform.localScale = new Vector3(radius * 2, .004f, radius * 2); disc = d.transform;
            mat = new Material(Shader.Find("Sprites/Default")) { color = color }; var r = d.GetComponent<Renderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            if (accel <= .2f)   // 冰面：亮一圈边
            {
                var ring = new GameObject("冰面边").AddComponent<LineRenderer>(); ring.transform.SetParent(transform); ring.material = Fx.Additive; ring.startWidth = ring.endWidth = .12f;
                ring.startColor = ring.endColor = new Color(.7f, .95f, 1f, .8f); Skills.Circle(ring, center + Vector3.up * .05f, radius);
            }
        }
        void Update()
        {
            float left = until - Time.time;
            if (left <= 0) { Destroy(gameObject); return; }
            if (disc != null) { float g = Grow; disc.localScale = new Vector3(radius * 2 * g, .004f, radius * 2 * g); }
            if (mat != null) mat.color = new Color(color.r, color.g, color.b, color.a * Mathf.Clamp01(left / 1.2f));
            if (Match.I == null) return;
            bool doTick = dps > 0 && Time.time >= tick; if (doTick) tick = Time.time + .25f;
            bool doStep = Time.time >= stepAt; if (doStep) stepAt = Time.time + .35f;
            foreach (var f in Match.I.fighters)
            {
                if (!f.alive || !f.grounded || !Inside(f.transform.position)) continue;
                if (doStep && !string.IsNullOrEmpty(step) && new Vector2(f.velocity.x, f.velocity.z).sqrMagnitude > 2f) Sfx.Play(step, f.transform.position, f.human ? .35f : .5f, 1.5f, f.human ? 0f : 1f, 20f);   // 踩上去"咕叽"
                if (doTick && (owner == null || f.team != owner.team))
                {
                    DamageCtx.Set(DamageKind.Env, hot ? "红油烫脚" : name); f.ReceiveDamage(dps * .25f, owner, center, false);
                    Fx.Burst(f.transform.position + Vector3.up * .2f, Vector3.up, new Color(1f, .35f, .15f), 4, 1.5f, .06f, .4f, -.2f);
                }
            }
        }
    }

    // ================================================================ 可射爆的道具：冰淇淋冰柜 / 爆米花机
    public class ShootProp : MonoBehaviour, IDamageable
    {
        public enum Kind { Freezer, Popcorn }
        public Kind kind; public float hp; public bool broken; Transform look; Vector3 lookScale; float lastHitAt;
        public void Build(Kind k)
        {
            kind = k; hp = k == Kind.Freezer ? V24.FreezerHp : V24.PopcornHp;
            var col = gameObject.AddComponent<BoxCollider>(); col.size = k == Kind.Freezer ? new Vector3(1.3f, 1.5f, .9f) : new Vector3(1f, 1.8f, 1f); col.center = new Vector3(0, col.size.y / 2, 0);
            var ob = gameObject.AddComponent<NavMeshObstacle>(); ob.shape = NavMeshObstacleShape.Box; ob.size = col.size; ob.center = col.center; ob.carving = true;
            look = new GameObject("外观").transform; look.SetParent(transform, false);
            if (k == Kind.Freezer)
            {
                var m = Arena.Model("Toon/Kitchen/Freezer");
                if (m != null) { foreach (var c in m.GetComponentsInChildren<Collider>()) Destroy(c); Arena.Fit(m, new Vector3(0, 1.45f, 0)); m.transform.SetParent(look, false); }
                else Box(look, new Vector3(0, .75f, 0), new Vector3(1.3f, 1.5f, .9f), new Color(.75f, .92f, 1f), .3f);
                Box(look, new Vector3(0, 1.58f, 0), new Vector3(1.2f, .12f, .8f), new Color(1f, .7f, .85f), 1.2f);   // 顶上一圈粉色冰淇淋
                Label(look, "冰淇淋冰柜 · 射爆结冰", new Color(.55f, .85f, 1f), 1.95f);
            }
            else
            {
                Box(look, new Vector3(0, .45f, 0), new Vector3(.95f, .9f, .95f), new Color(.92f, .2f, .22f), .8f);     // 红色小推车
                Box(look, new Vector3(0, 1.25f, 0), new Vector3(.85f, .7f, .85f), new Color(.85f, .95f, 1f), .2f);     // 玻璃箱
                for (int i = 0; i < 9; i++) Ball(look, new Vector3(Random.Range(-.3f, .3f), 1f + Random.Range(0f, .35f), Random.Range(-.3f, .3f)), .16f, new Color(1f, .93f, .65f));
                Box(look, new Vector3(0, 1.66f, 0), new Vector3(1f, .1f, 1f), new Color(1f, .85f, .2f), 1.5f);
                Label(look, "爆米花机 · 射爆起烟", new Color(1f, .85f, .3f), 2f);
            }
            lookScale = look.localScale;
        }
        static void Box(Transform p, Vector3 lp, Vector3 s, Color c, float glow) { var o = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(o.GetComponent<Collider>()); o.transform.SetParent(p, false); o.transform.localPosition = lp; o.transform.localScale = s; o.GetComponent<Renderer>().sharedMaterial = Arena.Glow(c, glow); }
        static void Ball(Transform p, Vector3 lp, float s, Color c) { var o = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(o.GetComponent<Collider>()); o.transform.SetParent(p, false); o.transform.localPosition = lp; o.transform.localScale = Vector3.one * s; o.GetComponent<Renderer>().sharedMaterial = Arena.Glow(c, .6f); }
        static void Label(Transform p, string s, Color c, float y)
        {
            var font = Resources.Load<Font>("NotoSansSC");
            var t = new GameObject("字").AddComponent<TextMesh>(); t.transform.SetParent(p, false); t.transform.localPosition = new Vector3(0, y, 0);
            t.text = s; t.anchor = TextAnchor.MiddleCenter; t.fontSize = 56; t.characterSize = .035f; t.color = c; t.font = font;
            if (font != null) t.GetComponent<MeshRenderer>().sharedMaterial = Arena.WorldText(font);
            t.gameObject.AddComponent<FaceCam>();
        }
        public Vector3 Center { get { return transform.position + Vector3.up * .9f; } }
        public void ResetRound()
        {
            broken = false; hp = kind == Kind.Freezer ? V24.FreezerHp : V24.PopcornHp;
            if (look != null) { look.gameObject.SetActive(true); look.localScale = lookScale; look.localRotation = Quaternion.identity; }
            var c = GetComponent<Collider>(); if (c != null) c.enabled = true;
        }
        public bool TakeDamage(float amount, Fighter source, bool explosive)
        {
            if (broken) return false;
            hp -= amount * (explosive ? 2f : 1f); lastHitAt = Time.time;
            if (kind == Kind.Freezer) { Sfx.Play("impactGlass_heavy", Center, .3f, 1.8f, 1f, 25f); Fx.Burst(Center, Vector3.up, new Color(.8f, .95f, 1f), 5, 2f, .05f, .4f, 1f); }
            else { Sfx.Play("food_popcorn", Center, .5f, 1.2f, 1f, 25f); Fx.Burst(Center + Vector3.up * .3f, Vector3.up, new Color(1f, .93f, .65f), 4, 2.5f, .08f, .5f, 1f); }
            if (look != null) look.localScale = lookScale * (1f + .05f * Mathf.Sin(Time.time * 50f));
            if (hp <= 0) Break(source);
            return true;
        }
        void Break(Fighter by)
        {
            broken = true;
            var col = GetComponent<Collider>(); if (col != null) col.enabled = false;
            Vector3 g = Skills.GroundAt(transform.position + Vector3.up * .5f);
            if (kind == Kind.Freezer)
            {
                SlickZone.Spawn("冰淇淋冰面", g, V24.IceRadius, V24.IceLife, V24.IceAccel, 0, null, new Color(.75f, .93f, 1f, .5f), "impactGlass_heavy");
                Fx.Burst(Center, Vector3.up, new Color(.8f, .95f, 1f), 60, 7f, .12f, .9f, 1f); Fx.Burst(Center, Vector3.up, new Color(1f, .7f, .85f), 30, 5f, .15f, .9f, 1f);
                Fx.Shockwave(g + Vector3.up * .05f, V24.IceRadius, new Color(.75f, .95f, 1f), .5f);
                Sfx.Play("impactGlass_heavy", Center, 1f, .7f, 1f, 45f); Sfx.Play("food_frost_wind", Center, .8f, 1.3f, 1f, 40f);
                SkillPop.World(Center + Vector3.up * 1.6f, "地面结冰！", new Color(.6f, .9f, 1f), null, 1.5f);
                if (look != null) { look.localScale = new Vector3(lookScale.x, lookScale.y * .45f, lookScale.z); look.localRotation = Quaternion.Euler(12, 0, 8); }
                V24.IceSheets++;
                if (Match.I != null) Match.I.Feed((by != null ? by.Hero.name + " " : "") + "射爆了冰柜 · 地面结冰 " + V24.IceLife.ToString("0") + " 秒（滑冰）", new Color(.6f, .9f, 1f));
            }
            else
            {
                var a = SteamArea.Spawn(SteamKind.Dust, this, transform.position + Vector3.up * 1.2f, V24.PopcornRadius, V24.PopcornLife, by);
                if (a != null) { a.popcorn = true; a.Recolor(new Color(1f, .95f, .78f)); }
                var go = new GameObject("爆米花噼啪"); go.transform.position = Center; go.AddComponent<PopcornCrackle>().until = Time.time + V24.PopcornLife; Skills.spawned.Add(go);
                Fx.Burst(Center, Vector3.up, new Color(1f, .95f, .75f), 80, 8f, .12f, 1.2f, 1.2f); Fx.Burst(Center, Vector3.up, new Color(1f, .8f, .3f), 30, 6f, .1f, 1f, 1.2f);
                Sfx.Play("food_popcorn", Center, 1f, .8f, 1f, 50f);
                SkillPop.World(Center + Vector3.up * 1.6f, "爆米花炸开了！", new Color(1f, .85f, .3f), null, 1.5f);
                if (look != null) look.gameObject.SetActive(false);
                V24.PopcornClouds++;
                if (Match.I != null) Match.I.Feed((by != null ? by.Hero.name + " " : "") + "射爆了爆米花机 · 噼啪烟 " + V24.PopcornLife.ToString("0") + " 秒（挡视线 + 盖住脚步声）", new Color(1f, .85f, .35f));
            }
            if (Match.I != null) Match.I.NoiseAt(by, Center);
            if (Match.I != null && Match.I.autoplay) Debug.Log("NEON_V24 r" + Match.I.round + " prop_break " + kind + " by=" + (by != null ? by.Hero.name : "-"));
        }
    }
    // 爆米花云期间：一直噼里啪啦（盖住脚步声：Fighter.Footstep 在云里音量 ×0.25）
    public class PopcornCrackle : MonoBehaviour
    {
        public float until; float next;
        public static readonly List<PopcornCrackle> Live = new List<PopcornCrackle>();
        void OnEnable() { Live.Add(this); }
        void OnDisable() { Live.Remove(this); }
        public static bool Masks(Vector3 p) { foreach (var c in Live) if (c != null && Time.time < c.until && Links.Flat(c.transform.position, p) < V24.PopcornRadius) return true; return false; }
        void Update()
        {
            if (Time.time >= until) { Destroy(gameObject); return; }
            if (Time.time >= next) { next = Time.time + Random.Range(.08f, .2f); Sfx.Play("food_popcorn", transform.position + Random.insideUnitSphere * 2f, .45f, Random.Range(1f, 1.5f), 1f, 35f); if (Random.value < .5f) Fx.Burst(transform.position + Random.insideUnitSphere * 2.5f, Vector3.up, new Color(1f, .95f, .75f), 3, 2f, .1f, .6f, 1f); }
        }
    }

    // ================================================================ 巨型甜甜圈：长按 G 1 s 推倒 → 沿街滚 45 m，撞到的人被推开（20 伤害），砸烂木箱
    public class GiantDonut : Interactable
    {
        public Vector3 dir, home; public bool rolling, done; public Fighter pusher; Transform vis, wheel; float rolled, fallT = -1; Collider col; NavMeshObstacle ob;
        readonly HashSet<Fighter> hitThisRoll = new HashSet<Fighter>();
        public void Build(Vector3 d)
        {
            dir = d.normalized; home = transform.position; holdTime = V24.DonutHold; range = 2.4f;
            transform.rotation = Quaternion.LookRotation(dir);
            vis = new GameObject("甜甜圈").transform; vis.SetParent(transform, false); vis.localPosition = Vector3.up * V24.DonutRadius;
            wheel = new GameObject("滚轮").transform; wheel.SetParent(vis, false);
            var m = Arena.Model("Toon/Food/Donut");
            if (m != null)
            {
                foreach (var c in m.GetComponentsInChildren<Collider>()) Destroy(c);
                Arena.Fit(m, new Vector3(V24.DonutRadius * 2, 0, V24.DonutRadius * 2));
                var holder = new GameObject("站起来").transform; holder.SetParent(wheel, false); holder.localRotation = Quaternion.Euler(0, 0, 90);   // 平放的模型竖起来（轴 = 滚动轴）
                m.transform.SetParent(holder, false); m.transform.localPosition = Vector3.zero;
                // 模型原点在底面：往回挪半个厚度，让圆心在轴上
                var rs = m.GetComponentsInChildren<Renderer>(); if (rs.Length > 0) { Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); m.transform.position -= b.center - wheel.position; }
            }
            else
            {
                var t = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(t.GetComponent<Collider>()); t.transform.SetParent(wheel, false); t.transform.localRotation = Quaternion.Euler(0, 0, 90);
                t.transform.localScale = new Vector3(V24.DonutRadius * 2, .35f, V24.DonutRadius * 2); t.GetComponent<Renderer>().sharedMaterial = Arena.Glow(new Color(1f, .6f, .75f), .8f);
            }
            // 竖着的甜甜圈：碰撞体 = 厚 1 m、高 / 长 = 直径的盒子（站着时挡子弹 / 挡人；滚动时关掉，撞人由代码判定）
            var bc = gameObject.AddComponent<BoxCollider>(); bc.size = new Vector3(1f, V24.DonutRadius * 2, V24.DonutRadius * 2); bc.center = Vector3.up * V24.DonutRadius; col = bc;
            ob = gameObject.AddComponent<NavMeshObstacle>(); ob.shape = NavMeshObstacleShape.Box; ob.size = new Vector3(1.1f, V24.DonutRadius * 2, V24.DonutRadius * 2); ob.center = Vector3.up * V24.DonutRadius; ob.carving = true;
            var font = Resources.Load<Font>("NotoSansSC");
            var tm = new GameObject("推我").AddComponent<TextMesh>(); tm.transform.SetParent(transform, false); tm.transform.localPosition = Vector3.up * (V24.DonutRadius * 2 + .5f);
            tm.text = "G 推倒巨型甜甜圈"; tm.anchor = TextAnchor.MiddleCenter; tm.fontSize = 56; tm.characterSize = .035f; tm.color = new Color(1f, .6f, .8f); tm.font = font;
            if (font != null) tm.GetComponent<MeshRenderer>().sharedMaterial = Arena.WorldText(font);
            tm.gameObject.AddComponent<FaceCam>();
        }
        public override Vector3 Anchor { get { return transform.position + Vector3.up - dir * .9f; } }
        public override bool Usable(Fighter f) { return f.alive && !rolling && !done && Match.I != null && Match.I.CanAct; }
        public override string Prompt(Fighter f) { return "按住 G 推倒巨型甜甜圈 · 滚过整条街（撞到的人被推开）"; }
        protected override void OnStart(Fighter f) { Sfx.Play("food_jelly_boing", transform.position, .6f, .7f, 1f, 25f); }
        protected override void Complete(Fighter f)
        {
            rolling = true; pusher = f; rolled = 0; hitThisRoll.Clear();
            if (col != null) col.enabled = false; if (ob != null) ob.enabled = false;
            Sfx.Play("food_boing", transform.position, 1f, .6f, 1f, 60f);
            if (Match.I != null) { Match.I.Feed((f != null ? f.Hero.name + " " : "") + "推倒了巨型甜甜圈 · 它滚过来了！", new Color(1f, .6f, .8f)); Match.I.NoiseAt(f, transform.position); }
            V24.DonutRolls++;
            if (Match.I != null && Match.I.autoplay) Debug.Log("NEON_V24 r" + Match.I.round + " donut_roll by=" + (f != null ? f.Hero.name : "-"));
        }
        public override void ResetRound()
        {
            base.ResetRound();
            rolling = done = false; rolled = 0; fallT = -1; pusher = null;
            transform.position = home; transform.rotation = Quaternion.LookRotation(dir);
            if (vis != null) vis.localRotation = Quaternion.identity; if (wheel != null) wheel.localRotation = Quaternion.identity;
            if (col != null) col.enabled = true; if (ob != null) ob.enabled = true;
        }
        float rumbleAt;
        void Update()
        {
            if (fallT >= 0)
            {
                fallT += Time.deltaTime; float k = Mathf.Clamp01(fallT / .5f);
                if (vis != null) vis.localRotation = Quaternion.Euler(0, 0, 90f * k * k);
                if (k >= 1f) fallT = -2;
                return;
            }
            if (!rolling) return;
            float step = V24.DonutSpeed * Time.deltaTime;
            Vector3 c = transform.position + Vector3.up * V24.DonutRadius;
            if (Physics.SphereCast(c, V24.DonutRadius * .7f, dir, out RaycastHit hit, step + .15f, ~0, QueryTriggerInteraction.Ignore))
            {
                var dest = hit.collider.GetComponentInParent<Destructible>();
                bool soft = hit.collider.GetComponentInParent<Fighter>() != null || hit.collider.GetComponentInParent<ShootProp>() != null && hit.collider.GetComponentInParent<ShootProp>().broken;
                if (dest != null && dest.Alive) { dest.TakeDamage(9999, pusher, true); Fx.Debris(hit.point, new Color(.8f, .6f, .4f), 10); Sfx.Play("food_bonk", hit.point, 1f, .7f, 1f, 40f); }
                else if (!soft) { Stop(); return; }
            }
            transform.position += dir * step; rolled += step;
            if (wheel != null) wheel.Rotate(step / V24.DonutRadius * Mathf.Rad2Deg, 0, 0, Space.Self);
            // 贴地（下坡 / 台阶）
            if (Physics.Raycast(transform.position + Vector3.up * 1.5f, Vector3.down, out RaycastHit g, 3f, ~0, QueryTriggerInteraction.Ignore) && g.collider.GetComponentInParent<Fighter>() == null)
                transform.position = new Vector3(transform.position.x, Mathf.MoveTowards(transform.position.y, g.point.y, Time.deltaTime * 4f), transform.position.z);
            if (Time.time >= rumbleAt) { rumbleAt = Time.time + .3f; Sfx.Play("impactSoft_heavy", c, .7f, .6f, 1f, 50f); Fx.Burst(transform.position + Vector3.up * .1f, Vector3.up, new Color(1f, .85f, .7f), 6, 2f, .1f, .5f, .5f); Fx.Shake(c, .25f, 14f); }
            if (Match.I != null)
                foreach (var f in Match.I.fighters)
                {
                    if (!f.alive || hitThisRoll.Contains(f)) continue;
                    Vector3 d = f.transform.position - transform.position; if (Mathf.Abs(d.y) > 2f) continue; d.y = 0;
                    if (d.magnitude > V24.DonutRadius + .45f) continue;
                    hitThisRoll.Add(f); V24.DonutHits++;
                    Vector3 side = Vector3.Cross(Vector3.up, dir); float s = Vector3.Dot(d, side) >= 0 ? 1f : -1f;
                    f.Launch((side * s * .7f + dir * .7f).normalized * V24.DonutPush + Vector3.up * 4f);
                    if (pusher == null || f.team != pusher.team || f == pusher) { DamageCtx.Set(DamageKind.Env, "巨型甜甜圈"); f.ReceiveDamage(V24.DonutDamage, pusher != f ? pusher : null, transform.position, false); }
                    Sfx.Play("food_boing", f.Chest, 1f, .8f, 1f, 35f); Fx.Burst(f.Chest, Vector3.up, new Color(1f, .6f, .8f), 20, 4f, .1f, .6f, .8f);
                    if (f.human) Fx.shake = Mathf.Max(Fx.shake, .7f);
                }
            if (rolled >= V24.DonutRange) Stop();
        }
        void Stop()
        {
            rolling = false; done = true; fallT = 0;
            Sfx.Play("food_bonk", transform.position + Vector3.up, 1f, .5f, 1f, 50f); Fx.Burst(transform.position + Vector3.up, Vector3.up, new Color(1f, .6f, .8f), 30, 4f, .12f, .7f, 1f);
            if (col != null) col.enabled = true; if (ob != null) ob.enabled = true;   // 倒下后留在原地当掩体（本回合）
        }
    }

    // ================================================================ 芥末蘑菇云：0.6 s 冲到 6 m 高 → 1.2 s 塌下来铺开
    public class Mushroom : MonoBehaviour
    {
        public float R; float born; ParticleSystem stem, cap;
        ParticleSystem P(string n, Vector3 lp, float s0, float s1, float v0, float v1, float rate, float life, float rad, ParticleSystemShapeType shape, Vector3 rot)
        {
            var g = new GameObject(n); g.transform.SetParent(transform, false); g.transform.localPosition = lp;
            var p = g.AddComponent<ParticleSystem>(); var main = p.main; main.startLifetime = new ParticleSystem.MinMaxCurve(life * .7f, life); main.startSpeed = new ParticleSystem.MinMaxCurve(v0, v1); main.startSize = new ParticleSystem.MinMaxCurve(s0, s1);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(.8f, 1f, .25f, .85f), new Color(.55f, .9f, .15f, .75f)); main.maxParticles = 500; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = p.emission; em.rateOverTime = rate; var sh = p.shape; sh.shapeType = shape; sh.radius = rad; sh.rotation = rot;
            var col = p.colorOverLifetime; col.enabled = true; var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(.2f, 0), new GradientAlphaKey(1, .2f), new GradientAlphaKey(.8f, .7f), new GradientAlphaKey(0, 1) }); col.color = gr;
            g.GetComponent<ParticleSystemRenderer>().material = UltFx.SoftMat; return p;
        }
        void Start()
        {
            born = Time.time;
            stem = P("蘑菇柄", Vector3.up * .6f, .8f, 1.6f, 7f, 10f, 140f, .8f, .7f, ParticleSystemShapeType.Cone, new Vector3(-90, 0, 0));
            var sh = stem.shape; sh.angle = 8f;
            cap = P("蘑菇伞", Vector3.up * 6f, 1.6f, 3f, 1.5f, 3.5f, 0f, 1.6f, 1.5f, ParticleSystemShapeType.Sphere, Vector3.zero);
            var b = cap.emission; b.SetBursts(new[] { new ParticleSystem.Burst(.45f, (short)70) });
            var vel = cap.velocityOverLifetime; vel.enabled = true; vel.x = new ParticleSystem.MinMaxCurve(0f); vel.y = new ParticleSystem.MinMaxCurve(-3.5f); vel.z = new ParticleSystem.MinMaxCurve(0f); vel.space = ParticleSystemSimulationSpace.World;   // 塌下来
            Fx.Burst(transform.position + Vector3.up * .8f, Vector3.up, Skills.Mustard, 40, 8f, .18f, 1f, .2f, UltFx.SoftMat);
            var l = new GameObject("蘑菇云光").AddComponent<Light>(); l.transform.SetParent(transform, false); l.transform.localPosition = Vector3.up * 4f; l.color = Skills.Mustard; l.range = 18f; l.intensity = 5f;
            Destroy(l.gameObject, 1.2f);
        }
        void Update()
        {
            float age = Time.time - born;
            if (age > .6f && stem != null) { var e = stem.emission; e.enabled = false; }
            if (age > 3.5f) Destroy(gameObject);
        }
    }

    // ================================================================ 碎蛋糕块：齐腰高临时掩体 20 s（子弹 / 人都挡，可打碎）
    public class CakeChunk : MonoBehaviour, IDamageable
    {
        float hp = 160f, born; Transform vis;
        public static readonly List<CakeChunk> Live = new List<CakeChunk>();
        void OnEnable() { Live.Add(this); }
        void OnDisable() { Live.Remove(this); }
        public void Build()
        {
            born = Time.time;
            var col = gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(1.2f, 1f, .8f); col.center = new Vector3(0, .5f, 0);
            var ob = gameObject.AddComponent<NavMeshObstacle>(); ob.shape = NavMeshObstacleShape.Box; ob.size = col.size; ob.center = col.center; ob.carving = true;
            vis = new GameObject("蛋糕块").transform; vis.SetParent(transform, false);
            var m = Arena.Model("Toon/Food/SpongeBlock");
            if (m != null) { foreach (var c in m.GetComponentsInChildren<Collider>()) Destroy(c); Arena.Fit(m, new Vector3(1.2f, 1f, .8f)); m.transform.SetParent(vis, false); m.transform.localPosition = Vector3.zero; m.transform.localRotation = Quaternion.Euler(0, 0, Random.Range(-8f, 8f)); }
            else { var c = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(c.GetComponent<Collider>()); c.transform.SetParent(vis, false); c.transform.localPosition = new Vector3(0, .5f, 0); c.transform.localScale = new Vector3(1.2f, 1f, .8f); c.GetComponent<Renderer>().sharedMaterial = Arena.Glow(new Color(1f, .85f, .65f), .3f); }
            var cream = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(cream.GetComponent<Collider>()); cream.transform.SetParent(vis, false); cream.transform.localPosition = new Vector3(0, 1.02f, 0); cream.transform.localScale = new Vector3(1.25f, .1f, .85f); cream.GetComponent<Renderer>().sharedMaterial = Arena.Glow(new Color(1f, .97f, .93f), .6f);
            vis.localScale = Vector3.one * .2f;
            Sfx.Play("food_rice_thud", transform.position, .8f, 1.2f, 1f, 30f);
        }
        void Update()
        {
            float age = Time.time - born;
            if (vis != null) vis.localScale = Vector3.one * Mathf.Min(1f, .2f + age * 4f) * (age > V24.CakeRubbleLife - .5f ? Mathf.Clamp01((V24.CakeRubbleLife - age) / .5f) : 1f);
            if (age > V24.CakeRubbleLife) Destroy(gameObject);
        }
        public bool TakeDamage(float amount, Fighter source, bool explosive)
        {
            hp -= amount;
            Fx.Burst(transform.position + Vector3.up * .7f, Vector3.up, new Color(1f, .88f, .7f), 4, 2f, .06f, .4f, 1.2f);
            if (hp <= 0) { Fx.Debris(transform.position + Vector3.up * .5f, new Color(1f, .85f, .65f), 10); Sfx.Play("food_cream", transform.position, .8f, 1.2f, 1f, 30f); Destroy(gameObject); }
            return true;
        }
    }

    // ================================================================ 脚印（粉色糖丝 / 糖霜雪地）
    public class Footprint : MonoBehaviour
    {
        static Texture2D tex; static readonly Dictionary<int, Material> mats = new Dictionary<int, Material>(); static readonly Queue<GameObject> pool = new Queue<GameObject>();
        float born, life; Material m; Color c;
        public static void Spawn(Vector3 pos, Vector3 dir, bool left, Color col, float lifeSecs)
        {
            if (!Physics.Raycast(pos + Vector3.up * .3f, Vector3.down, out RaycastHit g, .8f, ~0, QueryTriggerInteraction.Ignore)) return;
            if (tex == null)
            {
                const int N = 32; tex = new Texture2D(N, N * 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "脚印" };
                for (int y = 0; y < N * 2; y++) for (int x = 0; x < N; x++)
                    {
                        float u = (x + .5f) / N * 2 - 1, v = (y + .5f) / (N * 2) * 2 - 1;
                        float sole = Mathf.Clamp01((1f - (u * u / .5f + (v - .2f) * (v - .2f) / .45f)) * 6f), heel = Mathf.Clamp01((1f - (u * u / .3f + (v + .62f) * (v + .62f) / .08f)) * 6f);
                        tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Max(sole, heel)));
                    }
                tex.Apply();
            }
            if (pool.Count > 260) { var old = pool.Dequeue(); if (old != null) Destroy(old); }
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); q.name = "脚印"; DestroyImmediate(q.GetComponent<Collider>());
            if (dir.sqrMagnitude < .001f) dir = Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, dir.normalized) * (left ? -.13f : .13f);
            q.transform.position = g.point + g.normal * .012f + side;
            q.transform.rotation = Quaternion.LookRotation(-g.normal, dir.normalized);
            q.transform.localScale = new Vector3(.18f, .32f, 1f);
            int key = Mathf.RoundToInt(col.r * 15) * 256 + Mathf.RoundToInt(col.g * 15) * 16 + Mathf.RoundToInt(col.b * 15);
            if (!mats.TryGetValue(key, out var baseMat)) mats[key] = baseMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex, renderQueue = 3001 };
            var fp = q.AddComponent<Footprint>(); fp.born = Time.time; fp.life = lifeSecs; fp.c = col; fp.m = new Material(baseMat) { color = col };
            var r = q.GetComponent<Renderer>(); r.sharedMaterial = fp.m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            pool.Enqueue(q); Skills.spawned.Add(q);
        }
        void Update()
        {
            float left = life - (Time.time - born);
            if (left <= 0) { Destroy(gameObject); return; }
            if (m != null) m.color = new Color(c.r, c.g, c.b, c.a * Mathf.Clamp01(left / 1.5f));
        }
        void OnDestroy() { if (m != null) Destroy(m); }
    }
    // 糖霜风暴结束后的雪地（5 s）：地面变白，踩过的人留白脚印（FootprintTick 读）
    public class SnowField : MonoBehaviour
    {
        public static readonly List<SnowField> Live = new List<SnowField>();
        float r, until, life; Material mat; ParticleSystem snow;
        void OnEnable() { Live.Add(this); }
        void OnDisable() { Live.Remove(this); }
        public static bool At(Vector3 p) { foreach (var s in Live) if (s != null && Time.time < s.until && Mathf.Abs(p.y - s.transform.position.y) < 1.5f && Links.Flat(p, s.transform.position) < s.r) return true; return false; }
        public void Init(float radius, float secs)
        {
            r = radius; life = secs; until = Time.time + secs;
            var d = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(d.GetComponent<Collider>()); d.name = "糖霜雪"; d.transform.SetParent(transform, false);
            d.transform.localPosition = Vector3.up * .03f; d.transform.localScale = new Vector3(r * 2, .004f, r * 2);
            mat = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 1f, 1f, .6f) }; var rr = d.GetComponent<Renderer>(); rr.sharedMaterial = mat; rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var g = new GameObject("糖霜雪花"); g.transform.SetParent(transform, false); g.transform.localPosition = Vector3.up * 6f;
            snow = g.AddComponent<ParticleSystem>(); var main = snow.main; main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 3.5f); main.startSpeed = new ParticleSystem.MinMaxCurve(.1f, .4f); main.startSize = new ParticleSystem.MinMaxCurve(.06f, .14f);
            main.startColor = new ParticleSystem.MinMaxGradient(Color.white, new Color(1f, .85f, .95f)); main.maxParticles = 500; main.gravityModifier = .08f; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = snow.emission; em.rateOverTime = 120; var sh = snow.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = r; sh.rotation = new Vector3(90, 0, 0);
            snow.GetComponent<ParticleSystemRenderer>().material = UltFx.SoftMat;
            if (Match.I != null) Match.I.Feed("糖霜风暴停了 · 下了一场糖霜雪 · 雪地上的脚印能追踪", new Color(1f, .85f, .97f));
        }
        void Update()
        {
            float left = until - Time.time;
            if (left <= 0) { Destroy(gameObject); return; }
            if (mat != null) mat.color = new Color(1f, 1f, 1f, .6f * Mathf.Clamp01(left / 1.2f));
            if (snow != null && left < 1.5f) { var em = snow.emission; em.enabled = false; }
        }
    }

    // ================================================================ 汽水冲刺的气泡带：2 s，队友踩上去移速 ×1.1
    public class BubbleTrail : MonoBehaviour
    {
        public static readonly List<BubbleTrail> Live = new List<BubbleTrail>();
        public Fighter owner; readonly List<Vector3> pts = new List<Vector3>(); readonly List<float> at = new List<float>(); float fxAt;
        static readonly HashSet<Fighter> counted = new HashSet<Fighter>();
        void OnEnable() { Live.Add(this); }
        void OnDisable() { Live.Remove(this); }
        public static void Add(Fighter f)
        {
            if (f == null || !Skills.Food) return;
            BubbleTrail t = null; foreach (var b in Live) if (b != null && b.owner == f && Time.time - b.LastAt < .3f) t = b;
            if (t == null) { var go = new GameObject("可乐气泡带"); t = go.AddComponent<BubbleTrail>(); t.owner = f; Skills.spawned.Add(go); }
            Vector3 p = Skills.GroundAt(f.transform.position + Vector3.up * .5f);
            if (t.pts.Count == 0 || Links.Flat(t.pts[t.pts.Count - 1], p) > .5f) { t.pts.Add(p); t.at.Add(Time.time); }
        }
        float LastAt { get { return at.Count > 0 ? at[at.Count - 1] : -9; } }
        public static bool Boosts(Fighter f)
        {
            if (f == null || !f.grounded) return false;
            foreach (var t in Live)
            {
                if (t == null || t.owner == null || t.owner.team != f.team) continue;
                for (int i = 0; i < t.pts.Count; i++)
                    if (Time.time - t.at[i] < V24.BubbleTrailLife && Links.Flat(t.pts[i], f.transform.position) < V24.BubbleTrailWidth && Mathf.Abs(t.pts[i].y - f.transform.position.y) < 1f)
                    { if (f != t.owner && counted.Add(f)) V24.BubbleBoosts++; return true; }
            }
            counted.Remove(f);
            return false;
        }
        void Update()
        {
            if (at.Count > 0 && Time.time - LastAt > V24.BubbleTrailLife) { Destroy(gameObject); return; }
            if (Time.time < fxAt) return;
            fxAt = Time.time + .12f;
            for (int i = 0; i < pts.Count; i++)
                if (Time.time - at[i] < V24.BubbleTrailLife && Random.value < .5f)
                    Fx.Burst(pts[i] + Vector3.up * .08f, Vector3.up, i % 2 == 0 ? Skills.Bubble : Skills.Caramel, 2, .8f, .09f, .6f, -.3f, Fx.Additive);
        }
    }

    // ================================================================ 被"夹"进汉堡（0.6 s）
    public class BurgerClampFx : MonoBehaviour
    {
        Fighter v; float born; Transform top, bottom;
        public void Init(Fighter victim)
        {
            v = victim; born = Time.time;
            Transform Bun(float y) { var g = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(g.GetComponent<Collider>()); g.transform.SetParent(transform, false); g.transform.localPosition = Vector3.up * y; g.transform.localScale = new Vector3(1.6f, .55f, 1.6f); g.GetComponent<Renderer>().sharedMaterial = Arena.Glow(UltFx.BunHot, 1.2f); return g.transform; }
            top = Bun(2.6f); bottom = Bun(-.2f);
            var lettuce = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(lettuce.GetComponent<Collider>()); lettuce.transform.SetParent(bottom, false); lettuce.transform.localPosition = Vector3.up * .6f; lettuce.transform.localScale = new Vector3(1.1f, .1f, 1.1f); lettuce.GetComponent<Renderer>().sharedMaterial = Arena.Glow(new Color(.35f, .95f, .3f), 1.5f);
            Sfx.Play("food_burger_chomp", victim.Chest, 1f, .9f, 1f, 35f);
            if (Skills.Food) SkillPop.World(victim.Head + Vector3.up * .7f, "被夹进汉堡！", UltFx.BunHot, victim.transform, .9f);
        }
        void Update()
        {
            if (v == null) { Destroy(gameObject); return; }
            float age = Time.time - born;
            transform.position = v.transform.position;
            float k = Mathf.Clamp01(age / .15f), open = Mathf.Clamp01((age - V24.BurgerClampTime + .15f) / .15f);
            if (top != null) top.localPosition = Vector3.up * Mathf.Lerp(2.6f, 1.75f, k * (1 - open) + 0) + Vector3.up * open * 1.2f;
            if (bottom != null) bottom.localPosition = Vector3.up * Mathf.Lerp(-.2f, .2f, k);
            if (age > V24.BurgerClampTime + .2f) Destroy(gameObject);
        }
    }

    // ================================================================ 死了变成一盘菜，3 s 后被"吃掉"
    public class EatenDish : MonoBehaviour
    {
        float born; Transform dish; int bites; Vector3 full;
        public void Init(Role r)
        {
            born = Time.time;
            var pf = Resources.Load<GameObject>("Toon/Hats/Hat_" + ToonAssets.HatFor(r));
            if (pf != null)
            {
                var g = Instantiate(pf); g.name = "一盘菜"; foreach (var c in g.GetComponentsInChildren<Collider>()) Destroy(c);
                Arena.Fit(g, new Vector3(.9f, 0, .9f));
                dish = new GameObject("菜").transform; dish.SetParent(transform, false); dish.localPosition = Vector3.up * .12f;
                g.transform.SetParent(dish, false); g.transform.localPosition = Vector3.zero;
                full = dish.localScale;
            }
            var steam = new GameObject("热气"); steam.transform.SetParent(transform, false); steam.transform.localPosition = Vector3.up * .5f;
            var ps = steam.AddComponent<ParticleSystem>(); var main = ps.main; main.startLifetime = 1.4f; main.startSpeed = .5f; main.startSize = new ParticleSystem.MinMaxCurve(.15f, .35f); main.startColor = new Color(1, 1, 1, .4f); main.maxParticles = 40;
            var em = ps.emission; em.rateOverTime = 10; var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = .25f; sh.rotation = new Vector3(-90, 0, 0);
            steam.GetComponent<ParticleSystemRenderer>().material = UltFx.SoftMat;
            V24.OnDish();
        }
        void Update()
        {
            float age = Time.time - born;
            if (dish == null) return;
            // 3 s 后三口吃完：每口缩一圈 + 啊呜 + 碎屑
            int want = age < 3f ? 0 : Mathf.Min(3, 1 + Mathf.FloorToInt((age - 3f) / .3f));
            while (bites < want)
            {
                bites++;
                Sfx.Play("food_burger_chomp", transform.position, .7f, 1.1f + bites * .1f, 1f, 25f);
                Fx.Burst(dish.position + Vector3.up * .2f, Vector3.up, new Color(1f, .85f, .6f), 10, 2.5f, .06f, .5f, 1.2f);
            }
            float target = bites == 0 ? 1f : bites == 1 ? .7f : bites == 2 ? .4f : 0f;
            dish.localScale = Vector3.Lerp(dish.localScale, full * target, Time.deltaTime * 14f);
            if (bites >= 3 && dish.localScale.x < full.x * .05f) { Destroy(dish.gameObject); dish = null; }
        }
    }

    // ================================================================ 胜利：举起餐具干杯 + 彩糖；MVP 戴主厨帽
    public class Cheers : MonoBehaviour
    {
        Fighter f; bool mvp, hatOnly; Transform fork, spoon, hat; float born, clinkAt;
        public void Init(Fighter who, bool isMvp, bool onlyHat)
        {
            f = who; mvp = isMvp; hatOnly = onlyHat; born = Time.time; clinkAt = Time.time + .35f;
            Material silver = Arena.Glow(new Color(.92f, .93f, .97f), .6f);
            Transform Prim(PrimitiveType t, Transform p, Vector3 lp, Vector3 s, Material m) { var g = GameObject.CreatePrimitive(t); Destroy(g.GetComponent<Collider>()); g.transform.SetParent(p, false); g.transform.localPosition = lp; g.transform.localScale = s; var r = g.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; return g.transform; }
            if (!hatOnly)
            {
                fork = new GameObject("叉子").transform; fork.SetParent(transform, false);
                Prim(PrimitiveType.Cube, fork, new Vector3(0, .35f, 0), new Vector3(.06f, .7f, .03f), silver);
                for (int i = -1; i <= 1; i++) Prim(PrimitiveType.Cube, fork, new Vector3(i * .045f, .82f, 0), new Vector3(.02f, .25f, .02f), silver);
                spoon = new GameObject("勺子").transform; spoon.SetParent(transform, false);
                Prim(PrimitiveType.Cube, spoon, new Vector3(0, .35f, 0), new Vector3(.06f, .7f, .03f), silver);
                Prim(PrimitiveType.Sphere, spoon, new Vector3(0, .82f, 0), new Vector3(.18f, .26f, .06f), silver);
            }
            if (mvp)
            {
                var white = Arena.Glow(Color.white, .9f);
                hat = new GameObject("主厨帽").transform; hat.SetParent(transform, false);
                Prim(PrimitiveType.Cylinder, hat, Vector3.zero, new Vector3(.42f, .12f, .42f), white);
                Prim(PrimitiveType.Sphere, hat, new Vector3(0, .26f, 0), new Vector3(.62f, .42f, .62f), white);
                Prim(PrimitiveType.Sphere, hat, new Vector3(.12f, .32f, .1f), new Vector3(.35f, .3f, .35f), white);
            }
            if (f != null) { for (int i = 0; i < 4; i++) Fx.Burst(f.Head + Vector3.up * .5f, Vector3.up, FoodFx.Confetti[i], 12, 4f, .06f, 1.2f, .8f); }
        }
        void LateUpdate()
        {
            if (f == null) { Destroy(gameObject); return; }
            float age = Time.time - born;
            Vector3 head = f.alive ? f.Head : f.transform.position + Vector3.up * .3f;
            transform.position = head; transform.rotation = Quaternion.Euler(0, f.yaw, 0);
            float lift = Mathf.Clamp01(age / .3f), sway = Mathf.Sin(age * 8f) * 10f;
            Vector3 right = Vector3.right * .45f;
            if (fork != null) { fork.localPosition = -right + Vector3.up * (.1f + .3f * lift); fork.localRotation = Quaternion.Euler(0, 0, -20f * lift + sway); }
            if (spoon != null) { spoon.localPosition = right + Vector3.up * (.1f + .3f * lift); spoon.localRotation = Quaternion.Euler(0, 0, 20f * lift - sway); }
            if (hat != null) hat.localPosition = Vector3.up * .12f;
            if (fork != null && Time.time >= clinkAt) { clinkAt = Time.time + 1.1f; Sfx.Play("impactGlass_heavy", head + Vector3.up * .6f, .35f, 2.2f, 1f, 25f); Fx.Burst(head + Vector3.up * 1f, Vector3.up, FoodFx.Confetti[Random.Range(0, FoodFx.Confetti.Length)], 10, 3f, .05f, 1f, .7f); }
        }
    }

    // ================================================================ 程序合成音（生日歌音盒 / 环境音 / 紧张版锅铲节奏）
    public static class Synth
    {
        public const int Rate = 22050;
        static float Midi(int n) { return 440f * Mathf.Pow(2f, (n - 69) / 12f); }
        // 音盒：每个音一个衰减的正弦 + 二倍频，durs 是相对时长（自动拉到 total 秒）
        public static AudioClip Melody(string name, int[] notes, float[] durs, float total, int ch)
        {
            float sum = 0; foreach (var d in durs) sum += d;
            int n = Mathf.CeilToInt(Rate * (total + .35f)); var data = new float[n];
            float t0 = 0;
            for (int i = 0; i < notes.Length; i++)
            {
                float f = Midi(notes[i]), len = durs[i] / sum * total; int s0 = Mathf.RoundToInt(t0 * Rate);
                for (int k = 0; k < Rate * (len + .3f) && s0 + k < n; k++)
                {
                    float t = k / (float)Rate, env = Mathf.Exp(-t * 7f) * Mathf.Clamp01(t * 200f);
                    data[s0 + k] += (Mathf.Sin(2 * Mathf.PI * f * t) * .6f + Mathf.Sin(2 * Mathf.PI * f * 2 * t) * .25f + Mathf.Sin(2 * Mathf.PI * f * 3 * t) * .08f) * env * .5f;
                }
                t0 += len;
            }
            var clip = AudioClip.Create(name, n, ch, Rate, false); clip.SetData(data, 0); return clip;
        }
        public static void PlayAt(AudioClip c, Vector3 p, float vol, float maxDist)
        {
            if (c == null) return;
            var go = new GameObject("合成音 " + c.name); go.transform.position = p;
            var s = go.AddComponent<AudioSource>(); s.clip = c; s.spatialBlend = 1; s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 2f; s.maxDistance = maxDist; s.volume = vol; s.dopplerLevel = 0; s.Play();
            Object.Destroy(go, c.length + .2f);
        }
        static float Noise(System.Random r) { return (float)r.NextDouble() * 2 - 1; }
        // 烤串：滋滋（高频噪声 + 随机油星爆点）
        public static AudioClip Sizzle(float secs)
        {
            int n = (int)(Rate * secs); var d = new float[n]; var r = new System.Random(11); float hp = 0, prev = 0, pop = 0;
            for (int i = 0; i < n; i++)
            {
                float x = Noise(r); hp = .92f * (hp + x - prev); prev = x;
                if (r.NextDouble() < .0009) pop = 1f; pop *= .996f;
                float t = i / (float)Rate, sw = .6f + .4f * Mathf.Sin(t * 1.3f) * Mathf.Sin(t * .37f);
                d[i] = (hp * .18f * sw + Noise(r) * pop * .5f);
            }
            return Loop("烤串滋滋", d);
        }
        // 后厨：锅碗瓢盆（随机金属叮当 + 低沉的抽油烟机噪声）
        public static AudioClip Clatter(float secs)
        {
            int n = (int)(Rate * secs); var d = new float[n]; var r = new System.Random(5); float lp = 0;
            for (int i = 0; i < n; i++) { lp += (Noise(r) - lp) * .03f; d[i] = lp * .25f; }
            for (int hit = 0; hit < secs * 1.6f; hit++)
            {
                int s0 = r.Next(0, n - Rate / 2); float f = 900f + (float)r.NextDouble() * 2200f, f2 = f * 1.47f, amp = .15f + (float)r.NextDouble() * .25f;
                for (int k = 0; k < Rate / 2 && s0 + k < n; k++) { float t = k / (float)Rate, env = Mathf.Exp(-t * 9f); d[s0 + k] += (Mathf.Sin(2 * Mathf.PI * f * t) + .6f * Mathf.Sin(2 * Mathf.PI * f2 * t)) * env * amp; }
            }
            return Loop("后厨叮当", d);
        }
        // 甜品长街：八音盒（五声音阶慢旋律循环）
        public static AudioClip MusicBox()
        {
            int[] notes = { 72, 76, 79, 84, 81, 79, 76, 79, 77, 74, 72, 74, 76, 72, 67, 72 };
            var durs = new float[notes.Length]; for (int i = 0; i < durs.Length; i++) durs[i] = i % 4 == 3 ? 2f : 1f;
            return Melody("八音盒", notes, durs, 9.5f, 1);
        }
        // 紧张版：锅铲敲锅的节奏（140 bpm，四拍里第 1、3 拍重）
        public static AudioClip PotBeat(int bars)
        {
            float beat = 60f / 140f; int n = Mathf.RoundToInt(Rate * beat * 4 * bars); var d = new float[n]; var r = new System.Random(9);
            for (int b = 0; b < bars * 8; b++)
            {
                int s0 = Mathf.RoundToInt(b * beat * .5f * Rate); float amp = b % 4 == 0 ? .55f : b % 2 == 0 ? .32f : .16f, f = b % 4 == 0 ? 520f : 760f;
                for (int k = 0; k < Rate / 4 && s0 + k < n; k++) { float t = k / (float)Rate, env = Mathf.Exp(-t * (b % 2 == 0 ? 14f : 26f)); d[s0 + k] += (Mathf.Sin(2 * Mathf.PI * f * t) * .7f + Mathf.Sin(2 * Mathf.PI * f * 2.76f * t) * .3f + Noise(r) * .25f) * env * amp; }
            }
            var clip = AudioClip.Create("锅铲节奏", n, 1, Rate, false); clip.SetData(d, 0); return clip;
        }
        static AudioClip Loop(string name, float[] d)
        {
            int fade = Rate / 3, n = d.Length;
            for (int i = 0; i < fade; i++) { float k = i / (float)fade; d[i] = d[i] * k + d[n - fade + i] * (1 - k); }
            var trimmed = new float[n - fade]; System.Array.Copy(d, trimmed, n - fade);
            var clip = AudioClip.Create(name, n - fade, 1, Rate, false); clip.SetData(trimmed, 0); return clip;
        }
    }

    // ================================================================ 背景音乐随局势 + 分区环境音
    public class V24Audio : MonoBehaviour
    {
        public static V24Audio I; public static bool Tense; public static float TenseK;
        AudioSource beat; readonly List<AudioSource> zones = new List<AudioSource>();
        public static void Ensure(GameObject host) { if (I == null && host != null) I = host.AddComponent<V24Audio>(); }
        void Start()
        {
            if (!ToonAssets.Available) return;
            beat = gameObject.AddComponent<AudioSource>(); beat.clip = Synth.PotBeat(4); beat.loop = true; beat.spatialBlend = 0; beat.volume = 0; beat.Play();
            Zone("烤串巷 · 滋滋烤肉", new Vector3(-2, 1.5f, 20), Synth.Sizzle(9f), .5f, 16f);
            Zone("南后厨 · 锅碗瓢盆", new Vector3(-2, 1.5f, -20), Synth.Clatter(10f), .45f, 16f);
            Zone("甜品长街 · 八音盒", new Vector3(-28, 2.5f, 41), Synth.MusicBox(), .35f, 20f);
            Zone("甜品长街 · 八音盒（南）", new Vector3(-28, 2.5f, -41), Synth.MusicBox(), .3f, 20f);
        }
        void Zone(string n, Vector3 p, AudioClip c, float vol, float max)
        {
            var go = new GameObject(n); go.transform.SetParent(transform); go.transform.position = p;
            var s = go.AddComponent<AudioSource>(); s.clip = c; s.loop = true; s.spatialBlend = 1; s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 3f; s.maxDistance = max; s.volume = vol; s.dopplerLevel = 0;
            s.time = Random.Range(0f, c.length * .9f); s.Play(); zones.Add(s);
        }
        // 只剩最后一人（任一方）或下包后 → 紧张版：音乐加快 12% + 叠一层锅铲敲锅节奏
        public static bool WantTense()
        {
            var m = Match.I; if (m == null || m.phase != Phase.Live || Match.Mode != GameMode.Bomb) return false;
            if (m.Planted) return true;
            int a = 0, b = 0; foreach (var f in m.fighters) if (f.alive) { if (f.team == 0) a++; else b++; }
            return a == 1 || b == 1;
        }
        void Update()
        {
            if (!ToonAssets.Available) return;
            Tense = WantTense();
            TenseK = Mathf.MoveTowards(TenseK, Tense ? 1f : 0f, Time.deltaTime / 1.2f);   // 1.2 s 交叉淡入
            var music = Ambience.Music;
            if (music != null) { music.pitch = Mathf.Lerp(1f, 1.12f, TenseK); music.volume = Mathf.Lerp(.2f, .24f, TenseK); }
            if (beat != null) beat.volume = .32f * TenseK;
        }
    }

    // ================================================================ 每帧逻辑 + HUD（雾里水珠 / 近身剪影 / 泪光 / 大招美食大字）
    public class V24Ticker : MonoBehaviour
    {
        public static V24Ticker I;
        public static void Ensure() { if (I == null) I = new GameObject("V24 体验强化").AddComponent<V24Ticker>(); }
        struct Drop { public Vector2 p; public float r, born, life, speed; }
        readonly List<Drop> drops = new List<Drop>(); SteamArea lastInside; static Texture2D dropTex, blobTex;
        void Update() { V24.Tick(); }
        void LateUpdate()
        {
            var inside = LinkTicker.CamInside;
            if (inside != null && inside != lastInside && inside.Blind)
            {
                // 进雾瞬间：屏幕挂满小水珠，1–2 s 内慢慢滑落
                for (int i = 0; i < 46; i++) drops.Add(new Drop { p = new Vector2(Random.value, Random.Range(-.05f, .9f)), r = Random.Range(6f, 22f), born = Time.time, life = Random.Range(1.1f, 2.2f), speed = Random.Range(.03f, .16f) });
                Sfx.Ui("food_fizz", .25f, .5f);   // 嘶——
            }
            lastInside = inside;
            drops.RemoveAll(d => Time.time - d.born > d.life);
        }
        static Texture2D Disc(int n, bool highlight)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                {
                    float u = (x + .5f) / n * 2 - 1, v = (y + .5f) / n * 2 - 1, r = Mathf.Sqrt(u * u + v * v);
                    float a = highlight ? Mathf.Clamp01((1 - r) * 8f) * (.35f + .4f * Mathf.Clamp01(r)) : Mathf.Clamp01((1 - r) * 3f);
                    float hl = highlight && Vector2.Distance(new Vector2(u, v), new Vector2(-.3f, .35f)) < .22f ? .9f : 0;
                    t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Max(a, hl)));
                }
            t.Apply(); return t;
        }
        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            GUI.depth = 15;
            var o = GUI.color;
            var m = Match.I; var p = m != null ? m.player : null;
            // 水珠
            if (drops.Count > 0)
            {
                if (dropTex == null) dropTex = Disc(32, true);
                foreach (var d in drops)
                {
                    float age = Time.time - d.born, a = Mathf.Clamp01(1 - age / d.life) * .85f;
                    float y = (d.p.y + d.speed * age * age) * Screen.height;
                    GUI.color = new Color(.92f, .96f, 1f, a);
                    GUI.DrawTexture(new Rect(d.p.x * Screen.width - d.r, y - d.r, d.r * 2, d.r * 2.3f), dropTex);
                }
            }
            // 雾里 2.5 m 内的人：深色剪影（远处完全看不见；隔墙不画）
            var cam = Camera.main;
            if (LinkTicker.CamInside != null && LinkTicker.CamInside.Blind && cam != null && m != null)
            {
                if (blobTex == null) blobTex = Disc(48, false);
                foreach (var f in m.fighters)
                {
                    if (!f.alive || f == p) continue;
                    float d = Vector3.Distance(cam.transform.position, f.Chest); if (d > Config.SteamInsideSight) continue;
                    if (!Skills.BlastClear(cam.transform.position, f.Chest, f)) continue;
                    Vector3 sh = cam.WorldToScreenPoint(f.Head + Vector3.up * .2f), sf = cam.WorldToScreenPoint(f.transform.position);
                    if (sh.z <= 0 || sf.z <= 0) continue;
                    float hgt = Mathf.Abs(sf.y - sh.y), wid = hgt * .45f;
                    Rect r = new Rect(sh.x - wid / 2, Screen.height - sh.y, wid, hgt);
                    float a = Mathf.Clamp01((Config.SteamInsideSight - d) / .8f) * .7f;
                    GUI.color = new Color(.12f, .1f, .14f, a); GUI.DrawTexture(r, blobTex);
                    GUI.color = new Color(.12f, .1f, .14f, a * .9f); GUI.DrawTexture(new Rect(r.x + wid * .2f, r.y - wid * .1f, wid * .6f, wid * .6f), blobTex);
                }
            }
            // 被呛：屏幕边缘泪光
            if (V24.Tears > .01f && LinkTicker.CamInside == null)
            {
                if (blobTex == null) blobTex = Disc(48, false);
                GUI.color = new Color(.75f, .95f, .55f, .22f * V24.Tears);
                GUI.DrawTexture(new Rect(-Screen.width * .25f, -Screen.height * .2f, Screen.width * .5f, Screen.height * 1.4f), blobTex);
                GUI.DrawTexture(new Rect(Screen.width * .75f, -Screen.height * .2f, Screen.width * .5f, Screen.height * 1.4f), blobTex);
            }
            // 大招美食大字（其他人屏幕中上方，1.6 s：弹出 → 停 → 淡出）
            string bt = V24.BigText;
            if (!string.IsNullOrEmpty(bt))
            {
                float age = V24.BigAge, pop = age < .15f ? Mathf.Lerp(.4f, 1.15f, age / .15f) : age < .3f ? Mathf.Lerp(1.15f, 1f, (age - .15f) / .15f) : 1f, a = Mathf.Clamp01((1.6f - age) / .4f);
                if (bigStyle == null) { bigStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold }; var font = Resources.Load<Font>("NotoSansSC"); if (font != null) bigStyle.font = font; }
                bigStyle.fontSize = Mathf.RoundToInt(Screen.height * .065f * pop);
                Rect r = new Rect(0, Screen.height * .2f, Screen.width, Screen.height * .12f);
                var c = V24.BigColor;
                GUI.color = new Color(.2f, .08f, .12f, a * .85f); GUI.Label(new Rect(r.x + 3, r.y + 4, r.width, r.height), bt, bigStyle);
                GUI.color = new Color(Mathf.Lerp(c.r, 1f, .35f), Mathf.Lerp(c.g, 1f, .35f), Mathf.Lerp(c.b, 1f, .35f), a); GUI.Label(r, bt, bigStyle);
            }
            GUI.color = o;
        }
        static GUIStyle bigStyle;
    }
}
