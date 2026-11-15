using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace NeonSiege
{
    // Player-facing options, persisted with PlayerPrefs. Edited in the 设置 screen (GameUI.Settings).
    public static class GameSettings
    {
        public static float Sensitivity = 2f, AdsSensitivity = 1f, Fov = 78f, Volume = .9f;
        public static int Quality = 2;   // 0 低 · 1 中 · 2 高
        public static bool DamageNumbers = true;
        public static bool Tips = true;   // V21：首次提示卡（新手提示），默认开启，可在设置里关掉
        public static float EnemyOutline = 1f;
        public static bool MinimapRotate = true;   // V23 b：小地图跟随视角旋转（拍板 2 乙）；关 = 北向固定
        public static int IngredientGuide = 0;      // V23 b：食材指引 0 全部 / 1 只小地图 / 2 关
        static bool loaded;

        public static void Load()
        {
            if (loaded) return; loaded = true;
            Sensitivity = PlayerPrefs.GetFloat("ns_sens", 2f);
            AdsSensitivity = PlayerPrefs.GetFloat("ns_ads", 1f);
            Fov = PlayerPrefs.GetFloat("ns_fov", 78f);
            Volume = PlayerPrefs.GetFloat("ns_vol", .9f);
            Quality = PlayerPrefs.GetInt("ns_quality", 2);
            DamageNumbers = PlayerPrefs.GetInt("ns_dmgnum", 1) == 1;
            EnemyOutline = PlayerPrefs.GetFloat("ns_outline", 1f);
            Tips = PlayerPrefs.GetInt("ns_tips", 1) == 1;
            MinimapRotate = PlayerPrefs.GetInt("ns_maprot", 1) == 1;
            IngredientGuide = Mathf.Clamp(PlayerPrefs.GetInt("ns_ingguide", 0), 0, 2);
        }

        public static void Save()
        {
            PlayerPrefs.SetFloat("ns_sens", Sensitivity); PlayerPrefs.SetFloat("ns_ads", AdsSensitivity);
            PlayerPrefs.SetFloat("ns_fov", Fov); PlayerPrefs.SetFloat("ns_vol", Volume);
            PlayerPrefs.SetInt("ns_quality", Quality); PlayerPrefs.SetInt("ns_dmgnum", DamageNumbers ? 1 : 0); PlayerPrefs.SetFloat("ns_outline", EnemyOutline); PlayerPrefs.SetInt("ns_tips", Tips ? 1 : 0);
            PlayerPrefs.SetInt("ns_maprot", MinimapRotate ? 1 : 0); PlayerPrefs.SetInt("ns_ingguide", IngredientGuide);
            PlayerPrefs.Save();
            Apply();
        }

        public static void Apply()
        {
            Load();
            // V21：自动测试 / 自动对局 / 截图（命令行带 -neon 参数）一律静音，不打扰用户
            bool automated = System.Array.Exists(System.Environment.GetCommandLineArgs(), a => a.StartsWith("-neon"));
            AudioListener.volume = automated ? 0f : Volume;
            QualitySettings.shadows = Quality == 0 ? ShadowQuality.HardOnly : ShadowQuality.All;
            QualitySettings.shadowDistance = Quality == 0 ? 40 : Quality == 1 ? 60 : 80;
            QualitySettings.shadowResolution = Quality == 0 ? ShadowResolution.Medium : Quality == 1 ? ShadowResolution.High : ShadowResolution.VeryHigh;
            QualitySettings.pixelLightCount = Quality == 0 ? 4 : 8;
            QualitySettings.antiAliasing = 0;
            foreach (var vol in Object.FindObjectsOfType<PostProcessVolume>())
            {
                if (vol.sharedProfile == null) continue;
                if (vol.sharedProfile.TryGetSettings(out AmbientOcclusion ao)) ao.enabled.value = Quality > 0;
                if (vol.sharedProfile.TryGetSettings(out Bloom bloom)) bloom.enabled.value = Quality > 0;
            }
        }
    }
}
