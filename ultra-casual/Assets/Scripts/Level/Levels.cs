using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class AreaDefinition
{
    [Header("Meta")]
    public string areaName;
    public Sprite areaSprite;
    [TextArea]
    public string description;

    [Header("Levels in this Area")]
    public List<GameObject> levelPrefabs = new List<GameObject>();
}

[CreateAssetMenu(menuName = "Game/Levels", fileName = "Levels")]
public class Levels : ScriptableObject
{
    public AreaDefinition[] areas;

    public int TotalLevels
    {
        get
        {
            if (areas == null) return 0;
            int total = 0;
            foreach (var area in areas)
            {
                if (area == null || area.levelPrefabs == null) continue;
                total += area.levelPrefabs.Count;
            }
            return total;
        }
    }

    /// <summary>
    /// Get prefab by area + local level index.
    /// Returns null if out of range.
    /// </summary>
    public GameObject GetLevelByArea(
        int areaIndex,
        int levelIndexInArea,
        out int globalIndex,
        out AreaDefinition areaDef)
    {
        globalIndex = -1;
        areaDef = null;

        if (areas == null ||
            areaIndex < 0 ||
            areaIndex >= areas.Length)
        {
            return null;
        }

        areaDef = areas[areaIndex];
        if (areaDef == null ||
            areaDef.levelPrefabs == null ||
            levelIndexInArea < 0 ||
            levelIndexInArea >= areaDef.levelPrefabs.Count)
        {
            return null;
        }

        // Compute global index (sum all levels before this area, then add local index).
        int global = 0;
        for (int i = 0; i < areaIndex; i++)
        {
            var a = areas[i];
            if (a != null && a.levelPrefabs != null)
            {
                global += a.levelPrefabs.Count;
            }
        }

        global += levelIndexInArea;
        globalIndex = global;

        return areaDef.levelPrefabs[levelIndexInArea];
    }

    /// <summary>
    /// Get prefab by global index (0..TotalLevels-1).
    /// Returns null if out of range.
    /// </summary>
    public GameObject GetLevelByGlobalIndex(
        int globalIndex,
        out AreaDefinition areaDef,
        out int areaIndex,
        out int levelIndexInArea)
    {
        areaDef = null;
        areaIndex = -1;
        levelIndexInArea = -1;

        if (areas == null || globalIndex < 0)
        {
            return null;
        }

        int running = 0;

        for (int i = 0; i < areas.Length; i++)
        {
            var a = areas[i];
            if (a == null || a.levelPrefabs == null || a.levelPrefabs.Count == 0)
                continue;

            int count = a.levelPrefabs.Count;
            int next = running + count;

            if (globalIndex >= running && globalIndex < next)
            {
                int localIndex = globalIndex - running;
                areaDef = a;
                areaIndex = i;
                levelIndexInArea = localIndex;
                return a.levelPrefabs[localIndex];
            }

            running = next;
        }

        return null;
    }
}
