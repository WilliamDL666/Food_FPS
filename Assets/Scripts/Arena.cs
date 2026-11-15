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
        public const float LinkX0 = -5f, LinkX1 = 1f;          // V22 第二期：北烤串巷 / 南后厨 x 范围（6 m 宽）
        public const float CreamTop = 3.5f, RoofTop = 8f;      // 奶油平台台面 / 市场楼顶能站的高度（楼顶板只有外观没有碰撞，人站在楼体顶面 8 m）
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
            // V22 第二期（方案甲）：两条中段连接 + 中段十字 + 奶油平台
            new Zone { name = "北烤串巷", hint = "主街 ↔ 北长街中段 · 外卖阳台 3.5 m", area = Rect.MinMaxRect(-5, 10, 1, 30), color = new Color(1f, .55f, .35f) },
            new Zone { name = "南后厨", hint = "主街 ↔ 南长街中段 · 室内厨房 · 蒸汽阀", area = Rect.MinMaxRect(-5, -30, 1, -10), color = new Color(1f, .7f, .5f) },
            new Zone { name = "北关口 · 蒸笼门", hint = "默认开着 · 守方长按 G 盖上（巨响）· 墙洞可钻", area = Rect.MinMaxRect(0, 30, 14, 50), color = new Color(1f, .7f, .35f) },
            new Zone { name = "南关口 · 蒸笼门", hint = "默认开着 · 守方长按 G 盖上（巨响）· 火锅热气", area = Rect.MinMaxRect(0, -50, 14, -30), color = new Color(1f, .7f, .35f) },
            new Zone { name = "市场楼顶", hint = "高台 · 东侧楼梯 / 弹射板上来 · 横幅挡住主街西段", area = Rect.MinMaxRect(30, -9, 44, 9), color = new Color(.95f, .35f, .35f) },
            new Zone { name = "中庭 · 奶油平台", hint = "3.5 m 中层 · 西坡道 / 东楼梯 · 俯看两个连廊口", area = Rect.MinMaxRect(10, -6, 24, 6), color = new Color(1f, .7f, .85f) },
            new Zone { name = "中段十字", hint = "空中连廊下 · 北烤串巷 ↑ / 南后厨 ↓", area = Rect.MinMaxRect(-12, -10, 4, 10), color = new Color(1f, .4f, .6f) },
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
            // V22 第二期：组件（主要是"只要外观、不要碰撞"的碰撞体）立即销毁 —— NavMesh 在 Build() 同一帧烘焙，
            // 延迟销毁会让酱料圆片的胶囊碰撞（半径 = 长轴一半）等在 NavMesh 上留下看不见的"圆包"
            if (Application.isPlaying && !(o is Component)) Object.Destroy(o); else Object.DestroyImmediate(o);
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
            V22Buildings();    // V22 第二期：寿司吧 / 糖果铺 / 冰柜仓（长街错位建筑）+ 东通道奶茶亭
            Spawns();
            MidStreet();
            NorthStreet();
            SouthStreet();
            Plaza();
            PlazaV22();        // V22 第二期：中庭牌楼横幅 + 奶油平台
            SiteAArea();
            SiteBArea();
            Skyline();
            Alleys();
            KebabAlley();      // V22 第二期：北烤串巷（外卖阳台）
            SouthKitchen();    // V22 第二期：南后厨（室内厨房）
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
            Food("HotPot", new Vector3(-11, 16.6f, 20), 0, 5f);   // V22：原 x=-2 正好在北烤串巷上空
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
            // V22 第二期：北关口关东煮锅 —— 和南路火锅对称（热气挡视线、站进去烫），正面朝西边的蒸笼门
            {
                var hp = new Vector3(12.5f, 0, 44f);
                ClimbFood("HotPot", hp, .9f, 30, new Vector2(1.5f, 1.0f));
                var hz = new GameObject("关东煮烫伤区"); hz.transform.position = hp; hz.AddComponent<HotpotHazard>(); hz.transform.SetParent(transform, true);
                SteamCloud.particles.Add(BigSteam(hp + Vector3.up * 1.2f, 2.2f, .5f));
                SteamCloud.Add(hp + Vector3.up * 2.6f, 2.6f);
                Sign("关东煮", hp + new Vector3(-1.6f, 3.6f, 0), -90, new Color(1f, .7f, .35f), .6f);
            }
            // V22 第二期：蒸汽阀 ×3（中庭南北各一、南后厨一个）—— 长按 G 喷 6 s 白雾挡视线，冷却 25 s，谁都能用
            Valve(new Vector3(16, 0, 8), 180); Valve(new Vector3(16, 0, -8), 0); Valve(new Vector3(.35f, 0, -21f), -90);
            // V18：食材刷新点（V22：南关口那个原来在 (2, -34)，正好在岗亭里，AI 会一直去够它 → 挪到岗亭和门洞之间）
            foreach (var sp in new[] { new Vector3(-45, 0, -4), new Vector3(-30, 0, 5.2f), new Vector3(-13.5f, 0, -4.5f), new Vector3(14, 0, 0), new Vector3(24, 0, -7.5f),
                new Vector3(-52, 0, 40), new Vector3(-22, 0, 44), new Vector3(2, 0, 36), new Vector3(-52, 0, -39.5f), new Vector3(-24, 0, -41), new Vector3(1.6f, 0, -36.3f),
                new Vector3(24, 0, -36), new Vector3(39, 0, 22.5f), new Vector3(40, 0, -30), new Vector3(-21, 0, 14), new Vector3(-33, 0, -14), new Vector3(52.5f, 0, -8), new Vector3(-60, 0, 14) })
            { var g = new GameObject("食材刷新点"); g.transform.position = sp; g.AddComponent<IngredientSpot>(); g.transform.SetParent(transform, true); }
            // V18：厨房设施
            void Station(KitchenStation.Kind k, Vector3 p, float rotY) { var g = new GameObject("厨房 " + k); g.transform.position = p; g.transform.rotation = Quaternion.Euler(0, rotY, 0); g.AddComponent<KitchenStation>().Build(k); g.transform.SetParent(transform, true); }
            Station(KitchenStation.Kind.HotpotHeal, new Vector3(-19, 0, -8.6f), 0);   // V22：原 (-26,-8.3) 让给外摆棚 Station(KitchenStation.Kind.HotpotHeal, new Vector3(53, 0, -14), 0); Station(KitchenStation.Kind.HotpotHeal, new Vector3(-36, 0, 33.6f), 0);
            Station(KitchenStation.Kind.MilkTea, new Vector3(-58.5f, 0, 8.5f), 180); Station(KitchenStation.Kind.MilkTea, new Vector3(58.5f, 0, -8.5f), 0);
            Station(KitchenStation.Kind.Oven, new Vector3(-4.25f, 0, -19.5f), 90); Station(KitchenStation.Kind.Freezer, new Vector3(16, 0, 12.4f), 180);   // V22：烤箱从中庭挪进南后厨
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
            foreach (float tx in new[] { -63f, -45f, -38f, -14f, 6f, 30f, 52f, 60f })   // V22：x=44 那棵会被 A 点北侧方块盖住
                foreach (float tz in new[] { 49.4f, -49.4f })
                {
                    var tr = Model("Toon/Kitchen/CandyTree"); if (tr == null) break;
                    foreach (var c in tr.GetComponentsInChildren<Collider>()) Kill(c);
                    Fit(tr, new Vector3(0, 4.2f, 0)); var hold = new GameObject("糖果树"); tr.transform.SetParent(hold.transform, false);
                    hold.transform.position = new Vector3(tx, .12f, tz); hold.transform.rotation = Quaternion.Euler(0, tz > 0 ? 180 : 0, 0); hold.transform.SetParent(transform, true);
                }
            // V19：路边一堆堆彩色糖粒
            foreach (var sp in new[] { new Vector3(-48, .02f, 8.4f), new Vector3(-6.3f, .02f, -8.4f), new Vector3(-56, .02f, 31.4f), new Vector3(-30, .02f, -31.4f), new Vector3(6.4f, .02f, 34f), new Vector3(30, .02f, -6f), new Vector3(48.2f, .02f, 6f), new Vector3(-8, .02f, 48.6f), new Vector3(-40, .02f, -48.6f), new Vector3(20, .02f, 48.6f) })   // V22：三堆避开外摆棚 / 冰柜仓 / 楼顶楼梯
            {
                var pile = Model("Toon/Kitchen/SprinklePile"); if (pile == null) break;
                foreach (var c in pile.GetComponentsInChildren<Collider>()) Kill(c);
                Fit(pile, new Vector3(1.4f, 0, 0)); pile.transform.position += sp; pile.transform.rotation = Quaternion.Euler(0, sp.x * 13, 0); pile.transform.SetParent(transform, true);
            }
            // 出生区顶棚招牌
            Food("Donut", new Vector3(-64, 4.45f, 0), 90, 3.2f);
            Food("MilkTea", new Vector3(64, 4.45f, 0), 0, 3.2f);
            // 火锅冒热气
            foreach (var hp in new[] { new Vector3(-11, 21.4f, 20), new Vector3(30, 28.4f, 56), new Vector3(-40, 28.4f, -55) }) BigSteam(hp, 2f, .25f);
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

        // ================================================================== V22 第二期：方案甲（三路 + 中段十字 + 中庭双层）
        // 设计稿：Docs/设计稿_V22_地图重构与干员地图联动.md §3.1 / §3.4。全部用程序化方块 + 糖果材质 + 奶油滴边，和 V19 萌化风格一致。
        // 注意：生成顺序必须确定（烘焙按 Arena 下渲染器顺序匹配），这里不用随机数。
        static readonly Dictionary<string, Material> v22Mats = new Dictionary<string, Material>();
        Material Candy(string key, Color c, float gloss = .35f)
        {
            if (v22Mats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Shader.Find("Standard")) { name = "V22 " + key }; m.color = c; m.SetFloat("_Glossiness", gloss);
            return v22Mats[key] = m;
        }
        // 竖条纹（雨棚）/ 棋盘格（后厨地砖）
        Material Pattern(string key, Color a, Color b, bool checker)
        {
            if (v22Mats.TryGetValue(key, out var m) && m != null) return m;
            const int N = 64; var px = new Color[N * N];
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) px[y * N + x] = (checker ? (x / 32 + y / 32) % 2 == 0 : (x / 8) % 2 == 0) ? a : b;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear }; t.SetPixels(px); t.Apply();
            m = new Material(Shader.Find("Standard")) { name = "V22 " + key, mainTexture = t }; m.SetFloat("_Glossiness", checker ? .55f : .35f);
            return v22Mats[key] = m;
        }
        static void Tile(GameObject o, float sx, float sy) { var mpb = new MaterialPropertyBlock(); mpb.SetVector("_MainTex_ST", new Vector4(sx, sy, 0, 0)); o.GetComponent<Renderer>().SetPropertyBlock(mpb); }

        // 可走坡道（和狙击台楼梯同一做法：0.2 m 厚斜板，NavMesh 直接覆盖），top = 上沿中点，dirDown = 往下走的水平方向
        GameObject Ramp(string name, Vector3 top, Vector3 dirDown, float H, float run, float width, Material m, Material stepMat)
        {
            dirDown.y = 0; dirDown.Normalize();
            Vector3 bottom = top + dirDown * run - Vector3.up * H;
            float len = Mathf.Sqrt(run * run + H * H);
            var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube); ramp.name = name;
            ramp.transform.position = (top + bottom) / 2 + Vector3.down * .1f;
            ramp.transform.rotation = Quaternion.LookRotation(bottom - top);
            ramp.transform.localScale = new Vector3(width, .2f, len);
            ramp.GetComponent<Renderer>().sharedMaterial = m; ramp.transform.SetParent(transform, true);
            int n = Mathf.Max(4, Mathf.RoundToInt(len / .5f));
            for (int i = 1; i < n; i++)
            {
                var step = GameObject.CreatePrimitive(PrimitiveType.Cube); Kill(step.GetComponent<Collider>()); step.name = name + "台阶纹";
                step.transform.position = Vector3.Lerp(top, bottom, i / (float)n) + Vector3.up * .012f; step.transform.rotation = Quaternion.LookRotation(dirDown);
                step.transform.localScale = new Vector3(width * .96f, .05f, .12f); step.GetComponent<Renderer>().sharedMaterial = stepMat;
                step.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; step.transform.SetParent(transform, true);
            }
            Vector3 mid = (top + bottom) / 2; Vector3 ext = new Vector3(Mathf.Abs(dirDown.x) * run + Mathf.Abs(dirDown.z) * width, 0, Mathf.Abs(dirDown.z) * run + Mathf.Abs(dirDown.x) * width);
            minimapBlocks.Add(new Rect(mid.x - ext.x / 2, mid.z - ext.z / 2, ext.x, ext.z));
            return ramp;
        }

        // 单向"跳下"导航连接（高点 → 地面）。AI 走到起点会直接从缺口跳下去
        public readonly List<OffMeshLink> jumpLinks = new List<OffMeshLink>();
        void JumpLink(Vector3 a, Vector3 b)
        {
            var s = new GameObject("跳下连接起点").transform; s.SetParent(transform, false); s.position = a;
            var e = new GameObject("跳下连接终点").transform; e.SetParent(transform, false); e.position = b;
            var l = s.gameObject.AddComponent<OffMeshLink>(); l.startTransform = s; l.endTransform = e; l.biDirectional = false; l.costOverride = 2f;
            jumpLinks.Add(l);
        }

        void Valve(Vector3 p, float rotY)
        {
            var g = new GameObject("蒸汽阀"); g.transform.position = p; g.transform.rotation = Quaternion.Euler(0, rotY, 0);
            g.AddComponent<SteamValve>().Build(); g.transform.SetParent(transform, true);
        }

        // 长街错位建筑（寿司吧 / 糖果铺 / 冰柜仓）+ 东通道奶茶亭 / 冰淇淋亭
        void V22Buildings()
        {
            var rice = Candy("sushiWall", new Color(.97f, .94f, .87f), .2f);
            var nori = Candy("nori", new Color(.16f, .26f, .2f), .25f);
            var salmon = Pattern("salmonStripe", new Color(1f, .55f, .4f), new Color(1f, .93f, .86f), false);
            var candyWall = Candy("candyWall", new Color(1f, .78f, .87f), .3f);
            var candyStripe = Pattern("candyStripe", new Color(1f, .4f, .6f), Color.white, false);
            var iceWall = Candy("iceWall", new Color(.74f, .9f, 1f), .4f);
            var iceStripe = Pattern("iceStripe", new Color(.45f, .75f, 1f), Color.white, false);
            foreach (float s in new[] { 1f, -1f })
            {
                // 寿司吧：长街西段北（南）侧 z 35.5–50，把长街切成 ≤ 42 m；正对长街的一面挂海苔带和三文鱼条纹雨棚
                float zn = s > 0 ? 34.5f : -50f, zf = s > 0 ? 50f : -34.5f, face = s * 34.5f;
                Block("寿司吧", -34, -26, zn, zf, 7, rice);
                Box("寿司吧海苔带", new Vector3(-30, 3.1f, face - s * .07f), new Vector3(8.1f, .9f, .12f), nori, false, 0, false);
                Box("寿司吧海苔带", new Vector3(-34.07f, 3.1f, (zn + zf) / 2), new Vector3(.12f, .9f, 14.6f), nori, false, 0, false);
                var aw = Box("寿司吧雨棚", new Vector3(-30, 2.75f, face - s * .75f), new Vector3(8.4f, .1f, 1.5f), salmon, false, 0, false); Tile(aw, 4, 1);
                Sign("寿司吧", new Vector3(-34.1f, 5.2f, (zn + zf) / 2), -90, new Color(1f, .5f, .35f), 1.1f);
                Sign("回转寿司", new Vector3(-30, 5.3f, face - s * .1f), s > 0 ? 180 : 0, new Color(1f, .5f, .35f), .8f);
                Food("Sushi", new Vector3(-30, 7.6f, (zn + zf) / 2), 30, 2.6f);
                // 糖果铺：长街西段南（北）侧 z 30–35.5，和寿司吧错开、在 z=35.5 处首尾相接 → 没有贯穿长街的直线
                float cn = s > 0 ? 30f : -35.5f, cf = s > 0 ? 35.5f : -30f;
                Block("糖果铺", -52, -44, cn, cf, 6, candyWall);
                var ca = Box("糖果铺雨棚", new Vector3(-48, 2.75f, s * 35.5f + s * .75f), new Vector3(8.4f, .1f, 1.5f), candyStripe, false, 0, false); Tile(ca, 4, 1);
                Sign("糖果铺", new Vector3(-52.1f, 4.4f, (cn + cf) / 2), -90, new Color(1f, .45f, .7f), 1f);
                Sign("棉花糖 · 棒棒糖", new Vector3(-48, 4.5f, s * 35.5f + s * .1f), s > 0 ? 0 : 180, new Color(1f, .45f, .7f), .7f);
                Food("LollipopLamp", new Vector3(-50, 6.6f, s * 32.75f), 0, 2.6f); Food("Macarons", new Vector3(-46, 6.6f, s * 32.75f), 0, 1.4f);
                // 外卖铺：长街中段南（北）侧 z 30–36.5，和糖果铺一起把寿司吧夹在中间（南-北-南三段错位）→ 斜着也穿不过去
                float tn = s > 0 ? 30f : -36.5f, tf = s > 0 ? 36.5f : -30f;
                Block("外卖铺", -16, -10, tn, tf, 5, Candy("deliveryWall", new Color(1f, .88f, .6f), .3f));
                var da = Box("外卖铺雨棚", new Vector3(-13, 2.75f, s * 36.5f + s * .75f), new Vector3(6.4f, .1f, 1.5f), Pattern("deliveryStripe", new Color(1f, .6f, .25f), Color.white, false), false, 0, false); Tile(da, 3, 1);
                Sign("外卖", new Vector3(-16.1f, 3.8f, (tn + tf) / 2), -90, new Color(1f, .6f, .25f), .8f);
                Sign("霓城外卖站", new Vector3(-13, 3.9f, s * 36.5f + s * .1f), s > 0 ? 0 : 180, new Color(1f, .6f, .25f), .6f);
                Food("Burger", new Vector3(-13, 5.6f, s * 33.25f), 0, 1.8f);
                // 西侧通道：出生区门口一块美食方块，切断穿过出生区的南北直线（北长街西头 ↔ 南长街西头）
                Container(new Vector3(-63, 0, s * 18f), 0, new Color(.85f, .6f, .9f));
            }
            // 冰柜仓：长街东段，蒸笼门后第一道错位（北 x 8–12 / 南 x 11.5–15.5，南侧给火锅让出 2 m 通道），迫使进包点前换一次角度
            Block("冰柜仓", 8, 12, 30, 40, 5.5f, iceWall);
            Block("冰柜仓", 11.5f, 15.5f, -40, -30, 5.5f, iceWall);
            foreach (var w in new[] { (8f, 35f), (11.5f, -35f) })
            {
                var ia = Box("冰柜仓雨棚", new Vector3(w.Item1 - .7f, 2.75f, w.Item2), new Vector3(1.4f, .1f, 9.6f), iceStripe, false, 0, false); Tile(ia, 1, 5);
                Sign("冰柜仓", new Vector3(w.Item1 - .08f, 4.2f, w.Item2), -90, new Color(.4f, .8f, 1f), .9f);
                Food("IceCream", new Vector3(w.Item1 + 2, 6.1f, w.Item2), 0, 3.2f);
            }
            // 东通道：两处奶茶亭 + 两处冰淇淋亭，和 T 墙、出生区围墙错位，把 100 m 的南北直线切成 ≤ 40 m
            foreach (float s in new[] { 1f, -1f })
            {
                Kiosk("奶茶亭", 48.8f, 57.5f, s > 0 ? 15f : -21f, s > 0 ? 21f : -15f, 2.6f, new Color(.85f, .62f, .42f), "MilkTea");
                Kiosk("冰淇淋亭", 59.5f, 67.5f, s > 0 ? 20.5f : -28.5f, s > 0 ? 28.5f : -20.5f, 2.6f, new Color(.55f, .8f, 1f), "IceCream");
            }
        }

        void Kiosk(string name, float x0, float x1, float z0, float z1, float h, Color c, string food)
        {
            float cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
            Box(name, new Vector3(cx, h / 2, cz), new Vector3(x1 - x0, h, z1 - z0), Candy("kiosk" + name, Color.Lerp(c, Color.white, .55f), .3f));
            Box(name + "柜台条", new Vector3(cx, 1.05f, cz), new Vector3(x1 - x0 + .06f, .18f, z1 - z0 + .06f), Candy("kioskBand" + name, c, .4f), false, 0, false);
            var roof = Box(name + "雨棚", new Vector3(cx, h + .1f, cz), new Vector3(x1 - x0 + .8f, .14f, z1 - z0 + .8f), Pattern("kioskStripe" + name, c, Color.white, false), false, 0, false); Tile(roof, 5, 1);
            FrostingEdges(x0, x1, z0, z1, h + .1f);
            Food(food, new Vector3(cx, h + .2f, cz), 0, 2.2f);
            Sign(name, new Vector3(x0 - .07f, 1.9f, cz), -90, c, .6f);
            Sign(name, new Vector3(x1 + .07f, 1.9f, cz), 90, c, .6f);
        }

        // 主街高摊位岛：实心 2.5 m 的"甜品高摊"（海绵蛋糕 + 奶油层 + 草莓层），甜品屋摊位挂在它西侧
        void StallIsland(float x0, float x1, float z0, float z1)
        {
            float cx = (x0 + x1) / 2, cz = (z0 + z1) / 2, w = x1 - x0, d = z1 - z0;
            Box("高摊位岛", new Vector3(cx, 1.25f, cz), new Vector3(w, 2.5f, d), Candy("sponge", new Color(1f, .86f, .58f), .15f));
            Box("高摊位岛草莓层", new Vector3(cx, .85f, cz), new Vector3(w + .06f, .22f, d + .06f), Candy("pinkCream", new Color(1f, .6f, .75f), .5f), false, 0, false);
            Box("高摊位岛奶油层", new Vector3(cx, 1.7f, cz), new Vector3(w + .06f, .3f, d + .06f), Candy("cream", new Color(1f, .97f, .92f), .5f), false, 0, false);
            Box("高摊位岛顶", new Vector3(cx, 2.56f, cz), new Vector3(w + .3f, .12f, d + .3f), Candy("cream", new Color(1f, .97f, .92f), .5f), false, 0, false);
            FrostingEdges(x0, x1, z0, z1, 2.55f);
            Food("CakeSlice", new Vector3(cx - 1.8f, 2.62f, cz + 2.2f), 20, 1.5f); Food("Macarons", new Vector3(cx + 2f, 2.62f, cz - 2f), 0, 1.1f); Food("Donut", new Vector3(cx + 1.6f, 2.62f, cz + 2.6f), 0, 1.1f);
            Sign("甜品高摊", new Vector3(cx, 2.15f, z1 + .06f), 0, new Color(1f, .5f, .75f), .55f);
            Sign("甜品高摊", new Vector3(cx, 2.15f, z0 - .06f), 180, new Color(1f, .5f, .75f), .55f);
            Lamp(new Vector3(cx, 3.6f, cz), new Color(1f, .78f, .86f), 1.6f, 10f);
        }

        // 外摆棚：实心冷饮 / 甜甜圈柜台（2.4 m）+ 条纹雨棚，贴着楼面，把侧道挡住一半
        void Awning(string name, float x0, float x1, float z0, float z1, Color c, string food, float faceY)
        {
            float cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
            Box(name, new Vector3(cx, 1.2f, cz), new Vector3(x1 - x0, 2.4f, z1 - z0), Candy("awningBody" + name, Color.Lerp(c, Color.white, .6f), .3f));
            var roof = Box(name + "雨棚", new Vector3(cx, 2.62f, cz), new Vector3(x1 - x0 + .8f, .12f, z1 - z0 + .5f), Pattern("awningStripe" + name, c, Color.white, false), false, 0, false); Tile(roof, 4, 1);
            Box(name + "柜台条", new Vector3(cx, 1.05f, cz), new Vector3(x1 - x0 + .06f, .16f, z1 - z0 + .06f), Candy("awningBand" + name, c, .45f), false, 0, false);
            Food(food, new Vector3(cx, 2.7f, cz), 0, 1.3f);
            Vector3 front = faceY == 0 ? new Vector3(cx, 1.85f, z1 + .06f) : new Vector3(cx, 1.85f, z0 - .06f);
            Sign(name.Replace("外摆棚 · ", ""), front, faceY, c, .55f);
        }

        // 中庭：x=12 牌楼横幅（离地 3.5–9 m 实心，地面照常走；市场楼顶看不到主街西段）+ 奶油平台（3.5 m，西坡道 / 东楼梯）
        void PlazaV22()
        {
            var pillar = Candy("archPillar", new Color(.62f, .4f, .3f), .3f);
            foreach (float s in new[] { 1f, -1f }) Box("牌楼柱", new Vector3(12, 4.9f, s * 9.65f), new Vector3(.7f, 9.8f, .7f), pillar);
            // 牌楼两侧落地侧柜（z ±7–9.3，高 3.5 m）：中庭入口收成 14 m，主街贴墙的斜线不再从牌楼边上穿进中庭再穿到东通道
            foreach (float s in new[] { 1f, -1f }) { Box("牌楼侧柜", new Vector3(12, 1.75f, s * 8.15f), new Vector3(.8f, 3.5f, 2.3f), Candy("archCabinet", new Color(1f, .8f, .62f), .3f)); }
            Box("牌楼顶梁", new Vector3(12, 9.65f, 0), new Vector3(.9f, .6f, 20.4f), pillar, false);
            Color[] bc = { new Color(1f, .62f, .72f), new Color(.62f, .9f, .75f), new Color(1f, .86f, .5f), new Color(.75f, .65f, 1f) };
            for (int i = 0; i < 4; i++) Box("牌楼横幅", new Vector3(12, 6.25f, -7.2f + i * 4.8f), new Vector3(.3f, 5.5f, 4.8f), Candy("banner" + i, bc[i], .25f), false);
            Sign("美食中庭", new Vector3(11.8f, 7.4f, 0), -90, new Color(1f, .55f, .7f), 1.2f);
            Sign("美食中庭", new Vector3(12.2f, 7.4f, 0), 90, new Color(1f, .55f, .7f), 1.2f);
            Lamp(new Vector3(10.5f, 3f, 0), new Color(1f, .8f, .7f), 1.6f, 10f);

            const float T = CreamTop;
            var sponge = Candy("sponge", new Color(1f, .86f, .58f), .15f);
            var cream = Candy("cream", new Color(1f, .97f, .92f), .5f);
            var pink = Candy("pinkCream", new Color(1f, .6f, .75f), .5f);
            var wafer = Candy("wafer", new Color(.93f, .72f, .42f), .25f);
            Box("奶油平台", new Vector3(20, T - .2f, 0), new Vector3(8, .4f, 10), sponge, false);
            void Band(float y, float h, Material m)
            {
                Box("奶油平台饰边", new Vector3(20, y, 4.85f), new Vector3(8.1f, h, .3f), m, false);
                Box("奶油平台饰边", new Vector3(20, y, -4.85f), new Vector3(8.1f, h, .3f), m, false);
                Box("奶油平台饰边", new Vector3(16.15f, y, 0), new Vector3(.3f, h, 9.4f), m, false);
                Box("奶油平台饰边", new Vector3(23.85f, y, 0), new Vector3(.3f, h, 9.4f), m, false);
            }
            Band(T - .55f, .3f, cream); Band(T - .82f, .24f, pink);
            foreach (float px in new[] { 16.6f, 20f, 23.4f }) foreach (float pz in new[] { 4.3f, -4.3f })
                    Box("奶油平台支柱", new Vector3(px, (T - .4f) / 2f, pz), new Vector3(.8f, T - .4f, .8f), wafer, false);
            // 矮墙 1 m（蹲下是掩体）；北边 x 19.2–21.8、南边 x 19.2–21、东边 z ±1.2 留缺口 = 跳下点；北边 x 21.8–24 是楼梯口；西边 z 1.2–3.6 是坡道口
            Box("奶油平台矮墙", new Vector3(17.6f, T + .5f, 4.8f), new Vector3(3.2f, 1f, .4f), cream, false);
            Box("奶油平台矮墙", new Vector3(17.6f, T + .5f, -4.8f), new Vector3(3.2f, 1f, .4f), cream, false);
            Box("奶油平台矮墙", new Vector3(22.5f, T + .5f, -4.8f), new Vector3(3f, 1f, .4f), cream, false);
            Box("奶油平台矮墙", new Vector3(16.2f, T + .5f, -2f), new Vector3(.4f, 1f, 6f), cream, false);
            Box("奶油平台矮墙", new Vector3(16.2f, T + .5f, 4.4f), new Vector3(.4f, 1f, 1.2f), cream, false);
            Box("奶油平台矮墙", new Vector3(23.8f, T + .5f, -3.1f), new Vector3(.4f, 1f, 3.8f), cream, false);
            Box("奶油平台矮墙", new Vector3(23.8f, T + .5f, 3.1f), new Vector3(.4f, 1f, 3.8f), cream, false);
            FrostingEdges(16, 24, -5, 5, T - .08f);
            Ramp("奶油坡道", new Vector3(16, T, 2.4f), Vector3.left, T, 6f, 2.4f, Candy("rampPink", new Color(1f, .64f, .76f), .35f), cream);   // 西坡道（攻方方向，约 30°）
            Ramp("威化楼梯", new Vector3(22.9f, T, 5), Vector3.forward, T, 6f, 2.2f, wafer, Candy("waferDark", new Color(.72f, .5f, .26f), .2f));   // 东北角楼梯（守方方向）
            Food("CakeSlice", new Vector3(17.4f, T + 1f, -4.8f), 200, .7f); Food("Macarons", new Vector3(22.5f, T + 1f, -4.8f), 0, .6f);
            Sign("奶油平台", new Vector3(15.92f, T + .55f, -2f), -90, new Color(1f, .5f, .7f), .6f);
            Lamp(new Vector3(20, T + 2.6f, 0), new Color(1f, .85f, .75f), 1.8f, 13f);
            Lamp(new Vector3(20, 2.4f, 0), new Color(1f, .8f, .7f), 1.4f, 8f);   // 平台下的近战空间
            spots.Add(new Spot { label = "中层 · 奶油平台（坡道 / 楼梯）", pos = new Vector3(20, T, 0), kind = 0 });
            afterNav.Add(() => { JumpLink(new Vector3(20, T, -4.3f), new Vector3(20, 0, -6.7f)); JumpLink(new Vector3(20.4f, T, 4.3f), new Vector3(20.4f, 0, 6.7f)); JumpLink(new Vector3(23.3f, T, 0), new Vector3(25.8f, 0, -.4f)); });
        }

        // 北烤串巷：露天烤串摊 + 灯笼，北端 3.5 m 外卖阳台俯看北长街（西墙坡道上，北缺口跳下）
        void KebabAlley()
        {
            float x0 = LinkX0, x1 = LinkX1, cx = (x0 + x1) / 2; const float T = CreamTop;
            var floor = Box("烤串巷地面", new Vector3(cx, .014f, 20), new Vector3(x1 - x0, .02f, 20), Pattern("cobble", new Color(.9f, .76f, .66f), new Color(.82f, .66f, .56f), true), false, 0, false); Tile(floor, 3, 10);
            // 巷内三段错位的高柜台（2.1 m，西-东-西），走位是 S 形：巷子是近战空间，也切断"北长街 → 烤串巷 → 主街 → 后厨 → 南长街"的 98 m 南北直线
            AlleyCounter("烤炉台", x0, -1.8f, 12.4f, 13.4f, new Color(1f, .45f, .3f), "Skewers");
            AlleyCounter("烤肠柜", -3.2f, x1, 16f, 17f, new Color(1f, .62f, .3f), "Skewers");
            AlleyCounter("外卖打包台", x0, -1.8f, 27f, 28f, new Color(1f, .75f, .4f), "MilkTea");
            Stall(new Vector3(x1 - .8f, 0, 23f), -90, new Color(1f, .6f, .3f), "烤肠");
            Steam(new Vector3(-3.4f, 2.2f, 12.9f)); Steam(new Vector3(x1 - .8f, 1.25f, 23f));
            for (int i = 0; i < 4; i++)
            {
                float z = 12.5f + i * 4.4f;
                Glowbox("烤串巷灯笼线", new Vector3(cx, 5.4f, z), new Vector3(x1 - x0, .04f, .04f), Glow(i % 2 == 0 ? new Color(1f, .5f, .3f) : new Color(1f, .8f, .4f), 2.5f));
                Lamp(new Vector3(cx, 4.8f, z), new Color(1f, .7f, .45f), 1.7f, 8f);
            }
            Sign("北烤串巷", new Vector3(x0 + .06f, 4.2f, 17.5f), 90, new Color(1f, .55f, .35f), .7f, true);
            var wood = Candy("balconyWood", new Color(.8f, .58f, .4f), .25f);
            Box("外卖阳台", new Vector3(cx, T - .15f, 27.8f), new Vector3(x1 - x0, .3f, 3.6f), wood, false);
            Box("外卖阳台栏杆", new Vector3(x0 + 1f, T + .5f, 29.45f), new Vector3(2f, 1f, .3f), wood, false);
            Box("外卖阳台栏杆", new Vector3(x1 - 1f, T + .5f, 29.45f), new Vector3(2f, 1f, .3f), wood, false);
            Box("外卖阳台栏杆", new Vector3(cx + .8f, T + .5f, 26.15f), new Vector3(4.4f, 1f, .2f), wood, false);
            foreach (float px in new[] { x0 + .25f, x1 - .25f }) Box("外卖阳台支柱", new Vector3(px, (T - .3f) / 2f, 26.35f), new Vector3(.3f, T - .3f, .3f), wood, false);
            Ramp("外卖阳台楼梯", new Vector3(x0 + .8f, T, 26f), Vector3.back, T, 6f, 1.6f, wood, Candy("waferDark", new Color(.72f, .5f, .26f), .2f));
            var umb = Pattern("umbrella", new Color(1f, .6f, .7f), Color.white, false);
            foreach (float ux in new[] { cx - 1.6f, cx + 1.8f })
            {
                Box("阳台遮阳伞杆", new Vector3(ux, T + 1.1f, 27.6f), new Vector3(.08f, 2.2f, .08f), wood, false, 0, false);
                var u = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Kill(u.GetComponent<Collider>()); u.name = "阳台遮阳伞";
                u.transform.position = new Vector3(ux, T + 2.25f, 27.6f); u.transform.localScale = new Vector3(2f, .06f, 2f); u.GetComponent<Renderer>().sharedMaterial = umb; u.transform.SetParent(transform, true);
            }
            Sign("外卖阳台", new Vector3(cx, T + 1.55f, 29.75f), 0, new Color(1f, .55f, .35f), .5f);
            spots.Add(new Spot { label = "中层 · 外卖阳台（楼梯）", pos = new Vector3(cx, T, 27.8f), kind = 0 });
            afterNav.Add(() => JumpLink(new Vector3(cx, T, 29.05f), new Vector3(cx, 0, 31.5f)));
        }

        // 南后厨：室内厨房（天花板 6 m、门洞 6×4.2 m），烤箱从中庭挪进来，东墙一个蒸汽阀
        void SouthKitchen()
        {
            float x0 = LinkX0, x1 = LinkX1, cx = (x0 + x1) / 2; const float H = 6f;
            var tile = Pattern("kitchenTile", new Color(1f, .8f, .85f), new Color(.84f, .95f, .89f), true);
            var wall = Candy("kitchenWall", new Color(.97f, .94f, .92f), .3f);
            var mint = Candy("kitchenMint", new Color(.6f, .88f, .76f), .35f);
            var steel = Candy("steel", new Color(.8f, .82f, .86f), .7f);
            var floor = Box("后厨地砖", new Vector3(cx, .014f, -20), new Vector3(x1 - x0, .02f, 20), tile, false, 0, false); Tile(floor, 3, 10);
            Box("后厨天花板", new Vector3(cx, H + .2f, -20), new Vector3(x1 - x0, .4f, 20.6f), wall, false);
            foreach (float z in new[] { -10.15f, -29.85f })
            {
                Box("后厨门楣墙", new Vector3(cx, (4.2f + H) / 2f, z), new Vector3(x1 - x0, H - 4.2f, .3f), wall, false);
                Box("后厨门框", new Vector3(cx, 4.05f, z), new Vector3(x1 - x0, .3f, .5f), mint, false, 0, false);
                foreach (float px in new[] { x0 + .12f, x1 - .12f }) Box("后厨门框", new Vector3(px, 2.1f, z), new Vector3(.24f, 4.2f, .5f), mint, false, 0, false);
            }
            // 和烤串巷同样的西-东-西三段高柜台（2.1 m），后厨里走 S 形
            AlleyCounter("出餐台", x0, -1.8f, -13.4f, -12.4f, new Color(.8f, .82f, .86f), "Dumpling");
            AlleyCounter("冷藏柜", -3.2f, x1, -17f, -16f, new Color(.6f, .88f, .76f), "IceCream");
            AlleyCounter("蒸笼架", x0, -1.8f, -28f, -27f, new Color(.9f, .72f, .45f), "Dumpling");
            Box("备餐台", new Vector3(-1.6f, .5f, -23f), new Vector3(1f, 1f, 2.4f), steel);
            Box("调料架", new Vector3(x1 - .3f, 1.1f, -25.8f), new Vector3(.6f, 2.2f, 3f), wall);
            Box("抽油烟机", new Vector3(x0 + .6f, 3.3f, -19.5f), new Vector3(1.2f, .6f, 2.4f), steel, false, 0, false);
            Box("墙面瓷砖带", new Vector3(x0 + .03f, 1.6f, -20), new Vector3(.06f, .3f, 19.4f), mint, false, 0, false);
            Box("墙面瓷砖带", new Vector3(x1 - .03f, 1.6f, -20), new Vector3(.06f, .3f, 19.4f), mint, false, 0, false);
            Food("Burger", new Vector3(-1.6f, 1f, -22.4f), 20, .4f);
            foreach (float z in new[] { -13f, -20f, -27f })
            {
                Lamp(new Vector3(cx, H - .5f, z), new Color(1f, .88f, .76f), 2.4f, 10f);
                Glowbox("后厨灯管", new Vector3(cx, H - .04f, z), new Vector3(2.4f, .05f, .3f), Glow(new Color(1f, .95f, .85f), 1.2f));
            }
            Steam(new Vector3(x0 + .6f, 1.3f, -19.5f));
            Sign("南后厨", new Vector3(x1 - .07f, 3.4f, -19f), -90, new Color(1f, .6f, .5f), .8f);
        }

        // 连接巷 / 后厨里的高柜台：实心 2.1 m（站姿挡胸口、蹲下全挡），顶上摆一样美食
        void AlleyCounter(string name, float x0, float x1, float z0, float z1, Color c, string food)
        {
            float cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
            Box(name, new Vector3(cx, 1.05f, cz), new Vector3(x1 - x0, 2.1f, z1 - z0), Candy("counter" + name, Color.Lerp(c, Color.white, .45f), .35f));
            Box(name + "台面", new Vector3(cx, 2.14f, cz), new Vector3(x1 - x0 + .1f, .08f, z1 - z0 + .12f), Candy("counterTop" + name, c, .5f), false, 0, false);
            Box(name + "灯条", new Vector3(cx, 1.2f, cz), new Vector3(x1 - x0 + .04f, .12f, z1 - z0 + .04f), Candy("counterBand" + name, c, .5f), false, 0, false);
            Food(food, new Vector3(cx, 2.18f, cz), 0, .55f);
            Sign(name, new Vector3(cx, 1.65f, z0 - .06f), 180, c, .4f);
            Sign(name, new Vector3(cx, 1.65f, z1 + .06f), 0, c, .4f);
        }

        // A 点汉堡平台：2.0 m（翻越上限 2.05 m 以内），四面都有翻越导航连接；面包 / 肉饼 / 芝士 / 生菜分层
        void BurgerPlatform(Vector3 c)
        {
            const float W = 5.5f, D = 3.6f, H = 2f;
            var root = new GameObject("汉堡平台"); root.transform.position = c;
            var bc = root.AddComponent<BoxCollider>(); bc.center = new Vector3(0, H / 2, 0); bc.size = new Vector3(W, H, D);
            AddBlock(c, new Vector3(W, H, D), 0);
            var layers = new (float, float, Color, float)[] { (0f, .5f, new Color(.93f, .65f, .32f), 0f), (.5f, .95f, new Color(.42f, .24f, .15f), .06f), (.95f, 1.05f, new Color(1f, .82f, .25f), .22f), (1.05f, 1.2f, new Color(.45f, .8f, .3f), .16f), (1.2f, 2f, new Color(.96f, .68f, .34f), .04f) };
            for (int i = 0; i < layers.Length; i++)
            {
                var l = layers[i];
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube); Kill(g.GetComponent<Collider>()); g.name = "汉堡平台层";
                g.transform.SetParent(root.transform, false); g.transform.localPosition = new Vector3(0, (l.Item1 + l.Item2) / 2, 0); g.transform.localScale = new Vector3(W + l.Item4, l.Item2 - l.Item1, D + l.Item4);
                g.GetComponent<Renderer>().sharedMaterial = Candy("burger" + i, l.Item3, i == 4 ? .45f : .3f);
            }
            for (int k = 0; k < 6; k++)
            {
                var seed = GameObject.CreatePrimitive(PrimitiveType.Cube); Kill(seed.GetComponent<Collider>()); seed.name = "芝麻";
                seed.transform.SetParent(root.transform, false); seed.transform.localPosition = new Vector3(-1.9f + k * .75f, H + .01f, (k % 2 == 0 ? .7f : -.6f)); seed.transform.localScale = new Vector3(.22f, .03f, .12f);
                seed.GetComponent<Renderer>().sharedMaterial = Candy("sesame", new Color(1f, .97f, .88f), .4f);
            }
            root.transform.SetParent(transform, true);
            var rt = root.transform;
            afterNav.Add(() =>
            {
                var pts = new List<(Vector3, Vector3)>();
                foreach (float x in new[] { -W / 4, W / 4 }) for (int side = -1; side <= 1; side += 2) pts.Add((new Vector3(x, 0, side * (D / 2 + .65f)), new Vector3(x, H, side * (D / 2 - .55f))));
                for (int side = -1; side <= 1; side += 2) pts.Add((new Vector3(side * (W / 2 + .65f), 0, 0), new Vector3(side * (W / 2 - .55f), H, 0)));
                foreach (var (a, b) in pts)
                {
                    var s = new GameObject("翻越连接起点").transform; s.SetParent(rt, false); s.localPosition = a;
                    var e = new GameObject("翻越连接终点").transform; e.SetParent(rt, false); e.localPosition = b;
                    var link = s.gameObject.AddComponent<OffMeshLink>(); link.startTransform = s; link.endTransform = e; link.biDirectional = true; link.costOverride = 4f;
                    climbLinks++;
                }
            });
            spots.Add(new Spot { label = "高点 · A 点汉堡平台（2.0 m，翻越可上）", pos = c + Vector3.up * H, kind = 0 });
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
            // V22 第二期：x∈[-5,1] 切开两个楼块 → 北烤串巷 / 南后厨（中段十字连接）
            Block("北街区公寓", -18, LinkX0, 10, 30, 16, brick);
            Block("北街区公寓", LinkX1, 14, 10, 30, 16, brick);
            Block("南街区工厂", -56, -36, -30, -10, 13, panels);
            Block("南街区工厂", -30, LinkX0, -30, -10, 15, panels);
            Block("南街区工厂", LinkX1, 14, -30, -10, 15, panels);
            Block("北连廊商铺", 14, 26, 24, 34, 9, brick);
            Block("南连廊仓库", 14, 26, -34, -24, 9, panels);
            Block("中庭市场大楼", 30, 44, -9, 9, 8, concrete);

            Facade("modular_urban_apartments_facade", new Vector3(-40f, 0, 10), Vector3.back, 32, 14);
            Facade("modular_urban_apartments_facade", new Vector3(-11.5f, 0, 10), Vector3.back, 13, 16);
            Facade("modular_urban_apartments_facade", new Vector3(7.5f, 0, 10), Vector3.back, 13, 16);
            Facade("modular_factory_facade", new Vector3(-46f, 0, -10), Vector3.forward, 20, 13);
            Facade("modular_factory_facade", new Vector3(-17.5f, 0, -10), Vector3.forward, 25, 15);
            Facade("modular_factory_facade", new Vector3(7.5f, 0, -10), Vector3.forward, 13, 15);
            Facade("modular_urban_apartments_facade", new Vector3(-48, 0, 50), Vector3.back, 46, 18);
            Facade("modular_urban_apartments_facade", new Vector3(0, 0, 50), Vector3.back, 46, 18);
            Facade("modular_factory_facade", new Vector3(-48, 0, -50), Vector3.forward, 46, 18);
            Facade("modular_factory_facade", new Vector3(0, 0, -50), Vector3.forward, 46, 18);

            WindowStrips(new Vector3(-56, 0, 30), new Vector3(-24, 0, 30), 14, Vector3.forward, 3);
            WindowStrips(new Vector3(-18, 0, 30), new Vector3(LinkX0, 0, 30), 16, Vector3.forward, 3);
            WindowStrips(new Vector3(LinkX1, 0, 30), new Vector3(14, 0, 30), 16, Vector3.forward, 3);
            WindowStrips(new Vector3(-56, 0, -30), new Vector3(-36, 0, -30), 13, Vector3.back, 3);
            WindowStrips(new Vector3(-30, 0, -30), new Vector3(LinkX0, 0, -30), 15, Vector3.back, 3);
            WindowStrips(new Vector3(LinkX1, 0, -30), new Vector3(14, 0, -30), 15, Vector3.back, 3);
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
            // V22 第二期：攻方出生区北侧补给柜（和守方对称）—— 北长街西头 ↔ 南长街西头不再有穿过出生区两个出口的直线
            Box("攻方补给柜", new Vector3(-63.5f, 1.1f, 5.75f), new Vector3(8, 2.2f, 1.5f), Candy("supplyShelfAtt", new Color(1f, .7f, .45f), .35f));
            Food("Donut", new Vector3(-61.5f, 2.2f, 5.75f), 0, .9f); Food("MilkTea", new Vector3(-65.5f, 2.2f, 5.75f), 0, 1.2f);
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
            // V22 第二期：守方出生区北侧补给柜（2.2 m）—— 北出口 ↔ 南出口不再是一条 100 m 的直线（东通道南北贯通线）
            Box("守方补给柜", new Vector3(63.5f, 1.1f, 5.75f), new Vector3(8, 2.2f, 1.5f), Candy("supplyShelf", new Color(.55f, .75f, 1f), .35f));
            Food("MilkTea", new Vector3(61.5f, 2.2f, 5.75f), 0, 1.2f); Food("Donut", new Vector3(65.5f, 2.2f, 5.75f), 0, .9f);
            Sign("守方安保站", new Vector3(55.6f, 3.4f, 0), -90, new Color(.3f, .7f, 1f), .7f);
            Lamp(new Vector3(64, 3.8f, -4), new Color(.4f, .7f, 1f), 2.2f, 12f); Lamp(new Vector3(64, 3.8f, 4), new Color(.4f, .7f, 1f), 2.2f, 12f);
            noBuild.Add(new Vector3(-64, 10, 0)); noBuild.Add(new Vector3(64, 10, 0));
        }

        void MidStreet()
        {
            string[] names = { "拉面", "电玩城", "义体诊所", "霓虹酒吧", "当铺" };
            Color[] cols = { new Color(1f, .3f, .2f), new Color(.2f, .85f, 1f), new Color(.4f, 1f, .6f), new Color(1f, .2f, .8f), new Color(1f, .7f, .2f) };
            if (ToonAssets.Available) names = new[] { "奶茶", "火锅", "甜品屋", "拉面", "烧烤串串" };   // V15：美食街
            for (int i = 0; i < 5; i++)
            {
                if (i == 2) { Stall(new Vector3(-23.85f, 0, 0), -90, cols[i], names[i]); continue; }   // V22：甜品屋挂在高摊位岛西侧（原位置被岛占了）
                Stall(new Vector3(-46 + i * 12, 0, i % 2 == 0 ? .8f : -.8f), i % 2 == 0 ? 0 : 180, cols[i], names[i]);
            }
            // V22 第二期：高摊位岛（实心 2.5 m）把主街分成南北两道；两侧外摆棚错位，把每条侧道切成 ≤ 48 m
            StallIsland(-23f, -15f, -5.5f, 5.5f);
            Awning("外摆棚 · 冰饮", -38f, -34f, 5f, 10f, new Color(.55f, .85f, 1f), "Popsicle", 180);
            Awning("外摆棚 · 甜甜圈", -12f, -8f, -10f, -5f, new Color(1f, .55f, .75f), "Donut", 0);
            Awning("外摆棚 · 烤肠", -12f, -8f, 5f, 10f, new Color(1f, .62f, .35f), "Skewers", 180);
            Awning("外摆棚 · 冰粉", -30f, -26f, -10f, -5f, new Color(.6f, .9f, .75f), "IceCream", 0);
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
                Window(new Vector3(i == 2 ? -20.5f : -44 + i * 16, .9f, 9.75f), 180, new Color(1f, .5f, .8f));   // V22：x=-12 那扇让给外摆棚
                Window(new Vector3(i == 0 ? -42 : i == 2 ? 7 : -36 + i * 16, .9f, -9.75f), 0, new Color(.4f, .9f, 1f));   // V22：x=-4 那扇挪开（南后厨门口）
            }
            StreetLamp(new Vector3(-50, .1f, -8.4f), 0, new Color(1f, .78f, .55f));
            StreetLamp(new Vector3(-28, .1f, 8.4f), 180, new Color(1f, .78f, .55f));
            StreetLamp(new Vector3(-7, .1f, -8.4f), 0, new Color(1f, .78f, .55f));          // V22：原 x=-4 挡南后厨门口
            PH("utility_box_02", new Vector3(-32f, .12f, 8.9f), 180, new Vector3(1.1f, 0, 0));   // V22：原 x=-36 被外摆棚占了
            PH("metal_trash_can", new Vector3(-18, .12f, -8.8f), 0, new Vector3(1.85f, 0, 0));
            PH("modular_street_seating", new Vector3(-41.5f, .12f, -9f), 0, new Vector3(3.5f, 0, 0));   // V22：原 x=-30 让给外摆棚
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
            // V22：原 (8, 38) 的方块和冰柜仓重叠 → 挪到关口东侧当"第二道"错位掩体（两层，和冰柜仓一起把门洞后的直线切断）
            Container(new Vector3(17, 0, 42f), 90, new Color(.25f, .55f, .35f), 2);
            PH("metal_trash_can", new Vector3(-54, .12f, 48.6f), 180, new Vector3(1.85f, 0, 0));
            PH("utility_box_01", new Vector3(-24.5f, .12f, 49f), 180, new Vector3(.6f, 0, 0));   // V22：原 x=-30 在寿司吧里
            PH("old_tyre", new Vector3(-23.5f, .3f, 46f), 0, new Vector3(.8f, 0, 0), false);
            PH("propane_tank", new Vector3(-2, 0, 47.8f), 0, new Vector3(0, .8f, 0));
            PH("concrete_road_barrier", new Vector3(-24, 0, 37), 0, new Vector3(0, 1.05f, 0));
            TWall(new Vector3(23.5f, 0, 35.4f), 0);   // V22：原 (16, 44) 和第二道方块重叠；和方块之间留 3 m 通道
            StreetLamp(new Vector3(-48, .1f, 31.4f), 180, new Color(1f, .5f, .85f));
            StreetLamp(new Vector3(-20, .1f, 48.6f), 0, new Color(.5f, .85f, 1f));
            StreetLamp(new Vector3(6, .1f, 31.4f), 180, new Color(1f, .78f, .55f));
            Sign("A 长街 →", new Vector3(-62, 4.5f, 49.4f), 180, new Color(1f, .45f, .2f), .9f);
            Sign("霓城快运", new Vector3(-15, 9f, 49.4f), 180, new Color(.2f, .85f, 1f), 1.6f);
            Sign("胶囊旅馆", new Vector3(20, 7f, 34.5f), 0, new Color(1f, .2f, .7f), .9f, true);
            Puddle(new Vector3(-40, 0, 36.6f), 4, 2, 10); Puddle(new Vector3(0, 0, 42), 3, 2, -30);   // V22：原 (-34, 38) 在寿司吧里
            Steam(new Vector3(-16, .02f, 38));
            Container(new Vector3(-66, 0, 24), 90, new Color(.6f, .5f, .2f));
            PH("concrete_road_barrier", new Vector3(-62, 0, 34), 90, new Vector3(0, 1.05f, 0));
            StreetLamp(new Vector3(-71, .1f, 16), 90, new Color(1f, .78f, .55f));
        }

        void SouthStreet()
        {
            Box("人行道", new Vector3(-21, .06f, -31.4f), new Vector3(70, .12f, 2.8f), pavement, false);
            Box("人行道", new Vector3(-23, .06f, -48.6f), new Vector3(98, .12f, 2.8f), pavement, false);
            Container(new Vector3(-40, 0, -45.5f), 0, new Color(.7f, .35f, .15f), 2);   // V22：原 (-36, -42) 和南寿司吧重叠
            Container(new Vector3(-8, 0, -38), 90, new Color(.25f, .4f, .6f));
            Container(new Vector3(10, 0, -45), 0, new Color(.5f, .5f, .5f));
            Container(new Vector3(19.5f, 0, -42f), 90, new Color(.55f, .4f, .65f), 2);   // V22：关口东侧第二道错位掩体
            PH("portable_generator", new Vector3(-50, 0, -47.5f), 0, new Vector3(1f, 0, 0));
            PH("Barrel_01", new Vector3(-22, 0, -47.8f), 0, new Vector3(0, .9f, 0));
            PH("barrel_03", new Vector3(-21.2f, 0, -47.2f), 40, new Vector3(0, .9f, 0));
            PH("cardboard_box_01", new Vector3(-16, .12f, -48.6f), 15, new Vector3(.6f, 0, 0), false);
            PH("concrete_road_barrier", new Vector3(-20, 0, -37), 0, new Vector3(0, 1.05f, 0));
            TWall(new Vector3(23.5f, 0, -35.4f), 0);   // V22：原 (18, -40) 和第二道方块重叠；和方块之间留 3 m 通道
            StreetLamp(new Vector3(-44, .1f, -31.4f), 0, new Color(1f, .78f, .55f));
            StreetLamp(new Vector3(-12, .1f, -48.6f), 180, new Color(.5f, .85f, 1f));
            Sign("B 工厂通道 →", new Vector3(-62, 4.5f, -49.4f), 0, new Color(1f, .45f, .2f), .9f);
            Sign("棱能集团 · 第七工厂", new Vector3(-12, 10f, -49.4f), 0, new Color(1f, .65f, .2f), 1.4f);
            for (int i = 0; i < 3; i++) PH("rollershutter_window_01", new Vector3(-48 + i * 20, .12f, -30.25f), 180, new Vector3(5f, 0, 0), false);
            Puddle(new Vector3(-40, 0, -36.6f), 4, 2.4f, -10); Puddle(new Vector3(4, 0, -36), 3, 2, 25);   // V22：原 (-28, -40) 在南寿司吧里
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
            TWall(new Vector3(47.6f, 0, 12), 0);    // V22：西移 0.4 m，和楼顶楼梯 / 东南方块首尾相接，不留缝
            TWall(new Vector3(47.6f, 0, -12), 0);
            Container(new Vector3(52, 0, 30), 90, new Color(.55f, .2f, .2f));
            Container(new Vector3(52, 0, -30), 90, new Color(.2f, .35f, .55f));
            Container(new Vector3(45.3f, 0, -9), 90, new Color(.9f, .55f, .65f));   // V22：市场东南角，和楼梯对称，切断 x≈45 的南北直线
            StreetLamp(new Vector3(27, .1f, 12), 90, new Color(.5f, .85f, 1f));
            StreetLamp(new Vector3(50.5f, .1f, -12.5f), -90, new Color(1f, .78f, .55f));   // V22：原 (47, -12) 和 T 墙重叠
            PH("utility_box_01", new Vector3(48.6f, 0, 22.5f), 0, new Vector3(.6f, 0, 0));  // V22：原 (50, 16) 被奶茶亭占了
            Puddle(new Vector3(22, 0, -4), 4, 2, 15); Puddle(new Vector3(50, 0, 4), 3, 2, -20);
            Steam(new Vector3(26.5f, .02f, 6));
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
            BurgerPlatform(new Vector3(33, 0, 26.5f));   // V22 第二期：A 点汉堡平台（原蛋糕方块位置，2.0 m，翻越可上）
            Container(new Vector3(30, 0, 47.5f), 0, new Color(.3f, .3f, .35f));
            // V22 第二期：A 点北侧两块方块 —— 长街东段 / 东通道之间不再有穿过 A 北沿的 > 60 m 直线，也给长街口多一个掩体
            Container(new Vector3(40, 0, 45), 90, new Color(.95f, .7f, .35f));
            Container(new Vector3(44.5f, 0, 48.6f), 0, new Color(.55f, .8f, .6f));
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
            Box("B厂房北墙窗台", new Vector3(38, .825f, -18), new Vector3(8, 1.65f, .6f), iron);   // V22：窗台 1.0 → 1.65 m（打碎玻璃后仍能翻进去），主街 → B 北窗 → 东通道的 70 m 斜线被挡
            Box("B厂房北墙上沿", new Vector3(38, 4.3f, -18), new Vector3(8, 1.4f, .6f), iron, false);
            Box("B门框", new Vector3(29, 4.1f, -18), new Vector3(4.6f, 1.8f, .8f), iron, false);
            BreakableWall("B厂房西墙（可破坏）", new Vector3(27, 0, -46), 90, 8, 5f);
            Box("B厂房东墙", new Vector3(48, 2.5f, -34), new Vector3(.6f, 5, 16), iron);
            Box("B厂房屋顶", new Vector3(37, 7f, -34), new Vector3(22, .4f, 24), sheet, false, 0, false);
            for (int i = 0; i < 5; i++) Box("屋顶钢梁", new Vector3(28 + i * 4.5f, 6.6f, -34), new Vector3(.3f, .4f, 24), plate, false, 0, false);
            for (int i = 0; i < 4; i++)
            {
                var g = new GameObject("B厂房玻璃"); g.transform.position = new Vector3(35 + i * 2f, 1.65f, -18); g.transform.SetParent(transform, true);
                var gp = g.AddComponent<GlassPanel>(); gp.Build(new Vector3(2f, 1.95f, .06f), new Color(.5f, .8f, 1f)); glass.Add(gp);
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
            foreach (var h in new[] { new Vector3(-44, .12f, 9.2f), new Vector3(-13.6f, .12f, -9.2f), new Vector3(-40, .12f, 31.6f), new Vector3(-20, .12f, -31.6f), new Vector3(10, .12f, 48.8f) })   // V22：(-10,-9.2) 让给外摆棚
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
            afterNav.Add(() => JumpLink(new Vector3(-46.2f, 6f, 7.6f), new Vector3(-48.6f, 0, 7.6f)));   // V22：水塔西沿跳下（AI 离开狙击位时不再困在台上）
            SniperPerch("A 长街（守）", new Vector3(23, 0, 47.5f), -90, Vector3.left);
            SniperPerch("B 长街（守）", new Vector3(23, 0, -47.5f), -90, Vector3.left);
            // 注：4 个楼梯狙击台保留，原来 91 m 的对望被寿司吧 / 糖果铺 / 冰柜仓切断
            // 防守位：每个包点两处，面向主要进攻入口
            Bunker("A 点长街口", new Vector3(41, 0, 37), -100);
            Bunker("A 点连廊口", new Vector3(44, 0, 23.5f), -125);
            Bunker("B 点长街口", new Vector3(43.5f, 0, -38.5f), -80);
            Bunker("B 点连廊口", new Vector3(38, 0, -22.5f), 180);
            PlantPad("A", SiteA); PlantPad("B", SiteB);
            MarketRoof();
            Checkpoint("北关口", 40f);
            Checkpoint("南关口", -40f);
            // V22 第二期：关口西侧一摞蒸笼（2.2 m，实心）—— 攻方到门口有个掩体；从寿司吧 / 外卖铺之间斜穿过来再穿过门洞的直线被挡住
            foreach (float s in new[] { 1f, -1f }) SteamerStack(new Vector3(-7.75f, 0, s * 39.75f));
        }

        void SteamerStack(Vector3 c)
        {
            var bamboo = Candy("bamboo", new Color(.9f, .72f, .45f), .3f); var rim = Candy("bambooRim", new Color(.7f, .5f, .28f), .25f);
            Box("关口蒸笼堆", c + Vector3.up * 1.1f, new Vector3(2.5f, 2.2f, 4.5f), bamboo, true, 0, true).GetComponent<Renderer>().enabled = false;   // 碰撞 + 小地图；外观是下面的蒸笼
            for (int i = 0; i < 3; i++)
            {
                var t = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Kill(t.GetComponent<Collider>()); t.name = "关口蒸笼";
                t.transform.position = c + Vector3.up * (.37f + i * .73f); t.transform.localScale = new Vector3(2.6f, .35f, 4.6f);
                t.GetComponent<Renderer>().sharedMaterial = i % 2 == 0 ? bamboo : rim; t.transform.SetParent(transform, true);
            }
            Food("Dumpling", c + Vector3.up * 2.2f, 0, .8f);
        }

        // 市场楼顶狙击位：8 m 高，四周矮墙，俯瞰整条夜市主街；只能从楼东侧弹射板上去
        void MarketRoof()
        {
            const float Y = 8f;
            for (int i = -1; i <= 1; i += 2)
            {
                Box("楼顶矮墙", new Vector3(37, Y + .5f, i * 8.85f), new Vector3(14, 1f, .3f), concrete, false);
                if (i > 0) Box("楼顶矮墙", new Vector3(43.85f, Y + .5f, 5.25f), new Vector3(.3f, 1f, 7.2f), concrete, false);   // V22：东墙北段让出楼梯口（z 0.15–1.65）
                else Box("楼顶矮墙", new Vector3(37 + i * 6.85f, Y + .5f, i * 4.5f), new Vector3(.3f, 1f, 8.7f), concrete, false);
                Box("楼顶矮墙", new Vector3(37 + i * 6.85f, Y + .5f, -i * 6.6f), new Vector3(.3f, 1f, 4.2f), concrete, false);   // west/east gaps → firing slots
            }
            // V22 第二期：东侧楼梯 —— 守方可以走上来、也能走下去（原来只能弹上、只能跳下）。
            // 平台 8 m（楼体顶面，和弹射板落点同高），坡道沿楼东侧向北 14 m（约 30°，和狙击台楼梯同一手感，NavMesh 坡度上限 40°）
            Box("楼顶楼梯平台", new Vector3(45.05f, RoofTop - .15f, .2f), new Vector3(2.5f, .3f, 2.8f), plate, false);
            Box("楼顶楼梯栏杆", new Vector3(46.25f, RoofTop + .5f, .2f), new Vector3(.12f, 1f, 2.8f), plate, false);
            Ramp("楼顶楼梯", new Vector3(45.2f, RoofTop, 1.6f), Vector3.forward, RoofTop, 14f, 2.2f, Candy("wafer", new Color(.93f, .72f, .42f), .25f), Candy("waferDark", new Color(.72f, .5f, .26f), .2f));
            for (int k = 1; k <= 3; k++) Box("楼梯立柱", new Vector3(46.2f, RoofTop * (1 - k / 4f) / 2f, 1.6f + 14f * k / 4f), new Vector3(.25f, RoofTop * (1 - k / 4f), .25f), plate, false);
            Sign("楼顶楼梯 ↑", new Vector3(45.2f, 2.9f, 15.75f), 0, new Color(1f, .5f, .3f), .5f);
            // V22：楼梯下的奶茶仓（实心 2 m，在坡道下沿以下）+ 东北角可乐冰柜 —— 和 T 墙首尾相接，主街 / 中庭的斜线不再从市场东北角穿到东通道
            Box("楼梯下奶茶仓", new Vector3(45.2f, 1f, 6.8f), new Vector3(2.2f, 2f, 9.6f), Candy("kioskStore", new Color(.95f, .82f, .7f), .3f));
            Box("可乐冰柜", new Vector3(47.55f, 1.1f, 13.55f), new Vector3(2.3f, 2.2f, 2.9f), Candy("colaFridge", new Color(.85f, .25f, .3f), .5f));
            Box("可乐冰柜", new Vector3(47.55f, 1.1f, -13.55f), new Vector3(2.3f, 2.2f, 2.9f), Candy("colaFridge", new Color(.85f, .25f, .3f), .5f));
            Box("楼顶通风机", new Vector3(39, Y + .6f, 3), new Vector3(2, 1.2f, 2), plate, false);
            Box("楼顶水箱", new Vector3(35, Y + .9f, -4), new Vector3(1.6f, 1.8f, 1.6f), iron, false);
            PH("wooden_military_crate", new Vector3(32, Y, 5), 90, new Vector3(0, .75f, 0));
            var padGo = new GameObject("弹射板 市场楼顶"); padGo.transform.position = new Vector3(47.5f, 0, -2f);
            var jp = padGo.AddComponent<JumpPad>(); jp.Build(new Color(1f, .45f, .3f)); jp.AimAt(new Vector3(41f, Y, -2f), 3f);
            padGo.transform.SetParent(transform, true);
            Sign("弹射板 ↑ 楼顶狙击位", new Vector3(44.1f, 3.2f, -6.5f), 90, new Color(1f, .5f, .3f), .5f);
            spots.Add(new Spot { label = "狙击位 · 市场楼顶（楼梯 / 弹射板）", pos = new Vector3(37, Y, 0), kind = 0 });
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
                // V22 第二期：铁门 → 蒸笼门。默认开着（蒸笼升到门楣上方），守方长按 G 盖上（巨响），攻方长按 G 掀开；墙洞保留
                var go = new GameObject(label + "蒸笼门");
                go.transform.position = new Vector3(X, 0, z); go.transform.rotation = Quaternion.Euler(0, 90, 0);
                var gate = go.AddComponent<Shutter>(); gate.BuildGate(gateW, 3.8f, plate); go.transform.SetParent(transform, true); shutters.Add(gate);
            });
            noBuild.Add(new Vector3(X, 3f, z));
            // guard booth + warning signs on both faces
            Box("岗亭", new Vector3(X - 2.2f, 1.3f, z - (z > 0 ? 6.5f : -6.5f)), new Vector3(2.2f, 2.6f, 2.2f), panels);   // away from the wall hole
            Sign(label + " · 蒸笼门默认开 · 守方可盖上", new Vector3(X - .45f, 4.9f, z), -90, new Color(1f, .6f, .3f), .5f);
            Sign(label + " · 长按 G 盖上 / 掀开蒸笼", new Vector3(X + .45f, 4.9f, z), 90, new Color(1f, .6f, .3f), .5f);
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
            LightCover(new Vector3(-22, 0, 34.2f), 0); LightCover(new Vector3(-22, 0, -34.2f), 0);   // V22：原 (-30, 44) / (-28, -44) 在寿司吧里 → 挪到北/南小巷口
            LightCover(new Vector3(20, 0, 2), 90);

            Barrel(new Vector3(-28, 0, 7.2f)); Barrel(new Vector3(37.5f, 0, 46.5f)); Barrel(new Vector3(33, 0, -44)); Barrel(new Vector3(-6, 0, -46.5f));   // V22：A 点那只从 (40.5, 44.5) 让给新方块
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
            // V22：东侧栏杆两头各让出 3 m —— 从连廊跳下正好落到北烤串巷 / 南后厨门口（单向导航连接，见 JumpLink）
            Box("连廊栏杆", new Vector3(-10.4f, 9.2f, 0), new Vector3(.2f, .6f, 20), plate, false);
            Box("连廊栏杆", new Vector3(-5.6f, 9.2f, 0), new Vector3(.2f, .6f, 14), plate, false);
            foreach (float s in new[] { 1f, -1f }) afterNav.Add(() => JumpLink(new Vector3(-6.4f, 8.9f, s * 8.6f), new Vector3(-3.4f, 0, s * 7.6f)));
            Box("连廊沙袋", new Vector3(-8.4f, 9.3f, -6), new Vector3(2.2f, .8f, .6f), concrete, false); Box("连廊沙袋", new Vector3(-8.4f, 9.3f, 6), new Vector3(2.2f, .8f, .6f), concrete, false);   // V22：缩短 0.8 m，东侧留 1.6 m 让 NavMesh 连到两头的跳下口
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
            // V22：x=12 的"中庭"门楼换成牌楼横幅（PlazaV22）；长街门楼挪到糖果铺东边（原 x=-50 的立柱插进糖果铺）
            Arch(new Vector3(-41, 0, 40), 90, 15f, new Color(1f, .6f, .2f), "北长街");
            Arch(new Vector3(-41, 0, -40), 90, 15f, new Color(.6f, .3f, 1f), "南工厂通道");
            // holographic billboards on rooftops
            // vending machines & AC units along the streets
            Vending(new Vector3(-33.4f, 0, 8.6f), 180, new Color(.2f, .9f, 1f)); Vending(new Vector3(-16, 0, 8.6f), 180, new Color(1f, .3f, .7f));   // V22：原 x=-36 被外摆棚占了
            Vending(new Vector3(-44, 0, -8.6f), 0, new Color(1f, .6f, .2f)); Vending(new Vector3(4, 0, -8.6f), 0, new Color(.3f, 1f, .6f));
            Vending(new Vector3(-42.6f, 0, 31.6f), 0, new Color(1f, .3f, .7f)); Vending(new Vector3(-8.3f, 0, -31.6f), 180, new Color(.2f, .9f, 1f));   // V22：糖果铺 / 外卖铺让位
            Vending(new Vector3(48.3f, 0, 21.2f), -90, new Color(.6f, .3f, 1f));   // V22：原 (50.5, 20) 被奶茶亭占了
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
            // V22 第二期：新区域地面字
            FloorText("北烤串巷", new Vector3(-2, 0, 18.5f), 0, new Color(1f, .55f, .35f), 1f);
            FloorText("南后厨", new Vector3(-2, 0, -22.5f), 180, new Color(1f, .7f, .5f), 1f);
            FloorText("中段十字", new Vector3(-2, 0, -3.2f), 90, new Color(1f, .4f, .6f), 1.1f);
            FloorText("北关口", new Vector3(-3, 0, 36.2f), 90, new Color(1f, .7f, .35f), 1.1f);
            FloorText("南关口", new Vector3(-3, 0, -36.2f), 90, new Color(1f, .7f, .35f), 1.1f);
            // direction signs (face the approaching attacker)
            Sign("北长街 ↑   中庭 →   南工厂 ↓", new Vector3(-55.6f, 6.2f, 0), 90, new Color(.3f, 1f, .9f), .6f);
            Sign("← 主街   A 点 →", new Vector3(-21, 6.4f, 30.4f), 0, new Color(1f, .6f, .2f), .55f);
            Sign("A 连廊 ↖   B 连廊 ↙   中央市场 →", new Vector3(11.7f, 4.25f, 0), -90, new Color(.3f, 1f, .9f), .55f);   // V22：挂到牌楼横幅下沿
            Sign("↑ 北烤串巷 · 北长街中段", new Vector3(-2, 6.3f, 9.6f), 180, new Color(1f, .55f, .35f), .55f);
            Sign("↓ 南后厨 · 南工厂中段", new Vector3(-2, 5.2f, -9.75f), 0, new Color(1f, .7f, .5f), .55f);
            Sign("↓ 北烤串巷 · 回主街", new Vector3(-2, 6.6f, 30.4f), 0, new Color(1f, .55f, .35f), .5f);
            Sign("↑ 南后厨 · 回主街", new Vector3(-2, 5.2f, -30.25f), 180, new Color(1f, .7f, .5f), .5f);
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
            // V22 第二期：16 条进攻路线（长街 ×2、墙洞 ×2、连接巷 ×2、连廊 ×2、中庭侧口 ×2、北/南小巷 ×2、奶油平台 ×2、假打转点 ×2）。
            // 路点避开门洞正中（关口前后各一个点，偏开 1 m），连接巷 / 后厨走中线。
            Vector3 V(float x, float z, float y = 0) { return new Vector3(x, y, z); }
            Vector3[] N(params Vector3[] p) { return p; }
            var westN = N(V(-62, 13), V(-58, 22), V(-56, 36), V(-40, 37.5f), V(-29, 32.6f), V(-20, 38.5f));
            var westS = N(V(-62, -13), V(-58, -22), V(-56, -36), V(-40, -37.5f), V(-29, -32.6f), V(-20, -38.5f));
            var gateN = N(V(.5f, 41), V(7.5f, 41.5f), V(14, 37.5f), V(21, 37.5f), V(30, 37), SiteA);
            var gateS = N(V(.5f, -41), V(7.5f, -41.5f), V(24, -39.5f), V(30, -38), SiteB);
            Vector3[] Cat(params Vector3[][] parts) { var l = new List<Vector3>(); foreach (var p in parts) l.AddRange(p); return l.ToArray(); }
            routes["A长街"] = Cat(westN, gateN);
            routes["A长街墙洞"] = Cat(westN, N(V(-4, 46.5f), V(1, 47), V(7.5f, 47.5f), V(14, 46), V(22, 44), V(30, 38), SiteA));
            routes["北烤串巷A"] = Cat(N(V(-52, 5), V(-30, 4), V(-14, 7.5f), V(-2, 9), V(-2, 20), V(-1.5f, 32)), gateN);
            routes["A连廊"] = N(V(-52, 4), V(-28, 4), V(-13.5f, 3.5f), V(5, 5.5f), V(19, 11), V(20, 17), V(28, 21), SiteA);
            routes["中路A"] = N(V(-52, -4), V(-30, -3), V(-10, -3), V(6, -6.5f), V(18, -1.5f), V(26, 8), V(28, 14), V(29, 20), SiteA);
            routes["北小巷A"] = Cat(N(V(-52, 5), V(-21, 7.5f), V(-21, 20), V(-21, 32.5f), V(-20, 38.5f)), gateN);
            routes["奶油平台A"] = N(V(-52, 4), V(-28, 4), V(-13.5f, 3.5f), V(8.5f, 2.4f), V(18.5f, 2.4f, CreamTop), V(20.3f, 3.8f, CreamTop), V(25, 9), V(28, 14), V(29, 20), SiteA);
            routes["南巷转A"] = Cat(N(V(-52, -5), V(-14, -7.5f), V(-2, -12), V(-2, -21), V(-1, -32.5f), V(-2, -21), V(-2, 0), V(-2, 20), V(-1.5f, 32)), gateN);
            routes["B工厂"] = Cat(westS, gateS);
            routes["B工厂墙洞"] = Cat(westS, N(V(-4, -46.5f), V(1, -47), V(7.5f, -47.5f), V(16, -47), V(24, -41), V(30, -38), SiteB));
            routes["南后厨B"] = Cat(N(V(-52, -5), V(-30, -4), V(-14, -7.5f), V(-2, -9), V(-2, -20), V(-1.5f, -32)), gateS);
            routes["B连廊"] = N(V(-52, -4), V(-28, -4), V(-10, -3), V(6, -6.5f), V(19, -11), V(20, -17), V(28, -21), SiteB);
            routes["中路B"] = N(V(-52, 4), V(-28, 4), V(-13.5f, 3.5f), V(5, 5.5f), V(18, -1.5f), V(27, -12), V(29, -16), V(29, -21), SiteB);
            routes["南小巷B"] = Cat(N(V(-52, -5), V(-33, -6.5f), V(-33, -18), V(-33, -24), V(-29, -32.6f), V(-20, -38.5f)), gateS);
            routes["奶油平台B"] = N(V(-52, 4), V(-28, 4), V(-13.5f, 3.5f), V(8.5f, 2.4f), V(18.5f, 2.4f, CreamTop), V(19.8f, -3.8f, CreamTop), V(25, -9.5f), V(27, -12), V(29, -16), V(29, -21), SiteB);
            routes["北巷转B"] = Cat(N(V(-52, 5), V(-14, 7.5f), V(-2, 12), V(-2, 21), V(-1, 32.5f), V(-2, 21), V(-2, 0), V(-2, -20), V(-1.5f, -32)), gateS);
            anchors[0] = ToonAssets.Available
                ? new[] { new Vector3(42.5f, 0, 37.3f), new Vector3(45.3f, 0, 24.5f), new Vector3(33, 0, 31), new Vector3(30, 0, 36), new Vector3(33.5f, 2f, 26.5f) }   // V22：A 点汉堡平台顶
                : new[] { new Vector3(42.5f, 0, 37.3f), new Vector3(45.3f, 0, 24.5f), new Vector3(33, 0, 31), new Vector3(30, 0, 36) };
            anchors[1] = new[] { new Vector3(44.8f, 0, -38.3f), new Vector3(38, 0, -21.3f), new Vector3(33, 0, -30), new Vector3(31, 0, -42) };
            anchors[2] = new[] { new Vector3(26, 0, 8), new Vector3(26, 0, -8), new Vector3(28, 0, 0), new Vector3(21.5f, CreamTop, -2.5f), new Vector3(18, CreamTop, 1) };   // 中庭 + 奶油平台
            anchors[3] = new[] { new Vector3(6, 0, 35.5f), new Vector3(14, 0, 37.5f), new Vector3(10, 0, 47) };       // 北关口（守方一侧）
            anchors[4] = new[] { new Vector3(5.5f, 0, -32.5f), new Vector3(14, 0, -42), new Vector3(17, 0, -47) };    // 南关口
        }

        // 关口蒸笼门（z>0 北 / z<0 南）
        public Shutter Gate(int side) { foreach (var s in shutters) if (s != null && s.gate && Mathf.Sign(s.transform.position.z) == side) return s; return null; }

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
            SteamCloud.ClearTimed();   // V22：蒸汽阀的白雾不跨回合
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
