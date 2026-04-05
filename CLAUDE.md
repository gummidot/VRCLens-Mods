# Project Instructions

## Plan Mode

When operating in **plan mode**, always save the plan as a Markdown file in `.github/prompts/`.

### File naming

`YYYY-MM-DD-plan-<short-topic>.prompt.md`

- Use the current date for `YYYY-MM-DD`.
- `<short-topic>` should be a concise kebab-case slug describing the plan (e.g., `drone-roll-modifier`, `blur-enhancement`).

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

---

## Working in Unity

Adapted from [VRChat Agentic Tools](https://github.com/sentfromspacevr/vrchat-agentic-tools) — MCP bridge, agent instructions, and post-build verification for VRChat avatar creation.

When adding guidance to this file, prefer discovery-first approaches (runtime queries, dynamic lookups, search patterns) over hardcoded values, paths, or version-specific details that can go stale.

This is a Unity Project. We work inside the Assets folder (working directory).
Focused on Avatar Creation for VRChat.

**Primary approach**: Use the MCP bridge (`execute_csharp`) for all scene interaction — reading hierarchy, adding components, configuring settings, creating assets.
**File reads**: Freely read any file to explore assets, code, configs, YAML structure.
**File writes**: Fallback for creating/editing `.anim`, `.controller`, `.asset`, `.mat` files when the C# API is awkward or for bulk operations. When editing YAML directly, read an existing file of the same type first to understand the structure.
Do not create `.meta` files — Unity generates these automatically.

**Subagent limitation**: Subagents (Explore, Plan, etc.) do NOT have access to MCP tools like `execute_csharp`. Never delegate scene exploration or Unity Editor interaction to subagents — they will try to parse `.unity` files directly, which is unreliable. Always run MCP calls in the main conversation.

**Plan mode**: MCP tools (`execute_csharp`) are available during plan mode since the main agent retains all tools. Use MCP for scene exploration during planning — don't rely on parsing `.unity` YAML files.

The VRChat SDK are located at:
- Packages/com.vrchat.base
- Packages/com.vrchat.avatars

**VRChat SDK docs:** Use https://vrc-beta-docs.netlify.app/ as the primary reference for VRChat SDK behavior, components, and features. Key pages for avatar work:
- [Avatars overview](https://vrc-beta-docs.netlify.app/avatars) — AV3 concepts, playable layers, Write Defaults, visemes, eye simulation
- [Avatar components](https://vrc-beta-docs.netlify.app/avatars/avatar-components) — PhysBones, Contacts, Constraints, Raycast, Head Chop
- [Expression Menu & Controls](https://vrc-beta-docs.netlify.app/avatars/expression-menu-and-controls) — menus, parameters, control types
- [Animator parameters](https://vrc-beta-docs.netlify.app/avatars/animator-parameters) — built-in parameters and sync types
- [Performance ranking](https://vrc-beta-docs.netlify.app/avatars/avatar-performance-ranking-system) — performance limits and rankings
- [PhysBones](https://vrc-beta-docs.netlify.app/common-components/physbones), [Contacts](https://vrc-beta-docs.netlify.app/common-components/contacts), [Constraints](https://vrc-beta-docs.netlify.app/common-components/constraints) — detailed component docs

Fetch these pages when you need to verify SDK behavior, check parameter names, or understand component capabilities — don't guess from memory.

## MCP Bridge (`execute_csharp`)

C# snippets run inside the Unity Editor via the MCP `execute_csharp` tool. Key details:

- Runs on Unity main thread with a **30-second timeout**
- Must end with `return "some string";`
- Available usings: `System`, `System.Collections.Generic`, `System.IO`, `System.Linq`, `System.Text`, `UnityEditor`, `UnityEngine`, `UnityEngine.SceneManagement`
- All loaded assemblies are referenced (Unity, VRC SDK, VRCFury, project scripts)
- Each snippet is independent — chain multiple MCP calls for multi-step operations
- Always call `EditorUtility.SetDirty(obj)` on modified objects
- Save with `AssetDatabase.SaveAssets()` or `EditorSceneManager.SaveOpenScenes()`

## Workflow — Scene Exploration First

**Default**: work on the currently open scene unless the user specifies otherwise.

Start by exploring the scene hierarchy — find root GameObjects, locate the avatar, understand the structure before making changes.

- **Find avatar root** by enumerating all `VRCAvatarDescriptor` objects and recording each descriptor's full hierarchy path (`Root/Child/Avatar`), then target the avatar by exact path
- **Walk hierarchy** recursively from the avatar transform
- **Navigate bones** via `transform.Find("Armature/Hips/Spine/Chest/...")`
- **List components, blendshapes, materials** via standard Unity APIs (`GetComponents`, `sharedMesh.GetBlendShapeName`, `sharedMaterials`)

## Animation Clip Binding Reference

- **Blendshapes:** `EditorCurveBinding.FloatCurve("MeshPath", typeof(SkinnedMeshRenderer), "blendShape.ShapeName")`
- **GameObject active:** `EditorCurveBinding.FloatCurve("ObjectPath", typeof(GameObject), "m_IsActive")`
- Always `EditorUtility.SetDirty()` on modified objects; `Undo.RecordObject()` before changes

## VRC SDK Notes

The VRC SDK ships as compiled DLLs — no readable C# source for components.
- Whitelist source (readable C#): `AvatarValidation.cs` in both base and avatars packages
- PhysBone reference scene: `Packages/com.vrchat.avatars/Samples/Dynamics/Robot Avatar/Avatar Dynamics Robot Avatar PC.unity`

**Key C# namespaces:**
- `VRC.SDK3.Avatars.Components` — VRCAvatarDescriptor, VRCStation
- `VRC.SDK3.Dynamics.PhysBone.Components` — VRCPhysBone, VRCPhysBoneCollider
- `VRC.SDK3.Dynamics.Contact.Components` — VRCContactSender, VRCContactReceiver
- `VRC.SDK3.Dynamics.Constraint.Components` — VRCPositionConstraint, VRCRotationConstraint, etc.
- `VRC.SDK3.Avatars.ScriptableObjects` — VRCExpressionParameters, VRCExpressionsMenu

## Avatar Logic

### Write Defaults Convention

**Use Write Defaults ON** on all states.
- WD OFF in unmasked controllers causes FX to claim ownership of transforms/muscles from higher layers (Gesture)
- WD OFF with empty/None motions causes "sticky" properties that never reset
- Direct BlendTrees and Additive layers MUST always be WD ON (values fly off with WD OFF)
- Mixed WD in a controller causes random properties to stick
- **Rule:** All states WD ON. Never mix within a controller.

### Playable Layers

| Layer | Purpose |
|---|---|
| **Base** | Locomotion (humanoid muscles). Default supplied by VRChat. |
| **Additive** | Additive on top of Base (breathing, idle animations). Always WD ON. |
| **Gesture** | Hand gestures, face expressions. Masked to upper body / hands. |
| **Action** | Full-body overrides (emotes, AFK). Starts at weight 0; use VRCPlayableLayerControl to blend in. |
| **FX** | Non-transform only: toggles, blendshapes, materials, shader properties. |

### Toggle & Feature Inspection Workflow

When inspecting avatar toggles/features — regardless of whether the user mentions VRCFury, FX, or just "toggles" — **always check all sources**:

If the avatar has one or more VRCFury components, this workflow applies to **any work on that avatar** (including non-VRCFury edits like direct FX/controller/menu/parameter/animation/material changes). Verification must use the built output from `VRCAvatarDescriptor` after VRCFury build succeeds and Unity enters Play Mode.

1. **VRCFury components** — scan all VRCFury components in the scene (Toggle, FullController, GestureDriver, Puppet). These generate layers/menus/parameters during VRCFury build and may not appear in descriptor-linked playable layer controllers before build completes.
2. **Expression Parameters** — read the VRCExpressionParameters asset from the descriptor. Lists all synced parameters with types, defaults, saved/synced flags. Cross-reference with what controllers and VRCFury use.
3. **Expression Menus** — walk the full menu tree (root menu + all submenus). Shows what the user sees in the radial menu: toggles, submenus, puppets, buttons.
4. **All playable layer controllers** — not just FX. Inspect layers, states, transitions, blend trees, and state behaviors in:
   - **FX** (toggles, blendshapes, material swaps, shader properties)
   - **Gesture** (hand gesture → face expression mappings, can also have toggles)
   - **Action** (emotes, AFK states)
   - **Additive** (breathing, idle overlays)
   - **Base** (usually default, but may be customized)

   For each controller, also check:
   - **State behaviors** — especially VRCAvatarParameterDriver (side-effect parameter drives), VRCAnimatorTrackingControl (tracking overrides), VRCPlayableLayerControl (layer blending). These are invisible if you only look at transitions and motions.
   - **Layer weight and AvatarMask** — a layer at default weight 0 does nothing until blended in by a state behavior. Masks change what a layer can affect. Report both for every layer.
   - **WD consistency** — flag any states with Write Defaults OFF or mixed WD within a controller.
5. **Parameter budget** — calculate used bits from the VRCExpressionParameters asset (Bool=1, Int=8, Float=8 for synced params). Report used/256 bits remaining. Unsynced parameters don't count toward the budget but still exist in controllers. **Note:** When VRCFury is in the project, it automatically adds Parameter Compressor to the avatar at upload time, which reduces synced parameter bandwidth — so the raw bit count is a worst-case estimate.

Present a unified summary combining all sources. Flag any inconsistencies (e.g., a parameter in the menu but missing from the parameters asset, an FX layer with no corresponding menu entry, mixed Write Defaults, or a layer at weight 0 with no behavior to activate it).

### Avatar Audit Workflow

When the user asks to understand the full avatar setup — not just toggles — check **all** of the following:

1. **Dynamics:**
   - **PhysBones** — list all PhysBone roots, their key settings (pull, spring, stiffness, gravity), colliders, and parameter names (which feed `_IsGrabbed`, `_Angle`, etc. into animators).
   - **Contacts** — all VRCContactSenders and VRCContactReceivers. Report collision tags, receiver types (Constant/OnEnter/Proximity), parameter names, and what they're used for (headpats, boops, proximity toggles).
   - **Constraints** — VRC constraints (VRCPositionConstraint, VRCRotationConstraint, VRCScaleConstraint, VRCParentConstraint, VRCAimConstraint, VRCLookAtConstraint). Often used for world-fixed objects, orbit systems, and prop mechanics.
2. **Toggles & features** — run the Toggle & Feature Inspection Workflow above.
3. **Renderers & materials** — list all SkinnedMeshRenderers and MeshRenderers, their material counts, shader names, and blendshape counts. This gives a quick picture of visual complexity.
4. **Other components** — VRCStations, AudioSources, Lights, ParticleSystems, TrailRenderers, LineRenderers. These affect performance and behavior.

Present findings grouped by category. Flag potential issues: high PhysBone counts, contacts without matching parameters, constraints with missing sources, unoptimized renderers.

### Expression Parameters

Types: Bool=1bit, Int=8bits (0-255), Float=8bits (-1.0 to 1.0). **Synced budget: 256 bits.**

Create via `ScriptableObject.CreateInstance<VRCExpressionParameters>()`. The `.parameters` field is an array of `Parameter` with fields: `name` (string), `valueType` (Bool/Int/Float), `defaultValue`, `saved`, `networkSynced`.

### Expression Menus

Max 8 controls per menu. Control types: Button, Toggle, SubMenu, TwoAxisPuppet, FourAxisPuppet, RadialPuppet. Discover enum values and subParameter counts via `System.Enum.GetValues(typeof(VRCExpressionsMenu.Control.ControlType))`.

### Contacts, Constraints & PhysBone Parameters

**Dynamics inspection:** When understanding an avatar, list all contacts and constraints before creating new ones. For contacts, note collision tags, receiver types, and parameter names — these often drive interaction systems (headpats, boops, proximity toggles). For constraints, note source objects and what they achieve (world-fixed props, orbit systems, etc.).

### VRChat Upload-Ready Avatar Checklist

1. **VRCAvatarDescriptor** on the root GameObject (ViewPosition, lip sync, eye look, playable layers)
2. **PipelineManager** on the root (holds `blueprintId` — SDK auto-adds it)
3. **Animator** with a humanoid Avatar asset (from FBX import)
4. **ViewPosition** between the eyes, slightly forward: typically `(0, eyeHeight, 0.05-0.1)`
5. Root at scene root (not nested under another object)

## PhysBone Reference

**Inspection first:** When understanding an avatar, list all existing PhysBones before creating new ones — root bones, colliders, parameter names, grab/pose settings. Use `FindObjectsOfType<VRCPhysBone>()` and `FindObjectsOfType<VRCPhysBoneCollider>()` to enumerate them.

When configuring PhysBones, inspect the avatar's existing PhysBones first. For new PhysBones, reference the VRC SDK sample scene (discover via `AssetDatabase.FindAssets("t:Scene", new[]{"Packages/com.vrchat.avatars/Samples/"})`) and tune values to the specific avatar's scale and body part. Hair chains typically need a chest VRCPhysBoneCollider (capsule) to prevent clipping — size it to the avatar.

## VRCFury

**VRCFury docs:** https://vrcfury.com/ — non-destructive avatar tools (toggles, gestures, controller merging, prefab-based workflows).

Package path: `Packages/com.vrcfury.vrcfury`
Ships as **pure C# source** (no DLLs).

### Component Storage

VRCFury uses v3 storage: single feature in `content` field (`[SerializeReference] FeatureModel`). Legacy v2 (`config.features` list) is auto-migrated.

### Pre-Creation Conflict Check

**Before creating any new toggle or animation layer for an object**, search for existing bindings:

1. **Search all FullController-merged controllers** — find AnimatorControllers referenced by FullController components, then search their layers for bindings on the target object path (e.g., `shirt|m_IsActive`, `shirt|material.*`).
2. **Search existing VRCFury Toggles** — check if another Toggle already controls the same object.
3. **Search the avatar descriptor's playable layer controllers** — the FX controller (and others) may already have layers affecting the object.

If conflicts exist, decide whether to **modify** the existing system or **replace** it — never silently add a second controller for the same object.

### VRCFury Build Verification & Discovery (Required for Avatar Work)

If the target avatar has one or more VRCFury components, this applies to **all work on that avatar**. Use this discovery-first workflow to infer behavior from source + build output, not from assumptions.

1. **Inventory features** — enumerate all VRCFury components on the avatar. Record feature types, referenced controllers/menus/params, base object overrides, and binding rewrites.
2. **Trace builders** — for each feature in use, read the corresponding builder(s) in `Editor/VF/Feature/` and any called service logic that can rewrite parameters, bindings, layers, or object paths.
3. **Predict transformations** — create a quick authoring-to-built map for parameter names, animation binding paths/types, layer/state names, and object existence/paths after preprocess.
4. **Build and verify** — enter Play Mode to trigger the build pipeline. All tools implementing `IVRCSDKPreprocessAvatarCallback` (VRCFury, NDMF, etc.) run automatically on Play Mode entry. Read resulting built assets from `VRCAvatarDescriptor` (expression parameters, expression menu, all playable layer controllers). Treat this merged output as authoritative.
5. **Diff and resolve** — compare predicted vs built results, then adjust assets/config until they match intended behavior. If build fails or built descriptor assets are unavailable, treat verification as incomplete and report the blocker.

**Definition of done:** Do not mark avatar changes complete until both source-level checks and built-output verification pass.

### Scanning Existing VRCFury Components

**Reflection access:** Assembly `"VRCFury"`, type `"VF.Model.VRCFury"`, then `FindObjectsOfType(type)`. Cast each to `Component` to get `.gameObject`.

**SerializedProperty paths** (all relative to `SerializedObject` → `FindProperty`):
- **Feature type:** `"content"` → `.managedReferenceFullTypename` (e.g., contains `"Toggle"`, `"FullController"`)
- **Toggle fields:** `"content"` → `name` (menu label), `saved`, `defaultOn`, `state.actions` (array — each element's `.managedReferenceFullTypename` = action type)
- **FullController fields:** `"content"` → `controllers` array (each has `controller.objRef`, `type`), `menus` array (`menu.objRef`), `prms` array (`parameters.objRef`)

### Modifying Existing VRCFury Components

Use `SerializedObject` to modify existing VRCFury components with undo support.

- **Action types** live at `"VF.Model.StateAction.<ActionName>"` (e.g., `ObjectToggleAction`)
- **Two-pass pattern (critical):** After setting `managedReferenceValue = Activator.CreateInstance(actionType)` on a new array element, you must `ApplyModifiedProperties()` → `so.Update()` → re-fetch the element via `GetArrayElementAtIndex()` before setting child fields (e.g., `FindPropertyRelative("obj")`). Skipping this causes null references.

**GuidAnimationClip fields:** When setting animation clips via SerializedObject, use `FindPropertyRelative("clip.objRef")` — this is the `UnityEngine.Object` reference inside the GuidWrapper.

**Usage policy:** Only use VRCFury components when the user explicitly requests it. If VRCFury would simplify a task but wasn't requested, ask the user before using it. Default to manual FX controllers + expression parameters + expression menus.

**Source-first rule:** Before creating or configuring any VRCFury component:
1. **Read the public API first** — check `PublicApi/` for a supported method. The public API is safer and handles internal details automatically.
2. **If the public API doesn't cover the need**, fall back to `SerializedObject` — but first **read the model source** at `Runtime/VF/Model/Feature/<FeatureName>.cs` to understand all required fields, their types, and default values. Never guess at serialized field names or types.
3. **For advanced features** (transitions, separate local, security, exclusive tags), also **read the builder source** at `Editor/VF/Feature/<FeatureName>Builder.cs`. The model defines fields; the builder defines how they're used — transition timing, state machine structure, resting state registration. Never guess at runtime semantics.

### Features & StateAction Discovery

Discover available VRCFury features by scanning `Runtime/VF/Model/Feature/` for `FeatureModel` subclasses. Discover available state actions by scanning `Runtime/VF/Model/StateAction/` for action types and their fields.

Prefer specific actions (ObjectToggleAction, BlendShapeAction, MaterialAction, etc.) over AnimationClipAction when possible.

### Toggle Transition Pitfall

When using `hasTransition = true`, **the ON state (`state.actions`) must not be empty** — VRCFury uses it for resting state registration, WD ON defaults, and `expandIntoTransition`. Read `Editor/VF/Feature/ToggleBuilder.cs` for the full state machine structure.

### Key Source Paths

| Content | Path (relative to package root) |
|---|---|
| MonoBehaviour components | `Runtime/VF/Component/` |
| FeatureModel subclasses | `Runtime/VF/Model/Feature/` |
| StateAction subclasses | `Runtime/VF/Model/StateAction/` |

### Public API (`com.vrcfury.api`)

**Creating new components:** Use the public API — it's simpler and handles setup automatically.
**Reading/modifying existing components:** The public API is create-only. Internal types (`VF.Model.*`) can't be referenced directly in MCP snippets (they're `internal`). Use reflection to get the type (`vfAsm.GetType("VF.Model.VRCFury")`) and `SerializedObject` to read/write properties. See "Scanning Existing VRCFury Components" above.

**Entry point:** `com.vrcfury.api.FuryComponents` — read source files in `Packages/com.vrcfury.vrcfury/PublicApi/` to discover available factory methods (e.g., `CreateToggle`, `CreateArmatureLink`) and their returned types' APIs.

## FBX Metadata Cache (`userData`)

`Assets/Claude/Editor/ModelMetadataCache.cs` caches FBX hierarchy, fileIDs, materials, and blendshapes into each model's `.meta` file under `userData`. **Check `userData` first** when you need bone hierarchy, material slots, or blendshape names — but it may not be present. Always fall back to C# API queries if absent.

Prefer querying live data via `SkinnedMeshRenderer.sharedMesh` and `transform.Find()` rather than parsing the cache format.

## Poiyomi Toon Shader

Discover Poiyomi shaders from the material's `shader.name`, or search via `AssetDatabase.FindAssets("t:Shader").Select(g => AssetDatabase.GUIDToAssetPath(g))` filtering for names containing "poiyomi".

**Discovery-first:** Never hardcode Poiyomi property names, values, or keywords. Always dump the material to discover what's active, then dump specific module properties to find exact names.

### Dump-First Workflow

**Never guess Poiyomi property names.** Poiyomi has many properties with inconsistent slot numbering. Use a **two-phase approach** — discover active modules first, then dump only those.

**Locked shader name resolution:** If `shader.name` starts with `"Hidden/Locked/"`, strip that prefix and remove the trailing segment after the last `/` to get the unlocked shader name. Use `Shader.Find()` with the unlocked name to access the full property list.

**Phase 1 — Module Discovery:** Iterate shader properties. Properties named `m_start_*` are section markers. Parse `reference_property:<propName>` from `shader.GetPropertyDescription(i)` (substring after `"reference_property:"` up to the next `,`, `}`, or space). If that property exists on the material and is non-zero (`mat.GetFloat(prop) != 0`), the module is enabled. The section name is the `m_start_` suffix. Also check `mat.shaderKeywords` for active keywords.

**Phase 2 — Targeted Property Dump:** `m_start_<name>` / `m_end_<name>` pairs define section ranges. Use a stack to handle nesting — push on `m_start_`, pop on `m_end_`. For properties within a section range, compare values to shader defaults (`shader.GetPropertyDefaultFloatValue(i)`, `shader.GetPropertyDefaultVectorValue(i)`) to find non-defaults. Handle all property types: Float/Range, Color, Texture, Vector.

**Prefix Dump:** For features without section markers (e.g., outlines with `_EnableOutlines`), filter by property name prefix (e.g., `_Outline`). Iterate all shader properties and dump those matching the prefix.

### Source Reading Strategy

The shader is a large monolithic file. Never read it whole — use targeted searches.

**Locate shader source:** Resolve the locked name (see above), then `FindAssets("t:Shader")` and match by `.name`.

**Property semantics:** Search the shader's Properties block for the property name. Thry Editor metadata reveals ranges (`Range(min, max)`), enum options (`ThryWideEnum`), dependencies (`reference_property:`), and visibility conditions (`condition_showS:`).

**Module implementation:** Search for `//ifex _Enable<Feature>==0` in the `.shader` file to find feature code blocks. For older versions, search for `CGI_Poi<Feature>.cginc` include files.

**Discover paths dynamically** — Poiyomi's install location and version vary per project. Use `AssetDatabase.FindAssets` or `find` to locate shader sources, include files, and `PoiLabels.txt`.

### Animated Properties (Locking)

Mark properties animated at runtime with `material.SetOverrideTag("{PropertyName}Animated", "1")`.
- `"1"` = Animated (stays as shader parameter when locked)
- `"2"` = Renamed (becomes `{PropertyName}_{MaterialName}`)

Leave materials unlocked (`_ShaderOptimizerEnabled` = 0) — Poiyomi re-locks on upload.

### Locking Lifecycle

- **Play mode / VRCFury builds trigger Poiyomi re-locking.** The locked shader compiles out code paths for disabled modules. Always verify the material is unlocked (`_ShaderOptimizerEnabled = 0`) before editing, and re-check after play mode.
- **Programmatic unlock:** Use `ShaderOptimizer.UnlockMaterials(new[]{mat})` (class may be `Thry.ThryEditor.ShaderOptimizer` or `Thry.ShaderOptimizer` depending on version — resolve via reflection if needed). Check first with `ShaderOptimizer.IsMaterialLocked(mat)`.
- **After edits, leave unlocked** — Poiyomi re-locks automatically on upload and play mode. Do not manually re-lock.
- **VRCFury only locks, never unlocks** — `MaterialLocker.Lock()` pre-locks materials during VRCFury build. If VRCFury is present and the material needs editing, unlock it before VRCFury build runs.
- **After enabling a new module:** run Phase 1 Module Discovery to verify the enable property is set and the keyword is active. Don't assume — dump and confirm.

## Post-Build Verification (AvatarTypeChecker)

`Packages/xyz.sentfromspace-agentic-tools/Editor/AvatarTypeChecker.cs` performs static analysis on the **built** avatar clone after VRCFury/NDMF preprocessing. It catches parameter mismatches (like VRCFury renames), broken animation paths, missing blendshapes, and invalid material properties.

**When to run:** After any avatar edit that changes parameters, animations, toggles, materials, or PhysBones — run this as the final verification step:

1. Enter Play Mode (triggers VRCFury/NDMF build pipeline)
2. Discover descriptor paths (if needed) via MCP, then choose the exact avatar path:
   `var ds = UnityEngine.Object.FindObjectsOfType<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(true); var sb = new System.Text.StringBuilder(); foreach (var d in ds) { var t = d.transform; var p = new System.Collections.Generic.List<string>(); while (t != null) { p.Add(t.name); t = t.parent; } p.Reverse(); sb.AppendLine(string.Join("/", p)); } return sb.ToString();`
3. Call `return SentFromSpace.AgenticTools.Editor.AvatarTypeChecker.ValidateBuiltAvatar("Root/Child/Avatar");` via MCP
4. Review the output — fix any `[ERROR]` items before considering the task complete
5. Exit Play Mode

`ValidateBuiltAvatar()` without a path intentionally returns an error to avoid selecting the wrong avatar.

**What it checks:**
- **Parameters:** PhysBone/Contact params vs animator controllers (with VRCFury rename fuzzy matching), type mismatches, unused expression params, menu param validity, synced bit budget
- **Animation Paths:** hierarchy path resolution, component existence, blendshape names, material slot indices
- **Material Properties:** shader property existence on locked materials, Poiyomi AnimatedTag detection

**Interpreting results:**
- `[ERROR]` = broken at runtime, must fix
- `[WARN]` = wasteful but functional
- `[INFO]` = informational (bit budget, VRCFT/VRCFury type difference counts)
- VRCFT (OSC-driven face tracking) Bool→Float type differences and undefined controller params are auto-detected and skipped
- VRCFury toggle (`VF###_*` prefix) Bool→Float type differences are auto-detected and skipped
- VRCFury dummy paths (`ThisHopefullyDoesntExist`) and internal params (`VF DN`, etc.) are filtered out

## Toggle Verification (GestureManagerVerifier)

`Packages/xyz.sentfromspace-agentic-tools/Editor/GestureManagerVerifier.cs` uses Gesture Manager (if installed) to verify toggles actually work at runtime. It tests **specific toggles by parameter name** — pass the authoring-time names of the toggles you just changed. VRCFury `VF###_` prefix renames are matched automatically.

**When to run:** After AvatarTypeChecker, when toggle behavior needs runtime verification (new/modified toggles, debugging broken toggles). Requires play mode. If Gesture Manager isn't in the scene, it's instantiated automatically (cleaned up on play mode exit).

**MCP invocation:**
`return SentFromSpace.AgenticTools.Editor.GestureManagerVerifier.VerifyToggles("Root/Avatar", "Clothing/Top/Bikini", "Clothing/Acc/Glasses");`

Authoring-time parameter names work even when VRCFury renames them with `VF###_` prefixes at build time. Calling without parameter names returns an error — always specify which toggles to test.

**What it checks:** For each matched Toggle in the expression menu, flips the parameter and reports:
- Object active state changes (activeSelf)
- Blendshape weight changes
- Material swaps (different material instances)
- Material property changes (shader float/color values — dissolve, emission, etc.)

**Interpreting results:**
- `[PASS]` = toggle caused visible changes
- `[FAIL]` = toggle changed nothing (broken animation path, wrong parameter, WD issue)
- `[WARN]` = parameter not found in expression menu or Gesture Manager

## VRCLens Custom Shader Mods

This project adds optional shader mods to VRCLens without modifying the original VRCLens assets. Mods are patched into the shader at avatar build time.

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

### Shader Patching Patterns

- **Text replacement** — Direct string substitution (e.g., changing a threshold value). Used for simple mods like LowerMinFocus.
- **Anchor-based insertion** — Find a stable line in the shader, inject code after it. Used for feature additions like ManualFocusAssist. All injections wrapped in `// VRCLens_Custom BEGIN/END` markers for idempotency.
- **Anchor sites** are numbered (Site 1a, 1b, 2, 3, 4a, 4b, 5a, 5b, etc.) — see patcher source for the full list. Multiple mods can share anchor sites (injections stack).

### VRCFury Menu Convention

All custom features use `VRCLens/Custom/<Feature>` as the menu path. For features with multiple controls, use a submenu (e.g., `VRCLens/Custom/GhostFX` containing toggles + radial puppets).

### Pass Structure Reference

| Pass | Purpose | Key textures |
|------|---------|-------------|
| Pass 0 | Camera feed capture, CoC calculation | `_RenderTex`, `_DepthTex` → outputs color.rgb + CoC in .a |
| Pass 1 | Bokeh blur (228-348 sample disk kernel) | GrabPass `_HirabikiVRCLensPassTex_One` |
| Pass 2 | Final composition: tone mapping, exposure, overlays | GrabPass `_HirabikiVRCLensPassTexture` |

**Critical rule:** CoC serves dual purposes (blur amount + focus zone threshold). Modifications must preserve the original CoC in Pass 0's alpha for focus peaking, then apply changes only in Pass 1 (blur sampling) or Pass 2 (post-processing).
