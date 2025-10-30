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

    public bool TryUpgrade(UpgradeType type)
    {
        if (!_defs.TryGetValue(type, out var def))
            return false;

        int current = GetLevel(type);
        int cost = def.GetCostForLevel(current);

        if (coins < cost)
            return false;

        coins -= cost;
        _levels[type] = current + 1;
        return true;
    }

    public UpgradeDefinition GetDefinition(UpgradeType type)
    {
        _defs.TryGetValue(type, out var def);
        return def;
    }

    public float GetValue(UpgradeType type)
    {
        if (!_defs.TryGetValue(type, out var def))
            return 1f;

        int level = GetLevel(type);
        return def.GetValueForLevel(level);
    }

    public int GetCost(UpgradeType type)
    {
        if (!_defs.TryGetValue(type, out var def))
            return 0;

        return def.GetCostForLevel(GetLevel(type));
    }

    // Convenience properties
    public float SlingshotForce => GetValue(UpgradeType.SLINGSHOT);
    public float MoneyMultiplier => GetValue(UpgradeType.COIN);
    public float RampStrength => GetValue(UpgradeType.RAMP);

    // --- UpgradeSystem additions (public debug helpers) ---

    /// <summary>Force set the level for an upgrade (debug/test only).</summary>
    public void SetLevel(UpgradeType type, int level)
    {
        if (!_defs.ContainsKey(type)) return;
        _levels[type] = Mathf.Max(0, level);
    }

    /// <summary>Decrease level by one (debug/test only).</summary>
    public void DecreaseLevel(UpgradeType type)
    {
        if (!_levels.TryGetValue(type, out var lvl)) return;
        _levels[type] = Mathf.Max(0, lvl - 1);
    }

    /// <summary>Increase level by one without cost (debug/test only).</summary>
    public void IncreaseLevelForce(UpgradeType type)
    {
        if (!_levels.TryGetValue(type, out var lvl)) return;
        _levels[type] = lvl + 1;
    }

    /// <summary>Add coins to the player wallet (debug/test only).</summary>
    public void AddCoins(int amount)
    {
        coins = Mathf.Max(0, coins + amount);
    }

    /// <summary>Remove coins from the player wallet (debug/test only).</summary>
    public void RemoveCoins(int amount)
    {
        coins = Mathf.Max(0, coins - amount);
    }

    /// <summary>Reset all upgrade levels to zero (debug/test only).</summary>
    public void ResetAllLevels()
    {
        var keys = new List<UpgradeType>(_levels.Keys);
        foreach (var k in keys) _levels[k] = 0;
    }

}
