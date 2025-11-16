using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Attach this to your Level GameObject.
/// It finds all RagdollEnemy under the level hierarchy, tracks them,
/// keeps counts of alive vs dead enemies, and manages grade-based steps.
/// </summary>
[DisallowMultipleComponent]
public class LevelEnemyTracker : MonoBehaviour
{
    // --------------------------------------------------------------------
    // Static debug toggle
    // --------------------------------------------------------------------

    public static bool DebugLogsEnabled = false;

    private void Log(string message)
    {
        if (DebugLogsEnabled)
        {
            Debug.Log($"[LevelEnemyTracker] {message}", this);
        }
    }

    // --------------------------------------------------------------------
    // Snapshot types
    // --------------------------------------------------------------------

    [Serializable]
    public class LevelGradeSnapshot
    {
        public EnemyGrade grade;
        public int total;
        public int dead;
        public int alive;
        public bool isCurrent;
        public bool isCleared;
    }

    [Serializable]
    public class LevelSnapshot
    {
        public bool hasActiveStep;
        public int currentStepIndex;
        public EnemyGrade? currentGrade;

        public int totalGrades;
        public int totalEnemies;
        public int totalDead;
        public int totalAlive;

        /// <summary>
        /// True if the current grade is fully cleared and there is a
        /// next grade to go to.
        /// </summary>
        public bool canAdvanceToNextStep;

        public List<LevelGradeSnapshot> grades = new List<LevelGradeSnapshot>();
    }

    // --------------------------------------------------------------------
    // Inspector fields
    // --------------------------------------------------------------------

    [Header("Tracked Enemies (auto-populated)")]
    [Tooltip("All enemies found under this level. Use RefreshEnemiesInLevel() to repopulate.")]
    public List<RagdollEnemy> enemies = new List<RagdollEnemy>();

    [Header("Counts (read-only in inspector)")]
    [SerializeField] private int _totalCount;
    [SerializeField] private int _aliveCount;
    [SerializeField] private int _deadCount;

    public int TotalCount
    {
        get { return _totalCount; }
    }

    public int AliveCount
    {
        get { return _aliveCount; }
    }

    public int DeadCount
    {
        get { return _deadCount; }
    }

    [Header("Events")]
    [Tooltip("Invoked whenever counts change: (alive, dead).")]
    public UnityEvent<int, int> onEnemyCountsChanged;

    [Tooltip("Invoked when an enemy is registered into this level.")]
    public UnityEvent<RagdollEnemy> onEnemyRegistered;

    [Tooltip("Invoked when a tracked enemy dies.")]
    public UnityEvent<RagdollEnemy> onEnemyDied;

    [Tooltip("Invoked when all tracked enemies are dead.")]
    public UnityEvent onAllEnemiesDead;

    [Header("Step / Grade Progression")]
    [Tooltip("Invoked when a new step starts: (stepIndex, grade). stepIndex is 0-based.")]
    public UnityEvent<int, EnemyGrade> onStepStarted;

    [Tooltip("Invoked when the last grade step has been completed.")]
    public UnityEvent onAllStepsCompleted;

    // --------------------------------------------------------------------
    // Internal tracking
    // --------------------------------------------------------------------

    private readonly HashSet<RagdollEnemy> _tracked = new HashSet<RagdollEnemy>();
    private readonly HashSet<RagdollEnemy> _dead = new HashSet<RagdollEnemy>();

    // Grade / step management
    private readonly Dictionary<EnemyGrade, List<RagdollEnemy>> _enemiesByGrade =
        new Dictionary<EnemyGrade, List<RagdollEnemy>>();

    private readonly List<EnemyGrade> _gradeOrder = new List<EnemyGrade>();

    [SerializeField] private int _currentStepIndex = -1;
    [SerializeField] private bool _hasActiveStep = false;

    public int CurrentStepIndex
    {
        get { return _currentStepIndex; }
    }

    public bool HasActiveStep
    {
        get { return _hasActiveStep; }
    }

    public EnemyGrade? CurrentGrade
    {
        get
        {
            if (!_hasActiveStep)
            {
                return null;
            }

            if (_currentStepIndex < 0 || _currentStepIndex >= _gradeOrder.Count)
            {
                return null;
            }

            return _gradeOrder[_currentStepIndex];
        }
    }

    // --------------------------------------------------------------------
    // Unity lifecycle
    // --------------------------------------------------------------------

    private void Awake()
    {
        Log("Awake: Refreshing enemies and building grade buckets.");
        RefreshEnemiesInLevel();
        BuildGradeBuckets();
        Log($"Awake: enemies={enemies.Count}, grades={_gradeOrder.Count}");
    }

    private void OnDestroy()
    {
        Log("OnDestroy: UnhookAll.");
        UnhookAll();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        Log("OnValidate: Refreshing enemies and building grade buckets.");
        RefreshEnemiesInLevel();
        BuildGradeBuckets();
    }
#endif

    // --------------------------------------------------------------------
    // Public API - core tracking
    // --------------------------------------------------------------------

    /// <summary>
    /// Clears existing references, then finds and hooks all RagdollEnemy
    /// that are children of this Level GameObject.
    /// Call this again if you respawn or add enemies dynamically.
    /// </summary>
    [ContextMenu("Refresh Enemies In Level")]
    public void RefreshEnemiesInLevel()
    {
        Log("RefreshEnemiesInLevel: Unhooking all and re-scanning children.");

        UnhookAll();

        enemies.Clear();
        _tracked.Clear();
        _dead.Clear();

        var found = GetComponentsInChildren<RagdollEnemy>(includeInactive: true);
        Log($"RefreshEnemiesInLevel: Found {found.Length} RagdollEnemy under this level.");

        foreach (var enemy in found)
        {
            if (enemy == null)
            {
                continue;
            }

            HookEnemy(enemy);
        }

        RecalculateCounts();
        Log($"RefreshEnemiesInLevel: After hook, enemies={enemies.Count}, tracked={_tracked.Count}, dead={_dead.Count}");
    }

    /// <summary>
    /// Manually register a single enemy (e.g., when spawned at runtime).
    /// </summary>
    public void RegisterEnemy(RagdollEnemy enemy)
    {
        if (enemy == null)
        {
            return;
        }

        if (_tracked.Contains(enemy))
        {
            Log($"RegisterEnemy: Enemy {enemy.name} already tracked.");
            return;
        }

        Log($"RegisterEnemy: {enemy.name}");
        HookEnemy(enemy);
        RecalculateCounts();
        BuildGradeBuckets();
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

    /// <summary>
    /// Get a read-only view of enemies belonging to a specific grade.
    /// Returns an empty array if this grade is not present in the level.
    /// </summary>
    public IReadOnlyList<RagdollEnemy> GetEnemiesOfGrade(EnemyGrade grade)
    {
        if (_enemiesByGrade.TryGetValue(grade, out var list))
        {
            return list;
        }

        return Array.Empty<RagdollEnemy>();
    }

    /// <summary>
    /// Sets all enemy GameObjects inactive (regardless of grade or alive/dead).
    /// Use this before starting steps.
    /// </summary>
    [ContextMenu("Hide All Enemies")]
    public void HideAllEnemies()
    {
        Log("HideAllEnemies: Disabling all enemy GameObjects.");
        foreach (var enemy in enemies)
        {
            if (enemy == null)
            {
                continue;
            }

            if (enemy.gameObject.activeSelf)
            {
                Log($"  Hiding enemy {enemy.name}");
            }

            enemy.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Shows all enemies that are still alive (gameObject.SetActive(true)).
    /// Dead ones (already killed) stay whatever state they are in.
    /// </summary>
    [ContextMenu("Show All Alive Enemies")]
    public void ShowAllAliveEnemies()
    {
        Log("ShowAllAliveEnemies: Enabling all alive enemy GameObjects.");
        foreach (var enemy in enemies)
        {
            if (enemy == null)
            {
                continue;
            }

            if (_dead.Contains(enemy))
            {
                Log($"  Skipping dead enemy {enemy.name}");
                continue;
            }

            if (!enemy.gameObject.activeSelf)
            {
                Log($"  Showing alive enemy {enemy.name}");
            }

            enemy.gameObject.SetActive(true);
        }
    }

    // --------------------------------------------------------------------
    // Public API - step / grade based level flow
    // --------------------------------------------------------------------

    /// <summary>
    /// Builds grade buckets, hides all enemies, and starts from the first grade step.
    /// Step 0 will show all alive enemies of the first grade (by enum order).
    /// </summary>
    [ContextMenu("Start Level Steps")]
    public void StartLevelSteps()
    {
        Log("StartLevelSteps: Building buckets and hiding all enemies, then starting at step 0.");
        BuildGradeBuckets();
        HideAllEnemies();

        if (_gradeOrder.Count == 0)
        {
            Log("StartLevelSteps: No grades found. Nothing to start.");
            _currentStepIndex = -1;
            _hasActiveStep = false;
            return;
        }

        _currentStepIndex = 0;
        _hasActiveStep = true;
        Log($"StartLevelSteps: Starting step index 0, grade={_gradeOrder[0]}");
        ShowEnemiesForCurrentStep();
    }

    /// <summary>
    /// Same as StartLevelSteps(), but returns all enemy GameObjects that
    /// became active for the first step.
    /// </summary>
    public List<GameObject> StartLevelStepsAndGetNewEnemies()
    {
        Log("StartLevelStepsAndGetNewEnemies: Building buckets and hiding all enemies, then starting at step 0.");
        BuildGradeBuckets();
        HideAllEnemies();

        var result = new List<GameObject>();

        if (_gradeOrder.Count == 0)
        {
            Log("StartLevelStepsAndGetNewEnemies: No grades found. Nothing to start.");
            _currentStepIndex = -1;
            _hasActiveStep = false;
            return result;
        }

        _currentStepIndex = 0;
        _hasActiveStep = true;

        Log($"StartLevelStepsAndGetNewEnemies: Starting step index 0, grade={_gradeOrder[0]}");
        result = ShowEnemiesForCurrentStepInternal();
        Log($"StartLevelStepsAndGetNewEnemies: Newly activated enemies count={result.Count}");
        return result;
    }

    /// <summary>
    /// Manually attempt to advance to the next step (next grade).
    /// Will only advance if the current grade is fully cleared.
    /// Returns true if it actually advanced, false otherwise.
    /// </summary>
    public bool AdvanceToNextStep()
    {
        List<GameObject> _ = null;
        return AdvanceToNextStepInternal(out _);
    }

    /// <summary>
    /// RESUME logic:
    /// - If no step has started yet, behaves like StartLevelStepsAndGetNewEnemies().
    /// - If a step is active and its grade is fully cleared, advances to the next step
    ///   and returns the GameObjects for the newly activated enemies of that new step.
    /// - If a step is active but cannot advance, it just re-shows the current step's
    ///   alive enemies and returns an empty list.
    /// </summary>
    public List<GameObject> ResumeLevelAndTryAdvance()
    {
        Log("ResumeLevelAndTryAdvance: Checking whether to start fresh or advance.");
        var result = new List<GameObject>();

        // No grades at all
        if (_gradeOrder.Count == 0)
        {
            Log("ResumeLevelAndTryAdvance: No grades found. Disabling step state.");
            _hasActiveStep = false;
            _currentStepIndex = -1;
            return result;
        }

        // If we have never started, start fresh
        if (!_hasActiveStep || _currentStepIndex < 0 || _currentStepIndex >= _gradeOrder.Count)
        {
            Log("ResumeLevelAndTryAdvance: No active step. Starting from step 0.");
            return StartLevelStepsAndGetNewEnemies();
        }

        // We have an active step
        var currentGrade = _gradeOrder[_currentStepIndex];
        bool currentCleared = AreAllEnemiesOfGradeDead(currentGrade);
        Log($"ResumeLevelAndTryAdvance: Active step index={_currentStepIndex}, grade={currentGrade}, cleared={currentCleared}");

        if (currentCleared)
        {
            // Try to advance to next step and collect newly spawned enemies
            if (AdvanceToNextStepInternal(out result))
            {
                if (result == null)
                {
                    Log("ResumeLevelAndTryAdvance: AdvanceToNextStepInternal returned null list, normalizing to empty.");
                    return new List<GameObject>();
                }

                Log($"ResumeLevelAndTryAdvance: Advanced to step index={_currentStepIndex}, newly activated count={result.Count}");
                return result;
            }

            Log("ResumeLevelAndTryAdvance: Attempted to advance but AdvanceToNextStepInternal returned false.");
        }

        // Cannot advance, just ensure current step enemies are visible.
        Log("ResumeLevelAndTryAdvance: Cannot advance. Re-showing current step enemies.");
        ShowEnemiesForCurrentStepInternal();
        return result; // empty
    }

    /// <summary>
    /// Get a snapshot of the current level status.
    /// </summary>
    public LevelSnapshot GetSnapshot()
    {
        var snapshot = new LevelSnapshot();

        snapshot.hasActiveStep = _hasActiveStep;
        snapshot.currentStepIndex = _currentStepIndex;
        snapshot.currentGrade = CurrentGrade;
        snapshot.totalGrades = _gradeOrder.Count;
        snapshot.totalEnemies = _totalCount;
        snapshot.totalDead = _deadCount;
        snapshot.totalAlive = _aliveCount;

        bool canAdvance = false;

        for (int i = 0; i < _gradeOrder.Count; i++)
        {
            var grade = _gradeOrder[i];

            var gradeSnapshot = new LevelGradeSnapshot();
            gradeSnapshot.grade = grade;
            gradeSnapshot.isCurrent = (_hasActiveStep && i == _currentStepIndex);

            if (_enemiesByGrade.TryGetValue(grade, out var list))
            {
                int total = 0;
                int dead = 0;

                foreach (var enemy in list)
                {
                    if (enemy == null)
                    {
                        continue;
                    }

                    total++;

                    if (_dead.Contains(enemy))
                    {
                        dead++;
                    }
                }

                gradeSnapshot.total = total;
                gradeSnapshot.dead = dead;
                gradeSnapshot.alive = Mathf.Max(0, total - dead);
                gradeSnapshot.isCleared = (total > 0 && dead == total);
            }
            else
            {
                gradeSnapshot.total = 0;
                gradeSnapshot.dead = 0;
                gradeSnapshot.alive = 0;
                gradeSnapshot.isCleared = false;
            }

            snapshot.grades.Add(gradeSnapshot);
        }

        // canAdvanceToNextStep: only if we have an active step,
        // current grade is fully dead, and there *is* a next grade.
        if (_hasActiveStep &&
            _currentStepIndex >= 0 &&
            _currentStepIndex < _gradeOrder.Count)
        {
            var currentGrade2 = _gradeOrder[_currentStepIndex];
            bool currentCleared2 = AreAllEnemiesOfGradeDead(currentGrade2);
            bool hasNext = (_currentStepIndex < _gradeOrder.Count - 1);

            canAdvance = currentCleared2 && hasNext;
        }

        snapshot.canAdvanceToNextStep = canAdvance;

        return snapshot;
    }

    // --------------------------------------------------------------------
    // Internal grade / step helpers
    // --------------------------------------------------------------------

    /// <summary>
    /// Rebuilds the dictionary of enemies by grade and the sorted grade order
    /// based on the current 'enemies' list.
    /// </summary>
    private void BuildGradeBuckets()
    {
        _enemiesByGrade.Clear();
        _gradeOrder.Clear();

        Log("BuildGradeBuckets: Rebuilding grade buckets from enemies list.");

        foreach (var enemy in enemies)
        {
            if (enemy == null)
            {
                continue;
            }

            EnemyGrade grade = enemy.grade;

            if (!_enemiesByGrade.TryGetValue(grade, out var list))
            {
                list = new List<RagdollEnemy>();
                _enemiesByGrade[grade] = list;
                _gradeOrder.Add(grade);
                Log($"  New grade bucket: {grade}");
            }

            if (!list.Contains(enemy))
            {
                list.Add(enemy);
                Log($"  Added enemy {enemy.name} to grade {grade}");
            }
        }

        // Sort by enum's underlying order.
        _gradeOrder.Sort();

        Log("BuildGradeBuckets: Grade order after sort:");
        for (int i = 0; i < _gradeOrder.Count; i++)
        {
            Log($"  index={i}, grade={_gradeOrder[i]}");
        }
    }

    /// <summary>
    /// Public wrapper that does not care about the newly activated list.
    /// </summary>
    private void ShowEnemiesForCurrentStep()
    {
        _ = ShowEnemiesForCurrentStepInternal();
    }

    /// <summary>
    /// Shows all alive enemies of the current step grade.
    /// Only affects the grade of the current step. Dead ones for that grade stay disabled.
    /// Returns the list of GameObjects that were newly activated (were inactive before).
    /// </summary>
    private List<GameObject> ShowEnemiesForCurrentStepInternal()
    {
        var newlyActivated = new List<GameObject>();

        if (!_hasActiveStep)
        {
            Log("ShowEnemiesForCurrentStepInternal: No active step.");
            return newlyActivated;
        }

        if (_currentStepIndex < 0 || _currentStepIndex >= _gradeOrder.Count)
        {
            Log($"ShowEnemiesForCurrentStepInternal: Invalid step index={_currentStepIndex}.");
            return newlyActivated;
        }

        var grade = _gradeOrder[_currentStepIndex];
        Log($"ShowEnemiesForCurrentStepInternal: Showing enemies for stepIndex={_currentStepIndex}, grade={grade}.");

        if (_enemiesByGrade.TryGetValue(grade, out var list))
        {
            foreach (var enemy in list)
            {
                if (enemy == null)
                {
                    continue;
                }

                if (_dead.Contains(enemy))
                {
                    // If an enemy of this grade is already killed, keep it disabled.
                    enemy.gameObject.SetActive(false);

                    Log($"  Enemy {enemy.name} is dead, keeping disabled.");
                    continue;
                }

                bool wasActive = enemy.gameObject.activeSelf;
                enemy.gameObject.SetActive(true);

                if (!wasActive)
                {
                    Log($"  Activating enemy {enemy.name} for this step.");
                    newlyActivated.Add(enemy.gameObject);
                }
                else
                {
                    Log($"  Enemy {enemy.name} already active, not counted as new.");
                }
            }
        }
        else
        {
            Log($"ShowEnemiesForCurrentStepInternal: No enemies found for grade {grade}.");
        }

        onStepStarted?.Invoke(_currentStepIndex, grade);
        return newlyActivated;
    }

    /// <summary>
    /// Returns true if there is at least one enemy of this grade and they are all dead.
    /// </summary>
    private bool AreAllEnemiesOfGradeDead(EnemyGrade grade)
    {
        if (!_enemiesByGrade.TryGetValue(grade, out var list) || list.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < list.Count; i++)
        {
            var enemy = list[i];
            if (enemy == null)
            {
                continue;
            }

            if (!_dead.Contains(enemy))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Internal version of AdvanceToNextStep that can also output the list of
    /// newly activated enemy GameObjects (for the new step).
    /// </summary>
    private bool AdvanceToNextStepInternal(out List<GameObject> newlyActivated)
    {
        newlyActivated = null;

        if (!_hasActiveStep)
        {
            Log("AdvanceToNextStepInternal: No active step.");
            return false;
        }

        if (_currentStepIndex < 0 || _currentStepIndex >= _gradeOrder.Count)
        {
            Log($"AdvanceToNextStepInternal: Invalid step index={_currentStepIndex}.");
            return false;
        }

        var currentGrade = _gradeOrder[_currentStepIndex];
        if (!AreAllEnemiesOfGradeDead(currentGrade))
        {
            // Still enemies of this grade alive, do not advance.
            Log($"AdvanceToNextStepInternal: Grade {currentGrade} not cleared yet. Cannot advance.");
            return false;
        }

        _currentStepIndex++;
        Log($"AdvanceToNextStepInternal: Advancing to step index={_currentStepIndex}.");

        if (_currentStepIndex >= _gradeOrder.Count)
        {
            // No more steps.
            Log("AdvanceToNextStepInternal: Reached end of grade list. No more steps.");
            _hasActiveStep = false;
            onAllStepsCompleted?.Invoke();
            newlyActivated = new List<GameObject>();
            return true;
        }

        newlyActivated = ShowEnemiesForCurrentStepInternal();
        Log($"AdvanceToNextStepInternal: New step index={_currentStepIndex}, newly activated={newlyActivated.Count}");
        return true;
    }

    // --------------------------------------------------------------------
    // Internal hooking / unhooking
    // --------------------------------------------------------------------

    private void HookEnemy(RagdollEnemy enemy)
    {
        if (enemy == null)
        {
            return;
        }

        if (_tracked.Contains(enemy))
        {
            Log($"HookEnemy: Enemy {enemy.name} already tracked, skipping.");
            return;
        }

        Log($"HookEnemy: {enemy.name}");
        _tracked.Add(enemy);
        enemies.Add(enemy);
        _dead.Remove(enemy); // ensure it's not in dead set

        // Subscribe to its death event
        enemy.OnDied += HandleEnemyDied;

        onEnemyRegistered?.Invoke(enemy);
    }

    private void UnhookAll()
    {
        Log("UnhookAll: Removing death listeners and clearing sets.");
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
        if (enemy == null)
        {
            return;
        }

        Log($"HandleEnemyDied: {enemy.name}");
        _dead.Add(enemy);
        RecalculateCounts();

        onEnemyDied?.Invoke(enemy);

        // No auto-advance here anymore.
        // Progression is controlled by AdvanceToNextStep()/ResumeLevelAndTryAdvance().
        if (AreAllEnemiesDead())
        {
            Log("HandleEnemyDied: All enemies are dead, invoking onAllEnemiesDead.");
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
            if (e != null)
            {
                _deadCount++;
            }
        }

        _aliveCount = Mathf.Max(0, _totalCount - _deadCount);

        Log($"RecalculateCounts: total={_totalCount}, alive={_aliveCount}, dead={_deadCount}");
        onEnemyCountsChanged?.Invoke(_aliveCount, _deadCount);
    }

    internal void Reset()
    {
        Log("Reset: RefreshEnemiesInLevel + BuildGradeBuckets + HideAllEnemies + clear step state.");
        RefreshEnemiesInLevel();
        BuildGradeBuckets();
        HideAllEnemies(); // important so previous-run enemies don't stay visible
        _currentStepIndex = -1;
        _hasActiveStep = false;
    }
}
