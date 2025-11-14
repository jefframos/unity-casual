using System.Collections.Generic;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
[ExecuteAlways]
public class CameraViewIconSnapshotter : MonoBehaviour
{
    [Header("Camera")]
    public Camera captureCamera;

    [Header("Targets")]
    [Tooltip("Each element will get its own isolated icon.")]
    public List<Transform> targets = new List<Transform>();

    [Header("Output")]
    [Tooltip("Icons will be saved to Assets/Resources/<FolderName>/")]
    public string resourcesFolderName = "Icons";
    [Tooltip("Square size in pixels (e.g., 128, 256, 512, 1024).")]
    public int iconSize = 512;
    [Tooltip("Optional filename prefix for all icons.")]
    public string filenamePrefix = "";

    [Header("Isolation")]
    [Tooltip("If true, will temporarily set target and its children to this layer so only it renders.")]
    public bool isolateLayer = true;
    [Tooltip("Layer used for isolation. Use an otherwise unused layer.")]
    public int isolationLayer = 30;

    [Header("Animation")]
    [Tooltip("If true, tries to find an Animator under each target and evaluates it at the first frame before the snapshot.")]
    public bool evaluateAnimatorFirstFrame = false;

    [Header("Background")]
    [Tooltip("If true, temporarily override the camera clear color & flags for the capture.")]
    public bool overrideClear = true;
    [Tooltip("Clear color used for the capture camera. Alpha will be preserved in PNGs if A=0.")]
    public Color clearColor = new Color(0, 0, 0, 0);

#if UNITY_EDITOR
    [ContextMenu("Snapshot All")]
    public void SnapshotAll()
    {
        if (captureCamera == null)
        {
            Debug.LogError("[CameraViewIconSnapshotter] No captureCamera assigned.");
            return;
        }

        if (!Mathf.IsPowerOfTwo(iconSize))
        {
            Debug.LogWarning($"[CameraViewIconSnapshotter] iconSize {iconSize} is not a power of two. Rounding to nearest pow2.");
            iconSize = Mathf.ClosestPowerOfTwo(iconSize);
        }

        if (targets == null || targets.Count == 0)
        {
            Debug.LogWarning("[CameraViewIconSnapshotter] No targets to capture.");
            return;
        }

        // Setup output folder
        string root = Path.Combine("Assets", "Resources");
        string folder = Path.Combine(root, resourcesFolderName);
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
            Debug.Log($"[CameraViewIconSnapshotter] Created folder: {folder}");
        }

        var cam = captureCamera;

        // Save camera state
        var prevRT = cam.targetTexture;
        var prevBG = cam.backgroundColor;
        var prevClearFlags = cam.clearFlags;
        var prevMask = cam.cullingMask;

        // Determine RT size from camera aspect, then we crop a central square
        float aspect = cam.aspect;
        if (aspect <= 0.0001f || float.IsNaN(aspect) || float.IsInfinity(aspect))
            aspect = 1f;

        int rtWidth = iconSize;
        int rtHeight = iconSize;

        if (aspect >= 1f)
        {
            // Wider than tall
            rtWidth = Mathf.Max(iconSize, Mathf.RoundToInt(iconSize * aspect));
            rtHeight = iconSize;
        }
        else
        {
            // Taller than wide
            rtWidth = iconSize;
            rtHeight = Mathf.Max(iconSize, Mathf.RoundToInt(iconSize / aspect));
        }

        RenderTexture rt = new RenderTexture(rtWidth, rtHeight, 24, RenderTextureFormat.ARGB32);
        rt.name = "CameraViewIconSnapshotter_RT";
        rt.useMipMap = false;
        rt.autoGenerateMips = false;
        rt.antiAliasing = 8;

        cam.targetTexture = rt;

        if (overrideClear)
        {
            cam.backgroundColor = clearColor;
            cam.clearFlags = CameraClearFlags.SolidColor;
        }

        foreach (var t in targets)
        {
            if (t == null) continue;

            // Quick sanity: does this hierarchy even have renderers?
            if (!HasAnyRenderer(t))
            {
                Debug.LogWarning($"[CameraViewIconSnapshotter] No renderers found under {t.name}. Skipping.");
                continue;
            }

            // --- NEW: evaluate animator at first frame, if requested ---
            Animator animator = null;
            bool prevAnimEnabled = false;
            float prevAnimSpeed = 1f;
            AnimatorUpdateMode prevAnimUpdateMode = AnimatorUpdateMode.Normal;
            bool animatorTouched = false;

            if (evaluateAnimatorFirstFrame)
            {
                animator = t.GetComponentInChildren<Animator>(true);
                if (animator != null)
                {
                    animatorTouched = true;
                    prevAnimEnabled = animator.enabled;
                    prevAnimSpeed = animator.speed;
                    prevAnimUpdateMode = animator.updateMode;

                    animator.enabled = true;
                    animator.speed = 0f;
                    animator.updateMode = AnimatorUpdateMode.UnscaledTime;

                    // Reset to default pose
                    animator.Rebind();

                    // Force the default state to time 0 (layer 0)
                    animator.Play(0, 0, 0f);
                    animator.Update(0f);
                }
            }

            // Optionally isolate layer for this target
            Dictionary<Transform, int> originalLayers = null;
            if (isolateLayer)
            {
                originalLayers = new Dictionary<Transform, int>(64);
                CacheAndSetLayerRecursive(t, isolationLayer, originalLayers);
                cam.cullingMask = 1 << isolationLayer;
            }

            // Important: we do NOT move or rotate the camera or the object.
            // We just render exactly what the camera currently sees.

            RenderTexture.active = rt;
            cam.Render();

            // Crop central square from the rendered view
            int x = Mathf.Max(0, (rtWidth - iconSize) / 2);
            int y = Mathf.Max(0, (rtHeight - iconSize) / 2);
            Rect cropRect = new Rect(x, y, iconSize, iconSize);

            Texture2D tex = new Texture2D(iconSize, iconSize, TextureFormat.ARGB32, false, false);
            tex.ReadPixels(cropRect, 0, 0, false);
            tex.Apply(false, false);

            // Restore layers for this target
            if (isolateLayer && originalLayers != null)
            {
                foreach (var kv in originalLayers)
                {
                    kv.Key.gameObject.layer = kv.Value;
                }
                cam.cullingMask = prevMask;
            }

            // Restore animator state if we touched it
            if (animatorTouched && animator != null)
            {
                animator.speed = prevAnimSpeed;
                animator.updateMode = prevAnimUpdateMode;
                animator.enabled = prevAnimEnabled;
            }

            // Save PNG
            string safeName = MakeSafeFilename(t.name);
            string fileName = string.IsNullOrEmpty(filenamePrefix)
                ? $"{safeName}.png"
                : $"{filenamePrefix}_{safeName}.png";

            string fullPath = Path.Combine(folder, fileName);
            File.WriteAllBytes(fullPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            Debug.Log($"[CameraViewIconSnapshotter] Saved {fullPath.Replace("\\", "/")}");
        }

        // Restore camera state
        cam.targetTexture = prevRT;
        if (overrideClear)
        {
            cam.backgroundColor = prevBG;
            cam.clearFlags = prevClearFlags;
        }
        cam.cullingMask = prevMask;

        RenderTexture.active = null;
        rt.Release();
        Object.DestroyImmediate(rt);

        AssetDatabase.Refresh();
    }

    private static bool HasAnyRenderer(Transform root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r is TrailRenderer) continue;
            return true;
        }
        return false;
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
[CustomEditor(typeof(CameraViewIconSnapshotter))]
public class CameraViewIconSnapshotterEditor : Editor
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
        int idx = System.Array.IndexOf(Pow2Options, curSize);
        if (idx < 0) idx = 0;
        idx = EditorGUILayout.Popup("Icon Size", idx, System.Array.ConvertAll(Pow2Options, i => i.ToString()));
        iconSizeProp.intValue = Pow2Options[idx];

        EditorGUILayout.PropertyField(serializedObject.FindProperty("filenamePrefix"));

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Isolation", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("isolateLayer"));
        if (serializedObject.FindProperty("isolateLayer").boolValue)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("isolationLayer"));
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Animation", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("evaluateAnimatorFirstFrame"));

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Background", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("overrideClear"));
        if (serializedObject.FindProperty("overrideClear").boolValue)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("clearColor"));
        }

        EditorGUILayout.Space(10);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Snapshot All", GUILayout.Height(32)))
            {
                (target as CameraViewIconSnapshotter).SnapshotAll();
            }
        }

        serializedObject.ApplyModifiedProperties();
    }
}
#endif
