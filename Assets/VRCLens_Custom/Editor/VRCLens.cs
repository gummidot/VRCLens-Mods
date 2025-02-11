using UnityEngine;

public class VRCLens
{
    private Transform transform;

    public VRCLens(Transform transform)
    {
        this.transform = transform;
    }

    public Transform Transform => transform;

    public static class Paths
    {
        public const string ScreenOverride = "CamScreen/ScreenOverride";
        public const string AuxCopy = "CamScreen/AuxCopy";
        public const string CameraModel = "WorldC/CamPickup/CamBase/CamObject/CameraModel/VRCLensDefault";
        public const string PreviewMesh = "WorldC/CamPickupAlways/PreviewBase/PreviewMesh";
        public const string FocusP = "WorldC/FocusPickup/FocusObject/FocusP";
        public const string PivotAnchorModel = "WorldC/PivotPickup/PBase/PObject/PDroneBase/AnchorModel";
    }

    public static Transform FindVRCLens(Transform parent)
    {
        foreach (Transform child in parent)
        {
            if (IsVRCLens(child))
            {
                return child;
            }
            Transform result = FindVRCLens(child);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    public static bool IsVRCLens(Transform transform)
    {
        // Simple check for a critical component of VRCLens that shouldn't be renamed
        return transform != null && transform.Find(Paths.ScreenOverride) != null;
    }

    public Transform GetAuxCopy()
    {
        return transform.Find(Paths.AuxCopy);
    }

    public Transform GetCameraModel()
    {
        return transform.Find(Paths.CameraModel);
    }

    public Transform GetPreviewMesh()
    {
        return transform.Find(Paths.PreviewMesh);
    }

    public Transform GetFocusP()
    {
        return transform.Find(Paths.FocusP);
    }

    public Transform GetPivotAnchorModel()
    {
        return transform.Find(Paths.PivotAnchorModel);
    }
}