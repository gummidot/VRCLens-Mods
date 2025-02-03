#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

[CustomEditor(typeof(VRCLensModifier))]
public class VRCLensModifierEditor : Editor
{
    private VRCLensModifier modifier;

    private SerializedProperty addDroneVProp;

    private void OnEnable()
    {
        modifier = (VRCLensModifier)target;
        
        // Link the SerializedProperties to the fields in the target object
        addDroneVProp = serializedObject.FindProperty(nameof(modifier.addDroneV));
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

        // Display where we found the avatar
        EditorGUILayout.ObjectField("Found avatar at:", avatarDescriptor.transform, typeof(Transform), true);

        EditorGUILayout.Space();

        // Add features section
        EditorGUILayout.LabelField("Add features", EditorStyles.boldLabel);

        // Layout for DroneV
        EditorGUILayout.BeginHorizontal();
        addDroneVProp.boolValue = EditorGUILayout.ToggleLeft("Drone vertical movement", addDroneVProp.boolValue);
        EditorGUILayout.EndHorizontal();

        // Apply changes to the serialized object
        serializedObject.ApplyModifiedProperties();
    }
}
#endif