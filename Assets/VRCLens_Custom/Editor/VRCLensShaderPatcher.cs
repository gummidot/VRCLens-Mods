#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Unified shader patcher for VRCLens Custom.
/// Applies all shader modifications (LowerMinFocus and ManualFocusAssist) in a single pass.
///
/// LowerMinFocus uses text replacements to lower the auto-focus threshold from 0.5m to ~0m.
/// ManualFocusAssist uses anchor-based insertion to inject focus-assist code blocks.
///
/// Applying both in one pass ensures consistency — e.g. the LowerMinFocus threshold
/// replacements also apply to code blocks injected by ManualFocusAssist.
///
/// ════════════════════════════════════════════════════════════════════════
/// MAINTENANCE: When VRCLens updates to a new version
/// ════════════════════════════════════════════════════════════════════════
///
/// The original unmodified VRCLens shader lives at ORIGINAL_SHADER_PATH.
/// Re-importing the VRCLens package restores this to the stock version.
/// The patched reference (.patched) is saved alongside it in the same
/// gitignored directory. The VRCLens version is read from readme_DoNotDelete.txt.
///
/// 1. Re-import the new VRCLens package (restores the original shader).
///
/// 2. Run  Tools > VRCLens Custom > Reference > Verify Patcher
///    - If it passes, the new VRCLens version is compatible. Done.
///    - If it fails, it lists which anchor lines are missing or changed.
///
/// 3. To see what VRCLens changed between versions (if you kept the old .patched):
///      diff DepthOfField.shader.patched DepthOfField.shader  (in Resource/)
///    The .patched file shows the old baseline + our insertions.
///    The new DepthOfField.shader is the new baseline.
///
/// 4. Fix the broken ANCHOR_* constants and/or BLOCK_* code below.
///    Update VRCLENS_TESTED_VERSION to match the new version.
///
/// 5. Run  Tools > VRCLens Custom > Reference > Generate Patched Reference
///    Patches the original shader and saves as .patched (the new gold standard).
///
/// 6. Run  Tools > VRCLens Custom > Reference > Verify Patcher
///    Confirms the patcher reproduces .patched exactly.
///
/// All reference files are in the gitignored Assets/Hirabiki/ directory.
/// ════════════════════════════════════════════════════════════════════════
/// </summary>
public static class VRCLensShaderPatcher
{
    private const string LOG_PREFIX = "[VRCLensShaderPatcher]";

    public const string ORIGINAL_SHADER_PATH = "Assets/Hirabiki/VRCLens/Resource/DepthOfField.shader";
    public const string OUTPUT_SHADER_PATH = "Assets/Hirabiki/VRCLens/Resource/DepthOfFieldPatched.shader";

    // Patched reference — saved alongside the original shader in the same gitignored dir.
    // The original shader IS the baseline (re-import VRCLens package to restore it).
    private const string PATCHED_REF_PATH = "Assets/Hirabiki/VRCLens/Resource/DepthOfField.shader.patched";

    // VRCLens version tracking
    private const string VRCLENS_README_PATH = "Assets/Hirabiki/VRCLens/readme_DoNotDelete.txt";
    public const string VRCLENS_TESTED_VERSION = "1.9.2";

    private const string PATCH_MARKER = "VRCLens_Custom BEGIN";

    // ═══════════════════════════════════════════════════════════════════
    // LowerMinFocus — text replacements
    // ═══════════════════════════════════════════════════════════════════

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
    /// LowerMinFocus replacements. The original shader uses 0.5001 as a magic threshold
    /// to toggle auto-focus mode. We change it to 0.0001 to allow manual focus down to ~0m.
    /// The shader name replacement is NOT included here — handled by RenameShader().
    /// </summary>
    private static readonly List<ShaderReplacement> LowerMinFocusReplacements = new List<ShaderReplacement>
    {
        // Property range — allow Unity slider to go down to 1 cm
        new ShaderReplacement(
            "_FocusDistance (\"Focus (m)\", range(0.5, 100)) = 1.5",
            "_FocusDistance (\"Focus (m)\", range(0.01, 100)) = 1.5",
            "property range minimum"
        ),
        // Pass 1 auto-focus threshold
        new ShaderReplacement(
            "bool isAutoFocus = _FocusDistance < 0.5001;",
            "bool isAutoFocus = _FocusDistance < 0.0001;",
            "Pass 1 auto-focus threshold"
        ),
        // UI display condition
        new ShaderReplacement(
            ": uv.y < 0.25 && _FocusDistance > 0.5001 ? col.a",
            ": uv.y < 0.25 && _FocusDistance > 0.0001 ? col.a",
            "UI display condition"
        ),
        // MF/AF indicator
        new ShaderReplacement(
            ": y < 2 ? _FocusDistance > 0.5001",
            ": y < 2 ? _FocusDistance > 0.0001",
            "MF/AF indicator condition"
        ),
    };

    // ═══════════════════════════════════════════════════════════════════
    // ManualFocusAssist — anchor-based insertion
    // ═══════════════════════════════════════════════════════════════════

    // ── Anchor definitions ──────────────────────────────────────────────
    // Each anchor is a known stable line in the original VRCLens shader.
    // We find it and insert our code block immediately after it.

    // Site 1a: Properties block — insert after _DoFStrength line
    private const string ANCHOR_PROPERTIES = "_DoFStrength (\"F-number\"";

    // Site 1b: Texture property — insert after _FocusTex line in Properties
    private const string ANCHOR_TEXTURE_PROPERTY = "_FocusTex (\"Focus Point Texture\"";

    // Site 2: Pass 2 uniforms — insert after _SensorScale line
    private const string ANCHOR_PASS2_UNIFORMS = "uniform float _SensorScale, _AspectRatio;";

    // Site 3: Pass 2 helper functions — insert after the "//" line following DoF_LensShape.cginc include
    // We match "#include \"DoF_LensShape.cginc\"" then skip to the "//" line after it
    private const string ANCHOR_LENSSHAPE_INCLUDE = "#include \"DoF_LensShape.cginc\"";

    // Site 4a: centerSize — insert after this line
    private const string ANCHOR_CENTER_SIZE = "half centerSize = abs(color.a);";

    // Site 4b: sampleSize — insert after this line (partial match)
    private const string ANCHOR_SAMPLE_SIZE = "float sampleSize = lerp(abs(sampled.a)";

    // Site 5a: Pass 6 samplers — insert after this line
    private const string ANCHOR_PASS6_SAMPLERS = "sampler2D _FocusTex, _SymbolTex0";

    // Site 5b: Pass 6 uniforms — insert after the _FocusDistance line in Pass 6
    // This is the SECOND occurrence of this pattern (first is Pass 1/2 area)
    private const string ANCHOR_PASS6_UNIFORMS = "uniform float _FocusDistance, _DoFStrength";

    // Site 7: Debug visualization — insert after the focus peaking line
    private const string ANCHOR_FOCUS_PEAKING = "lerp(col, _FocusPeakingColor, edgeDetect(";

    // ═══════════════════════════════════════════════════════════════════
    // GhostFX — anchor-based insertion (Pass 2 / final composition)
    // ═══════════════════════════════════════════════════════════════════

    // Site 8a: Properties block — reuses ANCHOR_PROPERTIES (same insertion point as MFA)
    // Site 8b: Pass 2 uniforms — after white balance line (before tone mapping)
    private const string ANCHOR_GHOSTFX_UNIFORMS = "col.rgb *= colorTemp(-_WhiteBalance);";

    // Site 8c: Ghost FX application — after white balance, before tone mapping
    // Uses same anchor as 8b, function + call injected together

    // ── GhostFX code blocks ─────────────────────────────────────────────

    private static readonly string BLOCK_GHOSTFX_PROPERTIES = @"
		// VRCLens_Custom BEGIN - Ghost FX Properties
		[Header(Ghost FX)]
		[Enum(Disabled,0,Split,1,Dual,2)] _GhostFXMode (""Ghost FX Mode"", float) = 0
		_GhostFXAngle (""Rotation"", Range(0.0, 1.0)) = 0.0
		_GhostFXDistance (""Distance"", Range(0.0, 0.15)) = 0.05
		_GhostFXOpacity (""Intensity"", Range(0.0, 1.0)) = 0.5
		_GhostFXSoftEdge (""Soft Edge"", Range(0.01, 0.3)) = 0.08
		_GhostFXCenterWidth (""Center Width"", Range(0.05, 0.4)) = 0.15
		// VRCLens_Custom END";

    private static readonly string BLOCK_GHOSTFX_PASS2 = @"
				// VRCLens_Custom BEGIN - Ghost FX
				if(_GhostFXMode > 0.5) {
					half2 ghostCenter = sbsUV0 - 0.5;
					float ghostAngleRad = _GhostFXAngle * 6.28318530718;
					half2 ghostDir = half2(cos(ghostAngleRad), sin(ghostAngleRad));
					float ghostProj = dot(ghostCenter, ghostDir);

					float ghostZoneMask;
					half2 ghostBaseOffset;
					if(_GhostFXMode < 1.5) {
						// Split mode
						ghostZoneMask = smoothstep(-_GhostFXSoftEdge, _GhostFXSoftEdge, ghostProj);
						ghostBaseOffset = ghostDir * _GhostFXDistance;
					} else {
						// Dual mode: center-clear
						float ghostAbsDist = abs(ghostProj);
						ghostZoneMask = smoothstep(_GhostFXCenterWidth - _GhostFXSoftEdge, _GhostFXCenterWidth + _GhostFXSoftEdge, ghostAbsDist);
						ghostBaseOffset = ghostDir * _GhostFXDistance * sign(ghostProj);
					}

					// Multiple ghost layers with decreasing opacity
					half3 ghostAccum = col.rgb;
					[unroll]
					for(int gi = 1; gi <= 3; gi++) {
						half2 gUV = clamp(sbsUV0 + ghostBaseOffset * gi, 0.001, 0.999);
						half3 gSample = tex2D(_HirabikiVRCLensPassTexture, gUV).rgb;
						gSample = max(0.00001, gSample / finalExp.x);
						gSample *= colorTemp(-_WhiteBalance);
						half3 gBlended = 1.0 - (1.0 - ghostAccum) * (1.0 - gSample);
						ghostAccum = lerp(ghostAccum, gBlended, ghostZoneMask * _GhostFXOpacity / (float)gi);
					}
					col.rgb = ghostAccum;
				}
				// VRCLens_Custom END";

    private static readonly string BLOCK_GHOSTFX_UNIFORMS = @"
			// VRCLens_Custom BEGIN - Ghost FX Uniforms
			uniform float _GhostFXMode, _GhostFXAngle, _GhostFXDistance;
			uniform float _GhostFXOpacity, _GhostFXSoftEdge, _GhostFXCenterWidth;
			// VRCLens_Custom END";

    // ── Code blocks to inject ───────────────────────────────────────────

    private static readonly string BLOCK_PROPERTIES = @"
		// VRCLens_Custom BEGIN - Manual Focus Assist Properties
		[Header(Manual Focus Assist)]
		[Enum(Disabled,0,Enabled,1,Debug,2)] _ManualFocusAssist (""Manual Focus Assist"", float) = 0
		_ManualFocusAssistStrength (""Strength"", Range(0.0, 1.0)) = 0.85
		_ManualFocusAssistFeather (""Edge Feather"", Range(0.0, 0.02)) = 0.005
		_ManualFocusAssistZoneSize (""Zone Size (m)"", Range(0.0, 20.0)) = 2.0
		_ManualFocusAssistZoneSoftness (""Zone Softness (m)"", Range(0.0, 5.0)) = 1.0
		// VRCLens_Custom END";

    private static readonly string BLOCK_TEXTURE_PROPERTY = @"
		// VRCLens_Custom BEGIN - Manual Focus Assist Texture
		_DepthAvatarTex (""Avatar Depth Texture"", 2D) = ""black"" {}
		// VRCLens_Custom END";

    private static readonly string BLOCK_PASS2_UNIFORMS = @"
			// VRCLens_Custom BEGIN - Manual Focus Assist Uniforms
			sampler2D _DepthAvatarTex;
			sampler2D _DepthTex; // Scene depth for distance calculation
			sampler2D _AuxExpTex; // For reading focus distance in relative mode
			uniform float _FocusDistance;
			uniform int _IsExternalFocus;
			uniform float _ManualFocusAssist, _ManualFocusAssistStrength, _ManualFocusAssistFeather;
			uniform float _ManualFocusAssistZoneSize, _ManualFocusAssistZoneSoftness;
			// VRCLens_Custom END";

    private static readonly string BLOCK_PASS2_HELPERS = @"
			// VRCLens_Custom BEGIN - Manual Focus Assist Helper Function
			// Convert raw depth to linear eye depth (meters)
			float getLinearEyeDepth(float rawDepth) {
				float farPlane = 32000.0;
				float nearPlane = 0.04;
				float x = farPlane / nearPlane - 1.0;
				float z = x / farPlane;
				float w = 1.0 / farPlane;
				return 1.0 / (z * rawDepth + w);
			}
			
			// Check how much an avatar at given distance is protected
			// Returns 1.0 = fully protected, 0.0 = no protection (full blur)
			float getZoneProtection(float distance) {
				// Zone around focus distance: focus ± size
				bool isAutoFocus = _FocusDistance < 0.5001;
				float focusDist = _FocusDistance;
				
				if(isAutoFocus && _IsExternalFocus == 2) {
					// Avatar AF: read from aux texture
					float storedDepth = tex2Dlod(_AuxExpTex, half4(.25,.75,.0,.0)).z * 0.00390625;
					focusDist = getLinearEyeDepth(storedDepth);
				}
				
				// Zone: focus ± size, with smooth softness at edges
				float minDist = max(0.0, focusDist - _ManualFocusAssistZoneSize);
				float maxDist = focusDist + _ManualFocusAssistZoneSize;
				float softness = max(0.001, _ManualFocusAssistZoneSoftness);
				
				// Smooth transition at both edges
				float nearFade = smoothstep(minDist - softness, minDist, distance);
				float farFade = 1.0 - smoothstep(maxDist, maxDist + softness, distance);
				
				return nearFade * farFade;
			}
			
			// Sample avatar mask with optional edge feathering
			// Returns blur multiplier: 1.0 = full blur, lower = more focus assist
			float getFocusAssistMultiplier(half2 uv) {
				if(_ManualFocusAssist == 0) return 1.0; // Disabled
				
				float avatarMask = tex2D(_DepthAvatarTex, uv).r;
				
				// No avatar at this pixel = full blur
				if(avatarMask <= 0.0) return 1.0;
				
				// Get actual scene depth for this pixel (in meters)
				float rawDepth = SAMPLE_DEPTH_TEXTURE(_DepthTex, uv);
				float distance = getLinearEyeDepth(rawDepth);
				
				// Get zone protection (1.0 = fully protected, 0.0 = no protection)
				float protection = getZoneProtection(distance);
				if(protection <= 0.0) return 1.0;
				
				// Avatar pixel within range: apply reduction factor
				float blurFactor = 1.0 - _ManualFocusAssistStrength;
				
				// Optional: sample neighbors for edge feathering
				if(_ManualFocusAssistFeather > 0.0) {
					float feather = _ManualFocusAssistFeather;
					float neighbors = 0.0;
					neighbors += tex2D(_DepthAvatarTex, uv + half2(feather, 0)).r > 0.0 ? 1.0 : 0.0;
					neighbors += tex2D(_DepthAvatarTex, uv - half2(feather, 0)).r > 0.0 ? 1.0 : 0.0;
					neighbors += tex2D(_DepthAvatarTex, uv + half2(0, feather)).r > 0.0 ? 1.0 : 0.0;
					neighbors += tex2D(_DepthAvatarTex, uv - half2(0, feather)).r > 0.0 ? 1.0 : 0.0;
					
					// Edge pixels (not all neighbors are avatar) get partial reduction
					float edgeFactor = neighbors / 4.0;
					blurFactor = lerp(1.0, 1.0 - _ManualFocusAssistStrength, edgeFactor);
				}
				
				// Blend between full blur (1.0) and target reduction based on zone protection
				return lerp(1.0, blurFactor, protection);
			}
			// VRCLens_Custom END";

    private static readonly string BLOCK_CENTER_SIZE = @"
				// VRCLens_Custom BEGIN - Manual Focus Assist (centerSize)
				float focusAssistMult = getFocusAssistMultiplier(texCoord);
				centerSize *= focusAssistMult;
				// VRCLens_Custom END";

    private static readonly string BLOCK_SAMPLE_SIZE = @"
					// VRCLens_Custom BEGIN - Manual Focus Assist (sampleSize)
					float sampleFocusAssistMult = getFocusAssistMultiplier(tc);
					sampleSize *= sampleFocusAssistMult;
					// VRCLens_Custom END";

    private static readonly string BLOCK_PASS6_SAMPLERS = @"
			// VRCLens_Custom BEGIN - Manual Focus Assist Samplers (for debug in preview)
			sampler2D _DepthAvatarTex;
			// VRCLens_Custom END";

    private static readonly string BLOCK_PASS6_UNIFORMS = @"
			// VRCLens_Custom BEGIN - Manual Focus Assist Uniforms (for debug in preview)
			uniform float _ManualFocusAssist, _ManualFocusAssistZoneSize, _ManualFocusAssistZoneSoftness;
			// VRCLens_Custom END";

    private static readonly string BLOCK_PASS6_HELPERS = @"
			// VRCLens_Custom BEGIN - Manual Focus Assist Helper Functions (for debug in preview)
			float getLinearEyeDepthFinal(float rawDepth) {
				float farPlane = 32000.0;
				float nearPlane = 0.04;
				float x = farPlane / nearPlane - 1.0;
				float z = x / farPlane;
				float w = 1.0 / farPlane;
				return 1.0 / (z * rawDepth + w);
			}
			
			float getZoneProtectionFinal(float distance) {
				bool isAutoFocus = _FocusDistance < 0.5001;
				float focusDist = _FocusDistance;
				
				if(isAutoFocus && _IsExternalFocus == 2) {
					float storedDepth = tex2Dlod(_AuxExpTex, half4(.25,.75,.0,.0)).z * 0.00390625;
					focusDist = getLinearEyeDepthFinal(storedDepth);
				}
				
				float minDist = max(0.0, focusDist - _ManualFocusAssistZoneSize);
				float maxDist = focusDist + _ManualFocusAssistZoneSize;
				float softness = max(0.001, _ManualFocusAssistZoneSoftness);
				
				float nearFade = smoothstep(minDist - softness, minDist, distance);
				float farFade = 1.0 - smoothstep(maxDist, maxDist + softness, distance);
				
				return nearFade * farFade;
			}
			// VRCLens_Custom END";

    private static readonly string BLOCK_DEBUG = @"
					// VRCLens_Custom BEGIN - Manual Focus Assist Debug (preview only)
					if(_ManualFocusAssist == 2) {
						float avatarMask = tex2D(_DepthAvatarTex, sbsUV0).r;
						if(avatarMask > 0.0) {
							float debugRawDepth = SAMPLE_DEPTH_TEXTURE(_DepthTex, sbsUV0);
							float debugDistance = getLinearEyeDepthFinal(debugRawDepth);
							float protection = getZoneProtectionFinal(debugDistance);
							// Green = protected, Red = unprotected, Yellow = transition
							half3 debugColor = lerp(half3(1.0, 0.0, 0.0), half3(0.0, 1.0, 0.0), protection);
							col.rgb = lerp(col.rgb, debugColor, 0.7);
						}
					}
					// VRCLens_Custom END";

    // ═══════════════════════════════════════════════════════════════════
    // Public API
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Patches the shader with the enabled modifications.
    /// Reads from ORIGINAL_SHADER_PATH, writes to OUTPUT_SHADER_PATH.
    /// Returns the compiled Shader, or null on failure.
    /// </summary>
    public static Shader PatchShader(bool enableLowerMinFocus, bool enableManualFocusAssist, bool enableGhostFX = false)
    {
        if (!File.Exists(ORIGINAL_SHADER_PATH))
        {
            Debug.LogError($"{LOG_PREFIX} Shader not found at: {ORIGINAL_SHADER_PATH}");
            return null;
        }

        string content = File.ReadAllText(ORIGINAL_SHADER_PATH);

        // Idempotency check
        if (content.Contains(PATCH_MARKER))
        {
            Debug.LogWarning($"{LOG_PREFIX} Original shader already contains patch markers — skipping. " +
                "Re-import VRCLens to restore the original shader.");
            return null;
        }

        // Validate ManualFocusAssist anchors if that feature is enabled
        if (enableManualFocusAssist)
        {
            var anchors = GetManualFocusAssistAnchors();
            List<string> missing = new List<string>();
            foreach (var anchor in anchors)
            {
                if (!content.Contains(anchor.SearchString))
                    missing.Add($"  - {anchor.Description}: \"{anchor.SearchString}\"");
            }
            if (missing.Count > 0)
            {
                string errorMsg = $"Cannot patch shader — expected anchor lines not found.\n" +
                    $"VRCLens may have been updated with incompatible changes.\n\n" +
                    $"Missing anchors:\n{string.Join("\n", missing)}";
                Debug.LogError($"{LOG_PREFIX} {errorMsg}");
                return null;
            }
        }

        // Validate LowerMinFocus replacement patterns if that feature is enabled
        if (enableLowerMinFocus)
        {
            List<string> missingPatterns = new List<string>();
            foreach (var replacement in LowerMinFocusReplacements)
            {
                if (!content.Contains(replacement.OldText))
                    missingPatterns.Add($"  - {replacement.Description}: \"{replacement.OldText}\"");
            }
            if (missingPatterns.Count > 0)
            {
                string errorMsg = $"Cannot patch shader — expected LowerMinFocus patterns not found.\n" +
                    $"VRCLens may have been updated with incompatible changes.\n\n" +
                    $"Missing patterns:\n{string.Join("\n", missingPatterns)}";
                Debug.LogError($"{LOG_PREFIX} {errorMsg}");
                return null;
            }
        }

        // Validate GhostFX anchors if that feature is enabled
        if (enableGhostFX)
        {
            var ghostAnchors = GetGhostFXAnchors();
            List<string> missingGhost = new List<string>();
            foreach (var anchor in ghostAnchors)
            {
                if (!content.Contains(anchor.SearchString))
                    missingGhost.Add($"  - {anchor.Description}: \"{anchor.SearchString}\"");
            }
            if (missingGhost.Count > 0)
            {
                string errorMsg = $"Cannot patch shader — expected GhostFX anchor lines not found.\n" +
                    $"VRCLens may have been updated with incompatible changes.\n\n" +
                    $"Missing anchors:\n{string.Join("\n", missingGhost)}";
                Debug.LogError($"{LOG_PREFIX} {errorMsg}");
                return null;
            }
        }

        // Step 1: Apply ManualFocusAssist insertions FIRST (adds code blocks with 0.5001)
        if (enableManualFocusAssist)
        {
            content = ApplyManualFocusAssistInsertions(content);
        }

        // Step 1b: Apply GhostFX insertions
        if (enableGhostFX)
        {
            content = ApplyGhostFXInsertions(content);
        }

        // Step 2: Apply LowerMinFocus replacements SECOND
        // This replaces 0.5001 → 0.0001 in BOTH the original shader lines
        // AND any ManualFocusAssist code blocks that were just inserted.
        if (enableLowerMinFocus)
        {
            content = ApplyLowerMinFocusReplacements(content);
        }

        // Step 3: Rename shader
        content = RenameShader(content, enableLowerMinFocus, enableManualFocusAssist, enableGhostFX);

        // Step 4: Add header comment
        List<string> enabledMods = new List<string>();
        if (enableLowerMinFocus) enabledMods.Add("LowerMinFocus");
        if (enableManualFocusAssist) enabledMods.Add("ManualFocusAssist");
        if (enableGhostFX) enabledMods.Add("GhostFX");
        string modsStr = string.Join(" + ", enabledMods);

        string header = $"// VRCLens Patched Shader ({modsStr}) - Auto-generated by VRCLens Custom\n" +
            $"// Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
            $"// This file is auto-generated at build time and cleaned up afterwards.\n\n";
        content = header + content;

        // Step 5: Write and import
        WriteAndImport(content, OUTPUT_SHADER_PATH);

        // Step 6: Load and verify
        Shader shader = FindShader(content);
        if (shader == null)
        {
            Debug.LogError($"{LOG_PREFIX} Failed to load generated shader.");
            return null;
        }

        if (!shader.isSupported)
        {
            Debug.LogError($"{LOG_PREFIX} Generated shader failed to compile or is not supported.");
            return null;
        }

        Debug.Log($"{LOG_PREFIX} Shader patched successfully ({modsStr}) at: {OUTPUT_SHADER_PATH}");
        return shader;
    }

    /// <summary>
    /// Cleans up the generated shader file.
    /// </summary>
    public static void CleanupShader()
    {
        if (File.Exists(OUTPUT_SHADER_PATH))
        {
            AssetDatabase.DeleteAsset(OUTPUT_SHADER_PATH);
            Debug.Log($"{LOG_PREFIX} Cleaned up shader at: {OUTPUT_SHADER_PATH}");
        }
        string metaPath = OUTPUT_SHADER_PATH + ".meta";
        if (File.Exists(metaPath))
        {
            File.Delete(metaPath);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Reference file management
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reads the VRCLens version from readme_DoNotDelete.txt.
    /// Returns the version string (e.g. "1.9.2") or null if not found.
    /// </summary>
    private static string GetVRCLensVersion()
    {
        if (!File.Exists(VRCLENS_README_PATH)) return null;
        string firstLine = File.ReadAllLines(VRCLENS_README_PATH)[0];
        // Format: "VRCLens version X.Y.Z - Copyright ..."
        const string prefix = "VRCLens version ";
        if (!firstLine.StartsWith(prefix)) return null;
        int dashIdx = firstLine.IndexOf(" - ", prefix.Length, StringComparison.Ordinal);
        if (dashIdx < 0) return firstLine.Substring(prefix.Length).Trim();
        return firstLine.Substring(prefix.Length, dashIdx - prefix.Length).Trim();
    }

    /// <summary>
    /// Checks VRCLens version and returns a warning string if it differs from tested version.
    /// Returns null if version matches or cannot be determined.
    /// </summary>
    private static string CheckVersionWarning()
    {
        string version = GetVRCLensVersion();
        if (version == null) return "Could not read VRCLens version from readme.";
        if (version != VRCLENS_TESTED_VERSION)
            return $"VRCLens version {version} detected (patcher tested with {VRCLENS_TESTED_VERSION}).";
        return null;
    }

    /// <summary>
    /// Patches the original VRCLens shader with all mods and saves the result as .patched.
    /// The original shader at ORIGINAL_SHADER_PATH must be the unmodified VRCLens shader
    /// (re-import the VRCLens package to restore it).
    /// </summary>
    [MenuItem("Tools/VRCLens Custom/Reference/Generate Patched Reference")]
    public static void GeneratePatchedReference()
    {
        if (!File.Exists(ORIGINAL_SHADER_PATH))
        {
            EditorUtility.DisplayDialog("Generate Patched Reference",
                $"VRCLens shader not found at:\n{ORIGINAL_SHADER_PATH}\n\nInstall VRCLens first.", "OK");
            return;
        }

        string content = File.ReadAllText(ORIGINAL_SHADER_PATH);

        // Check for existing patches — the original should be clean
        if (content.Contains(PATCH_MARKER))
        {
            EditorUtility.DisplayDialog("Generate Patched Reference",
                "The shader at ORIGINAL_SHADER_PATH already contains patch markers.\n" +
                "Re-import the VRCLens package to restore the original shader first.", "OK");
            return;
        }

        // Check version
        string versionWarning = CheckVersionWarning();

        // Validate ManualFocusAssist anchors
        var anchors = GetManualFocusAssistAnchors();
        List<string> missing = new List<string>();
        foreach (var anchor in anchors)
        {
            if (!content.Contains(anchor.SearchString))
                missing.Add($"  - {anchor.Description}: \"{anchor.SearchString}\"");
        }
        if (missing.Count > 0)
        {
            string msg = $"Cannot patch — missing anchors:\n{string.Join("\n", missing)}";
            Debug.LogError($"{LOG_PREFIX} {msg}");
            EditorUtility.DisplayDialog("Generate Patched Reference", msg, "OK");
            return;
        }

        // Validate LowerMinFocus patterns
        List<string> missingPatterns = new List<string>();
        foreach (var replacement in LowerMinFocusReplacements)
        {
            if (!content.Contains(replacement.OldText))
                missingPatterns.Add($"  - {replacement.Description}: \"{replacement.OldText}\"");
        }
        if (missingPatterns.Count > 0)
        {
            string msg = $"Cannot patch — missing LowerMinFocus patterns:\n{string.Join("\n", missingPatterns)}";
            Debug.LogError($"{LOG_PREFIX} {msg}");
            EditorUtility.DisplayDialog("Generate Patched Reference", msg, "OK");
            return;
        }

        // Validate GhostFX anchors
        var ghostAnchors = GetGhostFXAnchors();
        List<string> missingGhost = new List<string>();
        foreach (var anchor in ghostAnchors)
        {
            if (!content.Contains(anchor.SearchString))
                missingGhost.Add($"  - {anchor.Description}: \"{anchor.SearchString}\"");
        }
        if (missingGhost.Count > 0)
        {
            string msg = $"Cannot patch — missing GhostFX anchors:\n{string.Join("\n", missingGhost)}";
            Debug.LogError($"{LOG_PREFIX} {msg}");
            EditorUtility.DisplayDialog("Generate Patched Reference", msg, "OK");
            return;
        }

        // Apply all modifications (all mods enabled for reference)
        content = ApplyManualFocusAssistInsertions(content);
        content = ApplyGhostFXInsertions(content);
        content = ApplyLowerMinFocusReplacements(content);
        File.WriteAllText(PATCHED_REF_PATH, content);

        int baselineLines = File.ReadAllLines(ORIGINAL_SHADER_PATH).Length;
        int patchedLines = content.Split('\n').Length;
        string version = GetVRCLensVersion() ?? "unknown";
        string result = $"Patched reference saved to:\n{PATCHED_REF_PATH}\n\n" +
            $"VRCLens version: {version}\n" +
            $"Original: {baselineLines} lines\nPatched: {patchedLines} lines (+{patchedLines - baselineLines})";
        if (versionWarning != null)
            result += $"\n\nWarning: {versionWarning}";

        Debug.Log($"{LOG_PREFIX} {result}");
        EditorUtility.DisplayDialog("Generate Patched Reference", result, "OK");
    }

    /// <summary>
    /// Verifies the patcher reproduces .patched exactly from the original shader.
    /// Also checks VRCLens version and validates all anchors.
    /// </summary>
    [MenuItem("Tools/VRCLens Custom/Reference/Verify Patcher")]
    public static void VerifyPatcher()
    {
        if (!File.Exists(ORIGINAL_SHADER_PATH))
        {
            EditorUtility.DisplayDialog("Verify Patcher",
                $"VRCLens shader not found at:\n{ORIGINAL_SHADER_PATH}\n\nInstall VRCLens first.", "OK");
            return;
        }
        if (!File.Exists(PATCHED_REF_PATH))
        {
            EditorUtility.DisplayDialog("Verify Patcher",
                $"No patched reference found at:\n{PATCHED_REF_PATH}\n\nRun 'Generate Patched Reference' first.", "OK");
            return;
        }

        var messages = new List<string>();

        // Check VRCLens version
        string version = GetVRCLensVersion();
        if (version != null)
        {
            messages.Add($"VRCLens version: {version}");
            if (version != VRCLENS_TESTED_VERSION)
                messages.Add($"WARNING: Patcher was tested with version {VRCLENS_TESTED_VERSION}");
        }
        else
        {
            messages.Add("WARNING: Could not read VRCLens version");
        }

        string originalContent = File.ReadAllText(ORIGINAL_SHADER_PATH);

        // Check if original has been manually edited
        if (originalContent.Contains(PATCH_MARKER))
        {
            string msg = "The original shader already contains patch markers.\n" +
                "Re-import the VRCLens package to restore the unmodified shader.";
            Debug.LogError($"{LOG_PREFIX} {msg}");
            EditorUtility.DisplayDialog("Verify Patcher — FAILED", msg, "OK");
            return;
        }

        // Validate ManualFocusAssist anchors
        var anchors = GetManualFocusAssistAnchors();
        List<string> missing = new List<string>();
        foreach (var anchor in anchors)
        {
            if (!originalContent.Contains(anchor.SearchString))
                missing.Add($"  - {anchor.Description}: \"{anchor.SearchString}\"");
        }
        if (missing.Count > 0)
        {
            string msg = $"Anchor validation FAILED — missing anchors:\n{string.Join("\n", missing)}\n\n" +
                "VRCLens may have been updated. See maintenance steps in the patcher source code.";
            Debug.LogError($"{LOG_PREFIX} {msg}");
            EditorUtility.DisplayDialog("Verify Patcher — FAILED", msg, "OK");
            return;
        }
        messages.Add($"All {anchors.Count} ManualFocusAssist anchors found.");

        // Validate LowerMinFocus patterns
        List<string> missingPatterns = new List<string>();
        foreach (var replacement in LowerMinFocusReplacements)
        {
            if (!originalContent.Contains(replacement.OldText))
                missingPatterns.Add($"  - {replacement.Description}: \"{replacement.OldText}\"");
        }
        if (missingPatterns.Count > 0)
        {
            string msg = $"LowerMinFocus validation FAILED — missing patterns:\n{string.Join("\n", missingPatterns)}\n\n" +
                "VRCLens may have been updated. See maintenance steps in the patcher source code.";
            Debug.LogError($"{LOG_PREFIX} {msg}");
            EditorUtility.DisplayDialog("Verify Patcher — FAILED", msg, "OK");
            return;
        }
        messages.Add($"All {LowerMinFocusReplacements.Count} LowerMinFocus patterns found.");

        // Validate GhostFX anchors
        var ghostAnchorsV = GetGhostFXAnchors();
        List<string> missingGhostV = new List<string>();
        foreach (var anchor in ghostAnchorsV)
        {
            if (!originalContent.Contains(anchor.SearchString))
                missingGhostV.Add($"  - {anchor.Description}: \"{anchor.SearchString}\"");
        }
        if (missingGhostV.Count > 0)
        {
            string msg = $"GhostFX validation FAILED — missing anchors:\n{string.Join("\n", missingGhostV)}\n\n" +
                "VRCLens may have been updated. See maintenance steps in the patcher source code.";
            Debug.LogError($"{LOG_PREFIX} {msg}");
            EditorUtility.DisplayDialog("Verify Patcher — FAILED", msg, "OK");
            return;
        }
        messages.Add($"All {ghostAnchorsV.Count} GhostFX anchors found.");

        // Patch the original (all mods) and compare to reference
        string patched = ApplyManualFocusAssistInsertions(originalContent);
        patched = ApplyGhostFXInsertions(patched);
        patched = ApplyLowerMinFocusReplacements(patched);
        string expected = File.ReadAllText(PATCHED_REF_PATH);

        if (patched == expected)
        {
            messages.Add("Patcher output matches .patched reference exactly.");
            string result = string.Join("\n", messages);
            Debug.Log($"{LOG_PREFIX} Verification PASSED\n{result}");
            EditorUtility.DisplayDialog("Verify Patcher — PASSED", result, "OK");
        }
        else
        {
            // Find first differing line for diagnosis
            string[] patchedLines = patched.Split('\n');
            string[] expectedLines = expected.Split('\n');
            int firstDiff = -1;
            for (int i = 0; i < Math.Min(patchedLines.Length, expectedLines.Length); i++)
            {
                if (patchedLines[i] != expectedLines[i])
                {
                    firstDiff = i + 1;
                    break;
                }
            }
            if (firstDiff < 0 && patchedLines.Length != expectedLines.Length)
                firstDiff = Math.Min(patchedLines.Length, expectedLines.Length) + 1;

            messages.Add($"MISMATCH: Output does NOT match .patched reference.");
            messages.Add($"Generated: {patchedLines.Length} lines");
            messages.Add($"Expected:  {expectedLines.Length} lines");
            messages.Add($"First difference at line: {firstDiff}");
            messages.Add("");
            messages.Add("Run 'Generate Patched Reference' to update the reference,");
            messages.Add("or fix the patcher code blocks.");

            string result = string.Join("\n", messages);
            Debug.LogWarning($"{LOG_PREFIX} {result}");
            EditorUtility.DisplayDialog("Verify Patcher — MISMATCH", result, "OK");
        }
    }

    // ── Debug menu ──────────────────────────────────────────────────────

    [MenuItem("Tools/VRCLens Custom/Debug/Test Generate Patched Shader (all mods)")]
    public static void GenerateFromMenu()
    {
        Shader shader = PatchShader(true, true, true);
        if (shader != null)
        {
            EditorUtility.DisplayDialog("VRCLens Shader Patcher",
                $"Shader generated successfully at:\n{OUTPUT_SHADER_PATH}\n\nShader: {shader.name}", "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("VRCLens Shader Patcher",
                "Failed to generate shader. Check console for errors.", "OK");
        }
    }

    [MenuItem("Tools/VRCLens Custom/Debug/Cleanup Patched Shader")]
    public static void CleanupFromMenu()
    {
        CleanupShader();
        EditorUtility.DisplayDialog("VRCLens Shader Patcher",
            "Cleanup complete.", "OK");
    }

    // ═══════════════════════════════════════════════════════════════════
    // LowerMinFocus replacement logic
    // ═══════════════════════════════════════════════════════════════════

    private static string ApplyLowerMinFocusReplacements(string content)
    {
        int totalReplacements = 0;
        foreach (var replacement in LowerMinFocusReplacements)
        {
            int countBefore = CountOccurrences(content, replacement.OldText);
            content = content.Replace(replacement.OldText, replacement.NewText);
            totalReplacements += countBefore;
        }
        Debug.Log($"{LOG_PREFIX} Applied {totalReplacements} LowerMinFocus replacements");
        return content;
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

    // ═══════════════════════════════════════════════════════════════════
    // ManualFocusAssist insertion logic
    // ═══════════════════════════════════════════════════════════════════

    private struct AnchorInfo
    {
        public string SearchString;
        public string Description;

        public AnchorInfo(string searchString, string description)
        {
            SearchString = searchString;
            Description = description;
        }
    }

    private static List<AnchorInfo> GetManualFocusAssistAnchors()
    {
        return new List<AnchorInfo>
        {
            new AnchorInfo(ANCHOR_PROPERTIES, "Properties: _DoFStrength line"),
            new AnchorInfo(ANCHOR_TEXTURE_PROPERTY, "Properties: _FocusTex texture line"),
            new AnchorInfo(ANCHOR_PASS2_UNIFORMS, "Pass 2: _SensorScale uniform"),
            new AnchorInfo(ANCHOR_LENSSHAPE_INCLUDE, "DoF_LensShape.cginc include"),
            new AnchorInfo(ANCHOR_CENTER_SIZE, "centerSize assignment"),
            new AnchorInfo(ANCHOR_SAMPLE_SIZE, "sampleSize assignment"),
            new AnchorInfo(ANCHOR_PASS6_SAMPLERS, "Pass 6: _FocusTex sampler"),
            new AnchorInfo(ANCHOR_PASS6_UNIFORMS, "Pass 6: _FocusDistance uniform"),
            new AnchorInfo(ANCHOR_FOCUS_PEAKING, "Focus peaking line"),
        };
    }

    private static string ApplyManualFocusAssistInsertions(string content)
    {
        // Split into lines for line-by-line processing
        var lines = new List<string>(content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
        int insertions = 0;

        // Process from bottom to top so line indices don't shift
        // Site 7: Debug visualization — after focus peaking line
        insertions += InsertAfterLine(lines, ANCHOR_FOCUS_PEAKING, BLOCK_DEBUG, "debug visualization", true);

        // Site 6: Pass 6 helpers — after second "//" following DoF_LensShape.cginc
        insertions += InsertAfterLensShapeComment(lines, 2, BLOCK_PASS6_HELPERS, "Pass 6 helper functions");

        // Site 5b: Pass 6 uniforms — after second occurrence of _FocusDistance uniform
        insertions += InsertAfterLine(lines, ANCHOR_PASS6_UNIFORMS, BLOCK_PASS6_UNIFORMS, "Pass 6 uniforms", false, 2);

        // Site 5a: Pass 6 samplers — after _FocusTex line
        insertions += InsertAfterLine(lines, ANCHOR_PASS6_SAMPLERS, BLOCK_PASS6_SAMPLERS, "Pass 6 samplers");

        // Site 4b: sampleSize — after sampleSize assignment
        insertions += InsertAfterLine(lines, ANCHOR_SAMPLE_SIZE, BLOCK_SAMPLE_SIZE, "sampleSize multiply");

        // Site 4a: centerSize — after centerSize assignment
        insertions += InsertAfterLine(lines, ANCHOR_CENTER_SIZE, BLOCK_CENTER_SIZE, "centerSize multiply");

        // Site 3: Pass 2 helpers — after first "//" following DoF_LensShape.cginc
        insertions += InsertAfterLensShapeComment(lines, 1, BLOCK_PASS2_HELPERS, "Pass 2 helper functions");

        // Site 2: Pass 2 uniforms — after _SensorScale line
        insertions += InsertAfterLine(lines, ANCHOR_PASS2_UNIFORMS, BLOCK_PASS2_UNIFORMS, "Pass 2 uniforms");

        // Site 1b: Texture property — after _FocusTex line
        insertions += InsertAfterLine(lines, ANCHOR_TEXTURE_PROPERTY, BLOCK_TEXTURE_PROPERTY, "texture property");

        // Site 1a: Properties — after _DoFStrength line
        insertions += InsertAfterLine(lines, ANCHOR_PROPERTIES, BLOCK_PROPERTIES, "properties");

        Debug.Log($"{LOG_PREFIX} Applied {insertions} ManualFocusAssist insertion sites");

        // Rejoin with \n (Unity shader files use \n)
        return string.Join("\n", lines);
    }

    // ═══════════════════════════════════════════════════════════════════
    // GhostFX insertion logic
    // ═══════════════════════════════════════════════════════════════════

    private static List<AnchorInfo> GetGhostFXAnchors()
    {
        return new List<AnchorInfo>
        {
            new AnchorInfo(ANCHOR_PROPERTIES, "Properties: _DoFStrength line"),
            new AnchorInfo(ANCHOR_PASS6_UNIFORMS, "Pass 6: _FocusDistance uniform"),
            new AnchorInfo(ANCHOR_GHOSTFX_UNIFORMS, "Pass 2: white balance line"),
        };
    }

    private static string ApplyGhostFXInsertions(string content)
    {
        var lines = new List<string>(content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
        int insertions = 0;

        // Process from bottom to top so line indices don't shift

        // Site 8c: Ghost FX application — after white balance line (before tone mapping)
        insertions += InsertAfterLine(lines, ANCHOR_GHOSTFX_UNIFORMS, BLOCK_GHOSTFX_PASS2, "GhostFX pass 2 application");

        // Site 8b: Ghost FX uniforms in Pass 2 — after second _FocusDistance uniform
        insertions += InsertAfterLine(lines, ANCHOR_PASS6_UNIFORMS, BLOCK_GHOSTFX_UNIFORMS, "GhostFX uniforms", false, 2);

        // Site 8a: Ghost FX properties — after _DoFStrength line
        insertions += InsertAfterLine(lines, ANCHOR_PROPERTIES, BLOCK_GHOSTFX_PROPERTIES, "GhostFX properties");

        Debug.Log($"{LOG_PREFIX} Applied {insertions} GhostFX insertion sites");

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Find a line containing searchString and insert blockText after it.
    /// If findEndOfStatement is true, finds the end of the statement (line ending with ";") first.
    /// occurrence: which occurrence to target (1-based, default 1).
    /// </summary>
    private static int InsertAfterLine(List<string> lines, string searchString, string blockText,
        string description, bool findEndOfStatement = false, int occurrence = 1)
    {
        int found = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Contains(searchString))
            {
                found++;
                if (found != occurrence) continue;

                int insertIndex = i;

                if (findEndOfStatement)
                {
                    // For multi-line statements like the focus peaking ternary,
                    // scan forward to find the line ending with ": col;" or just ";"
                    for (int j = i; j < lines.Count; j++)
                    {
                        string trimmed = lines[j].TrimEnd();
                        if (trimmed.EndsWith(": col;") || (j > i && trimmed.EndsWith(";")))
                        {
                            insertIndex = j;
                            break;
                        }
                    }
                }

                // Insert block lines after insertIndex
                string[] blockLines = blockText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                // Skip first empty line from the verbatim string literal
                int startLine = blockLines.Length > 0 && string.IsNullOrWhiteSpace(blockLines[0]) ? 1 : 0;

                for (int b = startLine; b < blockLines.Length; b++)
                {
                    lines.Insert(insertIndex + 1 + (b - startLine), blockLines[b]);
                }

                Debug.Log($"{LOG_PREFIX} Inserted {description} after line {insertIndex + 1}");
                return 1;
            }
        }

        Debug.LogWarning($"{LOG_PREFIX} Could not find anchor for {description}: \"{searchString}\"");
        return 0;
    }

    /// <summary>
    /// Find the Nth occurrence of "#include "DoF_LensShape.cginc"" and insert after
    /// the "//" comment line that follows it (the VRCLXT closing comment).
    /// </summary>
    private static int InsertAfterLensShapeComment(List<string> lines, int occurrence, string blockText, string description)
    {
        int found = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Contains(ANCHOR_LENSSHAPE_INCLUDE))
            {
                found++;
                if (found != occurrence) continue;

                // Find the "//" line after the include (typically the next line or one after)
                int commentIndex = -1;
                for (int j = i + 1; j < Math.Min(i + 5, lines.Count); j++)
                {
                    string trimmed = lines[j].Trim();
                    if (trimmed == "//")
                    {
                        commentIndex = j;
                        break;
                    }
                }

                if (commentIndex < 0)
                {
                    Debug.LogWarning($"{LOG_PREFIX} Could not find '//' comment after DoF_LensShape.cginc (occurrence {occurrence})");
                    return 0;
                }

                // Insert block after the "//" line
                string[] blockLines = blockText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                int startLine = blockLines.Length > 0 && string.IsNullOrWhiteSpace(blockLines[0]) ? 1 : 0;

                for (int b = startLine; b < blockLines.Length; b++)
                {
                    lines.Insert(commentIndex + 1 + (b - startLine), blockLines[b]);
                }

                Debug.Log($"{LOG_PREFIX} Inserted {description} after line {commentIndex + 1}");
                return 1;
            }
        }

        Debug.LogWarning($"{LOG_PREFIX} Could not find DoF_LensShape.cginc include (occurrence {occurrence})");
        return 0;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Shader naming
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Renames the shader by appending a suffix based on enabled mods.
    /// </summary>
    private static string RenameShader(string content, bool enableLowerMinFocus, bool enableManualFocusAssist, bool enableGhostFX = false)
    {
        // Build suffix from enabled mods
        var parts = new List<string>();
        if (enableLowerMinFocus) parts.Add("LowerMinFocus");
        if (enableManualFocusAssist) parts.Add("ManualFocusAssist");
        if (enableGhostFX) parts.Add("GhostFX");
        if (parts.Count == 0) return content;

        string suffix = " " + string.Join(" ", parts);

        // Find the Shader "..." line
        const string shaderPrefix = "Shader \"";
        int startIdx = content.IndexOf(shaderPrefix, StringComparison.Ordinal);
        if (startIdx < 0)
        {
            Debug.LogWarning($"{LOG_PREFIX} Could not find Shader declaration to rename");
            return content;
        }

        int nameStart = startIdx + shaderPrefix.Length;
        int nameEnd = content.IndexOf("\"", nameStart, StringComparison.Ordinal);
        if (nameEnd < 0) return content;

        string currentName = content.Substring(nameStart, nameEnd - nameStart);
        string newName = currentName + suffix;
        return content.Substring(0, nameStart) + newName + content.Substring(nameEnd);
    }

    /// <summary>
    /// Extracts the shader name from content and finds it via Shader.Find.
    /// </summary>
    private static Shader FindShader(string content)
    {
        const string shaderPrefix = "Shader \"";
        int startIdx = content.IndexOf(shaderPrefix, StringComparison.Ordinal);
        if (startIdx < 0) return null;

        int nameStart = startIdx + shaderPrefix.Length;
        int nameEnd = content.IndexOf("\"", nameStart, StringComparison.Ordinal);
        if (nameEnd < 0) return null;

        string shaderName = content.Substring(nameStart, nameEnd - nameStart);
        return Shader.Find(shaderName);
    }

    // ═══════════════════════════════════════════════════════════════════
    // File I/O
    // ═══════════════════════════════════════════════════════════════════

    private static void WriteAndImport(string content, string outputPath)
    {
        File.WriteAllText(outputPath, content);
        AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceSynchronousImport);
    }
}
#endif
