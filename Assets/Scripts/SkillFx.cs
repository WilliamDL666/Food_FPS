using System.Collections.Generic;
using UnityEngine;

namespace NeonSiege
{
    // ================================================================ V23 c：技能 / 大招的美食化表现（设计稿_V23 §2 + 追加需求 A2）
    // 只改表现：规则、数值、生效时间、可听距离、透明度全部不动（§2.1 统一约束）。
    //   SkillPop  —— 给施放者的"生效了"飘字（准星下方）+ 世界里的"呛！"字 + 屏幕边缘的气泡 / 糖丝 / 糖霜
    //   SkillHand —— 第一人称左手动作：只动左手，时长 ≤ 现有锁定时间，枪保持可开火；手里有对应的道具（汽水罐 / 爆米花机 / 布丁 / 签筒 / 打火机 / 棉花糖棒）
    //   UltFx     —— 6 个大招的施放瞬间 / 持续期间 / 结束：特效 + 音效 + 镜头反馈（幅度 × Tune.ShakeScale）+ 播报

    public static class SkillPop
    {
        class Pop { public string text; public Color c; public float born; public Fighter who; }
        static readonly List<Pop> pops = new List<Pop>();
        public static string Last = ""; public static float LastAt = -9; public static int Count;
        public static readonly List<string> History = new List<string>();   // 测试读
        static GUIStyle style; static Drawer drawer;
        public const float Life = 1.5f;

        // 给自己的结果字（和 V22 "结果字 = 遥测有效判定" 同一套）；AI 不画
        public static void Show(Fighter f, string text, Color c)
        {
            if (f == null) return;
            Last = text; LastAt = Time.time; Count++; History.Add(text); if (History.Count > 64) History.RemoveAt(0);
            if (!f.human) return;
            foreach (var p in pops) if (p.text.Length > 0 && p.text[0] == text[0] && Time.time - p.born < .5f) { p.text = text; p.born = Time.time; return; }   // 同一句连续刷新（"布丁吃下 N 伤害"）
            pops.Add(new Pop { text = text, c = c, born = Time.time, who = f });
            if (pops.Count > 4) pops.RemoveAt(0);
            Ensure();
        }
        static void Ensure() { if (drawer == null) drawer = new GameObject("技能飘字").AddComponent<Drawer>(); }
        public static void EnsureOverlay() { Ensure(); }

        // 世界里的字（敌人头顶"呛！"）：跟着目标、1.1 s 后消失
        public static void World(Vector3 at, string text, Color c, Transform follow = null, float life = 1.1f)
        {
            var go = new GameObject("世界飘字 " + text);
            go.transform.position = at;
            var tm = go.AddComponent<TextMesh>(); var font = Resources.Load<Font>("NotoSansSC");
            tm.font = font; tm.fontSize = 72; tm.characterSize = .09f; tm.anchor = TextAnchor.MiddleCenter; tm.text = text; tm.color = c;
            if (font != null) go.GetComponent<MeshRenderer>().sharedMaterial = Arena.WorldText(font);
            go.AddComponent<Billboard>();
            var w = go.AddComponent<WorldPop>(); w.follow = follow; w.offset = follow != null ? at - follow.position : Vector3.zero; w.life = life;
        }
        public class WorldPop : MonoBehaviour
        {
            public Transform follow; public Vector3 offset; public float life = 1.1f; float t;
            void Update()
            {
                t += Time.deltaTime;
                if (follow != null) transform.position = follow.position + offset + Vector3.up * t * .4f; else transform.position += Vector3.up * Time.deltaTime * .4f;
                var tm = GetComponent<TextMesh>(); if (tm != null) { var c = tm.color; c.a = Mathf.Clamp01((life - t) / .4f); tm.color = c; }
                if (t >= life) Destroy(gameObject);
            }
        }

        // 屏幕边缘覆盖：汽水冲刺的气泡流 / 棉花糖迷雾里的糖丝 / 糖霜风暴里的糖霜（都只贴边 15–20%，中心不挡 = 不改"不挡视线"）
        public static float EdgeBubbles, EdgeSugar, EdgeFrost;   // 0..1，由各系统每帧写，Drawer 读后衰减
        class Drawer : MonoBehaviour
        {
            Texture2D dot;
            void Awake() { DontDestroyOnLoad(gameObject); }
            void OnGUI()
            {
                if (Event.current.type != EventType.Repaint) return;
                var m = Match.I; var p = m != null ? m.player : null;
                if (p == null) return;
                if (style == null) { style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 24, fontStyle = FontStyle.Bold }; var f = Resources.Load<Font>("NotoSansSC"); if (f != null) style.font = f; }
                if (dot == null) dot = Icons.Ring;
                int W = Screen.width, H = Screen.height;
                // 边缘气泡（汽水冲刺）
                if (EdgeBubbles > .01f)
                {
                    var rnd = new System.Random(7);
                    for (int i = 0; i < 26; i++)
                    {
                        float ang = (float)rnd.NextDouble() * 6.283f, rr = .42f + (float)rnd.NextDouble() * .1f;
                        float drift = Mathf.Repeat(Time.time * 1.6f + i * .37f, 1f);
                        float x = W * .5f + Mathf.Cos(ang) * W * (rr + drift * .08f), y = H * .5f + Mathf.Sin(ang) * H * (rr + drift * .08f);
                        float s = 10 + (float)rnd.NextDouble() * 16;
                        Icons.Draw(new Rect(x - s / 2, y - s / 2, s, s), dot, new Color(1f, .78f, .4f, EdgeBubbles * (1 - drift) * .8f));
                    }
                }
                // 糖丝（棉花糖迷雾里）：四角几条粉色细线，中心不挡
                if (EdgeSugar > .01f)
                {
                    var c = new Color(1f, .62f, .92f, EdgeSugar * .55f);
                    for (int i = 0; i < 10; i++)
                    {
                        float k = i / 10f, sway = Mathf.Sin(Time.time * 2f + i) * 8f;
                        Strip(new Rect(W * k, 0, 2, H * (.12f + .06f * Mathf.Sin(i * 1.7f)) + sway), c);
                        Strip(new Rect(W * (k + .05f), H - H * (.1f + .07f * Mathf.Cos(i * 1.3f)) - sway, 2, H), c);
                    }
                    for (int i = 0; i < 6; i++) { Strip(new Rect(0, H * (i / 6f) + Mathf.Sin(Time.time + i) * 6f, W * .08f, 2), c); Strip(new Rect(W - W * .07f, H * (i / 6f + .08f), W, 2), c); }
                }
                // 糖霜（糖霜风暴里）：边缘一圈白色糖粒
                if (EdgeFrost > .01f)
                {
                    var rnd = new System.Random(11);
                    for (int i = 0; i < 70; i++)
                    {
                        float ang = (float)rnd.NextDouble() * 6.283f, rr = .4f + (float)rnd.NextDouble() * .12f;
                        float x = W * .5f + Mathf.Cos(ang) * W * rr + Mathf.Sin(Time.time * 3f + i) * 4f, y = H * .5f + Mathf.Sin(ang) * H * rr + Mathf.Repeat(Time.time * 40f + i * 13f, 30f);
                        float s = 3 + (float)rnd.NextDouble() * 5;
                        Strip(new Rect(x, y, s, s), new Color(1f, 1f, 1f, EdgeFrost * .7f));
                    }
                }
                EdgeBubbles = Mathf.MoveTowards(EdgeBubbles, 0, Time.deltaTime * 2.5f);
                EdgeSugar = Mathf.MoveTowards(EdgeSugar, 0, Time.deltaTime * 2f);
                EdgeFrost = Mathf.MoveTowards(EdgeFrost, 0, Time.deltaTime * 2f);
                // 飘字：准星下方 60 px，往上飘并淡出
                for (int i = pops.Count - 1; i >= 0; i--)
                {
                    var pp = pops[i]; float age = Time.time - pp.born;
                    if (age > Life || pp.who != p) { pops.RemoveAt(i); continue; }
                    float a = age < .15f ? age / .15f : Mathf.Clamp01((Life - age) / .4f);
                    float scale = age < .15f ? 1.25f - age / .15f * .25f : 1f;
                    var r = new Rect(W * .5f - 200, H * .5f + 58 - age * 22f - i * 28f, 400, 32);
                    style.fontSize = Mathf.RoundToInt(24 * scale);
                    var o = GUI.color; GUI.color = new Color(0, 0, 0, a * .7f); GUI.Label(new Rect(r.x + 1, r.y + 2, r.width, r.height), pp.text, style);
                    GUI.color = new Color(pp.c.r, pp.c.g, pp.c.b, a); GUI.Label(r, pp.text, style); GUI.color = o;
                }
            }
            void Strip(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = o; }
        }
    }

    // ---------------------------------------------------------------- 第一人称左手动作
    public class SkillHand : MonoBehaviour
    {
        public Fighter owner; public Role role; public float start = -9, dur; Transform prop; Role propRole; bool opened;
        public static readonly Dictionary<Role, float> MaxDur = new Dictionary<Role, float>();   // 测试读：每个动作的时长（≤ 锁定时间）
        public static float LastDur; public static Role LastRole; public static int Plays;
        public bool Active { get { return Time.time - start < dur; } }
        public float K { get { return dur > 0 ? Mathf.Clamp01((Time.time - start) / dur) : 1f; } }

        public static SkillHand Of(Fighter f) { if (f == null) return null; var h = f.GetComponent<SkillHand>(); if (h == null) { h = f.gameObject.AddComponent<SkillHand>(); h.owner = f; } return h; }
        // 时长：冲刺前摇 = Config.DashWindup（0.15）、威化部署 = Config.CoverDeploy（0.6）；其余"按下即生效"的技能只动左手 0.3 s，枪随时能开火
        public static float Duration(Role r)
        {
            switch (r) { case Role.Falcon: return Config.DashWindup; case Role.Rivet: return Config.CoverDeploy; default: return .3f; }
        }
        public static void Play(Fighter f, Role r)
        {
            if (f == null || !Skills.Food) return;
            var h = Of(f); h.role = r; h.start = Time.time; h.dur = Duration(r); h.opened = false;
            MaxDur[r] = h.dur; LastDur = h.dur; LastRole = r; Plays++;
            if (!f.human) return;
            h.BuildProp(r);
            if (r == Role.Rivet) f.ViewNudge(1.6f, 0f, 0f);   // 部署：镜头轻微下点头 1 次
        }

        // 左手相对"护木休息位"的偏移（枪 pivot 空间：x 右 / y 上 / z 前），每个技能一条曲线
        public Vector3 Offset()
        {
            if (!Active) { if (prop != null) prop.gameObject.SetActive(false); return Vector3.zero; }
            float k = K; float s = Mathf.Sin(k * Mathf.PI);
            switch (role)
            {
                case Role.Falcon:   // 掏出汽水罐摇两下，拇指"啵"开拉环
                    if (!opened && k > .6f) { opened = true; Sfx.Play("food_can_open", owner.transform.position, .8f, 1f, owner.human ? .25f : 1f, 20f); }
                    return new Vector3(.05f, .07f * s, -.03f) + new Vector3(Mathf.Sin(k * Mathf.PI * 4f) * .012f, 0, 0);
                case Role.Rivet:    // 爆米花机倒过来"挤"，三下
                    return new Vector3(0, -.03f * s, .07f * s) + new Vector3(0, Mathf.Abs(Mathf.Sin(k * Mathf.PI * 3f)) * -.012f, 0);
                case Role.Ironwall: // 往前一推
                    return new Vector3(.01f, .01f, .12f * s);
                case Role.Raven:    // 从签筒抽签 → 抹芥末 → 甩出
                    return k < .45f ? new Vector3(-.05f, -.07f, -.08f) * Mathf.Sin(k / .45f * Mathf.PI) : new Vector3(.02f, .03f, .11f) * Mathf.Sin((k - .45f) / .55f * Mathf.PI);
                case Role.Ember:    // 打火机"咔"点蜡烛 → 下手抛
                    return k < .4f ? new Vector3(.04f, .04f, .02f) * Mathf.Sin(k / .4f * Mathf.PI) : new Vector3(0, -.03f, .13f) * Mathf.Sin((k - .4f) / .6f * Mathf.PI);
                default:            // 转一根棉花糖棒，往前一甩
                    return new Vector3(Mathf.Sin(k * Mathf.PI * 2f) * .03f, .02f * s, .1f * Mathf.Clamp01((k - .5f) * 2f) * (1 - Mathf.Clamp01((k - .85f) / .15f)));
            }
        }

        void BuildProp(Role r)
        {
            if (prop != null && propRole == r) { prop.gameObject.SetActive(true); return; }
            if (prop != null) Destroy(prop.gameObject);
            propRole = r; prop = new GameObject("技能道具 " + r).transform;
            Material M(Color c, float e = 1.2f) { return Arena.Glow(c, Mathf.Max(e, 1.2f)); }   // V23 打磨：道具带自发光，第一人称暗处也看得清
            Transform P(PrimitiveType t, Vector3 lp, Vector3 s, Material m, Vector3 euler = default)
            {
                var g = GameObject.CreatePrimitive(t); Destroy(g.GetComponent<Collider>()); g.transform.SetParent(prop, false);
                g.transform.localPosition = lp; g.transform.localScale = s; g.transform.localEulerAngles = euler;
                var rr = g.GetComponent<Renderer>(); rr.sharedMaterial = m; rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; return g.transform;
            }
            switch (r)
            {
                case Role.Falcon:
                    P(PrimitiveType.Cylinder, Vector3.zero, new Vector3(.034f, .03f, .034f), M(new Color(.9f, .15f, .2f)));
                    P(PrimitiveType.Cylinder, new Vector3(0, .031f, 0), new Vector3(.03f, .002f, .03f), M(new Color(.85f, .85f, .9f)));
                    P(PrimitiveType.Cube, new Vector3(.008f, .035f, 0), new Vector3(.012f, .002f, .006f), M(new Color(.8f, .8f, .85f)));   // 拉环
                    break;
                case Role.Rivet:
                    P(PrimitiveType.Cube, Vector3.zero, new Vector3(.05f, .05f, .05f), M(new Color(.95f, .3f, .3f)));
                    P(PrimitiveType.Cube, new Vector3(0, .018f, 0), new Vector3(.052f, .012f, .052f), M(Color.white));
                    P(PrimitiveType.Sphere, new Vector3(0, -.03f, .02f), new Vector3(.022f, .022f, .022f), M(new Color(1f, .97f, .9f)));   // 奶油团
                    break;
                case Role.Ironwall:
                    P(PrimitiveType.Cylinder, Vector3.zero, new Vector3(.045f, .022f, .045f), M(Skills.Caramel));
                    P(PrimitiveType.Cylinder, new Vector3(0, .02f, 0), new Vector3(.04f, .004f, .04f), M(new Color(.55f, .28f, .08f)));
                    break;
                case Role.Raven:    // 签子竖着拿（朝上偏外），不顺着枪管指向准星
                    P(PrimitiveType.Cylinder, new Vector3(-.01f, .05f, .01f), new Vector3(.006f, .06f, .006f), M(new Color(.86f, .7f, .45f)), new Vector3(20, 0, 25));
                    P(PrimitiveType.Sphere, new Vector3(-.035f, .1f, .03f), new Vector3(.022f, .03f, .022f), M(Skills.Mustard, 3f));
                    break;
                case Role.Ember:
                    P(PrimitiveType.Cube, Vector3.zero, new Vector3(.022f, .038f, .012f), M(new Color(.9f, .75f, .2f)));
                    P(PrimitiveType.Sphere, new Vector3(0, .028f, 0), new Vector3(.012f, .018f, .012f), M(new Color(1f, .7f, .2f), 6f));   // 火苗
                    break;
                default:            // 棉花糖棒同样竖着拿
                    P(PrimitiveType.Cylinder, new Vector3(-.01f, .04f, 0), new Vector3(.006f, .05f, .006f), M(Color.white), new Vector3(15, 0, 20));
                    P(PrimitiveType.Sphere, new Vector3(-.03f, .1f, .015f), new Vector3(.055f, .05f, .055f), M(Skills.Marsh, 1.6f));
                    P(PrimitiveType.Sphere, new Vector3(-.015f, .115f, .01f), new Vector3(.032f, .028f, .032f), M(new Color(1f, .8f, .95f)));
                    break;
            }
            prop.localScale = Vector3.one * PropScale;
        }

        // V23 打磨：道具放大到 6–15 cm（原 2–6 cm）；贴手点往左下挪，并保证不进屏幕中心 ±12% 的区域（准星区）
        public const float PropScale = 2.6f;
        public static readonly Vector3 PropShift = new Vector3(-.035f, -.015f, -.02f);
        public const float CenterClear = .12f;
        public static Vector2 LastPropVP = new Vector2(-1, -1); public static int PropPlaced; public static float LastPropSize; public static Role? LastCenterRole;

        // 每帧由 Fighter.UpdateView 调用：把道具贴到左手拳心
        public void Place(Transform gun, Vector3 palmPivot, Quaternion rot)
        {
            if (prop == null || !Active) return;
            prop.gameObject.SetActive(true);
            prop.position = gun.TransformPoint(palmPivot + new Vector3(0, .02f, .01f) + PropShift); prop.rotation = gun.rotation * rot;
            var cam = Camera.main;
            if (cam != null)
            {
                // 道具包围盒的 8 个角都不能进中心区；进了就沿"屏幕中心 → 道具"的方向推开（最多 16 次 × 2 cm）
                for (int it = 0; it < 16 && InCenter(cam); it++)
                {
                    Vector3 v0 = cam.WorldToViewportPoint(prop.position); Vector2 d = new Vector2(v0.x - .5f, v0.y - .5f);
                    if (d.sqrMagnitude < 1e-4f) d = new Vector2(-.5f, -1f);
                    d.Normalize(); prop.position += (cam.transform.right * d.x + cam.transform.up * d.y) * .02f;
                }
                if (InCenter(cam)) LastCenterRole = role;
                Vector3 vp = cam.WorldToViewportPoint(prop.position); LastPropVP = new Vector2(vp.x, vp.y);
            }
            PropPlaced++;
        }
        bool InCenter(Camera cam)
        {
            bool any = false; Bounds b = default; float size = 0;
            foreach (var r in prop.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            if (!any) return false;
            size = Mathf.Max(b.size.x, b.size.y, b.size.z); LastPropSize = size;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 vp = cam.WorldToViewportPoint(c);
                if (vp.z > 0 && Mathf.Abs(vp.x - .5f) < CenterClear && Mathf.Abs(vp.y - .5f) < CenterClear) return true;
            }
            return false;
        }
        public bool PropInCenter() { var cam = Camera.main; return prop != null && prop.gameObject.activeSelf && cam != null && InCenter(cam); }
    }

    // ---------------------------------------------------------------- 大招表现
    public static class UltFx
    {
        public static string LastCast = ""; public static float LastCastAt = -9; public static int Casts, Ends;
        public static readonly Color Bun = new Color(.95f, .68f, .32f), Rice = new Color(.98f, .97f, .92f), Jelly = new Color(1f, .5f, .75f), Frost = new Color(1f, .93f, .98f);
        // V23 打磨：远景用的高饱和版本（大招和平时的技能拉开）
        public static readonly Color BunHot = new Color(1f, .58f, .12f), RiceHot = new Color(1f, .97f, .78f), Nori = new Color(.25f, .85f, .45f), JellyHot = new Color(1f, .3f, .68f), CandleHot = new Color(1f, .55f, .12f);
        public static int Beacons; public static float BeaconMaxH;   // 测试读

        // ---------------------------------------------------------------- V23 打磨：远景信号
        // 竖光柱（Additive，圆柱式朝向镜头）+ 深色外描边（在粉白街景上拉开对比）+ 地面 Additive 光圈。20–30 m 外一眼看出"放大招了"
        static Texture2D beamTex, discTex;
        static Texture2D BeamTex { get { if (beamTex == null) beamTex = Grad(32, 64, (u, v) => (Mathf.Exp(-u * u * 2.5f) * .7f + Mathf.Exp(-u * u * 20f) * .3f) * Mathf.Pow(1 - v, .5f) * Mathf.Clamp01(v * 10f)); return beamTex; } }
        public static Texture2D DiscTex { get { if (discTex == null) discTex = Grad(64, 64, (u, v) => { float w = v * 2 - 1, r = Mathf.Sqrt(u * u + w * w); return Mathf.Clamp01(1 - Mathf.Abs(r - .8f) / .18f) + (r < .8f ? .25f * (1 - r) : 0); }); return discTex; } }
        static Texture2D Grad(int w, int h, System.Func<float, float, float> f)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp }; var px = new Color[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = new Color(1, 1, 1, Mathf.Clamp01(f((x + .5f) / w * 2 - 1, (y + .5f) / h)));
            t.SetPixels(px); t.Apply(false); return t;
        }
        // V23 打磨：自己的柔和半透明粒子材质（Sprites/Default + 白色径向软点）。共用的 Fx.Smoke 在这套场景里会把颜色压暗（白泡沫也发灰 = "黑烟"），打磨过的特效都改用这个
        static Texture2D softTex; static Material softMat;
        public static Material SoftMat { get { if (softMat == null) { if (softTex == null) softTex = Grad(64, 64, (u, v) => { float w = v * 2 - 1, r = Mathf.Sqrt(u * u + w * w); return Mathf.Pow(Mathf.Clamp01(1 - r), 1.6f); }); softMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = softTex, name = "柔和粒子" }; } return softMat; } }
        public static Material AddMat(Texture2D tex, Color c)
        {
            var m = new Material(Fx.Additive) { mainTexture = tex, renderQueue = 3100 };
            Tint(m, c); return m;
        }
        public static void Tint(Material m, Color c) { if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", c); else m.color = c; }
        public static void Beacon(Vector3 ground, Color c, Color outline, float height, float width, float secs, Transform follow = null)
        {
            var go = new GameObject("大招光柱"); go.transform.position = ground;
            var b = go.AddComponent<UltBeacon>(); b.c = c; b.outline = outline; b.h = height; b.w = width; b.life = secs; b.follow = follow;
            Beacons++; BeaconMaxH = Mathf.Max(BeaconMaxH, height);
        }
        public class UltBeacon : MonoBehaviour
        {
            public Color c, outline; public float h, w, life; public Transform follow; float born; Transform halo, beam, core, disc; Material mh, mb, mc, md;
            Transform Q(string n, Material m) { var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(q.GetComponent<Collider>()); q.name = n; q.transform.SetParent(transform, false); var r = q.GetComponent<Renderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; return q.transform; }
            void Start()
            {
                born = Time.time;
                mh = new Material(Shader.Find("Sprites/Default")) { mainTexture = BeamTex, renderQueue = 3099, color = new Color(outline.r, outline.g, outline.b, .5f) };
                mb = AddMat(BeamTex, new Color(c.r, c.g, c.b, 1f)); mc = AddMat(BeamTex, new Color(1f, 1f, 1f, .9f)); md = AddMat(DiscTex, new Color(c.r, c.g, c.b, 1f));
                halo = Q("外描边", mh); beam = Q("光柱", mb); core = Q("白芯", mc); disc = Q("地面光圈", md); disc.localRotation = Quaternion.Euler(90, 0, 0);
                LateUpdate();
            }
            void LateUpdate()
            {
                if (halo == null) return;
                float age = Time.time - born; if (age >= life) { Destroy(mh); Destroy(mb); Destroy(mc); Destroy(md); Destroy(gameObject); return; }
                if (follow != null) transform.position = follow.position;
                float grow = Mathf.Clamp01(age / .15f), a = Mathf.Clamp01((life - age) / (life * .5f));
                var cam = Camera.main; Vector3 to = cam != null ? cam.transform.position - transform.position : Vector3.forward; to.y = 0;
                var rot = to.sqrMagnitude > .01f ? Quaternion.LookRotation(-to) : Quaternion.identity; Vector3 away = rot * Vector3.forward;
                float hh = Mathf.Max(.05f, h * grow);
                halo.position = transform.position + Vector3.up * hh / 2 + away * .06f; halo.rotation = rot; halo.localScale = new Vector3(w * 1.45f, hh * 1.04f, 1);
                beam.position = transform.position + Vector3.up * hh / 2; beam.rotation = rot; beam.localScale = new Vector3(w, hh, 1);
                core.position = transform.position + Vector3.up * hh / 2 - away * .03f; core.rotation = rot; core.localScale = new Vector3(w * .3f, hh, 1);
                disc.position = transform.position + Vector3.up * .06f; disc.localScale = Vector3.one * w * (2.2f + 1.6f * Mathf.Clamp01(age / .4f));
                mh.color = new Color(outline.r, outline.g, outline.b, .5f * a); Tint(mb, new Color(c.r, c.g, c.b, a)); Tint(mc, new Color(1, 1, 1, .9f * a)); Tint(md, new Color(c.r, c.g, c.b, a));
            }
        }
        // 一次性的 Additive 发光球壳（果冻罩出场）
        public static void Shell(Vector3 at, float radius, Color c, float secs)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere); Object.Destroy(s.GetComponent<Collider>()); s.name = "大招光壳"; s.transform.position = at;
            var r = s.GetComponent<Renderer>(); r.sharedMaterial = AddMat(DiscTex, new Color(c.r, c.g, c.b, .35f)); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var f = s.AddComponent<ShellFade>(); f.r = radius; f.life = secs; f.c = c;
        }
        public class ShellFade : MonoBehaviour
        {
            public float r, life; public Color c; float born;
            void Start() { born = Time.time; }
            void Update()
            {
                float k = (Time.time - born) / life; var m = GetComponent<Renderer>().sharedMaterial;
                if (k >= 1) { Destroy(m); Destroy(gameObject); return; }
                transform.localScale = Vector3.one * r * 2f * (.7f + .35f * Mathf.Sqrt(k)); Tint(m, new Color(c.r, c.g, c.b, .45f * (1 - k)));
            }
        }
        // 持续一段时间的地面发光圈（果冻罩 8 s 的边界）
        public static void GroundRing(Vector3 at, float radius, Color c, float secs, float width)
        {
            var lr = new GameObject("大招地面圈").AddComponent<LineRenderer>(); lr.material = Fx.Additive; lr.startWidth = lr.endWidth = width; lr.startColor = lr.endColor = c;
            Skills.Circle(lr, at + Vector3.up * .08f, radius); Object.Destroy(lr.gameObject, secs);
        }

        // 施放瞬间：由 FoodState.TryUlt 在规则生效之后调用（规则不变）
        public static void Cast(Fighter f, Vector3 ground)
        {
            if (f == null || !Skills.Food) return;
            LastCast = f.role.ToString(); LastCastAt = Time.time; Casts++;
            Vector3 at = f.transform.position;
            switch (f.role)
            {
                case Role.Falcon:   // 双层汉堡冲撞：身前炸开面包屑 + 芝麻，"啊呜"咬合
                    Sfx.Play("food_burger_chomp", at, 1f, 1f, 1f, 45f);
                    Fx.Burst(f.Chest, f.transform.forward, BunHot, 60, 7f, .18f, .7f, .4f); Fx.Burst(f.Chest, Vector3.up, new Color(1f, .95f, .8f), 32, 4f, .08f, .8f, .8f);
                    Fx.Shockwave(at, 5f, BunHot, .45f); Fx.Flash(f.Chest, BunHot, 6f, 14f, .5f);
                    Beacon(at, BunHot, new Color(.35f, .12f, .02f), 9f, 1.5f, 1.1f, f.transform);
                    f.ViewNudge(2.5f, 7f, .35f); Aura(f, .9f, 0);
                    Feed(f, "双层汉堡冲撞 · 啊呜！");
                    break;
                case Role.Rivet:    // 饭团城墙：三团饭团从天而降（RiceWall 自己做落地动画），这里是甩手 + 米粒
                    Sfx.Play("food_squish", at, .8f, .7f, 1f, 40f);
                    Fx.Burst(f.Chest + f.transform.forward, f.transform.forward, Rice, 50, 5f, .1f, .9f, .9f);
                    {   // V23 打磨：三团饭团的落点（和 FoodState.TryUlt 同一公式，只做表现）—— 每团一根米白光柱 + 海苔绿光圈
                        Vector3 side = Quaternion.Euler(0, f.yaw, 0) * Vector3.right, c0 = Skills.GroundAt(at + Quaternion.Euler(0, f.yaw, 0) * Vector3.forward * 3f);
                        for (int i = -1; i <= 1; i++) { Vector3 q = c0 + side * i * 2.5f; Beacon(q, RiceHot, new Color(.04f, .22f, .1f), 7f, 1.4f, 1.2f); Fx.Shockwave(q, 2.4f, Nori, .5f); Fx.Burst(q + Vector3.up * .6f, Vector3.up, Rice, 24, 4f, .1f, .8f, 1f); }
                        Fx.Flash(c0 + Vector3.up, RiceHot, 5f, 12f, .6f);
                    }
                    f.ViewNudge(1.5f, 4f, .25f);
                    Feed(f, "饭团城墙 · 三团饭团落地！");
                    break;
                case Role.Ironwall: // 果冻罩：JellyDome.Spawn 自己做 Q 弹出场，这里是脚下果冻波纹
                    Fx.Shockwave(at, 9f, JellyHot, .7f); Fx.Shockwave(at, 5f, Color.white, .4f); Fx.Burst(at + Vector3.up * .3f, Vector3.up, JellyHot, 80, 5f, .16f, 1f, -.1f);
                    Beacon(at, JellyHot, new Color(.3f, .03f, .15f), 10f, 2.2f, 1.2f); Shell(at, 5.2f, JellyHot, .8f); GroundRing(at, 5f, JellyHot, 8f, .3f); Fx.Flash(at + Vector3.up * 2f, JellyHot, 6f, 16f, .8f);
                    f.ViewNudge(1.2f, 5f, .3f);
                    Feed(f, "果冻罩 · Q 弹罩住全队！");
                    break;
                case Role.Raven:    // 回转寿司雷达：身边一圈寿司盘转 5 s（全图显示的持续期间可视化）+ 扫描波
                    Sfx.Play("food_sushi_radar", at, 1f, 1f, 1f, 50f);
                    Fx.Flash(f.Chest, Skills.Mustard, 5f, 12f, .5f);
                    var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere); Object.Destroy(sphere.GetComponent<Collider>()); sphere.name = "雷达扫描波"; sphere.transform.position = f.Chest;
                    var sm = new Material(Resources.Load<Material>("Materials/Holo") ?? Fx.Additive); sm.SetColor("_Color", Skills.Mustard * 1.2f); sphere.GetComponent<Renderer>().sharedMaterial = sm;
                    var ss = sphere.AddComponent<ScanSphere>(); ss.radius = 40f; ss.color = Skills.Mustard;
                    Beacon(at, Skills.Mustard, new Color(.18f, .2f, .02f), 10f, 1.6f, 1.2f, f.transform);
                    f.ViewNudge(1f, 5f, .2f); Aura(f, 5f, 3);
                    Feed(f, "回转寿司雷达 · 转起来！");
                    break;
                case Role.Ember:    // 生日蜡烛轰炸：吹气"呼—"，落点圈由 UltWarn 画
                    Sfx.Play("food_candle_whoosh", at, 1f, 1f, 1f, 45f);
                    Fx.Burst(f.Eye + f.AimDir * .6f, f.AimDir, new Color(1f, .75f, .3f), 40, 7f, .12f, .6f, 0);
                    Beacon(ground, CandleHot, new Color(.3f, .1f, .02f), 12f, 2f, 1.4f); Fx.Shockwave(ground, 6f, CandleHot, .6f); Fx.Flash(ground + Vector3.up * 2f, CandleHot, 6f, 18f, 1f);
                    f.ViewNudge(1.5f, 4f, .2f);
                    Feed(f, "生日蜡烛轰炸 · 许个愿！");
                    break;
                default:            // 糖霜风暴：落点起一场糖霜风（持续 6 s），铃声
                    Sfx.Play("food_frost_wind", ground, 1f, 1f, 1f, 50f);
                    FrostStorm.Spawn(ground, f); Beacon(ground, Skills.Marsh, new Color(.25f, .05f, .25f), 12f, 2.4f, 1.4f);
                    f.ViewNudge(1.2f, 5f, .25f);
                    Feed(f, "糖霜风暴 · 撒糖！");
                    break;
            }
        }
        static void Feed(Fighter f, string text) { if (Match.I != null) Match.I.Feed((f.human ? "你的" : f.Hero.name + " 的") + text, f.Hero.tint); }
        public static void End(Fighter f, Role r, Vector3 at)
        {
            Ends++;
            if (Match.I == null || !Skills.Food) return;
            switch (r)
            {
                case Role.Falcon: Fx.Burst(at + Vector3.up, Vector3.up, Bun, 12, 2f, .05f, .5f, .8f); Sfx.Play("food_burp", at, .7f, 1.1f, 1f, 30f); break;
                case Role.Raven: Sfx.Play("food_sushi", at, .6f, 1.2f, 1f, 30f); break;
            }
            if (f != null && f.human) Match.I.Feed("「" + FoodState.UltName(r) + "」结束", new Color(.8f, .8f, .85f));
        }

        // 持续期间跟在人身上的光环（汉堡 0.9 s：两片面包 + 芝麻；寿司雷达 5 s：转盘寿司）
        static void Aura(Fighter f, float secs, int kind)
        {
            var go = new GameObject("大招光环"); go.transform.position = f.transform.position + Vector3.up * 1.1f;   // 不挂在角色下面（豆子角色有缩放），每帧跟随
            var a = go.AddComponent<UltAura>(); a.owner = f; a.until = Time.time + secs; a.kind = kind; a.role = f.role;
        }
        public class UltAura : MonoBehaviour
        {
            public Fighter owner; public float until; public int kind; public Role role; readonly List<Transform> bits = new List<Transform>(); float nextSfx; Material shellMat;
            void OnDestroy() { if (shellMat != null) Destroy(shellMat); }
            void Start()
            {
                Material M(Color c, float e = 1.4f) { return Arena.Glow(c, e); }   // V23 打磨：原来 e = 0 → 只有 0.3 倍底色，远处发灰
                Transform Prim(PrimitiveType t, Vector3 lp, Vector3 sc, Material m) { var g = GameObject.CreatePrimitive(t); Destroy(g.GetComponent<Collider>()); g.transform.SetParent(transform, false); g.transform.localPosition = lp; g.transform.localScale = sc; g.GetComponent<Renderer>().sharedMaterial = m; return g.transform; }
                if (kind == 0)
                {
                    // 双层汉堡（体积 ×1.6）：上下两片面包 + 生菜 + 芝士 + 大芝麻 + 橙色发光外壳
                    bits.Add(Prim(PrimitiveType.Sphere, new Vector3(0, .8f, 0), new Vector3(2.2f, .8f, 2.2f), M(BunHot, 1.6f)));
                    bits.Add(Prim(PrimitiveType.Sphere, new Vector3(0, -1f, 0), new Vector3(2.2f, .7f, 2.2f), M(BunHot, 1.6f)));
                    Prim(PrimitiveType.Cylinder, new Vector3(0, .45f, 0), new Vector3(2.5f, .05f, 2.5f), M(new Color(.35f, .95f, .3f), 2f));
                    Prim(PrimitiveType.Cube, new Vector3(0, .52f, 0), new Vector3(1.8f, .06f, 1.8f), M(new Color(1f, .85f, .15f), 2f)).localRotation = Quaternion.Euler(0, 45, 0);
                    for (int i = 0; i < 10; i++) Prim(PrimitiveType.Sphere, new Vector3(Random.Range(-.7f, .7f), 1.15f, Random.Range(-.7f, .7f)), Vector3.one * .13f, M(new Color(1f, .97f, .85f), 2f));
                    var shell = Prim(PrimitiveType.Sphere, Vector3.zero, new Vector3(3f, 3.2f, 3f), AddMat(DiscTex, new Color(BunHot.r, BunHot.g, BunHot.b, .3f))); shellMat = shell.GetComponent<Renderer>().sharedMaterial;
                }
                else
                {
                    for (int i = 0; i < 6; i++)   // 回转寿司：6 个彩盘 + 盘上一贯寿司，绕着人转
                    {
                        // V23 打磨：转盘半径 1.3 → 2.0 m，盘子 / 寿司 ×1.6，自发光 0.6 → 2
                        float ang = i / 6f * Mathf.PI * 2; Vector3 lp = new Vector3(Mathf.Cos(ang) * 2f, 0, Mathf.Sin(ang) * 2f);
                        bits.Add(Prim(PrimitiveType.Cylinder, lp, new Vector3(.8f, .035f, .8f), M(i % 2 == 0 ? new Color(1f, .25f, .35f) : new Color(.25f, .55f, 1f), 2f)));
                        Prim(PrimitiveType.Cube, lp + Vector3.up * .15f, new Vector3(.48f, .22f, .29f), M(Rice, 1.2f));
                        Prim(PrimitiveType.Cube, lp + Vector3.up * .3f, new Vector3(.54f, .08f, .32f), M(i % 3 == 0 ? new Color(1f, .5f, .25f) : i % 3 == 1 ? new Color(1f, .2f, .25f) : new Color(1f, .85f, .2f), 2f));
                    }
                    // 脚下一圈芥末黄 Additive 光圈（跟着人，持续 5 s = 全图显示时长）
                    var lr = new GameObject("寿司光圈").AddComponent<LineRenderer>(); lr.transform.SetParent(transform, false); lr.transform.localPosition = new Vector3(0, -1.03f, 0);
                    lr.useWorldSpace = false; lr.loop = true; lr.positionCount = 48; lr.material = Fx.Additive; lr.startWidth = lr.endWidth = .3f; lr.startColor = lr.endColor = Skills.Mustard;
                    for (int i = 0; i < 48; i++) { float a2 = i / 48f * Mathf.PI * 2; lr.SetPosition(i, new Vector3(Mathf.Cos(a2) * 2.6f, 0, Mathf.Sin(a2) * 2.6f)); }
                }
                foreach (var r in GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
            }
            void Update()
            {
                if (owner == null || !owner.alive || Time.time >= until) { UltFx.End(owner, role, transform.position); Destroy(gameObject); return; }
                transform.position = owner.transform.position + Vector3.up * 1.1f;
                if (kind == 0) { float k = Mathf.Clamp01((until - Time.time) / .9f); transform.localScale = Vector3.one * (.8f + .3f * Mathf.Sin(Time.time * 30f) * k); if (Time.time >= nextSfx) { nextSfx = Time.time + .25f; Sfx.Play("food_burger_chomp", transform.position, .5f, 1.3f, 1f, 30f); } }
                else { transform.Rotate(0, 150f * Time.deltaTime, 0, Space.Self); if (Time.time >= nextSfx) { nextSfx = Time.time + 1f; Sfx.Play("food_sushi", transform.position, .35f, 1f, 1f, 30f); } }
            }
        }

        // 糖霜风暴（幻频大招的持续期间）：落点 11 m 内的粉白糖霜风，6 s（= 规则时长），敌人屏幕边缘结糖霜
        public class FrostStorm : MonoBehaviour
        {
            public Fighter owner; float born; ParticleSystem ps, glitter, column; LineRenderer ring; float nextSfx; Light lamp;
            ParticleSystem Particles(string n, Vector3 lp, Color a, Color b, float s0, float s1, float v0, float v1, float rate, int max, float radius, Material mat)
            {
                var g = new GameObject(n); g.transform.SetParent(transform, false); g.transform.localPosition = lp;
                var p = g.AddComponent<ParticleSystem>(); var main = p.main; main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.2f); main.startSpeed = new ParticleSystem.MinMaxCurve(v0, v1); main.startSize = new ParticleSystem.MinMaxCurve(s0, s1);
                main.startColor = new ParticleSystem.MinMaxGradient(a, b); main.maxParticles = max; main.simulationSpace = ParticleSystemSimulationSpace.World;
                var em = p.emission; em.rateOverTime = rate; var sh = p.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = radius; sh.rotation = new Vector3(90, 0, 0);
                var col = p.colorOverLifetime; col.enabled = true; var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .15f), new GradientAlphaKey(1, .7f), new GradientAlphaKey(0, 1) }); col.color = gr;
                var r = g.GetComponent<ParticleSystemRenderer>(); r.material = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; return p;
            }
            public static readonly List<FrostStorm> Live = new List<FrostStorm>();
            public const float Radius = 11f, Life = 6f;
            void OnEnable() { Live.Add(this); }
            void OnDisable() { Live.Remove(this); }
            public static FrostStorm Spawn(Vector3 at, Fighter owner)
            {
                var go = new GameObject("糖霜风暴"); go.transform.position = at;
                var s = go.AddComponent<FrostStorm>(); s.owner = owner; s.born = Time.time; Skills.spawned.Add(go); return s;
            }
            void Start()
            {
                var g = new GameObject("糖霜"); g.transform.SetParent(transform, false); g.transform.localPosition = Vector3.up * 2.5f;
                ps = g.AddComponent<ParticleSystem>();
                var main = ps.main; main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f); main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f); main.startSize = new ParticleSystem.MinMaxCurve(.3f, .8f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .95f, .99f, .85f), new Color(1f, .72f, .93f, .8f)); main.maxParticles = 900; main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = .15f;
                var em = ps.emission; em.rateOverTime = 320;
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = Radius; sh.rotation = new Vector3(90, 0, 0);
                var vel = ps.velocityOverLifetime; vel.enabled = true; vel.orbitalY = 2.2f; vel.space = ParticleSystemSimulationSpace.World;
                g.GetComponent<ParticleSystemRenderer>().material = SoftMat;   // 半透明混合：粉白糖霜在粉色街景上也看得见
                // V23 打磨：Additive 闪粉（粉 / 天蓝）+ 中央约 8 m 的糖霜旋风柱，远处一眼看得出"风暴"
                glitter = Particles("闪粉", Vector3.up * 2f, new Color(1f, .55f, .95f), new Color(.55f, .85f, 1f), .1f, .25f, 1f, 3f, 150, 400, Radius, Fx.Additive);
                var gv = glitter.velocityOverLifetime; gv.enabled = true; gv.orbitalY = 2.6f; gv.space = ParticleSystemSimulationSpace.World;
                column = Particles("旋风柱", Vector3.up * .3f, new Color(1f, .85f, .97f, .8f), new Color(1f, .5f, .88f, .7f), .8f, 1.6f, 3.5f, 4.5f, 60, 160, 1.6f, SoftMat);
                var csh = column.shape; csh.shapeType = ParticleSystemShapeType.Cone; csh.angle = 12f; csh.rotation = new Vector3(-90, 0, 0);
                var cv = column.velocityOverLifetime; cv.enabled = true; cv.orbitalY = 4f; cv.space = ParticleSystemSimulationSpace.Local;
                var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(disc.GetComponent<Collider>()); disc.name = "糖霜地面"; disc.transform.SetParent(transform, false);
                disc.transform.localPosition = Vector3.up * .04f; disc.transform.localScale = new Vector3(Radius * 2, .005f, Radius * 2);
                disc.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 1f, 1f, .28f) }; disc.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ring = new GameObject("风暴边界").AddComponent<LineRenderer>(); ring.transform.SetParent(transform); ring.material = Fx.Additive; ring.startWidth = ring.endWidth = .35f; ring.startColor = ring.endColor = new Color(1f, .45f, .9f);
                Skills.Circle(ring, transform.position, Radius);
                lamp = new GameObject("糖霜光").AddComponent<Light>(); lamp.transform.SetParent(transform, false); lamp.transform.localPosition = Vector3.up * 3f; lamp.color = Skills.Marsh; lamp.range = 18f; lamp.intensity = 4f;
                Fx.Shockwave(transform.position, Radius, Frost, .8f);
            }
            void Update()
            {
                float age = Time.time - born, left = Life - age;
                if (left <= 0 || Match.I == null || Match.I.phase != Phase.Live) { Fx.Burst(transform.position + Vector3.up * 2f, Vector3.up, Frost, 40, 5f, .12f, 1f, .3f); Sfx.Play("food_confetti", transform.position, .6f, 1.3f, 1f, 40f); UltFx.End(owner, Role.Phantom, transform.position); Destroy(gameObject); return; }
                if (Time.time >= nextSfx) { nextSfx = Time.time + .9f; Sfx.Play("food_frost_wind", transform.position, .6f, Random.Range(.9f, 1.1f), 1f, 45f); }
                if (lamp != null) lamp.intensity = 3.5f + Mathf.PerlinNoise(Time.time * 3f, 0) * 2f;
                var p = Match.I.player;
                if (p != null && p.alive && owner != null && p.team != owner.team && Links.Flat(p.transform.position, transform.position) <= Radius) SkillPop.EdgeFrost = Mathf.Min(1f, Mathf.Clamp01(left / .6f));
                if (left < .6f && ps != null) { var em = ps.emission; em.rateOverTime = 320f * left / .6f; if (glitter != null) { var e2 = glitter.emission; e2.rateOverTime = 150f * left / .6f; } if (column != null) { var e3 = column.emission; e3.rateOverTime = 60f * left / .6f; } }
            }
        }
    }
}
