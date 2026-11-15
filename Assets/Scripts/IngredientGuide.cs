using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace NeonSiege
{
    // ================================================================ V23 b：食材高亮（设计稿_V23 §3）
    // 手上正好 1 样时，场上当前存在、能和它凑成菜的食材：
    //   主目标 1 个 = 光柱（12 m 内收起换地面光圈）+ 隔墙描边 + 屏幕边缘方向箭头 + 小地图大图标（呼吸）
    //   备选 ≤ 2 个 = 弱光柱 + 小地图小图标
    // 排序按路径距离（NavMesh，0.5 s 一次）；新候选要比主目标近 SwitchRatio 才换；12 m 内锁定。交火 2 s 内透明度降到 QuietAlpha。
    // 候选集合和 AI 用的同一套规则（FoodGuide.Partners / Recipe），只给自己看。
    public class IngredientGuide : MonoBehaviour
    {
        public static IngredientGuide I;
        public static float BeamHeight = 10f, AltBeamHeight = 6f;      // 8–12 m
        public static float ThroughWallAlpha = .38f;                   // 30–45%
        public static float SwitchRatio = .25f;                        // 近 20–30% 才换主目标
        public static float LockRange = 12f;                           // 12 m 内锁定（= FoodGuide.DetailRange）
        public static float RefreshInterval = .5f;                     // 0.25–0.5 s
        public static float QuietAlpha = .3f;                          // 交火降噪 25–35%
        public static float BumpGap = 10f;                             // 新搭档提示每 8–12 s 最多 1 次
        public static float EdgeInset = .09f;                          // 边缘箭头椭圆内缩 8–10%
        public const int HighlightMax = 3;

        public IngredientPickup Primary;
        public readonly List<IngredientPickup> Alternates = new List<IngredientPickup>();
        public readonly List<IngredientPickup> Candidates = new List<IngredientPickup>();   // 全部候选（按距离）
        public float Alpha = 1f;
        public string Held;
        public bool ArrowShown; public Vector2 ArrowPos; public float ArrowAlpha;              // 测试读
        public string Notice; float noticeUntil;
        public int Switches, FollowPrimary, SecondPicks;                                      // 遥测
        readonly Dictionary<IngredientPickup, float> dist = new Dictionary<IngredientPickup, float>();
        readonly Dictionary<IngredientPickup, float> bornSeen = new Dictionary<IngredientPickup, float>();
        readonly HashSet<IngredientPickup> known = new HashSet<IngredientPickup>();
        float nextRefresh, lastBump = -99; int lastLiveCount = -1; bool reportDone;
        IngredientPickup prevPrimary; float prevPrimaryDist = 99; int prevHeldCount;

        class Beam { public GameObject root; public Transform outer, core, icon, ring; public Renderer ro, rc, ri, rr; }
        readonly Dictionary<IngredientPickup, Beam> beams = new Dictionary<IngredientPickup, Beam>();
        static Material beamMatBase; static Texture2D beamTex;
        CommandBuffer cb; Camera cbCam; Material xray;
        static GUIStyle st; static Font font;

        public static IngredientGuide Ensure()
        {
            if (I != null) return I;
            var go = GameObject.Find("食材指引") ?? new GameObject("食材指引");
            I = go.GetComponent<IngredientGuide>() ?? go.AddComponent<IngredientGuide>();
            return I;
        }
        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; DetachCb(); }

        // ---------------------------------------------------------------- 规则（静态，测试直接调）
        public static bool Active(Fighter f)
        {
            return f != null && f.alive && ToonAssets.Available && f.Food.ingredients.Count == 1 && !f.Food.Transformed;
        }
        public static List<IngredientPickup> CandidatesFor(Fighter f)
        {
            var l = new List<IngredientPickup>();
            if (!Active(f)) return l;
            string held = f.Food.ingredients[0];
            foreach (var ip in IngredientPickup.Live) if (ip != null && ip.id != null && Ingredients.Recipe(held, ip.id) != FoodForm.Dark) l.Add(ip);
            return l;
        }
        public static float PathDistance(Vector3 a, Vector3 b)
        {
            var p = new NavMeshPath();
            if (NavMesh.SamplePosition(a, out NavMeshHit ha, 2f, NavMesh.AllAreas) && NavMesh.SamplePosition(b, out NavMeshHit hb, 2f, NavMesh.AllAreas)
                && NavMesh.CalculatePath(ha.position, hb.position, NavMesh.AllAreas, p) && p.status == NavMeshPathStatus.PathComplete && p.corners.Length > 0)
            {
                float d = Vector3.Distance(a, ha.position); for (int i = 1; i < p.corners.Length; i++) d += Vector3.Distance(p.corners[i - 1], p.corners[i]);
                return d;
            }
            return Vector3.Distance(a, b) * 1.3f;   // 算不出路径：直线 × 1.3
        }
        // 主目标选择（滞后）：current 还在 → 12 m 内锁定；否则新的最近候选比它近 SwitchRatio 才换
        public static int ChoosePrimary(int currentIdx, IList<float> sortedDist)
        {
            if (sortedDist.Count == 0) return -1;
            if (currentIdx < 0 || currentIdx >= sortedDist.Count) return 0;
            float cur = sortedDist[currentIdx];
            if (cur <= LockRange) return currentIdx;
            return sortedDist[0] < cur * (1f - SwitchRatio) ? 0 : currentIdx;
        }
        // 边缘箭头：目标在屏幕外、没开镜、设置是"全部"
        public static bool ArrowVisible(Fighter p, bool onScreen) { return p != null && !onScreen && p.adsK <= .5f && GameSettings.IngredientGuide == 0; }
        public static bool InCombat(Fighter p) { return p != null && Time.time - Mathf.Max(p.lastShotAt, p.lastDamageAt) < FoodTips.CombatQuiet; }
        public static float AlphaFor(Fighter p) { return InCombat(p) ? QuietAlpha : 1f; }
        public float BumpAt(IngredientPickup ip) { return bornSeen.TryGetValue(ip, out float t) && Time.time - t < .6f ? 1f - (Time.time - t) / .6f : 0f; }

        // ---------------------------------------------------------------- 每帧
        void Update()
        {
            var m = Match.I; if (m == null) return;
            if (m.phase == Phase.MatchEnd && !reportDone) { reportDone = true; Report(); }
            if (m.phase != Phase.MatchEnd) reportDone = false;
            var p = m.player;
            bool on = Active(p) && GameSettings.IngredientGuide < 2;
            int liveCount = IngredientPickup.Live.Count;
            bool primaryGone = !ReferenceEquals(Primary, null) && !Primary;
            if (!on) { ClearSel(p); TrackPicks(p); return; }
            if (Held != p.Food.ingredients[0]) { Held = p.Food.ingredients[0]; Primary = null; prevPrimary = null; prevPrimaryId = null; nextRefresh = 0; known.Clear(); seeded = false; }
            if (Time.time >= nextRefresh || primaryGone || liveCount != lastLiveCount) Recompute(p);
            lastLiveCount = liveCount;
            Alpha = AlphaFor(p);
            TrackPicks(p);
            UpdateBeams(p);
            UpdateOutline(p);
        }
        void ClearSel(Fighter p)
        {
            Primary = null; Alternates.Clear(); Candidates.Clear(); Held = null; known.Clear(); ArrowShown = false;
            foreach (var b in beams.Values) if (b.root != null) Destroy(b.root);
            beams.Clear();
            if (cb != null) cb.Clear();
        }
        void Recompute(Fighter p)
        {
            nextRefresh = Time.time + RefreshInterval;
            var c = CandidatesFor(p);
            Vector3 me = p.transform.position;
            dist.Clear(); foreach (var ip in c) dist[ip] = PathDistance(me, ip.transform.position);
            c.Sort((a, b) => dist[a].CompareTo(dist[b]));
            // 新搭档出现：小地图图标"弹"一下 + 轻提示音（每 BumpGap 秒最多 1 次）
            foreach (var ip in c)
                if (!known.Contains(ip))
                {
                    known.Add(ip);
                    if (seeded && Time.time - lastBump >= BumpGap) { bornSeen[ip] = Time.time; lastBump = Time.time; Sfx.Ui("click", .22f, 1.7f); }
                }
            seeded = true;   // 刚拿起食材时场上已有的候选不算"新刷出"
            // 主目标被别人捡走
            bool lost = !ReferenceEquals(prevPrimary, null) && !prevPrimary && prevHeldCount == 1 && p.Food.ingredients.Count == 1 && prevPrimaryId != null;
            if (lost) { Notice = Ingredients.Name(prevPrimaryId) + "被抢走了 · 换一个"; noticeUntil = Time.time + 2.2f; LostAt = Time.time; }
            Candidates.Clear(); Candidates.AddRange(c);
            var ds = new List<float>(); foreach (var ip in c) ds.Add(dist[ip]);
            int cur = Primary != null && Primary ? c.IndexOf(Primary) : -1;
            int pick = ChoosePrimary(cur, ds);
            var np = pick >= 0 ? c[pick] : null;
            if (np != Primary && Primary != null && Primary && np != null) Switches++;
            Primary = np;
            Alternates.Clear();
            foreach (var ip in c) { if (ip == Primary) continue; if (Alternates.Count >= HighlightMax - 1) break; Alternates.Add(ip); }
            prevPrimary = Primary; prevPrimaryId = Primary != null ? Primary.id : null;
            prevPrimaryDist = Primary != null ? dist[Primary] : 99;
        }
        string prevPrimaryId; bool seeded; public float LostAt = -99;
        // follow_primary：捡到的第二样是不是主目标
        void TrackPicks(Fighter p)
        {
            int n = p != null ? p.Food.ingredients.Count : 0;
            bool transformed = p != null && p.Food.Transformed;
            if (p != null && prevHeldCount == 1 && (n == 2 || transformed && n != 1))
            {
                SecondPicks++;
                if (!ReferenceEquals(prevPrimary, null) && !prevPrimary && prevPrimaryDistLive < 2.5f) FollowPrimary++;
            }
            if (Primary != null && Primary && p != null) prevPrimaryDistLive = Links.Flat(Primary.transform.position, p.transform.position);
            prevHeldCount = n;
        }
        float prevPrimaryDistLive = 99;

        public float DistOf(IngredientPickup ip) { return ip != null && dist.TryGetValue(ip, out float d) ? d : -1; }

        // ---------------------------------------------------------------- HUD 文字：和 V21 "差一样" 合成一行，不重复
        public static string HudLine(Fighter p)
        {
            if (p == null || p.Food.ingredients.Count != 1) return "";
            string baseLine = FoodGuide.HudHint(p.Food.ingredients);
            if (baseLine == "" || GameSettings.IngredientGuide >= 2 || I == null) return baseLine;
            var g = I;
            if (g.Notice != null && Time.time < g.noticeUntil) return baseLine + "  ·  " + g.Notice;
            if (g.Primary != null && g.Primary) return baseLine + "  ·  最近的" + Ingredients.Name(g.Primary.id) + " " + Mathf.RoundToInt(g.DistOf(g.Primary)) + " 米";
            var parts = FoodGuide.Partners(p.Food.ingredients[0]);
            float next = float.MaxValue; foreach (var sp in IngredientSpot.All) if (sp != null) next = Mathf.Min(next, sp.NextIn);
            string nm = parts.Count > 0 ? Ingredients.Name(parts[0]) : "";
            return "地图上暂时没有" + nm + (next < 120 ? " · 约 " + Mathf.Max(1, Mathf.CeilToInt(next)) + " 秒后刷新下一批食材" : "") + "  ·  " + baseLine;
        }

        // ---------------------------------------------------------------- 光柱
        static Material BeamMat
        {
            get
            {
                if (beamMatBase == null)
                {
                    beamTex = new Texture2D(32, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "光柱" };
                    var px = new Color[32 * 64];
                    for (int y = 0; y < 64; y++) for (int x = 0; x < 32; x++)
                    {
                        float u = (x + .5f) / 32f * 2 - 1, v = (y + .5f) / 64f;
                        float a = (Mathf.Exp(-u * u * 2.2f) * .75f + Mathf.Exp(-u * u * 18f) * .25f) * Mathf.Pow(1 - v, .6f) * Mathf.Clamp01(v * 12f);
                        px[y * 32 + x] = new Color(1, 1, 1, a);
                    }
                    beamTex.SetPixels(px); beamTex.Apply(false);
                    beamMatBase = new Material(Shader.Find("Sprites/Default")) { mainTexture = beamTex, renderQueue = 3100 };
                }
                return beamMatBase;
            }
        }
        Beam MakeBeam(IngredientPickup ip)
        {
            var b = new Beam { root = new GameObject("食材光柱 " + ip.id) };
            b.root.transform.SetParent(transform, false);
            Transform Quad(string n, Material m, out Renderer r)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(q.GetComponent<Collider>()); q.name = n;
                q.transform.SetParent(b.root.transform, false); r = q.GetComponent<Renderer>(); r.sharedMaterial = m;
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; return q.transform;
            }
            Color col = Ingredients.Col(ip.id);
            b.outer = Quad("外光", new Material(BeamMat) { color = new Color(col.r, col.g, col.b, .55f) }, out b.ro);
            b.core = Quad("白芯", new Material(BeamMat) { color = new Color(1, 1, 1, .8f) }, out b.rc);
            b.icon = Quad("图标", new Material(Shader.Find("Sprites/Default")) { mainTexture = MapIcons.Ingredient(ip.id), renderQueue = 3101 }, out b.ri);
            b.ring = Quad("地面光圈", ToonAssets.RingMaterial(Color.Lerp(col, Color.white, .3f)), out b.rr);
            b.ring.localRotation = Quaternion.Euler(90, 0, 0);
            return b;
        }
        void UpdateBeams(Fighter p)
        {
            var want = new HashSet<IngredientPickup>();
            bool worldOn = GameSettings.IngredientGuide == 0;
            if (worldOn && Primary != null && Primary) want.Add(Primary);
            if (worldOn) foreach (var a in Alternates) if (a != null && a) want.Add(a);
            var dead = new List<IngredientPickup>();
            foreach (var kv in beams) if (kv.Key == null || !kv.Key || !want.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (var k in dead) { if (beams[k].root != null) Destroy(beams[k].root); beams.Remove(k); }
            var cam = Camera.main; Vector3 cp = cam != null ? cam.transform.position : p.transform.position;
            foreach (var ip in want)
            {
                if (!beams.TryGetValue(ip, out var b) || b.root == null) { b = MakeBeam(ip); beams[ip] = b; }
                bool prim = ip == Primary;
                Vector3 g = ip.transform.position; b.root.transform.position = g;
                float d = Links.Flat(g, p.transform.position);
                bool near = d <= LockRange;
                float h = prim ? BeamHeight : AltBeamHeight, a = Alpha * (prim ? 1f : .4f);
                b.outer.gameObject.SetActive(!near); b.core.gameObject.SetActive(!near && prim); b.icon.gameObject.SetActive(!near && prim);
                b.ring.gameObject.SetActive(near && prim);
                // 圆柱式朝向相机
                Vector3 to = cp - g; to.y = 0; var rot = to.sqrMagnitude > .01f ? Quaternion.LookRotation(-to) : Quaternion.identity;
                float wScale = Mathf.Clamp(d * .02f, .6f, 1.6f);   // 远处稍粗，保证 60 m 外能看到
                b.outer.localPosition = Vector3.up * h / 2; b.outer.rotation = rot; b.outer.localScale = new Vector3((prim ? 2.4f : 1.3f) * wScale, h, 1);
                b.core.localPosition = Vector3.up * h / 2; b.core.rotation = rot; b.core.localScale = new Vector3(.55f * wScale, h, 1);
                b.icon.localPosition = Vector3.up * (h + .5f); b.icon.rotation = cam != null ? Quaternion.LookRotation(b.icon.position - cp) : rot; b.icon.localScale = Vector3.one * 1.6f * wScale;
                var oc = b.ro.sharedMaterial.color; oc.a = .8f * a; b.ro.sharedMaterial.color = oc;
                var cc = b.rc.sharedMaterial.color; cc.a = .95f * a; b.rc.sharedMaterial.color = cc;
                b.ring.localPosition = Vector3.up * .04f; b.ring.localScale = Vector3.one * (1.9f + .2f * Mathf.Sin(Time.time * 5f));
            }
        }

        // ---------------------------------------------------------------- 隔墙描边（主目标）：XRay 材质（ZTest Greater），只在被挡住的部分画出来
        void UpdateOutline(Fighter p)
        {
            var cam = Camera.main;
            if (cam != cbCam) { DetachCb(); if (cam != null) { cb = new CommandBuffer { name = "食材隔墙描边" }; cam.AddCommandBuffer(CameraEvent.AfterForwardAlpha, cb); cbCam = cam; } }
            if (cb == null) return;
            cb.Clear();
            if (GameSettings.IngredientGuide != 0 || Primary == null || !Primary) return;
            if (xray == null) { var bm = Resources.Load<Material>("Materials/XRay"); if (bm == null) return; xray = new Material(bm); }
            Color col = Ingredients.Col(Primary.id);
            xray.SetColor("_Color", col * (ThroughWallAlpha * Alpha * 2.2f));
            foreach (var r in Primary.GetComponentsInChildren<MeshRenderer>())
            {
                if (r == null || !r.enabled) continue;
                var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                for (int i = 0; i < mf.sharedMesh.subMeshCount; i++) cb.DrawRenderer(r, xray, i);
            }
        }
        void DetachCb() { if (cbCam != null && cb != null) cbCam.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, cb); cb = null; cbCam = null; }

        // ---------------------------------------------------------------- 屏幕边缘箭头
        // 目标在屏幕内 → onScreen = true（不画箭头）；否则给出贴在内缩椭圆上的位置和方向（GUI 坐标，y 向下）
        public static void EdgePoint(Camera cam, Vector3 world, float W, float H, out bool onScreen, out Vector2 pos, out Vector2 dir)
        {
            Vector3 vp = cam.WorldToViewportPoint(world);
            onScreen = vp.z > 0 && vp.x >= .03f && vp.x <= .97f && vp.y >= .03f && vp.y <= .97f;
            Vector2 d = new Vector2(vp.x - .5f, .5f - vp.y);
            if (vp.z < 0) { d = -d; d.y = Mathf.Min(d.y, .1f); if (Mathf.Abs(d.x) < .05f) d.x = .3f; }   // 身后的目标贴到左右两侧，不压底部 HUD
            if (d.sqrMagnitude < 1e-6f) d = new Vector2(0, 1);
            d.x *= W; d.y *= H; d.Normalize();
            float rx = W / 2 * (1 - EdgeInset * 2), ry = H / 2 * (1 - EdgeInset * 2);
            float t = 1f / Mathf.Sqrt(d.x * d.x / (rx * rx) + d.y * d.y / (ry * ry));
            pos = new Vector2(W / 2, H / 2) + d * t; dir = d;
        }

        void OnGUI()
        {
            ArrowShown = false;
            var m = Match.I; if (m == null || Event.current.type != EventType.Repaint) return;
            var p = m.player;
            if (p == null || !p.alive || Primary == null || !Primary || GameSettings.IngredientGuide != 0) return;
            if (m.ui != null && (!m.ui.Started && !m.autoplay && !m.testMode || m.ui.HudOverlayOpen)) return;
            var cam = Camera.main; if (cam == null) return;
            if (font == null) font = Resources.Load<Font>("NotoSansSC");
            if (st == null) st = new GUIStyle(GUI.skin.label) { font = font, fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUI.depth = -4;
            float s = Mathf.Clamp(Screen.height / 1080f, .7f, 1.6f); GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            float W = Screen.width / s, H = Screen.height / s;
            EdgePoint(cam, Primary.transform.position + Vector3.up * .6f, W, H, out bool onScreen, out Vector2 pos, out Vector2 dir);
            if (!ArrowVisible(p, onScreen)) return;   // 屏幕内 / 开镜时不画
            ArrowShown = true; ArrowPos = pos; ArrowAlpha = Alpha;
            Color col = Ingredients.Col(Primary.id); float a = Alpha;
            float pulse = 1f + .08f * Mathf.Sin(Time.time * 6f);
            MapGL.Disc(pos, 22 * pulse, new Color(.2f, .06f, .16f, .75f * a));
            MapGL.Ring(pos, 22 * pulse, 2.5f, new Color(col.r, col.g, col.b, a));
            MapGL.Tri(pos + dir * 36, dir, 14, 18, new Color(1, 1, 1, .95f * a));
            var o = GUI.color; GUI.color = new Color(1, 1, 1, a);
            GUI.DrawTexture(new Rect(pos.x - 14, pos.y - 14, 28, 28), MapIcons.Ingredient(Primary.id));
            st.normal.textColor = new Color(0, 0, 0, .7f * a); GUI.Label(new Rect(pos.x - 39, pos.y + 23, 80, 20), Mathf.RoundToInt(DistOf(Primary)) + " 米", st);
            st.normal.textColor = new Color(1, 1, 1, a); GUI.Label(new Rect(pos.x - 40, pos.y + 22, 80, 20), Mathf.RoundToInt(DistOf(Primary)) + " 米", st);
            GUI.color = o;
        }

        // ---------------------------------------------------------------- 遥测：配对保底是否生效（neon.py match 会打印 NEON_TELEMETRY 行）
        void Report()
        {
            int c = IngredientSpot.PityCond;
            Debug.Log("NEON_TELEMETRY ingredient spawns=" + IngredientSpot.Spawns + " pity_cond=" + c + " partner_under_cond=" + IngredientSpot.PityPartner
                + " partner_ratio=" + (c > 0 ? (IngredientSpot.PityPartner * 100f / c).ToString("0") + "%" : "-")
                + " pity_hit=" + IngredientSpot.PityHit + " pity_dist_avg=" + (IngredientSpot.PityHit > 0 ? (IngredientSpot.PityDistSum / IngredientSpot.PityHit).ToString("0.0") : "-")
                + " player_second_picks=" + SecondPicks + " follow_primary=" + FollowPrimary + " primary_switches=" + Switches);
        }
    }
}
