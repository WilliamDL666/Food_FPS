using System.Collections.Generic;
using UnityEngine;

namespace NeonSiege
{
    // ================================================================ V19：能交互的东西一眼看得出来
    // 地上一圈呼吸闪烁的光环 + 头顶上下跳动的「G」气泡（写着能做什么）+ 亮晶晶的小粒子。装饰性的美食没有这些。
    public class InteractMarkers : MonoBehaviour
    {
        class Mk { public GameObject root; public Transform ring, bubble; public TextMesh text, arrow; public Renderer bg; public float baseH, seed; public string label; public Color color; }
        readonly Dictionary<Object, Mk> marks = new Dictionary<Object, Mk>();
        readonly HashSet<Object> seen = new HashSet<Object>();
        static Font font; static Material bgMat;
        public static int Count;
        public static InteractMarkers I;
        void Awake() { I = this; }
        // V21：测试读气泡文字
        public string LabelOf(Object key) { return marks.TryGetValue(key, out var mk) && mk.root != null ? mk.label : null; }

        void LateUpdate()
        {
            var m = Match.I; if (m == null || !ToonAssets.Available) return;
            var p = m.player; var cam = Camera.main;
            seen.Clear();
            foreach (var it in Interactable.All)
            {
                if (it == null || !it.isActiveAndEnabled) continue;
                if (!Describe(it, p, out string label, out Color c, out float h)) continue;
                // V22 第三期：能和你的技能联动的交互物（卷帘门 / 蒸笼门 → 铁幕，蒸汽阀 → 幻频），12 m 内气泡加一行金色联动
                string ll = Links.InteractLine(p, it);
                if (ll != null && Links.Flat(p.transform.position, it.transform.position) <= FoodGuide.DetailRange) { label += "\n" + ll; c = Links.Gold; }
                Show(it, it.transform.position, label, c, h, cam);
            }
            // V22 第三期：不是交互物的联动点（火锅 / 弹射板 / 蛋糕墙 / 破口 / 侧门），只给能联动的英雄、12 m 内显示金色气泡
            if (p != null && p.alive)
                foreach (var e in Links.ElementsFor(p.role))
                {
                    if (e.interact || e.key == null || Links.Flat(e.pos, p.transform.position) > FoodGuide.DetailRange) continue;
                    Show(e.key, e.pos, e.label, Links.Gold, e.h, cam);
                }
            // V21：食材气泡写配方（近处），手上有一样时绿色 = 能凑成，灰色 = 凑不成（要按住 G 才捡）
            foreach (var ip in IngredientPickup.Live)
                if (ip != null)
                {
                    float d = p != null ? Vector3.Distance(p.transform.position, ip.transform.position) : 0f;
                    Show(ip, ip.transform.position, FoodGuide.IngredientLabel(ip.id, p, d, out Color ic), ic, 1.55f, cam);
                }
            foreach (var dc in DeliveryCrate.Live)
                if (dc != null) Show(dc, dc.Landed ? dc.transform.position : dc.Target, dc.Landed ? "外卖！随机变身+满弹+酥脆" : "外卖落点 · 快去抢", new Color(1f, .6f, .85f), 1.7f, cam);
            foreach (var wd in WeaponDrop.Live) if (wd != null) Show(wd, wd.transform.position, "走过去捡枪：" + wd.w.name, FoodFx.For(wd.w).splat, 1.6f, cam);
            foreach (var mp in MentosPile.All) if (mp != null) Show(mp, mp.transform.position, "按 4 扔汽水罐炸我→喷泉冲天", new Color(.9f, .7f, .5f), 2.4f, cam);
            // V21：敌方技能产物，只对敌人显示
            if (p != null)
            {
                foreach (var dr in Drone.Live) if (dr != null && dr.owner != null && dr.owner.team != p.team) Show(dr, dr.transform.position, "芥末签 · 一枪打掉我", Skills.Mustard, .7f, cam);
                foreach (var bc in BlastCharge.Live) if (bc != null && bc.owner != null && bc.owner.team != p.team) Show(bc, bc.transform.position, "点火纸杯 · 快跑！", new Color(1f, .3f, .2f), 1f, cam);
            }
            var dead = new List<Object>();
            foreach (var kv in marks) if (kv.Key == null || !seen.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (var k in dead) { if (marks[k].root != null) Destroy(marks[k].root); marks.Remove(k); }
            Count = marks.Count;
        }

        static bool Describe(Interactable it, Fighter p, out string label, out Color c, out float h)
        {
            label = null; c = new Color(1f, .7f, .85f); h = 1.6f;
            switch (it)
            {
                case Snack s:
                {
                    if (s.Eaten) return false;
                    label = FoodGuide.SnackLabel(s, p, out bool ok); c = ok ? new Color(1f, .6f, .8f) : FoodGuide.Grey; h = 1.45f; return true;   // 满血变灰
                }
                case KitchenStation k:
                {
                    h = 2.45f;
                    label = FoodGuide.StationLabel(k, p, out bool ok);   // 冷却中变灰并写剩余秒数
                    c = !ok ? FoodGuide.Grey : k.kind == KitchenStation.Kind.HotpotHeal ? new Color(1f, .45f, .3f) : k.kind == KitchenStation.Kind.MilkTea ? new Color(.85f, .62f, .42f) : k.kind == KitchenStation.Kind.Oven ? new Color(1f, .75f, .25f) : new Color(.6f, .9f, 1f);
                    return true;
                }
                case CookPot cp:
                    if (Match.Mode != GameMode.Cook) return false;
                    h = 4.2f; bool mine = p != null && p.team == cp.team;
                    label = mine ? "把食材送到这里" : "G 偷对面的食材"; c = Config.TeamColor(cp.team); return true;
                case MedStation md: if (md.Used) return false; label = "G 医疗站 · 回血回甲"; c = new Color(.4f, 1f, .6f); h = 2.4f; return true;
                case PowerBox _: label = "G 拉电闸 · 包点断电"; c = new Color(1f, .85f, .3f); h = 2.2f; return true;
                case Shutter sh:
                    if (sh.gate)   // V22 第二期：蒸笼门（默认开，守方可盖上）
                    {
                        bool can = p != null && sh.Usable(p);
                        label = sh.open ? (can ? "G 盖上蒸笼门 · 巨响" : "蒸笼门 · 守方可以盖上") : "G 掀开蒸笼门";
                        c = new Color(1f, .78f, .45f); h = 4.3f; return true;
                    }
                    label = "G 开关卷帘门"; c = new Color(.5f, .85f, 1f); h = 2.6f; return true;
                case SteamValve v:   // V22 第二期：蒸汽阀
                    label = v.Active ? "白雾中 · 挡住视线" : v.CooldownLeft > 0 ? "蒸汽阀 · 还要 " + Mathf.CeilToInt(v.CooldownLeft) + " 秒" : "G 拧蒸汽阀 · 白雾挡视线";
                    c = v.CooldownLeft > 0 && !v.Active ? FoodGuide.Grey : new Color(.85f, .9f, 1f); h = 2.3f; return true;
                case Terminal _: label = "G 门禁终端"; c = new Color(.5f, .85f, 1f); h = 2.2f; return true;
            }
            return false;
        }

        void Show(Object key, Vector3 pos, string label, Color c, float h, Camera cam)
        {
            seen.Add(key);
            if (!marks.TryGetValue(key, out var mk) || mk.root == null) { mk = Create(c, h); marks[key] = mk; }
            Vector3 ground = new Vector3(pos.x, pos.y + .03f, pos.z);
            mk.root.transform.position = ground;
            float dist = cam != null ? Vector3.Distance(cam.transform.position, ground) : 10f;
            bool vis = dist < 38f;
            if (mk.root.activeSelf != vis) mk.root.SetActive(vis);
            if (!vis) return;
            if (mk.label != label)
            {
                mk.label = label; mk.text.text = label;
                var lines = label.Split('\n'); int longest = 0; foreach (var ln in lines) longest = Mathf.Max(longest, ln.Length);   // V22 第三期：两行气泡（交互 + 联动）
                mk.bg.transform.localScale = new Vector3(longest * .19f + .45f, .42f * lines.Length, 1f);
            }
            if (mk.color != c) { mk.color = c; mk.text.color = Color.Lerp(c, Color.white, .35f); if (mk.arrow != null) mk.arrow.color = c; }
            float t = Time.time + mk.seed;
            mk.ring.localScale = Vector3.one * (1.25f + .12f * Mathf.Sin(t * 4f));
            float s = Mathf.Clamp(dist * .045f, .7f, 1.6f);
            mk.bubble.localPosition = Vector3.up * (h + .1f * Mathf.Sin(t * 3f));
            mk.bubble.localScale = Vector3.one * s;
            if (cam != null) mk.bubble.rotation = Quaternion.LookRotation(mk.bubble.position - cam.transform.position);
        }

        Mk Create(Color c, float h)
        {
            if (font == null) font = Resources.Load<Font>("NotoSansSC");
            if (bgMat == null) bgMat = new Material(Shader.Find("Sprites/Default")) { color = new Color(.18f, .05f, .14f, .82f) };
            var mk = new Mk { root = new GameObject("交互标记"), baseH = h, seed = Random.value * 10f };
            var ring = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(ring.GetComponent<Collider>()); ring.name = "光环";
            ring.transform.SetParent(mk.root.transform, false); ring.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var rr = ring.GetComponent<Renderer>(); rr.sharedMaterial = ToonAssets.RingMaterial(Color.Lerp(c, Color.white, .25f)); rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mk.ring = ring.transform;
            mk.bubble = new GameObject("气泡").transform; mk.bubble.SetParent(mk.root.transform, false);
            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(bg.GetComponent<Collider>()); bg.transform.SetParent(mk.bubble, false); bg.transform.localPosition = new Vector3(0, 0, .02f);
            mk.bg = bg.GetComponent<Renderer>(); mk.bg.sharedMaterial = bgMat; mk.bg.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var arrow = new GameObject("箭头").AddComponent<TextMesh>(); arrow.transform.SetParent(mk.bubble, false); arrow.transform.localPosition = new Vector3(0, -.36f, 0);
            arrow.text = "▼"; arrow.font = font; arrow.anchor = TextAnchor.MiddleCenter; arrow.fontSize = 64; arrow.characterSize = .04f; arrow.color = c; mk.arrow = arrow; mk.color = c;
            mk.text = new GameObject("文字").AddComponent<TextMesh>(); mk.text.transform.SetParent(mk.bubble, false);
            mk.text.font = font; mk.text.anchor = TextAnchor.MiddleCenter; mk.text.fontSize = 64; mk.text.characterSize = .035f; mk.text.color = Color.Lerp(c, Color.white, .35f);
            if (font != null) { var wt = Arena.WorldText(font); mk.text.GetComponent<MeshRenderer>().sharedMaterial = wt; arrow.GetComponent<MeshRenderer>().sharedMaterial = wt; }
            var sp = new GameObject("闪光"); sp.transform.SetParent(mk.root.transform, false); sp.transform.localPosition = Vector3.up * .3f;
            var ps = sp.AddComponent<ParticleSystem>(); var main = ps.main; main.startLifetime = 1.2f; main.startSpeed = .5f; main.startSize = new ParticleSystem.MinMaxCurve(.05f, .12f);
            main.startColor = Color.Lerp(c, Color.white, .5f); main.maxParticles = 20; main.gravityModifier = -.15f;
            var em = ps.emission; em.rateOverTime = 7; var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = .6f; sh.rotation = new Vector3(-90, 0, 0);
            sp.GetComponent<ParticleSystemRenderer>().material = Fx.Additive;
            return mk;
        }
    }

    // 打中美食道具时像果冻一样抖一下
    public class FoodJiggle : MonoBehaviour
    {
        Transform target; Vector3 baseScale; float t = 9f;
        void Start() { target = transform.childCount > 0 ? transform.GetChild(0) : transform; baseScale = target.localScale; }
        public void Hit() { if (target == null) Start(); t = 0; }
        void Update()
        {
            if (target == null || t > 1.5f) return;
            t += Time.deltaTime;
            float w = Mathf.Exp(-t * 6f) * Mathf.Sin(t * 28f);
            target.localScale = new Vector3(baseScale.x * (1 + .1f * w), baseScale.y * (1 - .12f * w), baseScale.z * (1 + .1f * w));
            if (t > 1.5f) target.localScale = baseScale;
        }
    }

    // 天上慢慢飘的棉花糖云
    public class CloudDrift : MonoBehaviour
    {
        public float speed = 1.2f; float bobSeed;
        void Start() { bobSeed = Random.value * 10f; }
        void Update()
        {
            var p = transform.position; p.x += speed * Time.deltaTime; if (p.x > 140f) p.x = -140f;
            p.y += Mathf.Sin(Time.time * .3f + bobSeed) * .2f * Time.deltaTime; transform.position = p;
        }
    }
}
