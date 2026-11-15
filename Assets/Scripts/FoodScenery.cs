using System.Collections.Generic;
using UnityEngine;

namespace NeonSiege
{
    // V21 场景道具接入（Resources/Toon/FoodScenes）。
    // 光照：ArenaLightmaps 按「Arena 下渲染器顺序」匹配烘焙光照，所以这里的渲染器全部建在 Arena 层级之外，
    //       并且在 ArenaLightmaps.Apply 之后才创建 —— 烘焙匹配不受影响，新道具走实时光 + 烘焙光照探针。
    // 小道具：纯装饰，没有碰撞；按柜台 / 地面射线贴地，放之前做重叠检查（不穿模、不悬空），不通过就跳过并记日志。
    // 大道具：只换外观。原碰撞、翻越连接（OffMeshLink）、NavMesh、玩法触发器、交互标记都不动，只关掉旧模型的渲染器，
    //         新模型按原碰撞体尺寸摆在同一位置，并跟着原模型的"被打抖动"一起缩放。
    public static class FoodScenery
    {
        public const string Dir = "Toon/FoodScenes/";
        public static readonly string[] SmallModels = { "SmallBobaCup", "SmallRamenTray", "SmallSkewerRack", "SmallSprinkleCrate", "SmallSauceCaddy" };
        public static GameObject Root;
        public static readonly Dictionary<string, int> Placed = new Dictionary<string, int>();
        public static readonly List<string> Skipped = new List<string>();
        public static readonly List<string> Areas = new List<string>();
        public static float MaxGap;                       // 小道具底面到支撑面的最大距离（悬空检查）
        public static int ChocoFound, ChocoSwapped, HotpotFound, HotpotSwapped;
        public static readonly List<GameObject> ChocoRoots = new List<GameObject>(), HotpotRoots = new List<GameObject>();
        public const string KioskNote = "CandyKiosk 是带屋顶、侧墙、门洞的封闭小屋（柜台高约 0.6 m，屋顶到 3.3 m），原甜品摊是 1.15 m 高的开放柜台碰撞（可翻上去、可隔着打），外形差太多，不替换";
        public static int Total { get { int n = 0; foreach (var kv in Placed) n += kv.Value; return n; } }

        public static void Dress(Arena arena)
        {
            Placed.Clear(); Skipped.Clear(); Areas.Clear(); MaxGap = 0;
            ChocoFound = ChocoSwapped = HotpotFound = HotpotSwapped = 0; ChocoRoots.Clear(); HotpotRoots.Clear();
            if (Root != null) Arena.Kill(Root);
            if (!ToonAssets.Available || arena == null) return;
            Root = new GameObject("V21 场景道具（不在烘焙匹配范围内）");
            Physics.SyncTransforms();   // 地图刚在这一帧生成，射线 / 重叠检查前先同步碰撞体位置
            try { SwapBigProps(arena); } catch (System.Exception e) { Debug.LogWarning("NEON_PROPS big swap failed: " + e); }
            try { DressStalls(arena); DressStreets(); } catch (System.Exception e) { Debug.LogWarning("NEON_PROPS small props failed: " + e); }
            var sb = new System.Text.StringBuilder();
            foreach (var kv in Placed) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
            Debug.Log("NEON_PROPS small=" + Total + " [" + sb + "] maxGap=" + MaxGap.ToString("0.000") + " skipped=" + Skipped.Count + (Skipped.Count > 0 ? " (" + string.Join("; ", Skipped.ToArray()) + ")" : "")
                + " choco=" + ChocoSwapped + "/" + ChocoFound + " hotpot=" + HotpotSwapped + "/" + HotpotFound + " kiosk=未替换");
        }

        // ------------------------------------------------------------------ 小道具
        struct Slot { public string model; public float x, h, yaw; public Slot(string m, float x, float h, float yaw = 0) { model = m; this.x = x; this.h = h; this.yaw = yaw; } }
        const float Cup = .42f, Caddy = .34f, Ramen = .34f, Skewer = .30f, Crate = .62f;

        static void DressStalls(Arena arena)
        {
            // 柜台：3.6×1.6 m，台面 y≈1.145；原来的三样美食摆在 z=+0.2 一排，小道具摆在 z≈-0.5 那一排（台面边缘内侧）
            var plan = new Dictionary<string, Slot[]>
            {
                { "奶茶", new[] { new Slot("SmallBobaCup", -.62f, Cup, 20), new Slot("SmallBobaCup", -.22f, Cup, -15), new Slot("SmallSauceCaddy", .75f, Caddy, 8) } },
                { "火锅", new[] { new Slot("SmallSauceCaddy", .62f, Caddy, -6) } },
                { "甜品屋", new[] { new Slot("SmallSauceCaddy", .62f, Caddy, 5) } },
                { "拉面", new[] { new Slot("SmallRamenTray", -.62f, Ramen, 4), new Slot("SmallRamenTray", .02f, Ramen, -7), new Slot("SmallSauceCaddy", .82f, Caddy, 0) } },
                { "烧烤串串", new[] { new Slot("SmallSkewerRack", -.38f, Skewer, 0), new Slot("SmallSauceCaddy", .78f, Caddy, -5) } },
            };
            foreach (Transform t in arena.transform)
            {
                if (!t.name.StartsWith("夜市摊位 ")) continue;
                string stall = t.name.Substring(5);
                if (!plan.TryGetValue(stall, out var slots)) continue;
                var avoid = new List<Bounds>();
                foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null && mf.sharedMesh.name == "Cube") continue;   // 柜台本体是支撑面
                    if (r is MeshRenderer || r is SkinnedMeshRenderer) avoid.Add(r.bounds);
                }
                foreach (var s in slots)
                {
                    bool ok = false;
                    // 先按计划的位置放；和原有美食重叠就往柜台外沿挪、左右挪一点再试
                    float[] zs = { -.5f, -.57f, -.6f }, dxs = { 0, -.15f, .15f, -.3f, .3f };
                    for (int zi = 0; zi < zs.Length && !ok; zi++)
                        for (int xi = 0; xi < dxs.Length && !ok; xi++)
                        {
                            Vector3 wp = t.TransformPoint(new Vector3(s.x + dxs[xi], 1.6f, zs[zi]));
                            ok = Place(s.model, wp, t.eulerAngles.y + s.yaw, s.h, avoid, "夜市摊位·" + stall, zi == zs.Length - 1 && xi == dxs.Length - 1) != null;
                        }
                    if (!ok) Skipped.Add(stall + "·" + s.model);
                }
            }
        }

        static void DressStreets()
        {
            // 糖粒补给箱：甜品摊旁 + 街边杂物堆（都贴墙 / 贴着原有杂物，不在走位线上），盖子朝墙
            // V22 第二期迁移：甜品屋挂到高摊位岛西侧、配电箱挪到 x=-32、北长街杂物堆让给寿司吧 —— 三只箱子跟着挪（仍然贴墙 / 贴着原有杂物）
            var crates = new (Vector3 p, float yaw, string area)[]
            {
                (new Vector3(-24.3f, 0, 2.5f), 90, "甜品屋旁（高摊位岛西侧）"),
                (new Vector3(-30.8f, 0, 9.42f), 180, "夜市主街北人行道（配电箱旁）"),
                (new Vector3(-49.7f, 0, 9.42f), 180, "夜市主街北人行道（糖粒堆旁）"),
                (new Vector3(-16.25f, 0, -9.42f), 0, "夜市主街南人行道（拉面桶旁）"),
                (new Vector3(-52.35f, 0, 49.38f), 180, "北长街杂物堆"),
                (new Vector3(-23.55f, 0, 49.38f), 180, "北长街杂物堆（寿司吧东侧）"),
                (new Vector3(-19.1f, 0, -49.38f), 0, "南工厂通道杂物堆"),
                (new Vector3(-48.6f, 0, -49.38f), 0, "南工厂通道杂物堆"),
            };
            foreach (var c in crates)
                if (Place("SmallSprinkleCrate", c.p + Vector3.up * 1.5f, c.yaw, Crate, null, c.area, true) == null) Skipped.Add(c.area + "·SmallSprinkleCrate");
        }

        // 射线贴到支撑面上：中心 + 四角高度一致（不悬空、不探出台面），整体包围盒和碰撞体 / 避让物不重叠（不穿模）
        static GameObject Place(string model, Vector3 from, float yaw, float height, List<Bounds> avoid, string area, bool lastTry)
        {
            var prefab = Resources.Load<GameObject>(Dir + "SmallProps/" + model);
            if (prefab == null) { if (lastTry) Debug.LogWarning("NEON_PROPS missing " + model); return null; }
            var o = Object.Instantiate(prefab);
            foreach (var c in o.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            Arena.Fit(o, new Vector3(0, height, 0));                              // 按高度等比缩放，底面中心对齐原点
            var holder = new GameObject("场景小道具 " + model);
            o.transform.SetParent(holder.transform, false);
            holder.transform.SetParent(Root.transform, false);
            holder.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var rs = o.GetComponentsInChildren<Renderer>();
            Bounds lb = LocalBounds(holder.transform, rs);                       // holder 本地空间的包围盒
            holder.transform.position = from;

            // 支撑面：中心和四个角往下打射线
            float? support = null; bool flat = true;
            var pts = new[] { Vector3.zero, new Vector3(lb.min.x, 0, lb.min.z), new Vector3(lb.max.x, 0, lb.min.z), new Vector3(lb.min.x, 0, lb.max.z), new Vector3(lb.max.x, 0, lb.max.z) };
            for (int i = 0; i < pts.Length; i++)
            {
                Vector3 inset = new Vector3(pts[i].x * .9f, 0, pts[i].z * .9f);
                Vector3 wp = holder.transform.TransformPoint(inset); wp.y = from.y;
                if (!Physics.Raycast(wp, Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore)) { flat = false; break; }
                if (support == null) support = hit.point.y;
                else if (Mathf.Abs(hit.point.y - support.Value) > .02f) { flat = false; break; }
            }
            if (!flat || support == null) { if (lastTry) Debug.Log("NEON_PROPS skip " + model + " @" + area + " (支撑面不平 / 探出边缘)"); Arena.Kill(holder); return null; }
            holder.transform.position = new Vector3(from.x, support.Value, from.z);

            // 重叠检查：底面抬 3 cm，避免把支撑面算进去
            Vector3 c0 = holder.transform.TransformPoint(lb.center + Vector3.up * .03f);
            Vector3 ext = lb.extents - new Vector3(.01f, .03f, .01f);
            var hits = Physics.OverlapBox(c0, ext, holder.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            bool clash = hits.Length > 0;
            Bounds wb = rs[0].bounds; foreach (var r in rs) wb.Encapsulate(r.bounds);
            if (!clash && avoid != null) foreach (var a in avoid) { var aa = a; aa.Expand(-.02f); if (aa.Intersects(wb)) { clash = true; break; } }
            if (clash) { if (lastTry) Debug.Log("NEON_PROPS skip " + model + " @" + area + " (重叠：" + (hits.Length > 0 ? hits[0].name : "摊位美食") + ")"); Arena.Kill(holder); return null; }

            float gap = wb.min.y - support.Value; MaxGap = Mathf.Max(MaxGap, Mathf.Abs(gap));
            foreach (var r in rs) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true; r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes; }
            if (avoid != null) avoid.Add(wb);   // 同一个柜台上后放的道具也要避开先放的
            Placed[model] = (Placed.TryGetValue(model, out int n) ? n : 0) + 1;
            if (!Areas.Contains(area)) Areas.Add(area);
            return holder;
        }

        static Bounds LocalBounds(Transform space, Renderer[] rs)
        {
            bool any = false; Bounds b = default;
            foreach (var r in rs)
            {
                var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = space.InverseTransformPoint(r.transform.TransformPoint(c));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        // ------------------------------------------------------------------ 大道具（只换外观）
        static Mesh chocoMesh, hotpotMesh; static Material[] chocoMats, hotpotMats;

        static void SwapBigProps(Arena arena)
        {
            foreach (Transform t in arena.transform)
            {
                if (t.name == "美食方块" && t.childCount > 0 && t.GetChild(0).name.StartsWith("ChocoBlock"))
                {
                    ChocoFound++;
                    var bc = t.GetComponent<BoxCollider>();
                    if (bc == null || !LoadChoco()) continue;
                    int seed = Mathf.Abs(Mathf.RoundToInt(t.position.x * 7 + t.position.z * 13));
                    // 巧克力掩体：长边沿方块 x；按碰撞盒尺寸拉伸（各边内缩 1–2 cm），咬掉的一头随位置左右翻转
                    var mb = chocoMesh.bounds; var box = bc.size - new Vector3(.02f, 0, .04f);
                    var k = new Vector3(box.x / mb.size.x, box.y / mb.size.y, box.z / mb.size.z);
                    var vis = Visual("巧克力掩体（换皮）", chocoMesh, chocoMats, t, new Vector3(bc.center.x, bc.center.y - bc.size.y / 2, bc.center.z), seed % 2 == 0 ? 0 : 180, k);
                    Hide(t.GetChild(0)); Follow(vis, t.GetChild(0));
                    ChocoRoots.Add(t.gameObject); ChocoSwapped++;
                }
                else if (t.name == "可攀爬美食 HotPot" && t.childCount > 0)
                {
                    HotpotFound++;
                    if (!LoadHotpot()) continue;
                    // 原碰撞：半径 1.35 m、高 0.9 m 的圆柱（顶面就是锅里，站上去被烫）。
                    // 新锅：锅沿外径对齐 1.34 m；汤面对齐 0.9 m，锅沿平台约 0.97 m；遮阳棚已切掉（没有碰撞，会穿人）。正面（笑脸 + 台阶）朝关口蒸笼门，攻方进门先看到
                    // V22 第二期：北关口新增关东煮锅（ClimbFood 同款），这里同样换外观（HotpotFound = 2）
                    var vis = Visual("火锅（换皮）", hotpotMesh, hotpotMats, t, Vector3.zero, -135f, hotpotK);
                    Hide(t.GetChild(0)); Follow(vis, t.GetChild(0));
                    HotpotRoots.Add(t.gameObject); HotpotSwapped++;
                }
            }
        }

        static Vector3 hotpotK;   // 火锅缩放（模型单位 → 米）

        // 把 mesh 摆到 owner 局部坐标 basePos（底面中心），绕 y 转 yaw，按 k 缩放
        static GameObject Visual(string name, Mesh mesh, Material[] mats, Transform owner, Vector3 basePos, float yaw, Vector3 k)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root.transform, false);
            go.transform.position = owner.TransformPoint(basePos);
            go.transform.rotation = owner.rotation;
            var pivot = new GameObject("轴心").transform; pivot.SetParent(go.transform, false);   // 抖动绕底面中心缩放
            var mb = mesh.bounds;
            var model = new GameObject("模型"); model.transform.SetParent(pivot, false);
            model.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            model.transform.localScale = k;
            model.transform.localPosition = model.transform.localRotation * -Vector3.Scale(new Vector3(mb.center.x, mb.min.y, mb.center.z), k);
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = model.AddComponent<MeshRenderer>(); mr.sharedMaterials = mats;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
            return go;
        }

        static void Hide(Transform oldModel) { foreach (var r in oldModel.GetComponentsInChildren<Renderer>(true)) r.enabled = false; }
        static void Follow(GameObject vis, Transform src) { var f = vis.transform.GetChild(0).gameObject.AddComponent<LookFollow>(); f.src = src; }

        static bool Grab(string path, out Mesh mesh, out Material[] mats)
        {
            mesh = null; mats = null;
            var prefab = Resources.Load<GameObject>(Dir + path);
            if (prefab == null) { Debug.LogWarning("NEON_PROPS missing " + path); return false; }
            var mf = prefab.GetComponentInChildren<MeshFilter>(true); var mr = prefab.GetComponentInChildren<MeshRenderer>(true);
            if (mf == null || mf.sharedMesh == null || mr == null) { Debug.LogWarning("NEON_PROPS no mesh " + path); return false; }
            if (!mf.sharedMesh.isReadable) { Debug.LogWarning("NEON_PROPS mesh not readable " + path); return false; }
            if (mf.transform != prefab.transform && (mf.transform.localRotation != Quaternion.identity || mf.transform.localScale != Vector3.one)) Debug.Log("NEON_PROPS note: " + path + " 节点带变换，按网格空间处理");
            mesh = mf.sharedMesh; mats = mr.sharedMaterials;
            return true;
        }

        // 巧克力掩体：侧面的两级台阶会伸出方块碰撞 1.3 m（会穿人），把伸出去的顶点压回主体前脸，变成平的一面
        static bool LoadChoco()
        {
            if (chocoMesh != null) return true;
            if (!Grab("ChocolateCover", out var src, out chocoMats)) return false;
            var v = src.vertices; var n = src.normals;
            float top = src.bounds.max.y, zMin = float.MaxValue, zMax = float.MinValue;
            foreach (var p in v) if (p.y > top * .85f) { zMin = Mathf.Min(zMin, p.z); zMax = Mathf.Max(zMax, p.z); }
            int moved = 0;
            for (int i = 0; i < v.Length; i++)
            {
                if (v[i].z > zMax) { v[i].z = zMax; if (n.Length == v.Length) n[i] = Vector3.forward; moved++; }
                else if (v[i].z < zMin) { v[i].z = zMin; if (n.Length == v.Length) n[i] = Vector3.back; moved++; }
            }
            chocoMesh = Object.Instantiate(src); chocoMesh.name = "ChocolateCover_Block";
            chocoMesh.vertices = v; if (n.Length == v.Length) chocoMesh.normals = n;
            chocoMesh.RecalculateBounds();
            Debug.Log("NEON_PROPS choco flatten moved=" + moved + "/" + v.Length + " depth " + (src.bounds.size.z).ToString("0.000") + "→" + chocoMesh.bounds.size.z.ToString("0.000"));
            return true;
        }

        // 火锅：遮阳棚和柱子高出锅沿（模型高度 66% 以上）没有碰撞，玩家会穿过去 —— 去掉这些三角形
        static bool LoadHotpot()
        {
            if (hotpotMesh != null) return true;
            if (!Grab("HotpotHazard", out var src, out hotpotMats)) return false;
            var v = src.vertices; float cut = src.bounds.min.y + src.bounds.size.y * .66f;
            hotpotMesh = Object.Instantiate(src); hotpotMesh.name = "HotpotHazard_NoCanopy";
            int removed = 0;
            for (int s = 0; s < src.subMeshCount; s++)
            {
                var tri = src.GetTriangles(s); var keep = new List<int>(tri.Length);
                for (int i = 0; i < tri.Length; i += 3)
                {
                    if (v[tri[i]].y > cut || v[tri[i + 1]].y > cut || v[tri[i + 2]].y > cut) { removed++; continue; }
                    keep.Add(tri[i]); keep.Add(tri[i + 1]); keep.Add(tri[i + 2]);
                }
                hotpotMesh.SetTriangles(keep, s);
            }
            hotpotMesh.RecalculateBounds();
            // 模型里：锅沿外径约 0.448（模型宽 0.978），汤面约 0.41，锅沿平台约 0.44（模型高 0.737）
            float unit = src.bounds.size.x / .978f, kxz = 1.34f / (.448f * unit);
            hotpotK = new Vector3(kxz, 2.2f / unit, kxz);   // 汤面 ≈ 0.9 m 与碰撞顶面齐平；横向按锅沿外径
            Debug.Log("NEON_PROPS hotpot canopy removed tris=" + removed + " height " + src.bounds.size.y.ToString("0.000") + "→" + hotpotMesh.bounds.size.y.ToString("0.000"));
            return true;
        }
    }

    // 新外观跟着原模型的"被打抖动"（FoodJiggle 缩放原模型）一起缩放
    public class LookFollow : MonoBehaviour
    {
        public Transform src; Vector3 srcBase, myBase; bool ready;
        void LateUpdate()
        {
            if (src == null) { Destroy(transform.parent != null ? transform.parent.gameObject : gameObject); return; }
            if (!ready) { srcBase = src.localScale; myBase = transform.localScale; ready = true; return; }
            var s = src.localScale;
            transform.localScale = new Vector3(myBase.x * s.x / srcBase.x, myBase.y * s.y / srcBase.y, myBase.z * s.z / srcBase.z);
        }
    }
}
