#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class VRCLensResolutionModifier
{
    // Modifies the sensor resolution and anti-aliasing of VRCLens.
    public static bool CopyAndModifyMaterials(VRCLens vrclens, Vector2Int resolution, int msaa, string tempDir)
    {
        RenderTexture depthTex;
        Vector2Int renderTexSize, depthTexSize;
        int renderTexAntiAliasing;
        RenderTexture renderTex = GetRenderTexture(vrclens, out depthTex, out renderTexSize, out depthTexSize, out renderTexAntiAliasing);
        if (renderTex == null || depthTex == null)
        {
            Debug.LogError("[VRCLensResolutionModifier] Cannot find Render Texture or Depth Texture on VRCLens.");
            return false;
        }

        // Copy renderTex and depthTex so we don't modify the originals
        string renderTexPath = AssetDatabase.GetAssetPath(renderTex);
        string depthTexPath = AssetDatabase.GetAssetPath(depthTex);

        string renderTexGUID = AssetDatabase.AssetPathToGUID(renderTexPath);
        string depthTexGUID = AssetDatabase.AssetPathToGUID(depthTexPath);

        string modifiedRenderTexPath = $"{tempDir}/{renderTex.name}_{renderTexGUID}_modified.renderTexture";
        string modifiedDepthTexPath = $"{tempDir}/{depthTex.name}_{depthTexGUID}_modified.renderTexture";

        if (!AssetDatabase.CopyAsset(renderTexPath, modifiedRenderTexPath) || !AssetDatabase.CopyAsset(depthTexPath, modifiedDepthTexPath))
        {
            Debug.LogError("[VRCLensResolutionModifier] Failed to copy Render Texture or Depth Texture.");
            return false;
        }

        RenderTexture modifiedRenderTex = AssetDatabase.LoadAssetAtPath<RenderTexture>(modifiedRenderTexPath);
        RenderTexture modifiedDepthTex = AssetDatabase.LoadAssetAtPath<RenderTexture>(modifiedDepthTexPath);

        if (modifiedRenderTex == null || modifiedDepthTex == null)
        {
            Debug.LogError("[VRCLensResolutionModifier] Failed to load copied Render Texture or Depth Texture.");
            return false;
        }

        Renderer screenOverrideRenderer = GetScreenOverrideRenderer(vrclens);
        if (screenOverrideRenderer == null)
        {
            return false;
        }
        Material camMat = screenOverrideRenderer.sharedMaterials[0];

        // Copy camMat so we don't modify the original
        string camMatPath = AssetDatabase.GetAssetPath(camMat);
        string camMatGUID = AssetDatabase.AssetPathToGUID(camMatPath);
        string modifiedCamMatPath = $"{tempDir}/{camMat.name}_{camMatGUID}_modified.mat";

        if (!AssetDatabase.CopyAsset(camMatPath, modifiedCamMatPath))
        {
            Debug.LogError("[VRCLensResolutionModifier] Failed to copy CamMaterial.");
            return false;
        }

        Material modifiedCamMat = AssetDatabase.LoadAssetAtPath<Material>(modifiedCamMatPath);
        if (modifiedCamMat == null)
        {
            Debug.LogError("[VRCLensResolutionModifier] Failed to load copied CamMaterial.");
            return false;
        }

        // Camera components
        Camera cameraColor = vrclens.GetLensChildCameraColor().GetComponent<Camera>();
        Camera cameraDepth = vrclens.GetLensChildCameraDepth().GetComponent<Camera>();
        Camera cameraColorAvatar = vrclens.GetLensChildCameraColorAvatar().GetComponent<Camera>();
        Camera cameraDepthAvatar = vrclens.GetLensChildCameraDepthAvatar().GetComponent<Camera>();
        // For 3D
        Camera stereoLeftColor = vrclens.GetLensChildStereoLeftColor().GetComponent<Camera>();
        Camera stereoLeftDepth = vrclens.GetLensChildStereoLeftDepth().GetComponent<Camera>();
        Camera stereoRightColor = vrclens.GetLensChildStereoRightColor().GetComponent<Camera>();
        Camera stereoRightDepth = vrclens.GetLensChildStereoRightDepth().GetComponent<Camera>();

        // Set custom resolution
        if (resolution.x > 0 && resolution.y > 0)
        {
            Debug.Log($"[VRCLensResolutionModifier] Setting custom resolution: {resolution.x} x {resolution.y}");
            // Assign the render and depth textures. Both textures must have the same resolution.
            modifiedRenderTex.width = resolution.x;
            modifiedRenderTex.height = resolution.y;
            modifiedDepthTex.width = resolution.x;
            modifiedDepthTex.height = resolution.y;

            // Set aspect ratio
            float camAspectRatio = (float)resolution.x / resolution.y;
            modifiedCamMat.SetFloat("_AspectRatio", camAspectRatio);

            // Does not seem to be necessary for 2D after testing, but left commented here for reference.
            // VRCLens installer always keeps this at 36x20.25 (16:9)
            // If modified to be aspectRatio instead, the camera aspect ratio appears to be the same,
            // but has very slight differences in zoom level it seems.
            // Vector2 sensorSize = new Vector2(36f, 36f / camAspectRatio);
            // cameraColor.sensorSize = sensorSize;
            // cameraDepth.sensorSize = sensorSize;
            // cameraColorAvatar.sensorSize = sensorSize;
            // cameraDepthAvatar.sensorSize = sensorSize;

            // For 3D. VRCLens installer uses 36x(36/aspectRatio) (same as 2D aspect ratio), but this results
            // in a squished image. Leaving it at 16:9 also results in a squished image.
            // We will use half the aspect ratio instead.
            Vector2 sensorSize3D = new Vector2(36f, 36f / (camAspectRatio / 2));
            stereoLeftColor.sensorSize = sensorSize3D;
            stereoLeftDepth.sensorSize = sensorSize3D;
            stereoRightColor.sensorSize = sensorSize3D;
            stereoRightDepth.sensorSize = sensorSize3D;
        }

        // Set MSAA
        if (msaa > 0)
        {
            Debug.Log($"[VRCLensResolutionModifier] Setting MSAA: {msaa}");
            // Only the render texture should have MSAA set
            modifiedRenderTex.antiAliasing = msaa;
        }

        // Replace the original CamMaterial
        Material[] materials = screenOverrideRenderer.sharedMaterials;
        materials[0] = modifiedCamMat;
        screenOverrideRenderer.sharedMaterials = materials;
        Debug.Log($"[VRCLensResolutionModifier] Replaced CamMaterial with modified CamMaterial at {modifiedCamMatPath}.");

        // Set the copied textures on the modified CamMaterial. This must happen after the
        // textures have been modified.
        modifiedCamMat.SetTexture("_RenderTex", modifiedRenderTex);
        modifiedCamMat.SetTexture("_DepthTex", modifiedDepthTex);

        // Replace the render and depth textures on the Camera components
        cameraColor.targetTexture = modifiedRenderTex;
        cameraDepth.targetTexture = modifiedDepthTex;
        cameraColorAvatar.targetTexture = modifiedRenderTex;
        cameraDepthAvatar.targetTexture = modifiedDepthTex;

        stereoLeftColor.targetTexture = modifiedRenderTex;
        stereoLeftDepth.targetTexture = modifiedDepthTex;
        stereoRightColor.targetTexture = modifiedRenderTex;
        stereoRightDepth.targetTexture = modifiedDepthTex;

        Debug.Log($"[VRCLensResolutionModifier] Replaced Render Texture with {modifiedRenderTexPath} and Depth Texture with {modifiedDepthTexPath}.");

        // Save the modified textures to the AssetDatabase
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return true;
    }

    public static Renderer GetScreenOverrideRenderer(VRCLens vrclens)
    {
        Transform screenOverride = vrclens.GetScreenOverride();
        if (screenOverride == null)
        {
            Debug.LogError("[VRCLensResolutionModifier] ScreenOverride is missing.");
            return null;
        }

        Renderer renderer = screenOverride.GetComponent<Renderer>();
        if (renderer == null)
        {
            Debug.LogError("[VRCLensResolutionModifier] Renderer is missing on ScreenOverride.");
            return null;
        }
        if (renderer.sharedMaterials.Length == 0)
        {
            Debug.LogError("[VRCLensResolutionModifier] No materials found on ScreenOverride renderer.");
            return null;
        }

        return renderer;
    }

    public static RenderTexture GetRenderTexture(VRCLens vrclens, out RenderTexture depthTex, out Vector2Int renderTexSize, out Vector2Int depthTexSize, out int renderTexAntiAliasing)
    {
        depthTex = default;
        renderTexSize = default;
        depthTexSize = default;
        renderTexAntiAliasing = default;

        Renderer renderer = GetScreenOverrideRenderer(vrclens);
        if (renderer == null)
        {
            return null;
        }

        Material camMat = renderer.sharedMaterials[0];
        RenderTexture renderTex = camMat.GetTexture("_RenderTex") as RenderTexture;
        depthTex = camMat.GetTexture("_DepthTex") as RenderTexture;
        if (renderTex == null || depthTex == null)
        {
            return null;
        }

        renderTexSize = new Vector2Int(renderTex.width, renderTex.height);
        depthTexSize = new Vector2Int(depthTex.width, depthTex.height);
        renderTexAntiAliasing = renderTex.antiAliasing;
        return renderTex;
    }
}
#endif