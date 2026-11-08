using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

// Generates every asset the runtime expects (materials, animator, post-processing, scene)
// and builds the Windows player. Menu: Neon Siege/...  Batch: -executeMethod BuildDemo.SetupAndBuild
public static class BuildDemo
{
    const string Scene = "Assets/Scenes/NeonArena.unity";

    [MenuItem("Neon Siege/Open Demo Scene")]
    public static void OpenScene() { EditorSceneManager.OpenScene(Scene); }

    [MenuItem("Neon Siege/Rebuild Generated Assets")]
    public static void Setup()
    {
        Directory.CreateDirectory("Assets/Resources/Materials");
        Directory.CreateDirectory("Assets/Resources/Anim");
        Directory.CreateDirectory("Assets/Settings");
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.productName = "霓城封锁 Neon Lockdown";
        PlayerSettings.companyName = "NeonSiege";
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
        PlayerSettings.resizableWindow = true;
        AssetDatabase.Refresh();

        Triplanar("Asphalt", "asphalt_04", 5f, .08f, 0f, new Color(.42f, .42f, .42f), .5f);
        Triplanar("Brick", "brick_wall_10", 3.2f, .12f, 0f, new Color(.8f, .72f, .7f), 1.3f);
        Triplanar("ConcretePanels", "concrete_panels", 4f, .15f, 0f, new Color(.5f, .5f, .5f), 1.2f);
        Triplanar("MetalSheet", "box_profile_metal_sheet", 2.4f, .28f, .3f, Color.white, 1.4f);
        Triplanar("Pavement", "concrete_pavement", 2.5f, .12f, 0f, new Color(.55f, .54f, .53f), .8f);
        var puddle = Mat("Puddle", Shader.Find("Standard"));
        puddle.color = new Color(.02f, .025f, .035f); puddle.SetFloat("_Glossiness", .62f); puddle.SetFloat("_Metallic", 0f); EditorUtility.SetDirty(puddle);
        var gunMetal = Mat("GunMetal", Shader.Find("Standard"));
        gunMetal.color = new Color(.5f, .52f, .56f); gunMetal.SetFloat("_Glossiness", .32f); gunMetal.SetFloat("_Metallic", .15f);
        gunMetal.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Textures/metal_plate_normal.jpg")); gunMetal.EnableKeyword("_NORMALMAP");
        EditorUtility.SetDirty(gunMetal);
        var smoke = Mat("FxSmoke", Shader.Find("Legacy Shaders/Particles/Alpha Blended"));
        smoke.mainTexture = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
        smoke.SetColor("_TintColor", new Color(.5f, .5f, .5f, .5f)); EditorUtility.SetDirty(smoke);
        var hex = Mat("Hex", Shader.Find("NeonSiege/Hex"));
        hex.SetColor("_Color", new Color(.3f, .9f, 1f)); EditorUtility.SetDirty(hex);
        var rim = Mat("Rim", Shader.Find("NeonSiege/Rim"));
        rim.SetColor("_Color", new Color(1f, .2f, .2f)); EditorUtility.SetDirty(rim);
        var xray = Mat("XRay", Shader.Find("NeonSiege/XRay"));
        xray.SetColor("_Color", new Color(1f, .2f, .2f)); EditorUtility.SetDirty(xray);
        Triplanar("Concrete", "concrete_wall_008", 3f, .1f, 0f, new Color(.48f, .48f, .47f), 1f);
        Triplanar("ConcreteFloor", "concrete_floor_worn_001", 3.5f, .2f, 0f, new Color(.75f, .77f, .82f), 1f);
        Triplanar("Corrugated", "corrugated_iron_02", 2.2f, .3f, .3f, new Color(.55f, .62f, .7f), 1.2f);
        Triplanar("MetalPlate", "metal_plate", 2f, .35f, .4f, new Color(.7f, .72f, .78f), 1f);
        Triplanar("Rusty", "rusty_metal_02", 2.5f, .3f, .4f, Color.white, 1f);

        // V8：远景楼用 ambientCG 照片立面；老墙用剥落灰泥
        Triplanar("SkylineA", "facade018a", 12f, .12f, 0f, new Color(.78f, .76f, .74f), 1f);
        Triplanar("SkylineB", "facade019a", 12f, .2f, 0f, new Color(.8f, .8f, .82f), 1f);
        Triplanar("SkylineC", "facade020a", 12f, .15f, 0f, new Color(.8f, .78f, .76f), 1f);
        Triplanar("SkylineD", "facade020b", 12f, .15f, 0f, new Color(.8f, .78f, .76f), 1f);
        Triplanar("Plaster", "paintedplaster006", 3.5f, .08f, 0f, new Color(.7f, .68f, .64f), 1f);
        System.IO.Directory.CreateDirectory("Assets/Resources/Materials/Decals");
        foreach (var path in System.IO.Directory.GetFiles("Assets/Resources/Textures/Decals", "*.png"))
        {
            string n = System.IO.Path.GetFileNameWithoutExtension(path);
            var dm = Mat("Decals/" + n, Shader.Find("Legacy Shaders/Transparent/Diffuse"));
            dm.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path.Replace('\\', '/')); dm.color = Color.white;
            EditorUtility.SetDirty(dm);
        }

        var neon = Mat("Neon", Shader.Find("Standard"));
        neon.color = new Color(.1f, .1f, .1f); neon.EnableKeyword("_EMISSION");
        neon.SetColor("_EmissionColor", new Color(.1f, .9f, 1f) * 3f);
        neon.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        EditorUtility.SetDirty(neon);

        var worldText = Mat("WorldText", Shader.Find("NeonSiege/WorldText")); EditorUtility.SetDirty(worldText);
        var outline = Mat("Outline", Shader.Find("NeonSiege/Outline")); EditorUtility.SetDirty(outline);
        var holo = Mat("Holo", Shader.Find("NeonSiege/Holo"));
        holo.SetColor("_Color", new Color(.3f, .9f, 1f) * 1.2f); EditorUtility.SetDirty(holo);

        var fx = Mat("FxAdditive", Shader.Find("Legacy Shaders/Particles/Additive"));
        fx.mainTexture = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
        fx.SetColor("_TintColor", new Color(.5f, .5f, .5f, .5f)); EditorUtility.SetDirty(fx);

        // V14：晴朗的卡通天空
        AssetDatabase.DeleteAsset("Assets/Resources/Materials/Skybox.mat");
        var sky = Mat("Skybox", Shader.Find("Skybox/Procedural"));
        sky.SetColor("_SkyTint", new Color(.42f, .62f, 1f)); sky.SetColor("_GroundColor", new Color(.55f, .6f, .66f));
        sky.SetFloat("_AtmosphereThickness", .8f); sky.SetFloat("_Exposure", 1.25f); sky.SetFloat("_SunSize", .05f); sky.SetFloat("_SunSizeConvergence", 6f);
        EditorUtility.SetDirty(sky);

        BuildAnimator();
        var profile = BuildPostProfile();
        BuildScene(sky, profile);
        AssetDatabase.SaveAssets();
        Debug.Log("NEON_SETUP_OK");
    }

    static Material Mat(string name, Shader shader)
    {
        string path = "Assets/Resources/Materials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        else m.shader = shader;
        return m;
    }

    // V14 Q 版画风：所有场景材质改成干净的纯色（不用照片贴图），颜色明亮饱和
    static readonly System.Collections.Generic.Dictionary<string, Color> ToonPalette = new System.Collections.Generic.Dictionary<string, Color> {
        { "Asphalt", new Color(.6f, .58f, .8f) }, { "Brick", new Color(1f, .56f, .5f) }, { "ConcretePanels", new Color(.56f, .78f, 1f) },   // V16：糖豆人式糖果色
        { "MetalSheet", new Color(.38f, .88f, .8f) }, { "Pavement", new Color(1f, .8f, .86f) }, { "Concrete", new Color(1f, .93f, .8f) },
        { "ConcreteFloor", new Color(.86f, .84f, .98f) }, { "Corrugated", new Color(1f, .82f, .26f) }, { "MetalPlate", new Color(.74f, .7f, .95f) },
        { "Rusty", new Color(1f, .6f, .35f) }, { "SkylineA", new Color(1f, .78f, .6f) }, { "SkylineB", new Color(.62f, .82f, 1f) },
        { "SkylineC", new Color(.9f, .72f, 1f) }, { "SkylineD", new Color(.66f, .94f, .76f) }, { "Plaster", new Color(1f, .9f, .74f) } };
    static void Triplanar(string name, string tex, float scale, float gloss, float metal, Color tint, float bump)
    {
        if (ToonPalette.TryGetValue(name, out var toon))
        {
            var tm = Mat(name, Shader.Find("NeonSiege/Triplanar"));
            tm.SetTexture("_MainTex", null); tm.SetTexture("_BumpMap", null);
            tm.SetFloat("_Scale", scale); tm.SetFloat("_Glossiness", .32f); tm.SetFloat("_Metallic", 0f); tm.SetFloat("_BumpScale", 0f); tm.SetColor("_Color", toon);
            EditorUtility.SetDirty(tm);
            return;
        }
        var m = Mat(name, Shader.Find("NeonSiege/Triplanar"));
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Textures/" + tex + "_albedo.jpg"));
        m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Textures/" + tex + "_normal.jpg"));
        m.SetFloat("_Scale", scale); m.SetFloat("_Glossiness", gloss); m.SetFloat("_Metallic", metal);
        m.SetFloat("_BumpScale", bump); m.SetColor("_Color", tint);
        EditorUtility.SetDirty(m);
    }

    static AnimationClip Clip(string file, string contains)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath("Assets/Resources/Models/Anim/" + file))
            if (o is AnimationClip c && c.name.Contains(contains) && !c.name.StartsWith("__")) return c;
        throw new System.Exception("Animation clip not found: " + file + " " + contains);
    }

    static void BuildAnimator()
    {
        string path = "Assets/Resources/Anim/Fighter.controller";
        AssetDatabase.DeleteAsset(path);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
        ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
        ctrl.AddParameter("MoveDir", AnimatorControllerParameterType.Float);
        ctrl.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
        var layers = ctrl.layers; layers[0].iKPass = true; ctrl.layers = layers;
        var sm = ctrl.layers[0].stateMachine;
        var loco = ctrl.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
        tree.blendParameter = "Speed";
        tree.AddChild(Clip("idle.fbx", "Idle"), 0f);
        tree.AddChild(Clip("run.fbx", "Run"), 1f);
        loco.speedParameterActive = true; loco.speedParameter = "MoveDir";
        var jump = sm.AddState("Jump"); jump.motion = Clip("jump.fbx", "Jump");
        var toJump = loco.AddTransition(jump); toJump.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded"); toJump.duration = .1f; toJump.hasExitTime = false;
        var toLoco = jump.AddTransition(loco); toLoco.AddCondition(AnimatorConditionMode.If, 0, "Grounded"); toLoco.duration = .12f; toLoco.hasExitTime = false;
        sm.defaultState = loco;
        EditorUtility.SetDirty(ctrl);
    }

    static PostProcessProfile BuildPostProfile()
    {
        string path = "Assets/Settings/NeonPost.asset";
        AssetDatabase.DeleteAsset(path);
        var profile = ScriptableObject.CreateInstance<PostProcessProfile>();
        AssetDatabase.CreateAsset(profile, path);
        var bloom = profile.AddSettings<Bloom>();
        bloom.intensity.Override(.5f); bloom.threshold.Override(1.3f); bloom.softKnee.Override(.5f); bloom.diffusion.Override(6f);
        var grade = profile.AddSettings<ColorGrading>();
        // V6：写实军事风调色（类三角洲：低饱和、偏暖的黄昏，压暗高光）
        grade.tonemapper.Override(Tonemapper.Neutral); grade.postExposure.Override(.25f);   // V14：鲜艳明快的卡通调色
        grade.temperature.Override(2f); grade.saturation.Override(18f); grade.contrast.Override(10f);
        var ao = profile.AddSettings<AmbientOcclusion>();
        ao.mode.Override(AmbientOcclusionMode.MultiScaleVolumetricObscurance); ao.intensity.Override(.55f); ao.ambientOnly.Override(false);   // ambient-only AO occasionally zeroed all indirect light
        var vig = profile.AddSettings<Vignette>(); vig.intensity.Override(.18f); vig.smoothness.Override(.45f);
        var ca = profile.AddSettings<ChromaticAberration>(); ca.intensity.Override(0f);
        foreach (var s in profile.settings) { s.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; AssetDatabase.AddObjectToAsset(s, profile); }
        EditorUtility.SetDirty(profile);
        return profile;
    }

    static void BuildScene(Material sky, PostProcessProfile profile)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.allowHDR = true; cam.renderingPath = RenderingPath.DeferredShading; cam.nearClipPlane = .03f; cam.farClipPlane = 400; cam.fieldOfView = 78;
        cam.clearFlags = CameraClearFlags.Skybox;
        camGo.AddComponent<AudioListener>();
        camGo.transform.position = new Vector3(-30, 18, -24); camGo.transform.LookAt(Vector3.zero);
        var layer = camGo.AddComponent<PostProcessLayer>();
        layer.Init(AssetDatabase.LoadAssetAtPath<PostProcessResources>("Packages/com.unity.postprocessing/PostProcessing/PostProcessResources.asset"));
        layer.volumeTrigger = camGo.transform; layer.volumeLayer = 1;
        layer.antialiasingMode = PostProcessLayer.Antialiasing.SubpixelMorphologicalAntialiasing;
        layer.fog.enabled = true;
        var volGo = new GameObject("Post Volume");
        var vol = volGo.AddComponent<PostProcessVolume>(); vol.isGlobal = true; vol.sharedProfile = profile; vol.priority = 1;
        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = .003f;
        RenderSettings.fogColor = new Color(.72f, .82f, .96f);
        Lightmapping.giWorkflowMode = Lightmapping.GIWorkflowMode.OnDemand;
        EditorSceneManager.SaveScene(scene, Scene);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scene, true) };
    }

    public static void Validate()
    {
        bool scripts = System.Type.GetType("NeonSiege.Match, Assembly-CSharp") != null;
        if (!scripts) throw new System.Exception("NeonSiege.Match was not compiled");
        Debug.Log("NEON_SIEGE_VALIDATION_OK");
    }

    [MenuItem("Neon Siege/Build Windows Demo")]
    public static void BuildWindows()
    {
        string path = "Build/NeonSiegeDemo.exe";
        Directory.CreateDirectory("Build");
        BuildReport report = BuildPipeline.BuildPlayer(new[] { Scene }, path, BuildTarget.StandaloneWindows64, BuildOptions.None);
        if (report.summary.result != BuildResult.Succeeded) throw new System.Exception("Windows build failed: " + report.summary.result);
        Debug.Log("NEON_BUILD_OK " + path + " " + (report.summary.totalSize / 1048576f).ToString("0.0") + " MB");
    }

    public static void SetupAndBuild() { Setup(); Validate(); BakeArena.Bake(); BuildWindows(); }
}
