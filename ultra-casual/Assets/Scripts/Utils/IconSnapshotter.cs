using System.Collections.Generic;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
[ExecuteAlways]
public class IconSnapshotter : MonoBehaviour
{
    [Header("Camera & Targets")]
    public Camera captureCamera;
    public List<Transform> targets = new List<Transform>();

    [Header("Output")]
    [Tooltip("Icons will be saved to Assets/Resources/<FolderName>/")]
    public string resourcesFolderName = "Icons";
    [Tooltip("Power-of-two size, e.g., 128, 256, 512, 1024.")]
    public int iconSize = 512;
    [Tooltip("Optional filename prefix for all icons.")]
    public string filenamePrefix = "";

    [Header("Framing")]
    [Tooltip("If true, uses an orthographic camera for captures (camera.orthographic must be true).")]
    public bool useOrthographic = false;
    [Tooltip("Extra padding around object bounds (in normalized screen fraction, 0..0.5).")]
    [Range(0f, 0.4f)]
    public float padding = 0.08f;
    [Tooltip("If set, rotate target to this local euler during capture (restored after).")]
    public bool overrideLocalRotation = false;
    public Vector3 localEulerForCapture = new Vector3(0f, 30f, 0f);

    [Header("Isolation")]
    [Tooltip("Temporarily force the target (and its children) to this layer so only it renders.")]
    public bool isolateLayer = true;
    public int isolationLayer = 30; // pick an unused layer (Edit > Project Settings > Tags and Layers)

    [Header("Background")]
    [Tooltip("Clear color used for the capture camera. Alpha will be preserved in PNGs if A=0.")]
    public Color clearColor = new Color(0, 0, 0, 0);

#if UNITY_EDITOR
    [ContextMenu("Snapshot All")]
    public void SnapshotAll()
    {
        if (captureCamera == null)
        {
            Debug.LogError("[IconSnapshotter] No captureCamera assigned.");
            return;
        }

        if (!Mathf.IsPowerOfTwo(iconSize))
        {
            Debug.LogWarning($"[IconSnapshotter] iconSize {iconSize} is not a power of two. Rounding to nearest pow2.");
            iconSize = Mathf.ClosestPowerOfTwo(iconSize);
        }

        if (targets == null || targets.Count == 0)
        {
            Debug.LogWarning("[IconSnapshotter] No targets to capture.");
            return;
        }

        string root = Path.Combine("Assets", "Resources");
        string folder = Path.Combine(root, resourcesFolderName);
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
            Debug.Log($"[IconSnapshotter] Created folder: {folder}");
        }

        // Save camera state to restore later
        var cam = captureCamera;
        var prevRT = cam.targetTexture;
        var prevBG = cam.backgroundColor;
        var prevClearFlags = cam.clearFlags;
        var prevOrtho = cam.orthographic;
        var prevCullingMask = cam.cullingMask;

        cam.backgroundColor = clearColor;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.orthographic = useOrthographic;

        RenderTexture rt = new RenderTexture(iconSize, iconSize, 24, RenderTextureFormat.ARGB32);
        rt.name = "IconSnapshotter_RT";
        rt.useMipMap = false;
        rt.autoGenerateMips = false;
        rt.antiAliasing = 8;

        cam.targetTexture = rt;

        foreach (var t in targets)
        {
            if (t == null) continue;

            // Cache and optionally override rotation
            Quaternion originalLocalRot = t.localRotation;
            if (overrideLocalRotation)
            {
                t.localRotation = Quaternion.Euler(localEulerForCapture);
            }

            // Compute bounds in world space
            if (!TryGetWorldBounds(t, out Bounds worldBounds))
            {
                Debug.LogWarning($"[IconSnapshotter] No renderers found under {t.name}. Skipping.");
                if (overrideLocalRotation) t.localRotation = originalLocalRot;
                continue;
            }

            // Optionally isolate layer
            Dictionary<Transform, int> originalLayers = null;
            if (isolateLayer)
            {
                originalLayers = new Dictionary<Transform, int>(64);
                CacheAndSetLayerRecursive(t, isolationLayer, originalLayers);
                cam.cullingMask = 1 << isolationLayer;
            }

            // Frame the object
            FrameTarget(cam, worldBounds, padding);

            // Render
            RenderTexture.active = rt;
            cam.Render();

            // Read back
            Texture2D tex = new Texture2D(iconSize, iconSize, TextureFormat.ARGB32, false, false);
            tex.ReadPixels(new Rect(0, 0, iconSize, iconSize), 0, 0, false);
            tex.Apply(false, false);

            // Restore layers
            if (isolateLayer && originalLayers != null)
            {
                foreach (var kv in originalLayers)
                {
                    kv.Key.gameObject.layer = kv.Value;
                }
                cam.cullingMask = prevCullingMask;
            }

            // Restore rotation
            if (overrideLocalRotation)
            {
                t.localRotation = originalLocalRot;
            }

            // Save PNG
            var bytes = tex.EncodeToPNG();
            var safeName = MakeSafeFilename(t.name);
            string fileName = string.IsNullOrEmpty(filenamePrefix) ? $"{safeName}.png" : $"{filenamePrefix}_{safeName}.png";
            string fullPath = Path.Combine(folder, fileName);
            File.WriteAllBytes(fullPath, bytes);
            Object.DestroyImmediate(tex);

            Debug.Log($"[IconSnapshotter] Saved {fullPath.Replace("\\", "/")}");
        }

        // Cleanup and restore camera
        cam.targetTexture = prevRT;
        cam.backgroundColor = prevBG;
        cam.clearFlags = prevClearFlags;
        cam.orthographic = prevOrtho;
        cam.cullingMask = prevCullingMask;

        RenderTexture.active = null;
        rt.Release();
        Object.DestroyImmediate(rt);

        AssetDatabase.Refresh();
    }

    private static bool TryGetWorldBounds(Transform root, out Bounds bounds)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        bounds = new Bounds();
        bool hasAny = false;

        foreach (var r in renderers)
        {
            if (r is TrailRenderer) continue; // usually not desired in icons
            if (!hasAny)
            {
                bounds = r.bounds;
                hasAny = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }
        return hasAny;
    }

    private static void FrameTarget(Camera cam, Bounds worldBounds, float pad)
    {
        // Pad bounds
        var ext = worldBounds.extents;
        ext *= (1f + pad * 2f);
        var padded = new Bounds(worldBounds.center, ext * 2f);

        if (cam.orthographic)
        {
            // Fit the largest extent into the orthographic size
            float maxExtent = Mathf.Max(ext.x, ext.y, ext.z);
            // Project bounds to camera space
            Vector3 camFwd = cam.transform.forward;
            Vector3 camPos = padded.center - camFwd * 10f; // start a bit back
            cam.transform.position = camPos;
            cam.transform.LookAt(padded.center);

            float aspect = (float)cam.pixelWidth / cam.pixelHeight;
            // Estimate orthographic size to cover bounds in view
            float verticalSize = Mathf.Max(ext.y, ext.x / aspect, ext.z / aspect);
            cam.orthographicSize = verticalSize * 1.1f;
        }
        else
        {
            // Perspective: place camera so the object fits vertically and horizontally.
            float fovRad = cam.fieldOfView * Mathf.Deg2Rad;
            float aspect = (float)cam.pixelWidth / cam.pixelHeight;

            float radiusY = Mathf.Max(ext.y, 0.0001f);
            float radiusX = Mathf.Max(ext.x, 0.0001f);
            float distanceY = radiusY / Mathf.Tan(fovRad * 0.5f);
            // Horizontal FOV derived from vertical FOV and aspect
            float halfVert = Mathf.Tan(fovRad * 0.5f);
            float halfHorz = Mathf.Atan(halfVert * aspect);
            float distanceX = radiusX / Mathf.Tan(halfHorz);

            float distance = Mathf.Max(distanceX, distanceY) * 1.2f + ext.z; // a smidge farther

            cam.transform.position = padded.center - cam.transform.forward * distance;
            cam.transform.LookAt(padded.center);
            // Near/far planes
            cam.nearClipPlane = Mathf.Max(0.01f, distance - ext.magnitude * 2f);
            cam.farClipPlane = distance + ext.magnitude * 2f + 5f;
        }
    }

    private static void CacheAndSetLayerRecursive(Transform root, int layer, Dictionary<Transform, int> cache)
    {
        if (root == null) return;
        cache[root] = root.gameObject.layer;
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
        {
            CacheAndSetLayerRecursive(root.GetChild(i), layer, cache);
        }
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

#if UNITY_EDITOR
[CustomEditor(typeof(IconSnapshotter))]
public class IconSnapshotterEditor : Editor
{
    private static readonly int[] Pow2Options = new[] { 64, 128, 256, 512, 1024, 2048, 4096 };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(serializedObject.FindProperty("captureCamera"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("targets"), true);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("resourcesFolderName"));
        var iconSizeProp = serializedObject.FindProperty("iconSize");
        int curSize = iconSizeProp.intValue;
        int idx = Mathf.Clamp(System.Array.IndexOf(Pow2Options, curSize), 0, Pow2Options.Length - 1);
        idx = EditorGUILayout.Popup("Icon Size", idx, System.Array.ConvertAll(Pow2Options, i => i.ToString()));
        iconSizeProp.intValue = Pow2Options[idx];
        EditorGUILayout.PropertyField(serializedObject.FindProperty("filenamePrefix"));

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Framing", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("useOrthographic"));
        EditorGUILayout.Slider(serializedObject.FindProperty("padding"), 0f, 0.4f);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("overrideLocalRotation"));
        if (serializedObject.FindProperty("overrideLocalRotation").boolValue)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("localEulerForCapture"));
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Isolation", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("isolateLayer"));
        if (serializedObject.FindProperty("isolateLayer").boolValue)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("isolationLayer"));
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Background", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("clearColor"));

        EditorGUILayout.Space(10);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Snapshot All", GUILayout.Height(32)))
            {
                (target as IconSnapshotter).SnapshotAll();
            }
        }

        serializedObject.ApplyModifiedProperties();
    }
}
#endif
