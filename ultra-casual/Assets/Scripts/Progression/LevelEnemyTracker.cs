using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Attach this to your Level GameObject.
/// It finds all RagdollEnemy under the level hierarchy, tracks them,
/// and keeps counts of alive vs dead enemies.
/// </summary>
[DisallowMultipleComponent]
public class LevelEnemyTracker : MonoBehaviour
{
    [Header("Tracked Enemies (auto-populated)")]
    [Tooltip("All enemies found under this level. Use RefreshEnemiesInLevel() to repopulate.")]
    public List<RagdollEnemy> enemies = new List<RagdollEnemy>();

    [Header("Counts (read-only in inspector)")]
    [SerializeField] private int _totalCount;
    [SerializeField] private int _aliveCount;
    [SerializeField] private int _deadCount;

    public int TotalCount => _totalCount;
    public int AliveCount => _aliveCount;
    public int DeadCount => _deadCount;

    [Header("Events")]
    [Tooltip("Invoked whenever counts change: (alive, dead).")]
    public UnityEvent<int, int> onEnemyCountsChanged;

    [Tooltip("Invoked when an enemy is registered into this level.")]
    public UnityEvent<RagdollEnemy> onEnemyRegistered;

    [Tooltip("Invoked when a tracked enemy dies.")]
    public UnityEvent<RagdollEnemy> onEnemyDied;

    [Tooltip("Invoked when all tracked enemies are dead.")]
    public UnityEvent onAllEnemiesDead;

    // Internal sets for tracking
    private readonly HashSet<RagdollEnemy> _tracked = new HashSet<RagdollEnemy>();
    private readonly HashSet<RagdollEnemy> _dead = new HashSet<RagdollEnemy>();

    private void Awake()
    {
        // Auto-populate on level startup
        RefreshEnemiesInLevel();
    }

    private void OnDestroy()
    {
        UnhookAll();
    }

    // ---------------------------------------------------------
    // Public API
    // ---------------------------------------------------------

    /// <summary>
    /// Clears existing references, then finds and hooks all RagdollEnemy
    /// that are children of this Level GameObject.
    /// Call this again if you respawn or add enemies dynamically.
    /// </summary>
    [ContextMenu("Refresh Enemies In Level")]
    public void RefreshEnemiesInLevel()
    {
        UnhookAll();

        enemies.Clear();
        _tracked.Clear();
        _dead.Clear();

        var found = GetComponentsInChildren<RagdollEnemy>(includeInactive: true);
        foreach (var enemy in found)
        {
            if (enemy == null) continue;
            HookEnemy(enemy);
        }

        RecalculateCounts();
    }

    /// <summary>
    /// Manually register a single enemy (e.g., when spawned at runtime).
    /// </summary>
    public void RegisterEnemy(RagdollEnemy enemy)
    {
        if (enemy == null) return;
        if (_tracked.Contains(enemy)) return;

        HookEnemy(enemy);
        RecalculateCounts();
    }

    /// <summary>
    /// Returns true if the given enemy is known and marked as dead.
    /// </summary>
    public bool IsEnemyDead(RagdollEnemy enemy)
    {
        return enemy != null && _dead.Contains(enemy);
    }

    /// <summary>
    /// Returns true if *all* tracked enemies are dead.
    /// </summary>
    public bool AreAllEnemiesDead()
    {
        return _totalCount > 0 && _deadCount == _totalCount;
    }

    // ---------------------------------------------------------
    // Internal hooking / unhooking
    // ---------------------------------------------------------

    private void HookEnemy(RagdollEnemy enemy)
    {
        if (enemy == null) return;
        if (_tracked.Contains(enemy)) return;

        _tracked.Add(enemy);
        enemies.Add(enemy);
        _dead.Remove(enemy); // ensure it's not in dead set

        // Subscribe to its death event
        enemy.OnDied += HandleEnemyDied;

        onEnemyRegistered?.Invoke(enemy);
    }

    private void UnhookAll()
    {
        foreach (var enemy in _tracked)
        {
            if (enemy != null)
            {
                enemy.OnDied -= HandleEnemyDied;
            }
        }

        _tracked.Clear();
        _dead.Clear();
    }

    private void HandleEnemyDied(RagdollEnemy enemy)
    {
        if (enemy == null) return;

        _dead.Add(enemy);
        RecalculateCounts();

        onEnemyDied?.Invoke(enemy);

        if (AreAllEnemiesDead())
        {
            onAllEnemiesDead?.Invoke();
        }
    }

    private void RecalculateCounts()
    {
        // Remove any destroyed enemies from our lists/sets
        for (int i = enemies.Count - 1; i >= 0; i--)
        {
            if (enemies[i] == null)
            {
                enemies.RemoveAt(i);
            }
        }

        _tracked.RemoveWhere(e => e == null);
        _dead.RemoveWhere(e => e == null);

        _totalCount = enemies.Count;
        _deadCount = 0;
        foreach (var e in _dead)
        {
            if (e != null) _deadCount++;
        }

        _aliveCount = Mathf.Max(0, _totalCount - _deadCount);

        onEnemyCountsChanged?.Invoke(_aliveCount, _deadCount);
    }
}
