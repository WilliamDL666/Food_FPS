using System.Collections.Generic;
using System.Linq;
using NeonSiege;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Builds the procedural map inside the editor, bakes global illumination (lightmaps + light probes)
// and stores each renderer's lightmap slot in Resources/ArenaLightmaps.asset. See ArenaLightmaps.cs.
public static class BakeArena
{
    const string Scene = "Assets/Scenes/NeonArena.unity";
    const string DataPath = "Assets/Resources/ArenaLightmaps.asset";

    [MenuItem("Neon Siege/Bake Map Lighting")]
    public static void Bake()
    {
        EnableLightmapUVsForProps();
        KeepLightmapShaderVariants();
        var scene = EditorSceneManager.OpenScene(Scene);
        var before = new HashSet<GameObject>(scene.GetRootGameObjects());

        var arena = new GameObject("霓城 · 旧港夜市街区").AddComponent<Arena>();
        arena.Build();

        // ---- what gets baked: opaque, static environment meshes (not interactables, text or effects)
        var all = arena.GetComponentsInChildren<Renderer>(true);
        int bakedCount = 0;
        foreach (var r in all)
        {
            bool ok = IsStaticEnvironment(r, arena);
            var go = r.gameObject;
            if (ok)
            {
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic);
                ((MeshRenderer)r).receiveGI = ReceiveGI.Lightmaps;
                var so = new SerializedObject(r);
                float size = r.bounds.size.magnitude, dist = new Vector2(r.bounds.center.x, r.bounds.center.z).magnitude;
                float scale = dist > 95f ? .15f : size > 60f ? .5f : 1f;   // far skyline & huge walls get fewer texels
                so.FindProperty("m_ScaleInLightmap").floatValue = scale;
                so.ApplyModifiedPropertiesWithoutUndo();
                bakedCount++;
            }
            else { GameObjectUtility.SetStaticEditorFlags(go, 0); }
        }

        // ---- light probes for characters / moving objects
        var probes = new GameObject("烘焙光照探针").AddComponent<LightProbeGroup>();
        var pts = new List<Vector3>();
        for (float x = -70; x <= 70; x += 5f)
            for (float z = -48; z <= 48; z += 5f)
            {
                pts.Add(new Vector3(x, .8f, z)); pts.Add(new Vector3(x, 3.5f, z));
                if (Mathf.Abs(x) < 50 && Mathf.Abs(z) < 12) pts.Add(new Vector3(x, 9.5f, z));
            }
        probes.probePositions = pts.ToArray();

        // ---- sun: realtime direct light + shadows, baked bounce; street lamps stay realtime (blackouts)
        foreach (var l in Object.FindObjectsOfType<Light>())
            l.lightmapBakeType = l.type == LightType.Directional ? LightmapBakeType.Mixed : LightmapBakeType.Realtime;

        var ls = new LightingSettings
        {
            bakedGI = true, realtimeGI = false,
            lightmapper = LightingSettings.Lightmapper.ProgressiveGPU,
            mixedBakeMode = MixedLightingMode.IndirectOnly,
            lightmapResolution = 2f, lightmapPadding = 2, lightmapMaxSize = 2048,
            directSampleCount = 16, indirectSampleCount = 160, environmentSampleCount = 160, maxBounces = 3,
            ao = true, aoMaxDistance = 1.4f, aoExponentIndirect = 1f, aoExponentDirect = 0f,
            directionalityMode = LightmapsMode.NonDirectional, compressLightmaps = true,
            filteringMode = LightingSettings.FilterMode.Auto,
        };
        AssetDatabase.DeleteAsset("Assets/Settings/NeonLighting.lighting");
        AssetDatabase.CreateAsset(ls, "Assets/Settings/NeonLighting.lighting");
        Lightmapping.lightingSettings = ls;

        Debug.Log("NEON_BAKE start static=" + bakedCount + " at " + System.DateTime.Now.ToString("HH:mm:ss"));
        Lightmapping.ClearDiskCache();   // interrupted bakes can leave a corrupt GI cache behind
        Lightmapping.Clear();
        var t0 = System.DateTime.Now;
        bool baked = Lightmapping.Bake();
        Debug.Log("NEON_BAKE baked=" + baked + " renderers=" + all.Length + " static=" + bakedCount + " lightmaps=" + LightmapSettings.lightmaps.Length + " seconds=" + (System.DateTime.Now - t0).TotalSeconds.ToString("0"));

        // ---- record slots by hierarchy order
        AssetDatabase.DeleteAsset(DataPath);
        var data = ScriptableObject.CreateInstance<ArenaLightmaps>();
        data.rendererCount = all.Length; data.bakedAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        foreach (var m in LightmapSettings.lightmaps) { data.colors.Add(m.lightmapColor); if (m.lightmapDir != null) data.dirs.Add(m.lightmapDir); }
        int used = 0;
        foreach (var r in all)
        {
            data.slot.Add(r.lightmapIndex >= 0 && r.lightmapIndex < 0xFFFE ? r.lightmapIndex : -1);
            data.scaleOffset.Add(r.lightmapScaleOffset); data.names.Add(r.name);
            if (data.slot[data.slot.Count - 1] >= 0) used++;
        }
        AssetDatabase.CreateAsset(data, DataPath);
        Debug.Log("NEON_BAKE recorded " + used + " lightmapped renderers");

        // ---- remove the generated map again (the game builds it at runtime); keep lighting data + probes
        foreach (var root in scene.GetRootGameObjects())
            if (!before.Contains(root) && root != probes.gameObject) Object.DestroyImmediate(root);
        UnityEngine.AI.NavMesh.RemoveAllNavMeshData();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("NEON_BAKE_OK");
    }

    static bool IsStaticEnvironment(Renderer r, Arena arena)
    {
        if (!(r is MeshRenderer)) return false;
        if (r.GetComponent<TextMesh>() != null) return false;
        var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) return false;
        foreach (var m in r.sharedMaterials) if (m == null || m.renderQueue > 2450) return false;
        // anything owned by a behaviour (doors, shutters, crates, glass, pads, cameras, flicker…) moves or breaks
        for (var t = r.transform; t != null && t != arena.transform; t = t.parent)
            foreach (var mb in t.GetComponents<MonoBehaviour>()) if (mb != null) return false;
        if (!r.enabled || !r.gameObject.activeInHierarchy) return false;
        if (!IsFinite(mf.sharedMesh)) return false;
        var bb = r.bounds; if (!Fin(bb.center) || !Fin(bb.size)) return false;
        var mtx = r.localToWorldMatrix; for (int i = 0; i < 16; i++) if (float.IsNaN(mtx[i]) || float.IsInfinity(mtx[i])) return false;
        for (var t = r.transform; t != null; t = t.parent) if (t.name.Contains("facade")) return false;   // photoscan facade kit: degenerate pieces   // e.g. modular_factory_facade has NaN data → hangs the lightmapper
        // only architecture and large pieces get lightmap space; small props use light probes
        string mesh = mf.sharedMesh.name;
        bool primitive = mesh == "Cube" || mesh == "Quad" || mesh == "Cylinder" || mesh == "Plane";
        if (!primitive) return false;   // imported models (Poly Haven) use light probes
        if (r.bounds.size.magnitude < .8f) return false;
        // the distant skyline only needs probe lighting
        if (new Vector2(r.bounds.center.x, r.bounds.center.z).magnitude > 90f) return false;
        return true;
    }

    static bool Fin(Vector3 v) { return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z)); }
    static readonly Dictionary<Mesh, bool> finite = new Dictionary<Mesh, bool>();
    static bool IsFinite(Mesh m)
    {
        if (finite.TryGetValue(m, out bool ok)) return ok;
        ok = true;
        bool Bad(float f) { return float.IsNaN(f) || float.IsInfinity(f); }
        foreach (var v in m.vertices) if (Bad(v.x) || Bad(v.y) || Bad(v.z)) { ok = false; break; }
        if (ok) foreach (var n in m.normals) if (Bad(n.x) || Bad(n.y) || Bad(n.z)) { ok = false; break; }
        if (ok) { var uv2 = new List<Vector2>(); m.GetUVs(1, uv2); foreach (var u in uv2) if (Bad(u.x) || Bad(u.y)) { ok = false; break; } }
        if (!ok) Debug.LogWarning("NEON_BAKE skip non-finite mesh " + m.name);
        finite[m] = ok; return ok;
    }

    // glTFast: generate a secondary UV set so Poly Haven props can receive lightmaps.
    static void EnableLightmapUVsForProps()
    {
        foreach (var guid in AssetDatabase.FindAssets("", new[] { "Assets/Resources/Models/PH" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".gltf")) continue;
            var imp = AssetImporter.GetAtPath(path); if (imp == null) continue;
            var so = new SerializedObject(imp);
            var p = so.FindProperty("editorImportSettings.generateSecondaryUVSet");
            if (p == null || p.boolValue) continue;
            p.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo();
            imp.SaveAndReimport();
        }
    }

    // Lightmaps are assigned at runtime, so make sure the build keeps the lightmap shader variants.
    static void KeepLightmapShaderVariants()
    {
        var gs = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
        var so = new SerializedObject(gs);
        void Set(string name, int v) { var p = so.FindProperty(name); if (p != null) p.intValue = v; }
        Set("m_LightmapStripping", 1);
        Set("m_LightmapKeepPlain", 1); Set("m_LightmapKeepDirCombined", 1); Set("m_LightmapKeepDynamicPlain", 1);
        Set("m_LightmapKeepDynamicDirCombined", 1); Set("m_LightmapKeepShadowMask", 1); Set("m_LightmapKeepSubtractive", 1);
        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
    }

    public static void BakeAndBuild() { BuildDemo.Setup(); Bake(); BuildDemo.BuildWindows(); }
}
