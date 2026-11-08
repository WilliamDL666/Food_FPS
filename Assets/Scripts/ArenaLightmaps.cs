using System.Collections.Generic;
using UnityEngine;

namespace NeonSiege
{
    // Baked global illumination for the procedurally built map.
    // The editor builds the exact same map, bakes lightmaps + light probes, and records each renderer's
    // lightmap slot by its position in the hierarchy. The map build is deterministic, so at runtime the
    // freshly built renderers get their slots back (validated by count and names).
    public class ArenaLightmaps : ScriptableObject
    {
        public List<Texture2D> colors = new List<Texture2D>();
        public List<Texture2D> dirs = new List<Texture2D>();
        public int rendererCount;
        public List<int> slot = new List<int>();          // renderer order index → lightmap index (-1 = none)
        public List<Vector4> scaleOffset = new List<Vector4>();
        public List<string> names = new List<string>();
        public string bakedAt;

        public static bool Applied { get; private set; }
        public static string Status = "未烘焙";

        // Called again a moment after start: if anything reset the lightmap slots, put them back and log it.
        public static void Verify(Arena arena)
        {
            var data = Resources.Load<ArenaLightmaps>("ArenaLightmaps");
            if (data == null || !Applied) return;
            var rs = arena.GetComponentsInChildren<Renderer>(true);
            int lost = 0, total = 0;
            for (int i = 0; i < rs.Length && i < data.slot.Count; i++)
            {
                if (data.slot[i] < 0) continue; total++;
                if (rs[i].lightmapIndex != data.slot[i]) lost++;
            }
            var maps = LightmapSettings.lightmaps;
            bool texOk = maps.Length == data.colors.Count && maps.Length > 0 && maps[0].lightmapColor == data.colors[0];
            Debug.Log("NEON_GI verify lost=" + lost + "/" + total + " lightmaps=" + maps.Length + " texturesOk=" + texOk + " ambientMode=" + RenderSettings.ambientMode);
            if (lost > 0 || !texOk) Apply(arena);
        }

        public static void Apply(Arena arena)
        {
            Applied = false;
            var data = Resources.Load<ArenaLightmaps>("ArenaLightmaps");
            if (data == null) { Status = "没有烘焙数据"; return; }
            var rs = arena.GetComponentsInChildren<Renderer>(true);
            if (rs.Length != data.rendererCount) { Status = "渲染器数量不一致（烘焙 " + data.rendererCount + " / 运行 " + rs.Length + "）"; Debug.LogWarning("NEON_GI " + Status); return; }
            for (int i = 0; i < rs.Length; i++)
                if (data.slot[i] >= 0 && rs[i].name != data.names[i]) { Status = "第 " + i + " 个渲染器名称不一致：" + rs[i].name + " ≠ " + data.names[i]; Debug.LogWarning("NEON_GI " + Status); return; }

            var maps = new LightmapData[data.colors.Count];
            for (int i = 0; i < maps.Length; i++)
                maps[i] = new LightmapData { lightmapColor = data.colors[i], lightmapDir = i < data.dirs.Count ? data.dirs[i] : null };
            LightmapSettings.lightmaps = maps;
            int n = 0;
            for (int i = 0; i < rs.Length; i++)
            {
                if (data.slot[i] < 0) continue;
                rs[i].lightmapIndex = data.slot[i]; rs[i].lightmapScaleOffset = data.scaleOffset[i]; n++;
            }
            foreach (var probe in Object.FindObjectsOfType<ReflectionProbe>())
                if (probe.mode == UnityEngine.Rendering.ReflectionProbeMode.Realtime) probe.RenderProbe();
            Applied = true;
            Status = "已应用 " + n + " 个烘焙物体，" + maps.Length + " 张光照贴图";
            Debug.Log("NEON_GI " + Status + "（" + data.bakedAt + "）");
        }
    }
}
