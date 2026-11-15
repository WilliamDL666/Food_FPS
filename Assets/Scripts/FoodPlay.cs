using System.Collections.Generic;
using UnityEngine;

namespace NeonSiege
{
    // ================================================================ V18：美食大作战玩法
    public enum GameMode { Bomb, FFA, Cook, Grow, Range }
    public enum FoodForm { None, Cake, MilkTea, Spicy, Burger, IceCream, Onigiri, Dark }

    public static class Ingredients
    {
        public static readonly string[] All = { "Milk", "Tea", "Boba", "Flour", "Egg", "Chili", "HotpotBase", "Bread", "Cheese", "Ice", "Sugar", "Rice", "Nori" };
        public static string Name(string id)
        {
            switch (id)
            {
                case "Milk": return "牛奶"; case "Tea": return "茶叶"; case "Boba": return "珍珠"; case "Flour": return "面粉"; case "Egg": return "鸡蛋";
                case "Chili": return "辣椒"; case "HotpotBase": return "火锅底料"; case "Bread": return "面包"; case "Cheese": return "芝士"; case "Ice": return "冰块";
                case "Sugar": return "糖"; case "Rice": return "米饭"; default: return "海苔";
            }
        }
        public static Color Col(string id)
        {
            switch (id)
            {
                case "Milk": return new Color(.9f, .95f, 1f); case "Tea": return new Color(.4f, .8f, .4f); case "Boba": return new Color(.35f, .22f, .18f); case "Flour": return new Color(1f, .93f, .8f);
                case "Egg": return new Color(1f, .9f, .6f); case "Chili": return new Color(1f, .25f, .15f); case "HotpotBase": return new Color(.9f, .3f, .15f); case "Bread": return new Color(.9f, .6f, .3f);
                case "Cheese": return new Color(1f, .82f, .2f); case "Ice": return new Color(.7f, .9f, 1f); case "Sugar": return Color.white; case "Rice": return new Color(.95f, .95f, .9f); default: return new Color(.2f, .4f, .25f);
            }
        }
        static bool Pair(string a, string b, string x, string y) { return a == x && b == y || a == y && b == x; }
        public static FoodForm Recipe(string a, string b)
        {
            if (Pair(a, b, "Flour", "Egg")) return FoodForm.Cake;
            if (Pair(a, b, "Milk", "Tea") || Pair(a, b, "Milk", "Boba") || Pair(a, b, "Tea", "Boba")) return FoodForm.MilkTea;
            if (Pair(a, b, "Chili", "HotpotBase")) return FoodForm.Spicy;
            if (Pair(a, b, "Bread", "Cheese")) return FoodForm.Burger;
            if (Pair(a, b, "Ice", "Sugar")) return FoodForm.IceCream;
            if (Pair(a, b, "Rice", "Nori")) return FoodForm.Onigiri;
            return FoodForm.Dark;
        }
        public static readonly FoodForm[] Forms = { FoodForm.Cake, FoodForm.MilkTea, FoodForm.Spicy, FoodForm.Burger, FoodForm.IceCream, FoodForm.Onigiri, FoodForm.Dark };
        public static string FormName(FoodForm f)
        {
            switch (f)
            {
                case FoodForm.Cake: return "巨型蛋糕"; case FoodForm.MilkTea: return "奶茶杯"; case FoodForm.Spicy: return "麻辣火锅"; case FoodForm.Burger: return "汉堡";
                case FoodForm.IceCream: return "冰淇淋"; case FoodForm.Onigiri: return "饭团"; case FoodForm.Dark: return "黑暗料理"; default: return "";
            }
        }
        public static string FormRecipe(FoodForm f)
        {
            switch (f)
            {
                case FoodForm.Cake: return "面粉 + 鸡蛋"; case FoodForm.MilkTea: return "牛奶/茶叶/珍珠 任选两样"; case FoodForm.Spicy: return "辣椒 + 火锅底料"; case FoodForm.Burger: return "面包 + 芝士";
                case FoodForm.IceCream: return "冰块 + 糖"; case FoodForm.Onigiri: return "米饭 + 海苔"; default: return "乱配的任意两样";
            }
        }
        public static string FormEffect(FoodForm f)
        {
            switch (f)
            {
                case FoodForm.Cake: return "体型变大、+100 护甲、移动变慢，冲刺能撞飞敌人";
                case FoodForm.MilkTea: return "移动加速 30%，开火不耗弹药";
                case FoodForm.Spicy: return "子弹附带灼烧，身边热浪烫伤敌人";
                case FoodForm.Burger: return "面包盾：正面受到的伤害减少 60%";
                case FoodForm.IceCream: return "子弹让敌人减速，2 秒内连中 3 次冻住 1.2 秒";
                case FoodForm.Onigiri: return "冲刺时滚成饭团，撞到敌人造成伤害并弹飞";
                case FoodForm.Dark: return "冒绿烟、自己慢慢掉血，但子弹会让敌人看不清";
                default: return "";
            }
        }
        public static float FormTime(FoodForm f) { return f == FoodForm.MilkTea ? 10f : f == FoodForm.Dark ? 10f : 15f; }
        public static string FormModel(FoodForm f)
        {
            switch (f)
            {
                case FoodForm.Cake: return "Toon/Food/BigCake"; case FoodForm.MilkTea: return "Toon/Food/MilkTea"; case FoodForm.Spicy: return "Toon/Food/HotPot"; case FoodForm.Burger: return "Toon/Food/Burger";
                case FoodForm.IceCream: return "Toon/Food/IceCream"; case FoodForm.Onigiri: return "Toon/Hats/Hat_Onigiri"; default: return "Toon/Food/Ramen";
            }
        }
        public static float FormHatSize(FoodForm f) { return f == FoodForm.MilkTea || f == FoodForm.IceCream ? .75f : f == FoodForm.Cake ? .65f : .5f; }
        public static Color FormColor(FoodForm f)
        {
            switch (f)
            {
                case FoodForm.Cake: return new Color(1f, .6f, .8f); case FoodForm.MilkTea: return new Color(.85f, .62f, .42f); case FoodForm.Spicy: return new Color(1f, .3f, .15f); case FoodForm.Burger: return new Color(1f, .7f, .3f);
                case FoodForm.IceCream: return new Color(.6f, .9f, 1f); case FoodForm.Onigiri: return new Color(.95f, .95f, .9f); default: return new Color(.4f, .8f, .2f);
            }
        }
    }

    // ---------------------------------------------------------------- 收集：帽子解锁、菜谱图鉴（存在本机）
    public static class Collection
    {
        static int Get(string k) { try { return PlayerPrefs.GetInt("neon_" + k, 0); } catch { return 0; } }
        static void Set(string k, int v) { try { PlayerPrefs.SetInt("neon_" + k, v); PlayerPrefs.Save(); } catch { } }
        public static int Kills { get { return Get("kills"); } }
        public static int Transforms { get { return Get("transforms"); } }
        public static int Wins { get { return Get("wins"); } }
        public static void AddKill() { if (!Locked) Set("kills", Kills + 1); }
        public static void AddTransform(FoodForm f) { if (Locked) return; Set("transforms", Transforms + 1); Set("recipe_" + f, 1); }
        public static void AddWin() { if (!Locked) Set("wins", Wins + 1); }
        public static bool Discovered(FoodForm f) { return Get("recipe_" + f) > 0; }
        public static bool Locked { get { return Match.I != null && (Match.I.testMode || Match.I.autoplay); } }   // 自动测试不写存档
        public static readonly string[] SpecialHats = { "Takoyaki", "Xiaolongbao", "Pizza" };
        public static string HatName(string h) { return h == "Takoyaki" ? "章鱼烧" : h == "Xiaolongbao" ? "小笼包" : h == "Pizza" ? "披萨" : h; }
        public static string HatCondition(string h) { return h == "Takoyaki" ? "累计击杀 30 次" : h == "Xiaolongbao" ? "累计变身 10 次" : "赢下 1 场比赛"; }
        public static bool HatUnlocked(string h) { return h == "Takoyaki" ? Kills >= 30 : h == "Xiaolongbao" ? Transforms >= 10 : Wins >= 1; }
        public static string EquippedHat { get { try { return PlayerPrefs.GetString("neon_hat", ""); } catch { return ""; } } set { try { PlayerPrefs.SetString("neon_hat", value); PlayerPrefs.Save(); } catch { } } }
    }

    // ---------------------------------------------------------------- 每个角色身上的美食状态
    public class FoodState : MonoBehaviour
    {
        public Fighter f;
        public readonly List<string> ingredients = new List<string>();
        public FoodForm form; public float formUntil;
        public float ult;                         // 0..1
        public bool crispy; public int growLevel;
        public string emote; public float emoteUntil;
        public int transforms, darkDishes, snacks, potHealed, steals, delivered;
        GameObject formHat; ParticleSystem aura;
        float burnUntil, burnTick, auraTick, darkTick, ramUntil; Fighter burnFrom; int iceHits; float iceHitAt;
        readonly HashSet<Fighter> rammed = new HashSet<Fighter>();
        public bool CanCarry { get { return ingredients.Count < 2 && f.alive; } }
        public bool Transformed { get { return form != FoodForm.None && Time.time < formUntil; } }
        public float FormLeft { get { return Mathf.Max(0, formUntil - Time.time); } }
        public bool UltReady { get { return ult >= 1f; } }

        public void ResetMatch() { transforms = darkDishes = snacks = potHealed = steals = delivered = 0; ult = 0; growLevel = 0; }

        public void Pick(string id)
        {
            ingredients.Add(id);
            Sfx.Play("food_popcorn", f.transform.position, .7f, 1.3f, f.human ? 0f : 1f, 18f);
            if (f.human) Match.I.Feed("拿到 " + Ingredients.Name(id) + (ingredients.Count == 1 && Match.Mode != GameMode.Cook ? " · " + FoodGuide.HudHint(ingredients) : ""), Ingredients.Col(id));
            if (f.human) FoodTips.Trigger("ingredient");
            if (Match.Mode == GameMode.Cook) return;   // 抢食大作战：食材是用来交的，不变身
            if (ingredients.Count == 2)
            {
                var fm = Ingredients.Recipe(ingredients[0], ingredients[1]);
                ingredients.Clear();
                Become(fm);
            }
        }

        public void Become(FoodForm fm)
        {
            EndForm(false);
            form = fm; formUntil = Time.time + Ingredients.FormTime(fm); transforms++; if (fm == FoodForm.Dark) darkDishes++;
            if (f.human) { Collection.AddTransform(fm); FoodTips.Trigger("dish_" + fm); }
            if (fm == FoodForm.Cake) f.armor = Config.MaxArmor + 100;
            iceHits = 0; rammed.Clear();
            var col = Ingredients.FormColor(fm);
            FoodFx.KillPop(f.Chest);
            Sfx.Play(fm == FoodForm.Dark ? "food_squish" : "food_boing", f.transform.position, 1f, fm == FoodForm.Dark ? .6f : 1.1f, f.human ? 0f : 1f, 35f);
            Match.I.Feed((f.human ? "你" : f.Hero.name) + " 变身成了「" + Ingredients.FormName(fm) + "」！" + (f.human ? "  " + Ingredients.FormEffect(fm) : ""), col);
            // 头顶换上对应的大菜
            if (f.avatar != null) formHat = ToonAssets.AttachAbove(f.avatar.gameObject, Ingredients.FormModel(fm), Ingredients.FormHatSize(fm), -.05f);
            if (f.avatar != null && fm == FoodForm.Cake) f.avatar.Body.localScale = Vector3.one * (1.3f + .08f * growLevel);
            var go = new GameObject("变身光环"); go.transform.SetParent(f.transform, false); go.transform.localPosition = Vector3.up * .9f;
            aura = go.AddComponent<ParticleSystem>();
            var main = aura.main; main.startLifetime = .9f; main.startSpeed = .8f; main.startSize = new ParticleSystem.MinMaxCurve(.12f, .3f); main.startColor = new Color(col.r, col.g, col.b, .8f); main.maxParticles = 60; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = aura.emission; em.rateOverTime = fm == FoodForm.Dark ? 30 : 14;
            var sh = aura.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = .6f;
            go.GetComponent<ParticleSystemRenderer>().material = fm == FoodForm.Dark ? (Resources.Load<Material>("Materials/FxSmoke") ?? Fx.Additive) : Fx.Additive;
        }

        public void EndForm(bool announce)
        {
            if (form == FoodForm.None) return;
            if (form == FoodForm.Cake && f.armor > Config.MaxArmor) f.armor = Config.MaxArmor;
            if (f.avatar != null) f.avatar.Body.localScale = Vector3.one * (1f + .08f * growLevel);
            if (formHat != null) Destroy(formHat);
            if (aura != null) Destroy(aura.gameObject);
            if (announce && f.human && f.alive) Match.I.Feed("变身结束", new Color(.8f, .85f, .9f));
            form = FoodForm.None;
        }

        // ---- 移动 / 伤害修正（Fighter 调用）
        public float SpeedMul()
        {
            float k = 1f;
            if (Transformed)
            {
                if (form == FoodForm.Cake) k *= .85f;
                else if (form == FoodForm.MilkTea) k *= 1.3f;
                else if (form == FoodForm.Onigiri && f.sprinting) k *= 1.6f;
            }
            if (f.armor > Config.MaxArmor && form != FoodForm.Cake) k *= .95f;   // 冰护甲
            if (Match.Mode == GameMode.Grow) k *= 1f - .025f * growLevel;
            return k;
        }
        public bool InfiniteAmmo { get { return Transformed && form == FoodForm.MilkTea; } }
        public float DamageMul() { return crispy ? 1.15f : 1f; }

        // 被打之前：返回 false = 这一发被挡掉
        public static bool Incoming(Fighter victim, Fighter attacker, Vector3 dir, ref float dmg)
        {
            if (!ToonAssets.Available) return true;
            var vs = victim.Food;
            if (victim.role == Role.Phantom && Random.value < .15f)   // 幻频被动：甩出糖粒挡掉一发
            {
                Fx.Burst(victim.Chest, -dir, FoodFx.Confetti[Random.Range(0, 6)], 8, 3f, .05f, .4f, .8f);
                Sfx.Play("food_popcorn", victim.Chest, .6f, 1.5f, 1f, 20f);
                return false;
            }
            if (vs.Transformed && vs.form == FoodForm.Burger && Vector3.Angle(victim.transform.forward, -dir) < 70f) dmg *= .4f;
            if (attacker != null) dmg *= attacker.Food.DamageMul();
            return true;
        }

        // 打中之后：灼烧、冰冻、看不清、标记、大招充能
        public static void OnHit(Fighter attacker, Fighter victim, float dealt)
        {
            if (!ToonAssets.Available || attacker == null) return;
            var a = attacker.Food;
            a.ult = Mathf.Min(1f, a.ult + dealt / 520f);
            if (attacker.role == Role.Raven) victim.revealedUntil = Mathf.Max(victim.revealedUntil, Time.time + 3f);   // 夜鸦被动：芥末标记
            if (!a.Transformed) return;
            var v = victim.Food;
            switch (a.form)
            {
                case FoodForm.Spicy: v.burnUntil = Time.time + 2.5f; v.burnFrom = attacker; break;
                case FoodForm.IceCream:
                    victim.creamUntil = Time.time + 1.2f;
                    if (Time.time - v.iceHitAt > 2f) v.iceHits = 0;
                    v.iceHits++; v.iceHitAt = Time.time;
                    if (v.iceHits >= 3) { v.iceHits = 0; victim.Freeze(1.2f); Fx.Burst(victim.Chest, Vector3.up, new Color(.7f, .95f, 1f), 30, 3f, .1f, .8f, .2f); Sfx.Play("impactGlass_heavy", victim.Chest, .6f, 1.4f, 1f, 25f); }
                    break;
                case FoodForm.Dark: victim.jammedUntil = Mathf.Max(victim.jammedUntil, Time.time + 1.5f); break;
            }
        }

        public void OnDeath()
        {
            for (int i = 0; i < ingredients.Count; i++) IngredientPickup.Spawn(ingredients[i], f.transform.position + new Vector3(i * .6f - .3f, 0, 0), null, 25f);
            ingredients.Clear(); EndForm(false); crispy = false; burnUntil = 0;
            if (Match.Mode == GameMode.Grow) growLevel = growLevel / 2;
        }
        public void OnRespawn() { ingredients.Clear(); EndForm(false); crispy = false; burnUntil = 0; emoteUntil = 0; ApplyGrowScale(); }
        public void ApplyGrowScale() { if (f.avatar != null && f.avatar.Body != null) f.avatar.Body.localScale = Vector3.one * (1f + .08f * growLevel); }
        public void Grow(int levels)
        {
            if (Match.Mode != GameMode.Grow) return;
            growLevel = Mathf.Clamp(growLevel + levels, 0, 12);
            f.armor = Mathf.Min(Config.MaxArmor + 15 * growLevel, f.armor + 15 * levels);
            ApplyGrowScale();
            if (f.human) Match.I.Feed("变大了！现在 " + growLevel + " 级", new Color(1f, .8f, .4f));
        }

        public void Emote(string clip, float dur = 2.2f) { emote = clip; emoteUntil = Time.time + dur; }
        public string EmoteClip { get { return Time.time < emoteUntil ? emote : null; } }
        public static string EmoteLabel(string clip) { return clip == "Wave" ? "挥手~" : clip == "Yes" ? "点头！" : clip == "No" ? "摇头…" : ""; }

        void Update()
        {
            if (f == null || Match.I == null) return;
            if (form != FoodForm.None && Time.time >= formUntil) EndForm(true);
            if (!f.alive || Match.I.phase != Phase.Live) return;
            ult = Mathf.Min(1f, ult + Time.deltaTime * .004f);
            // 灼烧
            if (Time.time < burnUntil && Time.time >= burnTick) { burnTick = Time.time + .5f; DamageCtx.Set(DamageKind.Fire, "辣椒灼烧"); f.ReceiveDamage(4f, burnFrom, f.transform.position, false); Fx.Burst(f.Chest, Vector3.up, new Color(1f, .4f, .1f), 5, 1.5f, .07f, .4f, -.3f); }
            // 热巧克力地面
            if (Time.time >= burnTick && SauceZone.HotAt(f.transform.position) && f.grounded) { burnTick = Time.time + .5f; DamageCtx.Set(DamageKind.Env, "热巧克力地面"); f.ReceiveDamage(3f, null, f.transform.position, false); }
            if (!Transformed) return;
            if (form == FoodForm.Spicy && Time.time >= auraTick)
            {
                auraTick = Time.time + .5f;
                foreach (var e in Match.I.fighters) if (e != f && e.alive && e.team != f.team && Vector3.Distance(e.transform.position, f.transform.position) < 3.2f) { DamageCtx.Set(DamageKind.Food, "辣椒光环"); e.ReceiveDamage(3f, f, f.transform.position, false); }
            }
            if (form == FoodForm.Dark && Time.time >= darkTick) { darkTick = Time.time + 1f; DamageCtx.Set(DamageKind.Env, "黑暗料理"); f.ReceiveDamage(3f, null, f.transform.position, false); }
            bool ramming = Time.time < ramUntil || form == FoodForm.Cake && f.sprinting || form == FoodForm.Onigiri && f.sprinting;
            if (ramming)
                foreach (var e in Match.I.fighters)
                {
                    if (e == f || !e.alive || e.team == f.team || rammed.Contains(e)) continue;
                    if (Vector3.Distance(e.transform.position, f.transform.position) > 1.6f) continue;
                    rammed.Add(e); StartCoroutine(Unram(e));
                    Vector3 away = e.transform.position - f.transform.position; away.y = 0;
                    e.Launch(away.normalized * 9f + Vector3.up * 6f);
                    if (Time.time < ramUntil) DamageCtx.Set(DamageKind.Ult, UltName(f.role)); else DamageCtx.Set(DamageKind.Food, Ingredients.FormName(form) + "冲撞");
                    e.ReceiveDamage(Time.time < ramUntil ? 45f : 25f, f, f.transform.position, false);
                    Sfx.Play("food_boing", e.Chest, 1f, .8f, 1f, 30f); Fx.Burst(e.Chest, Vector3.up, Ingredients.FormColor(form), 18, 4f, .08f, .6f, .6f);
                }
        }
        System.Collections.IEnumerator Unram(Fighter e) { yield return new WaitForSeconds(1.2f); rammed.Remove(e); }

        // ---- 大招（X）
        public static string UltName(Role r)
        {
            switch (r) { case Role.Falcon: return "双层汉堡冲撞"; case Role.Rivet: return "饭团城墙"; case Role.Ironwall: return "果冻罩"; case Role.Raven: return "回转寿司雷达"; case Role.Ember: return "生日蜡烛轰炸"; default: return "糖霜风暴"; }
        }
        public static string Passive(Role r)
        {
            switch (r) { case Role.Falcon: return "吃零食回血翻倍"; case Role.Rivet: return "部署的掩体更耐打（+30%）"; case Role.Ironwall: return "受到爆炸伤害减半"; case Role.Raven: return "打中的敌人被芥末标记 3 秒"; case Role.Ember: return "手雷爆炸范围 +20%"; default: return "被打时 15% 概率甩出糖粒挡掉一发"; }
        }
        public bool TryUlt()
        {
            if (!UltReady || !f.alive || Match.I.phase != Phase.Live) return false;
            ult = 0;
            Vector3 aim = f.Eye + f.AimDir * 40f;
            if (Physics.Raycast(f.Eye, f.AimDir, out RaycastHit hit, 45f, ~0, QueryTriggerInteraction.Ignore)) aim = hit.point;
            Vector3 ground = Skills.GroundAt(aim);
            Match.I.Feed((f.human ? "你" : f.Hero.name) + " 放出大招「" + UltName(f.role) + "」！", f.Hero.tint);
            Sfx.Play("food_confetti", f.transform.position, 1f, .8f, f.human ? 0f : 1f, 60f);
            switch (f.role)
            {
                case Role.Falcon:
                    ramUntil = Time.time + .9f; rammed.Clear();
                    f.Launch(Quaternion.Euler(0, f.yaw, 0) * Vector3.forward * 17f + Vector3.up * 3f); break;
                case Role.Rivet:
                {
                    Vector3 side = Quaternion.Euler(0, f.yaw, 0) * Vector3.right;
                    Vector3 c = Skills.GroundAt(f.transform.position + Quaternion.Euler(0, f.yaw, 0) * Vector3.forward * 3f);
                    for (int i = -1; i <= 1; i++) RiceWall.Spawn(c + side * i * 2.5f, f.yaw, f);
                    break;
                }
                case Role.Ironwall: JellyDome.Spawn(f.transform.position, f); break;
                case Role.Raven:
                    foreach (var e in Match.I.fighters) if (e.team != f.team && e.alive) e.revealedUntil = Time.time + 5f;
                    break;
                case Role.Ember: CandleBarrage.Spawn(ground, f); break;
                default:
                    foreach (var e in Match.I.fighters) if (e.team != f.team && e.alive && Vector3.Distance(e.transform.position, ground) < 11f) e.jammedUntil = Time.time + 6f;
                    for (int i = 0; i < 6; i++) Fx.Burst(ground + Vector3.up * 1.5f, Vector3.up, FoodFx.Confetti[i], 40, 8f, .1f, 2f, .2f);
                    break;
            }
            UltWarn.OnUlt(f, ground);   // V22 第三期：敌方大招预警（横幅 + 全图专属音 + 状态条 + 蜡烛落点圈），生效时间不变
            return true;
        }
    }

    // ---------------------------------------------------------------- 食材拾取物
    public class IngredientPickup : MonoBehaviour
    {
        public static readonly List<IngredientPickup> Live = new List<IngredientPickup>();
        public string id; public IngredientSpot spot; float born, life;
        // V21 规则甲：手上已有一样、这一样又凑不成菜时，不自动捡，要按住 G（FoodGuide.HoldToPick 秒）
        public Fighter holder; public float holdT;
        public float HoldProgress(Fighter f) { return holder == f ? Mathf.Clamp01(holdT / FoodGuide.HoldToPick) : 0f; }
        public static IngredientPickup HoldTarget(Fighter f)
        {
            if (f == null || !f.alive || !f.Food.CanCarry) return null;
            foreach (var p in Live) if (p != null && Vector3.Distance(f.transform.position, p.transform.position) <= 1.3f && FoodGuide.NeedsHold(f, p.id)) return p;
            return null;
        }
        public static IngredientPickup Spawn(string id, Vector3 at, IngredientSpot spot, float life = 0)
        {
            var go = new GameObject("食材 " + Ingredients.Name(id)); go.transform.position = Skills.GroundAt(at) + Vector3.up * .05f;
            var p = go.AddComponent<IngredientPickup>(); p.id = id; p.spot = spot; p.born = Time.time; p.life = life;
            var holder = new GameObject("模型").transform; holder.SetParent(go.transform, false); holder.localPosition = Vector3.up * .45f;
            var m = Arena.Model("Toon/Kitchen/" + id);
            if (m != null) { foreach (var c in m.GetComponentsInChildren<Collider>()) Destroy(c); Arena.Fit(m, new Vector3(0, .42f, 0)); m.transform.SetParent(holder, false); }
            var sp = holder.gameObject.AddComponent<Spin>(); sp.speed = new Vector3(0, 90, 0); sp.bob = .08f;
            var l = new GameObject("光").AddComponent<Light>(); l.transform.SetParent(go.transform, false); l.transform.localPosition = Vector3.up * .8f; l.color = Ingredients.Col(id); l.range = 2.5f; l.intensity = 1.5f;
            Live.Add(p); return p;
        }
        void OnDestroy() { Live.Remove(this); }
        void Update()
        {
            if (life > 0 && Time.time - born > life) { Destroy(gameObject); return; }
            if (Match.I == null || Time.time - born < .5f) return;
            foreach (var f in Match.I.fighters)
            {
                if (!f.alive || !f.Food.CanCarry || Vector3.Distance(f.transform.position, transform.position) > 1.3f) { if (holder == f) { holder = null; holdT = 0; } continue; }
                if (FoodGuide.NeedsHold(f, id))
                {
                    if (f.human) FoodTips.Trigger("force_pick");
                    if (!f.UseHeld) { if (holder == f) { holder = null; holdT = 0; } continue; }
                    if (holder != f) { holder = f; holdT = 0; }
                    holdT += Time.deltaTime;
                    if (holdT < FoodGuide.HoldToPick) continue;
                }
                f.Food.Pick(id);
                if (spot != null) spot.Taken();
                Destroy(gameObject); return;
            }
        }
    }

    public class Billboard : MonoBehaviour
    {
        void LateUpdate() { var c = Camera.main; if (c != null) transform.rotation = Quaternion.LookRotation(transform.position - c.transform.position); }
    }

    public class IngredientSpot : MonoBehaviour
    {
        public static readonly List<IngredientSpot> All = new List<IngredientSpot>();
        IngredientPickup current; float respawnAt;
        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }
        float Delay { get { return Match.Mode == GameMode.Cook ? 8f : 16f; } }
        public void Taken() { current = null; respawnAt = Time.time + Delay; }
        public void ResetRound() { if (current != null) Destroy(current.gameObject); current = null; respawnAt = Time.time + Random.Range(0f, 4f); }
        void Update()
        {
            if (Match.I == null || current != null || Time.time < respawnAt) return;
            if (Match.I.phase == Phase.MatchEnd) return;
            current = IngredientPickup.Spawn(Ingredients.All[Random.Range(0, Ingredients.All.Length)], transform.position, this);
        }
    }

    // ---------------------------------------------------------------- 厨房设施
    public class KitchenStation : Interactable
    {
        public enum Kind { HotpotHeal, MilkTea, Oven, Freezer }
        public Kind kind;
        readonly Dictionary<Fighter, float> healed = new Dictionary<Fighter, float>(), cooldown = new Dictionary<Fighter, float>();
        AudioSource bubble;
        public void Build(Kind k)
        {
            kind = k; range = 2.1f;
            holdTime = k == Kind.HotpotHeal ? .25f : k == Kind.MilkTea ? .8f : k == Kind.Oven ? 5f : 1.5f;
            string model = k == Kind.HotpotHeal ? "HotpotStation" : k == Kind.MilkTea ? "MilkTeaMachine" : k == Kind.Oven ? "Oven" : "Freezer";
            var m = Arena.Model("Toon/Kitchen/" + model);
            if (m != null) { foreach (var c in m.GetComponentsInChildren<Collider>()) Arena.Kill(c); Arena.Fit(m, new Vector3(0, k == Kind.HotpotHeal ? 1.15f : k == Kind.Freezer ? 1.45f : 1.25f, 0)); m.transform.SetParent(transform, false); }
            if (k == Kind.HotpotHeal)
            {
                var clip = Resources.Load<AudioClip>("Music/hotpot_bubble");
                if (clip != null) { bubble = gameObject.AddComponent<AudioSource>(); bubble.clip = clip; bubble.loop = true; bubble.spatialBlend = 1; bubble.maxDistance = 12; bubble.rolloffMode = AudioRolloffMode.Linear; bubble.volume = .5f; bubble.Play(); }
                var steam = new GameObject("热气"); steam.transform.SetParent(transform, false); steam.transform.localPosition = Vector3.up * 1.2f;
                var ps = steam.AddComponent<ParticleSystem>(); var main = ps.main; main.startLifetime = 2f; main.startSpeed = .7f; main.startSize = new ParticleSystem.MinMaxCurve(.4f, .9f); main.startColor = new Color(1, 1, 1, .25f); main.maxParticles = 40;
                var em = ps.emission; em.rateOverTime = 8; var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = .3f; sh.rotation = new Vector3(-90, 0, 0);
                steam.GetComponent<ParticleSystemRenderer>().material = Resources.Load<Material>("Materials/FxSmoke") ?? Fx.Additive;
            }
        }
        float Cooldown(Fighter f) { return cooldown.TryGetValue(f, out float t) ? t - Time.time : 0; }
        public float CooldownLeft(Fighter f) { return Mathf.Max(0, Cooldown(f)); }   // V21：气泡显示"还要 N 秒"
        public void SetCooldownForTest(Fighter f, float secs) { cooldown[f] = Time.time + secs; }
        public override bool Usable(Fighter f)
        {
            if (!f.alive || Cooldown(f) > 0) return false;
            switch (kind)
            {
                case Kind.HotpotHeal: return f.hp < Config.MaxHp && (!healed.TryGetValue(f, out float h) || h < 60f);
                case Kind.MilkTea: return f.ammo < f.W.magazine || f.grenades < 1;
                case Kind.Oven: return !f.Food.crispy;
                default: return f.armor <= Config.MaxArmor;
            }
        }
        public override string Prompt(Fighter f)
        {
            switch (kind)
            {
                case Kind.HotpotHeal: return "按住 G 涮一涮（每秒 +10 生命，最多 60；热气会暴露你）";
                case Kind.MilkTea: return "按住 G 喝奶茶（补满弹药和手雷）";
                case Kind.Oven: return "按住 G 把枪烤 5 秒（金黄酥脆版：伤害 +15%）";
                default: return "按住 G 拿冰护甲（+50 护甲，稍微变慢）";
            }
        }
        protected override void OnStart(Fighter f) { if (kind != Kind.HotpotHeal) Sfx.Play("food_popcorn", transform.position, .5f, .8f, 1f, 15f); }
        public override Vector3 Anchor { get { return transform.position + Vector3.up; } }
        protected override void Complete(Fighter f)
        {
            switch (kind)
            {
                case Kind.HotpotHeal:
                {
                    float got = healed.TryGetValue(f, out float h) ? h : 0; float add = Mathf.Min(2.5f, 60f - got, Config.MaxHp - f.hp);
                    f.hp += add; healed[f] = got + add; f.Food.potHealed += Mathf.RoundToInt(add);
                    f.revealedUntil = Mathf.Max(f.revealedUntil, Time.time + .6f);   // 热气暴露位置
                    if (Random.value < .3f) Fx.Burst(f.Chest, Vector3.up, new Color(1f, .5f, .3f), 4, 1.5f, .06f, .5f, -.3f);
                    if (got + add >= 60f) cooldown[f] = Time.time + 30f;
                    break;
                }
                case Kind.MilkTea:
                    f.ammo = f.W.magazine; f.grenades = Mathf.Max(f.grenades, 1); cooldown[f] = Time.time + 40f;
                    Sfx.Play("food_boba", transform.position, .8f, .8f, 1f, 15f); Fx.Burst(f.Chest, Vector3.up, new Color(.85f, .62f, .42f), 14, 2f, .06f, .5f, .3f); break;
                case Kind.Oven:
                    f.Food.crispy = true; cooldown[f] = Time.time + 20f;
                    Sfx.Play("food_confetti", transform.position, .7f, 1.2f, 1f, 20f); Fx.Burst(transform.position + Vector3.up, Vector3.up, new Color(1f, .75f, .25f), 30, 3f, .08f, .8f, .2f);
                    if (f.human) Match.I.Feed("枪烤好了：金黄酥脆版，伤害 +15%（阵亡前有效）", new Color(1f, .75f, .25f)); break;
                default:
                    f.armor = Config.MaxArmor + 50; cooldown[f] = Time.time + 60f;
                    Sfx.Play("impactGlass_heavy", transform.position, .5f, 1.5f, 1f, 15f); Fx.Burst(f.Chest, Vector3.up, new Color(.7f, .95f, 1f), 20, 2f, .08f, .6f, .2f); break;
            }
        }
        public override void ResetRound() { base.ResetRound(); healed.Clear(); cooldown.Clear(); }
    }

    // 外卖无人机：每隔一段时间空投一个外卖箱
    public class Airdrop : MonoBehaviour
    {
        public static readonly Vector3[] Spots = {
            new Vector3(-30, 0, 39), new Vector3(1, 0, 41), new Vector3(-20, 0, 4.5f), new Vector3(10, 0, 4), new Vector3(20, 0, 9), new Vector3(-30, 0, -40),
            new Vector3(22, 0, -41), new Vector3(-21, 0, 20), new Vector3(-33, 0, -18), new Vector3(37, 0, 34), new Vector3(37, 0, -34), new Vector3(16, 0, -2) };
        float next;
        void Update()
        {
            var m = Match.I; if (m == null || !ToonAssets.Available) return;
            if (m.phase != Phase.Live || Match.Mode == GameMode.Range) { next = Time.time + (Match.Mode == GameMode.Bomb ? 40f : 45f); return; }
            if (Time.time < next) return;
            next = Time.time + (Match.Mode == GameMode.Bomb ? 999f : 45f);   // 爆破模式每回合一次
            DeliveryCrate.Drop(Spots[Random.Range(0, Spots.Length)]);
        }
    }

    public class DeliveryCrate : MonoBehaviour
    {
        public static readonly List<DeliveryCrate> Live = new List<DeliveryCrate>();
        Vector3 target; bool landed; float landedAt; Transform drone;
        public bool Landed { get { return landed; } }
        public Vector3 Target { get { return target; } }   // V21：下落时地上显示落点
        public static DeliveryCrate Drop(Vector3 at)
        {
            var go = new GameObject("外卖箱"); var c = go.AddComponent<DeliveryCrate>(); c.target = Skills.GroundAt(at);
            go.transform.position = c.target + Vector3.up * 26f;
            var m = Arena.Model("Toon/Kitchen/Crate"); if (m != null) { foreach (var cl in m.GetComponentsInChildren<Collider>()) Destroy(cl); Arena.Fit(m, new Vector3(0, .7f, 0)); m.transform.SetParent(go.transform, false); }
            c.drone = new GameObject("无人机").transform; c.drone.SetParent(go.transform, false); c.drone.localPosition = Vector3.up * 1.6f;
            for (int i = 0; i < 4; i++)
            {
                var r = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(r.GetComponent<Collider>()); r.transform.SetParent(c.drone, false);
                r.transform.localPosition = new Vector3(i % 2 == 0 ? .5f : -.5f, 0, i < 2 ? .5f : -.5f); r.transform.localScale = new Vector3(.45f, .015f, .45f);
                r.GetComponent<Renderer>().sharedMaterial = FoodFx.TrailMaterial;
            }
            var l = new GameObject("信号灯").AddComponent<Light>(); l.transform.SetParent(go.transform, false); l.transform.localPosition = Vector3.up; l.color = new Color(1f, .6f, .8f); l.range = 6; l.intensity = 3;
            Live.Add(c);
            Match.I.Feed("外卖到了！无人机正在空投一个外卖箱 · 抢到：随机变身 + 补满弹药 + 酥脆", new Color(1f, .7f, .85f));
            Sfx.Ui("maximize", .5f, 1.4f);
            return c;
        }
        void OnDestroy() { Live.Remove(this); }
        void Update()
        {
            if (!landed)
            {
                transform.position = Vector3.MoveTowards(transform.position, target, Time.deltaTime * 6f);
                if (drone != null) drone.Rotate(0, 900 * Time.deltaTime, 0);
                if (Vector3.Distance(transform.position, target) < .05f) { landed = true; landedAt = Time.time; Sfx.Play("impactSoft_heavy", target, 1f, .8f, 1f, 30f); if (drone != null) Destroy(drone.gameObject); }
                return;
            }
            if (Time.time - landedAt > 40f || Match.I == null) { Destroy(gameObject); return; }
            foreach (var f in Match.I.fighters)
            {
                if (!f.alive || Vector3.Distance(f.transform.position, transform.position) > 1.5f) continue;
                Open(f); Destroy(gameObject); return;
            }
        }
        void Open(Fighter f)
        {
            FoodFx.KillPop(transform.position + Vector3.up * .5f);
            f.ammo = f.W.magazine; f.Food.crispy = true;
            if (Match.Mode == GameMode.Cook) { f.Food.ingredients.Clear(); f.Food.ingredients.Add(Ingredients.All[Random.Range(0, Ingredients.All.Length)]); f.Food.ingredients.Add(Ingredients.All[Random.Range(0, Ingredients.All.Length)]); }
            else f.Food.Become(Ingredients.Forms[Random.Range(0, 6)]);
            Match.I.Feed((f.human ? "你" : f.Hero.name) + " 抢到了外卖箱！", new Color(1f, .7f, .85f));
        }
    }

    // 传菜转盘：站上去会被带着转
    public class Turntable : MonoBehaviour
    {
        public float radius = 2.25f, height = .25f, speed = 22f; Transform vis;
        public void Build()
        {
            var m = Arena.Model("Toon/Kitchen/Turntable");
            if (m != null) { foreach (var c in m.GetComponentsInChildren<Collider>()) Arena.Kill(c); Arena.Fit(m, new Vector3(radius * 2, 0, radius * 2)); vis = new GameObject("转盘").transform; vis.SetParent(transform, false); m.transform.SetParent(vis, false); }
        }
        void Update()
        {
            float dA = speed * Time.deltaTime;
            if (vis != null) vis.Rotate(0, dA, 0);
            if (Match.I == null) return;
            foreach (var f in Match.I.fighters)
            {
                if (!f.alive || !f.grounded) continue;
                Vector3 d = f.transform.position - transform.position;
                if (Mathf.Abs(d.y - height) > .35f || new Vector2(d.x, d.z).magnitude > radius) continue;
                Vector3 nd = Quaternion.Euler(0, dA, 0) * new Vector3(d.x, 0, d.z);
                f.cc.Move(new Vector3(nd.x - d.x, 0, nd.z - d.z));
                f.yaw += dA;
            }
        }
    }

    // 汽水手雷 + 曼妥思 = 喷泉
    public class MentosPile : MonoBehaviour
    {
        public static readonly List<MentosPile> All = new List<MentosPile>();
        float readyAt;
        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }
        public static void TryFountain(Vector3 blast, Fighter owner)
        {
            foreach (var p in All)
            {
                if (Time.time < p.readyAt || Vector3.Distance(p.transform.position, blast) > 4.5f) continue;
                p.readyAt = Time.time + 15f;
                Vector3 c = p.transform.position;
                for (int i = 0; i < 3; i++) Fx.Burst(c + Vector3.up * 1.5f, Vector3.up, new Color(.45f, .22f, .1f), 60, 16f, .15f, 1.6f, .7f);
                Fx.Burst(c + Vector3.up * 1.5f, Vector3.up, Color.white, 40, 12f, .25f, 1.4f, .3f, Fx.Smoke);
                Sfx.Play("food_fizz", c, 1f, .7f, 1f, 80f); Sfx.Play("food_boing", c, 1f, .6f, 1f, 60f);
                if (Match.I != null)
                    foreach (var f in Match.I.fighters)
                    {
                        if (!f.alive) continue;
                        Vector3 d = f.transform.position - c; d.y = 0;
                        if (d.magnitude > 5.5f) continue;
                        f.Launch(Vector3.up * 15f + d.normalized * 4f);
                    }
                Match.I.Feed("曼妥思 + 汽水 = 可乐喷泉！", new Color(.9f, .7f, .5f));
            }
        }
    }

    // 食物和食物的反应（子弹打到场景时调用）
    public static class FoodReact
    {
        static float splashReady;
        public static bool IsHotpot(Collider c)
        {
            var t = c.transform; for (int i = 0; i < 3 && t != null; i++, t = t.parent) if (t.name.Contains("HotPot")) return true;
            return false;
        }
        public static void OnImpact(Fighter attacker, RaycastHit h, WeaponSpec w)
        {
            if (!ToonAssets.Available || attacker == null) return;
            var fs = attacker.Food;
            if (IsHotpot(h.collider))
            {
                if (fs.Transformed && fs.form == FoodForm.IceCream && SteamCloud.Suppress(8f))
                    Match.I.Feed("冰淇淋把火锅降温了，热气散开！", new Color(.7f, .9f, 1f));
                if (w.id == "FS-12" && Time.time >= splashReady)
                {
                    splashReady = Time.time + .4f;
                    Vector3 c = h.collider.transform.position;
                    Fx.Burst(h.point, Vector3.up, new Color(.9f, .2f, .1f), 20, 5f, .08f, .6f, 1.2f);
                    Sfx.Play("food_splat", h.point, .9f, .7f, 1f, 30f);
                    foreach (var e in Match.I.fighters)
                        if (e.alive && e.team != attacker.team && Vector3.Distance(e.transform.position, c) < 3.4f) { DamageCtx.Set(DamageKind.Env, "火锅飞溅"); e.ReceiveDamage(8f, attacker, c, false); }
                }
            }
            int zone = SauceZone.At(h.point + Vector3.up * .05f);
            if (zone == SauceZone.Butter && w.id == "P-9") SauceZone.Mark(h.point, 6f, false);                    // 番茄酱 + 黄油 = 更滑
            if (zone == SauceZone.Chocolate && fs.Transformed && fs.form == FoodForm.Spicy) SauceZone.Mark(h.point, 8f, true);   // 辣椒 + 巧克力 = 热巧克力
        }
    }

    // ---------------------------------------------------------------- 大招产物
    public class RiceWall : MonoBehaviour, IDamageable
    {
        public float hp = 400; float born;
        public static void Spawn(Vector3 at, float yaw, Fighter owner)
        {
            var go = new GameObject("饭团墙"); go.transform.position = at; go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var bc = go.AddComponent<BoxCollider>(); bc.center = new Vector3(0, .7f, 0); bc.size = new Vector3(2.4f, 1.4f, .8f);
            var m = Arena.Model("Toon/Hats/Hat_Onigiri");
            if (m != null) { foreach (var c in m.GetComponentsInChildren<Collider>()) Destroy(c); Arena.Fit(m, new Vector3(2.4f, 1.4f, .8f)); m.transform.SetParent(go.transform, false); }
            var w = go.AddComponent<RiceWall>(); w.born = Time.time;
            FoodFx.KillPop(at + Vector3.up * .7f);
        }
        public bool TakeDamage(float amount, Fighter source, bool explosive)
        {
            hp -= amount; Fx.Burst(transform.position + Vector3.up * .7f, Vector3.up, Color.white, 4, 2f, .05f, .4f, .8f);
            if (hp <= 0) { FoodFx.KillPop(transform.position + Vector3.up * .7f); Destroy(gameObject); }
            return true;
        }
        void Update() { if (Time.time - born > 15f || Match.I == null || Match.I.phase != Phase.Live) Destroy(gameObject); }
    }

    public class JellyDome : MonoBehaviour
    {
        Fighter owner; float born, tick;
        public static void Spawn(Vector3 at, Fighter owner)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name = "果冻罩"; Destroy(go.GetComponent<Collider>());
            go.transform.position = at; go.transform.localScale = Vector3.one * 10f;
            var r = go.GetComponent<Renderer>(); var m = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, .5f, .75f, .18f) }; r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var d = go.AddComponent<JellyDome>(); d.owner = owner; d.born = Time.time;
            Sfx.Play("food_boing", at, 1f, .7f, 1f, 40f);
        }
        void Update()
        {
            float age = Time.time - born;
            transform.localScale = Vector3.one * 10f * (1f + .03f * Mathf.Sin(Time.time * 6f)) * Mathf.Clamp01(age * 4f);
            if (age > 8f || Match.I == null) { Destroy(gameObject); return; }
            if (Time.time < tick) return; tick = Time.time + .5f;
            foreach (var f in Match.I.fighters)
                if (f.alive && owner != null && f.team == owner.team && Vector3.Distance(f.transform.position, transform.position) < 5f && f.hp < Config.MaxHp)
                { f.hp = Mathf.Min(Config.MaxHp, f.hp + 5f); Fx.Burst(f.Chest, Vector3.up, new Color(1f, .6f, .8f), 3, 1f, .05f, .5f, -.3f); }
        }
    }

    public class CandleBarrage : MonoBehaviour
    {
        Fighter owner; Vector3 center; int dropped; float next;
        public int Dropped { get { return dropped; } }   // V22 第三期：测试读"生效时间不变"
        public static void Spawn(Vector3 at, Fighter owner) { var go = new GameObject("蜡烛轰炸"); var c = go.AddComponent<CandleBarrage>(); c.owner = owner; c.center = at; c.next = Time.time + .3f; }
        void Update()
        {
            if (Time.time < next) return;
            next = Time.time + .33f; dropped++;
            Vector2 r = Random.insideUnitCircle * 4.5f; Vector3 p = Skills.GroundAt(center + new Vector3(r.x, 0, r.y));
            Fx.Explosion(p, 2.5f);
            for (int i = 0; i < 3; i++) Fx.Burst(p + Vector3.up * .3f, Vector3.up, FoodFx.Confetti[(dropped + i) % 6], 18, 6f, .08f, .8f, .8f);
            FoodFx.Splat(p + Vector3.up * .02f, Vector3.up, new Color(1f, .97f, .92f), 1.4f);
            Sfx.Play("boom", p, .5f, 1.3f, 1f, 70f); Sfx.Play("food_confetti", p, .6f, 1.1f, 1f, 50f);
            if (Match.I != null)
                foreach (var f in Match.I.fighters)
                {
                    if (!f.alive || owner != null && f.team == owner.team && f != owner) continue;
                    float d = Vector3.Distance(f.Chest, p); if (d > 3.5f) continue;
                    DamageCtx.Set(DamageKind.Ult, FoodState.UltName(Role.Ember)); f.ReceiveDamage(45f * (1f - d / 3.5f) * (f == owner ? .5f : 1f), owner, p, false, true);
                }
            if (dropped >= 6) Destroy(gameObject);
        }
    }

    // ---------------------------------------------------------------- 抢食大作战：两队的大锅
    public class CookPot : Interactable
    {
        public static readonly CookPot[] Pots = new CookPot[2];
        public int team; TextMesh label;
        public const float Radius = 2.6f;
        public static void Create(int team, Vector3 at)
        {
            if (Pots[team] != null) return;
            var go = new GameObject((team == 0 ? "蓝队" : "红队") + "大锅"); go.transform.position = at;
            var p = go.AddComponent<CookPot>(); p.team = team; p.holdTime = 2f; p.range = 3f;
            var m = Arena.Model("Toon/Food/HotPot");
            if (m != null) { foreach (var c in m.GetComponentsInChildren<Collider>()) Arena.Kill(c); Arena.Fit(m, new Vector3(2.6f, 0, 2.6f)); m.transform.SetParent(go.transform, false); }
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Arena.Kill(ring.GetComponent<Collider>()); ring.transform.SetParent(go.transform, false);
            ring.transform.localScale = new Vector3(Radius * 2, .01f, Radius * 2); ring.transform.localPosition = Vector3.up * .02f;
            var rm = new Material(Shader.Find("Sprites/Default")) { color = new Color(Config.TeamColor(team).r, Config.TeamColor(team).g, Config.TeamColor(team).b, .3f) }; ring.GetComponent<Renderer>().sharedMaterial = rm;
            p.label = new GameObject("分数").AddComponent<TextMesh>(); p.label.transform.SetParent(go.transform, false); p.label.transform.localPosition = Vector3.up * 3f;
            var font = Resources.Load<Font>("NotoSansSC"); p.label.font = font; if (font != null) p.label.GetComponent<MeshRenderer>().sharedMaterial = Arena.WorldText(font);
            p.label.anchor = TextAnchor.MiddleCenter; p.label.fontSize = 64; p.label.characterSize = .06f; p.label.color = Config.TeamColor(team); p.label.gameObject.AddComponent<Billboard>();
            Pots[team] = p;
        }
        public static void Clear() { Pots[0] = Pots[1] = null; }
        void Update()
        {
            var m = Match.I; if (m == null) return;
            if (label != null) label.text = (team == 0 ? "蓝队大锅 " : "红队大锅 ") + m.roundsWon[team] + " 分";
            if (m.phase != Phase.Live) return;
            foreach (var f in m.fighters)
            {
                if (!f.alive || f.team != team || f.Food.ingredients.Count == 0) continue;
                if (Vector3.Distance(f.transform.position, transform.position) > Radius) continue;
                int n = f.Food.ingredients.Count; int pts = n;
                bool dish = n == 2 && Ingredients.Recipe(f.Food.ingredients[0], f.Food.ingredients[1]) != FoodForm.Dark;
                if (dish) pts += 1;
                string what = dish ? "做出了「" + Ingredients.FormName(Ingredients.Recipe(f.Food.ingredients[0], f.Food.ingredients[1])) + "」" : "放进了 " + n + " 样食材";
                f.Food.ingredients.Clear(); f.Food.delivered += n;
                m.CookScore(team, pts, (f.human ? "你" : f.Hero.name) + " " + what + "  +" + pts);
                FoodFx.KillPop(transform.position + Vector3.up * 1.2f);
            }
        }
        public override bool Usable(Fighter f) { return f.alive && f.team != team && Match.Mode == GameMode.Cook && Match.I.roundsWon[team] > 0 && f.Food.CanCarry; }
        public override string Prompt(Fighter f) { return "按住 G 偷对面锅里的食材（对方 -1 分）"; }
        public override Vector3 Anchor { get { return transform.position + Vector3.up; } }
        protected override void Complete(Fighter f)
        {
            Match.I.CookScore(team, -1, (f.human ? "你" : f.Hero.name) + " 从" + (team == 0 ? "蓝队" : "红队") + "锅里偷走了食材！");
            f.Food.steals++; f.Food.Pick(Ingredients.All[Random.Range(0, Ingredients.All.Length)]);
        }
    }

    // ---------------------------------------------------------------- 美食 HUD：食材格、变身、大招、准镜、表情气泡、帽子与图鉴
    public class FoodHud : MonoBehaviour
    {
        public static bool showCollection;
        static GUIStyle label, big, small, center; static Texture2D white;
        static readonly Dictionary<string, Texture2D> reticles = new Dictionary<string, Texture2D>();

        static void Styles()
        {
            if (label != null) return;
            var font = Resources.Load<Font>("NotoSansSC");
            label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 16, normal = { textColor = Color.white } };
            big = new GUIStyle(label) { fontSize = 26, fontStyle = FontStyle.Bold };
            small = new GUIStyle(label) { fontSize = 13 };
            center = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
            white = Texture2D.whiteTexture;
        }
        static void Fill(Rect r, Color c) { var o = GUI.color; GUI.color = new Color(c.r, c.g, c.b, c.a * o.a); GUI.DrawTexture(r, white); GUI.color = o; }   // V22：跟随外层透明度（准镜淡入）
        static void Txt(Rect r, string t, GUIStyle s, Color c) { var o = s.normal.textColor; var sh = new Rect(r.x + 1, r.y + 1, r.width, r.height); s.normal.textColor = new Color(0, 0, 0, .6f); GUI.Label(sh, t, s); s.normal.textColor = c; GUI.Label(r, t, s); s.normal.textColor = o; }

        // ---- 美食准镜（程序生成贴图）
        public static Texture2D Reticle(string id)
        {
            if (reticles.TryGetValue(id, out var t)) return t;
            const int N = 256; var px = new Color[N * N]; float c = N / 2f;
            System.Func<float, float, float, float> ring = (r, R0, w) => Mathf.Clamp01(1f - Mathf.Abs(r - R0) / w);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = x - c, dy = y - c, r = Mathf.Sqrt(dx * dx + dy * dy) / c, a = Mathf.Atan2(dy, dx); float v = 0;
                    switch (id)
                    {
                        case "SMG-7": v = Mathf.Max(ring(r, .34f, .05f), ring(r, .2f, .03f)); break;                                            // 吸管口双环
                        case "AR-9": for (int k = 0; k < 6; k++) { float ka = k * Mathf.PI / 3; v = Mathf.Max(v, Mathf.Clamp01(1f - (new Vector2(dx, dy) / c - new Vector2(Mathf.Cos(ka), Mathf.Sin(ka)) * .33f).magnitude / .06f)); } break;   // 爆米花散布圈
                        case "LMG-2": v = Mathf.Max(ring(r, .42f, .04f), ring(r, .32f, .02f)); break;                                          // 寿司盘
                        case "FS-12": v = Mathf.Max(ring(r, .44f, .03f), Mathf.Abs(dx) < 2 && r < .44f || Mathf.Abs(dy) < 2 && r < .44f ? .5f : 0); break;   // 蒸笼格
                        case "BR-5": { float star = .3f + .06f * Mathf.Cos(a * 6); v = ring(r, star, .03f); break; }                            // 裱花嘴星形
                        default: v = ring(r, .26f, .04f); break;                                                                                   // 番茄酱瓶口
                    }
                    if (r < .03f) v = 1;
                    px[y * N + x] = new Color(1, 1, 1, v);
                }
            t = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp }; t.SetPixels(px); t.Apply(); reticles[id] = t; return t;
        }

        public static void DrawReticle(float W, float H, Fighter p, float alpha = 1f)
        {
            Styles();
            var th = FoodFx.For(p.W); float cx = W / 2, cy = H / 2;
            var oc0 = GUI.color; GUI.color = new Color(1, 1, 1, alpha);
            if (p.W.sight == "scope")
            {
                // 冰糖葫芦 / 奶油裱花：整片柠檬切片做成全屏狙击镜
                float R = H * .46f;
                Fill(new Rect(0, 0, W, cy - R), new Color(.08f, .06f, .02f, .92f)); Fill(new Rect(0, cy + R, W, H - cy - R), new Color(.08f, .06f, .02f, .92f));
                Fill(new Rect(0, cy - R, cx - R, 2 * R), new Color(.08f, .06f, .02f, .92f)); Fill(new Rect(cx + R, cy - R, W - cx - R, 2 * R), new Color(.08f, .06f, .02f, .92f));
                if (!reticles.TryGetValue("LEMON", out var lemon)) reticles["LEMON"] = lemon = LemonTexture();
                GUI.DrawTexture(new Rect(cx - R, cy - R, 2 * R, 2 * R), lemon);
                Fill(new Rect(cx - R * .8f, cy - 1, R * 1.6f, 2), new Color(.2f, .1f, 0, .9f)); Fill(new Rect(cx - 1, cy - R * .8f, 2, R * 1.6f), new Color(.2f, .1f, 0, .9f));
                Fill(new Rect(cx - 3, cy - 3, 6, 6), new Color(1f, .2f, .2f));
                GUI.color = oc0;
                return;
            }
            float size = 150;
            GUI.color = new Color(0, 0, 0, .45f * alpha); GUI.DrawTexture(new Rect(cx - size / 2 + 2, cy - size / 2 + 2, size, size), Reticle(p.W.id));
            GUI.color = new Color(th.splat.r * .6f + .4f, th.splat.g * .6f + .4f, th.splat.b * .6f + .4f, .95f * alpha); GUI.DrawTexture(new Rect(cx - size / 2, cy - size / 2, size, size), Reticle(p.W.id)); GUI.color = oc0;
        }
        static Texture2D LemonTexture()
        {
            const int N = 512; var px = new Color[N * N]; float c = N / 2f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = x - c, dy = y - c, r = Mathf.Sqrt(dx * dx + dy * dy) / c, a = Mathf.Atan2(dy, dx);
                    Color col = new Color(0, 0, 0, 0);
                    if (r > 1f) col = new Color(.08f, .06f, .02f, .92f);
                    else if (r > .92f) col = new Color(1f, .82f, .15f, 1f);                       // 果皮
                    else if (r > .86f) col = new Color(1f, .97f, .8f, 1f);                        // 白色内皮
                    else
                    {
                        float seg = Mathf.Abs(Mathf.Sin(a * 4f));
                        if (seg < .06f && r > .1f) col = new Color(1f, .97f, .8f, .9f);           // 果瓣分隔
                        else if (r > .8f) col = new Color(1f, .9f, .4f, .35f);                    // 果肉边缘一点颜色
                    }
                    px[y * N + x] = col;
                }
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp }; t.SetPixels(px); t.Apply(); return t;
        }

        // ---- 今日菜单（结算称号）
        public static void DrawTitles(float W, float H)
        {
            Styles(); var m = Match.I; if (m == null) return;
            var list = new List<string>();
            Fighter Best(System.Func<Fighter, float> score) { Fighter b = null; float bs = 0; foreach (var f in m.fighters) { float s = score(f); if (s > bs) { bs = s; b = f; } } return b; }
            void Add(string title, Fighter f) { if (f != null) list.Add(title + "：" + f.Hero.name + (f.human ? "（你）" : "")); }
            Add("今日主厨", Best(f => Total(f).kills * 3 + Total(f).damage / 50f + f.Food.delivered * 2));
            Add("番茄酱狂魔", Best(f => Total(f).damage));
            Add("火锅守护者", Best(f => f.Food.potHealed));
            Add("百变吃货", Best(f => f.Food.transforms));
            Add("黑暗料理大师", Best(f => f.Food.darkDishes));
            Add("吃货本货", Best(f => f.Food.snacks));
            if (Match.Mode == GameMode.Cook) Add("偷菜大盗", Best(f => f.Food.steals));
            Rect r = new Rect(W / 2 - 520, H * .74f, 1040, 34);
            Fill(r, new Color(.25f, .08f, .2f, .85f));
            Txt(r, "今日菜单  ·  " + string.Join("   ·   ", list), new GUIStyle(center) { fontSize = 15 }, new Color(1f, .85f, .6f));
        }
        static Stats Total(Fighter f) { var s = new Stats(); s.Add(f.matchStats); if (Match.I.phase != Phase.RoundEnd && Match.I.phase != Phase.MatchEnd) s.Add(f.stats); return s; }

        void OnGUI()
        {
            var m = Match.I; if (m == null || !ToonAssets.Available) return;
            Styles();
            GUI.depth = -5;
            float s = Mathf.Clamp(Screen.height / 1080f, .7f, 1.6f); GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            float W = Screen.width / s, H = Screen.height / s;
            if (showCollection) { CollectionPanel(W, H); return; }
            if (m.ui != null && !m.ui.Started && !m.autoplay && !m.testMode) return;
            var p = m.player;
            // 表情气泡
            var cam = Camera.main;
            if (cam != null)
                foreach (var f in m.fighters)
                {
                    var clip = f.Food.EmoteClip; if (clip == null || f == p) continue;
                    Vector3 sp = cam.WorldToScreenPoint(f.transform.position + Vector3.up * 2.5f);
                    if (sp.z < 0 || sp.z > 40) continue;
                    Rect b = new Rect(sp.x / s - 50, (Screen.height - sp.y) / s - 16, 100, 30);
                    Fill(b, new Color(1, 1, 1, .9f)); Txt(b, FoodState.EmoteLabel(clip), new GUIStyle(center) { fontSize = 15 }, new Color(.3f, .15f, .2f));
                }
            if (p == null) return;
            var fs = p.Food;
            // 食材格 + 变身 + 大招（左下角面板上方）
            float x = 16, y = H - 230;
            for (int i = 0; i < 2; i++)
            {
                Rect slot = new Rect(x + i * 108, y, 100, 34);
                Fill(slot, new Color(.08f, .05f, .1f, .8f));
                if (i < fs.ingredients.Count) { var id = fs.ingredients[i]; Fill(new Rect(slot.x, slot.y, 6, 34), Ingredients.Col(id)); Txt(slot, Ingredients.Name(id), new GUIStyle(center) { fontSize = 15 }, Ingredients.Col(id)); }
                else Txt(slot, "空", new GUIStyle(center) { fontSize = 13 }, new Color(.6f, .6f, .7f));
            }
            if (Match.Mode != GameMode.Cook && fs.ingredients.Count == 1)
                Txt(new Rect(x + 222, y, 460, 34), FoodGuide.HudHint(fs.ingredients), new GUIStyle(small) { fontSize = 15 }, new Color(.6f, 1f, .65f));   // V21：差一样：鸡蛋 → 巨型蛋糕
            if (Match.Mode == GameMode.Cook && fs.ingredients.Count > 0) Txt(new Rect(x + 222, y, 360, 34), "把食材送回自己队的大锅", small, new Color(1f, .85f, .7f));
            // 大招
            Rect ub = new Rect(x, y + 40, 208, 14); Fill(ub, new Color(.08f, .05f, .1f, .8f));
            Fill(new Rect(ub.x, ub.y, ub.width * fs.ult, ub.height), fs.UltReady ? new Color(1f, .85f, .3f) : new Color(1f, .6f, .8f, .8f));
            bool flash = fs.UltReady && Mathf.Repeat(Time.time * 2f, 1f) < .5f;   // 就绪时整条闪
            if (flash) Fill(new Rect(ub.x - 2, ub.y - 2, ub.width + 4, ub.height + 4), new Color(1f, .9f, .4f, .5f));
            Txt(new Rect(x + 214, y + 34, 640, 24), (fs.UltReady ? "X 大招就绪 · " : "大招 " + Mathf.FloorToInt(fs.ult * 100) + "% · ") + FoodState.UltName(p.role) + "：" + FoodGuide.UltEffect(p.role), small, fs.UltReady ? new Color(1f, .85f, .3f) : new Color(.9f, .8f, .9f));
            if (fs.Transformed)
            {
                Rect fb = new Rect(W / 2 - 220, 98, 440, 50); var fc = Ingredients.FormColor(fs.form);
                Fill(fb, new Color(fc.r * .3f, fc.g * .3f, fc.b * .3f, .85f)); Fill(new Rect(fb.x, fb.yMax - 4, fb.width * fs.FormLeft / Ingredients.FormTime(fs.form), 4), fc);
                Txt(new Rect(fb.x, fb.y + 2, fb.width, 26), "变身：" + Ingredients.FormName(fs.form) + "  " + fs.FormLeft.ToString("0") + " 秒", new GUIStyle(center) { fontSize = 19, fontStyle = FontStyle.Bold }, fc);
                Txt(new Rect(fb.x, fb.y + 24, fb.width, 22), Ingredients.FormEffect(fs.form), new GUIStyle(center) { fontSize = 13 }, Color.white);
            }
            if (fs.crispy) Txt(new Rect(W - 436, H - 186, 420, 24), "金黄酥脆版 · 伤害 +15%", new GUIStyle(small) { alignment = TextAnchor.MiddleRight }, new Color(1f, .75f, .25f));
            if (Match.Mode == GameMode.Grow) Txt(new Rect(x, y - 30, 400, 26), "体型 " + fs.growLevel + " 级（吃零食 +1，击杀 +2，阵亡减半）", label, new Color(1f, .85f, .4f));
            var myClip = fs.EmoteClip; if (myClip != null) Txt(new Rect(W / 2 - 100, H / 2 + 140, 200, 26), "你：" + FoodState.EmoteLabel(myClip), new GUIStyle(center) { fontSize = 16 }, Color.white);
            float rk = GameUI.FoodReticleK(p);   // V22：美食准镜和腰射准星交叉淡入（adsK 0.6 → 0.85），开镜过程中屏幕中心始终有准星
            if (p.alive && rk > .01f && (m.ui == null || !m.ui.HudOverlayOpen)) DrawReticle(W, H, p, rk);
        }

        void CollectionPanel(float W, float H)
        {
            Fill(new Rect(0, 0, W, H), new Color(.1f, .04f, .1f, .92f));
            Txt(new Rect(0, 34, W, 56), "帽子与菜谱图鉴", new GUIStyle(center) { fontSize = 34, fontStyle = FontStyle.Bold }, new Color(1f, .8f, .9f));
            Txt(new Rect(0, 90, W, 26), "累计击杀 " + Collection.Kills + "   ·   累计变身 " + Collection.Transforms + "   ·   胜场 " + Collection.Wins, new GUIStyle(center) { fontSize = 15 }, new Color(.9f, .85f, .9f));
            float x0 = W / 2 - 560, y0 = 140;
            Txt(new Rect(x0, y0 - 6, 560, 42), "英雄帽子（跟随英雄）", big, Color.white);
            string[] heroes = { "猎隼 · 汉堡", "铆钉 · 饭团", "铁幕 · 布丁", "夜鸦 · 寿司", "焚芯 · 纸杯蛋糕", "幻频 · 甜甜圈" };
            for (int i = 0; i < 6; i++) Txt(new Rect(x0 + (i % 3) * 190, y0 + 40 + (i / 3) * 30, 190, 28), heroes[i], label, new Color(1f, .9f, .75f));
            Txt(new Rect(x0, y0 + 112, 560, 42), "特别帽子（解锁后只给你自己戴）", big, Color.white);
            for (int i = 0; i < 3; i++)
            {
                string h = Collection.SpecialHats[i]; bool un = Collection.HatUnlocked(h), on = Collection.EquippedHat == h;
                Rect b = new Rect(x0 + i * 190, y0 + 160, 180, 70);
                Fill(b, on ? new Color(.6f, .25f, .45f, .9f) : un ? new Color(.35f, .15f, .3f, .9f) : new Color(.2f, .2f, .22f, .9f));
                Txt(new Rect(b.x, b.y + 6, b.width, 26), Collection.HatName(h) + (on ? "（已戴）" : ""), new GUIStyle(center) { fontSize = 18 }, un ? Color.white : new Color(.6f, .6f, .65f));
                Txt(new Rect(b.x, b.y + 36, b.width, 24), un ? "点击佩戴 / 取下" : "未解锁：" + Collection.HatCondition(h), new GUIStyle(center) { fontSize = 12 }, new Color(.85f, .8f, .85f));
                if (un && GUI.Button(b, GUIContent.none, GUIStyle.none)) Collection.EquippedHat = on ? "" : h;
            }
            Txt(new Rect(x0 + 620, y0 - 6, 560, 42), "菜谱图鉴（凑成过的菜会点亮）", big, Color.white);
            for (int i = 0; i < Ingredients.Forms.Length; i++)
            {
                var fm = Ingredients.Forms[i]; bool d = Collection.Discovered(fm);
                Rect r = new Rect(x0 + 620, y0 + 40 + i * 62, 520, 56);
                Fill(r, d ? new Color(.3f, .12f, .25f, .9f) : new Color(.18f, .18f, .2f, .9f));
                Fill(new Rect(r.x, r.y, 6, r.height), d ? Ingredients.FormColor(fm) : new Color(.4f, .4f, .45f));
                // V21：配方是规则，名字和效果全部公开；"没做过"只影响点亮和帽子解锁
                Txt(new Rect(r.x + 14, r.y + 2, 500, 28), Ingredients.FormName(fm) + "  ·  " + Ingredients.FormRecipe(fm) + (d ? "  · 已做过" : "  · 还没做过"), label, d ? Ingredients.FormColor(fm) : new Color(.7f, .7f, .75f));
                Txt(new Rect(r.x + 14, r.y + 28, 500, 22), Ingredients.FormEffect(fm), small, new Color(.85f, .85f, .9f));
            }
            Rect close = new Rect(W / 2 - 120, H - 90, 240, 50);
            Fill(close, new Color(.6f, .25f, .45f, .95f)); Txt(close, "返回", new GUIStyle(center) { fontSize = 20 }, Color.white);
            if (GUI.Button(close, GUIContent.none, GUIStyle.none) || Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape) showCollection = false;
        }
    }
}
