using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace NeonSiege
{
    // ================================================================ V23 d：联动放大 + 副联动 + 硬约束的自动化测试（M85–M100）和截图（90–98）
    // 单独一个文件，PlaytestRunner 只加调用（并行改 PlaytestRunner 的人不冲突）。
    public static class LinkTests
    {
        static System.Action<string, string, bool, string> Check;
        static void Place(Fighter f, Vector3 p, float yaw)
        {
            f.StopMantle(); f.botMove = Vector3.zero; f.botSprint = false; f.botTrigger = false;
            f.cc.enabled = false; f.transform.position = p; f.cc.enabled = true;
            f.yaw = yaw; f.pitch = 0; f.transform.rotation = Quaternion.Euler(0, yaw, 0); f.velocity = Vector3.zero;
        }
        static void Hush(Fighter f) { var b = f.GetComponent<Bot>(); if (b != null) b.enabled = false; f.botMove = Vector3.zero; f.botSprint = f.botCrouch = f.botAim = f.botTrigger = false; f.botUse = false; }
        static Fighter Npc(int team, int index) { int i = 0; foreach (var f in Match.I.fighters) if (f.team == team && !f.human && i++ == index) return f; return null; }
        static Fighter NpcOr(int team, int index) { return Npc(team, index) ?? Npc(1 - team, index) ?? Npc(1 - team, 0); }
        static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
        static float YawTo(Vector3 from, Vector3 to) { Vector3 d = to - from; return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }
        static void Park(Fighter f) { var m = Match.I; f.Respawn(m.arena.SpawnPoint(f.team == m.AttackingTeam, f.slot), 0); Hush(f); }
        static SteamValve ValveAt(Vector3 p) { foreach (var v in SteamValve.Valves) if (v != null && !(v is HotpotKnob) && Flat(v.transform.position, p) < 1f) return v; return null; }
        static void Reset()
        {
            Skills.ClearRound(); SteamCloud.ClearTimed(); Pings.Clear();
            foreach (var fz in Object.FindObjectsOfType<FireZone>()) Object.Destroy(fz.gameObject);
            foreach (var v in SteamValve.Valves.ToArray()) if (v != null) v.ResetRound();
        }
        // 找一个离 target r 米、NavMesh 上、和 target 之间只看实体几何是通的位置
        static bool Spot(Vector3 target, float r, System.Func<Vector3, bool> extra, out Vector3 at)
        {
            for (int i = 0; i < 24; i++)
            {
                Vector3 p = target + Quaternion.Euler(0, i * 15f, 0) * Vector3.forward * r;
                if (!NavMesh.SamplePosition(p, out NavMeshHit h, .5f, NavMesh.AllAreas) || Mathf.Abs(h.position.y - target.y) > .4f) continue;
                Vector3 g = h.position;
                if (!Skills.BlastClear(g + Vector3.up * 1.6f, target + Vector3.up * 1.3f, null)) continue;
                if (extra != null && !extra(g)) continue;
                at = g; return true;
            }
            at = target; return false;
        }

        public static IEnumerator Run(System.Action<string, string, bool, string> check)
        {
            Check = check;
            var m = Match.I; var ar = m.arena; var p = m.player;
            m.phase = Phase.Live; m.roundClock = Config.RoundTime;
            int att = m.AttackingTeam, def = m.DefendingTeam;
            var saved = new Dictionary<Fighter, Role>(); foreach (var f in m.fighters) saved[f] = f.role;
            foreach (var sh in ar.shutters) sh.ResetRound();
            foreach (var w in ar.breakWalls) w.ResetRound();
            FoodTips.AutoEnabled = false; Reset();
            foreach (var f in m.fighters) { Hush(f); if (!f.human) Park(f); }
            if (p != null) { p.scripted = true; Hush(p); Park(p); }
            yield return new WaitForSeconds(.3f);
            var A = Npc(att, 0); var A2 = NpcOr(att, 1); var D = Npc(def, 0); var D2 = Npc(def, 1); var D3 = NpcOr(def, 2);

            // ---------------- M85：整片蒸汽（中庭 9 m / 后厨 7 m、10 s、冷却 40 s；判定半径 = 显示半径；外面看不进、里面只看 2.5 m；AI 同一判定；子弹 / 爆炸不受影响；地图有雾区）
            {
                var valve = ValveAt(new Vector3(16, 0, 8)); var kitchen = ValveAt(new Vector3(.35f, 0, -21f));
                bool geoIn = false, geoOut = false, near2 = false, far4 = false, outIn = false, clearBefore = false, aiOut = false, aiNear = false, aiFar = false, blast = false, bullet = false, mapOk = false, timing = false;
                float rr = 0; string where = "-";
                if (valve != null && kitchen != null && D != null && A != null && D2 != null)
                {
                    Vector3 c = valve.CloudCenter, foeP = new Vector3(14.3f, 0, 8);
                    Place(D, foeP + Vector3.up * .05f, 0); D.hp = 100; D.armor = 50; yield return new WaitForFixedUpdate(); yield return null;
                    float R = Config.SteamAreaMid;   // V24：半径 9 → 13 m，几何取点都改成相对半径
                    bool sOut = Spot(foeP, R + 2f, g => Flat(g, c) > R + .6f, out Vector3 outP);
                    bool sNear = Spot(foeP, 2f, g => Flat(g, c) < R - 1f, out Vector3 nearP);
                    bool sFar = Spot(foeP, 3.6f, g => Flat(g, c) < R - 1f, out Vector3 farP);
                    where = sOut + "/" + sNear + "/" + sFar;
                    Place(A, outP + Vector3.up * .05f, YawTo(outP, foeP)); Place(D2, nearP + Vector3.up * .05f, YawTo(nearP, foeP)); yield return null;
                    clearBefore = Skills.SightClear(A.Eye, D.Chest, D);
                    valve.Fire(null); yield return new WaitForSeconds(Config.SteamBloom + .25f);
                    var a = valve.area; rr = a != null ? a.R : 0;
                    float y = 1.6f;
                    geoIn = SteamArea.BlocksSight(new Vector3(c.x - R - 6, y, c.z + R - .4f), new Vector3(c.x + R + 6, y, c.z + R - .4f));
                    geoOut = !SteamArea.BlocksSight(new Vector3(c.x - R - 6, y, c.z + R + .5f), new Vector3(c.x + R + 6, y, c.z + R + .5f));
                    near2 = !SteamArea.BlocksSight(new Vector3(c.x - 1, y, c.z), new Vector3(c.x + 1, y, c.z));
                    far4 = SteamArea.BlocksSight(new Vector3(c.x - 2, y, c.z), new Vector3(c.x + 2, y, c.z));
                    outIn = SteamArea.BlocksSight(new Vector3(c.x - R - 3, y, c.z), new Vector3(c.x - 1, y, c.z));
                    aiOut = sOut && !Skills.SightClear(A.Eye, D.Chest, D) && !Skills.SightClear(A.Eye, D.Head, D);   // Bot.CanSee 就是这两条
                    aiNear = sNear && Skills.SightClear(D2.Eye, D.Chest, D);
                    Place(D2, farP + Vector3.up * .05f, YawTo(farP, foeP)); yield return null;
                    aiFar = sFar && !Skills.SightClear(D2.Eye, D.Chest, D) && !Skills.SightClear(D2.Eye, D.Head, D);
                    blast = Skills.BlastClear(A.Eye, D.Chest, D);
                    float hp0 = D.hp + D.armor; Bullet.Fire(A, A.Eye, A.Eye, (D.Chest - A.Eye).normalized, Color.white);
                    yield return new WaitForSeconds(.4f); bullet = D.hp + D.armor < hp0;
                    foreach (var z in LinkPlus.MapAreas()) if (Flat(z.c, c) < .5f && Mathf.Abs(z.r - R) < .3f && z.total > Config.SteamAreaLife - .1f) mapOk = true;
                    timing = a != null && Mathf.Abs(a.until - a.born - Config.SteamAreaLife) < .01f && Mathf.Abs(valve.CooldownLeft - (SteamValve.Cooldown - Config.SteamBloom - .65f)) < .3f && Mathf.Approximately(kitchen.areaRadius, Config.SteamAreaKitchen);
                    Reset();
                }
                Check("M85", "整片蒸汽：中庭蒸汽阀 " + Config.SteamAreaMid + " m / 后厨 " + Config.SteamAreaKitchen + " m、" + Config.SteamAreaLife + " s、冷却 " + Config.SteamAreaCooldown + " s；判定半径 = 显示半径（半径内 0.4 m 处挡、外 0.5 m 处不挡）；雾里 2 m 看得见、4 m 看不见；外面看不进去；AI 走同一个 SightClear；子弹照样打中、爆炸线不受影响；地图上画出雾区",
                    clearBefore && rr == Config.SteamAreaMid && geoIn && geoOut && near2 && far4 && outIn && aiOut && aiNear && aiFar && blast && bullet && mapOk && timing,
                    "喷前看得见=" + clearBefore + "，半径 " + rr + "，R−0.4 m 挡=" + geoIn + " R+0.5 m 通=" + geoOut + "，雾里 2 m 通=" + near2 + " 4 m 挡=" + far4 + "，外看内挡=" + outIn
                    + "；AI：雾外 11 m 看不见=" + aiOut + "，雾里 2 m 看得见=" + aiNear + "，雾里 3.6 m 看不见=" + aiFar + "（取点 " + where + "）；爆炸线通=" + blast + "，子弹打中=" + bullet + "，地图雾区=" + mapOk + "，时长/冷却/后厨半径=" + timing);
            }

            // ---------------- M86：进雾关阀（任何人长按 G 0.5 s；1.75 s 内散；拧开的人 3 s 内不会误关；关了冷却照算）
            {
                var valve = ValveAt(new Vector3(16, 0, 8));
                bool fired = false, ownerNo = false, otherYes = false, closed = false, gone = false, cdKeep = false; float held = 0;
                if (valve != null && A != null && D != null)
                {
                    Place(A, valve.transform.position + new Vector3(-1.3f, .05f, 0), 90); Place(D, valve.transform.position + new Vector3(1.3f, .05f, 0), -90); yield return null;
                    float t = 0; while (!fired && t < 1.5f) { fired = valve.Hold(A, Time.deltaTime); t += Time.deltaTime; yield return null; }
                    yield return new WaitForSeconds(1.5f);
                    ownerNo = !valve.Usable(A); otherYes = valve.Usable(D);
                    while (!closed && held < 1.5f) { closed = valve.Hold(D, Time.deltaTime); held += Time.deltaTime; yield return null; }
                    closed = closed && !valve.Active;
                    yield return new WaitForSeconds(Config.SteamCloseFade + .1f);
                    Vector3 c = valve.CloudCenter;
                    gone = !SteamArea.BlocksSight(new Vector3(c.x - 6, 1.6f, c.z), new Vector3(c.x + 6, 1.6f, c.z));
                    cdKeep = valve.CooldownLeft > 30f && !valve.Usable(D);
                    Reset();
                }
                Check("M86", "进雾关阀（反制）：喷着的蒸汽阀任何人走进去长按 G " + Config.SteamCloseHold + " s 关掉，" + Config.SteamCloseFade + " s 内散开；拧开的人 3 s 内一直按着 G 不会误关；关掉后冷却照算",
                    fired && ownerNo && otherYes && closed && Mathf.Abs(held - Config.SteamCloseHold) < .15f && gone && cdKeep && LinkPlus.ValveClosed > 0,
                    "拧开=" + fired + "，拧开的人 1.5 s 时不能关=" + ownerNo + "，别人能关=" + otherYes + "，长按 " + held.ToString("0.00") + " s 关掉=" + closed + "，散开=" + gone + "，冷却照算=" + cdKeep);
            }

            // ---------------- M87：关口火锅"开大火"（2 口锅各 1 个旋钮；长按 0.8 s；锅心 9 m 整片蒸汽；同样能进雾关火）
            {
                int nearPot = 0; bool fired = false, rOk = false, prompt = false, closed = false; float held = 0; string at = "";
                foreach (var k in LinkPlus.Knobs) if (k != null && k.pot != null && Flat(k.transform.position, k.pot.transform.position) < 3.3f && NavMesh.SamplePosition(k.transform.position, out _, .7f, NavMesh.AllAreas)) { nearPot++; at += k.transform.position.ToString("F1") + " "; }
                HotpotKnob kn = LinkPlus.Knobs.Count > 0 ? LinkPlus.Knobs[0] : null;
                if (kn != null && A != null && D != null)
                {
                    Place(A, kn.transform.position + kn.transform.forward * .9f + Vector3.up * .05f, 0); Place(D, kn.transform.position - kn.transform.right * 1f + Vector3.up * .05f, 0); yield return null;
                    prompt = kn.Prompt(A).Contains("开大火") && kn.MarkerLabel(A).Contains("开大火");
                    while (!fired && held < 2f) { fired = kn.Hold(A, Time.deltaTime); held += Time.deltaTime; yield return null; }
                    rOk = kn.area != null && Mathf.Approximately(kn.area.R, Config.SteamAreaPot) && Flat(kn.area.center, kn.pot.transform.position) < .1f;
                    yield return new WaitForSeconds(1.5f);
                    float t = 0; while (!closed && t < 1.5f) { closed = kn.Hold(D, Time.deltaTime); t += Time.deltaTime; yield return null; }
                    Reset();
                }
                Check("M87", "关口火锅\"开大火\"：两口锅各 1 个旋钮（锅边 3.3 m 内、能走到）；长按 G " + Config.KnobHold + " s → 锅心 " + Config.SteamAreaPot + " m 整片蒸汽；提示写\"开大火\"；同样能进雾关掉",
                    LinkPlus.Knobs.Count == 2 && nearPot == 2 && fired && Mathf.Abs(held - Config.KnobHold) < .15f && rOk && prompt && closed,
                    "旋钮 " + LinkPlus.Knobs.Count + " 个（" + at.Trim() + "），长按 " + held.ToString("0.00") + " s 喷出=" + fired + "，9 m 在锅心=" + rOk + "，提示=" + prompt + "，关火=" + closed);
            }

            // ---------------- M88：盲区上限（全图同时 ≤ 2 片；满了蒸汽阀拧不开、棉花糖蒸汽不另开；粉尘不算；地图上双方都画）
            {
                var v1 = ValveAt(new Vector3(16, 0, 8)); var v2 = ValveAt(new Vector3(16, 0, -8)); var v3 = ValveAt(new Vector3(.35f, 0, -21f));
                bool two = false, third = false, candyNo = false, dustOk = false, mapBoth = false, after = false; int maxSeen = 0;
                if (v1 != null && v2 != null && v3 != null && A != null)
                {
                    v1.Fire(null); v2.Fire(null); yield return null;
                    two = SteamArea.BlindCount == 2;
                    third = !v3.Usable(A); v3.Fire(null); third = third && v3.area == null && SteamArea.BlindCount == 2;
                    HotpotHazard pot = null; foreach (var h in HotpotHazard.All) if (h != null) pot = h;
                    candyNo = pot != null && CandySteam.Spawn(pot, pot.SteamCenter, Time.time + 5f, A) == null && SteamArea.BlindCount == 2;
                    var dust = SteamArea.Spawn(SteamKind.Dust, null, new Vector3(-40, 1.2f, 4), Config.CakeDustRadius, Config.CakeDustLife, A);
                    dustOk = dust != null && SteamArea.BlindCount == 2;
                    int white = 0; foreach (var z in LinkPlus.MapAreas()) if (z.col.r > .9f && z.col.g > .9f && z.col.b > .9f) white++;
                    mapBoth = white >= 2;   // MapUI.DynamicAreas 不按队伍过滤 → 双方小地图 / 大地图都画
                    maxSeen = LinkPlus.BlindMaxEver;
                    Reset(); yield return null;
                    after = SteamArea.BlindCount == 0 && v3.Usable(A);
                }
                Check("M88", "盲区上限（硬约束）：全图同时最多 " + Config.SteamAreaMax + " 片整片蒸汽（含棉花糖蒸汽）；满了第 3 个阀门拧不开、迷雾罩火锅也不会再开一片；开墙粉尘不算；雾区画在双方地图上（带剩余时间环）；回合重置清空",
                    two && third && candyNo && dustOk && mapBoth && after && maxSeen <= Config.SteamAreaMax,
                    "两片=" + two + "，第 3 个拧不开=" + third + "，棉花糖不另开=" + candyNo + "，粉尘不占名额=" + dustOk + "，地图白色雾区 ≥ 2=" + mapBoth + "，清空后能再拧=" + after + "，本次运行盲区最多 " + maxSeen + " 片");
            }

            // ---------------- M89：芥末呛雾（12 m / 9 s；1.75 s 扩散；雾里敌人透视 + 减速；芥末味穿透蒸汽；自己人不受影响）
            {
                HotpotHazard potN = null; foreach (var h in HotpotHazard.All) if (h != null && h.transform.position.z > 0) potN = h;
                HotpotKnob knobN = null; foreach (var k in LinkPlus.Knobs) if (k != null && k.pot == potN) knobN = k;
                var raven = A2; var foe = D; var mate = A;
                bool pot = false, early = false, full = false, rev = false, steamHid = false, slow = false, mateOk = false; float r06 = 0, r20 = 0, ratio = 0, vOut = 0, vIn = 0;
                if (potN != null && raven != null && foe != null && mate != null && raven != mate)
                {
                    if (raven.role != Role.Raven) raven.ChangeRole(Role.Raven);
                    Vector3 pc = potN.transform.position;
                    Place(raven, new Vector3(pc.x, .05f, 48.6f), 180);
                    Vector3 fp = pc + new Vector3(-6.5f, 0, -3.5f); Place(foe, fp + Vector3.up * .05f, 90); Place(mate, pc + new Vector3(3, .05f, -3.5f), 0);
                    foe.revealedUntil = 0; yield return new WaitForFixedUpdate(); yield return null;
                    // 雾外先量一次走路速度
                    foe.botMove = Vector3.right; yield return new WaitForSeconds(.35f); Vector3 s0 = foe.transform.position; yield return new WaitForSeconds(.5f); vOut = Flat(foe.transform.position, s0) / .5f; foe.botMove = Vector3.zero;
                    Place(foe, fp + Vector3.up * .05f, 90); yield return null;
                    if (knobN != null) knobN.Fire(null);   // 同时开大火：芥末味要能穿透整片蒸汽
                    Skills.ShootArrow(raven, Skills.ArrowVelocityTo(raven.Eye, pc + Vector3.up * .55f));
                    float t = 0; Drone d = null; while (t < 1f && (d = Links.PotDrone(potN)) == null) { t += Time.deltaTime; yield return null; }
                    pot = d != null;
                    if (d != null)
                    {
                        yield return new WaitForSeconds(.6f); r06 = d.PotRadiusNow; early = r06 < Config.MustardPotRadius * .6f;
                        yield return new WaitForSeconds(1.4f); r20 = d.PotRadiusNow; full = Mathf.Approximately(r20, Config.MustardPotRadius);
                        rev = foe.Revealed; steamHid = !Skills.SightClear(raven.Eye, foe.Chest, foe);
                        foe.botMove = Vector3.right; yield return new WaitForSeconds(.35f); Vector3 s1 = foe.transform.position; yield return new WaitForSeconds(.5f); vIn = Flat(foe.transform.position, s1) / .5f; foe.botMove = Vector3.zero;
                        ratio = vOut > .1f ? vIn / vOut : 0; slow = Mathf.Abs(ratio - Config.MustardSlow) < .06f && foe.mustardUntil > Time.time - .5f;
                        mateOk = mate.mustardUntil < Time.time && !mate.Revealed;
                    }
                    Reset();
                }
                Check("M89", "芥末呛雾：签子进锅 → " + Config.MustardPotRadius + " m / " + Config.MustardPotLife + " s，" + Config.MustardSpread + " s 从锅口扩到最大；雾里的敌人透视 + 移速 ×" + Config.MustardSlow + "；同时开大火时芥末味穿透蒸汽（看不见但被透视）；自己人不受影响",
                    pot && early && full && rev && steamHid && slow && mateOk,
                    "进锅=" + pot + "，0.6 s 半径 " + r06.ToString("0.0") + " m、2 s 半径 " + r20.ToString("0.0") + " m，敌人透视=" + rev + "（同时被蒸汽挡住视线=" + steamHid + "），走路 " + vOut.ToString("0.00") + " → " + vIn.ToString("0.00") + " m/s = ×" + ratio.ToString("0.00") + "，队友不受影响=" + mateOk);
            }

            // ---------------- M90：可乐弹射（×1.6，落点都在 NavMesh 上；落地 3.5 m 内敌人被推开、准星晃、不掉血）
            {
                bool launched = false, pushed = false, noDmg = false, shake = false, navAll = true; float push = 0, colaD = 0; string pads = "";
                foreach (var jp in JumpPad.All)
                {
                    if (jp == null) continue;
                    Vector3 v = jp.WorldLaunch, hz = new Vector3(v.x, 0, v.z); if (hz.sqrMagnitude < .01f) continue;
                    float b = LinkPlus.ColaBoost(jp.transform.position, hz.normalized, hz.magnitude, v.y);
                    bool ok = LinkPlus.LandsOnNav(jp.transform.position, hz.normalized * hz.magnitude * b + Vector3.up * v.y);
                    navAll &= ok; pads += "×" + b.ToString("0.00") + (ok ? "" : "!") + " ";
                }
                if (p != null && D != null)
                {
                    p.Respawn(new Vector3(-66, .05f, 38.5f), 90); p.scripted = true; Hush(p); if (p.role != Role.Falcon) p.ChangeRole(Role.Falcon);
                    var padGo = new GameObject("测试弹射板"); padGo.transform.position = new Vector3(-61, 0, 38.5f); padGo.transform.rotation = Quaternion.Euler(0, 90, 0);
                    var tp = padGo.AddComponent<JumpPad>(); tp.Build(new Color(1f, .8f, .3f)); tp.launch = new Vector3(0, 9f, 5f);
                    yield return null;
                    Vector3 h = new Vector3(5f, 0, 0); float b = LinkPlus.ColaBoost(padGo.transform.position, Vector3.right, 5f, 9f);
                    float flight = 2f * 9f / Tune.Gravity; Vector3 land = padGo.transform.position + Vector3.right * 5f * b * flight;
                    Place(D, land + new Vector3(1.2f, .05f, .6f), -90); D.hp = 100; D.armor = 50; D.aimRecoveryUntil = 0; Vector3 d0 = D.transform.position;
                    Place(p, new Vector3(-65.5f, .05f, 38.5f), 90); yield return new WaitForFixedUpdate(); yield return null; yield return null;
                    p.skillCharges = 2; float t0 = JumpPad.LastLaunchAt;
                    Skills.Cast(p, p.transform.position + Vector3.right * 7);
                    float w = 0; while (JumpPad.LastLaunchAt == t0 && w < 1.5f) { w += Time.deltaTime; yield return null; }
                    launched = JumpPad.LastWasCola && JumpPad.LastLaunchAt != t0; Vector3 from = JumpPad.LastLaunchPos;
                    yield return new WaitForSeconds(.3f); w = 0; while (!p.grounded && w < 3f) { w += Time.deltaTime; yield return null; }
                    colaD = Flat(p.transform.position, from);
                    yield return new WaitForSeconds(.15f); shake = D.aimRecoveryUntil > Time.time - .2f;
                    yield return new WaitForSeconds(.6f);
                    push = Flat(D.transform.position, d0); pushed = push >= .8f && LinkPlus.LastColaHits.Contains(D);
                    noDmg = D.hp >= 100 && D.armor >= 50;
                    Object.Destroy(padGo);
                    Reset();
                }
                Check("M90", "可乐弹射：水平 ×" + Config.DashPadBoost + "，地图上 " + JumpPad.All.Count + " 个弹床按冲刺方向算的落点都在 NavMesh 上（落不下就自动截短）；落地「可乐炸罐」" + Config.ColaImpactRadius + " m 内的敌人被推开、准星晃 " + Config.ColaImpactShake + " s，不掉血",
                    launched && navAll && pushed && noDmg && shake,
                    "冲刺踩板=" + launched + "（飞 " + colaD.ToString("0.0") + " m），弹床倍率 [" + pads.Trim() + "] 全部能落=" + navAll + "，敌人被推开 " + push.ToString("0.0") + " m=" + pushed + "，不掉血=" + noDmg + "，准星晃=" + shake);
            }

            // ---------------- M91：开墙（门洞 ≥ 3.5 × 2.6 m，NavMesh 两人并排；炸开扬起 2.5 s 蛋糕粉尘挡视线；补洞宽度跟洞走）
            {
                BreakWall wA = null; foreach (var w in ar.breakWalls) if (w != null && w.transform.position.z > 0) wA = w;
                var ember = A2; var rv = D3;
                bool hole = false, dust = false, dustGone = false, lanes = false, patchW = false; float hw = 0, hh = 0, l1 = -1, l2 = -1, pw = 0;
                if (wA != null && ember != null && rv != null)
                {
                    if (ember.role != Role.Ember) ember.ChangeRole(Role.Ember);
                    wA.ResetRound(); Place(ember, new Vector3(37, .05f, 11f), 0); yield return new WaitForSeconds(.3f);
                    Vector3 face = wA.transform.TransformPoint(new Vector3(0, 1.2f, -.2f));
                    WallPiece wp = null; float bd = 9f; foreach (var q in wA.pieces) { float dd = Vector3.Distance(q.transform.position, face); if (dd < bd) { bd = dd; wp = q; } }
                    new GameObject("测试纸杯（粘墙）").AddComponent<BlastCharge>().Init(ember, face, wp);
                    yield return new WaitForSeconds(Config.BlastWarning + .2f);
                    var o = wA.Opening(); hole = o != null; if (o != null) { hw = o.width; hh = o.height; }
                    Vector3 n = wA.transform.forward, rgt = wA.transform.right, hc = o != null ? o.center : wA.transform.position;
                    dust = SteamArea.BlocksSight(hc + n * 4f + Vector3.up * 1.4f, hc - n * 4f + Vector3.up * 1.4f);
                    yield return new WaitForSeconds(Config.CakeDustLife + .3f);
                    dustGone = !SteamArea.BlocksSight(hc + n * 4f + Vector3.up * 1.4f, hc - n * 4f + Vector3.up * 1.4f);
                    l1 = PlaytestRunner.PathLen(hc + n * 3f + rgt * .75f, hc - n * 3f + rgt * .75f); l2 = PlaytestRunner.PathLen(hc + n * 3f - rgt * .75f, hc - n * 3f - rgt * .75f);
                    lanes = l1 > 0 && l1 < 8f && l2 > 0 && l2 < 8f;
                    if (rv.role != Role.Rivet) rv.ChangeRole(Role.Rivet);
                    Place(rv, hc - n * 3f + Vector3.up * .05f, YawTo(hc - n * 3f, hc)); yield return null;
                    if (o != null && Links.PatchTarget(rv, o.center, out var br, out bool busy) && !busy) { rv.skillCharges = 2; Skills.Cast(rv, o.center); foreach (var c in DeployedCover.Live) if (c != null && c.patched && c.owner == rv) pw = c.w; }
                    patchW = Mathf.Abs(pw - hw - .06f) < .05f;
                    foreach (var fz in Object.FindObjectsOfType<FireZone>()) Object.Destroy(fz.gameObject);
                    Reset(); wA.ResetRound(); yield return new WaitForSeconds(.3f);
                }
                Check("M91", "开墙：门洞 ≥ 3.5 × " + Config.BlastWallDoorHeight + " m，NavMesh 左右各 0.75 m 两条线都能穿过（两人并排）；炸开瞬间洞口 " + Config.CakeDustLife + " s 蛋糕粉尘挡视线（同一套整片蒸汽判定），之后散掉；铆钉补洞宽度跟洞走",
                    hole && hw >= 3.5f && hh >= 2.6f && dust && dustGone && lanes && patchW,
                    "门洞 " + hw.ToString("0.0") + " × " + hh.ToString("0.0") + " m，粉尘挡视线=" + dust + " 散掉=" + dustGone + "，两条穿洞路线 " + l1.ToString("0.0") + " / " + l2.ToString("0.0") + " m，补洞宽 " + pw.ToString("0.00") + " m（洞 " + hw.ToString("0.00") + "）");
            }

            // ---------------- M92：果冻粘身（穿过门帘的敌人透墙可见 2.5 s，之后消失；铁幕全队都能看到）+ 入口保底读数
            int openA = -1;
            {
                Shutter aSh = null; foreach (var sh in ar.shutters) if (sh != null && sh.name.Contains("A 连廊")) aSh = sh;
                var iw = A; var walker = D;
                bool docked = false, passed = false, stuck = false, revealed = false, faded = false;
                if (aSh != null && iw != null && walker != null)
                {
                    if (iw.role != Role.Ironwall) iw.ChangeRole(Role.Ironwall);
                    aSh.ResetRound(); aSh.SetOpen(true); yield return new WaitForSeconds(2.1f);
                    Place(iw, new Vector3(20, .05f, 10.5f), 0); yield return null;
                    var fr = Links.DockFrame(iw, out _); iw.skillCharges = 1; if (fr != null) Skills.Cast(iw, iw.transform.position + Vector3.forward * 5);
                    var cur = fr != null ? Links.CurtainOf(fr) : null; docked = cur != null;
                    openA = LinkPlus.OpenEntrances(0);
                    Place(iw, new Vector3(6, .05f, 4), 0);
                    Place(walker, new Vector3(20, .05f, 18f), 180); walker.revealedUntil = 0; yield return null;
                    walker.botMove = Vector3.back; float wt = 0;
                    while (wt < 4f && walker.transform.position.z > 12f) { yield return null; wt += Time.deltaTime; }
                    walker.botMove = Vector3.zero; passed = walker.transform.position.z <= 12.5f;
                    yield return new WaitForSeconds(.3f);
                    stuck = walker.GetComponent<JellyStuck>() != null; revealed = walker.Revealed;
                    yield return new WaitForSeconds(Config.JellyStickTime + .3f);
                    faded = !walker.Revealed && walker.GetComponent<JellyStuck>() == null;
                    if (cur != null) Object.Destroy(cur.gameObject);
                    aSh.ResetRound(); Reset(); yield return new WaitForSeconds(.2f);
                }
                Check("M92", "果冻粘身：A 连廊挂上果冻门帘，敌人挤过去后身上粘果冻 → 对铁幕全队透墙可见 " + Config.JellyStickTime + " s，然后消失（承伤 / 时长不变）",
                    docked && passed && stuck && revealed && faded,
                    "挂上=" + docked + "，穿过=" + passed + "，粘身=" + stuck + "，透视=" + revealed + "，" + Config.JellyStickTime + " s 后消失=" + faded);
            }

            // ---------------- M93：棉花糖遇蒸汽（大小 = 整片蒸汽：后厨阀门 7 m 原地变粉；火锅 9 m；里面的敌人黏脚，迷雾外的部分也算）
            {
                var kv = ValveAt(new Vector3(.35f, 0, -21f));
                HotpotHazard potS = null; foreach (var h in HotpotHazard.All) if (h != null && h.transform.position.z < 0) potS = h;
                var ph = A2; var foe = D;
                bool kPink = false, kR = false, potR = false, jam = false;
                if (kv != null && potS != null && ph != null && foe != null)
                {
                    if (ph.role != Role.Phantom) ph.ChangeRole(Role.Phantom);
                    kv.Fire(null); yield return null;
                    var n1 = new GameObject("测试迷雾·后厨"); n1.transform.position = kv.transform.position + new Vector3(0, 0, 3f); n1.AddComponent<NoiseField>().Init(ph); Skills.spawned.Add(n1);
                    yield return new WaitForSeconds(.5f);
                    kPink = kv.area != null && kv.area.Candy; kR = kv.area != null && Mathf.Approximately(kv.area.R, Config.SteamAreaKitchen);
                    Reset(); yield return null;
                    Vector3 pc = potS.transform.position;
                    var n2 = new GameObject("测试迷雾·火锅"); n2.transform.position = pc + new Vector3(0, 0, -8.5f); n2.AddComponent<NoiseField>().Init(ph); Skills.spawned.Add(n2);
                    Place(foe, pc + new Vector3(0, .05f, 6f), 0); yield return new WaitForSeconds(1.2f);
                    CandySteam cs = null; foreach (var x in CandySteam.Live) if (x != null && x.key == potS) cs = x;
                    potR = cs != null && Mathf.Approximately(cs.area.R, Config.SteamAreaPot);
                    jam = Flat(foe.transform.position, n2.transform.position) > Config.NoiseRadius && foe.jammedUntil > Time.time - .1f;
                    Reset();
                }
                Check("M93", "棉花糖遇蒸汽：范围 = 整片蒸汽（后厨蒸汽阀 " + Config.SteamAreaKitchen + " m 原地变粉、火锅热气 " + Config.SteamAreaPot + " m）；棉花糖蒸汽里、迷雾圈外的敌人也黏脚",
                    kPink && kR && potR && jam,
                    "后厨阀门变粉=" + kPink + "（半径 = " + Config.SteamAreaKitchen + " m：" + kR + "），火锅棉花糖蒸汽 " + Config.SteamAreaPot + " m=" + potR + "，迷雾外 14 m 的敌人黏脚=" + jam);
            }

            // ---------------- M94：威化坡道（铆钉副联动）
            {
                int n = LinkPlus.Ramps.Count; bool aim = false, cast = false, busy = false, climbed = false, hOk = true; float topY = 0; string list = "";
                foreach (var r in LinkPlus.Ramps) { list += r.top.ToString("F0") + "h" + r.h.ToString("0.0") + " "; hOk &= r.h >= Config.RampMinH && r.h <= Config.RampMaxH; }
                var rv = D3; var mate = D2;
                if (n > 0 && rv != null && mate != null)
                {
                    var s = LinkPlus.Ramps[0];
                    if (rv.role != Role.Rivet) rv.ChangeRole(Role.Rivet);
                    Vector3 stand = s.basePos + s.dir * 1.8f;
                    Place(rv, stand + Vector3.up * .05f, YawTo(stand, s.top)); rv.pitch = 15; yield return null; yield return null;
                    var a = Links.AimFor(rv); aim = a.any && a.kind == LinkKind.WaferRamp;
                    int l0 = Links.Total[6]; rv.skillCharges = 2;
                    cast = Skills.Cast(rv, s.basePos) && s.ramp != null && Links.Total[6] == l0 + 1;
                    LinkPlus.RampTarget(rv, s.basePos, out _, out busy);
                    yield return new WaitForSeconds(Config.CoverDeploy + .2f);
                    Place(mate, s.basePos + s.dir * .8f + Vector3.up * .1f, YawTo(s.basePos, s.top)); yield return null;
                    mate.botMove = -s.dir; float t = 0;
                    while (t < 4f && mate.transform.position.y < s.h - .3f) { t += Time.deltaTime; yield return null; }
                    mate.botMove = Vector3.zero; topY = mate.transform.position.y; climbed = topY >= s.h - .3f;
                    Reset(); yield return null;
                }
                Check("M94", "威化坡道（铆钉副联动）：地图上自动找到 " + Config.RampMinH + "–" + Config.RampMaxH + " m 高的平台边（≥ 3 个）；预览对准变金；放下后约 " + Config.WaferRampAngle + "° 斜放，队友能直接走上去；同一处不能再放；存在 " + Config.WaferRampLife + " s",
                    n >= 3 && hOk && aim && cast && busy && climbed && Config.WaferRampLife <= 35f,
                    "坡道点 " + n + " 个 [" + list.Trim() + "]，变金=" + aim + "，放下=" + cast + "，再放=占用 " + busy + "，队友走上去 y=" + topY.ToString("0.00") + "=" + climbed);
            }

            // ---------------- M95：点酱（焚芯副联动）
            {
                int butter = -1, choco = -1;
                for (int i = 0; i < SauceZone.Count; i++) { if (SauceZone.KindOf(i) == SauceZone.Butter && butter < 0) butter = i; if (SauceZone.KindOf(i) == SauceZone.Chocolate && choco < 0) choco = i; }
                var ember = A2; var foe = D;
                bool fire = false, noCaramel = false, burn = false, hot = false, caramelKept = false, wasAlive = false, inButter = false, diffTeam = false; float dmg = 0; int l0 = Links.Total[7];
                if (butter >= 0 && choco >= 0 && ember != null && foe != null)
                {
                    if (ember.role != Role.Ember) ember.ChangeRole(Role.Ember);
                    Vector3 bc = SauceZone.Center(butter);
                    Place(ember, bc + new Vector3(12, .05f, 0), -90);
                    new GameObject("测试纸杯·黄油").AddComponent<BlastCharge>().Init(ember, bc + Vector3.up * .1f);
                    yield return new WaitForSeconds(Config.BlastWarning + .15f);
                    fire = SauceFire.Burning(butter);
                    noCaramel = true; foreach (var fz in Object.FindObjectsOfType<FireZone>()) if (Flat(fz.transform.position, bc) < 4f) noCaramel = false;
                    wasAlive = foe.alive; if (!foe.alive) Park(foe);
                    Place(foe, bc + Vector3.up * .05f, 0); foe.hp = 100; foe.armor = 0; yield return new WaitForFixedUpdate(); yield return null;
                    inButter = SauceZone.Inside(butter, foe.transform.position); diffTeam = foe.team != ember.team;
                    yield return new WaitForSeconds(1f);
                    dmg = 100 - foe.hp; burn = dmg > 5f;
                    Place(foe, new Vector3(-40, .05f, 4), 0); foe.hp = 100;
                    Vector3 cc = SauceZone.Center(choco);
                    new GameObject("测试纸杯·巧克力").AddComponent<BlastCharge>().Init(ember, cc + Vector3.up * .1f);
                    yield return new WaitForSeconds(Config.BlastWarning + .15f);
                    hot = SauceZone.HotIndex(choco);
                    foreach (var fz in Object.FindObjectsOfType<FireZone>()) if (Flat(fz.transform.position, cc) < 4f) caramelKept = true;
                    Reset();
                }
                Check("M95", "点酱（焚芯副联动）：纸杯蛋糕炸在黄油上 → 整条跑道着火 " + Config.SauceButterFire + " s（代替焦糖火、不叠加），站在上面的敌人掉血；炸在巧克力酱上 → 热巧克力 " + Config.SauceChocoHot + " s（焦糖火照常）",
                    fire && noCaramel && burn && hot && caramelKept && Links.Total[7] == l0 + 2,
                    "黄油着火=" + fire + "，没有另起焦糖火=" + noCaramel + "，1 s 掉血 " + dmg.ToString("0") + "（敌人原本存活=" + wasAlive + " 站在黄油里=" + inButter + " 不同队=" + diffTeam + "）" + "，巧克力变热=" + hot + "（焦糖火照常=" + caramelKept + "），联动计数 +" + (Links.Total[7] - l0));
            }

            // ---------------- M96：糊监控（幻频副联动）
            {
                SecurityCam cam = null; foreach (var c in ar.cams) if (c != null && c.Working) { cam = c; break; }
                var ph = A2; var spy = A;
                bool baseline = false, gummed = false, blind = true, defNo = false; int l0 = Links.Total[8];
                if (cam != null && ph != null && spy != null)
                {
                    if (ph.role != Role.Phantom) ph.ChangeRole(Role.Phantom);
                    Vector3 cp = cam.transform.position, fwd = Quaternion.Euler(0, cam.yawCenter, 0) * Vector3.forward, sp = Vector3.zero; bool got = false;
                    foreach (float dd in new[] { 12f, 10f, 14f, 8f })
                    {
                        Vector3 q = new Vector3(cp.x, 0, cp.z) + fwd * dd;
                        if (NavMesh.SamplePosition(q, out NavMeshHit nh, 1f, NavMesh.AllAreas)) { sp = nh.position; got = true; break; }
                    }
                    if (got)
                    {
                        Place(spy, sp + Vector3.up * .05f, 0); spy.revealedUntil = 0;
                        float t = 0; while (t < 7f && !spy.Revealed) { t += Time.deltaTime; yield return null; }
                        baseline = spy.Revealed;
                        // 守方自己的迷雾不会糊住自己的监控
                        var dn = new GameObject("测试迷雾·守方"); dn.transform.position = Skills.GroundAt(cp); dn.AddComponent<NoiseField>().Init(D); Skills.spawned.Add(dn);
                        yield return new WaitForSeconds(.4f); defNo = !LinkPlus.Gummed(cam); Object.Destroy(dn); yield return null;
                        var n = new GameObject("测试迷雾·糊监控"); n.transform.position = Skills.GroundAt(cp); n.AddComponent<NoiseField>().Init(ph); Skills.spawned.Add(n);
                        yield return new WaitForSeconds(.4f); gummed = LinkPlus.Gummed(cam);
                        spy.revealedUntil = 0; t = 0; while (t < 3f) { if (spy.Revealed) blind = false; t += Time.deltaTime; yield return null; }
                    }
                    Reset(); yield return null;
                }
                Check("M96", "糊监控（幻频副联动）：攻方棉花糖迷雾罩住守方监控 → 迷雾期间监控失效（镜头前的攻方不再被标记）；守方自己的迷雾不算",
                    baseline && defNo && gummed && blind && Links.Total[8] == l0 + 1,
                    "监控平时能标记=" + baseline + "，守方迷雾不糊=" + defNo + "，糊住=" + gummed + "，3 s 内不再标记=" + blind + "，联动计数 +" + (Links.Total[8] - l0));
            }

            // ---------------- M97：回血炉也能射芥末签（半径减半）
            {
                var st = LinkPlus.HealPots(); KitchenStation k = st.Count > 0 ? st[0] : null;
                var raven = A2; var foe = D; var foe2 = D2;
                bool potAt = false, radius = false, revNear = false, farNo = false, line = false;
                if (k != null && raven != null && foe != null && foe2 != null)
                {
                    if (raven.role != Role.Raven) raven.ChangeRole(Role.Raven);
                    Vector3 c = k.transform.position;
                    potAt = Links.PotAt(c + Vector3.up * 1f) == (Component)k;
                    radius = Mathf.Approximately(Links.PotRadius(k), Config.MustardPotRadius * Config.MustardStationMul);
                    Place(raven, c + new Vector3(0, .05f, 9f), 180); if (Spot(c, 4.5f, null, out Vector3 nearP)) Place(foe, nearP + Vector3.up * .05f, 0); Spot(c, 8.5f, null, out Vector3 farP); Place(foe2, farP + Vector3.up * .05f, 0);
                    foe.revealedUntil = foe2.revealedUntil = 0; yield return null;
                    var go = new GameObject("测试芥末签·回血炉"); var d = go.AddComponent<Drone>(); d.InitPot(raven, c + Vector3.up * 1f, Vector3.up, k);
                    yield return new WaitForSeconds(Config.MustardSpread + .4f);
                    revNear = foe.Revealed; farNo = !foe2.Revealed;
                    var pl = p; if (pl != null) { var save = pl.role; pl.ChangeRole(Role.Raven); line = Links.InteractLine(pl, k) != null; pl.ChangeRole(save); }
                    Object.Destroy(go); Reset();
                }
                Check("M97", "回血炉也能射芥末签：签子扎进火锅回血炉 → 半径减半（" + (Config.MustardPotRadius * Config.MustardStationMul) + " m）的芥末呛雾；4.5 m 内的敌人透视、8.5 m 的不会；夜鸦在回血炉气泡上看到金色联动行",
                    potAt && radius && revNear && farNo && line,
                    "回血炉 " + st.Count + " 个，认成锅=" + potAt + "，半径减半=" + radius + "，4.5 m 透视=" + revNear + "，8.5 m 不透视=" + farNo + "，气泡联动行=" + line);
            }

            // ---------------- M98：入口保底（硬约束）
            {
                string info = ""; bool ok = true;
                for (int s = 0; s < 2; s++)
                {
                    int free = 0;
                    for (int i = 0; i < 3; i++) { bool sealable = LinkPlus.EntranceSealable(LinkPlus.SiteEntrances[s][i]); if (!sealable) free++; info += LinkPlus.EntranceNames[s][i] + (sealable ? "（门框 ≤ 5 m，可挂门帘）" : "（挂不上）") + " "; }
                    ok &= free >= 2;
                }
                bool rampsLegal = true; foreach (var r in LinkPlus.Ramps) if (!ar.IsBuildLegal(r.basePos)) rampsLegal = false;
                bool steamNoBlock = true; foreach (var a in SteamArea.Live) if (a != null && a.GetComponentInChildren<Collider>() != null) steamNoBlock = false;
                Check("M98", "入口保底（硬约束）：每个包点至少 2 个常规入口不能被联动产物占住（门帘只能挂 ≤ 5 m 门框、且挂完包点还剩 ≥ 2 个入口才允许；补洞只补被打出来的洞；坡道底不在入口 / 出生区；蒸汽没有碰撞）",
                    ok && openA >= 2 && rampsLegal && steamNoBlock,
                    info.Trim() + "；A 连廊挂门帘时 A 点开着的入口 " + openA + " 个；坡道合法=" + rampsLegal + "，蒸汽无碰撞=" + steamNoBlock);
            }

            // ---------------- M99：可读性（金色联动提示 / H 帮助"我的英雄" / 首次提示卡 / 气泡字数）
            {
                bool bub = true, help = true, tips = true, elems = true, combo = true; string bad = "";
                foreach (var k in new[] { LinkKind.WaferRamp, LinkKind.SauceIgnite, LinkKind.CamGum })
                {
                    string b = Links.Bubble(k); if (b == null || b.Length > FoodGuide.BubbleMax) { bub = false; bad += "气泡:" + k + " "; }
                    if (string.IsNullOrEmpty(Pings.Line(PingKind.Link, k))) { combo = false; bad += "标记:" + k + " "; }
                }
                foreach (var r in new[] { Role.Rivet, Role.Ember, Role.Phantom })
                {
                    var side = (LinkKind)LinkPlus.SideFor(r);
                    if (!GameUI.HeroLinkText(r).Contains(Links.Name(side))) { help = false; bad += "H:" + r + " "; }
                    if (!Links.TipBody(r).Contains(Links.Name(side))) { combo = false; bad += "卡:" + r + " "; }
                    bool has = false; foreach (var e in Links.ElementsFor(r)) if (e.side) has = true;
                    if (!has) { elems = false; bad += "元素:" + r + " "; }
                    if (Links.Bubble(r, side) == null) { bub = false; bad += "对应:" + r + " "; }
                }
                if (p != null)
                    foreach (var id in new[] { "steam_area", "steam_inside", "enemy_jelly" })
                        if (string.IsNullOrEmpty(FoodTips.Body(id)) || FoodTips.Title(id) == id) { tips = false; bad += "提示:" + id + " "; }
                bool help2 = GameUI.HeroLinkText(Role.Raven).Contains("回血炉") && GameUI.HeroLinkText(Role.Ironwall).Contains("粘身") && GameUI.HeroLinkText(Role.Falcon).Contains("炸罐") && GameUI.HeroLinkText(Role.Phantom).Contains("整片");
                Check("M99", "可读性：3 个副联动都有金色气泡（≤ " + FoodGuide.BubbleMax + " 字、只给对应英雄）、地图星标元素、标记台词、首次提示卡（含副联动）、H 帮助\"我的英雄\"副联动说明；放大后的主联动说明（回血炉 / 粘身 / 炸罐 / 整片）都写进 H 页；整片蒸汽 / 雾里 / 粘身各有提示卡",
                    bub && help && tips && elems && combo && help2,
                    "气泡=" + bub + "，H 页=" + help + "/" + help2 + "，提示卡=" + tips + "，地图元素=" + elems + "，组合文案=" + combo + (bad != "" ? "（" + bad.Trim() + "）" : ""));
            }

            // ---------------- M100：AI 进雾关阀（敌方开的整片蒸汽，AI 会走进雾中心关掉）
            {
                var valve = ValveAt(new Vector3(16, 0, 8));
                bool closed = false; float took = -1;
                if (valve != null && A != null && D != null)
                {
                    foreach (var f in m.fighters) if (f != D && f != A) { Hush(f); Park(f); }
                    Place(A, valve.transform.position + new Vector3(-1.3f, .05f, 0), 90); valve.Fire(A);
                    Place(A, new Vector3(-55, .05f, 4), 90);
                    Spot(valve.transform.position, 9f, g => true, out Vector3 dp); Place(D, dp + Vector3.up * .05f, 0);
                    float save = Bot.CloseChance; Bot.CloseChance = 1f;
                    var bot = D.GetComponent<Bot>(); if (bot != null) bot.enabled = true;
                    float t = 0; while (t < 9f && valve.Active) { t += Time.deltaTime; D.hp = Config.MaxHp; yield return null; }
                    closed = !valve.Active && t < 9f; took = t;
                    if (bot != null) bot.enabled = false; Hush(D); Bot.CloseChance = save;
                    Reset();
                }
                Check("M100", "AI 进雾关阀：敌方拧开的整片蒸汽 32 m 内，守方 AI 走进雾中心长按 G 关掉（遥测 valve_close；整场对局里攻方 AI 会拧阀 / 守关口 AI 会开大火，见 match 日志 NEON_AI_KNOB / NEON_VALVE_CLOSE）",
                    closed, "关掉=" + closed + "，用时 " + took.ToString("0.0") + " s");
            }

            // ---------------- 收尾
            Reset(); FoodTips.AutoEnabled = true;
            foreach (var w in ar.breakWalls) w.ResetRound();
            foreach (var sh in ar.shutters) sh.ResetRound();
            foreach (var d in ar.destructibles) d.Restore();
            foreach (var kv in saved) if (kv.Key.role != kv.Value) kv.Key.ChangeRole(kv.Value);
            if (p != null) { p.scripted = false; Hush(p); }
            foreach (var f in m.fighters) { Park(f); }
            m.roundClock = Config.RoundTime; m.phase = Phase.Live;
            yield return null;
        }

        // ================================================================ 截图（窗口模式带 HUD）：90–98
        public static IEnumerator Shots(string dir, System.Func<string, IEnumerator> Grab)
        {
            var m = Match.I; var p = m.player; var ar = m.arena; var cam = p != null && p.cam != null ? p.cam : Camera.main;
            if (p == null || cam == null) yield break;
            HudCaptureBegin.Allow = true;
            m.phase = Phase.Live; m.roundClock = Config.RoundTime; m.ui.ClearBanner();
            var saved = new Dictionary<Fighter, Role>(); foreach (var f in m.fighters) saved[f] = f.role;
            FoodTips.AutoEnabled = false; Reset();
            foreach (var f in m.fighters) { Hush(f); if (!f.human) f.Respawn(new Vector3(-60 + f.slot * 2, .05f, f.team == 0 ? -46f : -44f), 0); }
            int att = m.AttackingTeam, def = m.DefendingTeam, foeTeam = p.team == att ? def : att, myTeam = p.team;
            // 外景用一台单独的截图相机（深度最高、盖住第一人称相机），第一人称时关掉它
            var shotCam = new GameObject("V23 d 截图相机").AddComponent<Camera>(); shotCam.depth = 100; shotCam.nearClipPlane = .05f; shotCam.enabled = false;
            var off = new List<Camera>();
            System.Action<Vector3, Vector3, float> Ext = (pos, look, fov) =>
            {
                p.SetViewModelVisible(false);
                foreach (var c in Camera.allCameras) if (c != shotCam && c.enabled && c.targetTexture == null) { c.enabled = false; off.Add(c); }   // 关掉其它相机，免得第一人称画面盖在上面
                shotCam.enabled = true; shotCam.transform.position = pos; shotCam.transform.LookAt(look); shotCam.fieldOfView = fov;
            };
            System.Action FP = () => { shotCam.enabled = false; foreach (var c in off) if (c != null) c.enabled = true; off.Clear(); p.enabled = true; p.AttachCamera(); p.SetViewModelVisible(true); };
            System.Action<Vector3> AimAt = (t) => { Vector3 d = t - p.Eye; p.yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; p.pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg; };
            string F(string n) { return System.IO.Path.Combine(dir, n + ".png"); }
            // 外景机位：按顺序挑第一个和目标之间没有遮挡的位置
            Vector3 View(Vector3 target, params Vector3[] offs) { foreach (var o in offs) if (!Physics.Linecast(target + Vector3.up * .8f, target + o, ~0, QueryTriggerInteraction.Ignore)) return target + o; return target + offs[0]; }
            var foe = Npc(foeTeam, 0); var foe2 = Npc(foeTeam, 1); var mate = Npc(myTeam, 0);

            // ---- 90 / 91 整片蒸汽：雾外看 + 雾里看
            var valve = ValveAt(new Vector3(16, 0, 8));
            if (valve != null && foe != null)
            {
                Vector3 c = valve.CloudCenter, foeP = new Vector3(14.3f, 0, 8);
                Hush(foe); Place(foe, foeP + Vector3.up * .05f, 180);
                p.Respawn(new Vector3(16, .05f, -6), 0); p.scripted = true; Hush(p); FP();
                Spot(foeP, 12f, g => Flat(g, c) > 10f, out Vector3 outP); Place(p, outP + Vector3.up * .05f, 0); AimAt(c);
                valve.Fire(foe); yield return new WaitForSeconds(1.8f);
                yield return Grab(F("90_steam_outside"));
                if (foe2 != null) { Hush(foe2); Place(foe2, foeP + new Vector3(0, .05f, -3.6f), 0); }
                Spot(foeP, 1.8f, g => Flat(g, c) < 8f, out Vector3 inP); Place(p, inP + Vector3.up * .05f, 0); AimAt(foe.Chest); yield return new WaitForSeconds(.6f);
                yield return Grab(F("91_steam_inside"));
                Reset();
            }
            // ---- 92 开大火 + 93 芥末呛雾（北关口关东煮锅）
            HotpotHazard potN = null; foreach (var h in HotpotHazard.All) if (h != null && h.transform.position.z > 0) potN = h;
            HotpotKnob knobN = null; foreach (var k in LinkPlus.Knobs) if (k != null && k.pot == potN) knobN = k;
            if (potN != null && knobN != null)
            {
                Vector3 pc = potN.transform.position;
                p.Respawn(knobN.transform.position + knobN.transform.forward * 1.6f + Vector3.up * .05f, 0); p.scripted = true; Hush(p); FP(); AimAt(knobN.transform.position + Vector3.up * .9f);
                yield return new WaitForSeconds(.6f); yield return Grab(F("92a_hotpot_knob"));
                knobN.Fire(p); yield return new WaitForSeconds(1.6f);
                Place(p, pc + new Vector3(-24f, .05f, -2f), 90);   // 玩家离开雾区（雾里会隐藏这片雾的雾团）
                Ext(View(pc, new Vector3(-16f, 7f, -10f), new Vector3(-12f, 11f, 0f), new Vector3(0f, 16f, -12f), new Vector3(10f, 12f, 0f)), pc + Vector3.up, 60); yield return new WaitForSeconds(.3f);
                yield return Grab(F("92_hotpot_steam"));
                Reset(); yield return null;
                var raven = mate; if (raven != null) { Hush(raven); if (raven.role != Role.Raven) raven.ChangeRole(Role.Raven); Place(raven, new Vector3(pc.x, .05f, 48.6f), 180); }
                if (foe != null) { Place(foe, pc + new Vector3(-5f, .05f, -3.5f), 90); }
                if (foe2 != null) { Place(foe2, pc + new Vector3(-9f, .05f, -6f), 90); }
                if (raven != null) Skills.ShootArrow(raven, Skills.ArrowVelocityTo(raven.Eye, pc + Vector3.up * .55f));
                yield return new WaitForSeconds(2.2f);
                p.Respawn(pc + new Vector3(-20f, .05f, -9f), 0); p.scripted = true; Hush(p); FP(); AimAt(pc + Vector3.up); yield return new WaitForSeconds(.4f);
                yield return Grab(F("93_mustard_fog"));
                Ext(View(pc, new Vector3(-14f, 9f, -12f), new Vector3(-10f, 14f, 0f), new Vector3(0f, 18f, -10f)), pc, 62); yield return new WaitForSeconds(.2f);
                yield return Grab(F("93b_mustard_fog_world"));
                Reset();
            }
            // ---- 94 威化坡道
            if (LinkPlus.Ramps.Count > 0)
            {
                var s = LinkPlus.Ramps[0]; var rv = mate;
                if (rv != null)
                {
                    if (rv.role != Role.Rivet) rv.ChangeRole(Role.Rivet);
                    Place(rv, s.basePos + s.dir * 1.8f + Vector3.up * .05f, YawTo(s.basePos, s.top)); rv.skillCharges = 2; Skills.Cast(rv, s.basePos);
                    yield return new WaitForSeconds(Config.CoverDeploy + .3f);
                    Vector3 side = Vector3.Cross(Vector3.up, s.dir);
                    Vector3 mid = (s.top + s.basePos) / 2f;
                    Ext(View(mid, s.dir * 5f + side * 5f + Vector3.up * 3f, s.dir * 5f - side * 5f + Vector3.up * 3f, s.dir * 6f + Vector3.up * 5f, side * 7f + Vector3.up * 6f, Vector3.up * 10f + s.dir * 3f), mid, 60); yield return new WaitForSeconds(.2f);
                    yield return Grab(F("94_wafer_ramp"));
                    Reset();
                }
            }
            // ---- 95 点酱（黄油跑道着火）
            {
                int butter = -1; for (int i = 0; i < SauceZone.Count; i++) if (SauceZone.KindOf(i) == SauceZone.Butter) { butter = i; break; }
                var ember = mate;
                if (butter >= 0 && ember != null)
                {
                    if (ember.role != Role.Ember) ember.ChangeRole(Role.Ember);
                    Vector3 bc = SauceZone.Center(butter);
                    new GameObject("截图纸杯").AddComponent<BlastCharge>().Init(ember, bc + Vector3.up * .1f);
                    yield return new WaitForSeconds(Config.BlastWarning + .5f);
                    Ext(View(bc, new Vector3(-7f, 4.5f, -6f), new Vector3(7f, 4.5f, -6f), new Vector3(-7f, 4.5f, 6f), new Vector3(7f, 4.5f, 6f), new Vector3(0f, 10f, -4f)), bc, 60); yield return new WaitForSeconds(.1f);
                    yield return Grab(F("95_sauce_fire"));
                    Reset();
                }
            }
            // ---- 96 糊监控
            {
                SecurityCam sc = null; foreach (var c in ar.cams) if (c != null && c.Working) { sc = c; break; }
                var ph = Npc(att, 0);
                if (sc != null && ph != null)
                {
                    if (ph.role != Role.Phantom) ph.ChangeRole(Role.Phantom);
                    var n = new GameObject("截图迷雾"); n.transform.position = Skills.GroundAt(sc.transform.position); n.AddComponent<NoiseField>().Init(ph); Skills.spawned.Add(n);
                    yield return new WaitForSeconds(.8f);
                    Vector3 cp = sc.transform.position, fwd = Quaternion.Euler(0, sc.yawCenter, 0) * Vector3.forward;
                    Ext(cp + fwd * 4f + Vector3.down * 2.2f + Vector3.Cross(Vector3.up, fwd) * 2f, cp, 55); yield return new WaitForSeconds(.2f);
                    yield return Grab(F("96_cam_gum"));
                    Reset();
                }
            }
            // ---- 97 开墙大门 + 蛋糕粉尘（97）/ 粉尘散后（97b）
            {
                BreakWall wA = null; foreach (var w in ar.breakWalls) if (w != null && w.transform.position.z > 0) wA = w;
                var ember = mate;
                if (wA != null && ember != null)
                {
                    if (ember.role != Role.Ember) ember.ChangeRole(Role.Ember);
                    wA.ResetRound(); yield return null;
                    Vector3 face = wA.transform.TransformPoint(new Vector3(0, 1.2f, -.2f));
                    WallPiece wp = null; float bd = 9f; foreach (var q in wA.pieces) { float dd = Vector3.Distance(q.transform.position, face); if (dd < bd) { bd = dd; wp = q; } }
                    new GameObject("截图纸杯（粘墙）").AddComponent<BlastCharge>().Init(ember, face, wp);
                    Vector3 n = wA.transform.forward;
                    Ext(wA.transform.position - n * 9f + Vector3.up * 2.4f, wA.transform.position + Vector3.up * 1.3f, 60);
                    yield return new WaitForSeconds(Config.BlastWarning + .35f);
                    yield return Grab(F("97_breach_dust"));
                    yield return new WaitForSeconds(Config.CakeDustLife + .3f);
                    foreach (var fz in Object.FindObjectsOfType<FireZone>()) Object.Destroy(fz.gameObject);
                    yield return new WaitForSeconds(.2f);
                    yield return Grab(F("97b_breach_wide"));
                    Reset(); wA.ResetRound();
                }
            }
            // ---- 收尾
            Reset(); FoodTips.AutoEnabled = true;
            foreach (var kv in saved) if (kv.Key.role != kv.Value) kv.Key.ChangeRole(kv.Value);
            FP(); p.scripted = false; Hush(p); Object.Destroy(shotCam.gameObject);
            foreach (var f in m.fighters) Park(f);
            yield return null;
        }
    }
}
