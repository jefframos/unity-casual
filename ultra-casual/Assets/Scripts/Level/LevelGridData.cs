using System;
using System.Collections.Generic;
using UnityEngine;

// Add to LevelGridData.cs
[CreateAssetMenu(menuName = "LevelGrid/Level Grid Data")]
public class LevelGridData : ScriptableObject
{
    [Header("Level")]
    public string levelName = "NewLevel";

    [Header("Grid")]
    public Vector2Int gridSize = new Vector2Int(8, 8);
    public float cellSize = 1.0f;

    [Header("Depth")]
    [Tooltip("Number of Z layers (0..Depth-1).")]
    public int depth = 1;

    [System.Serializable]
    public class PlacedItem
    {
        public PlaceableObjectDef def;
        public Vector2Int origin;
        public int layerZ = 0; // <-- which Z layer this item belongs to
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

    public void EnsureDepth(int zNeeded)
    {
        if (zNeeded >= depth)
        {
            depth = zNeeded + 1;
        }
    }
}
