using UnityEngine;

namespace NeonSiege
{
    public enum Role { Falcon, Rivet, Ironwall, Raven, Ember, Phantom }

    // All tunables come from the GDD (霓城封锁 V2.0, 第 3/6/7 章). Values the GDD does not
    // specify (spread, recoil, magazine, reload, head multiplier) are marked 原型假设.
    public class WeaponSpec
    {
        public string id, name, category, model;
        public float bodyDamage, rpm, fullRange, falloffRange, minFalloff;
        public int pellets = 1, burst = 1, magazine;
        public float burstInterval, reload;
        public float hipSpread, adsSpread, bloomPerShot, maxBloom, recoil, adsZoom = 1.35f, moveMultiplier = 1f;
        public bool automatic = true;
        public float bulletSpeed = 200f;                  // 可见弹丸飞行速度（m/s）
        public string sight = "reddot";
        public string model2;                             // V5：网上下载的高精度枪模（Resources/Models/Guns2）                   // reddot / holo / scope / none
        public string muzzleDevice = "comp";              // comp / suppressor / brake
        public bool laser;
        public string sound; public float soundPitch = 1f;
        public string strengths;
        // V20：战斗模型
        public int pen = 3;                                                     // 穿透等级（对比护甲等级）
        public float headMul = 1.8f, chestMul = 1f, stomachMul = .95f, armMul = .85f, legMul = .75f;   // 部位倍率
        public float adsTime = .25f, sprintToFire = .2f, reloadTac = 2f, reloadEmpty = 2.5f, drawTime = .5f;   // 关键操作时间
        public float weight = 3.5f, dropScale = .3f, wallPen = .25f;           // 重量 kg、子弹下坠系数、可穿透墙厚 m
        public float hDrift = .35f, hRandom = .18f, vRamp = .3f;                // 固定弹道：横摆、随机、上跳递增

        public float ShotInterval { get { return 60f / rpm * burst; } }
        public int ShotsToKill { get { return Mathf.CeilToInt((Config.MaxHp + Config.MaxArmor) / (bodyDamage * pellets)); } }
        public float TheoreticalTtk
        {
            get
            {
                int bullets = ShotsToKill;
                if (burst <= 1) return (bullets - 1) * 60f / rpm;
                int fullBursts = (bullets - 1) / burst, rest = (bullets - 1) % burst;
                return fullBursts * ShotInterval + rest * burstInterval;
            }
        }
    }

    public class SkillSpec
    {
        public string name, purpose, rule, counter, numbers, howTo;
        // V21：美食模式的技能文案（设计稿_V21_美食技能与可发现性 §2.1）。只换皮，规则和数值不变；写实模式仍读上面的旧字段。
        public string foodName, foodPurpose, foodRule, foodCounter, foodNumbers, foodHowTo;
        static string Pick(string food, string real) { return ToonAssets.Available && !string.IsNullOrEmpty(food) ? food : real; }
        public string Name { get { return Pick(foodName, name); } }
        public string Purpose { get { return Pick(foodPurpose, purpose); } }
        public string Rule { get { return Pick(foodRule, rule); } }
        public string Counter { get { return Pick(foodCounter, counter); } }
        public string Numbers { get { return Pick(foodNumbers, numbers); } }
        public string HowTo { get { return Pick(foodHowTo, howTo); } }
        public int maxCharges = 2;          // V4：技能改为可充能（试玩反馈：技能对局势影响太小）
        public float cooldown = 20f;
    }

    public class HeroSpec
    {
        public int armorClass = 3, helmetClass = 2;   // V20：护甲 / 头盔等级
        public Role role;
        public string name, duty, model;
        public string gltf;                               // V5：带持枪动画的高精度人物（Resources/Models/Chars2）
        public Color tint;
        public WeaponSpec weapon;
        public SkillSpec skill;
    }

    public static class Config
    {
        // 爆破模式「棱能爆破」（玩家反馈后替换原占点模式）：攻方安装棱能炸弹，守方拆除。
        public const float PrepTime = 15f, RoundTime = 120f, PlantTime = 4f, DefuseTime = 6f, BombTimer = 40f;
        public const float SiteRadius = 7f, DefuseRange = 1.8f, RoundEndPause = 6f, BombBlastRadius = 18f;
        public const int RoundsToWin = 5;
        public const bool SwapEveryRound = true;          // 每回合结束攻守互换
        // 生命 100 + 护甲 50（护甲先扣）：有效生命 150，TTK 约为 GDD 基线的 1.5 倍。
        public const float MaxHp = 100f, MaxArmor = 50f;
        public const float HeadMultiplier = 1.8f;          // 原型假设
        public static float WalkSpeed = 5.0f, SprintSpeed = 6.8f, CrouchSpeed = 2.7f;   // V20：可在 F1 面板 / tuning_3c.csv 调
        public const float PointRadius = 4.5f;             // legacy, unused

        // 6 主动技能参数（策划假设）
        // V4 加强：数值整体上调，并给每个技能增加一项改变局势的副作用。
        public const float DashDistance = 9f, DashWindup = 0.15f, DashTime = 0.2f, DashAimRecovery = 0.2f;
        public const float DashGuardTime = 1.5f, DashDamageReduction = 0.35f;          // 突进后减伤 + 立即装填
        public const float CoverHp = 450f, CoverDeploy = 0.6f, CoverLife = 18f, CoverWidth = 3.6f, CoverHeight = 1.7f;
        public const float ShieldLife = 8f, ShieldHp = 650f, ShieldMoveMultiplier = 0.7f, ShieldWidth = 4.4f, ShieldHeight = 2.8f;
        public const float ArrowSpeed = 48f, ArrowGravity = .35f;
        public const float DroneRadius = 14f, DroneScanDelay = 1.0f, DroneLife = 7f, DroneRevealTime = 5f, DroneSecondPulse = 3f;
        public const float BlastWarning = 0.9f, BlastMaxDamage = 65f, BlastRadius = 5f, BlastCoverDamage = 120f, BlastCoverMultiplier = 2f;
        public const float FireRadius = 3.5f, FireDuration = 5f, FireDps = 14f;          // 爆破后残留燃烧区，封锁走位
        public const float NoiseRadius = 9f, NoiseLife = 8f, NoiseSlow = 0.8f, NoiseSpread = 1.6f;   // 区域内敌人减速、散布变大
        public const float LightCoverHp = 150f;
        public const float TerminalHoldTime = 1.5f;

        // V22 第三期：干员 × 地图联动（设计稿_V22_地图重构与干员地图联动 §4.3，取区间内初版；原技能本体数值不变）
        public static float DashPadBoost = 1.45f, DashPadWindow = .25f;                     // 猎隼 可乐弹射：水平 ×1.3–1.6；冲刺结束后 0.2–0.3 s 内仍算
        public static float CoverPatchLife = 30f, CoverPatchDeploy = .9f, CoverPatchSnap = 2f, CoverPatchReach = 6f;   // 铆钉 嵌入补洞：25–35 s / 0.8–1.0 s / 吸附 1.5–2.5 m
        public static float ShieldDockHpMult = .9f, ShieldDockLife = 11f, ShieldDockPassSlow = .5f, ShieldDockMaxWidth = 5f, ShieldDockRange = 5f;   // 铁幕 果冻门帘
        public static float MustardPotRadius = 6f, MustardPotLife = 6f, MustardPotLinger = 1f;   // 夜鸦 芥末辣锅：5–7 m / 5–7 s / 离开后 1 s
        public static float BlastWallDoorHalf = 1.25f, BlastWallDoorHeight = 2.4f;           // 焚芯 开墙：粘在蛋糕墙上 → 落点正下方开 ≥ 2.5 × 2.4 m 的门洞（原 2.3 m 炸碎范围保留）
        public static float CandySteamRadius = 4f, CandySteamLife = 6f;                      // 幻频 棉花糖蒸汽：3–4.5 m / 5–8 s（不超过迷雾剩余时间）
        public static float PingLife = 6f, PingCooldown = .6f;                               // 团队标记

        public static readonly HeroSpec[] Heroes =
        {
            new HeroSpec {
                role = Role.Falcon, name = "猎隼", duty = "突击", model = "Men_Punk", gltf = "Swat", tint = new Color(1f, .55f, .2f),
                weapon = new WeaponSpec { id = "SMG-7", name = "珍珠奶茶", category = "冲锋枪 · 打珍珠", model = "SubmachineGun_2", model2 = "SMG7",
                    bodyDamage = 19, rpm = 850, fullRange = 15, falloffRange = 30, minFalloff = .55f, magazine = 30, reload = 1.8f,
                    hipSpread = 2.2f, adsSpread = .9f, bloomPerShot = .28f, maxBloom = 3.5f, recoil = .45f, adsZoom = 1.25f,
                    bulletSpeed = 170, sight = "holo", muzzleDevice = "comp", laser = true, sound = "gun_smg", soundPitch = 1f, strengths = "机动近战强；远距衰减明显" },
                skill = new SkillSpec { name = "推进喷射", foodName = "汽水冲刺", foodPurpose = "摇一摇汽水，喷着可乐冲出去 9 米，跨短枪线或从侧路切入", foodRule = "方向跟随移动键；途中不能开火；落地 0.2 秒后恢复瞄准", foodCounter = "开罐那一声\"嘶\"谁都听得见；预瞄冲刺落点、范围伤害", foodNumbers = "位移 9 m · 冲完打个饱嗝：1.5 s 减伤 35%，弹匣直接续杯 · 2 次充能 / 12 s", foodHowTo = "按住方向键再按 F，朝该方向汽水冲刺；不按方向键则向前",
                    purpose = "短距突进，跨越短枪线或从侧路切入",
                    rule = "方向跟随移动输入；途中无法开火；落地后短暂恢复瞄准", counter = "启动有明显音效与喷焰；受预瞄与范围压制克制",
                    numbers = "位移 9 m · 突进后 1.5 s 减伤 35% 并立即装填 · 2 次充能 / 12 s", maxCharges = 2, cooldown = 12f, howTo = "按住方向键再按 F，朝该方向突进；不按方向键则向前" } },
            new HeroSpec {
                role = Role.Rivet, name = "铆钉", duty = "工程", model = "Men_Worker", gltf = "SciFiFemale", tint = new Color(1f, .82f, .2f),
                weapon = new WeaponSpec { id = "AR-9", name = "爆米花机", category = "突击步枪 · 打爆米花", model = "AssaultRifle_1", model2 = "AR9",
                    bodyDamage = 28, rpm = 620, fullRange = 30, falloffRange = 50, minFalloff = .75f, magazine = 25, reload = 2.0f,
                    hipSpread = 2.4f, adsSpread = .35f, bloomPerShot = .38f, maxBloom = 3.2f, recoil = .75f,
                    bulletSpeed = 230, sight = "reddot", muzzleDevice = "brake", sound = "gun_ar", soundPitch = 1f, strengths = "中距稳定；近身爆发不及冲锋枪" },
                skill = new SkillSpec { name = "战术掩体投放", foodName = "威化饼干墙", foodPurpose = "在面前立一块威化饼干墙，临时挡子弹、掩护队友往前压或救援", foodRule = "只能贴地放在合法区域；不能封住包点入口；敌我都能打碎", foodCounter = "点火纸杯蛋糕（×2）、持续火力、绕侧翼", foodNumbers = "宽 3.6 m · 耐久 585（含被动）· 部署 0.6 s · 存在 18 s · 2 次充能 / 20 s", foodHowTo = "按住 F 预览威化墙（绿色=能放），松开 F 立墙；右键取消",
                    purpose = "临时遮蔽、推进保护或救援掩护",
                    rule = "只能贴地放在合法区域；不能封住棱核入口；敌我均可破坏", counter = "爆破技能、高持续火力、侧翼绕行",
                    numbers = "宽 3.6 m · 耐久 450 · 部署 0.6 s · 存在 18 s · 2 次充能 / 20 s", maxCharges = 2, cooldown = 20f, howTo = "按住 F 预览（绿色=合法），松开 F 部署；按右键取消" } },
            new HeroSpec {
                role = Role.Ironwall, name = "铁幕", duty = "防守", model = "Men_Swat", gltf = "Swat", tint = new Color(.35f, .75f, 1f),
                weapon = new WeaponSpec { id = "LMG-2", name = "回转寿司", category = "轻机枪 · 寿司加特林", model = "AssaultRifle2_2", model2 = "LMG2",
                    bodyDamage = 22, rpm = 700, fullRange = 30, falloffRange = 50, minFalloff = .7f, magazine = 60, reload = 3.2f,
                    hipSpread = 3.0f, adsSpread = .6f, bloomPerShot = .22f, maxBloom = 3.6f, recoil = .55f, moveMultiplier = .9f,
                    bulletSpeed = 210, sight = "holo", muzzleDevice = "brake", sound = "gun_lmg", soundPitch = 1f, strengths = "持续压制；移动与换弹效率较低" },
                skill = new SkillSpec { name = "防护阵列", foodName = "焦糖布丁盾", foodPurpose = "在身前立起一块 Q 弹的布丁盾，只挡正面，在包点前缘控线", foodRule = "只挡正面；举盾期间自己走得慢；敌我子弹都挡；双方都能隔着盾看到人", foodCounter = "绕侧后、用点火纸杯蛋糕烤化它、拖时间等它化掉", foodNumbers = "宽 4.4 m · 持续 8 s · Q 弹承伤 650 · 1 次充能 / 22 s", foodHowTo = "面向敌人来的方向按 F，在身前立起布丁盾",
                    purpose = "投射定向防护阵列，在争夺点前缘控线",
                    rule = "只挡正面；阵列存在期间自身移速降低；敌我子弹都无法穿透", counter = "远侧包抄、爆破清理、拖时间等阵列结束",
                    numbers = "宽 4.4 m · 持续 8 s · 承伤 650 · 1 次充能 / 22 s", maxCharges = 1, cooldown = 22f, howTo = "面向敌方来向按 F，在身前投射防护阵列" } },
            new HeroSpec {
                role = Role.Raven, name = "夜鸦", duty = "侦查", model = "Women_SciFi", gltf = "SciFiFemale", tint = new Color(.6f, .5f, 1f),
                weapon = new WeaponSpec { id = "DMR-3", name = "冰糖葫芦", category = "狙击步枪 · 打山楂", model = "SniperRifle_3", model2 = "DMR3",
                    bodyDamage = 42, rpm = 300, fullRange = 45, falloffRange = 70, minFalloff = .8f, magazine = 12, reload = 2.4f,
                    hipSpread = 3.6f, adsSpread = .08f, bloomPerShot = 1.1f, maxBloom = 4f, recoil = 2.0f, adsZoom = 4f, automatic = false,
                    bulletSpeed = 330, sight = "scope", muzzleDevice = "suppressor", sound = "gun_dmr", soundPitch = 1f, strengths = "单发收益高；近战容错低" },
                skill = new SkillSpec { name = "侦测箭", foodName = "芥末探签", foodPurpose = "射出一根芥末签，钉住后放出芥末味，闻到的敌人全队可见", foodRule = "高速、轻微下坠；钉住 1 s 后扫描，3 s 后再呛一次；签子和敌人之间要没有遮挡；发现的敌人全队透墙可见", foodCounter = "竹签一碰就断：一枪打掉签子；换位、躲到遮挡后、棉花糖迷雾", foodNumbers = "半径 14 m · 两次扫描 · 全队透视 5 s · 2 次充能 / 22 s", foodHowTo = "按住 F 显示弹道，松开 F 射出芥末签；右键取消",
                    purpose = "射出一支侦测箭，钉入墙面或地面后扫描附近敌人",
                    rule = "箭矢高速飞行、轻微下坠，钉入表面 1 s 后扫描，3 s 后再扫一次；需要信标与敌人之间无遮挡；被发现的敌人对全队透墙高亮", counter = "击毁箭矢信标（一枪即毁）、提前换位、利用掩体遮挡",
                    numbers = "半径 14 m · 两次扫描 · 全队透视 5 s · 2 次充能 / 22 s", maxCharges = 2, cooldown = 22f, howTo = "按住 F 显示弹道，松开 F 射出侦测箭；右键取消" } },
            new HeroSpec {
                role = Role.Ember, name = "焚芯", duty = "爆破", model = "Women_Soldier", gltf = "Swat", tint = new Color(1f, .3f, .2f),
                weapon = new WeaponSpec { id = "FS-12", name = "饺子蒸笼", category = "霰弹枪 · 一把饺子", model = "Shotgun_1", model2 = "FS12",
                    bodyDamage = 7, pellets = 8, rpm = 75, fullRange = 8, falloffRange = 20, minFalloff = .25f, magazine = 6, reload = 2.6f,
                    hipSpread = 5.5f, adsSpread = 3.8f, bloomPerShot = 0, maxBloom = 0, recoil = 3.2f, automatic = false,
                    bulletSpeed = 150, sight = "reddot", muzzleDevice = "brake", laser = true, sound = "gun_shotgun", soundPitch = 1f, strengths = "近身威胁强；中距散布显著" },
                skill = new SkillSpec { name = "定向爆破", foodName = "点火纸杯蛋糕", foodPurpose = "扔出插着烟花蜡烛的纸杯蛋糕，清掉威化墙 / 布丁盾，把人逼出点位", foodRule = "抛物线扔出并粘住；0.9 s 引信滋滋声 + 红圈预警后炸开，原地留下 5 s 焦糖火", foodCounter = "听到滋滋声立刻撤出红圈；躲到不可破坏的掩体后", foodNumbers = "预警 0.9 s · 最高 65 伤害 · 焦糖火 5 s · 对掩体 ×2 · 2 次充能 / 24 s", foodHowTo = "按住 F 显示抛物线，松开 F 扔出点火纸杯蛋糕",
                    purpose = "清理标记的轻型掩体并逼迫敌人离开点位",
                    rule = "抛物线投掷并贴附；0.9 s 声光预警后爆炸，并在原地留下 5 s 燃烧区", counter = "听到警报立即撤离爆炸范围、利用不可破坏掩体",
                    numbers = "预警 0.9 s · 伤害上限 65 · 残留燃烧 5 s · 对掩体 ×2 · 2 次充能 / 24 s", maxCharges = 2, cooldown = 24f, howTo = "按住 F 显示抛物线，松开 F 投掷爆破装置" } },
            new HeroSpec {
                role = Role.Phantom, name = "幻频", duty = "干扰", model = "Women_Punk", gltf = "SciFiFemale", tint = new Color(.9f, .35f, 1f),
                weapon = new WeaponSpec { id = "BR-5", name = "奶油裱花", category = "点射步枪 · 命中减速", model = "Bullpup_1", model2 = "BR5",
                    bodyDamage = 24, burst = 3, burstInterval = .065f, rpm = 540, fullRange = 25, falloffRange = 45, minFalloff = .75f, magazine = 27, reload = 2.1f,
                    hipSpread = 2.2f, adsSpread = .25f, bloomPerShot = .3f, maxBloom = 2.6f, recoil = .5f, automatic = false,
                    bulletSpeed = 250, adsZoom = 2f, sight = "scope", muzzleDevice = "comp", sound = "gun_br", soundPitch = 1f, strengths = "精确点射；连续跟枪要求高" },
                skill = new SkillSpec { name = "信号噪幕", foodName = "棉花糖迷雾", foodPurpose = "放一团棉花糖迷雾：糖味盖住芥末味，打断敌方信息", foodRule = "雾里的队友闻不出来（不会被扫描）；雾里的敌人黏脚变慢、手黏散布变大、小地图被糖糊住；不挡视线", foodCounter = "离开迷雾、等它散掉、用眼睛和队友报点", foodNumbers = "半径 9 m · 持续 8 s · 雾里敌人减速 20%、散布 +60% · 1 次充能 / 24 s", foodHowTo = "按住 F 选择落点（最远 22 m），松开 F 放出棉花糖迷雾",
                    purpose = "短时干扰区域信息，让敌方侦查标记失效",
                    rule = "区域内我方不会被侦测；区域内敌人小地图失效、屏幕受干扰、移动变慢、射击散布变大", counter = "离开区域、等待结束、用肉眼与队友报点",
                    numbers = "半径 9 m · 持续 8 s · 区域内敌人减速 20%、散布 +60% · 1 次充能 / 24 s", maxCharges = 1, cooldown = 24f, howTo = "按住 F 选择落点（最远 22 m），松开 F 布置噪幕" } },
        };

        public static HeroSpec Hero(Role role) { return Heroes[(int)role]; }

        // V13：开局选枪 —— 任何英雄都能买任何武器；英雄的招牌武器 7 折
        public static readonly WeaponSpec Pistol = new WeaponSpec { id = "P-9", name = "番茄酱瓶", category = "手枪（免费）", model = "Pistol", model2 = "",
            bodyDamage = 24, rpm = 400, fullRange = 12, falloffRange = 30, minFalloff = .6f, magazine = 14, reload = 1.4f,
            hipSpread = 2.2f, adsSpread = .5f, bloomPerShot = .9f, maxBloom = 3f, recoil = 1.2f, adsZoom = 1.2f, moveMultiplier = 1.05f, automatic = false,
            bulletSpeed = 180, sight = "reddot", muzzleDevice = "comp", sound = "gun_pistol", soundPitch = 1f, strengths = "免费；近距离补枪，射速快但威力低" };
        public const int StartCredits = 2000, WinCredits = 2500, LossCredits = 1700, LossStreakBonus = 300, KillCredits = 300, PlantCredits = 300, MaxCredits = 9000;
        static WeaponSpec[] arsenal;
        public static WeaponSpec[] Arsenal
        {
            get
            {
                if (arsenal == null)
                {
                    var l = new System.Collections.Generic.List<WeaponSpec> { Pistol };
                    foreach (var h in Heroes) l.Add(h.weapon);
                    arsenal = l.ToArray();
                }
                return arsenal;
            }
        }
        public static int BasePrice(WeaponSpec w)
        {
            switch (w.id) { case "SMG-7": return 1400; case "FS-12": return 1500; case "BR-5": return 2200; case "AR-9": return 2700; case "LMG-2": return 3100; case "DMR-3": return 3600; default: return 0; }
        }
        public static bool Signature(HeroSpec h, WeaponSpec w) { return h.weapon == w; }
        public static int Price(HeroSpec h, WeaponSpec w) { int b = BasePrice(w); return Signature(h, w) ? Mathf.RoundToInt(b * .7f / 50f) * 50 : b; }

        public static readonly Color Blue = new Color(.15f, .62f, 1f), Red = new Color(1f, .22f, .3f);
        static readonly Color[] FFAColors = { new Color(.35f, .7f, 1f), new Color(1f, .35f, .4f), new Color(1f, .8f, .2f), new Color(.4f, .95f, .55f), new Color(.75f, .45f, 1f), new Color(1f, .55f, .2f) };
        public static Color TeamColor(int team) { if (Match.FFA) return FFAColors[((team % 6) + 6) % 6]; return team == 0 ? Blue : Red; }
        public static string TeamName(int team) { if (Match.FFA) return "选手" + (team + 1); return team == 0 ? "蓝队" : "红队"; }
    }
}
