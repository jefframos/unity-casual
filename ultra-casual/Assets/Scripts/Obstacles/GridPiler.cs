using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class GridPiler : MonoBehaviour
{
    [Header("Targets")]
    public Transform parent;              // Where to place instances (defaults to this.transform)
    public GameObject prefab;             // Prefab to instantiate

    [Header("Pattern (Columns across X, Rows stacked up Y)")]
    [Min(1)] public int columns = 2;
    [Min(1)] public int rows = 8;

    [Header("Spacing")]
    public bool usePrefabSizeForSpacing = true;
    [Tooltip("Used only if 'usePrefabSizeForSpacing' is false.")]
    public Vector2 manualSpacingXZ = new Vector2(1f, 1f);   // X spacing (width), Z spacing (depth) if you need it
    [Tooltip("Vertical spacing (Y). If auto, taken from prefab height.")]
    public float manualSpacingY = 1f;

    [Range(0f, 1f)]
    [Tooltip("Extra gap as a fraction of prefab size (auto-spacing only). Example: 0.05 = +5% gap.")]
    public float paddingFraction = 0.05f;

    [Header("Offsets / Anchor")]
    [Tooltip("Local offset applied to the BOTTOM layer (Y).")]
    public float bottomYOffset = 0f;
    [Tooltip("Local XZ offset applied to the whole grid (left edge at 0 by default).")]
    public Vector2 gridOffsetXZ = Vector2.zero;
    public bool centerHorizontally = false; // centers columns around the parent's X

    [Header("Generation")]
    public string groupName = "GridPiler_Generated";
    public bool clearBeforeGenerate = true;

#if UNITY_EDITOR
    // --- Editor-time helper: Generate ---
    public void Generate()
    {
        if (prefab == null)
        {
            Debug.LogWarning("[GridPiler] No prefab assigned.");
            return;
        }

        var root = parent != null ? parent : transform;

        // Optional clean
        if (clearBeforeGenerate)
        {
            ClearGenerated();
        }

        // Find or create a group container under 'root'
        Transform group = root.Find(groupName);
        if (group == null)
        {
            var go = new GameObject(groupName);
            Undo.RegisterCreatedObjectUndo(go, "Create Grid Group");
            group = go.transform;
            group.SetParent(root, false);
            group.localPosition = Vector3.zero;
            group.localRotation = Quaternion.identity;
            group.localScale = Vector3.one;
        }

        // Determine spacing from prefab bounds or manual
        var b = GetPrefabBounds(prefab);
        Vector3 step = Vector3.one;
        if (usePrefabSizeForSpacing)
        {
            var padX = b.size.x * paddingFraction;
            var padY = b.size.y * paddingFraction;

            step.x = Mathf.Max(0.0001f, b.size.x + padX);
            step.y = Mathf.Max(0.0001f, b.size.y + padY);
        }
        else
        {
            step.x = Mathf.Max(0.0001f, manualSpacingXZ.x);
            step.y = Mathf.Max(0.0001f, manualSpacingY);
        }

        // Horizontal centering (around local X = 0)
        float totalWidth = (columns - 1) * step.x;
        float xOrigin = centerHorizontally ? -0.5f * totalWidth : 0f;

        // Base local offset
        Vector3 baseLocal = new Vector3(gridOffsetXZ.x, bottomYOffset, gridOffsetXZ.y);

        // Instantiate
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                Vector3 localPos = baseLocal;
                localPos.x += xOrigin + c * step.x;
                localPos.y += r * step.y;

                var instance = PrefabUtility.InstantiatePrefab(prefab, group) as GameObject;
                if (instance == null)
                {
                    Debug.LogError("[GridPiler] Failed to instantiate prefab.");
                    continue;
                }

                Undo.RegisterCreatedObjectUndo(instance, "Instantiate Grid Item");

                var t = instance.transform;
                t.localPosition = localPos;
                t.localRotation = Quaternion.identity;
                t.localScale = Vector3.one;
                instance.name = $"{prefab.name}_c{c}_r{r}";
            }
        }

        EditorUtility.SetDirty(group.gameObject);
        EditorUtility.SetDirty(this);
    }

    // --- Editor-time helper: Clear ---
    public void ClearGenerated()
    {
        var root = parent != null ? parent : transform;
        var group = root.Find(groupName);
        if (group == null)
        {
            return;
        }

        // Destroy all children under the group, then the group
        var toDestroy = group.Cast<Transform>().Select(t => t.gameObject).ToList();
        foreach (var go in toDestroy)
        {
            Undo.DestroyObjectImmediate(go);
        }
        Undo.DestroyObjectImmediate(group.gameObject);
    }

    // --- Bounds utility ---
    private static Bounds GetPrefabBounds(GameObject prefabGO)
    {
        // Try BoxCollider first
        var bc = prefabGO.GetComponentInChildren<BoxCollider>();
        if (bc != null)
        {
            var b = new Bounds(bc.transform.TransformPoint(bc.center), Vector3.zero);
            b.Encapsulate(bc.bounds);
            // Convert to local-size approximation by removing transform scale/rotation:
            // Simpler: just use world 'bounds.size' as a measure for spacing
            return new Bounds(Vector3.zero, bc.bounds.size);
        }

        // Try any Renderer bounds union
        var renderers = prefabGO.GetComponentsInChildren<Renderer>(true);
        if (renderers != null && renderers.Length > 0)
        {
            var b = new Bounds(renderers[0].bounds.center, Vector3.zero);
            foreach (var r in renderers)
            {
                b.Encapsulate(r.bounds);
            }
            return new Bounds(Vector3.zero, b.size);
        }

        // Fallback
        return new Bounds(Vector3.zero, Vector3.one);
    }
#endif

    // Gizmos (scene preview)
    private void OnDrawGizmosSelected()
    {
#if UNITY_EDITOR
        var root = parent != null ? parent : transform;
        if (prefab == null || root == null)
        {
            return;
        }

        var b = new Bounds(Vector3.zero, Vector3.one);
        if (usePrefabSizeForSpacing)
        {
            b = new Bounds(Vector3.zero, GetPrefabBounds(prefab).size);
        }
        else
        {
            b = new Bounds(Vector3.zero, new Vector3(manualSpacingXZ.x, manualSpacingY, 1f));
        }

        float padX = usePrefabSizeForSpacing ? b.size.x * paddingFraction : 0f;
        float padY = usePrefabSizeForSpacing ? b.size.y * paddingFraction : 0f;

        float stepX = Mathf.Max(0.0001f, usePrefabSizeForSpacing ? (b.size.x + padX) : manualSpacingXZ.x);
        float stepY = Mathf.Max(0.0001f, usePrefabSizeForSpacing ? (b.size.y + padY) : manualSpacingY);

        float totalWidth = (columns - 1) * stepX;
        float xOrigin = centerHorizontally ? -0.5f * totalWidth : 0f;

        Vector3 baseLocal = new Vector3(gridOffsetXZ.x, bottomYOffset, gridOffsetXZ.y);

        Gizmos.matrix = root.localToWorldMatrix;
        Gizmos.color = new Color(0f, 0.6f, 1f, 0.5f);

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                Vector3 localPos = baseLocal;
                localPos.x += xOrigin + c * stepX;
                localPos.y += r * stepY;

                var size = new Vector3(b.size.x, b.size.y, b.size.z > 0f ? b.size.z : 0.1f);
                Gizmos.DrawWireCube(localPos, size);
            }
        }
#endif
    }
}
