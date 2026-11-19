using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace NeonSiege
{
    // ================================================================ V20：战斗模型（角色 / 枪械 / 3C）
    public enum HitPart { Head, Chest, Stomach, Arm, Leg }

    // ---------------------------------------------------------------- 3C 可调参数（F1 面板和 tuning_3c.csv 都改这里）
    public static class Tune
    {
        // 移动
        public static float ProneSpeed = 1.2f, Accel = 40f, AirAccel = 6f, JumpSpeed = 5.6f, Gravity = 17f, AdsMoveMul = .6f;
        // 体力
        public static float StaminaMax = 100f, SprintDrain = 16f, StaminaRegen = 22f, RegenDelay = .8f, JumpCost = 8f, MantleCost = 12f, ExhaustRecover = 25f;
        // 负重：超过免费重量后，每公斤降低移速 / 增加开镜时间
        public static float WeightFree = 8f, WeightSpeedPerKg = .012f, WeightAdsPerKg = .03f, ArmorKgPerClass = 1.5f;
        // 姿态
        public static float CrouchSpreadMul = .75f, ProneSpreadMul = .55f, CrouchRecoilMul = .8f, ProneRecoilMul = .6f, ProneTransition = .45f;
        // 操作手感
        public static float JumpBuffer = .15f, CoyoteTime = .1f;
        public static bool AdsHold = false, CrouchHold = true;
        // 镜头
        public static float BobScale = 1f, ShakeScale = 1f, SprintFovAdd = 6f, SlideFovAdd = 10f;
        // V22 HUD 表现（只影响显示，不改规则）：体力条回满后淡出时长、耗尽锁定脉冲频率、恢复过线闪光时长
        public static float HudStaminaFade = 1f, HudExhaustPulseHz = 2.5f, HudRecoverFlash = .35f;
    }

    // ---------------------------------------------------------------- 部位判定、护甲 / 穿透、弹道
    public static class Ballistics
    {
        public static string PartName(HitPart p) { return p == HitPart.Head ? "头部" : p == HitPart.Chest ? "胸部" : p == HitPart.Stomach ? "腹部" : p == HitPart.Arm ? "手臂" : "腿部"; }

        // 统一判定：只看角色胶囊体里的相对位置（和外观、帽子、变身无关）
        public static HitPart PartOf(Fighter v, Vector3 point)
        {
            Vector3 l = v.transform.InverseTransformPoint(point);
            float h = v.cc != null ? v.cc.height : 1.92f;
            float y = l.y / h;                                   // 0 = 脚底, 1 = 头顶
            if (v.leanHeadHit(point)) return HitPart.Head;
            if (y >= .81f) return HitPart.Head;
            if (!v.Prone && Mathf.Abs(l.x) > .27f && y > .47f && y < .8f) return HitPart.Arm;
            if (y >= .6f) return HitPart.Chest;
            if (y >= .44f) return HitPart.Stomach;
            return HitPart.Leg;
        }

        public static float PartMul(WeaponSpec w, HitPart p)
        {
            switch (p) { case HitPart.Head: return w.headMul; case HitPart.Chest: return w.chestMul; case HitPart.Stomach: return w.stomachMul; case HitPart.Arm: return w.armMul; default: return w.legMul; }
        }

        // 护甲规则：头盔护头，护甲护胸腹；四肢不受保护。
        // 穿透 ≥ 护甲等级：85% 伤害进生命、护甲磨损 35%；差 1 级 55% / 60%；差 2 级 35% / 80%；差 3 级及以上 20% / 100%。
        public static readonly float[] HpShare = { .85f, .55f, .35f, .2f }, ArmorShare = { .35f, .6f, .8f, 1f };
        public static int ProtectionClass(Fighter v, HitPart p) { return p == HitPart.Head ? v.helmetClass : p == HitPart.Chest || p == HitPart.Stomach ? v.armorClass : 0; }
        public static void Split(WeaponSpec w, HitPart part, Fighter v, float falloff, out float hp, out float armor, out int cls)
        {
            float dmg = w.bodyDamage * falloff * PartMul(w, part);
            cls = ProtectionClass(v, part);
            if (cls <= 0 || v.armor <= 0) { hp = dmg; armor = 0; return; }
            int gap = Mathf.Clamp(cls - w.pen, 0, 3);
            hp = dmg * HpShare[gap]; armor = dmg * ArmorShare[gap];
        }

        // 固定弹道 + 少量随机：前几发主要往上跳，之后左右摆（每把枪的摆动相位不同），可以学会怎么压
        public static Vector2 PatternStep(WeaponSpec w, int shot)
        {
            int i = Mathf.Max(0, shot - 1);
            float v = w.recoil * (1f + w.vRamp * Mathf.Min(i, 6) / 6f) * (i >= 10 ? .75f : 1f);
            float phase = (w.id.GetHashCode() & 7) * .8f;
            float hx = w.recoil * w.hDrift * Mathf.Sin(i * .55f + phase) * Mathf.Min(1f, i / 3f);
            return new Vector2(hx, v);
        }
        public static Vector2 RecoilStep(WeaponSpec w, int shot)
        {
            var p = PatternStep(w, shot);
            return new Vector2(p.x + Random.Range(-1f, 1f) * w.recoil * w.hRandom, p.y * Random.Range(.92f, 1.08f));
        }

        // 理论击杀时间：同一部位连续命中、全部命中、考虑护甲磨损
        public static float Ttk(WeaponSpec w, int armorClass, int helmetClass, float distance, HitPart part, out int shots)
        {
            float hp = Config.MaxHp, armor = armorClass > 0 || helmetClass > 0 ? Config.MaxArmor : 0;
            float falloff = distance <= w.fullRange ? 1f : Mathf.Lerp(1f, w.minFalloff, Mathf.InverseLerp(w.fullRange, w.falloffRange, distance));
            int cls = part == HitPart.Head ? helmetClass : part == HitPart.Chest || part == HitPart.Stomach ? armorClass : 0;
            shots = 0;
            while (hp > 0 && shots < 200)
            {
                float dmg = w.bodyDamage * falloff * PartMul(w, part) * w.pellets;
                if (cls > 0 && armor > 0) { int gap = Mathf.Clamp(cls - w.pen, 0, 3); hp -= dmg * HpShare[gap]; armor = Mathf.Max(0, armor - dmg * ArmorShare[gap]); }
                else hp -= dmg;
                shots++;
            }
            if (w.burst <= 1) return (shots - 1) * 60f / w.rpm;
            int bursts = (shots - 1) / w.burst, rest = (shots - 1) % w.burst;
            return bursts * w.ShotInterval + rest * w.burstInterval;
        }

        public static float Weight(Fighter f) { return f.W.weight + f.armorClass * Tune.ArmorKgPerClass; }
        public static float WeightSpeedMul(Fighter f) { return 1f - Mathf.Max(0, Weight(f) - Tune.WeightFree) * Tune.WeightSpeedPerKg; }
        public static float AdsTime(Fighter f) { return f.W.adsTime * (1f + Mathf.Max(0, Weight(f) - Tune.WeightFree) * Tune.WeightAdsPerKg); }
    }

    // 受击记录（死亡回顾、靶场）。V22：所有伤害来源都写记录（kind / source / gun），不只是子弹
    public enum DamageKind { Gun, Melee, Skill, Grenade, Fire, Ult, Food, Env, Bomb }
    public struct HitRecord
    {
        public string attacker, weapon; public HitPart part; public float hp, armor, distance, time; public int pen, cls;
        public DamageKind kind; public Fighter source; public WeaponSpec gun; public bool hasPart;
    }
    // V22：非子弹伤害的来源说明。调用 ReceiveDamage 之前 Set 一次，ReceiveDamage 第一行取走（不会串到下一次）
    public static class DamageCtx
    {
        static bool set; static DamageKind kind; static string name;
        public static void Set(DamageKind k, string n) { set = true; kind = k; name = n; }
        public static bool Take(out DamageKind k, out string n) { k = kind; n = name; bool s = set; set = false; return s; }
        public static string KindName(DamageKind k)
        {
            switch (k) { case DamageKind.Gun: return "枪械"; case DamageKind.Melee: return "近战"; case DamageKind.Skill: return "技能"; case DamageKind.Grenade: return "手雷"; case DamageKind.Fire: return "燃烧"; case DamageKind.Ult: return "大招"; case DamageKind.Food: return "变身"; case DamageKind.Bomb: return "爆炸"; default: return "环境"; }
        }
    }

    // ---------------------------------------------------------------- 默认数值 + CSV 数据表（StreamingAssets/Data，打包后也能直接改表）
    public static class DataTables
    {
        static readonly string[] WeaponFields = { "bodyDamage", "rpm", "fullRange", "falloffRange", "minFalloff", "pellets", "burst", "burstInterval", "magazine", "hipSpread", "adsSpread", "bloomPerShot", "maxBloom",
            "recoil", "adsZoom", "moveMultiplier", "bulletSpeed", "pen", "headMul", "chestMul", "stomachMul", "armMul", "legMul", "adsTime", "sprintToFire", "reloadTac", "reloadEmpty", "drawTime", "weight", "dropScale", "wallPen", "hDrift", "hRandom", "vRamp" };
        static bool defaultsApplied; public static string LastStatus = "";
        static readonly Dictionary<string, string> defaultSnapshot = new Dictionary<string, string>();
        public static string Dir { get { return Path.Combine(Application.streamingAssetsPath, "Data"); } }

        public static void ApplyDefaults()
        {
            if (defaultsApplied) return; defaultsApplied = true;
            void W(string id, int pen, float head, float leg, float ads, float stf, float tac, float empty, float draw, float kg, float drop, float wall, float hDrift, float hRand, float vRamp)
            {
                foreach (var w in Config.Arsenal) if (w.id == id)
                {
                    w.pen = pen; w.headMul = head; w.chestMul = 1f; w.stomachMul = .95f; w.armMul = .85f; w.legMul = leg; w.adsTime = ads; w.sprintToFire = stf; w.reloadTac = tac; w.reloadEmpty = empty;
                    w.drawTime = draw; w.weight = kg; w.dropScale = drop; w.wallPen = wall; w.hDrift = hDrift; w.hRandom = hRand; w.vRamp = vRamp; w.reload = tac;
                }
            }
            // V24：SMG-7 腿 .85 → .72、AR-9 腿 .75 → .68 —— 以前打 3 级甲的胸和打腿杀人发数一样（7 / 7、5 / 5），现在 7 / 8、5 / 6（打胸更划算）
            //  id       穿透 头    腿    开镜  冲刺转开火 战术换弹 空仓换弹 拔枪 重量  下坠 穿墙  横摆 随机 上跳递增
            W("P-9",    2, 1.8f, .8f,  .14f, .12f, 1.3f, 1.6f, .35f, 1.2f, .4f, .1f,  .25f, .15f, .1f);
            W("SMG-7",  3, 1.6f, .72f, .18f, .10f, 1.6f, 2.1f, .45f, 2.8f, .5f, .12f, .45f, .25f, .25f);
            W("AR-9",   4, 1.8f, .68f, .26f, .20f, 1.9f, 2.4f, .55f, 3.6f, .3f, .3f,  .35f, .18f, .35f);
            W("LMG-2",  4, 1.7f, .75f, .40f, .32f, 3.0f, 3.8f, .80f, 7.5f, .3f, .45f, .55f, .25f, .3f);
            W("DMR-3",  5, 2.2f, .7f,  .35f, .30f, 2.2f, 2.8f, .65f, 4.8f, .2f, .5f,  .15f, .1f,  0f);
            W("FS-12",  2, 1.4f, .8f,  .24f, .20f, 2.4f, 3.0f, .55f, 3.4f, .8f, 0f,   .2f,  .2f,  0f);
            W("BR-5",   3, 1.9f, .75f, .28f, .22f, 1.9f, 2.4f, .55f, 3.4f, .3f, .25f, .25f, .12f, .2f);
            // 干员护甲 / 头盔等级（铁幕重甲，夜鸦、幻频轻装）
            void H(Role r, int armor, int helmet) { var h = Config.Hero(r); h.armorClass = armor; h.helmetClass = helmet; }
            H(Role.Falcon, 3, 2); H(Role.Rivet, 3, 3); H(Role.Ironwall, 4, 3); H(Role.Raven, 2, 2); H(Role.Ember, 3, 2); H(Role.Phantom, 2, 3);
            defaultSnapshot.Clear(); defaultSnapshot["weapons"] = WeaponsCsv(); defaultSnapshot["heroes"] = HeroesCsv(); defaultSnapshot["tune"] = TuneCsv();
        }

        // ---- 序列化
        static string F(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }
        static object Get(object o, string field) { var fi = o.GetType().GetField(field); return fi != null ? fi.GetValue(o) : null; }
        static void Set(object o, string field, string v)
        {
            var fi = o.GetType().GetField(field); if (fi == null) return;
            if (fi.FieldType == typeof(int) && int.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out int i)) fi.SetValue(o, i);
            else if (fi.FieldType == typeof(float) && float.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out float f)) fi.SetValue(o, f);
            else if (fi.FieldType == typeof(bool)) fi.SetValue(o, v == "1" || v.ToLower() == "true");
        }
        public static string WeaponsCsv()
        {
            var sb = new StringBuilder("id,name," + string.Join(",", WeaponFields) + "\n");
            foreach (var w in Config.Arsenal)
            {
                sb.Append(w.id).Append(',').Append(w.name);
                foreach (var f in WeaponFields) { var v = Get(w, f); sb.Append(',').Append(v is float fl ? F(fl) : v?.ToString()); }
                sb.Append('\n');
            }
            return sb.ToString();
        }
        public static string HeroesCsv()
        {
            var sb = new StringBuilder("role,name,duty,armorClass,helmetClass,skillCooldown,skillCharges\n");
            foreach (var h in Config.Heroes) sb.Append(h.role).Append(',').Append(h.name).Append(',').Append(h.duty).Append(',').Append(h.armorClass).Append(',').Append(h.helmetClass).Append(',').Append(F(h.skill.cooldown)).Append(',').Append(h.skill.maxCharges).Append('\n');
            return sb.ToString();
        }
        static IEnumerable<FieldInfo> TuneFields() { foreach (var f in typeof(Tune).GetFields(BindingFlags.Public | BindingFlags.Static)) yield return f; }
        public static string TuneCsv()
        {
            var sb = new StringBuilder("key,value\n");
            sb.Append("WalkSpeed,").Append(F(Config.WalkSpeed)).Append("\nSprintSpeed,").Append(F(Config.SprintSpeed)).Append("\nCrouchSpeed,").Append(F(Config.CrouchSpeed)).Append('\n');
            foreach (var f in TuneFields()) { var v = f.GetValue(null); sb.Append(f.Name).Append(',').Append(v is float fl ? F(fl) : v is bool b ? (b ? "1" : "0") : v.ToString()).Append('\n'); }
            return sb.ToString();
        }
        static void ParseWeapons(string csv)
        {
            var lines = csv.Replace("\r", "").Split('\n'); if (lines.Length < 2) return;
            var head = lines[0].Split(',');
            for (int li = 1; li < lines.Length; li++)
            {
                var c = lines[li].Split(','); if (c.Length != head.Length) continue;
                foreach (var w in Config.Arsenal) if (w.id == c[0]) for (int k = 2; k < head.Length; k++) Set(w, head[k], c[k]);
            }
        }
        static void ParseHeroes(string csv)
        {
            foreach (var line in csv.Replace("\r", "").Split('\n'))
            {
                var c = line.Split(','); if (c.Length < 7 || c[0] == "role") continue;
                foreach (var h in Config.Heroes) if (h.role.ToString() == c[0])
                {
                    int.TryParse(c[3], out h.armorClass); int.TryParse(c[4], out h.helmetClass);
                    if (float.TryParse(c[5], NumberStyles.Any, CultureInfo.InvariantCulture, out float cd)) h.skill.cooldown = cd;
                    if (int.TryParse(c[6], out int ch)) h.skill.maxCharges = ch;
                }
            }
        }
        static void ParseTune(string csv)
        {
            foreach (var line in csv.Replace("\r", "").Split('\n'))
            {
                var c = line.Split(','); if (c.Length < 2 || c[0] == "key") continue;
                if (!float.TryParse(c[1], NumberStyles.Any, CultureInfo.InvariantCulture, out float v) && c[1] != "true" && c[1] != "false") continue;
                if (c[0] == "WalkSpeed") Config.WalkSpeed = v; else if (c[0] == "SprintSpeed") Config.SprintSpeed = v; else if (c[0] == "CrouchSpeed") Config.CrouchSpeed = v;
                else { var f = typeof(Tune).GetField(c[0]); if (f == null) continue; if (f.FieldType == typeof(bool)) f.SetValue(null, v > .5f || c[1] == "true"); else f.SetValue(null, v); }
            }
        }

        // 启动时：先套默认值，再读表格覆盖（表格不存在就写一份默认的）
        public static void Load()
        {
            ApplyDefaults();
            try
            {
                Directory.CreateDirectory(Dir);
                string wp = Path.Combine(Dir, "weapons.csv"), hp = Path.Combine(Dir, "heroes.csv"), tp = Path.Combine(Dir, "tuning_3c.csv");
                if (File.Exists(wp)) ParseWeapons(File.ReadAllText(wp)); else File.WriteAllText(wp, WeaponsCsv(), Encoding.UTF8);
                if (File.Exists(hp)) ParseHeroes(File.ReadAllText(hp)); else File.WriteAllText(hp, HeroesCsv(), Encoding.UTF8);
                if (File.Exists(tp)) ParseTune(File.ReadAllText(tp)); else File.WriteAllText(tp, TuneCsv(), Encoding.UTF8);
                LastStatus = "已读取数据表：" + Dir;
            }
            catch (System.Exception e) { LastStatus = "数据表读取失败：" + e.Message; }
        }
        public static string Export()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(Path.Combine(Dir, "weapons.csv"), WeaponsCsv(), Encoding.UTF8);
                File.WriteAllText(Path.Combine(Dir, "heroes.csv"), HeroesCsv(), Encoding.UTF8);
                File.WriteAllText(Path.Combine(Dir, "tuning_3c.csv"), TuneCsv(), Encoding.UTF8);
                return LastStatus = "已导出到 " + Dir;
            }
            catch (System.Exception e) { return LastStatus = "导出失败：" + e.Message; }
        }
        public static void Reload()
        {
            try
            {
                string wp = Path.Combine(Dir, "weapons.csv"), hp = Path.Combine(Dir, "heroes.csv"), tp = Path.Combine(Dir, "tuning_3c.csv");
                if (File.Exists(wp)) ParseWeapons(File.ReadAllText(wp)); if (File.Exists(hp)) ParseHeroes(File.ReadAllText(hp)); if (File.Exists(tp)) ParseTune(File.ReadAllText(tp));
                LastStatus = "已从表格重新读取";
            }
            catch (System.Exception e) { LastStatus = "读取失败：" + e.Message; }
        }
        public static void RestoreDefaults()
        {
            if (defaultSnapshot.TryGetValue("weapons", out var w)) ParseWeapons(w);
            if (defaultSnapshot.TryGetValue("heroes", out var h)) ParseHeroes(h);
            if (defaultSnapshot.TryGetValue("tune", out var t)) ParseTune(t);
            LastStatus = "已恢复默认数值";
        }
        // 编辑器打包前写出默认表格（打包后在 NeonSiegeDemo_Data/StreamingAssets/Data 里可以直接改）
        public static void WriteDefaults(string dir)
        {
            ApplyDefaults(); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "weapons.csv"), defaultSnapshot["weapons"], Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "heroes.csv"), defaultSnapshot["heroes"], Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "tuning_3c.csv"), defaultSnapshot["tune"], Encoding.UTF8);
        }
    }

    // ---------------------------------------------------------------- F1 调参面板 / F2 判定框
    public class TuningPanel : MonoBehaviour
    {
        public static bool Open;
        int tab; Vector2 scroll; static GUIStyle lab, head, small; string msg = "";
        public static bool ShowHitboxes;
        readonly List<LineRenderer> boxes = new List<LineRenderer>();

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1)) { Open = !Open; Cursor.lockState = Open ? CursorLockMode.None : CursorLockMode.Locked; Cursor.visible = Open; }
            if (Input.GetKeyDown(KeyCode.F2)) ShowHitboxes = !ShowHitboxes;
            DrawHitboxes();
        }

        // F2：显示每个角色的统一判定区（头 / 胸 / 腹 / 腿 + 手臂区），和外观无关
        void DrawHitboxes()
        {
            var m = Match.I; int need = ShowHitboxes && m != null ? m.fighters.Count * 5 : 0;
            while (boxes.Count < need)
            {
                var lr = new GameObject("判定框").AddComponent<LineRenderer>(); lr.positionCount = 33; lr.useWorldSpace = true; lr.widthMultiplier = .025f;
                lr.material = FoodFx.TrailMaterial; lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; boxes.Add(lr);
            }
            for (int i = 0; i < boxes.Count; i++) boxes[i].enabled = i < need;
            if (need == 0) return;
            float[] bands = { .44f, .6f, .81f, 1f, 0f }; Color[] cols = { new Color(1f, .9f, .2f), new Color(1f, .55f, .2f), new Color(1f, .2f, .2f), new Color(1f, .2f, .2f), new Color(.3f, 1f, .4f) };
            int k = 0;
            foreach (var f in m.fighters)
                for (int b = 0; b < 5; b++, k++)
                {
                    var lr = boxes[k]; bool on = f.alive; lr.enabled = on; if (!on) continue;
                    float h = f.cc.height * bands[b]; lr.startColor = lr.endColor = cols[b];
                    for (int s = 0; s <= 32; s++) { float a = s / 32f * Mathf.PI * 2; lr.SetPosition(s, f.transform.position + new Vector3(Mathf.Cos(a) * f.cc.radius, h + .02f, Mathf.Sin(a) * f.cc.radius)); }
                }
        }

        static void Styles()
        {
            if (lab != null) return; var font = Resources.Load<Font>("NotoSansSC");
            lab = new GUIStyle(GUI.skin.label) { font = font, fontSize = 14, normal = { textColor = Color.white } };
            head = new GUIStyle(lab) { fontSize = 18, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, .8f, .4f) } };
            small = new GUIStyle(lab) { fontSize = 12, normal = { textColor = new Color(.8f, .85f, .9f) } };
        }
        float Row(string name, float v, float min, float max, string fmt = "0.00")
        {
            GUILayout.BeginHorizontal(); GUILayout.Label(name, lab, GUILayout.Width(200));
            float nv = GUILayout.HorizontalSlider(v, min, max, GUILayout.Width(220)); GUILayout.Label(nv.ToString(fmt), lab, GUILayout.Width(60)); GUILayout.EndHorizontal();
            return nv;
        }
        int RowI(string name, int v, int min, int max) { return Mathf.RoundToInt(Row(name, v, min, max, "0")); }
        bool RowB(string name, bool v, string on, string off)
        {
            GUILayout.BeginHorizontal(); GUILayout.Label(name, lab, GUILayout.Width(200));
            if (GUILayout.Button(v ? on : off, GUILayout.Width(220))) v = !v; GUILayout.EndHorizontal(); return v;
        }

        void OnGUI()
        {
            if (!Open || Match.I == null) return;
            Styles(); GUI.depth = -20;
            float s = Mathf.Clamp(Screen.height / 1080f, .7f, 1.6f); GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            float H = Screen.height / s;
            var o = GUI.color; GUI.color = new Color(.05f, .04f, .08f, .92f); GUI.DrawTexture(new Rect(10, 10, 540, H - 20), Texture2D.whiteTexture); GUI.color = o;
            GUILayout.BeginArea(new Rect(24, 20, 512, H - 40));
            GUILayout.Label("F1 调参面板（改完立即生效）", head);
            var p = Match.I.player;
            GUILayout.BeginHorizontal();
            string[] tabs = { "3C 手感", "当前武器", "当前干员" };
            for (int i = 0; i < 3; i++) if (GUILayout.Toggle(tab == i, tabs[i], "Button", GUILayout.Height(28))) tab = i;
            GUILayout.EndHorizontal();
            scroll = GUILayout.BeginScrollView(scroll);
            if (tab == 0)
            {
                GUILayout.Label("移动", head);
                Config.WalkSpeed = Row("行走速度 m/s", Config.WalkSpeed, 2f, 9f); Config.SprintSpeed = Row("冲刺速度 m/s", Config.SprintSpeed, 3f, 11f);
                Config.CrouchSpeed = Row("蹲走速度 m/s", Config.CrouchSpeed, 1f, 5f); Tune.ProneSpeed = Row("趴下爬行速度 m/s", Tune.ProneSpeed, .4f, 3f);
                Tune.Accel = Row("地面加速度", Tune.Accel, 5f, 80f, "0"); Tune.JumpSpeed = Row("起跳速度", Tune.JumpSpeed, 3f, 9f); Tune.Gravity = Row("重力", Tune.Gravity, 8f, 30f, "0.0");
                Tune.AdsMoveMul = Row("开镜移速倍率", Tune.AdsMoveMul, .3f, 1f);
                GUILayout.Label("体力与负重", head);
                Tune.StaminaMax = Row("体力上限", Tune.StaminaMax, 30f, 200f, "0"); Tune.SprintDrain = Row("冲刺消耗 /秒", Tune.SprintDrain, 0f, 40f, "0.0");
                Tune.StaminaRegen = Row("恢复 /秒", Tune.StaminaRegen, 5f, 60f, "0.0"); Tune.RegenDelay = Row("恢复延迟 秒", Tune.RegenDelay, 0f, 3f);
                Tune.JumpCost = Row("跳跃消耗", Tune.JumpCost, 0f, 30f, "0"); Tune.MantleCost = Row("翻越消耗", Tune.MantleCost, 0f, 30f, "0");
                Tune.WeightFree = Row("免费负重 kg", Tune.WeightFree, 2f, 15f, "0.0"); Tune.WeightSpeedPerKg = Row("每超 1kg 移速 -", Tune.WeightSpeedPerKg, 0f, .05f, "0.000");
                Tune.WeightAdsPerKg = Row("每超 1kg 开镜时间 +", Tune.WeightAdsPerKg, 0f, .1f, "0.000");
                GUILayout.Label("姿态", head);
                Tune.CrouchSpreadMul = Row("蹲下散布倍率", Tune.CrouchSpreadMul, .3f, 1f); Tune.ProneSpreadMul = Row("趴下散布倍率", Tune.ProneSpreadMul, .2f, 1f);
                Tune.CrouchRecoilMul = Row("蹲下后坐倍率", Tune.CrouchRecoilMul, .3f, 1f); Tune.ProneRecoilMul = Row("趴下后坐倍率", Tune.ProneRecoilMul, .2f, 1f);
                Tune.ProneTransition = Row("趴下 / 起身耗时", Tune.ProneTransition, 0f, 1.5f);
                GUILayout.Label("操作与镜头", head);
                Tune.AdsHold = RowB("开镜方式", Tune.AdsHold, "按住右键", "点击切换"); Tune.CrouchHold = RowB("蹲下方式", Tune.CrouchHold, "按住", "点击切换");
                Tune.JumpBuffer = Row("跳跃输入缓冲 秒", Tune.JumpBuffer, 0f, .4f); Tune.CoyoteTime = Row("离地宽限 秒", Tune.CoyoteTime, 0f, .3f);
                GameSettings.Fov = Row("视野 FOV", GameSettings.Fov, 60f, 110f, "0"); GameSettings.AdsSensitivity = Row("开镜灵敏度倍率", GameSettings.AdsSensitivity, .3f, 1.5f);
                Tune.BobScale = Row("视角晃动幅度", Tune.BobScale, 0f, 2f); Tune.ShakeScale = Row("镜头震动幅度", Tune.ShakeScale, 0f, 2f);
                Tune.SprintFovAdd = Row("冲刺视野增加", Tune.SprintFovAdd, 0f, 15f, "0.0");
                GUILayout.Label("HUD 表现（V22，只影响显示）", head);
                Tune.HudStaminaFade = Row("体力条回满后淡出 秒", Tune.HudStaminaFade, .3f, 3f); Tune.HudExhaustPulseHz = Row("耗尽锁定脉冲 Hz", Tune.HudExhaustPulseHz, .5f, 6f, "0.0");
                Tune.HudRecoverFlash = Row("恢复过线闪光 秒", Tune.HudRecoverFlash, .1f, 1f);
            }
            else if (tab == 1 && p != null)
            {
                var w = p.W; int st; float ttk = Ballistics.Ttk(w, 3, 2, 20f, HitPart.Chest, out st);
                GUILayout.Label(w.id + "「" + w.name + "」  ·  20m 打 3 级甲胸部：" + st + " 发 / " + ttk.ToString("0.00") + " 秒", head);
                w.bodyDamage = Row("基础伤害", w.bodyDamage, 3f, 80f, "0.0"); w.rpm = Row("射速 RPM", w.rpm, 40f, 1200f, "0");
                w.fullRange = Row("无衰减距离 m", w.fullRange, 2f, 80f, "0"); w.falloffRange = Row("衰减到底距离 m", w.falloffRange, 5f, 120f, "0"); w.minFalloff = Row("最低伤害比例", w.minFalloff, .1f, 1f);
                w.pen = RowI("穿透等级", w.pen, 1, 6);
                w.headMul = Row("头部倍率", w.headMul, 1f, 3f); w.chestMul = Row("胸部倍率", w.chestMul, .5f, 1.5f); w.stomachMul = Row("腹部倍率", w.stomachMul, .5f, 1.5f);
                w.armMul = Row("手臂倍率", w.armMul, .3f, 1.2f); w.legMul = Row("腿部倍率", w.legMul, .3f, 1.2f);
                w.hipSpread = Row("腰射散布 °", w.hipSpread, 0f, 8f); w.adsSpread = Row("开镜散布 °", w.adsSpread, 0f, 5f); w.bloomPerShot = Row("每发扩散", w.bloomPerShot, 0f, 2f);
                w.recoil = Row("后坐（上跳）", w.recoil, 0f, 4f); w.vRamp = Row("上跳递增", w.vRamp, 0f, 1f); w.hDrift = Row("横向摆动", w.hDrift, 0f, 1.5f); w.hRandom = Row("随机偏移", w.hRandom, 0f, 1f);
                w.adsTime = Row("开镜时间 秒", w.adsTime, .05f, .8f); w.sprintToFire = Row("冲刺转开火 秒", w.sprintToFire, 0f, .6f); w.drawTime = Row("拔枪时间 秒", w.drawTime, .1f, 1.5f);
                w.reloadTac = Row("战术换弹 秒", w.reloadTac, .5f, 5f); w.reloadEmpty = Row("空仓换弹 秒", w.reloadEmpty, .5f, 6f);
                w.bulletSpeed = Row("子弹速度 m/s", w.bulletSpeed, 50f, 600f, "0"); w.dropScale = Row("下坠系数", w.dropScale, 0f, 1.5f); w.wallPen = Row("可穿透墙厚 m", w.wallPen, 0f, 1f);
                w.weight = Row("重量 kg", w.weight, .5f, 12f, "0.0"); w.adsZoom = Row("开镜放大倍率", w.adsZoom, 1f, 6f);
            }
            else if (tab == 2 && p != null)
            {
                GUILayout.Label(p.Hero.name + "（" + p.Hero.duty + "）", head);
                p.armorClass = RowI("护甲等级（护胸腹）", p.armorClass, 0, 6); p.helmetClass = RowI("头盔等级（护头）", p.helmetClass, 0, 6);
                p.Hero.armorClass = p.armorClass; p.Hero.helmetClass = p.helmetClass;
                GUILayout.Label("负重 " + Ballistics.Weight(p).ToString("0.0") + " kg · 移速 ×" + Ballistics.WeightSpeedMul(p).ToString("0.00") + " · 开镜 " + Ballistics.AdsTime(p).ToString("0.00") + " 秒", lab);
                GUILayout.Label("技能：" + p.Hero.skill.Name + " · " + p.Hero.skill.Numbers, small);
                p.Hero.skill.cooldown = Row("技能冷却 秒", p.Hero.skill.cooldown, 3f, 60f, "0");
                GUILayout.Label("当前状态：" + p.MoveStateName + " · 体力 " + p.stamina.ToString("0"), lab);
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("导出到表格", GUILayout.Height(30))) msg = DataTables.Export();
            if (GUILayout.Button("从表格重读", GUILayout.Height(30))) { DataTables.Reload(); msg = DataTables.LastStatus; }
            if (GUILayout.Button("恢复默认", GUILayout.Height(30))) { DataTables.RestoreDefaults(); msg = DataTables.LastStatus; }
            GUILayout.EndHorizontal();
            GUILayout.Label(string.IsNullOrEmpty(msg) ? "F1 关闭 · F2 显示判定框 · 表格：" + DataTables.Dir : msg, small);
            GUILayout.EndArea();
        }
    }

    // ---------------------------------------------------------------- 战斗 HUD：准星下方状态带（体力 / 姿态）+ 死亡回顾
    // V22 第一期：动态状态贴准星，静态信息放边角；默认隐藏，跨过阈值才出现。显示判断都是静态函数，PlaytestRunner 直接调用（M30 / M33）。
    public enum StaminaLook { Hidden, Draining, Low, Delay, Regen, Locked, FullFade }
    public class CombatHud : MonoBehaviour
    {
        static GUIStyle lab, head, small; float diedAt = -1; bool wasAlive = true;
        static string lastPosture; static float postureAt = -10;
        public const float BandY = 62f, ArcR = 170f, ArcSpan = 26f;   // 弧顶在准星下 62 px，半径 170，±26° → 弦长约 150 px

        // ---- 显示判断（测试直接调用）
        public static float StaminaAlpha(Fighter p)
        {
            if (p == null || !p.alive) return 0f;
            if (p.stamina < Tune.StaminaMax - .5f) return 1f;
            return Mathf.Clamp01(1f - (Time.time - p.staminaFullAt) / Mathf.Max(.05f, Tune.HudStaminaFade));
        }
        public static StaminaLook StaminaState(Fighter p)
        {
            if (StaminaAlpha(p) <= .01f) return StaminaLook.Hidden;
            if (p.exhausted) return StaminaLook.Locked;
            if (p.stamina >= Tune.StaminaMax - .5f) return StaminaLook.FullFade;
            if (p.sprinting && !p.Sliding) return p.stamina < Tune.ExhaustRecover ? StaminaLook.Low : StaminaLook.Draining;
            return Time.time - p.StaminaUsedAt <= Tune.RegenDelay ? StaminaLook.Delay : StaminaLook.Regen;
        }
        public static string Posture(Fighter p) { return p == null || !p.alive ? null : p.Sliding ? "slide" : p.Prone ? "prone" : p.crouching ? "crouch" : null; }
        public static string PostureName(string k) { return k == "slide" ? "滑铲" : k == "prone" ? "趴下" : k == "crouch" ? "蹲下" : ""; }
        // 状态带上要画的元素（站立 + 满体力 + 不在切换时为空）
        public static List<string> Elements(Fighter p)
        {
            var list = new List<string>();
            if (p == null || !p.alive) return list;
            if (StaminaAlpha(p) > .01f) list.Add("stamina");
            if (p.exhausted) list.Add("lock");
            if (Time.time - p.staminaRecoveredAt < Tune.HudRecoverFlash) list.Add("flash");
            var po = Posture(p); if (po != null) list.Add("posture:" + po);
            if (p.ProneChangeK < 1f) list.Add("proneRing");
            return list;
        }
        public static Color StaminaColor(StaminaLook s)
        {
            switch (s)
            {
                case StaminaLook.Locked: return new Color(1f, .36f, .36f);
                case StaminaLook.Low: return new Color(1f, .84f, .3f);
                case StaminaLook.Delay: return new Color(.78f, .78f, .84f);
                case StaminaLook.Regen: return new Color(.55f, 1f, .74f);
                case StaminaLook.FullFade: return new Color(.6f, 1f, .78f);
                default: return new Color(1f, .97f, .93f);
            }
        }

        // ---- 死亡回顾文字（测试直接调用）
        public static bool LastHit(Fighter p, out HitRecord h) { h = default; if (p == null || p.damageLog.Count == 0) return false; h = p.damageLog[p.damageLog.Count - 1]; return true; }
        public static string RecapTitle(Fighter p)
        {
            if (!LastHit(p, out var h)) return "阵亡";
            var k = p.recapKiller;
            string dist = h.distance > .5f ? "（" + h.distance.ToString("0") + " 米）" : "";
            if (k == null) return "被 " + h.weapon + " 击倒";
            if (k == p) return "被自己的 " + h.weapon + " 击倒";
            return "被 " + k.Hero.name + " 用 " + h.weapon + " 击倒" + dist;
        }
        public static string RecapKillerLine(Fighter p)
        {
            var k = p != null ? p.recapKiller : null;
            if (k == null || k == p) return "";
            string left = p.recapKillerHp > 0 ? "对手剩余 " + Mathf.CeilToInt(p.recapKillerHp) + " 生命 · " + Mathf.CeilToInt(p.recapKillerArmor) + " 护甲" : "对手也倒下了";
            return left + "　·　你对他造成 " + Mathf.RoundToInt(p.recapDealt) + " 伤害";
        }
        public static string RowLabel(HitRecord h) { return h.hasPart ? Ballistics.PartName(h.part) : DamageCtx.KindName(h.kind); }

        // ---- 绘制
        void OnGUI()
        {
            var m = Match.I; if (m == null || m.player == null || TuningPanel.Open) return;
            if (m.ui != null && !m.ui.Started && !m.autoplay && !m.testMode) return;
            if (lab == null)
            {
                var font = Resources.Load<Font>("NotoSansSC");
                lab = new GUIStyle(GUI.skin.label) { font = font, fontSize = 14, normal = { textColor = Color.white } };
                head = new GUIStyle(lab) { fontSize = 19, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                small = new GUIStyle(lab) { fontSize = 12 };
            }
            GUI.depth = -3;   // 画在 GameUI（0）之上：世界标记 / 名牌不压住死亡回顾和状态带
            float s = Mathf.Clamp(Screen.height / 1080f, .7f, 1.6f); GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            float W = Screen.width / s, H = Screen.height / s; var p = m.player;
            if (p.alive && !wasAlive) diedAt = -1;
            if (!p.alive && wasAlive) diedAt = Time.time;
            wasAlive = p.alive;
            if (p.alive) { if (m.ui == null || !m.ui.HudOverlayOpen) Band(W, H, p); return; }
            if (diedAt < 0 || Time.time - diedAt > 7f || p.damageLog.Count == 0) return;
            Recap(W, H, p);
        }

        // 准星下方状态带：细弧形体力条（25% 解锁刻度）+ 白色豆子姿态剪影
        void Band(float W, float H, Fighter p)
        {
            float cx = W / 2, cy = H / 2;
            Vector2 c = new Vector2(cx, cy + BandY + ArcR);
            float a = StaminaAlpha(p); var st = StaminaState(p);
            if (a > .01f)
            {
                float frac = Mathf.Clamp01(p.stamina / Tune.StaminaMax), tick = Mathf.Clamp01(Tune.ExhaustRecover / Tune.StaminaMax);
                float a0 = -ArcSpan, a1 = ArcSpan, af = Mathf.Lerp(a0, a1, frac), at = Mathf.Lerp(a0, a1, tick);
                Color col = StaminaColor(st);
                float pulse = st == StaminaLook.Locked ? .5f + .5f * Mathf.Sin(Time.time * Tune.HudExhaustPulseHz * Mathf.PI * 2f) : 0f;
                // V22 打磨：弧加粗约 2 倍（填充 10 px），外圈深色描边 + 深色半透明底槽，亮地面上也清楚（对照 UI-1 / UI-2）
                if (st == StaminaLook.Locked) Arc(c, ArcR, a0 - 1.5f, a1 + 1.5f, 22 + 6 * pulse, new Color(1f, .25f, .25f, (.12f + .2f * pulse) * a));   // 红色呼吸光
                Arc(c, ArcR, a0 - .9f, a1 + .9f, 16, new Color(.16f, .06f, .14f, .7f * a));   // 描边
                Arc(c, ArcR, a0 - .3f, a1 + .3f, 12, st == StaminaLook.Locked ? new Color(.45f, .08f, .1f, (.75f + .15f * pulse) * a) : new Color(.1f, .05f, .1f, .45f * a));   // 深色底槽（锁定时变暗红）
                if (frac > .005f) Arc(c, ArcR, a0, af, 10, new Color(col.r, col.g, col.b, a));
                if (frac > .005f) Arc(c, ArcR - 3f, a0, af, 2, new Color(1, 1, 1, .35f * a));   // 顶部高光
                bool hot = st == StaminaLook.Locked || st == StaminaLook.Low;
                Tick(c, ArcR, at, 22, 5, new Color(.16f, .06f, .14f, .8f * a));
                Tick(c, ArcR, at, hot ? 20 : 18, hot ? 3f : 2.5f, hot ? new Color(1f, .95f, .9f, a) : new Color(1, 1, 1, .85f * a));
                if (st == StaminaLook.Locked)
                {
                    Vector2 lp = ArcPoint(c, ArcR, a0 - 9f);
                    Icons.Draw(new Rect(lp.x - 12 + 1.5f, lp.y - 13 + 1.5f, 24, 24), Icons.Lock, new Color(0, 0, 0, .55f * a));
                    Icons.Draw(new Rect(lp.x - 12, lp.y - 13, 24, 24), Icons.Lock, new Color(1f, .4f + .2f * pulse, .4f + .2f * pulse, a));
                }
                float fk = (Time.time - p.staminaRecoveredAt) / Mathf.Max(.05f, Tune.HudRecoverFlash);
                if (fk >= 0 && fk < 1)   // 恢复过线：闪一下 + 刻度旁小星星
                {
                    Arc(c, ArcR, a0, af, 10 + 12 * (1 - fk), new Color(1f, 1f, .85f, .7f * (1 - fk)));
                    Vector2 sp = ArcPoint(c, ArcR - 20, at + 4);
                    float sz = 10 + 10 * fk; Icons.Draw(new Rect(sp.x - sz / 2, sp.y - sz / 2, sz, sz), Icons.Diamond, new Color(1f, .95f, .5f, 1 - fk));
                }
            }
            string po = Posture(p);
            if (po != lastPosture) { lastPosture = po; postureAt = Time.time; }
            if (po != null)
            {
                float pop = Mathf.Lerp(1.3f, 1f, Mathf.Clamp01((Time.time - postureAt) / .15f));
                float sz = 40 * pop; Rect r = new Rect(cx - sz / 2, cy + BandY + 42 - sz / 2, sz, sz);
                GUI.DrawTexture(new Rect(cx - 25, cy + BandY + 17, 50, 50), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(.12f, .06f, .12f, .3f), 0, 25);   // 柔和深色底
                var tex = po == "slide" ? Icons.BeanSlide : po == "prone" ? Icons.BeanProne : Icons.BeanCrouch;
                Icons.Draw(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), tex, new Color(0, 0, 0, .45f));
                Icons.Draw(r, tex, new Color(1, 1, 1, p.ProneChangeK < 1f ? .55f + .45f * p.ProneChangeK : .95f));
            }
        }

        // V22：GUIUtility.RotateAroundPivot 的支点按屏幕像素算，而 HUD 都在 GUI.matrix 缩放后的坐标里画 —— 非 1080p 时旋转中心会偏。先把支点换算到屏幕像素。
        public static void RotP(float angle, Vector2 pivot) { GUIUtility.RotateAroundPivot(angle, GUI.matrix.MultiplyPoint3x4(pivot)); }
        public static Vector2 ArcPoint(Vector2 c, float r, float deg) { float a = deg * Mathf.Deg2Rad; return new Vector2(c.x + Mathf.Sin(a) * r, c.y - Mathf.Cos(a) * r); }
        // 角度 0 = 正上方，顺时针为正
        public static void Arc(Vector2 c, float r, float a0, float a1, float thick, Color col)
        {
            int segs = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(a1 - a0) / 1.5f));
            float step = (a1 - a0) / segs, len = r * Mathf.Abs(step) * Mathf.Deg2Rad + .8f; var m = GUI.matrix;
            for (int i = 0; i < segs; i++)
            {
                RotP(a0 + step * (i + .5f), c);
                Fill(new Rect(c.x - len / 2, c.y - r - thick / 2, len, thick), col);
                GUI.matrix = m;
            }
        }
        static void Tick(Vector2 c, float r, float deg, float len, float thick, Color col)
        {
            var m = GUI.matrix; RotP(deg, c);
            Fill(new Rect(c.x - thick / 2, c.y - r - len / 2, thick, len), col); GUI.matrix = m;
        }

        // 死亡回顾（参考图 UI-5）：骷髅抬头 → 击杀者 / 武器或技能 / 距离 → 豆子剪影命中点 + 每条伤害 → 对手剩余生命
        // V22 打磨：字号放大（标题 24 / 正文 16），面板 760×494，1600×900 下也不挤；剪影上的命中点 18 px 带白边 + 深色描边
        static GUIStyle rTitle, rText, rBold, rNum;
        void Recap(float W, float H, Fighter p)
        {
            bool food = ToonAssets.Available;
            if (rTitle == null)
            {
                rTitle = new GUIStyle(lab) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                rText = new GUIStyle(lab) { fontSize = 16, alignment = TextAnchor.MiddleLeft };
                rBold = new GUIStyle(rText) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                rNum = new GUIStyle(rText) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            }
            Color bg = food ? new Color(1f, .93f, .95f, .97f) : new Color(.07f, .05f, .08f, .93f), card = food ? new Color(.98f, .84f, .89f, .92f) : new Color(.14f, .1f, .14f, .92f);
            Color ink = food ? new Color(.36f, .14f, .24f) : new Color(1f, .92f, .9f), accent = food ? new Color(.93f, .38f, .55f) : new Color(1f, .4f, .35f), dim = food ? new Color(.58f, .4f, .48f) : new Color(.7f, .65f, .7f);
            float age = Time.time - diedAt, ap = Mathf.Clamp01(age / .2f) * Mathf.Clamp01((7f - age) / .4f);
            var oc = GUI.color; GUI.color = new Color(1, 1, 1, ap);
            float pw = 760, ph = 494, x = W / 2 - pw / 2, y = H * .5f - ph / 2 + 20;
            Round(new Rect(x, y, pw, ph), bg, 26); Round(new Rect(x, y, pw, ph), accent, 26, 2.5f);
            Round(new Rect(W / 2 - 74, y - 34, 148, 64), accent, 30);
            Icons.Draw(new Rect(W / 2 - 22, y - 29, 44, 44), Icons.Skull, Color.white);
            LastHit(p, out var last); var k = p.recapKiller;
            Text(new Rect(x + 20, y + 34, pw - 40, 38), RecapTitle(p), rTitle, ink);
            // 第一行：击杀者头像 | 武器 / 技能 | 距离
            Rect r1 = new Rect(x + 22, y + 82, pw - 44, 124); Round(r1, card, 16);
            Rect face = new Rect(r1.x + 12, r1.y + 10, 140, 104);
            Round(face, k != null ? new Color(k.Hero.tint.r, k.Hero.tint.g, k.Hero.tint.b, .5f) : new Color(.5f, .5f, .5f, .4f), 14);
            if (k != null && Portraits.hero.TryGetValue(k.role, out var ft) && ft != null) GUI.DrawTextureWithTexCoords(face, ft, new Rect(.1f, .3f, .8f, .62f));
            else Icons.Draw(new Rect(face.x + 36, face.y + 16, 68, 68), Icons.Skull, new Color(1, 1, 1, .8f));
            Round(new Rect(face.x, face.yMax - 26, face.width, 26), new Color(0, 0, 0, .35f), 10);
            Text(new Rect(face.x, face.yMax - 26, face.width, 26), k != null ? k.Hero.name : "环境", rBold, Color.white);
            Fill(new Rect(r1.x + 168, r1.y + 16, 2, 92), new Color(ink.r, ink.g, ink.b, .25f));
            Rect wr = new Rect(r1.x + 184, r1.y + 8, 330, 76);
            if (last.gun != null) { var gi = GameUI.GunIcon(last.gun); if (gi != null) GUI.DrawTexture(wr, gi, ScaleMode.ScaleToFit); }
            else Icons.Draw(new Rect(wr.center.x - 34, wr.y + 4, 68, 68), CauseIcon(last.kind, k), last.kind == DamageKind.Ult ? new Color(1f, .75f, .25f) : accent);
            Text(new Rect(wr.x, r1.yMax - 34, wr.width, 28), last.weapon + "　·　" + DamageCtx.KindName(last.kind), new GUIStyle(rText) { alignment = TextAnchor.MiddleCenter }, ink);
            Fill(new Rect(r1.x + 530, r1.y + 16, 2, 92), new Color(ink.r, ink.g, ink.b, .25f));
            Rect dr = new Rect(r1.x + 546, r1.y + 18, r1.width - 560, 88);
            Icons.Draw(new Rect(dr.center.x - 13, dr.y, 26, 26), Icons.Diamond, accent);
            Round(new Rect(dr.x + 14, dr.y + 38, dr.width - 28, 38), new Color(ink.r, ink.g, ink.b, .12f), 19);
            Text(new Rect(dr.x, dr.y + 38, dr.width, 38), last.distance.ToString("0") + " 米", new GUIStyle(rBold) { fontSize = 20 }, ink);
            // 第二行：剪影命中点 + 伤害列表（最近 5 条）
            Rect r2 = new Rect(x + 22, y + 218, pw - 44, 190); Round(r2, card, 16);
            Rect body = new Rect(r2.x + 22, r2.y + 12, 136, 166);
            Color silOutline = food ? new Color(ink.r, ink.g, ink.b, .55f) : new Color(0, 0, 0, .6f), silFill = food ? new Color(1f, .97f, .98f) : new Color(.8f, .8f, .86f);
            for (int o = 0; o < 4; o++) { float ox = o == 0 ? -2 : o == 1 ? 2 : 0, oy = o == 2 ? -2 : o == 3 ? 2 : 0; Icons.Draw(new Rect(body.x + ox, body.y + oy, body.width, body.height), Icons.BeanStand, silOutline); }
            Icons.Draw(body, Icons.BeanStand, silFill);
            int start = Mathf.Max(0, p.damageLog.Count - 5), armSide = 0;
            Color[] dots = { new Color(.92f, .2f, .35f), new Color(1f, .62f, .1f), new Color(.55f, .35f, 1f), new Color(.15f, .6f, 1f), new Color(.2f, .75f, .4f) };
            Fill(new Rect(r2.x + 176, r2.y + 14, 2, 162), new Color(ink.r, ink.g, ink.b, .25f));
            float maxDmg = 1; for (int i = start; i < p.damageLog.Count; i++) maxDmg = Mathf.Max(maxDmg, p.damageLog[i].hp + p.damageLog[i].armor);
            for (int i = start; i < p.damageLog.Count; i++)
            {
                var h = p.damageLog[i];
                Color dc = h.source == k && k != null ? dots[0] : dots[1 + (i - start) % (dots.Length - 1)];
                if (h.hasPart)
                {
                    Vector2 dp = DotPos(h.part, ref armSide);
                    Vector2 dpx = new Vector2(body.x + body.width * dp.x, body.y + body.height * dp.y);
                    Round(new Rect(dpx.x - 11, dpx.y - 11, 22, 22), new Color(.2f, .05f, .12f, .6f), 11);   // 深色描边
                    Round(new Rect(dpx.x - 10, dpx.y - 10, 20, 20), Color.white, 10);                       // 白边
                    Round(new Rect(dpx.x - 7, dpx.y - 7, 14, 14), dc, 7);
                }
                else Round(new Rect(body.x - 4, body.y - 4, body.width + 8, body.height + 8), new Color(dc.r, dc.g, dc.b, .95f), 50, 3.5f);   // 爆炸 / 燃烧：整圈
                float ry = r2.y + 14 + (i - start) * 34;
                Round(new Rect(r2.x + 192, ry + 9, 16, 16), dc, 8);
                Text(new Rect(r2.x + 216, ry, 200, 34), RowLabel(h) + " · " + h.attacker, rText, ink);
                Rect bar = new Rect(r2.x + 420, ry + 10, 190, 14); Round(bar, new Color(ink.r, ink.g, ink.b, .15f), 7);
                float hpW = bar.width * h.hp / maxDmg, arW = bar.width * h.armor / maxDmg;
                if (hpW > 1) Round(new Rect(bar.x, bar.y, hpW, bar.height), accent, 7);
                if (arW > 1) Round(new Rect(bar.x + hpW, bar.y, arW, bar.height), new Color(.35f, .7f, 1f), 7);
                Text(new Rect(r2.x + 616, ry, r2.width - 630, 34), "-" + Mathf.RoundToInt(h.hp + h.armor), rNum, ink);
            }
            // 第三行：对手剩余生命
            Rect r3 = new Rect(x + 22, y + 418, pw - 44, 62); Round(r3, card, 16);
            if (k != null && k != p)
            {
                Round(new Rect(r3.x + 12, r3.y + 9, 44, 44), Config.TeamColor(k.team), 12);
                if (Portraits.hero.TryGetValue(k.role, out var kt) && kt != null) GUI.DrawTextureWithTexCoords(new Rect(r3.x + 12, r3.y + 9, 44, 44), kt, new Rect(.25f, .62f, .5f, .35f));
                Rect hb = new Rect(r3.x + 70, r3.y + 14, 250, 18); Round(hb, new Color(ink.r, ink.g, ink.b, .18f), 9);
                float hk = Mathf.Clamp01(p.recapKillerHp / Config.MaxHp); if (hk > .01f) Round(new Rect(hb.x, hb.y, hb.width * hk, hb.height), new Color(.35f, .82f, .45f), 9);
                Rect ab = new Rect(hb.x, hb.yMax + 5, 250, 8); Round(ab, new Color(ink.r, ink.g, ink.b, .12f), 4);
                float ak = Mathf.Clamp01(p.recapKillerArmor / Config.MaxArmor); if (ak > .01f) Round(new Rect(ab.x, ab.y, ab.width * ak, ab.height), new Color(.35f, .7f, 1f), 4);
                var kl = RecapKillerLine(p).Split(new[] { "　·　" }, System.StringSplitOptions.None);   // 两行：剩余生命 / 你打了他多少
                Text(new Rect(r3.x + 336, r3.y + 4, r3.width - 346, 28), kl[0], new GUIStyle(rText) { fontStyle = FontStyle.Bold }, ink);
                if (kl.Length > 1) Text(new Rect(r3.x + 336, r3.y + 30, r3.width - 346, 28), kl[1], new GUIStyle(rText) { fontSize = 15 }, dim);
            }
            else Text(new Rect(r3.x, r3.y, r3.width, 62), k == p ? "被自己的攻击击倒" : "没有对手（环境 / 爆炸）", new GUIStyle(rText) { alignment = TextAnchor.MiddleCenter }, dim);
            GUI.color = oc;
        }
        static Vector2 DotPos(HitPart part, ref int armSide)
        {
            switch (part)
            {
                case HitPart.Head: return new Vector2(.5f, .2f);
                case HitPart.Chest: return new Vector2(.5f, .44f);
                case HitPart.Stomach: return new Vector2(.5f, .6f);
                case HitPart.Arm: armSide++; return new Vector2(armSide % 2 == 1 ? .12f : .88f, .55f);
                default: armSide++; return new Vector2(armSide % 2 == 1 ? .38f : .62f, .9f);
            }
        }
        public static Texture2D CauseIcon(DamageKind kind, Fighter k)
        {
            switch (kind)
            {
                case DamageKind.Skill: case DamageKind.Ult: return k != null ? Icons.Skill(k.role) : Icons.Skull;
                case DamageKind.Melee: return Icons.Chevron;
                case DamageKind.Fire: case DamageKind.Grenade: case DamageKind.Bomb: return Icons.Skill(Role.Ember);
                default: return Icons.Skull;
            }
        }
        static void Fill(Rect r, Color c) { var o = GUI.color; GUI.color = new Color(c.r, c.g, c.b, c.a * o.a); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = o; }
        static void Round(Rect r, Color c, float radius, float border = 0) { GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(c.r, c.g, c.b, c.a * GUI.color.a), border, radius); }
        static void Text(Rect r, string t, GUIStyle st, Color c) { var o = st.normal.textColor; st.normal.textColor = c; GUI.Label(r, t, st); st.normal.textColor = o; }
    }

    // ---------------------------------------------------------------- 靶场
    public static class Range
    {
        public static readonly Vector3 Origin = new Vector3(0, 0, -262);
        static readonly float[] Dist = { 10, 25, 50, 75 };
        static readonly float[] Xoff = { -6, -2, 2, 6 };
        static readonly int[] BaseClass = { 0, 3, 4, 5 };
        public static int ClassShift;
        static GameObject root;
        public class Lane { public Fighter dummy; public Vector3 pos; public int baseClass; public float dist; public bool moving; public float firstHit = -1, respawnAt = -1; public int hits; public string result = ""; public TextMesh label; }
        public static readonly List<Lane> Lanes = new List<Lane>();
        public static HitRecord? LastHit;
        public static readonly List<Vector2> Spray = new List<Vector2>();   // 最近一次连射的准星偏移（实际后坐）

        public static void Build()
        {
            if (root != null) return;
            root = new GameObject("靶场");
            Material M(Color c) { var m = new Material(Shader.Find("Standard")) { color = c }; m.SetFloat("_Glossiness", .3f); m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * .35f); return m; }   // 靶场不在烘焙范围内，加一点自发光免得背光面发黑
            void B(Vector3 c, Vector3 s, Material m) { var o = GameObject.CreatePrimitive(PrimitiveType.Cube); o.transform.SetParent(root.transform, false); o.transform.position = c; o.transform.localScale = s; o.GetComponent<Renderer>().sharedMaterial = m; }
            var floor = M(new Color(.86f, .84f, .98f)); var wall = M(new Color(1f, .8f, .86f)); var line = M(new Color(1f, 1f, 1f)); var back = M(new Color(.55f, .78f, 1f));
            B(Origin + new Vector3(0, -.25f, 50), new Vector3(32, .5f, 116), floor);
            B(Origin + new Vector3(-16, 2.5f, 50), new Vector3(.6f, 5, 116), wall); B(Origin + new Vector3(16, 2.5f, 50), new Vector3(.6f, 5, 116), wall);
            B(Origin + new Vector3(0, 4f, 104), new Vector3(32, 8, .8f), back); B(Origin + new Vector3(0, 2.5f, -8), new Vector3(32, 5, .6f), wall);
            var font = Resources.Load<Font>("NotoSansSC");
            for (int d = 10; d <= 100; d += 10)
            {
                B(Origin + new Vector3(0, .01f, d), new Vector3(31, .02f, .12f), line);
                var t = new GameObject(d + "m").AddComponent<TextMesh>(); t.transform.SetParent(root.transform, false);
                t.transform.position = Origin + new Vector3(-14.5f, .05f, d - .5f); t.transform.rotation = Quaternion.Euler(90, 0, 0);
                t.font = font; if (font != null) t.GetComponent<MeshRenderer>().sharedMaterial = Arena.WorldText(font);
                t.text = d + " 米"; t.fontSize = 64; t.characterSize = .08f; t.color = new Color(.4f, .3f, .55f);
            }
            // 薄墙（穿墙测试用）
            var thin = M(new Color(.95f, .9f, .7f));
            B(Origin + new Vector3(-11, 1.2f, 18), new Vector3(3, 2.4f, .25f), thin);
            var tw = new GameObject("薄墙说明").AddComponent<TextMesh>(); tw.transform.SetParent(root.transform, false); tw.transform.position = Origin + new Vector3(-11, 2.9f, 18);
            tw.font = font; if (font != null) tw.GetComponent<MeshRenderer>().sharedMaterial = Arena.WorldText(font); tw.text = "25cm 薄墙（测穿墙）"; tw.anchor = TextAnchor.MiddleCenter; tw.fontSize = 48; tw.characterSize = .05f; tw.color = new Color(.5f, .35f, .2f);
        }

        public static void Setup(Match m)
        {
            Build(); Lanes.Clear();
            var p = m.player;
            int k = 0;
            foreach (var f in m.fighters)
            {
                if (f == p) continue;
                var b = f.GetComponent<Bot>(); if (b != null) b.enabled = false;
                bool moving = k == 4;
                var lane = new Lane { dummy = f, moving = moving, dist = moving ? 20f : Dist[k], baseClass = moving ? 3 : BaseClass[k] };
                lane.pos = Origin + (moving ? new Vector3(0, .05f, 20f) : new Vector3(Xoff[k], .05f, Dist[k]));
                var lt = new GameObject("靶子说明").AddComponent<TextMesh>(); lt.transform.SetParent(f.transform, false); lt.transform.localPosition = Vector3.up * 3.1f; lt.transform.localRotation = Quaternion.Euler(0, 180, 0);
                var font = Resources.Load<Font>("NotoSansSC"); lt.font = font; if (font != null) lt.GetComponent<MeshRenderer>().sharedMaterial = Arena.WorldText(font);
                lt.anchor = TextAnchor.MiddleCenter; lt.fontSize = 48; lt.characterSize = .06f; lt.color = Color.white; lane.label = lt;
                Lanes.Add(lane); Respawn(lane); k++;
                if (k >= 5) break;
            }
            if (p != null) { p.Respawn(Origin, 0); p.weapon = p.Hero.weapon; p.BuildVisuals(); }
        }

        static int ClassOf(Lane l) { return l.baseClass == 0 ? 0 : Mathf.Clamp(l.baseClass + ClassShift, 1, 6); }
        static void Respawn(Lane l)
        {
            var f = l.dummy; f.Respawn(l.pos, 180); f.armorOverride = ClassOf(l);
            f.armorClass = f.helmetClass = ClassOf(l); f.armor = ClassOf(l) > 0 ? Config.MaxArmor : 0;
            f.botMove = Vector3.zero; f.botTrigger = false; l.firstHit = -1; l.hits = 0; l.respawnAt = -1;
            if (l.label != null) l.label.text = (l.moving ? "移动靶 · " : "") + l.dist.ToString("0") + "米 · " + (ClassOf(l) == 0 ? "无甲" : ClassOf(l) + "级甲");
        }
        public static void CycleArmor() { ClassShift = (ClassShift + 1) % 3; foreach (var l in Lanes) Respawn(l); }

        public static void Tick(Match m)
        {
            foreach (var l in Lanes)
            {
                if (l.dummy == null) continue;
                if (!l.dummy.alive) { if (l.respawnAt > 0 && Time.time >= l.respawnAt) Respawn(l); continue; }
                if (l.moving)
                {
                    float x = Mathf.Sin(Time.time * .9f) * 8f;
                    Vector3 target = Origin + new Vector3(x, l.dummy.transform.position.y - Origin.y, 20f);
                    l.dummy.cc.Move(target - l.dummy.transform.position);
                }
                if (l.dummy.damageLog.Count > 0 && l.firstHit < 0) l.firstHit = l.dummy.damageLog[0].time;
            }
            var p = m.player;
            if (p != null && p.alive)
            {
                if (Input.GetKeyDown(KeyCode.RightBracket) || Input.GetKeyDown(KeyCode.LeftBracket))
                {
                    var a = Config.Arsenal; int i = System.Array.IndexOf(a, p.W); i = (i + (Input.GetKeyDown(KeyCode.RightBracket) ? 1 : a.Length - 1)) % a.Length;
                    p.Equip(a[i]); Spray.Clear();
                }
                if (Input.GetKeyDown(KeyCode.T)) CycleArmor();
                if (Input.GetKeyDown(KeyCode.Y)) foreach (var l in Lanes) Respawn(l);
            }
            if (p != null && !p.alive) p.Respawn(Origin, 0);
        }

        public static void OnKilled(Fighter victim)
        {
            foreach (var l in Lanes)
                if (l.dummy == victim)
                {
                    float actual = l.firstHit >= 0 ? Time.time - l.firstHit : 0;
                    var killer = Match.I.player; var w = killer != null ? killer.W : Config.Pistol;
                    var main = victim.damageLog.Count > 0 ? victim.damageLog[victim.damageLog.Count - 1].part : HitPart.Chest;
                    float theo = Ballistics.Ttk(w, ClassOf(l), ClassOf(l), l.dist, main, out int st);
                    l.result = w.id + " · " + victim.damageLog.Count + " 发 · 实际 " + actual.ToString("0.00") + " 秒 · 理论（全中" + Ballistics.PartName(main) + "）" + st + " 发 " + theo.ToString("0.00") + " 秒";
                    l.respawnAt = Time.time + 1.5f;
                    Debug.Log("NEON_RANGE " + l.dist + "m class" + ClassOf(l) + " " + l.result);
                }
        }
    }

    public class RangeHud : MonoBehaviour
    {
        static GUIStyle lab, head;
        void OnGUI()
        {
            var m = Match.I; if (m == null || Match.Mode != GameMode.Range || TuningPanel.Open) return;
            if (m.ui != null && !m.ui.Started && !m.autoplay && !m.testMode) return;
            if (lab == null) { var font = Resources.Load<Font>("NotoSansSC"); lab = new GUIStyle(GUI.skin.label) { font = font, fontSize = 13, normal = { textColor = Color.white } }; head = new GUIStyle(lab) { fontSize = 16, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, .8f, .4f) } }; }
            float s = Mathf.Clamp(Screen.height / 1080f, .7f, 1.6f); GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            float W = Screen.width / s; var p = m.player; if (p == null) return;
            var w = p.W;
            Rect r = new Rect(W - 470, 100, 455, 560); Fill(r, new Color(.06f, .04f, .09f, .86f));
            float y = r.y + 10, x = r.x + 14;
            void L(string t, GUIStyle st = null) { GUI.Label(new Rect(x, y, r.width - 20, 22), t, st ?? lab); y += st == head ? 24 : 19; }
            L("靶场 · " + w.id + "「" + w.name + "」", head);
            L("伤害 " + w.bodyDamage + (w.pellets > 1 ? "×" + w.pellets : "") + " · " + w.rpm + " RPM · 穿透 " + w.pen + " 级 · 弹匣 " + w.magazine);
            L("部位倍率 头 " + w.headMul + " / 胸 " + w.chestMul + " / 腹 " + w.stomachMul + " / 臂 " + w.armMul + " / 腿 " + w.legMul);
            L("衰减 " + w.fullRange + "→" + w.falloffRange + " 米（最低 " + (w.minFalloff * 100).ToString("0") + "%）· 弹速 " + w.bulletSpeed + " m/s · 下坠 " + w.dropScale);
            L("开镜 " + Ballistics.AdsTime(p).ToString("0.00") + "s · 冲刺转开火 " + w.sprintToFire + "s · 拔枪 " + w.drawTime + "s");
            L("战术换弹 " + w.reloadTac + "s / 空仓换弹 " + w.reloadEmpty + "s · 重量 " + w.weight + "kg · 穿墙 ≤" + (w.wallPen * 100).ToString("0") + "cm");
            y += 6; L("理论 TTK（全部打胸 / 全部打头）", head);
            foreach (int cls in new[] { 0, 3, 5 })
                foreach (float d in new[] { 10f, 50f })
                {
                    float t1 = Ballistics.Ttk(w, cls, cls, d, HitPart.Chest, out int s1), t2 = Ballistics.Ttk(w, cls, cls, d, HitPart.Head, out int s2);
                    L((cls == 0 ? "无甲" : cls + "级甲") + " · " + d + "米：胸 " + s1 + " 发 " + t1.ToString("0.00") + "s · 头 " + s2 + " 发 " + t2.ToString("0.00") + "s");
                }
            y += 6; L("最近一发", head);
            if (Range.LastHit.HasValue) { var h = Range.LastHit.Value; L(Ballistics.PartName(h.part) + " · 生命 -" + h.hp.ToString("0.0") + " · 护甲 -" + h.armor.ToString("0.0") + " · " + h.distance.ToString("0") + " 米 · " + (h.cls == 0 ? "无甲" : "穿透 " + h.pen + " vs " + h.cls + " 级")); }
            else L("向靶子开火看看");
            y += 6; L("实测击杀", head);
            foreach (var l in Range.Lanes) L((l.moving ? "移动靶" : l.dist + "米") + "：" + (string.IsNullOrEmpty(l.result) ? "—" : l.result));
            y += 4; GUI.Label(new Rect(x, r.yMax - 24, r.width, 22), "[ ] 换枪 · T 换护甲等级 · Y 重置靶子 · F1 调参 · F2 判定框", lab);
            // 弹道图：灰点 = 固定弹道，粉点 = 你这次连射的实际后坐
            Rect g = new Rect(W - 470, 670, 300, 230); Fill(g, new Color(.06f, .04f, .09f, .86f));
            GUI.Label(new Rect(g.x + 8, g.y + 4, 290, 20), "弹道图（灰 = 固定弹道，粉 = 你的实际后坐）", lab);
            Vector2 c0 = new Vector2(g.center.x, g.yMax - 20); float k = 6f; Vector2 acc = Vector2.zero;
            for (int i = 1; i <= Mathf.Min(30, w.magazine); i++) { acc += Ballistics.PatternStep(w, i); Fill(new Rect(c0.x + acc.x * k - 2, c0.y - acc.y * k - 2, 4, 4), new Color(.7f, .7f, .75f, .8f)); }
            foreach (var sp in Range.Spray) Fill(new Rect(c0.x + sp.x * k - 3, c0.y - sp.y * k - 3, 6, 6), new Color(1f, .45f, .75f));
        }
        static void Fill(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = o; }
    }
}
