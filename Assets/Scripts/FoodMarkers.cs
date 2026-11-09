using System.Collections.Generic;
using UnityEngine;

namespace NeonSiege
{
    // ================================================================ V19：能交互的东西一眼看得出来
    // 地上一圈呼吸闪烁的光环 + 头顶上下跳动的「G」气泡（写着能做什么）+ 亮晶晶的小粒子。装饰性的美食没有这些。
    public class InteractMarkers : MonoBehaviour
    {
        class Mk { public GameObject root; public Transform ring, bubble; public TextMesh text; public Renderer bg; public float baseH, seed; public string label; }
        readonly Dictionary<Object, Mk> marks = new Dictionary<Object, Mk>();
        readonly HashSet<Object> seen = new HashSet<Object>();
        static Font font; static Material bgMat;
        public static int Count;

        void LateUpdate()
        {
            var m = Match.I; if (m == null || !ToonAssets.Available) return;
            var p = m.player; var cam = Camera.main;
            seen.Clear();
            foreach (var it in Interactable.All)
            {
                if (it == null || !it.isActiveAndEnabled) continue;
                if (!Describe(it, p, out string label, out Color c, out float h)) continue;
                Show(it, it.transform.position, label, c, h, cam);
            }
            foreach (var ip in IngredientPickup.Live) if (ip != null) Show(ip, ip.transform.position, "走过去捡：" + Ingredients.Name(ip.id), Ingredients.Col(ip.id) * .5f + new Color(.5f, .5f, .5f), 1.55f, cam);
            foreach (var dc in DeliveryCrate.Live) if (dc != null && dc.Landed) Show(dc, dc.transform.position, "外卖！走过去打开", new Color(1f, .6f, .85f), 1.7f, cam);
            foreach (var wd in WeaponDrop.Live) if (wd != null) Show(wd, wd.transform.position, "走过去捡枪：" + wd.w.name, FoodFx.For(wd.w).splat, 1.6f, cam);
            foreach (var mp in MentosPile.All) if (mp != null) Show(mp, mp.transform.position, "用汽水手雷炸我 → 可乐喷泉", new Color(.9f, .7f, .5f), 2.4f, cam);
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
                case Snack s: if (s.Eaten) return false; label = "G 吃掉 · +" + Snack.Heal + " 生命"; c = new Color(1f, .6f, .8f); h = 1.45f; return true;
                case KitchenStation k:
                    h = 2.45f;
                    label = k.kind == KitchenStation.Kind.HotpotHeal ? "G 涮火锅回血" : k.kind == KitchenStation.Kind.MilkTea ? "G 喝奶茶补弹药" : k.kind == KitchenStation.Kind.Oven ? "G 烤枪 · 伤害 +15%" : "G 拿冰护甲";
                    c = k.kind == KitchenStation.Kind.HotpotHeal ? new Color(1f, .45f, .3f) : k.kind == KitchenStation.Kind.MilkTea ? new Color(.85f, .62f, .42f) : k.kind == KitchenStation.Kind.Oven ? new Color(1f, .75f, .25f) : new Color(.6f, .9f, 1f);
                    return true;
                case CookPot cp:
                    if (Match.Mode != GameMode.Cook) return false;
                    h = 4.2f; bool mine = p != null && p.team == cp.team;
                    label = mine ? "把食材送到这里" : "G 偷对面的食材"; c = Config.TeamColor(cp.team); return true;
                case MedStation md: if (md.Used) return false; label = "G 医疗站 · 回血回甲"; c = new Color(.4f, 1f, .6f); h = 2.4f; return true;
                case PowerBox _: label = "G 拉电闸 · 包点断电"; c = new Color(1f, .85f, .3f); h = 2.2f; return true;
                case Shutter _: label = "G 开关卷帘门"; c = new Color(.5f, .85f, 1f); h = 2.6f; return true;
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
            if (mk.label != label) { mk.label = label; mk.text.text = label; mk.bg.transform.localScale = new Vector3(label.Length * .19f + .45f, .42f, 1f); }
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
            arrow.text = "▼"; arrow.font = font; arrow.anchor = TextAnchor.MiddleCenter; arrow.fontSize = 64; arrow.characterSize = .04f; arrow.color = c;
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
