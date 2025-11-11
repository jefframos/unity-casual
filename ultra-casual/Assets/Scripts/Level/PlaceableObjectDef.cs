using UnityEngine;

[CreateAssetMenu(menuName = "LevelGrid/Placeable Object Definition")]
public class PlaceableObjectDef : ScriptableObject
{
    [Header("Identity")]
    public string objectId;

    [Header("Prefab & Layout")]
    public GameObject prefab;
    [Tooltip("Size in grid cells (X width, Y height).")]
    public Vector2Int size = new Vector2Int(1, 1);

    [Tooltip("Optional local offset applied when baking.")]
    public Vector3 prefabOffset = Vector3.zero;
    // PlaceableObjectDef.cs
    [Header("Editor Visuals")]
    public Color editorColor = new Color(0.20f, 0.80f, 0.40f, 0.35f);

    private void OnValidate()
    {
        if (size.x < 1) size.x = 1;
        if (size.y < 1) size.y = 1;
    }
}
