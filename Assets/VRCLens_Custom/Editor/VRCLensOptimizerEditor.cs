#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(VRCLensOptimizer))]
public class VRCLensOptimizerEditor : Editor
{
    private VRCLensOptimizer optimizer;

    private SerializedProperty removeMeshCameraModelProp;
    private SerializedProperty removeMeshPivotAnchorModelProp;
    private SerializedProperty removeMeshFocusPProp;
    private SerializedProperty removeMeshAuxCopyProp;
    private SerializedProperty removeMeshPreviewMeshProp;

    private void OnEnable()
    {
        optimizer = (VRCLensOptimizer)target;
        
        // Link the SerializedProperties to the fields in the target object
        removeMeshCameraModelProp = serializedObject.FindProperty(nameof(optimizer.removeMeshCameraModel));
        removeMeshPivotAnchorModelProp = serializedObject.FindProperty(nameof(optimizer.removeMeshPivotAnchorModel));
        removeMeshFocusPProp = serializedObject.FindProperty(nameof(optimizer.removeMeshFocusP));
        removeMeshAuxCopyProp = serializedObject.FindProperty(nameof(optimizer.removeMeshAuxCopy));
        removeMeshPreviewMeshProp = serializedObject.FindProperty(nameof(optimizer.removeMeshPreviewMesh));
    }

    public override void OnInspectorGUI()
    {
        // Update the serialized object
        serializedObject.Update();

        VRCLens vrclens = optimizer.GetVRCLens();
        if (vrclens == null)
        {
            EditorGUILayout.HelpBox("Cannot find VRCLens. This script must be placed on an avatar with VRCLens.", MessageType.Warning);
            return;
        }

        // Display where we found VRCLens
        EditorGUILayout.ObjectField("Found VRCLens at:", vrclens.Transform, typeof(Transform), true);

        EditorGUILayout.Space();

        // Remove Optional Materials section
        EditorGUILayout.LabelField("Remove Optional Components on Upload", EditorStyles.boldLabel);

        // Layout for CameraModel
        EditorGUILayout.BeginHorizontal();
        removeMeshCameraModelProp.boolValue = EditorGUILayout.ToggleLeft("Camera model", removeMeshCameraModelProp.boolValue);
        Transform cameraModel = vrclens.GetCameraModel();
        EditorGUILayout.ObjectField(cameraModel, typeof(Transform), true);
        EditorGUILayout.EndHorizontal();

        // Layout for PivotAnchorModel
        EditorGUILayout.BeginHorizontal();
        removeMeshPivotAnchorModelProp.boolValue = EditorGUILayout.ToggleLeft("Pivot indicator", removeMeshPivotAnchorModelProp.boolValue);
        Transform pivotAnchorModel = vrclens.GetPivotAnchorModel();
        EditorGUILayout.ObjectField(pivotAnchorModel, typeof(Transform), true);
        EditorGUILayout.EndHorizontal();

        // Layout for FocusP
        EditorGUILayout.BeginHorizontal();
        removeMeshFocusPProp.boolValue = EditorGUILayout.ToggleLeft("Focus pointer (VR only)", removeMeshFocusPProp.boolValue);
        Transform focusP = vrclens.GetFocusP();
        EditorGUILayout.ObjectField(focusP, typeof(Transform), true);
        EditorGUILayout.EndHorizontal();

        // Layout for AuxCopy
        EditorGUILayout.BeginHorizontal();
        removeMeshAuxCopyProp.boolValue = EditorGUILayout.ToggleLeft("Avatar auto-focus", removeMeshAuxCopyProp.boolValue);
        Transform auxCopy = vrclens.GetAuxCopy();
        EditorGUILayout.ObjectField(auxCopy, typeof(Transform), true);
        EditorGUILayout.EndHorizontal();

        // Layout for PreviewMesh
        EditorGUILayout.BeginHorizontal();
        removeMeshPreviewMeshProp.boolValue = EditorGUILayout.ToggleLeft("Hand preview / HUD (VR only)", removeMeshPreviewMeshProp.boolValue);
        Transform previewMesh = vrclens.GetPreviewMesh();
        EditorGUILayout.ObjectField(previewMesh, typeof(Transform), true);
        EditorGUILayout.EndHorizontal();

        // Display total materials and triangles to be removed
        int totalMaterials = 0;
        int totalTriangles = 0;
        optimizer.CollectMeshesForRemoval(vrclens, out totalMaterials, out totalTriangles);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Total materials to remove: {totalMaterials}");
        EditorGUILayout.LabelField($"Total triangles to remove: {totalTriangles}");

        // Apply changes to the serialized object
        serializedObject.ApplyModifiedProperties();
    }
}
#endif