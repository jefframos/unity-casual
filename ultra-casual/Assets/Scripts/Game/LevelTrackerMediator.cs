using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks enemies per level and exposes stats via events.
/// No UI references; UI listens to this and does its own thing.
/// </summary>
public class LevelTrackerMediator : MonoBehaviour
{
    public static LevelTrackerMediator Instance { get; private set; }

    // Snapshot struct the UI can consume
    public struct EnemyStatsSnapshot
    {
        public EnemyGrade grade;
        public int total;
        public int dead;

        public EnemyStatsSnapshot(EnemyGrade grade, int total, int dead)
        {
            this.grade = grade;
            this.total = total;
            this.dead = dead;
        }
    }

    /// <summary>
    /// Fired after RefreshLevels() completes, with a full snapshot of all grades.
    /// UI should typically rebuild itself on this.
    /// </summary>
    public event Action<List<EnemyStatsSnapshot>> OnStatsRebuilt;

    /// <summary>
    /// Fired whenever a single enemy-grade's stats change (e.g., enemy registered or died).
    /// </summary>
    public event Action<EnemyGrade, int, int> OnEnemyStatsChanged;

    // All level trackers for current session
    private readonly List<LevelEnemyTracker> _trackers = new();

    // Internal stats
    private readonly Dictionary<EnemyGrade, EnemyStats> _stats = new();

    // NEW: per-refresh death counter (only counts deaths that happened AFTER the last RefreshLevels call)
    private readonly Dictionary<EnemyGrade, int> _deathCountsSinceRefresh = new();

    /// <summary>
    /// Read-only view of death counts since last RefreshLevels().
    /// </summary>
    public IReadOnlyDictionary<EnemyGrade, int> DeathCountsSinceRefresh => _deathCountsSinceRefresh;

    public bool persistentLevels = false;

    private struct EnemyStats
    {
        public int total;
        public int dead;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        // Optional:
        // DontDestroyOnLoad(gameObject);
    }

    private void OnDisable()
    {
        UnhookAllTrackers();
        _stats.Clear();
        _deathCountsSinceRefresh.Clear();
    }

    /// <summary>
    /// Called by GameManager when a new level/run is starting.
    /// Finds all LevelEnemyTracker, hooks events, rebuilds stats and notifies UI.
    /// Also resets the per-refresh death counter.
    /// </summary>
    public void RefreshLevels()
    {
        UnhookAllTrackers();
        _stats.Clear();

        // RESET death counter each time we refresh levels
        _deathCountsSinceRefresh.Clear();

        // Find all trackers in the scene
        var trackers = FindObjectsByType<LevelEnemyTracker>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var tracker in trackers)
        {
            if (tracker == null) continue;
            _trackers.Add(tracker);

            if (!persistentLevels)
            {
                tracker.Reset();
            }

            tracker.onEnemyRegistered.AddListener(OnEnemyRegistered);
            tracker.onEnemyDied.AddListener(OnEnemyDied);

            // Initialize stats from tracker's current enemies
            foreach (var enemy in tracker.enemies)
            {
                if (enemy == null) continue;
                bool isDead = tracker.IsEnemyDead(enemy);
                AddEnemyToStats(enemy, isDead);
            }
        }

        // Emit full snapshot so UI can rebuild itself
        RaiseStatsRebuilt();
    }

    // --------------------------------------------------
    // Internal: trackers & events
    // --------------------------------------------------

    private void UnhookAllTrackers()
    {
        foreach (var tracker in _trackers)
        {
            if (tracker == null) continue;
            tracker.onEnemyRegistered.RemoveListener(OnEnemyRegistered);
            tracker.onEnemyDied.RemoveListener(OnEnemyDied);
        }
        _trackers.Clear();
    }

    private void OnEnemyRegistered(RagdollEnemy enemy)
    {
        if (enemy == null) return;

        AddEnemyToStats(enemy, isDead: false);
        RaiseStatsChangedFor(enemy.grade);
    }

    private void OnEnemyDied(RagdollEnemy enemy)
    {
        if (enemy == null) return;

        var grade = enemy.grade;

        // Update main stats
        if (_stats.TryGetValue(grade, out var stats))
        {
            stats.dead = Mathf.Min(stats.dead + 1, stats.total);
            _stats[grade] = stats;
        }

        // UPDATE per-refresh death counter
        if (!_deathCountsSinceRefresh.TryGetValue(grade, out var deathCount))
        {
            deathCount = 0;
        }
        _deathCountsSinceRefresh[grade] = deathCount + 1;

        RaiseStatsChangedFor(grade);
    }

    private void AddEnemyToStats(RagdollEnemy enemy, bool isDead)
    {
        var grade = enemy.grade;

        if (!_stats.TryGetValue(grade, out var stats))
        {
            stats = new EnemyStats { total = 0, dead = 0 };
        }

        stats.total++;
        if (isDead)
        {
            stats.dead = Mathf.Min(stats.dead + 1, stats.total);
        }

        _stats[grade] = stats;
    }

    public void RaiseStatsRebuilt()
    {
        if (OnStatsRebuilt == null) return;

        var list = new List<EnemyStatsSnapshot>(_stats.Count);
        foreach (var kvp in _stats)
        {
            var grade = kvp.Key;
            var stats = kvp.Value;
            list.Add(new EnemyStatsSnapshot(grade, stats.total, stats.dead));
        }

        OnStatsRebuilt?.Invoke(list);
    }

    private void RaiseStatsChangedFor(EnemyGrade grade)
    {
        if (OnEnemyStatsChanged == null) return;

        if (_stats.TryGetValue(grade, out var stats))
        {
            OnEnemyStatsChanged.Invoke(grade, stats.total, stats.dead);
        }
        else
        {
            OnEnemyStatsChanged.Invoke(grade, 0, 0);
        }
    }

    /// <summary>
    /// Optional helper if you prefer a copy instead of the live dictionary reference.
    /// </summary>
    public Dictionary<EnemyGrade, int> GetDeathCountsSinceRefreshCopy()
    {
        return new Dictionary<EnemyGrade, int>(_deathCountsSinceRefresh);
    }
}
