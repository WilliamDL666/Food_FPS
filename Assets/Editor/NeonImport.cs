using UnityEditor;
using UnityEngine;

// Import rules for the downloaded CC0 assets: characters and the Kenney animation clips
// are imported as Humanoid so the clips retarget onto every character model.
public class NeonImport : AssetPostprocessor
{
    void OnPreprocessModel()
    {
        var importer = (ModelImporter)assetImporter;
        if (assetPath.Contains("/Models/Characters/") || assetPath.Contains("/Models/Anim/"))
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = assetPath.Contains("/Models/Anim/");
        }
        else if (assetPath.Contains("/Models/"))
        {
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            // GunModel reads vertices at runtime to find the muzzle end.
            importer.isReadable = assetPath.Contains("/Models/Weapons/");
        }
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
    }

    void OnPreprocessAnimation()
    {
        if (!assetPath.Contains("/Models/Anim/")) return;
        var importer = (ModelImporter)assetImporter;
        var clips = importer.defaultClipAnimations;
        foreach (var c in clips)
        {
            c.loopTime = !assetPath.EndsWith("jump.fbx");
            c.lockRootRotation = c.lockRootHeightY = c.lockRootPositionXZ = true;
            c.keepOriginalOrientation = c.keepOriginalPositionY = c.keepOriginalPositionXZ = true;
        }
        importer.clipAnimations = clips;
    }

    void OnPreprocessTexture()
    {
        var importer = (TextureImporter)assetImporter;
        if (assetPath.EndsWith("_normal.jpg")) importer.textureType = TextureImporterType.NormalMap;
        if (assetPath.EndsWith(".hdr"))
        {
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
        }
        if (assetPath.Contains("/Textures/")) importer.maxTextureSize = 1024;
        if (assetPath.Contains("/FPSRaw/"))
        {
            // FPSSample (HDRP) textures used through Built-in Standard materials
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Compressed;
            string n = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            if (n.Contains("Normal")) importer.textureType = TextureImporterType.NormalMap;
            if (n.Contains("Mask")) importer.sRGBTexture = false;
        }
        if (assetPath.Contains("/Textures/Decals/")) { importer.alphaIsTransparency = true; importer.wrapMode = TextureWrapMode.Clamp; }
    }
}

// glTFast 6.14 trips the job-safety checker when importing skinned glTF characters in Unity 2022.3.54
// ("SortAndNormalizeBoneWeightsJob ... must call JobHandle.Complete"). Turning the editor-only job
// debugger off before any import lets Models/Chars2/*.glb import cleanly; players are unaffected.
[InitializeOnLoad]
static class NeonGltfImportFix
{
    static NeonGltfImportFix() { Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled = false; }
}
