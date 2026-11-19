using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace NeonSiege
{
    // ================================================================ V25 自动化测试（M115 起）：奶茶机 / 变身外壳 / 监控接管 / 干员加料 / 腿部伤害 / 击杀表 / 回血炉
    public static class V25Tests
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
        static void Park(Fighter f) { var m = Match.I; f.Respawn(m.arena.SpawnPoint(f.team == m.AttackingTeam, f.slot), 0); Hush(f); }
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
            var A = Npc(att, 0); var D = Npc(def, 0);
            yield return null;

            // ---------------- M115：奶茶机 甲（加料奶茶）
            {
                bool usable = false, boba = false, gren = false, reload = false, slow = false;
                KitchenStation mt = null; foreach (var it in Interactable.All) if (it is KitchenStation ks && ks.kind == KitchenStation.Kind.MilkTea) mt = ks;
                if (mt != null && A != null && D != null)
                {
                    mt.ResetRound(); A.ammo = A.W.magazine; A.grenades = 1;
                    usable = mt.Usable(A);   // 弹匣满也能喝
                    for (float w = 0; w < 1.2f; w += Time.deltaTime) { if (mt.Hold(A, Time.deltaTime)) break; yield return null; }
                    boba = V25.Boba(A); gren = A.grenades == Mathf.Min(2, V25.MaxGrenades); reload = Mathf.Approximately(V25.ReloadMul(A), V25.BobaReload);
                    V25.OnGunHit(A, D, D.Chest); slow = Mathf.Approximately(V25.SpeedMul(D), V25.BobaSlow);
                    mt.ResetRound();
                }
                Check("M115", "奶茶机 甲（V25）：弹匣满也能喝；喝完 " + V25.BobaTime + " s 珍珠弹（打中减速 ×" + V25.BobaSlow + "）、换弹 ×" + V25.BobaReload + "、+1 手雷（上限 " + V25.MaxGrenades + "）", usable && boba && gren && reload && slow,
                    "能喝=" + usable + " 珍珠弹=" + boba + " 手雷=" + gren + " 换弹=" + reload + " 减速=" + slow);
            }

            // ---------------- M116：变身外壳 + 结束碎掉
            {
                bool on = false, off = false; int b0 = V25.FormBreaks;
                if (A != null && ToonAssets.Available)
                {
                    A.Food.Become(FoodForm.Burger); yield return null;
                    on = A.GetComponent<FormShell>() != null;
                    A.Food.EndForm(true); yield return null;
                    off = A.GetComponent<FormShell>() == null && V25.FormBreaks == b0 + 1;
                }
                Check("M116", "变身表现（V25）：变身时全身套上食物外壳（别人看得到），结束时外壳碎掉", on && off, "套上=" + on + "，碎掉=" + off);
            }

            // ---------------- M117：监控 —— 扇形、接管、守方打掉糖丝
            {
                bool fans = false, hij = false, broke = false; SecurityCam cam = null;
                if (m.arena.cams.Count > 0) { cam = m.arena.cams[0]; int n = 0; foreach (var c in m.arena.cams) if (c != null && c.GetComponent<CamFan>() != null) n++; fans = n == m.arena.cams.Count; }
                if (cam != null && A != null && D != null)
                {
                    cam.ResetRound(); V25.Hijack(cam, A); hij = V25.HijackedBy(cam) == A.team;
                    cam.TakeDamage(10f, D, false);
                    broke = V25.HijackedBy(cam) < 0 && cam.Working;
                }
                Check("M117", "监控（V25）：每个监控地上画扫描扇形；幻频糖丝接管 " + V25.HijackTime + " s（反过来扫守方）；守方能打掉糖丝（监控本身不坏）",
                    fans && hij && broke, "扇形=" + fans + " 接管=" + hij + " 打掉糖丝=" + broke);
            }

            // ---------------- M118：威化墙把人推开 / 果冻罩里敌方子弹变慢
            {
                bool pushed = false, slowB = false;
                if (A != null && D != null)
                {
                    Vector3 at = A.transform.position;
                    var holder = new GameObject("测试威化位置"); holder.transform.position = at; holder.transform.rotation = Quaternion.Euler(0, A.yaw, 0);
                    V25.CoverPushOut(holder.transform, Config.CoverWidth, Config.CoverHeight, .35f);
                    pushed = Vector3.Distance(A.transform.position, at) > .3f; Object.Destroy(holder);
                    JellyDome.Spawn(D.transform.position, D); yield return null;
                    var bgo = new GameObject("测试子弹"); bgo.transform.position = D.transform.position + Vector3.up; var b = bgo.AddComponent<Bullet>(); b.owner = A;
                    slowB = Mathf.Approximately(V25.BulletSlow(b), V25.DomeBulletSlow); Object.Destroy(bgo);
                    foreach (var d in JellyDome.Live.ToArray()) if (d != null) Object.Destroy(d.gameObject);
                    Park(A);
                }
                Check("M118", "干员加料（V25）：威化墙放下时把站在里面的人推开；果冻罩里敌方子弹 ×" + V25.DomeBulletSlow, pushed && slowB, "推开=" + pushed + " 子弹变慢=" + slowB);
            }

            // ---------------- M119：SMG-7 / AR-9 打 3 级甲：打胸比打腿少发数
            {
                bool ok = true; string s = "";
                foreach (var id in new[] { "SMG-7", "AR-9" })
                {
                    var w = Gun(id); if (w == null) { ok = false; continue; }
                    Ballistics.Ttk(w, 3, 2, 5f, HitPart.Chest, out int chest); Ballistics.Ttk(w, 3, 2, 5f, HitPart.Leg, out int leg);
                    if (chest >= leg) ok = false; s += id + " 胸 " + chest + " / 腿 " + leg + " 发；";
                }
                Check("M119", "腿部伤害（V25）：SMG-7、AR-9 打 3 级甲时打胸比打腿少发数（以前一样）", ok, s);
            }

            // ---------------- M120：击杀表 + 回血炉
            {
                var sb = new System.Text.StringBuilder(); V25.AppendKillTable(sb);
                bool table = sb.ToString().Contains("kills,round,killer,victim,weapon,part,distance_m,ttk_s,headshot");
                int heal = LinkPlus.HealPots().Count;
                Check("M120", "数据和地图（V25）：遥测 CSV 有击杀表（距离 / 武器 / 部位 / 击杀用时）；回血炉恢复到 ≥ 2 个", table && heal >= 2, "击杀表=" + table + "（已记 " + V25.Kills.Count + " 条），回血炉 " + heal + " 个");
            }

            // ---------------- 收尾
            Skills.ClearRound(); FoodTips.AutoEnabled = true;
            if (p != null) { p.scripted = false; Hush(p); }
            foreach (var f in m.fighters) { if (ToonAssets.Available) f.Food.EndForm(false); Park(f); }
            m.roundClock = Config.RoundTime; m.phase = Phase.Live;
            yield return null;
        }

        // ================================================================ V25 截图（120–127）：变身外壳 / 变身第一人称 / 监控扇形 / 被监控看到 / 看监控 / 回合战报 / 蛋糕影子
        public static IEnumerator Shots(string dir, System.Func<string, IEnumerator> Grab)
        {
            var m = Match.I; var p = m.player; var ar = m.arena;
            if (p == null || p.cam == null || !ToonAssets.Available) yield break;
            HudCaptureBegin.Allow = true;
            m.phase = Phase.Live; m.roundClock = Config.RoundTime; m.ui.ClearBanner();
            FoodTips.AutoEnabled = false; Skills.ClearRound();
            foreach (var f in m.fighters) { Hush(f); if (!f.human) f.Respawn(new Vector3(-60 + f.slot * 2, .05f, f.team == 0 ? -46f : -44f), 0); }
            int foeTeam = p.team == m.AttackingTeam ? m.DefendingTeam : m.AttackingTeam;
            var foe = Npc(foeTeam, 0); var foe2 = Npc(foeTeam, 1);
            var shotCam = new GameObject("V25 截图相机").AddComponent<Camera>(); shotCam.depth = 100; shotCam.nearClipPlane = .05f; shotCam.enabled = false;
            var off = new List<Camera>();
            System.Action<Vector3, Vector3, float> Ext = (pos, look, fov) =>
            {
                p.SetViewModelVisible(false);
                foreach (var c in Camera.allCameras) if (c != shotCam && c.enabled && c.targetTexture == null) { c.enabled = false; off.Add(c); }
                shotCam.enabled = true; shotCam.transform.position = pos; shotCam.transform.LookAt(look); shotCam.fieldOfView = fov;
            };
            System.Action FP = () => { shotCam.enabled = false; foreach (var c in off) if (c != null) c.enabled = true; off.Clear(); p.enabled = true; p.AttachCamera(); p.SetViewModelVisible(true); };
            string F(string n) { return System.IO.Path.Combine(dir, n + ".png"); }
            p.Respawn(new Vector3(16, .05f, -6), 0); p.scripted = true; Hush(p); FP();

            // ---- 120 变身外壳（别人看）：汉堡 + 奶茶
            if (foe != null && foe2 != null)
            {
                Vector3 c0 = p.transform.position + new Vector3(0, 0, 6f);
                Place(foe, c0 + Vector3.left * 1.4f, 180); Place(foe2, c0 + Vector3.right * 1.4f, 180);
                foe.Food.Become(FoodForm.Burger); foe2.Food.Become(FoodForm.MilkTea);
                yield return new WaitForSeconds(.8f);
                Ext(c0 + new Vector3(0, 1.8f, -4.5f), c0 + Vector3.up * 1.1f, 55); yield return new WaitForSeconds(.1f);
                yield return Grab(F("120_form_shell"));
                foe.Food.EndForm(false); foe2.Food.EndForm(false); FP();
            }
            // ---- 121 变身第一人称：大字 + 四角菜色框 + 倒计时
            {
                p.Food.Become(FoodForm.IceCream); yield return new WaitForSeconds(.35f);
                yield return Grab(F("121_form_fp"));
                p.Food.EndForm(false); yield return null;
            }
            // ---- 122 监控扇形（外景）+ 123 被监控看到（第一人称红眼睛）
            SecurityCam sc = null; foreach (var c in ar.cams) if (c != null && c.Working) { sc = c; break; }
            if (sc != null)
            {
                Vector3 cp = sc.transform.position, fwd = Quaternion.Euler(0, sc.yawCenter, 0) * Vector3.forward;
                Vector3 g = Skills.GroundAt(cp + fwd * 8f + Vector3.up);
                var cf = sc.GetComponent<CamFan>();
                Debug.Log("NEON_V25FAN cam=" + cp + " fan=" + (cf == null ? "none" : cf.Fan == null ? "null" : cf.Fan.gameObject.activeInHierarchy + " at " + cf.Fan.position + " yaw " + cf.Fan.eulerAngles.y.ToString("0")) + " col=" + (cf != null ? cf.Col.ToString() : "-") + " hij=" + V25.HijackedBy(sc) + " gum=" + LinkPlus.Gummed(sc));
                Ext(cp + Vector3.up * 2.5f - fwd * 3f, g, 70); yield return new WaitForSeconds(.3f);   // 站在监控身后往它看的方向拍：扇形一定在画面里
                yield return Grab(F("122_cam_fan"));
                FP(); V25Hud.Spotted(); yield return new WaitForSeconds(.15f);
                yield return Grab(F("123_cam_spotted"));
                // ---- 124 看监控画面（攻方 = 先让幻频糖丝接管；守方 = 自家监控）
                if (p.team != m.DefendingTeam) V25.Hijack(sc, p);
                if (CamView.Open(p)) { yield return new WaitForSeconds(.4f); yield return Grab(F("124_cam_view")); CamView.Close(); }
                yield return null; FP();
            }
            // ---- 125 回合战报
            {
                var A = Npc(m.AttackingTeam, 0); var D = Npc(m.DefendingTeam, 0);
                int r = m.round;
                V25.Kills.Add(new V25.KillRow { round = r, killer = A != null ? A.Hero.name : "猎隼", victim = D != null ? D.Hero.name : "铁幕", weapon = "AR-9", part = "胸", dist = 22f, ttk = .52f, killerTeam = m.AttackingTeam, victimTeam = m.DefendingTeam });
                V25.Kills.Add(new V25.KillRow { round = r, killer = D != null ? D.Hero.name : "铁幕", victim = A != null ? A.Hero.name : "猎隼", weapon = "DMR-3", part = "头", head = true, dist = 41f, ttk = .2f, killerTeam = m.DefendingTeam, victimTeam = m.AttackingTeam });
                m.phase = Phase.RoundEnd; V25Hud.ForceReport(r, 2f); yield return new WaitForSeconds(.2f);
                yield return Grab(F("125_round_report"));
                m.phase = Phase.Live; V25Hud.ForceReport(r, 0f);
                V25.Kills.RemoveAll(k => k.round == r && (k.weapon == "AR-9" || k.weapon == "DMR-3") && (k.ttk == .52f || k.ttk == .2f));
            }
            // ---- 126 蛋糕影子（焚芯大招落下前）
            {
                Vector3 at = Skills.GroundAt(p.transform.position + new Vector3(0, 1, 14f));
                V25.CakeShadow(at); yield return new WaitForSeconds(.7f);
                Ext(at + new Vector3(0, 4f, -16f), at + Vector3.up * 7f, 62); yield return new WaitForSeconds(.05f);
                yield return Grab(F("126_cake_shadow"));
            }
            // ---- 收尾
            Skills.ClearRound(); FoodTips.AutoEnabled = true;
            FP(); p.scripted = false; Hush(p); Object.Destroy(shotCam.gameObject);
            foreach (var f in m.fighters) Park(f);
            yield return null;
        }
    }
}
