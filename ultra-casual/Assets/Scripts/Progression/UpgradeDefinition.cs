using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Upgrade Definition")]
public class UpgradeDefinition : ScriptableObject
{
    public UpgradeType type;

    [Header("Identity")]
    public string upgradeId;
    public Sprite icon;
    public Color color = Color.white;
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
        {
            return null;
        }

        int current = 0;
        foreach (var step in steps)
        {
            if (step == null || step.levels == null)
            {
                continue;
            }

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
        {
            cost += step.addedCost;
        }
        return Mathf.RoundToInt(cost);
    }

    /// <summary>
    /// Returns true if the given global level index is at or beyond the maximum available level.
    /// </summary>
    public bool IsMaxLevel(int globalLevel)
    {
        // TotalLevels gives the number of available levels (0-based index means last valid is TotalLevels - 1)
        return globalLevel >= TotalLevels - 1;
    }

    public float GetValueForLevel(int globalLevel)
    {
        var data = GetLevelData(globalLevel, out _);
        if (data == null)
        {
            return baseValue;
        }

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
                {
                    if (s != null && s.levels != null)
                    {
                        total += s.levels.Length;
                    }
                }
            }
            return total;
        }
    }

#if UNITY_EDITOR
    /// <summary>Apply each step's globalAddPerIndex to its levels' addValue (overwrite): level[i].addValue = (i+1) * globalAddPerIndex.</summary>
    public void ApplyGlobalAddsToLevels()
    {
        if (steps == null)
        {
            return;
        }

        var stepCount = 0;
        foreach (var step in steps)
        {
            if (step == null || step.levels == null)
            {
                continue;
            }

            for (int i = 0; i < step.levels.Length; i++)
            {
                var ld = step.levels[i];
                if (ld == null)
                {
                    continue;
                }

                var added = step.useLastValueAsFirst && stepCount > 0 ? steps[stepCount - 1].levels[^1].addValue : 0f;
                ld.addValue = (i + 1) * step.globalAddPerIndex + added;
            }
            stepCount++;
        }
    }

    /// <summary>Revert all levels' addValue to 1.</summary>
    public void RevertAllAddValuesToOne()
    {
        if (steps == null)
        {
            return;
        }

        foreach (var step in steps)
        {
            if (step == null || step.levels == null)
            {
                continue;
            }

            foreach (var ld in step.levels)
            {
                if (ld == null)
                {
                    continue;
                }

                ld.addValue = 1f;
            }
        }
    }
#endif
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
    public float extraValue;

    [Header("Auto Add (Global)")]
    [Tooltip("If 1, levels' addValue becomes 1,2,3... If 0, no auto add.")]
    public float globalAddPerIndex = 0f;
    public bool useLastValueAsFirst = true;

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
