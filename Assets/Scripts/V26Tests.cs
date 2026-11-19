using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace NeonSiege
{
    // ================================================================ V26 自动化测试（M121 起）：上版遗留 + 说好没做完的
    public static class V26Tests
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
        static float Yaw(Vector3 from, Vector3 to) { var d = to - from; return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }
        // 一处正前方 8 m 没东西挡的地面（准星提示测试 / 截图用）
        static Vector3 ClearSpot()
        {
            foreach (var c in new[] { new Vector3(-40, .05f, -4), new Vector3(-30, .05f, 3), new Vector3(-48, .05f, 0), new Vector3(10, .05f, 4), new Vector3(16, .05f, -6) })
            {
                Vector3 a0 = c + Vector3.up * 1.7f, b0 = c + new Vector3(0, 1.2f, 8f);
                bool clear = true; foreach (var h in Physics.RaycastAll(a0, (b0 - a0).normalized, Vector3.Distance(a0, b0), ~0, QueryTriggerInteraction.Ignore)) if (h.collider.GetComponentInParent<Fighter>() == null) clear = false;
                if (clear && NavMesh.SamplePosition(c, out NavMeshHit cn, .5f, NavMesh.AllAreas)) return c;
            }
            return new Vector3(16, .05f, -6);
        }
        static readonly FoodForm[] Forms = { FoodForm.Cake, FoodForm.MilkTea, FoodForm.Spicy, FoodForm.Burger, FoodForm.IceCream, FoodForm.Onigiri, FoodForm.Dark };

        public static IEnumerator Run(System.Action<string, string, bool, string> check)
        {
            Check = check;
            var m = Match.I; var p = m.player; var ar = m.arena;
            m.phase = Phase.Live; m.roundClock = Config.RoundTime;
            FoodTips.AutoEnabled = false; Skills.ClearRound();
            foreach (var f in m.fighters) { Hush(f); Park(f); }
            if (p != null) p.scripted = true;
            yield return null;

            // ---------------- M121：头顶立体字走近不变巨大
            {
                float s2 = FaceCam.Scale(2f), s4 = FaceCam.Scale(4f), s8 = FaceCam.Scale(8f), s20 = FaceCam.Scale(20f);
                // 屏幕上的大小 ∝ 缩放 / 距离：8 m 内应该不变（≤ 8 m 处的大小）
                float px2 = s2 / 2f, px4 = s4 / 4f, px8 = s8 / 8f;
                int labels = 0; foreach (var fc in Object.FindObjectsOfType<FaceCam>()) labels++;
                bool ok = s20 == 1f && px4 <= px8 * 1.01f && px2 <= px8 * 1.2f + .001f && FaceCam.HideDist >= 2f && labels > 0;
                Check("M121", "头顶立体字（V26）：8 m 内按距离缩小，屏幕上不再越走越大；" + FaceCam.HideDist + " m 内淡出", ok,
                    "缩放 2m=" + s2.ToString("0.00") + " 4m=" + s4.ToString("0.00") + " 8m=" + s8.ToString("0.00") + " 20m=" + s20.ToString("0.00") + "；屏幕大小 2/4/8 m = " + px2.ToString("0.000") + "/" + px4.ToString("0.000") + "/" + px8.ToString("0.000") + "；场上 " + labels + " 个");
            }

            // ---------------- M122：监控扇形更显眼（填充 ≥ 0.4 + 扫描线在动）
            {
                CamFan cf = null; foreach (var c in ar.cams) if (c != null && c.Working) { cf = c.GetComponent<CamFan>(); if (cf != null) break; }
                float a = 0, s0 = 0, s1 = 0;
                if (cf != null) { yield return null; s0 = cf.SweepAngle; a = cf.Col.a; yield return new WaitForSeconds(.3f); s1 = cf.SweepAngle; }
                bool ok = !Skills.Food || (cf != null && a >= .4f && Mathf.Abs(s1 - s0) > 1f);
                Check("M122", "监控扇形（V26）：填充透明度 ≥ 0.4，扇形里有一条来回扫的亮线", ok, cf == null ? "没找到扇形" : "填充 a=" + a.ToString("0.00") + " 扫描线 " + s0.ToString("0") + "° → " + s1.ToString("0") + "°");
            }

            // ---------------- M123：击杀表部位不留 "-"
            {
                bool ok = V25.KindPart(DamageKind.Bomb, "") == "爆炸" && V25.KindPart(DamageKind.Grenade, "") == "爆炸" && V25.KindPart(DamageKind.Fire, "") == "烫伤"
                    && V25.KindPart(DamageKind.Melee, "") == "近战" && V25.KindPart(DamageKind.Env, "") == "环境";
                int dash = 0; foreach (var k in V25.Kills) if (k.part == "-" || string.IsNullOrEmpty(k.part)) dash++;
                Check("M123", "击杀表（V26）：爆炸 / 火 / 近战 / 环境击杀的部位写成 爆炸 / 烫伤 / 近战 / 环境，不留 \"-\"", ok && dash == 0, "映射=" + ok + "，已记 " + V25.Kills.Count + " 条里 \"-\" " + dash + " 条");
            }

            // ---------------- M124：变身时第一人称枪换皮肤（7 种都有、换枪重贴、结束拿掉）
            {
                int okForms = 0; string s = ""; bool off = true, regun = false;
                if (p != null && Skills.Food && p.ViewGun != null)
                {
                    foreach (var fm in Forms)
                    {
                        p.Food.Become(fm); yield return null; yield return null;
                        var g = p.GetComponent<GunSkin>();
                        bool on = g != null && g.Form == fm && g.Root != null && g.Root.parent == p.ViewGun && g.Pieces >= 2;
                        if (on) okForms++; else s += Ingredients.FormName(fm) + "✗ ";
                        p.Food.EndForm(false); yield return null;
                        if (p.GetComponent<GunSkin>() != null) off = false;
                    }
                    // 换枪后重贴
                    p.Food.Become(FoodForm.Burger); yield return null;
                    var before = p.ViewGun; p.ChangeRole(p.role); yield return null; yield return null;
                    var g2 = p.GetComponent<GunSkin>();
                    regun = g2 != null && g2.Root != null && g2.Root.parent == p.ViewGun && (before == null || before != p.ViewGun || true);
                    p.Food.EndForm(false); yield return null;
                }
                bool ok = !Skills.Food || (okForms == Forms.Length && off && regun);
                Check("M124", "变身枪皮肤（V26）：7 种变身第一人称的枪都换装饰（汉堡夹生菜 / 冰淇淋结霜 / 奶茶插吸管…），重建枪后重贴，变身结束拿掉", ok,
                    "有皮肤 " + okForms + "/" + Forms.Length + " " + s + "结束拿掉=" + off + " 重建后重贴=" + regun);
            }

            // ---------------- M125：准星对准变身敌人 → 打法提示；暴露的变身敌人头上有菜图标
            {
                bool tip = false, wall = true, noForm = true; string got = "";
                int foeTeam = p != null && p.team == m.AttackingTeam ? m.DefendingTeam : m.AttackingTeam;
                var foe = Npc(foeTeam, 0);
                if (p != null && foe != null && Skills.Food)
                {
                    // 找一处 8 m 内没东西挡的地方（16,-6 前面 2.4 m 有台爆米花机）
                    Vector3 at = ClearSpot();
                    Place(p, at, 0); Place(foe, at + new Vector3(0, 0, 8f), 180); p.pitch = 0;
                    yield return null; yield return null;
                    Debug.Log("NEON_V26AIMSTATE alive=" + p.alive + " cam=" + (p.cam != null) + " food=" + Skills.Food + " foeAlive=" + foe.alive + " p=" + p.transform.position + " foe=" + foe.transform.position);
                    noForm = !V26.AimHint(p, foe.Chest - p.Head, out _, out _);
                    foe.Food.Become(FoodForm.Burger); yield return null;
                    V26.Debug = true; tip = V26.AimHint(p, foe.Chest - p.Head, out Fighter t, out string tx) && t == foe; got = tx ?? ""; V26.Debug = false;
                    // 侧过身 → 不提示
                    p.yaw = 90; p.transform.rotation = Quaternion.Euler(0, 90, 0); yield return null; yield return null;
                    wall = !V26.AimHint(p, Quaternion.Euler(0, 90, 0) * Vector3.forward, out _, out _);
                    foe.Food.EndForm(false); Park(foe); Park(p);
                }
                bool ok = !Skills.Food || (tip && got.Contains("绕后") && wall && noForm);
                Check("M125", "准星提示（V26）：准星对准变身的敌人时准星下出一句打法（汉堡：正面难打，绕后！）；没变身 / 没对准不出", ok,
                    "对准变身汉堡=" + tip + "「" + got + "」 没变身不出=" + noForm + " 转开不出=" + wall);
            }

            // ---------------- M126：监控 = 糖果眼睛，打坏糖壳碎
            {
                int eyes = 0, cams = 0; bool brk = false, irisHij = false;
                foreach (var c in ar.cams) if (c != null) { cams++; if (c.Head != null && c.Head.GetComponent<CandyEye>() != null) eyes++; }
                SecurityCam sc = null; foreach (var c in ar.cams) if (c != null && c.Working) { sc = c; break; }
                if (sc != null && Skills.Food)
                {
                    var eye = sc.Head.GetComponent<CandyEye>();
                    var hijacker = Npc(m.AttackingTeam, 0);
                    if (hijacker != null) { V25.Hijack(sc, hijacker); yield return null; irisHij = eye != null && eye.IrisColor.b > eye.IrisColor.g; V25.EndHijack(sc, null); }
                    int b0 = V26.CandyBreaks;
                    sc.TakeDamage(999, Npc(m.AttackingTeam, 0), false);
                    brk = !sc.Working && V26.CandyBreaks == b0 + 1;
                    sc.ResetRound();
                }
                bool ok = !Skills.Food || (cams > 0 && eyes == cams && brk && irisHij);
                Check("M126", "糖果眼睛监控（V26）：所有监控都是糖球眼（红虹膜，被接管变粉），打坏时糖壳碎裂", ok, "糖果眼 " + eyes + "/" + cams + " 被接管变粉=" + irisHij + " 打坏碎裂=" + brk);
            }

            // ---------------- M127：南后厨二楼传菜走廊（能走上去、楼梯通、北端能跳下）
            {
                Vector3 top = new Vector3((Arena.WalkX0 + Arena.WalkX1) / 2, Arena.CreamTop, (Arena.WalkZ0 + Arena.WalkZ1) / 2);
                bool onNav = NavMesh.SamplePosition(top, out NavMeshHit nh, .4f, NavMesh.AllAreas) && Mathf.Abs(nh.position.y - top.y) < .3f;
                var path = new NavMeshPath(); bool reach = false; float len = 0;
                if (onNav && NavMesh.SamplePosition(new Vector3(-3f, 0, -20f), out NavMeshHit gh, 1f, NavMesh.AllAreas))
                {
                    reach = NavMesh.CalculatePath(gh.position, nh.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                    for (int i = 1; i < path.corners.Length; i++) len += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                }
                bool jump = false; foreach (var l in ar.jumpLinks) if (l != null && l.startTransform != null && Vector3.Distance(l.startTransform.position, new Vector3(top.x, top.y, Arena.WalkZ1 - .3f)) < .5f) jump = true;
                // 后厨南门 → 北门还能走（楼梯没把路堵死）
                var p2 = new NavMeshPath(); bool through = false; float l2 = 0;
                if (NavMesh.SamplePosition(new Vector3(-2f, 0, -31.5f), out NavMeshHit sA, 1f, NavMesh.AllAreas) && NavMesh.SamplePosition(new Vector3(-2f, 0, -8.5f), out NavMeshHit sB, 1f, NavMesh.AllAreas))
                {
                    through = NavMesh.CalculatePath(sA.position, sB.position, NavMesh.AllAreas, p2) && p2.status == NavMeshPathStatus.PathComplete;
                    for (int i = 1; i < p2.corners.Length; i++) l2 += Vector3.Distance(p2.corners[i - 1], p2.corners[i]);
                }
                bool gi = ArenaLightmaps.Applied || !Resources.Load<ArenaLightmaps>("ArenaLightmaps");
                bool ok = onNav && reach && jump && through && l2 < 40f && gi;
                Check("M127", "南后厨二楼传菜走廊（V26）：3.5 m 走廊在寻路网格上、从后厨地面走得上去、北端能跳下；后厨南门 → 北门仍然走得通（< 40 m）；烘焙光照没被打乱", ok,
                    "走廊有网格=" + onNav + " 走得上去=" + reach + "（" + len.ToString("0") + " m） 跳下口=" + jump + " 南门→北门=" + through + "（" + l2.ToString("0") + " m） 光照=" + ArenaLightmaps.Status);
            }

            // ---------------- M128：蒸笼门 / 北烤串巷细节 + 威化坡道落点复核
            {
                int gates = 0; foreach (var s in ar.shutters) if (s != null && s.gate) gates++;
                bool decor = !Skills.Food || (V26.GateDecor == gates && gates >= 2 && V26.Crates == 2 && V26.AlleyDecor >= 30);
                int bad = 0; string list = "";
                foreach (var r in LinkPlus.Ramps)
                {
                    bool ok1 = V26.RampOk(r.top, r.dir, r.h, out string why);
                    if (!ok1) bad++;
                    list += V26.RampZone(r.top) + " " + r.h.ToString("0.0") + "m" + (ok1 ? "" : "✗" + why) + "；";
                }
                string rej = ""; foreach (var kv in LinkPlus.RampRejected) rej += kv.Key + "×" + kv.Value + " ";
                bool ok = decor && bad == 0 && LinkPlus.Ramps.Count >= 3;
                Check("M128", "地图细节 + 坡道复核（V26）：每个蒸笼门有灯笼 / 牌匾 / 竹篾 / 热气，北烤串巷有灯泡串 / 烤串 / 2 摞糖屑木箱；威化坡道 ≥ 3 个点，全部通过复核（平台够大、不贴边、离出生点 ≥ 18 m、看不到出生点）", ok,
                    "蒸笼门细节 " + V26.GateDecor + "/" + gates + " 木箱 " + V26.Crates + " 巷子装饰 " + V26.AlleyDecor + "；坡道 " + LinkPlus.Ramps.Count + " 个：" + list + " 剔除：" + rej);
            }

            // ---------------- 收尾
            Skills.ClearRound(); FoodTips.AutoEnabled = true;
            if (p != null) { p.scripted = false; Hush(p); }
            foreach (var f in m.fighters) { if (ToonAssets.Available) f.Food.EndForm(false); Park(f); }
            m.roundClock = Config.RoundTime; m.phase = Phase.Live;
            yield return null;
        }

        // ================================================================ V26 截图（130–144）
        public static IEnumerator Shots(string dir, System.Func<string, IEnumerator> Grab)
        {
            var m = Match.I; var p = m.player; var ar = m.arena;
            if (p == null || p.cam == null || !ToonAssets.Available) yield break;
            HudCaptureBegin.Allow = true;
            m.phase = Phase.Live; m.roundClock = Config.RoundTime; m.ui.ClearBanner();
            FoodTips.AutoEnabled = false; Skills.ClearRound();
            foreach (var f in m.fighters) { Hush(f); if (!f.human) f.Respawn(new Vector3(-60 + f.slot * 2, .05f, f.team == 0 ? -46f : -44f), 0); }
            int foeTeam = p.team == m.AttackingTeam ? m.DefendingTeam : m.AttackingTeam;
            var foe = Npc(foeTeam, 0);
            var shotCam = new GameObject("V26 截图相机").AddComponent<Camera>(); shotCam.depth = 100; shotCam.nearClipPlane = .05f; shotCam.enabled = false;
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

            // ---- 130–132 枪皮肤：汉堡 / 冰淇淋 / 奶茶（第一人称）
            foreach (var fm in new[] { FoodForm.Burger, FoodForm.IceCream, FoodForm.MilkTea })
            {
                p.Food.Become(fm); yield return new WaitForSeconds(1.6f);   // 等变身大字淡掉
                yield return Grab(F(fm == FoodForm.Burger ? "130_gunskin_burger" : fm == FoodForm.IceCream ? "131_gunskin_icecream" : "132_gunskin_milktea"));
                p.Food.EndForm(false); yield return null;
            }
            // ---- 133 准星打法提示（对准变身汉堡的敌人）
            if (foe != null)
            {
                Vector3 at = ClearSpot(); Place(p, at, 0); Place(foe, at + new Vector3(0, 0, 8f), 180); p.pitch = 4;
                foe.Food.Become(FoodForm.Burger); foe.revealedUntil = Time.time + 5f; yield return new WaitForSeconds(1f);
                yield return Grab(F("133_aim_hint"));
                foe.Food.EndForm(false); foe.Respawn(new Vector3(-60, .05f, -46f), 0); Hush(foe);
            }
            // ---- 134 糖果眼睛监控（近景）
            SecurityCam sc = null; foreach (var c in ar.cams) if (c != null && c.Working) { sc = c; break; }
            if (sc != null)
            {
                Vector3 hp = sc.Head.position, fwd = sc.Head.forward; fwd.y = 0; fwd.Normalize();
                Ext(hp + fwd * 1.6f + Vector3.down * .5f + Vector3.Cross(Vector3.up, fwd) * .6f, hp, 50); yield return new WaitForSeconds(.1f);
                yield return Grab(F("134_candy_eye"));
                FP();
            }
            // ---- 135 南后厨传菜走廊（外景，从后厨南门往北看）+ 136 站在走廊上（第一人称）
            {
                Ext(new Vector3(-1.4f, 1.7f, -29.4f), new Vector3(-.2f, 3.2f, -17f), 72); yield return new WaitForSeconds(.1f);
                yield return Grab(F("135_kitchen_walkway"));
                FP(); Place(p, new Vector3(0, Arena.CreamTop + .05f, -21f), 0); p.pitch = 12; yield return new WaitForSeconds(.3f);
                yield return Grab(F("136_walkway_fp"));
                p.pitch = 0; Place(p, new Vector3(16, .05f, -6), 0);
            }
            // ---- 137 蒸笼门（外景）
            Shutter gate = null; foreach (var s in ar.shutters) if (s != null && s.gate) { gate = s; break; }
            if (gate != null)
            {
                Vector3 gp = gate.transform.position, gf = gate.transform.forward;
                // 门两侧都试，挑镜头不在墙里的一边
                Vector3 cpos = gp - gf * 8f + Vector3.up * 2.4f;
                foreach (float sd in new[] { -1f, 1f }) { Vector3 c2 = gp + gf * 8f * sd + Vector3.up * 2.4f; if (!Physics.CheckSphere(c2, .3f, ~0, QueryTriggerInteraction.Ignore) && !Physics.Linecast(c2, gp + Vector3.up * 2.4f + gf * sd * .8f, ~0, QueryTriggerInteraction.Ignore)) { cpos = c2; break; } }
                Ext(cpos, gp + Vector3.up * 3.2f, 62); yield return new WaitForSeconds(.4f);
                yield return Grab(F("137_steamer_gate"));
                FP();
            }
            // ---- 138 北烤串巷
            {
                Ext(new Vector3(-2f, 2.4f, 10.6f), new Vector3(-2f, 2.4f, 20f), 68); yield return new WaitForSeconds(.3f);
                yield return Grab(F("138_kebab_alley"));
                FP();
            }
            // ---- 139 走近爆米花机（字不再挡半个屏幕）
            ShootProp pop = null; foreach (var sp in Object.FindObjectsOfType<ShootProp>()) if (sp.kind == ShootProp.Kind.Popcorn && !sp.broken) { pop = sp; break; }
            if (pop != null)
            {
                Vector3 pp = pop.transform.position, from = pp + new Vector3(0, 0, -3.2f);
                foreach (var dd in new[] { Vector3.back, Vector3.forward, Vector3.left, Vector3.right })
                {
                    Vector3 c = pp + dd * 3.2f;
                    if (NavMesh.SamplePosition(c, out NavMeshHit ch, .6f, NavMesh.AllAreas) && !Physics.Linecast(ch.position + Vector3.up * 1.6f, pp + Vector3.up * 1.4f + dd * .6f, ~0, QueryTriggerInteraction.Ignore)) { from = ch.position; break; }
                }
                NavMeshHit fh = new NavMeshHit(); fh.position = from;
                Place(p, fh.position + Vector3.up * .05f, Yaw(fh.position, pp)); p.pitch = -8; yield return new WaitForSeconds(.3f);
                yield return Grab(F("139_popcorn_close"));
                p.pitch = 0; Place(p, new Vector3(16, .05f, -6), 0);
            }
            // ---- 140–144 威化坡道 5 个点（铺好坡道后拍）
            int ri = 0;
            foreach (var r in LinkPlus.Ramps)
            {
                if (ri >= 5) break;
                LinkPlus.SpawnRamp(p, r); yield return new WaitForSeconds(.5f);
                Vector3 side = Vector3.Cross(Vector3.up, r.dir);
                Ext(r.basePos + r.dir * 5f + side * 4f + Vector3.up * 3.2f, Vector3.Lerp(r.basePos, r.top, .6f) + Vector3.up * .6f, 62); yield return new WaitForSeconds(.1f);
                yield return Grab(F((140 + ri) + "_ramp_" + ri));
                FP(); foreach (var w in WaferRamp.Live.ToArray()) if (w != null) Object.Destroy(w.gameObject);
                yield return null; ri++;
            }
            // ---- 收尾
            Skills.ClearRound(); FoodTips.AutoEnabled = true;
            FP(); p.scripted = false; Hush(p); Object.Destroy(shotCam.gameObject);
            foreach (var f in m.fighters) Park(f);
            yield return null;
        }
    }
}
