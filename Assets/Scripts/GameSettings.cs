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
        public static float EnemyOutline = 1f;
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
        }

        public static void Save()
        {
            PlayerPrefs.SetFloat("ns_sens", Sensitivity); PlayerPrefs.SetFloat("ns_ads", AdsSensitivity);
            PlayerPrefs.SetFloat("ns_fov", Fov); PlayerPrefs.SetFloat("ns_vol", Volume);
            PlayerPrefs.SetInt("ns_quality", Quality); PlayerPrefs.SetInt("ns_dmgnum", DamageNumbers ? 1 : 0); PlayerPrefs.SetFloat("ns_outline", EnemyOutline);
            PlayerPrefs.Save();
            Apply();
        }

        public static void Apply()
        {
            Load();
            AudioListener.volume = Volume;
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
