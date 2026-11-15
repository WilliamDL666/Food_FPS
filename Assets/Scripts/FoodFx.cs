using System.Collections.Generic;
using UnityEngine;

namespace NeonSiege
{
    // V17：美食 FPS 的打击感 —— 每把枪打出对应的食物，命中溅酱汁，击杀爆彩带
    public static class FoodFx
    {
        public struct Theme { public string ammo, sound, label; public Color color, splat; public float ammoSize, splatSize; }

        public static Theme For(WeaponSpec w)
        {
            switch (w != null ? w.id : "")
            {
                case "SMG-7": return new Theme { ammo = "Ammo_Boba", sound = "food_boba", label = "珍珠", color = new Color(.35f, .2f, .14f), splat = new Color(.82f, .6f, .42f), ammoSize = .07f, splatSize = .28f };
                case "AR-9": return new Theme { ammo = "Ammo_Popcorn", sound = "food_popcorn", label = "爆米花", color = new Color(1f, .93f, .7f), splat = new Color(1f, .85f, .35f), ammoSize = .09f, splatSize = .32f };
                case "LMG-2": return new Theme { ammo = "Ammo_Sushi", sound = "food_sushi", label = "寿司", color = new Color(1f, .55f, .35f), splat = new Color(1f, .55f, .35f), ammoSize = .08f, splatSize = .3f };
                case "DMR-3": return new Theme { ammo = "Ammo_Haw", sound = "food_haw", label = "山楂", color = new Color(.95f, .1f, .15f), splat = new Color(.95f, .12f, .2f), ammoSize = .09f, splatSize = .45f };
                case "FS-12": return new Theme { ammo = "Ammo_Dumpling", sound = "food_dumpling", label = "饺子", color = new Color(1f, .97f, .88f), splat = new Color(1f, .95f, .82f), ammoSize = .07f, splatSize = .22f };
                case "BR-5": return new Theme { ammo = "Ammo_Cream", sound = "food_cream", label = "奶油", color = new Color(1f, .6f, .8f), splat = new Color(1f, .62f, .82f), ammoSize = .08f, splatSize = .34f };
                default: return new Theme { ammo = "Ammo_Ketchup", sound = "food_ketchup", label = "番茄酱", color = new Color(.95f, .15f, .12f), splat = new Color(.95f, .15f, .12f), ammoSize = .07f, splatSize = .3f };
            }
        }

        // ---------------------------------------------------------------- 飞行中的食物子弹
        static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        public static GameObject AmmoModel(WeaponSpec w)
        {
            var th = For(w);
            if (!prefabs.TryGetValue(th.ammo, out var pf)) prefabs[th.ammo] = pf = Resources.Load<GameObject>("Toon/FoodGuns/" + th.ammo);
            if (pf == null) return null;
            var o = Object.Instantiate(pf);
            foreach (var c in o.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            foreach (var r in o.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
            o.transform.localScale = Vector3.one * th.ammoSize;
            return o;
        }

        static Material trailMat;
        public static Material TrailMaterial { get { if (trailMat == null) trailMat = new Material(Shader.Find("Sprites/Default")); return trailMat; } }

        // ---------------------------------------------------------------- 酱汁溅射贴花
        static Texture2D splatTex;
        static readonly Dictionary<int, Material> splatMats = new Dictionary<int, Material>();
        static Texture2D SplatTexture
        {
            get
            {
                if (splatTex != null) return splatTex;
                const int N = 128; var px = new Color[N * N]; var rnd = new System.Random(7);
                var drops = new List<Vector3>();
                for (int i = 0; i < 9; i++) { float a = (float)rnd.NextDouble() * 6.283f, d = .36f + (float)rnd.NextDouble() * .12f; drops.Add(new Vector3(Mathf.Cos(a) * d, Mathf.Sin(a) * d, .03f + (float)rnd.NextDouble() * .05f)); }
                float p1 = (float)rnd.NextDouble() * 6, p2 = (float)rnd.NextDouble() * 6;
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float u = (x + .5f) / N * 2 - 1, v = (y + .5f) / N * 2 - 1, r = Mathf.Sqrt(u * u + v * v), a = Mathf.Atan2(v, u);
                        float edge = .3f + .05f * Mathf.Sin(a * 5 + p1) + .035f * Mathf.Sin(a * 9 + p2) + .02f * Mathf.Sin(a * 14);
                        float alpha = Mathf.Clamp01((edge - r) * 40f);
                        foreach (var d in drops) alpha = Mathf.Max(alpha, Mathf.Clamp01((d.z - Vector2.Distance(new Vector2(u, v), d)) * 60f));
                        float shade = 1f - .12f * Mathf.Clamp01(r / .3f) + (r < .12f && u < 0 && v > 0 ? .15f : 0);   // soft highlight = glossy sauce
                        px[y * N + x] = new Color(shade, shade, shade, alpha);
                    }
                splatTex = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "酱汁溅射" };
                splatTex.SetPixels(px); splatTex.Apply();
                return splatTex;
            }
        }

        public static GameObject Splat(Vector3 pos, Vector3 normal, Color c, float size)
        {
            int key = Mathf.RoundToInt(c.r * 31) * 1024 + Mathf.RoundToInt(c.g * 31) * 32 + Mathf.RoundToInt(c.b * 31);
            if (!splatMats.TryGetValue(key, out var m))
            {
                m = new Material(Shader.Find("Sprites/Default")) { mainTexture = SplatTexture, color = c, renderQueue = 3001 };
                splatMats[key] = m;
            }
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); q.name = "酱汁";
            Object.DestroyImmediate(q.GetComponent<Collider>());   // 立即移除：同一帧的射线不能被贴花挡住
            q.transform.position = pos + normal * (.008f + Random.value * .006f);
            q.transform.rotation = Quaternion.LookRotation(-normal) * Quaternion.Euler(0, 0, Random.Range(0, 360));
            q.transform.localScale = Vector3.one * size * Random.Range(.8f, 1.25f);
            var r = q.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            q.AddComponent<Decal>();
            q.AddComponent<SplatPop>();
            return q;
        }

        public static readonly Color[] Confetti = { new Color(1f, .35f, .55f), new Color(.35f, .8f, 1f), new Color(1f, .88f, .25f), new Color(.45f, 1f, .6f), new Color(.8f, .5f, 1f), new Color(1f, .6f, .3f) };

        // 击杀：角色"噗"地变成一团彩带和糖粒
        public static void KillPop(Vector3 at)
        {
            for (int i = 0; i < Confetti.Length; i++) Fx.Burst(at, Vector3.up, Confetti[i], 14, 6.5f, .07f, 1.2f, .9f);
            Fx.Burst(at, Vector3.up, Color.white, 10, 3f, .22f, .5f, -.1f, Fx.Smoke);
            Sfx.Play("food_confetti", at, 1f, Random.Range(.95f, 1.08f), 1f, 40f);
        }

        // 命中墙面 / 地面
        public static void Impact(Vector3 point, Vector3 normal, WeaponSpec w)
        {
            var th = For(w);
            Splat(point, normal, th.splat, th.splatSize * (w != null && w.pellets > 1 ? .8f : 1f));
            Fx.Burst(point, normal, th.splat, 6, 2.2f, .045f, .45f, 1.2f);
            if (Random.value < .6f) Sfx.Play("food_splat", point, .5f, Random.Range(.9f, 1.15f), 1f, 28f);
        }
    }

    // 贴花出现时"啪"地弹开
    public class SplatPop : MonoBehaviour
    {
        Vector3 full; float t;
        void Start() { full = transform.localScale; transform.localScale = full * .3f; }
        void Update()
        {
            t += Time.deltaTime * 9f;
            float k = t >= 1f ? 1f : 1f + Mathf.Sin(t * Mathf.PI) * .25f * (1 - t) + (t - 1f) * (1 - t) * .7f;
            transform.localScale = full * Mathf.Clamp(k, .3f, 1.3f);
            if (t >= 1.4f) { transform.localScale = full; enabled = false; }
        }
    }

    // 美食枪动画：开火时果冻般压扁回弹；奶茶液面跟弹药走；寿司转盘旋转；糖葫芦打一颗少一颗
    public class FoodGunAnim : MonoBehaviour
    {
        public Fighter owner;
        public Transform tea, spin; public Transform[] haws;
        Vector3 baseScale, teaScale; int lastShots = -1; float squashT = 9f, spinSpeed;

        void Start() { baseScale = transform.localScale; if (tea != null) teaScale = tea.localScale; }

        public void OnShot() { squashT = 0; spinSpeed = Mathf.Min(spinSpeed + 500f, 1400f); }

        void Update()
        {
            if (owner != null)
            {
                if (lastShots >= 0 && owner.stats.shots != lastShots) OnShot();
                lastShots = owner.stats.shots;
                var w = owner.W; float frac = w != null && w.magazine > 0 ? Mathf.Clamp01((float)owner.ammo / w.magazine) : 1f;
                if (tea != null) tea.localScale = new Vector3(teaScale.x, teaScale.y * Mathf.Max(.04f, frac), teaScale.z);
                if (haws != null)
                {
                    int show = Mathf.CeilToInt(frac * haws.Length);
                    for (int i = 0; i < haws.Length; i++) if (haws[i] != null) haws[i].gameObject.SetActive(i >= haws.Length - show);
                }
            }
            if (spin != null) { spin.Rotate(0, 0, spinSpeed * Time.deltaTime, Space.Self); spinSpeed = Mathf.MoveTowards(spinSpeed, 0, Time.deltaTime * 900f); }
            squashT += Time.deltaTime;
            float s = Mathf.Exp(-squashT * 14f) * Mathf.Cos(squashT * 38f);
            transform.localScale = new Vector3(baseScale.x * (1 + .14f * s), baseScale.y * (1 + .1f * s), baseScale.z * (1 - .16f * s));
        }
    }
}
