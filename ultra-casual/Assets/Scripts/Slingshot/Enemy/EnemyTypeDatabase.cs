using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Global database of enemy type definitions. 
/// Put this on a GameObject in a bootstrap scene or in each level.
/// </summary>
public class EnemyTypeDatabase : MonoBehaviour
{
    public static EnemyTypeDatabase Instance { get; private set; }

    [Tooltip("List of all enemy type definitions.")]
    public List<EnemyTypeDefinition> definitions = new();

    private readonly Dictionary<EnemyGrade, EnemyTypeDefinition> _map =
        new Dictionary<EnemyGrade, EnemyTypeDefinition>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        // Optional if you want it across scenes:
        // DontDestroyOnLoad(gameObject);

        BuildMap();
    }

    private void BuildMap()
    {
        _map.Clear();
        foreach (var def in definitions)
        {
            if (def == null) continue;
            if (_map.ContainsKey(def.type))
            {
                Debug.LogWarning(
                    $"[EnemyTypeDatabase] Duplicate definition for {def.type}, using first one.");
                continue;
            }
            _map.Add(def.type, def);
        }
    }

    /// <summary>
    /// Returns the definition for a given enemy type, or null if not found.
    /// </summary>
    public EnemyTypeDefinition GetDefinition(EnemyGrade type)
    {
        _map.TryGetValue(type, out var def);
        return def;
    }
}
