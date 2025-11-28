#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public class PrefabMaterialExtractorWindow : EditorWindow
{
    [Header("Prefab (root must have MeshRenderer)")]
    [SerializeField]
    private GameObject _prefab;

    [Header("Target Folder (Project folder asset)")]
    [Tooltip("Folder where material assets will be created/updated.")]
    [SerializeField]
    private DefaultAsset _targetFolder;

    [Header("Options")]
    [SerializeField]
    private bool _logVerbose = true;

    [MenuItem("Tools/Prefab Material Extractor")]
    public static void ShowWindow()
    {
        var window = GetWindow<PrefabMaterialExtractorWindow>("Prefab Material Extractor");
        window.minSize = new Vector2(420f, 200f);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Prefab Material Extractor", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        _prefab = (GameObject)EditorGUILayout.ObjectField(
            "Prefab",
            _prefab,
            typeof(GameObject),
            false
        );

        _targetFolder = (DefaultAsset)EditorGUILayout.ObjectField(
            "Target Folder",
            _targetFolder,
            typeof(DefaultAsset),
            false
        );

        _logVerbose = EditorGUILayout.Toggle("Log Verbose", _logVerbose);

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(_prefab == null || _targetFolder == null))
        {
            if (GUILayout.Button("Process Prefab Materials"))
            {
                ProcessPrefab();
            }
        }

        EditorGUILayout.HelpBox(
            "Drop a prefab asset above (not a scene instance). " +
            "The root GameObject must have a MeshRenderer. " +
            "Materials will be copied/updated into the chosen folder and " +
            "the prefab will be rewired to use those material assets.",
            MessageType.Info
        );
    }

    private void ProcessPrefab()
    {
        if (_prefab == null)
        {
            Debug.LogError("[PrefabMaterialExtractor] No prefab assigned.");
            return;
        }

        if (_targetFolder == null)
        {
            Debug.LogError("[PrefabMaterialExtractor] No target folder assigned.");
            return;
        }

        string prefabPath = AssetDatabase.GetAssetPath(_prefab);

        if (string.IsNullOrEmpty(prefabPath))
        {
            Debug.LogError("[PrefabMaterialExtractor] The assigned object is not a prefab asset.");
            return;
        }

        bool isPrefabAsset = PrefabUtility.IsPartOfPrefabAsset(_prefab);

        if (!isPrefabAsset)
        {
            Debug.LogError("[PrefabMaterialExtractor] Please assign a prefab asset (from the Project), not a scene instance.");
            return;
        }

        string folderPath = AssetDatabase.GetAssetPath(_targetFolder);

        if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
        {
            Debug.LogError($"[PrefabMaterialExtractor] Target folder path is invalid: {folderPath}");
            return;
        }

        GameObject prefabRoot = null;

        try
        {
            prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);

            if (prefabRoot == null)
            {
                Debug.LogError("[PrefabMaterialExtractor] Failed to load prefab contents.");
                return;
            }

            MeshRenderer meshRenderer = prefabRoot.GetComponent<MeshRenderer>();

            if (meshRenderer == null)
            {
                Debug.LogError("[PrefabMaterialExtractor] Root of prefab does not have a MeshRenderer.");
                return;
            }

            Material[] sharedMaterials = meshRenderer.sharedMaterials;

            if (sharedMaterials == null || sharedMaterials.Length == 0)
            {
                Debug.LogWarning("[PrefabMaterialExtractor] MeshRenderer has no materials assigned.");
            }

            for (int i = 0; i < sharedMaterials.Length; i++)
            {
                Material sourceMat = sharedMaterials[i];

                if (sourceMat == null)
                {
                    if (_logVerbose)
                    {
                        Debug.Log($"[PrefabMaterialExtractor] Slot {i} is null, skipping.");
                    }

                    continue;
                }

                // Build a safe file name from the material name
                string safeName = MakeSafeFileName(sourceMat.name);
                string matPath = Path.Combine(folderPath, safeName + ".mat");
                matPath = matPath.Replace("\\", "/");

                if (_logVerbose)
                {
                    Debug.Log($"[PrefabMaterialExtractor] Processing material '{sourceMat.name}' -> '{matPath}'.");
                }

                // Check if material already exists at path
                Material targetMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

                if (targetMat == null)
                {
                    // Create new asset
                    targetMat = new Material(sourceMat);
                    AssetDatabase.CreateAsset(targetMat, matPath);

                    if (_logVerbose)
                    {
                        Debug.Log($"[PrefabMaterialExtractor] Created new material asset at {matPath}.");
                    }
                }
                else
                {
                    // Overwrite existing asset data
                    EditorUtility.CopySerialized(sourceMat, targetMat);

                    if (_logVerbose)
                    {
                        Debug.Log($"[PrefabMaterialExtractor] Updated existing material asset at {matPath}.");
                    }
                }

                // Reassign to renderer
                sharedMaterials[i] = targetMat;
            }

            meshRenderer.sharedMaterials = sharedMaterials;

            // Save changes back to original prefab
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[PrefabMaterialExtractor] Done. Prefab materials have been extracted/updated and reassigned.");
        }
        finally
        {
            if (prefabRoot != null)
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }
    }

    private static string MakeSafeFileName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "Material";
        }

        // Strip invalid characters for file names
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c.ToString(), "_");
        }

        // Optional: remove Unity's "(Instance)" suffix if present
        name = name.Replace("(Instance)", string.Empty).Trim();

        if (string.IsNullOrEmpty(name))
        {
            name = "Material";
        }

        return name;
    }
}
#endif
