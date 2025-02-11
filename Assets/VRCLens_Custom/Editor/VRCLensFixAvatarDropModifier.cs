#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEditor.Animations;
using UnityEngine.Animations;
#if VRCSDK_HAS_VRCCONSTRAINTS
using VRC.SDK3.Dynamics.Constraint.Components;
#endif
public class VRCLensFixAvatarDropModifier
{
    public static string DropLayer = "vCNT_Drop 250-252 [1B] i1,t23";
    public static string AvatarFixState = "AvatarFix";

    // Modifies the VRCLens FX controller to fix the AvatarDrop animation bug in VRCLens 1.9.1,
    // still present as of VRCLens 1.9.2.
    // Returns the cloned and modified controller. Returns null if the controller
    // was already modified manually, or if the controller could not be found or modified.
    public static AnimatorController CopyAndModifyController(AnimatorController controller, string tempDir)
    {
        string controllerPath = AssetDatabase.GetAssetPath(controller);

        // Check if this is a VRCLens FX controller
        if (!VRCLensDroneVModifier.IsVRCLensController(controller))
        {
            Debug.LogError($"[VRCLensFixAvatarDropModifier] Not a VRCLens FX controller: {controller.name}");
            return null;
        }

        // Duplicate the controller so we don't modify the original.
        // Use GUID of the original controller for a unique filename within the temp dir.
        string controllerGUID = AssetDatabase.AssetPathToGUID(controllerPath);
        string modifiedControllerPath = VRCLensDroneVModifier.GenerateModifiedControllerPath(tempDir, controller.name, controllerGUID);

        if (!AssetDatabase.CopyAsset(controllerPath, modifiedControllerPath))
        {
            Debug.LogError($"[VRCLensFixAvatarDropModifier] Failed to copy controller from {controllerPath} to {modifiedControllerPath}");
            return null;
        }
        Debug.Log($"[VRCLensFixAvatarDropModifier] Duplicated controller to {modifiedControllerPath}");

        AnimatorController modifiedController = AssetDatabase.LoadAssetAtPath<AnimatorController>(modifiedControllerPath);
        if (modifiedController == null)
        {
            Debug.LogError($"[VRCLensFixAvatarDropModifier] Failed to load copied controller at path: {modifiedControllerPath}");
            return null;
        }

        // Find the Drop layer
        AnimatorControllerLayer dropLayer = VRCLensDroneVModifier.FindLayer(modifiedController, DropLayer);
        if (dropLayer == null)
        {
            Debug.LogError($"[VRCLensFixAvatarDropModifier] Layer '{DropLayer}' not found in the controller");
            return null;
        }

        // Find the AvatarFix animation clip (FixEnable)
        Motion avatarFix = null;

        foreach (ChildAnimatorState childState in dropLayer.stateMachine.states)
        {
            AnimatorState state = childState.state;
            if (state.motion != null)
            {
                if (state.name == AvatarFixState)
                {
                    avatarFix = state.motion;
                    Debug.Log($"[VRCLensFixAvatarDropModifier] Found motion '{AvatarFixState}': {state.motion.name}");
                }
            }
        }

        if (avatarFix == null)
        {
            Debug.LogError($"[VRCLensFixAvatarDropModifier] Could not find motion '{AvatarFixState}'.");
            return null;
        }

        // Find the path to the WorldC object by inspecting the AvatarFix motion.
        // This could change at build time, e.g. via VRCFury, so we need to find it dynamically.
        // The AvatarFix motion will be animating other objects under WorldC.
        string worldCPath = null;
        AnimationClip avatarFixClip = avatarFix as AnimationClip;

        if (avatarFixClip != null)
        {
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(avatarFixClip);
            foreach (var binding in bindings)
            {
                if (binding.path.Contains(VRCLens.Paths.WorldC))
                {
                    worldCPath = binding.path.Substring(0, binding.path.IndexOf(VRCLens.Paths.WorldC) + VRCLens.Paths.WorldC.Length);
                    Debug.Log($"[VRCLensFixAvatarDropModifier] Found path to {VRCLens.Paths.WorldC}: {worldCPath}");
                    break;
                }
            }
        }

        if (worldCPath == null)
        {
            Debug.LogError($"[VRCLensFixAvatarDropModifier] Path to {VRCLens.Paths.WorldC} could not be found in the AvatarFix motion.");
            return null;
        }

        // Edit the AvatarFix motion to disable the Parent Constraint on the WorldC object.
        // Other clips are fine. DropFixDisable already sets the Parent Constraint weight to 1.
        AnimationClip modifiedAvatarFixClip = new AnimationClip();
        EditorUtility.CopySerialized(avatarFixClip, modifiedAvatarFixClip);

        // Frames 0 and 3 to match the rest of the animation clip
        AnimationCurve curve = AnimationCurve.Constant(0, 0.05f, 0);
        AnimationUtility.SetEditorCurve(modifiedAvatarFixClip, EditorCurveBinding.FloatCurve(worldCPath, typeof(ParentConstraint), "m_Weight"), curve);

        // VRCFury auto-converts ParentConstraint to VRCParentConstraint, so we need to handle that too.
#if VRCSDK_HAS_VRCCONSTRAINTS
        AnimationUtility.SetEditorCurve(modifiedAvatarFixClip, EditorCurveBinding.FloatCurve(worldCPath, typeof(VRCParentConstraint), "m_Weight"), curve);
#endif

        // Save the modified clip to the AssetDatabase
        // Separate file instead of AddObjectToAsset for easier debugging, really
        string modifiedAvatarFixClipPath = $"{tempDir}/FixEnable_FixedForVRCLens1.9.1.anim";
        AssetDatabase.CreateAsset(modifiedAvatarFixClip, modifiedAvatarFixClipPath);
        Debug.Log($"[VRCLensFixAvatarDropModifier] Created modified AvatarFix clip at path: {modifiedAvatarFixClipPath}");

        // Replace the original AvatarFix motion with the modified one.
        // There's also the AvatarFixed state that uses the same motion, so check all states.
        foreach (ChildAnimatorState childState in dropLayer.stateMachine.states)
        {
            AnimatorState state = childState.state;
            if (state.motion == avatarFix)
            {
                state.motion = modifiedAvatarFixClip;
                Debug.Log($"[VRCLensFixAvatarDropModifier] Replaced original AvatarFix motion with modified one in state: {state.name}");
            }
        }

        // Save the changes to the AssetDatabase
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return modifiedController;
    }
}
#endif