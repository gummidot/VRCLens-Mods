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
        public const string LensChild = "WorldC/CamPickup/CamBase/CamObject/LensC/LensParent/LensChild";
        public const string PreviewMesh = "WorldC/CamPickupAlways/PreviewBase/PreviewMesh";
        public const string FocusP = "WorldC/FocusPickup/FocusObject/FocusP";
        public const string PivotAnchorModel = "WorldC/PivotPickup/PBase/PObject/PDroneBase/AnchorModel";
        public const string WorldC = "WorldC";

        // LensChild children
        public const string LensChildCameraColor = "Camera_Color";
        public const string LensChildCameraColorAvatar = "Camera_ColorAvatar";
        public const string LensChildCameraDepth = "Camera_Depth";
        public const string LensChildCameraDepthAvatar = "Camera_DepthAvatar";
        public const string LensChildStereoLeftColor = "Stereo/Left/CamLeft_Color";
        public const string LensChildStereoLeftDepth = "Stereo/Left/CamLeft_Depth";
        public const string LensChildStereoRightColor = "Stereo/Right/CamRight_Color";
        public const string LensChildStereoRightDepth = "Stereo/Right/CamRight_Depth";
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

    public Transform GetScreenOverride()
    {
        return transform.Find(Paths.ScreenOverride);
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

    public Transform GetLensChild()
    {
        return transform.Find(Paths.LensChild);
    }

    public Transform GetLensChildCameraColor()
    {
        return GetLensChild().Find(Paths.LensChildCameraColor);
    }

    public Transform GetLensChildCameraColorAvatar()
    {
        return GetLensChild().Find(Paths.LensChildCameraColorAvatar);
    }

    public Transform GetLensChildCameraDepth()
    {
        return GetLensChild().Find(Paths.LensChildCameraDepth);
    }

    public Transform GetLensChildCameraDepthAvatar()
    {
        return GetLensChild().Find(Paths.LensChildCameraDepthAvatar);
    }

    public Transform GetLensChildStereoLeftColor()
    {
        return GetLensChild().Find(Paths.LensChildStereoLeftColor);
    }

    public Transform GetLensChildStereoLeftDepth()
    {
        return GetLensChild().Find(Paths.LensChildStereoLeftDepth);
    }

    public Transform GetLensChildStereoRightColor()
    {
        return GetLensChild().Find(Paths.LensChildStereoRightColor);
    }

    public Transform GetLensChildStereoRightDepth()
    {
        return GetLensChild().Find(Paths.LensChildStereoRightDepth);
    }
}