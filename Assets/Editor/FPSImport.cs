using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Unity FPSSample (Unity Companion License) → Built-in pipeline.
// Reads the original HDRP .mat files (kept outside the project in E:\Claude工作区\素材\FPSSample) and builds
// Standard materials with the same textures (texture GUIDs are preserved through the copied .meta files).
public static class FPSImport
{
    const string Src = @"E:\Claude工作区\素材\FPSSample\Assets\Materials";
    const string MatDir = "Assets/Resources/FPS/Mats";

    [MenuItem("Neon Siege/FPSSample/Build Materials")]
    public static void BuildMaterials()
    {
        Directory.CreateDirectory(MatDir);
        int made = 0;
        foreach (var path in Directory.GetFiles(Src, "*.mat", SearchOption.AllDirectories))
        {
            if (!path.Contains("Terraformer")) continue;
            string text = File.ReadAllText(path);
            string name = Path.GetFileNameWithoutExtension(path);
            Texture2D Tex(string slot)
            {
                var m = Regex.Match(text, "- " + slot + @":\s*\n\s*m_Texture: \{fileID: \d+, guid: ([0-9a-f]{32})");
                if (!m.Success) return null;
                string p = AssetDatabase.GUIDToAssetPath(m.Groups[1].Value);
                return string.IsNullOrEmpty(p) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            }
            Color Col(string slot, Color fallback)
            {
                var m = Regex.Match(text, "- " + slot + @": \{r: ([-\d.e]+), g: ([-\d.e]+), b: ([-\d.e]+), a: ([-\d.e]+)\}");
                if (!m.Success) return fallback;
                float f(int i) => float.Parse(m.Groups[i].Value, System.Globalization.CultureInfo.InvariantCulture);
                return new Color(f(1), f(2), f(3), f(4));
            }
            bool glass = name.Contains("Glass"), liquid = name.Contains("Liquids");
            var mat = new Material(Shader.Find("Standard")) { name = name };
            var albedo = Tex("_BaseColorMap"); var normal = Tex("_NormalMap"); var mask = Tex("_MaskMap"); var emis = Tex("_EmissiveColorMap");
            mat.color = Col("_BaseColor", Color.white);
            if (albedo != null) mat.mainTexture = albedo;
            if (normal != null) { mat.SetTexture("_BumpMap", normal); mat.EnableKeyword("_NORMALMAP"); }
            if (mask != null)
            {
                // HDRP mask map: R metallic, G AO, A smoothness == Standard metallic(R)+gloss(A); occlusion reads G
                mat.SetTexture("_MetallicGlossMap", mask); mat.EnableKeyword("_METALLICGLOSSMAP"); mat.SetFloat("_GlossMapScale", .85f);
                mat.SetTexture("_OcclusionMap", mask); mat.SetFloat("_OcclusionStrength", 1f);
            }
            else { mat.SetFloat("_Metallic", 0f); mat.SetFloat("_Glossiness", .35f); }
            if (emis != null)
            {
                mat.SetTexture("_EmissionMap", emis); mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.white * 1.2f);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            if (glass || liquid)
            {
                // transparent (Fade) for visors, scope glass and the coloured liquids
                mat.SetFloat("_Mode", 2); mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0); mat.EnableKeyword("_ALPHABLEND_ON"); mat.renderQueue = 3000;
                var c = mat.color; c.a = glass ? .35f : .75f; mat.color = c; mat.SetFloat("_Glossiness", .9f);
            }
            string outPath = MatDir + "/" + name + ".mat";
            AssetDatabase.DeleteAsset(outPath);
            AssetDatabase.CreateAsset(mat, outPath);
            made++;
            Debug.Log("FPS_MAT " + name + " albedo=" + (albedo ? albedo.name : "-") + " normal=" + (normal ? normal.name : "-") + " mask=" + (mask ? mask.name : "-") + " emis=" + (emis ? emis.name : "-"));
        }
        AssetDatabase.SaveAssets();
        Debug.Log("FPS_MATS_DONE " + made);
    }

    [MenuItem("Neon Siege/FPSSample/Probe")]
    public static void Probe()
    {
        var sb = new StringBuilder();
        foreach (var path in new[] { "Assets/Resources/FPS/Char/Terraformer_A.fbx", "Assets/Resources/FPS/Char/Terraformer_A--FirstPerson.fbx",
                                     "Assets/Resources/FPS/Weapon/Terraformer_Weapon_A_1P.fbx", "Assets/Resources/FPS/Weapon/Terraformer_Weapon_A_3P.fbx",
                                     "Assets/Resources/FPS/Weapon/Terraformer_Weapon_A_1P--Sniper.fbx", "Assets/Resources/FPS/Weapon/Terraformer_Weapon_A_3P--Sniper.fbx" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            sb.AppendLine("##### " + path + (prefab == null ? "  (MISSING)" : ""));
            if (prefab == null) continue;
            var an = prefab.GetComponent<Animator>();
            sb.AppendLine("animator=" + (an != null) + " avatar=" + (an != null && an.avatar != null ? an.avatar.name + " human=" + an.avatar.isHuman : "none"));
            var rs = prefab.GetComponentsInChildren<Renderer>(true);
            Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(); foreach (var r in rs) b.Encapsulate(r.bounds);
            sb.AppendLine("bounds center=" + b.center.ToString("F3") + " size=" + b.size.ToString("F3"));
            foreach (var r in rs)
            {
                var ms = new List<string>(); foreach (var m in r.sharedMaterials) ms.Add(m ? m.name : "null");
                sb.AppendLine("  R " + r.name + " [" + r.GetType().Name + "] mats: " + string.Join(", ", ms));
            }
            void Walk(Transform t, int depth)
            {
                if (depth > 9) return;
                sb.AppendLine(new string(' ', depth * 2) + "- " + t.name + "  " + t.localPosition.ToString("F3"));
                foreach (Transform c in t) Walk(c, depth + 1);
            }
            Walk(prefab.transform, 0);
        }
        foreach (var dir in new[] { "Assets/Resources/FPS/Anim3P", "Assets/Resources/FPS/Anim1P" })
        {
            sb.AppendLine("##### clips in " + dir);
            foreach (var f in Directory.GetFiles(dir, "*.fbx"))
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(f.Replace('\\', '/')))
                    if (o is AnimationClip c && !c.name.StartsWith("__")) sb.AppendLine("  " + Path.GetFileName(f) + " -> " + c.name + "  len=" + c.length.ToString("F2") + " loop=" + c.isLooping + " human=" + c.humanMotion);
        }
        File.WriteAllText(@"E:\Claude工作区\素材\FPSSample\probe.txt", sb.ToString(), Encoding.UTF8);
        Debug.Log("FPS_PROBE_DONE");
    }

    static AnimationClip Clip(string file, string name)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(file)) if (o is AnimationClip c && c.name == name) return c;
        return null;
    }
    static Transform Find(Transform r, string n) { foreach (var t in r.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t; return null; }

    // Where do arms and weapon line up? Sample both rigs at the same time with roots at the origin.
    [MenuItem("Neon Siege/FPSSample/Probe Alignment")]
    public static void ProbeAlign()
    {
        var sb = new StringBuilder();
        var arms = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/FPS/Char/Terraformer_A--FirstPerson.fbx"));
        var gun = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/FPS/Weapon/Terraformer_Weapon_A_1P.fbx"));
        foreach (var pair in new[] { ("Idle", "Idle", .5f), ("Run", "Run", .2f), ("Reload", "Reload", 1f), ("ScopeAim", "ScopeAim_Weapon", 0f) })
        {
            var ca = Clip("Assets/Resources/FPS/Anim1P/Firstperson--" + pair.Item1 + ".anim.fbx", pair.Item1);
            var cw = Clip("Assets/Resources/FPS/Anim1P/Firstperson--" + (pair.Item1 == "ScopeAim" ? "ScopeAim_Weapon" : pair.Item1 + "_Weapon") + ".anim.fbx", pair.Item2);
            if (ca != null) ca.SampleAnimation(arms, pair.Item3);
            if (cw != null) cw.SampleAnimation(gun, pair.Item3);
            sb.AppendLine("== " + pair.Item1 + " armsClip=" + (ca != null) + " gunClip=" + (cw != null));
            foreach (var n in new[] { "Camera", "Head", "Right_Hand", "Left_Hand", "UpperChest" })
            { var t = Find(arms.transform, n); if (t) sb.AppendLine("  arms " + n + " pos=" + t.position.ToString("F3") + " fwd=" + t.forward.ToString("F2") + " up=" + t.up.ToString("F2")); }
            foreach (var n in new[] { "Main", "Trigger", "Bullit_Spawn", "Magazine", "Front_Hinge" })
            { var t = Find(gun.transform, n); if (t) sb.AppendLine("  gun  " + n + " pos=" + t.position.ToString("F3") + " fwd=" + t.forward.ToString("F2")); }
        }
        // 3P: weapon in the right hand with identity offset — how far is the left hand from the foregrip?
        var body = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/FPS/Char/Terraformer_A.fbx"));
        var g3 = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/FPS/Weapon/Terraformer_Weapon_A_3P.fbx"));
        var idle = Clip("Assets/Resources/FPS/Anim3P/Stand--Idle.anim.fbx", "Idle"); var shoot = Clip("Assets/Resources/FPS/Anim3P/Stand--Shoot.anim.fbx", "Shoot");
        var an = body.GetComponent<Animator>();
        foreach (var c in new[] { idle, shoot })
        {
            if (c == null) continue;
            c.SampleAnimation(body, .3f);
            var rh = Find(body.transform, "Right_Hand"); var lh = Find(body.transform, "Left_Hand");
            sb.AppendLine("== 3P " + c.name + " humanClip=" + c.humanMotion + " RH=" + rh.position.ToString("F3") + " fwd=" + rh.forward.ToString("F2") + " up=" + rh.up.ToString("F2") + " right=" + rh.right.ToString("F2") + "  LH=" + lh.position.ToString("F3"));
            g3.transform.SetParent(rh, false); g3.transform.localPosition = Vector3.zero; g3.transform.localRotation = Quaternion.identity;
            foreach (var n in new[] { "Trigger", "Bullit_Spawn", "Front_Hinge", "Magazine" }) { var t = Find(g3.transform, n); if (t) sb.AppendLine("  g3 " + n + " pos=" + t.position.ToString("F3") + " dist to LH=" + Vector3.Distance(t.position, lh.position).ToString("F3")); }
        }
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(Src), "align.txt"), sb.ToString(), Encoding.UTF8);
        Debug.Log("FPS_ALIGN_DONE");
    }

    public static void Run() { AssetDatabase.Refresh(); BuildMaterials(); Probe(); }
}
