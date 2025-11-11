using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "LevelGrid/Level Grid Data")]
public class LevelGridData : ScriptableObject
{
    [Header("Level")]
    public string levelName = "NewLevel";

    [Header("Grid")]
    public Vector2Int gridSize = new Vector2Int(8, 8);
    [Tooltip("World size per grid cell when baking.")]
    public float cellSize = 1.0f;

    [Serializable]
    public class PlacedItem
    {
        public PlaceableObjectDef def;
        public Vector2Int origin; // bottom-left cell of the item
    }

    [Header("Contents")]
    public List<PlacedItem> placed = new List<PlacedItem>();

    public bool IsInside(Vector2Int p)
    {
        return p.x >= 0 && p.y >= 0 && p.x < gridSize.x && p.y < gridSize.y;
    }

    public void EnsureFits(Vector2Int minInclusive, Vector2Int maxInclusive)
    {
        int needX = Mathf.Max(gridSize.x, maxInclusive.x + 1);
        int needY = Mathf.Max(gridSize.y, maxInclusive.y + 1);
        if (needX != gridSize.x || needY != gridSize.y)
        {
            gridSize = new Vector2Int(needX, needY);
        }
    }
}
