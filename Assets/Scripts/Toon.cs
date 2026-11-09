using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonSiege
{
    // V14 Q 版画风：Quaternius Toon Shooter Game Kit（CC0）—— 卡通士兵（手上已挂好 13 把枪）、卡通枪、卡通道具。
    public static class ToonAssets
    {
        static bool? available;
        public static bool Available { get { if (available == null) available = Resources.Load<GameObject>("Toon/Chars/Character_Soldier") != null; return available.Value; } }

        public static string CharFor(Role r)
        {
            switch (r)
            {
                case Role.Falcon: case Role.Raven: return BeanAvailable ? "Character_Bean" : "Character_Soldier";
                case Role.Rivet: case Role.Ironwall: return BeanAvailable ? "Character_Bean" : "Character_Hazmat";
                default: return BeanAvailable ? "Character_Bean" : "Character_Enemy";
            }
        }

        // the 7 buyable guns each get their own toon model
        public static string GunFor(WeaponSpec w)
        {
            switch (w.id)
            {
                case "SMG-7": return "SMG";
                case "AR-9": return "AK";
                case "LMG-2": return "ShortCannon";
                case "DMR-3": return "Sniper";
                case "FS-12": return "Shotgun";
                case "BR-5": return "Sniper_2";
                default: return "Pistol";
            }
        }
        static readonly string[] AllGuns = { "AK", "GrenadeLauncher", "Knife_1", "Knife_2", "Pistol", "Revolver", "Revolver_Small", "RocketLauncher", "ShortCannon", "Shotgun", "Shovel", "SMG", "Sniper", "Sniper_2" };
        // V17 第三阶段：炸弹 → 生日蛋糕；安装 → 点蜡烛；拆除 → 吹蜡烛；资金 → 小费
        static readonly string[][] FoodWords = {
            new[] { "棱能炸弹已安装", "生日蛋糕已点燃" }, new[] { "炸弹已安装", "蛋糕已点燃" }, new[] { "棱能炸弹引爆", "生日蛋糕爆炸" }, new[] { "炸弹已拆除", "蜡烛已吹灭" },
            new[] { "安装棱能炸弹", "放下蛋糕点蜡烛" }, new[] { "安装炸弹", "点蜡烛" }, new[] { "拆除炸弹", "吹灭蜡烛" }, new[] { "正在拆除炸弹", "正在吹蜡烛" },
            new[] { "安装中…", "点蜡烛中…" }, new[] { "拆除中…", "吹蜡烛中…" }, new[] { "炸弹掉落", "蛋糕掉了" }, new[] { "捡起了炸弹", "捡起了蛋糕" },
            new[] { "棱能炸弹", "生日蛋糕" }, new[] { "炸弹", "蛋糕" }, new[] { "安装/拆除", "点蜡烛/吹蜡烛" }, new[] { "安装", "点蜡烛" }, new[] { "拆除", "吹灭" },
            new[] { "资金不足", "小费不够" }, new[] { "资金", "小费" }, new[] { "选择武器", "美食街菜单" } };
        public static string FoodText(string s)
        {
            if (!Available || string.IsNullOrEmpty(s)) return s;
            foreach (var w in FoodWords) if (s.Contains(w[0])) s = s.Replace(w[0], w[1]);
            return s;
        }

        // V17 第四阶段：糖豆人式豆子角色 + 每个英雄一顶美食帽子
        public static bool BeanAvailable { get { return Resources.Load<GameObject>("Toon/Chars/Character_Bean") != null; } }
        public static string HatFor(Role r)
        {
            switch (r) { case Role.Falcon: return "Burger"; case Role.Rivet: return "Onigiri"; case Role.Ironwall: return "Pudding"; case Role.Raven: return "Sushi"; case Role.Ember: return "Cupcake"; default: return "Donut"; }
        }
        static readonly Dictionary<Color, Material> ringMats = new Dictionary<Color, Material>();
        static Texture2D ringTex;
        public static Material RingMaterial(Color c)
        {
            if (ringMats.TryGetValue(c, out var m)) return m;
            if (ringTex == null)
            {
                const int N = 128; var px = new Color[N * N];
                for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) { float r = new Vector2(x - N / 2f + .5f, y - N / 2f + .5f).magnitude / (N / 2f); float a = Mathf.Clamp01(1f - Mathf.Abs(r - .78f) / .14f) + (r < .7f ? .18f * (1 - r) : 0); px[y * N + x] = new Color(1, 1, 1, Mathf.Clamp01(a)); }
                ringTex = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp }; ringTex.SetPixels(px); ringTex.Apply();
            }
            m = new Material(Shader.Find("Sprites/Default")) { mainTexture = ringTex, color = new Color(c.r, c.g, c.b, .85f), renderQueue = 3002 };
            return ringMats[c] = m;
        }
        // 把一个模型放到角色头顶（变身时的大菜），跟着头部骨骼动
        public static GameObject AttachAbove(GameObject model, string path, float height, float lift)
        {
            var pf = Resources.Load<GameObject>(path); if (pf == null) return null;
            Transform headBone = null; Bounds b = new Bounds(); bool any = false;
            foreach (var t in model.GetComponentsInChildren<Transform>()) if (t.name == "Head" && t.GetComponent<Renderer>() == null) headBone = t;
            foreach (var rr in model.GetComponentsInChildren<Renderer>()) if (rr.enabled && !IsGunNode(rr.name) && rr.name != "美食帽子") { if (!any) { b = rr.bounds; any = true; } else b.Encapsulate(rr.bounds); }
            if (!any) return null;
            var o = Object.Instantiate(pf); o.name = "变身大菜";
            foreach (var c in o.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            Arena.Fit(o, new Vector3(0, height, 0));
            o.transform.position += new Vector3(headBone != null ? headBone.position.x : b.center.x, b.max.y + lift, headBone != null ? headBone.position.z : b.center.z);
            o.transform.SetParent(headBone != null ? headBone : model.transform, true);
            return o;
        }

        public static void AddHat(GameObject model, Role r, string overrideHat = null)
        {
            var pf = Resources.Load<GameObject>("Toon/Hats/Hat_" + (string.IsNullOrEmpty(overrideHat) ? HatFor(r) : overrideHat));
            if (pf == null) pf = Resources.Load<GameObject>("Toon/Hats/Hat_" + HatFor(r));
            if (pf == null) return;
            Transform headBone = null; Bounds b = new Bounds(); bool any = false;
            foreach (var t in model.GetComponentsInChildren<Transform>()) if (t.name == "Head" && t.GetComponent<Renderer>() == null) headBone = t;
            foreach (var rr in model.GetComponentsInChildren<Renderer>()) if (rr.enabled && !IsGunNode(rr.name)) { if (!any) { b = rr.bounds; any = true; } else b.Encapsulate(rr.bounds); }
            if (headBone == null || !any) return;
            var hat = Object.Instantiate(pf); hat.name = "美食帽子";
            foreach (var c in hat.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            hat.transform.localScale = Vector3.one * model.transform.lossyScale.y;
            hat.transform.position = new Vector3(headBone.position.x, b.max.y - .05f * model.transform.lossyScale.y, headBone.position.z);
            hat.transform.rotation = model.transform.rotation;
            hat.transform.SetParent(headBone, true);
        }

        public static bool IsGunNode(string n) { foreach (var g in AllGuns) if (g == n) return true; return false; }

        static Material mitt;
        public static Material Mitt { get { if (mitt == null) { mitt = new Material(Shader.Find("Standard")) { color = new Color(.06f, .06f, .07f) }; mitt.SetFloat("_Glossiness", .25f); } return mitt; } }

        // Toon gun for the first-person view / portraits: muzzle along +Z, pistol grip at the pivot.
        public static Transform CreateGun(WeaponSpec w, GunParts parts, bool firstPerson)
        {
            var model = Arena.Model("Toon/Guns/" + GunFor(w));
            if (model == null) return null;
            var pivot = new GameObject("卡通武器 " + w.id).transform;
            model.transform.position = Vector3.zero; model.transform.rotation = Quaternion.identity;
            var rs = model.GetComponentsInChildren<Renderer>();
            Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            float len = GunModel.Length(w) * (firstPerson ? .78f : 1.05f), scale = len / Mathf.Max(.01f, b.size.x);
            var holder = new GameObject("model").transform; holder.SetParent(pivot, false);
            model.transform.SetParent(holder, false);
            // grip sits about 30% from the back of the gun, a little under its centre line
            Vector3 grip = new Vector3(b.min.x + b.size.x * .3f, b.center.y - b.extents.y * .25f, b.center.z);
            model.transform.localPosition = -grip;
            holder.localRotation = Quaternion.Euler(0, -90, 0);   // +X (barrel) → +Z
            holder.localScale = Vector3.one * scale;
            foreach (var c in model.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            float front = (b.max.x - grip.x) * scale, top = (b.max.y - grip.y) * scale;
            parts.muzzle = new GameObject("枪口").transform; parts.muzzle.SetParent(pivot, false); parts.muzzle.localPosition = new Vector3(0, top * .45f, front);
            parts.eject = new GameObject("抛壳口").transform; parts.eject.SetParent(pivot, false); parts.eject.localPosition = new Vector3(.03f, top * .5f, front * .25f);
            parts.laser = parts.muzzle;
            parts.sightLocal = new Vector3(0, top + .01f, front * .2f);
            parts.foregrip = new Vector3(0, -.01f, front * .55f);
            if (firstPerson)
            {
                // chunky black mitts, same as the toon characters' hands
                foreach (var (pos, sc) in new[] { (new Vector3(.005f, -.05f, -.01f), new Vector3(.085f, .085f, .095f)), (parts.foregrip + new Vector3(-.01f, -.03f, 0), new Vector3(.08f, .08f, .09f)) })
                {
                    var h = GameObject.CreatePrimitive(PrimitiveType.Sphere); Object.Destroy(h.GetComponent<Collider>()); h.name = "手套";
                    h.transform.SetParent(pivot, false); h.transform.localPosition = pos; h.transform.localScale = sc;
                    h.GetComponent<Renderer>().sharedMaterial = Mitt;
                }
            }
            return pivot;
        }

        // Body colour = team colour (instant friend/foe read), helmet + shoulder pads = hero colour.
        public static void Recolor(GameObject model, Color team, Color hero)
        {
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (IsGunNode(r.name)) continue;
                var mats = r.materials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string n = System.Text.RegularExpressions.Regex.Replace(mats[i].name.Replace(" (Instance)", ""), @"\.\d+$", "");   // V18：Blender 导出会加 .001 后缀
                    Color? c = null;
                    bool head = r.name == "Head" || r.name.EndsWith("_Head");
                    if (n == "Character_Main" || n == "Hazmat_Main" && !head || n == "Enemy_Red" && !head) c = team;               // body = team colour
                    else if ((n == "Hazmat_Main" || n == "Enemy_Red") && head) c = Color.Lerp(hero, Color.white, .1f);           // helmet = hero colour
                    else if (n == "Grey" && (head || r.name.StartsWith("ShoulderPad") || r.name == "Character_Enemy")) c = Color.Lerp(hero, Color.white, .15f);
                    else if (n == "Pants") c = Color.Lerp(team, new Color(.15f, .15f, .18f), .55f);
                    if (c == null) continue;
                    if (mats[i].HasProperty("baseColorFactor")) mats[i].SetColor("baseColorFactor", c.Value);
                    if (mats[i].HasProperty("_Color")) mats[i].SetColor("_Color", c.Value);
                    if (mats[i].HasProperty("_BaseColor")) mats[i].SetColor("_BaseColor", c.Value);
                }
                r.materials = mats;
            }
        }
    }

    // Black cartoon outline on every fighter (inverted hull drawn after opaque geometry).
    [RequireComponent(typeof(Camera))]
    public class ToonOutline : MonoBehaviour
    {
        CommandBuffer cb; Material mat, enemyMat, mateMat; Camera cam;
        void OnEnable()
        {
            cam = GetComponent<Camera>();
            var baseMat = Resources.Load<Material>("Materials/Outline"); if (baseMat == null) return;
            mat = new Material(baseMat); mat.SetColor("_Color", new Color(.03f, .03f, .05f)); mat.SetFloat("_Width", .012f);
            // V18：敌人红色粗描边，队友蓝色描边
            enemyMat = new Material(baseMat); enemyMat.SetColor("_Color", new Color(1f, .12f, .15f)); enemyMat.SetFloat("_Width", .02f);
            mateMat = new Material(baseMat); mateMat.SetColor("_Color", new Color(.2f, .65f, 1f)); mateMat.SetFloat("_Width", .014f);
            cb = new CommandBuffer { name = "Toon Outline" };
            cam.AddCommandBuffer(CameraEvent.AfterForwardOpaque, cb);
        }
        void OnDisable() { if (cam != null && cb != null) cam.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, cb); }
        void LateUpdate()
        {
            if (cb == null || Match.I == null) return;
            cb.Clear();
            foreach (var f in Match.I.fighters)
            {
                if (f.avatar == null || f == Match.I.player && f.alive) continue;
                var me = Match.I.player; var om = !f.alive || me == null ? mat : f.team == me.team ? mateMat : enemyMat;
                foreach (var r in f.avatar.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy || r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                    Mesh m = r is SkinnedMeshRenderer s ? s.sharedMesh : (r.GetComponent<MeshFilter>() ? r.GetComponent<MeshFilter>().sharedMesh : null);
                    if (m == null) continue;
                    for (int i = 0; i < m.subMeshCount; i++) cb.DrawRenderer(r, om, i);
                }
            }
        }
    }
}
