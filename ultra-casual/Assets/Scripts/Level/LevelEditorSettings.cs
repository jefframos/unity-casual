using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "LevelGrid/Editor Settings")]
public class LevelEditorSettings : ScriptableObject
{
    [Header("Asset Paths")]
    [Tooltip("Where LevelGridData assets are saved.")]
    public string levelsFolder = "Assets/Levels/Data";

    [Tooltip("Where baked prefabs are saved.")]
    public string prefabsFolder = "Assets/Levels/Prefabs";

    [Header("Palette")]
    public List<PlaceableObjectDef> palette = new List<PlaceableObjectDef>();
}
