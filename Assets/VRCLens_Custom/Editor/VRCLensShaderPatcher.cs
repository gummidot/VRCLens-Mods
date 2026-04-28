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
					// Handheld shake: organic multi-layer wobble
					float ghostDistShake = 0.0;
					float ghostZoneShift = 0.0;
					float ghostOpacityShake = 1.0;
					float ghostDistGradient = 0.0;
					if(_GhostFXShake > 0.001) {
						float shakeSpd = lerp(0.1, 5.0, _GhostFXShakeSpeed);
						float t = _Time.y * shakeSpd;
						float ghostShakeAmt = _GhostFXShake * 0.5;
						// Amplitude envelope: tremor comes in bursts
						float envelope = 0.6 + 0.4 * sin(t * 0.31) * sin(t * 0.17);
						// Low drift (slow wander) + medium tremor + fast micro-jerk
						float drift = sin(t * 0.53) + sin(t * 0.79) * 0.7;
						float tremor = sin(t * 2.71) * 0.5 + sin(t * 3.93) * 0.3;
						float jerk = sin(sin(t * 7.1) * 2.0) * 0.15;
						ghostAngleRad += (drift + tremor + jerk) * ghostShakeAmt * envelope;
						// Zone boundary shift: filter moves off-center with hand
						ghostZoneShift = (sin(t * 0.43) + sin(t * 1.19) * 0.5) * _GhostFXShake * 0.06 * envelope;
						// Opacity flutter: filter tilt changes refraction strength
						ghostOpacityShake = 1.0 - _GhostFXShake * 0.25 * (1.0 - envelope);
						// Per-pixel distance gradient: filter tilt = stronger refraction at edge
						ghostDistGradient = (sin(t * 0.37) + sin(t * 0.89) * 0.6) * _GhostFXShake * 0.3 * envelope;
						// Distance wobble
						if(_GhostFXShakeDist > 0.001) {
							float driftD = sin(t * 0.61) + sin(t * 1.07) * 0.5;
							float tremorD = sin(t * 3.17) * 0.3;
							ghostDistShake = (driftD + tremorD) * envelope * _GhostFXShakeDist * _GhostFXShake * 0.04;
						}
					}
					half2 ghostDir = half2(cos(ghostAngleRad), sin(ghostAngleRad));
					float ghostProj = dot(ghostCenter, ghostDir) + ghostZoneShift;

					float ghostZoneMask;
					// Per-pixel distance: base + wobble + gradient (stronger deeper into ghost zone)
					float ghostPixelDist = _GhostFXDistance + ghostDistShake + abs(ghostProj) * ghostDistGradient;
					half2 ghostBaseOffset = ghostDir * ghostPixelDist;
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
							// Chromatic smear: R/B sample slightly further/closer than ghost tap
							if(_GhostFXChroma > 0.001) {
								half2 chromaVec = gUV - sbsUV0;
								float chromaScale = _GhostFXChroma * _GhostFXChroma * 0.4;
								half rShift = tex2D(_HirabikiVRCLensPassTexture, clamp(gUV + chromaVec * chromaScale, 0.001, 0.999)).r;
								half bShift = tex2D(_HirabikiVRCLensPassTexture, clamp(gUV - chromaVec * chromaScale, 0.001, 0.999)).b;
								gSample.r = rShift;
								gSample.b = bShift;
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
					col.rgb = lerp(col.rgb, ghostBlended, ghostZoneMask * _GhostFXOpacity * ghostOpacityShake);
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
				// Transverse CA: radial color fringing. 3-tap radial blur with a tight
				// ±15% spread around the actual offset gives the smooth color smear of
				// real lens dispersion without diluting the chromatic separation (the
				// previous ±50% spread averaged over 3× the offset and washed out the
				// effect). Offset has a flat base + radius² growth so the center isn't
				// dead at moderate slider values.
				if(_TransverseCA > 0.001) {
					float2 caCenter = sbsUV0 - 0.5;
					float caRadius = length(caCenter);
					// Multiply by caCenter (vector) directly instead of going through
					// caDir = caCenter / caRadius. caDir is undefined at the optical
					// center and the prior `max(0.001, caRadius)` snap meant a small
					// disc near center got a fixed-direction offset that all sampled
					// the same nearby pixel -> the convergent star artifact at center.
					// caCenter goes smoothly to zero at the center, no singularity.
					float caScale = _TransverseCA * (0.3 + 0.7 * caRadius * caRadius) * 0.4;
					float2 caOffsetVec = caCenter * caScale;
					// OOB edge decay (sample farthest from center for the bound)
					float2 caUVrFar = sbsUV0 + caOffsetVec * 1.15;
					float2 caUVbFar = sbsUV0 - caOffsetVec * 1.15;
					float2 caOobR = max(float2(0,0), max(-caUVrFar, caUVrFar - float2(1,1)));
					float caEdgeR = exp(-max(caOobR.x, caOobR.y) * 80.0);
					float2 caOobB = max(float2(0,0), max(-caUVbFar, caUVbFar - float2(1,1)));
					float caEdgeB = exp(-max(caOobB.x, caOobB.y) * 80.0);
					// 3-tap blur centered on the offset, weights 2/1/1 (sum=4). Mean
					// offset = (2*1.0 + 1*0.85 + 1*1.15)/4 = 1.0 -> chromatic shift
					// preserved; samples spread only ±15% around the peak for a soft
					// fringe instead of the prior wash-out.
					float2 caUVr0 = sbsUV0 + caOffsetVec;
					float2 caUVr1 = sbsUV0 + caOffsetVec * 0.85;
					float2 caUVr2 = caUVrFar;
					float2 caUVb0 = sbsUV0 - caOffsetVec;
					float2 caUVb1 = sbsUV0 - caOffsetVec * 0.85;
					float2 caUVb2 = caUVbFar;
					float rShifted = (tex2D(_HirabikiVRCLensPassTexture, clamp(caUVr0, 0.001, 0.999)).r * 2.0
					                + tex2D(_HirabikiVRCLensPassTexture, clamp(caUVr1, 0.001, 0.999)).r
					                + tex2D(_HirabikiVRCLensPassTexture, clamp(caUVr2, 0.001, 0.999)).r) * 0.25;
					float bShifted = (tex2D(_HirabikiVRCLensPassTexture, clamp(caUVb0, 0.001, 0.999)).b * 2.0
					                + tex2D(_HirabikiVRCLensPassTexture, clamp(caUVb1, 0.001, 0.999)).b
					                + tex2D(_HirabikiVRCLensPassTexture, clamp(caUVb2, 0.001, 0.999)).b) * 0.25;
					col.r = lerp(col.r, rShifted, caEdgeR);
					col.b = lerp(col.b, bShifted, caEdgeB);
				}
				// Axial CA: depth-scaled per-channel disc blur (soft colored halos on distant objects)
				if(_AxialCA > 0.001) {
					// Linearize depth (same formula as getLinearEyeDepth in MFA, inlined
					// because that helper is only injected when ManualFocusAssist is enabled)
					// VRCLens camera: nearPlane=0.04, farPlane=32000.0
					float axDepthRaw = SAMPLE_DEPTH_TEXTURE(_DepthTex, sbsUV0);
					float axFar = 32000.0; float axNear = 0.04;
					float axZ = (axFar / axNear - 1.0) / axFar;
					float axW = 1.0 / axFar;
					float axDepth = 1.0 / (axDepthRaw * axZ + axW);
					// Ramp: no CA at camera, full CA at 20m+ so the effect shows on a
					// wider variety of shots (was 50m, only visible at long distances).
					float axDepthFade = saturate(axDepth / 20.0);
					// Multiplier softened 100 -> 50: with the B-sharp Option A path
					// below, slider=1 at 100 flooded bright scenes red. 50 keeps the
					// asymmetric chromatic character (R bleed, B sharp) but caps the
					// peak so cranking the slider stays photographic rather than
					// wholesale red-tint. Slider=0.25 matches the prior 12-px sweet spot.
					float axPx = _AxialCA * 50.0 * axDepthFade;
					float2 axTs = axPx / _ScreenParams.xy;
					// 8 directions at 45° intervals
					float2 axD0 = float2(1.0, 0.0) * axTs;
					float2 axD1 = float2(0.707, 0.707) * axTs;
					float2 axD2 = float2(0.0, 1.0) * axTs;
					float2 axD3 = float2(-0.707, 0.707) * axTs;
					// R channel: 2-ring weighted disc blur (center=3, inner@0.5r=2, outer@1.0r=1)
					float axSrcR = tex2D(_HirabikiVRCLensPassTexture, sbsUV0).r;
					float rAcc = axSrcR * 3.0;
					// Inner ring at 0.5× radius
					rAcc += (tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 + axD0*0.5, 0.001, 0.999)).r + tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 - axD0*0.5, 0.001, 0.999)).r) * 2.0;
					rAcc += (tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 + axD1*0.5, 0.001, 0.999)).r + tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 - axD1*0.5, 0.001, 0.999)).r) * 2.0;
					rAcc += (tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 + axD2*0.5, 0.001, 0.999)).r + tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 - axD2*0.5, 0.001, 0.999)).r) * 2.0;
					rAcc += (tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 + axD3*0.5, 0.001, 0.999)).r + tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 - axD3*0.5, 0.001, 0.999)).r) * 2.0;
					// Outer ring at full radius
					rAcc += tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 + axD0, 0.001, 0.999)).r + tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 - axD0, 0.001, 0.999)).r;
					rAcc += tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 + axD1, 0.001, 0.999)).r + tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 - axD1, 0.001, 0.999)).r;
					rAcc += tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 + axD2, 0.001, 0.999)).r + tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 - axD2, 0.001, 0.999)).r;
					rAcc += tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 + axD3, 0.001, 0.999)).r + tex2D(_HirabikiVRCLensPassTexture, clamp(sbsUV0 - axD3, 0.001, 0.999)).r;
					// Additive composition: add the axial halo delta on top of whatever
					// transverse already wrote into col.r. delta = blurredR - sourceR is
					// what axial 'contributes' (the halation around bright pixels minus
					// what was at this pixel originally). Lets transverse's chromatic
					// shift survive when both effects are enabled.
					col.r += (rAcc - 27.0 * axSrcR) / 27.0;
					// B channel: kept sharp by axial's design (Option A asymmetric
					// fringing). Axial does NOT touch col.b -- transverse's chromatic
					// shift on B passes through unmodified. With axial alone, col.b
					// stays at the un-shifted source (since transverse is gated off).
				}
				// VRCLens_Custom END";

    // ── Film Grain code blocks ──────────────────────────────────────────

    // Anchor: after tone mapping, before dithering
    private const string ANCHOR_TONEMAPPING = "col.rgb = tonemap(col.rgb, _TonemapMode);";

    private static readonly string BLOCK_GRAIN_PROPERTIES = @"
		// VRCLens_Custom BEGIN - Film Grain Properties
		[Header(Film Grain)]
		[Toggle] _FilmGrainEnable (""Enable Film Grain"", Float) = 0
		_FilmGrain (""Film Grain"", Range(0.0, 1.0)) = 0.25
		_FilmGrainSize (""Grain Size"", Range(0.0, 1.0)) = 0.0
		_FilmGrainBrightness (""Grain Brightness"", Range(0.0, 1.0)) = 0.5
		// VRCLens_Custom END";

    private static readonly string BLOCK_GRAIN_UNIFORMS = @"
			// VRCLens_Custom BEGIN - Film Grain Uniforms
			uniform float _FilmGrainEnable, _FilmGrain, _FilmGrainSize, _FilmGrainBrightness;
			// VRCLens_Custom END";

    private static readonly string BLOCK_GRAIN_PASS2 = @"
				// VRCLens_Custom BEGIN - Film Grain
				if(_FilmGrainEnable > 0.5) {
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
		[Toggle] _TiltShiftEnable (""Enable Tilt-Shift"", Float) = 0
		_TiltShift (""Tilt-Shift Blur"", Range(0.0, 1.0)) = 0.25
		_TiltShiftPos (""Focus Band Position"", Range(0.0, 1.0)) = 0.0
		_TiltShiftWidth (""Focus Band Width"", Range(0.0, 0.5)) = 0.0
		_TiltAngle (""Focus Band Angle"", Range(0.0, 1.0)) = 0.5
		// VRCLens_Custom END";

    // Tilt-shift uniforms go in Pass 0 (occurrence 1 of _FocusDistance uniform)
    private static readonly string BLOCK_TILTSHIFT_PASS0_UNIFORMS = @"
			// VRCLens_Custom BEGIN - Tilt-Shift Uniforms
			uniform float _TiltShiftEnable, _TiltShift, _TiltShiftPos, _TiltShiftWidth, _TiltAngle;
			// VRCLens_Custom END";

    // Tilt-shift Pass 1 text replacements: enable blur even when DoF is off
    private const string TILTSHIFT_PASS1_DOF_OLD = "if(_EnableDoF) {";
    private const string TILTSHIFT_PASS1_DOF_NEW = "if(_EnableDoF || _TiltShiftEnable > 0.5) { // VRCLens_Custom: TiltShift";
    private const string TILTSHIFT_PASS1_UNIFORM_ANCHOR = "uniform bool _EnableDoF;\n\t\t\tuniform float _SensorScale";
    private const string TILTSHIFT_PASS1_UNIFORM_NEW = "uniform bool _EnableDoF;\n\t\t\tuniform float _TiltShiftEnable; // VRCLens_Custom: TiltShift\n\t\t\tuniform float _SensorScale";

    private static readonly string BLOCK_TILTSHIFT_PASS0 = @"
				// VRCLens_Custom BEGIN - Tilt-Shift
				if(_TiltShiftEnable > 0.5) {
					float tsDepthRaw = SAMPLE_DEPTH_TEXTURE(_DepthTex, uv);
					float tsDepthZ = 1.0 / ((32000.0/0.04 - 1.0)/32000.0 * tsDepthRaw + 1.0/32000.0);
					float tsAngleDeg = (_TiltAngle * 180.0) - 90.0;
					float tsRad = tsAngleDeg * (3.14159265 / 180.0);
					float tsS, tsC;
					sincos(tsRad, tsS, tsC);
					float2 tsCentered = uv - float2(0.5, 0.5);
					float tsAspect = _ScreenParams.x / _ScreenParams.y;
					tsCentered.x *= tsAspect;
					float tsRotY = tsCentered.x * tsS + tsCentered.y * tsC;
					float tsBandDist = abs(tsRotY - (_TiltShiftPos - 0.5));
					float tsMask = smoothstep(_TiltShiftWidth, _TiltShiftWidth + 0.15, tsBandDist);
					float tsDepthFactor = saturate(tsDepthZ / 30.0);
					float tsStrength = _TiltShift * _TiltShift;
					float tsCoC = tsMask * tsDepthFactor * tsStrength * 300.0;
					col.a = _EnableDoF ? ((abs(col.a) > abs(tsCoC)) ? col.a : sign(col.a + 0.0001) * tsCoC) : tsCoC;
				}
				// VRCLens_Custom END";

    // ── Fisheye Lens code blocks ────────────────────────────────────────

    // Anchor for Pass 2: right before rawColor sampling — UV distortion goes here
    private const string ANCHOR_FISHEYE_UV = "half2 sbsUV1 = isPreview ? sbsUV0Mask : half2(frac(i.uv.x * (1 + isSBSTrue)), i.uv.y);";

    // Anchor for the rawColor sampling line — supersample injection goes immediately after
    private const string ANCHOR_RAWCOLOR = "half4 rawColor = tex2D(_HirabikiVRCLensPassTexture, sbsUV0);";

    // Anchor for Pass 2: after dithering — fisheye mask goes here (after all post-processing)
    private const string ANCHOR_DITHERING = "col = dither(uv, col)";

    private static readonly string BLOCK_FISHEYE_PROPERTIES = @"
		// VRCLens_Custom BEGIN - Fisheye Lens Properties
		[Header(Fisheye Lens)]
		[Toggle] _FisheyeEnable (""Enable Fisheye"", Float) = 0
		_FisheyeStrength (""Fisheye Strength"", Range(0.0, 10.0)) = 1.2
		_FisheyeZoom (""Fisheye Zoom"", Range(0.0, 1.0)) = 0.0
		_FisheyeEdgeSoftness (""Fisheye Edge Softness"", Range(0.0, 1.0)) = 0.25
		_FisheyeShape (""Fisheye Shape"", Range(0.0, 1.0)) = 0.0
		_FisheyePincushion (""Fisheye Pincushion"", Range(0.0, 1.0)) = 0.0
		_FisheyeLensSize (""Fisheye Lens Size"", Range(0.4, 1.0)) = 0.65
		_FisheyeCenterX (""Fisheye Center X"", Range(-0.5, 0.5)) = 0.0
		_FisheyeCenterY (""Fisheye Center Y"", Range(-0.5, 0.5)) = 0.0
		[Toggle] _FisheyeDebug (""Fisheye Debug Mask"", Float) = 0.0
		// VRCLens_Custom END";

    private static readonly string BLOCK_FISHEYE_UNIFORMS = @"
			// VRCLens_Custom BEGIN - Fisheye Lens Uniforms
			uniform float _FisheyeEnable;
			uniform float _FisheyeStrength;
			uniform float _FisheyeZoom;
			uniform float _FisheyeEdgeSoftness;
			uniform float _FisheyeShape;
			uniform float _FisheyePincushion;
			uniform float _FisheyeLensSize;
			uniform float _FisheyeCenterX;
			uniform float _FisheyeCenterY;
			uniform float _FisheyeDebug;
			// VRCLens_Custom END";

    private static readonly string BLOCK_FISHEYE_PASS2 = @"
				// VRCLens_Custom BEGIN - Fisheye Lens
				float fisheyeMask = 1.0;
				// Pincushion auto-overrides barrel: when pincushion > 0, fade out
				// the Strength slider's contribution so pincushion is the sole warp.
				// Lets users keep Strength parked at a sensible default (e.g. 25%)
				// without polluting pincushion mode. Smooth fade prevents a jump as
				// pincushion crosses 0.
				float pincushionGate = 1.0 - smoothstep(0.0, 0.05, _FisheyePincushion);
				float effectiveStrength = _FisheyeStrength * pincushionGate - _FisheyePincushion;
				if(_FisheyeEnable > 0.5 && abs(effectiveStrength) > 0.001) {
					float2 origUV = sbsUV0;
					float fishAspect = _ScreenParams.x / _ScreenParams.y;
					// Center offset: shifts ONLY the focal point of distortion. The
					// visible mask circle and the auto-zoom level stay exactly where
					// iter-13 puts them — so adjusting Center X/Y does not change the
					// framing or zoom. The per-pixel maxScale below handles any OOB
					// risk at the worst pixels (those farthest from focalUV).
					float2 centerOffset = float2(_FisheyeCenterX, _FisheyeCenterY);
					float2 focalUV = 0.5 + centerOffset;
					// Auto-zoom Newton: iter-13 form, NOT offset-aware. autoZoomScale
					// stays the same at all Center X/Y values, so the framing doesn't
					// shrink as the focal point moves.
					float K = max(0.0, effectiveStrength) * (fishAspect * fishAspect + 1.0);
					float u = min(0.499, pow(0.499 / max(K, 0.001), 0.3333));
					u -= (u + K*u*u*u - 0.499) / (1.0 + 3.0*K*u*u);
					u -= (u + K*u*u*u - 0.499) / (1.0 + 3.0*K*u*u);
					u -= (u + K*u*u*u - 0.499) / (1.0 + 3.0*K*u*u);
					float safeRadius = max(0.05, u);
					float autoZoomScale = 0.5 / safeRadius;
					// User zoom adds on top of auto-zoom for further framing
					float zoomScale = autoZoomScale + _FisheyeZoom * 0.5;
					// Mask: anchored at SCREEN center (no centerOffset). Iter 13 byte-identical.
					float2 maskCenter = (origUV - 0.5) / zoomScale;
					// Mask shape morphs from oval (shape=0) to true on-screen circle
					// (shape=1). center coords are in raw UV units, so length(maskCenter)
					// is a UV-space circle that appears stretched horizontally on a
					// 16:9 screen. Weighting cx by aspect makes the radius measure in
					// equal screen pixels, producing a true circle. boundaryScale
					// shrinks from 0.65 (oval extends past screen, only corners cut)
					// to 0.5 (circle inscribed in screen height -- touches top/bottom,
					// black bars on L/R) so the slider=1 circle fits the screen.
					// fwidth(maskR) provides 1-pixel screen-space AA at softness=0.
					float xStretch = lerp(1.0, fishAspect, _FisheyeShape);
					float maskR = length(float2(maskCenter.x * xStretch, maskCenter.y));
					// _FisheyeLensSize scales the entire boundary uniformly. Default
					// 0.65 reproduces the original behavior; lower tightens the visible
					// region; higher pushes the mask past the screen so corners are not
					// clipped. Shape lerp factor is 1 -> 0.5/0.65 to keep slider=1 a
					// circle inscribed in screen height at default radius.
					float boundaryScale = _FisheyeLensSize * lerp(1.0, 0.5 / 0.65, _FisheyeShape);
					float maskBoundary = boundaryScale / autoZoomScale;
					float maskAA = fwidth(maskR);
					float maskSoft = maskAA + _FisheyeEdgeSoftness * maskBoundary;
					// boundaryMask: outer-edge fade. Used both for col attenuation and
					// for the UV blend below (geometric continuity at the mask edge).
					float boundaryMask = 1.0 - smoothstep(maskBoundary - maskSoft, maskBoundary, maskR);
					fisheyeMask = boundaryMask;
					// Distortion: pivots at focalUV in source UV. Compute the auto-zoomed
					// source position first (iter 13's `0.5 + maskCenter`), then build the
					// warp displacement relative to focalUV.
					float2 srcPos = 0.5 + maskCenter;
					float2 rel = srcPos - focalUV;
					float2 ac = rel;
					ac.x *= fishAspect;
					float r = length(ac);
					float scale;
					if(effectiveStrength >= 0.0) {
						// Barrel: cubic warp r_src = r_out * (1 + k*r²). Per-axis source
						// headroom is sign-aware (focal-toward side: 0.499 - |centerOffset|;
						// focal-away side: 0.499 + |centerOffset|).
						scale = 1.0 + effectiveStrength * r * r;
						float boundX = 0.499 - sign(rel.x) * centerOffset.x;
						float boundY = 0.499 - sign(rel.y) * centerOffset.y;
						float maxScaleX = boundX / max(abs(rel.x), 0.001);
						float maxScaleY = boundY / max(abs(rel.y), 0.001);
						float maxScale = min(maxScaleX, maxScaleY);
						// Conditional saturation. At centerOffset = 0 the iter-13 Newton
						// guarantees scale == maxScale at the worst pixel and the visible
						// look depends on the warp filling the mask edge to that bound.
						// A soft saturation here uniformly reduces edge warp by ~37%,
						// shrinking the apparent extent of the fisheye and making the
						// auto-zoom crop look more zoomed in. So at offset = 0, fall
						// back to iter-13 hard clamp (byte-identical look). At any
						// non-zero offset, switch to the smooth exponential saturation
						// + softer safety fade -- those eliminate the per-pixel-clamp
						// streak band that hard clamping causes when the per-axis bound
						// is asymmetric. Smoothly blend between the two regimes via
						// `offsetT` so there's no visible step as the slider crosses 0.
						float maxOffset = max(abs(centerOffset.x), abs(centerOffset.y));
						float offsetT = smoothstep(0.0, 0.05, maxOffset);
						float headroom = max(maxScale - 1.0, 0.001);
						float scaleSat = 1.0 + headroom * (1.0 - exp(-max(scale - 1.0, 0.0) / headroom));
						float scaleHard = min(scale, maxScale);
						scale = lerp(scaleHard, scaleSat, offsetT);
						// Safety fade only in offset regime (offsetT > 0).
						float satRatio = (scale - 1.0) / max(maxScale - 1.0, 0.001);
						fisheyeMask *= 1.0 - 0.7 * offsetT * smoothstep(0.85, 0.99, satRatio);
					} else {
						// Pincushion: Lorentzian r_src = r_out / (1 + |k|*r²) avoids the
						// cubic fold-back that produced visible image doubling at high
						// pincushion. r_src peaks at r = 1/sqrt(|k|) with value
						// 1/(2*sqrt(|k|)); past that point r_src drifts back down and
						// would re-cover earlier source content. Holding r_src at its
						// peak past the turning point keeps the warp non-decreasing.
						float pk = -effectiveStrength;
						scale = 1.0 / (1.0 + pk * r * r);
						float rPeak = 1.0 / sqrt(pk);
						if(r > rPeak) {
							float maxRsrc = 1.0 / (2.0 * sqrt(pk));
							scale = maxRsrc / max(r, 0.001);
						}
					}
					// Defensive floor: redundant for the new pincushion branch but kept
					// to guarantee scale stays positive across all paths.
					scale = max(scale, 0.05);
					ac *= scale;
					ac.x /= fishAspect;
					float2 distortedUV = focalUV + ac;
					// UV-blend uses boundaryMask (NOT fisheyeMask), so the safety-fade
					// band on the focal-opposite side does not interpolate UVs. At the
					// mask boundary, scale -> 1 so distortedUV -> focalUV + rel = srcPos:
					// endpoints meet smoothly even with offset focal point.
					sbsUV0 = lerp(srcPos, distortedUV, boundaryMask);
				}
				// VRCLens_Custom END";

    private static readonly string BLOCK_FISHEYE_SUPERSAMPLE = @"
				// VRCLens_Custom BEGIN - Fisheye Anti-Streak Supersample
				if(_FisheyeEnable > 0.5 && abs(effectiveStrength) > 0.001 && fisheyeMask > 0.001) {
					float2 ssDx = ddx(sbsUV0);
					float2 ssDy = ddy(sbsUV0);
					half4 ssC1 = tex2D(_HirabikiVRCLensPassTexture, sbsUV0 + 0.5 * ssDx);
					half4 ssC2 = tex2D(_HirabikiVRCLensPassTexture, sbsUV0 - 0.5 * ssDx);
					half4 ssC3 = tex2D(_HirabikiVRCLensPassTexture, sbsUV0 + 0.5 * ssDy);
					half4 ssC4 = tex2D(_HirabikiVRCLensPassTexture, sbsUV0 - 0.5 * ssDy);
					rawColor = (rawColor + ssC1 + ssC2 + ssC3 + ssC4) * 0.2;
				}
				// VRCLens_Custom END";

    private static readonly string BLOCK_FISHEYE_PASS2_MASK = @"
				// VRCLens_Custom BEGIN - Fisheye Lens Mask
				col.rgb *= fisheyeMask;
				// Debug viz: green = inside oval, yellow = falloff zone, red = outside.
				if(_FisheyeDebug > 0.5 && _FisheyeEnable > 0.5) {
					float3 dbgInside = float3(0.0, 1.0, 0.0);
					float3 dbgFalloff = float3(1.0, 1.0, 0.0);
					float3 dbgOutside = float3(1.0, 0.0, 0.0);
					float3 dbgCol = fisheyeMask > 0.99 ? dbgInside : (fisheyeMask > 0.01 ? dbgFalloff : dbgOutside);
					col.rgb = lerp(col.rgb, dbgCol, 0.5);
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
    public static Shader PatchShader(bool enableLowerMinFocus, bool enableManualFocusAssist, bool enableGhostFX = false, bool enableChromaticAberration = false, bool enableFilmGrain = false, bool enableDepthFog = false, bool enableTiltShift = false, bool enableFisheye = false, bool enableColorGrading = false)
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

        // Validate Fisheye anchors if that feature is enabled
        if (enableFisheye)
        {
            var fisheyeAnchors = GetFisheyeAnchors();
            List<string> missingFisheye = new List<string>();
            foreach (var anchor in fisheyeAnchors)
            {
                if (!content.Contains(anchor.SearchString))
                    missingFisheye.Add($"  - {anchor.Description}: \"{anchor.SearchString}\"");
            }
            if (missingFisheye.Count > 0)
            {
                string errorMsg = $"Cannot patch shader -- expected Fisheye anchor lines not found.\n" +
                    $"VRCLens may have been updated with incompatible changes.\n\n" +
                    $"Missing anchors:\n{string.Join("\n", missingFisheye)}";
                Debug.LogError($"{LOG_PREFIX} {errorMsg}");
                return null;
            }
        }

        // Validate ColorGrading anchors if that feature is enabled
        if (enableColorGrading)
        {
            var cgAnchors = GetColorGradingAnchors();
            List<string> missingCG = new List<string>();
            foreach (var anchor in cgAnchors)
            {
                if (!content.Contains(anchor.SearchString))
                    missingCG.Add($"  - {anchor.Description}: \"{anchor.SearchString}\"");
            }
            if (missingCG.Count > 0)
            {
                string errorMsg = $"Cannot patch shader -- expected ColorGrading anchor lines not found.\n" +
                    $"VRCLens may have been updated with incompatible changes.\n\n" +
                    $"Missing anchors:\n{string.Join("\n", missingCG)}";
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

        // Step 1g: Apply Fisheye insertions
        if (enableFisheye)
        {
            content = ApplyFisheyeInsertions(content);
        }

        // Step 1h: Apply ColorGrading insertions
        if (enableColorGrading)
        {
            content = ApplyColorGradingInsertions(content);
        }

        // Step 2: Apply LowerMinFocus replacements SECOND
        // This replaces 0.5001 → 0.0001 in BOTH the original shader lines
        // AND any ManualFocusAssist code blocks that were just inserted.
        if (enableLowerMinFocus)
        {
            content = ApplyLowerMinFocusReplacements(content);
        }

        // Step 3: Rename shader
        content = RenameShader(content, enableLowerMinFocus, enableManualFocusAssist, enableGhostFX, enableChromaticAberration, enableFilmGrain, enableDepthFog, enableTiltShift, enableFisheye, enableColorGrading);

        // Step 4: Add header comment
        List<string> enabledMods = new List<string>();
        if (enableLowerMinFocus) enabledMods.Add("LowerMinFocus");
        if (enableManualFocusAssist) enabledMods.Add("ManualFocusAssist");
        if (enableGhostFX) enabledMods.Add("GhostFX");
        if (enableChromaticAberration) enabledMods.Add("ChromaticAberration");
        if (enableFilmGrain) enabledMods.Add("FilmGrain");
        if (enableDepthFog) enabledMods.Add("DepthFog");
        if (enableTiltShift) enabledMods.Add("TiltShift");
        if (enableFisheye) enabledMods.Add("Fisheye");
        if (enableColorGrading) enabledMods.Add("ColorGrading");
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
        content = ApplyFisheyeInsertions(content);
        content = ApplyColorGradingInsertions(content);
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

        // Validate Fisheye anchors
        var fisheyeAnchorsV = GetFisheyeAnchors();
        List<string> missingFisheyeV = new List<string>();
        foreach (var anchor in fisheyeAnchorsV)
        {
            if (!originalContent.Contains(anchor.SearchString))
                missingFisheyeV.Add($"  - {anchor.Description}: \"{anchor.SearchString}\"");
        }
        if (missingFisheyeV.Count > 0)
        {
            string msg = $"Fisheye validation FAILED -- missing anchors:\n{string.Join("\n", missingFisheyeV)}\n\n" +
                "VRCLens may have been updated. See maintenance steps in the patcher source code.";
            Debug.LogError($"{LOG_PREFIX} {msg}");
            EditorUtility.DisplayDialog("Verify Patcher -- FAILED", msg, "OK");
            return;
        }
        messages.Add($"All {fisheyeAnchorsV.Count} Fisheye anchors found.");

        // Validate ColorGrading anchors
        var cgAnchorsV = GetColorGradingAnchors();
        List<string> missingCGV = new List<string>();
        foreach (var anchor in cgAnchorsV)
        {
            if (!originalContent.Contains(anchor.SearchString))
                missingCGV.Add($"  - {anchor.Description}: \"{anchor.SearchString}\"");
        }
        if (missingCGV.Count > 0)
        {
            string msg = $"ColorGrading validation FAILED -- missing anchors:\n{string.Join("\n", missingCGV)}\n\n" +
                "VRCLens may have been updated. See maintenance steps in the patcher source code.";
            Debug.LogError($"{LOG_PREFIX} {msg}");
            EditorUtility.DisplayDialog("Verify Patcher -- FAILED", msg, "OK");
            return;
        }
        messages.Add($"All {cgAnchorsV.Count} ColorGrading anchors found.");

        // Patch the original (all mods) and compare to reference
        string patched = ApplyManualFocusAssistInsertions(originalContent);
        patched = ApplyGhostFXInsertions(patched);
        patched = ApplyChromaticAberrationInsertions(patched);
        patched = ApplyFilmGrainInsertions(patched);
        patched = ApplyDepthFogInsertions(patched);
        patched = ApplyTiltShiftInsertions(patched);
        patched = ApplyFisheyeInsertions(patched);
        patched = ApplyColorGradingInsertions(patched);
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
        Shader shader = PatchShader(true, true, true, true, true, true, true, true);
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

    // ═══════════════════════════════════════════════════════════════════
    // Fisheye Lens insertion logic
    // ═══════════════════════════════════════════════════════════════════

    private static List<AnchorInfo> GetFisheyeAnchors()
    {
        return new List<AnchorInfo>
        {
            new AnchorInfo(ANCHOR_PROPERTIES, "Properties: _DoFStrength line"),
            new AnchorInfo(ANCHOR_PASS6_UNIFORMS, "Pass 2: _FocusDistance uniform"),
            new AnchorInfo(ANCHOR_FISHEYE_UV, "Pass 2: sbsUV1 line (before rawColor sampling)"),
            new AnchorInfo(ANCHOR_RAWCOLOR, "Pass 2: rawColor sampling line"),
            new AnchorInfo(ANCHOR_DITHERING, "Pass 2: dithering (for fisheye mask)"),
        };
    }

    private static string ApplyFisheyeInsertions(string content)
    {
        var lines = new List<string>(content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
        int insertions = 0;

        // Process from bottom to top so line indices don't shift

        // Fisheye mask application — after dithering, blacks out out-of-bounds pixels
        insertions += InsertAfterLine(lines, ANCHOR_DITHERING, BLOCK_FISHEYE_PASS2_MASK, "Fisheye mask application");

        // Fisheye anti-streak supersample — after rawColor sampling, averages 5 taps over the
        // local sample footprint to remove radial streaks at high distortion.
        insertions += InsertAfterLine(lines, ANCHOR_RAWCOLOR, BLOCK_FISHEYE_SUPERSAMPLE, "Fisheye anti-streak supersample");

        // Fisheye UV distortion — after sbsUV1 line, before rawColor sampling
        insertions += InsertAfterLine(lines, ANCHOR_FISHEYE_UV, BLOCK_FISHEYE_PASS2, "Fisheye pass 2 UV distortion");

        // Fisheye uniforms in Pass 2 — after second _FocusDistance uniform (occurrence 2)
        insertions += InsertAfterLine(lines, ANCHOR_PASS6_UNIFORMS, BLOCK_FISHEYE_UNIFORMS, "Fisheye uniforms", false, 2);

        // Fisheye properties — after _DoFStrength line
        insertions += InsertAfterLine(lines, ANCHOR_PROPERTIES, BLOCK_FISHEYE_PROPERTIES, "Fisheye properties");

        Debug.Log($"{LOG_PREFIX} Applied {insertions} Fisheye insertion sites");

        return string.Join("\n", lines);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Color Grading insertion logic
    // ═══════════════════════════════════════════════════════════════════

    private static readonly string BLOCK_CG_PROPERTIES = @"
		// VRCLens_Custom BEGIN - Color Grading Properties
		[Header(Color Grading)]
		[Toggle] _CGEnable (""Enable Color Grading"", float) = 0
		_CGSaturation (""Saturation"", Range(0.0, 1.0)) = 0.5
		_CGVibrance (""Vibrance"", Range(0.0, 1.0)) = 0.0
		_CGShadowTemp (""Shadow Temperature"", Range(0.0, 1.0)) = 0.5
		_CGShadowR (""Shadow Red"", Range(0.0, 1.0)) = 0.5
		_CGShadowG (""Shadow Green"", Range(0.0, 1.0)) = 0.5
		_CGShadowB (""Shadow Blue"", Range(0.0, 1.0)) = 0.5
		_CGMidtoneTemp (""Midtone Temperature"", Range(0.0, 1.0)) = 0.5
		_CGMidtoneR (""Midtone Red"", Range(0.0, 1.0)) = 0.5
		_CGMidtoneG (""Midtone Green"", Range(0.0, 1.0)) = 0.5
		_CGMidtoneB (""Midtone Blue"", Range(0.0, 1.0)) = 0.5
		_CGHighlightTemp (""Highlight Temperature"", Range(0.0, 1.0)) = 0.5
		_CGHighlightR (""Highlight Red"", Range(0.0, 1.0)) = 0.5
		_CGHighlightG (""Highlight Green"", Range(0.0, 1.0)) = 0.5
		_CGHighlightB (""Highlight Blue"", Range(0.0, 1.0)) = 0.5
		_CGContrast (""Contrast"", Range(0.0, 1.0)) = 0.5
		// VRCLens_Custom END";

    private static readonly string BLOCK_CG_UNIFORMS = @"
			// VRCLens_Custom BEGIN - Color Grading Uniforms
			uniform float _CGEnable, _CGSaturation, _CGVibrance, _CGContrast;
			uniform float _CGShadowTemp, _CGShadowR, _CGShadowG, _CGShadowB;
			uniform float _CGMidtoneTemp, _CGMidtoneR, _CGMidtoneG, _CGMidtoneB;
			uniform float _CGHighlightTemp, _CGHighlightR, _CGHighlightG, _CGHighlightB;
			// VRCLens_Custom END";

    private static readonly string BLOCK_CG_PASS2 = @"
				// VRCLens_Custom BEGIN - Color Grading
				if(_CGEnable > 0.5)
				{
					// Zone weights from luminance
					float cgLuma = dot(col.rgb, float3(0.299, 0.587, 0.114));
					float cgShadowW = 1.0 - smoothstep(0.0, 0.333, cgLuma);
					float cgHighlightW = smoothstep(0.667, 1.0, cgLuma);
					float cgMidtoneW = 1.0 - cgShadowW - cgHighlightW;

					// Per-zone temperature (reusing VRCLens colorTemp function)
					col.rgb *= lerp(float3(1,1,1), colorTemp(1.0 - 2.0 * _CGShadowTemp), cgShadowW);
					col.rgb *= lerp(float3(1,1,1), colorTemp(1.0 - 2.0 * _CGMidtoneTemp), cgMidtoneW);
					col.rgb *= lerp(float3(1,1,1), colorTemp(1.0 - 2.0 * _CGHighlightTemp), cgHighlightW);

					// Per-zone RGB shift (additive)
					col.rgb += (float3(_CGShadowR, _CGShadowG, _CGShadowB) - 0.5) * cgShadowW;
					col.rgb += (float3(_CGMidtoneR, _CGMidtoneG, _CGMidtoneB) - 0.5) * cgMidtoneW;
					col.rgb += (float3(_CGHighlightR, _CGHighlightG, _CGHighlightB) - 0.5) * cgHighlightW;

					// Global contrast (0-1 property -> 0.75x-1.25x around mid-grey 0.5)
					col.rgb = lerp(float3(0.5, 0.5, 0.5), col.rgb, _CGContrast * 0.5 + 0.75);

					// Global vibrance (saturation-adaptive boost, Photoshop-like)
					float cgVibLuma = dot(col.rgb, float3(0.299, 0.587, 0.114));
					float cgMaxC = max(col.r, max(col.g, col.b));
					float cgMinC = min(col.r, min(col.g, col.b));
					float cgChroma = cgMaxC - cgMinC;
					float cgVibWeight = saturate(1.0 - cgChroma);
					// Skin-tone protection: warm hues (R>G>B) get reduced boost
					float cgSkinFactor = smoothstep(0.1, 0.4, (col.r - col.b) / max(cgMaxC, 0.001))
									   * smoothstep(0.0, 0.15, col.g - col.b);
					cgVibWeight *= (1.0 - 0.5 * cgSkinFactor);
					col.rgb = lerp(float3(cgVibLuma, cgVibLuma, cgVibLuma), col.rgb,
								   1.0 + _CGVibrance * cgVibWeight);

					// Global saturation (0-1 property -> 0x-2x multiplier)
					float cgSatLuma = dot(col.rgb, float3(0.299, 0.587, 0.114));
					col.rgb = lerp(float3(cgSatLuma, cgSatLuma, cgSatLuma), col.rgb, _CGSaturation * 2.0);

					col.rgb = max(0.0, col.rgb);
				}
				// VRCLens_Custom END";

    private static List<AnchorInfo> GetColorGradingAnchors()
    {
        return new List<AnchorInfo>
        {
            new AnchorInfo(ANCHOR_PROPERTIES, "Properties: _DoFStrength line"),
            new AnchorInfo(ANCHOR_PASS6_UNIFORMS, "Pass 2: _FocusDistance uniform"),
            new AnchorInfo(ANCHOR_TONEMAPPING, "Pass 2: tonemap line"),
        };
    }

    private static string ApplyColorGradingInsertions(string content)
    {
        var lines = new List<string>(content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
        int insertions = 0;

        // Process from bottom to top so line indices don't shift

        // Color Grading application — after tone mapping (before dithering)
        insertions += InsertAfterLine(lines, ANCHOR_TONEMAPPING, BLOCK_CG_PASS2, "ColorGrading pass 2 application");

        // Color Grading uniforms in Pass 2 — after second _FocusDistance uniform
        insertions += InsertAfterLine(lines, ANCHOR_PASS6_UNIFORMS, BLOCK_CG_UNIFORMS, "ColorGrading uniforms", false, 2);

        // Color Grading properties — after _DoFStrength line
        insertions += InsertAfterLine(lines, ANCHOR_PROPERTIES, BLOCK_CG_PROPERTIES, "ColorGrading properties");

        Debug.Log($"{LOG_PREFIX} Applied {insertions} ColorGrading insertion sites");

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
    private static string RenameShader(string content, bool enableLowerMinFocus, bool enableManualFocusAssist, bool enableGhostFX = false, bool enableChromaticAberration = false, bool enableFilmGrain = false, bool enableDepthFog = false, bool enableTiltShift = false, bool enableFisheye = false, bool enableColorGrading = false)
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
        if (enableFisheye) parts.Add("Fisheye");
        if (enableColorGrading) parts.Add("ColorGrading");
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
