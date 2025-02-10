#if UNITY_EDITOR
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditor.Animations;

public class VRCLensDroneVModifier
{
    public static string DroneMoveLayer = "vCNP_Drone 212-214 i234";
    public static string DroneMoveLayerMoveHState = "MoveH";
    public static string DroneMoveLayerMoveVState = "MoveV";
    public static string DroneVParameter = "VRCLDroneV";
    public static string AnimMovFastUp = "MovFastUp";
    public static string AnimMovFastDown = "MovFastDown";
    public static string AnimMovNeutral = "MovNeutral";

    public static string DroneVBaseDir = "Assets/VRCLens_Custom/MoveDroneVertical";
    public static string DroneVTempDir = $"{DroneVBaseDir}/Temp";

    // Modifies the VRCLens FX controller to include a VRCLDroneV parameter and vertical movement BlendTrees
    // in the Drone Move layer. Returns the cloned and modified controller. Returns null if the controller
    // was already modified manually, or if the controller could not be found or modified.
    public static AnimatorController CopyAndModifyController(AnimatorController controller)
    {
        string controllerPath = AssetDatabase.GetAssetPath(controller);

        // Check if this is a VRCLens FX controller
        if (!IsVRCLensController(controller))
        {
            Debug.LogError($"[VRCLensDroneVModifier] Not a VRCLens FX controller: {controller.name}");
            return null;
        }

        // Check if the FX controller has already been modified with a VRCLDroneV parameter.
        // Users (me) may have already done this manually.
        if (controller.parameters.Any(p => p.name == DroneVParameter))
        {
            Debug.Log($"[VRCLensDroneVModifier] Parameter '{DroneVParameter}' already exists in the controller");
            return null;
        }

        // Clear and recreate temp dir
        if (AssetDatabase.IsValidFolder(DroneVTempDir))
        {
            Debug.Log($"[VRCLensDroneVModifier] Deleting temp directory: {DroneVTempDir}");
            AssetDatabase.DeleteAsset(DroneVTempDir);
        }
        string parentDir = GetDirectoryName(DroneVTempDir);
        string newFolderName = Path.GetFileName(DroneVTempDir);
        AssetDatabase.CreateFolder(parentDir, newFolderName);
        Debug.Log($"[VRCLensDroneVModifier] Created temp directory: {DroneVTempDir}");

        // Duplicate the controller so we don't modify the original.
        // Use GUID of the original controller for a unique filename within the temp dir.
        string controllerGUID = AssetDatabase.AssetPathToGUID(controllerPath);
        string modifiedControllerPath = $"{DroneVTempDir}/{controller.name}_{controllerGUID}_Modified.controller";

        if (!AssetDatabase.CopyAsset(controllerPath, modifiedControllerPath))
        {
            Debug.LogError($"[VRCLensDroneVModifier] Failed to copy controller from {controllerPath} to {modifiedControllerPath}");
            return null;
        }
        Debug.Log($"[VRCLensDroneVModifier] Duplicated controller to {modifiedControllerPath}");

        AnimatorController modifiedController = AssetDatabase.LoadAssetAtPath<AnimatorController>(modifiedControllerPath);
        if (modifiedController == null)
        {
            Debug.LogError($"[VRCLensDroneVModifier] Failed to load copied controller at path: {modifiedControllerPath}");
            return null;
        }

        // Add VRCLDroneV parameter
        modifiedController.AddParameter(DroneVParameter, AnimatorControllerParameterType.Float);
        Debug.Log($"[VRCLensDroneVModifier] Added parameter '{DroneVParameter}' to the controller");

        // Find the DroneMoveLayer layer
        AnimatorControllerLayer droneMoveLayer = FindLayer(modifiedController, DroneMoveLayer);
        if (droneMoveLayer == null)
        {
            Debug.LogError($"[VRCLensDroneVModifier] Layer '{DroneMoveLayer}' not found in the controller");
            return null;
        }

        // Find the MovNeutral, MovFastUp, and MovFastDown animations in the MoveV blendtree.
        // These will be reused later in the MoveH blendtree, rather than swapping in new animations,
        // as the original animations may have been modified, e.g., in a VRCFury processed controller
        // where animation paths AND names have been rewritten.
        Motion movNeutral = null;
        Motion movFastUp = null;
        Motion movFastDown = null;

        foreach (ChildAnimatorState childState in droneMoveLayer.stateMachine.states)
        {
            AnimatorState state = childState.state;
            if (state.name == DroneMoveLayerMoveVState)
            {
                Debug.Log($"[VRCLensDroneVModifier] Found state '{DroneMoveLayerMoveVState}' in layer '{DroneMoveLayer}'");
                if (state.motion is BlendTree blendTree)
                {
                    Debug.Log($"[VRCLensDroneVModifier] Found BlendTree '{blendTree.name}' in state '{DroneMoveLayerMoveVState}'");
                    foreach (var child in blendTree.children)
                    {
                        // Use position to identify the animation, as the name will have been rewritten by VRCFury
                        if (child.position == new Vector2(0, 0))
                        {
                            movNeutral = child.motion;
                            Debug.Log($"[VRCLensDroneVModifier] Found animation for MovNeutral: {child.motion.name}");
                        }
                        else if (child.position == new Vector2(0, 1))
                        {
                            movFastUp = child.motion;
                            Debug.Log($"[VRCLensDroneVModifier] Found animation for MovFastUp: {child.motion.name}");
                        }
                        else if (child.position == new Vector2(0, -1))
                        {
                            movFastDown = child.motion;
                            Debug.Log($"[VRCLensDroneVModifier] Found animation for MovFastDown: {child.motion.name}");
                        }
                    }
                }
            }
        }

        if (movFastUp == null || movNeutral == null || movFastDown == null)
        {
            Debug.LogError("[VRCLensDroneVModifier] One or more animations (MovNeutral, MovFastUp, MovFastDown) could not be found.");
            return null;
        }

        // Find and modify the MoveH blend tree
        foreach (ChildAnimatorState childState in droneMoveLayer.stateMachine.states)
        {
            AnimatorState state = childState.state;
            if (state.name == DroneMoveLayerMoveHState)
            {
                Debug.Log($"[VRCLensDroneVModifier] Found state '{DroneMoveLayerMoveHState}' in layer '{DroneMoveLayer}'");
                if (state.motion is BlendTree blendTree)
                {
                    Debug.Log($"[VRCLensDroneVModifier] Found BlendTree '{blendTree.name}' in layer '{DroneMoveLayer}'");
                    if (ModifyBlendTreeMoveH(blendTree, movNeutral, movFastUp, movFastDown))
                    {
                        // Save the changes to the AssetDatabase
                        AssetDatabase.SaveAssets();
                        AssetDatabase.Refresh();
                        return modifiedController;
                    }
                }
            }
        }

        Debug.LogError($"[VRCLensDroneVModifier] BlendTree '{DroneMoveLayerMoveHState}' not found in layer '{DroneMoveLayer}'");
        return null;
    }

    public static bool IsVRCLensController(AnimatorController controller)
    {
        if (controller == null)
        {
            return false;
        }
        return FindLayer(controller, DroneMoveLayer) != null;
    }

    private static AnimatorControllerLayer FindLayer(AnimatorController controller, string layerName)
    {
        foreach (AnimatorControllerLayer layer in controller.layers)
        {
            if (layer.name == layerName)
            {
                return layer;
            }
            // Handle VRCFury processed layers, which are renamed like "[VF95] vCNP_Drone 212-214 i234"
            if (layer.name.EndsWith(" " + layerName))
            {
                return layer;
            }
        }
        return null;
    }

    private static bool ModifyBlendTreeMoveH(BlendTree blendTree, Motion movNeutral, Motion movFastUp, Motion movFastDown)
    {
        // Create vertical movement BlendTrees for each direction with manual thresholds
        var directionPositions = new Dictionary<string, Vector2>
        {
            { "ahead", new Vector2(0, 1) },
            { "behind", new Vector2(0, -1) },
            { "left", new Vector2(-1, 0) },
            { "right", new Vector2(1, 0) },
            { "neutral", new Vector2(0, 0) }
        };

        foreach (var direction in directionPositions)
        {
            BlendTree newBlendTree = new BlendTree()
            {
                name = $"DroneV - {direction.Key}",
                blendType = BlendTreeType.Simple1D,
                useAutomaticThresholds = false,
                blendParameter = DroneVParameter
            };

            newBlendTree.AddChild(movFastDown, -1f);
            newBlendTree.AddChild(movNeutral, 0f);
            newBlendTree.AddChild(movFastUp, 1f);

            // Add the new BlendTree to the AssetDatabase
            AssetDatabase.AddObjectToAsset(newBlendTree, blendTree);
            Debug.Log($"[VRCLensDroneVModifier] Created new BlendTree '{newBlendTree.name}' at path: {AssetDatabase.GetAssetPath(newBlendTree)}");

            // Add the new BlendTree as a child of the original BlendTree
            blendTree.AddChild(newBlendTree, direction.Value);
        }
        return true;
    }

    // AssetDatabase suggests it doesn't support backslashes but doesn't explicitly say so.
    // Backslashes do seem to work as of Unity 2022, but just to be safe, convert all
    // backslashes to forward slashes.
    public static string GetDirectoryName(string path) {
        return Path.GetDirectoryName(path)?.Replace("\\", "/");
    }
}
#endif