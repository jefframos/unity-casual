using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Upgrade Definition")]
public class UpgradeDefinition : ScriptableObject
{
    public UpgradeType type;
    [Header("Identity")]
    public string upgradeId;
    public Sprite icon;
    public GameObject worldPrefab;

    [Header("Economy")]
    public int baseCost = 100;
    public float costMultiplier = 1.3f;

    [Header("Values")]
    public float baseValue = 1f;

    [Header("Steps (Tiers)")]
    [Tooltip("Each step can have its own icon, prefab, cost addition, and array of levels.")]
    public UpgradeStepData[] steps = Array.Empty<UpgradeStepData>();

    // ------------------------------------------------------

    /// <summary>Returns the LevelData for a given global level index (0-based).</summary>
    public LevelData GetLevelData(int globalLevel, out UpgradeStepData parentStep)
    {
        parentStep = null;
        if (steps == null || steps.Length == 0)
            return null;

        int current = 0;
        foreach (var step in steps)
        {
            if (step == null || step.levels == null) continue;

            if (globalLevel < current + step.levels.Length)
            {
                int local = globalLevel - current;
                parentStep = step;
                return step.levels[Mathf.Clamp(local, 0, step.levels.Length - 1)];
            }

            current += step.levels.Length;
        }

        // fallback to last step
        parentStep = steps[^1];
        return parentStep.levels.Length > 0 ? parentStep.levels[^1] : null;
    }

    /// <summary>Base cost multiplied by costMultiplier^level + step addedCost.</summary>
    public int GetCostForLevel(int globalLevel)
    {
        var levelData = GetLevelData(globalLevel, out var step);
        float cost = baseCost * Mathf.Pow(costMultiplier, globalLevel);
        if (step != null)
            cost += step.addedCost;
        return Mathf.RoundToInt(cost);
    }

    public float GetValueForLevel(int globalLevel)
    {
        var data = GetLevelData(globalLevel, out _);
        if (data == null)
            return baseValue;

        // base + add, then multiply
        return (baseValue + data.addValue) * data.multiplyValue;
    }

    public int TotalLevels
    {
        get
        {
            int total = 0;
            if (steps != null)
            {
                foreach (var s in steps)
                    if (s != null && s.levels != null)
                        total += s.levels.Length;
            }
            return total;
        }
    }
}

[Serializable]
public class UpgradeStepData
{
    [Tooltip("Label or description for this step.")]
    public string name;

    [Header("Visuals")]
    public Sprite icon;
    public GameObject worldPrefab;

    [Header("Economy Modifier")]
    [Tooltip("Extra cost added to all levels within this step.")]
    public int addedCost;

    [Header("Per-Level Data")]
    public LevelData[] levels;
}

[Serializable]
public class LevelData
{
    [Tooltip("Add this value to the base before multiplying.")]
    public float addValue;
    [Tooltip("Multiply the result by this factor.")]
    public float multiplyValue = 1f;
}
