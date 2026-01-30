#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Generates a modified DepthOfField shader that supports lower minimum focus distances down to 1 cm.
/// The shader is generated at build time to the same folder as the original (for include paths),
/// then cleaned up after the build completes.
/// </summary>
public static class LowerMinFocusShaderGenerator
{
    private const string LOG_PREFIX = "[LowerMinFocusShaderGenerator]";
    
    // Paths - Output to same folder as original so relative #include paths resolve correctly
    private const string ORIGINAL_SHADER_PATH = "Assets/Hirabiki/VRCLens/Resource/DepthOfField.shader";
    public const string OUTPUT_SHADER_PATH = "Assets/Hirabiki/VRCLens/Resource/DepthOfFieldLowerMinFocus.shader";
    
    // Shader name used for Shader.Find()
    public const string LOWER_MIN_FOCUS_SHADER_NAME = "Hirabiki/VRCLens/DepthOfField Cutout LowerMinFocus";
    
    // The replacements to make in the shader
    private static readonly List<ShaderReplacement> Replacements = new List<ShaderReplacement>
    {
        // Rename the shader to avoid conflicts with the original
        new ShaderReplacement(
            "Shader \"Hirabiki/VRCLens/DepthOfField Cutout\"",
            "Shader \"Hirabiki/VRCLens/DepthOfField Cutout LowerMinFocus\"",
            "shader name (must be unique)"
        ),
        // Line 6: Property range - allow Unity slider to go down to 1 cm
        new ShaderReplacement(
            "_FocusDistance (\"Focus (m)\", range(0.5, 100)) = 1.5",
            "_FocusDistance (\"Focus (m)\", range(0.01, 100)) = 1.5",
            "property range minimum"
        ),
        // Line ~198: Pass 1 auto-focus threshold
        new ShaderReplacement(
            "bool isAutoFocus = _FocusDistance < 0.5001;",
            "bool isAutoFocus = _FocusDistance < 0.0001;",
            "Pass 1 auto-focus threshold"
        ),
        // Line ~594: UI display condition
        new ShaderReplacement(
            ": uv.y < 0.25 && _FocusDistance > 0.5001 ? col.a",
            ": uv.y < 0.25 && _FocusDistance > 0.0001 ? col.a",
            "UI display condition"
        ),
        // Line ~735: MF/AF indicator
        new ShaderReplacement(
            ": y < 2 ? _FocusDistance > 0.5001",
            ": y < 2 ? _FocusDistance > 0.0001",
            "MF/AF indicator condition"
        ),
    };
    
    private struct ShaderReplacement
    {
        public string OldText;
        public string NewText;
        public string Description;
        
        public ShaderReplacement(string oldText, string newText, string description)
        {
            OldText = oldText;
            NewText = newText;
            Description = description;
        }
    }

    /// <summary>
    /// Menu item to manually test shader generation.
    /// </summary>
    [MenuItem("Tools/VRCLens Custom/Debug/Test Generate LowerMinFocus Shader")]
    public static void GenerateFromMenu()
    {
        Shader shader = GenerateShader();
        if (shader != null)
        {
            EditorUtility.DisplayDialog("LowerMinFocus Shader Generator", 
                $"Shader generated successfully at:\n{OUTPUT_SHADER_PATH}\n\nShader: {shader.name}", "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("LowerMinFocus Shader Generator", 
                "Failed to generate shader. Check console for errors.", "OK");
        }
    }
    
    /// <summary>
    /// Menu item to clean up generated shader.
    /// </summary>
    [MenuItem("Tools/VRCLens Custom/Debug/Cleanup LowerMinFocus Shader")]
    public static void CleanupFromMenu()
    {
        CleanupShader();
        EditorUtility.DisplayDialog("LowerMinFocus Shader Generator", 
            "Cleanup complete.", "OK");
    }
    
    /// <summary>
    /// Generates the LowerMinFocus shader. Returns the Shader, or null if generation failed.
    /// </summary>
    public static Shader GenerateShader()
    {
        // Find the original shader file
        if (!File.Exists(ORIGINAL_SHADER_PATH))
        {
            Debug.LogError($"{LOG_PREFIX} Original DepthOfField shader not found at: {ORIGINAL_SHADER_PATH}");
            return null;
        }
        
        // Read the original shader content
        string shaderContent = File.ReadAllText(ORIGINAL_SHADER_PATH);
        
        // Verify all expected patterns exist before making any changes
        List<string> missingPatterns = new List<string>();
        foreach (var replacement in Replacements)
        {
            if (!shaderContent.Contains(replacement.OldText))
            {
                missingPatterns.Add($"  - {replacement.Description}: \"{replacement.OldText}\"");
            }
        }
        
        if (missingPatterns.Count > 0)
        {
            string errorMsg = $"Cannot modify DepthOfField shader - expected patterns not found.\n" +
                $"VRCLens may have been updated with incompatible changes.\n\n" +
                $"Missing patterns:\n{string.Join("\n", missingPatterns)}";
            Debug.LogError($"{LOG_PREFIX} {errorMsg}");
            return null;
        }
        
        // Apply all replacements
        string modifiedContent = shaderContent;
        int totalReplacements = 0;
        
        foreach (var replacement in Replacements)
        {
            int countBefore = CountOccurrences(modifiedContent, replacement.OldText);
            modifiedContent = modifiedContent.Replace(replacement.OldText, replacement.NewText);
            totalReplacements += countBefore;
        }
        
        Debug.Log($"{LOG_PREFIX} Applied {totalReplacements} shader modifications");
        
        // Add header comment
        string headerComment = $"// VRCLens LowerMinFocus Shader - Auto-generated by VRCLens Custom\n" +
            $"// Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
            $"// This file is auto-generated at build time and cleaned up afterwards.\n\n";
        modifiedContent = headerComment + modifiedContent;
        
        // Write the modified shader
        File.WriteAllText(OUTPUT_SHADER_PATH, modifiedContent);
        
        // Import the shader
        AssetDatabase.ImportAsset(OUTPUT_SHADER_PATH, ImportAssetOptions.ForceSynchronousImport);
        
        // Load and verify the shader
        Shader generatedShader = Shader.Find(LOWER_MIN_FOCUS_SHADER_NAME);
        if (generatedShader == null)
        {
            Debug.LogError($"{LOG_PREFIX} Failed to load generated shader by name: {LOWER_MIN_FOCUS_SHADER_NAME}");
            return null;
        }
        
        if (!generatedShader.isSupported)
        {
            Debug.LogError($"{LOG_PREFIX} Generated shader failed to compile or is not supported.");
            return null;
        }
        
        Debug.Log($"{LOG_PREFIX} LowerMinFocus shader generated at: {OUTPUT_SHADER_PATH}");
        return generatedShader;
    }
    
    /// <summary>
    /// Cleans up the generated shader file.
    /// </summary>
    public static void CleanupShader()
    {
        if (File.Exists(OUTPUT_SHADER_PATH))
        {
            AssetDatabase.DeleteAsset(OUTPUT_SHADER_PATH);
            Debug.Log($"{LOG_PREFIX} Cleaned up LowerMinFocus shader at: {OUTPUT_SHADER_PATH}");
        }
        
        // Also delete meta file if it exists
        string metaPath = OUTPUT_SHADER_PATH + ".meta";
        if (File.Exists(metaPath))
        {
            File.Delete(metaPath);
        }
    }
    
    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
#endif
