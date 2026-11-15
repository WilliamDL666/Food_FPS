using System.Collections.Generic;
using UnityEngine;

namespace NeonSiege
{
    // ================================================================ V21：可发现性（设计稿_V21_美食技能与可发现性 §2.2，引导方案乙）
    // 所有给玩家看的规则文字都从这里拼出来：气泡、HUD、帮助页、开局横幅、提示卡共用，测试直接调这些函数比字符串。
    public static class FoodGuide
    {
        public static float DetailRange = 12f;     // 10–14 m 内气泡显示配方，更远只显示名字
        public const int BubbleMax = 18;           // 每个气泡不超过 18 字
        public static float HoldToPick = .4f;      // 规则甲：凑不成菜的第二样食材，按住 G 0.4 s 才捡（设计区间 0.3–0.6）

        public static readonly Color Ok = new Color(.4f, 1f, .5f), Grey = new Color(.42f, .42f, .46f);

        // ---- 配方
        public static List<string> Partners(string id)
        {
            var l = new List<string>();
            foreach (var o in Ingredients.All) if (o != id && Ingredients.Recipe(id, o) != FoodForm.Dark) l.Add(o);
            return l;
        }
        static string Names(List<string> ids, string sep) { var s = new List<string>(); foreach (var i in ids) s.Add(Ingredients.Name(i)); return string.Join(sep, s.ToArray()); }

        // 第二样食材凑不成菜 → 不自动捡，要按住 G（抢食模式例外：食材就是分）
        public static bool NeedsHold(Fighter f, string id)
        {
            if (f == null || Match.Mode == GameMode.Cook || f.Food.ingredients.Count != 1) return false;
            return Ingredients.Recipe(f.Food.ingredients[0], id) == FoodForm.Dark;
        }

        // ---- ① 世界内气泡
        public static string IngredientLabel(string id, Fighter p, float dist, out Color c)
        {
            string nm = Ingredients.Name(id);
            c = Ingredients.Col(id) * .5f + new Color(.5f, .5f, .5f);
            if (dist > DetailRange) return nm;
            var parts = Partners(id);
            string form = parts.Count > 0 ? Ingredients.FormName(Ingredients.Recipe(id, parts[0])) : "";
            if (Match.Mode == GameMode.Cook) return nm + " · +1分 配" + Names(parts, "/") + "再+1";
            if (p != null && p.Food.ingredients.Count == 1)
            {
                string held = p.Food.ingredients[0]; var fm = Ingredients.Recipe(held, id);
                if (fm != FoodForm.Dark) { c = Ok; return "和你的" + Ingredients.Name(held) + " → " + Ingredients.FormName(fm) + "！"; }
                c = Grey; return "和你的" + Ingredients.Name(held) + "凑不成 → 黑暗料理";
            }
            return nm + " · 配" + Names(parts, "/") + "→" + form;
        }

        public static string SnackLabel(Snack s, Fighter p, out bool usable)
        {
            usable = p == null || s.Usable(p);
            if (p != null && p.alive && p.hp >= Config.MaxHp) { usable = false; return "满血吃不下"; }
            float heal = Snack.Heal * (p != null && p.role == Role.Falcon ? 2f : 1f);
            return "G 吃掉 · +" + heal + " 生命" + (Match.Mode == GameMode.Grow ? " +1 级" : "");
        }

        public static string StationName(KitchenStation.Kind k) { return k == KitchenStation.Kind.HotpotHeal ? "火锅" : k == KitchenStation.Kind.MilkTea ? "奶茶机" : k == KitchenStation.Kind.Oven ? "烤箱" : "冰柜"; }
        public static string StationLabel(KitchenStation k, Fighter p, out bool usable)
        {
            usable = true;
            if (p != null)
            {
                float cd = k.CooldownLeft(p);
                if (cd > 0) { usable = false; return StationName(k.kind) + " · 还要 " + Mathf.CeilToInt(cd) + " 秒"; }
                if (!k.Usable(p) && p.alive)
                {
                    usable = false;
                    switch (k.kind)
                    {
                        case KitchenStation.Kind.HotpotHeal: return "火锅 · 满血不用涮";
                        case KitchenStation.Kind.MilkTea: return "奶茶机 · 弹药是满的";
                        case KitchenStation.Kind.Oven: return "烤箱 · 枪已经烤好了";
                        default: return "冰柜 · 已有冰护甲";
                    }
                }
            }
            return k.kind == KitchenStation.Kind.HotpotHeal ? "G 涮火锅回血" : k.kind == KitchenStation.Kind.MilkTea ? "G 喝奶茶补弹药" : k.kind == KitchenStation.Kind.Oven ? "G 烤枪 · 伤害 +15%" : "G 拿冰护甲";
        }

        // ---- ③ HUD：差一样
        public static string HudHint(List<string> held)
        {
            if (held == null || held.Count != 1) return "";
            var parts = Partners(held[0]);
            if (parts.Count == 0) return "";
            return "差一样：" + Names(parts, " / ") + " → " + Ingredients.FormName(Ingredients.Recipe(held[0], parts[0]));
        }

        // ---- 大招 / 被动
        public static string UltEffect(Role r)
        {
            switch (r)
            {
                case Role.Falcon: return "向前冲撞 17 米，撞飞敌人（45 伤害）";
                case Role.Rivet: return "面前立起 3 块矮饭团墙（能翻越），15 秒";
                case Role.Ironwall: return "8 秒果冻罩：罩里的队友持续回血";
                case Role.Raven: return "全图敌人透视 5 秒";
                case Role.Ember: return "落点连续砸下 6 根生日蜡烛";
                default: return "落点 11 米内敌人 6 秒看不清、变慢、小地图失效";
            }
        }
        public const string UltCharge = "打出 520 伤害充满，另外每秒自然 +0.4%";

        // ---- ⑤ 开局横幅：按模式写（修掉非爆破模式也写"进攻 / 防守"）
        public static void ModeBanner(GameMode mode, bool att, bool carrier, out string title, out string sub)
        {
            switch (mode)
            {
                case GameMode.FFA: title = "美食大乱斗"; sub = "6 人各自为战 · 先到 " + Match.FFATarget + " 杀获胜"; return;
                case GameMode.Cook: title = "抢食大作战"; sub = "把食材送回自己队的大锅 · 先到 " + Match.CookTarget + " 分获胜"; return;
                case GameMode.Grow: title = "吃货变大"; sub = "吃零食变大 · 时间到时最大的人获胜"; return;
                case GameMode.Range: title = "靶场"; sub = "随便打 · F1 调参"; return;
            }
            title = att ? "进攻" : "防守";
            sub = att ? (carrier ? "你携带着棱能炸弹 · 前往 A 或 B 点安装" : "掩护队友在 A 或 B 点安装棱能炸弹") : "阻止安装 · 若已安装，在引爆前拆除";
        }

        // ---- 6 种食物反应（H 帮助页"地图美食"）
        public static readonly string[] Reactions = {
            "汽水罐手雷（4）炸曼妥思 → 可乐喷泉，把人冲上天", "奶油裱花（BR-5）打蛋糕墙 → 补墙，不是打穿", "番茄酱瓶（P-9）打黄油地面 → 更滑",
            "麻辣火锅形态打巧克力酱 → 热巧克力，站上去烫", "冰淇淋形态打火锅 → 热气散开", "饺子蒸笼（FS-12）打火锅 → 溅射烫伤旁边的敌人" };
    }

    // ================================================================ ② 首次提示卡：右侧中部小卡片，不挡准星、不暂停；每条每个存档只出一次
    public class FoodTips : MonoBehaviour
    {
        public static FoodTips I;
        public static float TipDuration = 5f, TipGap = 4f, CombatQuiet = 2f;   // 显示 5 s（4–6）、间隔 4 s（3–5）、交火后安静 2 s（1.5–2.5）
        public static int MaxPerMatch = 10;   // 每局最多 10 张（8–12）
        public static bool AutoEnabled = true;                                 // 走近设施 / 大招充满等自动触发（测试里关掉，避免抢队列）
        public static readonly List<string> Log = new List<string>();          // 本次运行看过的提示（H 页"提示记录"）
        static readonly HashSet<string> session = new HashSet<string>();
        readonly List<string> queue = new List<string>();
        public string current; float shownAt = -99, hiddenAt = -99; public int shown;
        static GUIStyle head, body; static Font font;

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        public static bool Seen(string id)
        {
            if (session.Contains(id)) return true;
            if (Collection.Locked) return false;               // 测试 / 自动对局：不读写存档，只记在内存里
            try { return PlayerPrefs.GetInt("neon_tip_" + id, 0) > 0; } catch { return false; }
        }
        static void MarkSeen(string id)
        {
            session.Add(id);
            if (Collection.Locked) return;
            try { PlayerPrefs.SetInt("neon_tip_" + id, 1); PlayerPrefs.Save(); } catch { }
        }
        public static void ResetSession() { session.Clear(); Log.Clear(); if (I != null) { I.queue.Clear(); I.current = null; I.shown = 0; I.hiddenAt = -99; } }
        public static bool Queued(string id) { return I != null && (I.queue.Contains(id) || I.current == id); }

        public static void Trigger(string id)
        {
            if (I == null || !GameSettings.Tips || !ToonAssets.Available || Match.I == null || Match.I.player == null) return;
            if (Seen(id) || Queued(id) || Body(id) == null) return;
            I.queue.Add(id);
        }

        static bool Quiet(Fighter p) { return Time.time - Mathf.Max(p.lastShotAt, p.lastDamageAt) >= CombatQuiet; }

        void Update()
        {
            var m = Match.I; if (m == null || !ToonAssets.Available) return;
            var p = m.player;
            if (current != null && Time.time - shownAt >= TipDuration) { current = null; hiddenAt = Time.time; }
            if (p == null) return;
            if (!GameSettings.Tips) { queue.Clear(); current = null; return; }
            AutoTriggers(m, p);
            if (current == null && queue.Count > 0 && shown < MaxPerMatch && Time.time - hiddenAt >= TipGap && Quiet(p))
            {
                current = queue[0]; queue.RemoveAt(0); shownAt = Time.time; shown++;
                MarkSeen(current); Log.Add(Title(current) + " · " + Body(current));
                Sfx.Ui("click", .3f, 1.3f);
            }
        }

        void AutoTriggers(Match m, Fighter p)
        {
            if (!AutoEnabled || !p.alive || m.phase != Phase.Live) return;
            Vector3 pos = p.transform.position;
            foreach (var it in Interactable.All)
                if (it is KitchenStation k && Vector3.Distance(k.transform.position, pos) < 4f) Trigger("station_" + k.kind);
            foreach (var dc in DeliveryCrate.Live) if (dc != null) { Trigger("delivery"); break; }
            if (p.Food.UltReady) Trigger("ult");
            if (p.skillCharges > 0) Trigger("skill");
            if (SauceZone.At(pos) != SauceZone.None && p.grounded) Trigger("sauce");
            if (Match.Mode == GameMode.Cook && CookPot.Pots[p.team] != null && Vector3.Distance(CookPot.Pots[p.team].transform.position, pos) < 7f) Trigger("cookpot");
        }

        public static string Title(string id)
        {
            if (id.StartsWith("dish_")) { var f = (FoodForm)System.Enum.Parse(typeof(FoodForm), id.Substring(5)); return "第一次做出「" + Ingredients.FormName(f) + "」"; }
            switch (id)
            {
                case "ingredient": return "第一次捡到食材";
                case "force_pick": return "这两样凑不成菜";
                case "station_HotpotHeal": return "火锅回血炉";
                case "station_MilkTea": return "奶茶机";
                case "station_Oven": return "烤箱";
                case "station_Freezer": return "冰柜";
                case "delivery": return "外卖来了";
                case "ult": return "大招充满了";
                case "skill": return "技能可以用了";
                case "sauce": return "踩到酱料了";
                case "cookpot": return "自己队的大锅";
                case "enemy_mustard": return "被芥末签闻到了";
                case "enemy_cupcake": return "点火纸杯蛋糕！";
                case "enemy_marsh": return "被棉花糖迷雾黏住了";
                case "link_self": { var lp = Match.I != null ? Match.I.player : null; return lp == null ? "地图联动" : "你的技能能和地图联动：" + Links.Name(Links.For(lp.role)); }   // V22 第三期
                case "enemy_pot": return "被芥末辣锅呛到了";
            }
            return id;
        }
        public static string Body(string id)
        {
            var p = Match.I != null ? Match.I.player : null;
            if (id.StartsWith("dish_"))
            {
                FoodForm f; try { f = (FoodForm)System.Enum.Parse(typeof(FoodForm), id.Substring(5)); } catch { return null; }
                return f == FoodForm.Dark ? "乱配会这样：自己慢慢掉血。下次看气泡，绿色才凑得成" : Ingredients.FormEffect(f) + "，持续 " + Ingredients.FormTime(f).ToString("0") + " 秒";
            }
            switch (id)
            {
                case "ingredient": return "再捡一样能凑成菜就会变身。配方看头顶气泡，或者按 H 看菜谱";
                case "force_pick": return "走过去不会自动捡。真想要黑暗料理，就按住 G 硬捡";
                case "station_HotpotHeal": return "按住 G 涮：每秒 +10 生命，最多 60，然后冷却 30 秒；热气会暴露你";
                case "station_MilkTea": return "按住 G 0.8 秒：补满弹药和手雷，冷却 40 秒";
                case "station_Oven": return "按住 G 5 秒把枪烤成金黄酥脆：伤害 +15%，冷却 20 秒";
                case "station_Freezer": return "按住 G 1.5 秒拿冰护甲：+50 护甲、稍微变慢，冷却 60 秒";
                case "delivery": return "看地上的落点圈，抢到：随机变身 + 补满弹药 + 金黄酥脆";
                case "ult": return p == null ? null : "按 X 放「" + FoodState.UltName(p.role) + "」：" + FoodGuide.UltEffect(p.role);
                case "skill": return p == null ? null : "F「" + p.Hero.skill.Name + "」：" + p.Hero.skill.HowTo;
                case "sauce": return "巧克力酱变慢、黄油打滑；番茄酱打黄油更滑，麻辣火锅打巧克力会烫";
                case "cookpot": return "送回来每样 +1 分，凑成一道菜再 +1；也能按住 G 偷对面的锅";
                case "enemy_mustard": return "对面夜鸦的芥末探签：一枪就能打掉签子，或躲到遮挡后";
                case "enemy_cupcake": return "对面焚芯扔的，0.9 秒后炸开：听到滋滋声马上撤出红圈";
                case "enemy_marsh": return "雾里会变慢、散布变大、小地图被糊住。走出迷雾或等它散掉";
                case "link_self": return p == null ? null : Links.TipBody(p.role);
                case "enemy_pot": return "对面夜鸦把芥末签射进了火锅：辣雾里一直透墙可见。离开火锅 " + Config.MustardPotRadius.ToString("0") + " 米，或用棉花糖迷雾 / 冰淇淋破掉";
            }
            return null;
        }

        void OnGUI()
        {
            var m = Match.I; if (m == null || current == null || !ToonAssets.Available) return;
            if (m.ui != null && !m.ui.Started && !m.autoplay && !m.testMode) return;
            if (font == null) font = Resources.Load<Font>("NotoSansSC");
            if (head == null)
            {
                head = new GUIStyle(GUI.skin.label) { font = font, fontSize = 18, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, .85f, .5f) } };
                body = new GUIStyle(GUI.skin.label) { font = font, fontSize = 14, wordWrap = true, normal = { textColor = Color.white } };
            }
            GUI.depth = -6;
            float s = Mathf.Clamp(Screen.height / 1080f, .7f, 1.6f); GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            float W = Screen.width / s, H = Screen.height / s;
            float age = Time.time - shownAt, a = Mathf.Clamp01(age / .25f) * Mathf.Clamp01((TipDuration - age) / .4f);
            float slide = (1 - Mathf.Clamp01(age / .25f)) * 40f;
            Rect r = new Rect(W - 396 + slide, H * .42f, 380, 98);
            var o = GUI.color;
            GUI.color = new Color(.2f, .06f, .16f, .88f * a); GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = new Color(1f, .6f, .8f, a); GUI.DrawTexture(new Rect(r.x, r.y, 5, r.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 3, r.width * Mathf.Clamp01(1 - age / TipDuration), 3), Texture2D.whiteTexture);
            GUI.color = new Color(1, 1, 1, a);
            GUI.Label(new Rect(r.x + 16, r.y + 6, r.width - 24, 28), "提示 · " + Title(current), head);
            GUI.Label(new Rect(r.x + 16, r.y + 34, r.width - 24, 60), Body(current) ?? "", body);
            GUI.color = o;
        }
    }
}
