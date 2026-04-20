# Plan: Fisheye Lens v7 - UV Blending with Oval Mask

**Date:** 2026-04-20
**Status:** Implemented
**Complexity:** Medium

Replace the v6 auto-tight-mask fisheye with a UV-blending approach. A large oval mask extends past all 4 screen edges. Barrel distortion is applied inside the oval; at the boundary, UVs smoothly transition from distorted to undistorted, eliminating all OOB artifacts without shrinking the visible area. Outside the oval: either black (default) or undistorted corners (Show Corners toggle). A Zoom parameter lets users scale in until the mask boundary is pushed outside the screen, filling 100% with distorted content.

**Target file(s):**
- `Assets/VRCLens_Custom/Editor/VRCLensShaderPatcher.cs` - rewrite all BLOCK_FISHEYE_* constants
- `Assets/VRCLens_Custom/FisheyeLens/` - new/updated anim clips, updated LocalParams
- `Assets/VRCLens_Custom/FisheyeLens.prefab` - restructure VRCFury toggles

---

## Background

### What went wrong in v1-v6

| Version | Approach | Problem |
|---------|----------|---------|
| v1 | Shape+distortion coupled | Shape invisible at low strength |
| v2 | Shape as mask opacity | Lost oval geometry |
| v3 | Screen-space mask + narrow OOB | Rectangular OOB mask visible |
| v4 | Wider OOB (0.15) | Still rectangular at corners |
| v5 | Radial OOB | Still had geometric conflicts |
| reimpl | Inward-pull | Produced tunnel/streaks, not barrel distortion |
| v6 | Auto-tight safe mask | Mask shrinks with strength (less picture at higher distortion) |

**Core v6 problem:** The auto-tight mask computes a maximum safe radius via Newton's method and clips to that radius. As strength increases, the safe radius shrinks, so the visible picture area decreases. User wants the oval to remain large and extend past the screen regardless of strength.

**OOB artifact explained:** Outward-push barrel distortion pushes source UVs outside `[0,1]`. The GPU clamps them to the texture edge, creating stretched/smeared edge pixels. The clamping boundary is axis-aligned (rectangular) while the fisheye mask is oval/circular, so no masking approach in v1-v5 could cleanly hide the rectangular clamping geometry.

### New approach: UV blending

Instead of masking OOB pixels after distortion, blend UVs at the mask boundary:

```
sbsUV0 = lerp(undistortedUV, clamp(distortedUV), fisheyeMask)
```

- Inside the oval (mask=1): UV is fully distorted (barrel distortion)
- At the boundary (mask fades 1 to 0): UV smoothly transitions from distorted to undistorted
- Outside the oval (mask=0): UV is the original undistorted position

This eliminates OOB artifacts entirely. Where clamping might occur (high distortion at the boundary), the blend is already pulling toward the valid undistorted UV, so clamped pixels are never visible at full weight.

### Reference: Flex FishEye Lens

Flex FishEye uses 2 cameras for actual wider FOV capture (fundamentally different). Our post-process barrel distortion cannot add new scene content, but simulates the distortion mapping and visual aesthetic. We replicate the core behavior: oval fills screen, small corner clips, zoom to fill 100%.

| Feature | Flex FishEye | Our v7 |
|---------|-------------|--------|
| Oval fills screen | Yes | Yes (0.65 radius) |
| Zoom to fill 100% | Yes | Yes |
| Circular mode | No | No (future enhancement) |
| Per-axis distortion | Yes | No |
| Show undistorted corners | No | Yes |
| Edge softness | No | Yes |

---

## Decisions

1. **UV blending over OOB masking.** Eliminates the entire class of OOB artifacts from v1-v6.

2. **Fixed oval radius (0.65 in UV space).** A circle of radius 0.65 in raw UV space (no aspect correction) appears as a horizontal oval on 16:9. Screen midpoints are 0.15 inside the mask; only small corner crescents are clipped. Top/bottom/left/right edges all extend well past the screen.

3. **Zoom affects both mask and distortion.** Scaling UV toward center before computing mask and distortion naturally pushes the mask outside the screen AND crops the image.

4. **Outward-push distortion (confirmed correct).** `source = screen * (1 + k*r^2)` produces barrel distortion. Inward-pull produces tunnel/streak effect (confirmed in reimpl attempt).

5. **Show Corners toggle.** Default is black outside the oval. Toggle enables showing undistorted corners (Photoshop spherize style).

6. **Shape (oval-to-circle) deferred.** Future enhancement. Would add `_FisheyeShape` to interpolate mask aspect correction from 1.0 (oval) to `fishAspect` (circle), plus adjust `maskRadius` from 0.65 to 0.5.

7. **Strength range 0-5.** Higher values create more extreme distortion but the visible area does not shrink (unlike v6).

---

## Shader Properties

| Property | Type | Range | Default | Purpose |
|----------|------|-------|---------|---------|
| `_FisheyeStrength` | Float | 0.0-5.0 | 0.0 | Barrel distortion amount |
| `_FisheyeZoom` | Float | 0.0-1.0 | 0.0 | Pre-distortion zoom to fill screen |
| `_FisheyeEdgeSoftness` | Float | 0.0-0.2 | 0.05 | Transition width at mask boundary |
| `_FisheyeShowCorners` | Float | 0.0-1.0 | 0.0 | 0=black corners, 1=show undistorted |

### Changes from v6
- Removed: `_FisheyeShape` (deferred)
- Added: `_FisheyeZoom`, `_FisheyeShowCorners`

---

## HLSL Implementation

### Pass 2 UV Distortion Block

```hlsl
// VRCLens_Custom BEGIN - Fisheye Lens
float fisheyeMask = 1.0;
if(_FisheyeStrength > 0.001) {
    float2 origUV = sbsUV0;
    float fishAspect = _ScreenParams.x / _ScreenParams.y;

    // Pre-distortion zoom (scales UV toward center)
    float zoomScale = 1.0 + _FisheyeZoom * 0.5;
    float2 center = (origUV - 0.5) / zoomScale;

    // Oval mask (raw UV space, no aspect correction -> oval on 16:9 screen)
    // Radius 0.65: extends past all 4 screen edges, clips small corner crescents
    float maskR = length(center);
    float maskSoft = max(0.001, _FisheyeEdgeSoftness);
    fisheyeMask = 1.0 - smoothstep(0.65 - maskSoft, 0.65, maskR);

    // Barrel distortion (full aspect correction for uniform radial warp)
    float2 ac = center;
    ac.x *= fishAspect;
    float r = length(ac);
    float scale = 1.0 + _FisheyeStrength * r * r;
    ac *= scale;
    ac.x /= fishAspect;
    float2 distortedUV = clamp(0.5 + ac, 0.001, 0.999);

    // UV blend: distortion fades smoothly at mask boundary
    sbsUV0 = lerp(0.5 + center, distortedUV, fisheyeMask);
}
// VRCLens_Custom END
```

### Pass 2 Mask Application Block

```hlsl
// VRCLens_Custom BEGIN - Fisheye Lens Mask
col.rgb *= lerp(fisheyeMask, 1.0, _FisheyeShowCorners);
// VRCLens_Custom END
```

### Key behaviors

**Zoom interaction:**
- zoom=0: maskR at screen corner = 0.707, outside 0.65 mask. Small black corners.
- zoom~0.18: corner maskR = 0.707/1.09 = 0.649. Mask just covers entire screen.
- zoom=1: corner maskR = 0.707/1.5 = 0.471. Well inside mask. Full distorted content fills screen.

**ShowCorners interaction:**
- OFF (default): `col.rgb *= fisheyeMask`. Corners fade to black.
- ON: `col.rgb *= 1.0`. Corners show undistorted image. UV blend handles the smooth distortion transition.

---

## Interaction with Other Mods

Same interactions as documented in v4 plan. UV distortion injects before `depthOfField()` sampling (line 789), mask application runs after dithering. One difference: the UV blend means the transition zone samples a mix of distorted and undistorted positions. This is a small zone and the transition is smooth, so downstream mods (Ghost FX, CA, Depth Fog, Film Grain) operate normally on the blended content.

---

## VRCFury Menu

Menu path: `VRCLens/Custom/Fisheye Lens/`

| Control | Type | Parameter | Shader Default | Anim Value |
|---------|------|-----------|---------------|-----------|
| Strength | Radial | `VRCL_Custom/FisheyeStrength` | 0.0 | 5.0 |
| Zoom | Radial | `VRCL_Custom/FisheyeZoom` | 0.0 | 1.0 |
| Edge Softness | Radial | `VRCL_Custom/FisheyeEdgeSoftness` | 0.05 | 0.2 |
| Show Corners | Toggle | `VRCL_Custom/FisheyeShowCorners` | 0.0 | 1.0 |

All parameters unsynced (local-only) and saved.

### Slider math

| Parameter | Shader Default | Anim Keyframe | LocalParams Default | Slider 0% | Slider 100% |
|-----------|---------------|---------------|---------------------|-----------|-------------|
| `_FisheyeStrength` | 0.0 | 5.0 | 0.0 (Float) | 0.0 (off) | 5.0 (max) |
| `_FisheyeZoom` | 0.0 | 1.0 | 0.0 (Float) | 0.0 (no zoom) | 1.0 (fills screen) |
| `_FisheyeEdgeSoftness` | 0.05 | 0.2 | 0.0 (Float) | 0.05 (moderate) | 0.2 (soft) |
| `_FisheyeShowCorners` | 0.0 | 1.0 | 0.0 (Bool) | OFF (black) | ON (undistorted) |

---

## Steps

### Phase 1: Shader Patcher Update

1. **Rewrite `BLOCK_FISHEYE_PROPERTIES`** - Remove `_FisheyeShape`, add `_FisheyeZoom` and `_FisheyeShowCorners`. Adjust `_FisheyeEdgeSoftness` range to (0, 0.2).

2. **Rewrite `BLOCK_FISHEYE_UNIFORMS`** - Remove `_FisheyeShape`, add `uniform float _FisheyeZoom;` and `uniform float _FisheyeShowCorners;`.

3. **Rewrite `BLOCK_FISHEYE_PASS2`** - Replace v6 Newton-method approach with UV blending approach.

4. **Rewrite `BLOCK_FISHEYE_PASS2_MASK`** - Change from `col.rgb *= fisheyeMask;` to `col.rgb *= lerp(fisheyeMask, 1.0, _FisheyeShowCorners);`.

### Phase 2: Animation Clips & LocalParams

5. **Keep `FisheyeLens_Strength.anim`** unchanged (value 5.0).

6. **Create `FisheyeLens_Zoom.anim`** - `material._FisheyeZoom = 1.0` on `CamScreen/ScreenOverride`.

7. **Update `FisheyeLens_EdgeSoftness.anim`** - keyframe value 0.3 to 0.2.

8. **Delete `FisheyeLens_Shape.anim`, create `FisheyeLens_ShowCorners.anim`** - `material._FisheyeShowCorners = 1.0` on `CamScreen/ScreenOverride`.

9. **Update `LocalParams_FisheyeLens.asset`** - Remove `VRCL_Custom/FisheyeShape`. Add `VRCL_Custom/FisheyeZoom` (Float, default 0, unsynced, saved) and `VRCL_Custom/FisheyeShowCorners` (Bool, default 0, unsynced, saved).

### Phase 3: VRCFury Prefab

10. **Restructure `FisheyeLens.prefab`:**
    - Toggle 1 (radial): `VRCLens/Custom/Fisheye Lens/Strength`, globalParam `VRCL_Custom/FisheyeStrength`, clip `FisheyeLens_Strength.anim`
    - Toggle 2 (radial): `VRCLens/Custom/Fisheye Lens/Zoom`, globalParam `VRCL_Custom/FisheyeZoom`, clip `FisheyeLens_Zoom.anim`
    - Toggle 3 (radial): `VRCLens/Custom/Fisheye Lens/Edge Softness`, globalParam `VRCL_Custom/FisheyeEdgeSoftness`, clip `FisheyeLens_EdgeSoftness.anim`
    - Toggle 4 (binary toggle): `VRCLens/Custom/Fisheye Lens/Show Corners`, globalParam `VRCL_Custom/FisheyeShowCorners`, clip `FisheyeLens_ShowCorners.anim`
    - FullController: `prms` referencing updated `LocalParams_FisheyeLens.asset`, `globalParams: ['*']`
    - VRCLensModifier: `enableFisheye: 1` (unchanged)

### Phase 4: Verification

11. Enter Play Mode, confirm shader compilation succeeds.

12. Verify all 4 animation bindings resolve (no DummyClip replacement).

13. Visual checks:
    - **Strength=30%, Zoom=0, ShowCorners=OFF:** Barrel distortion, small black crescent corners, oval extends past all 4 screen edges.
    - **Strength=60%, Zoom=0:** Stronger distortion, same oval coverage.
    - **Zoom=50%:** Corners shrink. **Zoom=100%:** No visible mask, full-screen distortion.
    - **ShowCorners=ON:** Corners show undistorted image instead of black.
    - **Strength=0:** No effect regardless of other parameters.
    - **EdgeSoftness sweep:** Visible softness change at boundary.

---

## Risk Assessment

**Low-medium risk.** UV blending is novel compared to v1-v6 but mathematically sound:
- `undistortedUV = 0.5 + center` is always in bounds (center magnitude <= 0.5 at zoom >= 0)
- `distortedUV` is explicitly clamped
- `lerp(inBounds, inBounds, [0,1])` is always in bounds
- No Newton iterations, no complex safe-radius computation

**Potential concern:** At high strength + low zoom, pixels near the mask boundary where fisheyeMask is close to 1 could have heavily clamped distortedUVs. The UV blend attenuates this since those pixels are where the mask is fading. If visible, increasing EdgeSoftness or Zoom eliminates it.

**Tuning note:** Mask radius 0.65 was chosen so corners are small (~8% of screen diagonal outside mask). If testing shows too much or too little clipping, adjusting to 0.60-0.70 is a one-constant change.

---

## Future Enhancements

1. **Circle mode (Shape parameter):** Add `_FisheyeShape` to interpolate mask aspect correction from 1.0 (oval) to `fishAspect` (circle). Also needs `maskRadius = lerp(0.65, 0.5, _FisheyeShape)` so circle inscribes height. ~3 extra shader lines + 1 radial.

2. **Mask radius control:** Expose the 0.65 constant as a parameter for users who want more/less corner clipping.

3. **Pincushion mode:** Negative strength or separate toggle for inward distortion.

4. **Per-axis distortion:** Independent X/Y distortion amounts (Flex FishEye has this).
