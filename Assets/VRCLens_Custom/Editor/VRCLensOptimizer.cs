#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

[AddComponentMenu("Scripts/VRCLens Optimizer (VRCLens Custom)")]
public class VRCLensOptimizer : MonoBehaviour, IEditorOnly
{
    public bool removeMeshCameraModel;
    public bool removeMeshPivotAnchorModel;
    public bool removeMeshFocusP;
    public bool removeMeshAuxCopy;
    public bool removeMeshPreviewMesh;

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

    private VRCAvatarDescriptor FindAvatarDescriptor()
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

    public void Optimize()
    {
        Debug.Log($"[VRCLensOptimizer] Running Optimize() for: {gameObject.name}");
        VRCLens vrclens = GetVRCLens();
        if (vrclens == null)
        {
            Debug.LogWarning($"[VRCLensOptimizer] VRCLens not found");
            return;
        }

        int totalMaterials = 0;
        int totalTriangles = 0;
        List<Transform> meshesToRemove = CollectMeshesForRemoval(vrclens, out totalMaterials, out totalTriangles);

        Debug.Log($"[VRCLensOptimizer] Total materials to be removed: {totalMaterials}");
        Debug.Log($"[VRCLensOptimizer] Total triangles to be removed: {totalTriangles}");

        foreach (Transform mesh in meshesToRemove)
        {
            Debug.Log($"[VRCLensOptimizer] Removing mesh components from {mesh.name}");
            RemoveMeshComponents(mesh);
        }
    }

    public List<Transform> CollectMeshesForRemoval(VRCLens vrclens, out int totalMaterials, out int totalTriangles)
    {
        List<Transform> meshesToRemove = new List<Transform>();

        if (removeMeshCameraModel)
        {
            Transform cameraModel = vrclens.GetCameraModel();
            if (cameraModel != null)
            {
                meshesToRemove.Add(cameraModel);
            }
        }

        if (removeMeshPivotAnchorModel)
        {
            Transform pivotAnchorModel = vrclens.GetPivotAnchorModel();
            if (pivotAnchorModel != null)
            {
                meshesToRemove.Add(pivotAnchorModel);
            }
        }

        if (removeMeshFocusP)
        {
            Transform focusP = vrclens.GetFocusP();
            if (focusP != null)
            {
                meshesToRemove.Add(focusP);
            }
        }

        if (removeMeshAuxCopy)
        {
            Transform auxCopy = vrclens.GetAuxCopy();
            if (auxCopy != null)
            {
                meshesToRemove.Add(auxCopy);
            }
        }

        if (removeMeshPreviewMesh)
        {
            Transform previewMesh = vrclens.GetPreviewMesh();
            if (previewMesh != null)
            {
                meshesToRemove.Add(previewMesh);
            }
        }

        totalMaterials = 0;
        totalTriangles = 0;
        foreach (Transform mesh in meshesToRemove)
        {
            totalMaterials += GetMaterialCount(mesh);
            totalTriangles += GetTriangleCount(mesh);
        }

        return meshesToRemove;
    }

    private static void RemoveMeshComponents(Transform target)
    {
        MeshRenderer meshRenderer = GetMeshRenderer(target);
        if (meshRenderer != null)
        {
            DestroyImmediate(meshRenderer);
        }

        MeshFilter meshFilter = GetMeshFilter(target);
        if (meshFilter != null)
        {
            DestroyImmediate(meshFilter);
        }
    }

    private static MeshRenderer GetMeshRenderer(Transform target)
    {
        return target.GetComponent<MeshRenderer>();
    }

    private static MeshFilter GetMeshFilter(Transform target)
    {
        return target.GetComponent<MeshFilter>();
    }

    public static int GetMaterialCount(Transform target)
    {
        MeshRenderer meshRenderer = GetMeshRenderer(target);
        if (meshRenderer != null)
        {
            return meshRenderer.sharedMaterials.Length;
        }
        return 0;
    }

    public static int GetTriangleCount(Transform target)
    {
        MeshFilter meshFilter = GetMeshFilter(target);
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            return meshFilter.sharedMesh.triangles.Length / 3;
        }
        return 0;
    }
}
#endif