using System.Collections.Generic;
using UnityEngine;

namespace NeonSiege
{
    // HUD v2：圆角面板、角色立绘、武器图标、环形进度、包点导航、伤害数字、击杀横幅与屏幕状态效果。
    public class GameUI : MonoBehaviour
    {
        enum Overlay { None, Help, HeroSelect, Scoreboard, Map, Menu, Pause, Settings, Buy }
        Overlay overlay = Overlay.Menu;
        Overlay returnTo = Overlay.Menu;   // where Help / Settings go back to
        public bool Blocking { get { return TuningPanel.Open || overlay == Overlay.Help || overlay == Overlay.Menu || overlay == Overlay.Pause || overlay == Overlay.Settings || overlay == Overlay.HeroSelect && Match.I.phase != Phase.Prep; } }
        Font font;
        GUIStyle tiny, small, normal, large, title, huge, centered, button, wrap;
        Match game;
        string toast; float toastUntil, bannerUntil; string banner, bannerSub; Color bannerColor;
        Texture2D noise, vignette, radial;
        bool started;
        public bool Started { get { return started; } }

        static readonly Color Panel0 = new Color(.03f, .05f, .09f, .82f), Panel1 = new Color(.06f, .1f, .16f, .9f), Line = new Color(.2f, .85f, 1f, .9f);

        void Awake()
        {
            game = Match.I;
            GameSettings.Load();
            font = Resources.Load<Font>("NotoSansSC");
            noise = new Texture2D(96, 54) { filterMode = FilterMode.Point };
            vignette = MakeVignette(256);
            radial = MakeRadial(64);
            if (game.autoplay || game.testMode) { overlay = Overlay.None; started = true; }
            else ShowCursor(true);
            StartCoroutine(Portraits.Build());
            if (Camera.main != null && Camera.main.GetComponent<XRayOverlay>() == null) Camera.main.gameObject.AddComponent<XRayOverlay>();
            if (Camera.main != null && ToonAssets.Available && Camera.main.GetComponent<ToonOutline>() == null) Camera.main.gameObject.AddComponent<ToonOutline>();
            MapUI.Ensure(); IngredientGuide.Ensure();   // V23 b：地图底图 + 食材指引
        }

        static Texture2D MakeVignette(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                {
                    float dx = (x / (n - 1f)) * 2 - 1, dy = (y / (n - 1f)) * 2 - 1;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
                    t.SetPixel(x, y, new Color(1, 1, 1, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.45f, 1f, d))));
                }
            t.Apply(); return t;
        }

        static Texture2D MakeRadial(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                {
                    float dx = (x / (n - 1f)) * 2 - 1, dy = (y / (n - 1f)) * 2 - 1;
                    t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1 - Mathf.Sqrt(dx * dx + dy * dy))));
                }
            t.Apply(); return t;
        }

        public bool HasChineseFont { get { return font != null && font.HasCharacter('霓') && font.HasCharacter('核'); } }
        public string OverlayName { get { return overlay.ToString(); } }
        public int crossDrawn;   // 截图诊断用：准星本帧是否被调用
        public bool HudOverlayOpen { get { return overlay != Overlay.None && overlay != Overlay.Menu; } }   // V22：菜单 / 地图 / 计分板打开时状态带不画

        // ---- V22 第一期：准星与被施放文案的显示判断（静态，PlaytestRunner M31 / M32 / M34 直接调用）
        // 冲刺中 0.2；等待中（CanFireNow 为假）0.3 → 0.5 随进度上升；就绪那一帧跳到 0.95 —— "准星收拢、变实 = 能开枪"
        public static float CrossAlpha(Fighter p) { return p.sprinting ? .2f : !p.CanFireNow ? .3f + .2f * p.FireReadyK : .95f; }
        public static float CrossGapExtra(Fighter p) { return p.sprinting ? 6f : !p.CanFireNow ? 6f * (1f - p.FireReadyK) : 0f; }   // 等待中准星散开 6 px，按进度收拢
        // 美食模式开镜：adsK 0.6 → 0.85 之间腰射准星淡出、美食准镜淡入，两者之和恒为 1，开镜全程屏幕中心都有准星
        public static float FoodReticleK(Fighter p) { return ToonAssets.Available ? Mathf.Clamp01((p.adsK - .6f) / .25f) : 0f; }
        public static float HipCrossK(Fighter p)
        {
            if (ToonAssets.Available) return 1f - FoodReticleK(p);
            if (p.W.sight == "none") return 1f;
            return p.W.sight == "scope" ? (p.aiming ? 0f : 1f - p.adsK) : 1f - p.adsK;   // 写实：红点 / 全息随枪模举起淡出
        }
        public static string RevealText(Fighter p)
        {
            string t = p.RevealedByFireOnly ? "身处燃烧区 · 位置暴露" : "⚠ 你的位置已被侦测，敌方可透墙看到你";
            return ToonAssets.FoodText(t);
        }
        public static string JamText(Fighter p) { return ToonAssets.FoodText("▓ 信号干扰中 ▓"); }
        public static string FeedWeaponLabel(FeedItem it) { return it.gun != null ? it.gun.id : it.cause; }
        public void Toast(string t) { toast = t; toastUntil = Time.unscaledTime + 2.2f; }

        void ShowCursor(bool on) { Cursor.lockState = on ? CursorLockMode.None : CursorLockMode.Locked; Cursor.visible = on; }

        public void OnPrep() { if (game.testMode || game.autoplay) return; if (started && game.player != null) { overlay = game.round <= 1 ? Overlay.HeroSelect : Overlay.Buy; ShowCursor(true); } }
        public void OnLive()
        {
            if (overlay == Overlay.HeroSelect || overlay == Overlay.Buy) Close();
            var p = game.player; if (p == null) return;
            bool att = p.team == game.AttackingTeam;
            // V21：开局横幅按模式写（以前大乱斗 / 抢食 / 变大也显示"进攻 / 防守"）
            FoodGuide.ModeBanner(Match.Mode, att, game.BombCarrier == p, out string bt, out string bs);
            Banner(bt, bs, Match.Mode != GameMode.Bomb ? new Color(1f, .6f, .85f) : att ? new Color(1f, .5f, .2f) : new Color(.3f, .7f, 1f));
        }
        public string BannerText { get { return banner + " · " + bannerSub; } }
        public void ClearBanner() { bannerUntil = 0; }   // 截图脚本用：清掉上一张图残留的开局横幅
        public int helpTab;   // V21：H 帮助页页签 0 操作 · 1 菜谱 · 2 地图美食 · 3 我的英雄
        public static float HowToMinutes = 3f; public static int HowToRounds = 2;
        public bool ShowHowTo { get { return game.phase == Phase.Live && (Match.Mode == GameMode.Bomb ? game.round <= HowToRounds : Time.time - game.liveStartedAt < HowToMinutes * 60f); } }
        public void OnRoundEnd() { }
        public void OnMatchEnd() { overlay = Overlay.None; ShowCursor(true); }
        void Banner(string t, string sub, Color c) { t = ToonAssets.FoodText(t); sub = ToonAssets.FoodText(sub); banner = t; bannerSub = sub; bannerColor = c; bannerUntil = Time.unscaledTime + 3.5f; }
        public void UltBanner(string t, string sub, Color c) { Banner(t, sub, c); }   // V22 第三期：敌方大招预警横幅
        void Close() { overlay = Overlay.None; ShowCursor(false); Sfx.Ui("click", .4f); Time.timeScale = 1; }
        void Pause() { overlay = Overlay.Pause; ShowCursor(true); Time.timeScale = 0; Sfx.Ui("click", .4f, .8f); }
        void Resume() { Close(); }
        void BackToMenu() { Time.timeScale = 1; UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex); }
        void BeginPlay()
        {
            started = true; Sfx.Ui("confirmation", .5f);
            if (game.player != null) game.player.SetViewModelVisible(true);
            overlay = game.phase == Phase.Prep ? Overlay.HeroSelect : Overlay.None;
            ShowCursor(overlay != Overlay.None);
        }

        // Main-menu background: a slow flyover of the live map with the HUD hidden.
        public int preview;   // screenshot harness: 1 main menu, 2 settings
        public void PreviewPause(bool on) { overlay = on ? Overlay.Pause : Overlay.None; }
        public void OpenMap(bool on) { overlay = on ? Overlay.Map : Overlay.None; }   // V23 b：截图 / 测试打开 M 大地图
        void LateUpdate()
        {
            if (preview != 1 && (started || game.autoplay || game.testMode)) return;
            var cam = Camera.main; if (cam == null) return;
            if (game.player != null) game.player.SetViewModelVisible(false);
            float t = Time.unscaledTime * .035f;
            Vector3 pos = new Vector3(Mathf.Cos(t) * 62f, 26f + Mathf.Sin(t * 1.7f) * 4f, Mathf.Sin(t) * 44f);
            cam.transform.position = pos;
            cam.transform.rotation = Quaternion.LookRotation(new Vector3(8, 2, 0) - pos);
            cam.fieldOfView = 55;
        }

        void Update()
        {
            if (game.autoplay || game.testMode) return;
            if (game.phase == Phase.MatchEnd) return;
            if (!started)
            {
                if (Input.GetKeyDown(KeyCode.Escape) && (overlay == Overlay.Help || overlay == Overlay.Settings)) { if (overlay == Overlay.Settings) GameSettings.Save(); overlay = Overlay.Menu; }
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (overlay == Overlay.Pause) Resume();
                else if (overlay == Overlay.Settings) { GameSettings.Save(); overlay = returnTo; }
                else if (overlay == Overlay.Help) { if (returnTo == Overlay.Pause) overlay = Overlay.Pause; else Close(); }
                else if (overlay == Overlay.HeroSelect || overlay == Overlay.Buy) Close();
                else Pause();
            }
            if (Input.GetKeyDown(KeyCode.H) && (overlay == Overlay.None || overlay == Overlay.Scoreboard)) { returnTo = Overlay.None; overlay = Overlay.Help; ShowCursor(true); }
            else if (Input.GetKeyDown(KeyCode.H) && overlay == Overlay.Help && returnTo == Overlay.None) Close();
            if (Input.GetKeyDown(KeyCode.B) && game.phase == Phase.Prep && started)
            { if (overlay == Overlay.Buy) Close(); else { overlay = Overlay.Buy; ShowCursor(true); } }
            if (Input.GetKeyDown(KeyCode.M) && (overlay == Overlay.None || overlay == Overlay.Map)) { overlay = overlay == Overlay.Map ? Overlay.None : Overlay.Map; if (!game.testMode && !game.autoplay) ShowCursor(overlay == Overlay.Map); }   // V23 b：大地图里能点图层按钮
            if (overlay == Overlay.None && Input.GetKey(KeyCode.Tab)) overlay = Overlay.Scoreboard;
            else if (overlay == Overlay.Scoreboard && !Input.GetKey(KeyCode.Tab)) overlay = Overlay.None;
            if (game.player != null && overlay == Overlay.None && Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0)) ShowCursor(false);
            if (game.player != null)
            {
                if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus)) { GameSettings.Sensitivity = Mathf.Min(6f, GameSettings.Sensitivity + .2f); GameSettings.Save(); Toast("鼠标灵敏度 " + GameSettings.Sensitivity.ToString("0.0")); }
                if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus)) { GameSettings.Sensitivity = Mathf.Max(.4f, GameSettings.Sensitivity - .2f); GameSettings.Save(); Toast("鼠标灵敏度 " + GameSettings.Sensitivity.ToString("0.0")); }
            }
            HeartBeat();
            if (game.player != null && game.player.alive)
            {
                int z = Arena.ZoneAt(game.player.transform.position);
                if (z >= 0 && z != zone) { zone = z; zoneShownAt = Time.unscaledTime; }
            }
        }

        float nextBeat, zoneShownAt = -10, skillFlashAt = -10; int zone = -1;
        public void SkillFlash() { skillFlashAt = Time.unscaledTime; }
        void HeartBeat()
        {
            var p = game.player;
            if (p == null || !p.alive || p.hp > 30 || game.phase != Phase.Live) return;
            if (Time.time >= nextBeat) { nextBeat = Time.time + .75f; Sfx.Ui("impactSoft_heavy", .5f, .55f); }
        }

        // ------------------------------------------------------------------ primitives
        void Styles()
        {
            if (normal != null) return;
            tiny = Make(13, new Color(.72f, .82f, .9f));
            small = Make(15, new Color(.82f, .9f, .96f));
            normal = Make(18, Color.white);
            large = Make(24, Color.white, true);
            title = Make(38, new Color(.4f, .95f, 1f), true);
            huge = Make(46, Color.white, true); huge.alignment = TextAnchor.MiddleCenter;
            centered = Make(20, Color.white, true); centered.alignment = TextAnchor.MiddleCenter;
            wrap = Make(15, new Color(.85f, .92f, .97f)); wrap.wordWrap = true; wrap.alignment = TextAnchor.UpperLeft;
            button = new GUIStyle(GUI.skin.label) { font = font, fontSize = 19, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            button.normal.textColor = Color.white;
        }

        GUIStyle Make(int size, Color c, bool bold = false)
        {
            var s = new GUIStyle(GUI.skin.label) { font = font, fontSize = size, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal, alignment = TextAnchor.MiddleLeft };
            s.normal.textColor = c; return s;
        }
        GUIStyle With(GUIStyle s, int size, TextAnchor a) { return new GUIStyle(s) { fontSize = size, alignment = a }; }

        static void Fill(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = o; }
        static void Round(Rect r, Color c, float radius, float border = 0)
        {
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, c, border, radius);
        }
        static void PanelBox(Rect r, float radius = 10, bool accent = true)
        {
            Round(r, Panel0, radius);
            Round(r, new Color(.25f, .7f, .9f, .35f), radius, 1.2f);
            if (accent) Fill(new Rect(r.x + radius, r.y, Mathf.Min(70, r.width - radius * 2), 2), Line);
        }
        void Text(Rect r, string t, GUIStyle s, Color? c = null)
        {
            t = ToonAssets.FoodText(t);
            if (c.HasValue) { var o = s.normal.textColor; s.normal.textColor = c.Value; GUI.Label(r, t, s); s.normal.textColor = o; }
            else GUI.Label(r, t, s);
        }
        void Shadowed(Rect r, string t, GUIStyle s, Color c) { Text(new Rect(r.x + 2, r.y + 2, r.width, r.height), t, s, new Color(0, 0, 0, .75f)); Text(r, t, s, c); }
        bool Button(Rect r, string label, bool primary = true, bool enabled = true)
        {
            bool hover = enabled && r.Contains(Event.current.mousePosition);
            Color bg = !enabled ? new Color(.15f, .17f, .2f, .9f) : primary ? (hover ? new Color(.15f, .75f, .95f) : new Color(.07f, .5f, .7f)) : (hover ? new Color(.2f, .3f, .4f) : new Color(.1f, .16f, .22f));
            Round(r, bg, 8);
            if (primary && enabled) Round(r, new Color(.6f, .95f, 1f, .6f), 8, 1.2f);
            Text(r, label, button, enabled ? Color.white : new Color(.5f, .55f, .6f));
            return enabled && GUI.Button(r, GUIContent.none, GUIStyle.none);
        }
        void Ring(Vector2 c, float radius, float k, Color col, float thick = 5)
        {
            int segs = 48; var m = GUI.matrix;
            for (int i = 0; i < segs; i++)
            {
                float a = i / (float)segs * 360f;
                Color cc = i / (float)segs <= k ? col : new Color(1, 1, 1, .15f);
                CombatHud.RotP(a, c);
                Fill(new Rect(c.x - thick / 2, c.y - radius, thick, radius * .18f), cc);
                GUI.matrix = m;
            }
        }

        // ------------------------------------------------------------------ draw
        void OnGUI()
        {
            if (game == null) return;
            Styles();
            int w = Screen.width, h = Screen.height;
            float s = Mathf.Clamp(h / 1080f, .7f, 1.6f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            float W = w / s, H = h / s;
            Fighter p = game.player;
            if (preview == 1) { MainMenu(W, H); return; }
            if (preview == 2) { Settings(W, H); return; }
            if (preview == 3) { Help(W, H); return; }
            if (!started && !game.autoplay && !game.testMode)
            {
                if (overlay == Overlay.Settings) Settings(W, H);
                else if (overlay == Overlay.Help) Help(W, H);
                else MainMenu(W, H);
                return;
            }

            if (p != null && p.alive) ScreenEffects(W, H, p);
            if (p != null) { WorldMarkers(s, p); NearbyLabels(s, p); }
            TopBar(W);
            if (p != null)
            {
                Minimap(new Rect(16, 16, MiniSize, MiniSize));   // V23 b：圆形局部小地图
                {
                    bool food = ToonAssets.Available; var zc = zone >= 0 ? Arena.Zones[zone].color : new Color(.8f, .8f, .85f);
                    Round(new Rect(16, 22 + MiniSize, MiniSize, 26), food ? new Color(.25f, .08f, .2f, .82f) : new Color(.03f, .05f, .09f, .8f), 8);
                    Round(new Rect(16, 22 + MiniSize, 5, 26), zc, 2);
                    Text(new Rect(28, 22 + MiniSize, MiniSize - 70, 26), zone >= 0 ? "当前位置 · " + MapUI.ZoneDisplayName(zone) : MapUI.MapTitle, With(small, 14, TextAnchor.MiddleLeft), Color.Lerp(zc, Color.white, .35f));
                    Text(new Rect(16 + MiniSize - 64, 22 + MiniSize, 58, 26), "M 大地图", With(tiny, 11, TextAnchor.MiddleRight), new Color(.85f, .8f, .85f));
                }
                ZonePopup(W);
                if (p.alive) { Crosshair(W, H, p); if (GameSettings.DamageNumbers) DamageNumbers(s, p); DamageIndicators(W, H, p); Prompts(W, H, p); }
                else if (game.phase != Phase.Prep) Shadowed(new Rect(W / 2 - 320, H - 230, 640, 30), "你已阵亡 · 本回合不复活 · 正在观战队友（左键切换）", With(centered, 18, TextAnchor.MiddleCenter), new Color(1f, .8f, .7f));
                BottomLeft(H, p);
                BottomRight(W, H, p);
            }
            KillFeed(W);
            PhaseBanner(W, H);
            if (Time.unscaledTime < toastUntil) { Round(new Rect(W / 2 - 280, H * .7f, 560, 40), new Color(0, 0, 0, .65f), 8); Text(new Rect(W / 2 - 280, H * .7f, 560, 40), toast, centered); }
            switch (overlay)
            {
                case Overlay.Help: Help(W, H); break;
                case Overlay.Pause: PauseMenu(W, H); break;
                case Overlay.Settings: Settings(W, H); break;
                case Overlay.HeroSelect: HeroSelect(W, H); break;
                case Overlay.Buy: BuyMenu(W, H); break;
                case Overlay.Scoreboard: Scoreboard(W, H, false); break;
                case Overlay.Map: if (p != null) BigMap(W, H, p); break;   // V23 b
            }
            if (game.phase == Phase.MatchEnd) MatchEnd(W, H);
        }

        void ScreenEffects(float W, float H, Fighter p)
        {
            if (p.Revealed)
            {
                // you have been scanned: red pulsing frame + warning
                float ra = .35f + .25f * Mathf.Sin(Time.time * 9f); bool fire = p.RevealedByFireOnly;   // V22：火里暴露用橙色、独立文案
                var o = GUI.color; GUI.color = fire ? new Color(1f, .45f, .1f, ra * .7f) : new Color(1f, .1f, .12f, ra); GUI.DrawTexture(new Rect(0, 0, W, H), vignette); GUI.color = o;
                Shadowed(new Rect(W / 2 - 240, 130, 480, 28), RevealText(p), With(normal, 18, TextAnchor.MiddleCenter), fire ? new Color(1f, .7f, .35f) : new Color(1f, .4f, .4f));
            }
            float hurt = Mathf.Clamp01(1f - (Time.time - p.lastDamageAt) / .6f);
            float low = p.hp < 35 ? (.35f + .25f * Mathf.Sin(Time.time * 7f)) * (1 - p.hp / 35f) : 0;
            float a = Mathf.Max(hurt * .55f, low);
            if (a > .01f) { var o = GUI.color; GUI.color = new Color(.8f, 0, .05f, a); GUI.DrawTexture(new Rect(0, 0, W, H), vignette); GUI.color = o; }
            if (Time.time - p.lastArmorBreakAt < .5f) { var o = GUI.color; GUI.color = new Color(.3f, .8f, 1f, .5f * (1 - (Time.time - p.lastArmorBreakAt) / .5f)); GUI.DrawTexture(new Rect(0, 0, W, H), vignette); GUI.color = o; }
            if (Time.time < p.jammedUntil)
            {
                for (int i = 0; i < 9; i++)
                {
                    float y = Random.value * H, hh = Random.Range(2f, 14f);
                    Fill(new Rect(Random.Range(-40, 40), y, W, hh), new Color(Random.value < .5f ? .9f : .2f, .2f, 1f, .08f));
                }
                Text(new Rect(W / 2 - 240, 160, 480, 26), JamText(p), With(centered, 18, TextAnchor.MiddleCenter), new Color(.85f, .45f, 1f, .8f));
            }
            if (p.Dashing)
            {
                var m = GUI.matrix;
                for (int i = 0; i < 26; i++)
                {
                    float ang = i * 360f / 26 + Random.Range(-4f, 4f);
                    CombatHud.RotP(ang, new Vector2(W / 2, H / 2));
                    Fill(new Rect(W / 2 - 1, H / 2 - H * .62f, 2, H * Random.Range(.12f, .22f)), new Color(.6f, .85f, 1f, .35f));
                    GUI.matrix = m;
                }
            }
            if (p.ShieldUp) { var o = GUI.color; GUI.color = new Color(.2f, .6f, 1f, .18f); GUI.DrawTexture(new Rect(0, 0, W, H), vignette); GUI.color = o; }
        }

        void WorldMarkers(float s, Fighter p)
        {
            Camera cam = Camera.main; if (cam == null) return;
            bool att = p.team == game.AttackingTeam;
            void Marker(Vector3 world, string label, Color c, bool clampToEdge)
            {
                Vector3 sp = cam.WorldToScreenPoint(world);
                float x = sp.x / s, y = (Screen.height - sp.y) / s;
                float W = Screen.width / s, H = Screen.height / s;
                bool behind = sp.z < 0;
                if (behind) { x = W - x; y = H - 60; }
                if (!clampToEdge && (behind || x < 0 || x > W || y < 0 || y > H)) return;
                x = Mathf.Clamp(x, 40, W - 40); y = Mathf.Clamp(y, 150, H - 170);
                float dist = Vector3.Distance(cam.transform.position, world);
                Round(new Rect(x - 18, y - 18, 36, 36), new Color(c.r * .3f, c.g * .3f, c.b * .3f, .7f), 18);
                Round(new Rect(x - 18, y - 18, 36, 36), c, 18, 2);
                Text(new Rect(x - 18, y - 18, 36, 36), label, With(centered, 17, TextAnchor.MiddleCenter), c);
                Text(new Rect(x - 40, y + 18, 80, 18), Mathf.RoundToInt(dist) + "m", With(tiny, 12, TextAnchor.MiddleCenter));
            }
            if (game.phase == Phase.Live || game.phase == Phase.Prep)
            {
                if (game.Planted) Marker(game.bombPos + Vector3.up * .6f, "炸", new Color(1f, .3f, .2f), true);
                else
                {
                    Marker(Arena.SiteA + Vector3.up * 2, "A", new Color(1f, .5f, .2f), att || p.transform.position.x < 20);
                    Marker(Arena.SiteB + Vector3.up * 2, "B", new Color(.3f, .8f, 1f), att || p.transform.position.x < 20);
                    if (att && game.bomb == BombState.Dropped) Marker(game.bombPos + Vector3.up * .5f, "炸", new Color(1f, .6f, .2f), true);
                }
            }
            foreach (var f in game.fighters)
            {
                if (!f.alive || f == p || f.team != p.team) continue;
                Vector3 sp = cam.WorldToScreenPoint(f.Head + Vector3.up * .45f);
                if (sp.z < 0) continue;
                float x = sp.x / s, y = (Screen.height - sp.y) / s;
                Text(new Rect(x - 60, y - 22, 120, 20), f.Hero.name + (game.BombCarrier == f ? " · 炸弹" : ""), With(tiny, 13, TextAnchor.MiddleCenter), Config.TeamColor(f.team));
                Fill(new Rect(x - 20, y - 2, 40, 3), new Color(0, 0, 0, .5f));
                Fill(new Rect(x - 20, y - 2, 40 * f.hp / 100f, 3), Config.TeamColor(f.team));
            }
            if (!(p.alive && Time.time < p.jammedUntil))
                foreach (var f in game.fighters)
                {
                    if (!f.alive || f.team == p.team || !f.Revealed) continue;
                    Vector3 sp = cam.WorldToScreenPoint(f.Head + Vector3.up * .5f);
                    if (sp.z < 0) continue;
                    float x = sp.x / s, y = (Screen.height - sp.y) / s;
                    float pulse = 1f + .15f * Mathf.Sin(Time.time * 8f), sz = 26 * pulse;
                    Icons.Draw(new Rect(x - sz / 2, y - sz / 2, sz, sz), Icons.Diamond, new Color(1f, .18f, .22f, .95f));
                    float dist = Vector3.Distance(cam.transform.position, f.Chest);
                    Shadowed(new Rect(x - 70, y - 38, 140, 20), "已暴露 · " + f.Hero.name + " · " + Mathf.RoundToInt(dist) + "m", With(small, 13, TextAnchor.MiddleCenter), new Color(1f, .45f, .45f));
                }
        }

        void TopBar(float W)
        {
            float cx = W / 2;
            if (Match.FFA) { FFATopBar(W); return; }
            if (Match.Mode == GameMode.Cook) { CookTopBar(W); return; }
            if (Match.Mode == GameMode.Range) return;   // V20：靶场用右侧的靶场面板
            Rect r = new Rect(cx - 250, 12, 500, 78);
            Round(r, Panel0, 12); Round(r, new Color(.25f, .7f, .9f, .35f), 12, 1.2f);
            Round(new Rect(cx - 250, 12, 120, 78), new Color(Config.Blue.r, Config.Blue.g, Config.Blue.b, .25f), 12);
            Round(new Rect(cx + 130, 12, 120, 78), new Color(Config.Red.r, Config.Red.g, Config.Red.b, .25f), 12);
            Text(new Rect(cx - 250, 14, 120, 50), game.roundsWon[0].ToString(), With(huge, 44, TextAnchor.MiddleCenter));
            Text(new Rect(cx + 130, 14, 120, 50), game.roundsWon[1].ToString(), With(huge, 44, TextAnchor.MiddleCenter));
            Text(new Rect(cx - 250, 60, 120, 24), "蓝队 · " + (game.AttackingTeam == 0 ? "进攻" : "防守"), With(tiny, 13, TextAnchor.MiddleCenter), Config.Blue);
            Text(new Rect(cx + 130, 60, 120, 24), "红队 · " + (game.AttackingTeam == 1 ? "进攻" : "防守"), With(tiny, 13, TextAnchor.MiddleCenter), Config.Red);
            string clock; Color cc = Color.white;
            if (game.phase == Phase.Prep) { clock = Mathf.CeilToInt(game.phaseTimer).ToString(); cc = new Color(.5f, 1f, .95f); }
            else if (game.Planted) { clock = game.bombTimer.ToString("0.0"); cc = Mathf.Repeat(Time.time * (game.bombTimer < 10 ? 4 : 2), 1) < .5f ? new Color(1f, .3f, .25f) : new Color(1f, .7f, .5f); }
            else { int t = Mathf.CeilToInt(game.roundClock); clock = (t / 60) + ":" + (t % 60).ToString("00"); if (t <= 15) cc = new Color(1f, .5f, .4f); }
            Text(new Rect(cx - 130, 12, 260, 52), clock, With(huge, 42, TextAnchor.MiddleCenter), cc);
            string sub = game.phase == Phase.Prep ? "准备阶段 · 第 " + game.round + " 回合" :
                game.Planted ? "炸弹已安装 · " + (game.plantedSite == 0 ? "A" : "B") + " 点" :
                game.bomb == BombState.Dropped ? "炸弹掉落" : "第 " + game.round + " 回合 · 先胜 " + Config.RoundsToWin;
            Text(new Rect(cx - 130, 60, 260, 24), sub, With(tiny, 13, TextAnchor.MiddleCenter), game.Planted ? new Color(1f, .5f, .4f) : new Color(.75f, .85f, .95f));
            int i0 = 0, i1 = 0;
            foreach (var f in game.fighters)
            {
                bool blue = f.team == 0;
                float x = blue ? cx - 250 - 56 - (i0++) * 52 : cx + 250 + 8 + (i1++) * 52;
                Rect pr = new Rect(x, 14, 48, 56);
                Round(pr, new Color(.02f, .04f, .07f, .85f), 8);
                if (Portraits.hero.TryGetValue(f.role, out var tex) && tex != null)
                {
                    var o = GUI.color; GUI.color = f.alive ? Color.white : new Color(.35f, .2f, .2f, .8f);
                    GUI.DrawTextureWithTexCoords(pr, tex, new Rect(.2f, .55f, .6f, .42f)); GUI.color = o;
                }
                else Text(pr, f.Hero.name.Substring(0, 1), With(centered, 20, TextAnchor.MiddleCenter));
                Round(pr, f.alive ? Config.TeamColor(f.team) : new Color(.4f, .2f, .2f), 8, 1.5f);
                Fill(new Rect(x + 4, 66, 40 * (f.alive ? f.hp / 100f : 0), 3), Config.TeamColor(f.team));
                if (!f.alive) Text(pr, "✕", With(centered, 26, TextAnchor.MiddleCenter), new Color(1f, .35f, .35f));
                if (game.BombCarrier == f && game.player != null && f.team == game.player.team) Round(new Rect(x + 34, 16, 12, 12), new Color(1f, .4f, .2f), 6);
            }
        }

        // ================================================================ V23 b：地图 UI（底图 / 图标 / 标签在 MapUI.cs，这里只放外框和布局）
        public const float MiniSize = 240f;
        // Interactable colours（NearbyLabels 世界标签仍在用）
        static readonly Color CShutter = new Color(1f, .6f, .15f), CPower = new Color(1f, .9f, .2f), CMed = new Color(.3f, 1f, .5f), CPad = new Color(.3f, .9f, 1f),
            CBarrel = new Color(1f, .3f, .2f), CCam = new Color(1f, .2f, .5f), CGlass = new Color(.6f, .85f, 1f), CCover = new Color(1f, .78f, .1f), CDoor = new Color(1f, .5f, .1f),
            CSniper = new Color(.95f, .35f, .35f), CDefend = new Color(.45f, .85f, .55f), CPlant = new Color(1f, .8f, .2f), CGate = new Color(1f, .35f, .2f);
        void Minimap(Rect r)
        {
            var p = game.player;
            if (p.alive && Time.time < p.jammedUntil)
            {
                // V21：按干扰来源显示；V23：雪花换成慢慢流动的棉花糖
                string jam = !ToonAssets.Available ? "信号噪幕 · 小地图失效" : NoiseField.Covers(p) ? "棉花糖迷雾 · 小地图被糖糊住了" : "看不清 · 小地图被糊住了";
                MapUI.Jammed(r, jam);
                return;
            }
            MapUI.DrawMinimap(r, p);
        }

        void BigMap(float W, float H, Fighter p)
        {
            Fill(new Rect(0, 0, W, H), new Color(.08f, .03f, .08f, .88f));
            float lw = 290, top = 64, bottom = 70;
            float mh = H - top - bottom, mw = mh * MapBase.SizeX / MapBase.SizeZ;
            if (mw > W - lw - 70) { mw = W - lw - 70; mh = mw * MapBase.SizeZ / MapBase.SizeX; }
            float x0 = (W - (mw + 20 + lw)) / 2;
            Rect mr = new Rect(x0, top, mw, mh);
            Shadowed(new Rect(mr.x, 18, mr.width, 36), MapUI.MapTitle + "  ·  M 关闭", With(centered, 22, TextAnchor.MiddleCenter), ToonAssets.Available ? new Color(1f, .85f, .92f) : Color.white);
            MapUI.DrawBigMap(mr, p);
            MapUI.LayerButtons(new Rect(mr.center.x - 230, mr.yMax + 14, 460, 36), (br, label, on) => Button(br, label, on));
            MapLegend(new Rect(mr.xMax + 20, top, lw, mh));
        }

        void MapLegend(Rect r)
        {
            PanelBox(r, 12);
            Text(new Rect(r.x + 16, r.y + 10, r.width, 30), "地图图例", large);
            var rows = MapUI.LegendRows(game.player);
            float step = Mathf.Min(34, (r.height - 100) / rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                float y = r.y + 52 + i * step;
                MapIcons.Draw(new Rect(r.x + 14, y + 2, 22, 22), rows[i].Item1, rows[i].Item2);
                Text(new Rect(r.x + 44, y, r.width - 52, 26), rows[i].Item3, With(small, 13, TextAnchor.MiddleLeft));
            }
            Text(new Rect(r.x + 16, r.yMax - 34, r.width - 20, 24), "灰色图标 = 冷却中 · 数字 = 剩余秒数", With(tiny, 12, TextAnchor.MiddleLeft));
        }

        void NearbyLabels(float s, Fighter p)
        {
            Camera cam = Camera.main; if (cam == null || !p.alive) return;
            void Label(Vector3 world, string text, Color c)
            {
                float d = Vector3.Distance(cam.transform.position, world);
                if (d > 14f) return;
                Vector3 sp = cam.WorldToScreenPoint(world);
                if (sp.z < 0) return;
                if (Physics.Raycast(cam.transform.position, (world - cam.transform.position).normalized, out RaycastHit hit, d, ~0, QueryTriggerInteraction.Ignore) && hit.distance < d - 1.5f) return;
                float x = sp.x / s, y = (Screen.height - sp.y) / s, a = Mathf.Clamp01((14f - d) / 4f);
                float w = text.Length * 14 + 18;
                Round(new Rect(x - w / 2, y - 12, w, 22), new Color(0, 0, 0, .55f * a), 11);
                Round(new Rect(x - w / 2, y - 12, w, 22), new Color(c.r, c.g, c.b, .8f * a), 11, 1.2f);
                Text(new Rect(x - w / 2, y - 12, w, 22), text, With(tiny, 12, TextAnchor.MiddleCenter), new Color(c.r, c.g, c.b, a));
            }
            foreach (var i in Interactable.All)
            {
                if (i == null) continue;
                if (i is Shutter sh) Label(i.Anchor + Vector3.up * 2.4f, sh.gate ? (sh.open ? "铁门 · 已打开" : "铁门 · 长按 G 开启") : (sh.open ? "卷帘门 · 已升起" : "卷帘门"), sh.gate ? CGate : CShutter);
                else if (i is PowerBox pb) Label(i.Anchor + Vector3.up * .8f, pb.Dark ? "电闸 · 已断电" : "电闸", CPower);
                else if (i is MedStation) Label(i.Anchor + Vector3.up * 1.2f, "医疗站", CMed);
                else if (i is Terminal) Label(i.Anchor + Vector3.up * .8f, "门禁终端", CDoor);
            }
            foreach (var b in Arena.I.barrels) if (b != null && b.GetComponent<Collider>().enabled) Label(b.transform.position + Vector3.up * 1.4f, "爆炸桶", CBarrel);
            foreach (var pad in Object.FindObjectsOfType<JumpPad>()) Label(pad.transform.position + Vector3.up * 1.2f, "弹射板", CPad);
            foreach (var c in Arena.I.cams) if (c != null && c.Working) Label(c.transform.position + Vector3.up * .5f, "监控", CCam);
        }

        void Crosshair(float W, float H, Fighter p)
        {
            crossDrawn++;
            if (overlay != Overlay.None && overlay != Overlay.Menu) return;   // Menu 只在开始前出现；测试 / 截图模式下它会残留，不应挡住准星
            float cx = W / 2, cy = H / 2;
            float spread = p.CurrentSpread();
            float gap = 4 + Mathf.Tan(spread * Mathf.Deg2Rad) * (H / 2) / Mathf.Tan(p.cam.fieldOfView * .5f * Mathf.Deg2Rad) + CrossGapExtra(p);
            Color c = new Color(.9f, 1f, 1f, CrossAlpha(p));   // V22：冲刺转开火 / 拔枪 / 趴下切换期间半透明并散开，就绪那一帧收拢变实
            // AssaultCube-style teammate warning: crosshair turns green over a friendly
            if (Physics.Raycast(p.cam.transform.position, p.cam.transform.forward, out RaycastHit fh, 80f, ~0, QueryTriggerInteraction.Ignore))
            {
                var mate = fh.collider.GetComponentInParent<Fighter>();
                if (mate != null && mate != p && mate.team == p.team && mate.alive)
                { c = new Color(.35f, 1f, .45f, .95f); Shadowed(new Rect(cx - 60, cy + 18, 120, 20), "友军 · " + mate.Hero.name, With(small, 13, TextAnchor.MiddleCenter), c); }
            }
            bool scoped = p.Scoped;
            float hipK = HipCrossK(p);   // V22：开镜过程中腰射准星按 adsK 淡出（美食模式和美食准镜交叉淡入，见 FoodHud）
            if (!ToonAssets.Available && scoped) { ScopeView(W, H, p); hipK = 0; }
            else if (!ToonAssets.Available && p.aiming && p.W.sight == "scope") Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, .6f));   // brief blackout while the optic comes up
            if (hipK > .01f && ToonAssets.Available)
            {
                // V22 打磨：美食模式粉色粗准星（参考图 UI-3）：四段圆角短条 + 中心点，深色描边；透明度 / 收拢沿用 CrossAlpha / CrossGapExtra
                bool friend = c.r < .5f;   // 瞄到队友时上面已经把颜色换成绿色
                Color pc = friend ? new Color(.35f, 1f, .45f, c.a * hipK) : new Color(1f, .42f, .62f, c.a * hipK), ol = new Color(.28f, .04f, .14f, .8f * c.a * hipK);
                float L = 14, T = 5, g = gap + 2;
                Rect[] seg = { new Rect(cx - T / 2, cy - g - L, T, L), new Rect(cx - T / 2, cy + g, T, L), new Rect(cx - g - L, cy - T / 2, L, T), new Rect(cx + g, cy - T / 2, L, T) };
                foreach (var sr in seg) Round(new Rect(sr.x - 1.5f, sr.y - 1.5f, sr.width + 3, sr.height + 3), ol, 4);
                foreach (var sr in seg) Round(sr, pc, 2.5f);
                Round(new Rect(cx - 4.5f, cy - 4.5f, 9, 9), ol, 4.5f); Round(new Rect(cx - 3, cy - 3, 6, 6), pc, 3);
            }
            else if (hipK > .01f)
            {
                c.a *= hipK;
                Color sh = new Color(0, 0, 0, .5f * hipK);
                Fill(new Rect(cx - 1.5f, cy - gap - 10.5f, 3, 11), sh); Fill(new Rect(cx - 1.5f, cy + gap - .5f, 3, 11), sh);
                Fill(new Rect(cx - gap - 10.5f, cy - 1.5f, 11, 3), sh); Fill(new Rect(cx + gap - .5f, cy - 1.5f, 11, 3), sh);
                Fill(new Rect(cx - 1, cy - gap - 10, 2, 10), c); Fill(new Rect(cx - 1, cy + gap, 2, 10), c);
                Fill(new Rect(cx - gap - 10, cy - 1, 10, 2), c); Fill(new Rect(cx + gap, cy - 1, 10, 2), c);
                Fill(new Rect(cx - 1, cy - 1, 2, 2), c);
            }
            float sw = (Time.time - p.swallowedAt) / .15f;   // V22：等待中按开火被吞 → 准星中心闪一个灰点（不出声）
            if (sw >= 0 && sw < 1) { float d = Mathf.Lerp(12, 8, sw); Round(new Rect(cx - d / 2 - 1, cy - d / 2 - 1, d + 2, d + 2), new Color(.15f, .15f, .18f, .7f * (1 - sw)), d / 2 + 1); Round(new Rect(cx - d / 2, cy - d / 2, d, d), new Color(.62f, .62f, .68f, .97f * (1 - sw)), d / 2); }
            if (Time.time < p.hitMarkerUntil)
            {
                Color hc = p.hitMarkerKill ? new Color(1f, .2f, .2f) : p.hitMarkerHead ? new Color(1f, .85f, .2f) : Color.white;
                float punch = 1f + Mathf.Clamp01(1 - (Time.time - p.hitPunchAt) / .12f) * .8f;
                float size = (p.hitMarkerKill ? 18 : p.hitMarkerHead ? 14 : 11) * punch;
                var m = GUI.matrix;
                for (int i = 0; i < 4; i++)
                {
                    CombatHud.RotP(45 + i * 90, new Vector2(cx, cy));
                    Fill(new Rect(cx + 7 * punch, cy - 1.5f, size, 3), hc);
                    GUI.matrix = m;
                }
            }
            if (Time.time - p.armorBreakMarkAt < .7f)
            {
                // Apex-style "shield broken" marker: blue hex that bursts outward and splits.
                float k = (Time.time - p.armorBreakMarkAt) / .7f, sz = Mathf.Lerp(34, 64, Mathf.Sqrt(k));
                Color bc = new Color(.35f, .75f, 1f, 1 - k);
                Icons.Draw(new Rect(cx - sz / 2 - k * 6, cy - sz / 2, sz, sz), Icons.HexRing, bc);
                Icons.Draw(new Rect(cx - sz / 2 + k * 6, cy - sz / 2, sz, sz), Icons.HexRing, new Color(bc.r, bc.g, bc.b, bc.a * .6f));
                Shadowed(new Rect(cx - 80, cy + 30, 160, 22), "护甲击碎", With(centered, 15, TextAnchor.MiddleCenter), bc);
            }
            if (p.IsReloading) Ring(new Vector2(cx, cy), 26, p.ReloadProgress, new Color(.4f, 1f, .9f), 3);
            else if (p.ProneChangeK < 1f) Ring(new Vector2(cx, cy), 26, p.ProneChangeK, new Color(1f, .85f, .55f), 3);   // V22：趴下 / 起身 0.45 s 不能开火，用同一个环表示
            if (Skills.Previewing(p)) Text(new Rect(cx - 200, cy + 44, 400, 26), "松开 F 施放 · 右键取消", With(small, 15, TextAnchor.MiddleCenter));
            if (Time.time < p.killBannerUntil) KillEmblem(W, H, p);
            if (Time.unscaledTime - skillFlashAt < .7f)
            {
                float k = (Time.unscaledTime - skillFlashAt) / .7f, sz = Mathf.Lerp(70, 150, k);
                Icons.Draw(new Rect(cx - sz / 2, cy - sz / 2, sz, sz), Icons.HexRing, new Color(p.Hero.tint.r, p.Hero.tint.g, p.Hero.tint.b, 1 - k));
                Icons.Draw(new Rect(cx - sz * .25f, cy - sz * .25f, sz * .5f, sz * .5f), Icons.Skill(p.role), new Color(1, 1, 1, (1 - k) * .8f));
            }
        }

        // Valorant-style kill emblem: hexagon with victim portrait, skull, headshot mark and round kill count.
        void KillEmblem(float W, float H, Fighter p)
        {
            float t = 2.2f - (p.killBannerUntil - Time.time), a = t > 1.8f ? (2.2f - t) / .4f : 1f;
            float pop = t < .15f ? Mathf.Lerp(1.6f, 1f, t / .15f) : 1f;
            float s = 96 * pop, cx = W / 2, cy = H * .7f;
            Color red = new Color(1f, .25f, .3f, a);
            Icons.Draw(new Rect(cx - s * .75f, cy - s * .75f, s * 1.5f, s * 1.5f), Icons.HexFill, new Color(.25f, .02f, .05f, .55f * a));
            Icons.Draw(new Rect(cx - s * .75f, cy - s * .75f, s * 1.5f, s * 1.5f), Icons.HexRing, red);
            Icons.Draw(new Rect(cx - s * .62f, cy - s * .62f, s * 1.24f, s * 1.24f), Icons.HexRing, new Color(1, 1, 1, .35f * a));
            if (p.lastVictim != null && Portraits.hero.TryGetValue(p.lastVictim.role, out var tex) && tex != null)
            {
                var o = GUI.color; GUI.color = new Color(1, .75f, .75f, a); GUI.DrawTextureWithTexCoords(new Rect(cx - s * .4f, cy - s * .45f, s * .8f, s * .8f), tex, new Rect(.2f, .5f, .6f, .45f)); GUI.color = o;
            }
            Icons.Draw(new Rect(cx - s * .28f, cy + s * .05f, s * .56f, s * .56f), Icons.Skull, new Color(1, 1, 1, .9f * a));
            if (p.lastKillHead) Icons.Draw(new Rect(cx + s * .42f, cy - s * .7f, s * .4f, s * .4f), Icons.Headshot, new Color(1f, .85f, .2f, a));
            int kills = Mathf.Clamp(p.roundKills, 1, 5);
            for (int i = 0; i < kills; i++)
                Icons.Draw(new Rect(cx - kills * 11 + i * 22, cy + s * .78f, 20, 14), Icons.Chevron, red);
            string[] label = { "", "击杀", "双杀", "三杀", "四杀", "王牌" };
            Shadowed(new Rect(cx - 150, cy + s * .78f + 14, 300, 30), label[kills] + " · " + p.killBanner, With(centered, 19, TextAnchor.MiddleCenter), new Color(1, 1, 1, a));
        }

        void SkillIcon(Rect r, Fighter p, bool ready)
        {
            // V22 第三期：瞄着能联动的地图元素时技能图标变金色（只有能联动的英雄会出现）
            bool link = ready && LinkHud.Aim.any && LinkHud.Aim.ok;
            if (link) { float g = 4f + 2f * Mathf.Sin(Time.time * 7f); Icons.Draw(new Rect(r.x - g, r.y - g, r.width + g * 2, r.height + g * 2), Icons.HexRing, new Color(Links.Gold.r, Links.Gold.g, Links.Gold.b, .55f)); }
            Icons.Draw(r, Icons.HexFill, link ? new Color(.45f, .3f, .05f, .95f) : ready ? new Color(p.Hero.tint.r * .35f, p.Hero.tint.g * .35f, p.Hero.tint.b * .35f, .95f) : new Color(.12f, .14f, .17f, .95f));
            Icons.Draw(r, Icons.HexRing, link ? Links.Gold : ready ? p.Hero.tint : new Color(.35f, .38f, .42f));
            Icons.Draw(new Rect(r.x + r.width * .22f, r.y + r.height * .22f, r.width * .56f, r.height * .56f), Icons.Skill(p.role), link ? new Color(1f, .95f, .75f) : ready ? Color.white : new Color(.5f, .55f, .6f));
        }

        void ZonePopup(float W)
        {
            float t = Time.unscaledTime - zoneShownAt;
            if (zone < 0 || t > 2.6f) return;
            float a = t < .2f ? t / .2f : t > 2.1f ? (2.6f - t) / .5f : 1;
            var zn = Arena.Zones[zone];
            float y = 104;
            Fill(new Rect(W / 2 - 220, y + 4, 440, 1.5f), new Color(zn.color.r, zn.color.g, zn.color.b, .7f * a));
            Shadowed(new Rect(W / 2 - 300, y + 8, 600, 34), "◆ " + MapUI.ZoneDisplayName(zone), With(centered, 24, TextAnchor.MiddleCenter), new Color(zn.color.r, zn.color.g, zn.color.b, a));
            Text(new Rect(W / 2 - 300, y + 40, 600, 22), MapUI.ZoneDisplayHint(zone), With(tiny, 13, TextAnchor.MiddleCenter), new Color(.85f, .9f, 1f, a * .9f));
        }

        Texture2D scopeMask;
        // Full-screen magnified optic: clear circular field of view, mil-dot reticle, lens edge, zoom readout.
        void ScopeView(float W, float H, Fighter p)
        {
            if (scopeMask == null)
            {
                int n = 512; scopeMask = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                    {
                        float dx = x / (n - 1f) * 2 - 1, dy = y / (n - 1f) * 2 - 1, d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.9f, .99f, d));
                        float edge = Mathf.Clamp01(1 - Mathf.Abs(d - .9f) / .02f) * .6f;
                        scopeMask.SetPixel(x, y, new Color(0, 0, 0, Mathf.Max(a, edge)));
                    }
                scopeMask.Apply();
            }
            float cx = W / 2, cy = H / 2, size = H * .98f;
            Vector2 sway = new Vector2(Mathf.Sin(Time.time * 1.3f), Mathf.Sin(Time.time * 1.7f)) * (p.crouching ? .6f : 1.5f);
            Rect r = new Rect(cx - size / 2 + sway.x, cy - size / 2 + sway.y, size, size);
            GUI.DrawTexture(r, scopeMask);
            Fill(new Rect(0, 0, r.x + 1, H), Color.black); Fill(new Rect(r.xMax - 1, 0, W - r.xMax + 1, H), Color.black);
            Fill(new Rect(0, 0, W, r.y + 1), Color.black); Fill(new Rect(0, r.yMax - 1, W, H - r.yMax + 1), Color.black);
            var o = GUI.color; GUI.color = new Color(.2f, .6f, .9f, .12f); GUI.DrawTexture(new Rect(r.x + size * .1f, r.y + size * .1f, size * .8f, size * .8f), vignette); GUI.color = o;
            Color ret = new Color(0, 0, 0, .85f), red = new Color(1f, .2f, .2f, .95f);
            Fill(new Rect(r.x + size * .06f, cy - .75f, size * .38f, 1.5f), ret); Fill(new Rect(cx + size * .06f, cy - .75f, size * .38f, 1.5f), ret);
            Fill(new Rect(cx - .75f, cy + size * .06f, 1.5f, size * .38f), ret); Fill(new Rect(cx - .75f, r.y + size * .06f, 1.5f, size * .38f), ret);
            Fill(new Rect(r.x + size * .06f, cy - 3, size * .12f, 6), ret); Fill(new Rect(r.xMax - size * .18f, cy - 3, size * .12f, 6), ret);
            Fill(new Rect(cx - 3, r.yMax - size * .18f, 6, size * .12f), ret);
            for (int i = 1; i <= 4; i++)
            {
                float k = i * size * .05f;
                Fill(new Rect(cx + k - 1, cy - 4, 2, 8), ret); Fill(new Rect(cx - k - 1, cy - 4, 2, 8), ret);
                Fill(new Rect(cx - 4, cy + k - 1, 8, 2), ret);
            }
            Fill(new Rect(cx - 30, cy - .5f, 60, 1), red); Fill(new Rect(cx - .5f, cy - 30, 1, 60), red);
            Round(new Rect(cx - 2.5f, cy - 2.5f, 5, 5), red, 2.5f);
            Text(new Rect(r.xMax - size * .3f, r.yMax - size * .2f, 120, 24), p.W.adsZoom.ToString("0.0") + "x", With(small, 15, TextAnchor.MiddleLeft), new Color(.6f, .9f, 1f, .8f));
            Text(new Rect(r.x + size * .2f, r.yMax - size * .2f, 220, 24), "右键 退出瞄准镜", With(tiny, 13, TextAnchor.MiddleLeft), new Color(.7f, .8f, .9f, .7f));
        }

        void DamageNumbers(float s, Fighter p)
        {
            Camera cam = p.cam; if (cam == null) return;
            for (int i = Fx.numbers.Count - 1; i >= 0; i--)
            {
                var n = Fx.numbers[i];
                float age = Time.time - n.born;
                if (age > .9f) { Fx.numbers.RemoveAt(i); continue; }
                Vector3 sp = cam.WorldToScreenPoint(n.pos + Vector3.up * age * .8f);
                if (sp.z < 0) continue;
                float x = sp.x / s, y = (Screen.height - sp.y) / s;
                float a = 1 - Mathf.Clamp01((age - .5f) / .4f);
                Color c = n.kill ? new Color(1f, .3f, .3f, a) : n.head ? new Color(1f, .85f, .2f, a) : n.armor ? new Color(.45f, .8f, 1f, a) : new Color(1f, .95f, .9f, a);
                int size = Mathf.RoundToInt((n.head || n.kill ? 24 : 18) * (1f + Mathf.Clamp01(1 - age / .12f) * .45f));
                Shadowed(new Rect(x - 40, y - 15, 80, 30), Mathf.RoundToInt(n.amount).ToString(), With(centered, size, TextAnchor.MiddleCenter), c);
            }
        }

        void DamageIndicators(float W, float H, Fighter p)
        {
            if (Time.time - p.lastDamageAt > 1.2f) return;
            float a = 1f - (Time.time - p.lastDamageAt) / 1.2f;
            Vector3 to = p.lastDamageFrom - p.transform.position; to.y = 0;
            float ang = Vector3.SignedAngle(Quaternion.Euler(0, p.yaw, 0) * Vector3.forward, to, Vector3.up);
            var m = GUI.matrix;
            CombatHud.RotP(ang, new Vector2(W / 2, H / 2));
            Round(new Rect(W / 2 - 60, H / 2 - 170, 120, 12), new Color(1f, .15f, .1f, .9f * a), 6);
            Round(new Rect(W / 2 - 25, H / 2 - 186, 50, 10), new Color(1f, .15f, .1f, .6f * a), 5);
            GUI.matrix = m;
        }

        void Prompts(float W, float H, Fighter p)
        {
            var g = game;
            float k = -1; string label = null; Color col = new Color(1f, .65f, .2f);
            bool att = p.team == g.AttackingTeam;
            if (g.Planted && !att && Vector3.Distance(p.transform.position, g.bombPos) <= Config.DefuseRange)
            { label = g.defuser == p && p.bombBusy ? "拆除中…" : "按住 G 拆除炸弹（" + Config.DefuseTime + " 秒）"; k = g.defuser == p ? g.defuseProgress / Config.DefuseTime : 0; col = new Color(.3f, .8f, 1f); }
            else if (g.BombCarrier == p && g.bomb == BombState.Carried)
            {
                if (Arena.SiteAt(p.transform.position) >= 0) { label = p.bombBusy ? "安装中…" : "按住 G 安装炸弹（" + Config.PlantTime + " 秒）"; k = g.planter == p ? g.plantProgress / Config.PlantTime : 0; col = new Color(1f, .4f, .2f); }
                else Text(new Rect(W / 2 - 220, H - 200, 440, 24), "你携带着棱能炸弹 · 进入 A / B 点后按住 G 安装", With(small, 15, TextAnchor.MiddleCenter), new Color(1f, .6f, .35f));
            }
            else
            {
                var near = Interactable.Nearest(p);
                if (near != null) { label = near.Prompt(p); k = near.user == p ? near.progress / near.holdTime : 0; }
                else if (ToonAssets.Available)
                {
                    // V21 规则甲：凑不成菜的第二样食材要按住 G 才捡
                    var ip = IngredientPickup.HoldTarget(p);
                    if (ip != null) { label = "按住 G 硬捡（和你的" + Ingredients.Name(p.Food.ingredients[0]) + "凑不成 → 黑暗料理）"; k = ip.HoldProgress(p); col = new Color(.7f, .7f, .75f); }
                }
            }
            if (label == null) return;
            Vector2 c = new Vector2(W / 2, H / 2 + 132);   // V22：下移，给准星下方的状态带（+62 ~ +110）让位
            if (k > 0) Ring(c, 30, k, col, 4);
            Round(new Rect(c.x - 15, c.y - 15, 30, 30), new Color(.05f, .08f, .12f, .85f), 6);
            Text(new Rect(c.x - 15, c.y - 15, 30, 30), "G", With(centered, 17, TextAnchor.MiddleCenter), col);   // V18：交互键是 G
            float tw = label.Length * 17 + 30;
            Round(new Rect(c.x - tw / 2, c.y + 36, tw, 32), new Color(.03f, .05f, .09f, .8f), 8);
            Text(new Rect(c.x - tw / 2, c.y + 36, tw, 32), label, With(normal, 17, TextAnchor.MiddleCenter));
        }

        void BottomLeft(float H, Fighter p)
        {
            Rect r = new Rect(16, H - 158, 420, 142);
            PanelBox(r, 14);
            Rect pr = new Rect(28, H - 148, 92, 120);
            Round(pr, new Color(p.Hero.tint.r * .25f, p.Hero.tint.g * .25f, p.Hero.tint.b * .25f, .9f), 10);
            if (Portraits.hero.TryGetValue(p.role, out var tex) && tex != null) GUI.DrawTextureWithTexCoords(pr, tex, new Rect(.1f, .38f, .8f, .62f));
            Round(pr, p.Hero.tint, 10, 1.5f);
            Text(new Rect(132, H - 150, 200, 30), p.Hero.name, large, Color.Lerp(p.Hero.tint, Color.white, .35f));
            Text(new Rect(214, H - 150, 120, 30), p.Hero.duty, With(tiny, 13, TextAnchor.MiddleLeft));
            // armor
            Text(new Rect(132, H - 120, 60, 22), "护甲", tiny, new Color(.5f, .85f, 1f));
            for (int i = 0; i < 5; i++)
            {
                float seg = Mathf.Clamp01((p.armor - i * 10) / 10f);
                Rect sr = new Rect(176 + i * 50, H - 115, 46, 10);
                Round(sr, new Color(.12f, .18f, .24f), 3);
                if (seg > 0) Round(new Rect(sr.x, sr.y, sr.width * seg, sr.height), new Color(.4f, .8f, 1f), 3);
            }
            // health
            Color hc = p.hp < 35 ? new Color(1f, .35f, .3f) : new Color(.35f, 1f, .75f);
            Text(new Rect(132, H - 100, 110, 58), Mathf.CeilToInt(p.alive ? p.hp : 0).ToString(), With(huge, 52, TextAnchor.MiddleLeft), hc);
            Rect hb = new Rect(232, H - 84, 194, 14);
            Round(hb, new Color(.12f, .16f, .2f), 5);
            Round(new Rect(hb.x, hb.y, hb.width * Mathf.Clamp01(p.hp / 100f), hb.height), hc, 5);
            Text(new Rect(232, H - 66, 194, 20), "生命 " + Mathf.CeilToInt(p.hp) + " / 100 · 护甲 " + Mathf.CeilToInt(p.armor), tiny);
            // skill tile
            bool ready = p.skillCharges > 0 && game.CanAct;
            Rect q = new Rect(132, H - 44, 290, 24);
            SkillIcon(new Rect(q.x - 2, q.y - 6, 30, 34), p, ready);
            string cd = p.skillCharges < p.Hero.skill.maxCharges && p.skillRechargeAt > 0 ? " · 充能 " + Mathf.CeilToInt(Mathf.Max(0, p.skillRechargeAt - Time.time)) + "s" : "";
            Text(new Rect(q.x + 34, q.y, 200, 24), p.Hero.skill.name + (game.CanAct ? cd : " · 准备阶段锁定"), small, ready ? Color.white : new Color(.6f, .65f, .7f));
            for (int i = 0; i < p.Hero.skill.maxCharges; i++)
            {
                Rect pip = new Rect(q.x + 236 + i * 24, q.y + 6, 20, 12);
                Round(pip, new Color(.15f, .2f, .26f), 4);
                float k = i < p.skillCharges ? 1 : (i == p.skillCharges && p.skillRechargeAt > 0 ? 1 - Mathf.Clamp01((p.skillRechargeAt - Time.time) / p.Hero.skill.cooldown) : 0);
                if (k > 0) Round(new Rect(pip.x, pip.y, pip.width * k, pip.height), i < p.skillCharges ? p.Hero.tint : new Color(p.Hero.tint.r, p.Hero.tint.g, p.Hero.tint.b, .45f), 4);
            }
            // V21：前 2 回合（爆破）/ 前 3 分钟（其他模式）在技能条右边显示怎么用，学会了就收起来
            if (ShowHowTo)
            {
                string how = "F 怎么用：" + p.Hero.skill.HowTo;
                Round(new Rect(444, H - 46, how.Length * 15 + 24, 28), new Color(.03f, .05f, .09f, .7f), 8);
                Text(new Rect(456, H - 46, how.Length * 15 + 12, 28), how, With(small, 14, TextAnchor.MiddleLeft), new Color(.85f, .95f, 1f));
            }
        }

        void BottomRight(float W, float H, Fighter p)
        {
            Rect r = new Rect(W - 436, H - 158, 420, 142);
            PanelBox(r, 14);
            var wpn = p.W;
            var g = GunIcon(p.W); if (g != null) GUI.DrawTexture(new Rect(r.x + 10, r.y + 14, 200, 75), g, ScaleMode.ScaleToFit);
            Text(new Rect(r.x + 16, r.y + 88, 220, 26), wpn.id + "「" + wpn.name + "」", large);
            Text(new Rect(r.x + 16, r.y + 10, 180, 20), "资金 $" + p.credits, With(tiny, 12, TextAnchor.MiddleLeft), new Color(.5f, 1f, .6f));
            Text(new Rect(r.x + 230, r.y + 10, 180, 20), "4 手雷 ×" + p.grenades + "   V 近战", With(tiny, 12, TextAnchor.MiddleRight), p.grenades > 0 ? new Color(1f, .7f, .4f) : new Color(.5f, .55f, .6f));
            Text(new Rect(r.x + 16, r.y + 114, 220, 20), wpn.category + (wpn.burst > 1 ? " · 三连发" : wpn.automatic ? " · 全自动" : " · 半自动"), tiny);
            Color ac = p.ammo <= wpn.magazine / 4 ? new Color(1f, .45f, .3f) : Color.white;
            Text(new Rect(r.x + 220, r.y + 8, 130, 74), p.IsReloading ? "--" : p.ammo.ToString(), With(huge, 60, TextAnchor.MiddleRight), ac);
            Text(new Rect(r.x + 352, r.y + 36, 60, 30), "/" + wpn.magazine, With(normal, 20, TextAnchor.MiddleLeft), new Color(.6f, .7f, .8f));
            int n = Mathf.Min(wpn.magazine, 30);
            float tw = 190f / n;
            for (int i = 0; i < n; i++)
            {
                bool full = i < Mathf.CeilToInt(p.ammo * (float)n / wpn.magazine);
                Fill(new Rect(r.x + 222 + i * tw, r.y + 92, Mathf.Max(1, tw - 2), 12), full ? new Color(.9f, .95f, 1f, .9f) : new Color(.25f, .3f, .35f, .8f));
            }
            Text(new Rect(r.x + 222, r.y + 108, 190, 22), p.IsReloading ? "换弹中…" : p.aiming ? "已开镜 · 右键 退出" : "R 换弹 · 右键 切换开镜", With(tiny, 13, TextAnchor.MiddleLeft), p.aiming ? new Color(.5f, 1f, .9f) : (Color?)null);
        }

        void KillFeed(float W)
        {
            float y = 16;
            for (int i = game.feed.Count - 1; i >= 0 && y < 260; i--)
            {
                var it = game.feed[i];
                float age = Time.unscaledTime - it.time;
                float a = Mathf.Clamp01((8f - age) / .5f);
                if (it.kill && it.victim != null)
                {
                    string killer = it.killer != null ? it.killer.Hero.name : "爆炸";
                    float w = 360;
                    Rect r = new Rect(W - w - 16, y, w, 30);
                    bool mine = game.player != null && (it.killer == game.player || it.victim == game.player);
                    Round(r, new Color(.03f, .05f, .09f, .8f * a), 15);
                    if (mine) Round(r, new Color(1f, .85f, .3f, a), 15, 1.5f);
                    Color kc = it.killer != null ? Config.TeamColor(it.killer.team) : Color.white, vc = Config.TeamColor(it.victim.team);
                    if (it.killer != null && Portraits.hero.TryGetValue(it.killer.role, out var kt) && kt != null) { var o = GUI.color; GUI.color = new Color(1, 1, 1, a); GUI.DrawTextureWithTexCoords(new Rect(r.x + 4, y + 2, 26, 26), kt, new Rect(.25f, .62f, .5f, .35f)); GUI.color = o; }
                    Text(new Rect(r.x + 34, y, 70, 30), killer, With(small, 14, TextAnchor.MiddleLeft), new Color(kc.r, kc.g, kc.b, a));
                    // V22：画致命一击实际用的枪；技能 / 手雷 / 大招 / 环境画图标 + 名字（以前画的是击杀者的招牌枪）
                    var gt = it.gun != null ? GunIcon(it.gun, true) : null;
                    if (gt != null) { var o = GUI.color; GUI.color = new Color(1, 1, 1, a); GUI.DrawTexture(new Rect(r.x + 102, y + 1, 84, 28), gt, ScaleMode.ScaleToFit); GUI.color = o; }
                    else if (it.gun != null) Text(new Rect(r.x + 102, y, 84, 30), it.gun.id, With(tiny, 12, TextAnchor.MiddleCenter), new Color(1f, .9f, .8f, a));
                    else
                    {
                        Icons.Draw(new Rect(r.x + 102, y + 4, 22, 22), CombatHud.CauseIcon(it.kind, it.killer), it.kind == DamageKind.Ult ? new Color(1f, .8f, .3f, a) : new Color(1f, .5f, .2f, a));
                        Text(new Rect(r.x + 125, y, 64, 30), it.cause, With(tiny, 11, TextAnchor.MiddleLeft), new Color(1f, .85f, .7f, a));
                    }
                    if (it.head) Icons.Draw(new Rect(r.x + 190, y + 5, 20, 20), Icons.Headshot, new Color(1f, .85f, .2f, a));
                    Icons.Draw(new Rect(r.x + 214, y + 6, 18, 18), Icons.Skull, new Color(1f, .35f, .35f, a));
                    Text(new Rect(r.x + 236, y, 90, 30), it.victim.Hero.name, With(small, 14, TextAnchor.MiddleRight), new Color(vc.r, vc.g, vc.b, a));
                    if (Portraits.hero.TryGetValue(it.victim.role, out var vt) && vt != null) { var o = GUI.color; GUI.color = new Color(.8f, .6f, .6f, a); GUI.DrawTextureWithTexCoords(new Rect(r.xMax - 30, y + 2, 26, 26), vt, new Rect(.25f, .62f, .5f, .35f)); GUI.color = o; }
                }
                else
                {
                    float w = Mathf.Min(460, it.text.Length * 15 + 30);
                    Rect r = new Rect(W - w - 16, y, w, 28);
                    Round(r, new Color(0, 0, 0, .5f * a), 14);
                    Text(new Rect(r.x + 12, y, w - 20, 28), it.text, With(small, 14, TextAnchor.MiddleLeft), new Color(it.color.r, it.color.g, it.color.b, a));
                }
                y += 34;
            }
        }

        void PhaseBanner(float W, float H)
        {
            if (game.phase == Phase.RoundEnd)
            {
                Rect r = new Rect(W / 2 - 360, H * .24f, 720, 140);
                Color c = game.lastRoundWinner < 0 ? Color.white : Config.TeamColor(game.lastRoundWinner);
                Round(r, new Color(.02f, .04f, .08f, .9f), 16); Round(r, c, 16, 2);
                Fill(new Rect(r.x, r.y + 20, 6, r.height - 40), c);
                Text(new Rect(r.x, r.y + 12, r.width, 64), game.lastRoundResult, With(huge, 50, TextAnchor.MiddleCenter), c);
                Text(new Rect(r.x, r.y + 76, r.width, 28), game.lastRoundReason + "   ·   比分 " + game.roundsWon[0] + " : " + game.roundsWon[1], With(normal, 19, TextAnchor.MiddleCenter));
                Text(new Rect(r.x, r.y + 106, r.width, 22), Config.SwapEveryRound ? "下回合攻守互换 · 按住 Tab 查看数据" : "按住 Tab 查看数据", With(tiny, 13, TextAnchor.MiddleCenter));
                // Round MVP card: highest kills, then damage; plants/defuses count as 2 kills.
                Fighter mvp = null; float best = -1;
                foreach (var f in game.fighters)
                {
                    float sc = f.stats.kills * 100 + f.stats.damage + (f.stats.plants + f.stats.defuses) * 200;
                    if (sc > best) { best = sc; mvp = f; }
                }
                if (mvp != null)
                {
                    Rect m = new Rect(W / 2 - 200, r.yMax + 16, 400, 110);
                    Color mc = Config.TeamColor(mvp.team);
                    Round(m, new Color(.02f, .04f, .08f, .88f), 14); Round(m, new Color(1f, .85f, .3f, .9f), 14, 1.5f);
                    Rect pr = new Rect(m.x + 10, m.y + 10, 72, 90);
                    Round(pr, new Color(mc.r * .3f, mc.g * .3f, mc.b * .3f, 1), 8);
                    if (Portraits.hero.TryGetValue(mvp.role, out var tex) && tex != null) GUI.DrawTextureWithTexCoords(pr, tex, new Rect(.1f, .38f, .8f, .62f));
                    Text(new Rect(m.x + 94, m.y + 8, 300, 26), "本回合 MVP", With(tiny, 13, TextAnchor.MiddleLeft), new Color(1f, .85f, .3f));
                    Text(new Rect(m.x + 94, m.y + 30, 300, 32), mvp.Hero.name + (mvp.human ? "（你）" : "") + " · " + Config.TeamName(mvp.team), With(large, 22, TextAnchor.MiddleLeft), mc);
                    Text(new Rect(m.x + 94, m.y + 66, 300, 24), mvp.stats.kills + " 击杀 · " + mvp.stats.damage.ToString("0") + " 伤害 · " + mvp.stats.headshots + " 爆头" + (mvp.stats.plants + mvp.stats.defuses > 0 ? " · 安装/拆除 " + (mvp.stats.plants + mvp.stats.defuses) : ""), With(small, 14, TextAnchor.MiddleLeft));
                    Icons.Draw(new Rect(m.xMax - 46, m.y + 12, 32, 32), Icons.Skull, new Color(1f, .85f, .3f, .8f));
                }
            }
            else if (game.phase == Phase.Prep && overlay == Overlay.None && game.player != null)
            {
                bool att = game.player.team == game.AttackingTeam;
                string who = Match.Mode == GameMode.Bomb ? (att ? "本回合你是进攻方" : "本回合你是防守方") : Match.ModeName(Match.Mode);   // V21：非爆破模式不写攻守
                Shadowed(new Rect(W / 2 - 360, H * .2f, 720, 40), who + " · 准备 " + Mathf.CeilToInt(game.phaseTimer) + " 秒（B 选英雄）", With(centered, 24, TextAnchor.MiddleCenter), att ? new Color(1f, .6f, .3f) : new Color(.4f, .8f, 1f));
            }
            if (Time.unscaledTime < bannerUntil && game.phase == Phase.Live)
            {
                float k = 1 - (bannerUntil - Time.unscaledTime) / 3.5f;
                float a = k < .1f ? k / .1f : k > .8f ? (1 - k) / .2f : 1;
                Rect r = new Rect(0, H * .2f, W, 110);
                Fill(r, new Color(0, 0, 0, .45f * a));
                Fill(new Rect(0, r.y, W, 2), new Color(bannerColor.r, bannerColor.g, bannerColor.b, a));
                Fill(new Rect(0, r.yMax - 2, W, 2), new Color(bannerColor.r, bannerColor.g, bannerColor.b, a));
                Text(new Rect(0, r.y + 8, W, 60), banner, With(huge, 52, TextAnchor.MiddleCenter), new Color(bannerColor.r, bannerColor.g, bannerColor.b, a));
                Text(new Rect(0, r.y + 66, W, 32), bannerSub, With(normal, 20, TextAnchor.MiddleCenter), new Color(1, 1, 1, a));
            }
        }

        // ------------------------------------------------------------------ overlays
        void Backdrop(float W, float H, float a = .8f)
        {
            Fill(new Rect(0, 0, W, H), new Color(.01f, .02f, .05f, a));
            var o = GUI.color; GUI.color = new Color(.1f, .5f, .8f, .15f); GUI.DrawTexture(new Rect(-W * .2f, -H * .4f, W * .9f, H * 1.2f), radial); GUI.color = new Color(.8f, .1f, .6f, .1f); GUI.DrawTexture(new Rect(W * .4f, H * .1f, W * .9f, H * 1.2f), radial); GUI.color = o;
        }

        void Help(float W, float H)
        {
            Backdrop(W, H);
            float pw = Mathf.Min(1120, W - 40), ph = Mathf.Min(720, H - 40), x = (W - pw) / 2, y = (H - ph) / 2;
            PanelBox(new Rect(x, y, pw, ph), 18);
            if (ToonAssets.Available) { FoodHelp(x, y, pw, ph); return; }
            Text(new Rect(x + 34, y + 18, pw, 50), "霓城封锁 · 棱能爆破", title);
            Text(new Rect(x + 36, y + 64, pw, 24), "NEON LOCKDOWN · 3v3 BOMB DEFUSAL", With(tiny, 13, TextAnchor.MiddleLeft), new Color(.5f, .8f, 1f));
            GUI.Label(new Rect(x + 36, y + 96, pw - 72, 70), ToonAssets.Available ? "攻方带着生日蛋糕，在 A 或 B 点按住 G 放下蛋糕并点燃蜡烛（4 秒），40 秒后蛋糕爆炸即获胜。守方阻止放蛋糕，或在爆炸前按住 G 吹灭蜡烛（6 秒）。打倒所有敌人不会直接结束回合。回合内不复活，每回合攻守互换，先赢 5 回合获胜。地图上的零食按 G 吃掉可以回血。" : "攻方携带棱能炸弹，在 A 或 B 点按住 G 安装（4 秒），炸弹 40 秒后引爆即获胜。守方阻止安装，或在引爆前按住 G 拆除（6 秒）。击杀所有敌人不会直接结束回合——攻方必须真正引爆炸弹。回合内不复活，每回合结束攻守互换，先赢 5 回合获胜。", wrap);
            Fill(new Rect(x + 34, y + 172, pw - 68, 1), new Color(.25f, .5f, .6f));
            Text(new Rect(x + 36, y + 182, 400, 32), "操作", large);
            string[] keys = { "WASD 移动 · 鼠标 瞄准 · 左键 射击 · 右键 切换开镜", "Q / E 左右探头 · Shift 冲刺 · Ctrl/C 蹲伏（冲刺中=滑铲）· Space 跳跃/翻越", "R 换弹 · 4 手雷（按住瞄准）· V 近战（平底锅，能挡正面子弹）· X 大招 · 1/2/3 表情", "F 技能（投掷/部署类：按住预览、松开施放）· G（按住）安装/拆除炸弹、交互", "B 购买武器（准备阶段）· Tab 数据 · M 大地图 · H 帮助 · Esc 暂停", "+ / - 调整鼠标灵敏度" };
            for (int i = 0; i < keys.Length; i++) Text(new Rect(x + 36, y + 220 + i * 30, pw * .48f, 28), keys[i], small);
            Text(new Rect(x + pw * .52f, y + 182, 400, 32), "场景互动", large);
            string[] map = { "卷帘门：A/B 连廊，G 升降，会发出噪音", "电闸：拉闸让包点断电 20 秒（灯光与霓虹全灭）", "爆炸桶：射击后短暂引燃再爆炸，伤害所有人", "橱窗/厂房玻璃：可击碎，打开新视线", "医疗站：每回合一次 +50 生命 +25 护甲", "铁门：南北长街各一道，唯一通路，开门巨响", "弹射板：把你弹上集装箱 / 水塔 / 市场楼顶狙击位", "守方监控：会暴露进攻方位置，可以打掉", "B 点侧门：门禁终端开启 · 黄灯条木箱可被摧毁" };
            for (int i = 0; i < map.Length; i++) Text(new Rect(x + pw * .52f, y + 220 + i * 28, pw * .46f, 26), map[i], With(small, 14, TextAnchor.MiddleLeft));
            Fill(new Rect(x + 34, y + ph - 140, pw - 68, 1), new Color(.25f, .5f, .6f));
            GUI.Label(new Rect(x + 36, y + ph - 130, pw - 72, 46), "生命 100 + 护甲 50（护甲优先扣除）· 子弹有飞行时间，远距离需要提前量 · 夜鸦侦测到的敌人会透墙高亮。", wrap);
            if (Button(new Rect(x + pw / 2 - 160, y + ph - 74, 320, 52), !started ? "返回主菜单" : returnTo == Overlay.Pause ? "返回" : "继续游戏"))
            {
                if (!started) overlay = Overlay.Menu;
                else if (returnTo == Overlay.Pause) overlay = Overlay.Pause;
                else Close();
            }
        }

        // V19：美食模式的帮助页（每行都很短，不会折行重叠）
        void FoodHelp(float x, float y, float pw, float ph)
        {
            Text(new Rect(x + 34, y + 14, pw, 56), "霓城封锁 · 美食大作战", title);
            Text(new Rect(x + 36, y + 68, pw, 24), "当前模式：" + Match.ModeName(Match.Mode), With(tiny, 14, TextAnchor.MiddleLeft), new Color(1f, .75f, .9f));
            string rule = Match.Mode == GameMode.Cook ? "两队把地图上的食材送回自己的大锅：每样 +1 分，凑成一道菜再 +1，也可以去对面锅里偷。先到 20 分获胜，阵亡 4 秒后复活。"
                : Match.Mode == GameMode.Grow ? "6 人各自为战：吃零食 +1 级，击杀 +2 级，阵亡减半。越大越肉、越慢、越显眼，时间到时最大的人获胜。"
                : Match.Mode == GameMode.FFA ? "6 人各自为战：阵亡 3 秒后复活并随机拿一把美食枪，阵亡会掉落手里的枪。先到 15 杀获胜。"
                : "攻方带着生日蛋糕，在 A 或 B 点按住 G 放下蛋糕点蜡烛（4 秒），40 秒后蛋糕爆炸即获胜；守方阻止，或按住 G 吹灭蜡烛（6 秒）。回合内不复活，每回合攻守互换，先赢 5 回合获胜。";
            GUI.Label(new Rect(x + 36, y + 96, pw - 72, 56), rule, wrap);
            // V21：页签（操作 / 菜谱 / 地图美食 / 我的英雄），局内随时能开
            string[] tabs = { "操作", "菜谱", "地图美食", "我的英雄" };
            for (int i = 0; i < tabs.Length; i++)
                if (Button(new Rect(x + 36 + i * 168, y + 158, 160, 38), tabs[i], helpTab == i)) helpTab = i;
            if (Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.RightArrow || Event.current.keyCode == KeyCode.LeftArrow))
                helpTab = (helpTab + (Event.current.keyCode == KeyCode.RightArrow ? 1 : 3)) % 4;
            Fill(new Rect(x + 34, y + 202, pw - 68, 1), new Color(.6f, .35f, .55f));
            float cy = y + 212;
            if (helpTab == 1) HelpRecipes(x, cy, pw);
            else if (helpTab == 2) HelpMapFood(x, cy, pw);
            else if (helpTab == 3) HelpHero(x, cy, pw);
            else
            {
                string[] keys = { "WASD 移动 · 鼠标瞄准 · 左键开火", "右键 开镜（显示美食准镜）", "Shift 冲刺 · Ctrl/C 蹲下（冲刺中滑铲）", "Space 跳跃 / 翻上美食方块", "Q / E 左右探头 · R 换弹",
                    "4 汽水罐手雷（按住瞄准）· V 平底锅", "F 技能 · X 大招 · 1/2/3 表情", "G（按住）交互 · B 买枪 · Tab 数据", "M 大地图 · H 帮助 · Esc 暂停" };
                for (int i = 0; i < keys.Length; i++) Text(new Rect(x + 36, cy + i * 30, pw * .46f, 28), keys[i], With(small, 15, TextAnchor.MiddleLeft));
                Text(new Rect(x + pw * .5f, cy - 4, 300, 34), "新手提示卡", large);
                if (Button(new Rect(x + pw * .5f + 170, cy - 2, 90, 32), "开", GameSettings.Tips)) { GameSettings.Tips = true; if (!game.testMode && !game.autoplay) GameSettings.Save(); }
                if (Button(new Rect(x + pw * .5f + 266, cy - 2, 90, 32), "关", !GameSettings.Tips)) { GameSettings.Tips = false; if (!game.testMode && !game.autoplay) GameSettings.Save(); }
                Text(new Rect(x + pw * .5f, cy + 36, pw * .46f, 24), "提示记录（本次看过的提示）", With(small, 14, TextAnchor.MiddleLeft), new Color(1f, .85f, .6f));
                if (FoodTips.Log.Count == 0) Text(new Rect(x + pw * .5f, cy + 62, pw * .46f, 24), "还没有弹过提示", With(tiny, 13, TextAnchor.MiddleLeft));
                for (int i = 0; i < Mathf.Min(8, FoodTips.Log.Count); i++)
                {
                    string t = FoodTips.Log[FoodTips.Log.Count - 1 - i]; if (t.Length > 34) t = t.Substring(0, 33) + "…";
                    Text(new Rect(x + pw * .5f, cy + 62 + i * 24, pw * .47f, 24), t, With(tiny, 13, TextAnchor.MiddleLeft));
                }
            }
            Fill(new Rect(x + 34, y + ph - 120, pw - 68, 1), new Color(.6f, .35f, .55f));
            Text(new Rect(x + 36, y + ph - 112, pw - 72, 26), "生命 100 + 护甲 50 · 子弹有飞行时间 · ← → 切换页签 · 主菜单里有帽子与菜谱图鉴", With(small, 14, TextAnchor.MiddleLeft), new Color(.85f, .8f, .9f));
            if (Button(new Rect(x + pw / 2 - 160, y + ph - 74, 320, 52), !started ? "返回主菜单" : returnTo == Overlay.Pause ? "返回" : "继续游戏"))
            {
                if (!started) overlay = Overlay.Menu;
                else if (returnTo == Overlay.Pause) overlay = Overlay.Pause;
                else Close();
            }
        }

        void HelpRecipes(float x, float y, float pw)
        {
            for (int i = 0; i < Ingredients.Forms.Length; i++)
            {
                var fm = Ingredients.Forms[i]; bool d = Collection.Discovered(fm);
                Rect r = new Rect(x + 36, y + i * 52, pw - 72, 48);
                Round(r, d ? new Color(.3f, .12f, .25f, .9f) : new Color(.16f, .12f, .18f, .9f), 6);
                Fill(new Rect(r.x, r.y, 6, r.height), Ingredients.FormColor(fm));
                Text(new Rect(r.x + 16, r.y + 2, 520, 26), Ingredients.FormName(fm) + "  =  " + Ingredients.FormRecipe(fm), With(normal, 17, TextAnchor.MiddleLeft), Ingredients.FormColor(fm));
                Text(new Rect(r.x + 16, r.y + 24, r.width - 200, 22), Ingredients.FormEffect(fm) + " · " + Ingredients.FormTime(fm).ToString("0") + " 秒", With(tiny, 13, TextAnchor.MiddleLeft), new Color(.9f, .88f, .92f));
                Text(new Rect(r.xMax - 170, r.y, 160, 48), d ? "★ 已做过" : "还没做过", With(small, 14, TextAnchor.MiddleRight), d ? new Color(1f, .85f, .35f) : new Color(.6f, .6f, .65f));
            }
        }
        void HelpMapFood(float x, float y, float pw)
        {
            string[] map = { "闪光圈 + 头顶「G」气泡 = 可以交互，灰色 = 现在用不了", "零食：按住 G 吃掉，+25 生命（猎隼 +50；变大模式 +1 级）", "火锅回血炉：每秒 +10，最多 60，冷却 30 秒；热气暴露你",
                "奶茶机补弹药（冷却 40 秒）· 烤箱伤害 +15%（20 秒）", "冰柜：+50 冰护甲，稍微变慢（冷却 60 秒）", "食材：两样凑成一道菜就变身；凑不成的要按住 G 才捡",
                "外卖箱：随机变身 + 补满弹药 + 金黄酥脆", "果冻弹床弹你上高处 · 传菜转盘会转", "巧克力酱减速 · 黄油打滑 · 地图火锅会烫、热气挡视线", "蛋糕墙能打穿；奶油裱花（BR-5）打它是补墙",
                "金色 ★ = 你的技能能在这里联动 · 中键标记（敌人 / 这里 / 能联动）" };
            for (int i = 0; i < map.Length; i++) Text(new Rect(x + 36, y + i * 30, pw * .5f, 28), map[i], With(small, 14, TextAnchor.MiddleLeft));
            Text(new Rect(x + pw * .56f, y - 4, 400, 34), "6 种食物反应", large);
            for (int i = 0; i < FoodGuide.Reactions.Length; i++) Text(new Rect(x + pw * .56f, y + 36 + i * 34, pw * .42f, 30), FoodGuide.Reactions[i], With(small, 14, TextAnchor.MiddleLeft), new Color(1f, .9f, .75f));
        }
        void HelpHero(float x, float y, float pw)
        {
            var p = game.player; if (p == null) { Text(new Rect(x + 36, y, 600, 30), "进入对局后这里显示你当前英雄的技能、被动和大招", normal); return; }
            var h = p.Hero; var sk = h.skill;
            Text(new Rect(x + 36, y - 4, 600, 36), h.name + " · " + h.duty, With(large, 26, TextAnchor.MiddleLeft), Color.Lerp(h.tint, Color.white, .3f));
            float lx = x + 36, lw = pw * .5f - 40;
            Text(new Rect(lx, y + 36, lw, 26), "F「" + sk.Name + "」", With(normal, 18, TextAnchor.MiddleLeft), h.tint);
            string[] rows = { "目的：" + sk.Purpose, "规则：" + sk.Rule, "数值：" + sk.Numbers, "反制：" + sk.Counter, "怎么用：" + sk.HowTo };
            var ws = new GUIStyle(wrap) { fontSize = 13 }; float ry = y + 66;
            foreach (var r in rows) { float hh = ws.CalcHeight(new GUIContent(r), lw); GUI.Label(new Rect(lx, ry, lw, hh), r, ws); ry += hh + 6; }
            float rx = x + pw * .53f, rw = pw * .44f;
            Text(new Rect(rx, y + 36, rw, 26), "被动（一直生效）", With(normal, 18, TextAnchor.MiddleLeft), new Color(1f, .85f, .5f));
            GUI.Label(new Rect(rx, y + 66, rw, 40), FoodState.Passive(p.role), new GUIStyle(wrap) { fontSize = 15 });
            Text(new Rect(rx, y + 120, rw, 26), "X 大招「" + FoodState.UltName(p.role) + "」", With(normal, 18, TextAnchor.MiddleLeft), new Color(1f, .6f, .85f));
            GUI.Label(new Rect(rx, y + 150, rw, 44), FoodGuide.UltEffect(p.role), new GUIStyle(wrap) { fontSize = 15 });
            Text(new Rect(rx, y + 200, rw, 24), "充能：" + FoodGuide.UltCharge, With(tiny, 13, TextAnchor.MiddleLeft));
            Text(new Rect(rx, y + 226, rw, 24), "当前：" + Mathf.FloorToInt(p.Food.ult * 100) + "%", With(tiny, 13, TextAnchor.MiddleLeft), new Color(1f, .85f, .3f));
            // V22 第三期：地图联动
            var lk = Links.For(p.role);
            Round(new Rect(rx - 8, y + 250, rw + 16, 136), new Color(.25f, .16f, .03f, .85f), 8); Round(new Rect(rx - 8, y + 250, rw + 16, 136), Links.Gold, 8, 1.5f);
            Text(new Rect(rx, y + 252, rw, 24), "★ 地图联动「" + Links.Name(lk) + "」 " + Links.Combo(p.role), With(normal, 15, TextAnchor.MiddleLeft), Links.Gold);
            var lw2 = new GUIStyle(wrap) { fontSize = 12 }; string rule = "规则：" + Links.HelpRule(p.role), counter = "反制：" + Links.HelpCounter(p.role);
            float rh = lw2.CalcHeight(new GUIContent(rule), rw); GUI.Label(new Rect(rx, y + 278, rw, rh), rule, lw2);
            GUI.Label(new Rect(rx, y + 280 + rh, rw, 34), counter, lw2);
        }
        public static string HeroLinkText(Role r) { return Links.Name(Links.For(r)) + "：" + Links.HelpRule(r) + " 反制：" + Links.HelpCounter(r); }   // V22 第三期：测试读 H 页联动说明

        void HeroSelect(float W, float H)
        {
            var p = game.player; if (p == null) return;
            Backdrop(W, H, .75f);
            float pw = Mathf.Min(1400, W - 30), ph = Mathf.Min(800, H - 30), x = (W - pw) / 2, y = (H - ph) / 2;
            Text(new Rect(x + 10, y, pw, 50), "选择英雄", title);
            bool att = p.team == game.AttackingTeam;
            Text(new Rect(x + 210, y + 8, pw - 220, 36), (Match.Mode == GameMode.Bomb ? (att ? "本回合：进攻方" : "本回合：防守方") : Match.ModeName(Match.Mode)) + "   ·   准备剩余 " + Mathf.CeilToInt(Mathf.Max(0, game.phaseTimer)) + " 秒 · 同队不可重复", normal, att ? new Color(1f, .6f, .3f) : new Color(.4f, .8f, 1f));
            float gap = 14, cw = (pw - gap * 5) / 6f, ch = ph - 140;
            for (int i = 0; i < 6; i++)
            {
                var hero = Config.Heroes[i];
                float cx = x + i * (cw + gap), cy = y + 60;
                bool mine = p.role == hero.role;
                Fighter mate = null; foreach (var f in game.fighters) if (f != p && f.team == p.team && f.role == hero.role) mate = f;
                Rect card = new Rect(cx, cy, cw, ch);
                bool hover = card.Contains(Event.current.mousePosition);
                Round(card, mine ? new Color(.06f, .2f, .3f, .97f) : hover ? new Color(.07f, .13f, .2f, .97f) : new Color(.04f, .08f, .13f, .95f), 14);
                Round(card, mine ? hero.tint : new Color(hero.tint.r, hero.tint.g, hero.tint.b, .35f), 14, mine ? 2.5f : 1.2f);
                Rect art = new Rect(cx + 6, cy + 6, cw - 12, ch * .46f);
                Round(art, new Color(hero.tint.r * .2f, hero.tint.g * .2f, hero.tint.b * .2f, 1), 10);
                if (Portraits.hero.TryGetValue(hero.role, out var tex) && tex != null) GUI.DrawTexture(art, tex, ScaleMode.ScaleAndCrop);
                Fill(new Rect(art.x, art.yMax - 46, art.width, 46), new Color(0, 0, 0, .55f));
                Text(new Rect(art.x + 12, art.yMax - 46, art.width, 30), hero.name, With(large, 26, TextAnchor.MiddleLeft), Color.Lerp(hero.tint, Color.white, .35f));
                Text(new Rect(art.x + 12, art.yMax - 20, art.width, 18), hero.duty + " · " + (i + 1), With(tiny, 12, TextAnchor.MiddleLeft));
                if (mate != null) { Round(new Rect(art.xMax - 92, art.y + 8, 84, 22), new Color(0, 0, 0, .7f), 11); Text(new Rect(art.xMax - 92, art.y + 8, 84, 22), "队友已选", With(tiny, 12, TextAnchor.MiddleCenter), new Color(1f, .8f, .5f)); }
                float ty = art.yMax + 8;
                if (Portraits.gun.TryGetValue(hero.role, out var gtex) && gtex != null) GUI.DrawTexture(new Rect(cx + 10, ty, cw - 20, 52), gtex, ScaleMode.ScaleToFit);
                var wp = hero.weapon;
                Text(new Rect(cx + 12, ty + 36, cw - 20, 18), "招牌武器 7 折", With(tiny, 11, TextAnchor.MiddleLeft), new Color(1f, .8f, .4f));
                Text(new Rect(cx + 12, ty + 52, cw - 20, 22), wp.id + "「" + wp.name + "」", With(normal, 16, TextAnchor.MiddleLeft));
                Text(new Rect(cx + 12, ty + 72, cw - 20, 20), wp.category + " · " + wp.bodyDamage + (wp.pellets > 1 ? "×" + wp.pellets : "") + " 伤 · " + wp.rpm + " RPM", With(tiny, 12, TextAnchor.MiddleLeft));
                Text(new Rect(cx + 12, ty + 90, cw - 20, 20), "TTK " + wp.TheoreticalTtk.ToString("0.00") + "s（" + wp.ShotsToKill + " 发 · 含护甲）", With(tiny, 12, TextAnchor.MiddleLeft), new Color(.6f, 1f, .9f));
                Bars(new Rect(cx + 12, ty + 112, cw - 24, 44), wp);
                Icons.Draw(new Rect(cx + 10, ty + 158, 26, 26), Icons.HexFill, new Color(hero.tint.r * .3f, hero.tint.g * .3f, hero.tint.b * .3f, 1));
                Icons.Draw(new Rect(cx + 10, ty + 158, 26, 26), Icons.HexRing, hero.tint);
                Icons.Draw(new Rect(cx + 15, ty + 163, 16, 16), Icons.Skill(hero.role), Color.white);
                Text(new Rect(cx + 40, ty + 160, cw - 40, 22), "F「" + hero.skill.Name + "」", With(normal, 16, TextAnchor.MiddleLeft), hero.tint);
                GUI.Label(new Rect(cx + 12, ty + 182, cw - 24, 112), HeroCardText(hero), new GUIStyle(wrap) { fontSize = 12 });
                bool canPick = game.phase == Phase.Prep;
                if (Button(new Rect(cx + 10, cy + ch - 50, cw - 20, 40), mine ? "当前英雄" : mate != null ? "交换" : "选择", !mine, canPick && !mine))
                    game.SelectPlayerHero(hero.role);
            }
            if (Button(new Rect(x + pw / 2 - 170, y + ph - 64, 340, 50), game.phase == Phase.Prep ? "确定 · 选择武器" : "关闭")) { if (game.phase == Phase.Prep) overlay = Overlay.Buy; else Close(); }
        }

        // V21：选英雄卡补上被动和大招（美食模式）
        public static string HeroCardText(HeroSpec hero)
        {
            if (!ToonAssets.Available) return hero.skill.Purpose + "\n反制：" + hero.skill.Counter;
            return hero.skill.Purpose + "\n被动：" + FoodState.Passive(hero.role) + "\nX「" + FoodState.UltName(hero.role) + "」" + FoodGuide.UltEffect(hero.role);
        }

        public static Texture2D GunIcon(WeaponSpec w, bool exact = false)
        {
            if (w != null && Portraits.gunById.TryGetValue(w.id, out var e) && e != null) return e;   // V22：按枪 id 取（含免费手枪）
            foreach (var h in Config.Heroes) if (h.weapon == w && Portraits.gun.TryGetValue(h.role, out var t)) return t;
            if (exact) return null;
            return Portraits.gun.TryGetValue(Role.Rivet, out var d) ? d : null;
        }

        void BuyMenu(float W, float H)
        {
            var p = game.player; if (p == null) return;
            Backdrop(W, H, .75f);
            float pw = Mathf.Min(1400, W - 30), ph = Mathf.Min(720, H - 30), x = (W - pw) / 2, y = (H - ph) / 2;
            Text(new Rect(x + 10, y, pw, 50), "选择武器", title);
            Text(new Rect(x + 230, y + 8, pw - 240, 36), "资金 $" + p.credits + "   ·   准备剩余 " + Mathf.CeilToInt(Mathf.Max(0, game.phaseTimer)) + " 秒   ·   存活到回合结束可保留武器", normal, new Color(.5f, 1f, .6f));
            var list = Config.Arsenal;
            float gap = 12, cw = (pw - gap * (list.Length - 1)) / list.Length, ch = ph - 150;
            for (int i = 0; i < list.Length; i++)
            {
                var w = list[i];
                float cx = x + i * (cw + gap), cy = y + 60;
                bool owned = p.W == w, sig = Config.Signature(p.Hero, w);
                int price = Config.Price(p.Hero, w); bool afford = p.credits >= price;
                Rect card = new Rect(cx, cy, cw, ch);
                if (ToonAssets.Available)   // V17：美食街菜单 —— 每把枪是一张菜品卡
                {
                    var th = FoodFx.For(w);
                    Round(card, owned ? new Color(.35f, .12f, .3f, .97f) : new Color(.22f, .08f, .2f, .95f), 16);
                    Round(new Rect(cx, cy, cw, 86), new Color(th.splat.r, th.splat.g, th.splat.b, .35f), 16);
                    Round(card, owned ? new Color(1f, .6f, .85f) : sig ? new Color(1f, .8f, .35f, .9f) : new Color(1f, .7f, .85f, .35f), 16, owned ? 2.5f : 1.2f);
                }
                else
                {
                    Round(card, owned ? new Color(.06f, .2f, .3f, .97f) : new Color(.04f, .08f, .13f, .95f), 12);
                    Round(card, owned ? new Color(.3f, .9f, 1f) : sig ? new Color(1f, .75f, .3f, .8f) : new Color(.3f, .4f, .5f, .5f), 12, owned ? 2.5f : 1.2f);
                }
                var icon = GunIcon(w); if (icon != null) GUI.DrawTexture(new Rect(cx + 8, cy + 10, cw - 16, 70), icon, ScaleMode.ScaleToFit);
                float ty = cy + 88;
                Text(new Rect(cx + 10, ty, cw - 16, 26), w.id + "「" + w.name + "」", With(normal, 17, TextAnchor.MiddleLeft));
                Text(new Rect(cx + 10, ty + 26, cw - 16, 20), w.category, With(tiny, 12, TextAnchor.MiddleLeft), new Color(.7f, .8f, .9f));
                Text(new Rect(cx + 10, ty + 48, cw - 16, 20), w.bodyDamage + (w.pellets > 1 ? "×" + w.pellets : "") + " 伤 · " + w.rpm + " RPM · " + w.magazine + " 发", With(tiny, 12, TextAnchor.MiddleLeft));
                Text(new Rect(cx + 10, ty + 68, cw - 16, 20), "TTK " + w.TheoreticalTtk.ToString("0.00") + "s", With(tiny, 12, TextAnchor.MiddleLeft), new Color(.6f, 1f, .9f));
                Bars(new Rect(cx + 10, ty + 92, cw - 20, 44), w);
                GUI.Label(new Rect(cx + 10, ty + 142, cw - 20, 60), (ToonAssets.Available ? "打出：" + FoodFx.For(w).label + "\n" : "") + w.strengths, new GUIStyle(wrap) { fontSize = 12 });
                if (sig) { Round(new Rect(cx + 8, cy + 8, 86, 22), new Color(0, 0, 0, .7f), 11); Text(new Rect(cx + 8, cy + 8, 86, 22), "招牌 · 7 折", With(tiny, 12, TextAnchor.MiddleCenter), new Color(1f, .8f, .4f)); }
                string label = owned ? "已装备" : price == 0 ? "免费装备" : "$" + price + (afford ? "" : " · 资金不足");
                if (Button(new Rect(cx + 8, cy + ch - 48, cw - 16, 40), label, !owned && afford, !owned && (afford || price == 0) && game.phase == Phase.Prep))
                { if (price == 0) p.Equip(w); else p.Buy(w); }
            }
            if (Button(new Rect(x + pw / 2 - 350, y + ph - 64, 330, 50), "更换英雄", false)) overlay = Overlay.HeroSelect;
            if (Button(new Rect(x + pw / 2 + 20, y + ph - 64, 330, 50), "确定 · 回到准备区")) Close();
        }

        void Bars(Rect r, WeaponSpec w)
        {
            string[] names = { "伤害", "射速", "射程" };
            float[] v = { Mathf.Clamp01(w.bodyDamage * w.pellets / 56f), Mathf.Clamp01(w.rpm / 850f), Mathf.Clamp01(w.fullRange / 45f) };
            for (int i = 0; i < 3; i++)
            {
                float yy = r.y + i * 15;
                Text(new Rect(r.x, yy - 3, 40, 16), names[i], With(tiny, 11, TextAnchor.MiddleLeft));
                Round(new Rect(r.x + 36, yy + 2, r.width - 36, 6), new Color(.15f, .2f, .26f), 3);
                Round(new Rect(r.x + 36, yy + 2, (r.width - 36) * v[i], 6), new Color(.4f, .9f, 1f), 3);
            }
        }

        // V18：抢食大作战 HUD
        void CookTopBar(float W)
        {
            float cx = W / 2; Rect r = new Rect(cx - 260, 12, 520, 78);
            Round(r, Panel0, 12); Round(r, new Color(1f, .55f, .8f, .45f), 12, 1.4f);
            Round(new Rect(cx - 260, 12, 130, 78), new Color(Config.Blue.r, Config.Blue.g, Config.Blue.b, .25f), 12);
            Round(new Rect(cx + 130, 12, 130, 78), new Color(Config.Red.r, Config.Red.g, Config.Red.b, .25f), 12);
            Text(new Rect(cx - 260, 14, 130, 50), game.roundsWon[0].ToString(), With(huge, 44, TextAnchor.MiddleCenter));
            Text(new Rect(cx + 130, 14, 130, 50), game.roundsWon[1].ToString(), With(huge, 44, TextAnchor.MiddleCenter));
            Text(new Rect(cx - 260, 60, 130, 24), "蓝队大锅", With(tiny, 13, TextAnchor.MiddleCenter), Config.Blue);
            Text(new Rect(cx + 130, 60, 130, 24), "红队大锅", With(tiny, 13, TextAnchor.MiddleCenter), Config.Red);
            int t = Mathf.CeilToInt(game.phase == Phase.Prep ? game.phaseTimer : game.roundClock);
            Text(new Rect(cx - 130, 12, 260, 52), game.phase == Phase.Prep ? t.ToString() : (t / 60) + ":" + (t % 60).ToString("00"), With(huge, 42, TextAnchor.MiddleCenter));
            Text(new Rect(cx - 130, 60, 260, 24), "抢食大作战 · 先到 " + Match.CookTarget + " 分", With(tiny, 13, TextAnchor.MiddleCenter), new Color(1f, .75f, .9f));
            var p = game.player;
            if (p != null && !p.alive && game.phase == Phase.Live) Text(new Rect(0, 140, W, 40), "复活中… " + Mathf.Max(0, game.FFARespawnIn(p)).ToString("0.0") + " 秒", With(normal, 24, TextAnchor.MiddleCenter), new Color(1f, .85f, .9f));
        }

        // V17：美食大乱斗 HUD
        void FFATopBar(float W)
        {
            float cx = W / 2; Rect r = new Rect(cx - 280, 12, 560, 78);
            Round(r, Panel0, 12); Round(r, new Color(1f, .55f, .8f, .45f), 12, 1.4f);
            var p = game.player; bool grow = Match.Mode == GameMode.Grow;
            int mine = p != null ? (grow ? p.Food.growLevel : game.FFAKills(p)) : 0;
            Fighter lead = game.FFALeader(); int best = lead != null ? (grow ? lead.Food.growLevel : game.FFAKills(lead)) : 0;
            Text(new Rect(cx - 280, 14, 140, 50), mine.ToString(), With(huge, 44, TextAnchor.MiddleCenter), new Color(1f, .85f, .4f));
            Text(new Rect(cx - 280, 60, 140, 24), grow ? "你的体型" : "你的击杀", With(tiny, 13, TextAnchor.MiddleCenter), new Color(1f, .85f, .4f));
            Text(new Rect(cx + 140, 14, 140, 50), best.ToString(), With(huge, 44, TextAnchor.MiddleCenter), lead != null ? Config.TeamColor(lead.team) : Color.white);
            Text(new Rect(cx + 140, 60, 140, 24), "领先 · " + (lead != null ? lead.Hero.name : "-"), With(tiny, 13, TextAnchor.MiddleCenter), lead != null ? Config.TeamColor(lead.team) : Color.white);
            int t = Mathf.CeilToInt(game.phase == Phase.Prep ? game.phaseTimer : game.roundClock);
            Text(new Rect(cx - 140, 12, 280, 52), game.phase == Phase.Prep ? t.ToString() : (t / 60) + ":" + (t % 60).ToString("00"), With(huge, 42, TextAnchor.MiddleCenter), t <= 15 && game.phase == Phase.Live ? new Color(1f, .5f, .4f) : Color.white);
            Text(new Rect(cx - 140, 60, 280, 24), grow ? "吃货变大 · 时间到时最大的人获胜" : "美食大乱斗 · 先到 " + Match.FFATarget + " 杀获胜", With(tiny, 13, TextAnchor.MiddleCenter), new Color(1f, .75f, .9f));
            if (p != null && !p.alive && game.phase == Phase.Live) Text(new Rect(0, 140, W, 40), "复活中… " + Mathf.Max(0, game.FFARespawnIn(p)).ToString("0.0") + " 秒", With(normal, 24, TextAnchor.MiddleCenter), new Color(1f, .85f, .9f));
        }

        void FFAScoreboard(float W, float H, bool final)
        {
            float pw = Mathf.Min(900, W - 40), x = (W - pw) / 2, y = final ? H * .24f : 130, rowH = 44;
            PanelBox(new Rect(x, y, pw, 70 + rowH * game.fighters.Count), 16);
            string[] head = { "选手", Match.Mode == GameMode.Grow ? "体型 / 击杀" : "击杀", "阵亡", "伤害", "命中率" }; float[] cols = { 0, .45f, .58f, .71f, .84f };
            for (int i = 0; i < head.Length; i++) Text(new Rect(x + 24 + cols[i] * pw, y + 14, 160, 26), head[i], tiny);
            var list = new List<Fighter>(game.fighters); list.Sort((a, b) => game.FFAScore(b).CompareTo(game.FFAScore(a)));
            for (int row = 0; row < list.Count; row++)
            {
                var f = list[row]; float yy = y + 46 + row * rowH; Color tc = Config.TeamColor(f.team);
                Round(new Rect(x + 14, yy, pw - 28, rowH - 6), new Color(tc.r, tc.g, tc.b, f.human ? .32f : .12f), 8);
                var st = Live(f);
                string[] vals = { (row + 1) + ". " + f.Hero.name + (f.human ? "（你）" : "") + (f.alive ? "" : " ✕"), Match.Mode == GameMode.Grow ? f.Food.growLevel + " 级 / " + game.FFAKills(f) : game.FFAKills(f).ToString(), game.FFADeaths(f).ToString(), st.damage.ToString("0"), st.shots > 0 ? (100f * st.hits / st.shots).ToString("0") + "%" : "-" };
                for (int i = 0; i < vals.Length; i++) Text(new Rect(x + 24 + cols[i] * pw, yy, 220, rowH - 6), vals[i], normal, i == 0 ? tc : (Color?)null);
            }
        }

        void Scoreboard(float W, float H, bool final)
        {
            if (Match.FFA) { FFAScoreboard(W, H, final); return; }
            float pw = Mathf.Min(1100, W - 40), x = (W - pw) / 2, y = final ? H * .24f : 130;
            float rowH = 44, ph = 90 + rowH * 6 + 20;
            PanelBox(new Rect(x, y, pw, ph), 16);
            string[] head = { "英雄", "击杀", "助攻", "阵亡", "伤害", "爆头", "命中率", "安装/拆除", "技能 有效/使用" };
            float[] cols = { 0, .24f, .32f, .40f, .48f, .57f, .65f, .74f, .86f };
            for (int i = 0; i < head.Length; i++) Text(new Rect(x + 24 + cols[i] * pw, y + 14, 160, 26), head[i], tiny);
            int row = 0;
            for (int team = 0; team < 2; team++)
            {
                Text(new Rect(x + 24, y + 42 + row * rowH + team * 20, 200, 20), Config.TeamName(team) + " · " + game.roundsWon[team] + " 回合", With(tiny, 13, TextAnchor.MiddleLeft), Config.TeamColor(team));
                foreach (var f in game.fighters)
                {
                    if (f.team != team) continue;
                    var st = final ? f.matchStats : Live(f);
                    float yy = y + 62 + row * rowH + team * 20;
                    Round(new Rect(x + 14, yy, pw - 28, rowH - 6), new Color(Config.TeamColor(team).r, Config.TeamColor(team).g, Config.TeamColor(team).b, f.human ? .28f : .1f), 8);
                    if (Portraits.hero.TryGetValue(f.role, out var tex) && tex != null) GUI.DrawTextureWithTexCoords(new Rect(x + 20, yy + 3, 32, 32), tex, new Rect(.25f, .62f, .5f, .35f));
                    string[] vals = { f.Hero.name + (f.human ? "（你）" : "") + (f.alive ? "" : " ✕"), st.kills.ToString(), st.assists.ToString(), st.deaths.ToString(), st.damage.ToString("0"), st.headshots.ToString(),
                        st.shots > 0 ? (100f * st.hits / st.shots).ToString("0") + "%" : "-", st.plants + " / " + st.defuses, st.skillEffective + " / " + st.skillUses };
                    for (int i = 0; i < vals.Length; i++) Text(new Rect(x + 24 + cols[i] * pw + (i == 0 ? 36 : 0), yy, 180, rowH - 6), vals[i], normal);
                    row++;
                }
            }
        }

        static Stats Live(Fighter f) { var s = new Stats(); s.Add(f.matchStats); if (Match.I.phase != Phase.RoundEnd) s.Add(f.stats); return s; }

        void MatchEnd(float W, float H)
        {
            Backdrop(W, H, .7f);
            if (Match.FFA)
            {
                var lead = game.FFALeader();
                Text(new Rect(0, H * .08f, W, 80), (lead != null ? lead.Hero.name + (lead.human ? "（你）" : "") : "无人") + " 赢得" + Match.ModeName(Match.Mode) + "！", With(huge, 60, TextAnchor.MiddleCenter), lead != null ? Config.TeamColor(lead.team) : Color.white);
                Text(new Rect(0, H * .08f + 80, W, 34), lead == null ? "" : Match.Mode == GameMode.Grow ? lead.Food.growLevel + " 级 · " + game.FFAKills(lead) + " 杀" : game.FFAKills(lead) + " 杀   ·   先到 " + Match.FFATarget + " 杀获胜", With(normal, 20, TextAnchor.MiddleCenter));
                Scoreboard(W, H, true);
                if (ToonAssets.Available) FoodHud.DrawTitles(W, H);
                if (Button(new Rect(W / 2 - 340, H * .84f, 320, 54), "再来一场")) { game.StartMatch(); overlay = Overlay.HeroSelect; }
                if (Button(new Rect(W / 2 + 20, H * .84f, 320, 54), "返回主菜单", false)) BackToMenu();
                return;
            }
            int winner = Match.Mode == GameMode.Cook ? (game.roundsWon[0] >= game.roundsWon[1] ? 0 : 1) : game.roundsWon[0] >= Config.RoundsToWin ? 0 : 1;
            bool draw = Match.Mode == GameMode.Cook && game.roundsWon[0] == game.roundsWon[1];
            Text(new Rect(0, H * .08f, W, 80), draw ? "平局！" : Config.TeamName(winner) + " 赢得" + (Match.Mode == GameMode.Cook ? "抢食大作战" : "比赛"), With(huge, 64, TextAnchor.MiddleCenter), draw ? Color.white : Config.TeamColor(winner));
            if (ToonAssets.Available) FoodHud.DrawTitles(W, H);
            Text(new Rect(0, H * .08f + 80, W, 34), "回合比分 " + game.roundsWon[0] + " : " + game.roundsWon[1] + "   ·   试玩记录已导出（CSV）", With(normal, 20, TextAnchor.MiddleCenter));
            Scoreboard(W, H, true);
            if (Button(new Rect(W / 2 - 340, H * .84f, 320, 54), "再来一场")) { game.StartMatch(); overlay = Overlay.HeroSelect; }
            if (Button(new Rect(W / 2 + 20, H * .84f, 320, 54), "返回主菜单", false)) BackToMenu();
        }
    
        // ------------------------------------------------------------------ V9 menus
        Texture2D menuGradient;
        void MainMenu(float W, float H)
        {
            // dark gradient panel on the left, live map flyover behind
            if (menuGradient == null)
            {
                menuGradient = new Texture2D(256, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int i = 0; i < 256; i++) menuGradient.SetPixel(i, 0, new Color(.01f, .02f, .04f, .9f * Mathf.Pow(1f - i / 255f, 1.6f)));
                menuGradient.Apply();
            }
            GUI.DrawTexture(new Rect(0, 0, W * .6f, H), menuGradient);
            Fill(new Rect(0, H - 90, W, 90), new Color(0, 0, 0, .55f));
            float x = 90, y = H * .2f;
            Text(new Rect(x, y, 900, 110), "霓城封锁", With(huge, 96, TextAnchor.MiddleLeft), Color.white);
            Text(new Rect(x + 6, y + 108, 900, 34), "N E O N   L O C K D O W N", With(normal, 22, TextAnchor.MiddleLeft), new Color(.95f, .75f, .35f));
            Fill(new Rect(x + 6, y + 150, 360, 2), new Color(.95f, .75f, .35f, .8f));
            Text(new Rect(x + 6, y + 160, 700, 30), (ToonAssets.Available ? (Match.FFA ? "6 人美食大乱斗  ·  霓城美食街  ·  单机对战 AI" : "3v3 蛋糕爆破  ·  霓城美食街  ·  单机对战 AI") : "3v3 战术爆破  ·  旧港军事封锁区  ·  单机对战 AI"), With(small, 17, TextAnchor.MiddleLeft), new Color(.8f, .85f, .9f));
            float by = H * .5f;
            if (Button(new Rect(x, by, 340, 58), "开始 · " + Match.ModeName(Match.Mode))) BeginPlay();
            // V18：四种模式循环切换（重新载入场景）
            if (Button(new Rect(x, by + 72, 340, 50), "切换模式（当前：" + Match.ModeName(Match.Mode) + "）", false)) { Match.Mode = (GameMode)(((int)Match.Mode + 1) % 5); BackToMenu(); }
            if (ToonAssets.Available && Button(new Rect(x, by + 134, 340, 50), "帽子与菜谱图鉴", false)) FoodHud.showCollection = true;
            if (Button(new Rect(x, by + 196, 340, 50), "操作说明", false)) { returnTo = Overlay.Menu; overlay = Overlay.Help; }
            if (Button(new Rect(x, by + 258, 340, 50), "设置", false)) { returnTo = Overlay.Menu; overlay = Overlay.Settings; }
            if (Button(new Rect(x, by + 320, 340, 50), "退出游戏", false)) Application.Quit();
            Text(new Rect(x, H - 70, W - 2 * x, 26), Match.Mode == GameMode.Cook ? "抢食大作战  ·  3v3  ·  把食材送回自己队的大锅，凑成菜额外加分，也可以去偷对面的锅  ·  先到 " + Match.CookTarget + " 分获胜" : Match.Mode == GameMode.Grow ? "吃货变大  ·  6 人各自为战  ·  吃零食 +1 级，击杀 +2 级，阵亡减半  ·  时间到时最大的人获胜" : Match.FFA ? "美食大乱斗  ·  6 人各自为战  ·  阵亡 3 秒后复活  ·  捡起对手掉落的美食枪  ·  先到 " + Match.FFATarget + " 杀获胜" : "先赢 5 回合获胜  ·  攻方安装棱能炸弹，守方阻止或拆除  ·  每回合攻守互换", With(small, 15, TextAnchor.MiddleLeft), new Color(.7f, .75f, .8f));
            Text(new Rect(x, H - 42, W - 2 * x, 22), "V17  ·  Esc 暂停  ·  设置可调整灵敏度 / 视野 / 音量 / 画质", With(tiny, 13, TextAnchor.MiddleLeft), new Color(.5f, .55f, .6f));
        }

        void PauseMenu(float W, float H)
        {
            Backdrop(W, H, .6f);
            float pw = 420, ph = 470, x = (W - pw) / 2, y = (H - ph) / 2;
            PanelBox(new Rect(x, y, pw, ph), 16);
            Text(new Rect(x, y + 20, pw, 50), "已暂停", With(title, 36, TextAnchor.MiddleCenter), Color.white);
            Text(new Rect(x, y + 66, pw, 24), "第 " + (game.roundsWon[0] + game.roundsWon[1] + 1) + " 回合  ·  比分 " + game.roundsWon[0] + " : " + game.roundsWon[1], With(small, 15, TextAnchor.MiddleCenter), new Color(.7f, .8f, .9f));
            float bx = x + 40, bw = pw - 80, by = y + 112;
            if (Button(new Rect(bx, by, bw, 54), "继续游戏")) Resume();
            if (Button(new Rect(bx, by + 66, bw, 50), "设置", false)) { returnTo = Overlay.Pause; overlay = Overlay.Settings; }
            if (Button(new Rect(bx, by + 126, bw, 50), "操作说明", false)) { returnTo = Overlay.Pause; overlay = Overlay.Help; }
            if (Button(new Rect(bx, by + 186, bw, 50), "返回主菜单", false)) BackToMenu();
            if (Button(new Rect(bx, by + 246, bw, 50), "退出游戏", false)) Application.Quit();
        }

        int sliderHot;
        float Slider(Rect r, float v, float min, float max)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            Rect hit = new Rect(r.x - 8, r.y - 10, r.width + 16, r.height + 20);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (hit.Contains(e.mousePosition)) { GUIUtility.hotControl = id; v = Mathf.Lerp(min, max, Mathf.InverseLerp(r.x, r.xMax, e.mousePosition.x)); GUI.changed = true; e.Use(); }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id) { v = Mathf.Lerp(min, max, Mathf.InverseLerp(r.x, r.xMax, e.mousePosition.x)); GUI.changed = true; e.Use(); }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); Sfx.Ui("click", .25f, 1.4f); }
                    break;
            }
            float k = Mathf.InverseLerp(min, max, v);
            Round(new Rect(r.x, r.center.y - 3, r.width, 6), new Color(.15f, .2f, .26f), 3);
            Round(new Rect(r.x, r.center.y - 3, r.width * k, 6), new Color(.95f, .72f, .3f), 3);
            Round(new Rect(r.x + r.width * k - 9, r.center.y - 9, 18, 18), Color.white, 9);
            return v;
        }

        void Settings(float W, float H)
        {
            Backdrop(W, H, .78f);
            float pw = Mathf.Min(820, W - 40), ph = 790, x = (W - pw) / 2, y = (H - ph) / 2;
            PanelBox(new Rect(x, y, pw, ph), 16);
            Text(new Rect(x + 36, y + 18, pw, 50), "设置", With(title, 34, TextAnchor.MiddleLeft), Color.white);
            float ly = y + 92, lx = x + 40, sx = x + 300, sw = pw - 460;
            void Row(string label, string value)
            {
                Text(new Rect(lx, ly, 250, 34), label, With(normal, 19, TextAnchor.MiddleLeft), new Color(.85f, .9f, .95f));
                Text(new Rect(sx + sw + 20, ly, 120, 34), value, With(normal, 19, TextAnchor.MiddleLeft), new Color(.95f, .75f, .35f));
            }
            Row("鼠标灵敏度", GameSettings.Sensitivity.ToString("0.0"));
            GameSettings.Sensitivity = Mathf.Round(Slider(new Rect(sx, ly + 8, sw, 18), GameSettings.Sensitivity, .4f, 6f) * 10f) / 10f; ly += 56;
            Row("开镜灵敏度倍率", "×" + GameSettings.AdsSensitivity.ToString("0.00"));
            GameSettings.AdsSensitivity = Mathf.Round(Slider(new Rect(sx, ly + 8, sw, 18), GameSettings.AdsSensitivity, .3f, 1.5f) * 20f) / 20f; ly += 56;
            Row("视野（FOV）", GameSettings.Fov.ToString("0") + "°");
            GameSettings.Fov = Mathf.Round(Slider(new Rect(sx, ly + 8, sw, 18), GameSettings.Fov, 65f, 100f)); ly += 56;
            Row("主音量", Mathf.RoundToInt(GameSettings.Volume * 100) + "%");
            float vol = Slider(new Rect(sx, ly + 8, sw, 18), GameSettings.Volume, 0f, 1f);
            if (!Mathf.Approximately(vol, GameSettings.Volume)) { GameSettings.Volume = vol; AudioListener.volume = vol; }
            ly += 56;
            Text(new Rect(lx, ly, 250, 34), "画质", With(normal, 19, TextAnchor.MiddleLeft), new Color(.85f, .9f, .95f));
            string[] q = { "低", "中", "高" };
            for (int i = 0; i < 3; i++)
                if (Button(new Rect(sx + i * (sw / 3f + 4), ly, sw / 3f - 4, 38), q[i], GameSettings.Quality == i)) { GameSettings.Quality = i; GameSettings.Apply(); }
            ly += 56;
            Row("敌人描边强度", Mathf.RoundToInt(GameSettings.EnemyOutline * 100) + "%");
            GameSettings.EnemyOutline = Mathf.Round(Slider(new Rect(sx, ly + 8, sw, 18), GameSettings.EnemyOutline, 0f, 2f) * 20f) / 20f; ly += 56;
            Text(new Rect(lx, ly, 250, 34), "伤害数字", With(normal, 19, TextAnchor.MiddleLeft), new Color(.85f, .9f, .95f));
            if (Button(new Rect(sx, ly, sw / 3f - 4, 38), "显示", GameSettings.DamageNumbers)) GameSettings.DamageNumbers = true;
            if (Button(new Rect(sx + sw / 3f + 4, ly, sw / 3f - 4, 38), "隐藏", !GameSettings.DamageNumbers)) GameSettings.DamageNumbers = false;
            ly += 56;
            Text(new Rect(lx, ly, 250, 34), "新手提示卡", With(normal, 19, TextAnchor.MiddleLeft), new Color(.85f, .9f, .95f));   // V21：默认开，可关
            if (Button(new Rect(sx, ly, sw / 3f - 4, 38), "开", GameSettings.Tips)) GameSettings.Tips = true;
            if (Button(new Rect(sx + sw / 3f + 4, ly, sw / 3f - 4, 38), "关", !GameSettings.Tips)) GameSettings.Tips = false;
            ly += 56;
            Text(new Rect(lx, ly, 250, 34), "小地图朝向", With(normal, 19, TextAnchor.MiddleLeft), new Color(.85f, .9f, .95f));   // V23 b：拍板 2 乙
            if (Button(new Rect(sx, ly, sw / 2f - 4, 38), "跟随视角", GameSettings.MinimapRotate)) GameSettings.MinimapRotate = true;
            if (Button(new Rect(sx + sw / 2f + 4, ly, sw / 2f - 4, 38), "北向固定", !GameSettings.MinimapRotate)) GameSettings.MinimapRotate = false;
            ly += 56;
            Text(new Rect(lx, ly, 250, 34), "食材指引", With(normal, 19, TextAnchor.MiddleLeft), new Color(.85f, .9f, .95f));   // V23 b：§3.3
            string[] gopt = { "全部", "只小地图", "关" };
            for (int i = 0; i < 3; i++)
                if (Button(new Rect(sx + i * (sw / 3f + 4), ly, sw / 3f - 4, 38), gopt[i], GameSettings.IngredientGuide == i)) GameSettings.IngredientGuide = i;
            ly += 56;
            Text(new Rect(lx, ly, pw - 80, 26), "画质「低」关闭环境光遮蔽和泛光，阴影距离缩短，适合轻薄本。设置会自动保存。", With(small, 14, TextAnchor.MiddleLeft), new Color(.6f, .65f, .7f));
            if (Button(new Rect(x + 40, y + ph - 76, 220, 52), "恢复默认", false))
            { GameSettings.Sensitivity = 2f; GameSettings.AdsSensitivity = 1f; GameSettings.Fov = 78f; GameSettings.Volume = .9f; GameSettings.Quality = 2; GameSettings.DamageNumbers = true; GameSettings.Tips = true; GameSettings.MinimapRotate = true; GameSettings.IngredientGuide = 0; GameSettings.Apply(); }
            if (Button(new Rect(x + pw - 300, y + ph - 76, 260, 52), "保存并返回")) { GameSettings.Save(); overlay = returnTo; if (overlay == Overlay.None) ShowCursor(false); }
        }
    }
}
