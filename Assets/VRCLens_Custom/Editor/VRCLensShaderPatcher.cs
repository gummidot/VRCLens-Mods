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
		[Toggle] _GhostFXEnable (""Enable Ghost FX"", float) = 0
		[Enum(Split,1,Dual,2)] _GhostFXMode (""Ghost FX Mode"", float) = 1
		[Toggle] _GhostFXFullScreen (""Full Screen"", float) = 0
		_GhostFXAngle (""Rotation"", Range(0.0, 1.0)) = 0.0
		_GhostFXDistance (""Distance"", Range(0.0, 0.15)) = 0.05
		_GhostFXOpacity (""Intensity"", Range(0.0, 1.0)) = 0.5
		_GhostFXLayers (""Layers"", Range(1.0, 5.0)) = 1.0
		_GhostFXSmear (""Smear"", Range(0.0, 1.0)) = 0.0
		_GhostFXSmearWidth (""Smear Width"", Range(0.0, 1.0)) = 0.25
		_GhostFXSoftEdge (""Soft Edge"", Range(0.01, 0.3)) = 0.08
		_GhostFXCenterWidth (""Center Width"", Range(0.0, 0.4)) = 0.05
		[Enum(Normal,0,Lighten,1,Screen,2,Additive,3,Darken,4)] _GhostFXBlendMode (""Blend Mode"", float) = 1
		[Toggle] _GhostFXEdgeFix (""Edge Fix"", float) = 1
		[Toggle] _GhostFXDepthMask (""Focus Depth"", float) = 0
		_GhostFXDepthFade (""Falloff"", Range(0.1, 4.0)) = 1.0
		[Toggle] _GhostFXDepthInvert (""Invert"", float) = 0
		[Toggle] _GhostFXAvatarMask (""Avatar AF"", float) = 0
		_GhostFXShake (""Shake"", Range(0.0, 1.0)) = 0.0
		_GhostFXShakeSpeed (""Shake Speed"", Range(0.0, 1.0)) = 0.3
		_GhostFXShakeDist (""Shake Distance"", Range(0.0, 1.0)) = 0.0
		_GhostFXShimmer (""Shimmer"", Range(0.0, 1.0)) = 0.0
		_GhostFXChroma (""Chroma"", Range(0.0, 1.0)) = 0.0
		// VRCLens_Custom END";

    private static readonly string BLOCK_GHOSTFX_PASS2 = @"
				// VRCLens_Custom BEGIN - Ghost FX
				if(_GhostFXEnable > 0.5) {
					half2 ghostCenter = sbsUV0 - 0.5;
					float ghostAngleRad = _GhostFXAngle * 6.28318530718;
					// Handheld shake: multi-frequency sine wobble on angle + distance
					if(_GhostFXShake > 0.001) {
						float shakeSpd = lerp(0.1, 5.0, _GhostFXShakeSpeed);
						float ghostShakeAmt = _GhostFXShake * 0.5;
						ghostAngleRad += sin(_Time.y * 0.7 * shakeSpd) * ghostShakeAmt
						               + sin(_Time.y * 1.3 * shakeSpd) * ghostShakeAmt * 0.6
						               + sin(_Time.y * 2.9 * shakeSpd) * ghostShakeAmt * 0.3;
					}
					half2 ghostDir = half2(cos(ghostAngleRad), sin(ghostAngleRad));
					float ghostProj = dot(ghostCenter, ghostDir);

					float ghostZoneMask;
					float ghostDistShake = 0.0;
					if(_GhostFXShakeDist > 0.001 && _GhostFXShake > 0.001) {
						float shakeSpd = lerp(0.1, 5.0, _GhostFXShakeSpeed);
						ghostDistShake = sin(_Time.y * 0.9 * shakeSpd) * _GhostFXShakeDist * _GhostFXShake * 0.06;
					}
					half2 ghostBaseOffset = ghostDir * (_GhostFXDistance + ghostDistShake);
					int ghostDirCount = 1;
					if(_GhostFXFullScreen > 0.5) {
						ghostZoneMask = 1.0;
						if(_GhostFXMode > 1.5) ghostDirCount = 2;
					} else if(_GhostFXMode < 1.5) {
						// Split mode
						ghostZoneMask = smoothstep(-_GhostFXSoftEdge, _GhostFXSoftEdge, ghostProj);
					} else {
						// Dual mode: center-clear, sample both directions
						float ghostAbsDist = abs(ghostProj);
						ghostZoneMask = smoothstep(_GhostFXCenterWidth - _GhostFXSoftEdge, _GhostFXCenterWidth + _GhostFXSoftEdge, ghostAbsDist);
						ghostDirCount = 2;
					}

					// Depth-based ghost masking: suppress ghost in focal zone
					if(_GhostFXDepthMask > 0.5) {
						float gDepthRaw = SAMPLE_DEPTH_TEXTURE(_DepthTex, sbsUV0);
						float gDepthZ = 1.0 / ((32000.0/0.04 - 1.0)/32000.0 * gDepthRaw + 1.0/32000.0);
						float gFocusDist = _FocusDistance;
						if(_FocusDistance < 0.5001) {
							float fRaw = SAMPLE_DEPTH_TEXTURE(_DepthTex, focusPos);
							gFocusDist = 1.0 / ((32000.0/0.04 - 1.0)/32000.0 * fRaw + 1.0/32000.0);
						}
						float depthDiff = abs(log(max(0.001, gDepthZ) / max(0.001, gFocusDist)));
						float depthMask = smoothstep(0.0, _GhostFXDepthFade, depthDiff);
						if(_GhostFXDepthInvert > 0.5) depthMask = 1.0 - depthMask;
						ghostZoneMask *= depthMask;
					}

					// Avatar masking: suppress ghost on avatar pixels (requires Avatar AF)
					// Respects Depth Invert: normal = protect avatars, inverted = ghost only on avatars
					if(_GhostFXAvatarMask > 0.5 && _IsExternalFocus == 2) {
						float gAvatarPixel = tex2D(_DepthAvatarTex, sbsUV0).r;
						float avatarMask = gAvatarPixel > 0.0 ? 0.0 : 1.0;
						if(_GhostFXDepthInvert > 0.5) avatarMask = 1.0 - avatarMask;
						ghostZoneMask *= avatarMask;
					}

					// Continuous directional smear with per-pixel spatial jitter
					// _GhostFXLayers controls trail distance, _GhostFXSmear controls density + blur
					float trailLength = _GhostFXLayers;
					float nearT = lerp(1.0, 0.05, _GhostFXSmear);
					int smearTaps = clamp((int)(_GhostFXSmear * 23.0 + 1.5), max(1, (int)trailLength), 24);

					// Per-pixel 2D Interleaved Gradient Noise
					// Jorge Jimenez, Next Generation Post Processing in Call of Duty: Advanced Warfare, SIGGRAPH 2014
					float2 ghostPixelPos = floor(sbsUV0 * _ScreenParams.xy);
					float ghostNoise = frac(52.9829189 * frac(dot(ghostPixelPos, float2(0.06711056, 0.00583715))));
					float ghostNoise2 = frac(52.9829189 * frac(dot(ghostPixelPos + float2(47.0, 17.0), float2(0.06711056, 0.00583715))));
					float tapSpacing = (trailLength - nearT) / max(1.0, (float)(smearTaps - 1));
					float tJitter = (ghostNoise - 0.5) * tapSpacing * _GhostFXSmear;
					half2 ghostPerp = half2(-ghostDir.y, ghostDir.x);
					float perpJitter = (ghostNoise2 - 0.5) * _GhostFXSmear * lerp(0.008, 0.05, _GhostFXSmearWidth);
					// Shimmer: slow temporal seed for per-tap noise drift
					float shimmerSeed = _Time.y * 3.0;

					// Direction loop: 1 pass for Split/Full, 2 passes for Dual (both directions)
					half3 ghostAccumFinal = half3(0,0,0);
					// Pre-compute scene color for OOB decay target (same processing as taps)
					half3 ghostSceneColor = max(0.00001, tex2D(_HirabikiVRCLensPassTexture, sbsUV0).rgb / finalExp.x);
					ghostSceneColor *= colorTemp(-_WhiteBalance);
					for(int gdir = 0; gdir < 2; gdir++) {
						if(gdir >= ghostDirCount) break;
						float dirSign = (gdir == 0) ? 1.0 : -1.0;
						half2 curOffset = ghostBaseOffset * dirSign;

						half3 ghostAccum = half3(0,0,0);
						float totalWeight = 0.0;
						[unroll]
						for(int gi = 0; gi < 24; gi++) {
							if(gi >= smearTaps) break;
							// Per-tap shimmer: re-hash noise with tap index + time when shimmer active
							float tapNoise = ghostNoise;
							float tapNoise2 = ghostNoise2;
							if(_GhostFXShimmer > 0.001) {
								float tapSeed = shimmerSeed + (float)gi * 3.7;
								tapNoise = frac(52.9829189 * frac(dot(ghostPixelPos + float2(tapSeed, tapSeed * 0.73), float2(0.06711056, 0.00583715))));
								tapNoise2 = frac(52.9829189 * frac(dot(ghostPixelPos + float2(47.0 + tapSeed, 17.0 + tapSeed * 0.73), float2(0.06711056, 0.00583715))));
							}
							float tapTJitter = (tapNoise - 0.5) * tapSpacing * _GhostFXSmear;
							float tapPerpJitter = (tapNoise2 - 0.5) * _GhostFXSmear * lerp(0.008, 0.05, _GhostFXSmearWidth);
							// Blend between base jitter and per-tap jitter (capped at 35% for subtlety)
							float shimmerBlend = _GhostFXShimmer * 0.35;
							float curTJitter = lerp(tJitter, tapTJitter, shimmerBlend);
							float curPerpJitter = lerp(perpJitter, tapPerpJitter, shimmerBlend);
							float t = lerp(nearT, trailLength, (float)gi / max(1.0, (float)(smearTaps - 1))) + curTJitter;
							t = max(0.01, t);
							half2 gUV = sbsUV0 + curOffset * t + ghostPerp * curPerpJitter;
							float tNorm = (t - nearT) / max(0.001, trailLength - nearT);
							float tapWeight = exp(-2.5 * tNorm * tNorm);
							float blurRadius = _GhostFXSmear * lerp(0.015, 0.08, _GhostFXSmearWidth) * (0.3 + t);
							half2 blurStep = ghostDir * blurRadius;
							// 5-tap Gaussian blur kernel (1/16, 4/16, 6/16, 4/16, 1/16)
							half2 blurHalf = blurStep * 0.5;
							half3 gSample  = tex2D(_HirabikiVRCLensPassTexture, clamp(gUV - blurStep, 0.001, 0.999)).rgb * 0.0625;
							gSample += tex2D(_HirabikiVRCLensPassTexture, clamp(gUV - blurHalf, 0.001, 0.999)).rgb * 0.25;
							gSample += tex2D(_HirabikiVRCLensPassTexture, clamp(gUV, 0.001, 0.999)).rgb * 0.375;
							gSample += tex2D(_HirabikiVRCLensPassTexture, clamp(gUV + blurHalf, 0.001, 0.999)).rgb * 0.25;
							gSample += tex2D(_HirabikiVRCLensPassTexture, clamp(gUV + blurStep, 0.001, 0.999)).rgb * 0.0625;
							// Chromatic smear: offset R/B channels along trail direction
							if(_GhostFXChroma > 0.001) {
								float chromaSpread = _GhostFXChroma * t * 0.015;
								half2 chromaOff = curOffset * chromaSpread;
								half rShift = tex2D(_HirabikiVRCLensPassTexture, clamp(gUV + chromaOff, 0.001, 0.999)).r;
								half bShift = tex2D(_HirabikiVRCLensPassTexture, clamp(gUV - chromaOff, 0.001, 0.999)).b;
								gSample.r = lerp(gSample.r, rShift, _GhostFXChroma);
								gSample.b = lerp(gSample.b, bShift, _GhostFXChroma);
							}
							gSample = max(0.00001, gSample / finalExp.x);
							gSample *= colorTemp(-_WhiteBalance);
							// Edge fix: decay OOB taps toward scene color instead of black
							if(_GhostFXEdgeFix > 0.5) {
								half2 oobAmount = max(half2(0,0), max(-gUV, gUV - half2(1,1)));
								float oobDist = max(oobAmount.x, oobAmount.y);
								float oobDecay = exp(-oobDist * 80.0);
								gSample = lerp(ghostSceneColor, gSample, oobDecay);
							}
							ghostAccum += gSample * tapWeight;
							totalWeight += tapWeight;
						}
						ghostAccum /= max(0.001, totalWeight);
						ghostAccumFinal = max(ghostAccumFinal, ghostAccum);
					}

					half3 ghostBlended;
					if(_GhostFXBlendMode < 0.5) {
						// Normal blend: direct mix regardless of brightness
						ghostBlended = ghostAccumFinal;
					} else if(_GhostFXBlendMode < 1.5) {
						ghostBlended = max(col.rgb, ghostAccumFinal);
					} else if(_GhostFXBlendMode < 2.5) {
						half3 screenRaw = 1.0 - (1.0 - col.rgb) * (1.0 - ghostAccumFinal);
						half maxScreen = max(screenRaw.r, max(screenRaw.g, screenRaw.b));
						half3 ghostHueRatio = ghostAccumFinal / max(0.001, max(ghostAccumFinal.r, max(ghostAccumFinal.g, ghostAccumFinal.b)));
						half ghostIntensity = max(ghostAccumFinal.r, max(ghostAccumFinal.g, ghostAccumFinal.b));
						half hueBlend = smoothstep(0.3, 0.8, ghostIntensity);
						half3 hueCorrected = maxScreen * ghostHueRatio;
						ghostBlended = lerp(screenRaw, hueCorrected, hueBlend);
					} else if(_GhostFXBlendMode < 3.5) {
						// Additive: maximum energy, no capping
						ghostBlended = col.rgb + ghostAccumFinal;
					} else {
						// Darken: dark ghosts dominate
						ghostBlended = min(col.rgb, ghostAccumFinal);
					}
					col.rgb = lerp(col.rgb, ghostBlended, ghostZoneMask * _GhostFXOpacity);
				}
				// VRCLens_Custom END";

    private static readonly string BLOCK_GHOSTFX_UNIFORMS = @"
			// VRCLens_Custom BEGIN - Ghost FX Uniforms
			uniform float _GhostFXEnable;
			uniform float _GhostFXMode, _GhostFXFullScreen, _GhostFXAngle, _GhostFXDistance;
			uniform float _GhostFXOpacity, _GhostFXLayers, _GhostFXSmear, _GhostFXSmearWidth, _GhostFXSoftEdge, _GhostFXCenterWidth;
			uniform float _GhostFXBlendMode;
			uniform float _GhostFXEdgeFix;
			uniform float _GhostFXDepthMask, _GhostFXDepthFade, _GhostFXDepthInvert;
			uniform float _GhostFXAvatarMask;
			uniform float _GhostFXShake, _GhostFXShakeSpeed, _GhostFXShakeDist;
			uniform float _GhostFXShimmer, _GhostFXChroma;
			// VRCLens_Custom END";

    // ── Chromatic Aberration code blocks ─────────────────────────────────

    private static readonly string BLOCK_CA_PROPERTIES = @"
		// VRCLens_Custom BEGIN - Chromatic Aberration Properties
		[Header(Chromatic Aberration)]
		_TransverseCA (""Transverse CA"", Range(0.0, 1.0)) = 0.0
		_AxialCA (""Axial CA"", Range(0.0, 1.0)) = 0.0
		// VRCLens_Custom END";

    private static readonly string BLOCK_CA_UNIFORMS = @"
			// VRCLens_Custom BEGIN - Chromatic Aberration Uniforms
			uniform float _TransverseCA, _AxialCA;
			// VRCLens_Custom END";

    private static readonly string BLOCK_CA_PASS2 = @"
				// VRCLens_Custom BEGIN - Chromatic Aberration
				// Transverse CA: radial color fringing increasing toward screen edges
				if(_TransverseCA > 0.001) {
					float2 caCenter = sbsUV0 - 0.5;
					float caRadius = length(caCenter);
					float2 caDir = caCenter / max(0.001, caRadius);
					float caOffset = _TransverseCA * caRadius * caRadius * 0.08;
					float2 caUVr = sbsUV0 + caDir * caOffset;
					float2 caUVb = sbsUV0 - caDir * caOffset;
					float2 caOobR = max(float2(0,0), max(-caUVr, caUVr - float2(1,1)));
					float caEdgeR = exp(-max(caOobR.x, caOobR.y) * 80.0);
					float2 caOobB = max(float2(0,0), max(-caUVb, caUVb - float2(1,1)));
					float caEdgeB = exp(-max(caOobB.x, caOobB.y) * 80.0);
					col.r = lerp(col.r, tex2D(_HirabikiVRCLensPassTexture, clamp(caUVr, 0.001, 0.999)).r, caEdgeR);
					col.b = lerp(col.b, tex2D(_HirabikiVRCLensPassTexture, clamp(caUVb, 0.001, 0.999)).b, caEdgeB);
				}
				// Axial CA: depth-scaled per-channel circular blur (colored halos on distant objects)
				if(_AxialCA > 0.001) {
					// Linearize depth (same formula as getLinearEyeDepth in MFA, inlined
					// because that helper is only injected when ManualFocusAssist is enabled)
					// VRCLens camera: nearPlane=0.04, farPlane=32000.0
					float axDepthRaw = SAMPLE_DEPTH_TEXTURE(_DepthTex, sbsUV0);
					float axFar = 32000.0; float axNear = 0.04;
					float axZ = (axFar / axNear - 1.0) / axFar;
					float axW = 1.0 / axFar;
					float axDepth = 1.0 / (axDepthRaw * axZ + axW);
					// Ramp: no CA at camera, full CA at 50m+ (atmospheric depth haze distance)
					float axDepthFade = saturate(axDepth / 50.0);
					float axPx = _AxialCA * 16.0 * axDepthFade;
					float2 axTs = axPx / _ScreenParams.xy;
					// 8-tap circular pattern at 45° intervals
					float2 axD0 = float2(1.0, 0.0) * axTs;
					float2 axD1 = float2(0.707, 0.707) * axTs;
					float2 axD2 = float2(0.0, 1.0) * axTs;
					float2 axD3 = float2(-0.707, 0.707) * axTs;
					float rAcc = tex2D(_HirabikiVRCLensPassTexture, sbsUV0).r;
					rAcc += tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 + axD0, 0.001, 0.999)).r + tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 - axD0, 0.001, 0.999)).r;
					rAcc += tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 + axD1, 0.001, 0.999)).r + tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 - axD1, 0.001, 0.999)).r;
					rAcc += tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 + axD2, 0.001, 0.999)).r + tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 - axD2, 0.001, 0.999)).r;
					rAcc += tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 + axD3, 0.001, 0.999)).r + tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 - axD3, 0.001, 0.999)).r;
					col.r = rAcc / 9.0;
					float bAcc = tex2D(_HirabikiVRCLensPassTexture, sbsUV0).b;
					bAcc += tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 + axD0 * 0.5, 0.001, 0.999)).b + tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 - axD0 * 0.5, 0.001, 0.999)).b;
					bAcc += tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 + axD1 * 0.5, 0.001, 0.999)).b + tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 - axD1 * 0.5, 0.001, 0.999)).b;
					bAcc += tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 + axD2 * 0.5, 0.001, 0.999)).b + tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 - axD2 * 0.5, 0.001, 0.999)).b;
					bAcc += tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 + axD3 * 0.5, 0.001, 0.999)).b + tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 - axD3 * 0.5, 0.001, 0.999)).b;
					col.b = bAcc / 9.0;
				}
				// VRCLens_Custom END";

    // ── Film Grain code blocks ──────────────────────────────────────────

    // Anchor: after tone mapping, before dithering
    private const string ANCHOR_TONEMAPPING = "col.rgb = tonemap(col.rgb, _TonemapMode);";

    private static readonly string BLOCK_GRAIN_PROPERTIES = @"
		// VRCLens_Custom BEGIN - Film Grain Properties
		[Header(Film Grain)]
		_FilmGrain (""Film Grain"", Range(0.0, 1.0)) = 0.0
		_FilmGrainSize (""Grain Size"", Range(0.0, 1.0)) = 0.0
		_FilmGrainBrightness (""Grain Brightness"", Range(0.0, 1.0)) = 0.5
		// VRCLens_Custom END";

    private static readonly string BLOCK_GRAIN_UNIFORMS = @"
			// VRCLens_Custom BEGIN - Film Grain Uniforms
			uniform float _FilmGrain, _FilmGrainSize, _FilmGrainBrightness;
			// VRCLens_Custom END";

    private static readonly string BLOCK_GRAIN_PASS2 = @"
				// VRCLens_Custom BEGIN - Film Grain
				if(_FilmGrain > 0.001) {
					float grainClump = lerp(1.5, 4.0, _FilmGrainSize);
					float3 gt = _Time.y * float3(20.0, 26.0, 33.0);
					float2 gs = float2(12.9898, 78.233);
					// Octave 1: base frequency
					float2 guv1 = sbsUV0 * _ScreenParams.xy / grainClump;
					float2 gi1 = floor(guv1);
					float2 gf1 = frac(guv1);
					float2 gu1 = gf1 * gf1 * (3.0 - 2.0 * gf1);
					// Fully chaotic spatial anchors (frac(sin()*43758) decorrelates neighbors)
					float gh00_1 = frac(sin(dot(gi1, gs)) * 43758.5453);
					float gh10_1 = frac(sin(dot(gi1 + float2(1,0), gs)) * 43758.5453);
					float gh01_1 = frac(sin(dot(gi1 + float2(0,1), gs)) * 43758.5453);
					float gh11_1 = frac(sin(dot(gi1 + float2(1,1), gs)) * 43758.5453);
					// Temporal boil (1234.5 scrambles phase so neighbors evolve independently)
					float3 gn00 = frac(sin(gh00_1 * 1234.5 + gt) * 43758.5453);
					float3 gn10 = frac(sin(gh10_1 * 1234.5 + gt) * 43758.5453);
					float3 gn01 = frac(sin(gh01_1 * 1234.5 + gt) * 43758.5453);
					float3 gn11 = frac(sin(gh11_1 * 1234.5 + gt) * 43758.5453);
					float3 gnoise1 = lerp(lerp(gn00, gn10, gu1.x), lerp(gn01, gn11, gu1.x), gu1.y) - 0.5;
					// Octave 2: rotated grid (0.8/0.6 Pythagorean rotation breaks Cartesian alignment)
					float2 guv2 = float2(guv1.x * 0.8 + guv1.y * 0.6, guv1.x * -0.6 + guv1.y * 0.8) * 2.0;
					float2 gi2 = floor(guv2);
					float2 gf2 = frac(guv2);
					float2 gu2 = gf2 * gf2 * (3.0 - 2.0 * gf2);
					float3 gt2 = gt + float3(14.5, 23.1, 9.8);
					float gh00_2 = frac(sin(dot(gi2, gs)) * 43758.5453);
					float gh10_2 = frac(sin(dot(gi2 + float2(1,0), gs)) * 43758.5453);
					float gh01_2 = frac(sin(dot(gi2 + float2(0,1), gs)) * 43758.5453);
					float gh11_2 = frac(sin(dot(gi2 + float2(1,1), gs)) * 43758.5453);
					float3 gn00b = frac(sin(gh00_2 * 1234.5 + gt2) * 43758.5453);
					float3 gn10b = frac(sin(gh10_2 * 1234.5 + gt2) * 43758.5453);
					float3 gn01b = frac(sin(gh01_2 * 1234.5 + gt2) * 43758.5453);
					float3 gn11b = frac(sin(gh11_2 * 1234.5 + gt2) * 43758.5453);
					float3 gnoise2 = lerp(lerp(gn00b, gn10b, gu2.x), lerp(gn01b, gn11b, gu2.x), gu2.y) - 0.5;
					// FBM composite + brightness bias
					float3 grainNoise = (gnoise1 + gnoise2 * 0.5) / 1.5 + (_FilmGrainBrightness - 0.5) * 0.4;
					// Midtone parabola: grain peaks at ~35% luma, fades in deep shadows AND highlights
					float grainLuma = dot(col.rgb, float3(0.299, 0.587, 0.114));
					float grainMask = max(0.05, saturate(1.0 - abs(grainLuma - 0.35) * 2.5));
					// Lens vignette: heavier grain at frame edges (1.0x center, ~1.75x corners)
					float grainVignette = 1.0 + length(sbsUV0 - 0.5) * 1.5;
					col.rgb += grainNoise * float3(0.8, 0.8, 1.4) * _FilmGrain * 0.35 * grainMask * grainVignette;
				}
				// VRCLens_Custom END";

    // ── Depth Fog code blocks ───────────────────────────────────────────

    private static readonly string BLOCK_DEPTHFOG_PROPERTIES = @"
		// VRCLens_Custom BEGIN - Depth Fog Properties
		[Header(Depth Fog)]
		[Toggle] _DepthFogEnable (""Enable Depth Fog"", float) = 0
		_DepthFogDensity (""Fog Density"", Range(0.0, 1.0)) = 0
		_DepthFogStart (""Fog Start (m)"", Range(0.0, 500.0)) = 0
		_DepthFogColorR (""Fog Color R"", Range(0.0, 1.0)) = 0
		_DepthFogColorG (""Fog Color G"", Range(0.0, 1.0)) = 0
		_DepthFogColorB (""Fog Color B"", Range(0.0, 1.0)) = 0
		// VRCLens_Custom END";

    private static readonly string BLOCK_DEPTHFOG_UNIFORMS = @"
			// VRCLens_Custom BEGIN - Depth Fog Uniforms
			uniform float _DepthFogEnable, _DepthFogDensity, _DepthFogStart;
			uniform float _DepthFogColorR, _DepthFogColorG, _DepthFogColorB;
			// VRCLens_Custom END";

    private static readonly string BLOCK_DEPTHFOG_PASS2 = @"
				// VRCLens_Custom BEGIN - Depth Fog
				if(_DepthFogEnable > 0.5) {
					float fogDepthRaw = SAMPLE_DEPTH_TEXTURE(_DepthTex, sbsUV0);
					float fogDepthZ = 1.0 / ((32000.0/0.04 - 1.0)/32000.0 * fogDepthRaw + 1.0/32000.0);
					float fogDist = max(0.0, fogDepthZ - _DepthFogStart);
					float fogDensityScaled = _DepthFogDensity * _DepthFogDensity * _DepthFogDensity;
					float fogFactor = 1.0 - exp(-fogDist * fogDensityScaled);
					fogFactor = min(fogFactor, 0.95);
					float3 fogColor = float3(_DepthFogColorR, _DepthFogColorG, _DepthFogColorB);
					col.rgb = lerp(col.rgb, fogColor, saturate(fogFactor));
				}
				// VRCLens_Custom END";

    // ── Tilt-Shift code blocks ──────────────────────────────────────────

    // Anchor for Pass 0: CoC output line (unique — only appears once)
    private const string ANCHOR_COC_OUTPUT = "getBlurSize(eyeDepthUV, 0.001 * _FocalLength";

    // Anchor for Pass 0: bounds check line — after the if/else that may reset col.a to 1.0
    private const string ANCHOR_BOUNDS_CHECK = "col = bounds(uv) ? col : half4(";

    private static readonly string BLOCK_TILTSHIFT_PROPERTIES = @"
		// VRCLens_Custom BEGIN - Tilt-Shift Properties
		[Header(Tilt Shift)]
		_TiltShift (""Tilt-Shift Blur"", Range(0.0, 1.0)) = 0.0
		_TiltShiftPos (""Focus Band Position"", Range(0.0, 1.0)) = 0.0
		_TiltShiftWidth (""Focus Band Width"", Range(0.0, 0.5)) = 0.0
		// VRCLens_Custom END";

    // Tilt-shift uniforms go in Pass 0 (occurrence 1 of _FocusDistance uniform)
    private static readonly string BLOCK_TILTSHIFT_PASS0_UNIFORMS = @"
			// VRCLens_Custom BEGIN - Tilt-Shift Uniforms
			uniform float _TiltShift, _TiltShiftPos, _TiltShiftWidth;
			// VRCLens_Custom END";

    // Tilt-shift Pass 1 text replacements: enable blur even when DoF is off
    private const string TILTSHIFT_PASS1_DOF_OLD = "if(_EnableDoF) {";
    private const string TILTSHIFT_PASS1_DOF_NEW = "if(_EnableDoF || _TiltShift > 0.001) { // VRCLens_Custom: TiltShift";
    private const string TILTSHIFT_PASS1_UNIFORM_ANCHOR = "uniform bool _EnableDoF;\n\t\t\tuniform float _SensorScale";
    private const string TILTSHIFT_PASS1_UNIFORM_NEW = "uniform bool _EnableDoF;\n\t\t\tuniform float _TiltShift; // VRCLens_Custom: TiltShift\n\t\t\tuniform float _SensorScale";

    private static readonly string BLOCK_TILTSHIFT_PASS0 = @"
				// VRCLens_Custom BEGIN - Tilt-Shift
				if(_TiltShift > 0.001) {
					float tsBandDist = abs(uv.y - _TiltShiftPos);
					float tsMask = smoothstep(_TiltShiftWidth, _TiltShiftWidth + 0.15, tsBandDist);
					float tsStrength = _TiltShift * _TiltShift;
					float tsCoC = tsMask * tsStrength * 300.0;
					col.a = (abs(col.a) > abs(tsCoC)) ? col.a : sign(col.a + 0.0001) * tsCoC;
				}
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
    public static Shader PatchShader(bool enableLowerMinFocus, bool enableManualFocusAssist, bool enableGhostFX = false, bool enableChromaticAberration = false, bool enableFilmGrain = false, bool enableDepthFog = false, bool enableTiltShift = false)
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

        // Validate ChromaticAberration anchors if that feature is enabled
        if (enableChromaticAberration)
        {
            var caAnchors = GetChromaticAberrationAnchors();
            List<string> missingCA = new List<string>();
            foreach (var anchor in caAnchors)
            {
                if (!content.Contains(anchor.SearchString))
                    missingCA.Add($"  - {anchor.Description}: \"{anchor.SearchString}\"");
            }
            if (missingCA.Count > 0)
            {
                string errorMsg = $"Cannot patch shader — expected ChromaticAberration anchor lines not found.\n" +
                    $"VRCLens may have been updated with incompatible changes.\n\n" +
                    $"Missing anchors:\n{string.Join("\n", missingCA)}";
                Debug.LogError($"{LOG_PREFIX} {errorMsg}");
                return null;
            }
        }

        // Validate FilmGrain anchors if that feature is enabled
        if (enableFilmGrain)
        {
            var grainAnchors = GetFilmGrainAnchors();
            List<string> missingGrain = new List<string>();
            foreach (var anchor in grainAnchors)
            {
                if (!content.Contains(anchor.SearchString))
                    missingGrain.Add($"  - {anchor.Description}: \"{anchor.SearchString}\"");
            }
            if (missingGrain.Count > 0)
            {
                string errorMsg = $"Cannot patch shader \u2014 expected FilmGrain anchor lines not found.\n" +
                    $"VRCLens may have been updated with incompatible changes.\n\n" +
                    $"Missing anchors:\n{string.Join("\n", missingGrain)}";
                Debug.LogError($"{LOG_PREFIX} {errorMsg}");
                return null;
            }
        }

        // Validate DepthFog anchors if that feature is enabled
        if (enableDepthFog)
        {
            var fogAnchors = GetDepthFogAnchors();
            List<string> missingFog = new List<string>();
            foreach (var anchor in fogAnchors)
            {
                if (!content.Contains(anchor.SearchString))
                    missingFog.Add($"  - {anchor.Description}: \"{anchor.SearchString}\"");
            }
            if (missingFog.Count > 0)
            {
                string errorMsg = $"Cannot patch shader — expected DepthFog anchor lines not found.\n" +
                    $"VRCLens may have been updated with incompatible changes.\n\n" +
                    $"Missing anchors:\n{string.Join("\n", missingFog)}";
                Debug.LogError($"{LOG_PREFIX} {errorMsg}");
                return null;
            }
        }

        // Validate TiltShift anchors if that feature is enabled
        if (enableTiltShift)
        {
            var tsAnchors = GetTiltShiftAnchors();
            List<string> missingTS = new List<string>();
            foreach (var anchor in tsAnchors)
            {
                if (!content.Contains(anchor.SearchString))
                    missingTS.Add($"  - {anchor.Description}: \"{anchor.SearchString}\"");
            }
            if (missingTS.Count > 0)
            {
                string errorMsg = $"Cannot patch shader — expected TiltShift anchor lines not found.\n" +
                    $"VRCLens may have been updated with incompatible changes.\n\n" +
                    $"Missing anchors:\n{string.Join("\n", missingTS)}";
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

        // Step 1c: Apply ChromaticAberration insertions
        if (enableChromaticAberration)
        {
            content = ApplyChromaticAberrationInsertions(content);
        }

        // Step 1d: Apply FilmGrain insertions
        if (enableFilmGrain)
        {
            content = ApplyFilmGrainInsertions(content);
        }

        // Step 1e: Apply DepthFog insertions
        if (enableDepthFog)
        {
            content = ApplyDepthFogInsertions(content);
        }

        // Step 1f: Apply TiltShift insertions
        if (enableTiltShift)
        {
            content = ApplyTiltShiftInsertions(content);
        }

        // Step 2: Apply LowerMinFocus replacements SECOND
        // This replaces 0.5001 → 0.0001 in BOTH the original shader lines
        // AND any ManualFocusAssist code blocks that were just inserted.
        if (enableLowerMinFocus)
        {
            content = ApplyLowerMinFocusReplacements(content);
        }

        // Step 3: Rename shader
        content = RenameShader(content, enableLowerMinFocus, enableManualFocusAssist, enableGhostFX, enableChromaticAberration, enableFilmGrain, enableDepthFog, enableTiltShift);

        // Step 4: Add header comment
        List<string> enabledMods = new List<string>();
        if (enableLowerMinFocus) enabledMods.Add("LowerMinFocus");
        if (enableManualFocusAssist) enabledMods.Add("ManualFocusAssist");
        if (enableGhostFX) enabledMods.Add("GhostFX");
        if (enableChromaticAberration) enabledMods.Add("ChromaticAberration");
        if (enableFilmGrain) enabledMods.Add("FilmGrain");
        if (enableDepthFog) enabledMods.Add("DepthFog");
        if (enableTiltShift) enabledMods.Add("TiltShift");
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
        content = ApplyChromaticAberrationInsertions(content);
        content = ApplyFilmGrainInsertions(content);
        content = ApplyDepthFogInsertions(content);
        content = ApplyTiltShiftInsertions(content);
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

        // Validate DepthFog anchors
        var fogAnchorsV = GetDepthFogAnchors();
        List<string> missingFogV = new List<string>();
        foreach (var anchor in fogAnchorsV)
        {
            if (!originalContent.Contains(anchor.SearchString))
                missingFogV.Add($"  - {anchor.Description}: \"{anchor.SearchString}\"");
        }
        if (missingFogV.Count > 0)
        {
            string msg = $"DepthFog validation FAILED — missing anchors:\n{string.Join("\n", missingFogV)}\n\n" +
                "VRCLens may have been updated. See maintenance steps in the patcher source code.";
            Debug.LogError($"{LOG_PREFIX} {msg}");
            EditorUtility.DisplayDialog("Verify Patcher — FAILED", msg, "OK");
            return;
        }
        messages.Add($"All {fogAnchorsV.Count} DepthFog anchors found.");

        // Validate TiltShift anchors
        var tsAnchorsV = GetTiltShiftAnchors();
        List<string> missingTSV = new List<string>();
        foreach (var anchor in tsAnchorsV)
        {
            if (!originalContent.Contains(anchor.SearchString))
                missingTSV.Add($"  - {anchor.Description}: \"{anchor.SearchString}\"");
        }
        if (missingTSV.Count > 0)
        {
            string msg = $"TiltShift validation FAILED — missing anchors:\n{string.Join("\n", missingTSV)}\n\n" +
                "VRCLens may have been updated. See maintenance steps in the patcher source code.";
            Debug.LogError($"{LOG_PREFIX} {msg}");
            EditorUtility.DisplayDialog("Verify Patcher — FAILED", msg, "OK");
            return;
        }
        messages.Add($"All {tsAnchorsV.Count} TiltShift anchors found.");

        // Patch the original (all mods) and compare to reference
        string patched = ApplyManualFocusAssistInsertions(originalContent);
        patched = ApplyGhostFXInsertions(patched);
        patched = ApplyChromaticAberrationInsertions(patched);
        patched = ApplyFilmGrainInsertions(patched);
        patched = ApplyDepthFogInsertions(patched);
        patched = ApplyTiltShiftInsertions(patched);
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
        Shader shader = PatchShader(true, true, true, true, true, true, true);
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
            new AnchorInfo(ANCHOR_TEXTURE_PROPERTY, "Properties: _FocusTex texture line"),
            new AnchorInfo(ANCHOR_PASS2_UNIFORMS, "Pass 2: _SensorScale uniform"),
            new AnchorInfo(ANCHOR_PASS6_UNIFORMS, "Pass 6: _FocusDistance uniform"),
            new AnchorInfo(ANCHOR_GHOSTFX_UNIFORMS, "Pass 2: white balance line"),
        };
    }

    // Optional blocks for _DepthAvatarTex — only inserted if MFA hasn't already declared them
    private static readonly string BLOCK_GHOSTFX_AVATAR_PROPERTY = @"
		// VRCLens_Custom BEGIN - Ghost FX Avatar Depth Texture
		_DepthAvatarTex (""Avatar Depth Texture"", 2D) = ""black"" {}
		// VRCLens_Custom END";

    private static readonly string BLOCK_GHOSTFX_AVATAR_SAMPLER = @"
			// VRCLens_Custom BEGIN - Ghost FX Avatar Depth Sampler
			sampler2D _DepthAvatarTex;
			// VRCLens_Custom END";

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

        // Conditional: _DepthAvatarTex property + sampler (only if MFA hasn't declared them)
        string rejoined = string.Join("\n", lines);
        if (!rejoined.Contains("_DepthAvatarTex"))
        {
            lines = new List<string>(rejoined.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
            // Property after _FocusTex (same anchor MFA uses, or after _DoFStrength if _FocusTex absent)
            insertions += InsertAfterLine(lines, ANCHOR_TEXTURE_PROPERTY, BLOCK_GHOSTFX_AVATAR_PROPERTY, "GhostFX avatar texture property");
            // Sampler in Pass 2 uniforms — after _SensorScale
            insertions += InsertAfterLine(lines, ANCHOR_PASS2_UNIFORMS, BLOCK_GHOSTFX_AVATAR_SAMPLER, "GhostFX avatar texture sampler");
            Debug.Log($"{LOG_PREFIX} Added _DepthAvatarTex declarations (MFA not enabled)");
        }
        else
        {
            Debug.Log($"{LOG_PREFIX} _DepthAvatarTex already declared by MFA, skipping");
        }

        Debug.Log($"{LOG_PREFIX} Applied {insertions} GhostFX insertion sites");

        return string.Join("\n", lines);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Chromatic Aberration insertion logic
    // ═══════════════════════════════════════════════════════════════════

    private static List<AnchorInfo> GetChromaticAberrationAnchors()
    {
        return new List<AnchorInfo>
        {
            new AnchorInfo(ANCHOR_PROPERTIES, "Properties: _DoFStrength line"),
            new AnchorInfo(ANCHOR_PASS6_UNIFORMS, "Pass 6: _FocusDistance uniform"),
            new AnchorInfo(ANCHOR_GHOSTFX_UNIFORMS, "Pass 2: white balance line"),
        };
    }

    private static string ApplyChromaticAberrationInsertions(string content)
    {
        var lines = new List<string>(content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
        int insertions = 0;

        // Process from bottom to top so line indices don't shift

        // CA application — after white balance line (before tone mapping), same anchor as GhostFX
        insertions += InsertAfterLine(lines, ANCHOR_GHOSTFX_UNIFORMS, BLOCK_CA_PASS2, "ChromaticAberration pass 2 application");

        // CA uniforms in Pass 2 — after second _FocusDistance uniform (same as GhostFX)
        insertions += InsertAfterLine(lines, ANCHOR_PASS6_UNIFORMS, BLOCK_CA_UNIFORMS, "ChromaticAberration uniforms", false, 2);

        // CA properties — after _DoFStrength line
        insertions += InsertAfterLine(lines, ANCHOR_PROPERTIES, BLOCK_CA_PROPERTIES, "ChromaticAberration properties");

        Debug.Log($"{LOG_PREFIX} Applied {insertions} ChromaticAberration insertion sites");

        return string.Join("\n", lines);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Film Grain insertion logic
    // ═══════════════════════════════════════════════════════════════════

    private static List<AnchorInfo> GetFilmGrainAnchors()
    {
        return new List<AnchorInfo>
        {
            new AnchorInfo(ANCHOR_PROPERTIES, "Properties: _DoFStrength line"),
            new AnchorInfo(ANCHOR_PASS6_UNIFORMS, "Pass 6: _FocusDistance uniform"),
            new AnchorInfo(ANCHOR_TONEMAPPING, "Pass 2: tonemap line"),
        };
    }

    private static string ApplyFilmGrainInsertions(string content)
    {
        var lines = new List<string>(content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
        int insertions = 0;

        // Process from bottom to top so line indices don't shift

        // Film Grain application — after tone mapping (before dithering)
        insertions += InsertAfterLine(lines, ANCHOR_TONEMAPPING, BLOCK_GRAIN_PASS2, "FilmGrain pass 2 application");

        // Film Grain uniforms in Pass 2 — after second _FocusDistance uniform
        insertions += InsertAfterLine(lines, ANCHOR_PASS6_UNIFORMS, BLOCK_GRAIN_UNIFORMS, "FilmGrain uniforms", false, 2);

        // Film Grain properties — after _DoFStrength line
        insertions += InsertAfterLine(lines, ANCHOR_PROPERTIES, BLOCK_GRAIN_PROPERTIES, "FilmGrain properties");

        Debug.Log($"{LOG_PREFIX} Applied {insertions} FilmGrain insertion sites");

        return string.Join("\n", lines);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Depth Fog insertion logic
    // ═══════════════════════════════════════════════════════════════════

    private static List<AnchorInfo> GetDepthFogAnchors()
    {
        return new List<AnchorInfo>
        {
            new AnchorInfo(ANCHOR_PROPERTIES, "Properties: _DoFStrength line"),
            new AnchorInfo(ANCHOR_PASS6_UNIFORMS, "Pass 6: _FocusDistance uniform"),
            new AnchorInfo(ANCHOR_GHOSTFX_UNIFORMS, "Pass 2: white balance line"),
        };
    }

    private static string ApplyDepthFogInsertions(string content)
    {
        var lines = new List<string>(content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
        int insertions = 0;

        // Process from bottom to top so line indices don't shift

        // Depth Fog application — after white balance line (before tone mapping)
        insertions += InsertAfterLine(lines, ANCHOR_GHOSTFX_UNIFORMS, BLOCK_DEPTHFOG_PASS2, "DepthFog pass 2 application");

        // Depth Fog uniforms in Pass 2 — after second _FocusDistance uniform
        insertions += InsertAfterLine(lines, ANCHOR_PASS6_UNIFORMS, BLOCK_DEPTHFOG_UNIFORMS, "DepthFog uniforms", false, 2);

        // Depth Fog properties — after _DoFStrength line
        insertions += InsertAfterLine(lines, ANCHOR_PROPERTIES, BLOCK_DEPTHFOG_PROPERTIES, "DepthFog properties");

        Debug.Log($"{LOG_PREFIX} Applied {insertions} DepthFog insertion sites");

        return string.Join("\n", lines);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Tilt-Shift insertion logic
    // ═══════════════════════════════════════════════════════════════════

    private static List<AnchorInfo> GetTiltShiftAnchors()
    {
        return new List<AnchorInfo>
        {
            new AnchorInfo(ANCHOR_PROPERTIES, "Properties: _DoFStrength line"),
            new AnchorInfo(ANCHOR_PASS6_UNIFORMS, "Pass 0: _FocusDistance uniform"),
            new AnchorInfo(ANCHOR_BOUNDS_CHECK, "Pass 0: bounds check after CoC if/else"),
            new AnchorInfo(TILTSHIFT_PASS1_UNIFORM_ANCHOR, "Pass 1: _EnableDoF + _SensorScale uniforms"),
            new AnchorInfo(TILTSHIFT_PASS1_DOF_OLD, "Pass 1: if(_EnableDoF) condition"),
        };
    }

    private static string ApplyTiltShiftInsertions(string content)
    {
        var lines = new List<string>(content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
        int insertions = 0;

        // Process from bottom to top so line indices don't shift

        // Tilt-Shift CoC modification — BEFORE the bounds check line in Pass 0
        // Must go after the if/else block that resets col.a=1.0 in non-render mode
        insertions += InsertBeforeLine(lines, ANCHOR_BOUNDS_CHECK, BLOCK_TILTSHIFT_PASS0, "TiltShift pass 0 CoC modification");

        // Tilt-Shift uniforms in Pass 0 — after first _FocusDistance uniform (occurrence 1)
        insertions += InsertAfterLine(lines, ANCHOR_PASS6_UNIFORMS, BLOCK_TILTSHIFT_PASS0_UNIFORMS, "TiltShift pass 0 uniforms", false, 1);

        // Tilt-Shift properties — after _DoFStrength line
        insertions += InsertAfterLine(lines, ANCHOR_PROPERTIES, BLOCK_TILTSHIFT_PROPERTIES, "TiltShift properties");

        // Rejoin for text replacements in Pass 1
        content = string.Join("\n", lines);

        // Pass 1: Add _TiltShift uniform declaration
        if (content.Contains(TILTSHIFT_PASS1_UNIFORM_ANCHOR))
        {
            content = content.Replace(TILTSHIFT_PASS1_UNIFORM_ANCHOR, TILTSHIFT_PASS1_UNIFORM_NEW);
            insertions++;
            Debug.Log($"{LOG_PREFIX} Applied TiltShift Pass 1 uniform");
        }
        else
        {
            Debug.LogWarning($"{LOG_PREFIX} Could not find Pass 1 uniform anchor for TiltShift");
        }

        // Pass 1: Modify _EnableDoF condition to also trigger on TiltShift
        if (content.Contains(TILTSHIFT_PASS1_DOF_OLD))
        {
            content = content.Replace(TILTSHIFT_PASS1_DOF_OLD, TILTSHIFT_PASS1_DOF_NEW);
            insertions++;
            Debug.Log($"{LOG_PREFIX} Applied TiltShift Pass 1 DoF condition override");
        }
        else
        {
            Debug.LogWarning($"{LOG_PREFIX} Could not find Pass 1 DoF condition for TiltShift");
        }

        Debug.Log($"{LOG_PREFIX} Applied {insertions} TiltShift insertion/replacement sites");

        return content;
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
    /// Find a line containing searchString and insert blockText BEFORE it.
    /// </summary>
    private static int InsertBeforeLine(List<string> lines, string searchString, string blockText,
        string description, int occurrence = 1)
    {
        int found = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Contains(searchString))
            {
                found++;
                if (found != occurrence) continue;

                string[] blockLines = blockText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                int startLine = blockLines.Length > 0 && string.IsNullOrWhiteSpace(blockLines[0]) ? 1 : 0;

                for (int b = startLine; b < blockLines.Length; b++)
                {
                    lines.Insert(i + (b - startLine), blockLines[b]);
                }

                Debug.Log($"{LOG_PREFIX} Inserted {description} before line {i + 1}");
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
    private static string RenameShader(string content, bool enableLowerMinFocus, bool enableManualFocusAssist, bool enableGhostFX = false, bool enableChromaticAberration = false, bool enableFilmGrain = false, bool enableDepthFog = false, bool enableTiltShift = false)
    {
        // Build suffix from enabled mods
        var parts = new List<string>();
        if (enableLowerMinFocus) parts.Add("LowerMinFocus");
        if (enableManualFocusAssist) parts.Add("ManualFocusAssist");
        if (enableGhostFX) parts.Add("GhostFX");
        if (enableChromaticAberration) parts.Add("ChromaticAberration");
        if (enableFilmGrain) parts.Add("FilmGrain");
        if (enableDepthFog) parts.Add("DepthFog");
        if (enableTiltShift) parts.Add("TiltShift");
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
