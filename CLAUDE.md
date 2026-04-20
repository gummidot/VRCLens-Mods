@Packages/xyz.sentfromspace.agentic-tools/Docs/INDEX.md

# Project Instructions

## Plan Mode

When operating in **plan mode**, always save the plan as a Markdown file in `.github/prompts/`.

### File organization

Each plan gets its own folder under `.github/prompts/`:

`YYYY-MM-DD-<short-topic>/`

- Use the plan's start date for `YYYY-MM-DD`.
- `<short-topic>` should be a concise kebab-case slug (e.g., `ghost-fx`, `blur-enhancement`).

The main plan file lives inside the folder:

`YYYY-MM-DD-<short-topic>/YYYY-MM-DD-plan-<short-topic>.prompt.md`

Related documents (research logs, reference material) go in the same folder.

### File format

Follow this template (see existing files in `.github/prompts/` for real examples):

```markdown
# Plan: <Title>

**Date:** YYYY-MM-DD
**Status:** Not started
**Complexity:** Low | Medium | High

<One-paragraph summary: what the change does and why.>

**Target file(s):** <paths>

---

## Background

<Context, motivation, relevant technical details.>

## Decisions

<Key design choices and their rationale.>

## Steps

1. <Actionable step with file paths and symbol references>
2. …

## Verification

<How to confirm correctness: commands, tests, manual checks.>
```

### Rules

- **Always** write the plan file — do not only present the plan in chat.
- Keep the plan self-contained: someone reading only the file should be able to implement it.
- Reference concrete file paths, symbol names, and line numbers where relevant.
- Update `**Status:**` as work progresses (`Not started` → `In progress` → `Implemented`).
- **Always** update the plan file when the plan changes — the file is the source of truth, not the chat history.
- **Before running pre-commit reviews**, update the plan file to reflect the current implementation. Stale plan files cause reviewers to flag intentional changes as deviations.

---

## VRCLens Custom Shader Mods

This project adds optional shader mods to VRCLens without modifying the original VRCLens assets. Mods are patched into the shader at avatar build time.

### Mod Catalog

See `README.md` for the full list of available mods with descriptions and usage.

### Discovery Workflow

When working on a new shader mod or understanding existing ones:

1. **Read existing plan files** in `.github/prompts/` — these are the source of truth for each mod's design, shader anchors, and implementation details. Start here.
2. **Read the patcher** at `Assets/VRCLens_Custom/Editor/VRCLensShaderPatcher.cs` — this contains all anchor constants, code blocks, and injection methods. Each mod follows the same `Apply<Feature>Insertions()` pattern.
3. **Read the build pipeline** — `AvatarBuildHook.cs` → `VRCLensShaderModifier.cs` → `VRCLensShaderPatcher.cs`. Flags flow from `VRCLensModifier` component through this chain.
4. **Read the original shader** at `Assets/Hirabiki/VRCLens/Resource/DepthOfField.shader` — understand the pass structure and identify anchor strings for new injection sites.
5. **Reference existing prefabs** under `Assets/VRCLens_Custom/` (e.g., `ManualFocusAssist/`) for animation clip and VRCFury prefab patterns.

### Architecture Overview

| Component | File | Role |
|-----------|------|------|
| Toggle flag | `VRCLensModifier.cs` | `public bool enable<Feature>` field on the MonoBehaviour |
| Inspector UI | `VRCLensModifierEditor.cs` | Checkbox in the component inspector |
| Build hook | `AvatarBuildHook.cs` | OR-merges flags across modifiers, passes to shader modifier |
| Shader modifier | `VRCLensShaderModifier.cs` | Copies material, calls patcher, swaps shader on ScreenOverride |
| Shader patcher | `VRCLensShaderPatcher.cs` | Text replacement + anchor-based code injection into DepthOfField.shader |
| Animation clips | `Assets/VRCLens_Custom/<Feature>/` | Material property animations for VRCFury toggles/radials |
| VRCFury prefab | `Assets/VRCLens_Custom/<Feature>.prefab` or `<Feature>/<Feature>.prefab` | Toggles + radial puppets under `VRCLens/Custom/<Feature>` menu path |

**Critical: Every mod prefab that adds custom shader properties MUST include a `VRCLensModifier` component with its `enable<Feature>` flag set.** Without it, `AvatarBuildHook` won't pass the flag to the shader patcher, so the patched shader won't contain the new properties. Then d4rk Avatar Optimizer strips the animation bindings because the material properties don't exist on the shader. Symptom: all radials do nothing in-game (built clips replaced with `DummyClip_0`).

### Shader Patching Patterns

- **Text replacement** -- Direct string substitution (e.g., changing a threshold value). Used for simple mods like LowerMinFocus.
- **Anchor-based insertion** -- Find a stable line in the shader, inject code after it. Used for feature additions like ManualFocusAssist. All injections wrapped in `// VRCLens_Custom BEGIN/END` markers for idempotency.
- **Anchor sites** are numbered (Site 1a, 1b, 2, 3, 4a, 4b, 5a, 5b, etc.) -- see patcher source for the full list. Multiple mods can share anchor sites (injections stack).

### Preventing Mod Conflicts (Shared Anchor Insertion Order)

All mods are independently addable -- every anchor exists in the original unpatched shader. No mod may depend on an anchor introduced by another mod.

**Shared anchor reversal:** When multiple mods insert after the same anchor line, `List.Insert()` causes code blocks to appear in **reverse apply order** in the output. For shared anchors like `ANCHOR_PROPERTIES` (used by 7 mods) and `ANCHOR_PASS6_UNIFORMS` (used by 6 mods), this reversal is cosmetic only -- properties/uniforms still work regardless of order.

**Functional ordering matters for Pass 2 code blocks.** Shader operations that read or write `col.rgb` execute in insertion order, so the reversal changes the processing pipeline. Rules:

1. **Never share an anchor for two Pass 2 code blocks that must execute in a specific order.** If mod A's output must be masked/modified by mod B's code, they need different anchors so insertion order is deterministic.
2. **Pick the latest possible anchor for "final" operations** (e.g., masking, clamping). The dithering line (`ANCHOR_DITHERING`) runs after tone mapping, film grain, and filter post-processing, making it safe for operations that must come last.
3. **When adding a new mod that injects into Pass 2**, check which other mods share the same anchor. If the new code depends on running before or after existing injections, use a different anchor.

### VRCFury Menu Convention

All custom features use `VRCLens/Custom/<Feature>` as the menu path. For features with multiple controls, use a submenu (e.g., `VRCLens/Custom/GhostFX` containing toggles + radial puppets).

### VRCFury Parameter Convention — Unsynced (Local-Only)

All VRCFury toggles and radials in custom prefabs **must use unsynced parameters**. Camera settings are local-only — no need to sync to other players, and we avoid consuming the 256-bit synced parameter budget.

**Pattern (see ManualFocusAssist for reference):**

1. **Global parameter names:** Each toggle uses `useGlobalParam = true` with a namespaced name: `VRCL_Custom/<Feature><Control>` (e.g., `VRCL_Custom/GhostFXSplit`, `VRCL_Custom/ManualFocusAssistStrength`). This is separate from the menu path (`VRCLens/Custom/<Feature>/...`).

2. **LocalParams asset:** Create `LocalParams_<Feature>.asset` (VRCExpressionParameters ScriptableObject) in the feature folder. Every parameter has `networkSynced: 0` and `saved: 1`. **`defaultValue` MUST be 0** for VRCFury Toggle slider parameters. The parameter represents the slider position (blend weight 0-1), not the shader property value. At slider=0, Write Defaults ON causes the shader property to revert to its own default from the `Properties` block. At slider=1, the animation clip value is applied. Setting defaultValue=0 means the slider starts at minimum (effect off / backward-compatible state) on first avatar load.

3. **FullController:** Add one VRCFury FullController component to the prefab with:
   - `prms` array referencing the LocalParams asset
   - `globalParams: ['*']` — makes all params in the asset global and unsynced
   - No controllers or menus needed — only params

### Pass Structure Reference

| Pass | Purpose | Key textures |
|------|---------|-------------|
| Pass 0 | Camera feed capture, CoC calculation | `_RenderTex`, `_DepthTex` → outputs color.rgb + CoC in .a |
| Pass 1 | Bokeh blur (228-348 sample disk kernel) | GrabPass `_HirabikiVRCLensPassTex_One` |
| Pass 2 | Final composition: tone mapping, exposure, overlays | GrabPass `_HirabikiVRCLensPassTexture` |

**Critical rule:** CoC serves dual purposes (blur amount + focus zone threshold). Modifications must preserve the original CoC in Pass 0's alpha for focus peaking, then apply changes only in Pass 1 (blur sampling) or Pass 2 (post-processing).

**Pass 0 CoC leak:** `getBlurSize()` runs unconditionally in Pass 0 regardless of `_EnableDoF`, so `col.a` always contains a focus-dependent CoC. Custom mods that write to `col.a` (e.g., tilt-shift) must check `_EnableDoF` — when DoF is off, assign the custom CoC directly (`col.a = tsCoC`) instead of `max(col.a, tsCoC)`, otherwise the focus pointer position affects the result even with DoF disabled.

**`_DepthTex` is always available:** The scene depth texture is populated by the VRCLens camera regardless of whether DoF is enabled. Mods can safely sample `_DepthTex` in Pass 2 for depth-dependent effects (e.g., axial CA depth fade) without requiring DoF to be active.
