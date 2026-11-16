using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace NeonSiege
{
    // Bomb-mode bot. Attackers commit to a site as a team (different lanes), the carrier plants,
    // the others hold angles around the bomb. Defenders anchor on sites / mid, rotate on intel and
    // retake + defuse after a plant. Both sides use shutters, med stations and site breakers.
    [RequireComponent(typeof(Fighter))]
    public class Bot : MonoBehaviour
    {
        static readonly Dictionary<int, int> teamSitePlan = new Dictionary<int, int>();
        static readonly int[] siteAttempts = new int[2];
        // V22 第二期：每个包点 8 条（长街 / 连接巷 / 连廊 / 中庭侧口 / 北南小巷 / 奶油平台 / 墙洞 / 假打转点）
        static readonly string[][] SiteRoutes = {
            new[] { "A长街", "北烤串巷A", "A连廊", "中路A", "北小巷A", "奶油平台A", "A长街墙洞", "南巷转A" },
            new[] { "B工厂", "南后厨B", "B连廊", "中路B", "南小巷B", "奶油平台B", "B工厂墙洞", "北巷转B" } };
        // V22 第二期：蒸笼门（开着是常态）。攻方遇到关着的门去掀开，4 秒没掀开就改走墙洞；守方关口位开局有一定概率去盖上
        Shutter gateTry; float gateTryStart, gateTouchAt; bool gateGaveUp, wantCloseGate; float valveThinkAt, valveUntil; SteamValve valveTarget;
        // V23 d：进雾关阀（敌方开的整片蒸汽）/ 守关口开大火 / 幻频糊监控
        SteamValve closeTarget; float closeUntil, closeThinkAt; readonly HashSet<SteamArea> closeTried = new HashSet<SteamArea>(), closeSeen = new HashSet<SteamArea>(); bool knobUsed, gumUsed;
        public static float CloseChance = .8f;   // 测试可调成 1（V24：60% → 80%，V23 实测关阀回合只有 4%）
        // V24：会用全部联动 —— 可乐弹射 / 果冻门帘 / 威化坡道 / 射可乐瓶 / 射火锅溢锅 / 射冰柜 / 射爆米花机 / 推甜甜圈
        float colaUntil; bool colaUsed, curtainUsed, rampUsed, donutUsed; readonly HashSet<Object> propTried = new HashSet<Object>();
        Object propTarget; Vector3 propAim; float propUntil; GiantDonut donutTarget; float donutUntil, propThinkAt;
        public bool WantsCola { get { return Time.time < colaUntil; } }
        public static int PropShots, DonutPushes, ColaDashes, CurtainCasts, RampCasts;   // 遥测 / 测试读
        // V10：狙击高台（只能踩弹射板上去）
        bool nesting; Vector3 nestPad, nestSpot, nestLook; float nestHeight, nestLeaveAfter;
        public bool WantsPad { get { return nesting && f.transform.position.y < nestHeight - 1.5f && FlatDist(nestPad, f.transform.position) < 2f; } }
        public float accuracy = .55f;

        Fighter f;
        Vector3[] route; int routeIdx;
        readonly NavMeshPath path = new NavMeshPath();
        public Vector3? forceGoal;
        Vector3 ffaGoal; float ffaRethink;
        Vector3[] corners = new Vector3[0]; int cornerIdx; float repathAt; Vector3 pathGoal;
        Fighter target; float reactionReadyAt; Vector3 aimError; float aimErrorAt;
        Vector3 lastKnown; float heardAt = -10;
        float strafeSign = 1, strafeSwitchAt, thinkAt, skillAt, burstUntil, pauseUntil, stuckAt, semiAt, holdLookYaw, nextRotateCheck;
        Vector3 stuckPos; Vector3 holdSpot; int holdZone; Phase lastPhase = Phase.MatchEnd; int lastRound = -1;
        bool usedMed, usedBreaker;
        // V22 第三期：联动（夜鸦射锅 / 焚芯开墙 / 铆钉补洞）+ 团队标记响应
        Vector3 linkGoal; float linkGoalUntil; BreakWall emberWall; Vector3 emberSide; float emberAt;
        Vector3 pingGoal, lookHint; float pingGoalUntil, lookHintUntil, nextPingAt;
        public bool HasPingGoal { get { return Time.time < pingGoalUntil; } }
        public Vector3 PingGoal { get { return pingGoal; } }
        public Vector3 LookHint { get { return lookHint; } }
        public bool Looking { get { return Time.time < lookHintUntil; } }

        // V24：准度由 Match.BalanceBotSkill 按位置配对分配（两队平均相等），这里不再各自随机
        void Start() { f = GetComponent<Fighter>(); }

        // 队友标记：都看过去；"这里 / 这儿能联动"时被选中的那个走过去；玩家标的敌人还会当成情报
        public void OnPing(Ping p, bool move)
        {
            lookHint = p.pos; lookHintUntil = Time.time + 5f;
            if (p.kind == PingKind.Enemy && p.owner != null && p.owner.human) { lastKnown = p.pos; heardAt = Time.time; }
            if (move && p.kind != PingKind.Enemy) { pingGoal = p.pos; pingGoalUntil = Time.time + 10f; }
        }
        Vector3 LookFallback(Vector3 dflt) { return Time.time < lookHintUntil ? lookHint : dflt; }

        public void Hear(Fighter source, Vector3 pos, float weight)
        {
            if (source != null && !source.alive) return;
            lastKnown = pos; heardAt = Time.time;
        }

        bool Attacker { get { return f.team == Match.I.AttackingTeam; } }

        void Update()
        {
            if (f == null || Match.I == null) return;
            var m = Match.I;
            if (m.round != lastRound) { if (m.phase == Phase.Prep) NewRound(); lastRound = m.round; }
            lastPhase = m.phase;
            f.botTrigger = false; f.botAim = false; f.botSprint = false; f.botMove = Vector3.zero; f.botUse = false;
            if (!f.alive || m.phase == Phase.RoundEnd || m.phase == Phase.MatchEnd) return;
            if (m.phase == Phase.Prep) { f.yaw = Mathf.MoveTowardsAngle(f.yaw, Attacker ? 90 : -90, 180 * Time.deltaTime); return; }

            if (forceGoal.HasValue) { MoveAlong(forceGoal.Value, false); return; }   // 测试用
            CarrierWatchdog();
            if (Time.time >= thinkAt) { thinkAt = Time.time + .15f; Perceive(); }
            bool visible = target != null && target.alive && CanSee(target);

            if (visible && !f.bombBusy) Engage();
            else
            {
                if (target != null && !target.alive) target = null;
                Objective();
            }
            if (Time.time >= skillAt) { skillAt = Time.time + .4f; ThinkSkill(visible); TryGrenade(visible); ThinkLean(visible); if (ToonAssets.Available && visible && f.Food.UltReady && Random.value < .4f) f.Food.TryUlt(); }
            if (target != null && visible && Vector3.Distance(target.transform.position, f.transform.position) < 1.7f) f.Melee();
            StuckCheck();
        }

        void NewRound()
        {
            var m = Match.I;
            target = null; heardAt = -10; corners = new Vector3[0]; usedMed = usedBreaker = false; nesting = false;
            linkGoalUntil = pingGoalUntil = lookHintUntil = 0; emberWall = null; potUsed = false;
            gateTry = null; gateGaveUp = false; wantCloseGate = false; valveTarget = null; closeTarget = null; closeTried.Clear(); knobUsed = gumUsed = false;
            colaUntil = 0; colaUsed = curtainUsed = rampUsed = donutUsed = false; propTried.Clear(); propTarget = null; donutTarget = null;
            bool marksman = f.W.id == "DMR-3" || f.W.id == "BR-5";
            if (f.team == m.AttackingTeam)
            {
                if (!teamSitePlan.TryGetValue(m.round * 10 + f.team, out int site))
                {
                    // V22 第二期：这场打得少的那个点概率更高（每差一次 +15%，20%–80%），避免一场里连续 5 回合都打同一点
                    float pA = Mathf.Clamp(.5f + .15f * (siteAttempts[1] - siteAttempts[0]), .2f, .8f);
                    site = Random.value < pA ? 0 : 1; teamSitePlan[m.round * 10 + f.team] = site; siteAttempts[site]++;
                }
                var options = SiteRoutes[site];
                f.routeName = options[(f.slot + m.round) % options.Length];
                if (Random.value < .25f) f.routeName = options[Random.Range(0, options.Length)];
                route = Arena.I.routes[f.routeName];
                routeIdx = 0;
                carrierBest = 999f; carrierBestAt = Time.time; carrierDirect = false;
                if (marksman && m.BombCarrier != f && Random.value < .45f)   // V24 修复：携弹者永远不走狙击位
                {
                    // 先上主街水塔压制中路，30 秒后跳下参与进攻
                    nesting = true; nestPad = new Vector3(-45, 0, 2.1f); nestSpot = new Vector3(-45, 6f, 7.6f); nestLook = new Vector3(30, 3f, 0);
                    nestHeight = 6f; nestLeaveAfter = 30f; f.routeName = "水塔狙击→" + f.routeName;
                }
            }
            else
            {
                // V22 第二期：第三个守方在 中庭 / 奶油平台 / 北关口 / 南关口 / 包点 之间选；A、B 守方偶尔前压到自己一侧的关口
                float r = Random.value;
                holdZone = f.slot == 0 ? (r < .2f ? 3 : 0) : f.slot == 1 ? (r < .2f ? 4 : 1) : (r < .4f ? 2 : r < .6f ? 3 : r < .8f ? 4 : Random.Range(0, 2));
                if (!Arena.I.anchors.ContainsKey(holdZone)) holdZone = Mathf.Min(holdZone, 2);
                var spots = Arena.I.anchors[holdZone];
                holdSpot = spots[Random.Range(0, spots.Length)];
                f.routeName = holdZone == 0 ? "守A" : holdZone == 1 ? "守B" : holdZone == 2 ? "守中庭" : holdZone == 3 ? "守北关口" : "守南关口";
                wantCloseGate = holdZone >= 3 && Random.value < .5f;
                if (wantCloseGate) f.routeName += "·关门";
                route = null;
                if (marksman && Random.value < .6f)
                {
                    // 守方神射手：走东侧楼梯上市场楼顶（横幅挡住主街西段，只控中庭和主街东段），或者上奶油平台看两个连廊口
                    nesting = true; nestLeaveAfter = 999f; wantCloseGate = false;
                    if (Random.value < .55f) { nestSpot = new Vector3(32.6f, Arena.RoofTop, -1.5f); nestLook = new Vector3(0, 1.5f, 0); nestHeight = Arena.RoofTop; f.routeName = "楼顶狙击"; }
                    else { nestSpot = new Vector3(17.6f, Arena.CreamTop, -2.6f); nestLook = new Vector3(6, 1.5f, 0); nestHeight = Arena.CreamTop; f.routeName = "奶油平台"; }
                    nestPad = nestSpot;   // 走楼梯 / 坡道（NavMesh），不再踩弹射板
                }
            }
        }

        // ---------------------------------------------------------------- objective logic (no visible enemy)
        void Objective()
        {
            var m = Match.I;
            Vector3 pos = f.transform.position;
            Vector3 goal; bool sprint = true; Vector3 look;

            // Opportunistic interactions
            if (TryUseInteractables()) return;
            // V22 第三期：铆钉先走到破口旁边再补
            if (Time.time < linkGoalUntil && m.BombCarrier != f)
            {
                MoveAlong(linkGoal, true); AimToward(NextCorner(linkGoal) + Vector3.up * 1.4f, 240f, false);
                return;
            }
            // V22 第三期：队友"这里 / 这儿能联动"的标记 —— 被选中的 AI 走过去，到了就停下看一会儿
            if (Time.time < pingGoalUntil && m.BombCarrier != f && !(m.Planted && !Attacker))
            {
                if (FlatDist(pingGoal, pos) > 2.5f) { MoveAlong(pingGoal, true); AimToward(NextCorner(pingGoal) + Vector3.up * 1.4f, 240f, false); }
                else
                {
                    pingGoalUntil = Mathf.Min(pingGoalUntil, Time.time + 3f);
                    holdLookYaw = Mathf.Repeat(holdLookYaw + Time.deltaTime * 40f, 360f);
                    f.yaw = Mathf.MoveTowardsAngle(f.yaw, holdLookYaw, 90f * Time.deltaTime);
                }
                return;
            }
            if (ToonAssets.Available && FoodGoals(pos)) return;   // V18：捡食材 / 送菜 / 偷锅 / 吃零食

            if (Match.FFA)   // V17：美食大乱斗 —— 去找最近的对手（偶尔绕路）
            {
                if (Time.time >= ffaRethink || ffaGoal == Vector3.zero || FlatDist(ffaGoal, pos) < 2f)
                {
                    ffaRethink = Time.time + Random.Range(3f, 6f);
                    Fighter near = null; float nd = float.MaxValue;
                    foreach (var e in m.fighters) if (e != f && e.alive) { float d = Vector3.Distance(e.transform.position, pos); if (d < nd) { nd = d; near = e; } }
                    ffaGoal = near != null && Random.value < .75f ? near.transform.position : Arena.I.routes["A长街"][Random.Range(0, 6)];
                }
                Vector3 lk = Time.time - heardAt < 3f ? lastKnown : NextCorner(ffaGoal);
                MoveAlong(ffaGoal, true);
                AimToward(lk + Vector3.up * 1.4f, 240f, false);
                if (f.ammo < f.W.magazine * .4f && !f.IsReloading && Time.time - heardAt > 2f) f.Reload();
                return;
            }

            if (nesting)
            {
                // V24 修复：炸弹掉了且自己是最近的电脑攻方 → 也要下来捡
                bool fetch = Attacker && m.bomb == BombState.Dropped && NearestAttackerTo(m.bombPos) == f;
                if (m.Planted || m.BombCarrier == f || fetch || Time.time - m.liveStartedAt > nestLeaveAfter) nesting = false;
                else { NestStep(); return; }
            }

            if (Attacker)
            {
                if (m.Planted)
                {
                    // guard the bomb from nearby cover
                    if (holdSpot == Vector3.zero || Vector3.Distance(holdSpot, m.bombPos) > 14f)
                    {
                        var spots = Arena.I.anchors[m.plantedSite];
                        holdSpot = spots[(f.slot + 1) % spots.Length];
                    }
                    goal = holdSpot; sprint = false;
                    look = Time.time - heardAt < 4f ? lastKnown : LookFallback(m.bombPos + (m.bombPos - pos).normalized * 6f);
                }
                else if (m.bomb == BombState.Dropped && NearestAttackerTo(m.bombPos) == f)
                {
                    goal = m.bombPos; look = goal;
                }
                else if (m.BombCarrier == f && Arena.SiteAt(pos) >= 0)
                {
                    f.botUse = true; f.botCrouch = false;
                    look = pos + f.transform.forward;
                    AimToward(f.Eye + Quaternion.Euler(0, f.yaw, 0) * Vector3.forward * 5 + Vector3.down * 2f, 120f, false);
                    return;
                }
                else
                {
                    goal = RouteGoal();
                    if (m.BombCarrier != null && m.BombCarrier != f && route != null && routeIdx >= route.Length - 1)
                    {
                        // escort: hold near the site while the carrier comes in
                        var spots = Arena.I.anchors[SiteOf(route)];
                        goal = spots[f.slot % spots.Length]; sprint = false;
                    }
                    look = Time.time - heardAt < 3f ? lastKnown : LookFallback(NextCorner(goal));
                }
            }
            else
            {
                if (m.Planted)
                {
                    goal = m.bombPos;
                    float d = FlatDist(goal, pos);
                    if (d <= Config.DefuseRange - .2f)
                    {
                        bool someoneDefusing = m.defuser != null && m.defuser != f && m.defuser.alive && m.defuser.bombBusy;
                        if (!someoneDefusing) { f.botUse = true; return; }
                        goal = m.bombPos + (pos - m.bombPos).normalized * 4f;
                    }
                    look = Time.time - heardAt < 3f ? lastKnown : goal;
                    sprint = d > 10;
                }
                else
                {
                    if (wantCloseGate && TryCloseGate(pos)) return;   // V22 第二期：守方关口位去盖蒸笼门
                    if (Time.time >= nextRotateCheck) { nextRotateCheck = Time.time + 1f; ConsiderRotate(); }
                    goal = holdSpot; sprint = FlatDist(goal, pos) > 10f;
                    if (FlatDist(goal, pos) < 1.2f)
                    {
                        f.botCrouch = Random.value < .002f ? !f.botCrouch : f.botCrouch;
                        holdLookYaw = Mathf.Repeat(holdLookYaw + Time.deltaTime * 12f, 360f);
                        Vector3 toward = Time.time - heardAt < 5f ? lastKnown : LookFallback(new Vector3(-40, 0, holdSpot.z * .8f));
                        Vector3 dir = toward - pos; dir.y = 0;
                        float baseYaw = Quaternion.LookRotation(dir.sqrMagnitude > .1f ? dir : Vector3.left).eulerAngles.y;
                        f.yaw = Mathf.MoveTowardsAngle(f.yaw, baseYaw + Mathf.Sin(Time.time * .7f + f.slot) * 35f, 120f * Time.deltaTime);
                        f.pitch = Mathf.MoveTowards(f.pitch, 0, 60 * Time.deltaTime);
                        if (f.ammo < f.W.magazine * .5f && !f.IsReloading) f.Reload();
                        return;
                    }
                    look = Time.time - heardAt < 3f ? lastKnown : LookFallback(NextCorner(goal));
                }
            }
            MoveAlong(goal, sprint);
            AimToward(look + Vector3.up * 1.4f, 240f, false);
            if (f.ammo < f.W.magazine * .4f && !f.IsReloading && Time.time - heardAt > 2f) f.Reload();
        }

        // peek around a wall edge: if the threat is blocked straight ahead but clear from one side, lean that way
        void ThinkLean(bool visible)
        {
            f.botLean = 0;
            if (f.botMove.sqrMagnitude > .05f || Time.time - heardAt > 4f && !visible) return;
            Vector3 threat = visible && target != null ? target.Chest : lastKnown + Vector3.up * 1.3f;
            Vector3 eye = f.transform.position + Vector3.up * 1.62f;
            if (Skills.SightClear(eye, threat, target)) return;
            foreach (int side in new[] { 1, -1 })
                if (Skills.SightClear(eye + f.transform.right * side * .38f, threat, target)) { f.botLean = side; return; }
        }

        float nextGrenadeThink;
        // AssaultCube-style bot nade: throw at an enemy last seen behind cover, 8–24 m away
        bool TryGrenade(bool visible)
        {
            if (f.grenades <= 0 || Time.time < nextGrenadeThink || !Match.I.CanAct) return false;
            nextGrenadeThink = Time.time + 1.5f;
            if (visible) return false;
            // V23 d：敌方开的整片蒸汽里听到人 → 往雾里扔雷（爆炸不受雾影响，是标准反制）
            if (Time.time - heardAt < 6f)
                foreach (var a in SteamArea.Live)
                {
                    if (a == null || !a.Alive || !a.Blind || a.team == f.team || !a.Inside(lastKnown, 1f)) continue;
                    float da = FlatDist(a.center, f.transform.position); if (da < 7f || da > 26f || Random.value > .6f) continue;
                    FaceFlat(lastKnown); f.ThrowGrenade(Skills.VelocityTo(f.Eye, lastKnown, 38f));
                    if (Match.I.autoplay) Debug.Log("NEON_AI_NADE_STEAM r" + Match.I.round + " " + f.Hero.name);
                    return true;
                }
            if (Time.time - heardAt > 4f) return false;
            float d = FlatDist(lastKnown, f.transform.position);
            if (d < 8f || d > 24f || Random.value > .35f) return false;
            FaceFlat(lastKnown);
            f.ThrowGrenade(Skills.VelocityTo(f.Eye, lastKnown, 38f));
            return true;
        }

        void NestStep()
        {
            Vector3 pos = f.transform.position;
            if (pos.y < nestHeight - 1.5f)
            {
                if (!f.grounded) return;                       // in flight: let the launch carry us
                MoveAlong(nestPad, true);
                AimToward(NextCorner(nestPad) + Vector3.up * 1.4f, 240f, false);
                return;
            }
            Vector3 to = nestSpot - pos; to.y = 0;
            if (to.magnitude > .8f) { f.botMove = to.normalized; f.botSprint = false; f.botCrouch = false; }
            else { f.botMove = Vector3.zero; f.botCrouch = true; }
            Vector3 toward = Time.time - heardAt < 5f ? lastKnown : nestLook + new Vector3(0, 0, Mathf.Sin(Time.time * .4f + f.slot) * 8f);
            AimToward(toward + Vector3.up * 1.4f, 120f, false);
            if (f.ammo < f.W.magazine * .5f && !f.IsReloading) f.Reload();
        }

        // V22 第二期：守方盖蒸笼门（站到门的东侧 1.3 m，长按 2 s；门已经关了 / 开局 25 s 后就不管了）
        bool TryCloseGate(Vector3 pos)
        {
            var g = Arena.I.Gate(holdZone == 3 ? 1 : -1);
            if (g == null || !g.open || Time.time - Match.I.liveStartedAt > 25f) { wantCloseGate = false; return false; }
            Vector3 stand = g.transform.position + Vector3.right * 1.3f;
            if (FlatDist(stand, pos) > 1.2f) { MoveAlong(stand, true); AimToward(NextCorner(stand) + Vector3.up * 1.4f, 240f, false); return true; }
            if (!g.Usable(f)) return true;
            f.botUse = true; AimToward(g.transform.position + Vector3.up * 1.2f, 200f, false);
            return true;
        }

        // V22 第二期：关着的蒸笼门掀了 4 秒还没开（被打断 / 守方来回关）→ 改走同侧墙洞，从墙洞前最近的路点接着走
        void GiveUpGate()
        {
            gateGaveUp = true; gateTry = null;
            if (route == null) return;
            int site = SiteOf(route);
            route = Arena.I.routes[site == 0 ? "A长街墙洞" : "B工厂墙洞"]; f.routeName += "→墙洞";
            routeIdx = 0; float best = float.MaxValue;
            for (int i = 0; i < route.Length; i++) { float dd = FlatDist(route[i], f.transform.position); if (Mathf.Abs(route[i].z) > 44f && dd < best) { best = dd; routeIdx = i; } }
            if (Match.I.autoplay) Debug.Log("NEON_GATE_GIVEUP r" + Match.I.round + " " + f.Hero.name + " → " + f.routeName);
        }

        Vector3 RouteGoal()
        {
            if (route == null) return Arena.SiteA;
            if (routeIdx < route.Length)
            {
                Vector3 wp = route[routeIdx];
                if (FlatDist(wp, f.transform.position) < 2.4f && (wp.y < 1f || Mathf.Abs(wp.y - f.transform.position.y) < 1.6f)) routeIdx++;   // V22：只有高处路点（奶油平台）要求真的上去；地面路点照旧只看水平距离
            }
            if (routeIdx >= route.Length) routeIdx = route.Length - 1;
            return route[routeIdx];
        }

        static int SiteOf(Vector3[] r) { return r[r.Length - 1].z > 0 ? 0 : 1; }

        Fighter NearestAttackerTo(Vector3 p)
        {
            // V24 修复：只在电脑里选（玩家离炸弹 6 m 内才算玩家去捡）—— 以前玩家最近时，所有电脑都不去捡，回合超时
            Fighter best = null; float bd = float.MaxValue;
            foreach (var o in Match.I.fighters)
            {
                if (!o.alive || o.team != f.team) continue;
                float d = Vector3.Distance(o.transform.position, p);
                if (o.human && d > 6f) continue;
                if (d < bd) { bd = d; best = o; }
            }
            return best;
        }

        // V24 修复：携弹者兜底。离包点的路程 8 s 没缩短 2 m 以上（卡在高台 / 墙洞 / 门前），或回合只剩 40 s，
        // 就丢掉原路线，直接按 NavMesh 走最近的包点；高处（y > 2.5，不在包点）先找最近的地面点下去。
        float carrierBest = 999f, carrierBestAt; bool carrierDirect;
        void CarrierWatchdog()
        {
            var m = Match.I;
            if (m.BombCarrier != f || m.Planted || Match.Mode != GameMode.Bomb || route == null) return;
            Vector3 pos = f.transform.position;
            int site = SiteOf(route);
            float d = FlatDist(Arena.SiteCenter(site), pos);
            if (d < carrierBest - 2f) { carrierBest = d; carrierBestAt = Time.time; }
            bool stalled = Time.time - carrierBestAt > 8f, late = m.roundClock < 40f;
            if (carrierDirect || !(stalled || late)) return;
            carrierDirect = true; nesting = false;
            var steps = new List<Vector3>();
            if (pos.y > 2.5f && Arena.SiteAt(pos) < 0 && NavMesh.SamplePosition(new Vector3(pos.x, 0, pos.z), out NavMeshHit low, 8f, NavMesh.AllAreas)) steps.Add(low.position);
            steps.Add(Arena.SiteCenter(site));
            route = steps.ToArray(); routeIdx = 0; f.routeName += stalled ? "→卡住直奔" : "→时间紧直奔";
            repathAt = 0; carrierBestAt = Time.time;
            if (m.autoplay) Debug.Log("NEON_CARRIER_DIRECT r" + m.round + " " + f.Hero.name + " stalled=" + stalled + " site=" + site + " pos=" + pos.ToString("F0"));
        }

        void ConsiderRotate()
        {
            // Rotate to a site where enemies were heard/seen recently, or where the bomb carrier was revealed.
            if (Time.time - heardAt < 3f)
            {
                int zone = lastKnown.z > 12 && lastKnown.x > 10 ? 0 : lastKnown.z < -12 && lastKnown.x > 10 ? 1 : -1;
                if (zone >= 0 && zone != holdZone && Random.value < .35f)
                {
                    holdZone = zone; var spots = Arena.I.anchors[zone]; holdSpot = spots[Random.Range(0, spots.Length)];
                    f.routeName = "转点" + (zone == 0 ? "A" : "B");
                }
            }
        }

        bool TryUseInteractables()
        {
            Vector3 pos = f.transform.position;
            if (TryProps(pos)) return true;   // V24
            // closed shutter on our path → open it（V22：蒸笼门 4 秒掀不开就改走墙洞）
            foreach (var s in Arena.I.shutters)
            {
                if (s.open || !s.Usable(f)) continue;
                if (s.gate && gateGaveUp) continue;
                float d = Vector3.Distance(s.transform.position, pos);
                bool onRoute = route != null && routeIdx < route.Length && Vector3.Distance(route[Mathf.Min(routeIdx, route.Length - 1)], s.transform.position) < (s.gate ? 6f : 5f);
                if (d < (s.gate ? 7f : 6f) && onRoute)
                {
                    if (s.gate)
                    {
                        if (gateTry != s || Time.time - gateTouchAt > 1.5f) { gateTry = s; gateTryStart = Time.time; }
                        gateTouchAt = Time.time;
                        if (Time.time - gateTryStart > 4f) { GiveUpGate(); return false; }
                    }
                    if (d > 2.3f) { MoveAlong(s.transform.position + (pos - s.transform.position).normalized * 1.4f, false); AimToward(s.transform.position + Vector3.up, 200, false); }
                    else f.botUse = true;
                    return true;
                }
            }
            // V22 第二期：攻方路过蒸汽阀（4.5 m 内），最近听到敌人就去拧开，白雾掩护过中庭 / 后厨
            if (Attacker && route != null && !Match.I.Planted)
            {
                if (valveTarget == null && Time.time >= valveThinkAt)
                {
                    valveThinkAt = Time.time + 1f;
                    foreach (var v in SteamValve.Valves)
                        if (v != null && !v.Active && v.Usable(f) && FlatDist(v.transform.position, pos) < 4.5f && Time.time - heardAt < 5f) { valveTarget = v; valveUntil = Time.time + 4f; break; }
                }
                if (valveTarget != null)
                {
                    if (!valveTarget.Usable(f) || valveTarget.Active || Time.time > valveUntil) valveTarget = null;
                    else
                    {
                        Vector3 vp = valveTarget.transform.position;
                        if (FlatDist(vp, pos) > 1.5f) MoveAlong(vp + (pos - vp).normalized * 1.2f, true);
                        else f.botUse = true;
                        AimToward(valveTarget.Anchor, 240f, false);
                        return true;
                    }
                }
            }
            // V23 d：进雾关阀 —— 敌方开的整片蒸汽 32 m 内：每片雾每人决定一次（60%），9 s 内走到走进雾中心关掉（反制，关阀的人自己要走进看不见的地方）
            if (Match.I.BombCarrier != f && !f.bombBusy)
            {
                if (closeTarget == null && Time.time >= closeThinkAt)
                {
                    closeThinkAt = Time.time + .8f;
                    foreach (var v in SteamValve.Valves)
                    {
                        if (v == null || !v.Active || v.area == null || v.area.team < 0 || v.area.team == f.team || closeTried.Contains(v.area)) continue;
                        if (FlatDist(v.transform.position, pos) > 32f) { if (Match.I.autoplay && closeSeen.Add(v.area)) Debug.Log("NEON_AI_CLOSE_FAR r" + Match.I.round + " " + f.Hero.name + " d=" + FlatDist(v.transform.position, pos).ToString("0")); continue; }
                        closeTried.Add(v.area);
                        if (Random.value < CloseChance) { closeTarget = v; closeUntil = Time.time + 9f; if (Match.I.autoplay) Debug.Log("NEON_AI_CLOSE r" + Match.I.round + " " + f.Hero.name + " → " + v.Noun); }
                        break;
                    }
                }
                if (closeTarget != null)
                {
                    if (!closeTarget.Active || Time.time > closeUntil) closeTarget = null;
                    else
                    {
                        Vector3 vp = closeTarget.transform.position;
                        if (FlatDist(vp, pos) > 1.5f) MoveAlong(vp + (pos - vp).normalized * 1.1f, false);
                        else if (closeTarget.Usable(f)) f.botUse = true;
                        AimToward(closeTarget.Anchor, 240f, false);
                        return true;
                    }
                }
            }
            // V23 d：守关口的 AI 听到攻方逼近关口（5 s 内、30 m 内）→ 开大火，整片蒸汽封住关口视线（每回合一次）
            if (!Attacker && !knobUsed && !Match.I.Planted && (holdZone == 3 || holdZone == 4) && Time.time - heardAt < 5f)
                foreach (var k in LinkPlus.Knobs)
                {
                    if (k == null || (k.transform.position.z > 0) != (holdZone == 3) || !k.Usable(f) || k.Active) continue;
                    if (FlatDist(k.transform.position, pos) > 12f || FlatDist(lastKnown, k.transform.position) > 30f) continue;
                    if (valveTarget != k) { valveTarget = k; valveUntil = Time.time + 4f; if (Match.I.autoplay) Debug.Log("NEON_AI_KNOB r" + Match.I.round + " " + f.Hero.name); }
                    knobUsed = true; break;
                }
            if (valveTarget != null && !Attacker)
            {
                if (!valveTarget.Usable(f) || valveTarget.Active || Time.time > valveUntil) valveTarget = null;
                else
                {
                    Vector3 vp = valveTarget.transform.position;
                    if (FlatDist(vp, pos) > 1.5f) MoveAlong(vp + (pos - vp).normalized * 1.2f, true);
                    else f.botUse = true;
                    AimToward(valveTarget.Anchor, 240f, false);
                    return true;
                }
            }
            // wall hole on our route: shoot the boarded-up crates, then crawl through
            if (Attacker && route != null && routeIdx < route.Length)
                foreach (var d in Arena.I.destructibles)
                {
                    if (d == null || !d.Alive || !d.name.Contains("墙洞")) continue;
                    float dd = Vector3.Distance(d.transform.position, pos);
                    if (dd > 10f || Vector3.Distance(route[routeIdx], d.transform.position) > 5f) continue;
                    if (dd > 5f) MoveAlong(d.transform.position + (pos - d.transform.position).normalized * 4.5f, false);
                    else f.botMove = Vector3.zero;
                    AimToward(d.transform.position + Vector3.up * 1.1f, 300f, true);
                    f.botTrigger = f.W.automatic || (Time.frameCount & 4) == 0;   // semi-auto weapons need repeated presses
                    return true;
                }
            // med station when hurt
            if (!usedMed && f.hp < 55)
                foreach (var i in Interactable.All)
                {
                    if (!(i is MedStation) || !i.Usable(f)) continue;
                    float d = Vector3.Distance(i.transform.position, pos);
                    if (d > 14f) continue;
                    if (d > 1.6f) MoveAlong(i.transform.position + i.transform.forward * 1f, true);
                    else { f.botUse = true; if (f.hp >= 99) usedMed = true; }
                    AimToward(i.Anchor, 200, false);
                    return true;
                }
            // attackers cut the power at the target site before entering
            if (Attacker && !usedBreaker && route != null)
                foreach (var pb in Arena.I.powerBoxes)
                {
                    if (!pb.Usable(f) || Vector3.Distance(pb.transform.position, pos) > 6f) continue;
                    if (Vector3.Distance(pb.transform.position, route[route.Length - 1]) > 20f) continue;
                    if (Vector3.Distance(pb.transform.position, pos) > 1.6f) MoveAlong(pb.transform.position + pb.transform.forward * 1.1f, false);
                    else { f.botUse = true; if (!pb.Usable(f)) usedBreaker = true; }
                    AimToward(pb.Anchor, 200, false);
                    return true;
                }
            return false;
        }

        // ---------------------------------------------------------------- V24：机关联动（射爆 / 推倒）
        // 听到敌人（4 s 内）在某个机关的效果范围里 → 30 m 内看得见这个机关就开枪打它（每个机关每回合每人试一次，最多 3 s）
        bool TryProps(Vector3 pos)
        {
            var m = Match.I;
            if (m.BombCarrier == f || f.bombBusy || !ToonAssets.Available) return false;
            if (propTarget != null)
            {
                bool done = Time.time > propUntil || PropDone(propTarget);
                if (done) { propTarget = null; return false; }
                f.botMove = Vector3.zero;
                AimToward(propAim, 320f, true);
                if (Vector3.Angle(f.AimDir, propAim - f.Eye) < 4f) { f.botTrigger = f.W.automatic || (Time.frameCount & 4) == 0; if (f.botTrigger) PropShots++; }
                if (f.ammo == 0) f.Reload();
                return true;
            }
            if (donutTarget != null)
            {
                if (Time.time > donutUntil || !donutTarget.Usable(f)) { donutTarget = null; return false; }
                Vector3 a = donutTarget.Anchor; a.y = donutTarget.transform.position.y;
                if (FlatDist(a, pos) > 1.2f) { MoveAlong(a, true); AimToward(donutTarget.transform.position + Vector3.up, 240f, false); }
                else { f.botUse = true; FaceFlat(donutTarget.transform.position); }
                return true;
            }
            if (Time.time < propThinkAt || Time.time - heardAt > 4f) return false;
            propThinkAt = Time.time + .5f;
            Vector3 enemy = lastKnown;
            // 推甜甜圈：敌人在它要滚过的那条街上（离滚动线 3 m 内、在前方 45 m 内），自己 14 m 内
            if (!donutUsed)
                foreach (var d in V24.Donuts)
                {
                    if (d == null || !d.Usable(f) || FlatDist(d.transform.position, pos) > 14f) continue;
                    Vector3 to = enemy - d.home; to.y = 0; float along = Vector3.Dot(to, d.dir);
                    if (along < 3f || along > V24.DonutRange || Links.SegFlatDist(d.home, d.home + d.dir * V24.DonutRange, enemy) > 3f) continue;
                    donutUsed = true; donutTarget = d; donutUntil = Time.time + 5f; DonutPushes++;
                    if (m.autoplay) Debug.Log("NEON_AI_V24 r" + m.round + " donut " + f.Hero.name);
                    return true;
                }
            foreach (var p in PropCandidates())
            {
                if (p.key == null || propTried.Contains(p.key)) continue;
                float dp = Vector3.Distance(p.aim, f.Eye);
                if (dp > 30f || dp < 3f || FlatDist(p.effect, enemy) > p.radius) continue;
                if (FlatDist(p.effect, pos) < p.radius * .8f) continue;   // 自己别站在效果范围里
                if (Physics.Linecast(f.Eye, p.aim, out RaycastHit hh, ~0, QueryTriggerInteraction.Ignore) && FlatDist(hh.point, p.aim) > 1.2f) continue;
                propTried.Add(p.key); propTarget = p.key; propAim = p.aim; propUntil = Time.time + 3f;
                if (m.autoplay) Debug.Log("NEON_AI_V24 r" + m.round + " shoot " + p.name + " " + f.Hero.name);
                return true;
            }
            return false;
        }
        struct PropC { public Object key; public Vector3 aim, effect; public float radius; public string name; }
        IEnumerable<PropC> PropCandidates()
        {
            foreach (var h in HotpotHazard.All) if (h != null && !V24.PotRefilling(h)) yield return new PropC { key = h, aim = h.transform.position + Vector3.up * .6f, effect = h.transform.position, radius = V24.HotpotWaveRadius + 1f, name = "hotpot" };
            foreach (var mp in MentosPile.All) if (mp != null && mp.Ready) yield return new PropC { key = mp, aim = mp.transform.position + Vector3.up * .85f, effect = mp.transform.position, radius = 5.5f, name = "cola" };
            foreach (var sp in V24.Props)
            {
                if (sp == null || sp.broken) continue;
                // 爆米花机：敌人在它附近 = 用烟挡住对方视线 / 脚步声
                float r = sp.kind == ShootProp.Kind.Freezer ? V24.IceRadius : V24.PopcornRadius + 3f;
                yield return new PropC { key = sp, aim = sp.Center, effect = sp.transform.position, radius = r, name = sp.kind.ToString() };
            }
        }
        static bool PropDone(Object k)
        {
            if (k is HotpotHazard h) return V24.PotRefilling(h);
            if (k is MentosPile mp) return !mp.Ready;
            if (k is ShootProp sp) return sp.broken;
            return true;
        }

        // ---------------------------------------------------------------- perception
        void Perceive()
        {
            Fighter best = null; float bestScore = float.MaxValue; bool bestSeen = false;
            bool jammed = Time.time < f.jammedUntil;
            var m = Match.I;
            foreach (var e in m.fighters)
            {
                if (!e.alive || e.team == f.team) continue;
                float d = Vector3.Distance(e.transform.position, f.transform.position);
                bool seen = d < 80f && (Vector3.Angle(f.AimDir, e.Chest - f.Eye) < 75f || d < 7f || e.lastShotAt > Time.time - .5f && d < 35f) && CanSee(e);
                if (seen && !f.human && DarkAt(e.transform.position) && d > 14f && e.lastShotAt < Time.time - .5f) seen = false;   // blackout hides targets at range
                bool known = seen || (!jammed && e.Revealed);
                if (!known) continue;
                if (seen) e.lastSeenByEnemyAt = Time.time;
                float score = d + (seen ? 0 : 25f) + (e.bombBusy ? -20f : 0) + (e == target ? -6f : 0);
                if (score < bestScore) { bestScore = score; best = e; bestSeen = seen; }
            }
            if (best != target)
            {
                target = best;
                // V22 第三期：AI 看到新敌人时偶尔标记给队友（每人 12 s 最多一次；队友只是看过去）
                if (best != null && bestSeen && Match.Mode == GameMode.Bomb && Time.time >= nextPingAt && Random.value < .5f) { nextPingAt = Time.time + 12f; Pings.Create(f, PingKind.Enemy, best.Chest, best); }
                if (best != null)
                {
                    reactionReadyAt = Time.time + Random.Range(.22f, .42f) * Mathf.Lerp(1.4f, .8f, accuracy);
                    aimError = Random.insideUnitSphere * Mathf.Lerp(1.3f, .5f, accuracy); aimErrorAt = Time.time;
                    strafeSwitchAt = Time.time + Random.Range(.4f, 1f);
                }
            }
            if (target != null) { lastKnown = target.transform.position; heardAt = Time.time; }
        }

        static bool DarkAt(Vector3 p)
        {
            foreach (var pb in Arena.I.powerBoxes)
                if (pb.Dark && Vector3.Distance(p, pb.zone == "A 点" ? Arena.SiteA : Arena.SiteB) < 16f) return true;
            return false;
        }

        bool CanSee(Fighter e)
        {
            return Skills.SightClear(f.Eye, e.Chest, e) || Skills.SightClear(f.Eye, e.Head, e);
        }

        void MoveAlong(Vector3 goal, bool allowSprint)
        {
            if (Time.time >= repathAt || Vector3.Distance(goal, pathGoal) > 1.5f)
            {
                repathAt = Time.time + .6f; pathGoal = goal;
                if (NavMesh.SamplePosition(goal, out NavMeshHit gh, 4f, NavMesh.AllAreas) &&
                    NavMesh.SamplePosition(f.transform.position, out NavMeshHit sh, 2f, NavMesh.AllAreas) &&
                    NavMesh.CalculatePath(sh.position, gh.position, NavMesh.AllAreas, path) && path.corners.Length > 0)
                { corners = path.corners; cornerIdx = 0; }
                else { corners = new[] { goal }; cornerIdx = 0; }
            }
            while (cornerIdx < corners.Length && FlatDist(corners[cornerIdx], f.transform.position) < .55f && Mathf.Abs(corners[cornerIdx].y - f.transform.position.y) < 1f) cornerIdx++;
            if (cornerIdx >= corners.Length) return;
            Vector3 dir = corners[cornerIdx] - f.transform.position;
            if (dir.y > .5f && dir.y < 2.2f && FlatDist(corners[cornerIdx], f.transform.position) < 2.6f && f.BotMantle(new Vector3(dir.x, 0, dir.z))) { repathAt = Time.time + .5f; return; }   // V17：翻上美食方块
            if (dir.y > .5f && FlatDist(corners[cornerIdx], f.transform.position) < .7f) { while (cornerIdx < corners.Length - 1 && corners[cornerIdx].y > f.transform.position.y + .5f && FlatDist(corners[cornerIdx], f.transform.position) < .9f) cornerIdx++; }
            dir.y = 0;
            f.botMove = dir.normalized;
            f.botSprint = allowSprint && FlatDist(goal, f.transform.position) > 8f && Time.time - heardAt > 2f;
        }

        // ---------------------------------------------------------------- V18：美食目标
        void GoFood(Vector3 g) { MoveAlong(g, true); AimToward(NextCorner(g) + Vector3.up * 1.4f, 240f, false); }
        IngredientPickup NearestIngredient(Vector3 pos, float max)
        {
            IngredientPickup best = null; float bd = max;
            foreach (var p in IngredientPickup.Live) { if (p == null || FoodGuide.NeedsHold(f, p.id)) continue;   // V21：AI 和玩家同一规则，只去捡能自动捡的（空手 / 能配上的）
                float d = FlatDist(p.transform.position, pos); if (d < bd) { bd = d; best = p; } }
            return best;
        }
        bool FoodGoals(Vector3 pos)
        {
            var m = Match.I; var fs = f.Food;
            if (Match.Mode == GameMode.Cook)
            {
                var home = CookPot.Pots[f.team]; var enemyPot = CookPot.Pots[1 - f.team];
                if (home != null && (fs.ingredients.Count >= 2 || fs.ingredients.Count == 1 && FlatDist(home.transform.position, pos) < 25f)) { GoFood(home.transform.position); return true; }
                if (fs.CanCarry)
                {
                    var ing = NearestIngredient(pos, f.slot == 2 ? 30f : 999f);
                    if (ing != null) { GoFood(ing.transform.position); return true; }
                    if (enemyPot != null && m.roundsWon[1 - f.team] > 0)
                    {
                        GoFood(enemyPot.transform.position);
                        if (FlatDist(enemyPot.transform.position, pos) < 2.6f) f.botUse = true;
                        return true;
                    }
                }
                if (home != null && fs.ingredients.Count > 0) { GoFood(home.transform.position); return true; }
                return false;
            }
            if (Match.Mode == GameMode.Grow)
            {
                Snack best = null; float bd = 25f;
                foreach (var it in Interactable.All) if (it is Snack sn && !sn.Eaten) { float d = FlatDist(sn.transform.position, pos); if (d < bd) { bd = d; best = sn; } }
                if (best != null && f.hp < Config.MaxHp)
                {
                    GoFood(best.transform.position);
                    if (bd < 1.6f) f.botUse = true;
                    return true;
                }
                if (best != null && f.hp >= Config.MaxHp && bd < 12f) { f.hp = Mathf.Min(f.hp, Config.MaxHp - 1); }   // 变大模式：零食总是可以吃
            }
            if (m.BombCarrier == f || m.Planted) return false;
            foreach (var c in DeliveryCrate.Live) if (c != null && c.Landed && FlatDist(c.transform.position, pos) < 20f) { GoFood(c.transform.position); return true; }
            if (fs.CanCarry && !fs.Transformed) { var ing = NearestIngredient(pos, 11f); if (ing != null) { GoFood(ing.transform.position); return true; } }
            return false;
        }

        Vector3 NextCorner(Vector3 goal)
        {
            return corners.Length > cornerIdx ? corners[Mathf.Min(cornerIdx + 1, corners.Length - 1)] : goal;
        }

        static float FlatDist(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }

        // ---------------------------------------------------------------- combat
        void Engage()
        {
            var w = f.W;
            float d = Vector3.Distance(target.transform.position, f.transform.position);
            float want = w.pellets > 1 ? 6f : w.fullRange * .8f;
            bool sniper = w.id == "DMR-3" || w.id == "BR-5";
            if (sniper) want = 45f;   // V24：狙击手在 40–60 m 架枪，不往前凑（V23 远距离击杀只有 1%）
            bool anchored = !Attacker && FlatDist(holdSpot, f.transform.position) < 3f && !Match.I.Planted || sniper && d < 62f;
            if (d > want * 1.7f && !anchored) MoveAlong(target.transform.position, false);
            else
            {
                if (Time.time >= strafeSwitchAt) { strafeSign = -strafeSign; strafeSwitchAt = Time.time + Random.Range(.5f, 1.3f); }
                Vector3 side = f.transform.right * strafeSign;
                if (Physics.Raycast(f.transform.position + Vector3.up * .8f, side, 1.2f, ~0, QueryTriggerInteraction.Ignore)) { strafeSign = -strafeSign; side = -side; }
                Vector3 toward = d < want * .5f && w.pellets == 1 ? -f.transform.forward * .4f : Vector3.zero;
                f.botMove = (side + toward).normalized * (anchored ? .5f : .8f);
            }
            f.botCrouch = d > 18f && (w.id == "DMR-3" || w.id == "LMG-2") && Random.value < .02f ? !f.botCrouch : f.botCrouch && d > 12f;
            f.botAim = d > 11f && w.pellets == 1;
            float errK = Mathf.Clamp01(1f - (Time.time - aimErrorAt) / .8f);
            bool head = accuracy > .6f && d < 25f;
            // lead the target by bullet travel time (visible projectiles have flight time)
            float tof = d / Mathf.Max(50f, w.bulletSpeed);
            Vector3 aimPoint = (head ? target.Head : target.Chest) + aimError * errK * Mathf.Clamp(d / 15f, .5f, 2f) * (sniper ? .45f : 1f) + target.velocity * (tof + .04f);   // V24：狙击手远距离误差减半
            AimToward(aimPoint, 300f + accuracy * 200f, true);
            float ang = Vector3.Angle(f.AimDir, aimPoint - f.Eye);
            float tolerance = Mathf.Clamp(140f / Mathf.Max(d, 1f), 2.5f, 12f);
            if (Time.time >= reactionReadyAt && ang < tolerance && f.ammo > 0)
            {
                if (!w.automatic)
                {
                    if (Time.time >= semiAt) { f.botTrigger = true; semiAt = Time.time + w.ShotInterval * Random.Range(1.05f, 1.5f) + (d > 25 ? .15f : 0); }
                }
                else if (d > 20f)
                {
                    if (Time.time < burstUntil) f.botTrigger = true;
                    else if (Time.time >= pauseUntil) { burstUntil = Time.time + Random.Range(.25f, .45f); pauseUntil = burstUntil + Random.Range(.2f, .35f); }
                }
                else f.botTrigger = true;
            }
            if (f.ammo == 0) f.Reload();
        }

        void AimToward(Vector3 point, float degPerSec, bool precise)
        {
            Vector3 dir = point - f.Eye;
            if (dir.sqrMagnitude < .01f) return;
            Quaternion q = Quaternion.LookRotation(dir);
            float tYaw = q.eulerAngles.y, tPitch = q.eulerAngles.x; if (tPitch > 180) tPitch -= 360;
            if (!precise) tPitch = Mathf.Clamp(tPitch, -25, 15);
            f.yaw = Mathf.MoveTowardsAngle(f.yaw, tYaw, degPerSec * Time.deltaTime);
            f.pitch = Mathf.MoveTowards(f.pitch, tPitch, degPerSec * .6f * Time.deltaTime);
        }

        void StuckCheck()
        {
            if (Time.time < stuckAt) return;
            if (f.botMove.sqrMagnitude > .1f && Vector3.Distance(stuckPos, f.transform.position) < .4f)
            {
                f.botJump = true; repathAt = 0; strafeSign = -strafeSign;
                if (route != null && routeIdx < route.Length - 1 && Random.value < .5f) routeIdx++;
            }
            stuckPos = f.transform.position; stuckAt = Time.time + 1.2f;
        }

        // ---------------------------------------------------------------- skills (situational)
        // ---------------------------------------------------------------- V22 第三期：AI 会用的联动（规则型，设计稿 §5.1 第三期 c）
        // 夜鸦：守关口 / 进攻路过关口时，往 26 m 内看得见的火锅射签 → 芥末辣锅
        // 焚芯：进攻、目标包点有完整的蛋糕墙、离墙 5–18 m 且看得见墙 → 纸杯粘墙开洞，开出来后改走这个洞
        // 铆钉：守方（未下包）/ 攻方（已下包）15 m 内有没补的破口 → 走到破口旁边补上
        bool potUsed;
        bool RavenSavesForPot() { return !potUsed && Match.Mode == GameMode.Bomb && !Attacker && (holdZone == 3 || holdZone == 4) && Time.time - Match.I.liveStartedAt < 45f; }
        bool RouteNear(Vector3 c, float r) { if (route == null) return false; for (int i = routeIdx; i < route.Length; i++) if (FlatDist(route[i], c) < r) return true; return false; }
        bool TryLinkSkill(bool visible)
        {
            var m = Match.I; Vector3 pos = f.transform.position;
            if (Match.FFA || Match.Mode == GameMode.Cook || Match.Mode == GameMode.Grow) { if (f.role != Role.Raven) return false; }
            switch (f.role)
            {
                case Role.Falcon:
                {
                    // V24：路线前方 4–8.5 m 有果冻弹床 → 对准它汽水冲刺 = 可乐弹射（每回合一次；携弹者不跳）
                    if (colaUsed || visible || m.BombCarrier == f || Match.Mode != GameMode.Bomb || route == null || m.Planted) return false;
                    foreach (var jp in JumpPad.All)
                    {
                        if (jp == null) continue;
                        Vector3 c = jp.transform.position; float d = FlatDist(c, pos);
                        if (d < 4f || d > 8.5f || Mathf.Abs(c.y - pos.y) > .6f || !RouteNear(c + Vector3.ProjectOnPlane(jp.WorldLaunch, Vector3.up).normalized * 6f, 18f)) continue;
                        if (Physics.Linecast(pos + Vector3.up * .6f, c + Vector3.up * .4f, ~0, QueryTriggerInteraction.Ignore)) continue;
                        FaceFlat(c); colaUntil = Time.time + Config.DashWindup + Config.DashTime + Config.DashPadWindow + .3f;
                        if (Skills.Cast(f, c + (c - pos).normalized * 1.5f)) { colaUsed = true; ColaDashes++; if (m.autoplay) Debug.Log("NEON_AILINK r" + m.round + " cola " + f.Hero.name); return true; }
                        colaUntil = 0; return false;
                    }
                    return false;
                }
                case Role.Ironwall:
                {
                    // V24：守包点（或攻方下包后守包）听到敌人 25 m 内逼近 → 在 5 m 内开着的门框挂果冻门帘（入口保底照旧由 CurtainAllowed 管）
                    if (curtainUsed || visible || Match.Mode != GameMode.Bomb || Time.time - heardAt > 5f) return false;
                    bool duty = !Attacker && !m.Planted && holdZone <= 1 || Attacker && m.Planted;
                    if (!duty || FlatDist(lastKnown, pos) > 25f) return false;
                    foreach (var fr in Links.Frames())
                    {
                        if (!fr.open || Links.CurtainOf(fr) != null || !LinkPlus.CurtainAllowed(fr)) continue;
                        Vector3 to = fr.center - pos; float dy = to.y; to.y = 0;
                        if (to.magnitude > Config.ShieldDockRange - .3f || Mathf.Abs(dy) > 1.5f) continue;
                        FaceFlat(fr.center);
                        if (Links.DockFrame(f, out _) == null) continue;
                        if (Skills.Cast(f, fr.center)) { curtainUsed = true; CurtainCasts++; if (m.autoplay) Debug.Log("NEON_AILINK r" + m.round + " curtain " + f.Hero.name); return true; }
                        return false;
                    }
                    return false;
                }
                case Role.Raven:
                    if (visible) return false;
                    foreach (var h in HotpotHazard.All)
                    {
                        if (h == null || Links.PotBusy(h)) continue;
                        Vector3 c = h.transform.position; float d = FlatDist(c, pos);
                        if (d > 28f || d < 4f || Mathf.Abs(c.y - pos.y) > 3f) continue;
                        bool gateDuty = Match.Mode == GameMode.Bomb && !Attacker && (holdZone == 3 && c.z > 0 || holdZone == 4 && c.z < 0);
                        bool passing = Match.Mode == GameMode.Bomb && Attacker && !m.Planted && RouteNear(c, 20f);
                        bool other = Match.Mode != GameMode.Bomb && Time.time - heardAt < 4f;
                        if (!gateDuty && !passing && !other) continue;
                        if (!(Time.time - heardAt < 6f || Random.value < .3f)) continue;
                        Vector3 aim = c + Vector3.up * .55f;
                        if (Physics.Linecast(f.Eye, aim, out RaycastHit hh, ~0, QueryTriggerInteraction.Ignore) && FlatDist(hh.point, c) > 2f) continue;   // 看不到锅
                        FaceFlat(c);
                        if (Skills.Cast(f, aim)) { potUsed = true; if (Match.I.autoplay) Debug.Log("NEON_AILINK r" + m.round + " pot " + f.Hero.name); return true; }
                        return false;
                    }
                    return false;
                case Role.Ember:
                {
                    if (!Attacker || m.Planted || route == null || visible || Match.Mode != GameMode.Bomb) return false;
                    int site = SiteOf(route);
                    foreach (var w in Arena.I.breakWalls)
                    {
                        if (w == null || w == emberWall || (w.transform.position.z > 0 ? 0 : 1) != site || w.Opening() != null) continue;
                        bool pending = false; foreach (var bc in BlastCharge.Live) if (bc != null && bc.stuck != null && bc.stuck.wall == w) pending = true;
                        if (pending) continue;   // 已经有一个纸杯粘在这面墙上（2 次充能别连扔两个）
                        Vector3 c = w.transform.position + Vector3.up * 1.1f; float d = FlatDist(c, pos);
                        if (d > 18f || d < 5f) continue;
                        Vector3 n = w.transform.forward; if (Vector3.Dot(n, pos - c) < 0) n = -n;
                        Vector3 aim = c + n * .05f;
                        if (Physics.Linecast(f.Eye, aim + n * .3f, out RaycastHit hh, ~0, QueryTriggerInteraction.Ignore) && hh.collider.GetComponent<WallPiece>() == null) continue;   // 中间被挡住
                        FaceFlat(aim);
                        if (!Skills.Cast(f, aim)) return false;
                        emberWall = w; emberSide = n; emberAt = Time.time;
                        if (Match.I.autoplay) Debug.Log("NEON_AILINK r" + m.round + " breach-throw " + f.Hero.name);
                        return true;
                    }
                    return false;
                }
                case Role.Phantom:
                {
                    // V23 d 副联动：进攻时 20 m 内有没被糊住的守方监控 → 迷雾扔在监控下面糊住它（每回合一次）
                    if (gumUsed || !Attacker || m.Planted || Match.Mode != GameMode.Bomb || visible || route == null) return false;
                    Vector3 goal = route[route.Length - 1];
                    foreach (var cam in Arena.I.cams)
                    {
                        if (cam == null || !cam.Working || LinkPlus.Gummed(cam)) continue;
                        Vector3 cp = cam.transform.position; float d = FlatDist(cp, pos);
                        if (d > 20f || d < 3f || FlatDist(cp, goal) > 28f) continue;
                        Vector3 at = Skills.GroundAt(cp + Vector3.down * .5f);
                        if (Mathf.Abs(at.y - pos.y) > 2.5f) continue;
                        FaceFlat(at);
                        if (Skills.Cast(f, at)) { gumUsed = true; if (Match.I.autoplay) Debug.Log("NEON_AILINK r" + m.round + " gum " + f.Hero.name); return true; }
                        return false;
                    }
                    return false;
                }
                case Role.Rivet:
                {
                    if (Match.Mode != GameMode.Bomb) return false;
                    // V24：进攻路线经过威化坡道点（坡底 7 m 内）→ 斜放威化给队友搭坡（每回合一次）
                    if (!rampUsed && Attacker && !m.Planted && !visible && route != null)
                        foreach (var rs in LinkPlus.Ramps)
                        {
                            if (rs == null || rs.ramp != null || FlatDist(rs.basePos, pos) > 7f || Mathf.Abs(rs.basePos.y - pos.y) > 1f || !RouteNear(rs.top, 15f)) continue;
                            Vector3 mid = Vector3.Lerp(rs.basePos, rs.top, .5f); FaceFlat(mid);
                            if (Skills.Cast(f, mid)) { rampUsed = true; RampCasts++; if (m.autoplay) Debug.Log("NEON_AILINK r" + m.round + " ramp " + f.Hero.name); return true; }
                            break;
                        }
                    bool duty = !Attacker && !m.Planted || Attacker && m.Planted;
                    if (!duty || visible && target != null && Vector3.Distance(target.transform.position, pos) < 12f) return false;
                    foreach (var b in Links.Breaches())
                    {
                        if (Links.PatchOf(b) != null) continue;
                        float d = FlatDist(b.center, pos); if (d > 22f || Mathf.Abs(b.center.y - pos.y) > 1.5f) continue;
                        Vector3 n = b.rot * Vector3.forward; if (Vector3.Dot(n, pos - b.center) < 0) n = -n;
                        Vector3 stand = b.center + n * 2.6f;
                        if (FlatDist(stand, pos) > 2f) { linkGoal = stand; linkGoalUntil = Time.time + 2.5f; return false; }   // 先走过去（Objective 里走）
                        FaceFlat(b.center);
                        if (Skills.Cast(f, b.center)) { linkGoalUntil = 0; if (Match.I.autoplay) Debug.Log("NEON_AILINK r" + m.round + " patch " + f.Hero.name); return true; }
                        return false;
                    }
                    return false;
                }
            }
            return false;
        }
        // 焚芯开墙后：如果真的开出了洞，就改走这个洞进点
        void EmberReroute()
        {
            if (emberWall == null || Time.time - emberAt < Config.BlastWarning + .6f) return;
            var b = emberWall.Opening(); var w = emberWall; emberWall = null;
            if (b == null || route == null || Links.PatchOf(b) != null) return;
            int site = SiteOf(route);
            route = new[] { b.center + emberSide * 2.2f, b.center - emberSide * 2.6f, Arena.SiteCenter(site) }; routeIdx = 0; f.routeName += "→开墙";
            if (Match.I.autoplay) Debug.Log("NEON_AILINK r" + Match.I.round + " breach-route " + f.Hero.name + " " + w.name);
        }

        void ThinkSkill(bool visible)
        {
            EmberReroute();
            if (!Skills.CanUse(f)) return;
            if (TryLinkSkill(visible)) return;
            var m = Match.I;
            Vector3 pos = f.transform.position;
            Vector3 objective = m.Planted ? m.bombPos : Attacker && route != null ? route[route.Length - 1] : holdSpot;
            float toObj = FlatDist(objective, pos);
            float d = target != null ? Vector3.Distance(target.transform.position, pos) : 999f;
            switch (f.role)
            {
                case Role.Falcon:
                    if (visible && d > 6f && d < 20f && f.hp > 45 && Random.value < .5f)
                        Skills.Cast(f, pos + ((target.transform.position - pos).normalized + f.transform.right * strafeSign * .8f).normalized * 7f);
                    else if (!visible && Attacker && toObj > 8f && toObj < 20f && Random.value < .3f)
                        Skills.Cast(f, objective);
                    break;
                case Role.Rivet:
                    if ((visible && d > 11f && Random.value < .45f) || (m.Planted && Attacker && toObj < 6 && Random.value < .2f) || (m.Planted && !Attacker && FlatDist(m.bombPos, pos) < 2.5f))
                    {
                        Vector3 toward = visible ? target.transform.position : lastKnown;
                        Vector3 dir = toward - pos; dir.y = 0; if (dir.sqrMagnitude < .1f) dir = f.transform.forward; dir.Normalize();
                        f.yaw = Quaternion.LookRotation(dir).eulerAngles.y;
                        Skills.Cast(f, pos + dir * 2.6f);
                    }
                    break;
                case Role.Ironwall:
                    if (visible && d > 7f && (toObj < 12f || Random.value < .3f))
                    {
                        Vector3 dir = target.transform.position - pos; dir.y = 0;
                        f.yaw = Quaternion.LookRotation(dir).eulerAngles.y;
                        Skills.Cast(f, target.transform.position);
                    }
                    break;
                case Role.Raven:
                    if (RavenSavesForPot()) break;   // V22 第三期：守关口的夜鸦把签子留给火锅
                    if (!visible && toObj < 28f && toObj > 6f && Random.value < .4f)
                    {
                        Vector3 aim = Time.time - heardAt < 5f ? lastKnown : objective + new Vector3(Random.Range(-3f, 3f), 0, Random.Range(-3f, 3f));
                        if (Vector3.Distance(aim, pos) < 30f) { FaceFlat(aim); Skills.Cast(f, aim); }
                    }
                    break;
                case Role.Ember:
                    if (visible && d > 6f && d < 22f && (target.bombBusy || target.velocity.magnitude < 1.5f || Random.value < .25f))
                    { FaceFlat(target.transform.position); Skills.Cast(f, target.transform.position + target.velocity * .5f); }
                    else if (m.Planted && Attacker && !visible && m.defuser != null && m.defuser.alive && Vector3.Distance(m.bombPos, pos) < 20f)
                    { FaceFlat(m.bombPos); Skills.Cast(f, m.bombPos); }
                    break;
                case Role.Phantom:
                    bool enemyIntel = false;
                    foreach (var mate in m.fighters) if (mate.team == f.team && mate.alive && mate.revealedUntil > Time.time) enemyIntel = true;
                    if ((enemyIntel || (toObj < 14f && Random.value < .25f) || (m.Planted && FlatDist(m.bombPos, pos) < 3f)) && toObj < 22f)
                        Skills.Cast(f, enemyIntel ? pos : objective);
                    break;
            }
        }

        void FaceFlat(Vector3 p)
        {
            Vector3 dir = p - f.transform.position; dir.y = 0;
            if (dir.sqrMagnitude > .01f) f.yaw = Quaternion.LookRotation(dir).eulerAngles.y;
        }
    }
}
