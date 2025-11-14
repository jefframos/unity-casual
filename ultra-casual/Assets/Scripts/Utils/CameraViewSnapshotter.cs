using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
[ExecuteAlways]
public class CameraViewSnapshotter : MonoBehaviour
{
    [Header("Camera")]
    public Camera captureCamera;

    [Header("Output")]
    [Tooltip("Icons will be saved to Assets/Resources/<FolderName>/")]
    public string resourcesFolderName = "Icons";
    [Tooltip("Square size in pixels (e.g., 128, 256, 512, 1024).")]
    public int iconSize = 512;
    [Tooltip("Optional filename prefix for the icon.")]
    public string filenamePrefix = "";
    [Tooltip("Base filename without extension.")]
    public string snapshotName = "Icon";

    [Header("Background")]
    [Tooltip("If true, temporarily override the camera clear color & flags for the capture.")]
    public bool overrideClear = true;
    [Tooltip("Clear color used for the capture camera. Alpha will be preserved in PNGs if A=0.")]
    public Color clearColor = new Color(0, 0, 0, 0);

#if UNITY_EDITOR
    [ContextMenu("Snapshot View")]
    public void SnapshotView()
    {
        if (captureCamera == null)
        {
            Debug.LogError("[CameraViewSnapshotter] No captureCamera assigned.");
            return;
        }

        if (!Mathf.IsPowerOfTwo(iconSize))
        {
            Debug.LogWarning($"[CameraViewSnapshotter] iconSize {iconSize} is not a power of two. Rounding to nearest pow2.");
            iconSize = Mathf.ClosestPowerOfTwo(iconSize);
        }

        // Setup output folder
        string root = Path.Combine("Assets", "Resources");
        string folder = Path.Combine(root, resourcesFolderName);
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
            Debug.Log($"[CameraViewSnapshotter] Created folder: {folder}");
        }

        var cam = captureCamera;

        // Save camera state
        var prevRT = cam.targetTexture;
        var prevBG = cam.backgroundColor;
        var prevClearFlags = cam.clearFlags;

        // Determine current aspect (fallback to 1 if weird)
        float aspect = cam.aspect;
        if (aspect <= 0.0001f || float.IsNaN(aspect) || float.IsInfinity(aspect))
            aspect = 1f;

        // We render at the camera's aspect ratio, then crop a central square
        int rtWidth = iconSize;
        int rtHeight = iconSize;

        if (aspect >= 1f)
        {
            // Wider than tall: width grows
            rtWidth = Mathf.Max(iconSize, Mathf.RoundToInt(iconSize * aspect));
            rtHeight = iconSize;
        }
        else
        {
            // Taller than wide: height grows
            rtWidth = iconSize;
            rtHeight = Mathf.Max(iconSize, Mathf.RoundToInt(iconSize / aspect));
        }

        RenderTexture rt = new RenderTexture(rtWidth, rtHeight, 24, RenderTextureFormat.ARGB32);
        rt.name = "CameraViewSnapshotter_RT";
        rt.useMipMap = false;
        rt.autoGenerateMips = false;
        rt.antiAliasing = 8;

        cam.targetTexture = rt;

        if (overrideClear)
        {
            cam.backgroundColor = clearColor;
            cam.clearFlags = CameraClearFlags.SolidColor;
        }

        // Render
        RenderTexture.active = rt;
        cam.Render();

        // Crop central square from the rendered view
        int x = Mathf.Max(0, (rtWidth - iconSize) / 2);
        int y = Mathf.Max(0, (rtHeight - iconSize) / 2);
        Rect cropRect = new Rect(x, y, iconSize, iconSize);

        Texture2D tex = new Texture2D(iconSize, iconSize, TextureFormat.ARGB32, false, false);
        tex.ReadPixels(cropRect, 0, 0, false);
        tex.Apply(false, false);

        // Restore camera state
        cam.targetTexture = prevRT;
        if (overrideClear)
        {
            cam.backgroundColor = prevBG;
            cam.clearFlags = prevClearFlags;
        }

        RenderTexture.active = null;
        rt.Release();
        DestroyImmediate(rt);

        // Save PNG
        string safeBaseName = MakeSafeFilename(
            string.IsNullOrEmpty(snapshotName) ? "Icon" : snapshotName
        );

        string fileName = string.IsNullOrEmpty(filenamePrefix)
            ? $"{safeBaseName}.png"
            : $"{filenamePrefix}_{safeBaseName}.png";

        string fullPath = Path.Combine(folder, fileName);
        File.WriteAllBytes(fullPath, tex.EncodeToPNG());
        DestroyImmediate(tex);

        Debug.Log($"[CameraViewSnapshotter] Saved {fullPath.Replace("\\", "/")}");

        AssetDatabase.Refresh();
    }

    private static string MakeSafeFilename(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            s = s.Replace(c.ToString(), "_");
        }
        return s.Trim();
    }
#endif
}
