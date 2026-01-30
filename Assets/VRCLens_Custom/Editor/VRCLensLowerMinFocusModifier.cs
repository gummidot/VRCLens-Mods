#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Modifies the VRCLens DepthOfField shader to support lower minimum focus distances.
/// 
/// The original shader uses 0.5001 as a magic threshold to toggle auto-focus mode.
/// When _FocusDistance drops below 0.5001, it switches from manual focus to auto-focus.
/// This modifier changes that threshold to 0.0001 to allow manual focus down to near 0m,
/// with auto-focus only triggering at effectively 0m.
/// 
/// The modified shader is generated at build time and cleaned up afterwards.
/// </summary>
public class VRCLensLowerMinFocusModifier
{
    private const string LOG_PREFIX = "[VRCLensLowerMinFocusModifier]";

    /// <summary>
    /// Applies the LowerMinFocus shader to the VRCLens camera material.
    /// Returns the path to the modified shader, or null if modification failed.
    /// </summary>
    public static string CopyAndModifyShader(VRCLens vrclens, string tempDir)
    {
        // Generate the LowerMinFocus shader
        Shader lowerMinFocusShader = LowerMinFocusShaderGenerator.GenerateShader();
        if (lowerMinFocusShader == null)
        {
            Debug.LogError($"{LOG_PREFIX} Failed to generate LowerMinFocus shader.");
            return null;
        }
        
        Debug.Log($"{LOG_PREFIX} Using LowerMinFocus shader: {lowerMinFocusShader.name}");
        
        // Update the ScreenOverride material to use the LowerMinFocus shader
        if (!UpdateMaterialShader(vrclens, lowerMinFocusShader, tempDir))
        {
            return null;
        }
        
        // Return the shader asset path
        return LowerMinFocusShaderGenerator.OUTPUT_SHADER_PATH;
    }
    
    /// <summary>
    /// Cleans up the generated shader after the build.
    /// </summary>
    public static void Cleanup()
    {
        LowerMinFocusShaderGenerator.CleanupShader();
    }
    
    /// <summary>
    /// Updates the ScreenOverride material to use the modified shader.
    /// </summary>
    private static bool UpdateMaterialShader(VRCLens vrclens, Shader lowerMinFocusShader, string tempDir)
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
        
        // Copy the material using AssetDatabase.CopyAsset
        string camMatPath = AssetDatabase.GetAssetPath(camMat);
        string camMatGUID = AssetDatabase.AssetPathToGUID(camMatPath);
        string modifiedCamMatPath = $"{tempDir}/{camMat.name}_{camMatGUID}_LowerMinFocus.mat";
        
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
        modifiedCamMat.shader = lowerMinFocusShader;
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
