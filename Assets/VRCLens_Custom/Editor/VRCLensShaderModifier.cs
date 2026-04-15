#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Integrates shader patches with VRCLens at build time.
/// Patches the shader (with enabled mods) and updates the ScreenOverride material.
/// </summary>
public class VRCLensShaderModifier
{
    private const string LOG_PREFIX = "[VRCLensShaderModifier]";

    /// <summary>
    /// Patches the shader with the specified mods enabled,
    /// then updates the VRCLens material to use the patched shader.
    /// Returns the output shader path, or null on failure.
    /// </summary>
    public static string CopyAndModifyShader(VRCLens vrclens, string tempDir,
        bool enableLowerMinFocus, bool enableManualFocusAssist, bool enableGhostFX = false, bool enableChromaticAberration = false, bool enableFilmGrain = false, bool enableDepthFog = false, bool enableTiltShift = false)
    {
        Shader patchedShader = VRCLensShaderPatcher.PatchShader(enableLowerMinFocus, enableManualFocusAssist, enableGhostFX, enableChromaticAberration, enableFilmGrain, enableDepthFog, enableTiltShift);
        if (patchedShader == null)
        {
            Debug.LogError($"{LOG_PREFIX} Failed to patch shader.");
            return null;
        }

        Debug.Log($"{LOG_PREFIX} Using patched shader: {patchedShader.name}");

        if (!UpdateMaterialShader(vrclens, patchedShader, tempDir))
        {
            return null;
        }

        return VRCLensShaderPatcher.OUTPUT_SHADER_PATH;
    }

    /// <summary>
    /// Cleans up the generated shader after the build.
    /// </summary>
    public static void Cleanup()
    {
        VRCLensShaderPatcher.CleanupShader();
    }

    /// <summary>
    /// Updates the ScreenOverride material to use the patched shader.
    /// </summary>
    private static bool UpdateMaterialShader(VRCLens vrclens, Shader patchedShader, string tempDir)
    {
        Transform screenOverride = vrclens.GetScreenOverride();
        if (screenOverride == null)
        {
            Debug.LogError($"{LOG_PREFIX} ScreenOverride not found on VRCLens.");
            return false;
        }

        Renderer renderer = screenOverride.GetComponent<Renderer>();
        if (renderer == null || renderer.sharedMaterials.Length == 0)
        {
            Debug.LogError($"{LOG_PREFIX} Renderer or materials not found on ScreenOverride.");
            return false;
        }

        Material camMat = renderer.sharedMaterials[0];
        if (camMat == null)
        {
            Debug.LogError($"{LOG_PREFIX} CamMaterial not found on ScreenOverride.");
            return false;
        }

        // Copy the material so we don't modify the original
        string camMatPath = AssetDatabase.GetAssetPath(camMat);
        string camMatGUID = AssetDatabase.AssetPathToGUID(camMatPath);
        string modifiedCamMatPath = $"{tempDir}/{camMat.name}_{camMatGUID}_Patched.mat";

        if (!AssetDatabase.CopyAsset(camMatPath, modifiedCamMatPath))
        {
            Debug.LogError($"{LOG_PREFIX} Failed to copy CamMaterial from {camMatPath} to {modifiedCamMatPath}");
            return false;
        }

        Material modifiedCamMat = AssetDatabase.LoadAssetAtPath<Material>(modifiedCamMatPath);
        if (modifiedCamMat == null)
        {
            Debug.LogError($"{LOG_PREFIX} Failed to load copied CamMaterial at: {modifiedCamMatPath}");
            return false;
        }

        // Update the shader on the copied material
        modifiedCamMat.shader = patchedShader;
        Debug.Log($"{LOG_PREFIX} Updated CamMaterial shader to: {modifiedCamMat.shader.name}");

        // Replace the material on the renderer
        Material[] materials = renderer.sharedMaterials;
        materials[0] = modifiedCamMat;
        renderer.sharedMaterials = materials;

        Debug.Log($"{LOG_PREFIX} Replaced CamMaterial on ScreenOverride with: {modifiedCamMatPath}");
        return true;
    }
}
#endif
