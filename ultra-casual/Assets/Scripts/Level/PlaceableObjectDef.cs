using UnityEngine;

[CreateAssetMenu(menuName = "LevelGrid/Placeable Object Definition")]
public class PlaceableObjectDef : ScriptableObject
{
    [Header("Identity")]
    public PlaceableBaseName baseName = PlaceableBaseName.WoodBox;  // enum instead of string
    public string objectId;

    [Header("Prefab & Layout")]
    public GameObject prefab;
    public Vector2Int size = new Vector2Int(1, 1);

    public Vector3 prefabOffset = Vector3.zero;

    [Header("Editor Visuals")]
    public Color editorColor = new Color(0.20f, 0.80f, 0.40f, 0.35f);

    private void OnValidate()
    {
        size.x = Mathf.Max(1, size.x);
        size.y = Mathf.Max(1, size.y);
    }
}
