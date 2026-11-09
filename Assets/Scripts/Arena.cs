using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace NeonSiege
{
    // 「霓城 · 旧港夜市街区」爆破图（约 144×100 m）。
    // 攻方出生在西侧停车场，守方出生在东侧；A 点＝东北货运广场，B 点＝东南工厂车间。
    // 路线：北长街→A、夜市主街→中庭→A/B、A/B 连廊（卷帘门）、南工厂通道→B。
    public class Arena : MonoBehaviour
    {
        public static Arena I;
        public const float HalfX = 72f, HalfZ = 50f;
        public static readonly Vector3 SiteA = new Vector3(37, 0, 34), SiteB = new Vector3(37, 0, -34);
        public readonly List<Rect> minimapBlocks = new List<Rect>();
        public readonly List<Vector3> noBuild = new List<Vector3>();
        public readonly List<Destructible> destructibles = new List<Destructible>();
        public readonly List<Door> doors = new List<Door>();
        public readonly List<ExplosiveBarrel> barrels = new List<ExplosiveBarrel>();
        public readonly List<GlassPanel> glass = new List<GlassPanel>();
        public readonly List<SecurityCam> cams = new List<SecurityCam>();
        public readonly List<PowerBox> powerBoxes = new List<PowerBox>();
        public readonly List<Shutter> shutters = new List<Shutter>();
        public readonly Dictionary<string, Vector3[]> routes = new Dictionary<string, Vector3[]>();
        public readonly Dictionary<int, Vector3[]> anchors = new Dictionary<int, Vector3[]>();
        public bool navReady;

        // 区域标注（地图更明显：小地图/大地图/进入提示/地面字）
        public struct Zone { public string name, hint; public Rect area; public Color color; }
        public static readonly Zone[] Zones =
        {
            new Zone { name = "A 点 · 货运广场", hint = "包点 A", area = Rect.MinMaxRect(26, 18, 48, 50), color = new Color(1f, .5f, .2f) },
            new Zone { name = "B 点 · 工厂车间", hint = "包点 B", area = Rect.MinMaxRect(26, -50, 48, -18), color = new Color(.3f, .8f, 1f) },
            new Zone { name = "攻方集结区", hint = "进攻方出生点", area = Rect.MinMaxRect(-72, -10, -56, 10), color = new Color(1f, .45f, .2f) },
            new Zone { name = "守方安保站", hint = "防守方出生点", area = Rect.MinMaxRect(56, -10, 72, 10), color = new Color(.3f, .7f, 1f) },
            new Zone { name = "北小巷", hint = "主街 ↔ 北长街", area = Rect.MinMaxRect(-24, 10, -18, 30), color = new Color(1f, .3f, .7f) },
            new Zone { name = "南小巷", hint = "主街 ↔ 南工厂通道 · 卷帘门", area = Rect.MinMaxRect(-36, -30, -30, -10), color = new Color(.7f, .4f, 1f) },
            new Zone { name = "北铁门检查站", hint = "北长街唯一通路 · 长按 G 开铁门（巨响）", area = Rect.MinMaxRect(0, 30, 9, 50), color = new Color(1f, .35f, .2f) },
            new Zone { name = "南铁门检查站", hint = "南工厂通道唯一通路 · 长按 G 开铁门（巨响）", area = Rect.MinMaxRect(0, -50, 9, -30), color = new Color(1f, .35f, .2f) },
            new Zone { name = "市场楼顶", hint = "高台狙击位 · 只能从东侧弹射板上来", area = Rect.MinMaxRect(30, -9, 44, 9), color = new Color(.95f, .35f, .35f) },
            new Zone { name = "夜市主街", hint = "中路 · 通往中庭", area = Rect.MinMaxRect(-56, -10, 14, 10), color = new Color(1f, .25f, .7f) },
            new Zone { name = "北长街", hint = "通往 A 点", area = Rect.MinMaxRect(-72, 30, 26, 50), color = new Color(1f, .6f, .2f) },
            new Zone { name = "南工厂通道", hint = "通往 B 点", area = Rect.MinMaxRect(-72, -50, 26, -30), color = new Color(.6f, .35f, 1f) },
            new Zone { name = "西侧通道", hint = "攻方出生 ↔ 北长街", area = Rect.MinMaxRect(-72, 10, -56, 30), color = new Color(1f, .7f, .4f) },
            new Zone { name = "西侧通道", hint = "攻方出生 ↔ 南工厂通道", area = Rect.MinMaxRect(-72, -30, -56, -10), color = new Color(1f, .7f, .4f) },
            new Zone { name = "A 连廊", hint = "中庭 ↔ A 点 · 卷帘门", area = Rect.MinMaxRect(14, 10, 26, 24), color = new Color(1f, .55f, .2f) },
            new Zone { name = "B 连廊", hint = "中庭 ↔ B 点 · 卷帘门", area = Rect.MinMaxRect(14, -24, 26, -10), color = new Color(.3f, .8f, 1f) },
            new Zone { name = "中庭西口", hint = "分路 A / B", area = Rect.MinMaxRect(14, -10, 30, 10), color = new Color(.3f, 1f, .9f) },
            new Zone { name = "中央市场", hint = "防守方转点枢纽", area = Rect.MinMaxRect(26, -18, 56, 18), color = new Color(.3f, 1f, .9f) },
            new Zone { name = "东侧通道", hint = "守方 ↔ 包点", area = Rect.MinMaxRect(48, -50, 72, 50), color = new Color(.4f, .7f, 1f) },
        };
        public static int ZoneAt(Vector3 p)
        {
            for (int i = 0; i < Zones.Length; i++) if (Zones[i].area.Contains(new Vector2(p.x, p.z))) return i;
            return -1;
        }

        Material asphalt, brick, concrete, panels, iron, plate, sheet, pavement, cyan, magenta, amber, white, holo, puddle, darkGlass, plaster;
        Material[] skyline;
        readonly List<GameObject> spawnBarriers = new List<GameObject>();
        PowerBox zoneA, zoneB;

        void Awake() { I = this; }

        // Destroy that also works when the map is generated in the editor for light baking.
        public static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }

        static Material Load(string name, Color fallback)
        {
            var m = Resources.Load<Material>("Materials/" + name);
            if (m != null) return m;
            var made = new Material(Shader.Find("Standard")); made.color = fallback; return made;
        }

        // World-space text: the built-in font material ignores depth, so signs showed through walls and roofs.
        static Material worldText;
        public static Material WorldText(Font font)
        {
            if (worldText == null)
            {
                var src = Resources.Load<Material>("Materials/WorldText");
                worldText = src != null ? new Material(src) : new Material(font.material);
                worldText.mainTexture = font.material.mainTexture;
            }
            return worldText;
        }

        static readonly Dictionary<Color, Material> glowCache = new Dictionary<Color, Material>();
        public static Material Glow(Color c, float intensity)
        {
            intensity *= .5f;   // V6：霓虹只做点缀，不再主导画面
            Color key = c * intensity; key.a = 1;
            if (glowCache.TryGetValue(key, out var cached) && cached != null) return cached;
            var m = new Material(Resources.Load<Material>("Materials/Neon") ?? new Material(Shader.Find("Standard")));
            m.color = c * .3f;
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * intensity);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            glowCache[key] = m;
            return m;
        }

        public void Build()
        {
            I = this; SauceZone.Clear(); SteamCloud.Clear(); sauceIndex = 0;
            asphalt = Load("Asphalt", new Color(.2f, .2f, .22f));
            brick = Load("Brick", new Color(.5f, .3f, .25f));
            concrete = Load("Concrete", new Color(.45f, .45f, .47f));
            panels = Load("ConcretePanels", new Color(.45f, .45f, .47f));
            iron = Load("Corrugated", new Color(.35f, .38f, .42f));
            plate = Load("MetalPlate", new Color(.4f, .42f, .45f));
            sheet = Load("MetalSheet", new Color(.4f, .42f, .45f));
            pavement = Load("Pavement", new Color(.5f, .5f, .5f));
            puddle = Load("Puddle", Color.black);
            skyline = new[] { Load("SkylineA", Color.gray), Load("SkylineB", Color.gray), Load("SkylineC", Color.gray), Load("SkylineD", Color.gray) };
            plaster = Load("Plaster", new Color(.6f, .58f, .55f));
            darkGlass = new Material(Shader.Find("Standard")); darkGlass.color = new Color(.07f, .08f, .09f); darkGlass.SetFloat("_Glossiness", .45f);
            holo = Resources.Load<Material>("Materials/Holo");
            cyan = Glow(new Color(.1f, .95f, 1f), 2.4f); magenta = Glow(new Color(1f, .15f, .8f), 2.6f);
            amber = Glow(new Color(1f, .6f, .15f), 2.4f); white = Glow(new Color(.85f, .9f, 1f), 2f);

            Lighting();
            var ground = Box("地面 · 沥青路", new Vector3(0, -.25f, 0), new Vector3(HalfX * 2 + 60, .5f, HalfZ * 2 + 60), asphalt, false);
            ground.GetComponent<Renderer>().receiveShadows = true;
            Buildings();
            Spawns();
            MidStreet();
            NorthStreet();
            SouthStreet();
            Plaza();
            SiteAArea();
            SiteBArea();
            Skyline();
            Alleys();
            CyberDressing();
            ZoneMarkings();
            TacticalLayout();
            MilitaryDressing();
            RoadDecals();
            WallGrime();
            FoodCity();
            BuildNavMesh();
            Dynamic();
            DefineRoutes();
            var probe = new GameObject("Reflection probe").AddComponent<ReflectionProbe>();
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
            probe.size = new Vector3(HalfX * 2 + 20, 40, HalfZ * 2 + 20); probe.transform.position = new Vector3(0, 4, 0);
            probe.intensity = .45f; probe.resolution = 128;
            probe.RenderProbe();
        }

        void Lighting()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            // V6：写实黄昏光照（暖色低角度太阳 + 灰蓝天光 + 薄雾），反射整体压低
            RenderSettings.fogColor = new Color(.72f, .82f, .96f);   // V14：晴天薄雾
            RenderSettings.fogDensity = .003f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;   // 可控的天光补光，阴影不发黑
            RenderSettings.ambientSkyColor = new Color(1.05f, 1.15f, 1.35f);
            RenderSettings.ambientEquatorColor = new Color(1f, .97f, .92f);
            RenderSettings.ambientGroundColor = new Color(.62f, .6f, .56f);
            RenderSettings.reflectionIntensity = .3f;
            DynamicGI.UpdateEnvironment();
            var moon = new GameObject("夕阳").AddComponent<Light>();
            moon.type = LightType.Directional; moon.color = new Color(1f, .96f, .88f); moon.intensity = 1.35f;
            moon.shadows = LightShadows.Soft; moon.shadowStrength = .55f;
            moon.transform.rotation = Quaternion.Euler(52, -40, 0);
            RenderSettings.sun = moon;
            QualitySettings.shadowDistance = 80; QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.High; QualitySettings.pixelLightCount = 8;
        }

        // ------------------------------------------------------------------ helpers
        public GameObject Box(string name, Vector3 p, Vector3 size, Material mat, bool block = true, float rotY = 0, bool collider = true)
        {
            var o = GameObject.CreatePrimitive(PrimitiveType.Cube);
            o.name = name; o.transform.position = p; o.transform.rotation = Quaternion.Euler(0, rotY, 0);
            o.transform.localScale = size;
            o.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider) Kill(o.GetComponent<Collider>());
            if (block && collider && size.y > 1.1f) AddBlock(p, size, rotY);
            o.transform.SetParent(transform, true);
            if (ToonAssets.Available && name == "人行道" && mat == pavement) CandyCurbs(p, size);
            return o;
        }

        GameObject Glowbox(string name, Vector3 p, Vector3 size, Material m, float rotY = 0)
        {
            var g = Box(name, p, size, m, false, rotY, false);
            g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g;
        }

        void AddBlock(Vector3 c, Vector3 size, float rotY)
        {
            bool swap = Mathf.Abs(Mathf.Sin(rotY * Mathf.Deg2Rad)) > .7f;
            float w = swap ? size.z : size.x, d = swap ? size.x : size.z;
            minimapBlocks.Add(new Rect(c.x - w / 2, c.z - d / 2, w, d));
        }

        public static GameObject Model(string path)
        {
            var prefab = Resources.Load<GameObject>(path.StartsWith("Toon/") ? path : "Models/" + path);   // V14 toon assets live in Resources/Toon
            return prefab != null ? Instantiate(prefab) : null;
        }

        // Instantiate a modular kit and keep only one named piece.
        public static GameObject Piece(string path, string child)
        {
            var all = Model(path);
            if (all == null) return null;
            Transform found = null;
            foreach (var t in all.GetComponentsInChildren<Transform>()) if (t.name == child) { found = t; break; }
            if (found == null) return all;
            found.SetParent(null, true);
            Kill(all);
            found.position = Vector3.zero;
            return found.gameObject;
        }

        public static void Fit(GameObject o, Vector3 size)
        {
            o.transform.position = Vector3.zero;
            var rs = o.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            Vector3 k = new Vector3(size.x > 0 ? size.x / b.size.x : 0, size.y > 0 ? size.y / b.size.y : 0, size.z > 0 ? size.z / b.size.z : 0);
            float uni = Mathf.Max(k.x, Mathf.Max(k.y, k.z));
            if (k.x == 0) k.x = uni; if (k.y == 0) k.y = uni; if (k.z == 0) k.z = uni;
            o.transform.localScale = Vector3.Scale(o.transform.localScale, k);
            b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            o.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z);
        }

        // Realistic Poly Haven prop with a fitted box collider; size components of 0 keep aspect.
        // V14：Poly Haven 写实道具 → Quaternius 卡通道具 / KayKit 城市道具（null = 卡通风里不放）
        static readonly Dictionary<string, string> ToonProps = new Dictionary<string, string> {
            { "wooden_military_crate", "Toon/Env/Crate" }, { "old_military_crate", "Toon/Env/Crate" }, { "ammo_box", "Toon/Env/Crate" }, { "wooden_crate_01", "Toon/Env/Crate" }, { "wooden_crate_02", "Toon/Env/Crate" },
            { "concrete_road_barrier", "Toon/Food/CakeSlice" }, { "concrete_road_barrier_02", "Toon/Food/Sushi" },
            { "industrial_pastic_container", "Toon/Food/Dumpling" }, { "portable_generator", "Toon/Food/Burger" }, { "old_military_compressor", "Toon/Env/GasTank" },
            { "metal_trash_can", "Toon/Food/Ramen" }, { "metal_jerrycan", "Toon/Env/GasCan" }, { "metal_jerrycan_green", "Toon/Env/GasCan" },
            { "utility_box_01", "Toon/City/box_A" }, { "utility_box_02", "Toon/City/box_B" }, { "steel_frame_shelves_01", "Toon/Env/CardboardBoxes_3" },
            { "plastic_crate_01", "Toon/Food/Macarons" }, { "plastic_crate_03", "Toon/Env/CardboardBoxes_1" }, { "cardboard_box_01", "Toon/Food/Macarons" },
            { "covered_car", "Toon/City/car_sedan" }, { "Barrel_01", "Toon/Env/ExplodingBarrel" }, { "barrel_03", "Toon/Food/Skewers" }, { "barrel_stove", "Toon/Env/ExplodingBarrel_Spilled" },
            { "propane_tank", "Toon/Food/Popsicle" }, { "wooden_ladder", null }, { "tool_cart", "Toon/Food/Burger" }, { "portable_welding_cart", "Toon/Food/Dumpling" },
            { "hand_truck", "Toon/Food/Macarons" }, { "street_lamp_01", "Toon/Food/LollipopLamp" }, { "street_lamp_02", "Toon/Food/LollipopLamp" }, { "old_tyre", "Toon/Food/Donut" },
            { "modular_street_seating", "Toon/City/bench" }, { "modular_industrial_pipes_01", "Toon/Env/Pipes" }, { "fire_hydrant", "Toon/City/firehydrant" },
            { "WetFloorSign_01", "Toon/Food/IceCream" },
            { "security_light", null }, { "mounted_fluorescent_lights", null }, { "hanging_industrial_lamp", null }, { "industrial_wall_lamp", null }, { "portable_searchlight", null },
            { "water_manhole_cover", null }, { "rollershutter_window_01", null }, { "modular_fire_escape", null }, { "korean_fire_extinguisher_01", null } };

        GameObject PH(string name, Vector3 p, float rotY, Vector3 size, bool solid = true, bool shadows = true)
        {
            GameObject o;
            string toonPathUsed = null;
            if (ToonAssets.Available && ToonProps.TryGetValue(name, out var toonPath))
            {
                if (toonPath == null) return null;
                o = Model(toonPath); toonPathUsed = toonPath;
            }
            else o = Model("PH/" + name + "/" + name);
            if (o == null) return null;
            Fit(o, size);
            if (ToonAssets.Available && size.x == 0 && size.z == 0)
            {
                // V16：只按高度缩放时，扁平的卡通模型会被放大成几米宽的"木板"，这里限制水平尺寸
                var rr = o.GetComponentsInChildren<Renderer>(); Bounds fb = rr[0].bounds; foreach (var r in rr) fb.Encapsulate(r.bounds);
                float maxH = Mathf.Max(fb.size.x, fb.size.z), limit = Mathf.Max(4.6f, size.y * 3f);
                if (maxH > limit)
                {
                    o.transform.localScale *= limit / maxH; o.transform.position = Vector3.zero;
                    fb = rr[0].bounds; foreach (var r in rr) fb.Encapsulate(r.bounds);
                    o.transform.position -= new Vector3(fb.center.x, fb.min.y, fb.center.z);
                }
            }
            var holder = new GameObject(name);
            o.transform.SetParent(holder.transform, false);
            var rs = o.GetComponentsInChildren<Renderer>();
            Bounds b = rs[0].bounds; foreach (var r in rs) { b.Encapsulate(r.bounds); if (!shadows) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            if (solid) { var c = holder.AddComponent<BoxCollider>(); c.center = b.center; c.size = b.size; }
            holder.transform.position = p; holder.transform.rotation = Quaternion.Euler(0, rotY, 0);
            if (solid && b.size.y > 1.1f) AddBlock(p, b.size, rotY);
            holder.transform.SetParent(transform, true);
            if (ToonAssets.Available && solid && toonPathUsed != null && toonPathUsed.StartsWith("Toon/Food/")) holder.AddComponent<FoodJiggle>();
            return holder;
        }

        GameObject ToonProp(string model, Vector3 p, float rotY, Vector3 size, bool solid = true)
        {
            var o = Model("Props/" + model);
            if (o == null) return null;
            Fit(o, size);
            var holder = new GameObject(model);
            o.transform.SetParent(holder.transform, false);
            var rs = o.GetComponentsInChildren<Renderer>();
            Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            if (solid) { var c = holder.AddComponent<BoxCollider>(); c.center = b.center; c.size = b.size; }
            holder.transform.position = p; holder.transform.rotation = Quaternion.Euler(0, rotY, 0);
            if (solid && b.size.y > 1.1f) AddBlock(p, b.size, rotY);
            holder.transform.SetParent(transform, true);
            return holder;
        }

        // Shipping container: corrugated PBR body, frame posts, end doors with lock bars.
        // V16：卡通模式下集装箱换成可攀爬的美食方块（巧克力 / 海绵蛋糕 / 威化 / 果冻），高 2 m 可以直接翻上去；
        // 叠放时上层短 1.5 m，留出台阶，可以一层层爬上去。
        static readonly string[] FoodBlocks = { "ChocoBlock", "SpongeBlock", "WaferBlock", "JellyBlock" };
        GameObject FoodBlock(Vector3 p, float rotY, int stack)
        {
            GameObject first = null;
            int seed = Mathf.Abs(Mathf.RoundToInt(p.x * 7 + p.z * 13));
            for (int i = 0; i < stack; i++)
            {
                float len = i == 0 ? 6.06f : 4.5f, off = i == 0 ? 0 : .78f;
                var root = new GameObject("美食方块");
                root.transform.position = p + Vector3.up * 2f * i; root.transform.rotation = Quaternion.Euler(0, rotY, 0);
                var o = Model("Toon/Food/" + FoodBlocks[(seed + i) % FoodBlocks.Length]);
                if (o != null)
                {
                    foreach (var c in o.GetComponentsInChildren<Collider>()) Kill(c);
                    Fit(o, new Vector3(len, 2f, 2.44f));
                    o.transform.SetParent(root.transform, false); o.transform.localPosition += new Vector3(off, 0, 0);
                }
                var bc = root.AddComponent<BoxCollider>(); bc.center = new Vector3(off, 1f, 0); bc.size = new Vector3(len, 2f, 2.44f);
                root.AddComponent<FoodJiggle>();
                if (i == 0) AddBlock(root.transform.position, new Vector3(6.06f, 2f, 2.44f), rotY);
                var rt = root.transform; int level = i; float lo = off, ll = len;
                afterNav.Add(() => ClimbLinks(rt, level, lo, ll));
                root.transform.SetParent(transform, true);
                if (first == null) first = root;
            }
            return first;
        }

        // V17：AI 翻越美食方块用的导航连接（两侧长边各一条；上层从台阶处上去）
        void ClimbLinks(Transform block, int level, float off, float len)
        {
            var pts = new List<(Vector3, Vector3)>();
            if (level == 0)
                foreach (float x in new[] { -1.6f, 1.6f })
                    for (int side = -1; side <= 1; side += 2)
                        pts.Add((new Vector3(x, 0, side * 2.1f), new Vector3(x, 2f, side * .55f)));
            else pts.Add((new Vector3(-2.35f, 0, 0), new Vector3(off - len / 2 + .7f, 2f, 0)));
            foreach (var (a, b) in pts)
            {
                var s = new GameObject("翻越连接起点").transform; s.SetParent(block, false); s.localPosition = a;
                var e = new GameObject("翻越连接终点").transform; e.SetParent(block, false); e.localPosition = b;
                var link = s.gameObject.AddComponent<OffMeshLink>(); link.startTransform = s; link.endTransform = e; link.biDirectional = true; link.costOverride = 4f;
                climbLinks++;
            }
        }
        public int climbLinks;

        // 可以一层层爬上去的大型美食（每层 ≤ 1.5 m，用翻越就能上），tiers = (半径, 层高)，按模型原始尺寸再乘 scale
        void ClimbFood(string model, Vector3 p, float scale, float rotY, params Vector2[] tiers)
        {
            var o = Model("Toon/Food/" + model);
            if (o == null) return;
            foreach (var c in o.GetComponentsInChildren<Collider>()) Kill(c);
            var root = new GameObject("可攀爬美食 " + model);
            root.transform.position = p; root.transform.rotation = Quaternion.Euler(0, rotY, 0);
            o.transform.SetParent(root.transform, false); o.transform.localPosition = Vector3.zero; o.transform.localRotation = Quaternion.identity; o.transform.localScale = Vector3.one * scale;
            var cylMesh = ColliderCylinder();
            root.AddComponent<FoodJiggle>();
            float y = 0;
            foreach (var t in tiers)
            {
                float r = t.x * scale, h = t.y * scale;
                var col = new GameObject("台阶碰撞"); col.transform.SetParent(root.transform, false);
                col.transform.localPosition = new Vector3(0, y + h / 2, 0); col.transform.localScale = new Vector3(r * 2, h / 2, r * 2);
                var mc = col.AddComponent<MeshCollider>(); mc.sharedMesh = cylMesh; mc.convex = true;
                y += h;
            }
            AddBlock(p, new Vector3(tiers[0].x * scale * 1.6f, y, tiers[0].x * scale * 1.6f), 0);
            root.transform.SetParent(transform, true);
            if (model != "HotPot") spots.Add(new Spot { label = "可攀爬 · " + (model == "Pancakes" ? "松饼塔" : "生日蛋糕"), pos = p + Vector3.up * y, kind = 0 });
        }

        // 运行时可读的圆柱碰撞网格（内置 Cylinder 网格在打包后不可读，凸包碰撞会失效）：半径 0.5，高 2
        static Mesh colliderCyl;
        static Mesh ColliderCylinder()
        {
            if (colliderCyl != null) return colliderCyl;
            const int n = 24; var v = new Vector3[n * 2]; var tri = new List<int>();
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2 / n;
                v[i] = new Vector3(Mathf.Cos(a) * .5f, -1, Mathf.Sin(a) * .5f); v[i + n] = new Vector3(v[i].x, 1, v[i].z);
                int j = (i + 1) % n;
                tri.AddRange(new[] { i, i + n, j, j, i + n, j + n });
                if (i > 0 && i < n - 1) { tri.AddRange(new[] { 0, i, i + 1 }); tri.AddRange(new[] { n, n + i + 1, n + i }); }
            }
            colliderCyl = new Mesh { name = "台阶圆柱碰撞", vertices = v, triangles = tri.ToArray() };
            colliderCyl.RecalculateNormals(); colliderCyl.RecalculateBounds();
            return colliderCyl;
        }

        GameObject Container(Vector3 p, float rotY, Color tint, int stack = 1)
        {
            if (ToonAssets.Available) return FoodBlock(p, rotY, stack);
            GameObject first = null;
            for (int i = 0; i < stack; i++)
            {
                var root = new GameObject("集装箱");
                root.transform.position = p + Vector3.up * 2.6f * i; root.transform.rotation = Quaternion.Euler(0, rotY, 0);
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.transform.SetParent(root.transform, false); body.transform.localPosition = new Vector3(0, 1.3f, 0); body.transform.localScale = new Vector3(6.06f, 2.6f, 2.44f);
                var m = new Material(sheet); m.SetColor("_Color", Color.Lerp(tint, Color.white, i * .15f));
                body.GetComponent<Renderer>().sharedMaterial = m;
                for (int x = -1; x <= 1; x += 2) for (int z = -1; z <= 1; z += 2)
                    {
                        var post = GameObject.CreatePrimitive(PrimitiveType.Cube); Kill(post.GetComponent<Collider>());
                        post.transform.SetParent(root.transform, false); post.transform.localPosition = new Vector3(x * 3.0f, 1.3f, z * 1.2f); post.transform.localScale = new Vector3(.14f, 2.62f, .14f);
                        post.GetComponent<Renderer>().sharedMaterial = plate;
                    }
                for (int x = -1; x <= 1; x += 2)
                    for (int bar = -1; bar <= 1; bar += 2)
                    {
                        var rod = GameObject.CreatePrimitive(PrimitiveType.Cube); Kill(rod.GetComponent<Collider>());
                        rod.transform.SetParent(root.transform, false); rod.transform.localPosition = new Vector3(x * 3.04f, 1.3f, bar * .45f); rod.transform.localScale = new Vector3(.04f, 2.4f, .05f);
                        rod.GetComponent<Renderer>().sharedMaterial = plate;
                    }
                AddBlock(root.transform.position, new Vector3(6.06f, 2.6f, 2.44f), rotY);
                root.transform.SetParent(transform, true);
                if (first == null) first = root;
            }
            return first;
        }

        public Light Lamp(Vector3 p, Color c, float intensity, float range, bool flicker = false, PowerBox zone = null)
        {
            var l = new GameObject("灯光").AddComponent<Light>();
            l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range;
            l.shadows = LightShadows.None; l.transform.position = p;
            l.transform.SetParent(transform, true);
            // Lights on a breaker never flicker, otherwise the flicker script would relight them during a blackout.
            if (flicker && zone == null) l.gameObject.AddComponent<Flicker>().baseIntensity = intensity;
            if (zone != null) zone.Register(l);
            return l;
        }

        void StreetLamp(Vector3 p, float rotY, Color c)
        {
            if (PH("street_lamp_01", p, rotY, new Vector3(0, 4.6f, 0), true) == null) return;
            Lamp(p + Vector3.up * 4.4f, c, 3.4f, 18f);
        }

        // rotY points the sign toward the viewer.
        void Sign(string text, Vector3 p, float rotY, Color c, float size = 1f, bool vertical = false, PowerBox zone = null)
        {
            if (vertical) { var t = ""; foreach (var ch in text) t += ch + "\n"; text = t.TrimEnd('\n'); }
            Quaternion rot = Quaternion.Euler(0, rotY, 0);
            Vector3 toViewer = rot * Vector3.forward;
            var go = new GameObject("霓虹招牌 " + text);
            go.transform.position = p + toViewer * .07f;
            go.transform.rotation = rot * Quaternion.Euler(0, 180, 0);
            var tm = go.AddComponent<TextMesh>();
            var font = Resources.Load<Font>("NotoSansSC");
            tm.font = font; tm.text = text; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            tm.fontSize = 64; tm.characterSize = .08f * size; tm.color = Color.Lerp(c, Color.white, .55f); tm.lineSpacing = .85f;
            var mr = go.GetComponent<MeshRenderer>(); if (font != null) mr.sharedMaterial = WorldText(font);
            go.transform.SetParent(transform, true);
            int lines = vertical ? text.Split('\n').Length : 1;
            int cols = vertical ? 1 : text.Length;
            Vector3 boxSize = new Vector3(cols * .62f * size + .5f, lines * .78f * size + .35f, .1f);
            var back = Glowbox("招牌灯箱", p - toViewer * .05f, boxSize, Glow(c * .45f, .9f), rotY);
            var frameT = Glowbox("招牌边框", p - toViewer * .03f + Vector3.up * (boxSize.y / 2), new Vector3(boxSize.x + .06f, .05f, .12f), Glow(c, 3f), rotY);
            var frameB = Glowbox("招牌边框", p - toViewer * .03f - Vector3.up * (boxSize.y / 2), new Vector3(boxSize.x + .06f, .05f, .12f), Glow(c, 3f), rotY);
            Lamp(p + toViewer * 1.4f, c, 1.4f, 7f, Random.value < .15f, zone);
            if (zone != null) { zone.neon.Add(back.GetComponent<Renderer>()); zone.neon.Add(frameT.GetComponent<Renderer>()); zone.neon.Add(frameB.GetComponent<Renderer>()); zone.neon.Add(mr); }
        }

        void Block(string name, float x0, float x1, float z0, float z1, float h, Material skin)
        {
            Box(name, new Vector3((x0 + x1) / 2, h / 2, (z0 + z1) / 2), new Vector3(x1 - x0, h, z1 - z0), skin);
            Box(name + " 楼顶", new Vector3((x0 + x1) / 2, h + .3f, (z0 + z1) / 2), new Vector3(x1 - x0 + .4f, .6f, z1 - z0 + .4f), concrete, false, 0, false);
            if (ToonAssets.Available) FrostingEdges(x0, x1, z0, z1, h + .5f);
        }

        // V19：楼顶一圈奶油滴边（白 / 粉 / 巧克力轮换）
        int frostingIndex;
        void FrostingEdges(float x0, float x1, float z0, float z1, float y)
        {
            string variant = "Toon/Kitchen/FrostingEdge_" + "WPC"[frostingIndex++ % 3];
            var pf = Resources.Load<GameObject>(variant); if (pf == null) return;
            void Edge(Vector3 a, Vector3 b, float yaw)
            {
                float len = Vector3.Distance(a, b); int n = Mathf.Max(1, Mathf.RoundToInt(len / 4f)); float seg = len / n;
                for (int i = 0; i < n; i++)
                {
                    var o = Instantiate(pf); o.name = "奶油滴边";
                    o.transform.position = Vector3.Lerp(a, b, (i + .5f) / n); o.transform.rotation = Quaternion.Euler(0, yaw, 0);
                    o.transform.localScale = new Vector3(seg / 4f, 1f, 1f);
                    foreach (var r in o.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    o.transform.SetParent(transform, true);
                }
            }
            const float off = .32f;
            Edge(new Vector3(x0, y, z1 + off), new Vector3(x1, y, z1 + off), 0);
            Edge(new Vector3(x0, y, z0 - off), new Vector3(x1, y, z0 - off), 180);
            Edge(new Vector3(x1 + off, y, z0), new Vector3(x1 + off, y, z1), 90);
            Edge(new Vector3(x0 - off, y, z0), new Vector3(x0 - off, y, z1), -90);
        }

        // V19：人行道两边的糖果条纹路沿
        static Material curbMat;
        void CandyCurbs(Vector3 p, Vector3 size)
        {
            if (curbMat == null)
            {
                const int N = 64; var px = new Color[N * N];
                for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) px[y * N + x] = ((x + y) / 16) % 2 == 0 ? new Color(.95f, .2f, .3f) : Color.white;
                var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat }; t.SetPixels(px); t.Apply();
                curbMat = new Material(Shader.Find("Standard")) { mainTexture = t }; curbMat.SetFloat("_Glossiness", .55f);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                var c = Box("糖果路沿", new Vector3(p.x, .1f, p.z + side * (size.z / 2 - .12f)), new Vector3(size.x, .2f, .24f), curbMat, false);
                var mpb = new MaterialPropertyBlock(); mpb.SetVector("_MainTex_ST", new Vector4(size.x / .8f, 1, 0, 0)); c.GetComponent<Renderer>().SetPropertyBlock(mpb);
            }
        }

        // Photoscanned facade kit placed on a block face; the kit's street side is its +Z face.
        void Facade(string model, Vector3 basePoint, Vector3 facing, float width, float height)
        {
            if (ToonAssets.Available) return;   // V14：照片立面不适合 Q 版画风
            var o = Model("PH/" + model + "/" + model);
            if (o == null) return;
            Fit(o, new Vector3(width, height, 4.5f));
            var holder = new GameObject("外立面 " + model);
            o.transform.SetParent(holder.transform, false);
            var rs = o.GetComponentsInChildren<Renderer>();
            Bounds b = rs[0].bounds; foreach (var r in rs) { b.Encapsulate(r.bounds); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            o.transform.localPosition -= new Vector3(0, 0, b.max.z);
            if (model.Contains("factory"))
                foreach (var r in rs)
                {
                    // V10：工厂立面的白色窗框和墙板在黄昏光下过亮，压暗、去掉光泽
                    var mats = r.materials;
                    foreach (var m in mats)
                    {
                        if (m.HasProperty("baseColorFactor")) { var c = m.GetColor("baseColorFactor"); m.SetColor("baseColorFactor", new Color(c.r * .6f, c.g * .6f, c.b * .6f, c.a)); }
                        if (m.HasProperty("roughnessFactor")) m.SetFloat("roughnessFactor", 1f);
                        if (m.HasProperty("metallicFactor")) m.SetFloat("metallicFactor", 0f);
                    }
                    r.materials = mats;
                }
            holder.transform.position = basePoint + facing.normalized * .55f;
            holder.transform.rotation = Quaternion.LookRotation(facing);
            holder.transform.SetParent(transform, true);
        }

        void WindowStrips(Vector3 a, Vector3 b, float h, Vector3 facing, int rows)
        {
            Vector3 dir = b - a; float len = dir.magnitude; dir /= len;
            float rotY = Quaternion.LookRotation(facing).eulerAngles.y;
            var rnd = new System.Random((int)(a.x * 13 + a.z * 7));
            for (int r = 0; r < rows; r++)
            {
                float y = 4f + r * ((h - 5f) / Mathf.Max(1, rows - 1));
                for (float t = 2f; t < len - 2f; t += 4.5f)
                {
                    if (rnd.NextDouble() < .5) continue;
                    Vector3 wp = a + dir * t + Vector3.up * y;
                    Box("窗框", wp + facing * .03f, new Vector3(1.9f, 1.4f, .08f), plate, false, rotY, false).GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    // V8：多数窗户是暗玻璃，只有少数亮暖光
                    bool lit = rnd.NextDouble() < .18;
                    Glowbox("窗户", wp + facing * .08f, new Vector3(1.6f, 1.1f, .03f), lit ? Glow(new Color(1f, .78f, .5f), .45f) : darkGlass, rotY);
                    Glowbox("窗格", wp + facing * .1f, new Vector3(.06f, 1.1f, .03f), plate, rotY);
                }
            }
        }

        void Cable(Vector3 a, Vector3 b, float sag)
        {
            var lr = new GameObject("电线").AddComponent<LineRenderer>();
            lr.positionCount = 12; lr.startWidth = lr.endWidth = .035f;
            lr.material = Resources.Load<Material>("Materials/MetalPlate"); lr.startColor = lr.endColor = Color.black;
            for (int i = 0; i < 12; i++) { float t = i / 11f; lr.SetPosition(i, Vector3.Lerp(a, b, t) + Vector3.down * sag * 4 * t * (1 - t)); }
            lr.transform.SetParent(transform, true);
        }

        int sauceIndex; Material chocoMat, butterMat;
        void Puddle(Vector3 p, float sx, float sz, float rotY)
        {
            if (ToonAssets.Available)
            {
                // V17：积水换成巧克力酱（减速）/ 黄油（打滑）
                bool choco = sauceIndex++ % 2 == 0;
                var d = GameObject.CreatePrimitive(PrimitiveType.Cylinder); d.name = choco ? "巧克力酱地面" : "黄油地面";
                Kill(d.GetComponent<Collider>());
                d.transform.position = new Vector3(p.x, .02f, p.z); d.transform.rotation = Quaternion.Euler(0, rotY, 0); d.transform.localScale = new Vector3(sx, .012f, sz);
                var sm = choco ? chocoMat : butterMat;
                if (sm == null)
                {
                    sm = new Material(Shader.Find("Standard")); sm.color = choco ? new Color(.4f, .23f, .14f) : new Color(1f, .88f, .45f); sm.SetFloat("_Glossiness", .58f);
                    if (choco) chocoMat = sm; else butterMat = sm;
                }
                d.GetComponent<Renderer>().sharedMaterial = sm; d.transform.SetParent(transform, true);
                SauceZone.Add(new Vector3(p.x, 0, p.z), sx * .5f, sz * .5f, rotY, choco ? SauceZone.Chocolate : SauceZone.Butter);
                return;
            }
            return;   // V6：取消积水镜面（用户反馈反光过强）
            Glowbox("积水", new Vector3(p.x, .015f, p.z), new Vector3(sx * .7f, .01f, sz * .7f), puddle, rotY);
        }

        void Steam(Vector3 p)
        {
            PH("water_manhole_cover", p, 0, new Vector3(.9f, 0, 0), false);
            var go = new GameObject("蒸汽"); go.transform.position = p + Vector3.up * .1f; go.transform.SetParent(transform, true);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.startLifetime = 3f; main.startSpeed = 1.2f; main.startSize = new ParticleSystem.MinMaxCurve(1f, 2.4f);
            main.startColor = new Color(.6f, .65f, .75f, .12f); main.maxParticles = 60; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 12;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = .3f; sh.rotation = new Vector3(-90, 0, 0);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .2f), new GradientAlphaKey(0, 1) }); col.color = g;
            go.GetComponent<ParticleSystemRenderer>().material = Resources.Load<Material>("Materials/FxSmoke") ?? Fx.Additive;
        }

        void Stall(Vector3 p, float rotY, Color c, string text)
        {
            var root = new GameObject("夜市摊位 " + text);
            root.transform.position = p; root.transform.rotation = Quaternion.Euler(0, rotY, 0);
            root.transform.SetParent(transform, true);
            void Part(Vector3 lp, Vector3 s, Material m, bool col = true)
            {
                var o = GameObject.CreatePrimitive(PrimitiveType.Cube);
                if (!col) Kill(o.GetComponent<Collider>());
                o.transform.SetParent(root.transform, false); o.transform.localPosition = lp; o.transform.localScale = s;
                o.GetComponent<Renderer>().sharedMaterial = m;
            }
            Part(new Vector3(0, .55f, 0), new Vector3(3.6f, 1.1f, 1.6f), plate);
            Part(new Vector3(0, 1.12f, 0), new Vector3(3.7f, .05f, 1.7f), sheet);
            for (int i = -1; i <= 1; i += 2) for (int j = -1; j <= 1; j += 2) Part(new Vector3(i * 1.75f, 1.35f, j * .75f), new Vector3(.08f, 2.7f, .08f), plate, false);
            var awning = new Material(sheet); awning.SetColor("_Color", Color.Lerp(c, new Color(.45f, .45f, .42f), .65f));
            Part(new Vector3(0, 2.7f, 0), new Vector3(4.0f, .08f, 2.2f), awning, false);
            // V8：摊位不再有霓虹边条
            var l = new GameObject("摊位灯").AddComponent<Light>(); l.transform.SetParent(root.transform, false); l.transform.localPosition = new Vector3(0, 2.3f, 0);
            l.color = Color.Lerp(c, new Color(1f, .8f, .6f), .5f); l.range = 7; l.intensity = 1.8f;
            for (int k = 0; k < 3; k++)
            {
                var crate = ToonAssets.Available ? Model("Toon/Food/" + StallFood(text, k)) : Model("PH/plastic_crate_01/plastic_crate_01");
                if (crate == null) break;
                if (ToonAssets.Available) foreach (var cc in crate.GetComponentsInChildren<Collider>()) Kill(cc);
                Fit(crate, ToonAssets.Available ? new Vector3(0, .75f, 0) : new Vector3(.42f, 0, 0)); crate.transform.SetParent(root.transform, false);
                crate.transform.localPosition += new Vector3(-1.2f + k * 1.2f, 1.15f, .2f); crate.transform.localRotation = Quaternion.Euler(0, k * 17, 0);
            }
            AddBlock(p, new Vector3(3.6f, 1.2f, 1.6f), rotY);
            Sign(text, p + Quaternion.Euler(0, rotY, 0) * new Vector3(0, 3.25f, 0), rotY, c, .55f);
        }

        static string StallFood(string stall, int k)
        {
            switch (stall)
            {
                case "奶茶": return new[] { "MilkTea", "Popsicle", "MilkTea" }[k];
                case "火锅": return new[] { "Dumpling", "HotPot", "Skewers" }[k];
                case "甜品屋": return new[] { "CakeSlice", "Macarons", "Donut" }[k];
                case "拉面": return new[] { "Ramen", "Sushi", "Ramen" }[k];
                default: return new[] { "Skewers", "Burger", "Skewers" }[k];
            }
        }

        // V15：美食城地标 —— 屋顶上的巨型奶茶 / 火锅 / 蛋糕，只做装饰（不挡路、不进导航）
        void Food(string model, Vector3 p, float rotY, float height, bool solid = false)
        {
            var o = Model("Toon/Food/" + model);
            if (o == null) return;
            foreach (var c in o.GetComponentsInChildren<Collider>()) Kill(c);
            Fit(o, new Vector3(0, height, 0));
            var holder = new GameObject("美食地标 " + model);
            o.transform.SetParent(holder.transform, false);
            if (solid)
            {
                var rs = o.GetComponentsInChildren<Renderer>(); Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                var bc = holder.AddComponent<BoxCollider>(); bc.center = b.center; bc.size = b.size * .9f;
            }
            holder.transform.position = p; holder.transform.rotation = Quaternion.Euler(0, rotY, 0);
            holder.transform.SetParent(transform, true);
        }

        void FoodCity()
        {
            if (!ToonAssets.Available) return;
            // 内圈楼顶
            Food("MilkTea", new Vector3(-40, 14.6f, 20), 20, 11f);
            Food("HotPot", new Vector3(-2, 16.6f, 20), 0, 5f);
            Food("BigCake", new Vector3(-46, 13.6f, -20), 0, 7f);
            Food("IceCream", new Vector3(-8, 15.6f, -20), 0, 10f);
            Food("Burger", new Vector3(20, 9.6f, 29), 30, 4f);
            Food("Donut", new Vector3(20, 9.6f, -29), 90, 5f);
            Food("Macarons", new Vector3(-24, 15.6f, -22), 0, 4f);
            Food("Ramen", new Vector3(-28, 14.6f, 24), 0, 4f);
            // 外圈天际线
            Food("MilkTea", new Vector3(-60, 20.6f, 55), 0, 16f);
            Food("BigCake", new Vector3(-20, 20.6f, 55), 0, 12f);
            Food("HotPot", new Vector3(30, 20.6f, 56), 0, 8f);
            Food("IceCream", new Vector3(60, 20.6f, 55), 0, 14f);
            Food("HotPot", new Vector3(-40, 20.6f, -55), 0, 8f);
            Food("MilkTea", new Vector3(0, 20.6f, -55), 0, 16f);
            Food("Popsicle", new Vector3(40, 20.6f, -55), 0, 12f);
            Food("Donut", new Vector3(-77, 18.6f, 30), 90, 9f);
            Food("Burger", new Vector3(-77, 18.6f, -30), 0, 6f);
            Food("BigCake", new Vector3(77, 18.6f, 30), 0, 10f);
            Food("MilkTea", new Vector3(77, 18.6f, -30), 0, 14f);
            // V16：街上可攀爬的大型美食
            ClimbFood("Pancakes", new Vector3(-46.5f, 0, 44f), 1.25f, 0, new Vector2(2.4f, .75f), new Vector2(1.65f, .75f), new Vector2(.95f, .75f));
            ClimbFood("BigCake", new Vector3(-46.5f, 0, -44f), 1.6f, 0, new Vector2(1.65f, .9f), new Vector2(1.05f, .75f), new Vector2(.5f, .65f));
            // V17：能吃的补给
            string[] snacks = { "CakeSlice", "MilkTea", "Donut", "IceCream", "Macarons", "CakeSlice", "MilkTea" };
            var snackSpots = new[] { new Vector3(-34, 0, 4.2f), new Vector3(-21, 0, 21), new Vector3(-33, 0, -20), new Vector3(20, 0, .5f), new Vector3(24, 0, 44.5f), new Vector3(24, 0, -44.5f), new Vector3(50.5f, 0, 5f) };
            for (int i = 0; i < snackSpots.Length; i++)
            {
                var sg = new GameObject("美食补给 " + snacks[i]); sg.transform.position = snackSpots[i];
                sg.AddComponent<Snack>().Build(snacks[i]); sg.transform.SetParent(transform, true);
            }
            // V17：南工厂通道的火锅 —— 热气挡视线，站进锅里会被烫，可以翻上锅沿
            {
                var hp = new Vector3(8, 0, -35.5f);
                ClimbFood("HotPot", hp, .9f, 0, new Vector2(1.5f, 1.0f));
                var hz = new GameObject("火锅烫伤区"); hz.transform.position = hp; hz.AddComponent<HotpotHazard>(); hz.transform.SetParent(transform, true);
                SteamCloud.particles.Add(BigSteam(hp + Vector3.up * 1.2f, 2.2f, .5f));
                SteamCloud.Add(hp + Vector3.up * 2.6f, 2.6f);
            }
            // V18：食材刷新点
            foreach (var sp in new[] { new Vector3(-45, 0, -4), new Vector3(-30, 0, 5.2f), new Vector3(-12, 0, -5.5f), new Vector3(14, 0, 0), new Vector3(24, 0, -7.5f),
                new Vector3(-52, 0, 40), new Vector3(-22, 0, 44), new Vector3(2, 0, 36), new Vector3(-52, 0, -39.5f), new Vector3(-24, 0, -41), new Vector3(2, 0, -34),
                new Vector3(24, 0, -36), new Vector3(39, 0, 22.5f), new Vector3(40, 0, -30), new Vector3(-21, 0, 14), new Vector3(-33, 0, -14), new Vector3(52.5f, 0, -8), new Vector3(-60, 0, 14) })
            { var g = new GameObject("食材刷新点"); g.transform.position = sp; g.AddComponent<IngredientSpot>(); g.transform.SetParent(transform, true); }
            // V18：厨房设施
            void Station(KitchenStation.Kind k, Vector3 p, float rotY) { var g = new GameObject("厨房 " + k); g.transform.position = p; g.transform.rotation = Quaternion.Euler(0, rotY, 0); g.AddComponent<KitchenStation>().Build(k); g.transform.SetParent(transform, true); }
            Station(KitchenStation.Kind.HotpotHeal, new Vector3(-26, 0, -8.3f), 0); Station(KitchenStation.Kind.HotpotHeal, new Vector3(53, 0, -14), 0); Station(KitchenStation.Kind.HotpotHeal, new Vector3(-36, 0, 33.6f), 0);
            Station(KitchenStation.Kind.MilkTea, new Vector3(-58.5f, 0, 8.5f), 180); Station(KitchenStation.Kind.MilkTea, new Vector3(58.5f, 0, -8.5f), 0);
            Station(KitchenStation.Kind.Oven, new Vector3(16, 0, -12.4f), 0); Station(KitchenStation.Kind.Freezer, new Vector3(16, 0, 12.4f), 180);
            // V18：传菜转盘（站上去会被带着转）
            {
                var tg = new GameObject("传菜转盘"); tg.transform.position = new Vector3(8, 0, -2.5f); tg.transform.SetParent(transform, true);
                var tt = tg.AddComponent<Turntable>(); tt.Build();
                var col = new GameObject("转盘碰撞"); col.transform.SetParent(tg.transform, false); col.transform.localPosition = new Vector3(0, .125f, 0); col.transform.localScale = new Vector3(4.5f, .125f, 4.5f);
                var mc = col.AddComponent<MeshCollider>(); mc.sharedMesh = ColliderCylinder(); mc.convex = true;
            }
            // V18：曼妥思 + 可乐（汽水手雷炸到这里会喷泉）
            foreach (var mp in new[] { new Vector3(-17, 0, 40.5f), new Vector3(-17, 0, -40.5f) })
            {
                var mg = new GameObject("曼妥思可乐"); mg.transform.position = mp; mg.transform.SetParent(transform, true);
                var mm = Model("Toon/Kitchen/Mentos"); if (mm != null) { foreach (var c in mm.GetComponentsInChildren<Collider>()) Kill(c); Fit(mm, new Vector3(0, 1.7f, 0)); mm.transform.SetParent(mg.transform, false); }
                var cc = new GameObject("瓶子碰撞"); cc.transform.SetParent(mg.transform, false); cc.transform.localPosition = new Vector3(0, .85f, 0); cc.transform.localScale = new Vector3(.6f, .85f, .6f);
                var mc2 = cc.AddComponent<MeshCollider>(); mc2.sharedMesh = ColliderCylinder(); mc2.convex = true;
                mg.AddComponent<MentosPile>();
            }
            // V19：天上的棉花糖云
            for (int i = 0; i < 12; i++)
            {
                var cl = Model("Toon/Kitchen/Cloud_" + (1 + i % 3)); if (cl == null) break;
                foreach (var c in cl.GetComponentsInChildren<Collider>()) Kill(c);
                foreach (var r in cl.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                float k = 1.4f + (i % 4) * .35f; cl.transform.localScale = Vector3.one * k;
                cl.transform.position = new Vector3(-130 + i * 23f, 42 + (i % 3) * 7f, (i % 2 == 0 ? 1 : -1) * (25 + (i * 17) % 60));
                cl.transform.rotation = Quaternion.Euler(0, i * 37, 0);
                cl.AddComponent<CloudDrift>().speed = .8f + (i % 3) * .4f;
            }
            // V19：外圈人行道的糖果树（贴墙，不挡路）
            foreach (float tx in new[] { -63f, -45f, -38f, -14f, 6f, 30f, 44f, 60f })
                foreach (float tz in new[] { 49.4f, -49.4f })
                {
                    var tr = Model("Toon/Kitchen/CandyTree"); if (tr == null) break;
                    foreach (var c in tr.GetComponentsInChildren<Collider>()) Kill(c);
                    Fit(tr, new Vector3(0, 4.2f, 0)); var hold = new GameObject("糖果树"); tr.transform.SetParent(hold.transform, false);
                    hold.transform.position = new Vector3(tx, .12f, tz); hold.transform.rotation = Quaternion.Euler(0, tz > 0 ? 180 : 0, 0); hold.transform.SetParent(transform, true);
                }
            // V19：路边一堆堆彩色糖粒
            foreach (var sp in new[] { new Vector3(-48, .02f, 8.4f), new Vector3(-8, .02f, -8.4f), new Vector3(-56, .02f, 31.4f), new Vector3(-30, .02f, -31.4f), new Vector3(10, .02f, 31.4f), new Vector3(30, .02f, -6f), new Vector3(46, .02f, 6f), new Vector3(-8, .02f, 48.6f), new Vector3(-40, .02f, -48.6f), new Vector3(20, .02f, 48.6f) })
            {
                var pile = Model("Toon/Kitchen/SprinklePile"); if (pile == null) break;
                foreach (var c in pile.GetComponentsInChildren<Collider>()) Kill(c);
                Fit(pile, new Vector3(1.4f, 0, 0)); pile.transform.position += sp; pile.transform.rotation = Quaternion.Euler(0, sp.x * 13, 0); pile.transform.SetParent(transform, true);
            }
            // 出生区顶棚招牌
            Food("Donut", new Vector3(-64, 4.45f, 0), 90, 3.2f);
            Food("MilkTea", new Vector3(64, 4.45f, 0), 0, 3.2f);
            // 火锅冒热气
            foreach (var hp in new[] { new Vector3(-2, 21.4f, 20), new Vector3(30, 28.4f, 56), new Vector3(-40, 28.4f, -55) }) BigSteam(hp, 2f, .25f);
        }

        ParticleSystem BigSteam(Vector3 p, float radius, float alpha)
        {
            var go = new GameObject("火锅热气"); go.transform.position = p; go.transform.SetParent(transform, true);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.startLifetime = 4f; main.startSpeed = 1.5f; main.startSize = new ParticleSystem.MinMaxCurve(2.5f, 5f);
            main.startColor = new Color(1f, 1f, 1f, alpha); main.maxParticles = alpha > .4f ? 140 : 80; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = alpha > .4f ? 28 : 10;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = radius; sh.rotation = new Vector3(-90, 0, 0);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .2f), new GradientAlphaKey(0, 1) }); col.color = g;
            go.GetComponent<ParticleSystemRenderer>().material = Resources.Load<Material>("Materials/FxSmoke") ?? Fx.Additive;
            return ps;
        }

        // ------------------------------------------------------------------ layout
        void Buildings()
        {
            Block("北侧街区", -82, 82, 50, 60, 20, brick);
            Block("南侧街区", -82, 82, -60, -50, 20, panels);
            Block("西侧街区", -82, -72, -50, 50, 18, brick);
            Block("东侧街区", 72, 82, -50, 50, 18, panels);
            // V4：两个街区中间打通小巷（x∈[-24,-18] 与 x∈[-36,-30]），形成新的绕行路线
            Block("北街区公寓", -56, -24, 10, 30, 14, brick);
            Block("北街区公寓", -18, 14, 10, 30, 16, brick);
            Block("南街区工厂", -56, -36, -30, -10, 13, panels);
            Block("南街区工厂", -30, 14, -30, -10, 15, panels);
            Block("北连廊商铺", 14, 26, 24, 34, 9, brick);
            Block("南连廊仓库", 14, 26, -34, -24, 9, panels);
            Block("中庭市场大楼", 30, 44, -9, 9, 8, concrete);

            Facade("modular_urban_apartments_facade", new Vector3(-40f, 0, 10), Vector3.back, 32, 14);
            Facade("modular_urban_apartments_facade", new Vector3(-2f, 0, 10), Vector3.back, 32, 16);
            Facade("modular_factory_facade", new Vector3(-46f, 0, -10), Vector3.forward, 20, 13);
            Facade("modular_factory_facade", new Vector3(-8f, 0, -10), Vector3.forward, 44, 15);
            Facade("modular_urban_apartments_facade", new Vector3(-48, 0, 50), Vector3.back, 46, 18);
            Facade("modular_urban_apartments_facade", new Vector3(0, 0, 50), Vector3.back, 46, 18);
            Facade("modular_factory_facade", new Vector3(-48, 0, -50), Vector3.forward, 46, 18);
            Facade("modular_factory_facade", new Vector3(0, 0, -50), Vector3.forward, 46, 18);

            WindowStrips(new Vector3(-56, 0, 30), new Vector3(-24, 0, 30), 14, Vector3.forward, 3);
            WindowStrips(new Vector3(-18, 0, 30), new Vector3(14, 0, 30), 16, Vector3.forward, 3);
            WindowStrips(new Vector3(-56, 0, -30), new Vector3(-36, 0, -30), 13, Vector3.back, 3);
            WindowStrips(new Vector3(-30, 0, -30), new Vector3(14, 0, -30), 15, Vector3.back, 3);
            WindowStrips(new Vector3(26, 0, 50), new Vector3(72, 0, 50), 20, Vector3.back, 4);
            WindowStrips(new Vector3(26, 0, -50), new Vector3(72, 0, -50), 20, Vector3.forward, 4);
            WindowStrips(new Vector3(72, 0, -50), new Vector3(72, 0, 50), 18, Vector3.left, 4);
            WindowStrips(new Vector3(-72, 0, -50), new Vector3(-72, 0, 50), 18, Vector3.right, 4);
            foreach (float fx in new[] { -45f, -10f, 6f }) PH("modular_fire_escape", new Vector3(fx, 3.4f, 30.8f), 0, new Vector3(0, 9.7f, 0), false, false);
            for (int i = 0; i < 6; i++)
            {
                float x = -50 + i * 12;
                Cable(new Vector3(x, 11, 10), new Vector3(x + 3, 11.5f, -10), 1.2f);
                Cable(new Vector3(x + 2, 12, 30), new Vector3(x - 1, 13, 50), 1.5f);
                Cable(new Vector3(x - 2, 12, -30), new Vector3(x + 1, 13, -50), 1.5f);
            }
        }

        void Spawns()
        {
            Box("攻方出生区北墙", new Vector3(-69.5f, 2, 10), new Vector3(5, 4, .6f), concrete);
            Box("攻方出生区北墙", new Vector3(-58, 2, 10), new Vector3(4, 4, .6f), concrete);
            Box("攻方出生区南墙", new Vector3(-69.5f, 2, -10), new Vector3(5, 4, .6f), concrete);
            Box("攻方出生区南墙", new Vector3(-58, 2, -10), new Vector3(4, 4, .6f), concrete);
            Box("攻方出生区顶棚", new Vector3(-64, 4.3f, 0), new Vector3(16, .3f, 20), plate, false, 0, false);
            for (int i = -1; i <= 1; i++) Glowbox("停车位线", new Vector3(-66, .02f, i * 4), new Vector3(5, .02f, .12f), white);
            Sign("攻方集结区", new Vector3(-56.4f, 3.4f, 9.4f), 90, new Color(1f, .45f, .2f), .7f);
            Lamp(new Vector3(-64, 3.8f, -4), new Color(1f, .6f, .3f), 2.2f, 12f); Lamp(new Vector3(-64, 3.8f, 4), new Color(1f, .6f, .3f), 2.2f, 12f);
            PH("portable_generator", new Vector3(-70, 0, -7.5f), 90, new Vector3(1f, 0, 0));
            PH("old_military_crate", new Vector3(-70, 0, 7), 90, new Vector3(1.9f, 0, 0));
            Box("守方出生区北墙", new Vector3(58, 2, 10), new Vector3(4, 4, .6f), concrete);
            Box("守方出生区北墙", new Vector3(69.5f, 2, 10), new Vector3(5, 4, .6f), concrete);
            Box("守方出生区南墙", new Vector3(58, 2, -10), new Vector3(4, 4, .6f), concrete);
            Box("守方出生区南墙", new Vector3(69.5f, 2, -10), new Vector3(5, 4, .6f), concrete);
            Box("守方出生区西墙", new Vector3(56, 2, -6.5f), new Vector3(.6f, 4, 7), concrete);
            Box("守方出生区西墙", new Vector3(56, 2, 6.5f), new Vector3(.6f, 4, 7), concrete);
            Box("守方出生区顶棚", new Vector3(64, 4.3f, 0), new Vector3(16, .3f, 20), plate, false, 0, false);
            Sign("守方安保站", new Vector3(55.6f, 3.4f, 0), -90, new Color(.3f, .7f, 1f), .7f);
            Lamp(new Vector3(64, 3.8f, -4), new Color(.4f, .7f, 1f), 2.2f, 12f); Lamp(new Vector3(64, 3.8f, 4), new Color(.4f, .7f, 1f), 2.2f, 12f);
            noBuild.Add(new Vector3(-64, 10, 0)); noBuild.Add(new Vector3(64, 10, 0));
        }

        void MidStreet()
        {
            string[] names = { "拉面", "电玩城", "义体诊所", "霓虹酒吧", "当铺" };
            Color[] cols = { new Color(1f, .3f, .2f), new Color(.2f, .85f, 1f), new Color(.4f, 1f, .6f), new Color(1f, .2f, .8f), new Color(1f, .7f, .2f) };
            if (ToonAssets.Available) names = new[] { "奶茶", "火锅", "甜品屋", "拉面", "烧烤串串" };   // V15：美食街
            for (int i = 0; i < 5; i++) Stall(new Vector3(-46 + i * 12, 0, i % 2 == 0 ? .8f : -.8f), i % 2 == 0 ? 0 : 180, cols[i], names[i]);
            Box("人行道", new Vector3(-21, .06f, 8.6f), new Vector3(70, .12f, 2.8f), pavement, false);
            Box("人行道", new Vector3(-21, .06f, -8.6f), new Vector3(70, .12f, 2.8f), pavement, false);
            Sign("药房24H", new Vector3(-50, 6.5f, 9.3f), 180, new Color(.3f, 1f, .5f), .8f, true);
            Sign("网吧", new Vector3(-26, 7f, 9.3f), 180, new Color(.2f, .8f, 1f), 1f, true);
            Sign("旧港夜市", new Vector3(-6, 8f, 9.3f), 180, new Color(1f, .25f, .7f), 1f, true);
            Sign("机械维修", new Vector3(-40, 6.5f, -9.3f), 0, new Color(1f, .6f, .2f), .8f, true);
            Sign("义体改造", new Vector3(-14, 7f, -9.3f), 0, new Color(.8f, .3f, 1f), .9f, true);
            Sign("旧港夜市 · 主街", new Vector3(-21, 10.5f, 9.4f), 180, new Color(1f, .25f, .7f), 1.2f);
            // V8：取消檐口霓虹灯带
            for (int i = 0; i < 4; i++)
            {
                Window(new Vector3(-44 + i * 16, .9f, 9.75f), 180, new Color(1f, .5f, .8f));
                Window(new Vector3(i == 0 ? -42 : -36 + i * 16, .9f, -9.75f), 0, new Color(.4f, .9f, 1f));
            }
            StreetLamp(new Vector3(-50, .1f, -8.4f), 0, new Color(1f, .78f, .55f));
            StreetLamp(new Vector3(-28, .1f, 8.4f), 180, new Color(1f, .78f, .55f));
            StreetLamp(new Vector3(-4, .1f, -8.4f), 0, new Color(1f, .78f, .55f));
            PH("utility_box_02", new Vector3(-36, .12f, 8.9f), 180, new Vector3(1.1f, 0, 0));
            PH("metal_trash_can", new Vector3(-18, .12f, -8.8f), 0, new Vector3(1.85f, 0, 0));
            PH("modular_street_seating", new Vector3(-30, .12f, -8.6f), 0, new Vector3(3.5f, 0, 0));
            PH("WetFloorSign_01", new Vector3(-24, 0, 6.5f), 30, new Vector3(0, .63f, 0), false);
            Puddle(new Vector3(-40, 0, 4), 3.5f, 2f, 20); Puddle(new Vector3(-14, 0, -4.5f), 4f, 2.4f, -15); Puddle(new Vector3(6, 0, 5), 3f, 1.7f, 40);
            Steam(new Vector3(-32, .02f, -3.8f)); Steam(new Vector3(0, .02f, 3.5f));
        }

        void Window(Vector3 p, float rotY, Color c)
        {
            var go = new GameObject("商铺橱窗");
            go.transform.position = p; go.transform.rotation = Quaternion.Euler(0, rotY, 0);
            var g = go.AddComponent<GlassPanel>(); g.Build(new Vector3(4f, 2.2f, .06f), c);
            go.transform.SetParent(transform, true);
            glass.Add(g);
            Vector3 back = go.transform.rotation * Vector3.back;
            Glowbox("橱窗内光", p + back * .25f + Vector3.up * 1.1f, new Vector3(3.8f, 2.0f, .05f), Glow(c * .35f, .6f), rotY);
            Box("橱窗雨棚", p + Vector3.up * 2.45f + go.transform.rotation * Vector3.forward * .4f, new Vector3(4.3f, .08f, .9f), plate, false, rotY, false);
        }

        void NorthStreet()
        {
            Box("人行道", new Vector3(-21, .06f, 31.4f), new Vector3(70, .12f, 2.8f), pavement, false);
            Box("人行道", new Vector3(-23, .06f, 48.6f), new Vector3(98, .12f, 2.8f), pavement, false);
            Container(new Vector3(-40, 0, 41), 0, new Color(.2f, .45f, .65f));
            Container(new Vector3(-12, 0, 44), 90, new Color(.7f, .3f, .2f), 2);
            Container(new Vector3(8, 0, 38), 0, new Color(.25f, .55f, .35f));
            PH("metal_trash_can", new Vector3(-54, .12f, 48.6f), 180, new Vector3(1.85f, 0, 0));
            PH("utility_box_01", new Vector3(-30, .12f, 49f), 180, new Vector3(.6f, 0, 0));
            PH("old_tyre", new Vector3(-26, .3f, 46f), 0, new Vector3(.8f, 0, 0), false);
            PH("propane_tank", new Vector3(-2, 0, 47.8f), 0, new Vector3(0, .8f, 0));
            PH("concrete_road_barrier", new Vector3(-24, 0, 37), 0, new Vector3(0, 1.05f, 0));
            TWall(new Vector3(16, 0, 44), 90);
            StreetLamp(new Vector3(-48, .1f, 31.4f), 180, new Color(1f, .5f, .85f));
            StreetLamp(new Vector3(-20, .1f, 48.6f), 0, new Color(.5f, .85f, 1f));
            StreetLamp(new Vector3(6, .1f, 31.4f), 180, new Color(1f, .78f, .55f));
            Sign("A 长街 →", new Vector3(-62, 4.5f, 49.4f), 180, new Color(1f, .45f, .2f), .9f);
            Sign("霓城快运", new Vector3(-30, 9f, 49.4f), 180, new Color(.2f, .85f, 1f), 1.6f);
            Sign("胶囊旅馆", new Vector3(20, 7f, 34.5f), 0, new Color(1f, .2f, .7f), .9f, true);
            Puddle(new Vector3(-34, 0, 38), 4, 2, 10); Puddle(new Vector3(0, 0, 42), 3, 2, -30);
            Steam(new Vector3(-16, .02f, 38));
            Container(new Vector3(-66, 0, 24), 90, new Color(.6f, .5f, .2f));
            PH("concrete_road_barrier", new Vector3(-62, 0, 34), 90, new Vector3(0, 1.05f, 0));
            StreetLamp(new Vector3(-71, .1f, 16), 90, new Color(1f, .78f, .55f));
        }

        void SouthStreet()
        {
            Box("人行道", new Vector3(-21, .06f, -31.4f), new Vector3(70, .12f, 2.8f), pavement, false);
            Box("人行道", new Vector3(-23, .06f, -48.6f), new Vector3(98, .12f, 2.8f), pavement, false);
            Container(new Vector3(-36, 0, -42), 0, new Color(.7f, .35f, .15f), 2);
            Container(new Vector3(-8, 0, -38), 90, new Color(.25f, .4f, .6f));
            Container(new Vector3(10, 0, -45), 0, new Color(.5f, .5f, .5f));
            PH("portable_generator", new Vector3(-50, 0, -47.5f), 0, new Vector3(1f, 0, 0));
            PH("Barrel_01", new Vector3(-22, 0, -47.8f), 0, new Vector3(0, .9f, 0));
            PH("barrel_03", new Vector3(-21.2f, 0, -47.2f), 40, new Vector3(0, .9f, 0));
            PH("cardboard_box_01", new Vector3(-16, .12f, -48.6f), 15, new Vector3(.6f, 0, 0), false);
            PH("concrete_road_barrier", new Vector3(-20, 0, -37), 0, new Vector3(0, 1.05f, 0));
            TWall(new Vector3(18, 0, -40), 90);
            StreetLamp(new Vector3(-44, .1f, -31.4f), 0, new Color(1f, .78f, .55f));
            StreetLamp(new Vector3(-12, .1f, -48.6f), 180, new Color(.5f, .85f, 1f));
            Sign("B 工厂通道 →", new Vector3(-62, 4.5f, -49.4f), 0, new Color(1f, .45f, .2f), .9f);
            Sign("棱能集团 · 第七工厂", new Vector3(-24, 10f, -49.4f), 0, new Color(1f, .65f, .2f), 1.4f);
            for (int i = 0; i < 3; i++) PH("rollershutter_window_01", new Vector3(-48 + i * 20, .12f, -30.25f), 180, new Vector3(5f, 0, 0), false);
            Puddle(new Vector3(-28, 0, -40), 4, 2.4f, -10); Puddle(new Vector3(4, 0, -36), 3, 2, 25);
            Steam(new Vector3(-6, .02f, -44));
            Container(new Vector3(-66, 0, -26), 90, new Color(.3f, .5f, .5f));
            PH("concrete_road_barrier", new Vector3(-62, 0, -36), 90, new Vector3(0, 1.05f, 0));
            StreetLamp(new Vector3(-71, .1f, -16), 90, new Color(1f, .78f, .55f));
        }

        void Plaza()
        {
            Box("A连廊隔墙", new Vector3(16, 2, 14), new Vector3(4, 4, .6f), concrete);
            Box("A连廊隔墙", new Vector3(24, 2, 14), new Vector3(4, 4, .6f), concrete);
            Box("B连廊隔墙", new Vector3(16, 2, -14), new Vector3(4, 4, .6f), concrete);
            Box("B连廊隔墙", new Vector3(24, 2, -14), new Vector3(4, 4, .6f), concrete);
            Sign("A 连廊", new Vector3(20, 5f, 13.6f), 180, new Color(1f, .5f, .2f), .7f);
            Sign("B 连廊", new Vector3(20, 5f, -13.6f), 0, new Color(1f, .5f, .2f), .7f);
            Sign("霓城中央市场", new Vector3(37, 6.2f, 9.1f), 0, new Color(.3f, 1f, .9f), 1.1f);
            Sign("霓城中央市场", new Vector3(37, 6.2f, -9.1f), 180, new Color(.3f, 1f, .9f), 1.1f);
            Sign("全息广告 · 棱能饮料", new Vector3(29.9f, 5.5f, 0), -90, new Color(1f, .2f, .7f), .8f);
            // V7：市场楼顶改为狙击位，原屋顶灯带取消
            PH("concrete_road_barrier", new Vector3(26, 0, 3), 90, new Vector3(0, 1.05f, 0));
            TWall(new Vector3(48, 0, 12), 0);
            TWall(new Vector3(48, 0, -12), 0);
            Container(new Vector3(52, 0, 30), 90, new Color(.55f, .2f, .2f));
            Container(new Vector3(52, 0, -30), 90, new Color(.2f, .35f, .55f));
            StreetLamp(new Vector3(27, .1f, 12), 90, new Color(.5f, .85f, 1f));
            StreetLamp(new Vector3(47, .1f, -12), -90, new Color(1f, .78f, .55f));
            PH("utility_box_01", new Vector3(50, 0, 16), 0, new Vector3(.6f, 0, 0));
            Puddle(new Vector3(22, 0, -4), 4, 2, 15); Puddle(new Vector3(50, 0, 4), 3, 2, -20);
            Steam(new Vector3(24, .02f, 6));
        }

        PowerBox NewPowerBox(string zone, Vector3 p, float rotY)
        {
            var go = new GameObject("电闸 " + zone); go.transform.position = p; go.transform.rotation = Quaternion.Euler(0, rotY, 0);
            var pb = go.AddComponent<PowerBox>(); pb.zone = zone; pb.Build();
            go.transform.SetParent(transform, true); powerBoxes.Add(pb);
            return pb;
        }

        void SiteAArea()
        {
            zoneA = NewPowerBox("A 点", new Vector3(27.2f, 0, 45), 90);
            BreakableWall("A点南墙（可破坏）", new Vector3(37, 0, 18), 0, 10, 3.5f);
            Box("A点东墙", new Vector3(48, 1.75f, 34), new Vector3(.6f, 3.5f, 16), concrete);
            Container(new Vector3(31, 0, 41), 0, new Color(.75f, .45f, .1f), 2);
            Container(new Vector3(43, 0, 30), 90, new Color(.2f, .45f, .6f), 2);
            Container(new Vector3(33, 0, 26), 0, new Color(.6f, .2f, .2f));
            Container(new Vector3(30, 0, 47.5f), 0, new Color(.3f, .3f, .35f));
            PH("portable_generator", new Vector3(39, 0, 40), 30, new Vector3(1.1f, 0, 0));
            PH("old_military_crate", new Vector3(36, 0, 44), 0, new Vector3(1.9f, 0, 0));
            PH("metal_jerrycan", new Vector3(40, 0, 25), 10, new Vector3(0, .5f, 0), false);
            SiteMarker("A", SiteA, new Color(1f, .45f, .2f));
            Sign("A · 霓城货运广场", new Vector3(37, 7f, 49.4f), 180, new Color(1f, .5f, .2f), 1.3f, false, zoneA);
            Sign("A", new Vector3(26.4f, 5.5f, 21f), -90, new Color(1f, .5f, .2f), 2f, false, zoneA);
            for (int i = 0; i < 3; i++)
            {
                Lamp(new Vector3(30 + i * 7, 6f, 34), new Color(1f, .75f, .5f), 2.2f, 13f, false, zoneA);
                PH("mounted_fluorescent_lights", new Vector3(30 + i * 7, 6.2f, 34), 0, new Vector3(1.4f, 0, 0), false, false);
            }
            Flood(new Vector3(27.5f, 5.5f, 30), 90, zoneA); Flood(new Vector3(37, 5.5f, 49.4f), 180, zoneA); Flood(new Vector3(47.4f, 5.5f, 40), -90, zoneA);
            PH("security_light", new Vector3(47.6f, 4.5f, 22), -90, new Vector3(0, .6f, 0), false);
            Lamp(new Vector3(46.5f, 4.2f, 22), new Color(1f, .95f, .85f), 2f, 12f, false, zoneA);
            Puddle(new Vector3(36, 0, 32), 3.4f, 2.2f, 30);
        }

        void SiteBArea()
        {
            zoneB = NewPowerBox("B 点", new Vector3(27.6f, 0, -45), 90);
            Box("B厂房北墙", new Vector3(32.5f, 2.5f, -18), new Vector3(3, 5, .6f), iron);
            Box("B厂房北墙", new Vector3(43, 2.5f, -18), new Vector3(2, 5, .6f), iron);
            Box("B厂房北墙窗台", new Vector3(38, .5f, -18), new Vector3(8, 1f, .6f), iron);
            Box("B厂房北墙上沿", new Vector3(38, 4.3f, -18), new Vector3(8, 1.4f, .6f), iron, false);
            Box("B门框", new Vector3(29, 4.1f, -18), new Vector3(4.6f, 1.8f, .8f), iron, false);
            BreakableWall("B厂房西墙（可破坏）", new Vector3(27, 0, -46), 90, 8, 5f);
            Box("B厂房东墙", new Vector3(48, 2.5f, -34), new Vector3(.6f, 5, 16), iron);
            Box("B厂房屋顶", new Vector3(37, 7f, -34), new Vector3(22, .4f, 24), sheet, false, 0, false);
            for (int i = 0; i < 5; i++) Box("屋顶钢梁", new Vector3(28 + i * 4.5f, 6.6f, -34), new Vector3(.3f, .4f, 24), plate, false, 0, false);
            for (int i = 0; i < 4; i++)
            {
                var g = new GameObject("B厂房玻璃"); g.transform.position = new Vector3(35 + i * 2f, 1f, -18); g.transform.SetParent(transform, true);
                var gp = g.AddComponent<GlassPanel>(); gp.Build(new Vector3(2f, 2.6f, .06f), new Color(.5f, .8f, 1f)); glass.Add(gp);
            }
            PH("steel_frame_shelves_01", new Vector3(30.5f, 0, -27), 90, new Vector3(0, 3.6f, 0));
            PH("steel_frame_shelves_01", new Vector3(45, 0, -42), 0, new Vector3(0, 3.6f, 0));
            Container(new Vector3(42, 0, -26), 0, new Color(.4f, .4f, .45f));
            PH("portable_generator", new Vector3(35, 0, -42), 0, new Vector3(1.1f, 0, 0));
            PH("old_military_crate", new Vector3(32, 0, -38), 90, new Vector3(1.9f, 0, 0));
            PH("metal_jerrycan", new Vector3(40, 0, -38), 10, new Vector3(0, .5f, 0), false);
            MilCrates(new Vector3(40, 0, -38), 0);
            SiteMarker("B", SiteB, new Color(.3f, .8f, 1f));
            Sign("B · 第七工厂车间", new Vector3(37, 5.7f, -18.4f), 0, new Color(.3f, .8f, 1f), .9f, false, zoneB);
            Sign("B", new Vector3(26.4f, 5f, -21f), -90, new Color(.3f, .8f, 1f), 2f, false, zoneB);
            for (int i = 0; i < 3; i++) for (int j = 0; j < 2; j++)
                {
                    var p = new Vector3(30 + i * 7, 6.3f, -28 - j * 12);
                    PH("mounted_fluorescent_lights", p, 0, new Vector3(1.4f, 0, 0), false, false);
                    Lamp(p + Vector3.down * .4f, new Color(.85f, .95f, 1f), 2.2f, 11f, i == 1 && j == 1, zoneB);
                }
            PH("korean_fire_extinguisher_01", new Vector3(47.5f, 0, -24), -90, new Vector3(0, .66f, 0), false);
            Flood(new Vector3(47.4f, 5f, -30), -90, zoneB); Flood(new Vector3(27.6f, 5f, -40), 90, zoneB);
            Puddle(new Vector3(36, 0, -30), 3, 2, -20);
        }

        // ------------------------------------------------------------------ V8 art pass
        static readonly Dictionary<string, Material> decalCache = new Dictionary<string, Material>();
        int decalOrder;
        // Flat textured quad. onWall: faces along `normal`; otherwise lies on the ground (image up → rotY direction).
        void Decal(string tex, Vector3 p, float rotY, Vector2 size, Vector3 normal = default)
        {
            if (!decalCache.TryGetValue(tex, out var m) || m == null)
            {
                m = Resources.Load<Material>("Materials/Decals/" + tex);
                if (m == null) return;
                decalCache[tex] = m;
            }
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(q.GetComponent<Collider>());   // immediately: the navmesh is baked in this same frame
            q.name = "贴花 " + tex;
            if (normal == default)
            {
                q.transform.position = new Vector3(p.x, .018f + (decalOrder++ % 12) * .0015f, p.z);
                q.transform.rotation = Quaternion.Euler(90, rotY, 0);
            }
            else
            {
                q.transform.position = p + normal * (.03f + (decalOrder++ % 6) * .004f);
                q.transform.rotation = Quaternion.LookRotation(-normal);
            }
            q.transform.localScale = new Vector3(size.x, size.y, 1);
            var r = q.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            q.transform.SetParent(transform, true);
        }

        void RoadDecals()
        {
            var rnd = new System.Random(21);
            // centre lines: yellow dashes on the long streets, white on the main street
            foreach (float z in new[] { 40f, -40f })
                for (float x = -66; x < 24; x += 6f) { if (x > 0 && x < 8) continue; Decal("PaintDashYellow", new Vector3(x, 0, z), 90, new Vector2(.35f, 3f)); }
            for (float x = -54; x < 12; x += 6f) Decal("PaintDashWhite", new Vector3(x, 0, 0), 90, new Vector2(.3f, 2.6f));
            // crosswalks at junctions
            Decal("Crosswalk", new Vector3(-21, 0, 8), 0, new Vector2(6f, 3f));
            Decal("Crosswalk", new Vector3(-33, 0, -8), 0, new Vector2(6f, 3f));
            Decal("Crosswalk", new Vector3(-52, 0, 0), 90, new Vector2(8f, 3f));
            Decal("Crosswalk", new Vector3(-64, 0, 31.5f), 0, new Vector2(7f, 3f));
            Decal("Crosswalk", new Vector3(-64, 0, -31.5f), 0, new Vector2(7f, 3f));
            // painted arrows toward the sites (route guidance)
            foreach (float x in new[] { -46f, -20f, 14f })
            {
                Decal("RoadLines029C", new Vector3(x, 0, 36.5f), 90, new Vector2(1.2f, 4.5f));
                Decal("RoadLines029C", new Vector3(x, 0, -36.5f), 90, new Vector2(1.2f, 4.5f));
            }
            Decal("RoadLines031C", new Vector3(8, 0, -4), 90, new Vector2(1.2f, 4.5f));
            Decal("RoadLines031C", new Vector3(8, 0, 4), 90, new Vector2(-1.2f, 4.5f));
            Decal("RoadLines035B", new Vector3(-21, 0, 4.5f), 0, new Vector2(1.6f, 2.6f));
            for (int i = -1; i <= 1; i++) Decal("RoadLines034C", new Vector3(-66, 0, i * 4 + 2), 90, new Vector2(1.6f, 2.4f));
            // wear: asphalt patches, oil, tyre tracks
            for (int i = 0; i < (ToonAssets.Available ? 0 : 26); i++)
            {
                float x = -66 + (float)rnd.NextDouble() * 120, z = (float)rnd.NextDouble() * 96 - 48;
                Decal("AsphaltPatch", new Vector3(x, 0, z), (float)rnd.NextDouble() * 360, Vector2.one * (2.5f + (float)rnd.NextDouble() * 4));
            }
            for (int i = 0; i < (ToonAssets.Available ? 0 : 18); i++)
            {
                float x = -66 + (float)rnd.NextDouble() * 120, z = (float)rnd.NextDouble() * 96 - 48;
                Decal("OilStain", new Vector3(x, 0, z), (float)rnd.NextDouble() * 360, Vector2.one * (1f + (float)rnd.NextDouble() * 2.2f));
            }
            foreach (var m in new[] { new Vector3(-30, 0, -2), new Vector3(-6, 0, 44), new Vector3(-40, 0, -44), new Vector3(20, 0, 0) })
                Decal("ManholeCover011", m, 0, Vector2.one * .9f);
        }

        // Rain streaks under roof lines and dirt along the base of street-facing walls.
        void WallGrime()
        {
            if (ToonAssets.Available) return;
            var rnd = new System.Random(33);
            void Run(Vector3 a, Vector3 b, float h, Vector3 n)
            {
                Vector3 d = b - a; float len = d.magnitude; d /= len;
                for (float t = 2.5f; t < len - 2f; t += 5.5f + (float)rnd.NextDouble() * 3f)
                {
                    float w = 3f + (float)rnd.NextDouble() * 3f, hh = Mathf.Min(h * .7f, 5f + (float)rnd.NextDouble() * 5f);
                    if (rnd.NextDouble() < .8) Decal("GrimeStreaks", a + d * t + Vector3.up * (h - hh / 2 - .1f), 0, new Vector2(w, hh), n);
                }
                for (float t = 4f; t < len; t += 8f) Decal("GrimeBase", a + d * Mathf.Min(t, len - 4f) + Vector3.up * .9f, 0, new Vector2(8.2f, 1.8f), n);
            }
            Run(new Vector3(-56, 0, 30), new Vector3(-24, 0, 30), 14, Vector3.forward);
            Run(new Vector3(-18, 0, 30), new Vector3(14, 0, 30), 16, Vector3.forward);
            Run(new Vector3(-56, 0, -30), new Vector3(-36, 0, -30), 13, Vector3.back);
            Run(new Vector3(-30, 0, -30), new Vector3(14, 0, -30), 15, Vector3.back);
            Run(new Vector3(30, 0, 9), new Vector3(44, 0, 9), 8, Vector3.forward);
            Run(new Vector3(30, 0, -9), new Vector3(44, 0, -9), 8, Vector3.back);
            Run(new Vector3(30, 0, -9), new Vector3(30, 0, 9), 8, Vector3.left);
            Run(new Vector3(14, 0, 34), new Vector3(26, 0, 34), 9, Vector3.forward);
            Run(new Vector3(14, 0, -34), new Vector3(26, 0, -34), 9, Vector3.back);
            Run(new Vector3(26, 0, 50), new Vector3(72, 0, 50), 20, Vector3.back);
            Run(new Vector3(26, 0, -50), new Vector3(72, 0, -50), 20, Vector3.forward);
            Run(new Vector3(-72, 0, -50), new Vector3(-72, 0, 50), 18, Vector3.right);
            Run(new Vector3(72, 0, -50), new Vector3(72, 0, 50), 18, Vector3.left);
            foreach (float z in new[] { 40f, -40f })
                foreach (var seg in new[] { (z - 10f, z - 3.4f), (z + 3.4f, z + 10f) })   // skip the gate opening
                {
                    Run(new Vector3(3.55f, 0, seg.Item1), new Vector3(3.55f, 0, seg.Item2), 4.6f, Vector3.left);
                    Run(new Vector3(4.45f, 0, seg.Item1), new Vector3(4.45f, 0, seg.Item2), 4.6f, Vector3.right);
                }
        }

        // Lockdown story props: checkpoints, vehicles, supply dumps, factory crane.
        void MilitaryDressing()
        {
            foreach (float z in new[] { 40f, -40f })
            {
                float s = Mathf.Sign(z);
                PH("wooden_military_crate", new Vector3(2.4f, 0, z + s * 4.2f), 90, new Vector3(0, .75f, 0));
                PH("portable_searchlight", new Vector3(2.4f, .75f, z + s * 4.2f), 90, new Vector3(0, .32f, 0), false);
                var spot = new GameObject("检查站探照灯").AddComponent<Light>(); spot.type = LightType.Spot; spot.spotAngle = 40; spot.range = 30; spot.intensity = 2.5f;
                spot.color = new Color(1f, .95f, .85f); spot.transform.position = new Vector3(2.2f, 1.9f, z + s * 4.2f); spot.transform.rotation = Quaternion.Euler(12, -90, 0); spot.transform.SetParent(transform, true);
                PH("ammo_box", new Vector3(6.2f, 0, z + s * 6.2f), 20, new Vector3(0, .32f, 0));
                PH("ammo_box", new Vector3(6.8f, 0, z + s * 6.0f), -10, new Vector3(0, .32f, 0));
                PH("ammo_box", new Vector3(6.5f, .32f, z + s * 6.1f), 5, new Vector3(0, .32f, 0), false);
                PH("metal_jerrycan_green", new Vector3(5.6f, 0, z + s * 7.4f), 30, new Vector3(0, .48f, 0), false);
                PH("old_military_compressor", new Vector3(6.4f, 0, z - s * 7.5f), 90, new Vector3(0, 1.1f, 0));
                PH("covered_car", new Vector3(-5, 0, z - s * 6.2f), 84, new Vector3(0, 1.6f, 0));
                Decal("OilStain", new Vector3(-5, 0, z - s * 6.2f), 0, new Vector2(3, 3));
            }
            // attacker staging: supply dump
            PH("wooden_military_crate", new Vector3(-69.5f, 0, -4), 90, new Vector3(0, .75f, 0));
            PH("ammo_box", new Vector3(-69.5f, .75f, -4.3f), 0, new Vector3(0, .32f, 0), false);
            PH("metal_jerrycan_green", new Vector3(-70.6f, 0, -2.6f), 15, new Vector3(0, .48f, 0), false);
            PH("wooden_ladder", new Vector3(-71.6f, 0, 5), 90, new Vector3(0, 3.2f, 0), false);
            // defender post
            PH("covered_car", new Vector3(62, 0, 15.5f), 90, new Vector3(0, 1.6f, 0));
            PH("industrial_pastic_container", new Vector3(69.5f, 0, -14), 0, new Vector3(0, 1.15f, 0));
            PH("industrial_pastic_container", new Vector3(69.5f, 0, -15.3f), 0, new Vector3(0, 1.15f, 0));
            // A cargo yard
            PH("industrial_pastic_container", new Vector3(45.6f, 0, 46.5f), 0, new Vector3(0, 1.15f, 0));
            PH("industrial_pastic_container", new Vector3(44.3f, 0, 46.5f), 0, new Vector3(0, 1.15f, 0));
            PH("industrial_pastic_container", new Vector3(45f, 1.15f, 46.5f), 8, new Vector3(0, 1.15f, 0), false);
            PH("hand_truck", new Vector3(41.5f, 0, 21f), 40, new Vector3(0, 1.2f, 0), false);
            PH("plastic_crate_03", new Vector3(28.4f, 0, 37f), 0, new Vector3(0, .35f, 0));
            PH("plastic_crate_03", new Vector3(28.4f, .35f, 37f), 12, new Vector3(0, .35f, 0), false);
            // B factory: overhead crane, work carts, hanging lamps
            PH("tool_cart", new Vector3(46.8f, 0, -29), -90, new Vector3(0, 1f, 0));
            PH("portable_welding_cart", new Vector3(29f, 0, -33), 90, new Vector3(0, 1.1f, 0));
            for (int i = 0; i < 3; i++) PH("hanging_industrial_lamp", new Vector3(31 + i * 6, 5.2f, -40), 0, new Vector3(0, 1.4f, 0), false, false);
            // street furniture
            foreach (var h in new[] { new Vector3(-44, .12f, 9.2f), new Vector3(-10, .12f, -9.2f), new Vector3(-40, .12f, 31.6f), new Vector3(-20, .12f, -31.6f), new Vector3(10, .12f, 48.8f) })
                PH("fire_hydrant", h, 0, new Vector3(0, .8f, 0));
            foreach (var w in new[] { (new Vector3(-56.2f, 3.2f, 6), 90f), (new Vector3(13.8f, 3.2f, -6), -90f), (new Vector3(47.7f, 3.4f, -28), -90f) })
                PH("industrial_wall_lamp", w.Item1, w.Item2, new Vector3(0, .5f, 0), false, false);
            PH("barrel_stove", new Vector3(-62.5f, 0, -24), 0, new Vector3(0, .9f, 0));
            var fire = new GameObject("火桶光").AddComponent<Light>(); fire.color = new Color(1f, .55f, .2f); fire.range = 6; fire.intensity = 1.6f;
            fire.transform.position = new Vector3(-62.5f, 1.3f, -24); fire.transform.SetParent(transform, true); fire.gameObject.AddComponent<Flicker>().baseIntensity = 1.6f;
            Steam(new Vector3(-62.5f, .9f, -24));
        }

        // ------------------------------------------------------------------ V6 tactical layout
        public struct Spot { public string label; public Vector3 pos; public int kind; }   // kind 0 狙击位 · 1 防守位 · 2 爆破点
        public readonly List<Spot> spots = new List<Spot>();
        readonly List<System.Action> afterNav = new List<System.Action>();

        // 混凝土 T 型墙（替代卡通路障）：2.4 m 全掩体
        void TWall(Vector3 p, float rotY)
        {
            var root = new GameObject("混凝土T墙"); root.transform.position = p; root.transform.rotation = Quaternion.Euler(0, rotY, 0);
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube); slab.transform.SetParent(root.transform, false);
            slab.transform.localPosition = new Vector3(0, 1.2f, 0); slab.transform.localScale = new Vector3(2.6f, 2.4f, .3f); slab.GetComponent<Renderer>().sharedMaterial = concrete;
            var foot = GameObject.CreatePrimitive(PrimitiveType.Cube); foot.transform.SetParent(root.transform, false);
            foot.transform.localPosition = new Vector3(0, .2f, 0); foot.transform.localScale = new Vector3(2.6f, .4f, 1.1f); foot.GetComponent<Renderer>().sharedMaterial = concrete;
            for (int i = -1; i <= 1; i += 2)
            {
                var hook = GameObject.CreatePrimitive(PrimitiveType.Cube); Kill(hook.GetComponent<Collider>()); hook.transform.SetParent(root.transform, false);
                hook.transform.localPosition = new Vector3(i * .8f, 2.45f, 0); hook.transform.localScale = new Vector3(.08f, .12f, .08f); hook.GetComponent<Renderer>().sharedMaterial = plate;
            }
            AddBlock(p, new Vector3(2.6f, 2.4f, .6f), rotY);
            root.transform.SetParent(transform, true);
        }

        void MilCrates(Vector3 p, float rotY)
        {
            PH("wooden_military_crate", p, rotY, new Vector3(0, .75f, 0));
            PH("wooden_military_crate", p + Vector3.up * .75f, rotY + 8, new Vector3(0, .75f, 0), false);
        }

        // 狙击台：3×3 m 钢平台（3.2 m 高）+ 正面混凝土胸墙 + 侧面坡道楼梯
        void SniperPerch(string label, Vector3 p, float faceY, Vector3 rampDir, float H = 3.2f, bool stairs = true)
        {
            var root = new GameObject("狙击台 " + label); root.transform.position = p; root.transform.SetParent(transform, true);
            Material steel = plate;
            var deck = Box("狙击台平台", p + Vector3.up * (H - .12f), new Vector3(3.2f, .24f, 3.2f), steel, false);
            deck.transform.SetParent(root.transform, true);
            for (int x = -1; x <= 1; x += 2) for (int z = -1; z <= 1; z += 2)
                    Box("狙击台立柱", p + new Vector3(x * 1.45f, H / 2 - .12f, z * 1.45f), new Vector3(.18f, H - .24f, .18f), steel, false).transform.SetParent(root.transform, true);
            for (int x = -1; x <= 1; x += 2)
                Box("狙击台斜撑", p + new Vector3(x * 1.45f, H / 2, 0), new Vector3(.1f, .1f, 3.6f), steel, false, 0, false).transform.localRotation = Quaternion.Euler(42, 0, 0);
            Quaternion face = Quaternion.Euler(0, faceY, 0);
            // chest-high parapet on the firing side + low sides
            var front = Box("狙击台胸墙", p + Vector3.up * (H + .5f) + face * new Vector3(0, 0, 1.45f), new Vector3(3.2f, 1f, .3f), concrete, false, faceY);
            front.transform.SetParent(root.transform, true);
            for (int i = -1; i <= 1; i += 2)
                Box("狙击台侧挡", p + Vector3.up * (H + .35f) + face * new Vector3(i * 1.45f, 0, .55f), new Vector3(.3f, .7f, 1.5f), concrete, false, faceY).transform.SetParent(root.transform, true);
            PH("wooden_military_crate", p + Vector3.up * H + face * new Vector3(-1.05f, 0, -1.05f), faceY, new Vector3(0, .5f, 0), false);
            if (!stairs)
            {
                // 无楼梯：只能踩弹射板上去
                var padGo = new GameObject("弹射板 " + label); padGo.transform.position = p + rampDir.normalized * 5.5f;
                var jp = padGo.AddComponent<JumpPad>(); jp.Build(new Color(1f, .45f, .3f)); jp.AimAt(p + Vector3.up * H);
                padGo.transform.SetParent(transform, true);
                Sign("弹射板 ↑ 狙击高台", p + rampDir.normalized * 1.75f + Vector3.up * 2.2f, Quaternion.LookRotation(rampDir).eulerAngles.y, new Color(1f, .5f, .3f), .45f);
                minimapBlocks.Add(new Rect(p.x - 1.6f, p.z - 1.6f, 3.2f, 3.2f));
                spots.Add(new Spot { label = "狙击位 · " + label + "（弹射板）", pos = p, kind = 0 });
                noBuild.Add(p + Vector3.up * H);
                return;
            }
            // ramp stairs (walkable slope ≈ 28°)
            rampDir.y = 0; rampDir.Normalize();
            float run = 6f, len = Mathf.Sqrt(run * run + H * H);
            Vector3 top = p + rampDir * 1.6f + Vector3.up * H, bottom = top + rampDir * run - Vector3.up * H;
            var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube); ramp.name = "狙击台楼梯";
            ramp.transform.position = (top + bottom) / 2 + Vector3.down * .1f;
            ramp.transform.rotation = Quaternion.LookRotation(bottom - top) ;
            ramp.transform.localScale = new Vector3(1.5f, .2f, len);
            ramp.GetComponent<Renderer>().sharedMaterial = steel; ramp.transform.SetParent(root.transform, true);
            for (int i = 1; i < 10; i++)
            {
                var step = GameObject.CreatePrimitive(PrimitiveType.Cube); Kill(step.GetComponent<Collider>());
                step.transform.position = Vector3.Lerp(top, bottom, i / 10f) + Vector3.up * .01f; step.transform.rotation = Quaternion.LookRotation(rampDir);
                step.transform.localScale = new Vector3(1.5f, .06f, .12f); step.GetComponent<Renderer>().sharedMaterial = iron; step.transform.SetParent(root.transform, true);
            }
            minimapBlocks.Add(new Rect(p.x - 1.6f, p.z - 1.6f, 3.2f, 3.2f));
            spots.Add(new Spot { label = "狙击位 · " + label, pos = p, kind = 0 });
            noBuild.Add(p + Vector3.up * H);
        }

        // 防守工事：三段混凝土路障组成 U 形，开口朝向守方
        void Bunker(string label, Vector3 p, float faceY)
        {
            Quaternion q = Quaternion.Euler(0, faceY, 0);
            PH("concrete_road_barrier", p + q * new Vector3(0, 0, 1.2f), faceY, new Vector3(0, 1.05f, 0));
            PH("concrete_road_barrier_02", p + q * new Vector3(-1.9f, 0, 0), faceY + 90, new Vector3(0, 1.05f, 0));
            PH("concrete_road_barrier_02", p + q * new Vector3(1.9f, 0, 0), faceY + 90, new Vector3(0, 1.05f, 0));
            PH("metal_jerrycan", p + q * new Vector3(-1f, 0, -.6f), faceY, new Vector3(0, .45f, 0), false);
            spots.Add(new Spot { label = "防守位 · " + label, pos = p, kind = 1 });
        }

        // 爆破点：黄黑警示边框 + 地面喷漆字
        void PlantPad(string letter, Vector3 c)
        {
            var paint = new Material(Shader.Find("Standard")); paint.color = new Color(.62f, .42f, .04f); paint.SetFloat("_Glossiness", .15f);
            var dark = new Material(Shader.Find("Standard")); dark.color = new Color(.08f, .08f, .08f); dark.SetFloat("_Glossiness", .1f);
            const float S = 4f;
            for (int i = 0; i < 4; i++)
            {
                Quaternion q = Quaternion.Euler(0, i * 90, 0);
                var edge = Glowbox("爆破点边框", c + Vector3.up * .02f + q * new Vector3(0, 0, S / 2), new Vector3(S + .3f, .015f, .3f), dark, i * 90);
                for (int k = 0; k < 6; k++)
                {
                    var st = Glowbox("警示条纹", c + Vector3.up * .03f + q * new Vector3(-S / 2 + .35f + k * .72f, 0, S / 2), new Vector3(.3f, .015f, .32f), paint, i * 90 + 45);
                }
            }
            FloorText("爆破点 " + letter, c + new Vector3(0, 0, -1.2f), 0, new Color(.95f, .75f, .2f), .9f);
            spots.Add(new Spot { label = "爆破点 " + letter, pos = c, kind = 2 });
        }

        void TacticalLayout()
        {
            // 狙击位：攻守双方各自控制一条长街的对枪线 + 攻方主街架点
            SniperPerch("北长街西（攻）", new Vector3(-68, 0, 46), 90, Vector3.back);
            SniperPerch("南长街西（攻）", new Vector3(-68, 0, -46), 90, Vector3.forward);
            SniperPerch("主街水塔（攻）", new Vector3(-45, 0, 7.6f), 90, Vector3.back, 6f, false);
            SniperPerch("A 长街（守）", new Vector3(23, 0, 47.5f), -90, Vector3.left);
            SniperPerch("B 长街（守）", new Vector3(23, 0, -47.5f), -90, Vector3.left);
            // 防守位：每个包点两处，面向主要进攻入口
            Bunker("A 点长街口", new Vector3(41, 0, 37), -100);
            Bunker("A 点连廊口", new Vector3(44, 0, 23.5f), -125);
            Bunker("B 点长街口", new Vector3(43.5f, 0, -38.5f), -80);
            Bunker("B 点连廊口", new Vector3(38, 0, -22.5f), 180);
            PlantPad("A", SiteA); PlantPad("B", SiteB);
            MarketRoof();
            Checkpoint("北铁门检查站", 40f);
            Checkpoint("南铁门检查站", -40f);
        }

        // 市场楼顶狙击位：8 m 高，四周矮墙，俯瞰整条夜市主街；只能从楼东侧弹射板上去
        void MarketRoof()
        {
            const float Y = 8f;
            for (int i = -1; i <= 1; i += 2)
            {
                Box("楼顶矮墙", new Vector3(37, Y + .5f, i * 8.85f), new Vector3(14, 1f, .3f), concrete, false);
                Box("楼顶矮墙", new Vector3(37 + i * 6.85f, Y + .5f, i * 4.5f), new Vector3(.3f, 1f, 8.7f), concrete, false);
                Box("楼顶矮墙", new Vector3(37 + i * 6.85f, Y + .5f, -i * 6.6f), new Vector3(.3f, 1f, 4.2f), concrete, false);   // west/east gaps → firing slots
            }
            Box("楼顶通风机", new Vector3(39, Y + .6f, 3), new Vector3(2, 1.2f, 2), plate, false);
            Box("楼顶水箱", new Vector3(35, Y + .9f, -4), new Vector3(1.6f, 1.8f, 1.6f), iron, false);
            PH("wooden_military_crate", new Vector3(32, Y, 5), 90, new Vector3(0, .75f, 0));
            var padGo = new GameObject("弹射板 市场楼顶"); padGo.transform.position = new Vector3(47.5f, 0, -2f);
            var jp = padGo.AddComponent<JumpPad>(); jp.Build(new Color(1f, .45f, .3f)); jp.AimAt(new Vector3(41f, Y, -2f), 3f);
            padGo.transform.SetParent(transform, true);
            Sign("弹射板 ↑ 楼顶狙击位", new Vector3(44.1f, 3.2f, -6.5f), 90, new Color(1f, .5f, .3f), .5f);
            spots.Add(new Spot { label = "狙击位 · 市场楼顶（弹射板）", pos = new Vector3(37, Y, 0), kind = 0 });
            noBuild.Add(new Vector3(37, Y, 0));
        }

        // 检查站：横跨整条长街的混凝土墙，中间一扇铁门 —— 不开门就过不去
        void Checkpoint(string label, float z)
        {
            const float X = 4f, gateW = 5f, wallH = 4.6f;
            float zMin = z > 0 ? 30f : -50f, zMax = z > 0 ? 50f : -30f;
            float g0 = z - gateW / 2 - .45f, g1 = z + gateW / 2 + .45f;   // opening incl. frame
            if (z > 0) Box(label + "隔离墙", new Vector3(X, wallH / 2, (zMin + g0) / 2), new Vector3(.8f, wallH, g0 - zMin), concrete);
            else Box(label + "隔离墙", new Vector3(X, wallH / 2, (g1 + zMax) / 2), new Vector3(.8f, wallH, zMax - g1), concrete);
            // 墙洞（被木箱堵住）：打碎木箱就能从侧面钻过去 —— 和开铁门二选一
            float zH = z + Mathf.Sign(z) * 7f, h0 = Mathf.Min(g1, zH) , h1 = Mathf.Max(g1, zH);
            float holeA = zH - .95f * Mathf.Sign(z), holeB = zH + .95f * Mathf.Sign(z);
            float far = z > 0 ? zMax : zMin, near = z > 0 ? g1 : g0;
            Box(label + "隔离墙", new Vector3(X, wallH / 2, (near + holeA) / 2), new Vector3(.8f, wallH, Mathf.Abs(holeA - near)), concrete);
            Box(label + "隔离墙", new Vector3(X, wallH / 2, (holeB + far) / 2), new Vector3(.8f, wallH, Mathf.Abs(far - holeB)), concrete);
            Box(label + "墙洞过梁", new Vector3(X, (2.3f + wallH) / 2, zH), new Vector3(.8f, wallH - 2.3f, 1.9f), concrete, false);
            // 动态物体在烘焙 NavMesh 之后再生成（否则会被烘成永久障碍）
            afterNav.Add(() =>
            {
                var hole = new GameObject(label + "墙洞木箱"); hole.transform.position = new Vector3(X, 0, zH); hole.transform.rotation = Quaternion.Euler(0, 90, 0);
                var dst = hole.AddComponent<Destructible>(); dst.Build(new Vector3(1.8f, 2.2f, .8f)); hole.transform.SetParent(transform, true); destructibles.Add(dst);
            });
            spots.Add(new Spot { label = "墙洞（打碎木箱可钻过）", pos = new Vector3(X, 0, zH), kind = 3 });
            Box(label + "门楣", new Vector3(X, wallH - .4f, z), new Vector3(1f, .8f, g1 - g0), concrete, false);
            // razor wire on top so it reads as "not climbable"
            for (float zz = zMin + 1; zz < zMax; zz += 1.2f)
                if (zz < g0 || zz > g1) { var w = Box("铁丝网", new Vector3(X, wallH + .25f, zz), new Vector3(.05f, .5f, .05f), iron, false, 0, false); }
            Box("铁丝网横线", new Vector3(X, wallH + .45f, (zMin + zMax) / 2), new Vector3(.04f, .04f, zMax - zMin), iron, false, 0, false);
            afterNav.Add(() =>
            {
                var go = new GameObject(label + "铁门"); go.name = label + "铁门";
                go.transform.position = new Vector3(X, 0, z); go.transform.rotation = Quaternion.Euler(0, 90, 0);   // slides toward +z/-z inside the wall
                var gate = go.AddComponent<Shutter>(); gate.BuildGate(gateW, 3.8f, plate); go.transform.SetParent(transform, true); shutters.Add(gate);
            });
            noBuild.Add(new Vector3(X, 3f, z));
            // guard booth + warning signs on both faces
            Box("岗亭", new Vector3(X - 2.2f, 1.3f, z - (z > 0 ? 6.5f : -6.5f)), new Vector3(2.2f, 2.6f, 2.2f), panels);   // away from the wall hole
            Sign(label + " · 长按 G 开启铁门", new Vector3(X - .45f, 4.9f, z), -90, new Color(1f, .4f, .2f), .5f);
            Sign(label + " · 长按 G 开启铁门", new Vector3(X + .45f, 4.9f, z), 90, new Color(1f, .4f, .2f), .5f);
            Lamp(new Vector3(X - 1.2f, 4.2f, z), new Color(1f, .7f, .45f), 1.6f, 9f);
            Lamp(new Vector3(X + 1.2f, 4.2f, z), new Color(1f, .7f, .45f), 1.6f, 9f);
        }

        public readonly List<BreakWall> breakWalls = new List<BreakWall>();
        void BreakableWall(string label, Vector3 basePos, float rotY, float width, float height)
        {
            minimapBlocks.Add(rotY == 0 ? new Rect(basePos.x - width / 2, basePos.z - .3f, width, .6f) : new Rect(basePos.x - .3f, basePos.z - width / 2, .6f, width));
            afterNav.Add(() =>
            {
                var go = new GameObject(label); go.transform.position = basePos; go.transform.rotation = Quaternion.Euler(0, rotY, 0);
                var w = go.AddComponent<BreakWall>();
                if (ToonAssets.Available) w.layers = CakeLayers();   // V17：一层层的蛋糕墙
                w.Build(width, height, .3f, plaster); go.transform.SetParent(transform, true);
                breakWalls.Add(w);
            });
        }

        Material[] cakeLayers;
        Material[] CakeLayers()
        {
            if (cakeLayers != null) return cakeLayers;
            Material M(Color c, float g) { var m = new Material(Shader.Find("Standard")); m.color = c; m.SetFloat("_Glossiness", g); return m; }
            var sponge = M(new Color(1f, .86f, .58f), .15f);
            return cakeLayers = new[] { sponge, M(new Color(1f, .97f, .92f), .5f), sponge, M(new Color(1f, .6f, .75f), .55f), sponge, M(new Color(.42f, .24f, .15f), .6f) };
        }

        void Flood(Vector3 p, float rotY, PowerBox zone)
        {
            PH("security_light", p, rotY, new Vector3(0, .6f, 0), false, false);
            var l = new GameObject("泛光灯").AddComponent<Light>();
            l.type = LightType.Spot; l.spotAngle = 95; l.range = 30; l.intensity = 3.2f; l.color = new Color(1f, .93f, .82f);
            l.transform.position = p + Quaternion.Euler(0, rotY, 0) * Vector3.forward * .4f;
            l.transform.rotation = Quaternion.Euler(38, rotY, 0);
            l.shadows = LightShadows.None; l.transform.SetParent(transform, true);
            if (zone != null) zone.Register(l);
        }

        void SiteMarker(string letter, Vector3 c, Color col)
        {
            var go = new GameObject("包点标识 " + letter);
            go.transform.position = c + Vector3.up * .03f; go.transform.rotation = Quaternion.Euler(90, 0, 0);
            var tm = go.AddComponent<TextMesh>();
            var font = Resources.Load<Font>("NotoSansSC");
            tm.font = font; tm.text = letter; tm.anchor = TextAnchor.MiddleCenter; tm.fontSize = 128; tm.characterSize = .12f;
            tm.color = new Color(col.r, col.g, col.b, .9f);
            if (font != null) go.GetComponent<MeshRenderer>().sharedMaterial = WorldText(font);
            go.transform.SetParent(transform, true);
            var ring = new GameObject("包点范围").AddComponent<LineRenderer>();
            ring.material = Fx.Additive; ring.startWidth = ring.endWidth = .14f; ring.startColor = ring.endColor = col * 1.4f;
            Skills.Circle(ring, c, Config.SiteRadius);
            ring.transform.SetParent(transform, true);
            Lamp(c + Vector3.up * 1f, col, 1.2f, 9f);
        }

        void Skyline()
        {
            var rnd = new System.Random(11);
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 12; i++)
                {
                    float x = -95 + i * 17 + (float)rnd.NextDouble() * 4;
                    float h = 30 + (float)rnd.NextDouble() * 55;
                    float z = side * (70 + (float)rnd.NextDouble() * 18);
                    GameObject b;
                    if (ToonAssets.Available && (b = Model("Toon/City/building_" + "ABCDEFGH"[(i + (side > 0 ? 3 : 0)) % 8] + "_withoutBase")) != null)
                    {
                        b.name = "远景楼宇"; Fit(b, new Vector3(13, 0, 0)); b.transform.localScale *= Mathf.Clamp(h / 20f, 1.2f, 2.6f);
                        var bb = b.GetComponentInChildren<Renderer>().bounds; foreach (var r in b.GetComponentsInChildren<Renderer>()) bb.Encapsulate(r.bounds);
                        b.transform.position = new Vector3(x, -bb.min.y + b.transform.position.y, z); b.transform.rotation = Quaternion.Euler(0, side > 0 ? 180 : 0, 0);
                        b.transform.SetParent(transform, true);
                    }
                    else b = Box("远景楼宇", new Vector3(x, h / 2, z), new Vector3(13, h, 14), skyline[i % skyline.Length], false, 0, false);
                    foreach (var sr in b.GetComponentsInChildren<Renderer>()) sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    for (int w = 0; w < 6; w++) rnd.NextDouble();   // keep the random sequence stable
                    Box("楼顶设备", new Vector3(x + 2, h + .9f, z), new Vector3(4, 1.8f, 3), plate, false, 0, false);
                }
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 6; i++)
                {
                    float z = -60 + i * 24, h = 35 + i * 7;
                    Box("远景楼宇", new Vector3(side * 95, h / 2, z), new Vector3(14, h, 16), skyline[(i + 2) % skyline.Length], false, 0, false)
                        .GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            // V8：远景只保留一块褪色的旧招牌
            Sign("霓城 · 旧港", new Vector3(-10, 26f, 61f), 180, new Color(.85f, .55f, .35f), 3.5f);
        }

        void Dynamic()
        {
            foreach (var late in afterNav) late(); afterNav.Clear();
            var sa = new GameObject("南小巷卷帘门").AddComponent<Shutter>(); sa.name = "南小巷卷帘门";
            sa.transform.position = new Vector3(-33, 0, -20); sa.Build(6f, 3.6f, plate); sa.transform.SetParent(transform, true); shutters.Add(sa);
            noBuild.Add(new Vector3(-33, 3.5f, -20));
            var a = new GameObject("A 连廊卷帘门").AddComponent<Shutter>();
            a.transform.position = new Vector3(20, 0, 14); a.Build(4f, 3.6f, plate); a.transform.SetParent(transform, true); shutters.Add(a);
            var b = new GameObject("B 连廊卷帘门").AddComponent<Shutter>();
            b.transform.position = new Vector3(20, 0, -14); b.Build(4f, 3.6f, plate); b.transform.SetParent(transform, true); shutters.Add(b);
            noBuild.Add(new Vector3(20, 3f, 14)); noBuild.Add(new Vector3(20, 3f, -14));

            var door = new GameObject("B点侧门").AddComponent<Door>();
            door.transform.position = new Vector3(29, 0, -18); door.Build(plate, amber); door.transform.SetParent(transform, true); doors.Add(door);
            var term = new GameObject("门禁终端").AddComponent<Terminal>();
            term.transform.position = new Vector3(32.4f, 0, -16.6f);
            term.door = door; term.Build(plate, amber); term.transform.SetParent(transform, true);
            noBuild.Add(new Vector3(29, 3f, -18));

            LightCover(new Vector3(-16, 0, -6), 0); LightCover(new Vector3(6, 0, 6.5f), 0);
            LightCover(new Vector3(36, 0, 30), 90); LightCover(new Vector3(36, 0, -30), 0);
            LightCover(new Vector3(-30, 0, 44), 0); LightCover(new Vector3(-28, 0, -44), 0);
            LightCover(new Vector3(20, 0, 2), 90);

            Barrel(new Vector3(-28, 0, 7.2f)); Barrel(new Vector3(40.5f, 0, 44.5f)); Barrel(new Vector3(33, 0, -44)); Barrel(new Vector3(-6, 0, -46.5f));
            Barrel(new Vector3(-4, 0, 46)); Barrel(new Vector3(46, 0, 19.2f)); Barrel(new Vector3(23, 0, -9));

            Med(new Vector3(-55.2f, 0, -6.5f), 90); Med(new Vector3(55.3f, 0, 14), -90);

            var pad = new GameObject("弹射板 A").AddComponent<JumpPad>();
            pad.transform.position = new Vector3(43, 0, 23); pad.Build(new Color(.3f, .9f, 1f)); pad.launch = new Vector3(0, 14.5f, 5.6f); pad.transform.SetParent(transform, true);
            var pad2 = new GameObject("弹射板 北街").AddComponent<JumpPad>();
            pad2.transform.position = new Vector3(-12, 0, 39); pad2.Build(new Color(1f, .3f, .8f)); pad2.launch = new Vector3(0, 14.5f, 4.2f); pad2.transform.SetParent(transform, true);

            Cam(new Vector3(47.4f, 4.6f, 47), 225); Cam(new Vector3(47.4f, 4.6f, -21), 225); Cam(new Vector3(27.5f, 5f, -1), 270);
        }

        // ------------------------------------------------------------------ V4 additions
        void Alleys()
        {
            // North alley x∈[-24,-18]: neon lanterns, pipes, AC units, holo sign, barrel
            Arch(new Vector3(-21, 0, 10.2f), 0, 6.4f, new Color(1f, .2f, .7f), "暗巷");
            Arch(new Vector3(-21, 0, 29.8f), 180, 6.4f, new Color(.2f, .9f, 1f), "北小巷");
            for (int i = 0; i < 4; i++)
            {
                float z = 13 + i * 4.5f;
                Glowbox("巷子灯笼线", new Vector3(-21, 5.2f, z), new Vector3(6, .04f, .04f), Glow(i % 2 == 0 ? new Color(1f, .3f, .5f) : new Color(1f, .7f, .3f), 2.5f));
                Lamp(new Vector3(-21, 4.6f, z), i % 2 == 0 ? new Color(1f, .35f, .6f) : new Color(1f, .7f, .35f), 1.6f, 7f);
            }
            Sign("面馆", new Vector3(-23.7f, 4f, 16), 90, new Color(1f, .5f, .2f), .7f, true);
            Sign("诊所", new Vector3(-18.3f, 4.5f, 22), -90, new Color(.3f, 1f, .6f), .7f, true);
            AC(new Vector3(-23.6f, 6.5f, 20), 90); AC(new Vector3(-18.4f, 7.5f, 14), -90); AC(new Vector3(-23.6f, 9f, 25), 90);
            PH("modular_industrial_pipes_01", new Vector3(-18.35f, 2f, 18), -90, new Vector3(0, 2.2f, 0), false, false);
            PH("metal_trash_can", new Vector3(-22.6f, 0, 26), 90, new Vector3(1.85f, 0, 0));
            MilCrates(new Vector3(-19.2f, 0, 12.5f), 0);
            Steam(new Vector3(-21, .02f, 18.5f));
            // South alley x∈[-36,-30] with a shutter at z=-20 (spawned in Dynamic)
            Arch(new Vector3(-33, 0, -10.2f), 180, 6.4f, new Color(.6f, .3f, 1f), "南小巷");
            Arch(new Vector3(-33, 0, -29.8f), 0, 6.4f, new Color(1f, .6f, .2f), "工厂后巷");
            Box("卷帘门框", new Vector3(-33, 4.15f, -20), new Vector3(6.4f, .9f, .5f), plate, false, 0, false);
            for (int i = 0; i < 3; i++)
            {
                float z = -13 - i * 6;
                Glowbox("巷子灯带", new Vector3(-35.9f, 3.2f, z), new Vector3(.05f, .05f, 4.5f), Glow(new Color(.6f, .3f, 1f), 3f));
                Lamp(new Vector3(-33, 4.4f, z), new Color(.7f, .45f, 1f), 1.6f, 8f);
            }
            AC(new Vector3(-35.6f, 5f, -15), 90); AC(new Vector3(-30.4f, 6.5f, -24), -90);
            PH("utility_box_02", new Vector3(-35.4f, 0, -26), 90, new Vector3(1.1f, 0, 0));
            PH("Barrel_01", new Vector3(-30.7f, 0, -13), 0, new Vector3(0, .9f, 0));
            Sign("地下赌场", new Vector3(-30.3f, 4.2f, -16), -90, new Color(1f, .2f, .5f), .6f, true);
            Barrel(new Vector3(-21.5f, 0, 23)); Barrel(new Vector3(-34.8f, 0, -23.5f));
        }

        void Arch(Vector3 p, float rotY, float width, Color c, string label)
        {
            Quaternion r = Quaternion.Euler(0, rotY, 0);
            for (int i = -1; i <= 1; i += 2)
                Box("霓虹拱门柱", p + r * new Vector3(i * width / 2, 2.6f, 0), new Vector3(.25f, 5.2f, .25f), plate, false, rotY);
            // V8：霓虹拱门改为军方管制钢架门楼
            Box("管制门楼横梁", p + Vector3.up * 5.3f, new Vector3(width + .3f, .45f, .35f), plate, false, rotY);
            Box("管制门楼横梁", p + Vector3.up * 4.85f, new Vector3(width + .3f, .12f, .25f), plate, false, rotY);
            Sign("军事管制区 · " + label, p + Vector3.up * 5.95f, rotY + 180, new Color(.95f, .75f, .35f), .5f);
        }

        void AC(Vector3 p, float rotY)
        {
            var root = new GameObject("空调外机"); root.transform.position = p; root.transform.rotation = Quaternion.Euler(0, rotY, 0);
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube); Kill(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false); body.transform.localScale = new Vector3(1.1f, .7f, .45f); body.transform.localPosition = new Vector3(0, 0, .23f);
            body.GetComponent<Renderer>().sharedMaterial = concrete;
            var fan = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Kill(fan.GetComponent<Collider>());
            fan.transform.SetParent(root.transform, false); fan.transform.localScale = new Vector3(.5f, .02f, .5f); fan.transform.localPosition = new Vector3(-.2f, 0, .46f); fan.transform.localRotation = Quaternion.Euler(90, 0, 0);
            fan.GetComponent<Renderer>().sharedMaterial = plate; fan.AddComponent<Spin>().speed = new Vector3(0, 0, 0);
            root.transform.SetParent(transform, true);
        }

        void Vending(Vector3 p, float rotY, Color c)
        {
            var b = Box("自动售货机", p + Vector3.up * 1f, new Vector3(1.1f, 2f, .8f), plate, true, rotY);
            Quaternion r = Quaternion.Euler(0, rotY, 0);
            Glowbox("售货机屏", p + Vector3.up * 1.15f + r * new Vector3(-.1f, 0, .41f), new Vector3(.7f, 1.5f, .02f), Glow(c, 1.6f), rotY);
            Glowbox("售货机灯条", p + Vector3.up * 1.98f + r * new Vector3(0, 0, .41f), new Vector3(1.1f, .06f, .03f), Glow(c, 3.5f), rotY);
            Lamp(p + Vector3.up * 1.2f + r * Vector3.forward * .9f, c, 1.2f, 4.5f);
        }

        void Billboard(Vector3 p, float rotY, Vector2 size, Color c, string text)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Kill(q.GetComponent<Collider>());
            q.name = "全息广告"; q.transform.position = p; q.transform.rotation = Quaternion.Euler(0, rotY + 180, 0); q.transform.localScale = new Vector3(size.x, size.y, 1);
            var m = new Material(holo != null ? holo : Fx.Additive); m.SetColor("_Color", c * .9f); m.SetFloat("_Lines", 6);
            q.GetComponent<Renderer>().sharedMaterial = m; q.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            q.AddComponent<HoloPulse>().baseColor = c;
            q.transform.SetParent(transform, true);
            Sign(text, p + Quaternion.Euler(0, rotY, 0) * Vector3.forward * .1f, rotY, c, size.y * .55f);
            Glowbox("广告支架", p - Vector3.up * (size.y / 2 + .1f), new Vector3(size.x, .1f, .1f), Glow(c, 2f), rotY);
        }

        void CyberDressing()
        {
            // skybridge over the main street (decorative, neon underside)
            Box("空中连廊", new Vector3(-8, 8.2f, 0), new Vector3(5, 1.4f, 20), concrete, false);
            // V7：连廊顶可站人 —— 两侧弹射板上去，矮栏杆做掩体，攻守都能抢的中路高点
            for (int i = -1; i <= 1; i += 2) Box("连廊栏杆", new Vector3(-8 + i * 2.4f, 9.2f, 0), new Vector3(.2f, .6f, 20), plate, false);
            Box("连廊沙袋", new Vector3(-8, 9.3f, -6), new Vector3(3, .8f, .6f), concrete, false); Box("连廊沙袋", new Vector3(-8, 9.3f, 6), new Vector3(3, .8f, .6f), concrete, false);
            foreach (var pad in new[] { (new Vector3(-14.5f, 0, -3), new Vector3(-8.5f, 8.9f, -3), "西"), (new Vector3(-1.5f, 0, 3), new Vector3(-7.5f, 8.9f, 3), "东") })
            {
                var pg = new GameObject("弹射板 空中连廊" + pad.Item3); pg.transform.position = pad.Item1;
                var jp = pg.AddComponent<JumpPad>(); jp.Build(new Color(1f, .45f, .3f)); jp.AimAt(pad.Item2); pg.transform.SetParent(transform, true);
            }
            spots.Add(new Spot { label = "狙击位 · 空中连廊（弹射板）", pos = new Vector3(-8, 8.9f, 0), kind = 0 });
            Glowbox("连廊灯带", new Vector3(-8, 7.45f, 0), new Vector3(4.6f, .05f, 19.6f), Glow(new Color(.2f, .9f, 1f), 1.6f));
            Glowbox("连廊窗光", new Vector3(-10.52f, 8.3f, 0), new Vector3(.03f, .7f, 18f), Glow(new Color(1f, .6f, .9f), 1.2f));
            Glowbox("连廊窗光", new Vector3(-5.48f, 8.3f, 0), new Vector3(.03f, .7f, 18f), Glow(new Color(1f, .6f, .9f), 1.2f));
            Sign("霓城快线", new Vector3(-8, 9.3f, -10.1f), 0, new Color(.2f, .9f, 1f), .9f);
            // neon arches over main entrances
            Arch(new Vector3(-53, 0, 0), 90, 14f, new Color(1f, .25f, .75f), "旧港夜市");
            Arch(new Vector3(12, 0, 0), -90, 14f, new Color(.2f, .9f, 1f), "中庭");
            Arch(new Vector3(-50, 0, 40), 90, 15f, new Color(1f, .6f, .2f), "北长街");
            Arch(new Vector3(-50, 0, -40), 90, 15f, new Color(.6f, .3f, 1f), "南工厂通道");
            // holographic billboards on rooftops
            // vending machines & AC units along the streets
            Vending(new Vector3(-36, 0, 8.6f), 180, new Color(.2f, .9f, 1f)); Vending(new Vector3(-16, 0, 8.6f), 180, new Color(1f, .3f, .7f));
            Vending(new Vector3(-44, 0, -8.6f), 0, new Color(1f, .6f, .2f)); Vending(new Vector3(4, 0, -8.6f), 0, new Color(.3f, 1f, .6f));
            Vending(new Vector3(-44, 0, 31.6f), 0, new Color(1f, .3f, .7f)); Vending(new Vector3(-8, 0, -31.6f), 180, new Color(.2f, .9f, 1f));
            Vending(new Vector3(50.5f, 0, 20), -90, new Color(.6f, .3f, 1f));
            foreach (var x in new[] { -50f, -40f, -12f, 2f }) { AC(new Vector3(x, 6f + (x % 3), 10.1f), 180); AC(new Vector3(x + 4, 5.5f, -10.1f), 0); }
            // ground neon guide lines along the main street edges
            // V8：取消地面霓虹线（改为路面标线贴花）
            // flying cars & searchlights
            var rnd = new System.Random(5);
            for (int i = 0; i < 0; i++)   // V6：写实风取消悬浮车
            {
                var car = new GameObject("悬浮车").AddComponent<FlyingCar>();
                car.center = new Vector3((float)rnd.NextDouble() * 60 - 30, 22 + (float)rnd.NextDouble() * 18, (float)rnd.NextDouble() * 40 - 20);
                car.radius = 55 + (float)rnd.NextDouble() * 35; car.speed = (rnd.NextDouble() < .5 ? 1 : -1) * (.08f + (float)rnd.NextDouble() * .08f);
                car.phase = (float)rnd.NextDouble() * 6.28f; car.color = i % 3 == 0 ? new Color(1f, .2f, .7f) : i % 3 == 1 ? new Color(.2f, .9f, 1f) : new Color(1f, .7f, .3f);
                car.Build(plate); car.transform.SetParent(transform, true);
            }
            // V6：取消探照光柱
        }

        void FloorText(string text, Vector3 p, float rotY, Color c, float size)
        {
            var go = new GameObject("地面区域名 " + text);
            go.transform.position = new Vector3(p.x, .03f, p.z); go.transform.rotation = Quaternion.Euler(90, rotY, 0);
            var tm = go.AddComponent<TextMesh>(); var font = Resources.Load<Font>("NotoSansSC");
            tm.font = font; tm.text = text; tm.anchor = TextAnchor.MiddleCenter; tm.fontSize = 96; tm.characterSize = .06f * size;
            tm.color = new Color(c.r, c.g, c.b, .55f);
            if (font != null) go.GetComponent<MeshRenderer>().sharedMaterial = WorldText(font);
            go.transform.SetParent(transform, true);
        }

        // Painted floor callouts + overhead direction signs at every junction.
        void ZoneMarkings()
        {
            FloorText("夜市主街", new Vector3(-38, 0, -4.2f), 90, new Color(1f, .3f, .7f), 1.6f);
            FloorText("北长街", new Vector3(-30, 0, 34.5f), 90, new Color(1f, .6f, .2f), 1.6f);
            FloorText("南工厂通道", new Vector3(-30, 0, -34.5f), 90, new Color(.6f, .35f, 1f), 1.6f);
            FloorText("北小巷", new Vector3(-21, 0, 20), 0, new Color(1f, .3f, .7f), 1f);
            FloorText("南小巷", new Vector3(-33, 0, -14), 180, new Color(.7f, .4f, 1f), 1f);
            FloorText("中庭", new Vector3(21, 0, -4), 90, new Color(.3f, 1f, .9f), 1.4f);
            FloorText("A 连廊", new Vector3(20, 0, 19), 0, new Color(1f, .55f, .2f), 1f);
            FloorText("B 连廊", new Vector3(20, 0, -19), 180, new Color(.3f, .8f, 1f), 1f);
            FloorText("中央市场", new Vector3(50, 0, 0), -90, new Color(.3f, 1f, .9f), 1.2f);
            // direction signs (face the approaching attacker)
            Sign("北长街 ↑   中庭 →   南工厂 ↓", new Vector3(-55.6f, 6.2f, 0), 90, new Color(.3f, 1f, .9f), .6f);
            Sign("← 主街   A 点 →", new Vector3(-21, 6.4f, 30.4f), 0, new Color(1f, .6f, .2f), .55f);
            Sign("A 连廊 ↖   B 连廊 ↙   中央市场 →", new Vector3(13.6f, 6.2f, 0), -90, new Color(.3f, 1f, .9f), .55f);
            Sign("A 点 →", new Vector3(24, 6f, 40), -90, new Color(1f, .5f, .2f), .9f);
            Sign("B 点 →", new Vector3(24, 6f, -40), -90, new Color(.3f, .8f, 1f), .9f);
            Sign("← 主街   B 点 →", new Vector3(-33, 6.2f, -30.4f), 180, new Color(.7f, .4f, 1f), .55f);
            Sign("A 点 ↑", new Vector3(20, 4.8f, 23.6f), 180, new Color(1f, .5f, .2f), .7f);
            Sign("B 点 ↓", new Vector3(20, 4.8f, -23.6f), 0, new Color(.3f, .8f, 1f), .7f);
        }

        void Searchlight(Vector3 p, Color c)
        {
            var go = new GameObject("探照灯"); go.transform.position = p;
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Kill(beam.GetComponent<Collider>());
            beam.transform.SetParent(go.transform, false); beam.transform.localPosition = new Vector3(0, 40, 0); beam.transform.localScale = new Vector3(4, 40, 4);
            var m = new Material(holo != null ? holo : Fx.Additive); m.SetColor("_Color", c * .25f); m.SetFloat("_Lines", 0);
            beam.GetComponent<Renderer>().sharedMaterial = m; beam.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<Sweep>(); go.transform.SetParent(transform, true);
        }

        void LightCover(Vector3 p, float rotY)
        {
            var go = new GameObject("轻型可破坏掩体");
            go.transform.position = p; go.transform.rotation = Quaternion.Euler(0, rotY, 0);
            var d = go.AddComponent<Destructible>(); d.Build(new Vector3(1.8f, 1.3f, 1f));
            go.transform.SetParent(transform, true); destructibles.Add(d);
        }

        void Barrel(Vector3 p)
        {
            var go = new GameObject("爆炸桶"); go.transform.position = p;
            var b = go.AddComponent<ExplosiveBarrel>(); b.Build(); go.transform.SetParent(transform, true); barrels.Add(b);
        }

        void Med(Vector3 p, float rotY)
        {
            var go = new GameObject("医疗站"); go.transform.position = p; go.transform.rotation = Quaternion.Euler(0, rotY, 0);
            go.AddComponent<MedStation>().Build(); go.transform.SetParent(transform, true);
        }

        void Cam(Vector3 p, float yaw)
        {
            var go = new GameObject("守方监控"); go.transform.position = p;
            var c = go.AddComponent<SecurityCam>(); c.yawCenter = yaw; c.Build(); go.transform.SetParent(transform, true); cams.Add(c);
        }

        void BuildNavMesh()
        {
            var sources = new List<NavMeshBuildSource>();
            var bounds = new Bounds(new Vector3(0, 4, 0), new Vector3(HalfX * 2 + 2, 16, HalfZ * 2 + 2));
            NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = .42f; settings.agentHeight = 1.8f; settings.agentClimb = .45f; settings.agentSlope = 40;
            settings.overrideVoxelSize = true; settings.voxelSize = .16f;
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (data != null) { navInstance = NavMesh.AddNavMeshData(data); navReady = true; }
        }

        NavMeshDataInstance navInstance;
        void OnDestroy() { if (navInstance.valid) navInstance.Remove(); }

        void DefineRoutes()
        {
            routes["A长街"] = new[] { new Vector3(-64, 0, 14), new Vector3(-64, 0, 40), new Vector3(-30, 0, 40), new Vector3(1, 0, 40), new Vector3(7, 0, 40), new Vector3(22, 0, 41), new Vector3(30, 0, 37), SiteA };
            routes["A连廊"] = new[] { new Vector3(-52, 0, 4), new Vector3(-20, 0, 4.5f), new Vector3(10, 0, 4), new Vector3(20, 0, 11), new Vector3(20, 0, 17), new Vector3(28, 0, 21), SiteA };
            routes["中路A"] = new[] { new Vector3(-52, 0, -4), new Vector3(-10, 0, -4.5f), new Vector3(16, 0, -2), new Vector3(27, 0, 12), new Vector3(29, 0, 20), SiteA };
            routes["B工厂"] = new[] { new Vector3(-64, 0, -14), new Vector3(-64, 0, -40), new Vector3(-30, 0, -40), new Vector3(1, 0, -40), new Vector3(7, 0, -40), new Vector3(22, 0, -40), new Vector3(30, 0, -38), SiteB };
            routes["A长街墙洞"] = new[] { new Vector3(-64, 0, 14), new Vector3(-64, 0, 40), new Vector3(-30, 0, 40), new Vector3(-4, 0, 46.5f), new Vector3(1, 0, 47), new Vector3(7.5f, 0, 47.5f), new Vector3(22, 0, 44), new Vector3(30, 0, 37), SiteA };
            routes["B工厂墙洞"] = new[] { new Vector3(-64, 0, -14), new Vector3(-64, 0, -40), new Vector3(-30, 0, -40), new Vector3(-4, 0, -46.5f), new Vector3(1, 0, -47), new Vector3(7.5f, 0, -47.5f), new Vector3(22, 0, -44), new Vector3(30, 0, -38), SiteB };
            routes["B连廊"] = new[] { new Vector3(-52, 0, -4), new Vector3(-10, 0, -4.5f), new Vector3(10, 0, -4), new Vector3(20, 0, -11), new Vector3(20, 0, -17), new Vector3(28, 0, -21), SiteB };
            routes["北小巷A"] = new[] { new Vector3(-52, 0, 5), new Vector3(-21, 0, 6.5f), new Vector3(-21, 0, 20), new Vector3(-21, 0, 38), new Vector3(1, 0, 40), new Vector3(7, 0, 40), new Vector3(22, 0, 41), new Vector3(30, 0, 37), SiteA };
            routes["南小巷B"] = new[] { new Vector3(-52, 0, -5), new Vector3(-33, 0, -6.5f), new Vector3(-33, 0, -18), new Vector3(-33, 0, -24), new Vector3(-33, 0, -38), new Vector3(1, 0, -40), new Vector3(7, 0, -40), new Vector3(22, 0, -40), new Vector3(30, 0, -38), SiteB };
            routes["中路B"] = new[] { new Vector3(-52, 0, 4), new Vector3(-10, 0, 4.5f), new Vector3(16, 0, 2), new Vector3(27, 0, -12), new Vector3(29, 0, -16), new Vector3(29, 0, -21), SiteB };
            anchors[0] = ToonAssets.Available
                ? new[] { new Vector3(42.5f, 0, 37.3f), new Vector3(45.3f, 0, 24.5f), new Vector3(33, 0, 30), new Vector3(30, 0, 36), new Vector3(33.5f, 2f, 26f) }   // V17：A 点蛋糕方块顶
                : new[] { new Vector3(42.5f, 0, 37.3f), new Vector3(45.3f, 0, 24.5f), new Vector3(33, 0, 30), new Vector3(30, 0, 36) };
            anchors[1] = new[] { new Vector3(44.8f, 0, -38.3f), new Vector3(38, 0, -21.3f), new Vector3(33, 0, -30), new Vector3(31, 0, -42) };
            anchors[2] = new[] { new Vector3(26, 0, 8), new Vector3(26, 0, -8), new Vector3(28, 0, 0) };
        }

        public Vector3 SpawnPoint(bool attacker, int slot)
        {
            float x = attacker ? -64f : 64f;
            return new Vector3(x + (attacker ? -1 : 1) * (slot == 1 ? 2 : 0), .05f, (slot - 1) * 3.2f);
        }

        public void SetSpawnBarriers(bool on)
        {
            if (spawnBarriers.Count == 0 && on)
            {
                spawnBarriers.Add(Barrier(new Vector3(-56, 2, 0), new Vector3(.2f, 4, 20)));
                spawnBarriers.Add(Barrier(new Vector3(-64, 2, 10), new Vector3(7.5f, 4, .2f)));
                spawnBarriers.Add(Barrier(new Vector3(-64, 2, -10), new Vector3(7.5f, 4, .2f)));
                spawnBarriers.Add(Barrier(new Vector3(56, 2, 0), new Vector3(.2f, 4, 6)));
                spawnBarriers.Add(Barrier(new Vector3(63.75f, 2, 10), new Vector3(7.5f, 4, .2f)));
                spawnBarriers.Add(Barrier(new Vector3(63.75f, 2, -10), new Vector3(7.5f, 4, .2f)));
            }
            foreach (var b in spawnBarriers) b.SetActive(on);
        }

        GameObject Barrier(Vector3 p, Vector3 size)
        {
            var b = Box("出生区屏障", p, size, holo != null ? holo : cyan, false);
            b.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return b;
        }

        public void ResetRound()
        {
            foreach (var d in destructibles) d.Restore();
            foreach (var door in doors) door.Close();
            foreach (var i in Interactable.All.ToArray()) i.ResetRound();
            foreach (var b in barrels) b.ResetRound();
            foreach (var g in glass) g.ResetRound();
            foreach (var c in cams) c.ResetRound();
            foreach (var w in breakWalls) w.ResetRound();
            foreach (var sp in IngredientSpot.All.ToArray()) sp.ResetRound();
            foreach (var ip in IngredientPickup.Live.ToArray()) if (ip != null && ip.spot == null) Destroy(ip.gameObject);
            foreach (var dc in DeliveryCrate.Live.ToArray()) if (dc != null) Destroy(dc.gameObject);
        }

        public bool IsBuildLegal(Vector3 p)
        {
            if (Mathf.Abs(p.x) > HalfX - 1 || Mathf.Abs(p.z) > HalfZ - 1) return false;
            foreach (var n in noBuild)
                if (new Vector2(p.x - n.x, p.z - n.z).magnitude < n.y) return false;
            return true;
        }

        public static int SiteAt(Vector3 p)
        {
            if (new Vector2(p.x - SiteA.x, p.z - SiteA.z).magnitude <= Config.SiteRadius && p.y < 2.5f) return 0;
            if (new Vector2(p.x - SiteB.x, p.z - SiteB.z).magnitude <= Config.SiteRadius && p.y < 2.5f) return 1;
            return -1;
        }
        public static Vector3 SiteCenter(int s) { return s == 0 ? SiteA : SiteB; }
    }

    public class HoloPulse : MonoBehaviour
    {
        public Color baseColor; Material m; float seed;
        void Start() { m = GetComponent<Renderer>().sharedMaterial; seed = Random.value * 10; }
        void Update()
        {
            float glitch = Mathf.PerlinNoise(seed, Time.time * 4f) > .85f ? .2f : 1f;
            m.SetColor("_Color", baseColor * (.6f + .25f * Mathf.Sin(Time.time * 2 + seed)) * glitch);
        }
    }

    public class FlyingCar : MonoBehaviour
    {
        public Vector3 center; public float radius, speed, phase; public Color color;
        public void Build(Material body)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube); Arena.Kill(b.GetComponent<Collider>());
            b.transform.SetParent(transform, false); b.transform.localScale = new Vector3(1.8f, .7f, 4.2f); b.GetComponent<Renderer>().sharedMaterial = body;
            var under = GameObject.CreatePrimitive(PrimitiveType.Cube); Arena.Kill(under.GetComponent<Collider>());
            under.transform.SetParent(transform, false); under.transform.localPosition = new Vector3(0, -.38f, 0); under.transform.localScale = new Vector3(1.6f, .05f, 3.8f);
            under.GetComponent<Renderer>().sharedMaterial = Arena.Glow(color, 4f);
            for (int i = -1; i <= 1; i += 2)
            {
                var head = GameObject.CreatePrimitive(PrimitiveType.Cube); Arena.Kill(head.GetComponent<Collider>());
                head.transform.SetParent(transform, false); head.transform.localPosition = new Vector3(i * .6f, 0, 2.11f); head.transform.localScale = new Vector3(.4f, .12f, .05f);
                head.GetComponent<Renderer>().sharedMaterial = Arena.Glow(new Color(1f, .95f, .85f), 6f);
                var tail = GameObject.CreatePrimitive(PrimitiveType.Cube); Arena.Kill(tail.GetComponent<Collider>());
                tail.transform.SetParent(transform, false); tail.transform.localPosition = new Vector3(i * .6f, 0, -2.11f); tail.transform.localScale = new Vector3(.4f, .12f, .05f);
                tail.GetComponent<Renderer>().sharedMaterial = Arena.Glow(new Color(1f, .1f, .1f), 5f);
            }
            foreach (var r in GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        void Update()
        {
            float a = phase + Time.time * speed;
            Vector3 p = center + new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a * 2.3f) * 2f, Mathf.Sin(a) * radius * .6f);
            Vector3 dir = p - transform.position;
            transform.position = p;
            if (dir.sqrMagnitude > .0001f) transform.rotation = Quaternion.LookRotation(dir);
        }
    }

    public class Sweep : MonoBehaviour
    {
        float seed;
        void Start() { seed = Random.value * 10; }
        void Update() { transform.rotation = Quaternion.Euler(Mathf.Sin(Time.time * .3f + seed) * 25f, Time.time * 12f + seed * 30, 15f); }
    }

    // Light rain that follows the camera.
    public class Rain : MonoBehaviour
    {
        Transform t;
        void Start()
        {
            var go = new GameObject("雨"); t = go.transform;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.startLifetime = 1.2f; main.startSpeed = 22f; main.startSize = .025f; main.maxParticles = 2500;
            main.startColor = new Color(.6f, .7f, .85f, .35f); main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 1600;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(40, 40, 1);
            var r = go.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.lengthScale = 4f; r.velocityScale = .04f;
            r.material = Fx.Additive; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        void LateUpdate()
        {
            var cam = Camera.main; if (cam == null || t == null) return;
            t.position = cam.transform.position + Vector3.up * 14f + cam.transform.forward * 8f;
            t.rotation = Quaternion.Euler(90, 0, 0);
        }
    }
}
