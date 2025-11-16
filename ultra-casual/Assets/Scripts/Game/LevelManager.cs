using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

public class LevelManager : MonoBehaviour
{
    [Header("Level Builders")]
    public LevelBuilder[] levelBuilders;

    [Header("Orchestration")]
    [Tooltip("Optional: if null, LevelManager will try to find one at runtime.")]
    public EnemyAppearingOrchestrator enemyAppearingOrchestrator;

    private readonly List<IResettable> _resettableCache = new();

    /// <summary>
    /// True if the most recent EndLevel() call determined that the
    /// current level is fully completed (all steps done, all enemies dead).
    /// </summary>
    public bool LastRunCompletedLevel { get; private set; }

    private void FindEnemyAppear()
    {
        // Find orchestrator if not manually wired in the inspector.
        if (enemyAppearingOrchestrator == null)
        {
#if UNITY_2023_1_OR_NEWER
            enemyAppearingOrchestrator = FindFirstObjectByType<EnemyAppearingOrchestrator>();
#else
            enemyAppearingOrchestrator = FindObjectOfType<EnemyAppearingOrchestrator>();
#endif
            if (enemyAppearingOrchestrator == null)
            {
                Debug.LogWarning("[LevelManager] No EnemyAppearingOrchestrator found. " +
                                 "Enemies will appear immediately with no intro.");
            }
        }

    }
    private void Start()
    {

        RebuildResettableCache();
    }

    private void OnDisable()
    {
        // nothing yet
    }

    // --------------------------------------------------
    // Resettable cache
    // --------------------------------------------------

    public void RebuildResettableCache()
    {
        _resettableCache.Clear();

        var all = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        foreach (var mb in all)
        {
            if (!mb || !mb.isActiveAndEnabled)
            {
                continue;
            }

            if (mb is IResettable r)
            {
                _resettableCache.Add(r);
            }
        }
    }

    // --------------------------------------------------
    // Level flow
    // --------------------------------------------------

    /// <summary>
    /// Called when the player is in state to shoot / start a run.
    /// Each time this is called, it must:
    /// - Resume or advance the current level that is on the scene.
    /// - If this call started a new step (either first step or a later one),
    ///   it must call the async LevelStepStarted() with all enemies of that step.
    /// </summary>
    internal void StartLevel()
    {
        EnemyFallCoordinator.Instance?.ResetCoordinator();

        var mediator = LevelTrackerMediator.Instance;
        if (mediator == null)
        {
            Debug.LogWarning("LevelManager.StartLevel: No LevelTrackerMediator.Instance in scene.");
            return;
        }

        // If mediator has no current tracker yet, we must refresh levels first.
        if (mediator.CurrentTracker == null)
        {
            Debug.Log("[LevelManager] StartLevel: No current tracker, calling RefreshLevels().");
            mediator.RefreshLevels();
        }

        // Still nothing? Then there is genuinely no LevelEnemyTracker in the scene.
        if (mediator.CurrentTracker == null)
        {
            Debug.LogWarning("[LevelManager] StartLevel: RefreshLevels() did not find any LevelEnemyTracker. Aborting StartLevel.");
            return;
        }

        // Start or resume: this will either start the first step or,
        // if the current grade is cleared, advance to the next one.
        // It returns the newly activated enemies for that step.
        List<GameObject> stepEnemies = mediator.StartOrResumeLevel() ?? new List<GameObject>();

        int count = stepEnemies.Count;
        Debug.Log($"[LevelManager] StartLevel: stepEnemies.Count = {count}");

        if (count > 0)
        {
            // We actually started a step on this frame.
            // Fire-and-forget the async intro of this step.
            _ = LevelStepStarted(stepEnemies);
        }
    }

    /// <summary>
    /// Called when a run (shot) ends. This method must know if the level
    /// is fully completed or not.
    /// </summary>
    internal void EndLevel()
    {
        LastRunCompletedLevel = false;

        var mediator = LevelTrackerMediator.Instance;
        if (mediator == null)
        {
            Debug.LogWarning("LevelManager.EndLevel: No LevelTrackerMediator.Instance in scene.");
            return;
        }

        var snapshot = mediator.GetSnapshot();
        if (snapshot == null)
        {
            Debug.LogWarning("LevelManager.EndLevel: Snapshot is null (no active tracker?).");
            return;
        }

        Debug.Log($"[LevelManager] Total killed so far: {snapshot.totalDead}/{snapshot.totalEnemies}");
        foreach (var g in snapshot.grades)
        {
            Debug.Log($"[LevelManager] Grade {g.grade}: {g.dead}/{g.total} dead (current: {g.isCurrent})");
        }

        // Determine if the WHOLE level is completed:
        // - There were enemies.
        // - All enemies are dead.
        // - Tracker has no active step anymore (AllStepsCompleted fired).
        bool anyEnemies = snapshot.totalEnemies > 0;
        bool allDead = anyEnemies && snapshot.totalDead == snapshot.totalEnemies;
        bool noActiveStep = !snapshot.hasActiveStep;

        bool levelCompleted = allDead; // && noActiveStep;
        LastRunCompletedLevel = levelCompleted;

        if (levelCompleted)
        {
            Debug.Log("[LevelManager] Level COMPLETED.");
        }
        else
        {
            Debug.Log("[LevelManager] Level NOT completed yet (either more enemies or more steps exist).");
        }

        // Note: we do NOT advance to next step here.
        // Step advancement is handled by StartLevel() via StartOrResumeLevel().
    }

    /// <summary>
    /// Start a new game/run: reset everything and prepare gameplay.
    /// </summary>
    public void StartGame()
    {
        ResetAll();
        // Optionally trigger StartLevel() here if your flow wants
        // to immediately start the first step after reset.
        // StartLevel();
    }

    /// <summary>
    /// End the current run/game.
    /// </summary>
    public void EndGame()
    {
        // Your higher-level game-over / victory logic can look at:
        // - LastRunCompletedLevel
        // - current snapshot, etc.
    }

    // --------------------------------------------------
    // Resetting
    // --------------------------------------------------

    public void ResetAll()
    {
        foreach (var levelBuilder in levelBuilders)
        {
            if (levelBuilder != null)
            {
                levelBuilder.ResetLevel();
            }
        }

        foreach (var r in _resettableCache.ToList())
        {
            if (r != null)
            {
                r.ResetToInitial();
            }
        }

        LevelTrackerMediator.Instance.RefreshLevels();
    }

    // --------------------------------------------------
    // Async hook when a level step actually starts
    // --------------------------------------------------

    /// <summary>
    /// Async hook called whenever a new level step actually begins.
    /// It receives all enemies that will be active on this step.
    /// Now delegates to EnemyAppearingOrchestrator.
    /// </summary>
    private async UniTask LevelStepStarted(List<GameObject> enemiesOnStep)
    {
        if (enemiesOnStep == null || enemiesOnStep.Count == 0)
        {
            return;
        }


        FindEnemyAppear();
        Debug.LogWarning("FIX THIS APPEARING ORCHESTRATOR");
        // This token is cancelled when LevelManager is destroyed.
        CancellationToken token = this.GetCancellationTokenOnDestroy();

        if (enemyAppearingOrchestrator != null)
        {
            await enemyAppearingOrchestrator.ShowStepEnemiesAsync(enemiesOnStep, token);
        }
        else
        {
            // Fallback: just make sure they are active immediately.
            foreach (var enemy in enemiesOnStep)
            {
                if (enemy != null && !enemy.activeSelf)
                {
                    enemy.SetActive(true);
                }
            }
        }
    }
}
