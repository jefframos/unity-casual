#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlaceableObjectDef))]
public class PlaceableObjectDefEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var baseNameProp = serializedObject.FindProperty("baseName");
        EditorGUILayout.PropertyField(baseNameProp, new GUIContent("Base Name"));
        // Show fields in the order you described: base id, prefab, size
        EditorGUILayout.PropertyField(serializedObject.FindProperty("objectId"), new GUIContent("Base Id"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("prefab"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("size"));

        EditorGUILayout.Space();

        // Show the rest of your normal fields
        EditorGUILayout.PropertyField(serializedObject.FindProperty("prefabOffset"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("editorColor"));

        EditorGUILayout.Space();

        // Apply Size button (only enabled if prefab is assigned)
        var prefabProp = serializedObject.FindProperty("prefab");
        GUI.enabled = prefabProp.objectReferenceValue != null;

        if (GUILayout.Button("Apply Size (Modify Prefab)"))
        {
            ApplySize_modifyExisting();
        }

        // Button 2: save as new prefab if it doesn't exist
        if (GUILayout.Button("Apply Size & Save As NEW Prefab"))
        {
            ApplySize_saveAsNew();
        }

        GUI.enabled = true;

        serializedObject.ApplyModifiedProperties();
    }
    private bool PrepareSizeAndId(PlaceableObjectDef def, out int sizeX, out int sizeY, out string newId)
    {
        sizeX = Mathf.Max(1, def.size.x);
        sizeY = Mathf.Max(1, def.size.y);
        def.size = new Vector2Int(sizeX, sizeY);
        def.editorColor = def.baseName.ToColor();



        string baseNameString = def.baseName.ToId(); // enum → string
        newId = $"{baseNameString}{sizeX}x{sizeY}";

        Undo.RecordObject(def, "Apply Placeable Object Size");
        def.objectId = newId;
        EditorUtility.SetDirty(def);

        return true;
    }

    private void ApplyToPrefabRoot(GameObject root, int sizeX, int sizeY, string newId, float mass)
    {
        // Rename root
        Undo.RecordObject(root, "Rename Prefab Root");
        root.name = newId;
        EditorUtility.SetDirty(root);


        Rigidbody rb = root.GetComponent<Rigidbody>();
        if (rb)
        {
            rb.mass = mass * sizeX * sizeY;
        }

        // BoxCollider on root
        BoxCollider box = root.GetComponent<BoxCollider>();
        if (box != null)
        {
            Undo.RecordObject(box, "Resize BoxCollider");

            Vector3 colliderSize = box.size;
            colliderSize.x = sizeX;
            colliderSize.y = sizeY; // keep z
            box.size = colliderSize;

            Vector3 center = box.center;
            center.y = sizeY * 0.5f;
            box.center = center;

            EditorUtility.SetDirty(box);
        }
        else
        {
            Debug.LogWarning("[PlaceableObjectDefEditor] Prefab root has no BoxCollider.");
        }

        // First child scale
        if (root.transform.childCount > 0)
        {
            Transform child = root.transform.GetChild(0);
            Undo.RecordObject(child, "Scale First Child");

            Vector3 p = child.localPosition;
            p.y = sizeY * 0.5f;
            child.localPosition = p;

            Vector3 s = child.localScale;
            s.x = sizeX * 100f; // e.g. 3 -> 300
            s.y = sizeY * 100f; // e.g. 1 -> 100
            child.localScale = s;

            EditorUtility.SetDirty(child);
        }
        else
        {
            Debug.LogWarning("[PlaceableObjectDefEditor] Prefab root has no children to scale.");
        }
    }

    // ------------------------- BUTTON 1: MODIFY EXISTING -------------------------

    private void ApplySize_modifyExisting()
    {
        var def = (PlaceableObjectDef)target;

        if (def.prefab == null)
        {
            Debug.LogWarning("[PlaceableObjectDefEditor] No prefab assigned.");
            return;
        }

        if (!PrepareSizeAndId(def, out int sizeX, out int sizeY, out string newId))
            return;

        string prefabPath = AssetDatabase.GetAssetPath(def.prefab);
        if (string.IsNullOrEmpty(prefabPath))
        {
            Debug.LogWarning("[PlaceableObjectDefEditor] Prefab is not an asset on disk.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            ApplyToPrefabRoot(root, sizeX, sizeY, newId, def.baseName.ToBaseMass());

            // Save back into the same asset
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

            // Rename the asset file itself
            AssetDatabase.RenameAsset(prefabPath, newId);

            // Keep reference name in sync (optional)
            def.prefab.name = newId;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[PlaceableObjectDefEditor] Modified existing prefab to {newId} ({sizeX}x{sizeY}).");
    }

    // ------------------------- BUTTON 2: SAVE AS NEW -------------------------

    private void ApplySize_saveAsNew()
    {
        var def = (PlaceableObjectDef)target;

        if (def.prefab == null)
        {
            Debug.LogWarning("[PlaceableObjectDefEditor] No prefab assigned.");
            return;
        }

        if (!PrepareSizeAndId(def, out int sizeX, out int sizeY, out string newId))
            return;

        string originalPath = AssetDatabase.GetAssetPath(def.prefab);
        if (string.IsNullOrEmpty(originalPath))
        {
            Debug.LogWarning("[PlaceableObjectDefEditor] Prefab is not an asset on disk.");
            return;
        }

        string folder = Path.GetDirectoryName(originalPath);
        string newPath = Path.Combine(folder, newId + ".prefab").Replace("\\", "/");

        // If prefab with that name already exists, don't overwrite
        if (AssetDatabase.LoadAssetAtPath<GameObject>(newPath) != null)
        {
            Debug.LogWarning($"[PlaceableObjectDefEditor] Prefab '{newId}' already exists at {newPath}. Not creating a new one.");
            return;
        }

        // Load original prefab contents, apply changes, then save as NEW asset
        GameObject root = PrefabUtility.LoadPrefabContents(originalPath);
        GameObject newPrefabAsset = null;

        try
        {
            ApplyToPrefabRoot(root, sizeX, sizeY, newId, def.baseName.ToBaseMass());

            newPrefabAsset = PrefabUtility.SaveAsPrefabAsset(root, newPath);

            if (newPrefabAsset == null)
            {
                Debug.LogError($"[PlaceableObjectDefEditor] Failed to save new prefab at {newPath}");
                return;
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        // Update the ScriptableObject to point to the NEW prefab
        Undo.RecordObject(def, "Assign New Prefab");
        def.prefab = newPrefabAsset;
        def.prefab.name = newId;
        EditorUtility.SetDirty(def);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[PlaceableObjectDefEditor] Created NEW prefab {newId} at {newPath} ({sizeX}x{sizeY}) and assigned it.");
    }
}
#endif