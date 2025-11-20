using System;
using System.Collections.Generic;
using UnityEngine;

public class UpgradeSystem : MonoBehaviour
{
    public static UpgradeSystem Instance { get; private set; }

    [Header("Player Currency")]
    public int coins = 0;

    [Header("Available Upgrades")]
    public List<UpgradeDefinition> upgradeDefinitions = new();

    // runtime: enum -> current level
    private readonly Dictionary<UpgradeType, int> _levels = new();
    private readonly Dictionary<UpgradeType, UpgradeDefinition> _defs = new();

    // ---------------------------
    // Events / Actions
    // ---------------------------

    /// <summary>Fired after a successful purchase.
    /// Args: (type, newGlobalLevel, costSpent)</summary>
    public Action<UpgradeType, int, int> OnUpgradePurchased;

    /// <summary>Fired when a purchase crosses into a new step/tier.
    /// Args: (type, newStepIndex)</summary>
    public Action<UpgradeType, int> OnReachedNextStep;

    /// <summary>Fired when at max level. Called when attempting to buy at max,
    /// and also when a successful purchase lands on the last level.
    /// Args: (type, currentOrFinalLevel)</summary>
    public Action<UpgradeType, int> OnReachedMaxLevel;

    private void Awake()
    {
        gameObject.transform.SetParent(null);
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        _defs.Clear();
        _levels.Clear();

        foreach (var def in upgradeDefinitions)
        {
            if (def == null) continue;
            _defs[def.type] = def;
            if (!_levels.ContainsKey(def.type))
                _levels[def.type] = 0;
        }
    }

    // ------------------------------------------------------
    // Main API
    // ------------------------------------------------------

    public int GetLevel(UpgradeType type)
    {
        return _levels.TryGetValue(type, out int lvl) ? lvl : 0;
    }

    /// <summary>
    /// Attempts to buy the next level for the upgrade type.
    /// Returns true if purchased.
    /// Emits:
    /// - OnUpgradePurchased on success,
    /// - OnReachedNextStep if the purchase crosses into a new step,
    /// - OnReachedMaxLevel if already at max OR the purchase lands you on the last level.
    /// </summary>
    public bool TryUpgrade(UpgradeType type)
    {
        if (!_defs.TryGetValue(type, out var def) || def == null)
            return false;

        int totalLevels = def.TotalLevels;
        if (totalLevels <= 0)
        {
            // No data: treat as max
            OnReachedMaxLevel?.Invoke(type, 0);
            return false;
        }

        int current = GetLevel(type);
        int lastIndex = totalLevels - 1;

        // Already at / beyond max?
        if (current >= lastIndex)
        {
            OnReachedMaxLevel?.Invoke(type, current);
            return false;
        }

        int cost = def.GetCostForLevel(current);
        if (coins < cost)
            return false;

        // Compute step before purchase
        int prevStep = GetStepIndexAtLevel(def, current);

        // Purchase
        coins -= cost;
        int newLevel = current + 1;
        _levels[type] = newLevel;

        OnUpgradePurchased?.Invoke(type, newLevel, cost);

        // Check if crossed into a new step
        int newStep = GetStepIndexAtLevel(def, newLevel);
        if (newStep != prevStep)
        {
            OnReachedNextStep?.Invoke(type, newStep);
        }

        // If we just landed on last level, ping max
        if (newLevel >= lastIndex)
        {
            OnReachedMaxLevel?.Invoke(type, newLevel);
        }

        return true;
    }

    public UpgradeDefinition GetDefinition(UpgradeType type)
    {
        _defs.TryGetValue(type, out var def);
        return def;
    }

    public float GetValue(UpgradeType type)
    {
        if (!_defs.TryGetValue(type, out var def) || def == null)
            return 1f;

        int level = GetLevel(type);
        // Value method is clamped in UpgradeDefinition; safe for display/runtime.
        return def.GetValueForLevel(level);
    }

    public int GetCost(UpgradeType type)
    {
        if (!_defs.TryGetValue(type, out var def) || def == null)
            return 0;

        return def.GetCostForLevel(GetLevel(type));
    }

    // Convenience properties
    public float SlingshotForce => GetValue(UpgradeType.SLINGSHOT);
    public float MoneyMultiplier => GetValue(UpgradeType.COIN);
    public float RampStrength => GetValue(UpgradeType.RAMP);

    // --- Extras / Helpers ---

    /// <summary>Total levels for a definition (0 if not found).</summary>
    public int GetTotalLevels(UpgradeType type)
    {
        return _defs.TryGetValue(type, out var def) && def != null ? def.TotalLevels : 0;
    }

    /// <summary>Returns current step index for this upgrade type (0-based). -1 if no data.</summary>
    public int GetCurrentStepIndex(UpgradeType type)
    {
        if (!_defs.TryGetValue(type, out var def) || def == null) return -1;
        return GetStepIndexAtLevel(def, GetLevel(type));
    }

    public UpgradeStepData GetCurrentStepData(UpgradeType type)
    {
        if (!_defs.TryGetValue(type, out var def) || def == null) return null;
        var id = GetStepIndexAtLevel(def, GetLevel(type));

        return def.steps[id];
    }

    /// <summary>Utility to calculate which step a global level belongs to (0-based). -1 if invalid or no steps.</summary>
    public static int GetStepIndexAtLevel(UpgradeDefinition def, int globalLevel)
    {
        if (def == null || def.steps == null || def.steps.Length == 0) return -1;

        int cursor = 0;
        for (int s = 0; s < def.steps.Length; s++)
        {
            var step = def.steps[s];
            int len = (step?.levels?.Length) ?? 0;
            if (len <= 0)
            {
                // empty step; keep scanning
            }
            else if (globalLevel < cursor + len)
            {
                return s;
            }
            cursor += len;
        }

        // Out of range → clamp to last step if level >= total (optional):
        // return def.steps.Length - 1;

        return -1;
    }

    // --- UpgradeSystem additions (public debug helpers) ---

    /// <summary>Force set the level for an upgrade (debug/test only). Clamped to valid range if a definition exists.</summary>
    public void SetLevel(UpgradeType type, int level)
    {
        if (_defs.TryGetValue(type, out var def) && def != null)
        {
            int maxIdx = Math.Max(0, def.TotalLevels - 1);
            _levels[type] = Mathf.Clamp(level, 0, maxIdx);
        }
        else
        {
            _levels[type] = Mathf.Max(0, level);
        }
    }

    /// <summary>Decrease level by one (debug/test only).</summary>
    public void DecreaseLevel(UpgradeType type)
    {
        if (!_levels.TryGetValue(type, out var lvl)) return;
        _levels[type] = Mathf.Max(0, lvl - 1);
    }

    /// <summary>Increase level by one without cost (debug/test only). Clamped to max if definition exists.</summary>
    public void IncreaseLevelForce(UpgradeType type)
    {
        int lvl = GetLevel(type);

        if (_defs.TryGetValue(type, out var def) && def != null)
        {
            int maxIdx = Math.Max(0, def.TotalLevels - 1);
            _levels[type] = Mathf.Min(lvl + 1, maxIdx);
        }
        else
        {
            _levels[type] = lvl + 1;
        }
    }

    /// <summary>Add coins to the player wallet (debug/test only).</summary>
    public void AddCoins(int amount)
    {
        coins = Mathf.Max(0, coins + amount);

        ProgressionManager.Instance.SetCoins(coins);
    }

    /// <summary>Remove coins from the player wallet (debug/test only).</summary>
    public void RemoveCoins(int amount)
    {
        coins = Mathf.Max(0, coins - amount);

        ProgressionManager.Instance.SetCoins(coins);
    }

    /// <summary>Reset all upgrade levels to zero (debug/test only).</summary>
    public void ResetAllLevels()
    {
        var keys = new List<UpgradeType>(_levels.Keys);
        foreach (var k in keys) _levels[k] = 0;
    }
}
