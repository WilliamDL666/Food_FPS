using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace NeonSiege
{
    // ================================================================ V23 b：地图 UI + 食材高亮的自动化测试（M70–M76）和截图（80–86）
    // 单独放一个文件，PlaytestRunner 里只加两行调用（避免和并行改 PlaytestRunner 的人冲突）。
    // 测试走 batchmode -nographics：没有 OnGUI / 相机画面，所以规则、布局、坐标都抽成纯函数直接验。
    public static class MapUiTests
    {
        static readonly string[] Blacklist = { "工厂", "安保", "集结", "铁门", "炸弹", "混凝土", "医疗" };
        static readonly string[] MiniForbidden = { "cam", "power", "barrel", "glass", "crate" };

        static void Place(Fighter f, Vector3 p, float yaw)
        {
            f.StopMantle(); f.botMove = Vector3.zero; f.botSprint = false; f.botTrigger = false;
            f.cc.enabled = false; f.transform.position = p; f.cc.enabled = true;
            f.yaw = yaw; f.pitch = 0; f.transform.rotation = Quaternion.Euler(0, yaw, 0); f.velocity = Vector3.zero;
        }

        class Frozen { public List<IngredientSpot> spots = new List<IngredientSpot>(); public Dictionary<Bot, bool> bots = new Dictionary<Bot, bool>(); public int guide; public bool rot; }
        static Frozen Freeze()
        {
            var fz = new Frozen { guide = GameSettings.IngredientGuide, rot = GameSettings.MinimapRotate };
            foreach (var sp in IngredientSpot.All.ToArray()) if (sp != null) { fz.spots.Add(sp); sp.enabled = false; }
            foreach (var ip in IngredientPickup.Live.ToArray()) if (ip != null) Object.DestroyImmediate(ip.gameObject);
            foreach (var f in Match.I.fighters) { var b = f.GetComponent<Bot>(); if (b != null) { fz.bots[b] = b.enabled; b.enabled = false; } f.botMove = Vector3.zero; f.botTrigger = false; }
            GameSettings.IngredientGuide = 0;
            return fz;
        }
        static void Thaw(Frozen fz)
        {
            foreach (var ip in IngredientPickup.Live.ToArray()) if (ip != null) Object.DestroyImmediate(ip.gameObject);
            foreach (var sp in fz.spots) if (sp != null) { sp.enabled = true; sp.ResetRound(); }
            foreach (var kv in fz.bots) if (kv.Key != null) kv.Key.enabled = kv.Value;
            GameSettings.IngredientGuide = fz.guide; GameSettings.MinimapRotate = fz.rot;
            var p = Match.I.player; if (p != null) { p.Food.ingredients.Clear(); if (p.Food.Transformed) p.Food.EndForm(false); }
        }
        static void ParkOthers(Fighter p)
        {
            var m = Match.I;
            foreach (var f in m.fighters) if (f != p) Place(f, m.arena.SpawnPoint(f.team == m.AttackingTeam, f.slot) + (f.team == m.AttackingTeam ? Vector3.left : Vector3.right) * 2f, 0);
        }
        static bool Clean(string s, out string hit) { foreach (var w in Blacklist) if (s != null && s.Contains(w)) { hit = w + "「" + s + "」"; return false; } hit = null; return true; }

        public static IEnumerator Run(System.Action<string, string, bool, string> Check)
        {
            var m = Match.I; var p = m.player;
            MapUI.Ensure(); IngredientGuide.Ensure();
            // ---------------- M70：俯视底图生成
            if (!MapBase.Built) { MapBase.Dispose(); MapBase.Build(); }   // 测试里一次生成完（游戏里分帧）
            int gOk = 0, gN = 0, bOk = 0, bN = 0; var rnd = new System.Random(7);
            for (int t = 0; t < 6000 && gN < 200; t++)
            {
                float x = (float)(rnd.NextDouble() * 2 - 1) * (Arena.HalfX - 1), z = (float)(rnd.NextDouble() * 2 - 1) * (Arena.HalfZ - 1);
                if (NavMesh.SamplePosition(new Vector3(x, 1f, z), out NavMeshHit h, 1f, NavMesh.AllAreas) && h.position.y < MapBase.CutHeight - .5f)
                { gN++; if (MapBase.IsGroundColor(MapBase.ClassAt(h.position))) gOk++; }
            }
            // 建筑：从高处往下打，第一下打到 ≥ 2 m 高、周围 1.5 m 没有可走面的点 = 建筑 / 墙体中心
            for (int t = 0; t < 20000 && bN < 50; t++)
            {
                float x = (float)(rnd.NextDouble() * 2 - 1) * (Arena.HalfX - 2), z = (float)(rnd.NextDouble() * 2 - 1) * (Arena.HalfZ - 2);
                if (!Physics.Raycast(new Vector3(x, MapBase.CutHeight - .1f, z), Vector3.down, out RaycastHit rh, MapBase.CutHeight, ~0, QueryTriggerInteraction.Ignore) || rh.point.y < 2f) continue;
                if (rh.collider is CharacterController) continue;
                if (NavMesh.SamplePosition(rh.point, out NavMeshHit nh2, 1.5f, NavMesh.AllAreas)) continue;
                if (NavMesh.SamplePosition(new Vector3(x, .2f, z), out NavMeshHit nh3, .6f, NavMesh.AllAreas)) continue;   // 雨棚 / 外摆棚 / 连廊下面能走：底图故意画成地面（看得到下面）
                bN++; if (MapBase.IsBuildingColor(MapBase.ClassAt(rh.point))) bOk++;
            }
            float gr = gN > 0 ? gOk / (float)gN : 0, br = bN > 0 ? bOk / (float)bN : 0;
            Check("M70", "俯视底图：运行时按真实几何生成（离地 " + MapBase.CutHeight + " m 以上切掉），宽 ≥ 1024；NavMesh 可走点 ≥ 95% 落在地面色，建筑点 ≥ 95% 落在建筑色",
                MapBase.Tex != null && MapBase.W >= 1024 && gr >= .95f && br >= .95f,
                "贴图 " + MapBase.W + "×" + MapBase.H + "（" + MapBase.PxPerMeter.ToString("0") + " px/m），生成 " + MapBase.BuildMs.ToString("0") + " ms，可走点 " + gOk + "/" + gN + "，建筑点 " + bOk + "/" + bN + "，局部刷新 " + MapBase.Refreshes + " 次");

            // ---------------- M71：坐标和比例
            float pxx = MapBase.W / MapBase.SizeX, pxz = MapBase.H / MapBase.SizeZ;
            var v = new MapView { center = new Vector2(130, 130), worldCenter = new Vector3(-20, 0, 7), pxPerM = 120f / MapUI.MiniRadius, rotDeg = 37f, clipRadius = 120 };
            float maxErr = 0; for (int i = 0; i < 50; i++) { var w = new Vector3((float)(rnd.NextDouble() * 2 - 1) * 70, 0, (float)(rnd.NextDouble() * 2 - 1) * 48); var back = v.Unmap(v.Map(w)); maxErr = Mathf.Max(maxErr, Vector3.Distance(w, back)); }
            var vf = new MapView { center = Vector2.zero, worldCenter = Vector3.zero, pxPerM = 3, rotDeg = -90 };   // 面朝东（yaw 90）跟随旋转
            Vector2 east = vf.Map(new Vector3(10, 0, 0));
            bool headingUp = east.y < -25 && Mathf.Abs(east.x) < .5f;
            float uvErr = 0; foreach (var w in new[] { Arena.SiteA, Arena.SiteB, new Vector3(-60, 0, 40), new Vector3(65, 0, -45) }) uvErr = Mathf.Max(uvErr, Vector3.Distance(MapBase.World(MapBase.UV(w)), new Vector3(w.x, 0, w.z)));
            Check("M71", "坐标和比例：世界 → 小地图 → 世界往返误差 ≤ 1 m（含旋转）；底图横纵 px/m 相等（≤ 1%）；跟随旋转时面朝方向在正上方；小地图半径 35–50 m",
                maxErr <= 1f && Mathf.Abs(pxx / pxz - 1) <= .01f && headingUp && uvErr <= 1f && MapUI.MiniRadius >= 35 && MapUI.MiniRadius <= 50,
                "往返最大误差 " + maxErr.ToString("0.000") + " m，横 " + pxx.ToString("0.00") + " / 纵 " + pxz.ToString("0.00") + " px/m，朝东时东边 10 m 的点在 (" + east.x.ToString("0.0") + ", " + east.y.ToString("0.0") + ")，UV 误差 " + uvErr.ToString("0.000") + " m，半径 " + MapUI.MiniRadius + " m");

            // ---------------- M72：图层和文案
            var bigV = new MapView { center = new Vector2(700, 480), worldCenter = Vector3.zero, pxPerM = Mathf.Min(1180 / MapBase.SizeX, 860 / MapBase.SizeZ) };
            var rects = new List<Rect>(); var texts = new List<string>(); var zs = new List<int>();
            MapUI.LayoutLabels(bigV, rects, texts, zs);
            int overlaps = 0; for (int i = 0; i < rects.Count; i++) for (int j = i + 1; j < rects.Count; j++) if (rects[i].Overlaps(rects[j])) overlaps++;
            string bad = null;
            if (ToonAssets.Available)
            {
                var all = new List<string>(texts); all.Add(MapUI.MapTitle);
                for (int i = 0; i < Arena.Zones.Length; i++) { all.Add(MapUI.ZoneDisplayName(i)); all.Add(MapUI.ZoneDisplayHint(i)); }
                foreach (var row in MapUI.LegendRows(p)) all.Add(row.Item3);
                foreach (var s in all) if (!Clean(s, out string hit)) { bad = hit; break; }
            }
            bool idKept = Arena.Zones[0].name == "A 点 · 货运广场" && Arena.Zones[1].name == "B 点 · 工厂车间";
            string miniBad = null; foreach (var k in MapUI.MiniKinds) foreach (var f in MiniForbidden) if (k.Contains(f)) miniBad = k;
            Check("M72", "图层和文案：大地图区域标签 ≤ 12 个且两两不重叠；美食模式地图文字过黑名单（工厂 / 安保 / 集结 / 铁门 / 炸弹 / 混凝土 / 医疗）0 命中；小地图不画监控 / 电闸 / 爆炸桶 / 玻璃 / 木箱；Zones[].name 不变（遥测用）",
                rects.Count <= MapUI.LabelMax && rects.Count >= 8 && overlaps == 0 && bad == null && idKept && miniBad == null,
                "标签 " + rects.Count + " 个（" + string.Join("、", texts.ToArray()) + "），重叠 " + overlaps + "，黑名单 " + (bad ?? "0 命中") + "，name 保留=" + idKept
                + "，小地图实画 " + (MapUI.MiniFrames > 0 ? MapUI.MiniKinds.Count + " 种图标" + (miniBad != null ? "（含 " + miniBad + "）" : "") : "（batchmode 无 OnGUI，只验布局）"));

            if (!ToonAssets.Available)
            {
                Check("M73", "食材候选", true, "写实模式没有食材，跳过"); Check("M74", "实时更新", true, "写实模式没有食材，跳过");
                Check("M75", "边缘箭头和降噪", true, "写实模式没有食材，跳过");
            }
            else
            {
                var spotPos = new List<Vector3>(); foreach (var sp in IngredientSpot.All) if (sp != null) spotPos.Add(sp.transform.position);
                var fz = Freeze();
                var g = IngredientGuide.I;
                Vector3 P0 = new Vector3(-44, .05f, 3.5f);
                p.Respawn(P0, 90); yield return null; Place(p, P0, 90); ParkOthers(p);
                p.Food.ingredients.Clear(); p.Food.ingredients.Add("Flour");
                var e1 = IngredientPickup.Spawn("Egg", new Vector3(-34, .05f, 3.5f), null);
                var e2 = IngredientPickup.Spawn("Egg", new Vector3(-24, .05f, 2f), null);
                var e3 = IngredientPickup.Spawn("Egg", new Vector3(-14, .05f, -2f), null);
                var ch = IngredientPickup.Spawn("Chili", new Vector3(-38, .05f, -3f), null);
                yield return new WaitForSeconds(.6f);
                // ---------------- M73：食材候选
                bool candOk = g.Candidates.Count == 3 && g.Candidates.TrueForAll(c => c.id == "Egg") && g.Primary == e1 && 1 + g.Alternates.Count <= IngredientGuide.HighlightMax && !g.Candidates.Contains(ch) && !g.Alternates.Contains(ch);
                string candDetail = "候选 " + g.Candidates.Count + " 个，主目标 = " + (g.Primary == e1 ? "最近的鸡蛋" : g.Primary == null ? "无" : "其他") + "（路径 " + g.DistOf(g.Primary).ToString("0.0") + " m），备选 " + g.Alternates.Count + "，辣椒被选中=" + g.Candidates.Contains(ch);
                p.Food.ingredients.Clear(); yield return new WaitForSeconds(.15f);
                bool emptyOk = g.Candidates.Count == 0 && g.Primary == null;
                p.Food.ingredients.Add("Flour"); p.Food.Become(FoodForm.Cake); if (p.Food.ingredients.Count == 0) p.Food.ingredients.Add("Flour");
                bool formOk = IngredientGuide.CandidatesFor(p).Count == 0;
                p.Food.EndForm(false); p.Food.ingredients.Clear(); p.Food.ingredients.Add("Flour");
                Check("M73", "食材候选：手拿面粉、场上 3 个鸡蛋 + 1 个辣椒 → 候选 = 3 个鸡蛋，主目标 = 路径最近，高亮 ≤ 3（主 1 + 备 2），辣椒不亮；空手 / 变身中候选为空",
                    candOk && emptyOk && formOk, candDetail + "；空手=" + emptyOk + "，变身中=" + formOk);
                yield return new WaitForSeconds(.6f);

                // ---------------- M74：实时更新 + 滞后
                var before = g.Primary;
                Object.Destroy(e1.gameObject);
                float t0 = Time.time; while (Time.time - t0 < 1f && !(g.Primary != null && g.Primary == e2)) yield return null;
                float switchT = Time.time - t0; bool noticeOk = g.Notice != null && g.Notice.Contains("被抢走了") && IngredientGuide.HudLine(p).Contains("被抢走了");
                var e4 = IngredientPickup.Spawn("Egg", new Vector3(-38, .05f, 4.5f), null);
                t0 = Time.time; while (Time.time - t0 < 1f && !g.Candidates.Contains(e4)) yield return null;
                float newT = Time.time - t0;
                yield return new WaitForSeconds(.1f);
                bool toNear = g.Primary == e4;
                int keep = IngredientGuide.ChoosePrimary(1, new List<float> { 24.5f, 30f }), sw = IngredientGuide.ChoosePrimary(1, new List<float> { 20f, 30f }), lockd = IngredientGuide.ChoosePrimary(1, new List<float> { 5f, 11f });
                Check("M74", "实时更新：主目标被捡走 → 0.5 s 内切到下一个并提示\"被抢走了 · 换一个\"；新刷出的搭档 0.5 s 内出现；两个候选差 < " + Mathf.RoundToInt(IngredientGuide.SwitchRatio * 100) + "% 时不换、12 m 内锁定",
                    before == e1 && switchT <= .5f && noticeOk && newT <= .5f && toNear && keep == 1 && sw == 0 && lockd == 1,
                    "切换 " + switchT.ToString("0.00") + " s，提示=" + noticeOk + "，新搭档出现 " + newT.ToString("0.00") + " s，切到更近的=" + toNear + "，滞后：近 18% 保持=" + (keep == 1) + "、近 33% 切换=" + (sw == 0) + "、12 m 内锁定=" + (lockd == 1));

                // ---------------- M75：边缘箭头和降噪
                var cam = Camera.main; float W = 1920, H = 1080;
                bool onA = false, onB = true; Vector2 posB = Vector2.zero; float ell = 0;
                if (cam != null)
                {
                    Vector3 target = e4.transform.position + Vector3.up * .6f;
                    Place(p, P0, 90); p.pitch = 0; yield return null; yield return null;
                    IngredientGuide.EdgePoint(cam, target, W, H, out onA, out _, out _);
                    Place(p, P0, -90); yield return null; yield return null;
                    IngredientGuide.EdgePoint(cam, target, W, H, out onB, out posB, out _);
                    float rx = W / 2 * (1 - IngredientGuide.EdgeInset * 2), ry = H / 2 * (1 - IngredientGuide.EdgeInset * 2);
                    ell = Mathf.Pow((posB.x - W / 2) / rx, 2) + Mathf.Pow((posB.y - H / 2) / ry, 2);
                }
                p.lastShotAt = Time.time; yield return null;
                float quiet = g.Alpha, quietStatic = IngredientGuide.AlphaFor(p);
                p.lastShotAt = -10; p.lastDamageAt = -10; yield return null;
                float calm = g.Alpha;
                float adsSave = p.adsK; p.adsK = 1f; bool adsHidden = !IngredientGuide.ArrowVisible(p, false); p.adsK = adsSave;
                bool shownBehind = IngredientGuide.ArrowVisible(p, onB), hiddenFront = !IngredientGuide.ArrowVisible(p, onA);
                Check("M75", "边缘箭头和降噪：目标在屏幕外 → 有箭头且贴在内缩 " + Mathf.RoundToInt(IngredientGuide.EdgeInset * 100) + "% 的椭圆上；在屏幕内 → 无箭头；开火后 2 s 内透明度 ≤ 0.35；开镜时隐藏",
                    cam != null && onA && !onB && shownBehind && hiddenFront && Mathf.Abs(ell - 1) < .02f && quiet <= .35f && quietStatic <= .35f && calm > .9f && adsHidden,
                    "面朝目标在屏幕内=" + onA + "，背对时箭头 (" + posB.x.ToString("0") + ", " + posB.y.ToString("0") + ") 椭圆值 " + ell.ToString("0.000") + "，交火透明度 " + quiet.ToString("0.00") + " / 平时 " + calm.ToString("0.00") + "，开镜隐藏=" + adsHidden);
                Thaw(fz);

                // ---------------- M76：刷新规则乙（洗牌袋 + 配对保底）
                var srnd = new System.Random(11); int cond = 0, partner = 0, pityN = 0, distBad = 0; float dmin = 999, dmax = 0;
                var holders = new[] { P0, new Vector3(0, 0, 0), new Vector3(40, 0, 0) };
                for (int i = 0; i < 2000; i++)
                {
                    Vector3 hp = holders[i % holders.Length]; string held = i % 2 == 0 ? "Flour" : "Rice";
                    string id = IngredientSpot.Decide(held, hp, spotPos, srnd, out int idx, out bool pity);
                    cond++; if (Ingredients.Recipe(held, id) != FoodForm.Dark) partner++;
                    if (pity) { pityN++; float d = Links.Flat(spotPos[idx], hp); dmin = Mathf.Min(dmin, d); dmax = Mathf.Max(dmax, d); if (d < IngredientSpot.PityMinDist || d > IngredientSpot.PityMaxDist) distBad++; }
                }
                IngredientSpot.ResetBag(); var cnt = new Dictionary<string, int>(); foreach (var id in Ingredients.All) cnt[id] = 0;
                for (int i = 0; i < Ingredients.All.Length * 200; i++) cnt[IngredientSpot.DrawBag(srnd)]++;
                int fmin = int.MaxValue, fmax = 0; foreach (var kv in cnt) { fmin = Mathf.Min(fmin, kv.Value); fmax = Mathf.Max(fmax, kv.Value); }
                float ratio = partner / (float)cond;
                Check("M76", "刷新规则乙：有人拿着 X 且场上没有搭档时，下一次刷新是搭档的比例在 50–70%，保底刷新点离持有者 " + IngredientSpot.PityMinDist + "–" + IngredientSpot.PityMaxDist + " m；洗牌袋 13 种频率各在平均值 ±15% 内；刷新点仍是 " + spotPos.Count + " 个",
                    ratio >= .5f && ratio <= .7f && distBad == 0 && pityN > 0 && fmin >= 170 && fmax <= 230 && spotPos.Count == 18,
                    "搭档比例 " + (ratio * 100).ToString("0.0") + "%（保底触发 " + pityN + "/" + cond + "，保底概率 " + IngredientSpot.PityChance + "），保底距离 " + dmin.ToString("0.0") + "–" + dmax.ToString("0.0") + " m，越界 " + distBad + "；洗牌袋每种 " + fmin + "–" + fmax + " 次");
            }
            yield return null;
        }

        // ---------------- 截图（80–86）：窗口模式跑 exe -neonShots 时走 ScreenCapture，带 HUD
        public static IEnumerator Shots(string dir, System.Func<string, IEnumerator> grab)
        {
            var m = Match.I; var p = m.player; if (p == null) yield break;
            HudCaptureBegin.Allow = true;
            m.phase = Phase.Live; m.roundClock = Config.RoundTime;
            m.ui.ClearBanner(); m.ui.PreviewPause(false);
            p.enabled = true; p.AttachCamera(); p.SetViewModelVisible(true);
            var fz = Freeze();
            string F(string n) { return System.IO.Path.Combine(dir, n); }
            Vector3 P0 = new Vector3(-30, .05f, 2.5f);
            p.Respawn(P0, 60); p.scripted = true; yield return null; Place(p, P0, 60); p.pitch = 2; ParkOthers(p);
            // 两个队友在附近，展示视野扇形
            int k = 0; foreach (var f in m.fighters) if (f != p && f.team == p.team && k < 2) { Place(f, P0 + new Vector3(-6 - k * 4, 0, k == 0 ? 3 : -3), 90); k++; }
            yield return new WaitForSeconds(.6f);
            GameSettings.MinimapRotate = true; yield return null;
            yield return grab(F("80_minimap_follow.png"));
            GameSettings.MinimapRotate = false; yield return null;
            yield return grab(F("81_minimap_north.png"));
            GameSettings.MinimapRotate = true;
            m.ui.OpenMap(true); yield return null;
            yield return grab(F("82_bigmap.png"));
            m.ui.OpenMap(false);
            if (ToonAssets.Available)
            {
                // 食材高亮：拿面粉，主目标鸡蛋在前方 32 m（主街东段），两个备选在别处
                Vector3 Q = new Vector3(-44, .05f, 3.5f);
                Place(p, Q, 90); p.pitch = 4;
                p.Food.ingredients.Clear(); p.Food.ingredients.Add("Flour");
                IngredientPickup.Spawn("Egg", new Vector3(-12, .05f, 1.5f), null);
                IngredientPickup.Spawn("Egg", new Vector3(-2, .05f, 22f), null);
                IngredientPickup.Spawn("Egg", new Vector3(-58, .05f, 38f), null);
                IngredientPickup.Spawn("Chili", new Vector3(-36, .05f, -4f), null);
                yield return new WaitForSeconds(1.0f);
                yield return grab(F("83_ingredient_beam.png"));
                Place(p, Q, -90); yield return new WaitForSeconds(.6f);
                yield return grab(F("84_edge_arrow.png"));
                p.lastShotAt = Time.time; yield return null; yield return null;
                yield return grab(F("85_combat_quiet.png"));
                p.lastShotAt = -10;
                m.ui.OpenMap(true); yield return null;
                yield return grab(F("86_bigmap_ingredients.png"));
                m.ui.OpenMap(false);
            }
            Thaw(fz);
            p.scripted = false;
            Debug.Log("NEON_MAPSHOTS minimapFrames=" + MapUI.MiniFrames + " bigFrames=" + MapUI.BigFrames + " build=" + MapBase.BuildMs.ToString("0") + "ms");
            yield return null;
        }
    }
}
