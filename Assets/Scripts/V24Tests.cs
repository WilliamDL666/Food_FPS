using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;

namespace NeonSiege
{
    // ================================================================ V24 体验强化的自动化测试（M101 起）
    // 单独一个文件，PlaytestRunner 只加一行调用。私有状态（Bot 的 nesting / 携弹者兜底）用反射读，只在测试里用。
    public static class V24Tests
    {
        static System.Action<string, string, bool, string> Check;
        const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
        static void Place(Fighter f, Vector3 p, float yaw)
        {
            f.StopMantle(); f.botMove = Vector3.zero; f.botSprint = false; f.botTrigger = false;
            f.cc.enabled = false; f.transform.position = p; f.cc.enabled = true;
            f.yaw = yaw; f.pitch = 0; f.transform.rotation = Quaternion.Euler(0, yaw, 0); f.velocity = Vector3.zero;
        }
        static void Hush(Fighter f) { var b = f.GetComponent<Bot>(); if (b != null) b.enabled = false; f.botMove = Vector3.zero; f.botSprint = f.botCrouch = f.botAim = f.botTrigger = false; f.botUse = false; }
        static Fighter Npc(int team, int index) { int i = 0; foreach (var f in Match.I.fighters) if (f.team == team && !f.human && i++ == index) return f; return null; }
        static void Park(Fighter f) { var m = Match.I; f.Respawn(m.arena.SpawnPoint(f.team == m.AttackingTeam, f.slot), 0); Hush(f); }
        static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
        static T Get<T>(object o, string field) { var fi = o.GetType().GetField(field, Priv); return fi != null ? (T)fi.GetValue(o) : default(T); }
        static void Set(object o, string field, object v) { var fi = o.GetType().GetField(field, Priv); if (fi != null) fi.SetValue(o, v); }
        static object Call(object o, string method) { var mi = o.GetType().GetMethod(method, Priv); return mi != null ? mi.Invoke(o, null) : null; }
        static WeaponSpec Gun(string id) { foreach (var w in Config.Arsenal) if (w.id == id) return w; return null; }

        public static IEnumerator Run(System.Action<string, string, bool, string> check)
        {
            Check = check;
            var m = Match.I; var p = m.player;
            m.phase = Phase.Live; m.roundClock = Config.RoundTime;
            int att = m.AttackingTeam, def = m.DefendingTeam;
            FoodTips.AutoEnabled = false; Skills.ClearRound();
            foreach (var f in m.fighters) { Hush(f); Park(f); }
            if (p != null) p.scripted = true;
            var A = Npc(att, 0); var D = Npc(def, 0); var D2 = Npc(def, 1);

            // ---------------- M101：电脑买得到枪（BotBuy 在准备阶段调用）
            {
                bool ok = false; string got = "-";
                if (A != null)
                {
                    m.phase = Phase.Prep; A.Equip(Config.Pistol); A.credits = 9000;
                    m.BotBuy(A); got = A.W.id; ok = A.W != Config.Pistol;
                    m.phase = Phase.Live;
                }
                Check("M101", "电脑买枪（V24 修复）：准备阶段、手里是免费手枪、钱够 → 买到招牌枪（以前 BotBuy 在设准备阶段之前调用，永远买不到）", ok, "买完拿着 " + got);
            }

            // ---------------- M102：两队电脑准度配对（同位置两边一样）
            {
                int oldRound = m.round; m.round = 1; m.BalanceBotSkill(); m.round = oldRound;
                bool ok = true; string s = "";
                for (int slot = 0; slot < 3; slot++)
                {
                    Bot b0 = null, b1 = null;
                    foreach (var f in m.fighters) if (!f.human && f.slot == slot) { var b = f.GetComponent<Bot>(); if (f.team == 0) b0 = b; else b1 = b; }
                    if (b0 != null && b1 != null) { if (!Mathf.Approximately(b0.accuracy, b1.accuracy)) ok = false; s += "位置" + slot + " " + b0.accuracy.ToString("0.00") + "/" + b1.accuracy.ToString("0.00") + " "; }
                }
                Check("M102", "两队电脑强度配对（V24 修复）：每个位置抽一个准度，两队同位置一样（0.45–0.65），平均准度相等", ok, s.Trim());
            }

            // ---------------- M103：携弹者不去水塔 + 卡住 8 s 直奔包点
            {
                bool neverNest = false, direct = false; string why = "-";
                if (A != null)
                {
                    var bot = A.GetComponent<Bot>(); var dmr = Gun("DMR-3"); var oldW = A.W; var oldCarrier = m.BombCarrier;
                    if (bot != null && dmr != null)
                    {
                        A.Equip(dmr); m.BombCarrier = A; neverNest = true;
                        for (int i = 0; i < 30; i++) { Call(bot, "NewRound"); if (Get<bool>(bot, "nesting")) neverNest = false; }
                        // 兜底：路程 8 s 没缩短 → 换成直奔包点
                        Set(bot, "carrierBest", -100f); Set(bot, "carrierBestAt", Time.time - 9f); Set(bot, "carrierDirect", false);
                        Call(bot, "CarrierWatchdog");
                        var route = Get<Vector3[]>(bot, "route");
                        direct = Get<bool>(bot, "carrierDirect") && route != null && route.Length > 0 && (Flat(route[route.Length - 1], Arena.SiteA) < .5f || Flat(route[route.Length - 1], Arena.SiteB) < .5f);
                        why = "路线=" + A.routeName;
                        A.Equip(oldW); m.BombCarrier = oldCarrier;
                    }
                }
                Check("M103", "携弹者（V24 修复）：带炸弹的神射手 30 次分配都不走水塔狙击位；路程 8 s 没缩短（卡住）→ 丢掉原路线直奔包点", neverNest && direct, "从不上水塔=" + neverNest + "，卡住直奔=" + direct + "，" + why);
            }

            // ---------------- M104 / M105：可乐托举 → 悬浮 → 羽毛缓降；悬浮能开枪但散布 +2°
            {
                bool rose = false, hovered = false, feather = false, spread = false; float top = 0, minVy = 0;
                MentosPile pile = null; foreach (var mp in MentosPile.All) if (mp != null) { pile = mp; break; }
                if (pile != null && D != null)
                {
                    Vector3 c = pile.transform.position;
                    Vector3 at = c + new Vector3(1.8f, .05f, 0);
                    if (NavMesh.SamplePosition(at, out NavMeshHit nh, 2f, NavMesh.AllAreas)) at = nh.position + Vector3.up * .05f;
                    Place(D, at, 0); yield return new WaitForFixedUpdate(); yield return null;
                    V24.FountainLift(c, 5.5f);
                    for (float t = 0; t < 1.6f; t += Time.deltaTime) { top = Mathf.Max(top, D.transform.position.y); yield return null; }
                    rose = top >= c.y + V24.ColaLiftHeight - 1.5f;
                    hovered = ColaLift.Floating(D);
                    spread = Mathf.Approximately(V24.SpreadAdd(D), V24.HoverSpread);
                    for (float t = 0; t < V24.ColaHover + 2.5f && D.alive; t += Time.deltaTime) { if (ColaLift.Active.TryGetValue(D, out var l) && l != null && l.phase == ColaLift.Phase.Feather) { feather = true; minVy = Mathf.Min(minVy, D.velocity.y); } yield return null; }
                    if (!feather) feather = false; else feather = minVy >= -V24.ColaFeatherMaxFall - .3f;
                    Park(D);
                }
                Check("M104", "可乐喷泉托举（V24）：被托到 ≈ " + V24.ColaLiftHeight + " m → 顶上悬浮 " + V24.ColaHover + " s → 羽毛缓降（下落不超过 " + V24.ColaFeatherMaxFall + " m/s）", rose && hovered && feather,
                    "最高 " + (top - (pile != null ? pile.transform.position.y : 0)).ToString("0.0") + " m，悬浮中=" + hovered + "，缓降最快 " + (-minVy).ToString("0.0") + " m/s");
                Check("M105", "悬浮时能开枪，但散布 +" + V24.HoverSpread + "°（全图可见）", spread, "SpreadAdd=" + (D != null ? V24.SpreadAdd(D).ToString("0.0") : "-"));
            }

            // ---------------- M106：可乐瓶打 4 枪喷泉
            {
                bool ok = false; int f0 = MentosPile.Fountains;
                MentosPile pile = null; foreach (var mp in MentosPile.All) if (mp != null) { pile = mp; break; }
                if (pile != null && A != null)
                {
                    pile.ResetRound();
                    Vector3 c = pile.transform.position + Vector3.up * .85f, from = c + new Vector3(-3f, .3f, 0);
                    if (Physics.Raycast(from, (c - from).normalized, out RaycastHit h, 5f, ~0, QueryTriggerInteraction.Ignore) && h.collider.GetComponentInParent<MentosPile>() != null)
                    {
                        for (int i = 0; i < V24.ColaShots - 1; i++) V24.OnBulletImpact(A, h, A.W);
                        bool notYet = MentosPile.Fountains == f0;
                        V24.OnBulletImpact(A, h, A.W);
                        ok = notYet && MentosPile.Fountains == f0 + 1;
                    }
                    yield return new WaitForSeconds(.2f);
                    foreach (var f in m.fighters) Park(f);
                    pile.ResetRound();
                }
                Check("M106", "可乐瓶也能开枪打爆（V24）：打 " + (V24.ColaShots - 1) + " 枪摇晃、第 " + V24.ColaShots + " 枪喷泉", ok, "喷泉次数 " + f0 + " → " + MentosPile.Fountains);
            }

            // ---------------- M107：火锅连打 7 枪溢锅（烫人 + 打滑，30 s 回满）
            {
                bool ok = false, slick = false; string where = "-";
                foreach (var hz in HotpotHazard.All)
                {
                    if (hz == null || A == null) continue;
                    Vector3 c = hz.transform.position;
                    foreach (var off in new[] { new Vector3(0, 2.5f, -1.2f), new Vector3(1.2f, 2.5f, 0), new Vector3(0, 2.5f, 1.2f), new Vector3(-1.2f, 2.5f, 0) })
                    {
                        Vector3 from = c + off;
                        if (!Physics.Raycast(from, (c + Vector3.up * .5f - from).normalized, out RaycastHit h, 4f, ~0, QueryTriggerInteraction.Ignore) || !FoodReact.IsHotpot(h.collider)) continue;
                        int w0 = V24.HotpotWaves;
                        for (int i = 0; i < V24.HotpotShots; i++) V24.OnBulletImpact(A, h, A.W);
                        ok = V24.HotpotWaves == w0 + 1 && V24.PotRefilling(hz);
                        foreach (var z in SlickZone.Live) if (z != null && z.hot && Flat(z.center, c) < 2f) slick = z.accel < 1f && z.dps > 0;
                        where = hz.name; break;
                    }
                    if (where != "-") break;
                }
                Check("M107", "火锅溢锅（V24 新机关）：对火锅连开 " + V24.HotpotShots + " 枪 → 红油溢出 " + V24.HotpotWaveRadius + " m、" + V24.HotpotWaveLife + " s（烫 " + V24.HotpotWaveDps + "/s + 打滑），之后 " + V24.HotpotRefill + " s 才回满",
                    ok && slick, "锅=" + where + "，溢锅=" + ok + "，红油区=" + slick);
                Skills.ClearRound();
            }

            // ---------------- M108：冰柜射爆结冰面 / 爆米花机射爆起烟（挡视线）
            {
                ShootProp fz = null, pc = null; foreach (var sp in V24.Props) if (sp != null) { if (sp.kind == ShootProp.Kind.Freezer && fz == null) fz = sp; if (sp.kind == ShootProp.Kind.Popcorn && pc == null) pc = sp; }
                bool ice = false, slide = false, smoke = false; string diag = "";
                if (fz != null && D != null)
                {
                    fz.ResetRound(); fz.TakeDamage(999, A, false);
                    foreach (var z in SlickZone.Live) if (z != null && z.accel <= V24.IceAccel + .01f && Flat(z.center, fz.transform.position) < 2f) ice = true;
                    Vector3 at = fz.transform.position + fz.transform.forward * 2f;
                    if (NavMesh.SamplePosition(at, out NavMeshHit nh, 1.5f, NavMesh.AllAreas))
                    {
                        if (!D.alive) Park(D);
                        Place(D, nh.position + Vector3.up * .05f, 0); yield return new WaitForFixedUpdate(); yield return new WaitForSeconds(.15f);
                        slide = V24.AccelMul(D) <= V24.IceAccel + .01f;
                        diag = "（存活=" + D.alive + " 着地=" + D.grounded + " 区域系数=" + SlickZone.AccelAt(D.transform.position).ToString("0.00") + "）";
                    }
                    fz.ResetRound(); Park(D);
                }
                if (pc != null)
                {
                    pc.ResetRound(); pc.TakeDamage(999, A, false); yield return new WaitForSeconds(.3f);
                    Vector3 c = pc.transform.position + Vector3.up * 1.2f;
                    foreach (var a in SteamArea.Live) if (a != null && a.popcorn) smoke = SteamArea.BlocksSight(c + Vector3.left * (V24.PopcornRadius + 2f), c + Vector3.right * (V24.PopcornRadius + 2f)) && PopcornCrackle.Masks(pc.transform.position);
                    pc.ResetRound();
                }
                Check("M108", "新机关（V24）：射爆冰柜 → 地面结冰 " + V24.IceRadius + " m / " + V24.IceLife + " s（加速度 ×" + V24.IceAccel + "，滑冰）；射爆爆米花机 → " + V24.PopcornRadius + " m 噼啪烟挡视线 + 盖住脚步声",
                    fz != null && pc != null && ice && slide && smoke, "冰柜=" + (fz != null) + " 结冰=" + ice + " 站上去打滑=" + slide + diag + "；爆米花机=" + (pc != null) + " 挡视线+盖脚步=" + smoke + "（道具 " + V24.Props.Count + " 个）");
                Skills.ClearRound(); SteamArea.EndAll();
            }

            // ---------------- M109：推倒巨型甜甜圈 → 沿街滚，撞开前面的人
            {
                bool rolled = false, hit = false; float moved = 0; GiantDonut dn = V24.Donuts.Count > 0 ? V24.Donuts[0] : null;
                if (dn != null && A != null && D != null)
                {
                    dn.ResetRound(); Vector3 start = dn.transform.position;
                    Vector3 ahead = start + dn.dir * 8f; if (NavMesh.SamplePosition(ahead, out NavMeshHit nh, 1.5f, NavMesh.AllAreas)) ahead = nh.position;
                    Place(D, ahead + Vector3.up * .05f, 0); Place(A, dn.Anchor - Vector3.up * .9f, 0); yield return new WaitForFixedUpdate(); yield return null;
                    int h0 = V24.DonutHits;
                    dn.Hold(A, V24.DonutHold + .05f);
                    for (float t = 0; t < 2f; t += Time.deltaTime) yield return null;
                    moved = Flat(dn.transform.position, start); rolled = dn.rolling || dn.done; hit = V24.DonutHits > h0;
                    dn.ResetRound(); Park(A); Park(D);
                }
                Check("M109", "巨型甜甜圈（V24 新机关）：长按 G " + V24.DonutHold + " s 推倒 → 以 " + V24.DonutSpeed + " m/s 沿街滚（最多 " + V24.DonutRange + " m），撞到的人被推开", dn != null && rolled && moved > 6f && hit,
                    "甜甜圈 " + V24.Donuts.Count + " 个，滚动=" + rolled + "，2 s 滚了 " + moved.ToString("0.0") + " m，撞到人=" + hit);
            }

            // ---------------- M110：整片蒸汽 12–14 m、随风飘
            {
                bool size = Config.SteamAreaMid >= 12f && Config.SteamAreaMid <= 14f && Config.SteamAreaPot >= 12f;
                bool drift = false; float moved = 0;
                SteamValve v = null; foreach (var x in SteamValve.Valves) if (x != null && !(x is HotpotKnob) && x.transform.position.x > 5f) { v = x; break; }
                if (v != null)
                {
                    v.ResetRound(); V24.Wind = Vector3.right * V24.WindSpeed;
                    v.Fire(null); yield return null;
                    var a = v.area; Vector3 c0 = a != null ? a.center : Vector3.zero;
                    yield return new WaitForSeconds(2f);
                    if (a != null) { moved = Flat(a.center, c0); drift = moved > 1.5f && moved < 2.6f; }
                    V24.Wind = Vector3.zero; SteamArea.EndAll(); v.ResetRound();
                }
                Check("M110", "整片蒸汽（V24）：半径 " + Config.SteamAreaMid + " m（12–14）、" + Config.SteamBloom + " s 滚开；每回合随机风向，雾团往下风处飘 " + V24.WindSpeed + " m/s（最多 " + V24.WindMaxDrift + " m）", size && drift, "半径合格=" + size + "，2 s 飘了 " + moved.ToString("0.0") + " m");
            }

            // ---------------- M111：芥末呛雾 14–16 m；被呛的人打喷嚏；芥末雾 + 火 = 辣油爆燃（雾消失）
            {
                bool size = Config.MustardPotRadius >= 14f && Config.MustardPotRadius <= 16f, sneeze = false, oil = false;
                if (D != null)
                {
                    int s0 = V24.Sneezes; D.mustardUntil = Time.time + 4.5f;
                    yield return new WaitForSeconds(4f);
                    sneeze = V24.Sneezes > s0; D.mustardUntil = 0;
                }
                HotpotHazard pot = null; foreach (var hz in HotpotHazard.All) if (hz != null) { pot = hz; break; }
                if (pot != null && A != null)
                {
                    var go = new GameObject("测试芥末签"); var dr = go.AddComponent<Drone>(); dr.InitPot(A, pot.transform.position + Vector3.up, Vector3.up, pot);
                    yield return new WaitForSeconds(Config.MustardSpread + .1f);
                    int o0 = V24.SpicyOils; bool fired = V24.FireAt(pot.transform.position + Vector3.right * 2f, A); yield return null;
                    oil = fired && V24.SpicyOils == o0 + 1 && dr == null;
                    if (dr != null) Object.Destroy(dr.gameObject);
                }
                foreach (var f in m.fighters) if (!f.alive) Park(f);
                Check("M111", "芥末呛雾（V24）：范围 " + Config.MustardPotRadius + " m（14–16）；被呛的人每 " + V24.SneezeMin + "–" + V24.SneezeMax + " s 打喷嚏（暴露位置）；芥末雾碰到火 = 辣油爆燃，雾随之消失", size && sneeze && oil, "半径合格=" + size + "，打喷嚏=" + sneeze + "，辣油爆燃=" + oil);
                Skills.ClearRound();
            }

            // ---------------- M112：美食动词播报
            {
                var smg = Gun("SMG-7");
                string v1 = V24.Verb(smg, DamageKind.Gun, false), v2 = V24.Verb(smg, DamageKind.Gun, true);
                string line = A != null && D != null ? V24.KillLine(new FeedItem { killer = A, victim = D, gun = smg, kind = DamageKind.Gun }) : "";
                bool ok = v1 == "吸干了" && v2 == "一口闷了" && smg != null && line.Contains("用" + smg.name) && line.Contains("吸干了");
                Check("M112", "击杀播报美食动词（V24）：珍珠奶茶 = 吸干了，爆头 = 一口闷", ok, "\"" + line + "\" / 爆头 \"" + v2 + "\"");
            }

            // ---------------- M113：死了变成一盘菜，3 s 后被吃掉
            {
                bool dish = false, gone = false;
                if (D2 != null && ToonAssets.Available)
                {
                    int d0 = V24.Dishes;
                    DamageCtx.Set(DamageKind.Env, "测试"); D2.ReceiveDamage(999, null, D2.transform.position, false);
                    yield return new WaitForSeconds(1.2f);
                    dish = V24.Dishes > d0 && Object.FindObjectsOfType<EatenDish>().Length > 0;
                    yield return new WaitForSeconds(4.5f);
                    gone = Object.FindObjectsOfType<EatenDish>().Length == 0;
                    Park(D2);
                }
                Check("M113", "死亡（V24）：倒下变成一盘对应帽子的菜，3 s 后三口被\"吃掉\"消失", dish && gone, "出现=" + dish + "，吃掉=" + gone);
            }

            // ---------------- M114：紧张版音乐（下包 / 只剩最后一人）
            {
                var old = m.bomb; m.bomb = BombState.Planted; bool planted = V24Audio.WantTense(); m.bomb = old;
                bool calm = !V24Audio.WantTense() || CountAlive(m, 0) == 1 || CountAlive(m, 1) == 1;
                Check("M114", "背景音乐随局势（V24）：下包后 / 任一方只剩最后一人 → 切紧张版（加快 + 锅铲节奏）；平时是夜市轻音乐", planted && calm, "下包=" + planted + "，平时=" + calm);
            }

            // ---------------- 收尾
            Skills.ClearRound(); SteamArea.EndAll(); V24.Wind = Vector3.zero;
            foreach (var dn in V24.Donuts) if (dn != null) dn.ResetRound();
            foreach (var sp in V24.Props) if (sp != null) sp.ResetRound();
            FoodTips.AutoEnabled = true;
            if (p != null) { p.scripted = false; Hush(p); }
            foreach (var f in m.fighters) Park(f);
            m.roundClock = Config.RoundTime; m.phase = Phase.Live;
            yield return null;
        }
        static int CountAlive(Match m, int team) { int n = 0; foreach (var f in m.fighters) if (f.alive && f.team == team) n++; return n; }
    }
}
