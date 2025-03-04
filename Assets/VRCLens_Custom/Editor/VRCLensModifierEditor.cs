#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

[CustomEditor(typeof(VRCLensModifier))]
public class VRCLensModifierEditor : Editor
{
    private VRCLensModifier modifier;

    private SerializedProperty addDroneVProp;
    private SerializedProperty fixAvatarDropProp;

    private SerializedProperty useCustomResolutionProp;
    private SerializedProperty sensorResProp;
    private SerializedProperty msaaProp;

    private void OnEnable()
    {
        modifier = (VRCLensModifier)target;

        // Link the SerializedProperties to the fields in the target object
        addDroneVProp = serializedObject.FindProperty(nameof(modifier.addDroneV));
        fixAvatarDropProp = serializedObject.FindProperty(nameof(modifier.fixAvatarDrop));

        useCustomResolutionProp = serializedObject.FindProperty(nameof(modifier.useCustomResolution));
        sensorResProp = serializedObject.FindProperty(nameof(modifier.sensorRes));
        msaaProp = serializedObject.FindProperty(nameof(modifier.msaa));
    }

    public override void OnInspectorGUI()
    {
        // Update the serialized object
        serializedObject.Update();

        VRCAvatarDescriptor avatarDescriptor = modifier.FindAvatarDescriptor();
        if (avatarDescriptor == null)
        {
            EditorGUILayout.HelpBox("Cannot find avatar. This script must be placed on an avatar with VRCLens.", MessageType.Warning);
            return;
        }

        VRCLens vrclens = modifier.GetVRCLens();
        if (vrclens == null)
        {
            EditorGUILayout.HelpBox("Cannot find VRCLens. This script must be placed on an avatar with VRCLens.", MessageType.Warning);
            return;
        }

        // Display where we found the avatar
        EditorGUILayout.ObjectField("Found avatar at:", avatarDescriptor.transform, typeof(Transform), false);

        EditorGUILayout.Space();

        // Add features section
        EditorGUILayout.LabelField("Add features", EditorStyles.boldLabel);

        // Layout for DroneV
        EditorGUILayout.BeginHorizontal();
        addDroneVProp.boolValue = EditorGUILayout.ToggleLeft("Drone vertical movement", addDroneVProp.boolValue);
        EditorGUILayout.EndHorizontal();

        // Layout for FixAvatarDrop
        EditorGUILayout.BeginHorizontal();
        fixAvatarDropProp.boolValue = EditorGUILayout.ToggleLeft("Fix Avatar Drop (bugged in VRCLens 1.9.1 and later)", fixAvatarDropProp.boolValue);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        // Sensor Resolution and Anti-Aliasing section
        EditorGUILayout.LabelField("Custom Resolution / Anti-Aliasing", EditorStyles.boldLabel);
        useCustomResolutionProp.boolValue = EditorGUILayout.ToggleLeft("Use Custom Resolution / Anti-Aliasing", useCustomResolutionProp.boolValue);
        if (useCustomResolutionProp.boolValue)
        {
            RenderTexture depthTex;
            Vector2Int renderTexSize, depthTexSize;
            int renderTexAntiAliasing;
            RenderTexture renderTex = VRCLensResolutionModifier.GetRenderTexture(vrclens, out depthTex, out renderTexSize, out depthTexSize, out renderTexAntiAliasing);
            if (renderTex == null)
            {
                EditorGUILayout.HelpBox("Cannot find Render Texture on VRCLens.", MessageType.Warning);
                return;
            }
            EditorGUILayout.LabelField("Current Resolution:", $"{renderTexSize.x} x {renderTexSize.y}");
            if (renderTexSize != depthTexSize)
            {
                EditorGUILayout.HelpBox("Render Texture and Depth Texture sizes do not match. You may want to reinstall VRCLens or fix this manually.", MessageType.Warning);
            }
            EditorGUILayout.LabelField("Current Anti-Aliasing:", renderTexAntiAliasing != 1 ? $"MSAA {renderTexAntiAliasing}x" : "Off");

            EditorGUILayout.Space();

            sensorResProp.vector2IntValue = EditorGUILayout.Vector2IntField("Override Resolution", sensorResProp.vector2IntValue);

            if (sensorResProp.vector2IntValue.x != 0 && sensorResProp.vector2IntValue.y != 0)
            {
                float aspectRatio = (float)sensorResProp.vector2IntValue.x / sensorResProp.vector2IntValue.y;
                EditorGUILayout.LabelField($"Aspect Ratio: {aspectRatio:F2}");
            }

            string[] msaaOptions = { "", "Off", "MSAA 2x", "MSAA 4x", "MSAA 8x" };
            int[] msaaValues = { 0, 1, 2, 4, 8 };
            int selectedIndex = Array.IndexOf(msaaValues, msaaProp.intValue);
            selectedIndex = EditorGUILayout.Popup("Override Anti-Aliasing", selectedIndex, msaaOptions);
            msaaProp.intValue = msaaValues[selectedIndex];
        }

        // Apply changes to the serialized object
        serializedObject.ApplyModifiedProperties();
    }
}
#endif