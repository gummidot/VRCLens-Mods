
#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using VRC.SDKBase.Editor.BuildPipeline;

// IVRCSDKPreprocessAvatarCallback is not documented anywhere, probably deprecated?
// To figure out how to use it, see https://github.com/search?q=IVRCSDKPreprocessAvatarCallback&type=code
// Mostly adapted from https://github.com/d4rkc0d3r/d4rkAvatarOptimizer/blob/f1a85c9026cf62f0ab37bf6c385891a8a34680e2/Editor/AvatarBuildHook.cs

[InitializeOnLoad]
public class AvatarBuildHook : IVRCSDKPreprocessAvatarCallback
{

    // This has to be before -1024 when VRCSDK deletes our components.
    // VRCFury runs at -10000, Modular Avatar at -25, d4rkAvatarOptimizer at -15 or -1025.
    // Really just need to run after VRCFury in case the VRCLens controller is added as
    // a VRCFury component. Not sure if we can run after Modular, but it's probably rare
    // that VRCFury would be added as a MA prefab.
    public int callbackOrder => -1025;

    public static string TempDir = "Assets/VRCLens_Custom/Temp";

    public bool OnPreprocessAvatar(GameObject avatarGameObject)
    {
        Debug.Log($"[VRCLensCustom] Running OnPreprocessAvatar for: {avatarGameObject.name}");
        // Optimzers
        var optimizers = avatarGameObject.GetComponentsInChildren<VRCLensOptimizer>();
        try
        {
            foreach (var optimizer in optimizers)
            {
                Debug.Log($"[VRCLensCustom] Running optimizer from '{optimizer.gameObject.name}' on avatar: {avatarGameObject.name}");
                optimizer.Optimize();
            }
        }
        catch (Exception e)
        {
            Debug.LogError(e);
            return false;
        }

        // Modifiers
        var modifiers = avatarGameObject.GetComponentsInChildren<VRCLensModifier>();
        if (modifiers.Length > 0)
        {
            // Clear and recreate temp dir
            if (AssetDatabase.IsValidFolder(TempDir))
            {
                Debug.Log($"[VRCLensCustom] Deleting temp directory: {TempDir}");
                AssetDatabase.DeleteAsset(TempDir);
            }
            string parentDir = GetDirectoryName(TempDir);
            string newFolderName = Path.GetFileName(TempDir);
            AssetDatabase.CreateFolder(parentDir, newFolderName);
            Debug.Log($"[VRCLensCustom] Created temp directory: {TempDir}");

            try
            {
                foreach (var modifier in modifiers)
                {
                    Debug.Log($"[VRCLensCustom] Running modifier from '{modifier.gameObject.name}' on avatar: {avatarGameObject.name}");
                    modifier.Modify(TempDir);
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                return false;
            }
        }

        return true;
    }

    // AssetDatabase suggests it doesn't support backslashes but doesn't explicitly say so.
    // Backslashes do seem to work as of Unity 2022, but just to be safe, convert all
    // backslashes to forward slashes.
    public static string GetDirectoryName(string path)
    {
        return Path.GetDirectoryName(path)?.Replace("\\", "/");
    }
}
#endif
