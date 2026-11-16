using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace NeonSiege
{
    // ================================================================ V23 b：地图 UI 重做（设计稿_V23 §6；拍板 1 = 甲+乙叠加，拍板 2 = 乙 跟随旋转、可切北向）
    // 底图（甲）：运行时按真实几何生成 —— 射线网格（离地 CutHeight 以上切掉，看得到室内）+ NavMesh 可走区域 + 正交相机俯视渲染取色，再做美食调色。
    // 叠加（乙）：结构线（门 / 蒸笼门 / 蛋糕墙 / 高台滴边）+ 美食图标 + 区域名（≤ 12 个、按优先级避让）+ 动态区域 + 食材候选 + 队友 / 敌情。
    // 小地图：以自己为中心、半径 MiniRadius、跟随视角旋转（GameSettings.MinimapRotate=false 时北向固定），圆形，用 GL 画旋转贴图。
    public static class MapBase
    {
        public const float Cell = .25f, Margin = 4f, CutHeight = 4.5f, PlatformMin = 2.4f, LowMax = 1.4f;
        public const int PxPerCell = 2;
        public static float MinX { get { return -Arena.HalfX - Margin; } }
        public static float MinZ { get { return -Arena.HalfZ - Margin; } }
        public static float SizeX { get { return (Arena.HalfX + Margin) * 2; } }
        public static float SizeZ { get { return (Arena.HalfZ + Margin) * 2; } }
        public static int Cols, Rows;
        public static Texture2D Tex, TexMini;       // 全彩底图 / 小地图用（饱和度降 50%、结构线更重）
        public static byte[] Class;                // 每格分类（低 7 位）+ 顶上有东西标记（0x80）
        public static float[] HeightCut;           // 切面以下的最高几何
        public static bool Built; public static int Builds, Refreshes; public static float BuildMs;
        public enum K : byte { Void, Ground, Flat, Low, Wall, Tall, Platform }
        public const byte Overhead = 0x80;
        static Color[] photo; static int photoW, photoH;
        static Color[] px, pxMini;

        public static float PxPerMeter { get { return PxPerCell / Cell; } }
        public static int W { get { return Cols * PxPerCell; } }
        public static int H { get { return Rows * PxPerCell; } }
        // 世界 → 贴图 UV（左下角 = (MinX, MinZ)）
        public static Vector2 UV(Vector3 w) { return new Vector2((w.x - MinX) / SizeX, (w.z - MinZ) / SizeZ); }
        public static Vector3 World(Vector2 uv) { return new Vector3(MinX + uv.x * SizeX, 0, MinZ + uv.y * SizeZ); }
        public static K ClassAt(Vector3 w)
        {
            if (Class == null) return K.Void;
            int cx = Mathf.FloorToInt((w.x - MinX) / Cell), cz = Mathf.FloorToInt((w.z - MinZ) / Cell);
            if (cx < 0 || cz < 0 || cx >= Cols || cz >= Rows) return K.Void;
            return (K)(Class[cz * Cols + cx] & 0x7f);
        }
        public static bool IsGroundColor(K k) { return k == K.Ground || k == K.Platform || k == K.Flat; }
        public static bool IsBuildingColor(K k) { return k == K.Wall || k == K.Tall || k == K.Low; }

        // ---- 调色板（美食 / 写实）
        static bool Food { get { return ToonAssets.Available; } }
        static Color Void { get { return Food ? new Color(.36f, .32f, .46f) : new Color(.08f, .09f, .12f); } }
        static Color Edge { get { return Food ? new Color(.72f, .46f, .56f) : new Color(.12f, .14f, .18f); } }
        static Color Drip { get { return Food ? new Color(1f, 1f, 1f) : new Color(.85f, .88f, .92f); } }
        static Color Palette(K k)
        {
            if (Food)
                switch (k)
                {
                    case K.Low: return new Color(.88f, .70f, .60f);
                    case K.Wall: return new Color(.99f, .93f, .84f);
                    case K.Tall: return new Color(.97f, .84f, .87f);
                    case K.Platform: return new Color(1f, .97f, .90f);
                    default: return new Color(.98f, .80f, .86f);
                }
            switch (k)
            {
                case K.Low: return new Color(.45f, .42f, .40f);
                case K.Wall: return new Color(.70f, .72f, .76f);
                case K.Tall: return new Color(.82f, .84f, .88f);
                case K.Platform: return new Color(.52f, .58f, .64f);
                default: return new Color(.30f, .33f, .38f);
            }
        }
        // 地面按路线着色（对齐 MAP-A1：中路薄荷绿、北桃色、南薰衣草紫、其余粉）
        public static Color RoadColor(float x, float z)
        {
            if (!Food) return new Color(.30f, .33f, .38f);
            int zi = Arena.ZoneAt(new Vector3(x, 0, z));
            string n = zi >= 0 ? Arena.Zones[zi].name : "";
            if (n.Contains("主街") || n.Contains("中段十字") || n.Contains("中庭")) return new Color(.74f, .90f, .80f);
            if (n.StartsWith("北") ) return new Color(.99f, .82f, .72f);
            if (n.StartsWith("南")) return new Color(.84f, .78f, .96f);
            if (n.StartsWith("A 点")) return new Color(.99f, .84f, .74f);
            if (n.StartsWith("B 点")) return new Color(.80f, .88f, .98f);
            if (n.Contains("攻方")) return new Color(.72f, .82f, .98f);
            if (n.Contains("守方")) return new Color(.99f, .78f, .66f);
            return new Color(.98f, .80f, .86f);
        }

        // ================================================================ 生成
        public static void Build()
        {
            if (Arena.I == null) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Cols = Mathf.RoundToInt(SizeX / Cell); Rows = Mathf.RoundToInt(SizeZ / Cell);
            Class = new byte[Cols * Rows]; HeightCut = new float[Cols * Rows];
            Photo();
            Classify(0, 0, Cols, Rows);
            Dilate(0, 0, Cols, Rows);
            if (Tex == null || Tex.width != W) { Tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "地图底图" }; TexMini = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "小地图底图" }; px = new Color[W * H]; pxMini = new Color[W * H]; }
            Compose(0, 0, Cols, Rows);
            Tex.SetPixels(px); Tex.Apply(false); TexMini.SetPixels(pxMini); TexMini.Apply(false);
            Built = true; Builds++; BuildMs = (float)sw.Elapsed.TotalMilliseconds;
        }

        // 分帧生成（游戏里用，避免开局卡 2–3 秒）：照片 1 帧，之后按行分 3 遍（分类 / 膨胀 / 调色），每帧最多 budgetMs 毫秒
        static int stage = -1, row; static byte[] dilateSrc; static System.Diagnostics.Stopwatch total;
        public static bool Building { get { return stage >= 0; } }
        public static void BuildBegin()
        {
            if (Arena.I == null) return;
            total = System.Diagnostics.Stopwatch.StartNew();
            Cols = Mathf.RoundToInt(SizeX / Cell); Rows = Mathf.RoundToInt(SizeZ / Cell);
            Class = new byte[Cols * Rows]; HeightCut = new float[Cols * Rows];
            Photo();
            if (Tex == null || Tex.width != W) { Tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "地图底图" }; TexMini = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "小地图底图" }; px = new Color[W * H]; pxMini = new Color[W * H]; }
            stage = 0; row = 0;
        }
        public static bool BuildStep(float budgetMs)
        {
            if (stage < 0) return true;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed.TotalMilliseconds < budgetMs)
            {
                if (stage == 0) { Classify(0, row, Cols, Mathf.Min(Rows, row + 4)); row += 4; if (row >= Rows) { stage = 1; row = 0; dilateSrc = (byte[])Class.Clone(); } }
                else if (stage == 1) { DilateRows(dilateSrc, row, Mathf.Min(Rows, row + 8)); row += 8; if (row >= Rows) { stage = 2; row = 0; dilateSrc = null; } }
                else { Compose(0, row, Cols, Mathf.Min(Rows, row + 8)); row += 8; if (row >= Rows) { Tex.SetPixels(px); Tex.Apply(false); TexMini.SetPixels(pxMini); TexMini.Apply(false); Built = true; Builds++; BuildMs = (float)total.Elapsed.TotalMilliseconds; stage = -1; return true; } }
            }
            return false;
        }

        // 局部刷新（门开关 / 墙被炸 / 回合重置）：只重算这一片格子，不重渲照片
        public static void Refresh(Rect world)
        {
            if (!Built) return;
            int x0 = Mathf.Clamp(Mathf.FloorToInt((world.xMin - MinX) / Cell) - 3, 0, Cols), x1 = Mathf.Clamp(Mathf.CeilToInt((world.xMax - MinX) / Cell) + 3, 0, Cols);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((world.yMin - MinZ) / Cell) - 3, 0, Rows), z1 = Mathf.Clamp(Mathf.CeilToInt((world.yMax - MinZ) / Cell) + 3, 0, Rows);
            if (x1 <= x0 || z1 <= z0) return;
            Classify(x0, z0, x1, z1);
            Dilate(x0, z0, x1, z1);
            Compose(x0, z0, x1, z1);
            int pw = (x1 - x0) * PxPerCell, ph = (z1 - z0) * PxPerCell;
            var block = new Color[pw * ph]; var blockM = new Color[pw * ph];
            for (int y = 0; y < ph; y++) for (int x = 0; x < pw; x++) { int src = (z0 * PxPerCell + y) * W + x0 * PxPerCell + x; block[y * pw + x] = px[src]; blockM[y * pw + x] = pxMini[src]; }
            Tex.SetPixels(x0 * PxPerCell, z0 * PxPerCell, pw, ph, block); Tex.Apply(false);
            TexMini.SetPixels(x0 * PxPerCell, z0 * PxPerCell, pw, ph, blockM); TexMini.Apply(false);
            Refreshes++;
        }

        // 正交相机从正上方渲一张"照片"：近裁剪面放在离地 CutHeight，楼顶切掉；角色 / 拾取物 / 标记先藏起来
        static void Photo()
        {
            photoW = W; photoH = H; photo = new Color[photoW * photoH];
            var go = new GameObject("地图俯拍相机"); var cam = go.AddComponent<Camera>();
            cam.enabled = false; cam.orthographic = true; cam.orthographicSize = SizeZ / 2; cam.aspect = SizeX / SizeZ;
            cam.transform.position = new Vector3(0, 60, 0); cam.transform.rotation = Quaternion.Euler(90, 0, 0);
            cam.nearClipPlane = 60 - CutHeight; cam.farClipPlane = 60 + 3; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.cullingMask = ~0; cam.allowHDR = false; cam.allowMSAA = false; cam.useOcclusionCulling = false;
            var hidden = new List<Renderer>();
            // 排除：角色、拾取物、粒子 / 拖尾 / 线、世界文字、交互标记气泡、食材光柱、出生区屏障
            foreach (var r in Object.FindObjectsOfType<Renderer>())
            {
                if (!r.enabled) continue;
                var root = r.transform.root;
                bool skip = r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer || r.GetComponent<TextMesh>() != null
                    || r.GetComponentInParent<Fighter>() != null || r.GetComponentInParent<IngredientPickup>() != null
                    || root.name == "交互标记" || root.name == "食材指引" || r.gameObject.name.StartsWith("出生区屏障");
                if (skip) { r.enabled = false; hidden.Add(r); }
            }
            bool fog = RenderSettings.fog; RenderSettings.fog = false;
            var rt = RenderTexture.GetTemporary(photoW, photoH, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            try
            {
                cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var t = new Texture2D(photoW, photoH, TextureFormat.RGBA32, false);
                t.ReadPixels(new Rect(0, 0, photoW, photoH), 0, 0); t.Apply(false);
                photo = t.GetPixels(); Object.Destroy(t);
                RenderTexture.active = prev;
            }
            catch (System.Exception e) { Debug.LogWarning("地图俯拍失败：" + e.Message); }
            cam.targetTexture = null; RenderTexture.ReleaseTemporary(rt);
            RenderSettings.fog = fog;
            foreach (var r in hidden) if (r != null) r.enabled = true;
            Object.Destroy(go);
        }

        // 每格：切面以下最高几何（忽略角色 / 触发器）+ 不切的最高几何（判断顶上有没有楼顶 / 连廊）+ NavMesh 可走
        static void Classify(int x0, int z0, int x1, int z1)
        {
            var hits = new RaycastHit[8];
            for (int cz = z0; cz < z1; cz++)
                for (int cx = x0; cx < x1; cx++)
                {
                    float wx = MinX + (cx + .5f) * Cell, wz = MinZ + (cz + .5f) * Cell;
                    int i = cz * Cols + cx;
                    if (Mathf.Abs(wx) > Arena.HalfX || Mathf.Abs(wz) > Arena.HalfZ) { Class[i] = (byte)K.Void; HeightCut[i] = 0; continue; }
                    float hCut = Top(new Vector3(wx, CutHeight, wz), CutHeight + 1f, hits);
                    float hTop = Top(new Vector3(wx, 60f, wz), 61f, hits);
                    HeightCut[i] = hCut;
                    bool walk = NavMesh.SamplePosition(new Vector3(wx, Mathf.Max(hCut, 0) + .2f, wz), out NavMeshHit nh, .45f, NavMesh.AllAreas) && Mathf.Abs(nh.position.x - wx) < .3f && Mathf.Abs(nh.position.z - wz) < .3f;
                    K k;
                    if (walk) k = nh.position.y >= PlatformMin ? K.Platform : K.Ground;
                    else if (hCut >= LowMax) k = hTop > CutHeight + .5f ? K.Tall : K.Wall;
                    else if (hTop > CutHeight + .5f) k = K.Tall;   // 4.5 m 以上的实心楼：射线从楼体内部出发打不到它，只能靠"顶上有东西 + 不可走"判断
                    else if (hCut >= .3f) k = K.Low;
                    else k = K.Flat;
                    byte b = (byte)k;
                    if (hTop >= CutHeight && hTop > hCut + 2.5f && k != K.Tall) b |= Overhead;
                    Class[i] = b;
                }
        }
        static float Top(Vector3 from, float dist, RaycastHit[] buf)
        {
            int n = Physics.RaycastNonAlloc(from, Vector3.down, buf, dist, ~0, QueryTriggerInteraction.Ignore);
            float best = -1f;
            for (int j = 0; j < n; j++)
            {
                var c = buf[j].collider; if (c == null) continue;
                if (c is CharacterController || c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue;   // 角色 / 飞行中的手雷
                if (c.gameObject.name.StartsWith("出生区屏障")) continue;
                best = Mathf.Max(best, buf[j].point.y);
            }
            return best < 0 ? 0f : best;
        }
        // NavMesh 被角色半径收了一圈：平地格子 3 格内有可走格就算地面
        static void Dilate(int x0, int z0, int x1, int z1) { DilateRect((byte[])Class.Clone(), x0, z0, x1, z1); }
        static void DilateRows(byte[] src, int z0, int z1) { DilateRect(src, 0, z0, Cols, z1); }
        static void DilateRect(byte[] src, int x0, int z0, int x1, int z1)
        {
            for (int cz = z0; cz < z1; cz++)
                for (int cx = x0; cx < x1; cx++)
                {
                    int i = cz * Cols + cx; if ((src[i] & 0x7f) != (byte)K.Flat) continue;
                    bool near = false;
                    for (int dz = -3; dz <= 3 && !near; dz++) for (int dx = -3; dx <= 3 && !near; dx++)
                    {
                        int nx = cx + dx, nz = cz + dz; if (nx < 0 || nz < 0 || nx >= Cols || nz >= Rows) continue;
                        if ((src[nz * Cols + nx] & 0x7f) == (byte)K.Ground) near = true;
                    }
                    if (near) Class[i] = (byte)((src[i] & Overhead) | (byte)K.Ground);
                }
        }
        static K KAt(int cx, int cz) { if (cx < 0 || cz < 0 || cx >= Cols || cz >= Rows) return K.Void; return (K)(Class[cz * Cols + cx] & 0x7f); }
        static bool Open(K k) { return k == K.Ground || k == K.Flat || k == K.Platform; }
        public const float HatchBlend = .12f, TopLight = .3f, ShadowMul = .8f;   // V23 打磨：斜纹 0.28 → 0.12；顶面高光 / 投影
        static void Compose(int x0, int z0, int x1, int z1)
        {
            bool food = Food;
            for (int cz = z0; cz < z1; cz++)
                for (int cx = x0; cx < x1; cx++)
                {
                    int i = cz * Cols + cx; K k = (K)(Class[i] & 0x7f); bool over = (Class[i] & Overhead) != 0;
                    float wx = MinX + (cx + .5f) * Cell, wz = MinZ + (cz + .5f) * Cell;
                    Color c;
                    if (k == K.Void) c = Void;
                    else if (k == K.Ground) c = RoadColor(wx, wz);
                    else if (k == K.Flat) c = RoadColor(wx, wz) * .9f;
                    else c = Palette(k);
                    // 照片取色：建筑 / 矮掩体掺 35%（"粉色波点那栋楼"），地面掺 12%
                    if (photo != null && k != K.Void)
                    {
                        var ph = photo[(cz * PxPerCell) * photoW + cx * PxPerCell];
                        if (ph.a > .5f)
                        {
                            Color.RGBToHSV(ph, out float hh, out float ss, out float vv);
                            var lifted = Color.HSVToRGB(hh, Mathf.Min(ss, food ? .55f : .25f), Mathf.Clamp(vv * 1.4f, food ? .72f : .35f, food ? 1f : .9f));
                            c = Color.Lerp(c, lifted, IsBuildingColor(k) ? .35f : .12f);
                        }
                    }
                    // 结构线：建筑贴地面 → 描边；高台边缘 → 奶油滴边
                    bool edge = false, drip = false;
                    if (IsBuildingColor(k) || k == K.Platform)
                        for (int d = 0; d < 4; d++)
                        {
                            K n = KAt(cx + (d == 0 ? 1 : d == 1 ? -1 : 0), cz + (d == 2 ? 1 : d == 3 ? -1 : 0));
                            if (k == K.Platform) { if (n != K.Platform && n != K.Void) drip = true; }
                            else if (n == K.Ground || n == K.Flat || n == K.Platform) edge = true;
                        }
                    // V23 打磨：立体感 —— 建筑内侧左上一圈顶面高光、右下一圈压暗；地面上建筑的右下方 2 格投影
                    if (IsBuildingColor(k))
                    {
                        if (Open(KAt(cx - 2, cz)) || Open(KAt(cx, cz + 2)) || Open(KAt(cx - 1, cz)) || Open(KAt(cx, cz + 1))) c = Color.Lerp(c, Color.white, TopLight);
                        else if (Open(KAt(cx + 2, cz)) || Open(KAt(cx, cz - 2))) c *= .9f;
                    }
                    else if (k == K.Ground || k == K.Flat || k == K.Platform)
                    {
                        if (IsBuildingColor(KAt(cx - 1, cz + 1)) || IsBuildingColor(KAt(cx - 2, cz + 2))) c *= ShadowMul;
                    }
                    if (over) { int s = ((cx + cz) % 6); if (s == 0) c = Color.Lerp(c, food ? new Color(.85f, .55f, .72f) : new Color(.35f, .38f, .45f), HatchBlend); }
                    Color cm = Color.Lerp(c, new Color(.5f, .5f, .5f) * (c.r + c.g + c.b) / 1.5f, .5f);
                    if (edge) { c = Edge; cm = Edge; } else if (drip) { c = Drip; cm = Drip; }
                    c.a = 1; cm.a = 1;
                    for (int py = 0; py < PxPerCell; py++) for (int pxx = 0; pxx < PxPerCell; pxx++)
                    {
                        int pi = (cz * PxPerCell + py) * W + cx * PxPerCell + pxx; px[pi] = c; pxMini[pi] = cm;
                    }
                }
        }
        public static void Dispose() { Built = false; stage = -1; if (Tex != null) Object.Destroy(Tex); if (TexMini != null) Object.Destroy(TexMini); Tex = TexMini = null; Class = null; photo = null; }
    }

    // ================================================================ 视图：世界 ↔ 屏幕（含旋转），小地图和大地图共用
    public struct MapView
    {
        public Vector2 center;      // 屏幕中心（GUI 坐标）
        public Vector3 worldCenter; // 对应的世界点
        public float pxPerM, rotDeg;  // rotDeg：地图顺时针转多少度（跟随视角 = -yaw）
        public float clipRadius;    // > 0：圆形裁剪（小地图）
        public Vector2 Map(Vector3 w)
        {
            float dx = (w.x - worldCenter.x) * pxPerM, dy = -(w.z - worldCenter.z) * pxPerM;
            float a = rotDeg * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector2(center.x + dx * c - dy * s, center.y + dx * s + dy * c);
        }
        public Vector3 Unmap(Vector2 g)
        {
            float a = -rotDeg * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            float dx = g.x - center.x, dy = g.y - center.y;
            float rx = dx * c - dy * s, ry = dx * s + dy * c;
            return new Vector3(worldCenter.x + rx / pxPerM, 0, worldCenter.z - ry / pxPerM);
        }
        public bool Inside(Vector2 g, float pad = 0) { return clipRadius <= 0 || (g - center).magnitude <= clipRadius - pad; }
        // 超出圆的点贴边
        public Vector2 Clamp(Vector2 g, float pad) { if (clipRadius <= 0) return g; var d = g - center; float m = d.magnitude; return m <= clipRadius - pad ? g : center + d / m * (clipRadius - pad); }
    }

    // ================================================================ GL 画图（旋转贴图圆盘、扇形、圆环都走这里；坐标用 GUI 逻辑坐标，内部换算成像素）
    public static class MapGL
    {
        static Material mat;
        static float S { get { return GUI.matrix.m00 > 0 ? GUI.matrix.m00 : 1f; } }
        static Vector3 P(Vector2 g) { float s = S; return new Vector3(g.x * s, Screen.height - g.y * s, 0); }
        static Material Mat { get { if (mat == null) { mat = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.HideAndDontSave }; } return mat; } }
        static bool Begin(Texture t)
        {
            if (Event.current != null && Event.current.type != EventType.Repaint) return false;
            var m = Mat; m.mainTexture = t != null ? t : Texture2D.whiteTexture; if (!m.SetPass(0)) return false;
            GL.PushMatrix(); GL.LoadPixelMatrix(); return true;
        }
        static void End() { GL.PopMatrix(); }

        // 圆盘：uvOf 给每个顶点的贴图坐标（旋转小地图）
        public static void TexDisc(Vector2 c, float r, Texture t, System.Func<Vector2, Vector2> uvOf, Color tint, int seg = 72)
        {
            if (!Begin(t)) return;
            GL.Begin(GL.TRIANGLES); GL.Color(tint);
            Vector2 prev = c + new Vector2(r, 0);
            for (int i = 1; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2; Vector2 cur = c + new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
                GL.TexCoord2(uvOf(c).x, uvOf(c).y); GL.Vertex(P(c));
                GL.TexCoord2(uvOf(prev).x, uvOf(prev).y); GL.Vertex(P(prev));
                GL.TexCoord2(uvOf(cur).x, uvOf(cur).y); GL.Vertex(P(cur));
                prev = cur;
            }
            GL.End(); End();
        }
        public static void Disc(Vector2 c, float r, Color col, int seg = 40) { Wedge(c, r, 0, 360, col, seg); }
        // 扇形：角度 0 = 正上方，顺时针
        public static void Wedge(Vector2 c, float r, float a0, float a1, Color col, int seg = 24)
        {
            if (!Begin(null)) return;
            GL.Begin(GL.TRIANGLES); GL.Color(col);
            Vector2 prev = CombatHud.ArcPoint(c, r, a0);
            for (int i = 1; i <= seg; i++)
            {
                Vector2 cur = CombatHud.ArcPoint(c, r, Mathf.Lerp(a0, a1, i / (float)seg));
                GL.TexCoord2(.5f, .5f); GL.Vertex(P(c)); GL.TexCoord2(.5f, .5f); GL.Vertex(P(prev)); GL.TexCoord2(.5f, .5f); GL.Vertex(P(cur));
                prev = cur;
            }
            GL.End(); End();
        }
        public static void Ring(Vector2 c, float r, float thick, Color col, float a0 = 0, float a1 = 360, int seg = 48)
        {
            if (!Begin(null)) return;
            GL.Begin(GL.TRIANGLE_STRIP); GL.Color(col);
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)seg);
                GL.TexCoord2(.5f, .5f); GL.Vertex(P(CombatHud.ArcPoint(c, r + thick / 2, a)));
                GL.TexCoord2(.5f, .5f); GL.Vertex(P(CombatHud.ArcPoint(c, r - thick / 2, a)));
            }
            GL.End(); End();
        }
        public static void Line(Vector2 a, Vector2 b, float w, Color col)
        {
            if (!Begin(null)) return;
            Vector2 d = (b - a).normalized; Vector2 n = new Vector2(-d.y, d.x) * w / 2;
            GL.Begin(GL.QUADS); GL.Color(col);
            GL.TexCoord2(.5f, .5f); GL.Vertex(P(a + n)); GL.TexCoord2(.5f, .5f); GL.Vertex(P(b + n)); GL.TexCoord2(.5f, .5f); GL.Vertex(P(b - n)); GL.TexCoord2(.5f, .5f); GL.Vertex(P(a - n));
            GL.End(); End();
        }
        // 三角箭头：指向 dir（屏幕方向，y 向下）
        public static void Tri(Vector2 tip, Vector2 dir, float len, float wid, Color col)
        {
            if (!Begin(null)) return;
            dir.Normalize(); Vector2 n = new Vector2(-dir.y, dir.x);
            GL.Begin(GL.TRIANGLES); GL.Color(col);
            GL.TexCoord2(.5f, .5f); GL.Vertex(P(tip)); GL.TexCoord2(.5f, .5f); GL.Vertex(P(tip - dir * len + n * wid / 2)); GL.TexCoord2(.5f, .5f); GL.Vertex(P(tip - dir * len - n * wid / 2));
            GL.End(); End();
        }
        public static void Poly(IList<Vector2> pts, Color col)
        {
            if (pts.Count < 3 || !Begin(null)) return;
            GL.Begin(GL.TRIANGLES); GL.Color(col);
            for (int i = 1; i + 1 < pts.Count; i++) { GL.TexCoord2(.5f, .5f); GL.Vertex(P(pts[0])); GL.TexCoord2(.5f, .5f); GL.Vertex(P(pts[i])); GL.TexCoord2(.5f, .5f); GL.Vertex(P(pts[i + 1])); }
            GL.End(); End();
        }
    }

    // ================================================================ 美食图标集（§6.4）：程序绘制，32–48 px，缓存
    public static class MapIcons
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
        delegate Color Px(float x, float y);   // x, y ∈ [-1, 1]，y 向上
        static Texture2D Paint(string key, int n, Px f)
        {
            if (cache.TryGetValue(key, out var t) && t != null) return t;
            t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, name = "图标 " + key };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                // 2×2 超采样抗锯齿
                Color acc = Color.clear;
                for (int sy = 0; sy < 2; sy++) for (int sx = 0; sx < 2; sx++) acc += f((x + .25f + sx * .5f) / n * 2 - 1, (y + .25f + sy * .5f) / n * 2 - 1);
                px[y * n + x] = acc / 4f;
            }
            t.SetPixels(px); t.Apply(true); cache[key] = t; return t;
        }
        static bool InCircle(float x, float y, float cx, float cy, float r) { return (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r; }
        static bool InBox(float x, float y, float cx, float cy, float hx, float hy) { return Mathf.Abs(x - cx) <= hx && Mathf.Abs(y - cy) <= hy; }
        static bool InRBox(float x, float y, float cx, float cy, float hx, float hy, float r)
        {
            float dx = Mathf.Abs(x - cx) - (hx - r), dy = Mathf.Abs(y - cy) - (hy - r);
            if (dx <= 0 && dy <= 0) return true; if (dx > r || dy > r) return false;
            if (dx <= 0 || dy <= 0) return true; return dx * dx + dy * dy <= r * r;
        }
        static readonly Color Ink = new Color(.35f, .18f, .28f), Cream = new Color(1f, .95f, .86f), Pink = new Color(1f, .62f, .78f), Choc = new Color(.45f, .27f, .2f), Straw = new Color(.95f, .3f, .4f);
        static Color Outline(bool inside, Color c, float x, float y, System.Func<float, float, bool> shape, float w = .12f)
        {
            if (!inside) return Color.clear;
            // 边缘一圈深色描边
            bool edge = !shape(x + w, y) || !shape(x - w, y) || !shape(x, y + w) || !shape(x, y - w);
            return edge ? Ink : c;
        }

        // 生日蛋糕（包点）：两层 + 蜡烛
        public static Texture2D Cake(bool lit) { return Paint("cake" + lit, 48, (x, y) =>
        {
            System.Func<float, float, bool> body = (a, b) => InRBox(a, b, 0, -.45f, .85f, .3f, .15f) || InRBox(a, b, 0, .05f, .62f, .25f, .15f);
            if (body(x, y)) return Outline(true, InRBox(x, y, 0, .05f, .62f, .25f, .15f) ? Pink : Cream, x, y, body);
            if (InBox(x, y, 0, .45f, .07f, .2f)) return Choc;
            if (lit && InCircle(x, y, 0, .78f, .17f)) return new Color(1f, .85f, .3f);
            if (!lit && InCircle(x, y, 0, .72f, .1f)) return Ink;
            return Color.clear;
        }); }
        // 餐盘（出生点）
        public static Texture2D Plate { get { return Paint("plate", 40, (x, y) =>
        {
            if (!InCircle(x, y, 0, 0, .92f)) return Color.clear;
            if (!InCircle(x, y, 0, 0, .8f)) return Ink;
            if (!InCircle(x, y, 0, 0, .55f)) return Cream;
            if (!InCircle(x, y, 0, 0, .45f)) return new Color(.85f, .8f, .78f);
            return Cream;
        }); } }
        // 蒸笼（蒸笼门）：开 = 盖子掀起
        public static Texture2D Steamer(bool open) { return Paint("steamer" + open, 40, (x, y) =>
        {
            System.Func<float, float, bool> b = (a, c) => InRBox(a, c, 0, -.3f, .8f, .4f, .12f);
            if (b(x, y)) return Outline(true, new Color(.93f, .78f, .5f), x, y, b);
            if (InBox(x, y, 0, -.3f, .7f, .04f)) return Choc;
            if (open) { if (InRBox(x, y, .35f, .55f, .55f, .13f, .1f) && Mathf.Abs((y - .55f) - (x - .35f) * .45f) < .14f) return new Color(.85f, .65f, .4f); }
            else if (InRBox(x, y, 0, .3f, .85f, .15f, .1f)) return new Color(.85f, .65f, .4f);
            if (!open && InBox(x, y, 0, -.3f, .9f, .5f) && !InBox(x, y, 0, -.3f, .78f, .38f)) return new Color(1f, .3f, .3f);
            return Color.clear;
        }); }
        // 小门（卷帘门 / 侧门）
        public static Texture2D Door(bool open) { return Paint("door" + open, 32, (x, y) =>
        {
            if (InBox(x, y, 0, 0, .75f, .85f) && !InBox(x, y, 0, -.1f, .55f, .7f)) return Ink;
            if (open) return InBox(x, y, -.35f, -.1f, .2f, .7f) ? new Color(.6f, .9f, .6f) : Color.clear;
            return InBox(x, y, 0, -.1f, .55f, .7f) ? (Mathf.Repeat(y * 3f, 1f) < .5f ? new Color(1f, .7f, .35f) : new Color(.9f, .55f, .25f)) : Color.clear;
        }); }
        // 蛋糕切块（蛋糕墙）：完整 / 缺一口
        public static Texture2D Slice(bool holed) { return Paint("slice" + holed, 40, (x, y) =>
        {
            System.Func<float, float, bool> tri = (a, b) => b >= -.8f && b <= .8f && Mathf.Abs(a) <= (b + .8f) * .55f;
            bool inside = tri(x, y) && !(holed && InCircle(x, y, .35f, .6f, .35f));
            if (!inside) return Color.clear;
            Color c = y > .4f ? Pink : y > -.1f ? Cream : Choc;
            return Outline(true, c, x, y, (a, b) => tri(a, b) && !(holed && InCircle(a, b, .35f, .6f, .35f)));
        }); }
        // 被咬一口的饼干（墙洞 / 破口）
        public static Texture2D Cookie(bool open) { return Paint("cookie" + open, 32, (x, y) =>
        {
            bool inside = InCircle(x, y, 0, 0, .85f) && !(open && InCircle(x, y, .6f, .6f, .45f));
            if (!inside) return Color.clear;
            Color c = new Color(.85f, .6f, .35f);
            if (InCircle(x, y, -.3f, .2f, .12f) || InCircle(x, y, .2f, -.35f, .12f) || InCircle(x, y, .25f, .25f, .1f)) c = Choc;
            return Outline(true, c, x, y, (a, b) => InCircle(a, b, 0, 0, .85f) && !(open && InCircle(a, b, .6f, .6f, .45f)));
        }); }
        // 锅（火锅 / 关东煮 / 抢食大锅）
        public static Texture2D Pot(Color soup) { return Paint("pot" + ColorUtility.ToHtmlStringRGB(soup), 40, (x, y) =>
        {
            System.Func<float, float, bool> body = (a, b) => InCircle(a, b, 0, -.15f, .7f) && b <= .1f || InBox(a, b, 0, .1f, .78f, .1f) || InBox(a, b, 0, .08f, .98f, .07f);
            if (body(x, y)) return Outline(true, InBox(x, y, 0, .1f, .78f, .1f) ? soup : new Color(.9f, .35f, .3f), x, y, body);
            if (InCircle(x, y, -.25f, .5f, .16f) || InCircle(x, y, .2f, .7f, .14f) || InCircle(x, y, .05f, .4f, .12f)) return new Color(1f, 1f, 1f, .8f);
            return Color.clear;
        }); }
        // 茶壶（蒸汽阀 / 开大火旋钮）
        public static Texture2D Teapot { get { return Paint("teapot", 40, (x, y) =>
        {
            System.Func<float, float, bool> body = (a, b) => InCircle(a, b, -.1f, -.2f, .62f) || InBox(a, b, .62f, .05f, .25f, .1f) && a > .3f || InBox(a, b, -.1f, .45f, .22f, .08f) || InCircle(a, b, -.1f, .55f, .1f);
            if (body(x, y)) return Outline(true, Cream, x, y, body);
            if (InCircle(x, y, -.1f, -.2f, .62f) == false && InCircle(x, y, -.75f, -.1f, .32f) && !InCircle(x, y, -.75f, -.1f, .2f)) return Ink;
            return Color.clear;
        }); } }
        // 果冻（弹床）
        public static Texture2D Jelly { get { return Paint("jelly", 36, (x, y) =>
        {
            System.Func<float, float, bool> b = (a, c) => InCircle(a, c, 0, -.3f, .78f) && c >= -.5f || InBox(a, c, 0, -.6f, .85f, .12f);
            if (!b(x, y)) return Color.clear;
            Color c2 = new Color(.55f, .9f, .55f); if (InCircle(x, y, -.25f, .05f, .14f)) c2 = Color.white;
            return Outline(true, c2, x, y, b);
        }); } }
        // 奶茶杯 / 烤箱 / 冰柜 / 回血炉
        public static Texture2D Station(KitchenStation.Kind k) { return Paint("station" + k, 36, (x, y) =>
        {
            switch (k)
            {
                case KitchenStation.Kind.MilkTea:
                {
                    System.Func<float, float, bool> cup = (a, b) => b >= -.8f && b <= .5f && Mathf.Abs(a) <= .35f + (b + .8f) * .25f;
                    if (cup(x, y)) return Outline(true, InCircle(x, y, -.2f, -.5f, .12f) || InCircle(x, y, .15f, -.35f, .12f) ? Choc : new Color(.93f, .78f, .62f), x, y, cup);
                    if (InBox(x, y, .15f, .55f, .07f, .4f)) return Straw; return Color.clear;
                }
                case KitchenStation.Kind.Oven:
                {
                    System.Func<float, float, bool> b = (a, c) => InRBox(a, c, 0, 0, .8f, .7f, .1f);
                    if (!b(x, y)) return Color.clear;
                    if (InBox(x, y, 0, -.15f, .55f, .35f)) return InBox(x, y, 0, -.15f, .45f, .25f) ? new Color(1f, .6f, .25f) : Ink;
                    if (InCircle(x, y, -.4f, .45f, .09f) || InCircle(x, y, 0, .45f, .09f) || InCircle(x, y, .4f, .45f, .09f)) return Ink;
                    return Outline(true, new Color(.85f, .85f, .88f), x, y, b);
                }
                case KitchenStation.Kind.Freezer:
                {
                    System.Func<float, float, bool> b = (a, c) => InRBox(a, c, 0, 0, .65f, .85f, .1f);
                    if (!b(x, y)) return Color.clear;
                    bool flake = Mathf.Abs(x) < .07f && Mathf.Abs(y) < .5f || Mathf.Abs(y) < .07f && Mathf.Abs(x) < .45f || Mathf.Abs(Mathf.Abs(x) - Mathf.Abs(y)) < .08f && Mathf.Abs(x) < .38f;
                    return Outline(true, flake ? Color.white : new Color(.6f, .85f, 1f), x, y, b);
                }
                default:
                {
                    System.Func<float, float, bool> body = (a, b) => InCircle(a, b, 0, -.15f, .7f) && b <= .1f || InBox(a, b, 0, .1f, .78f, .1f);
                    if (!body(x, y)) return Color.clear;
                    bool plus = Mathf.Abs(x) < .09f && Mathf.Abs(y + .2f) < .35f || Mathf.Abs(y + .2f) < .09f && Mathf.Abs(x) < .35f;
                    return Outline(true, plus ? Color.white : new Color(.95f, .4f, .35f), x, y, body);
                }
            }
        }); }
        // 外卖袋
        public static Texture2D Bag { get { return Paint("bag", 36, (x, y) =>
        {
            System.Func<float, float, bool> b = (a, c) => c >= -.85f && c <= .35f && Mathf.Abs(a) <= .55f + (c + .85f) * .15f;
            if (b(x, y)) return Outline(true, InCircle(x, y, 0, -.25f, .22f) ? Straw : new Color(1f, .78f, .62f), x, y, b);
            if (y > .3f && y < .8f && Mathf.Abs(Mathf.Abs(x) - .3f) < .07f) return Ink;
            return Color.clear;
        }); } }
        // 曼妥思（糖柱）
        public static Texture2D Mentos { get { return Paint("mentos", 32, (x, y) =>
        {
            System.Func<float, float, bool> b = (a, c) => InRBox(a, c, 0, 0, .45f, .85f, .3f);
            if (!b(x, y)) return Color.clear;
            return Outline(true, Mathf.Repeat(y * 2.5f + .3f, 1f) < .5f ? Color.white : new Color(.9f, .85f, .75f), x, y, b);
        }); } }
        // 食材圆片（本色 + 白圈）
        public static Texture2D Ingredient(string id) { var col = Ingredients.Col(id); return Paint("ing" + id, 32, (x, y) =>
        {
            if (!InCircle(x, y, 0, 0, .92f)) return Color.clear;
            if (!InCircle(x, y, 0, 0, .78f)) return Color.white;
            if (!InCircle(x, y, 0, 0, .68f)) return Ink;
            return InCircle(x, y, -.25f, .25f, .18f) ? Color.Lerp(col, Color.white, .6f) : col;
        }); }
        // 箭头（自己 / 队友）
        public static Texture2D Arrow { get { return Paint("maparrow", 32, (x, y) =>
        {
            bool inside = y <= .85f && y >= -.75f && Mathf.Abs(x) <= (.85f - y) * .55f && !(y < -.25f && Mathf.Abs(x) < (-.25f - y) * .6f);
            return inside ? Color.white : Color.clear;
        }); } }
        public static Texture2D Cross { get { return Paint("cross", 32, (x, y) => Mathf.Abs(Mathf.Abs(x) - Mathf.Abs(y)) < .22f && Mathf.Abs(x) < .8f ? Color.white : Color.clear); } }
        public static Texture2D Star { get { return Paint("star", 32, (x, y) =>
        {
            float a = Mathf.Atan2(y, x), r = Mathf.Sqrt(x * x + y * y);
            float k = .55f + .4f * Mathf.Abs(Mathf.Cos(a * 2.5f + Mathf.PI / 2));
            return r <= k * .95f ? Color.white : Color.clear;
        }); } }
        public static Texture2D Bomb { get { return Paint("bomb", 32, (x, y) =>
        {
            if (InCircle(x, y, 0, -.15f, .62f)) return InCircle(x, y, -.2f, .05f, .14f) ? Color.white : Ink;
            if (InBox(x, y, .25f, .55f, .1f, .2f)) return Choc; if (InCircle(x, y, .35f, .82f, .14f)) return new Color(1f, .8f, .3f);
            return Color.clear;
        }); } }

        public static void Draw(Rect r, Texture2D t, Color tint) { var o = GUI.color; GUI.color = tint; GUI.DrawTexture(r, t, ScaleMode.StretchToFill, true); GUI.color = o; }
        public static HashSet<string> Recording;   // 测试：小地图这一帧画了哪些图标（贴图名）
        public static float Scale = 1f;   // V23 打磨：大地图期间 = MapUI.BigIconScale
        public static void Draw(Vector2 c, float size, Texture2D t, Color tint, float rotDeg = 0)
        {
            if (Recording != null && t != null) Recording.Add(t.name);
            size *= Scale;
            if (Mathf.Abs(rotDeg) > .01f) { var m = GUI.matrix; CombatHud.RotP(rotDeg, c); Draw(new Rect(c.x - size / 2, c.y - size / 2, size, size), t, tint); GUI.matrix = m; }
            else Draw(new Rect(c.x - size / 2, c.y - size / 2, size, size), t, tint);
        }
    }

    // ================================================================ 画小地图 / 大地图（GameUI 只负责外框和位置）
    public class MapUI : MonoBehaviour
    {
        public static MapUI I;
        public static float MiniRadius = 40f;        // 小地图半径（35–50 m）
        public static float MiniGearRange = 40f;     // 小地图只画 40 m 内的机关
        public static float LastSeenFade = 4f;        // 敌人最后位置淡出（3–5 s）
        public static int LabelMax = 12;
        public static bool LayerGear = true, LayerFood = true, LayerLink = true, LayerMore = false;   // 大地图图层开关
        public static readonly List<Rect> LastLabelRects = new List<Rect>();   // 测试：大地图标签矩形
        public static readonly List<string> LastLabelTexts = new List<string>();
        public static readonly List<string> LastMiniTexts = new List<string>();   // 测试：小地图上出现过的文字
        public static readonly Dictionary<object, Vector2> LastIconPos = new Dictionary<object, Vector2>();   // 测试：图标像素位置（最近一次大地图）
        public static MapView LastMini, LastBig;
        public static readonly HashSet<string> MiniKinds = new HashSet<string>(); public static int MiniFrames, BigFrames;
        public static float LastMiniRot;
        static GUIStyle lbl, lblBig, tiny; static Font font;

        class Seen { public Vector3 pos; public float at; }
        readonly Dictionary<Fighter, Seen> lastSeen = new Dictionary<Fighter, Seen>();
        readonly Dictionary<Object, int> state = new Dictionary<Object, int>();
        float nextPoll; bool dirtyAll; int builtRound = -1;

        public static MapUI Ensure()
        {
            if (I != null) return I;
            var go = GameObject.Find("地图UI") ?? new GameObject("地图UI");
            I = go.GetComponent<MapUI>() ?? go.AddComponent<MapUI>();
            return I;
        }
        void Awake() { I = this; }
        void OnDestroy() { if (I == this) { I = null; MapBase.Dispose(); } }
        public static void MarkDirty() { if (I != null) I.dirtyAll = true; }

        void Update()
        {
            var m = Match.I; if (m == null || Arena.I == null || !Arena.I.navReady) return;
            if (!MapBase.Built) { if (!MapBase.Building) MapBase.BuildBegin(); if (MapBase.BuildStep(6f)) SnapshotState(); return; }
            if (dirtyAll) { dirtyAll = false; PollChanges(true); }
            if (Time.time >= nextPoll) { nextPoll = Time.time + .5f; PollChanges(false); }
            var p = m.player;
            if (p != null)
                foreach (var f in m.fighters)
                {
                    if (f.team == p.team || f == p) continue;
                    if (f.alive && f.Revealed) { if (!lastSeen.TryGetValue(f, out var s)) lastSeen[f] = s = new Seen(); s.pos = f.transform.position; s.at = Time.time; }
                    if (!f.alive) lastSeen.Remove(f);
                }
        }
        // 门 / 蒸笼门 / 蛋糕墙 / 木箱状态变了 → 局部刷新底图
        int StateOf(Object o)
        {
            switch (o)
            {
                case Shutter s: return s.open ? 1 : 0;
                case Door d: return d.open ? 1 : 0;
                case BreakWall w: return w.Broken;
                case Destructible ds: return ds.Alive ? 1 : 0;
            }
            return 0;
        }
        IEnumerable<(Object, Vector3, float)> Tracked()
        {
            var A = Arena.I;
            foreach (var s in A.shutters) if (s != null) yield return (s, s.transform.position, 4f);
            foreach (var d in A.doors) if (d != null) yield return (d, d.transform.position, 4f);
            foreach (var w in A.breakWalls) if (w != null) yield return (w, w.transform.position, w.Width / 2 + 2f);
            foreach (var ds in A.destructibles) if (ds != null) yield return (ds, ds.transform.position, 3f);
        }
        void SnapshotState() { state.Clear(); foreach (var t in Tracked()) state[t.Item1] = StateOf(t.Item1); }
        void PollChanges(bool all)
        {
            foreach (var t in Tracked())
            {
                int now = StateOf(t.Item1);
                if (all || !state.TryGetValue(t.Item1, out int was) || was != now)
                {
                    state[t.Item1] = now;
                    MapBase.Refresh(new Rect(t.Item2.x - t.Item3, t.Item2.z - t.Item3, t.Item3 * 2, t.Item3 * 2));
                }
            }
        }

        // ---------------------------------------------------------------- 公共：区域显示名
        public static string ZoneDisplayName(int zi)
        {
            if (zi < 0 || zi >= Arena.Zones.Length) return "";
            var z = Arena.Zones[zi];
            return ToonAssets.Available && !string.IsNullOrEmpty(z.foodName) ? z.foodName : z.name;
        }
        // 提示语里出现的旧区域名也换成美食名（"主街 ↔ 南工厂通道" → "主街 ↔ 南烧烤长街"）
        public static string ZoneDisplayHint(int zi)
        {
            if (zi < 0 || zi >= Arena.Zones.Length) return "";
            string h = Arena.Zones[zi].hint ?? "";
            if (!ToonAssets.Available) return h;
            foreach (var z in Arena.Zones) if (!string.IsNullOrEmpty(z.foodName) && z.foodName != z.name && h.Contains(z.name)) h = h.Replace(z.name, z.foodName);
            return h.Replace("进攻方出生点", "进攻方出餐起点").Replace("防守方出生点", "防守方出餐起点").Replace("攻方出生", "西门备餐区").Replace("守方", "东门");
        }
        public static string MapTitle { get { return ToonAssets.Available ? "霓城 · 旧港夜市美食街" : "霓城 · 旧港夜市街区"; } }

        static void Styles()
        {
            if (lbl != null) return;
            font = Resources.Load<Font>("NotoSansSC");
            lbl = new GUIStyle(GUI.skin.label) { font = font, fontSize = 12, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            lblBig = new GUIStyle(GUI.skin.label) { font = font, fontSize = 17, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            tiny = new GUIStyle(GUI.skin.label) { font = font, fontSize = 11, alignment = TextAnchor.MiddleCenter };
        }
        static void Txt(Rect r, string s, GUIStyle st, Color c, bool shadow = true)
        {
            s = ToonAssets.FoodText(s);
            var o = st.normal.textColor;
            if (shadow) { st.normal.textColor = new Color(0, 0, 0, .7f * c.a); GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), s, st); }
            st.normal.textColor = c; GUI.Label(r, s, st); st.normal.textColor = o;
        }
        static Color Team(Fighter me, int team) { return team == me.team ? Config.Blue : Match.FFA ? Config.TeamColor(team) : Config.Red; }

        // ================================================================ 小地图：圆形、以自己为中心、跟随旋转
        public static void DrawMinimap(Rect panel, Fighter p)
        {
            Styles();
            var m = Match.I; if (m == null || p == null) return;
            Vector2 c = panel.center; float R = Mathf.Min(panel.width, panel.height) / 2 - 10;
            bool rotate = GameSettings.MinimapRotate;
            var v = new MapView { center = c, worldCenter = p.transform.position, pxPerM = R / MiniRadius, rotDeg = rotate ? -p.yaw : 0, clipRadius = R };
            LastMini = v; LastMiniRot = v.rotDeg; LastMiniTexts.Clear(); MiniKinds.Clear(); MapIcons.Recording = MiniKinds; MiniFrames++;
            bool food = ToonAssets.Available;
            // 奶油色描边 + 糖粒
            MapGL.Disc(c, R + 7, food ? new Color(1f, .93f, .86f, .95f) : new Color(.08f, .1f, .14f, .9f));
            if (food) for (int i = 0; i < 12; i++) { var q = CombatHud.ArcPoint(c, R + 3.5f, i * 30 + 15); MapGL.Disc(q, 1.6f, new Color(1f, .6f, .8f, .9f), 8); }
            if (MapBase.Built && MapBase.TexMini != null)
            {
                var tex = MapBase.TexMini; var vv = v;
                MapGL.TexDisc(c, R, tex, g => MapBase.UV(vv.Unmap(g)), Color.white);
            }
            else MapGL.Disc(c, R, new Color(.2f, .2f, .25f));
            // L5 动态区域
            DynamicAreas(v, p, false);
            // L1 结构：门 / 蒸笼门 / 蛋糕墙（只画范围内）
            Structures(v, p, false);
            // L2 目标：包点 / 出生 / 大锅
            Objectives(v, p, false);
            // L4 机关：40 m 内、能用的
            if (food) Gear(v, p, false);
            // L6 食材候选 + 联动星
            IngredientLayer(v, p, false);
            LinkHud.MapDraw(w => { var a = v.Map(w); return v.Inside(a, 6) ? a : new Vector2(-9999, -9999); }, false);
            // L7 / L8 队伍 + 敌情
            Fighters(v, p, false);
            // 自己：白箭头 + 视野扇形
            float fov = Camera.main != null ? Camera.main.fieldOfView * Camera.main.aspect * .62f : 75f;
            float heading = rotate ? 0 : p.yaw;
            MapGL.Wedge(c, R * .45f, heading - fov / 2, heading + fov / 2, new Color(1, 1, 1, .16f));
            MapIcons.Draw(c, 18, MapIcons.Arrow, Color.white, heading);
            // 北向标记
            Vector2 n = v.Map(p.transform.position + Vector3.forward * MiniRadius * 1.08f);
            n = c + (n - c).normalized * (R + 7);
            Txt(new Rect(n.x - 10, n.y - 9, 20, 18), "N", tiny, food ? new Color(.5f, .25f, .4f) : Color.white, false);
            MapIcons.Recording = null;
        }

        // ================================================================ 大地图 M：全图 + 区域名 + 图层开关
        public const float BigIconScale = 1.4f;   // V23 打磨：大地图图标 ×1.4（小地图不变）
        public static void DrawBigMap(Rect r, Fighter p)
        {
            MapIcons.Scale = BigIconScale;
            try { DrawBigMapInner(r, p); } finally { MapIcons.Scale = 1f; }
        }
        static void DrawBigMapInner(Rect r, Fighter p)
        {
            Styles();
            var m = Match.I; if (m == null || p == null) return;
            bool food = ToonAssets.Available;
            float pxPerM = Mathf.Min(r.width / MapBase.SizeX, r.height / MapBase.SizeZ);
            var v = new MapView { center = r.center, worldCenter = Vector3.zero, pxPerM = pxPerM, rotDeg = 0, clipRadius = 0 };
            LastBig = v; LastLabelRects.Clear(); LastLabelTexts.Clear(); LastIconPos.Clear(); BigFrames++;
            float w = MapBase.SizeX * pxPerM, h = MapBase.SizeZ * pxPerM;
            var mr = new Rect(r.center.x - w / 2, r.center.y - h / 2, w, h);
            GUI.DrawTexture(new Rect(mr.x - 6, mr.y - 6, mr.width + 12, mr.height + 12), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, food ? new Color(1f, .93f, .86f) : new Color(.1f, .12f, .16f), 0, 14);
            if (MapBase.Built && MapBase.Tex != null) GUI.DrawTexture(mr, MapBase.Tex);
            DynamicAreas(v, p, true);
            Structures(v, p, true);
            Platforms(v);
            Objectives(v, p, true);
            if (LayerGear && food) Gear(v, p, true);
            if (LayerMore) More(v, p);
            if (LayerFood) IngredientLayer(v, p, true);
            if (LayerLink) LinkHud.MapDraw(wp => v.Map(wp), true);
            Labels(v);
            Fighters(v, p, true);
            // 自己
            Vector2 me = v.Map(p.transform.position);
            MapGL.Wedge(me, 22, p.yaw - 35, p.yaw + 35, new Color(1, 1, 1, .18f));
            MapIcons.Draw(me, 20, MapIcons.Arrow, Color.white, p.yaw);
        }

        // 底部图层按钮：机关 / 食材 / 联动 / 更多
        public static void LayerButtons(Rect r, System.Func<Rect, string, bool, bool> button)
        {
            float bw = (r.width - 12) / 4;
            if (button(new Rect(r.x, r.y, bw, r.height), "机关", LayerGear)) LayerGear = !LayerGear;
            if (button(new Rect(r.x + bw + 4, r.y, bw, r.height), "食材", LayerFood)) LayerFood = !LayerFood;
            if (button(new Rect(r.x + (bw + 4) * 2, r.y, bw, r.height), "联动", LayerLink)) LayerLink = !LayerLink;
            if (button(new Rect(r.x + (bw + 4) * 3, r.y, bw, r.height), "更多", LayerMore)) LayerMore = !LayerMore;
        }

        // 图例（美食词）
        public static (Texture2D, Color, string)[] LegendRows(Fighter p)
        {
            bool food = ToonAssets.Available;
            var l = new List<(Texture2D, Color, string)>
            {
                (MapIcons.Cake(false), Color.white, food ? "A / B 包点（生日蛋糕）" : "A / B 包点"),
                (MapIcons.Plate, Config.Blue, "出生点 · 蓝 = 我方 / 红 = 敌方"),
                (MapIcons.Steamer(true), Color.white, food ? "蒸笼门（开 / 盖上）" : "关口大门（开 / 关）"),
                (MapIcons.Door(true), Color.white, "卷帘门 / 侧门"),
                (MapIcons.Slice(false), Color.white, food ? "蛋糕墙（完整 / 有洞）" : "可破坏墙"),
                (MapIcons.Cookie(false), Color.white, "墙洞（木箱在 / 已打开）"),
            };
            if (food)
            {
                l.Add((MapIcons.Pot(new Color(1f, .45f, .3f)), Color.white, "火锅 / 关东煮（冒烟）"));
                l.Add((MapIcons.Teapot, Color.white, "蒸汽阀（就绪 / 喷气 / 冷却环）"));
                l.Add((MapIcons.Jelly, Color.white, "果冻弹床"));
                l.Add((MapIcons.Station(KitchenStation.Kind.MilkTea), Color.white, "奶茶机 · 烤箱 · 冰柜 · 回血炉"));
                l.Add((MapIcons.Bag, Color.white, "外卖落点"));
                l.Add((MapIcons.Ingredient("Egg"), Color.white, "食材 · 大圈 = 能和手上凑成菜"));
                l.Add((MapIcons.Star, Links.Gold, "我的技能能联动的点"));
            }
            l.Add((MapIcons.Arrow, Config.Blue, "队友（带视野扇形）"));
            l.Add((MapIcons.Cross, new Color(1f, .4f, .4f), "敌人最后出现的位置（渐隐）"));
            return l.ToArray();
        }

        // ---------------------------------------------------------------- 各层
        static void DynamicAreas(MapView v, Fighter p, bool big)
        {
            float k = v.pxPerM;
            void Area(Vector3 c, float r, Color col, float left, float total)
            {
                Vector2 a = v.Map(c); if (!v.Inside(a, -r * k)) return;
                MapGL.Disc(a, r * k, col);
                if (total > 0) MapGL.Ring(a, r * k, 2, new Color(col.r, col.g, col.b, .9f), 0, 360 * Mathf.Clamp01(left / total));
            }
            foreach (var z in LinkPlus.MapAreas()) Area(z.c, z.r, z.col, z.left, z.total);   // V23 d：整片蒸汽 / 棉花糖蒸汽 / 蛋糕粉尘 / 芥末呛雾（含剩余时间环，双方都画）
            foreach (var nf in NoiseField.Live) if (nf != null) Area(nf.transform.position, Config.NoiseRadius, new Color(.9f, .5f, 1f, .35f), nf.EndsAt - Time.time, Config.NoiseLife);
        }
        static void Structures(MapView v, Fighter p, bool big)
        {
            var A = Arena.I; float sz = big ? 22 : 14;
            foreach (var s in A.shutters)
            {
                if (s == null) continue; Vector2 a = v.Map(s.transform.position); if (!v.Inside(a, 4)) continue;
                MapIcons.Draw(a, s.gate ? sz * 1.1f : sz * .8f, s.gate ? MapIcons.Steamer(s.open) : MapIcons.Door(s.open), Color.white);
                if (big) LastIconPos[s] = a;
            }
            foreach (var d in A.doors) { if (d == null) continue; Vector2 a = v.Map(d.transform.position); if (!v.Inside(a, 4)) continue; MapIcons.Draw(a, sz * .7f, MapIcons.Door(d.open), Color.white); }
            foreach (var w in A.breakWalls)
            {
                if (w == null) continue; Vector2 a = v.Map(w.transform.position); if (!v.Inside(a, 4)) continue;
                bool holed = w.Broken > 0;
                // 墙本身：一条短线
                Vector3 dir = w.transform.right * w.Width / 2;
                MapGL.Line(v.Map(w.transform.position - dir), v.Map(w.transform.position + dir), big ? 3 : 2, holed ? new Color(1f, .6f, .75f) : new Color(.95f, .45f, .6f));
                MapIcons.Draw(a, sz * .75f, MapIcons.Slice(holed), Color.white);
            }
            foreach (var br in Links.Breaches())
            {
                if (br.wall != null) continue;   // 蛋糕墙的洞上面画过了
                Vector2 a = v.Map(br.center); if (!v.Inside(a, 4)) continue;
                MapIcons.Draw(a, sz * .7f, MapIcons.Cookie(br.crate == null || !br.crate.Alive), Color.white);
            }
        }
        // 高台：奶油滴边底图已画，这里补高度数字（只在大地图）
        static void Platforms(MapView v)
        {
            foreach (var zn in Arena.Zones)
            {
                if (zn.height <= 0) continue;
                Vector2 a = v.Map(new Vector3(zn.area.xMax - 2.5f, 0, zn.area.yMin + 1.8f));
                Txt(new Rect(a.x - 18, a.y - 8, 36, 16), zn.height.ToString("0.#") + " m", tiny, new Color(.5f, .3f, .4f), false);
            }
        }
        static void Objectives(MapView v, Fighter p, bool big)
        {
            var m = Match.I; float sz = big ? 30 : 20;
            if (Match.Mode == GameMode.Bomb)
            {
                for (int s = 0; s < 2; s++)
                {
                    Vector3 sc = Arena.SiteCenter(s); Vector2 a = v.Map(sc);
                    float rr = Config.SiteRadius * v.pxPerM; Color col = s == 0 ? new Color(1f, .55f, .3f) : new Color(.4f, .75f, 1f);
                    if (v.Inside(a, -rr)) { MapGL.Disc(a, rr, new Color(col.r, col.g, col.b, .18f)); MapGL.Ring(a, rr, 1.5f, new Color(col.r, col.g, col.b, .6f)); }
                    bool lit = m.Planted && m.plantedSite == s;
                    Vector2 ic = v.Clamp(a, 12);
                    MapIcons.Draw(ic, sz * (lit && Mathf.Repeat(Time.time * 2, 1) < .5f ? 1.15f : 1f), MapIcons.Cake(lit), Color.white);
                    Txt(new Rect(ic.x - 12, ic.y - sz * MapIcons.Scale * .72f - 8, 24, 16), s == 0 ? "A" : "B", big ? lblBig : lbl, col);
                    if (big) LastIconPos["site" + s] = a;
                }
                // 出生点（餐盘）
                for (int t = 0; t < 2; t++)
                {
                    Vector3 sp = m.arena.SpawnPoint(t == m.AttackingTeam, 1);
                    Vector2 a = v.Map(sp); if (!v.Inside(a, 6)) continue;
                    MapIcons.Draw(a, sz * .7f, MapIcons.Plate, Team(p, t));
                }
                bool att = p.team == m.AttackingTeam;
                if (m.Planted || (att && m.bomb == BombState.Dropped) || m.BombCarrier != null)
                {
                    Vector3 bp = m.BombCarrier != null ? m.BombCarrier.transform.position : m.bombPos;
                    bool show = m.Planted || m.bomb == BombState.Dropped || (m.BombCarrier != null && m.BombCarrier.team == p.team);
                    if (show) { Vector2 a = v.Clamp(v.Map(bp), 8); MapIcons.Draw(a, big ? 18 : 13, MapIcons.Bomb, Mathf.Repeat(Time.time * 3, 1) < .5f && m.Planted ? new Color(1f, .5f, .4f) : Color.white); }
                }
            }
            if (Match.Mode == GameMode.Cook)
                for (int t = 0; t < 2; t++)
                {
                    var pot = CookPot.Pots[t]; if (pot == null) continue;
                    Vector2 a = v.Clamp(v.Map(pot.transform.position), 12);
                    MapIcons.Draw(a, sz, MapIcons.Pot(Team(p, t)), Color.white);
                }
        }
        static void Gear(MapView v, Fighter p, bool big)
        {
            float sz = big ? 20 : 14; Vector3 me = p.transform.position;
            bool Near(Vector3 w) { return big || Links.Flat(w, me) <= MiniGearRange; }
            foreach (var hz in HotpotHazard.All)
            {
                if (hz == null || !Near(hz.transform.position)) continue; Vector2 a = v.Map(hz.transform.position); if (!v.Inside(a, 4)) continue;
                bool mustard = Links.PotDrone(hz) != null;
                MapIcons.Draw(a, sz, MapIcons.Pot(mustard ? new Color(.75f, .9f, .25f) : new Color(1f, .45f, .3f)), Color.white);
                if (big) LastIconPos[hz] = a;
            }
            foreach (var sv in SteamValve.Valves)
            {
                if (sv == null || !Near(sv.transform.position)) continue; Vector2 a = v.Map(sv.transform.position); if (!v.Inside(a, 4)) continue;
                bool cd = sv.CooldownLeft > 0 && !sv.Active;
                if (!big && cd) continue;   // 小地图只画能用的
                MapIcons.Draw(a, sz, MapIcons.Teapot, cd ? new Color(.6f, .6f, .6f) : Color.white);
                if (sv.Active) MapGL.Ring(a, sz * .62f, 2, new Color(1, 1, 1, .9f));
                else if (cd) { MapGL.Ring(a, sz * .62f, 2, new Color(1f, .8f, .4f, .9f), 0, 360 * (1 - sv.CooldownLeft / SteamValve.Cooldown)); if (big) Txt(new Rect(a.x - 16, a.y + sz * .5f, 32, 14), Mathf.CeilToInt(sv.CooldownLeft) + "s", tiny, new Color(.5f, .3f, .4f), false); }
                if (big) LastIconPos[sv] = a;
            }
            foreach (var jp in JumpPad.All) { if (jp == null || !Near(jp.transform.position)) continue; Vector2 a = v.Map(jp.transform.position); if (!v.Inside(a, 4)) continue; MapIcons.Draw(a, sz * .85f, MapIcons.Jelly, Color.white); }
            foreach (var it in Interactable.All)
            {
                if (!(it is KitchenStation k) || !Near(k.transform.position)) continue;
                Vector2 a = v.Map(k.transform.position); if (!v.Inside(a, 4)) continue;
                float cd = k.CooldownLeft(p); bool grey = cd > 0;
                if (!big && grey) continue;
                MapIcons.Draw(a, sz * .9f, MapIcons.Station(k.kind), grey ? new Color(.6f, .6f, .6f) : Color.white);
                if (big && grey) Txt(new Rect(a.x - 16, a.y + sz * .5f, 32, 14), Mathf.CeilToInt(cd) + "s", tiny, new Color(.5f, .3f, .4f), false);
                if (big) LastIconPos[k] = a;
            }
            foreach (var dc in DeliveryCrate.Live)
            {
                if (dc == null) continue; Vector3 w = dc.Landed ? dc.transform.position : dc.Target; Vector2 a = v.Clamp(v.Map(w), 8);
                if (!dc.Landed) MapGL.Ring(a, sz * .7f, 1.5f, new Color(1f, .6f, .85f, .9f));
                MapIcons.Draw(a, sz * .9f, MapIcons.Bag, Color.white);
            }
            foreach (var mp in MentosPile.All) { if (mp == null || !Near(mp.transform.position)) continue; Vector2 a = v.Map(mp.transform.position); if (!v.Inside(a, 4)) continue; MapIcons.Draw(a, sz * .7f, MapIcons.Mentos, Color.white); }
        }
        // "更多"图层：监控 / 电闸 / 爆炸桶 / 玻璃 / 木箱（6–8 px，大地图默认关）
        static void More(MapView v, Fighter p)
        {
            var A = Arena.I;
            foreach (var c in A.cams) if (c != null) MapGL.Disc(v.Map(c.transform.position), 3.5f, c.Working ? new Color(1f, .3f, .55f) : new Color(.4f, .4f, .4f));
            foreach (var pb in A.powerBoxes) if (pb != null) MapGL.Disc(v.Map(pb.transform.position), 3.5f, pb.Dark ? new Color(.4f, .4f, .4f) : new Color(1f, .9f, .3f));
            foreach (var b in A.barrels) if (b != null && b.GetComponent<Collider>().enabled) MapGL.Disc(v.Map(b.transform.position), 3f, new Color(1f, .4f, .3f));
            foreach (var g in A.glass) if (g != null && g.GetComponent<Collider>().enabled) MapGL.Disc(v.Map(g.transform.position), 3f, new Color(.6f, .85f, 1f));
            foreach (var d in A.destructibles) if (d != null && d.Alive) MapGL.Disc(v.Map(d.transform.position), 3f, new Color(.9f, .7f, .3f));
        }
        // L6：食材（大地图全部淡色；候选按 IngredientGuide 高亮）
        static void IngredientLayer(MapView v, Fighter p, bool big)
        {
            if (!ToonAssets.Available) return;
            var g = IngredientGuide.I;
            bool guideOn = GameSettings.IngredientGuide < 2 && g != null;
            foreach (var ip in IngredientPickup.Live)
            {
                if (ip == null) continue;
                bool primary = guideOn && g.Primary == ip, alt = guideOn && g.Alternates.Contains(ip);
                if (!big && !primary && !alt) continue;   // 小地图只画候选；大地图淡色画全部
                Vector2 a = v.Map(ip.transform.position);
                if (!v.Inside(a, 8)) { if (!primary && !alt) continue; a = v.Clamp(a, 9); }   // 候选超出小地图：贴边
                float sz = primary ? (big ? 24 : 17) : alt ? (big ? 18 : 12) : 13;
                float alpha = primary || alt ? Mathf.Max(.45f, g.Alpha) : .55f;
                if (primary) { float pulse = 1f + .18f * Mathf.Sin(Time.time * 5f); MapGL.Ring(a, sz * .62f * pulse, 2f, new Color(1, 1, 1, alpha)); }
                if (guideOn && g.BumpAt(ip) > 0) sz *= 1f + .35f * g.BumpAt(ip);
                MapIcons.Draw(a, sz, MapIcons.Ingredient(ip.id), new Color(1, 1, 1, alpha));
                if (big) LastIconPos[ip] = a;
                if (!big) LastMiniTexts.Add(primary ? "primary:" + ip.id : "alt:" + ip.id);
            }
        }
        static void Fighters(MapView v, Fighter p, bool big)
        {
            var m = Match.I; float sz = big ? 18 : 14;
            foreach (var f in m.fighters)
            {
                if (f == p) continue;
                bool mate = f.team == p.team && !Match.FFA;
                if (f.alive && mate)
                {
                    Vector2 a = v.Clamp(v.Map(f.transform.position), 8);
                    float hd = f.yaw + v.rotDeg;
                    MapGL.Wedge(a, big ? 20 : 16, hd - 32, hd + 32, new Color(Config.Blue.r, Config.Blue.g, Config.Blue.b, .18f));
                    MapIcons.Draw(a, sz, MapIcons.Arrow, Config.Blue, hd);
                    if (m.BombCarrier == f) MapIcons.Draw(a + new Vector2(sz * .5f, -sz * .5f), 10, MapIcons.Bomb, Color.white);
                    continue;
                }
                if (f.alive && f.Revealed)
                {
                    Vector2 a = v.Map(f.transform.position); if (!v.Inside(a, 6)) continue;
                    Color ec = Match.FFA ? Config.TeamColor(f.team) : Config.Red;
                    MapGL.Disc(a, big ? 7 : 5.5f, ec); MapGL.Ring(a, big ? 9 : 7, 1.5f, new Color(1, 1, 1, .8f));
                    continue;
                }
                if (I != null && I.lastSeen.TryGetValue(f, out var s) && Time.time - s.at < LastSeenFade && !(f.alive && f.Revealed))
                {
                    float a2 = 1f - (Time.time - s.at) / LastSeenFade;
                    Vector2 a = v.Map(s.pos); if (!v.Inside(a, 6)) continue;
                    MapIcons.Draw(a, big ? 14 : 11, MapIcons.Cross, new Color(1f, .35f, .35f, .85f * a2));
                }
            }
        }
        // L3 区域名（大地图）：按优先级避让，最多 LabelMax 个。布局是纯函数（测试直接调，batchmode 没有 OnGUI 也能验"不压字"）
        public static int LabelFont(int pri) { return pri == 1 ? 18 : pri == 2 ? 15 : 13; }
        public static void LayoutLabels(MapView v, List<Rect> rects, List<string> texts, List<int> zones)
        {
            rects.Clear(); texts.Clear(); zones.Clear();
            var order = new List<int>();
            for (int i = 0; i < Arena.Zones.Length; i++) if (Arena.Zones[i].mapPriority > 0) order.Add(i);
            order.Sort((a, b) =>
            {
                int c = Arena.Zones[a].mapPriority.CompareTo(Arena.Zones[b].mapPriority); if (c != 0) return c;
                c = (Arena.Zones[b].area.width * Arena.Zones[b].area.height).CompareTo(Arena.Zones[a].area.width * Arena.Zones[a].area.height); if (c != 0) return c;
                return a.CompareTo(b);
            });
            foreach (int zi in order)
            {
                if (rects.Count >= LabelMax) break;
                var zn = Arena.Zones[zi]; string nm = ZoneDisplayName(zi);
                int fs = LabelFont(zn.mapPriority);
                float tw = nm.Length * fs * 1.02f + 20, th = fs + 10;
                Vector2 c = v.Map(new Vector3(zn.area.center.x, 0, zn.area.center.y));
                if (zn.labelOffset != Vector2.zero) c += new Vector2(zn.labelOffset.x, -zn.labelOffset.y) * v.pxPerM;   // 偏移是世界米（x, z）
                var rr = new Rect(c.x - tw / 2, c.y - th / 2, tw, th);
                bool clash = false; foreach (var o in rects) if (o.Overlaps(rr)) { clash = true; break; }
                if (clash) continue;
                rects.Add(rr); texts.Add(nm); zones.Add(zi);
            }
        }
        static readonly List<int> labelZones = new List<int>();
        static void Labels(MapView v)
        {
            bool food = ToonAssets.Available;
            LayoutLabels(v, LastLabelRects, LastLabelTexts, labelZones);
            for (int i = 0; i < LastLabelRects.Count; i++)
            {
                var rr = LastLabelRects[i]; var zn = Arena.Zones[labelZones[i]]; float th = rr.height;
                GUI.DrawTexture(rr, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, food ? new Color(1f, .97f, .92f, .85f) : new Color(0, 0, 0, .62f), 0, th / 2);
                var st = new GUIStyle(lblBig) { fontSize = LabelFont(zn.mapPriority) };
                Txt(rr, LastLabelTexts[i], st, food ? Color.Lerp(zn.color, new Color(.35f, .15f, .3f), .55f) : Color.Lerp(zn.color, Color.white, .45f), false);
            }
        }

        // 被糊住：粉色棉花糖慢慢流动（代替电视雪花）
        public static void Jammed(Rect panel, string text)
        {
            Styles();
            Vector2 c = panel.center; float R = Mathf.Min(panel.width, panel.height) / 2 - 10;
            MapGL.Disc(c, R + 7, new Color(1f, .93f, .86f, .95f));
            MapGL.Disc(c, R, new Color(1f, .78f, .9f));
            for (int i = 0; i < 7; i++)
            {
                float t = Time.time * .35f + i * 1.7f;
                Vector2 q = c + new Vector2(Mathf.Sin(t) * R * .55f, Mathf.Cos(t * 1.3f + i) * R * .55f);
                MapGL.Disc(q, R * (.28f + .08f * Mathf.Sin(t * 2)), new Color(1f, .9f, .97f, .55f));
            }
            Txt(new Rect(c.x - R, c.y - 12, R * 2, 24), text, lbl, new Color(.5f, .2f, .4f), false);
        }
    }
}
