using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace NeonSiege
{
    // Command-line verification modes:
    //   -neonTests      规则/技能/交互自动化测试，输出 Playtest/test_report.md
    //   -neonMatchTest  6 个 AI 全自动整场比赛，导出试玩记录 CSV
    //   -neonShots      渲染若干机位截图用于画面检查
    public class PlaytestRunner : MonoBehaviour
    {
        public string mode;
        readonly StringBuilder report = new StringBuilder();
        int passed, failed;

        IEnumerator Start()
        {
            yield return null; yield return null;
            if (mode == "tests") yield return Tests();
            else if (mode == "match") yield return MatchRun();
            else if (mode == "shots") { gameObject.AddComponent<HudCaptureBegin>(); gameObject.AddComponent<HudCaptureEnd>(); yield return Shots(); }
            else if (mode == "range") yield return RangeRun();
        }

        void Check(string id, string desc, bool ok, string detail)
        {
            if (ok) passed++; else failed++;
            report.AppendLine("| " + id + " | " + desc + " | " + (ok ? "通过" : "**未通过**") + " | " + detail + " |");
            Debug.Log("NEON_TEST " + id + " " + (ok ? "PASS" : "FAIL") + " " + detail);
        }

        static void Place(Fighter f, Vector3 p, float yaw)
        {
            f.StopMantle(); f.botMove = Vector3.zero; f.botSprint = false; f.botTrigger = false;   // 清掉 AI 留下的移动指令
            f.cc.enabled = false; f.transform.position = p; f.cc.enabled = true;
            f.yaw = yaw; f.pitch = 0; f.transform.rotation = Quaternion.Euler(0, yaw, 0); f.velocity = Vector3.zero;
        }

        void ForceLive()
        {
            var m = Match.I;
            m.phase = Phase.Live; m.roundClock = Config.RoundTime;
            m.arena.SetSpawnBarriers(false);
            foreach (var f in m.fighters) { var b = f.GetComponent<Bot>(); if (b != null) b.enabled = false; f.skillCharges = 1; f.botMove = Vector3.zero; f.botUse = false; f.botTrigger = false; }
        }

        void ReviveAll()
        {
            var m = Match.I;
            foreach (var f in m.fighters) f.Respawn(m.arena.SpawnPoint(f.team == m.AttackingTeam, f.slot), 0);
        }

        Fighter Get(int team, Role role)
        {
            foreach (var f in Match.I.fighters) if (f.team == team) { if (f.role != role) f.ChangeRole(role); return f; }
            return null;
        }
        Fighter Other(int team, int index) { int i = 0; foreach (var f in Match.I.fighters) if (f.team == team && i++ == index) return f; return null; }

        IEnumerator Hold(Fighter f, float seconds)
        {
            f.scriptUse = true;
            yield return new WaitForSeconds(seconds);
            f.scriptUse = false;
            yield return null;
        }

        static string Comp(int team) { var s = ""; foreach (var f in Match.I.fighters) if (f.team == team) s += f.Hero.name; return s; }

        IEnumerator Tests()
        {
            var m = Match.I;
            Time.timeScale = 1;
            report.AppendLine("# 《霓城封锁》自动化测试报告 · V3 爆破模式");
            report.AppendLine();
            report.AppendLine("运行时间：" + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "，Unity " + Application.unityVersion + "。每一项都在打包后的程序里实际运行判定，不是人工勾选。");
            report.AppendLine();
            report.AppendLine("| 编号 | 测试情境 | 结果 | 实测数据 |");
            report.AppendLine("| --- | --- | --- | --- |");

            Check("P0", "地图、NavMesh、6 名角色、中文字体、写实模型资源", m.fighters.Count == 6 && m.arena.navReady && m.ui.HasChineseFont
                && Resources.Load<GameObject>("Models/PH/modular_urban_apartments_facade/modular_urban_apartments_facade") != null
                && Resources.Load<RuntimeAnimatorController>("Anim/Fighter") != null,
                "fighters=" + m.fighters.Count + " navmesh=" + m.arena.navReady + " font=" + m.ui.HasChineseFont + " 互动物件=" + Interactable.All.Count);
            bool unique = true;
            for (int t = 0; t < 2; t++) { var seen = new HashSet<Role>(); foreach (var f in m.fighters) if (f.team == t && !seen.Add(f.role)) unique = false; }
            Check("P0", "同队不可重复选取相同角色", unique, Comp(0) + " / " + Comp(1));

            ForceLive();
            int att = m.AttackingTeam, def = m.DefendingTeam;
            yield return null;

            // --- armor / TTK
            var dummy = Other(def, 0);
            dummy.hp = 100; dummy.armor = 50;
            dummy.ReceiveDamage(60, Other(att, 0), Vector3.zero, false);
            Check("R01", "护甲先扣：生命 100 + 护甲 50 = 有效 150", Mathf.Approximately(dummy.armor, 0) && Mathf.Approximately(dummy.hp, 90),
                "受到 60 伤害后 护甲=" + dummy.armor + " 生命=" + dummy.hp + "；SMG-7 躯干击杀需 " + Config.Hero(Role.Falcon).weapon.ShotsToKill + " 发（原 6 发）");
            dummy.hp = 100; dummy.armor = 50;

            // --- visible projectile with travel time
            var shooter = Other(att, 0); var tgt = Other(def, 1);
            // V22 第二期：原 (-50,36)→(0,36) 这条 50 m 线被寿司吧 / 外卖铺切断（这正是改图目标），换到长街西段（寿司吧以西）31 m 的线
            Place(shooter, new Vector3(-66, .05f, 38.5f), 90); Place(tgt, new Vector3(-35, .05f, 38.5f), -90);
            tgt.hp = 100; tgt.armor = 50;
            yield return new WaitForFixedUpdate(); yield return null;
            Bullet.Fire(shooter, shooter.Eye, shooter.Eye, (tgt.Chest - shooter.Eye).normalized, Color.white);
            yield return null;
            bool notYet = tgt.armor >= 50;
            yield return new WaitForSeconds(.6f);
            bool arrived = tgt.armor < 50 || tgt.hp < 100;
            Check("R02", "子弹可见且有飞行时间：31 m 外不是瞬间命中", notYet && arrived, "发射后第 1 帧未命中=" + notYet + "，" + shooter.W.id + " 弹速 " + shooter.W.bulletSpeed + " m/s，0.6 s 内命中=" + arrived);

            // --- T01 dash into wall
            var falcon = Get(att, Role.Falcon);
            Place(falcon, new Vector3(16, .05f, 10.5f), 0);
            yield return new WaitForFixedUpdate(); yield return null;
            bool cast = Skills.Cast(falcon, falcon.transform.position + Vector3.forward * 7);
            bool cue = Sfx.LastPlayed == "thrusterFire";
            yield return new WaitForSeconds(.1f);
            float hp0 = falcon.hp + falcon.armor;
            falcon.ReceiveDamage(10, Other(def, 0), Vector3.zero, false);
            bool hurtInDash = falcon.hp + falcon.armor < hp0 && falcon.Dashing;
            yield return new WaitForSeconds(.5f);
            Vector3 fp = falcon.transform.position;
            int bad = 0; foreach (var o in Physics.OverlapCapsule(fp + Vector3.up * .45f, fp + Vector3.up * 1.4f, .3f, ~0, QueryTriggerInteraction.Ignore)) if (!o.transform.IsChildOf(falcon.transform)) bad++;
            Check("T01", "猎隼贴墙突进：无穿模、无无敌、启动提示可识别", cast && cue && bad == 0 && fp.z < 13.7f && hurtInDash,
                "起点 z=10.5 → 终点 z=" + fp.z.ToString("0.00") + "（墙面 13.7），重叠 " + bad + "，突进中受伤=" + hurtInDash + "，音效=" + cue);
            Skills.ClearRound();

            // --- T02 cover legality
            var rivet = Get(att, Role.Rivet);
            Place(rivet, new Vector3(20, .05f, 10f), 0); yield return null;
            bool illegal = !Skills.CoverPlacementAt(rivet, new Vector3(20, 0, 12.5f), out _, out _);
            Place(rivet, new Vector3(-33, .05f, 4.5f), 90); yield return null;
            rivet.skillCharges = 1;
            bool placed = Skills.Cast(rivet, new Vector3(-30.4f, 0, 4.5f));
            yield return new WaitForSeconds(1.2f);
            var path = new NavMeshPath();
            NavMesh.SamplePosition(m.arena.SpawnPoint(true, 1), out NavMeshHit pa, 3, NavMesh.AllAreas);
            NavMesh.SamplePosition(Arena.SiteA, out NavMeshHit pb, 3, NavMesh.AllAreas);
            bool pathOk = NavMesh.CalculatePath(pa.position, pb.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
            Check("T02", "铆钉掩体：卷帘门口禁止投放，主街可投放且不封死通路", illegal && placed && pathOk, "门口非法=" + illegal + "，主街投放=" + placed + "，攻方出生→A 点路径完整=" + pathOk);
            Skills.ClearRound();

            // --- T03 drone radius + destroyable
            var raven = Get(att, Role.Raven);
            var near = Other(def, 0); var far = Other(def, 1);
            // V22 第二期：原位置 (-34,38)/(-30,40)/(-26,43) 被寿司吧盖住，整组平移 (+10, +3)，相对距离不变（4.7 m 内 / 15.8 m 外）
            Vector3 arrowAt = new Vector3(-20, 0, 43);
            Place(raven, new Vector3(-24, .05f, 41), 90); Place(near, new Vector3(-16.5f, .05f, 46.2f), 180); Place(far, new Vector3(-4.2f, .05f, 43), -90);
            near.revealedUntil = far.revealedUntil = 0;
            yield return new WaitForFixedUpdate();
            // V6：夜鸦改为射出侦测箭 —— 箭矢飞行后钉在地面，成为扫描信标
            Skills.ShootArrow(raven, Skills.ArrowVelocityTo(raven.Eye, arrowAt));
            Drone drone = null; float fly = Time.time;
            while (drone == null && Time.time - fly < 2f) { drone = FindObjectOfType<Drone>(); yield return null; }
            bool stuck = drone != null && Vector3.Distance(drone.transform.position, arrowAt) < 1.5f && drone.GetComponent<Arrow>() == null;
            float t0 = drone != null ? drone.Landed : Time.time; bool early = true;
            // frame-rate independent: before the scan time the enemy must never be revealed
            while (Time.time - t0 < Config.DroneScanDelay + .4f)
            {
                if (Time.time - t0 < Config.DroneScanDelay - .05f && near.Revealed) early = false;
                yield return null;
            }
            bool nearSeen = near.Revealed, farHidden = !far.Revealed;
            var dGo2 = new GameObject("test drone 2"); dGo2.transform.localScale = new Vector3(.38f, .04f, .38f);
            var drone2 = dGo2.AddComponent<Drone>(); drone2.Init(raven, new Vector3(-21, .1f, 44));
            drone2.TakeDamage(10, near, false);
            yield return null;
            Check("T03", "夜鸦侦测箭：箭矢钉入落点，只发现范围内敌人，可被击毁", stuck && early && nearSeen && farHidden && drone2 == null, "箭矢命中落点=" + stuck + "，扫描前未暴露=" + early + "，5 m 发现=" + nearSeen + "，15.8 m（半径外）未发现=" + farHidden + "，可击毁=" + (drone2 == null));
            if (drone != null) Destroy(drone.gameObject);

            // --- T04 blast vs light cover
            var ember = Get(att, Role.Ember);
            Destructible crate = null;
            foreach (var d in m.arena.destructibles) if (Vector3.Distance(d.transform.position, new Vector3(-16, 0, -6)) < 1f) crate = d;
            var victim = Other(def, 2);
            Place(ember, new Vector3(-24, .05f, -3), 90); Place(victim, crate.transform.position + new Vector3(1.6f, .05f, 1.4f), -90);
            victim.hp = 100; victim.armor = 50;
            yield return new WaitForFixedUpdate();
            new GameObject("test charge").AddComponent<BlastCharge>().Init(ember, crate.transform.position + new Vector3(0, 1.4f, 0));
            yield return new WaitForSeconds(Config.BlastWarning + .2f);
            bool gone = !crate.Alive && !crate.GetComponent<Collider>().enabled;
            float taken = 150 - victim.hp - victim.armor;
            yield return new WaitForSeconds(.3f);
            var p2 = new NavMeshPath(); Vector3 c = crate.transform.position;
            bool walk = NavMesh.CalculatePath(c + new Vector3(-2.2f, 0, -.8f), c + new Vector3(2.2f, 0, -.8f), NavMesh.AllAreas, p2) && p2.status == NavMeshPathStatus.PathComplete;   // V22：木箱北侧现在贴着高摊位岛，改成东西方向穿过木箱原位置
            Check("T04", "焚芯爆破轻型掩体：碰撞同步移除、可通行；角色伤害≤" + Config.BlastMaxDamage, gone && taken > 0 && taken <= Config.BlastMaxDamage + .01f && walk, "摧毁=" + gone + "，2.1 m 敌人受伤 " + taken.ToString("0.0") + "，可通行=" + walk);
            crate.Restore();

            // --- interactables
            var shutter = m.arena.shutters.Find(s => !s.gate);   // V22 第二期：shutters[0] 是蒸笼门（默认开），这条测卷帘门
            bool closedBlocks = shutter.GetComponent<Collider>().enabled;
            shutter.SetOpen(true); yield return new WaitForSeconds(2f);
            bool openPasses = !shutter.GetComponent<Collider>().enabled;
            Check("I01", "卷帘门：关闭时阻挡，升起后可通行（NavMesh 同步）", closedBlocks && openPasses, "关闭碰撞=" + closedBlocks + "，升起后碰撞关闭=" + openPasses);
            shutter.ResetRound();

            var pbox = m.arena.powerBoxes[0]; var user = Other(att, 1);
            Place(user, pbox.transform.position + pbox.transform.forward * 1f, 0);
            yield return null;
            float tt = 0; while (tt < 1.4f) { pbox.Hold(user, Time.deltaTime); tt += Time.deltaTime; yield return null; }
            bool dark = pbox.Dark && pbox.lights.Count > 0 && pbox.lights.TrueForAll(l => l.intensity == 0);
            Check("I02", "电闸：拉闸后包点灯光全部熄灭", dark, "受控灯光 " + pbox.lights.Count + " 盏，断电=" + pbox.Dark);
            pbox.ResetRound();

            var barrel = m.arena.barrels[0]; var bystander = Other(def, 0);
            Place(bystander, barrel.transform.position + new Vector3(2f, .05f, 0), 0); bystander.hp = 100; bystander.armor = 50;
            yield return new WaitForFixedUpdate();
            barrel.TakeDamage(40, Other(att, 0), false);
            yield return new WaitForSeconds(.8f);
            float bdmg = 150 - bystander.hp - bystander.armor;
            Check("I03", "爆炸桶：中弹引燃后爆炸，伤害附近角色", bdmg > 20, "2 m 处角色受到 " + bdmg.ToString("0") + " 伤害");
            barrel.ResetRound(); bystander.hp = 100; bystander.armor = 50;

            var glass = m.arena.glass[0];
            bool absorbed = glass.TakeDamage(10, null, false);
            bool broken = !glass.GetComponent<Collider>().enabled;
            Check("I04", "玻璃：被击中后碎裂并移除碰撞", broken, "碎裂=" + broken);
            glass.ResetRound();

            MedStation med = null; foreach (var i in Interactable.All) if (i is MedStation) med = (MedStation)i;
            var patient = Other(att, 2); Place(patient, med.transform.position + med.transform.forward * 1f, 0); patient.hp = 40; patient.armor = 0;
            tt = 0; while (tt < 1.2f) { med.Hold(patient, Time.deltaTime); tt += Time.deltaTime; yield return null; }
            Check("I05", "医疗站：+50 生命 +25 护甲，每回合一次", Mathf.Approximately(patient.hp, 90) && Mathf.Approximately(patient.armor, 25) && !med.Usable(patient), "生命 40→" + patient.hp + "，护甲 0→" + patient.armor);

            var pad = FindObjectOfType<JumpPad>(); var jumper = Other(att, 0);
            Place(jumper, pad.transform.position + Vector3.up * .1f, 0);
            float y0 = jumper.transform.position.y, ymax = y0;
            for (int i = 0; i < 40; i++) { yield return null; ymax = Mathf.Max(ymax, jumper.transform.position.y); }
            Check("I06", "弹射板：把角色弹起", ymax - y0 > 2.5f, "最大升高 " + (ymax - y0).ToString("0.0") + " m");

            // --- V7 map: jump-pad-only sniper nests must actually land the player on top
            var flyer = Other(att, 0); var flyBot = flyer.GetComponent<Bot>(); if (flyBot != null) flyBot.enabled = false;
            flyer.botMove = Vector3.zero; flyer.botJump = false;
            var landings = new System.Text.StringBuilder(); bool allLanded = true;
            foreach (var jp in FindObjectsOfType<JumpPad>())
            {
                if (!jp.worldLaunch) continue;
                // settle first (clears leftover vertical speed from the previous launch)
                Place(flyer, jp.transform.position + new Vector3(2.5f, .1f, 2.5f), 0); flyer.botMove = Vector3.zero;
                for (float w = 0; w < 1f; w += Time.deltaTime) yield return null;
                Place(flyer, jp.transform.position + Vector3.up * .1f, 0);
                float t1 = 0; bool left = false;
                float peak = 0;
                while (t1 < 3.5f) { yield return null; t1 += Time.deltaTime; peak = Mathf.Max(peak, flyer.transform.position.y); if (!flyer.grounded) left = true; else if (left && t1 > .4f) break; }
                float yEnd = flyer.transform.position.y;
                bool ok = yEnd > 5.5f;
                if (!ok) { var v0 = jp.launch; Vector3 pp = jp.transform.position + Vector3.up; for (int k = 0; k < 40; k++) { Vector3 nx = pp + v0 * .04f; v0 += Vector3.down * 17f * .04f; if (Physics.SphereCast(pp, .35f, nx - pp, out RaycastHit hh, (nx - pp).magnitude)) { landings.Append("[撞到 ").Append(hh.collider.name).Append(" @").Append(hh.point.ToString("0.0")).Append("]"); break; } pp = nx; } }
                allLanded &= ok; landings.Append(jp.name.Replace("弹射板 ", "")).Append(' ').Append(yEnd.ToString("0.0")).Append("m（最高 ").Append(peak.ToString("0.0")).Append("）；");
            }
            Check("M01", "弹射板高台：市场楼顶 / 水塔 / 空中连廊都能弹上去并站稳（V22：市场楼顶另加了东侧楼梯，弹射板保留）", allLanded && landings.Length > 0, landings.ToString());
            if (flyBot != null) flyBot.enabled = true;

            // V22 第二期（用户已同意改写）：蒸笼门默认开 → 先关门再测，断言不变
            Shutter northGate = null; foreach (var sh in m.arena.shutters) if (sh.gate && sh.transform.position.z > 0) northGate = sh;
            var gp = new NavMeshPath(); System.Func<float> len = () =>
            {
                if (!NavMesh.CalculatePath(new Vector3(-6, 0, 40), new Vector3(14, 0, 40), NavMesh.AllAreas, gp) || gp.status != NavMeshPathStatus.PathComplete) return 999f;
                float L = 0; for (int i = 1; i < gp.corners.Length; i++) L += Vector3.Distance(gp.corners[i - 1], gp.corners[i]); return L;
            };
            bool startedOpen = northGate != null && northGate.open;
            if (northGate != null) { northGate.SetOpen(false); yield return new WaitForSeconds(Shutter.GateMoveTime + .4f); }
            float closedLen = len(); bool gateBlocks = northGate != null && northGate.GetComponent<Collider>().enabled;
            if (northGate != null) { northGate.SetOpen(true); yield return new WaitForSeconds(Shutter.GateMoveTime + .4f); }
            float openLen = len();
            Check("M02", "北长街蒸笼门：先关门 → 长街被封死（只能绕中路 / 烤串巷），再开门 → 直通", gateBlocks && closedLen > 45f && openLen < 26f, "开局开着=" + startedOpen + "，关门时绕行 " + closedLen.ToString("0") + " m，开门后 " + openLen.ToString("0") + " m");
            if (northGate != null) northGate.ResetRound();

            // --- V9 movement: slide is faster than sprint; mantle climbs a 1 m concrete barrier
            var me = m.player;
            if (me != null)
            {
                Place(me, new Vector3(-40, .05f, -2), 90);
                for (float w = 0; w < .3f; w += Time.deltaTime) yield return null;
                Vector3 s0 = me.transform.position; me.StartSlide(); float st = 0;
                while (st < .4f) { yield return null; st += Time.deltaTime; }
                float slideSpeedMeasured = (me.transform.position - s0).magnitude / st;
                Check("M04", "滑铲：冲刺中下蹲滑行，速度高于冲刺", slideSpeedMeasured > Config.SprintSpeed, "滑铲前 0.4 秒平均速度 " + slideSpeedMeasured.ToString("0.0") + " m/s（冲刺 " + Config.SprintSpeed + " m/s）");
                for (float w = 0; w < 1f; w += Time.deltaTime) yield return null;
                // concrete barrier on the north street at (-24, 0, 37), ~1.05 m tall
                Place(me, new Vector3(-24, .05f, 35.8f), 0);
                for (float w = 0; w < .3f; w += Time.deltaTime) yield return null;
                float y0m = me.transform.position.y; bool started = me.DebugMantle(); float mt = 0;
                while (me.Mantling && mt < 1.5f) { yield return null; mt += Time.deltaTime; }
                for (float w = 0; w < .2f; w += Time.deltaTime) yield return null;
                float climbed = me.transform.position.y - y0m;
                Check("M05", "翻越：跳向齐胸高的障碍会翻上去", started && climbed > .6f, "触发=" + started + "，升高 " + climbed.ToString("0.00") + " m，用时 " + mt.ToString("0.00") + " s");

                if (ToonAssets.Available)
                {
                    // V16：松饼塔（北长街 -32, 43.5）三层，连续翻越爬到顶；美食方块 2 m 也能直接翻上去
                    Place(me, new Vector3(-46.5f, .05f, 44f - 3.0f - .55f), 0);
                    for (float w = 0; w < .3f; w += Time.deltaTime) yield return null;
                    float py0 = me.transform.position.y; int ups = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        if (!me.DebugMantle()) break; ups++; float t2 = 0;
                        while (me.Mantling && t2 < 1.5f) { yield return null; t2 += Time.deltaTime; }
                        for (float w = 0; w < .25f; w += Time.deltaTime) yield return null;
                    }
                    float tower = me.transform.position.y - py0;
                    Place(me, new Vector3(-40, .05f, 41f - 1.22f - .55f), 0);
                    for (float w = 0; w < .3f; w += Time.deltaTime) yield return null;
                    float by0 = me.transform.position.y; bool bStart = me.DebugMantle(); float bt = 0;
                    while (me.Mantling && bt < 1.5f) { yield return null; bt += Time.deltaTime; }
                    for (float w = 0; w < .2f; w += Time.deltaTime) yield return null;
                    float block = me.transform.position.y - by0;
                    Check("M11", "美食掩体可攀爬：松饼塔一层层翻到顶，美食方块能直接翻上去", ups >= 3 && tower > 2.4f && bStart && block > 1.8f,
                        "松饼塔翻越 " + ups + " 次、升高 " + tower.ToString("0.00") + " m；巧克力方块升高 " + block.ToString("0.00") + " m");
                }
                if (ToonAssets.Available)
                {
                    // V17：美食枪 / 食物子弹 / 酱汁 / 平底锅挡子弹
                    int guns = 0, ammoOk = 0; bool tea = false, haws = false, spin = false;
                    foreach (var wpn in Config.Arsenal)
                    {
                        var fgun = DessertGuns.Create(wpn, new GunParts(), false); var an = fgun.GetComponent<FoodGunAnim>();
                        if (an != null) { guns++; tea |= an.tea != null; haws |= an.haws != null && an.haws[4] != null; spin |= an.spin != null; }
                        var am = FoodFx.AmmoModel(wpn); if (am != null) { ammoOk++; Destroy(am); }
                        Destroy(fgun.gameObject);
                    }
                    FoodFx.Impact(new Vector3(-40, .02f, 0), Vector3.up, Config.Pistol);
                    yield return null;
                    bool splat = FindObjectsOfType<SplatPop>().Length > 0;
                    var sh = Other(att, 0); var vic = Other(def, 1);
                    Place(sh, new Vector3(-50, .05f, 36), 90); Place(vic, new Vector3(-42, .05f, 36), -90);
                    vic.hp = 100; vic.armor = 50; vic.meleeUntil = Time.time + 2f;
                    yield return new WaitForFixedUpdate(); yield return null;
                    Bullet.Fire(sh, sh.Eye, sh.Eye, (vic.Chest - sh.Eye).normalized, Color.white);
                    yield return new WaitForSeconds(.3f);
                    bool blocked = vic.hp >= 100 && vic.armor >= 50;
                    vic.meleeUntil = 0;
                    for (int k = 0; k < 4; k++) { Bullet.Fire(sh, sh.Eye, sh.Eye, (vic.Chest - sh.Eye).normalized, Color.white); yield return new WaitForSeconds(.15f); }   // 幻频被动有 15% 概率挡掉一发
                    yield return new WaitForSeconds(.2f);
                    bool hurt = vic.hp + vic.armor < 150;
                    vic.hp = 100; vic.armor = 50;
                    // V21：外部素材包的整块枪模型已拆件（Tools/blender_foodgun_parts.py），液面 TeaLevel / 糖葫芦 Haw0-4 / 转盘 Spin 恢复为通过条件
                    Check("M12", "美食枪：7 把枪和食物子弹都能加载，奶茶液面/糖葫芦/寿司转盘可动，打墙留酱汁，平底锅挡正面子弹",
                        guns == Config.Arsenal.Length && ammoOk == guns && tea && haws && spin && splat && blocked && hurt,
                        "枪 " + guns + "/" + Config.Arsenal.Length + "，子弹模型 " + ammoOk + "，液面=" + tea + " 糖葫芦=" + haws + " 转盘=" + spin + "，酱汁=" + splat + "，锅挡住=" + blocked + "，不挡时受伤=" + hurt);
                }
                if (ToonAssets.Available)
                {
                    // V17 第二阶段：零食回血 / 巧克力酱减速 / 火锅烫 / 热气挡视线 / AI 翻越美食方块
                    Snack snack = null; foreach (var it in Interactable.All) if (it is Snack sn) { snack = sn; break; }
                    var eater = Other(att, 2); eater.hp = 50;
                    bool ate = false;
                    if (snack != null) { Place(eater, snack.transform.position + Vector3.right * .6f, -90); yield return null; for (float w = 0; w < 1f && !ate; w += Time.deltaTime) { ate = snack.Hold(eater, Time.deltaTime); yield return null; } }
                    bool healed = ate && eater.hp >= 74f && snack.Eaten; eater.hp = 100;
                    bool choco = SauceZone.At(new Vector3(-40, 0, 4)) == SauceZone.Chocolate, butter = SauceZone.At(new Vector3(-14, 0, -4.5f)) == SauceZone.Butter;
                    var cook = Other(def, 2); cook.hp = 100; cook.armor = 50;
                    Place(cook, new Vector3(8, 1.0f, -35.5f), 0);
                    for (float w = 0; w < 1.2f; w += Time.deltaTime) yield return null;
                    bool burned = cook.hp + cook.armor < 150; Place(cook, new Vector3(50, .05f, 0), -90); cook.hp = 100; cook.armor = 50;   // 离开火锅
                    bool steamBlocks = !Skills.SightClear(new Vector3(8, 2.2f, -40.5f), new Vector3(8, 2.2f, -30.5f), null) && Skills.SightClear(new Vector3(0, 1.6f, -41f), new Vector3(0, 1.6f, -47f), null);
                    var climber = Other(def, 0); var cb = climber.GetComponent<Bot>(); float topY = 0;
                    if (cb != null)
                    {
                        Place(climber, new Vector3(33.5f, .05f, 21.5f), 0);
                        yield return new WaitForFixedUpdate(); yield return null;
                        bool wasOn = cb.enabled; cb.enabled = true;
                        cb.forceGoal = new Vector3(33.5f, 2f, 26f);
                        for (float w = 0; w < 8f && climber.transform.position.y < 1.8f; w += Time.deltaTime) yield return null;
                        topY = climber.transform.position.y; cb.forceGoal = null; cb.enabled = wasOn;
                        var pth = new UnityEngine.AI.NavMeshPath(); bool ok = UnityEngine.AI.NavMesh.CalculatePath(new Vector3(33.5f, 0, 21.5f), new Vector3(33.5f, 2f, 26f), UnityEngine.AI.NavMesh.AllAreas, pth);
                        bool sTop = UnityEngine.AI.NavMesh.SamplePosition(new Vector3(33.5f, 2f, 26f), out UnityEngine.AI.NavMeshHit th2, 1f, UnityEngine.AI.NavMesh.AllAreas);
                        string cs = ""; foreach (var pc in pth.corners) cs += "(" + pc.x.ToString("0.0") + "," + pc.y.ToString("0.0") + "," + pc.z.ToString("0.0") + ")";
                        Debug.Log("NEON_CLIMB path=" + ok + " status=" + pth.status + " topNav=" + sTop + (sTop ? th2.position.ToString() : "") + " corners=" + cs + " botPos=" + climber.transform.position);
                    }
                    Check("M13", "美食地图玩法：零食回血、巧克力酱/黄油地面、火锅会烫、热气挡视线、AI 会翻上蛋糕方块",
                        healed && choco && butter && burned && steamBlocks && topY > 1.8f && m.arena.climbLinks > 0,
                        "吃零食=" + healed + "，巧克力=" + choco + " 黄油=" + butter + "，火锅烫=" + burned + "，热气挡视线=" + steamBlocks + "，AI 爬到 " + topY.ToString("0.0") + " m（导航连接 " + m.arena.climbLinks + " 条）");
                }
                if (ToonAssets.Available)
                {
                    // V17 第三、四阶段：蛋糕炸弹文案 / 小费 / 豆子角色 + 美食帽子
                    bool words = ToonAssets.FoodText("按住 G 安装炸弹").Contains("蜡烛") && ToonAssets.FoodText("资金 $2000").StartsWith("小费") && ToonAssets.FoodText("按住 G 拆除炸弹").Contains("吹灭");
                    int beans = 0, hats = 0;
                    foreach (var f in m.fighters)
                    {
                        foreach (var t in f.GetComponentsInChildren<Transform>(true)) { if (t.name.StartsWith("Shoe.")) { beans++; break; } }
                        foreach (var t in f.GetComponentsInChildren<Transform>(true)) { if (t.name == "美食帽子") { hats++; break; } }
                    }
                    Check("M14", "蛋糕爆破文案（点蜡烛 / 吹蜡烛 / 小费），6 个角色都是糖豆人式豆子并戴着美食帽子", words && beans == m.fighters.Count && hats == m.fighters.Count,
                        "文案=" + words + "，豆子角色 " + beans + "/" + m.fighters.Count + "，美食帽子 " + hats + "/" + m.fighters.Count);
                }
                if (ToonAssets.Available)
                {
                    // ---------------- V18 A：敌我颜色 / 阵亡消失 / 火锅回血炉
                    Color BodyColor(Fighter f)
                    {
                        foreach (var r in f.GetComponentsInChildren<Renderer>(true))
                            if (r.name == "Body") foreach (var mt in r.materials) if (mt.name.StartsWith("Character_Main") && mt.HasProperty("baseColorFactor")) return mt.GetColor("baseColorFactor");
                        return Color.black;
                    }
                    Color v18c0 = BodyColor(Other(att, 0)), v18c1 = BodyColor(Other(def, 0));
                    bool v18colorsDiffer = Mathf.Abs(v18c0.r - v18c1.r) + Mathf.Abs(v18c0.b - v18c1.b) > .6f;
                    var v18corpse = Other(def, 1); Place(v18corpse, new Vector3(-30, .05f, -2), 0); yield return null;
                    v18corpse.ReceiveDamage(999, Other(att, 0), v18corpse.transform.position, false);
                    for (float w = 0; w < 1.3f; w += Time.deltaTime) yield return null;
                    bool v18hidden = true; foreach (var r in v18corpse.GetComponentsInChildren<Renderer>()) if (r.enabled && r.name != "Shine") v18hidden = false;
                    bool v18plate = GameObject.Find("Plate(Clone)") != null;
                    v18corpse.Respawn(new Vector3(-30, .05f, -2), 0); yield return null;
                    bool v18back = false; foreach (var r in v18corpse.GetComponentsInChildren<Renderer>()) if (r.enabled && r.name == "Body") v18back = true;
                    KitchenStation v18pot = null; foreach (var it in Interactable.All) if (it is KitchenStation ks && ks.kind == KitchenStation.Kind.HotpotHeal) { v18pot = ks; break; }
                    var v18diner = Other(att, 2); v18diner.hp = 40; float v18hp0 = v18diner.hp;
                    if (v18pot != null) { Place(v18diner, v18pot.transform.position + Vector3.right * 1.2f, -90); yield return null; for (float w = 0; w < 1.5f; w += Time.deltaTime) { v18pot.Hold(v18diner, Time.deltaTime); yield return null; } }
                    bool v18potHeal = v18diner.hp >= v18hp0 + 10f && v18diner.Revealed; v18diner.hp = 100;
                    Check("M15", "敌我颜色不同、阵亡后消失并留下盘子、复活后恢复、火锅回血炉回血并暴露位置", v18colorsDiffer && v18hidden && v18plate && v18back && v18potHeal,
                        "蓝 " + v18c0 + " 红 " + v18c1 + "，消失=" + v18hidden + " 盘子=" + v18plate + " 复活显示=" + v18back + "，火锅回血 " + v18hp0.ToString("0") + "→" + (v18hp0 + (v18potHeal ? 10 : 0)) + "+ 暴露=" + v18potHeal);

                    // ---------------- V18 B：食材变身 / 黑暗料理 / 食物反应 / 奶油补墙
                    var v18chef = Other(att, 1); Place(v18chef, new Vector3(-40, .05f, 2), 90); v18chef.armor = 50;
                    v18chef.Food.Pick("Flour"); v18chef.Food.Pick("Egg");
                    bool v18cake = v18chef.Food.Transformed && v18chef.Food.form == FoodForm.Cake && v18chef.armor > Config.MaxArmor;
                    v18chef.Food.EndForm(false); v18chef.Food.Pick("Chili"); v18chef.Food.Pick("Sugar");
                    bool v18dark = v18chef.Food.form == FoodForm.Dark;
                    v18chef.Food.EndForm(false); v18chef.Food.Pick("Milk"); v18chef.Food.Pick("Boba");
                    int v18ammo0 = v18chef.ammo; bool v18milk = v18chef.Food.InfiniteAmmo; v18chef.Food.EndForm(false); v18chef.armor = 50;
                    bool v18recipes = Ingredients.Recipe("Rice", "Nori") == FoodForm.Onigiri && Ingredients.Recipe("Cheese", "Bread") == FoodForm.Burger && Ingredients.Recipe("Sugar", "Ice") == FoodForm.IceCream && Ingredients.Recipe("HotpotBase", "Chili") == FoodForm.Spicy;
                    SauceZone.Mark(new Vector3(-40, 0, 4), 3f, true); bool v18hotChoco = SauceZone.HotAt(new Vector3(-40, .05f, 4));
                    SauceZone.Mark(new Vector3(-14, 0, -4.5f), 3f, false); bool v18slick = SauceZone.SlickAt(new Vector3(-14, .05f, -4.5f));
                    SteamCloud.Suppress(2f); bool v18cooled = Skills.SightClear(new Vector3(8, 2.2f, -40.5f), new Vector3(8, 2.2f, -30.5f), null);
                    bool v18repaired = false;
                    if (m.arena.breakWalls.Count > 0)
                    {
                        var v18wall = m.arena.breakWalls[0]; var v18p0 = v18wall.pieces[0]; var v18p1 = v18wall.pieces[1];
                        v18p1.Break(null); v18wall.RepairNear(v18p0); v18repaired = v18p1.Alive; v18wall.ResetRound();
                    }
                    Check("M16", "食材变身：面粉+鸡蛋=蛋糕（+护甲）、乱配=黑暗料理、奶茶无限弹药、菜谱表；热巧克力 / 更滑黄油 / 火锅降温；奶油补蛋糕墙",
                        v18cake && v18dark && v18milk && v18recipes && v18hotChoco && v18slick && v18cooled && v18repaired,
                        "蛋糕=" + v18cake + " 黑暗料理=" + v18dark + " 奶茶=" + v18milk + " 菜谱=" + v18recipes + "，热巧克力=" + v18hotChoco + " 更滑=" + v18slick + " 降温=" + v18cooled + "，补墙=" + v18repaired);

                    // ---------------- V18 C：奶茶机 / 烤箱 / 冰柜 / 外卖 / 大招 / 转盘
                    KitchenStation Find(KitchenStation.Kind k) { foreach (var it in Interactable.All) if (it is KitchenStation ks && ks.kind == k) return ks; return null; }
                    var v18cook2 = Other(att, 2); v18cook2.ammo = 1; v18cook2.grenades = 0;
                    var v18mt2 = Find(KitchenStation.Kind.MilkTea); bool v18refill = false;
                    if (v18mt2 != null) { for (float w = 0; w < 1.2f && !v18refill; w += Time.deltaTime) { v18refill = v18mt2.Hold(v18cook2, Time.deltaTime); yield return null; } v18refill = v18refill && v18cook2.ammo == v18cook2.W.magazine && v18cook2.grenades == 1; }
                    var v18ov = Find(KitchenStation.Kind.Oven); bool v18crispy = false;
                    if (v18ov != null) { for (float w = 0; w < 5.6f && !v18crispy; w += Time.deltaTime) { v18crispy = v18ov.Hold(v18cook2, Time.deltaTime); yield return null; } v18crispy = v18cook2.Food.crispy; }
                    var v18fz = Find(KitchenStation.Kind.Freezer); bool v18iceArmor = false; v18cook2.armor = 50;
                    if (v18fz != null) { for (float w = 0; w < 2f && !v18iceArmor; w += Time.deltaTime) { v18iceArmor = v18fz.Hold(v18cook2, Time.deltaTime); yield return null; } v18iceArmor = v18cook2.armor > Config.MaxArmor; }
                    v18cook2.armor = 50; v18cook2.Food.crispy = false;
                    var v18crate = DeliveryCrate.Drop(new Vector3(-36, 0, -2));
                    for (float w = 0; w < 6f && v18crate != null && !v18crate.Landed; w += Time.deltaTime) yield return null;
                    var v18opener = Other(att, 0); v18opener.Food.EndForm(false);
                    if (v18crate != null) Place(v18opener, v18crate.transform.position, 0);
                    for (float w = 0; w < .5f; w += Time.deltaTime) yield return null;
                    bool v18opened = v18crate == null && v18opener.Food.Transformed; v18opener.Food.EndForm(false); v18opener.Food.crispy = false;
                    Fighter v18raven = null; foreach (var f in m.fighters) if (f.role == Role.Raven) v18raven = f;
                    bool v18ultOk = false;
                    if (v18raven != null) { v18raven.Food.ult = 1f; v18ultOk = v18raven.Food.TryUlt(); foreach (var e in m.fighters) if (e.team != v18raven.team && e.alive && !e.Revealed) v18ultOk = false; }
                    var v18rider = Other(def, 2); Place(v18rider, new Vector3(8 + 1.5f, .3f, -2.5f), 0);
                    for (float w = 0; w < .3f; w += Time.deltaTime) yield return null;
                    Vector3 v18r0 = v18rider.transform.position;
                    for (float w = 0; w < 1.2f; w += Time.deltaTime) yield return null;
                    bool v18spun = Vector3.Distance(v18rider.transform.position, v18r0) > .3f;
                    Check("M17", "厨房设施：奶茶机补弹药和手雷、烤箱金黄酥脆、冰柜冰护甲；外卖箱空投落地后能开出变身；夜鸦大招全图显示敌人；传菜转盘会带着人转",
                        v18refill && v18crispy && v18iceArmor && v18opened && (v18raven == null || v18ultOk) && v18spun,
                        "奶茶机=" + v18refill + " 烤箱=" + v18crispy + " 冰柜=" + v18iceArmor + "，外卖=" + v18opened + "，大招=" + (v18raven == null ? "（本局没有夜鸦）" : v18ultOk.ToString()) + "，转盘=" + v18spun);
                    foreach (var e in m.fighters) { e.revealedUntil = 0; e.Food.EndForm(false); e.Food.ingredients.Clear(); }
                    // ---------------- V19：交互标记 / 果冻抖动 / 萌化装饰
                    yield return null;
                    int jiggles = FindObjectsOfType<FoodJiggle>().Length, frost = 0, clouds = FindObjectsOfType<CloudDrift>().Length;
                    foreach (Transform t in m.arena.transform) if (t.name == "奶油滴边") frost++;
                    bool snackMarked = InteractMarkers.Count >= 7;
                    Check("M18", "能交互的东西有闪光圈和 G 气泡（装饰美食没有）；美食道具被打会抖；楼顶奶油滴边、棉花糖云", snackMarked && jiggles > 10 && frost > 50 && clouds >= 10,
                        "交互标记 " + InteractMarkers.Count + " 个，会抖的美食 " + jiggles + " 个，奶油滴边 " + frost + " 段，云 " + clouds + " 朵");
                }
                {
                    // ---------------- V20：枪械模型（部位 / 护甲穿透 / 固定弹道 / 关键时间）
                    var v20tgt = Other(def, 0); v20tgt.armor = Config.MaxArmor; Place(v20tgt, new Vector3(-30, .05f, 2), 0); yield return null;
                    var v20ar = Config.Hero(Role.Rivet).weapon; var v20dmr = Config.Hero(Role.Raven).weapon;
                    v20tgt.armorClass = 0; v20tgt.helmetClass = 0;
                    Ballistics.Split(v20ar, HitPart.Head, v20tgt, 1f, out float v20hHead, out _, out _); Ballistics.Split(v20ar, HitPart.Chest, v20tgt, 1f, out float v20hChest, out _, out _); Ballistics.Split(v20ar, HitPart.Leg, v20tgt, 1f, out float v20hLeg, out _, out _);
                    bool v20parts = v20hHead > v20hChest && v20hChest > v20hLeg;
                    v20tgt.armorClass = 3; Ballistics.Split(v20dmr, HitPart.Chest, v20tgt, 1f, out float v20penHp, out float v20penArm, out _);   // 穿透 5 ≥ 3 级
                    var v20smg = Config.Hero(Role.Falcon).weapon; v20tgt.armorClass = 5; Ballistics.Split(v20smg, HitPart.Chest, v20tgt, 1f, out float v20blockHp, out float v20blockArm, out _);   // 穿透 3 < 5 级
                    bool v20armor = v20penHp / (v20penHp + v20penArm) > v20blockHp / (v20blockHp + v20blockArm) + .3f;
                    Vector3 v20headPt = v20tgt.transform.position + Vector3.up * 1.8f, v20chestPt = v20tgt.transform.position + Vector3.up * 1.3f, v20legPt = v20tgt.transform.position + Vector3.up * .4f, v20armPt = v20tgt.transform.TransformPoint(new Vector3(.35f, 1.2f, 0));
                    bool v20zones = Ballistics.PartOf(v20tgt, v20headPt) == HitPart.Head && Ballistics.PartOf(v20tgt, v20chestPt) == HitPart.Chest && Ballistics.PartOf(v20tgt, v20legPt) == HitPart.Leg && Ballistics.PartOf(v20tgt, v20armPt) == HitPart.Arm;
                    bool v20sameBox = true; foreach (var f in m.fighters) if (f.alive && !f.Prone && !f.crouching && (Mathf.Abs(f.cc.height - 1.92f) > .01f || Mathf.Abs(f.cc.radius - .4f) > .01f)) v20sameBox = false;
                    bool v20pattern = Ballistics.PatternStep(v20ar, 5) == Ballistics.PatternStep(v20ar, 5) && Ballistics.PatternStep(v20ar, 5) != Ballistics.PatternStep(v20ar, 9);
                    var v20gunner = Other(att, 1); v20gunner.Equip(v20ar); bool v20drawBlocks = !v20gunner.CanFireNow;
                    v20gunner.ammo = 5; v20gunner.Reload(); float v20tac = v20gunner.ReloadProgress; yield return new WaitForSeconds(.5f); float v20tacK = v20gunner.ReloadProgress;
                    v20gunner.Equip(v20ar); v20gunner.ammo = 0; v20gunner.Reload(); yield return new WaitForSeconds(.5f); float v20emptyK = v20gunner.ReloadProgress;
                    bool v20reloads = v20tacK > v20emptyK + .02f;   // 同样 0.5 秒，战术换弹进度更快
                    v20gunner.Equip(v20ar); v20tgt.armorClass = Config.Hero(v20tgt.role).armorClass; v20tgt.helmetClass = Config.Hero(v20tgt.role).helmetClass;
                    Check("M19", "枪械模型：部位倍率（头>胸>腿）、统一判定区（头/胸/臂/腿）、所有角色判定框一致、穿透≥护甲时伤害主要进生命、固定弹道可复现、拔枪期间不能开火、战术换弹比空仓换弹快",
                        v20parts && v20zones && v20sameBox && v20armor && v20pattern && v20drawBlocks && v20reloads,
                        "部位=" + v20parts + "（头 " + v20hHead.ToString("0") + " / 胸 " + v20hChest.ToString("0") + " / 腿 " + v20hLeg.ToString("0") + "）判定区=" + v20zones + " 判定框一致=" + v20sameBox + "，穿透 5v3 生命占比 " + (v20penHp / (v20penHp + v20penArm)).ToString("0.00") + " vs 穿透 3v5 " + (v20blockHp / (v20blockHp + v20blockArm)).ToString("0.00") + "，弹道=" + v20pattern + " 拔枪=" + v20drawBlocks + " 换弹 战术 " + v20tacK.ToString("0.00") + " / 空仓 " + v20emptyK.ToString("0.00"));

                    // ---------------- V20：3C（趴下 / 体力 / 开镜时间 / 负重 / 数据表）
                    var v20mover = Other(def, 2); var v20mb = v20mover.GetComponent<Bot>(); if (v20mb != null) v20mb.enabled = false; Place(v20mover, new Vector3(-40, .05f, 4), 90); yield return null;
                    v20mover.SetProne(true); bool v20proneOk = v20mover.Prone && v20mover.cc.height < 1f && Ballistics.PartOf(v20mover, v20mover.transform.position + Vector3.up * .65f) == HitPart.Head;
                    yield return new WaitForSeconds(Tune.ProneTransition + .1f);
                    // ---------------- V21：趴下动作（Prone / Prone_Crawl），外观最高点 ≤ 0.8 米，和 0.70 米判定框对得上
                    float v21still = 99f, v21crawl = 0f; string v21clipStill = "", v21clipCrawl = "";
                    if (v20mover.avatar != null)
                    {
                        yield return new WaitForSeconds(.7f); v21still = v20mover.avatar.VisualTop(); v21clipStill = v20mover.avatar.ClipName;
                        for (float w = 0; w < 1.2f; w += Time.deltaTime) { v20mover.botMove = v20mover.transform.forward; if (w > .4f) { v21crawl = Mathf.Max(v21crawl, v20mover.avatar.VisualTop()); v21clipCrawl = v20mover.avatar.ClipName; } yield return null; }
                        v20mover.botMove = Vector3.zero;
                    }
                    bool v21clips = v21clipStill.EndsWith("Prone") && v21clipCrawl.EndsWith("Prone_Crawl");
                    Check("M21", "趴下动作：播放 Prone / Prone_Crawl（不是前倾 72°），趴着和匍匐时角色外观（含帽子、枪）最高点 ≤ 0.8 米",
                        v20mover.Prone && v21clips && v21still <= .8f && v21crawl <= .8f,
                        "动作 " + (v21clipStill == "" ? "无" : v21clipStill) + " / " + (v21clipCrawl == "" ? "无" : v21clipCrawl) + "，最高点 趴着 " + v21still.ToString("0.000") + " m / 匍匐 " + v21crawl.ToString("0.000") + " m（判定框 " + v20mover.cc.height.ToString("0.00") + " m）");
                    v20mover.SetProne(false); yield return null;
                    v20mover.stamina = Tune.StaminaMax; v20mover.exhausted = false;
                    for (float w = 0; w < 1.5f; w += Time.deltaTime) { v20mover.botMove = Vector3.right; v20mover.botSprint = true; yield return null; }
                    float v20afterSprint = v20mover.stamina; v20mover.botMove = Vector3.zero; v20mover.botSprint = false;
                    bool v20staminaOk = v20afterSprint < Tune.StaminaMax - 10f;
                    v20mover.Equip(Config.Hero(Role.Ironwall).weapon); float v20lmgAds = Ballistics.AdsTime(v20mover); v20mover.Equip(Config.Pistol); float v20pistolAds = Ballistics.AdsTime(v20mover);
                    bool v20weightOk = v20lmgAds > v20pistolAds * 2f && Ballistics.WeightSpeedMul(v20mover) >= Ballistics.WeightSpeedMul(v20mover);
                    v20mover.botAim = true; yield return new WaitForSeconds(.08f); float v20k1 = v20mover.adsK; yield return new WaitForSeconds(v20pistolAds + .1f); float v20k2 = v20mover.adsK; v20mover.botAim = false;
                    bool v20adsOk = v20k1 < .95f && v20k2 > .95f;
                    bool v20tables = DataTables.LastStatus.StartsWith("已读取") && System.IO.File.Exists(System.IO.Path.Combine(DataTables.Dir, "weapons.csv"));
                    v20mover.weapon = null; v20mover.BuildVisuals(); v20mover.stamina = Tune.StaminaMax; if (v20mb != null) v20mb.enabled = false;
                    Check("M20", "3C：趴下（判定框变矮、头部判定跟着变）、冲刺消耗体力、负重越重开镜越慢、开镜需要时间、数值从 CSV 表格读取",
                        v20proneOk && v20staminaOk && v20weightOk && v20adsOk && v20tables,
                        "趴下=" + v20proneOk + "，冲刺 1.5 秒后体力 " + v20afterSprint.ToString("0") + "，开镜 LMG " + v20lmgAds.ToString("0.00") + "s / 手枪 " + v20pistolAds.ToString("0.00") + "s，开镜进度 0.08s=" + v20k1.ToString("0.00") + " → " + v20k2.ToString("0.00") + "，数据表=" + v20tables);
                }
                if (ToonAssets.Available) yield return V21Tests();   // V21：美食技能 + 可发现性（M22–M28）
                if (ToonAssets.Available) yield return PropTests();  // V21：场景道具接入（M29）
                yield return V22Tests();                             // V22 第一期：体力与 UI（M30–M35）
                yield return V22MapTests();                          // V22 第二期：地图方案甲（M40–M45）
                yield return V22LinkTests();                         // V22 第三期：干员 × 地图联动 + 团队标记 + 大招预警（M46–M54）
                if (ToonAssets.Available) yield return V23CombatTests();   // V23 a + c：视线 / 爆炸拆分、曼妥思喷泉、技能 / 大招美食表现（M55–M60）
                yield return MapUiTests.Run(Check);                  // V23 b：地图 UI + 食材高亮（M70–M76，MapUiTests.cs）
                yield return LinkTests.Run(Check);                   // V23 d：联动放大 + 副联动 + 硬约束（M85 起，LinkTests.cs）
                if (ToonAssets.Available) yield return V23PolishTests();   // V23 打磨：大招远景 / 可乐喷泉 / 食材光柱 / 左手道具 / 大地图（M77）
                m.roundClock = Config.RoundTime; m.phase = Phase.Live;   // V17 的新测试较长，重置回合时钟，免得后面的测试撞上超时
            }

            // --- V11: grenade + melee (ideas from AssaultCube)
            {
                var thrower = Other(att, 1); var foe = Other(def, 0); var mate = Other(att, 2);
                Vector3 c0 = new Vector3(-40, .05f, -4);
                Place(thrower, c0 + new Vector3(-9, 0, 0), 90); Place(foe, c0 + new Vector3(2.5f, 0, 0), -90); Place(mate, c0 + new Vector3(-2.5f, 0, 0), 90);
                foe.hp = 100; foe.armor = 50; mate.hp = 100; mate.armor = 50; thrower.grenades = 1;
                yield return null;
                var gr = Grenade.Throw(thrower, Vector3.zero); gr.transform.position = c0 + Vector3.up * .3f;
                for (float w = 0; w < Grenade.Fuse + .4f; w += Time.deltaTime) yield return null;
                float foeLost = 150 - (foe.hp + foe.armor), mateLost = 150 - (mate.hp + mate.armor);
                Check("M06", "手雷：2.5 秒引爆，范围内敌人受伤，队友不受伤", foeLost > 20 && foeLost <= Grenade.MaxDamage + .1f && mateLost == 0, "敌人掉血 " + foeLost.ToString("0") + "，队友掉血 " + mateLost.ToString("0") + (foe.alive ? "" : "（敌人测试前已阵亡）"));

                var knifer = Other(att, 0); var victim2 = Other(def, 1);
                Place(victim2, c0 + new Vector3(4, 0, 6), 90); Place(knifer, c0 + new Vector3(2.8f, 0, 6), 90);   // behind, both facing +x
                victim2.hp = 100; victim2.armor = 50; yield return null;
                bool hitBack = knifer.Melee(); float backDmg = 150 - (victim2.hp + victim2.armor);
                for (float w = 0; w < .8f; w += Time.deltaTime) yield return null;
                victim2.hp = 100; victim2.armor = 50; victim2.alive = true;
                Place(victim2, c0 + new Vector3(4, 0, 6), -90); yield return null;   // now facing the attacker
                bool hitFront = knifer.Melee(); float frontDmg = 150 - (victim2.hp + victim2.armor);
                Check("M07", "近战：正面 55 伤害，背刺 120 伤害", hitBack && hitFront && Mathf.Approximately(backDmg, 120) && Mathf.Approximately(frontDmg, 55), "背刺 " + backDmg + "，正面 " + frontDmg);
            }

            // --- V13: lean, buy menu, breakable walls
            {
                var leaner = Other(att, 1); Place(leaner, new Vector3(-40, .05f, -4), 90); leaner.botLean = 1;
                for (float w = 0; w < .5f; w += Time.deltaTime) yield return null;
                Vector3 shift = leaner.Eye - (leaner.transform.position + Vector3.up * 1.62f);
                float side = Vector3.Dot(shift, leaner.transform.right);
                leaner.botLean = 0;
                Check("M08", "探头：眼睛和枪口向侧面探出 0.3 m 以上", side > .3f, "侧移 " + side.ToString("0.00") + " m");

                var buyer = Other(def, 2); var oldPhase = m.phase; m.phase = Phase.Prep;
                var target = Config.Hero(Role.Raven).weapon;                         // DMR-3, 3600 (2520 for Raven)
                var ar = Config.Hero(Role.Ironwall).weapon;                          // LMG-2
                if (buyer.W == ar) ar = Config.Hero(Role.Rivet).weapon;              // 买家刚好拿着 LMG-2 时换成 AR-9
                int priceAr = Config.Price(buyer.Hero, ar); buyer.credits = priceAr + 100; int before = buyer.credits;
                bool boughtAr = buyer.Buy(ar);
                bool equipped = buyer.W == ar && buyer.ammo == ar.magazine; int afterAr = buyer.credits;
                // 换枪会退回上一把的钱，所以只有"退款后仍买不起"时才验证拒绝购买（与英雄随机分配无关）
                bool tooExpensive = Config.Price(buyer.Hero, target) > before ? !buyer.Buy(target) : true;
                buyer.credits = afterAr;
                m.phase = oldPhase; buyer.weapon = null; buyer.BuildVisuals();
                Check("M09", "开局选枪：任何英雄都能买任何武器，扣钱正确，钱不够买不了", boughtAr && equipped && tooExpensive && buyer.credits == before - priceAr,
                    buyer.Hero.name + " 买 " + ar.id + " 花 $" + priceAr + "，剩 $" + buyer.credits + "，装备=" + equipped);

                var wall = m.arena.breakWalls.Count > 0 ? m.arena.breakWalls[0] : null;
                bool wallOk = false; string wallInfo = "没有可破坏墙";
                if (wall != null)
                {
                    int total = wall.pieces.Count;
                    // shoot one piece until it breaks
                    var piece = wall.pieces[0]; int hitsNeeded = 0;
                    while (piece.Alive && hitsNeeded < 20) { piece.TakeDamage(28, Other(att, 0), false); hitsNeeded++; }
                    yield return null;
                    var gr = Grenade.Throw(Other(att, 0), Vector3.zero); gr.transform.position = wall.transform.position + Vector3.up * 1f + wall.transform.forward * .4f;
                    for (float w = 0; w < Grenade.Fuse + .3f; w += Time.deltaTime) yield return null;
                    int brokenN = wall.Broken;
                    wallOk = !piece.Alive && hitsNeeded >= 2 && brokenN > 4 && brokenN < total * .6f;
                    wallInfo = "单块被 " + hitsNeeded + " 发子弹打穿；手雷炸开 " + brokenN + "/" + total + " 块";
                    wall.ResetRound();
                }
                Check("M10", "可破坏墙：子弹逐块打穿，手雷炸出一个洞（不是整面墙）", wallOk, wallInfo);
            }

            Destructible holeCrate = null; foreach (var d in m.arena.destructibles) if (Vector3.Distance(d.transform.position, new Vector3(4, 0, 47)) < 1f) holeCrate = d;
            if (holeCrate != null) holeCrate.TakeDamage(9999, Other(att, 1), true);
            yield return new WaitForSeconds(.3f);
            bool holeOpen = holeCrate != null && !holeCrate.GetComponent<Collider>().enabled;
            Check("M03", "检查站墙洞：打碎堵洞木箱后可以钻过去", holeOpen, "木箱存在=" + (holeCrate != null) + "，碰撞已移除=" + holeOpen);
            if (holeCrate != null) holeCrate.Restore();

            // --- V4 skill impact / scope
            var rc = Other(att, 1); rc.skillCharges = 0; rc.skillRechargeAt = Time.time + .3f;
            yield return new WaitForSeconds(.5f);
            Check("S01", "技能可充能：冷却结束后自动恢复次数", rc.skillCharges == 1, rc.Hero.skill.name + " 最多 " + rc.Hero.skill.maxCharges + " 次、冷却 " + rc.Hero.skill.cooldown + " s；0.5 s 后次数=" + rc.skillCharges);

            var sniper = m.player;
            if (sniper != null)
            {
                sniper.ChangeRole(Role.Raven); Place(sniper, new Vector3(-40, .05f, 3), 90);
                yield return new WaitForSeconds(.2f);
                sniper.adsToggle = true;
                yield return new WaitForSeconds(.6f);
                bool scoped = sniper.Scoped && Mathf.Abs(sniper.cam.fieldOfView - 78f / sniper.W.adsZoom) < 3f;
                sniper.adsToggle = false;
                yield return new WaitForSeconds(.3f);
                Check("S02", "倍镜改为切换式：开镜进入 4 倍瞄准镜视图（隐藏枪模），再次切换退出", scoped && !sniper.Scoped, "开镜后视场角 " + (78f / sniper.W.adsZoom).ToString("0.0") + "°、倍率 " + sniper.W.adsZoom + "x，退出后=" + !sniper.Scoped);
            }

            var burner = Get(att, Role.Ember); var walker = Other(def, 1);
            Place(walker, new Vector3(-40, .05f, -40), 0); walker.hp = 100; walker.armor = 50;
            yield return new WaitForFixedUpdate();
            var fz = new GameObject("test fire"); fz.transform.position = new Vector3(-40, 0, -40); fz.AddComponent<FireZone>().owner = burner;
            yield return new WaitForSeconds(1.1f);
            float burnt = 150 - walker.hp - walker.armor;
            Destroy(fz);
            Check("S03", "焚芯燃烧区：持续伤害区域内敌人", burnt > 8f, "站在燃烧区 1 s 受到 " + burnt.ToString("0.0") + " 伤害（" + Config.FireDps + "/s）");

            walker.hp = 100; walker.armor = 50; walker.jammedUntil = Time.time + 2f;
            float spreadJam = walker.CurrentSpread(); walker.jammedUntil = 0; float spreadNorm = walker.CurrentSpread();
            Check("S04", "幻频噪幕：区域内敌人散布变大", spreadJam > spreadNorm * 1.5f, "散布 " + spreadNorm.ToString("0.00") + "° → " + spreadJam.ToString("0.00") + "°，移速 ×" + Config.NoiseSlow);

            // --- bomb rules
            ReviveAll(); ForceLive(); yield return null;
            att = m.AttackingTeam; def = m.DefendingTeam;
            var carrier = Other(att, 0); m.BombCarrier = carrier; m.bomb = BombState.Carried;
            Place(carrier, new Vector3(30, .05f, 21), 0); yield return new WaitForFixedUpdate(); yield return null;
            yield return Hold(carrier, 4.5f);
            bool notOutside = m.bomb == BombState.Carried;
            Place(carrier, new Vector3(38.5f, .05f, 34), 0); yield return new WaitForFixedUpdate(); yield return null;
            yield return Hold(carrier, Config.PlantTime + .3f);
            bool planted = m.Planted && m.plantedSite == 0;
            Check("B01", "炸弹只能在包点内安装（4 秒）", notOutside && planted, "包点外按住 4.5 s 未安装=" + notOutside + "，A 点内安装=" + planted);

            var defuser = Other(def, 0);
            Place(defuser, m.bombPos + new Vector3(.8f, .05f, 0), 0); yield return new WaitForFixedUpdate(); yield return null;
            yield return Hold(defuser, 2f);
            bool interruptedReset = m.defuseProgress == 0;
            yield return Hold(defuser, Config.DefuseTime + .3f);
            bool defusedWin = m.phase == Phase.RoundEnd && m.lastRoundWinner == def && m.lastRoundReason.Contains("拆除");
            Check("B02", "拆除：中断后进度清零；在引爆前拆除 → 守方获胜", interruptedReset && defusedWin, "中断清零=" + interruptedReset + "，结果=" + m.lastRoundResult + "（" + m.lastRoundReason + "）");

            // next round: defenders wiped must NOT end the round; explosion wins
            m.phaseTimer = 0; yield return null; yield return null;
            int round2Att = m.AttackingTeam;
            bool swapped = round2Att != att;
            ForceLive(); yield return null;
            att = m.AttackingTeam; def = m.DefendingTeam;
            foreach (var f in m.fighters) if (f.team == def) f.ReceiveDamage(999, null, Vector3.zero, false);
            yield return null; yield return null;
            bool stillLive = m.phase == Phase.Live;
            carrier = m.BombCarrier ?? Other(att, 0); m.BombCarrier = carrier;
            Place(carrier, new Vector3(38.5f, .05f, -34), 0); yield return new WaitForFixedUpdate(); yield return null;
            yield return Hold(carrier, Config.PlantTime + .3f);
            bool plantedB = m.Planted && m.plantedSite == 1;
            Place(carrier, new Vector3(-20, .05f, 7.5f), 0);   // V22：原 (-20,0) 在高摊位岛里
            m.bombTimer = .3f;
            yield return new WaitForSeconds(.6f);
            bool exploded = m.phase == Phase.RoundEnd && m.lastRoundWinner == att && m.lastRoundReason.Contains("引爆");
            Check("B03", "守方全灭不会结束回合，必须安装并引爆炸弹", stillLive && plantedB && exploded, "守方全灭后仍在进行=" + stillLive + "，B 点安装=" + plantedB + "，引爆结果=" + m.lastRoundResult);
            Check("B04", "每回合结束攻守互换", swapped, "第 1 回合进攻=" + Config.TeamName(1 - round2Att) + " → 第 2 回合进攻=" + Config.TeamName(round2Att));

            // attackers wiped before plant → defenders
            m.phaseTimer = 0; yield return null; yield return null;
            ForceLive(); yield return null;
            att = m.AttackingTeam; def = m.DefendingTeam;
            foreach (var f in m.fighters) if (f.team == att) f.ReceiveDamage(999, null, Vector3.zero, false);
            yield return null; yield return null;
            bool attWipe = m.phase == Phase.RoundEnd && m.lastRoundWinner == def;
            Check("B05", "炸弹未安装时攻方全灭 → 守方获胜", attWipe, m.lastRoundResult + "（" + m.lastRoundReason + "）");

            m.phaseTimer = 0; yield return null; yield return null;
            ForceLive(); yield return null;
            def = m.DefendingTeam;
            m.roundClock = .2f;
            yield return new WaitForSeconds(.4f);
            bool timeout = m.phase == Phase.RoundEnd && m.lastRoundWinner == def && m.lastRoundReason.Contains("时间");
            Check("B06", "时间耗尽且未安装 → 守方获胜", timeout, m.lastRoundResult + "（" + m.lastRoundReason + "）");

            // dropped bomb pickup
            m.phaseTimer = 0; yield return null; yield return null;
            ForceLive(); yield return null;
            att = m.AttackingTeam;
            var c1 = m.BombCarrier; Fighter c2 = null; foreach (var f in m.fighters) if (f.team == att && f != c1) c2 = f;
            Place(c1, new Vector3(-30, .05f, 0), 0); Place(c2, new Vector3(-20, .05f, 7.5f), 0);   // V22：原 (-20,0) 在高摊位岛里
            yield return new WaitForFixedUpdate();
            c1.ReceiveDamage(999, null, Vector3.zero, false);
            yield return null;
            bool dropped = m.bomb == BombState.Dropped;
            Place(c2, m.bombPos + Vector3.up * .05f, 0);
            yield return null; yield return null;
            Check("B07", "携带者阵亡炸弹掉落，攻方队友可拾取", dropped && m.BombCarrier == c2 && m.bomb == BombState.Carried, "掉落=" + dropped + "，拾取者=" + (m.BombCarrier != null ? m.BombCarrier.Hero.name : "无"));

            report.AppendLine();
            report.AppendLine("合计：" + passed + " 项通过，" + failed + " 项未通过。");
            string dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Playtest");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "test_report.md"), report.ToString(), new UTF8Encoding(true));
            Debug.Log("NEON_TESTS_DONE passed=" + passed + " failed=" + failed);
            Application.Quit(failed == 0 ? 0 : 1);
        }

        // ================================================================ V21：美食技能 + 可发现性（设计稿_V21_美食技能与可发现性 §4，编号顺延为 M22–M28）
        IEnumerator V21Tests()
        {
            var m = Match.I; m.phase = Phase.Live; m.roundClock = Config.RoundTime;
            // ---------------- M22：技能只换皮（数值快照）+ 文案无军事词 + 不被 FoodText 误替换 + 物体名
            bool numsOk = Config.DashDistance == 9f && Config.DashWindup == .15f && Config.DashTime == .2f && Config.DashAimRecovery == .2f && Config.DashGuardTime == 1.5f && Config.DashDamageReduction == .35f
                && Config.CoverHp == 450f && Config.CoverDeploy == .6f && Config.CoverLife == 18f && Config.CoverWidth == 3.6f && Config.CoverHeight == 1.7f
                && Config.ShieldLife == 8f && Config.ShieldHp == 650f && Config.ShieldMoveMultiplier == .7f && Config.ShieldWidth == 4.4f && Config.ShieldHeight == 2.8f
                && Config.ArrowSpeed == 48f && Config.ArrowGravity == .35f && Config.DroneRadius == 14f && Config.DroneScanDelay == 1f && Config.DroneLife == 7f && Config.DroneRevealTime == 5f && Config.DroneSecondPulse == 3f
                && Config.BlastWarning == .9f && Config.BlastMaxDamage == 65f && Config.BlastRadius == 5f && Config.BlastCoverDamage == 120f && Config.BlastCoverMultiplier == 2f
                && Config.FireRadius == 3.5f && Config.FireDuration == 5f && Config.FireDps == 14f && Config.NoiseRadius == 9f && Config.NoiseLife == 8f && Config.NoiseSlow == .8f && Config.NoiseSpread == 1.6f;
            float[] cds = { 12, 20, 22, 22, 24, 24 }; int[] chs = { 2, 2, 1, 2, 2, 1 }; string cdInfo = "";
            for (int i = 0; i < 6; i++) { var sk = Config.Heroes[i].skill; if (sk.cooldown != cds[i] || sk.maxCharges != chs[i]) numsOk = false; cdInfo += sk.cooldown + "/" + sk.maxCharges + " "; }
            string[] black = { "战术", "阵列", "侦测", "爆破", "信号", "噪幕", "推进", "棱核", "信标" };
            string[] names = { "汽水冲刺", "威化饼干墙", "焦糖布丁盾", "芥末探签", "点火纸杯蛋糕", "棉花糖迷雾" };
            int blackHits = 0; bool namesOk = true; string hitInfo = "";
            for (int i = 0; i < 6; i++)
            {
                var sk = Config.Heroes[i].skill;
                if (sk.Name != names[i] || ToonAssets.FoodText(sk.Name) != sk.Name) namesOk = false;
                foreach (var t in new[] { sk.Name, sk.Purpose, sk.Rule, sk.Counter, sk.Numbers, sk.HowTo })
                    foreach (var b in black) if (ToonAssets.FoodText(t).Contains(b)) { blackHits++; hitInfo += b + " "; }
            }
            bool coverText = Config.Hero(Role.Rivet).skill.Numbers.Contains("585") && Mathf.Approximately(Config.CoverHp * 1.3f, 585f);
            var v21iron = Get(1, Role.Ironwall); var v21ph = Get(0, Role.Phantom); var v21rv = Other(1, 2);
            foreach (var f in new[] { v21iron, v21ph }) { var b = f.GetComponent<Bot>(); if (b != null) b.enabled = false; f.skillCharges = 2; if (!f.alive) f.Respawn(m.arena.SpawnPoint(true, 0), 0); }
            Place(v21iron, new Vector3(-40, .05f, -8), 90); Place(v21ph, new Vector3(-40, .05f, 8), 90); yield return null;
            int before = Skills.spawned.Count;
            Skills.Cast(v21iron, Vector3.zero); Skills.Cast(v21ph, new Vector3(-30, 0, 8));
            Skills.ShootArrow(v21rv, Vector3.down * 5f);
            yield return null;
            string objNames = "";
            for (int i = before; i < Skills.spawned.Count; i++) if (Skills.spawned[i] != null) { objNames += Skills.spawned[i].name + " "; }
            foreach (var b in black) if (objNames.Contains(b)) { blackHits++; hitInfo += "物体:" + b + " "; }
            bool objOk = objNames.Contains("焦糖布丁盾") && objNames.Contains("棉花糖迷雾") && objNames.Contains("芥末探签");
            for (int i = Skills.spawned.Count - 1; i >= before; i--) { if (Skills.spawned[i] != null) Destroy(Skills.spawned[i]); Skills.spawned.RemoveAt(i); }
            v21iron.shieldActiveUntil = 0; yield return null;
            Check("M22", "技能美食化只换皮：技能常量 / 冷却 / 充能与改前一致；6 个技能文案和物体名不含军事词；新名字不被 FoodText 误替换；铆钉耐久写 585",
                numsOk && blackHits == 0 && namesOk && coverText && objOk,
                "数值快照=" + numsOk + "（冷却/充能 " + cdInfo.Trim() + "），军事词命中 " + blackHits + (hitInfo != "" ? "（" + hitInfo.Trim() + "）" : "") + "，改名=" + namesOk + "，585=" + coverText + "，物体：" + objNames.Trim());

            // ---------------- M23：气泡写配方、跟随状态、≤ 18 字
            var p = m.player;
            if (!p.alive) p.Respawn(new Vector3(-44, .05f, 3.5f), 90);
            Place(p, new Vector3(-44, .05f, 3.5f), 92); p.hp = 100; p.Food.ingredients.Clear(); p.Food.EndForm(false);
            bool allPartner = true, allShort = true; string longest = ""; int maxLen = 0;
            System.Action<string> len = s => { if (s.Length > maxLen) { maxLen = s.Length; longest = s; } if (s.Length > FoodGuide.BubbleMax) allShort = false; };
            var saveMode = Match.Mode;
            foreach (var id in Ingredients.All)
            {
                string l0 = FoodGuide.IngredientLabel(id, p, 3f, out _); len(l0);
                bool hasPartner = false; foreach (var q in FoodGuide.Partners(id)) if (l0.Contains(Ingredients.Name(q))) hasPartner = true;
                if (!hasPartner) allPartner = false;
                foreach (var held in Ingredients.All) { p.Food.ingredients.Clear(); p.Food.ingredients.Add(held); len(FoodGuide.IngredientLabel(id, p, 3f, out _)); }
                p.Food.ingredients.Clear();
                Match.Mode = GameMode.Cook; len(FoodGuide.IngredientLabel(id, p, 3f, out _)); Match.Mode = saveMode;
            }
            p.Food.ingredients.Add("Flour");
            string eggL = FoodGuide.IngredientLabel("Egg", p, 3f, out Color eggC), chiliL = FoodGuide.IngredientLabel("Chili", p, 3f, out Color chiliC), farL = FoodGuide.IngredientLabel("Egg", p, 30f, out _);
            bool heldOk = eggL.Contains("巨型蛋糕") && chiliL.Contains("黑暗料理") && eggC == FoodGuide.Ok && chiliC == FoodGuide.Grey && farL == "鸡蛋";
            // 真实气泡：生成一个鸡蛋，读 InteractMarkers 上的文字
            var v21egg = IngredientPickup.Spawn("Egg", p.transform.position + p.transform.forward * 4f, null, 30f);
            yield return null; yield return null;
            string worldL = InteractMarkers.I != null ? InteractMarkers.I.LabelOf(v21egg) : null;
            bool worldOk = worldL != null && worldL.Contains("巨型蛋糕");
            Destroy(v21egg.gameObject); p.Food.ingredients.Clear();
            Snack v21snack = null; KitchenStation v21oven = null;
            foreach (var it in Interactable.All) { if (v21snack == null && it is Snack sn && !sn.Eaten) v21snack = sn; if (v21oven == null && it is KitchenStation ks && ks.kind == KitchenStation.Kind.Oven) v21oven = ks; }
            bool snackOk = true, stationOk = true; string snackL = "", stationL = "";
            if (v21snack != null)
            {
                p.hp = 100; snackL = FoodGuide.SnackLabel(v21snack, p, out bool sOk1); len(snackL);
                p.hp = 50; string s2 = FoodGuide.SnackLabel(v21snack, p, out bool sOk2); len(s2); p.hp = 100;
                snackOk = !sOk1 && snackL.Contains("满血") && sOk2 && s2.Contains("+25");
            }
            if (v21oven != null)
            {
                p.Food.crispy = false; v21oven.SetCooldownForTest(p, 11.5f);   // V22：12f 时 (t+12)-t 浮点误差会算成 12.000008 → 向上取整 13，测试偶发失败
                stationL = FoodGuide.StationLabel(v21oven, p, out bool okC); len(stationL);
                stationOk = !okC && stationL.Contains("还要 12 秒");
                v21oven.SetCooldownForTest(p, -1f);
            }
            Check("M23", "食材气泡写配方（13 种都有配菜名；拿着面粉时鸡蛋绿色写巨型蛋糕、辣椒灰色写黑暗料理；远处只写名字）；满血零食变灰；冷却中的设施写剩余秒数；所有气泡 ≤ 18 字",
                allPartner && heldOk && worldOk && snackOk && stationOk && allShort,
                "配菜名=" + allPartner + "，拿面粉：鸡蛋「" + eggL + "」辣椒「" + chiliL + "」远处「" + farL + "」，场景气泡「" + worldL + "」，零食「" + snackL + "」，烤箱「" + stationL + "」，最长 " + maxLen + " 字「" + longest + "」");

            // ---------------- M24：规则甲 —— 第二样凑不成菜要按住 G 才捡；凑得成的照常自动捡；抢食模式不受影响
            Place(p, new Vector3(-44, .05f, 3.5f), 92); p.Food.EndForm(false); p.Food.ingredients.Clear(); p.Food.ingredients.Add("Flour");
            var v21chili = IngredientPickup.Spawn("Chili", p.transform.position, null, 30f);
            yield return new WaitForSeconds(1f);
            bool notAuto = v21chili != null && p.Food.ingredients.Count == 1 && p.Food.ingredients[0] == "Flour";
            yield return Hold(p, FoodGuide.HoldToPick + .3f);
            bool forced = v21chili == null && p.Food.ingredients.Count == 0 && p.Food.form == FoodForm.Dark;
            if (v21chili != null) Destroy(v21chili.gameObject);
            p.Food.EndForm(false); p.Food.ingredients.Clear(); p.Food.ingredients.Add("Flour");
            var v21egg2 = IngredientPickup.Spawn("Egg", p.transform.position, null, 30f);
            yield return new WaitForSeconds(1f);
            bool autoMatch = v21egg2 == null && p.Food.form == FoodForm.Cake;
            if (v21egg2 != null) Destroy(v21egg2.gameObject);
            p.Food.EndForm(false); p.Food.ingredients.Clear(); p.armor = 50;
            p.Food.ingredients.Add("Flour"); Match.Mode = GameMode.Cook; bool cookFree = !FoodGuide.NeedsHold(p, "Chili"); Match.Mode = saveMode; p.Food.ingredients.Clear();
            bool emptyFree = !FoodGuide.NeedsHold(p, "Chili");
            Check("M24", "拾取规则甲：手上有一样时凑不成菜的不自动捡、按住 G " + FoodGuide.HoldToPick + " 秒才捡；凑得成的照常自动捡；空手和抢食模式不受影响",
                notAuto && forced && autoMatch && cookFree && emptyFree,
                "路过不捡=" + notAuto + "，按住 G 硬捡=" + forced + "，能凑成自动捡=" + autoMatch + "，抢食不受影响=" + cookFree + "，空手不受影响=" + emptyFree);

            // ---------------- M25：HUD"差一样"列出全部配菜；6 个英雄的选英雄卡含被动和大招效果
            bool hintOk = true; string hintBad = "";
            foreach (var id in Ingredients.All)
            {
                var h = FoodGuide.HudHint(new List<string> { id }); var parts = FoodGuide.Partners(id);
                int need = id == "Milk" || id == "Tea" || id == "Boba" ? 2 : 1;
                bool ok = h.StartsWith("差一样：") && parts.Count == need; foreach (var q in parts) if (!h.Contains(Ingredients.Name(q))) ok = false;
                if (!ok) { hintOk = false; hintBad += id + " "; }
            }
            bool cardOk = true;
            foreach (var hero in Config.Heroes) { string c = GameUI.HeroCardText(hero); if (!c.Contains(FoodState.Passive(hero.role)) || !c.Contains(FoodGuide.UltEffect(hero.role)) || !c.Contains(FoodState.UltName(hero.role))) cardOk = false; }
            Check("M25", "HUD 状态栏提示\"差一样\"（奶茶类 2 个配菜，其余 1 个，13 种全对）；6 个英雄选人卡都有被动和大招效果",
                hintOk && cardOk, "差一样=" + hintOk + (hintBad != "" ? "（错：" + hintBad + "）" : "") + " 例「" + FoodGuide.HudHint(new List<string> { "Milk" }) + "」，选人卡=" + cardOk);

            // ---------------- M26：开局横幅按模式
            string bannerBad = ""; var bannerSeen = new List<string>();
            foreach (var mode in new[] { GameMode.FFA, GameMode.Cook, GameMode.Grow, GameMode.Bomb })
            {
                Match.Mode = mode;
                if (m.ui != null) m.ui.OnLive();
                string b = m.ui != null ? m.ui.BannerText : "";
                bannerSeen.Add(mode + ":" + b);
                if (mode != GameMode.Bomb && (b.Contains("进攻") || b.Contains("防守") || b.Contains("蛋糕") || b.Length < 4)) bannerBad += mode + " ";
                if (mode == GameMode.Bomb && !(b.Contains("进攻") || b.Contains("防守"))) bannerBad += mode + " ";
            }
            Match.Mode = saveMode;
            Check("M26", "开局横幅按模式：大乱斗 / 抢食 / 变大不出现\"进攻 / 防守 / 蛋糕\"，爆破照旧写攻守", bannerBad == "", string.Join(" | ", bannerSeen.ToArray()) + (bannerBad != "" ? "；错：" + bannerBad : ""));

            // ---------------- M27：首次提示卡（交火中延后、只出一次、可关、测试不写存档）
            bool tipsOn0 = GameSettings.Tips; FoodTips.AutoEnabled = false; GameSettings.Tips = true; FoodTips.ResetSession();
            int pref0 = PlayerPrefs.GetInt("neon_tip_ingredient", -1);
            p.lastShotAt = Time.time;   // 刚开过火
            float t0 = Time.time; FoodTips.Trigger("ingredient");
            yield return new WaitForSeconds(.8f);
            bool delayed = FoodTips.I != null && FoodTips.I.current != "ingredient";
            float shownDelay = -1;
            for (float w = 0; w < 4f; w += Time.deltaTime) { if (FoodTips.I != null && FoodTips.I.current == "ingredient") { shownDelay = Time.time - t0; break; } yield return null; }
            FoodTips.Trigger("ingredient");
            bool once = !FoodTips.Queued("ingredient") || FoodTips.I.current == "ingredient";
            int queuedAgain = 0; yield return new WaitForSeconds(FoodTips.TipDuration + .2f); if (FoodTips.I.current == "ingredient") queuedAgain++; FoodTips.Trigger("ingredient"); if (FoodTips.Queued("ingredient")) queuedAgain++;
            FoodTips.ResetSession(); GameSettings.Tips = false; FoodTips.Trigger("dish_Cake"); yield return new WaitForSeconds(.5f);
            bool offZero = FoodTips.I.current == null && !FoodTips.Queued("dish_Cake") && FoodTips.I.shown == 0;
            bool noPrefs = PlayerPrefs.GetInt("neon_tip_ingredient", -1) == pref0;
            GameSettings.Tips = tipsOn0; FoodTips.AutoEnabled = true; FoodTips.ResetSession();
            Check("M27", "首次提示卡：交火中（2 秒内开过火）延后弹出；同一条只出一次；关掉开关时 0 张；测试模式不写存档",
                delayed && shownDelay >= FoodTips.CombatQuiet - .1f && shownDelay < FoodTips.CombatQuiet + 1f && once && queuedAgain == 0 && offZero && noPrefs,
                "0.8 秒时未弹=" + delayed + "，实际 " + shownDelay.ToString("0.00") + " 秒后弹出（安静阈值 " + FoodTips.CombatQuiet + " 秒），只出一次=" + (once && queuedAgain == 0) + "，关掉=0 张：" + offZero + "，未写存档=" + noPrefs);

            // ---------------- M28：H 帮助页四个页签；菜谱页 7 道菜名字 / 配方 / 效果全公开；我的英雄写被动和大招
            bool recipesOk = Ingredients.Forms.Length == 7;
            foreach (var fm in Ingredients.Forms) if (string.IsNullOrEmpty(Ingredients.FormName(fm)) || Ingredients.FormName(fm).Contains("？") || string.IsNullOrEmpty(Ingredients.FormRecipe(fm)) || string.IsNullOrEmpty(Ingredients.FormEffect(fm))) recipesOk = false;
            bool heroTab = !string.IsNullOrEmpty(FoodState.Passive(p.role)) && !string.IsNullOrEmpty(FoodGuide.UltEffect(p.role)) && !string.IsNullOrEmpty(p.Hero.skill.HowTo);
            bool tabsOk = false;
            if (m.ui != null) { m.ui.helpTab = 1; m.ui.preview = 3; yield return null; yield return null; tabsOk = m.ui.helpTab == 1; m.ui.helpTab = 3; yield return null; tabsOk &= m.ui.helpTab == 3; m.ui.preview = 0; m.ui.helpTab = 0; }
            Check("M28", "H 帮助页有\"菜谱\"\"我的英雄\"页签：7 道菜名字 / 配方 / 效果全公开（不再有？？？）；我的英雄写技能怎么用、被动和大招",
                recipesOk && heroTab && tabsOk, "菜谱=" + recipesOk + "，我的英雄=" + heroTab + "（" + p.Hero.name + "：" + FoodState.Passive(p.role) + "；" + FoodGuide.UltEffect(p.role) + "），页签可切换=" + tabsOk);
            m.phase = Phase.Live; m.roundClock = Config.RoundTime;
        }

        // V20：靶场自动测量 —— 每把枪对 25 米 3 级甲靶子打胸 / 打头，记录实际发数和用时，对比理论值；再测穿墙
        IEnumerator RangeRun()
        {
            var m = Match.I; yield return new WaitForSeconds(.5f);
            var p = m.player; var lane = Range.Lanes[1]; var sb = new System.Text.StringBuilder();
            var mov = Range.Lanes[4]; mov.moving = false; Place(mov.dummy, Range.Origin + new Vector3(13f, .05f, 20f), 180);   // 移动靶挪开，别挡住 25 米靶的弹道
            foreach (var w in Config.Arsenal)
                foreach (var part in new[] { HitPart.Chest, HitPart.Head })
                {
                    lane.dummy.Respawn(lane.pos, 180); lane.dummy.armorClass = lane.dummy.helmetClass = 3; lane.dummy.armor = Config.MaxArmor; lane.dummy.damageLog.Clear();
                    p.Equip(w); yield return new WaitForSeconds(w.drawTime + .05f);
                    Vector3 from = Range.Origin + new Vector3(lane.pos.x - Range.Origin.x, 1.62f, lane.pos.z - Range.Origin.z - 25f);
                    Place(p, new Vector3(from.x, .05f, from.z), 0); yield return null;
                    float t0 = -1; int fired = 0; float lastFire = 0;
                    while (lane.dummy.alive && fired < 120)
                    {
                        Vector3 aim = lane.dummy.transform.position + Vector3.up * (part == HitPart.Head ? 1.75f : 1.32f);
                        for (int k = 0; k < w.pellets; k++) Bullet.Fire(p, from, from, (aim - from).normalized, Color.white);
                        fired++; if (t0 < 0) t0 = Time.time; lastFire = Time.time;
                        float wait = w.burst > 1 && fired % w.burst != 0 ? w.burstInterval : 60f / w.rpm * (w.burst > 1 ? w.burst : 1) - (w.burst > 1 ? w.burstInterval * (w.burst - 1) : 0);
                        yield return new WaitForSeconds(Mathf.Max(.02f, wait));
                    }
                    yield return new WaitForSeconds(.3f);
                    float actual = lastFire - t0;   // 第一发到击杀那一发之间的时间（和理论 TTK 的定义一致）
                    float theo = Ballistics.Ttk(w, 3, 3, 25f, part, out int st);
                    string line = w.id + " " + Ballistics.PartName(part) + " 实际 " + (fired) + " 发 / " + actual.ToString("0.00") + "s · 理论 " + st + " 发 / " + theo.ToString("0.00") + "s · 击杀=" + !lane.dummy.alive;
                    Debug.Log("NEON_RANGE_RESULT " + line); sb.AppendLine("| " + w.id + " | " + Ballistics.PartName(part) + " | " + fired + " | " + actual.ToString("0.00") + " | " + st + " | " + theo.ToString("0.00") + " |");
                }
            // 穿墙：25cm 薄墙后面放一个靶子，AR-9（可穿 30cm）能打到，SMG-7（只能穿 12cm）打不到
            var tgt = Range.Lanes[0].dummy; Vector3 wallBehind = Range.Origin + new Vector3(-11f, .05f, 21f);
            bool arThrough = false, smgThrough = false;
            foreach (var wid in new[] { "AR-9", "SMG-7" })
            {
                tgt.Respawn(wallBehind, 180); tgt.armorClass = tgt.helmetClass = 0; tgt.armor = 0; tgt.damageLog.Clear();
                foreach (var w in Config.Arsenal) if (w.id == wid) p.Equip(w);
                Vector3 from = Range.Origin + new Vector3(-11f, 1.4f, 12f); Place(p, new Vector3(from.x, .05f, from.z), 0);
                yield return new WaitForSeconds(.7f);
                for (int k = 0; k < 3; k++) { Bullet.Fire(p, from, from, (tgt.transform.position + Vector3.up * 1.3f - from).normalized, Color.white); yield return new WaitForSeconds(.15f); }
                yield return new WaitForSeconds(.3f);
                bool hit = tgt.damageLog.Count > 0 || tgt.hp < Config.MaxHp;
                if (wid == "AR-9") arThrough = hit; else smgThrough = hit;
            }
            Debug.Log("NEON_RANGE_WALL AR-9穿透=" + arThrough + " SMG-7穿透=" + smgThrough);
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "range_report.md"),
                "| 武器 | 部位 | 实际发数 | 实际用时 s | 理论发数 | 理论用时 s |\n| --- | --- | --- | --- | --- | --- |\n" + sb + "\n穿墙：AR-9 打穿 25cm 薄墙=" + arThrough + "，SMG-7=" + smgThrough + "\n");
            if (!Application.isBatchMode)
            {
                // 截图：靶场面板 / F1 调参面板 / F2 判定框
                string sdir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Screenshots"); System.IO.Directory.CreateDirectory(sdir);
                foreach (var l in Range.Lanes) l.dummy.Respawn(l.pos, 180);
                foreach (var w in Config.Arsenal) if (w.id == "AR-9") p.Equip(w);
                Place(p, Range.Origin, 0); p.yaw = 0; p.pitch = 2f; yield return new WaitForSeconds(.8f);
                var aimT = Range.Lanes[1].dummy.transform.position + Vector3.up * 1.3f;
                for (int k = 0; k < 4; k++) { Bullet.Fire(p, p.Eye, p.Eye, (aimT - p.Eye).normalized, Color.white); yield return new WaitForSeconds(.1f); }
                yield return new WaitForSeconds(.4f);
                yield return Grab(System.IO.Path.Combine(sdir, "26_range.png"));
                TuningPanel.Open = true; yield return null; yield return Grab(System.IO.Path.Combine(sdir, "27_tuning_panel.png")); TuningPanel.Open = false;
                TuningPanel.ShowHitboxes = true; Place(p, Range.Origin + new Vector3(-1, 0, 18), 0); yield return new WaitForSeconds(.3f);
                yield return Grab(System.IO.Path.Combine(sdir, "28_hitboxes.png")); TuningPanel.ShowHitboxes = false;
            }
            Debug.Log("NEON_RANGE_DONE");
            Application.Quit(0);
        }

        IEnumerator MatchRun()
        {
            Time.timeScale = 4f;
            float startReal = Time.realtimeSinceStartup;
            Debug.Log("NEON_MAPMETRIC " + MapMetrics());
            { float t0 = Time.realtimeSinceStartup; var sr = Sightlines(); Debug.Log("NEON_SIGHT pts=" + sr.pts + " over60=" + sr.over60 + " over48=" + sr.over48 + " longest=" + sr.longest.ToString("0") + "m roof→mainstreet=" + sr.roofLines + " (" + (Time.realtimeSinceStartup - t0).ToString("0.0") + "s) worst: " + sr.worst); }
            while (Match.I.phase != Phase.MatchEnd && Time.realtimeSinceStartup - startReal < 1100f) yield return null;
            var m = Match.I;
            Debug.Log("NEON_MATCH_DONE phase=" + m.phase + " score=" + m.roundsWon[0] + ":" + m.roundsWon[1] + " rounds=" + m.records.Count);
            { int sw = 0, ex = 0; foreach (var f in m.fighters) { sw += f.matchStats.swallowedShots + f.stats.swallowedShots; ex += f.matchStats.exhaustCount + f.stats.exhaustCount; } Debug.Log("NEON_V22_TELEMETRY swallowed_shots=" + sw + " exhaust_count=" + ex); }
            foreach (var r in m.records)
                Debug.Log("NEON_ROUND " + r.round + " att=" + r.attackers + " winner=" + r.winner + " reason=" + r.reason + " dur=" + r.duration.ToString("0.0") + " plant=" + r.plantSite + "@" + r.plantTime.ToString("0.0")
                    + " contact=" + r.firstContact.ToString("0.0") + "@" + r.firstContactZone + " kills=" + r.kills[0] + "/" + r.kills[1]
                    + " kd=" + string.Join("/", r.killDist) + " retake=" + r.retake.ToString("0.0") + " rotate=" + r.rotate + " gate=" + r.gateClose + "c/" + r.gateOpen + "o stuck=" + r.stuck + " doorEnd=" + r.doorEnd + " valves=" + r.valves
                    + " links=" + string.Join("/", r.links) + " pings=" + r.pings
                    + " skills=" + string.Join("/", r.skillUses) + " eff=" + string.Join("/", r.skillEffective) + " routes=" + r.routes);
            Debug.Log("NEON_MAP_SUMMARY " + m.MapSummary());
            if (m.phase != Phase.MatchEnd) m.WriteTelemetry();
            Application.Quit(m.phase == Phase.MatchEnd ? 0 : 2);
        }

        // V22 第二期：静态轮转距离（NavMesh 路径长度，门按当前状态），改图前后同一组点
        public static float PathLen(Vector3 a, Vector3 b)
        {
            var p = new NavMeshPath();
            if (!NavMesh.SamplePosition(a, out NavMeshHit ha, 3f, NavMesh.AllAreas) || !NavMesh.SamplePosition(b, out NavMeshHit hb, 3f, NavMesh.AllAreas)) return -1f;
            if (!NavMesh.CalculatePath(ha.position, hb.position, NavMesh.AllAreas, p) || p.status != NavMeshPathStatus.PathComplete) return -1f;
            float L = 0; for (int i = 1; i < p.corners.Length; i++) L += Vector3.Distance(p.corners[i - 1], p.corners[i]); return L;
        }
        // V22 第二期：视线采样（M41）。三条主路 + 东通道，2 m 网格、胸口高 1.4 m，两两射线；出生点内部不采样。
        // 挡视线 = 任意非触发碰撞体（角色、可打碎玻璃、技能盾不算）。返回 > 60 m 点对数、> 48 m 点对数、最长无遮挡长度和最长的几条。
        public static readonly Rect[] SightRegions = { Rect.MinMaxRect(-72, 30, 26, 50), Rect.MinMaxRect(-72, -50, 26, -30), Rect.MinMaxRect(-56, -10, 14, 10), Rect.MinMaxRect(48, -50, 72, 50) };
        public static List<Vector3> SightPoints(float step = 2f)
        {
            var pts = new List<Vector3>();
            foreach (var r in SightRegions)
                for (float x = r.xMin + step / 2; x < r.xMax; x += step)
                    for (float z = r.yMin + step / 2; z < r.yMax; z += step)
                    {
                        if (Mathf.Abs(z) < 10f && Mathf.Abs(x) > 56f) continue;                 // 出生点内部
                        var p = new Vector3(x, 0, z);
                        if (!NavMesh.SamplePosition(p, out NavMeshHit h, .8f, NavMesh.AllAreas) || h.position.y > .5f) continue;   // 只取能走到的地面
                        bool dup = false; foreach (var q in pts) if ((q - h.position).sqrMagnitude < .25f) { dup = true; break; }
                        Vector3 g = h.position; if (Physics.Raycast(g + Vector3.up * .6f, Vector3.down, out RaycastHit gh, 1.5f, ~0, QueryTriggerInteraction.Ignore)) g.y = gh.point.y;   // 胸口高按真实地面算（NavMesh 表面略高于地面）
                        // NavMesh 在每栋实心楼体内部地面上都有烘焙出来的"孤岛"（走不进去），射线从碰撞体内部出发又打不到它自己 → 楼体里的点不算
                        if (Physics.CheckCapsule(g + Vector3.up * .5f, g + Vector3.up * 1.6f, .25f, ~0, QueryTriggerInteraction.Ignore)) continue;
                        if (!dup) pts.Add(g);
                    }
            return pts;
        }
        public static bool SightClear(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a; float L = d.magnitude;
            if (!Physics.Raycast(a, d / L, out RaycastHit first, L, ~0, QueryTriggerInteraction.Ignore)) return true;
            if (!Ignorable(first.collider)) return false;
            foreach (var h in Physics.RaycastAll(a, d / L, L, ~0, QueryTriggerInteraction.Ignore)) if (!Ignorable(h.collider)) return false;
            return true;
        }
        static bool Ignorable(Collider c) { return c.GetComponentInParent<Fighter>() != null || c.GetComponentInParent<GlassPanel>() != null || c.GetComponentInParent<ShieldWall>() != null || c.GetComponentInParent<Drone>() != null; }
        public struct SightResult { public int pts, over60, over48; public float longest; public string worst; public int roofLines; }
        public static SightResult Sightlines()
        {
            var res = new SightResult(); var pts = SightPoints(); res.pts = pts.Count;
            Vector3 up = Vector3.up * 1.4f;
            string[] rn = { "北", "南", "主", "东" };
            int Reg(Vector3 p) { for (int r = 0; r < SightRegions.Length; r++) if (SightRegions[r].Contains(new Vector2(p.x, p.z))) return r; return 0; }
            var cls = new Dictionary<string, (int n, float L, Vector3 a, Vector3 b)>();
            for (int i = 0; i < pts.Count; i++)
                for (int j = i + 1; j < pts.Count; j++)
                {
                    float L = Vector3.Distance(pts[i], pts[j]);
                    if (L <= 44f) continue;
                    if (!SightClear(pts[i] + up, pts[j] + up)) continue;
                    if (L > 48f) res.over48++;
                    if (L > 60f)
                    {
                        res.over60++;
                        int ra = Reg(pts[i]), rb = Reg(pts[j]); string key = rn[Mathf.Min(ra, rb)] + rn[Mathf.Max(ra, rb)];
                        cls.TryGetValue(key, out var c); c.n++; if (L > c.L) { c.L = L; c.a = pts[i]; c.b = pts[j]; } cls[key] = c;
                    }
                    res.longest = Mathf.Max(res.longest, L);
                }
            var sb = new StringBuilder();
            foreach (var kv in cls) sb.Append(kv.Key).Append('×').Append(kv.Value.n).Append(" 最长 ").Append(kv.Value.L.ToString("0")).Append("m ").Append(kv.Value.a.ToString("F0")).Append("→").Append(kv.Value.b.ToString("F0")).Append("; ");
            res.worst = sb.ToString();
            // 市场楼顶（站姿眼高）→ x < 0 的主街地面
            for (float x = 31; x <= 43; x += 2) for (float z = -7; z <= 7; z += 2)
                {
                    Vector3 eye = new Vector3(x, Arena.RoofTop + 1.62f, z);
                    foreach (var q in pts) if (q.x < 0 && q.x > -56 && Mathf.Abs(q.z) <= 10 && SightClear(eye, q + up)) res.roofLines++;
                }
            return res;
        }

        // 路径不通时打印：两端采样到哪、路径状态、最后一个拐点（PathPartial 停在离终点最近的可达点 → 就是挡路的地方）
        public static string PathDebug(Vector3 a, Vector3 b)
        {
            bool sa = NavMesh.SamplePosition(a, out NavMeshHit ha, 3f, NavMesh.AllAreas), sb = NavMesh.SamplePosition(b, out NavMeshHit hb, 3f, NavMesh.AllAreas);
            if (!sa || !sb) return "sample a=" + sa + " b=" + sb;
            var p = new NavMeshPath(); NavMesh.CalculatePath(ha.position, hb.position, NavMesh.AllAreas, p);
            return "from " + ha.position.ToString("F1") + " to " + hb.position.ToString("F1") + " status=" + p.status + " corners=" + p.corners.Length + " last=" + (p.corners.Length > 0 ? p.corners[p.corners.Length - 1].ToString("F1") : "-");
        }

        static string MapMetrics()
        {
            var ar = Arena.I; if (ar == null) return "no arena";
            Vector3 att = ar.SpawnPoint(true, 1), def = ar.SpawnPoint(false, 1), cross = new Vector3(-2, 0, 0);
            float nS = PathLen(new Vector3(-4, 0, 40), new Vector3(-4, 0, -40)), ab = PathLen(Arena.SiteA, Arena.SiteB);
            string gates = ""; foreach (var s in ar.shutters) if (s.gate) gates += (s.transform.position.z > 0 ? "N" : "S") + (s.open ? "开" : "关");
            return "gates=" + gates + " att_N2S=" + nS.ToString("0") + "m att_spawn→A=" + PathLen(att, Arena.SiteA).ToString("0") + "m att_spawn→B=" + PathLen(att, Arena.SiteB).ToString("0")
                + "m att_spawn→cross=" + PathLen(att, cross).ToString("0") + "m def_spawn→cross=" + PathLen(def, cross).ToString("0") + "m def_A→B=" + ab.ToString("0") + "m rotateRatio=" + (ab > 0 ? (nS / ab).ToString("0.00") : "-");
        }

        // Capture exactly what is on screen (same render path as the game) at the end of the frame.
        static IEnumerator Grab(string file)
        {
            if (Camera.main != null) Camera.main.targetTexture = null;
            yield return null;
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            // V21：窗口被遮挡 / 锁屏时抓屏会返回空贴图，退回到相机离屏渲染（没有 HUD），不再卡死
            if (tex == null || tex.width < 16)
            {
                Debug.LogWarning("NEON_SHOT_FALLBACK " + file);
                if (Camera.main != null) { if (HudCaptureBegin.Allow) yield return RenderWithHud(Camera.main, file); else Render(Camera.main, file); }
                yield break;
            }
            System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.Destroy(tex);
        }

        static void Render(Camera cam, string file)
        {
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.DefaultHDR);
            cam.targetTexture = rt; cam.Render();
            var ldr = RenderTexture.GetTemporary(1600, 900, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(rt, ldr);
            RenderTexture.active = ldr;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply();
            System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
            RenderTexture.active = null; cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(ldr); rt.Release();
        }

        IEnumerator Shots()
        {
            var m = Match.I;
            string dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Screenshots");
            System.IO.Directory.CreateDirectory(dir);
            var cam = Camera.main;
            ForceLive();
            var p = m.player;
            if (System.Environment.CommandLine.Contains("-neonLinkShots"))   // V23 d：只拍联动截图（节省时间）
            {
                yield return new WaitForSeconds(1f);
                if (p != null) yield return LinkTests.Shots(dir, Grab);
                Debug.Log("NEON_SHOTS_DONE " + dir); Application.Quit(0); yield break;
            }
            Place(Other(1, 0), new Vector3(-24, .05f, 4), -100);
            Place(Other(1, 1), new Vector3(-22, .05f, 6.5f), -70);
            Place(Other(0, 1), new Vector3(-27, .05f, 2.2f), 60);
            Place(Other(0, 2), new Vector3(36, .05f, 30), 200);
            if (ToonAssets.Available) Other(1, 1).Food.Become(FoodForm.Cake);   // V18：截图里展示一个变身成蛋糕的豆子
            yield return new WaitForSeconds(1.5f);
            var views = new List<(string, Vector3, Vector3, float)>
            {
                ("01_overview", new Vector3(-30, 34, -40), new Vector3(8, 0, 0), 60),
                ("02_mid_street", new Vector3(-52, 1.7f, 3), new Vector3(0, 2.5f, 0), 78),
                ("03_characters", new Vector3(-30, 1.7f, 2.5f), new Vector3(-23.5f, 1.2f, 4.5f), 50),
                ("04_site_A", new Vector3(27, 1.7f, 21), new Vector3(40, 1f, 38), 78),
                ("05_site_B", new Vector3(30, 1.7f, -21), new Vector3(42, 1f, -40), 78),
                ("06_north_street", new Vector3(-62, 1.8f, 40), new Vector3(0, 3f, 41), 78),
                ("11_sniper_tower", new Vector3(14, 2.2f, 38), new Vector3(23, 3f, 47.5f), 70),
                ("12_sniper_view", new Vector3(22.6f, 4.9f, 47.5f), new Vector3(-40, 2f, 42), 60),
                ("13_site_bunker", new Vector3(32, 1.8f, 30), new Vector3(42, .6f, 37), 70),
                ("14_checkpoint_gate", new Vector3(-7, 1.8f, 37), new Vector3(4, 2f, 41.5f), 70),
                ("15_market_roof_view", new Vector3(42.5f, 9.7f, -1), new Vector3(-20, 3f, 0), 65),
                ("16_skybridge", new Vector3(-24, 2f, -7), new Vector3(-8, 8.5f, 0), 70),
                ("20_pancake_tower", new Vector3(-38, 2.2f, 36), new Vector3(-47, 1.5f, 43.5f), 68),
                ("21_cake_tower", new Vector3(-38, 2.2f, -37), new Vector3(-47, 1.5f, -44), 68),
                ("22_kitchen", new Vector3(2, 2.6f, -9), new Vector3(10, .5f, -2), 70),
                ("23_hotpot_station", new Vector3(-21, 2f, -7), new Vector3(-26, 1f, -8.3f), 60),   // V22：原机位在高摊位岛边上
                // V22 第二期：新区域（设计稿 §6.3）
                ("44_north_kebab_alley", new Vector3(-2, 1.75f, 3.5f), new Vector3(-2, 3.2f, 26), 72),
                ("45_north_gate_new", new Vector3(14.5f, 2.4f, 47.6f), new Vector3(2, 2.2f, 40), 72),
                ("46_plaza_mezzanine", new Vector3(5, 3.2f, -9.5f), new Vector3(20, 3.4f, 0), 70),
                ("46b_roof_view_west", new Vector3(42.5f, 9.7f, -1), new Vector3(-20, 3f, 0), 65),
                ("46c_south_kitchen", new Vector3(-2, 1.8f, -10.6f), new Vector3(-2.6f, 1.3f, -24), 75),
                ("46d_market_stairs", new Vector3(53, 2.2f, 21), new Vector3(44.5f, 5f, 4), 70),
                ("46e_burger_platform", new Vector3(28, 2f, 20.5f), new Vector3(33, 1.5f, 26.5f), 65),
                ("46f_main_island", new Vector3(-46, 2f, -7.5f), new Vector3(-16, 1.5f, 2), 72),
                ("46g_long_street_shops", new Vector3(-58, 2f, 33), new Vector3(-28, 2.5f, 38), 72),
                ("47_north_street_view", new Vector3(-62, 1.8f, 40), new Vector3(0, 3f, 41), 78),
            };
            if (p != null) { p.SetViewModelVisible(false); p.enabled = false; }
            cam.transform.SetParent(null);
            {
                // GI diagnostics (bright/dark runs)
                Renderer wall = null; foreach (var r in m.arena.GetComponentsInChildren<Renderer>()) if (r.name.Contains("隔离墙")) { wall = r; break; }
                var lm = LightmapSettings.lightmaps;
                var sh = RenderSettings.ambientProbe; float shSum = 0; for (int c = 0; c < 3; c++) for (int k = 0; k < 9; k++) shSum += Mathf.Abs(sh[c, k]);
                var probes = LightmapSettings.lightProbes;
                Debug.Log("NEON_DIAG wallIdx=" + (wall != null ? wall.lightmapIndex.ToString() : "none") + " so=" + (wall != null ? wall.lightmapScaleOffset.ToString("F3") : "") +
                    " lightmaps=" + lm.Length + " tex=" + (lm.Length > 0 && lm[0].lightmapColor != null ? lm[0].lightmapColor.name + " " + lm[0].lightmapColor.format : "null") +
                    " probes=" + (probes != null ? probes.count : -1) + " ambientSH=" + shSum.ToString("F2") + " mode=" + RenderSettings.ambientMode +
                    " sun=" + (RenderSettings.sun != null ? RenderSettings.sun.intensity.ToString() : "null") + " lights=" + Object.FindObjectsOfType<Light>().Length +
                    " fog=" + RenderSettings.fogDensity + " refl=" + RenderSettings.reflectionIntensity + " time=" + Time.time.ToString("F1"));
            }
            foreach (var v in views)
            {
                cam.transform.position = v.Item2; cam.transform.LookAt(v.Item3); cam.fieldOfView = v.Item4;
                yield return new WaitForSeconds(.4f);
                yield return new WaitForEndOfFrame();
                yield return Grab(System.IO.Path.Combine(dir, v.Item1 + ".png"));
            }
            // V22 第二期：蒸笼门关上的样子 + 蒸汽阀喷气
            {
                var gN = m.arena.Gate(1);
                if (gN != null)
                {
                    gN.SetOpen(false); yield return new WaitForSeconds(Shutter.GateMoveTime + .6f);
                    cam.transform.position = new Vector3(-6, 1.8f, 36.4f); cam.transform.LookAt(new Vector3(4, 2.3f, 40.5f)); cam.fieldOfView = 70;
                    yield return new WaitForSeconds(.3f); yield return new WaitForEndOfFrame();
                    yield return Grab(System.IO.Path.Combine(dir, "45b_gate_closed.png"));
                    gN.ResetRound();
                }
                SteamValve sv = null; foreach (var v in SteamValve.Valves) if (v != null && v.transform.position.z > 0 && v.transform.position.x > 10) sv = v;
                if (sv != null)
                {
                    sv.Fire(null); yield return new WaitForSeconds(1.6f);
                    cam.transform.position = new Vector3(6, 2.2f, 11); cam.transform.LookAt(new Vector3(16, 1.6f, 8)); cam.fieldOfView = 70;
                    yield return new WaitForEndOfFrame();
                    yield return Grab(System.IO.Path.Combine(dir, "45c_steam_valve.png"));
                    sv.ResetRound(); SteamCloud.ClearTimed();
                }
            }
            if (ToonAssets.Available) yield return PropShots(dir, cam);   // V21：场景道具机位（36–37）
            // V21：趴下动作（侧面 / 正面 / 匍匐），旁边站一个做身高对比
            {
                var pr = Other(0, 2); var st = Other(0, 1); var prb = pr.GetComponent<Bot>(); var stb = st.GetComponent<Bot>();
                if (prb != null) prb.enabled = false; if (stb != null) stb.enabled = false;
                Place(pr, new Vector3(-40, .05f, 4), 90); Place(st, new Vector3(-40.5f, .05f, 6f), 180); yield return null;
                pr.SetProne(true);
                yield return new WaitForSeconds(1.2f);
                Debug.Log("NEON_PRONE_TOP still=" + (pr.avatar != null ? pr.avatar.VisualTop().ToString("0.00") + " clip=" + pr.avatar.ClipName : "none"));
                cam.transform.position = new Vector3(-40.2f, .9f, -.6f); cam.transform.LookAt(new Vector3(-40.2f, .5f, 4.5f)); cam.fieldOfView = 45;
                yield return new WaitForSeconds(.1f); yield return new WaitForEndOfFrame();
                yield return Grab(System.IO.Path.Combine(dir, "29_prone_side.png"));
                cam.transform.position = new Vector3(-36.3f, .75f, 4.6f); cam.transform.LookAt(new Vector3(-40, .35f, 4.3f)); cam.fieldOfView = 50;
                yield return new WaitForSeconds(.1f); yield return new WaitForEndOfFrame();
                yield return Grab(System.IO.Path.Combine(dir, "30_prone_front.png"));
                Place(pr, new Vector3(-41, .05f, 4), 90); pr.botMove = pr.transform.forward;
                yield return new WaitForSeconds(.6f);
                Debug.Log("NEON_PRONE_TOP crawl=" + (pr.avatar != null ? pr.avatar.VisualTop().ToString("0.00") + " clip=" + pr.avatar.ClipName : "none"));
                cam.transform.position = new Vector3(-40.2f, .9f, -.6f); cam.transform.LookAt(new Vector3(-40.2f, .5f, 4.5f)); cam.fieldOfView = 45;
                yield return new WaitForEndOfFrame();
                yield return Grab(System.IO.Path.Combine(dir, "31_prone_crawl.png"));
                pr.botMove = Vector3.zero; pr.SetProne(false);
                if (prb != null) prb.enabled = true; if (stb != null) stb.enabled = true;
            }
            // skill FX shot
            // V22：原站位 (-16, 2.5) 在高摊位岛里，整组挪到岛东边、机位从中段十字往西拍
            var iron = Get(0, Role.Ironwall); Place(iron, new Vector3(-12.5f, .05f, 3), 90); iron.skillCharges = 1; Skills.Cast(iron, Vector3.zero);
            var ph = Get(1, Role.Phantom); Place(ph, new Vector3(-10, .05f, -3), 90); ph.skillCharges = 1; Skills.Cast(ph, new Vector3(-10, 0, -3.5f));
            var em = Get(1, Role.Ember); new GameObject("fx charge").AddComponent<BlastCharge>().Init(em, new Vector3(-7, .1f, 4));
            if (p != null) p.SetViewModelVisible(false);
            cam.transform.position = new Vector3(3, 2.4f, -6.5f); cam.transform.LookAt(new Vector3(-10, 1.2f, 0.5f)); cam.fieldOfView = 70;
            yield return new WaitForSeconds(.4f); yield return new WaitForEndOfFrame();
            yield return Grab(System.IO.Path.Combine(dir, "08_skills.png"));
            yield return new WaitForSeconds(.55f); yield return new WaitForEndOfFrame();
            yield return Grab(System.IO.Path.Combine(dir, "09_explosion.png"));
            // weapon lineup (checks orientation / scale of the downloaded gun models)
            var line = new GameObject("武器陈列"); line.transform.position = new Vector3(-40, 50, 0);
            var key = new GameObject("key").AddComponent<Light>(); key.type = LightType.Point; key.range = 12; key.intensity = 3; key.transform.position = new Vector3(-40, 52, -2);
            var lineup = Config.Arsenal;
            for (int i = 0; i < lineup.Length; i++)
            {
                Transform g;
                g = GunModel.Create(lineup[i], out _, Color.white, false); g.localScale = Vector3.one * 1.3f;
                g.SetParent(line.transform, false); g.localPosition = new Vector3(i < 4 ? -.62f : .62f, 1.3f - (i % 4) * .55f, 0); g.localRotation = Quaternion.Euler(0, 90, 0);
            }
            cam.transform.position = new Vector3(-40, 49.8f, -3.6f); cam.transform.LookAt(new Vector3(-40, 49.8f, 0)); cam.fieldOfView = 45;
            yield return new WaitForSeconds(.3f); yield return new WaitForEndOfFrame();
            yield return Grab(System.IO.Path.Combine(dir, "10_weapons.png"));
            Destroy(line); Destroy(key.gameObject);
            if (p != null)
            {
                p.enabled = true; p.AttachCamera(); p.SetViewModelVisible(true);
                Place(p, new Vector3(-44, .05f, 3.5f), 92); p.pitch = 1;
                yield return new WaitForSeconds(.8f);
                for (int i = 0; i < 2; i++) { p.ammo = 30; yield return null; }
                yield return new WaitForEndOfFrame();
                yield return Grab(System.IO.Path.Combine(dir, "07_first_person.png"));
                if (ToonAssets.Available)   // V22：第一人称手臂 —— 七把枪的腰射 + 一张换弹，检查手对齐 / 不挡准星 / 不穿模
                {
                    var w0 = p.weapon;
                    foreach (var gid in new[] { "SMG-7", "AR-9", "LMG-2", "P-9", "DMR-3", "FS-12", "BR-5" })
                    {
                        WeaponSpec ws = gid == Config.Pistol.id ? Config.Pistol : null;
                        foreach (var a2 in Config.Arsenal) if (a2.id == gid) ws = a2;
                        if (ws == null) continue;
                        p.Equip(ws); yield return new WaitForSeconds(Mathf.Max(.6f, ws.drawTime + .3f));
                        yield return new WaitForEndOfFrame();
                        yield return Grab(System.IO.Path.Combine(dir, "07_fp_" + gid.Replace("-", "") + ".png"));
                        if (gid == "AR-9")
                        {
                            p.ammo = 1; p.Reload();
                            for (float w = 0; w < 3f && p.IsReloading && p.ReloadProgress < .45f; w += Time.deltaTime) yield return null;
                            yield return new WaitForEndOfFrame();
                            yield return Grab(System.IO.Path.Combine(dir, "07_fp_AR9_reload.png"));
                            for (float w = 0; w < 6f && p.IsReloading; w += Time.deltaTime) yield return null;
                        }
                    }
                    p.Equip(w0); yield return new WaitForSeconds(.5f);
                }
                if (ToonAssets.Available)
                {
                    // V21：拿着面粉看鸡蛋（绿色：巨型蛋糕）和辣椒（灰色：黑暗料理）+ 开局横幅 + 首次提示卡
                    p.Food.EndForm(false); p.Food.ingredients.Clear(); p.Food.ingredients.Add("Flour");
                    Vector3 fwd = Quaternion.Euler(0, p.yaw, 0) * Vector3.forward, side = Quaternion.Euler(0, p.yaw, 0) * Vector3.right;
                    var se = IngredientPickup.Spawn("Egg", p.transform.position + fwd * 4.5f - side * 1.3f, null, 20f);
                    var sc = IngredientPickup.Spawn("Chili", p.transform.position + fwd * 5.5f + side * 1.5f, null, 20f);
                    if (m.ui != null) m.ui.OnLive();
                    FoodTips.ResetSession(); FoodTips.AutoEnabled = false; p.lastShotAt = p.lastDamageAt = -10; FoodTips.Trigger("ingredient");
                    yield return new WaitForSeconds(.5f); yield return new WaitForEndOfFrame();
                    yield return Grab(System.IO.Path.Combine(dir, "32_ingredient_bubble.png"));
                    var saveMode = Match.Mode; Match.Mode = GameMode.FFA; if (m.ui != null) m.ui.OnLive(); Match.Mode = saveMode;
                    yield return new WaitForSeconds(.4f); yield return new WaitForEndOfFrame();
                    yield return Grab(System.IO.Path.Combine(dir, "33_banner_ffa.png"));
                    if (se != null) Destroy(se.gameObject); if (sc != null) Destroy(sc.gameObject);
                    p.Food.ingredients.Clear(); FoodTips.AutoEnabled = true;
                }
            }
            if (p != null) yield return HudShots(dir);   // V22 第一期：状态带 / 冲刺转开火 / 开镜 / 死亡回顾（38–41）
            if (p != null) yield return LinkShots(dir);  // V22 第三期：6 个联动瞬间 + 联动提示 + 标记 + 大招预警（48–57）
            if (p != null && ToonAssets.Available) yield return V23SkillShots(dir);   // V23 c：6 技能 + 6 大招施放瞬间、曼妥思喷泉、蒸汽里被炸（60–74）
            if (p != null) yield return MapUiTests.Shots(dir, Grab);   // V23 b：小地图跟随 / 北向、大地图、食材光柱 / 边缘箭头 / 交火变淡（80–86，MapUiTests.cs）
            if (p != null) yield return LinkTests.Shots(dir, Grab);    // V23 d：整片蒸汽内外 / 芥末呛雾 / 开大火 / 副联动 / 门洞（90–98，LinkTests.cs）
            // V9 menus
            m.ui.preview = 1; for (int f = 0; f < 3; f++) yield return null;
            yield return Grab(System.IO.Path.Combine(dir, "17_main_menu.png"));
            m.ui.preview = 2; yield return Grab(System.IO.Path.Combine(dir, "18_settings.png"));
            m.ui.preview = 3; yield return Grab(System.IO.Path.Combine(dir, "24_help.png"));
            if (ToonAssets.Available)
            {
                // V21：H 帮助页的"菜谱"和"我的英雄"页签
                m.ui.helpTab = 1; yield return null; yield return Grab(System.IO.Path.Combine(dir, "34_help_recipe.png"));
                m.ui.helpTab = 3; yield return null; yield return Grab(System.IO.Path.Combine(dir, "35_help_hero.png"));
                m.ui.helpTab = 0;
            }
            if (ToonAssets.Available) { m.ui.preview = 1; FoodHud.showCollection = true; yield return null; yield return Grab(System.IO.Path.Combine(dir, "25_collection.png")); FoodHud.showCollection = false; }
            m.ui.preview = 0; if (p != null) p.SetViewModelVisible(true);
            m.ui.PreviewPause(true); yield return Grab(System.IO.Path.Combine(dir, "19_pause.png")); m.ui.PreviewPause(false);
            Debug.Log("NEON_SHOTS_DONE " + dir);
            Application.Quit(0);
        }

        // ================================================================ V21：场景道具接入（M29）
        IEnumerator PropTests()
        {
            var m = Match.I;
            int loaded = 0; foreach (var n in FoodScenery.SmallModels) if (Resources.Load<GameObject>(FoodScenery.Dir + "SmallProps/" + n) != null) loaded++;
            bool bigLoaded = Resources.Load<GameObject>(FoodScenery.Dir + "ChocolateCover") != null && Resources.Load<GameObject>(FoodScenery.Dir + "HotpotHazard") != null;
            int total = FoodScenery.Total, kinds = 0; foreach (var n in FoodScenery.SmallModels) if (FoodScenery.Placed.TryGetValue(n, out int c) && c > 0) kinds++;
            var root = FoodScenery.Root;
            int propCols = root != null ? root.GetComponentsInChildren<Collider>(true).Length : -1;
            bool outside = root != null && !root.transform.IsChildOf(m.arena.transform);
            var gi = Resources.Load<ArenaLightmaps>("ArenaLightmaps");
            bool giOk = gi == null || ArenaLightmaps.Applied;   // 道具在 Arena 外创建，烘焙按顺序匹配仍然成功
            // 巧克力方块：碰撞盒尺寸 / 翻越连接 / 抖动都还在，旧模型只是不显示
            bool chocoOk = FoodScenery.ChocoFound > 0 && FoodScenery.ChocoSwapped == FoodScenery.ChocoFound; int chocoLinks = 0;
            foreach (var b in FoodScenery.ChocoRoots)
            {
                var bc = b != null ? b.GetComponent<BoxCollider>() : null;
                if (bc == null || !bc.enabled || Mathf.Abs(bc.size.y - 2f) > .01f || Mathf.Abs(bc.size.z - 2.44f) > .01f) chocoOk = false;
                if (b != null && b.GetComponent<FoodJiggle>() == null) chocoOk = false;
                if (b != null) { chocoLinks += b.GetComponentsInChildren<OffMeshLink>().Length; foreach (var r in b.transform.GetChild(0).GetComponentsInChildren<Renderer>(true)) if (r.enabled) chocoOk = false; }
            }
            // 火锅：圆柱碰撞、烫伤区、热气都在
            bool potOk = FoodScenery.HotpotFound == 2 && FoodScenery.HotpotSwapped == 2; string potInfo = "";   // V22 第二期：北关口新增关东煮锅（设计稿 §3.1），1 → 2
            foreach (var h in FoodScenery.HotpotRoots)
            {
                Collider col = null; foreach (var cc in h.GetComponentsInChildren<Collider>()) if (cc.enabled) col = cc;
                HotpotHazard hz = null; foreach (var z in Object.FindObjectsOfType<HotpotHazard>()) if (Vector3.Distance(z.transform.position, h.transform.position) < .1f) hz = z;
                bool top = col != null && Mathf.Abs(col.bounds.max.y - .9f) < .05f && Mathf.Abs(col.bounds.extents.x - 1.35f) < .05f;
                bool nav = NavMesh.SamplePosition(h.transform.position + Vector3.up * .9f, out NavMeshHit nh, .6f, NavMesh.AllAreas);
                potOk &= top && hz != null && hz.enabled;
                potInfo += "碰撞顶 " + (col != null ? col.bounds.max.y.ToString("0.00") : "无") + " m、半径 " + (col != null ? col.bounds.extents.x.ToString("0.00") : "-") + "，烫伤区=" + (hz != null) + "，锅顶在导航上=" + nav + "；";
            }
            // 甜品摊：没有换外观，碰撞 / 渲染都原样
            Transform stall = m.arena.transform.Find("夜市摊位 甜品屋"); int stallCols = 0, stallHidden = 0;
            if (stall != null) { foreach (var cc in stall.GetComponentsInChildren<Collider>()) if (cc.enabled) stallCols++; foreach (var r in stall.GetComponentsInChildren<Renderer>(true)) if (!r.enabled) stallHidden++; }
            bool stallOk = stall != null && stallCols >= 2 && stallHidden == 0;
            int inter = 0, interInProps = 0; foreach (var it in Interactable.All) if (it != null) { inter++; if (root != null && it.transform.IsChildOf(root.transform)) interInProps++; }
            Check("M29", "场景道具：5 种小道具都加载并摆出 15–30 件（无碰撞、贴地 ≤ 3 cm、在 Arena 外不影响烘焙匹配）；巧克力方块 / 火锅只换外观（碰撞、翻越连接、烫伤区原样）；甜品摊未换、碰撞原样",
                loaded == 5 && bigLoaded && total >= 15 && total <= 30 && kinds == 5 && propCols == 0 && FoodScenery.MaxGap <= .03f && outside && giOk && chocoOk && chocoLinks >= FoodScenery.ChocoSwapped && potOk && stallOk && interInProps == 0,
                "小道具加载 " + loaded + "/5、摆放 " + total + " 件（" + kinds + " 种，跳过 " + FoodScenery.Skipped.Count + "）、碰撞体 " + propCols + "、最大贴地误差 " + FoodScenery.MaxGap.ToString("0.000") + " m、在 Arena 外=" + outside + "、烘焙匹配=" + giOk
                + "；巧克力 " + FoodScenery.ChocoSwapped + "/" + FoodScenery.ChocoFound + "（翻越连接 " + chocoLinks + " 条）=" + chocoOk + "；火锅 " + FoodScenery.HotpotSwapped + "/" + FoodScenery.HotpotFound + "（" + potInfo + "）=" + potOk
                + "；甜品摊碰撞 " + stallCols + " 个、隐藏渲染器 " + stallHidden + "；交互物 " + inter + " 个（道具里 " + interInProps + "）");
            yield break;
        }

        // V21：场景道具截图（36 奶茶摊柜台、37 火锅），先把火锅热气清掉再拍
        IEnumerator PropShots(string dir, Camera cam)
        {
            foreach (var ps in SteamCloud.particles) if (ps != null) { var em = ps.emission; em.enabled = false; ps.Clear(); }
            var shots = new List<(string, Vector3, Vector3, float)>
            {
                ("36_food_props_stalls", new Vector3(-42.6f, 2.0f, -2.9f), new Vector3(-46.5f, 1.15f, .6f), 55),
                ("37_hotpot_cover", new Vector3(5.4f, 2.5f, -40.6f), new Vector3(8f, .6f, -35.5f), 62),
            };
            foreach (var v in shots)
            {
                cam.transform.position = v.Item2; cam.transform.LookAt(v.Item3); cam.fieldOfView = v.Item4;
                yield return new WaitForSeconds(.4f);
                yield return new WaitForEndOfFrame();
                yield return Grab(System.IO.Path.Combine(dir, v.Item1 + ".png"));
            }
            foreach (var ps in SteamCloud.particles) if (ps != null) { var em = ps.emission; em.enabled = true; }
        }

        // ================================================================ V22 第二期：地图方案甲（设计稿_V22_地图重构与干员地图联动 §6.1，M40–M45）
        // 编号对照：M40 连接巷 + 轮转 + 路线 + 包点入口（设计稿 M40 + M42 合并）、M41 视线、M42 蒸汽阀、M43 蒸笼门、M44 奶油平台 / 高点退路、M45 AI 走两条连接巷到 A / B
        static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
        IEnumerator V22MapTests()
        {
            var m = Match.I; var ar = m.arena; m.phase = Phase.Live; m.roundClock = Config.RoundTime;
            int att = m.AttackingTeam, def = m.DefendingTeam;
            foreach (var sh in ar.shutters) sh.ResetRound();
            ClearFire(); Skills.ClearRound();
            yield return new WaitForSeconds(.3f);
            const float T = Arena.CreamTop;

            // ---------------- M40：两条连接巷可通行、轮转距离、16 条进攻路线连通、包点入口（A 3+1 / B 3+1+侧门）
            System.Func<Vector3, Vector3, bool> viaLink = (a, b) =>
            {
                var p = new NavMeshPath();
                if (!NavMesh.SamplePosition(a, out NavMeshHit ha, 2f, NavMesh.AllAreas) || !NavMesh.SamplePosition(b, out NavMeshHit hb, 2f, NavMesh.AllAreas) || !NavMesh.CalculatePath(ha.position, hb.position, NavMesh.AllAreas, p) || p.status != NavMeshPathStatus.PathComplete) return false;
                for (int i = 1; i < p.corners.Length; i++) { Vector3 mid = (p.corners[i - 1] + p.corners[i]) / 2; if (Match.InLink(mid)) return true; }
                return false;
            };
            float kebab = PathLen(new Vector3(-2, 0, 7), new Vector3(-2, 0, 33)), kitchen = PathLen(new Vector3(-2, 0, -7), new Vector3(-2, 0, -33));
            bool kebabVia = viaLink(new Vector3(-2, 0, 7), new Vector3(-2, 0, 33)), kitchenVia = viaLink(new Vector3(-2, 0, -7), new Vector3(-2, 0, -33));
            float n2s = PathLen(new Vector3(-2, 0, 36), new Vector3(-2, 0, -36)), n2sOld = PathLen(new Vector3(-4, 0, 40), new Vector3(-4, 0, -40)), ab = PathLen(Arena.SiteA, Arena.SiteB);
            foreach (var sh in ar.shutters) if (!sh.gate) sh.SetOpen(true);   // 卷帘门 AI 会自己开，连通性按开着算
            yield return new WaitForSeconds(2.2f);
            Vector3 spawn = ar.SpawnPoint(true, 1); int routesOk = 0; string badRoutes = "";
            foreach (var kv in ar.routes)
            {
                bool ok = PathLen(spawn, kv.Value[0]) >= 0;
                for (int i = 1; i < kv.Value.Length && ok; i++) if (PathLen(kv.Value[i - 1], kv.Value[i]) < 0) { ok = false; badRoutes += kv.Key + "@" + i + " "; Debug.Log("NEON_PATHDBG " + kv.Key + "@" + i + " " + PathDebug(kv.Value[i - 1], kv.Value[i])); }
                if (ok) routesOk++; else if (!badRoutes.Contains(kv.Key)) badRoutes += kv.Key + " ";
            }
            int Entr(Vector3 site, params Vector3[] doors) { int n = 0; foreach (var d in doors) if (PathLen(spawn, d) >= 0 && PathLen(d, site) >= 0) n++; return n; }
            int aEnt = Entr(Arena.SiteA, new Vector3(24, 0, 40), new Vector3(20, 0, 17.5f), new Vector3(29, 0, 15));
            int bEnt = Entr(Arena.SiteB, new Vector3(24, 0, -39.5f), new Vector3(20, 0, -17.5f), new Vector3(46, 0, -16.5f));
            bool aWall = ar.breakWalls.Exists(w => w.transform.position.z > 0), bWall = ar.breakWalls.Exists(w => w.transform.position.z < 0), bDoor = ar.doors.Count > 0;
            foreach (var sh in ar.shutters) if (!sh.gate) sh.ResetRound();
            yield return new WaitForSeconds(.3f);
            Check("M40", "连接巷：北烤串巷 / 南后厨可通行；攻方 北路中段 → 南路中段 ≤ 80 m；守方 A → B 70–85 m；16 条进攻路线全连通；A 3 + 1、B 3 + 1 + 侧门",
                kebab > 0 && kebab < 32 && kebabVia && kitchen > 0 && kitchen < 32 && kitchenVia && n2s > 0 && n2s <= 80f && ab >= 70f && ab <= 85f && routesOk == ar.routes.Count && ar.routes.Count >= 16 && aEnt == 3 && bEnt == 3 && aWall && bWall && bDoor,
                "烤串巷 " + kebab.ToString("0") + " m（走巷=" + kebabVia + "）、后厨 " + kitchen.ToString("0") + " m（走巷=" + kitchenVia + "）；北→南 (−2,±36) " + n2s.ToString("0") + " m / (−4,±40) " + n2sOld.ToString("0") + " m（改前 157 m）；A→B " + ab.ToString("0")
                + " m；路线 " + routesOk + "/" + ar.routes.Count + (badRoutes != "" ? "（断：" + badRoutes + "）" : "") + "；入口 A " + aEnt + "+" + (aWall ? 1 : 0) + " / B " + bEnt + "+" + (bWall ? 1 : 0) + "，侧门=" + bDoor);

            // ---------------- M41：三条主路 + 东通道 2 m 网格两两射线，无遮挡 > 60 m 的点对 = 0；市场楼顶看不到 x < 0 的主街
            float st0 = Time.realtimeSinceStartup; var sr = Sightlines();
            Check("M41", "视线：三条主路 + 东通道（2 m 网格、胸口高）无遮挡且 > 60 m 的点对 = 0；市场楼顶 → x < 0 的主街地面无遮挡视线 = 0",
                sr.pts > 800 && sr.over60 == 0 && sr.roofLines == 0,
                "采样点 " + sr.pts + "，> 60 m " + sr.over60 + " 对（改前 18254），> 48 m " + sr.over48 + " 对（改前 32482），最长 " + sr.longest.ToString("0") + " m（改前 130 m），楼顶→主街西段 " + sr.roofLines + " 条（改前 9634）"
                + (sr.worst != "" ? "；最长几条：" + sr.worst : "") + "（" + (Time.realtimeSinceStartup - st0).ToString("0.0") + " s）");

            // ---------------- M42：蒸汽阀
            SteamValve valve = null; foreach (var v in SteamValve.Valves) if (v != null && Flat(v.transform.position, new Vector3(16, 0, 8)) < 1f) valve = v;
            bool clearBefore = false, fired = false, blocks = false, noReuse = false, clearAfter = false, cdOk = false; float held = 0, cdLeft = 0;
            if (valve != null)
            {
                valve.ResetRound(); SteamCloud.ClearTimed();
                var user = Other(att, 0); Place(user, valve.transform.position + new Vector3(-1.3f, .05f, 0), 90); yield return null;
                Vector3 la = new Vector3(13, 2.2f, 8), lb = new Vector3(21, 2.2f, 8);   // 牌楼侧柜在 x=12，从它东边量
                clearBefore = Skills.SightClear(la, lb, null);
                while (!fired && held < 1.5f) { fired = valve.Hold(user, Time.deltaTime); held += Time.deltaTime; yield return null; }
                blocks = valve.Active && !Skills.SightClear(la, lb, null);
                noReuse = !valve.Usable(user);
                yield return new WaitForSeconds(SteamValve.Duration + .3f);
                clearAfter = !valve.Active && Skills.SightClear(la, lb, null);
                cdLeft = valve.CooldownLeft; cdOk = cdLeft > SteamValve.Cooldown - SteamValve.Duration - 1.5f && cdLeft < SteamValve.Cooldown - SteamValve.Duration + .5f;
                valve.ResetRound(); SteamCloud.ClearTimed();
            }
            int plainValves = 0; foreach (var v in SteamValve.Valves) if (v != null && !(v is HotpotKnob)) plainValves++;   // V23 d：另有关口火锅"开大火"旋钮（也是 SteamValve）
            Check("M42", "蒸汽阀：中庭 ×2 + 南后厨 ×1；长按 G 0.5 s 喷整片白雾，穿过白雾的视线被挡；" + SteamValve.Duration + " s 后散开；冷却 " + SteamValve.Cooldown + " s 内不能再拧（V23 d 新数值）",
                plainValves == 3 && valve != null && clearBefore && fired && Mathf.Abs(held - SteamValve.HoldSecs) < .15f && blocks && noReuse && clearAfter && cdOk,
                "蒸汽阀 " + plainValves + " 个（+ 开大火旋钮 " + (SteamValve.Valves.Count - plainValves) + "），喷前视线通=" + clearBefore + "，长按 " + held.ToString("0.00") + " s 喷出=" + fired + "，白雾挡视线=" + blocks + "，冷却中不能再用=" + noReuse + "，" + (SteamValve.Duration + .3f).ToString("0.0") + " s 后散开=" + clearAfter + "，剩余冷却 " + cdLeft.ToString("0.0") + " s");

            // ---------------- M43：蒸笼门
            var gN = ar.Gate(1); var gS = ar.Gate(-1);
            bool bothOpen = false, attCantClose = false, closed = false, gBlocks = false, opened = false, passes = false; float tc = 0, to = 0, holeBefore = -1, holeLen = -1;
            if (gN != null && gS != null)
            {
                gN.ResetRound(); gS.ResetRound(); yield return null;
                bothOpen = gN.open && gS.open && !gN.GetComponent<Collider>().enabled && !gS.GetComponent<Collider>().enabled;
                var defF = Other(def, 0); var attF = Other(att, 1);
                Place(defF, gN.transform.position + new Vector3(1.3f, .05f, 0), -90); Place(attF, gN.transform.position + new Vector3(-1.3f, .05f, 1.5f), 90); yield return null;
                attCantClose = !gN.Usable(attF);
                while (!closed && tc < 3.5f) { closed = gN.Hold(defF, Time.deltaTime); tc += Time.deltaTime; yield return null; }
                yield return new WaitForSeconds(Shutter.GateMoveTime + .25f);
                gBlocks = !gN.open && gN.GetComponent<Collider>().enabled;
                while (!opened && to < 3f) { opened = gN.Hold(attF, Time.deltaTime); to += Time.deltaTime; yield return null; }
                yield return new WaitForSeconds(Shutter.GateMoveTime + .25f);
                passes = gN.open && !gN.GetComponent<Collider>().enabled;
                gN.SetOpen(false); yield return new WaitForSeconds(Shutter.GateMoveTime + .25f);
                Destructible hc = null; foreach (var d in ar.destructibles) if (d != null && d.name.Contains("墙洞") && d.transform.position.z > 0) hc = d;
                holeBefore = PathLen(new Vector3(-1.5f, 0, 47), new Vector3(8, 0, 47.5f));
                if (hc != null) hc.TakeDamage(9999, attF, true);
                yield return new WaitForSeconds(.5f);
                holeLen = PathLen(new Vector3(-1.5f, 0, 47), new Vector3(8, 0, 47.5f));
                if (hc != null) hc.Restore();
                gN.ResetRound(); yield return new WaitForSeconds(.3f);
            }
            Check("M43", "蒸笼门：开局开着（南北都是）；攻方不能关；守方长按 1.5–2.5 s 盖上（有碰撞）；攻方长按 1.0–1.5 s 掀开；关门后打碎墙洞木箱仍可钻过",
                bothOpen && attCantClose && closed && tc >= 1.5f && tc <= 2.5f && gBlocks && opened && to >= 1f && to <= 1.5f && passes && holeLen > 0 && holeLen < 15f && (holeBefore < 0 || holeBefore > holeLen + 10f),
                "开局开着=" + bothOpen + "，攻方不能关=" + attCantClose + "，守方盖上用时 " + tc.ToString("0.00") + " s（碰撞=" + gBlocks + "），攻方掀开用时 " + to.ToString("0.00") + " s（通行=" + passes + "），关门时墙洞路线 " + (holeBefore < 0 ? "不通" : holeBefore.ToString("0") + " m") + " → 打碎木箱后 " + holeLen.ToString("0") + " m");

            // ---------------- M44：奶油平台能上（坡道 + 楼梯 + AI 实走）；奶油平台 / 外卖阳台 / 市场楼顶 / 空中连廊 / 汉堡平台都有上去和下来的路
            Vector3 deck = new Vector3(20, T, 0);
            float creamUpW = PathLen(new Vector3(7, 0, 2.4f), deck), creamUpE = PathLen(new Vector3(27, 0, 9), deck), creamDown = PathLen(deck, new Vector3(20, 0, -8.5f));
            float balUp = PathLen(new Vector3(-2, 0, 15), new Vector3(-2, T, 28)), balDown = PathLen(new Vector3(-2, T, 28), new Vector3(-2, 0, 34));
            float roofUp = PathLen(new Vector3(50, 0, 10), new Vector3(37, Arena.RoofTop, 0)), roofDown = PathLen(new Vector3(37, Arena.RoofTop, 0), new Vector3(50, 0, -4));
            float bridgeDown = PathLen(new Vector3(-8, 8.9f, 0), new Vector3(-3, 0, 6)); int bridgePads = 0;
            foreach (var jp in FindObjectsOfType<JumpPad>()) if (jp.worldLaunch && jp.name.Contains("空中连廊")) bridgePads++;
            float burgerUp = PathLen(new Vector3(33, 0, 21.5f), new Vector3(33.5f, 2f, 26.5f)), burgerDown = PathLen(new Vector3(33.5f, 2f, 26.5f), new Vector3(33, 0, 31));
            var climber = Npc(def, 0); float topY = 0, walkT = 0;
            if (climber != null)
            {
                var cb = climber.GetComponent<Bot>(); Place(climber, new Vector3(6, .05f, 4.5f), 90); yield return new WaitForFixedUpdate();
                if (cb != null)
                {
                    cb.enabled = true; cb.forceGoal = new Vector3(19.5f, T, -1.5f);
                    while (walkT < 14f && !(climber.transform.position.y > T - .4f && Flat(climber.transform.position, cb.forceGoal.Value) < 1.6f)) { climber.hp = Config.MaxHp; walkT += Time.deltaTime; yield return null; }
                    topY = climber.transform.position.y; cb.forceGoal = null; cb.enabled = false; climber.botMove = Vector3.zero;
                }
            }
            bool hp = creamUpW > 0 && creamUpE > 0 && creamDown > 0 && balUp > 0 && balDown > 0 && roofUp > 0 && roofDown > 0 && bridgeDown > 0 && bridgePads == 2 && burgerUp > 0 && burgerDown > 0 && ar.jumpLinks.Count >= 6;
            Check("M44", "奶油平台：西坡道 / 东楼梯都能上，AI 实走能上台面；奶油平台、外卖阳台、市场楼顶、空中连廊、汉堡平台都有\"上去\"和\"下来\"两条路（NavMesh / 导航连接 / 弹射板）",
                hp && topY > T - .4f,
                "奶油平台 西坡 " + creamUpW.ToString("0") + " / 东梯 " + creamUpE.ToString("0") + " / 下 " + creamDown.ToString("0") + " m，AI " + walkT.ToString("0.0") + " s 走到 " + topY.ToString("0.0") + " m；阳台 上 " + balUp.ToString("0") + " / 下 " + balDown.ToString("0")
                + "；楼顶 上 " + roofUp.ToString("0") + " / 下 " + roofDown.ToString("0") + "；连廊 弹射板 " + bridgePads + " / 跳下 " + bridgeDown.ToString("0") + "；汉堡 上 " + burgerUp.ToString("0") + " / 下 " + burgerDown.ToString("0") + "；跳下连接 " + ar.jumpLinks.Count + " 条（-1 = 不通）");

            // ---------------- M45：AI 实走 —— 一个攻方从主街经北烤串巷 → 北关口 → A，一个经南后厨 → 南关口 → B
            var w1 = Npc(att, 0); var w2 = Npc(att, 1);
            bool viaN = false, viaS = false, atA = false, atB = false; float wt = 0; int i1 = 0, i2 = 0;
            if (w1 != null && w2 != null)
            {
                var b1 = w1.GetComponent<Bot>(); var b2 = w2.GetComponent<Bot>();
                Place(w1, new Vector3(-9, .05f, 6.5f), 90); Place(w2, new Vector3(-9, .05f, -6.5f), 90); yield return new WaitForFixedUpdate();
                var g1 = new[] { new Vector3(-2, 0, 20), new Vector3(-1.5f, 0, 33), new Vector3(7.5f, 0, 41.5f), Arena.SiteA };
                var g2 = new[] { new Vector3(-2, 0, -20), new Vector3(-1.5f, 0, -33), new Vector3(7.5f, 0, -41.5f), Arena.SiteB };
                b1.enabled = b2.enabled = true;
                while (wt < 70f && (i1 < g1.Length || i2 < g2.Length))
                {
                    if (i1 < g1.Length) { b1.forceGoal = g1[i1]; if (Flat(w1.transform.position, g1[i1]) < (i1 == g1.Length - 1 ? Config.SiteRadius - .5f : 2.5f)) i1++; }
                    if (i2 < g2.Length) { b2.forceGoal = g2[i2]; if (Flat(w2.transform.position, g2[i2]) < (i2 == g2.Length - 1 ? Config.SiteRadius - .5f : 2.5f)) i2++; }
                    viaN |= Match.InLink(w1.transform.position); viaS |= Match.InLink(w2.transform.position);
                    w1.hp = w2.hp = Config.MaxHp; wt += Time.deltaTime; yield return null;
                }
                atA = Arena.SiteAt(w1.transform.position) == 0; atB = Arena.SiteAt(w2.transform.position) == 1;
                b1.forceGoal = b2.forceGoal = null; b1.enabled = b2.enabled = false; w1.botMove = w2.botMove = Vector3.zero;
            }
            Check("M45", "AI 能从两条连接巷走到两个包点：主街 → 北烤串巷 → 北关口（蒸笼门开着）→ A；主街 → 南后厨 → 南关口 → B（实际行走，不是只算路径）",
                viaN && viaS && atA && atB,
                "北：走巷=" + viaN + " 到 A=" + atA + "（路点 " + i1 + "/4）；南：走巷=" + viaS + " 到 B=" + atB + "（路点 " + i2 + "/4）；用时 " + wt.ToString("0.0") + " s");
            m.roundClock = Config.RoundTime; m.phase = Phase.Live;
        }

        // ================================================================ V22 第三期：干员 × 地图联动（设计稿_V22_地图重构与干员地图联动 §4 / §6.1，M46–M54）
        // M46 可乐弹射 · M47 嵌入补洞 · M48 果冻门帘 · M49 芥末辣锅 · M50 开墙 · M51 棉花糖蒸汽 · M52 联动可读 · M53 团队标记 · M54 敌方大招预警
        IEnumerator WaitGrounded(Fighter f, float max) { yield return new WaitForSeconds(.25f); float w = 0; while (!f.grounded && w < max) { w += Time.deltaTime; yield return null; } }
        IEnumerator V22LinkTests()
        {
            var m = Match.I; var ar = m.arena; m.phase = Phase.Live; m.roundClock = Config.RoundTime;
            int att = m.AttackingTeam, def = m.DefendingTeam;
            var p = m.player;
            if (p == null) { Check("M46", "联动测试", false, "没有玩家角色"); yield break; }
            var savedRoles = new Dictionary<Fighter, Role>(); foreach (var f in m.fighters) savedRoles[f] = f.role;
            foreach (var sh in ar.shutters) sh.ResetRound();
            foreach (var w in ar.breakWalls) w.ResetRound();
            ClearFire(); Skills.ClearRound(); SteamCloud.ClearTimed(); FoodTips.AutoEnabled = false;
            foreach (var f in m.fighters) Hush(f);
            yield return new WaitForSeconds(.3f);

            // ---------------- M46：可乐弹射（冲刺中踩弹射板 → 水平距离 × DashPadBoost；不冲刺正常弹）
            p.Respawn(new Vector3(-66, .05f, 38.5f), 90); p.scripted = true; Hush(p);
            if (p.role != Role.Falcon) p.ChangeRole(Role.Falcon);
            var padGo = new GameObject("测试弹射板"); padGo.transform.position = new Vector3(-61, 0, 38.5f); padGo.transform.rotation = Quaternion.Euler(0, 90, 0);
            var tp = padGo.AddComponent<JumpPad>(); tp.Build(new Color(1f, .8f, .3f)); tp.launch = new Vector3(0, 9f, 5f);
            yield return null;
            Place(p, padGo.transform.position + Vector3.up * .05f, 90);
            float t0 = JumpPad.LastLaunchAt, w0 = 0;
            while (JumpPad.LastLaunchAt == t0 && w0 < 1.5f) { w0 += Time.deltaTime; yield return null; }
            Vector3 nFrom = JumpPad.LastLaunchPos; bool nLaunched = JumpPad.LastLaunchAt != t0, nCola = JumpPad.LastWasCola;
            yield return WaitGrounded(p, 3f);
            float normalD = Flat(p.transform.position, nFrom);
            yield return new WaitForSeconds(1.1f);
            int cola0 = Links.Total[0];
            Place(p, new Vector3(-65.5f, .05f, 38.5f), 90); yield return new WaitForFixedUpdate(); yield return null; yield return null;
            p.skillCharges = 2; t0 = JumpPad.LastLaunchAt; w0 = 0;
            bool dashCast = Skills.Cast(p, p.transform.position + Vector3.right * 7);
            while (JumpPad.LastLaunchAt == t0 && w0 < 1.5f) { w0 += Time.deltaTime; yield return null; }
            Vector3 cFrom = JumpPad.LastLaunchPos; bool cCola = JumpPad.LastWasCola && JumpPad.LastLaunchAt != t0;
            yield return WaitGrounded(p, 3f);
            float colaD = Flat(p.transform.position, cFrom), ratio = normalD > .1f ? colaD / normalD : 0;
            Destroy(padGo);
            Check("M46", "可乐弹射：汽水冲刺中踩弹射板 → 沿冲刺方向弹出，水平距离 = 普通弹射 × " + Config.DashPadBoost + " ± 10%；不冲刺时正常弹射、不触发联动",
                nLaunched && !nCola && dashCast && cCola && Links.Total[0] == cola0 + 1 && normalD > 2f && Mathf.Abs(ratio - Config.DashPadBoost) <= Config.DashPadBoost * .1f,
                "普通弹射 " + normalD.ToString("0.0") + " m（联动=" + nCola + "）；冲刺踩板 " + colaD.ToString("0.0") + " m（联动=" + cCola + "）；倍率 " + ratio.ToString("0.00") + "（目标 " + Config.DashPadBoost + "）；触发计数 +" + (Links.Total[0] - cola0));

            // ---------------- M47：嵌入补洞（开局完整的墙不能吸附；原有入口仍不能封；破口 2 m 内吸附变金；补上后 NavMesh 不通；同一破口不能再补；能被打掉；30 s 后消失）
            var rv = Npc(def, 0); Hush(rv); if (rv.role != Role.Rivet) rv.ChangeRole(Role.Rivet);
            Destructible hc = null; foreach (var d in ar.destructibles) if (d != null && d.name.Contains("墙洞") && d.transform.position.z > 0) hc = d;
            var gN = ar.Gate(1);
            Place(rv, new Vector3(37, .05f, 14.5f), 0); yield return null;
            bool intactNo = !Links.PatchTarget(rv, new Vector3(37, 0, 17.6f), out _, out _);
            Place(rv, new Vector3(20, .05f, 10.5f), 0); yield return null;
            bool entranceNo = !Skills.CoverPlacementAt(rv, new Vector3(20, 0, 13.2f), out _, out _);
            bool snapGold = false, cast47 = false, blocks47 = false, busy47 = false, breakable = false, expired = false; float before47 = -1, after47 = -1, life47 = 0; string kind47 = "-";
            if (hc != null && gN != null)
            {
                gN.SetOpen(false); hc.Restore(); yield return new WaitForSeconds(Shutter.GateMoveTime + .3f);
                hc.TakeDamage(9999, null, true); yield return new WaitForSeconds(.5f);
                before47 = PathLen(new Vector3(-1.5f, 0, 47), new Vector3(8, 0, 47.5f));
                Place(rv, new Vector3(1.4f, .05f, 47f), 90); rv.pitch = 20; yield return null; yield return null;
                var aim = Links.AimFor(rv); snapGold = aim.any && aim.ok && aim.kind == LinkKind.WaferPatch; kind47 = aim.text;
                rv.skillCharges = 2; cast47 = Skills.Cast(rv, Skills.CoverDesired(rv));
                DeployedCover patch = null; foreach (var c in DeployedCover.Live) if (c != null && c.patched && c.owner == rv) patch = c;
                yield return new WaitForSeconds(Config.CoverPatchDeploy + .6f);
                after47 = PathLen(new Vector3(-1.5f, 0, 47), new Vector3(8, 0, 47.5f));
                blocks47 = before47 > 0 && (after47 < 0 || after47 > before47 + 8f);
                Links.PatchTarget(rv, new Vector3(4, 0, 47), out _, out busy47);
                if (patch != null) { life47 = patch.life; patch.TakeDamage(patch.MaxHp + 1, Npc(att, 0), false); }
                yield return null; yield return new WaitForSeconds(.5f);
                breakable = patch == null && PathLen(new Vector3(-1.5f, 0, 47), new Vector3(8, 0, 47.5f)) > 0;
                rv.skillCharges = 2; Skills.Cast(rv, new Vector3(4, 0, 47));
                DeployedCover patch2 = null; foreach (var c in DeployedCover.Live) if (c != null && c.patched && c.owner == rv) patch2 = c;
                Time.timeScale = 10f; yield return new WaitForSeconds(Config.CoverPatchDeploy + Config.CoverPatchLife + .5f); Time.timeScale = 1f;
                expired = patch2 == null;
                hc.Restore(); gN.ResetRound(); yield return new WaitForSeconds(.3f);
            }
            Check("M47", "嵌入补洞：开局完整的蛋糕墙不能吸附；原有入口（A 连廊）仍不能封；墙洞被打开后预览 2 m 内吸附变金；补上后从破口走不通；同一破口不能再补（先到先得）；能被打掉；" + Config.CoverPatchLife + " s 后自己消失",
                intactNo && entranceNo && snapGold && cast47 && blocks47 && busy47 && breakable && expired && Mathf.Approximately(life47, Config.CoverPatchLife),
                "完整墙不能吸附=" + intactNo + "，入口不能封=" + entranceNo + "，吸附变金=" + snapGold + "（" + kind47 + "），部署=" + cast47 + "，破口路线 " + before47.ToString("0") + " m → 补上后 " + (after47 < 0 ? "不通" : after47.ToString("0") + " m") + "，再补=占用 " + busy47 + "，打掉后又通=" + breakable + "，存在 " + life47 + " s 后消失=" + expired);

            // ---------------- M48：果冻门帘（A 连廊门框吸附；铁幕走开后还在；子弹被挡；人能穿过且移速 × ShieldDockPassSlow）
            var iw = Npc(att, 0); Hush(iw); if (iw.role != Role.Ironwall) iw.ChangeRole(Role.Ironwall);
            Shutter aSh = null; foreach (var sh in ar.shutters) if (sh != null && sh.name.Contains("A 连廊")) aSh = sh;
            bool found48 = false, docked48 = false, notHeld = false, stays = false, blocked48 = false, passed = false, closedNo = false; float vIn = 0, vOut = 0, hp48 = 0; int dockLinks0 = Links.Total[2];
            if (aSh != null)
            {
                aSh.ResetRound(); yield return null;
                Place(iw, new Vector3(20, .05f, 10.5f), 0); yield return null;
                closedNo = Links.DockFrame(iw, out _) == null;   // 卷帘门关着时不能挂
                aSh.SetOpen(true); yield return new WaitForSeconds(2.1f);
                var fr = Links.DockFrame(iw, out _); found48 = fr != null;
                iw.skillCharges = 1; Skills.Cast(iw, iw.transform.position + Vector3.forward * 5);
                var cur = fr != null ? Links.CurtainOf(fr) : null; docked48 = cur != null && cur.docked; notHeld = !iw.ShieldUp;
                Place(iw, new Vector3(6, .05f, 4), 0); yield return new WaitForSeconds(.3f);
                stays = cur != null;
                var shooter = Npc(def, 0); var walker = Npc(att, 1); Hush(shooter); Hush(walker);
                Place(shooter, new Vector3(20, .05f, 9.5f), 0); Place(walker, new Vector3(20, .05f, 19.5f), 180); walker.hp = 100; walker.armor = 50;
                yield return new WaitForFixedUpdate(); yield return null;
                if (cur != null)
                {
                    hp48 = cur.Hp;
                    Bullet.Fire(shooter, shooter.Eye, shooter.Eye, (walker.Chest - shooter.Eye).normalized, Color.white);
                    yield return new WaitForSeconds(.45f);
                    blocked48 = walker.hp >= 100 && walker.armor >= 50 && cur != null && cur.Hp < hp48;
                    Place(walker, new Vector3(20, .05f, 18f), 180); yield return null;
                    walker.botMove = Vector3.back;
                    float sIn = 0, tIn = 0, sOut = 0, tOut = 0, wt = 0; Vector3 last = walker.transform.position; bool wasIn = false; float enteredAt = -1;
                    while (wt < 3.5f && walker.transform.position.z > 11.5f)
                    {
                        yield return null; wt += Time.deltaTime;
                        Vector3 now = walker.transform.position; float step = Flat(now, last); last = now;
                        bool inC = ShieldWall.CurtainAt(now) != null;
                        if (inC && !wasIn) enteredAt = wt;
                        wasIn = inC;
                        if (inC && wt - enteredAt > .12f) { sIn += step; tIn += Time.deltaTime; }
                        else if (!inC && enteredAt < 0 && wt > .25f) { sOut += step; tOut += Time.deltaTime; }
                    }
                    walker.botMove = Vector3.zero;
                    vIn = tIn > 0 ? sIn / tIn : 0; vOut = tOut > 0 ? sOut / tOut : 0; passed = walker.transform.position.z < 13f;
                    if (cur != null) Destroy(cur.gameObject);
                }
                aSh.ResetRound(); yield return new WaitForSeconds(.2f);
            }
            float slowK = vOut > 0 ? vIn / vOut : 0;
            Check("M48", "果冻门帘：卷帘门关着不能挂；A 连廊门框开着时吸附成功（不用举着）；铁幕走开后门帘还在；子弹被挡（门帘掉血、后面的人没事）；人能穿过且穿过时移速 × " + Config.ShieldDockPassSlow,
                closedNo && found48 && docked48 && notHeld && stays && blocked48 && passed && Mathf.Abs(slowK - Config.ShieldDockPassSlow) <= .12f && Links.Total[2] == dockLinks0 + 1,
                "关门不能挂=" + closedNo + "，吸附=" + found48 + "/" + docked48 + "，不用举着=" + notHeld + "，走开后还在=" + stays + "，挡子弹=" + blocked48 + "（门帘 " + hp48.ToString("0") + "）"
                + "，穿过=" + passed + "，门帘里 " + vIn.ToString("0.00") + " m/s / 门帘外 " + vOut.ToString("0.00") + " m/s = ×" + slowK.ToString("0.00"));

            // ---------------- M49：芥末辣锅（签子射进关东煮锅 → 6 m 内、隔着热气的敌人持续透视；迷雾罩住时不刷新；6 s 后结束）
            Skills.ClearRound(); yield return null;
            HotpotHazard potN = null; foreach (var h in HotpotHazard.All) if (h != null && h.transform.position.z > 0) potN = h;
            var raven = Npc(att, 1); Hush(raven); if (raven.role != Role.Raven) raven.ChangeRole(Role.Raven);
            var foe = Npc(def, 1); Hush(foe); var foeMate = Npc(def, 0); Hush(foeMate);
            bool pot49 = false, rev1 = false, rev2 = false, hidden = false, sugarNo = false, ended = false; string where49 = "-"; int potLinks0 = Links.Total[3];
            if (potN != null)
            {
                Vector3 pc = potN.transform.position;
                Place(raven, new Vector3(pc.x, .05f, 48.6f), 180); Place(foe, pc + new Vector3(0, .05f, -3.4f), 0); Place(foeMate, new Vector3(-30, .05f, 30), 0);
                foe.hp = 100; foe.revealedUntil = 0; yield return new WaitForFixedUpdate(); yield return null;
                hidden = !Skills.SightClear(raven.Eye, foe.Chest, foe) && !Skills.SightClear(raven.Eye, foe.Head, foe);
                Skills.ShootArrow(raven, Skills.ArrowVelocityTo(raven.Eye, pc + Vector3.up * .55f));
                yield return new WaitForSeconds(.5f);
                var dr = Links.PotDrone(potN); pot49 = dr != null;
                if (!pot49) foreach (var d in Drone.Live) if (d != null && d.owner == raven) where49 = d.transform.position.ToString("F1");
                yield return new WaitForSeconds(1f); rev1 = foe.Revealed;
                yield return new WaitForSeconds(1.6f); rev2 = foe.Revealed && foe.revealedUntil - Time.time < Config.MustardPotLinger + .5f;   // 持续刷新（不是一次性 5 s）
                var ng = new GameObject("测试迷雾"); ng.transform.position = foe.transform.position; ng.AddComponent<NoiseField>().Init(foeMate); Skills.spawned.Add(ng);
                yield return new WaitForSeconds(Config.MustardPotLinger + .6f);
                sugarNo = foe.revealedUntil < Time.time;
                Destroy(ng);
                yield return new WaitForSeconds(Mathf.Max(.2f, Config.MustardPotLife - 4.4f));
                ended = Links.PotDrone(potN) == null;
                Place(foe, new Vector3(-30, .05f, 34), 0);
            }
            Check("M49", "芥末辣锅：芥末签射进关东煮锅 → 不扫描，锅 " + Config.MustardPotRadius + " m 内、隔着热气的敌人持续透视（不需要视线）；棉花糖迷雾罩住时不刷新；" + Config.MustardPotLife + " s 后结束",
                pot49 && hidden && rev1 && rev2 && sugarNo && ended && Links.Total[3] == potLinks0 + 1,
                "进锅=" + pot49 + (pot49 ? "" : "（签子落在 " + where49 + "）") + "，敌人被热气挡住视线=" + hidden + "，1.5 s 时透视=" + rev1 + "，3.1 s 时仍在刷新=" + rev2 + "，迷雾里不刷新=" + sugarNo + "，到时结束=" + ended);

            // ---------------- M50：开墙（纸杯粘在蛋糕墙 2.9 m 高处：不联动时炸不到地面、走不过去；联动时落点正下方开出门洞、NavMesh 能过；破口能被铆钉补）
            var ember = NpcOr(att, 2); Hush(ember); if (ember.role != Role.Ember) ember.ChangeRole(Role.Ember);
            BreakWall wA = null; foreach (var w in ar.breakWalls) if (w != null && w.transform.position.z > 0) wA = w;
            bool baseNo = false, link50 = false, rivetCan = false; float pathBefore = -1, pathAfter = -1, holeW = 0, holeH = 0; int brLinks0 = Links.Total[4];
            if (wA != null)
            {
                Place(ember, new Vector3(37, .05f, 11f), 0); yield return null;
                wA.ResetRound(); yield return new WaitForSeconds(.3f);
                pathBefore = PathLen(new Vector3(37, 0, 15f), new Vector3(37, 0, 21.5f));
                Vector3 face = wA.transform.TransformPoint(new Vector3(0, 2.9f, -.2f));
                new GameObject("测试纸杯（不粘墙）").AddComponent<BlastCharge>().Init(ember, face, null);
                yield return new WaitForSeconds(Config.BlastWarning + .4f);
                baseNo = wA.Opening() == null;
                ClearFire(); wA.ResetRound(); yield return new WaitForSeconds(.3f);
                WallPiece wp = null; float bd = 9f; foreach (var q in wA.pieces) { float dd = Vector3.Distance(q.transform.position, face); if (dd < bd) { bd = dd; wp = q; } }
                new GameObject("测试纸杯（粘墙）").AddComponent<BlastCharge>().Init(ember, face, wp);
                yield return new WaitForSeconds(Config.BlastWarning + .6f);
                var hole = wA.Opening(); link50 = hole != null; if (hole != null) { holeW = hole.width; holeH = hole.height; }
                pathAfter = PathLen(new Vector3(37, 0, 15f), new Vector3(37, 0, 21.5f));
                Place(rv, new Vector3(37, .05f, 15f), 0); rv.pitch = 20; yield return null;
                rivetCan = Links.PatchTarget(rv, hole != null ? hole.center : face, out _, out bool b50) && !b50;
                ClearFire(); wA.ResetRound(); yield return new WaitForSeconds(.3f);
            }
            Check("M50", "开墙：纸杯粘在蛋糕墙 2.9 m 高处 —— 不联动（原规则 2.3 m 炸碎）炸不到地面、没有能过人的洞；联动时落点正下方开出 ≥ 2.4 × 2.0 m 的门洞，NavMesh 能直接穿过；这个破口铆钉能补（不会无解）",
                baseNo && link50 && holeW >= 2.4f && holeH >= 2f && pathAfter > 0 && pathAfter < 9f && (pathBefore < 0 || pathBefore > pathAfter + 2f) && rivetCan && Links.Total[4] == brLinks0 + 1,
                "不联动没洞=" + baseNo + "，联动开洞=" + link50 + "（" + holeW.ToString("0.0") + " × " + holeH.ToString("0.0") + " m）、墙两侧路线 " + (pathBefore < 0 ? "不通" : pathBefore.ToString("0") + " m") + " → " + pathAfter.ToString("0.0") + " m，铆钉能补=" + rivetCan);

            // ---------------- M51：棉花糖蒸汽（V23 d：迷雾罩住火锅热气 → 整片粉色棉花糖蒸汽，半径 = 整片蒸汽；罩住正在喷的阀门 → 原地变粉；没有蒸汽的迷雾不变；冰淇淋散热气后一起散）
            Skills.ClearRound(); SteamCloud.ClearTimed(); yield return null;
            SteamValve valve = null; foreach (var v in SteamValve.Valves) if (v != null && !(v is HotpotKnob) && Flat(v.transform.position, new Vector3(16, 0, 8)) < 1f) valve = v;
            var ph = NpcOr(att, 2); if (ph.role != Role.Phantom) ph.ChangeRole(Role.Phantom);
            bool before51 = true, candy51 = false, blocks51 = false, farNo = false, lifeOk = false, dispersed = false, valvePink = false, radiusOk = false; float r51 = 0; int steamLinks0 = Links.Total[5];
            if (potN != null && valve != null)
            {
                Vector3 c = potN.SteamCenter; Vector3 la = c + new Vector3(-9, 0, 6f), lb = c + new Vector3(9, 0, 6f);   // 离中心 6 m：火锅常驻热气（2.6 m）挡不住，整片棉花糖蒸汽（9 m）挡得住
                before51 = SteamCloud.Blocks(la, lb);
                int live0 = CandySteam.Live.Count;
                var far = new GameObject("测试迷雾·远"); far.transform.position = new Vector3(-40, 0, -4); var nf = far.AddComponent<NoiseField>(); nf.Init(ph); Skills.spawned.Add(far);
                yield return new WaitForSeconds(.5f);
                farNo = CandySteam.Live.Count == live0 && Links.Total[5] == steamLinks0;
                var near = new GameObject("测试迷雾·锅"); near.transform.position = potN.transform.position + new Vector3(0, 0, -4f); var nn = near.AddComponent<NoiseField>(); nn.Init(ph); Skills.spawned.Add(near);
                yield return new WaitForSeconds(1.4f);
                CandySteam cs = null; foreach (var x in CandySteam.Live) if (x != null && x.key == potN) cs = x;
                candy51 = cs != null && cs.area != null && cs.area.Candy; blocks51 = SteamCloud.Blocks(la, lb);
                if (cs != null) { r51 = cs.area.R; radiusOk = Mathf.Approximately(r51, Config.SteamAreaPot) && Config.CandySteamRadius >= Config.SteamAreaMid - .01f; lifeOk = cs.until <= nn.EndsAt + .01f && cs.until - Time.time <= Config.CandySteamLife; }
                // 正在喷的中庭阀门被迷雾罩住 → 原地变粉（不另开一片，不占盲区上限）
                valve.ResetRound(); valve.Fire(null); int blind0 = SteamArea.BlindCount;
                var nv = new GameObject("测试迷雾·阀"); nv.transform.position = valve.transform.position + new Vector3(0, 0, -4f); nv.AddComponent<NoiseField>().Init(ph); Skills.spawned.Add(nv);
                yield return new WaitForSeconds(.6f);
                valvePink = valve.area != null && valve.area.Candy && SteamArea.BlindCount == blind0;
                SteamCloud.Suppress(.6f); yield return null; yield return null;
                yield return new WaitForSeconds(Config.SteamCloseFade + .2f); SteamCloud.Tick();
                dispersed = !CandySteam.Active(potN) && !SteamCloud.Blocks(la, lb);
                Destroy(far); Destroy(near); Destroy(nv); valve.ResetRound(); SteamCloud.ClearTimed();
                yield return new WaitForSeconds(.7f); SteamCloud.Tick();
            }
            Check("M51", "棉花糖蒸汽（V23 d 新数值）：迷雾罩住火锅热气 → 整片粉色棉花糖蒸汽，半径 = 整片蒸汽 " + Config.SteamAreaPot + " m，离中心 6 m 的视线被挡（常驻热气挡不住这条线）；罩住正在喷的阀门 → 原地变粉、不多占一片；没罩住蒸汽的迷雾不变；持续 ≤ " + Config.CandySteamLife + " s 且不超过迷雾；冰淇淋散热气后一起散",
                !before51 && farNo && candy51 && blocks51 && radiusOk && lifeOk && valvePink && dispersed && Links.Total[5] >= steamLinks0 + 1,
                "联动前这条线被挡=" + before51 + "，远处迷雾不变=" + farNo + "，棉花糖蒸汽=" + candy51 + "（半径 " + r51.ToString("0.0") + " m）、挡视线=" + blocks51 + "，时长合规=" + lifeOk + "，阀门原地变粉=" + valvePink + "，冰淇淋后散开=" + dispersed);

            // ---------------- M52：联动可读（只给能联动的英雄显示）
            bool tableOk = true, lenOk = true; string tbl = "";
            foreach (Role r in System.Enum.GetValues(typeof(Role)))
                for (int k = 0; k < 6; k++)
                {
                    string line = Links.Bubble(r, (LinkKind)k);
                    bool should = Links.For(r) == (LinkKind)k;
                    if ((line != null) != should) { tableOk = false; tbl += r + "/" + (LinkKind)k + " "; }
                    if (line != null && line.Length > FoodGuide.BubbleMax) { lenOk = false; tbl += "长:" + line + " "; }
                }
            bool helpOk = true; foreach (Role r in System.Enum.GetValues(typeof(Role))) { string ht = GameUI.HeroLinkText(r); if (string.IsNullOrEmpty(ht) || !ht.Contains(Links.Name(Links.For(r)))) helpOk = false; }
            string potLabelRaven = null, potLabelFalcon = "-"; bool aimRaven = false, aimFalcon = true, tipOk = false;
            if (potN != null && ToonAssets.Available)
            {
                p.ChangeRole(Role.Raven); p.skillCharges = 2;
                Vector3 pc = potN.transform.position;
                p.Respawn(new Vector3(pc.x, .05f, 48.6f), 180); p.scripted = true; Hush(p);
                Vector3 dir = (pc + Vector3.up * .55f) - p.Eye; p.yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg; p.pitch = -Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg;
                yield return null; yield return null; yield return null;
                potLabelRaven = InteractMarkers.I != null ? InteractMarkers.I.LabelOf(potN) : null;
                var ar52 = Links.AimFor(p); aimRaven = ar52.any && ar52.ok && ar52.kind == LinkKind.MustardPot;
                tipOk = !string.IsNullOrEmpty(FoodTips.Body("link_self")) && FoodTips.Body("link_self").Contains("芥末辣锅");
                p.ChangeRole(Role.Falcon); p.skillCharges = 2; yield return null; yield return null; yield return null;
                potLabelFalcon = InteractMarkers.I != null ? InteractMarkers.I.LabelOf(potN) : null;
                aimFalcon = Links.AimFor(p).any;
            }
            Check("M52", "联动可读：6 个英雄 × 6 类元素，只有对应英雄有联动行（≤ 18 字）；夜鸦站在关东煮锅旁看到金色气泡、瞄锅时技能图标变金；换成猎隼就看不到；H 页 6 个英雄都有联动说明；首次提示卡有内容",
                tableOk && lenOk && helpOk && potLabelRaven != null && potLabelRaven.Contains("芥末辣锅") && aimRaven && potLabelFalcon == null && !aimFalcon && tipOk,
                "对应表=" + tableOk + (tbl != "" ? "（" + tbl + "）" : "") + "，字数=" + lenOk + "，H 页=" + helpOk + "，夜鸦气泡=" + (potLabelRaven ?? "无") + "，夜鸦瞄锅变金=" + aimRaven + "，猎隼气泡=" + (potLabelFalcon ?? "无") + "，猎隼瞄锅变金=" + aimFalcon + "，提示卡=" + tipOk);

            // ---------------- M53：团队标记（这里 → 最近的 AI 队友走过去；看着火锅按中键 = 这儿能联动；看着敌人 = 敌人；都记进遥测）
            int pings0 = Pings.Total; bool here53 = false, moved = false, looked = false, link53 = false, enemy53 = false; string linkLine = "-"; float d53 = -1;
            {
                var mate = Npc(p.team, 0); var mate2 = Npc(p.team, 1);
                if (mate != null && mate2 != null)
                {
                    Hush(mate); Hush(mate2);
                    p.Respawn(new Vector3(-56, .05f, 4), 90); p.scripted = true; Hush(p);
                    Place(mate, new Vector3(-53, .05f, 2.5f), 90); Place(mate2, new Vector3(-55, .05f, -2f), 90);
                    var mb = mate.GetComponent<Bot>(); var mb2 = mate2.GetComponent<Bot>();
                    yield return new WaitForFixedUpdate(); yield return null;
                    if (mb != null) mb.enabled = true;
                    var pg = Pings.Create(p, PingKind.Here, new Vector3(-43, 0, 3.5f));
                    here53 = pg != null && pg.responder == mate && mb != null && mb.HasPingGoal;
                    looked = mb2 != null && mb2.Looking;
                    float wt = 0; while (wt < 5f && Flat(mate.transform.position, new Vector3(-43, 0, 3.5f)) > 3f) { mate.hp = Config.MaxHp; wt += Time.deltaTime; yield return null; }
                    d53 = Flat(mate.transform.position, new Vector3(-43, 0, 3.5f)); moved = d53 <= 3f;
                    if (mb != null) mb.enabled = false; Hush(mate);
                }
                if (potN != null)
                {
                    Vector3 pc = potN.transform.position;
                    p.Respawn(new Vector3(pc.x, .05f, 48.6f), 180); p.scripted = true; Hush(p);
                    Vector3 dir = (pc + Vector3.up * .55f) - p.Eye; p.yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg; p.pitch = -Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg;
                    yield return new WaitForSeconds(.7f);
                    var lp = Pings.FromAim(p); link53 = lp != null && lp.kind == PingKind.Link && lp.link == LinkKind.MustardPot; linkLine = lp != null ? lp.line : "-";
                    var foe2 = Npc(p.team == att ? def : att, 0); Hush(foe2); p.yaw = -90; p.pitch = 0; Place(foe2, p.transform.position + Vector3.left * 5f, 90);
                    yield return new WaitForSeconds(.7f);
                    var ep = Pings.FromAim(p); enemy53 = ep != null && ep.kind == PingKind.Enemy && ep.target == foe2;
                }
            }
            Check("M53", "团队标记：中键\"这里\" → 最近的 AI 队友走过去、其他队友看过去；看着火锅 = \"这儿能联动\"；看着敌人 = \"敌人\"；每次都记进遥测 pings",
                here53 && moved && looked && link53 && enemy53 && Pings.Total == pings0 + 3,
                "这里：响应者是最近的队友=" + here53 + "，走到 " + d53.ToString("0.0") + " m 内=" + moved + "，另一个队友看过去=" + looked + "；联动标记=" + link53 + "（" + linkLine + "）；敌人标记=" + enemy53 + "；pings +" + (Pings.Total - pings0));

            // ---------------- M54：敌方大招预警（横幅 + 全图专属音 + 状态条 + 蜡烛轰炸落点圈；生效时间不变；队友放大招不预警）
            if (!ToonAssets.Available) Check("M54", "敌方大招预警", true, "写实模式没有大招，跳过");
            else
            {
                UltWarn.Clear(); m.ui.ClearBanner(); foreach (var cr in CandleRing.Live.ToArray()) if (cr != null) Destroy(cr.gameObject);
                p.Respawn(new Vector3(-44, .05f, 4), 90); p.scripted = true; Hush(p); p.hp = Config.MaxHp; p.armor = Config.MaxArmor;
                var foeE = Npc(p.team == att ? def : att, 0); Hush(foeE); if (foeE.role != Role.Ember) foeE.ChangeRole(Role.Ember);
                Place(foeE, new Vector3(-30, .05f, 4), -90); yield return null;
                Vector3 dir = p.transform.position - foeE.Eye; foeE.yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg; foeE.pitch = -Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg;
                int warned0 = UltWarn.Warned; foeE.Food.ult = 1f; bool fired = foeE.Food.TryUlt();
                CandleBarrage barrage = Object.FindObjectOfType<CandleBarrage>();
                string bannerText = m.ui.BannerText; bool banner = bannerText.Contains(FoodState.UltName(Role.Ember)) && bannerText.Contains("敌方大招");
                bool sound = UltWarn.LastSound == "food_confetti", ring = CandleRing.Live.Count > 0 && CandleRing.Covers(p.transform.position);
                bool status = false; foreach (var su in UltWarn.Statuses) if (su.text.Contains("蜡烛") && (su.live == null || su.live())) status = true;
                yield return new WaitForSeconds(.2f); bool notYet = barrage != null && barrage.Dropped == 0;
                yield return new WaitForSeconds(.25f); bool dropped = barrage == null || barrage.Dropped >= 1;
                yield return new WaitForSeconds(2.4f);
                foeE.ChangeRole(Role.Raven); foeE.Food.ult = 1f; int st0 = UltWarn.Statuses.Count; foeE.Food.TryUlt();
                bool ravenStatus = false; foreach (var su in UltWarn.Statuses) if (su.text.Contains("回转寿司")) ravenStatus = true;
                var mateU = Npc(p.team, 0); Hush(mateU); int warned1 = UltWarn.Warned; mateU.Food.ult = 1f; mateU.Food.TryUlt();
                bool mateQuiet = UltWarn.Warned == warned1;
                Check("M54", "敌方大招预警：敌方焚芯放「生日蜡烛轰炸」→ 横幅 + 专属音 + \"快离开红圈\"状态条 + 8 m 落点圈；第一根蜡烛仍在 0.3 s 后落下（时间不变）；敌方夜鸦大招弹\"被扫到\"状态条；队友放大招不预警",
                    fired && banner && sound && ring && status && notYet && dropped && ravenStatus && mateQuiet && UltWarn.Warned == warned0 + 2,
                    "放出=" + fired + "，横幅=" + banner + "（" + bannerText + "），专属音=" + sound + "，落点圈罩住你=" + ring + "，状态条=" + status + "，0.2 s 未落=" + notYet + "，0.45 s 已落=" + dropped + "，夜鸦状态条=" + ravenStatus + "，队友不预警=" + mateQuiet);
                m.ui.ClearBanner(); UltWarn.Clear();
            }

            // ---------------- 收尾：恢复角色 / 位置 / 地图状态
            Time.timeScale = 1f; FoodTips.AutoEnabled = true;
            ClearFire(); Skills.ClearRound(); SteamCloud.ClearTimed(); Pings.Clear();
            foreach (var w in ar.breakWalls) w.ResetRound();
            foreach (var sh in ar.shutters) sh.ResetRound();
            foreach (var d in ar.destructibles) d.Restore();
            foreach (var kv in savedRoles) if (kv.Key.role != kv.Value) kv.Key.ChangeRole(kv.Value);
            p.scripted = false; Hush(p);
            foreach (var f in m.fighters) { f.Respawn(ar.SpawnPoint(f.team == m.AttackingTeam, f.slot), 0); Hush(f); }
            m.roundClock = Config.RoundTime; m.phase = Phase.Live;
            yield return null;
        }

        // ================================================================ V22 第一期：体力与 UI（设计稿_V22_3C反馈与干员体验 §6.1，M30–M35）
        Fighter NpcOr(int team, int index) { return Npc(team, index) ?? Npc(1 - team, index) ?? Npc(1 - team, 0); }   // V22 第三期：玩家所在队只有 2 个 AI
        Fighter Npc(int team, int index) { int i = 0; foreach (var f in Match.I.fighters) if (f.team == team && !f.human && i++ == index) return f; return null; }
        static void Hush(Fighter f) { var b = f.GetComponent<Bot>(); if (b != null) b.enabled = false; f.botMove = Vector3.zero; f.botSprint = f.botCrouch = f.botAim = f.botTrigger = false; }
        static void ClearFire() { foreach (var fz in Object.FindObjectsOfType<FireZone>()) Object.Destroy(fz.gameObject); }

        IEnumerator V22Tests()
        {
            var m = Match.I; m.phase = Phase.Live; m.roundClock = Config.RoundTime;
            var p = m.player;
            if (p == null) { Check("M30", "V22 状态带", false, "没有玩家角色"); yield break; }
            Vector3 spot = new Vector3(-40, .05f, 4);
            p.Respawn(spot, 90); p.scripted = true; Hush(p);
            yield return null; yield return null;

            // ---------------- M30：状态带"不用时隐藏" + 姿态图标 + 体力四态 + 趴下镜头按 ProneTransition 走
            var e0 = CombatHud.Elements(p); bool idleEmpty = e0.Count == 0;
            p.botCrouch = true; yield return new WaitForSeconds(.2f);
            bool crouchIcon = CombatHud.Elements(p).Contains("posture:crouch"); p.botCrouch = false; yield return new WaitForSeconds(.25f);
            float camStand = p.cam.transform.localPosition.y;
            p.SetProne(true); yield return null;
            bool proneRing = CombatHud.Elements(p).Contains("proneRing");
            yield return new WaitForSeconds(Tune.ProneTransition * .4f);
            float camMid = p.cam.transform.localPosition.y;
            yield return new WaitForSeconds(Tune.ProneTransition * .6f + .05f);
            float camEnd = p.cam.transform.localPosition.y;
            var eProne = CombatHud.Elements(p); bool proneIcon = eProne.Contains("posture:prone") && !eProne.Contains("proneRing");
            bool proneCam = camMid - camEnd > .2f && Mathf.Abs(camEnd - .45f) < .05f;   // 旧版 Lerp(14/秒) 在 40% 时已经只差 0.09 m
            p.SetProne(false); yield return new WaitForSeconds(Tune.ProneTransition + .15f);
            p.StartSlide(); yield return null;
            bool slideIcon = CombatHud.Elements(p).Contains("posture:slide");
            yield return new WaitForSeconds(1f);
            bool standEmpty = CombatHud.Elements(p).Count == 0;
            p.stamina = 40f; yield return null;
            bool barShown = CombatHud.Elements(p).Contains("stamina");
            p.stamina = Tune.StaminaMax; float tFull = Time.time, fadeT = -1;
            for (float w = 0; w < 3f; w += Time.deltaTime) { yield return null; if (!CombatHud.Elements(p).Contains("stamina")) { fadeT = Time.time - tFull; break; } }
            int ex0 = p.stats.exhaustCount; p.stamina = 3f; p.exhausted = false;
            p.botMove = p.transform.forward; p.botSprint = true;
            bool drainSeen = false;
            for (float w = 0; w < 1.5f && !p.exhausted; w += Time.deltaTime) { yield return null; if (CombatHud.StaminaState(p) == StaminaLook.Low || CombatHud.StaminaState(p) == StaminaLook.Draining) drainSeen = true; }
            p.botMove = Vector3.zero; p.botSprint = false; yield return null;
            bool lockShown = CombatHud.Elements(p).Contains("lock") && CombatHud.StaminaState(p) == StaminaLook.Locked;
            bool flashSeen = false; float tLock = Time.time;
            for (float w = 0; w < 4f; w += Time.deltaTime) { yield return null; if (!p.exhausted) { flashSeen = CombatHud.Elements(p).Contains("flash"); break; } }
            float lockDur = Time.time - tLock;
            bool exCount = p.stats.exhaustCount == ex0 + 1;
            Check("M30", "状态带不用时隐藏：站立 + 满体力为空；蹲 / 趴 / 滑铲出现豆子剪影；趴下切换环 + 镜头按 0.45 s 走完；体力不满出现、回满后 0.8–1.5 s 淡出；耗尽红色锁定直到 25% 刻度、过线闪一下；遥测 exhaustCount +1",
                idleEmpty && crouchIcon && proneRing && proneIcon && proneCam && slideIcon && standEmpty && barShown && fadeT >= .8f && fadeT <= 1.5f && drainSeen && lockShown && flashSeen && exCount,
                "站立空=" + idleEmpty + "(" + string.Join(",", e0.ToArray()) + ") 蹲=" + crouchIcon + " 趴=" + proneIcon + " 切换环=" + proneRing + " 滑铲=" + slideIcon + " 起身后空=" + standEmpty
                + "；镜头 站 " + camStand.ToString("0.00") + " → 40% 时 " + camMid.ToString("0.00") + " → 结束 " + camEnd.ToString("0.00") + " m =" + proneCam
                + "；体力条出现=" + barShown + " 回满淡出 " + fadeT.ToString("0.00") + " s；消耗中=" + drainSeen + " 锁定=" + lockShown + "（" + lockDur.ToString("0.00") + " s 后过线）闪光=" + flashSeen + " 耗尽计数 " + ex0 + "→" + p.stats.exhaustCount);

            // ---------------- M31：冲刺转开火（LMG-2 0.32 s）：等待中准星 ≤ 0.5、就绪那一帧 ≥ 0.9、抬枪到位时间 = sprintToFire ± 0.05 s、吞键计数
            var lmg = Config.Hero(Role.Ironwall).weapon;
            p.Equip(lmg); yield return new WaitForSeconds(lmg.drawTime + .15f);
            p.stamina = Tune.StaminaMax; p.exhausted = false;
            p.botMove = p.transform.forward; p.botSprint = true; yield return new WaitForSeconds(.6f);
            bool sprinted = p.sprinting; float poseAtSprint = p.ViewSprintPose; int sw0 = p.stats.swallowedShots;
            p.botSprint = false; p.botMove = Vector3.zero; p.botTrigger = true;
            float waitMaxA = 0f, readyA = -1f, tEnd = -1f, tPose = -1f, tReady = -1f; bool dotSeen = false;
            for (float w = 0; w < 1f; w += Time.deltaTime)
            {
                yield return null;
                if (tEnd < 0 && !p.sprinting) tEnd = p.SprintEndedAt;
                if (Time.time - p.swallowedAt < .15f) dotSeen = true;
                if (!p.CanFireNow) waitMaxA = Mathf.Max(waitMaxA, GameUI.CrossAlpha(p));
                else if (readyA < 0) { readyA = GameUI.CrossAlpha(p); tReady = Time.time; }
                if (tPose < 0 && p.ViewSprintPose <= .001f) tPose = Time.time;
                if (w > .1f) p.botTrigger = false;
            }
            p.botTrigger = false;
            float dPose = tPose - tEnd, dReady = tReady - tEnd; int swallowed = p.stats.swallowedShots - sw0;
            Check("M31", "冲刺转开火可读（LMG-2 " + lmg.sprintToFire + " s）：等待中准星透明度 ≤ 0.5、就绪那一帧 ≥ 0.9；枪模抬起到位时间 = sprintToFire ± 0.05 s；等待中按开火不缓冲、只记吞键 + 闪灰点",
                sprinted && poseAtSprint > .9f && waitMaxA <= .5f && readyA >= .9f && Mathf.Abs(dPose - lmg.sprintToFire) <= .05f && Mathf.Abs(dReady - lmg.sprintToFire) <= .05f && swallowed == 1 && dotSeen,
                "冲刺=" + sprinted + "（姿势权重 " + poseAtSprint.ToString("0.00") + "），等待中最高透明度 " + waitMaxA.ToString("0.00") + "，就绪帧 " + readyA.ToString("0.00") + "，抬枪到位 " + dPose.ToString("0.000") + " s / 能开火 " + dReady.ToString("0.000") + " s（规则 " + lmg.sprintToFire + " s），吞键 " + swallowed + " 次，灰点=" + dotSeen);

            // ---------------- M32：开镜（LMG-2 + 4 级甲超重）：枪模到达开镜位 = AdsTime ± 0.05 s；美食模式腰射准星 + 美食准镜透明度之和全程 ≥ 0.8
            yield return new WaitForSeconds(1.2f);   // 等 M31 的后坐 / 枪口上跳回落
            p.armorClass = 4; float adsT = Ballistics.AdsTime(p);
            p.botAim = true; float t0 = Time.time, tK = -1f, tPivot = -1f, minSum = 9f;
            for (float w = 0; w < adsT + .6f; w += Time.deltaTime)
            {
                yield return null;
                if (p.adsK > 0f) minSum = Mathf.Min(minSum, GameUI.HipCrossK(p) + GameUI.FoodReticleK(p));
                if (tK < 0 && p.adsK >= .999f) tK = Time.time - t0;
                if (tPivot < 0 && p.HasViewModel && (p.ViewPivotLocal - p.ViewAdsLocal).magnitude < .002f) tPivot = Time.time - t0;
            }
            p.botAim = false; yield return new WaitForSeconds(.5f);
            bool crossOk = !ToonAssets.Available || minSum >= .8f;
            Check("M32", "开镜过程始终有准星（美食模式两种准星透明度之和 ≥ 0.8）；枪模到达开镜位 = AdsTime ± 0.05 s（LMG-2 + 4 级甲 " + Ballistics.Weight(p).ToString("0.0") + " kg）",
                crossOk && tK > 0 && Mathf.Abs(tK - adsT) <= .05f && tPivot > 0 && Mathf.Abs(tPivot - adsT) <= .05f,
                "AdsTime " + adsT.ToString("0.000") + " s，adsK 到 1 用 " + tK.ToString("0.000") + " s，枪模到位 " + tPivot.ToString("0.000") + " s，准星透明度之和最低 " + (ToonAssets.Available ? minSum.ToString("0.00") : "（写实模式不测）"));
            p.armorClass = p.Hero.armorClass; p.weapon = null; p.Equip(p.Hero.weapon); p.weapon = null;

            // ---------------- M33 / M35：死亡回顾覆盖所有伤害来源 + 播报画实际武器
            int foe = 1 - p.team;
            var victim = Npc(p.team, 0); var gunner = Npc(foe, 0); var ember = Npc(foe, 1);
            if (victim.role == Role.Phantom) victim.ChangeRole(Role.Rivet);   // 幻频被动会随机挡子弹，换一个确定的
            if (ember.role != Role.Ember) ember.ChangeRole(Role.Ember);
            Hush(victim); Hush(gunner); Hush(ember);
            var deaths = new List<string>(); bool recapAll = true; bool feedGun = false, feedSkill = false;
            Vector3 basePos = new Vector3(-46, .05f, -2);
            for (int kind = 0; kind < 6; kind++)
            {
                if (kind == 5 && !ToonAssets.Available) break;   // 大招只在美食模式
                ClearFire();
                Vector3 at = basePos + new Vector3(kind * 1.2f, 0, kind % 2 == 0 ? 0 : 1.5f);
                victim.Respawn(at, 0); victim.hp = 1; victim.armor = 0; Hush(victim);
                gunner.Respawn(at + new Vector3(0, 0, -6), 0); ember.Respawn(at + new Vector3(3, 0, -3), 0); Hush(gunner); Hush(ember);
                yield return null; yield return new WaitForFixedUpdate();
                Fighter expect = null; DamageKind want = DamageKind.Gun; string label = "";
                switch (kind)
                {
                    case 0:   // 枪（非招牌枪：免费手枪）
                    {
                        expect = gunner; want = DamageKind.Gun; label = "枪";
                        gunner.Equip(Config.Pistol);
                        Vector3 o = gunner.Eye, d = (victim.Chest - o).normalized;
                        RaycastHit best = default; bool got = false;
                        foreach (var h in Physics.RaycastAll(o, d, 20f, ~0, QueryTriggerInteraction.Ignore)) if (h.collider.GetComponentInParent<Fighter>() == victim && (!got || h.distance < best.distance)) { best = h; got = true; }
                        if (got) gunner.Resolve(best, d, best.distance, gunner.W);
                        yield return null; break;
                    }
                    case 1:   // 技能：点火纸杯蛋糕
                        expect = ember; want = DamageKind.Skill; label = "纸杯蛋糕";
                        new GameObject("v22 cupcake").AddComponent<BlastCharge>().Init(ember, at + new Vector3(0, .1f, 1f));
                        yield return new WaitForSeconds(Config.BlastWarning + .3f); break;
                    case 2:   // 手雷
                        expect = gunner; want = DamageKind.Grenade; label = "手雷";
                        Place(gunner, at + new Vector3(0, .0f, -3f), 0); yield return null;
                        Grenade.Throw(gunner, Vector3.zero);
                        yield return new WaitForSeconds(Grenade.Fuse + .4f); break;
                    case 3:   // 焦糖火
                    {
                        expect = ember; want = DamageKind.Fire; label = "焦糖火";
                        var fz = new GameObject("v22 fire"); fz.transform.position = at; fz.AddComponent<FireZone>().owner = ember;
                        yield return new WaitForSeconds(.5f); break;
                    }
                    case 4:   // 近战（背后）
                        expect = gunner; want = DamageKind.Melee; label = "近战";
                        Place(gunner, at + new Vector3(0, 0, -1.2f), 0); yield return null; yield return new WaitForFixedUpdate();
                        gunner.Melee(); yield return null; break;
                    default:  // 大招：生日蜡烛轰炸（两轮，避免随机落点都落空）
                        expect = ember; want = DamageKind.Ult; label = "大招";
                        CandleBarrage.Spawn(at, ember); CandleBarrage.Spawn(at, ember);
                        yield return new WaitForSeconds(2.4f); break;
                }
                bool dead = !victim.alive; CombatHud.LastHit(victim, out var kb);
                string title = CombatHud.RecapTitle(victim), line = CombatHud.RecapKillerLine(victim);
                bool ok = dead && victim.recapKiller == expect && kb.kind == want && kb.source == expect && title.Contains(expect.Hero.name) && title.Contains(kb.weapon ?? "?") && line.Contains("剩余");
                recapAll &= ok;
                deaths.Add(label + (ok ? "✓" : "✗") + "「" + title + "｜" + line + "」");
                FeedItem last = null; for (int i = m.feed.Count - 1; i >= 0; i--) if (m.feed[i].kill && m.feed[i].victim == victim) { last = m.feed[i]; break; }
                if (kind == 0) feedGun = last != null && last.gun == Config.Pistol && GameUI.FeedWeaponLabel(last) == Config.Pistol.id && gunner.Hero.weapon != Config.Pistol;
                if (kind == 1) feedSkill = last != null && last.gun == null && last.kind == DamageKind.Skill && GameUI.FeedWeaponLabel(last) == ember.Hero.skill.Name;
            }
            ClearFire();
            Check("M33", "死亡回顾覆盖所有伤害来源：被枪 / 纸杯蛋糕 / 手雷 / 焦糖火 / 近战" + (ToonAssets.Available ? " / 大招" : "") + "打死，标题击杀者 = Die() 收到的 killer、来源名正确、有\"对手剩余生命\"行",
                recapAll && deaths.Count >= 5, string.Join("；", deaths.ToArray()));
            Check("M35", "击杀播报画实际武器：用非招牌枪（P-9）击杀时播报 = P-9；技能击杀画技能图标 + 技能名", feedGun && feedSkill, "枪=" + feedGun + " 技能=" + feedSkill);

            // ---------------- M34：被施放文案美食化 + 焦糖火不借用"侦测"警告
            p.Respawn(spot, 90); p.scripted = true; Hush(p); p.revealedUntil = 0; yield return null;
            var fireGo = new GameObject("v22 fire on player"); fireGo.transform.position = p.transform.position; fireGo.AddComponent<FireZone>().owner = ember;
            yield return new WaitForSeconds(.35f);
            bool fireOnly = p.RevealedByFireOnly; string fireText = GameUI.RevealText(p);
            Object.Destroy(fireGo); yield return null;
            p.revealedUntil = Time.time + 5f; string scanText = GameUI.RevealText(p); bool scanNotFire = !p.RevealedByFireOnly;
            string jamText = GameUI.JamText(p); p.revealedUntil = 0; p.fireRevealUntil = 0;
            string[] banned = { "侦测", "信号", "噪幕", "干扰" }; string bad = "";
            if (ToonAssets.Available) foreach (var t in new[] { fireText, scanText, jamText }) foreach (var b in banned) if (t.Contains(b)) bad += b + "@" + t + " ";
            bool textOk = ToonAssets.Available ? bad == "" && fireText.Contains("焦糖火") && !scanText.Contains("焦糖火") && scanText.Contains("芥末") : fireText.Contains("燃烧区") && scanText.Contains("侦测");
            Check("M34", "被施放文案：美食模式下暴露 / 干扰警告不含\"侦测 / 信号 / 噪幕 / 干扰\"；只站在焦糖火里时暴露原因 = 焦糖火（不再借用芥末警告）",
                fireOnly && scanNotFire && textOk, "火里=" + fireOnly + "「" + fireText + "」 扫描「" + scanText + "」 干扰「" + jamText + "」" + (bad != "" ? " 违规：" + bad : ""));
            // ---------------- M36（V22）：第一人称手臂 —— 有手臂、左手对准护木、袖子队伍色 / 袖口英雄色、不挡准星、换弹时左手下探再回来
            {
                bool want = ToonAssets.Available && Resources.Load<GameObject>("Toon/FPArms/FPArms_Sweater") != null;
                var vp = p.ViewParts; var vg = p.ViewGun; string info; bool armsOk;
                if (!want) { armsOk = true; info = "写实模式 / 没有手臂模型：保持旧表现，跳过"; }
                else if (vp == null || vg == null || vp.arms == null || vp.armL == null) { armsOk = false; info = "第一人称没有挂上手臂"; }
                else
                {
                    Transform palmL = null, palmR = null;
                    foreach (var t in vp.arms.GetComponentsInChildren<Transform>(true)) { if (t.name == "Palm_L") palmL = t; if (t.name == "Palm_R") palmR = t; }
                    float dL = palmL != null ? Vector3.Distance(vg.InverseTransformPoint(palmL.position), vp.palmLTarget) : 9f;
                    float dFg = p.W.id == "P-9" ? 0f : Vector3.Distance(vp.palmLTarget, vp.foregrip);
                    Color sleeve = FighterAvatar.FPArmColor(vp.arms, "FPArm_Sleeve"), cuff = FighterAvatar.FPArmColor(vp.arms, "FPArm_Cuff");
                    Color wantSleeve = Color.Lerp(Config.TeamColor(p.team), Color.white, .1f), wantCuff = p.Hero.tint;
                    float cS = Mathf.Abs(sleeve.r - wantSleeve.r) + Mathf.Abs(sleeve.g - wantSleeve.g) + Mathf.Abs(sleeve.b - wantSleeve.b);
                    float cC = Mathf.Abs(cuff.r - wantCuff.r) + Mathf.Abs(cuff.g - wantCuff.g) + Mathf.Abs(cuff.b - wantCuff.b);
                    float ctr = 9f;
                    foreach (var pt in new[] { palmL, palmR })
                        if (pt != null) { var vpt = p.cam.WorldToViewportPoint(pt.position); ctr = Mathf.Min(ctr, Vector2.Distance(new Vector2(vpt.x, vpt.y), new Vector2(.5f, .5f))); }
                    Vector3 rest = vp.armLRest;   // 枪 pivot 空间：排除换弹时整把枪的下沉 / 旋转
                    p.ammo = 1; p.Reload(); float dip = 0f;
                    for (float w = 0; w < 6f && p.IsReloading; w += Time.deltaTime) { dip = Mathf.Max(dip, Vector3.Distance(vg.InverseTransformPoint(vp.armL.position), rest)); yield return null; }
                    yield return null; yield return null;
                    float back = Vector3.Distance(vg.InverseTransformPoint(vp.armL.position), rest);
                    armsOk = dL < .02f && dFg < .03f && cS < .06f && cC < .06f && ctr > .12f && dip > .05f && back < .005f;
                    info = p.W.id + " 左手偏差=" + dL.ToString("0.000") + "m 目标离护木=" + dFg.ToString("0.000") + "m 袖子色差=" + cS.ToString("0.00") + " 袖口色差=" + cC.ToString("0.00")
                        + " 手离准星=" + ctr.ToString("0.00") + "(视口) 换弹下探=" + dip.ToString("0.000") + "m 回位=" + back.ToString("0.000") + "m";
                }
                Check("M36", "第一人称手臂（甜心针织袖）：左手 Palm 对准护木（< 2 cm）、袖子 = 队伍色 / 袖口 = 英雄色、双手离准星 > 0.12 视口、换弹时左手下探 > 5 cm 并回位", armsOk, info);
            }
            p.scripted = false; Hush(p);
            foreach (var f in new[] { victim, gunner, ember }) { f.weapon = null; f.Respawn(m.arena.SpawnPoint(f.team == m.AttackingTeam, f.slot), 0); Hush(f); }
            p.Respawn(m.arena.SpawnPoint(p.team == m.AttackingTeam, p.slot), 0);
            yield return null;
        }

        // V22 第一期截图（38–41）：状态带 / 冲刺转开火 / 开镜过程 / 死亡回顾。batchmode 下抓屏失败时用 HudCapture 把 IMGUI 画进离屏图
        IEnumerator HudShots(string dir)
        {
            var m = Match.I; var p = m.player; if (p == null) yield break;
            HudCaptureBegin.Allow = true;
            m.phase = Phase.Live; m.roundClock = Config.RoundTime;
            m.ui.ClearBanner();         // 33_banner_ffa 临时切到大乱斗拍横幅，横幅还会挂 3.5 秒 —— 清掉，免得爆破截图里出现"美食大乱斗"
            m.ui.PreviewPause(false);   // 截图模式下 overlay 会残留 Menu：清掉，否则准星不画、玩家输入被冻结
            p.enabled = true; p.AttachCamera(); p.SetViewModelVisible(true);
            Vector3 spot = new Vector3(-44, .05f, 3.5f);
            p.Respawn(spot, 92); p.scripted = true; Hush(p); p.pitch = 1;
            yield return new WaitForSeconds(.8f);
            HudDiag("38_hud_idle"); yield return Grab(System.IO.Path.Combine(dir, "38_hud_idle.png"));
            p.SetProne(true); yield return new WaitForSeconds(.6f); p.stamina = 40f; yield return null;
            HudDiag("39_hud_states"); yield return Grab(System.IO.Path.Combine(dir, "39_hud_states.png"));
            p.SetProne(false); yield return new WaitForSeconds(.6f);
            Place(p, spot, 92); p.pitch = 1; p.stamina = 70f; p.botMove = p.transform.forward; p.botSprint = true;
            yield return new WaitForSeconds(.45f);
            HudDiag("39b_hud_draining"); yield return Grab(System.IO.Path.Combine(dir, "39b_hud_draining.png"));
            p.stamina = 2f; for (float w = 0; w < 1f && !p.exhausted; w += Time.deltaTime) yield return null;
            p.botMove = Vector3.zero; p.botSprint = false; p.botCrouch = true; yield return new WaitForSeconds(.35f);
            HudDiag("39c_hud_locked"); yield return Grab(System.IO.Path.Combine(dir, "39c_hud_locked.png"));
            p.botCrouch = false; yield return new WaitForSeconds(.3f);
            // 冲刺转开火：LMG-2（0.32 s），停下的同时扣扳机 → 准星散开半透明 + 中心灰点 + 枪还斜着
            var lmg = Config.Hero(Role.Ironwall).weapon;
            Place(p, spot, 92); p.pitch = 1; p.Equip(lmg); p.stamina = Tune.StaminaMax; p.exhausted = false; yield return new WaitForSeconds(lmg.drawTime + .2f);
            p.botMove = p.transform.forward; p.botSprint = true; yield return new WaitForSeconds(.5f);
            p.botMove = Vector3.zero; p.botSprint = false; p.botTrigger = true; yield return null; p.botTrigger = false;
            HudDiag("40b_sprint_to_fire"); yield return Grab(System.IO.Path.Combine(dir, "40b_sprint_to_fire.png"));
            yield return new WaitForSeconds(.6f);
            // 开镜过程：超重 LMG 开镜到一半（美食模式两种准星同时半透明）
            p.armorClass = 4; p.botAim = true;
            for (float w = 0; w < 1f && p.adsK < .7f; w += Time.deltaTime) yield return null;
            HudDiag("40_ads_transition"); yield return Grab(System.IO.Path.Combine(dir, "40_ads_transition.png"));
            p.botAim = false; p.armorClass = p.Hero.armorClass; yield return new WaitForSeconds(.5f);
            // 死亡回顾：被纸杯蛋糕炸死（非子弹）
            Fighter ember = null; foreach (var f in m.fighters) if (f.team != p.team && !f.human) { ember = f; break; }
            if (ember != null)
            {
                if (ember.role != Role.Ember) ember.ChangeRole(Role.Ember);
                ember.Respawn(p.transform.position + new Vector3(8, 0, 2), -100); Hush(ember); ember.hp = 64; ember.armor = 0;
                p.hp = 50; p.armor = 10; yield return null;
                // 先吃一发子弹（剪影上出现部位点），再被纸杯蛋糕炸死 —— 回顾里两种来源都能看到
                Vector3 eo = ember.Eye, ed = (p.Chest - eo).normalized; RaycastHit eh = default; bool eg = false;
                foreach (var h in Physics.RaycastAll(eo, ed, 20f, ~0, QueryTriggerInteraction.Ignore)) if (h.collider.GetComponentInParent<Fighter>() == p && (!eg || h.distance < eh.distance)) { eh = h; eg = true; }
                if (eg) ember.Resolve(eh, ed, eh.distance, ember.W);
                p.hp = Mathf.Min(p.hp, 20f); p.armor = 0;
                new GameObject("v22 shot cupcake").AddComponent<BlastCharge>().Init(ember, p.transform.position + p.transform.forward * 1.5f + Vector3.up * .1f);
                yield return new WaitForSeconds(Config.BlastWarning + .8f);
                HudDiag("41_death_recap_skill"); yield return Grab(System.IO.Path.Combine(dir, "41_death_recap_skill.png"));
                ClearFire();
            }
            p.scripted = false; Hush(p); p.weapon = null; p.Respawn(spot, 92); p.armorClass = p.Hero.armorClass;
            HudCaptureBegin.Allow = false;
            yield return null;
        }

        // V22 第三期截图（48–57）。第一人称的带 HUD；外部机位看联动本身
        IEnumerator LinkShots(string dir)
        {
            var m = Match.I; var p = m.player; var ar = m.arena; var cam = p.cam != null ? p.cam : Camera.main;
            if (p == null || cam == null) yield break;
            HudCaptureBegin.Allow = true;
            m.phase = Phase.Live; m.roundClock = Config.RoundTime; m.ui.ClearBanner(); m.ui.PreviewPause(false);
            var savedRoles = new Dictionary<Fighter, Role>(); foreach (var f in m.fighters) savedRoles[f] = f.role;
            ClearFire(); Skills.ClearRound(); SteamCloud.ClearTimed(); Pings.Clear(); UltWarn.Clear(); FoodTips.AutoEnabled = false;
            foreach (var f in m.fighters) { Hush(f); if (!f.human) f.Respawn(new Vector3(-60 + f.slot * 2, .05f, f.team == 0 ? -46f : -44f), 0); }
            foreach (var w in ar.breakWalls) w.ResetRound();
            foreach (var d in ar.destructibles) d.Restore();
            foreach (var sh in ar.shutters) sh.ResetRound();
            int att = m.AttackingTeam, def = m.DefendingTeam; int foeTeam = p.team == att ? def : att;
            System.Action<Vector3, Vector3, float> Ext = (pos, look, fov) => { p.enabled = false; cam.transform.SetParent(null); cam.transform.position = pos; cam.transform.LookAt(look); cam.fieldOfView = fov; };
            System.Action FP = () => { p.enabled = true; p.AttachCamera(); p.SetViewModelVisible(true); };
            System.Action<Vector3> AimAt = (t) => { Vector3 d = t - p.Eye; p.yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; p.pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg; };
            HotpotHazard potN = null; foreach (var h in HotpotHazard.All) if (h != null && h.transform.position.z > 0) potN = h;

            // ---- 55 联动提示（LINK-7）：夜鸦瞄关东煮锅 → 金色气泡 + 技能图标变金 + 组合提示
            if (potN != null)
            {
                Vector3 pc = potN.transform.position;
                p.ChangeRole(Role.Raven); p.Respawn(new Vector3(pc.x - 1.2f, .05f, 49f), 180); p.scripted = true; Hush(p); FP(); p.skillCharges = 2;
                AimAt(pc + Vector3.up * .55f); yield return new WaitForSeconds(.8f);
                HudDiag("55_link_hint"); yield return Grab(System.IO.Path.Combine(dir, "55_link_hint.png"));
                // ---- 50 芥末辣锅：签子进锅，热气后面的敌人被透视（菱形标记）
                var foe = Npc(foeTeam, 0); Hush(foe); Place(foe, pc + new Vector3(.8f, .05f, -3.6f), 0);
                Skills.ShootArrow(p, Skills.ArrowVelocityTo(p.Eye, pc + Vector3.up * .55f)); p.skillCharges = 2;
                yield return new WaitForSeconds(1.4f);
                yield return Grab(System.IO.Path.Combine(dir, "50_combo_mustard_pot.png"));
                Ext(new Vector3(pc.x + 9f, 4.2f, pc.z - 7f), pc + Vector3.up * .8f, 62); yield return new WaitForSeconds(.2f);
                yield return Grab(System.IO.Path.Combine(dir, "50b_combo_mustard_pot_world.png"));
                // ---- 56 团队标记：看着火锅按中键 = 这儿能联动；再标一个"这里"
                FP(); AimAt(pc + Vector3.up * .55f); yield return new WaitForSeconds(.7f);
                Pings.FromAim(p); yield return new WaitForSeconds(.7f);
                Pings.Create(p, PingKind.Here, new Vector3(4, 0, 44f)); yield return new WaitForSeconds(.4f);
                HudDiag("56_ping"); yield return Grab(System.IO.Path.Combine(dir, "56_ping.png"));
                Place(foe, new Vector3(-30, .05f, 34), 0); Skills.ClearRound(); Pings.Clear();
            }
            // ---- 53 开墙 + 48 嵌入补洞（A 点南蛋糕墙）
            BreakWall wA = null; foreach (var w in ar.breakWalls) if (w != null && w.transform.position.z > 0) wA = w;
            if (wA != null)
            {
                var em = Npc(foeTeam, 1); Hush(em); if (em.role != Role.Ember) em.ChangeRole(Role.Ember);
                Place(em, new Vector3(35.5f, .05f, 12.5f), 0);
                Vector3 face = wA.transform.TransformPoint(new Vector3(0, 1.3f, -.2f));
                WallPiece wp = null; float bd = 9f; foreach (var q in wA.pieces) { float dd = Vector3.Distance(q.transform.position, face); if (dd < bd) { bd = dd; wp = q; } }
                var bc = new GameObject("截图纸杯").AddComponent<BlastCharge>(); bc.Init(em, face, wp);
                Ext(new Vector3(40.5f, 1.9f, 11.5f), new Vector3(37, 1.4f, 18), 60);
                yield return new WaitForSeconds(Config.BlastWarning * .6f);
                yield return Grab(System.IO.Path.Combine(dir, "53_combo_breach_armed.png"));
                yield return new WaitForSeconds(Config.BlastWarning * .4f + .12f);
                yield return Grab(System.IO.Path.Combine(dir, "53_combo_breach.png"));
                yield return new WaitForSeconds(1.2f); ClearFire();
                yield return Grab(System.IO.Path.Combine(dir, "53b_combo_breach_hole.png"));
                var hole = wA.Opening();
                var rv = Npc(p.team, 0); Hush(rv); if (rv.role != Role.Rivet) rv.ChangeRole(Role.Rivet);
                if (hole != null)
                {
                    Place(rv, hole.center + new Vector3(-1.4f, .05f, 2.8f), 160); rv.skillCharges = 2;
                    Skills.Cast(rv, hole.center);
                    yield return new WaitForSeconds(Config.CoverPatchDeploy * .5f);
                    yield return Grab(System.IO.Path.Combine(dir, "48b_combo_wall_deploy.png"));
                    yield return new WaitForSeconds(Config.CoverPatchDeploy * .5f + .5f);
                    Ext(new Vector3(39.8f, 1.9f, 12.8f), hole.center + Vector3.up * 1.1f, 58); yield return new WaitForSeconds(.2f);
                    yield return Grab(System.IO.Path.Combine(dir, "48_combo_wall.png"));
                }
                Skills.ClearRound(); wA.ResetRound();
            }
            // ---- 49 嵌入补洞预览（第一人称，金色吸附 + 结果字）：北关口墙洞
            Destructible hc = null; foreach (var d in ar.destructibles) if (d != null && d.name.Contains("墙洞") && d.transform.position.z > 0) hc = d;
            if (hc != null)
            {
                hc.TakeDamage(9999, null, true);
                p.ChangeRole(Role.Rivet); p.Respawn(new Vector3(.6f, .05f, 46.4f), 90); p.scripted = true; Hush(p); FP(); p.skillCharges = 2; p.pitch = 16;
                yield return new WaitForSeconds(.5f);
                for (int i = 0; i < 6; i++) { Skills.DebugPreview(p); yield return null; }
                HudDiag("49_combo_preview"); yield return Grab(System.IO.Path.Combine(dir, "49_combo_preview.png"));
                Skills.Cancel(p); hc.Restore();
            }
            // ---- 52 果冻门帘：铁幕把布丁盾嵌进北蒸笼门，队友挤过门帘
            var gN = ar.Gate(1);
            if (gN != null)
            {
                var iw = Npc(p.team, 1); Hush(iw); if (iw.role != Role.Ironwall) iw.ChangeRole(Role.Ironwall);
                Place(iw, new Vector3(1.2f, .05f, 40.8f), 90); iw.skillCharges = 1; yield return null;
                Skills.Cast(iw, gN.transform.position);
                Place(iw, new Vector3(-3f, .05f, 37.5f), 40);
                var walker = Npc(p.team, 0); Hush(walker); Place(walker, new Vector3(1.4f, .05f, 40.2f), 90); walker.botMove = Vector3.right;
                Ext(new Vector3(10.5f, 2.3f, 36.2f), new Vector3(4, 1.5f, 40), 62);
                float wt = 0; while (wt < 1.5f && ShieldWall.CurtainAt(walker.transform.position) == null) { wt += Time.deltaTime; yield return null; }
                yield return new WaitForSeconds(.12f);
                yield return Grab(System.IO.Path.Combine(dir, "52_combo_curtain.png"));
                walker.botMove = Vector3.zero; Skills.ClearRound();
            }
            // ---- 51 可乐弹射：北长街西段的果冻弹床，猎隼冲刺踩上去
            {
                var fal = Npc(p.team, 0); Hush(fal); if (fal.role != Role.Falcon) fal.ChangeRole(Role.Falcon);
                var padGo = new GameObject("截图弹射板"); padGo.transform.position = new Vector3(-58, 0, 38.5f); padGo.transform.rotation = Quaternion.Euler(0, 90, 0);
                var tp = padGo.AddComponent<JumpPad>(); tp.Build(new Color(1f, .45f, .3f)); tp.launch = new Vector3(0, 9f, 5f);
                JumpPad.TestAllowBots = true;
                Place(fal, new Vector3(-62.5f, .05f, 38.5f), 90); yield return new WaitForFixedUpdate(); yield return null; yield return null;
                fal.skillCharges = 2; float t0 = JumpPad.LastLaunchAt; Skills.Cast(fal, fal.transform.position + Vector3.right * 8f);
                Ext(new Vector3(-50.5f, 2.4f, 31.5f), new Vector3(-53.5f, 3.2f, 38.5f), 66);
                float wt = 0; while (JumpPad.LastLaunchAt == t0 && wt < 1.5f) { wt += Time.deltaTime; yield return null; }
                yield return new WaitForSeconds(.42f);
                yield return Grab(System.IO.Path.Combine(dir, "51_combo_cola.png"));
                yield return new WaitForSeconds(1f);
                JumpPad.TestAllowBots = false; Destroy(padGo);
            }
            // ---- 54 棉花糖蒸汽：中庭蒸汽阀在喷 + 棉花糖迷雾罩住
            SteamValve valve = null; foreach (var v in SteamValve.Valves) if (v != null && Flat(v.transform.position, new Vector3(16, 0, 8)) < 1f) valve = v;
            if (valve != null)
            {
                var ph = Npc(foeTeam, 2); Hush(ph); if (ph.role != Role.Phantom) ph.ChangeRole(Role.Phantom);
                valve.ResetRound(); valve.Fire(null);
                var ng = new GameObject("截图迷雾"); ng.transform.position = valve.transform.position + new Vector3(-1f, 0, -3.5f); ng.AddComponent<NoiseField>().Init(ph); Skills.spawned.Add(ng);
                var mate = Npc(p.team, 1); Hush(mate); Place(mate, valve.transform.position + new Vector3(4.5f, .05f, 1.5f), -90);
                Ext(new Vector3(5.5f, 2.2f, 11.5f), new Vector3(16, 1.6f, 8), 66); yield return new WaitForSeconds(1.2f);
                yield return Grab(System.IO.Path.Combine(dir, "54_combo_candy_steam.png"));
                valve.ResetRound(); Skills.ClearRound(); SteamCloud.ClearTimed();
            }
            // ---- 57 敌方大招预警：敌方焚芯朝你放生日蜡烛轰炸 → 横幅 + 落点圈 + 状态条
            if (ToonAssets.Available)
            {
                p.ChangeRole(savedRoles[p]); p.Respawn(new Vector3(-44, .05f, 4), 90); p.scripted = true; Hush(p); FP(); p.hp = Config.MaxHp; p.armor = Config.MaxArmor;
                var foeE = Npc(foeTeam, 1); Hush(foeE); if (foeE.role != Role.Ember) foeE.ChangeRole(Role.Ember);
                Place(foeE, new Vector3(-30, .05f, 4), -90); yield return null;
                Vector3 dd = p.transform.position + Vector3.right * 2f - foeE.Eye; foeE.yaw = Mathf.Atan2(dd.x, dd.z) * Mathf.Rad2Deg; foeE.pitch = -Mathf.Atan2(dd.y, new Vector2(dd.x, dd.z).magnitude) * Mathf.Rad2Deg;
                p.yaw = 90; p.pitch = 8; yield return new WaitForSeconds(.3f);
                foeE.Food.ult = 1f; foeE.Food.TryUlt();
                yield return new WaitForSeconds(.25f);
                HudDiag("57_ult_warning"); yield return Grab(System.IO.Path.Combine(dir, "57_ult_warning.png"));
                yield return new WaitForSeconds(2.5f); m.ui.ClearBanner(); UltWarn.Clear();
            }
            // ---- 收尾
            JumpPad.TestAllowBots = false; FoodTips.AutoEnabled = true;
            ClearFire(); Skills.ClearRound(); SteamCloud.ClearTimed(); Pings.Clear();
            foreach (var w in ar.breakWalls) w.ResetRound();
            foreach (var d in ar.destructibles) d.Restore();
            foreach (var sh in ar.shutters) sh.ResetRound();
            foreach (var kv in savedRoles) if (kv.Key.role != kv.Value) kv.Key.ChangeRole(kv.Value);
            p.scripted = false; Hush(p); FP(); p.Respawn(new Vector3(-44, .05f, 3.5f), 92);
            HudCaptureBegin.Allow = false;
            yield return null;
        }

        // 相机渲染到离屏图后，让本帧的 IMGUI（GameUI / CombatHud / FoodHud）也画进同一张图，再读回存盘
        void HudDiag(string tag)
        {
            var m = Match.I; var p = m.player;
            Debug.Log("NEON_HUDDIAG " + tag + " overlay=" + m.ui.OverlayName + " started=" + m.ui.Started + " alive=" + p.alive + " crossCalls=" + m.ui.crossDrawn + " alpha=" + GameUI.CrossAlpha(p).ToString("0.00") + " hipK=" + GameUI.HipCrossK(p).ToString("0.00")
                + " elems=" + string.Join(",", CombatHud.Elements(p).ToArray()) + " stamina=" + p.stamina.ToString("0") + " crouch=" + p.crouching + " prone=" + p.Prone + " sprint=" + p.sprinting + " exhausted=" + p.exhausted + " adsK=" + p.adsK.ToString("0.00") + " tuning=" + TuningPanel.Open + " hud=" + (Object.FindObjectOfType<CombatHud>() != null) + " screen=" + Screen.width + "x" + Screen.height);
        }

        static IEnumerator RenderWithHud(Camera cam, string file)
        {
            int w = Screen.width >= 640 ? Screen.width : 1600, h = Screen.width >= 640 ? Screen.height : 900;
            var hdr = new RenderTexture(w, h, 24, RenderTextureFormat.DefaultHDR);
            cam.targetTexture = hdr; cam.Render(); cam.targetTexture = null;
            var ldr = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(hdr, ldr); hdr.Release();
            HudCaptureEnd.file = file; HudCaptureEnd.done = false; HudCaptureBegin.target = ldr;
            for (int i = 0; i < 10 && !HudCaptureEnd.done; i++) yield return null;
            if (!HudCaptureEnd.done) { HudCaptureBegin.target = null; HudCaptureEnd.file = null; Debug.LogWarning("NEON_HUD_CAPTURE_NONE guiFrames=" + HudCaptureBegin.guiFrames); Render(cam, file); }
            else Debug.Log("NEON_HUD_CAPTURE " + w + "x" + h + " " + System.IO.Path.GetFileName(file));
            ldr.Release();
        }

        // ================================================================ V23 a + c：视线 / 爆炸拆分、曼妥思喷泉、技能 / 大招美食表现（设计稿_V23 §2、§4.0、A1、A2，M55–M60）
        static readonly Role[] V23Roles = { Role.Falcon, Role.Rivet, Role.Ironwall, Role.Raven, Role.Ember, Role.Phantom };
        static void V23Cleanup()
        {
            ClearFire(); Skills.ClearRound(); SteamCloud.ClearTimed();
            foreach (var rw in Object.FindObjectsOfType<RiceWall>()) Object.Destroy(rw.gameObject);
            foreach (var jd in Object.FindObjectsOfType<JellyDome>()) Object.Destroy(jd.gameObject);
            foreach (var cb in Object.FindObjectsOfType<CandleBarrage>()) Object.Destroy(cb.gameObject);
            foreach (var dc in Object.FindObjectsOfType<DeployedCover>()) Object.Destroy(dc.gameObject);
            foreach (var sw in Object.FindObjectsOfType<ShieldWall>()) Object.Destroy(sw.gameObject);
        }

        IEnumerator V23CombatTests()
        {
            var m = Match.I; var p = m.player; var ar = m.arena;
            m.phase = Phase.Live; m.roundClock = Config.RoundTime;
            if (p == null) { Check("M55", "V23 战斗表现", false, "没有玩家角色"); yield break; }
            var savedRoles = new Dictionary<Fighter, Role>(); foreach (var f in m.fighters) savedRoles[f] = f.role;
            FoodTips.AutoEnabled = false; V23Cleanup(); UltWarn.Clear();
            foreach (var f in m.fighters) Hush(f);
            int myTeam = p.team, foeTeam = 1 - p.team;
            Vector3 c = new Vector3(-40, .05f, 4);
            p.Respawn(new Vector3(-44, .05f, -8), 0); p.scripted = true; Hush(p);

            // ---------------- M55：站在蒸汽里 —— 看不见，但手雷 / 纸杯蛋糕炸得到；冰淇淋散雾只散局部
            var thrower = Npc(myTeam, 0); var foe = Npc(foeTeam, 0);
            Hush(thrower); Hush(foe); if (foe.role == Role.Ironwall) foe.ChangeRole(Role.Falcon);   // 铁幕被动爆炸减半，换掉免得干扰读数
            foe.Respawn(c, 0); Hush(foe); Place(thrower, c + new Vector3(-7, 0, 0), 90);
            yield return new WaitForFixedUpdate(); yield return null;
            SteamCloud.ClearTimed(); SteamCloud.AddTimed(foe.Chest, 2.8f, Time.time + 30f);   // 和蒸汽阀同款的限时白雾，罩住目标
            bool hidden = !Skills.SightClear(thrower.Eye, foe.Chest, foe) && !Skills.SightClear(thrower.Eye, foe.Head, foe);
            bool blastLine = Skills.BlastClear(c + new Vector3(-2f, .3f, 0), foe.Chest, foe);
            foe.hp = Config.MaxHp; foe.armor = 0;
            var gr = Grenade.Throw(thrower, Vector3.zero); gr.transform.position = c + new Vector3(-2f, .3f, 0); gr.Detonate();
            yield return null;
            float gDmg = Config.MaxHp - foe.hp;
            foe.hp = Config.MaxHp; foe.armor = 0; BlastCharge.LastResult = "";
            var bc = new GameObject("测试纸杯").AddComponent<BlastCharge>(); bc.Init(thrower, c + new Vector3(-1.8f, .1f, 0));
            yield return new WaitForSeconds(Config.BlastWarning + .12f);
            float cDmg = Config.MaxHp - foe.hp; string cupResult = BlastCharge.LastResult;
            bool stillHidden = !Skills.SightClear(thrower.Eye, foe.Chest, foe);
            ClearFire(); SteamCloud.ClearTimed();
            Vector3 A = new Vector3(-40, 1.3f, 4), B = new Vector3(-40, 1.3f, 30);
            SteamCloud.AddTimed(A, 2.8f, Time.time + 30f); SteamCloud.AddTimed(B, 2.8f, Time.time + 30f);
            SteamCloud.Suppress(A, SteamCloud.SuppressRadius, 1.5f);
            bool aGone = !SteamCloud.Blocks(A + Vector3.left * 4, A + Vector3.right * 4);
            bool bStays = SteamCloud.Blocks(B + Vector3.left * 4, B + Vector3.right * 4);
            yield return new WaitForSeconds(1.7f);
            bool aBack = SteamCloud.Blocks(A + Vector3.left * 4, A + Vector3.right * 4);
            SteamCloud.ClearTimed();
            Check("M55", "视线 / 爆炸拆分：目标站在蒸汽里 → SightClear 看不见，BlastClear 不受蒸汽影响，手雷和纸杯蛋糕都炸得到；冰淇淋散雾只散 " + SteamCloud.SuppressRadius + " m 内的蒸汽（远处的照样挡视线），到期恢复",
                hidden && stillHidden && blastLine && gDmg > 20f && cDmg > 15f && aGone && bStays && aBack,
                "看不见=" + hidden + "/" + stillHidden + "，爆炸线通=" + blastLine + "，手雷伤害 " + gDmg.ToString("0") + "，纸杯伤害 " + cDmg.ToString("0") + "（结果字 " + cupResult + "），局部散雾：近处散=" + aGone + " 26 m 外仍挡=" + bStays + " 到期恢复=" + aBack);

            // ---------------- M56：A1 汽水罐手雷炸曼妥思 → 喷泉 + 附近角色被弹起（真实投掷 + 直接引爆 + 冷却 + 超出范围）
            MentosPile pile = null; foreach (var mp in MentosPile.All) if (mp != null && mp.transform.position.z > 0) pile = mp;
            if (pile == null) Check("M56", "曼妥思喷泉", false, "MentosPile.All 里没有北侧曼妥思（All=" + MentosPile.All.Count + "）");
            else
            {
                Vector3 pp = pile.transform.position;
                var blockers = new List<string>();
                foreach (var col in Physics.OverlapCapsule(pp + Vector3.up * .3f, pp + Vector3.up * 1.6f, 1f, ~0, QueryTriggerInteraction.Ignore))
                    if (!col.transform.IsChildOf(pile.transform) && col.GetComponentInParent<Fighter>() == null) blockers.Add(col.name);
                bool hasCol = pile.GetComponentInChildren<Collider>() != null;
                bool reach = NavMesh.SamplePosition(pp + Vector3.left * 1.5f, out NavMeshHit nh, 1f, NavMesh.AllAreas);
                var mate = Npc(myTeam, 1); Hush(mate);
                // 复现 A1：从 8 m 外平直地砸中瓶子（玩家对着曼妥思扔的典型情况）。旧行为（MentosFix=false）vs 修好后
                float[] hitDist = new float[2]; bool[] hitFount = new bool[2];
                for (int pass = 0; pass < 2; pass++)
                {
                    Grenade.MentosFix = pass == 1; pile.ResetRound();
                    Place(thrower, pp + new Vector3(-8f, .05f, 0), 90); Place(mate, pp + new Vector3(30f, .05f, 0), -90);
                    yield return new WaitForFixedUpdate(); yield return null;
                    int h0 = MentosPile.Fountains;
                    Vector3 fr = thrower.Eye + thrower.AimDir * .5f + Vector3.down * .15f;
                    var gh = Grenade.Throw(thrower, Skills.VelocityTo(fr, pp + Vector3.up * .9f, 6f));
                    Vector3 lastH = gh.transform.position; float wth = 0;
                    while (gh != null && wth < Grenade.Fuse + 1f) { lastH = gh.transform.position; wth += Time.deltaTime; yield return null; }
                    hitDist[pass] = Links.Flat(lastH, pp); hitFount[pass] = MentosPile.Fountains > h0;
                }
                Grenade.MentosFix = true;
                pile.ResetRound();
                Place(thrower, pp + new Vector3(-6f, .05f, 0), 90); Place(mate, pp + new Vector3(1.6f, .05f, 1.2f), -90);
                yield return new WaitForFixedUpdate(); yield return null;
                int f0 = MentosPile.Fountains;
                Vector3 from = thrower.Eye + thrower.AimDir * .5f + Vector3.down * .15f;
                var g2 = Grenade.Throw(thrower, Skills.VelocityTo(from, pp + new Vector3(-1.3f, 0, 0), 25f));
                Vector3 last = g2.transform.position; float wt = 0, mateTop = mate.transform.position.y;
                while (g2 != null && wt < Grenade.Fuse + 1f) { last = g2.transform.position; wt += Time.deltaTime; yield return null; }
                float landDist = Links.Flat(last, pp);
                bool fountA = MentosPile.Fountains > f0;
                for (float t2 = 0; t2 < .5f; t2 += Time.deltaTime) { mateTop = Mathf.Max(mateTop, mate.transform.position.y); yield return null; }
                bool launched = mateTop > pp.y + 1f;
                yield return new WaitForSeconds(2.5f);
                pile.ResetRound(); int f1 = MentosPile.Fountains;
                var g3 = Grenade.Throw(thrower, Vector3.zero); g3.transform.position = pp + new Vector3(-2.5f, .2f, 0); g3.Detonate(); yield return null;
                bool fountB = MentosPile.Fountains == f1 + 1;
                var g4 = Grenade.Throw(thrower, Vector3.zero); g4.transform.position = pp + new Vector3(-2.5f, .2f, 0); g4.Detonate(); yield return null;
                bool cooldown = MentosPile.Fountains == f1 + 1;
                pile.ResetRound();
                var g5 = Grenade.Throw(thrower, Vector3.zero); g5.transform.position = pp + new Vector3(-5.5f, .2f, 0); g5.Detonate(); yield return null;
                bool farNo = MentosPile.Fountains == f1 + 1;
                pile.ResetRound();
                Check("M56", "曼妥思喷泉（A1）：8 m 外平直砸中瓶子 → 修好后必定喷泉（旧行为会被弹出触发圈）；站 6 m 外把汽水罐手雷扔到曼妥思脚下 → 引信结束时喷泉触发、旁边的人被弹起；2.5 m 直接引爆触发；15 s 冷却内不重复；4.5 m 外不触发；瓶子有碰撞、旁边可走到",
                    hasCol && reach && fountA && launched && fountB && cooldown && farNo && hitFount[1],
                    "曼妥思 (" + pp.x.ToString("0") + ", " + pp.z.ToString("0.0") + ")，All=" + MentosPile.All.Count + "，瓶子碰撞=" + hasCol + "，1 m 内其它碰撞=[" + string.Join(",", blockers.ToArray()) + "]，NavMesh 可达=" + reach
                    + "；砸中瓶子：旧行为弹到 " + hitDist[0].ToString("0.0") + " m 外、喷泉=" + hitFount[0] + " → 修好后停在 " + hitDist[1].ToString("0.0") + " m、喷泉=" + hitFount[1] + "；吊射到脚下：手雷停在离瓶子 " + landDist.ToString("0.0") + " m 处，喷泉=" + fountA + "，队友最高 y=" + mateTop.ToString("0.0") + "（被弹起=" + launched + "）；直接引爆=" + fountB + "，冷却=" + cooldown + "，5.5 m 不触发=" + farNo);
            }

            // ---------------- M57：第一人称左手动作 —— 6 个技能都有、时长 ≤ 锁定时间、"按下即生效"的 4 个技能动作期间枪仍可开火
            string durs = ""; bool durOk = true, fireOk = true, handMoved = true, anyArm = false; int plays0 = SkillHand.Plays;
            foreach (var r in V23Roles)
            {
                p.ChangeRole(r); p.Respawn(c, 90); p.scripted = true; Hush(p); p.skillCharges = 2;
                yield return null; yield return new WaitForSeconds(.1f);
                var parts = p.ViewParts; bool arm = parts != null && parts.armL != null && p.ViewGun != null; anyArm |= arm;
                Vector3 rest = arm ? p.ViewGun.InverseTransformPoint(parts.armL.position) : Vector3.zero;
                bool canBefore = p.CanFireNow;
                Vector3 tgt = p.transform.position + p.AimDir * 6f; if (r == Role.Rivet) Skills.CoverPlacementAt(p, Skills.CoverDesired(p), out tgt, out _); else tgt = Skills.GroundAt(tgt);
                bool cast = Skills.Cast(p, tgt);
                float dur = SkillHand.Duration(r), lockT = r == Role.Falcon ? Config.DashWindup : r == Role.Rivet ? Config.CoverDeploy : .4f;
                if (!cast || dur > lockT + 1e-4f) durOk = false;
                yield return new WaitForSeconds(dur * .5f);
                Vector3 mid = arm ? p.ViewGun.InverseTransformPoint(parts.armL.position) : Vector3.zero;
                float moved = (mid - rest).magnitude;
                if (arm && moved < .004f) handMoved = false;
                if (r != Role.Falcon && r != Role.Rivet && canBefore && !p.CanFireNow) fireOk = false;
                durs += r + " " + dur.ToString("0.00") + "/" + lockT.ToString("0.00") + "s 手移 " + (moved * 100).ToString("0.0") + "cm；";
                yield return new WaitForSeconds(dur * .5f + .3f);
                V23Cleanup();
            }
            Check("M57", "第一人称左手动作：6 个技能各有一段（汽水罐 / 爆米花机 / 布丁 / 签筒 / 打火机 / 棉花糖棒），时长 ≤ 现有锁定时间（冲刺 0.15、威化 0.6、其余 ≤ 0.4 s），按下即生效的 4 个技能动作期间可以开火",
                durOk && fireOk && handMoved && SkillHand.Plays >= plays0 + 6,
                durs + "有第一人称手臂=" + anyArm + "，开火不受影响=" + fireOk);

            // ---------------- M58：生效飘字（结果字 = 遥测有效判定）
            SkillPop.History.Clear();
            foe.Respawn(c + new Vector3(4, 0, 0), -90); Hush(foe);
            var fal = Npc(myTeam, 0); Hush(fal); fal.ChangeRole(Role.Falcon); Place(fal, c, 90); yield return null;
            Skills.DashFinished(fal); Skills.NotifyDamage(fal, foe, 10f);
            var cg = new GameObject("测试威化"); cg.transform.position = c + new Vector3(0, 0, -6); var dcv = cg.AddComponent<DeployedCover>(); dcv.Init(fal); yield return null; dcv.TakeDamage(70f, foe, false);
            var iw = Npc(myTeam, 1); Hush(iw); iw.ChangeRole(Role.Ironwall); Place(iw, c + new Vector3(0, 0, 6), 90); yield return null;
            ShieldWall sw = null; if (Skills.Cast(iw, iw.transform.position + Vector3.right * 5f)) foreach (var s0 in Object.FindObjectsOfType<ShieldWall>()) if (s0.owner == iw) sw = s0;
            if (sw != null) sw.TakeDamage(70f, foe, false);
            var dg = new GameObject("测试芥末签"); dg.transform.position = foe.Chest + Vector3.left * 3f; var dr = dg.AddComponent<Drone>(); dr.Init(fal, dg.transform.position, Vector3.up);
            var ng = new GameObject("测试棉花糖迷雾"); ng.transform.position = foe.transform.position; ng.AddComponent<NoiseField>().Init(fal); Skills.spawned.Add(ng); Skills.spawned.Add(dg);
            yield return new WaitForSeconds(Config.DroneScanDelay + .15f);
            bool choke = GameObject.Find("世界飘字 呛！") != null;
            yield return new WaitForSeconds(.3f);
            string hist = string.Join(" / ", SkillPop.History.ToArray());
            bool popsOk = hist.Contains("续杯！") && hist.Contains("威化挡住了！") && hist.Contains("布丁吃下") && hist.Contains("呛到") && hist.Contains("糊住了！") && cupResult == "生日快乐！";
            Check("M58", "生效飘字：续杯！/ 威化挡住了！/ 布丁吃下 N 伤害 / 呛到 N 人！（敌人头顶\"呛！\"）/ 生日快乐！/ 糊住了！—— 和 skillEffective 同一判定",
                popsOk && choke, hist + " / 纸杯=" + cupResult + "，头顶呛字=" + choke);
            V23Cleanup();

            // ---------------- M59：6 个大招的美食表现（施放瞬间 + 持续 + 结束），规则 / 生效时间不变；镜头反馈受"镜头震动幅度"控制
            var um = Npc(myTeam, 0); Hush(um); foe.Respawn(c + new Vector3(5, 0, 0), -90); Hush(foe);
            int casts0 = UltFx.Casts, ends0 = UltFx.Ends; string ultLog = ""; bool rulesOk = true;
            foreach (var r in V23Roles)
            {
                um.ChangeRole(r); Place(um, c, 90); um.pitch = 10f; yield return null;
                um.Food.ult = 1f; bool fired = um.Food.TryUlt(); bool ok = fired;
                switch (r)
                {
                    case Role.Rivet: { int walls = 0; foreach (var rw in Object.FindObjectsOfType<RiceWall>()) { var bx = rw.GetComponent<BoxCollider>(); if (bx != null && Mathf.Abs(bx.center.y - .7f) < .01f) walls++; } ok &= walls == 3; ultLog += "饭团墙 " + walls + " 面（碰撞第 1 帧就位）；"; break; }
                    case Role.Ironwall: ok &= Object.FindObjectOfType<JellyDome>() != null; ultLog += "果冻罩=" + ok + "；"; break;
                    case Role.Raven: ok &= foe.revealedUntil >= Time.time + 4.9f; ultLog += "全图显示 5 s=" + ok + "；"; break;
                    case Role.Ember: { var cb = Object.FindObjectOfType<CandleBarrage>(); ok &= cb != null && cb.Dropped == 0; ultLog += "蜡烛 0 帧未落=" + ok + "；"; break; }
                    case Role.Phantom: ok &= UltFx.FrostStorm.Live.Count > 0 && Mathf.Approximately(UltFx.FrostStorm.Life, 6f) && Mathf.Approximately(UltFx.FrostStorm.Radius, 11f); ultLog += "糖霜风暴 11 m / 6 s=" + ok + "；"; break;
                    default: ultLog += "汉堡冲撞=" + ok + "；"; break;
                }
                if (!ok) rulesOk = false;
                yield return new WaitForSeconds(.35f);
            }
            bool castOk = UltFx.Casts == casts0 + 6;
            yield return new WaitForSeconds(2.2f);   // 汉堡 0.9 s、蜡烛 6 × 0.33 s 结束
            bool endOk = UltFx.Ends >= ends0 + 2;
            V23Cleanup();
            float ss0 = Tune.ShakeScale;
            p.ChangeRole(Role.Rivet); p.Respawn(c, 90); p.scripted = true; Hush(p); yield return null;
            Tune.ShakeScale = 0f; p.Food.ult = 1f; p.Food.TryUlt(); yield return null; float pulse0 = p.UltFovPulse;
            yield return new WaitForSeconds(.8f); V23Cleanup();
            Tune.ShakeScale = 1f; p.Food.ult = 1f; p.Food.TryUlt(); yield return null; float pulse1 = p.UltFovPulse;
            Tune.ShakeScale = ss0;
            Check("M59", "大招美食表现：6 个大招施放都有专属特效 / 音效 / 播报，汉堡与蜡烛轰炸有\"结束\"表现；饭团墙碰撞第 1 帧就位、蜡烛 0 帧未落、全图显示仍 5 s、糖霜风暴 11 m / 6 s（规则不变）；镜头震动幅度 = 0 时没有视野脉冲",
                castOk && rulesOk && endOk && pulse0 < .01f && pulse1 > .5f,
                ultLog + "施放表现 " + (UltFx.Casts - casts0) + "/6，结束 " + (UltFx.Ends - ends0) + " 次，视野脉冲：幅度 0 → " + pulse0.ToString("0.00") + "°，幅度 1 → " + pulse1.ToString("0.00") + "°");
            V23Cleanup();

            // ---------------- M60：新音效 / 图标资源都在（程序合成 / 程序绘制，不引用 FPSRaw）
            string[] sfx = { "food_can_shake", "food_can_open", "food_burp", "food_wafer_crunch", "food_wafer_shatter", "food_jelly_boing", "food_bamboo_thunk", "food_sneeze", "food_fuse",
                "food_caramel_bubble", "food_marsh_puff", "food_poprock_loop", "food_burger_chomp", "food_rice_thud", "food_jelly_dome", "food_sushi_radar", "food_candle_whoosh", "food_frost_wind" };
            var missing = new List<string>(); foreach (var s1 in sfx) if (Sfx.Variants(s1) < 3) missing.Add(s1);
            int icons = 0; foreach (var r in V23Roles) if (Icons.FoodSkill(r) != null && Icons.Skill(r) == Icons.FoodSkill(r)) icons++;
            Check("M60", "V23 资源：18 组新合成音效（每组 3 个变体，走 Sfx 分组加载）+ 6 个美食技能图标（Icons.Skill 美食模式返回新图标）",
                missing.Count == 0 && icons == 6, "缺音效=[" + string.Join(",", missing.ToArray()) + "]，图标 " + icons + "/6");

            // ---------------- 收尾
            V23Cleanup(); UltWarn.Clear(); FoodTips.AutoEnabled = true; SkillPop.History.Clear();
            foreach (var mp in MentosPile.All) if (mp != null) mp.ResetRound();
            foreach (var kv in savedRoles) if (kv.Key.role != kv.Value) kv.Key.ChangeRole(kv.Value);
            p.scripted = false; Hush(p);
            foreach (var f in m.fighters) { f.Respawn(ar.SpawnPoint(f.team == m.AttackingTeam, f.slot), 0); Hush(f); }
            m.roundClock = Config.RoundTime; m.phase = Phase.Live;
            yield return null;
        }

        // V23 c 截图（60–74）：6 个技能施放瞬间（第一人称带 HUD）、6 个大招施放瞬间（外部机位）、曼妥思喷泉、蒸汽里被炸
        IEnumerator V23SkillShots(string dir)
        {
            var m = Match.I; var p = m.player; var cam = p.cam != null ? p.cam : Camera.main;
            if (p == null || cam == null) yield break;
            HudCaptureBegin.Allow = true;
            m.phase = Phase.Live; m.roundClock = Config.RoundTime; m.ui.ClearBanner();
            var savedRoles = new Dictionary<Fighter, Role>(); foreach (var f in m.fighters) savedRoles[f] = f.role;
            V23Cleanup(); UltWarn.Clear(); FoodTips.AutoEnabled = false;
            foreach (var f in m.fighters) { Hush(f); if (!f.human) f.Respawn(new Vector3(-60 + f.slot * 2, .05f, f.team == 0 ? -46f : -44f), 0); }
            int myTeam = p.team, foeTeam = 1 - p.team;
            System.Action<Vector3, Vector3, float> Ext = (pos, look, fov) => { p.enabled = false; p.SetViewModelVisible(false); cam.transform.SetParent(null); cam.transform.position = pos; cam.transform.LookAt(look); cam.fieldOfView = fov; };
            System.Action FP = () => { p.enabled = true; p.AttachCamera(); p.SetViewModelVisible(true); };
            Vector3 c = new Vector3(-40, .05f, 4);
            var foe = Npc(foeTeam, 0); Hush(foe);
            string[] names = { "falcon", "rivet", "ironwall", "raven", "ember", "phantom" };
            for (int i = 0; i < V23Roles.Length; i++)
            {
                var r = V23Roles[i];
                p.ChangeRole(r); p.Respawn(c, 90); p.scripted = true; Hush(p); FP(); p.skillCharges = 2; p.pitch = r == Role.Rivet || r == Role.Phantom ? 8f : 0f;
                foe.Respawn(c + new Vector3(11, 0, 1.5f), -90); Hush(foe);
                yield return new WaitForSeconds(.5f);
                Vector3 aimAt = p.transform.position + p.AimDir * 8f; if (r == Role.Rivet) Skills.CoverPlacementAt(p, Skills.CoverDesired(p), out aimAt, out _); else if (r == Role.Phantom) aimAt = Skills.GroundAt(aimAt);   // 和真人一样用预览落点（贴地）
                Skills.Cast(p, aimAt);
                float wait = r == Role.Falcon ? .2f : r == Role.Rivet ? .4f : r == Role.Raven ? .15f : .14f;
                yield return new WaitForSeconds(wait);
                yield return Grab(System.IO.Path.Combine(dir, "6" + i + "_skill_" + names[i] + ".png"));
                yield return new WaitForSeconds(.4f); V23Cleanup();
            }
            // 大招：队友在街上放，外部机位从侧前方看
            var um = Npc(myTeam, 0); Hush(um);
            for (int i = 0; i < V23Roles.Length; i++)
            {
                var r = V23Roles[i];
                um.ChangeRole(r); um.Respawn(c, 90); Hush(um); um.pitch = 12f;
                foe.Respawn(c + new Vector3(9, 0, 0), -90); Hush(foe);
                Vector3 look = r == Role.Phantom || r == Role.Ember ? c + new Vector3(10, 1, 0) : c + new Vector3(2, 1.2f, 0);
                bool wide = r == Role.Phantom || r == Role.Ember;
                Ext(wide ? c + new Vector3(1f, 7.5f, -11f) : c + new Vector3(3.5f, 3.2f, -9.5f), wide ? c + new Vector3(8f, 0, 0) : look, wide ? 70 : 62);
                yield return new WaitForSeconds(.4f);
                um.yaw = 90; um.pitch = 12f; um.Food.ult = 1f; um.Food.TryUlt();   // 俯角 12° → 大招落点在身前约 7.5 m
                float wait = r == Role.Rivet ? .3f : r == Role.Ember ? .55f : r == Role.Phantom ? .8f : r == Role.Raven ? .25f : .25f;
                yield return new WaitForSeconds(wait);
                yield return Grab(System.IO.Path.Combine(dir, (66 + i) + "_ult_" + names[i] + ".png"));
                yield return new WaitForSeconds(.5f); m.ui.ClearBanner(); V23Cleanup();
            }
            // 72 曼妥思喷泉
            MentosPile pile = null; foreach (var mp in MentosPile.All) if (mp != null && mp.transform.position.z > 0) pile = mp;
            if (pile != null)
            {
                Vector3 pp = pile.transform.position; pile.ResetRound();
                var mate = Npc(myTeam, 1); Hush(mate); Place(mate, pp + new Vector3(1.6f, .05f, 1.2f), -90); Place(um, pp + new Vector3(-6, .05f, 0), 90);
                Ext(pp + new Vector3(-7f, 3f, -7.5f), pp + new Vector3(0, 3f, 0), 70);
                yield return new WaitForSeconds(.3f);
                var g = Grenade.Throw(um, Vector3.zero); g.transform.position = pp + new Vector3(-2f, .2f, 0); g.Detonate();
                yield return new WaitForSeconds(.35f);
                yield return Grab(System.IO.Path.Combine(dir, "72_mentos_fountain.png"));
                yield return new WaitForSeconds(2.5f); pile.ResetRound();
            }
            // 73 蒸汽里被炸到：敌人站在火锅热气里，汽水罐手雷照样炸到（V23 之前炸不到）
            HotpotHazard pot = null; foreach (var h in HotpotHazard.All) if (h != null && h.transform.position.z > 0) pot = h;
            if (pot != null)
            {
                Vector3 hp = pot.transform.position;
                foe.Respawn(hp + new Vector3(1.6f, .05f, 0), -90); Hush(foe); foe.hp = Config.MaxHp; foe.armor = 0;
                Ext(hp + new Vector3(-3f, 2.4f, -6.5f), hp + new Vector3(1f, 1.4f, 0), 66);
                yield return new WaitForSeconds(.4f);
                bool hid = !Skills.SightClear(hp + new Vector3(-6, 1.6f, 0), foe.Chest, foe);
                var g = Grenade.Throw(um, Vector3.zero); g.transform.position = hp + new Vector3(2.6f, .3f, -1f); g.Detonate();
                yield return null;
                SkillPop.World(foe.Eye + Vector3.up * .6f, "蒸汽里也炸到了 -" + Mathf.RoundToInt(Config.MaxHp - foe.hp) + (hid ? "（看不见）" : ""), new Color(1f, .9f, .4f), foe.transform, 2f);
                yield return new WaitForSeconds(.2f);
                yield return Grab(System.IO.Path.Combine(dir, "73_steam_blast.png"));
                yield return new WaitForSeconds(.5f);
            }
            // 收尾
            V23Cleanup(); UltWarn.Clear(); m.ui.ClearBanner(); FoodTips.AutoEnabled = true;
            foreach (var kv in savedRoles) if (kv.Key.role != kv.Value) kv.Key.ChangeRole(kv.Value);
            p.scripted = false; Hush(p); FP(); p.Respawn(new Vector3(-44, .05f, 3.5f), 92);
            HudCaptureBegin.Allow = false;
            yield return null;
        }

        // ================================================================ V23 打磨（M77）：只改表现 —— 大招远景光柱、可乐喷泉 6 层、食材光柱加宽 + 描边、左手道具放大不进准星区、大地图图标 ×1.4
        IEnumerator V23PolishTests()
        {
            var m = Match.I; var p = m.player;
            if (p == null) { Check("M77", "V23 视觉打磨", false, "没有玩家角色"); yield break; }
            // 1 大招：M59 已施放 6 个大招，每个至少 1 根光柱；再手动放 1 根看对象确实生成
            UltFx.Beacon(p.transform.position + p.transform.forward * 8f, UltFx.JellyHot, Color.black, 10f, 2f, .5f);
            yield return null;
            bool beaconObj = GameObject.Find("大招光柱") != null;
            bool ultOk = UltFx.Beacons >= 7 && UltFx.BeaconMaxH >= 7f && beaconObj;
            // 2 可乐喷泉：1 s 后 6 层粒子都在（含顶部泡沫花），水柱是亮焦糖色（不是深棕）
            var cf = ColaFountain.Spawn(p.transform.position + p.transform.forward * 10f);
            yield return new WaitForSeconds(1f);
            int layers = 0, live = 0;
            foreach (var ps in cf.GetComponentsInChildren<ParticleSystem>()) { if (System.Array.IndexOf(ColaFountain.Layers, ps.name) >= 0 && ps.particleCount > 0) layers++; live += ps.particleCount; }
            var cc = ColaFountain.Caramel; float lum = cc.r * .3f + cc.g * .59f + cc.b * .11f;
            Object.Destroy(cf.gameObject);
            bool colaOk = layers == ColaFountain.Layers.Length && lum > .5f;
            // 3 食材光柱：主 ≥ 2.5 m + 深色描边，备选更窄
            bool beamOk = IngredientGuide.PrimaryWidth >= 2.5f && IngredientGuide.AltWidth < IngredientGuide.PrimaryWidth && IngredientGuide.HaloScale > 1f
                && (IngredientGuide.LastPrimaryWidth <= 0 || IngredientGuide.LastPrimaryWidth >= 2.5f);
            // 4 左手道具：6 个技能各放一次，道具 ≥ 6 cm，包围盒不进屏幕中心 ±12%
            int placed0 = SkillHand.PropPlaced; bool inCenter = false; float minSize = 9f; string centerRoles = "";
            foreach (var r in V23Roles)
            {
                SkillHand.Play(p, r);
                for (int i = 0; i < 6; i++) { yield return null; var h = p.GetComponent<SkillHand>(); if (h != null && h.PropInCenter()) { inCenter = true; centerRoles += r + " "; break; } }
                if (SkillHand.PropPlaced > placed0) minSize = Mathf.Min(minSize, SkillHand.LastPropSize);
                yield return new WaitForSeconds(.65f);
            }
            bool placed = SkillHand.PropPlaced > placed0;
            bool handOk = SkillHand.PropScale >= 2.4f && (!placed || (!inCenter && minSize >= .06f));
            // 5 大地图
            bool mapOk = MapUI.BigIconScale >= 1.3f && MapUI.BigIconScale <= 1.5f && MapBase.HatchBlend <= .15f && MapBase.ShadowMul < 1f && Mathf.Approximately(MapIcons.Scale, 1f);
            Check("M77", "V23 视觉打磨（只改表现）：每个大招有 Additive 远景光柱（≥ 7 m）；可乐喷泉 6 层（水柱 / 发光芯 / 泡沫 / 气泡 / 泡沫花 / 底部泡沫）且水柱是亮焦糖色；食材主光柱 ≥ 2.5 m 带深色描边、备选更窄；左手道具 ≥ 6 cm 且不进准星区；大地图图标 ×1.3–1.5、斜纹 ≤ 0.15、有投影",
                ultOk && colaOk && beamOk && handOk && mapOk,
                "光柱 " + UltFx.Beacons + " 根（最高 " + UltFx.BeaconMaxH.ToString("0") + " m）；喷泉 " + layers + "/" + ColaFountain.Layers.Length + " 层、" + live + " 个粒子、亮度 " + lum.ToString("0.00")
                + "；主光柱 " + IngredientGuide.PrimaryWidth + " m（最近一次实测 " + IngredientGuide.LastPrimaryWidth.ToString("0.0") + "）/ 备选 " + IngredientGuide.AltWidth + " m"
                + "；道具贴手 " + (SkillHand.PropPlaced - placed0) + " 帧、最小 " + (placed ? (minSize * 100).ToString("0") + " cm" : "-") + "、进准星区=" + inCenter + (centerRoles.Length > 0 ? "（" + centerRoles.Trim() + "）" : "")
                + "；大地图图标 ×" + MapUI.BigIconScale + "、斜纹 " + MapBase.HatchBlend);
            SkillPop.EdgeBubbles = 0;
        }
    }

    // V22：batchmode / 窗口被遮挡时 ScreenCapture 拿不到画面，只能走相机离屏渲染（没有 HUD）。
    // 做法：把相机画面放进一张 RT，在本帧 IMGUI 重绘开始时（depth 最高，最先画）把 RT 设为当前目标，
    // 所有 OnGUI（GameUI / CombatHud / FoodHud …）都画进这张 RT，最后一个（depth 最低）读回存盘。
    public class HudCaptureBegin : MonoBehaviour
    {
        public static bool Allow; public static RenderTexture target; public static int guiFrames;
        void OnGUI()
        {
            GUI.depth = 1000;
            if (Event.current.type != EventType.Repaint) return;
            guiFrames++;
            if (target != null) RenderTexture.active = target;
        }
    }
    public class HudCaptureEnd : MonoBehaviour
    {
        public static string file; public static bool done;
        void OnGUI()
        {
            GUI.depth = -1000;
            if (Event.current.type != EventType.Repaint || HudCaptureBegin.target == null || file == null) return;
            var rt = HudCaptureBegin.target;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.Destroy(tex);
            RenderTexture.active = null; HudCaptureBegin.target = null; file = null; done = true;
        }
    }
}
