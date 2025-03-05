#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

[AddComponentMenu("Scripts/VRCLens Modifier (VRCLens Custom)")]
public class VRCLensModifier : MonoBehaviour, IEditorOnly
{
    public bool addDroneV;
    public bool fixAvatarDrop;

    public bool useCustomResolution;
    public Vector2Int sensorRes;
    public bool useFullSBS3d;
    public int msaa;

    public void Modify(string tempDir)
    {
        Debug.Log($"[VRCLensModifier] Running Modify() for: {gameObject.name}");

        VRCAvatarDescriptor avatarDescriptor = FindAvatarDescriptor();
        if (avatarDescriptor == null)
        {
            Debug.LogWarning($"[VRCLensModifier] Avatar not found. This script must be placed on an avatar with VRCLens.");
            return;
        }

        VRCLens vrclens = GetVRCLens();
        if (vrclens == null)
        {
            Debug.LogWarning($"[VRCLensModifier] VRCLens not found. This script must be placed on an avatar with VRCLens.");
            return;
        }

        AnimatorController controller = FindVRCLensController(avatarDescriptor);
        if (controller == null)
        {
            Debug.LogWarning($"[VRCLensModifier] No VRCLens FX controller found. This script must be placed on an avatar with VRCLens.");
            return;
        }
        String path = AssetDatabase.GetAssetPath(controller);
        Debug.Log($"[VRCLensModifier] Found VRCLens FX controller '{controller.name}' at path: {path}");

        AnimatorController newController = controller;

        if (addDroneV)
        {
            newController = VRCLensDroneVModifier.CopyAndModifyController(newController, tempDir);
            if (newController == null)
            {
                Debug.LogWarning($"[VRCLensModifier] Could not modify VRCLens FX controller for DroneV: {controller.name}");
                return;
            }
        }

        if (fixAvatarDrop)
        {
            newController = VRCLensFixAvatarDropModifier.CopyAndModifyController(newController, tempDir);
            if (newController == null)
            {
                Debug.LogWarning($"[VRCLensModifier] Could not modify VRCLens FX controller for AvatarDrop: {controller.name}");
                return;
            }
        }

        if (!ReplaceControllerInAvatar(avatarDescriptor, controller, newController))
        {
            Debug.LogWarning($"[VRCLensModifier] Could not replace VRCLens FX controller with: {newController.name}");
            return;
        }
        Debug.Log($"[VRCLensModifier] Successfully replaced VRCLens FX controller with: {newController.name}");

        if (useCustomResolution)
        {
            if (!VRCLensResolutionModifier.CopyAndModifyMaterials(vrclens, sensorRes, useFullSBS3d, msaa, tempDir))
            {
                Debug.LogWarning($"[VRCLensModifier] Could not modify VRCLens materials for custom resolution.");
                return;
            }
        }
    }

    public VRCLens GetVRCLens()
    {
        // First, check the parent object, as this is supposed to be a child of the VRCLens object
        Transform parent = transform.parent;
        if (VRCLens.IsVRCLens(parent))
        {
            return new VRCLens(parent);
        }
        // Otherwise, fall back to scanning the entire avatar
        VRCAvatarDescriptor avatarDescriptor = FindAvatarDescriptor();
        if (avatarDescriptor == null)
        {
            return null;
        }
        Transform vrclensTransform = VRCLens.FindVRCLens(avatarDescriptor.transform);
        if (vrclensTransform != null)
        {
            return new VRCLens(vrclensTransform);
        }
        return null;
    }

    public AnimatorController FindVRCLensController(VRCAvatarDescriptor avatarDescriptor)
    {
        foreach (var layer in avatarDescriptor.baseAnimationLayers)
        {
            var controller = layer.animatorController as AnimatorController;
            if (controller != null && VRCLensDroneVModifier.IsVRCLensController(controller))
            {
                return controller;
            }
        }
        return null;
    }

    public VRCAvatarDescriptor FindAvatarDescriptor()
    {
        Transform current = transform;
        while (current != null)
        {
            var avatarDescriptor = current.GetComponent<VRCAvatarDescriptor>();
            if (avatarDescriptor != null)
            {
                return avatarDescriptor;
            }
            current = current.parent;
        }
        return null;
    }

    private bool ReplaceControllerInAvatar(VRCAvatarDescriptor avatarDescriptor, AnimatorController originalController, AnimatorController newController)
    {
        for (int i = 0; i < avatarDescriptor.baseAnimationLayers.Length; i++)
        {
            if (avatarDescriptor.baseAnimationLayers[i].animatorController == originalController)
            {
                avatarDescriptor.baseAnimationLayers[i].animatorController = newController;
                Debug.Log($"[VRCLensModifier] Replaced controller '{originalController.name}' with '{newController.name}'");
                return true;
            }
        }
        return false;
    }
}

#endif