using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace NeonSiege
{
    public enum Phase { Prep, Live, RoundEnd, MatchEnd }
    public enum BombState { Carried, Dropped, Planted, Defused, Exploded }

    public class FeedItem
    {
        public string text; public float time; public Color color; public bool kill; public Fighter killer, victim; public bool head;
        public WeaponSpec gun; public string cause = ""; public DamageKind kind;   // V22：播报画实际用的枪 / 技能（不是击杀者的招牌枪）
    }

    public class RoundRecord
    {
        public int round, winner, attackers; public string reason; public float duration, firstContact = -1, plantTime = -1;
        public string plantSite = "-", firstContactZone = "-";
        public string blueComp, redComp, routes = ""; public int[] skillUses = new int[6], skillEffective = new int[6];
        public int[] kills = new int[2];
        // V22 第二期：地图类遥测（设计稿 §6.2）
        public int[] killDist = new int[4];            // < 15 / 15–40 / 40–60 / > 60 m
        public float retake = -1;                      // 下包后第一个守方进入包点用了几秒
        public int rotate, gateClose, gateOpen, stuck, doorEnd, valves;   // 走连接巷的攻方人数 / 关门 / 开门 / 卡在交互物前 / 回合结束停在门前 / 拧蒸汽阀
        // V22 第三期：联动触发次数（按 LinkKind：可乐弹射 / 嵌入补洞 / 果冻门帘 / 芥末辣锅 / 开墙 / 棉花糖蒸汽）、团队标记次数
        public int[] links = new int[6]; public int pings;
        public bool Timeout { get { return reason != null && reason.Contains("时间"); } }
    }

    // 爆破模式「棱能爆破」规则机：杀光对手不直接结束回合，攻方必须安装并引爆炸弹；守方拆除或拖到时间结束获胜。
    public class Match : MonoBehaviour
    {
        public static Match I;
        public readonly List<Fighter> fighters = new List<Fighter>();
        public readonly List<FeedItem> feed = new List<FeedItem>();
        public readonly List<RoundRecord> records = new List<RoundRecord>();
        public Fighter player;
        public GameUI ui;
        public Arena arena;
        public Phase phase = Phase.Prep;
        public int round;
        public readonly int[] roundsWon = new int[2];
        public float phaseTimer, roundClock;
        public string lastRoundResult = "", lastRoundReason = "";
        public int lastRoundWinner = -1;
        public bool autoplay, testMode;
        public float liveStartedAt;
        RoundRecord current;
        Vector3 specVel; int specIndex;
        bool eliminationCheck, defendersWipedAnnounced;

        // ---- bomb
        public BombState bomb = BombState.Carried;
        public Fighter BombCarrier;
        public Vector3 bombPos;
        public int plantedSite = -1;
        public float bombTimer, plantProgress, defuseProgress;
        public Fighter planter, defuser;
        BombObject bombObject;

        public int AttackingTeam { get { return Config.SwapEveryRound ? (round % 2 == 1 ? 0 : 1) : 0; } }
        public int DefendingTeam { get { return 1 - AttackingTeam; } }
        public bool CanAct { get { return phase == Phase.Live; } }
        float hitstopUntil;
        // Brief time freeze on the player's kills (打击感). Disabled for automated runs.
        public void RequestHitstop()
        {
            if (autoplay || testMode) return;
            hitstopUntil = Time.unscaledTime + .06f; Time.timeScale = .15f;
        }
        public bool CanLeaveSpawn { get { return phase != Phase.Prep; } }
        public bool Planted { get { return bomb == BombState.Planted; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindObjectOfType<Match>() != null) return;
            new GameObject("Neon Siege").AddComponent<Match>();
        }

        static bool Arg(string a) { return Array.IndexOf(Environment.GetCommandLineArgs(), a) >= 0; }

        void Awake()
        {
            I = this;
            Application.targetFrameRate = 120;
            Application.runInBackground = true;
            autoplay = Arg("-neonMatchTest") || Arg("-neonAutoplay") || Arg("-neonFFATest") || Arg("-neonCookTest") || Arg("-neonGrowTest");
            if (Arg("-neonFFA") || Arg("-neonFFATest")) Mode = GameMode.FFA;
            if (Arg("-neonCook") || Arg("-neonCookTest")) Mode = GameMode.Cook;
            if (Arg("-neonGrow") || Arg("-neonGrowTest")) Mode = GameMode.Grow;
            if (Arg("-neonRange") || Arg("-neonRangeTest")) Mode = GameMode.Range;
            if (Arg("-neonTests") || Arg("-neonShots") || Arg("-neonMatchTest")) Mode = GameMode.Bomb;
            CookPot.Clear();
            testMode = Arg("-neonTests") || Arg("-neonShots") || Arg("-neonRangeTest");
            Interactable.All.Clear();
            DataTables.Load();   // V20：读取 StreamingAssets/Data 里的数值表
            arena = new GameObject("霓城 · 旧港夜市街区").AddComponent<Arena>();
            arena.Build();
            ArenaLightmaps.Apply(arena);   // 编辑器里烘焙好的全局光照
            FoodScenery.Dress(arena);      // V21：场景道具（建在 Arena 层级外、烘焙匹配之后，不影响光照贴图按顺序匹配）
            GameSettings.Apply();
            Time.timeScale = 1;
            gameObject.AddComponent<Ambience>();
            StartCoroutine(VerifyGI());
            CreateTeams();
            ui = gameObject.AddComponent<GameUI>();
            if (Arg("-neonTests")) gameObject.AddComponent<PlaytestRunner>().mode = "tests";
            else if (Arg("-neonMatchTest") || Arg("-neonFFATest") || Arg("-neonCookTest") || Arg("-neonGrowTest")) gameObject.AddComponent<PlaytestRunner>().mode = "match";
            else if (Arg("-neonShots")) gameObject.AddComponent<PlaytestRunner>().mode = "shots";
            else if (Arg("-neonRangeTest")) gameObject.AddComponent<PlaytestRunner>().mode = "range";
            if (ToonAssets.Available) { gameObject.AddComponent<Airdrop>(); gameObject.AddComponent<FoodHud>(); gameObject.AddComponent<InteractMarkers>(); gameObject.AddComponent<FoodTips>(); }
            gameObject.AddComponent<TuningPanel>(); gameObject.AddComponent<CombatHud>(); gameObject.AddComponent<RangeHud>();
            gameObject.AddComponent<LinkHud>();   // V22 第三期：联动提示 / 团队标记 / 敌方大招预警
            StartMatch();
        }

        System.Collections.IEnumerator VerifyGI()
        {
            yield return null; ArenaLightmaps.Verify(arena);
            yield return new WaitForSecondsRealtime(1f); ArenaLightmaps.Verify(arena);
            yield return new WaitForSecondsRealtime(2f); ArenaLightmaps.Verify(arena);
        }

        // ------------------------------------------------------------------ V17 第三阶段：美食大乱斗（6 人各自为战）
        public static GameMode Mode = GameMode.Bomb;
        public static bool FFA { get { return Mode == GameMode.FFA || Mode == GameMode.Grow; } }
        public static string ModeName(GameMode m) { return m == GameMode.Bomb ? "3v3 蛋糕爆破" : m == GameMode.FFA ? "美食大乱斗" : m == GameMode.Cook ? "抢食大作战" : m == GameMode.Grow ? "吃货变大" : "靶场"; }
        public const int CookTarget = 20, GrowTarget = 12;
        public const int FFATarget = 15; public const float FFATime = 300f, FFARespawn = 3f;
        readonly Dictionary<Fighter, int> ffaKills = new Dictionary<Fighter, int>(), ffaDeaths = new Dictionary<Fighter, int>();
        readonly Dictionary<Fighter, float> ffaRespawnAt = new Dictionary<Fighter, float>();
        public int FFAKills(Fighter f) { return ffaKills.TryGetValue(f, out int k) ? k : 0; }
        public int FFADeaths(Fighter f) { return ffaDeaths.TryGetValue(f, out int k) ? k : 0; }
        public float FFARespawnIn(Fighter f) { return ffaRespawnAt.TryGetValue(f, out float t) ? t - Time.time : 0; }
        public int FFAScore(Fighter f) { return Mode == GameMode.Grow ? f.Food.growLevel * 100 + FFAKills(f) : FFAKills(f); }
        public Fighter FFALeader() { Fighter best = null; foreach (var f in fighters) if (best == null || FFAScore(f) > FFAScore(best)) best = f; return best; }
        static WeaponSpec RandomFoodGun() { var a = Config.Arsenal; return a[UnityEngine.Random.Range(1, a.Length)]; }

        static readonly Vector3[] FFASpawns = {
            new Vector3(-64, .05f, 14), new Vector3(-64, .05f, 40), new Vector3(-30, .05f, 39), new Vector3(1, .05f, 41), new Vector3(22, .05f, 41), new Vector3(-52, .05f, 4),
            new Vector3(-20, .05f, 4.5f), new Vector3(10, .05f, 4), new Vector3(20, .05f, 9), new Vector3(16, .05f, -2), new Vector3(-64, .05f, -14), new Vector3(-64, .05f, -40),
            new Vector3(-30, .05f, -40), new Vector3(22, .05f, -41), new Vector3(-21, .05f, 20), new Vector3(-33, .05f, -18), new Vector3(37, .05f, 34), new Vector3(37, .05f, -34),
            new Vector3(51, .05f, 0), new Vector3(-64, .05f, 0) };
        Vector3 FFASpawnFor(Fighter who)
        {
            Vector3 best = FFASpawns[0]; float bestD = -1;
            foreach (var s in FFASpawns)
            {
                float d = 999;
                foreach (var f in fighters) if (f != who && f.alive) d = Mathf.Min(d, Vector3.Distance(f.transform.position, s));
                d += UnityEngine.Random.value * 6f;
                if (d > bestD) { bestD = d; best = s; }
            }
            return best;
        }
        void RespawnFFA(Fighter f)
        {
            f.Respawn(FFASpawnFor(f), UnityEngine.Random.Range(0, 360));
            f.Equip(RandomFoodGun());
            ffaRespawnAt.Remove(f);
        }
        void FFATick(float dt)
        {
            roundClock -= dt;
            foreach (var f in fighters) if (!f.alive && ffaRespawnAt.TryGetValue(f, out float t) && Time.time >= t) RespawnFFA(f);
            if (roundClock <= 0) { roundClock = 0; EndFFA("时间到"); }
        }
        // ---- 抢食大作战
        void CookTick(float dt)
        {
            roundClock -= dt;
            foreach (var f in fighters)
                if (!f.alive && ffaRespawnAt.TryGetValue(f, out float t) && Time.time >= t)
                { f.Respawn(arena.SpawnPoint(f.team == 0, f.slot), f.team == 0 ? 90 : -90); f.Equip(f.Hero.weapon); ffaRespawnAt.Remove(f); }
            if (roundClock <= 0) { roundClock = 0; EndCook("时间到"); }
        }
        public void CookScore(int team, int pts, string msg)
        {
            if (Mode != GameMode.Cook || phase != Phase.Live) return;
            roundsWon[team] = Mathf.Max(0, roundsWon[team] + pts);
            Feed(msg, Config.TeamColor(team));
            Sfx.Ui(pts > 0 ? "confirmation" : "error", .5f, pts > 0 ? 1.2f : .9f);
            if (roundsWon[team] >= CookTarget) EndCook(Config.TeamName(team) + " 先做满 " + CookTarget + " 分");
        }
        void EndCook(string reason)
        {
            if (phase == Phase.MatchEnd) return;
            int w = roundsWon[0] == roundsWon[1] ? -1 : roundsWon[0] > roundsWon[1] ? 0 : 1;
            lastRoundWinner = w; lastRoundReason = reason; lastRoundResult = w < 0 ? "平局" : Config.TeamName(w) + " 获胜";
            foreach (var f in fighters) { f.matchStats.Add(f.stats); f.bombBusy = false; }
            Feed("抢食大作战结束：" + lastRoundResult + "（" + reason + "）", w < 0 ? Color.white : Config.TeamColor(w));
            Debug.Log("NEON_COOK_DONE score=" + roundsWon[0] + ":" + roundsWon[1] + " reason=" + reason + " delivered=" + CookDelivered());
            if (player != null && w == player.team) Collection.AddWin();
            phase = Phase.MatchEnd;
            Sfx.Ui(player != null && w == player.team ? "confirmation" : "error", .6f, .8f);
            if (ui != null) ui.OnMatchEnd();
        }
        string CookDelivered() { var sb = new StringBuilder(); foreach (var f in fighters) sb.Append(f.Hero.name).Append(':').Append(f.Food.delivered).Append('/').Append(f.Food.steals).Append(' '); return sb.ToString().Trim(); }

        void EndFFA(string reason)
        {
            if (phase == Phase.MatchEnd) return;
            var lead = FFALeader();
            if (player != null && lead == player) Collection.AddWin();
            lastRoundResult = (lead != null ? lead.Hero.name : "无人") + " 获胜"; lastRoundReason = reason;
            foreach (var f in fighters) { f.matchStats.Add(f.stats); f.bombBusy = false; }
            Feed("美食大乱斗结束：" + lastRoundResult + "（" + reason + "）", lead != null ? Config.TeamColor(lead.team) : Color.white);
            Debug.Log("NEON_FFA_DONE mode=" + Mode + " winner=" + (lead != null ? lead.Hero.name : "-") + " kills=" + (lead != null ? FFAKills(lead) : 0) + " level=" + (lead != null ? lead.Food.growLevel : 0) + " reason=" + reason + " time=" + (FFATime - roundClock).ToString("0") + " table=" + FFATable());
            phase = Phase.MatchEnd;
            Sfx.Ui(player != null && lead == player ? "confirmation" : "error", .6f, .8f);
            if (ui != null) ui.OnMatchEnd();
        }
        string FFATable() { var sb = new StringBuilder(); foreach (var f in fighters) sb.Append(f.Hero.name).Append(':').Append(FFAKills(f)).Append('/').Append(FFADeaths(f)).Append(' '); return sb.ToString().Trim(); }

        void CreateTeams()
        {
            if (Mode == GameMode.Range)
            {
                // V20：靶场 —— 你 + 5 个敌方靶子（不同干员）
                var all = new List<Role>((Role[])Enum.GetValues(typeof(Role)));
                Spawn(0, !autoplay, Role.Falcon, 0);
                int s = 0; foreach (var r in all) if (r != Role.Falcon && s < 5) Spawn(1, false, r, s++);
                if (player == null) player = fighters[0];
                return;
            }
            if (FFA)
            {
                var all = new List<Role>((Role[])Enum.GetValues(typeof(Role)));
                for (int i = 0; i < all.Count; i++) { int j = UnityEngine.Random.Range(i, all.Count); var t = all[i]; all[i] = all[j]; all[j] = t; }
                int pi = all.IndexOf(Role.Falcon); if (pi > 0) { all[pi] = all[0]; all[0] = Role.Falcon; }
                for (int i = 0; i < 6; i++) Spawn(i, i == 0 && !autoplay, all[i], 0);
                return;
            }
            var blueRoles = new[] { Role.Falcon, Role.Rivet, Role.Raven };
            var redRoles = PickRoles(new List<Role>());
            for (int i = 0; i < 3; i++) Spawn(0, i == 0 && !autoplay, blueRoles[i], i);
            for (int i = 0; i < 3; i++) Spawn(1, false, redRoles[i], i);
        }

        static Role[] PickRoles(List<Role> taken)
        {
            var pool = new List<Role>();
            foreach (Role r in Enum.GetValues(typeof(Role))) if (!taken.Contains(r)) pool.Add(r);
            for (int i = 0; i < pool.Count; i++) { int j = UnityEngine.Random.Range(i, pool.Count); var t = pool[i]; pool[i] = pool[j]; pool[j] = t; }
            return pool.GetRange(0, Mathf.Min(3, pool.Count)).ToArray();
        }

        void Spawn(int team, bool human, Role role, int slot)
        {
            var go = new GameObject((FFA ? "选手" + (team + 1) + " " : team == 0 ? "蓝 " : "红 ") + Config.Hero(role).name);
            go.transform.position = arena.SpawnPoint(team == 0, slot);
            go.AddComponent<CharacterController>();
            var f = go.AddComponent<Fighter>();
            fighters.Add(f);
            f.Init(team, human, role, slot);
            if (human) player = f; else go.AddComponent<Bot>();
        }

        // ------------------------------------------------------------------ flow
        public void StartMatch()
        {
            roundsWon[0] = roundsWon[1] = 0; round = 0; records.Clear(); ffaKills.Clear(); ffaDeaths.Clear(); ffaRespawnAt.Clear();
            if (ToonAssets.Available) foreach (var f in fighters) f.Food.ResetMatch();
            foreach (var f in fighters) { f.matchStats.Clear(); f.credits = Config.StartCredits; f.lossStreak = 0; f.weapon = null; f.alive = false; }
            NextRound();
        }

        void NextRound()
        {
            round++;
            Skills.ClearRound();
            arena.ResetRound();
            Pings.Clear(); UltWarn.Clear();   // V22 第三期
            if (FFA)
            {
                foreach (var f in fighters) { f.alive = false; RespawnFFA(f); }
                BombCarrier = null; bomb = BombState.Carried;
                phase = Phase.Prep; phaseTimer = autoplay ? 1f : 4f; roundClock = Mode == GameMode.Grow ? (autoplay ? 120f : 180f) : autoplay ? 150f : FFATime;
                arena.SetSpawnBarriers(false);
                current = new RoundRecord { round = round, attackers = 0 };
                if (ui != null) ui.OnPrep();
                Feed(Mode == GameMode.Grow ? "吃货变大 · 吃零食和击杀让你变大，时间到时最大的人获胜" : "美食大乱斗 · 6 人各自为战 · 先到 " + FFATarget + " 杀获胜", new Color(1f, .7f, .9f));
                return;
            }
            if (Mode == GameMode.Range)
            {
                // V20：靶场 —— 5 个不同距离 / 护甲等级的靶子（含移动靶），随时开火
                BombCarrier = null; bomb = BombState.Carried; arena.SetSpawnBarriers(false);
                Range.Setup(this);
                phase = Phase.Live; liveStartedAt = Time.time; roundClock = 99999f;
                current = new RoundRecord { round = round, attackers = 0 };
                if (ui != null) ui.OnLive();
                Feed("靶场 · [ ] 换枪 · T 换护甲等级 · Y 重置靶子 · F1 调参 · F2 判定框", new Color(1f, .8f, .5f));
                return;
            }
            if (Mode == GameMode.Cook)
            {
                // V18：抢食大作战 —— 3v3，有复活，把食材送回自己队的大锅
                CookPot.Create(0, new Vector3(-60.5f, 0, 0)); CookPot.Create(1, new Vector3(60.5f, 0, 0));
                foreach (var f in fighters) { f.Respawn(arena.SpawnPoint(f.team == 0, f.slot), f.team == 0 ? 90 : -90); f.Equip(f.Hero.weapon); }
                BombCarrier = null; bomb = BombState.Carried;
                phase = Phase.Prep; phaseTimer = autoplay ? 1f : 4f; roundClock = autoplay ? 150f : FFATime;
                arena.SetSpawnBarriers(false);
                current = new RoundRecord { round = round, attackers = 0 };
                if (ui != null) ui.OnPrep();
                Feed("抢食大作战 · 把地图上的食材送回自己队的大锅 · 凑成一道菜额外 +1 分 · 先到 " + CookTarget + " 分获胜", new Color(1f, .7f, .9f));
                return;
            }
            RerollBots();
            foreach (var f in fighters)
            {
                bool att = f.team == AttackingTeam;
                f.Respawn(arena.SpawnPoint(att, f.slot), att ? 90 : -90);
            }
            if (!testMode) foreach (var f in fighters) if (!f.human) BotBuy(f);
            ResetBomb();
            phase = Phase.Prep; phaseTimer = autoplay ? 2f : Config.PrepTime; roundClock = Config.RoundTime;
            arena.SetSpawnBarriers(true);
            defendersWipedAnnounced = false;
            current = new RoundRecord { round = round, attackers = AttackingTeam };
            stillFrom.Clear(); stillSince.Clear(); stuckFlag.Clear(); rotated.Clear();
            if (ui != null) ui.OnPrep();
            Feed("第 " + round + " 回合 · " + Config.TeamName(AttackingTeam) + "进攻 / " + Config.TeamName(DefendingTeam) + "防守", Color.white);
        }

        // Bots: signature gun if affordable, otherwise the best they can pay for (keep a gun they survived with)
        void BotBuy(Fighter f)
        {
            if (f.W != Config.Pistol) return;
            if (f.Buy(f.Hero.weapon)) return;
            WeaponSpec best = null; int bestPrice = 0;
            foreach (var w in Config.Arsenal)
            {
                int pr = Config.Price(f.Hero, w);
                if (pr <= f.credits && pr > bestPrice && UnityEngine.Random.value < .8f) { best = w; bestPrice = pr; }
            }
            if (best != null) f.Buy(best);
        }

        void ResetBomb()
        {
            if (bombObject != null) Destroy(bombObject.gameObject);
            bomb = BombState.Carried; plantedSite = -1; plantProgress = defuseProgress = 0; planter = defuser = null; bombTimer = Config.BombTimer;
            var atts = fighters.FindAll(f => f.team == AttackingTeam);
            BombCarrier = atts[UnityEngine.Random.Range(0, atts.Count)];
        }

        void RerollBots()
        {
            for (int team = 0; team < 2; team++)
            {
                if (round > 1 && UnityEngine.Random.value < .5f) continue;
                var taken = new List<Role>();
                foreach (var f in fighters) if (f.team == team && f.human) taken.Add(f.role);
                var picks = PickRoles(taken);
                int k = 0;
                foreach (var f in fighters) if (f.team == team && !f.human) f.ChangeRole(picks[k++]);
            }
        }

        public bool RoleTakenByTeammate(Fighter who, Role r)
        {
            foreach (var f in fighters) if (f != who && f.team == who.team && f.role == r) return true;
            return false;
        }

        public void SelectPlayerHero(Role r)
        {
            if (player == null || phase != Phase.Prep || player.role == r) return;
            Role old = player.role;
            foreach (var f in fighters)
                if (f != player && f.team == player.team && f.role == r) { f.ChangeRole(old); Feed("队友 " + f.Hero.name + " 让出了角色", new Color(.7f, .85f, 1f)); }
            player.ChangeRole(r);
            Sfx.Ui("select", .5f);
        }

        void Update()
        {
            if (hitstopUntil > 0 && Time.unscaledTime >= hitstopUntil) { hitstopUntil = 0; if (ui == null || !ui.Blocking) Time.timeScale = 1; }
            float dt = Time.deltaTime;
            if (ui != null && !ui.Started) return;
            switch (phase)
            {
                case Phase.Prep:
                    phaseTimer -= dt;
                    if (phaseTimer <= 0) GoLive();
                    break;
                case Phase.Live:
                    TickLive(dt);
                    break;
                case Phase.RoundEnd:
                    phaseTimer -= dt;
                    if (phaseTimer <= 0)
                    {
                        if (roundsWon[0] >= Config.RoundsToWin || roundsWon[1] >= Config.RoundsToWin) EndMatch();
                        else NextRound();
                    }
                    break;
            }
            for (int i = feed.Count - 1; i >= 0; i--) if (Time.unscaledTime - feed[i].time > 8f) feed.RemoveAt(i);
        }

        void GoLive()
        {
            phase = Phase.Live; liveStartedAt = Time.time;
            arena.SetSpawnBarriers(false);
            if (FFA || Mode == GameMode.Cook) { Feed("开吃！", new Color(1f, .6f, .8f)); Sfx.Ui("bong", .6f); if (ui != null) ui.OnLive(); return; }
            current.blueComp = Comp(0); current.redComp = Comp(1);
            Feed("战斗开始！" + Config.TeamName(AttackingTeam) + "需在 A 或 B 点安装棱能炸弹", new Color(1f, .6f, .3f));
            Sfx.Ui("bong", .6f);
            if (ui != null) ui.OnLive();
        }

        string Comp(int team)
        {
            var sb = new StringBuilder();
            foreach (var f in fighters) if (f.team == team) sb.Append(f.Hero.name).Append(f.human ? "(玩家)" : "").Append(' ');
            return sb.ToString().Trim();
        }

        void TickLive(float dt)
        {
            if (Mode == GameMode.Range) { Range.Tick(this); return; }
            if (FFA) { FFATick(dt); return; }
            if (Mode == GameMode.Cook) { CookTick(dt); return; }
            if (bomb == BombState.Planted)
            {
                bombTimer -= dt;
                if (bombTimer <= 0) { Explode(); return; }
            }
            else
            {
                roundClock -= dt;
                if (roundClock <= 0) { roundClock = 0; EndRound(DefendingTeam, "时间耗尽 · 炸弹未安装"); return; }
                if (bomb == BombState.Dropped)
                    foreach (var f in fighters)
                        if (f.alive && f.team == AttackingTeam && Vector3.Distance(f.transform.position, bombPos) < 1.4f)
                        {
                            bomb = BombState.Carried; BombCarrier = f;
                            Feed(Config.TeamName(f.team) + " " + f.Hero.name + " 捡起了炸弹", new Color(1f, .6f, .3f));
                            if (f.human) Sfx.Ui("confirmation", .5f, 1.3f);
                            break;
                        }
            }
            foreach (var f in fighters) if (f.alive && Arena.SiteAt(f.transform.position) >= 0) f.stats.captureTime += dt;
            MapTelemetryTick();
        }

        // ------------------------------------------------------------------ V22 第二期：地图遥测
        readonly Dictionary<Fighter, Vector3> stillFrom = new Dictionary<Fighter, Vector3>();
        readonly Dictionary<Fighter, float> stillSince = new Dictionary<Fighter, float>();
        readonly HashSet<Fighter> stuckFlag = new HashSet<Fighter>(), rotated = new HashSet<Fighter>();
        float nextMapSample;
        // 北烤串巷 / 南后厨（方案甲新增的两条中段连接）
        public static bool InLink(Vector3 p) { return p.x > -5.5f && p.x < 1.5f && Mathf.Abs(p.z) > 10f && Mathf.Abs(p.z) < 30f; }
        // 门 / 卷帘门 / 蒸笼门 / 墙洞 3 m 内
        public bool NearDoor(Vector3 p, float r = 3f)
        {
            if (arena == null) return false;
            foreach (var s in arena.shutters) if (s != null && FlatDistance(s.transform.position, p) < r + (s.gate ? 1.5f : .5f)) return true;
            foreach (var d in arena.doors) if (d != null && FlatDistance(d.transform.position, p) < r) return true;
            foreach (var d in arena.destructibles) if (d != null && d.name.Contains("墙洞") && FlatDistance(d.transform.position, p) < r) return true;
            return false;
        }
        static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }

        void MapTelemetryTick()
        {
            if (current == null || Time.time < nextMapSample) return;
            nextMapSample = Time.time + .25f;
            if (Planted && current.retake < 0)
                foreach (var f in fighters) if (f.alive && f.team == DefendingTeam && Arena.SiteAt(f.transform.position) == plantedSite) { current.retake = Time.time - liveStartedAt - current.plantTime; break; }
            foreach (var f in fighters)
            {
                if (!f.alive || f.team != AttackingTeam) continue;
                Vector3 p = f.transform.position;
                if (InLink(p) && rotated.Add(f)) current.rotate++;
                // 交火中（3 秒内开过枪 / 挨过打）原地扫射不算卡住；下包后守包也不算
                if (Planted || f.bombBusy || Time.time - f.lastShotAt < 3f || Time.time - f.lastDamageAt < 3f) { stillFrom[f] = p; stillSince[f] = Time.time; stuckFlag.Remove(f); continue; }
                if (!stillFrom.TryGetValue(f, out Vector3 from) || FlatDistance(from, p) > .6f) { stillFrom[f] = p; stillSince[f] = Time.time; stuckFlag.Remove(f); continue; }
                if (!stillSince.TryGetValue(f, out float since)) { stillSince[f] = Time.time; continue; }
                if (Time.time - since >= 5f && !stuckFlag.Contains(f) && NearDoor(p))
                {
                    stuckFlag.Add(f); current.stuck++;
                    if (autoplay) Debug.Log("NEON_STUCK r" + round + " " + f.Hero.name + " pos=" + p.ToString("F1") + " route=" + f.routeName);
                }
            }
        }

        // 蒸笼门 / 蒸汽阀调用
        public void OnGate(bool open) { if (current != null && phase == Phase.Live) { if (open) current.gateOpen++; else current.gateClose++; } }
        public void OnValve() { if (current != null && phase == Phase.Live) current.valves++; }
        public void OnLink(LinkKind k) { if (current != null && phase == Phase.Live) current.links[(int)k]++; }   // V22 第三期
        public void OnPing() { if (current != null && phase == Phase.Live) current.pings++; }

        // Called every frame from Fighter.HandleUse. Returns true when the bomb consumed the E key.
        public bool BombAction(Fighter f, bool held, float dt)
        {
            if (phase != Phase.Live || !f.alive) { Cancel(f); return false; }
            if (bomb == BombState.Planted && f.team == DefendingTeam && held && Vector3.Distance(f.transform.position, bombPos) <= Config.DefuseRange)
            {
                if (defuser != null && defuser != f && defuser.alive && defuser.bombBusy) return false;
                if (defuser != f) { defuser = f; defuseProgress = 0; Sfx.Play("computerNoise", bombPos, 1f, .8f, 1f, 30f); Feed(Config.TeamName(f.team) + " " + f.Hero.name + " 正在拆除炸弹！", new Color(.4f, .8f, 1f)); }
                f.bombBusy = true;
                defuseProgress += dt;
                if (defuseProgress >= Config.DefuseTime) Defused(f);
                return true;
            }
            if (bomb == BombState.Carried && f == BombCarrier && held && f.grounded && Arena.SiteAt(f.transform.position) >= 0)
            {
                if (planter != f) { planter = f; plantProgress = 0; Sfx.Play("switch", f.transform.position, 1f, .6f, 1f, 30f); }
                f.bombBusy = true;
                plantProgress += dt;
                if (Mathf.Repeat(plantProgress, .5f) < dt) Sfx.Play("tick", f.transform.position, .8f, 1.4f, 1f, 25f);
                if (plantProgress >= Config.PlantTime) Plant(f);
                return true;
            }
            Cancel(f);
            return false;
        }

        void Cancel(Fighter f)
        {
            if (f == planter) { planter = null; plantProgress = 0; }
            if (f == defuser) { defuser = null; defuseProgress = 0; }
            f.bombBusy = false;
        }

        void Plant(Fighter f)
        {
            bomb = BombState.Planted; plantedSite = Arena.SiteAt(f.transform.position);
            f.credits = Mathf.Min(Config.MaxCredits, f.credits + Config.PlantCredits);
            bombPos = Skills.GroundAt(f.transform.position + f.transform.forward * .6f);
            bombTimer = Config.BombTimer; planter = null; f.bombBusy = false; BombCarrier = null;
            f.stats.plants++;
            current.plantTime = Time.time - liveStartedAt; current.plantSite = plantedSite == 0 ? "A" : "B";
            var go = new GameObject("棱能炸弹"); go.transform.position = bombPos;
            bombObject = go.AddComponent<BombObject>(); bombObject.Build();
            Feed("⚠ 棱能炸弹已安装在 " + (plantedSite == 0 ? "A" : "B") + " 点！" + Config.BombTimer + " 秒后引爆", new Color(1f, .35f, .2f));
            Sfx.Ui("maximize", .7f, .7f);
            foreach (var b in fighters) if (b.TryGetComponent(out Bot bot)) bot.Hear(f, bombPos, 3f);
        }

        void Defused(Fighter f)
        {
            bomb = BombState.Defused; f.bombBusy = false; f.stats.defuses++;
            if (bombObject != null) bombObject.Defuse();
            Sfx.Play("forceField", bombPos, 1f, .6f, 1f, 60f);
            EndRound(DefendingTeam, "炸弹已拆除");
        }

        void Explode()
        {
            bomb = BombState.Exploded;
            if (ToonAssets.Available)   // V17：生日蛋糕爆炸 —— 奶油和彩带炸满一地
            {
                Vector3 cp = bombPos + Vector3.up * .5f;
                for (int i = 0; i < FoodFx.Confetti.Length; i++) Fx.Burst(cp, Vector3.up, FoodFx.Confetti[i], 40, 14f, .14f, 1.6f, .8f);
                for (int i = 0; i < 26; i++)
                {
                    Vector2 rr = UnityEngine.Random.insideUnitCircle * Config.BombBlastRadius * .7f;
                    if (Physics.Raycast(bombPos + new Vector3(rr.x, 3f, rr.y), Vector3.down, out RaycastHit gh, 6f, ~0, QueryTriggerInteraction.Ignore))
                        FoodFx.Splat(gh.point, gh.normal, i % 3 == 0 ? new Color(1f, .62f, .8f) : new Color(1f, .97f, .92f), UnityEngine.Random.Range(1f, 2.2f));
                }
                Sfx.Play("food_confetti", cp, 1f, .7f, 1f, 200f); Sfx.Play("food_dumpling", cp, 1f, .6f, 1f, 200f);
            }
            if (bombObject != null) Destroy(bombObject.gameObject);
            Vector3 p = bombPos + Vector3.up * .5f;
            Fx.Explosion(p, 6f);
            Fx.Shockwave(p, Config.BombBlastRadius * 1.3f, new Color(1f, .4f, .9f), .8f);
            Fx.Burst(p, Vector3.up, new Color(1f, .3f, .9f), 120, 30, .6f, 1.2f, .1f);
            Fx.Flash(p + Vector3.up * 3, new Color(1f, .4f, .9f), 20, 60, 1.2f);
            Fx.Shake(p, 2f, 120f);
            foreach (var f in fighters)
                if (f.alive)
                {
                    float d = Vector3.Distance(f.Chest, p);
                    if (d < Config.BombBlastRadius) { DamageCtx.Set(DamageKind.Bomb, ToonAssets.FoodText("棱能炸弹") + "爆炸"); f.ReceiveDamage(500 * (1 - d / Config.BombBlastRadius) + 20, null, p, false, true); }
                }
            EndRound(AttackingTeam, "棱能炸弹引爆");
        }

        public void OnKilled(Fighter victim, Fighter killer, bool head)
        {
            string k = killer != null ? killer.Hero.name : "爆炸";
            var item = new FeedItem { text = k + " ▶ " + victim.Hero.name, time = Time.unscaledTime, color = killer != null ? Config.TeamColor(killer.team) : Color.white, kill = true, killer = killer, victim = victim, head = head };
            if (CombatHud.LastHit(victim, out HitRecord kb)) { item.gun = kb.gun; item.cause = kb.weapon ?? ""; item.kind = kb.kind; if (killer == null && kb.kind != DamageKind.Gun) k = kb.weapon; }   // 致命一击的真实来源
            item.text = k + " ▶ " + victim.Hero.name;
            feed.Add(item);
            if (feed.Count > 7) feed.RemoveAt(0);
            if (Mode == GameMode.Range) { Range.OnKilled(victim); return; }
            if (FFA)
            {
                if (killer != null && killer != victim) { ffaKills[killer] = FFAKills(killer) + 1; if (Mode == GameMode.Grow) killer.Food.Grow(2); }
                ffaDeaths[victim] = FFADeaths(victim) + 1;
                ffaRespawnAt[victim] = Time.time + FFARespawn;
                WeaponDrop.Spawn(victim.W, victim.transform.position);
                if (Mode == GameMode.FFA && killer != null && FFAKills(killer) >= FFATarget) EndFFA(killer.Hero.name + " 拿到 " + FFATarget + " 杀");
                if (Mode == GameMode.Grow && killer != null && killer.Food.growLevel >= GrowTarget) EndFFA(killer.Hero.name + " 变到了最大");
                return;
            }
            if (Mode == GameMode.Cook)
            {
                if (killer != null && killer != victim) ffaKills[killer] = FFAKills(killer) + 1;
                ffaDeaths[victim] = FFADeaths(victim) + 1;
                ffaRespawnAt[victim] = Time.time + 4f;
                return;
            }
            if (killer != null && killer.team != victim.team && current != null)
            {
                current.kills[killer.team]++;
                float kd = Vector3.Distance(killer.transform.position, victim.transform.position);
                current.killDist[kd < 15f ? 0 : kd < 40f ? 1 : kd < 60f ? 2 : 3]++;
            }
            if (killer != null && killer.team != victim.team) killer.credits = Mathf.Min(Config.MaxCredits, killer.credits + Config.KillCredits);
            if (victim == BombCarrier && bomb == BombState.Carried)
            {
                bomb = BombState.Dropped; bombPos = Skills.GroundAt(victim.transform.position); BombCarrier = null;
                Feed("炸弹掉落！", new Color(1f, .6f, .3f));
                if (bombObject == null) { var go = new GameObject("掉落的炸弹"); go.transform.position = bombPos; bombObject = go.AddComponent<BombObject>(); bombObject.Build(true); }
            }
            eliminationCheck = true;
        }

        void CheckElimination()
        {
            if (phase != Phase.Live || FFA || Mode == GameMode.Cook || Mode == GameMode.Range) return;
            bool attAlive = false, defAlive = false;
            foreach (var f in fighters) if (f.alive) { if (f.team == AttackingTeam) attAlive = true; else defAlive = true; }
            if (!attAlive && !Planted) EndRound(DefendingTeam, "攻方全灭 · 炸弹未安装");
            else if (!defAlive && !defendersWipedAnnounced)
            {
                defendersWipedAnnounced = true;
                Feed(Planted ? "守方全灭 · 等待炸弹引爆" : "守方全灭 · 攻方必须安装并引爆炸弹才能获胜", new Color(1f, .7f, .3f));
            }
        }

        public void OnHit(Fighter source, Fighter victim)
        {
            if (current != null && current.firstContact < 0 && source != null)
            {
                current.firstContact = Time.time - liveStartedAt;
                Vector3 p = victim.transform.position;
                current.firstContactZone = ContactZone(p);
            }
        }

        // V22 第二期：在旧分类上加 关口 / 连接巷 / 中段十字 / 奶油平台，改前改后可比
        public static string ContactZone(Vector3 p)
        {
            if (InLink(p)) return p.z > 0 ? "北烤串巷" : "南后厨";
            if (Mathf.Abs(p.z) > 30 && p.x > -2 && p.x < 14) return p.z > 0 ? "北关口" : "南关口";
            if (p.z > 30) return "北长街";
            if (p.z < -30) return "南工厂通道";
            if (p.x > 24) return p.z > 14 ? "A点" : p.z < -14 ? "B点" : "中庭";
            if (p.x > 12) return Mathf.Abs(p.z) < 6 ? "中庭" : "连廊";
            if (p.x > -12 && p.x < 4) return "中段十字";
            return "夜市主街";
        }

        void EndRound(int winner, string reason)
        {
            if (phase == Phase.RoundEnd || phase == Phase.MatchEnd) return;
            phase = Phase.RoundEnd; phaseTimer = autoplay ? 2f : Config.RoundEndPause;
            lastRoundWinner = winner; lastRoundReason = reason;
            lastRoundResult = winner < 0 ? "平局" : Config.TeamName(winner) + (winner == AttackingTeam ? "（攻）" : "（守）") + "胜利";
            if (winner >= 0) roundsWon[winner]++;
            foreach (var f in fighters)
            {
                bool won = f.team == winner;
                f.lossStreak = won ? 0 : Mathf.Min(f.lossStreak + 1, 3);
                f.credits = Mathf.Min(Config.MaxCredits, f.credits + (won ? Config.WinCredits : Config.LossCredits + Config.LossStreakBonus * (f.lossStreak - 1)));
            }
            if (autoplay)
                foreach (var f in fighters)
                    Debug.Log("NEON_ROUNDEND r" + round + " " + (f.team == AttackingTeam ? "攻" : "守") + " " + f.Hero.name + " alive=" + f.alive + " pos=" + f.transform.position.ToString("F0") + " route=" + f.routeName + " carrier=" + (BombCarrier == f));
            foreach (var f in fighters) { f.bombBusy = false; }
            if (current.plantSite == "-") foreach (var f in fighters) if (f.alive && f.team == AttackingTeam && NearDoor(f.transform.position)) current.doorEnd++;   // 只统计没下包的回合（下包后守在门边是战术位置）
            current.winner = winner; current.reason = reason; current.duration = Time.time - liveStartedAt;
            var routes = new StringBuilder();
            foreach (var f in fighters)
            {
                f.matchStats.Add(f.stats);
                current.skillUses[(int)f.role] += f.stats.skillUses; current.skillEffective[(int)f.role] += f.stats.skillEffective;
                if (!string.IsNullOrEmpty(f.routeName)) routes.Append(f.Hero.name).Append(':').Append(f.routeName).Append(' ');
            }
            current.routes = routes.ToString().Trim();
            records.Add(current);
            Feed("回合结束：" + lastRoundResult + "（" + reason + "）", winner < 0 ? Color.white : Config.TeamColor(winner));
            if (Config.SwapEveryRound) Feed("下回合攻守互换", new Color(1f, .85f, .3f));
            Sfx.Ui(player != null && winner == player.team ? "confirmation" : "error", .6f, .8f);
            if (ui != null) ui.OnRoundEnd();
        }

        void EndMatch()
        {
            phase = Phase.MatchEnd;
            if (player != null && roundsWon[player.team] >= Config.RoundsToWin) Collection.AddWin();
            WriteTelemetry();
            if (ui != null) ui.OnMatchEnd();
        }

        public void Feed(string text) { Feed(text, new Color(.85f, .9f, 1f)); }
        public void Feed(string text, Color c)
        {
            text = ToonAssets.FoodText(text);
            feed.Add(new FeedItem { text = text, time = Time.unscaledTime, color = c });
            if (feed.Count > 7) feed.RemoveAt(0);
        }

        public void SkillCue(Fighter f)
        {
            if (f == player && ui != null) ui.SkillFlash();
            if (player != null && f.team != player.team && Vector3.Distance(f.transform.position, player.transform.position) < 30f)
                Feed("敌方 " + f.Hero.name + " 使用了「" + f.Hero.skill.Name + "」", new Color(1f, .55f, .5f));
            foreach (var b in fighters) if (b.team != f.team && b.alive) { if (b.TryGetComponent(out Bot bb)) bb.Hear(f, f.transform.position, 1.5f); }
        }

        public void NoiseAt(Fighter source, Vector3 pos)
        {
            foreach (var f in fighters)
                if (f.alive && (source == null || f.team != source.team) && Vector3.Distance(f.transform.position, pos) < 34f)
                    { if (f.TryGetComponent(out Bot fb)) fb.Hear(source, pos, 1f); }
        }

        // ------------------------------------------------------------------ spectator camera
        void LateUpdate()
        {
            if (eliminationCheck) { eliminationCheck = false; CheckElimination(); }
            if (player == null || player.cam == null)
            {
                if (autoplay) OverviewCamera();
                return;
            }
            if (player.alive || phase == Phase.Prep) return;
            Camera cam = player.cam;
            if (cam.transform.parent != null) cam.transform.SetParent(null, true);
            var mates = fighters.FindAll(f => f.alive && f.team == player.team);
            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space)) specIndex++;
            Vector3 targetPos; Quaternion targetRot;
            if (mates.Count > 0)
            {
                var m = mates[specIndex % mates.Count];
                targetRot = Quaternion.Euler(m.pitch * .5f + 12f, m.yaw, 0);
                targetPos = m.transform.position + Vector3.up * 2.1f + targetRot * new Vector3(.6f, 0, -3.4f);
                if (Physics.Linecast(m.transform.position + Vector3.up * 1.8f, targetPos, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
                    targetPos = hit.point + hit.normal * .2f;
            }
            else
            {
                Vector3 focus = Planted ? bombPos : Vector3.zero;
                targetPos = focus + new Vector3(-18, 26, -18); targetRot = Quaternion.LookRotation(focus - targetPos);
            }
            cam.transform.position = Vector3.SmoothDamp(cam.transform.position, targetPos, ref specVel, .12f);
            cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, targetRot, Time.deltaTime * 8f);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 70, Time.deltaTime * 4f);
        }

        void OverviewCamera()
        {
            var cam = Camera.main; if (cam == null) return;
            Fighter focus = null;
            foreach (var f in fighters) if (f.alive && (focus == null || f.transform.position.x > focus.transform.position.x)) focus = f;
            Vector3 center = Planted ? bombPos : focus != null ? focus.transform.position : Vector3.zero;
            cam.transform.position = Vector3.Lerp(cam.transform.position, center + new Vector3(-20, 30, -24), Time.unscaledDeltaTime * 2f);
            cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(center - cam.transform.position), Time.unscaledDeltaTime * 2f);
        }

        // V22 第二期：整场地图指标汇总（设计稿 §6.2），NEON_MAP_SUMMARY 一行给 Agent 读
        public string MapSummary()
        {
            int n = 0, timeouts = 0, a = 0, b = 0, rot = 0, gateRounds = 0, stuck = 0, doorEnd = 0, retN = 0, pings = 0; float ret = 0;
            var kd = new int[4]; var zones = new Dictionary<string, int>(); var lk = new int[6];
            foreach (var r in records)
            {
                n++; if (r.Timeout) timeouts++;
                for (int i = 0; i < 6; i++) lk[i] += r.links[i]; pings += r.pings;
                if (r.plantSite == "A") a++; else if (r.plantSite == "B") b++;
                if (r.rotate > 0) rot++; if (r.gateClose > 0) gateRounds++;
                stuck += r.stuck; doorEnd += r.doorEnd;
                if (r.retake >= 0) { ret += r.retake; retN++; }
                for (int i = 0; i < 4; i++) kd[i] += r.killDist[i];
                zones[r.firstContactZone] = (zones.TryGetValue(r.firstContactZone, out int z) ? z : 0) + 1;
            }
            int kills = kd[0] + kd[1] + kd[2] + kd[3];
            string Pct(int v, int of) { return of > 0 ? (v * 100f / of).ToString("0") + "%" : "-"; }
            var zs = new StringBuilder(); foreach (var kv in zones) zs.Append(kv.Key).Append(':').Append(kv.Value).Append(' ');
            return "rounds=" + n + " timeout=" + timeouts + "(" + Pct(timeouts, n) + ") plantA:B=" + a + ":" + b
                + " kills=" + kills + " near/mid/far/xfar=" + kd[0] + "/" + kd[1] + "/" + kd[2] + "/" + kd[3] + "(" + Pct(kd[0], kills) + "/" + Pct(kd[1], kills) + "/" + Pct(kd[2], kills) + "/" + Pct(kd[3], kills) + ")"
                + " retake=" + (retN > 0 ? (ret / retN).ToString("0.0") : "-") + "s(n=" + retN + ") rotateRounds=" + rot + "(" + Pct(rot, n) + ") gateCloseRounds=" + gateRounds + "(" + Pct(gateRounds, n) + ")"
                + " stuck=" + stuck + " doorEnd=" + doorEnd + " contact=[" + zs.ToString().Trim() + "]"
                + " links(cola/patch/curtain/pot/breach/steam)=" + string.Join("/", lk) + " pings=" + pings;
        }

        // ------------------------------------------------------------------ 试玩记录
        public string WriteTelemetry()
        {
            var sb = new StringBuilder();
            sb.AppendLine("round,attackers,winner,reason,duration_s,plant_site,plant_time_s,first_contact_s,first_contact_zone,blue_kills,red_kills,blue_comp,red_comp,routes,skill_uses(猎隼/铆钉/铁幕/夜鸦/焚芯/幻频),skill_effective,"
                + "kill_near(<15),kill_mid(15-40),kill_far(40-60),kill_xfar(>60),retake_s,rotate_attackers,gate_close,gate_open,stuck,door_end,timeout,valves,"
                + "link_cola,link_patch,link_curtain,link_pot,link_breach,link_steam,pings");
            foreach (var r in records)
                sb.AppendLine(string.Join(",", r.round, Config.TeamName(r.attackers), r.winner < 0 ? "draw" : Config.TeamName(r.winner), r.reason, r.duration.ToString("0.0"),
                    r.plantSite, r.plantTime.ToString("0.0"), r.firstContact.ToString("0.0"), r.firstContactZone, r.kills[0], r.kills[1],
                    r.blueComp, r.redComp, "\"" + r.routes + "\"", string.Join("/", r.skillUses), string.Join("/", r.skillEffective),
                    r.killDist[0], r.killDist[1], r.killDist[2], r.killDist[3], r.retake.ToString("0.0"), r.rotate, r.gateClose, r.gateOpen, r.stuck, r.doorEnd, r.Timeout ? 1 : 0, r.valves,
                    r.links[0], r.links[1], r.links[2], r.links[3], r.links[4], r.links[5], r.pings));
            sb.AppendLine();
            sb.AppendLine("summary," + MapSummary());
            // V20：每个角色的战斗数据（部位命中分布、护甲伤害、穿墙命中、技能使用 / 有效）
            sb.AppendLine();
            sb.AppendLine("fighter,team,weapon,kills,deaths,damage,armor_damage,shots,hits,accuracy,head,chest,stomach,arm,leg,pierced,skill_uses,skill_effective,swallowed_shots,exhaust_count");
            foreach (var f in fighters)
            {
                var st = new Stats(); st.Add(f.matchStats); if (phase != Phase.RoundEnd && phase != Phase.MatchEnd) st.Add(f.stats);
                sb.AppendLine(string.Join(",", f.Hero.name, Config.TeamName(f.team), f.W.id, st.kills, st.deaths, st.damage.ToString("0"), st.armorDamage.ToString("0"), st.shots, st.hits,
                    st.shots > 0 ? (st.hits * 100f / st.shots).ToString("0.0") + "%" : "-", st.headshots, st.chestHits, st.stomachHits, st.armHits, st.legHits, st.pierced, st.skillUses, st.skillEffective, st.swallowedShots, st.exhaustCount));
            }
            string dir = System.IO.Path.Combine(Application.isEditor ? System.IO.Path.GetDirectoryName(Application.dataPath) : Application.persistentDataPath, "Playtest");
            try
            {
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, "playtest_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
                System.IO.File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                Debug.Log("NEON_TELEMETRY " + path);
                return path;
            }
            catch (Exception e) { Debug.LogWarning("Telemetry not written: " + e.Message); return null; }
        }
    }

    // 棱能炸弹模型：倒计时屏、加速蜂鸣、脉冲灯光。
    public class BombObject : MonoBehaviour
    {
        TextMesh timer; Light lamp; float nextBeep; bool dropped, defused; Renderer core;
        public void Build(bool isDropped = false)
        {
            if (ToonAssets.Available) BuildCake(isDropped);
            dropped = isDropped;
            var plate = Resources.Load<Material>("Materials/MetalPlate");
            Part(PrimitiveType.Cube, new Vector3(0, .12f, 0), new Vector3(.55f, .24f, .38f), plate);
            for (int i = -1; i <= 1; i++) Part(PrimitiveType.Cylinder, new Vector3(i * .16f, .3f, -.05f), new Vector3(.11f, .14f, .11f), Arena.Glow(new Color(1f, .3f, .9f), 3f));
            core = Part(PrimitiveType.Cube, new Vector3(0, .25f, .15f), new Vector3(.3f, .12f, .02f), Arena.Glow(new Color(1f, .15f, .1f), 3f)).GetComponent<Renderer>();
            var tgo = new GameObject("倒计时"); tgo.transform.SetParent(transform, false);
            tgo.transform.localPosition = ToonAssets.Available ? new Vector3(0, .95f, 0) : new Vector3(0, .26f, .17f); tgo.transform.localRotation = Quaternion.Euler(0, 180, 0);
            timer = tgo.AddComponent<TextMesh>(); timer.font = Resources.Load<Font>("NotoSansSC"); timer.fontSize = 48; timer.characterSize = .012f;
            timer.anchor = TextAnchor.MiddleCenter; timer.color = new Color(1f, .9f, .8f);
            if (timer.font != null) tgo.GetComponent<MeshRenderer>().sharedMaterial = Arena.WorldText(timer.font);
            lamp = new GameObject("炸弹灯").AddComponent<Light>(); lamp.transform.SetParent(transform, false); lamp.transform.localPosition = Vector3.up * .6f;
            lamp.color = new Color(1f, .2f, .3f); lamp.range = 7; lamp.intensity = 2;
        }
        Transform Part(PrimitiveType t, Vector3 lp, Vector3 s, Material m)
        {
            var o = GameObject.CreatePrimitive(t); Destroy(o.GetComponent<Collider>());
            o.transform.SetParent(transform, false); o.transform.localPosition = lp; o.transform.localScale = s;
            o.GetComponent<Renderer>().sharedMaterial = m; if (ToonAssets.Available) o.GetComponent<Renderer>().enabled = false;   // 卡通模式下只显示蛋糕
            return o.transform;
        }
        // V17：炸弹 = 插着蜡烛的生日蛋糕（拆除 = 吹灭蜡烛）
        readonly List<Light> candles = new List<Light>(); readonly List<GameObject> flames = new List<GameObject>();
        void BuildCake(bool isDropped)
        {
            var cake = Arena.Model("Toon/Food/BigCake");
            if (cake == null) return;
            foreach (var c in cake.GetComponentsInChildren<Collider>()) Destroy(c);
            Arena.Fit(cake, new Vector3(0, .62f, 0)); cake.transform.SetParent(transform, false);
            if (isDropped) return;
            for (int i = 0; i < 5; i++)
            {
                float a = i / 5f * Mathf.PI * 2;
                var fl = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(fl.GetComponent<Collider>());
                fl.transform.SetParent(transform, false); fl.transform.localPosition = new Vector3(Mathf.Cos(a) * .06f, .64f, Mathf.Sin(a) * .06f); fl.transform.localScale = new Vector3(.035f, .06f, .035f);
                fl.GetComponent<Renderer>().sharedMaterial = Arena.Glow(new Color(1f, .65f, .2f), 4f); flames.Add(fl);
            }
            var l = new GameObject("蜡烛光").AddComponent<Light>(); l.transform.SetParent(transform, false); l.transform.localPosition = Vector3.up * .9f;
            l.color = new Color(1f, .7f, .35f); l.range = 4f; l.intensity = 2.5f; candles.Add(l);
        }
        void BlowCandles()
        {
            foreach (var f in flames) if (f != null) f.SetActive(false);
            foreach (var l in candles) if (l != null) l.enabled = false;
            Fx.Burst(transform.position + Vector3.up * .7f, Vector3.up, new Color(.9f, .9f, .95f, .6f), 14, 1.5f, .12f, 1f, -.3f, Fx.Smoke);
        }

        public void Defuse() { if (ToonAssets.Available) BlowCandles(); defused = true; lamp.color = new Color(.3f, 1f, .5f); core.sharedMaterial = Arena.Glow(new Color(.3f, 1f, .5f), 3f); timer.text = "已拆除"; }
        void Update()
        {
            var m = Match.I; if (m == null || defused) return;
            if (dropped)
            {
                if (m.bomb != BombState.Dropped) { Destroy(gameObject); return; }
                timer.text = "掉落"; lamp.intensity = 1 + Mathf.Sin(Time.time * 4); transform.Rotate(0, 30 * Time.deltaTime, 0);
                return;
            }
            float t = m.bombTimer;
            timer.text = t.ToString("0.0");
            float interval = Mathf.Lerp(.12f, 1f, t / Config.BombTimer);
            if (Time.time >= nextBeep) { nextBeep = Time.time + interval; Sfx.Play("tick", transform.position, 1f, t < 10 ? 2f : 1.5f, 1f, 50f); lamp.intensity = 6; }
            lamp.intensity = Mathf.MoveTowards(lamp.intensity, .5f, Time.deltaTime * 20);
        }
    }
}
