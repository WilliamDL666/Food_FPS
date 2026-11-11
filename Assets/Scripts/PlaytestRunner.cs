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
            else if (mode == "shots") yield return Shots();
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
            Place(shooter, new Vector3(-50, .05f, 36), 90); Place(tgt, new Vector3(0, .05f, 36), -90);
            tgt.hp = 100; tgt.armor = 50;
            yield return new WaitForFixedUpdate(); yield return null;
            Bullet.Fire(shooter, shooter.Eye, shooter.Eye, (tgt.Chest - shooter.Eye).normalized, Color.white);
            yield return null;
            bool notYet = tgt.armor >= 50;
            yield return new WaitForSeconds(.6f);
            bool arrived = tgt.armor < 50 || tgt.hp < 100;
            Check("R02", "子弹可见且有飞行时间：50 m 外不是瞬间命中", notYet && arrived, "发射后第 1 帧未命中=" + notYet + "，" + shooter.W.id + " 弹速 " + shooter.W.bulletSpeed + " m/s，0.6 s 内命中=" + arrived);

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
            Place(raven, new Vector3(-34, .05f, 38), 90); Place(near, new Vector3(-26, .05f, 43), 180); Place(far, new Vector3(-14.2f, .05f, 40), -90);
            near.revealedUntil = far.revealedUntil = 0;
            yield return new WaitForFixedUpdate();
            // V6：夜鸦改为射出侦测箭 —— 箭矢飞行后钉在地面，成为扫描信标
            Skills.ShootArrow(raven, Skills.ArrowVelocityTo(raven.Eye, new Vector3(-30, 0, 40)));
            Drone drone = null; float fly = Time.time;
            while (drone == null && Time.time - fly < 2f) { drone = FindObjectOfType<Drone>(); yield return null; }
            bool stuck = drone != null && Vector3.Distance(drone.transform.position, new Vector3(-30, 0, 40)) < 1.5f && drone.GetComponent<Arrow>() == null;
            float t0 = drone != null ? drone.Landed : Time.time; bool early = true;
            // frame-rate independent: before the scan time the enemy must never be revealed
            while (Time.time - t0 < Config.DroneScanDelay + .4f)
            {
                if (Time.time - t0 < Config.DroneScanDelay - .05f && near.Revealed) early = false;
                yield return null;
            }
            bool nearSeen = near.Revealed, farHidden = !far.Revealed;
            var dGo2 = new GameObject("test drone 2"); dGo2.transform.localScale = new Vector3(.38f, .04f, .38f);
            var drone2 = dGo2.AddComponent<Drone>(); drone2.Init(raven, new Vector3(-31, .1f, 41));
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
            bool walk = NavMesh.CalculatePath(c + new Vector3(0, 0, -2.2f), c + new Vector3(0, 0, 2.2f), NavMesh.AllAreas, p2) && p2.status == NavMeshPathStatus.PathComplete;
            Check("T04", "焚芯爆破轻型掩体：碰撞同步移除、可通行；角色伤害≤" + Config.BlastMaxDamage, gone && taken > 0 && taken <= Config.BlastMaxDamage + .01f && walk, "摧毁=" + gone + "，2.1 m 敌人受伤 " + taken.ToString("0.0") + "，可通行=" + walk);
            crate.Restore();

            // --- interactables
            var shutter = m.arena.shutters[0];
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
            Check("M01", "弹射板高台：市场楼顶 / 水塔 / 空中连廊都能弹上去并站稳（无楼梯）", allLanded && landings.Length > 0, landings.ToString());
            if (flyBot != null) flyBot.enabled = true;

            Shutter northGate = null; foreach (var sh in m.arena.shutters) if (sh.gate && sh.transform.position.z > 0) northGate = sh;
            var gp = new NavMeshPath(); System.Func<float> len = () =>
            {
                if (!NavMesh.CalculatePath(new Vector3(-6, 0, 40), new Vector3(14, 0, 40), NavMesh.AllAreas, gp) || gp.status != NavMeshPathStatus.PathComplete) return 999f;
                float L = 0; for (int i = 1; i < gp.corners.Length; i++) L += Vector3.Distance(gp.corners[i - 1], gp.corners[i]); return L;
            };
            yield return null;
            float closedLen = len(); bool gateBlocks = northGate != null && northGate.GetComponent<Collider>().enabled;
            if (northGate != null) { northGate.SetOpen(true); yield return new WaitForSeconds(2.6f); }
            float openLen = len();
            Check("M02", "北长街铁门：关闭时长街被封死（只能绕中路），打开后直通", gateBlocks && closedLen > 45f && openLen < 26f, "关门时绕行 " + closedLen.ToString("0") + " m，开门后 " + openLen.ToString("0") + " m");
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
                    bool steamBlocks = !Skills.LineClear(new Vector3(8, 2.2f, -40.5f), new Vector3(8, 2.2f, -30.5f), null) && Skills.LineClear(new Vector3(0, 1.6f, -41f), new Vector3(0, 1.6f, -47f), null);
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
                    SteamCloud.Suppress(2f); bool v18cooled = Skills.LineClear(new Vector3(8, 2.2f, -40.5f), new Vector3(8, 2.2f, -30.5f), null);
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
                    // Grow mode: a full-health player can use a snack for growth without losing health.
                    Snack growSnack = null;
                    foreach (var it in Interactable.All) if (it is Snack sn && !sn.Eaten) { growSnack = sn; break; }
                    var grower = Other(def, 2);
                    var previousMode = Match.Mode;
                    float previousHp = grower.hp, previousArmor = grower.armor;
                    int previousLevel = grower.Food.growLevel;
                    Match.Mode = GameMode.Grow;
                    grower.hp = Config.MaxHp;
                    bool growUsable = growSnack != null && growSnack.Usable(grower);
                    bool growConsumed = growUsable && growSnack.Hold(grower, growSnack.holdTime);
                    bool growCorrect = growConsumed && grower.Food.growLevel == previousLevel + 1 && Mathf.Approximately(grower.hp, Config.MaxHp);
                    if (growSnack != null) growSnack.ResetRound();
                    Match.Mode = GameMode.Bomb;
                    bool ordinaryFullBlocked = growSnack != null && !growSnack.Usable(grower);
                    Match.Mode = previousMode;
                    grower.hp = previousHp; grower.armor = previousArmor; grower.Food.growLevel = previousLevel; grower.Food.ApplyGrowScale();
                    Check("M19", "变大模式满血也能吃零食升级，且不会为了吃零食损失生命；普通模式满血不能吃", growCorrect && ordinaryFullBlocked,
                        "可交互=" + growUsable + " 已吃掉=" + growConsumed + " 升级且生命不变=" + growCorrect + " 普通模式满血不可吃=" + ordinaryFullBlocked);
                }
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
            Place(carrier, new Vector3(-20, .05f, 0), 0);
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
            Place(c1, new Vector3(-30, .05f, 0), 0); Place(c2, new Vector3(-20, .05f, 0), 0);
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

        IEnumerator MatchRun()
        {
            Time.timeScale = 4f;
            float startReal = Time.realtimeSinceStartup;
            while (Match.I.phase != Phase.MatchEnd && Time.realtimeSinceStartup - startReal < 1100f) yield return null;
            var m = Match.I;
            Debug.Log("NEON_MATCH_DONE phase=" + m.phase + " score=" + m.roundsWon[0] + ":" + m.roundsWon[1] + " rounds=" + m.records.Count);
            foreach (var r in m.records)
                Debug.Log("NEON_ROUND " + r.round + " att=" + r.attackers + " winner=" + r.winner + " reason=" + r.reason + " dur=" + r.duration.ToString("0.0") + " plant=" + r.plantSite + "@" + r.plantTime.ToString("0.0")
                    + " contact=" + r.firstContact.ToString("0.0") + "@" + r.firstContactZone + " kills=" + r.kills[0] + "/" + r.kills[1] + " skills=" + string.Join("/", r.skillUses) + " eff=" + string.Join("/", r.skillEffective) + " routes=" + r.routes);
            if (m.phase != Phase.MatchEnd) m.WriteTelemetry();
            Application.Quit(m.phase == Phase.MatchEnd ? 0 : 2);
        }

        // Capture exactly what is on screen (same render path as the game) at the end of the frame.
        static IEnumerator Grab(string file)
        {
            if (Camera.main != null) Camera.main.targetTexture = null;
            yield return null;
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
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
                ("23_hotpot_station", new Vector3(-22, 2f, -5.5f), new Vector3(-26, 1f, -8.3f), 60),
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
            // skill FX shot
            var iron = Get(0, Role.Ironwall); Place(iron, new Vector3(-16, .05f, 2.5f), -90); iron.skillCharges = 1; Skills.Cast(iron, Vector3.zero);
            var ph = Get(1, Role.Phantom); Place(ph, new Vector3(-12, .05f, -3), -90); ph.skillCharges = 1; Skills.Cast(ph, new Vector3(-12, 0, -3.5f));
            var em = Get(1, Role.Ember); new GameObject("fx charge").AddComponent<BlastCharge>().Init(em, new Vector3(-9, .1f, 4));
            if (p != null) p.SetViewModelVisible(false);
            cam.transform.position = new Vector3(-27, 2.4f, -5.5f); cam.transform.LookAt(new Vector3(-12, 1.2f, 0.5f)); cam.fieldOfView = 70;
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
            }
            // V9 menus
            m.ui.preview = 1; for (int f = 0; f < 3; f++) yield return null;
            yield return Grab(System.IO.Path.Combine(dir, "17_main_menu.png"));
            m.ui.preview = 2; yield return Grab(System.IO.Path.Combine(dir, "18_settings.png"));
            m.ui.preview = 3; yield return Grab(System.IO.Path.Combine(dir, "24_help.png"));
            if (ToonAssets.Available) { m.ui.preview = 1; FoodHud.showCollection = true; yield return null; yield return Grab(System.IO.Path.Combine(dir, "25_collection.png")); FoodHud.showCollection = false; }
            m.ui.preview = 0; if (p != null) p.SetViewModelVisible(true);
            m.ui.PreviewPause(true); yield return Grab(System.IO.Path.Combine(dir, "19_pause.png")); m.ui.PreviewPause(false);
            Debug.Log("NEON_SHOTS_DONE " + dir);
            Application.Quit(0);
        }
    }
}
