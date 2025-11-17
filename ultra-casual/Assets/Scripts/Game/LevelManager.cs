using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using DG.Tweening;

public class LevelManager : MonoBehaviour
{
    [Header("Level Data")]
    [SerializeField] private Levels levels;          // ScriptableObject
    [SerializeField] private Transform levelPivot;   // Where levels are spawned

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

    // -----------------------------
    // Current level state
    // -----------------------------
    public int CurrentAreaIndex { get; private set; } = -1;
    public int CurrentLevelIndexInArea { get; private set; } = -1;
    public int CurrentGlobalLevelIndex { get; private set; } = -1;
    public GameObject CurrentLevelInstance { get; private set; }

    // Event payload
    public struct LevelSpawnInfo
    {
        public int areaIndex;
        public int levelIndexInArea;
        public int globalIndex;
        public AreaDefinition areaDefinition;
        public GameObject levelInstance;
    }

    /// <summary>
    /// Fired whenever a new level prefab has been spawned under the pivot.
    /// </summary>
    public event Action<LevelSpawnInfo> NewLevelSpawnedEvent;

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
    // Level SPAWNING API
    // --------------------------------------------------

    /// <summary>
    /// Spawn level by area + local index.
    /// </summary>
    public async Task SpawnLevelByAreaAndIndex(int areaIndex, int levelIndexInArea)
    {
        if (levels == null)
        {
            Debug.LogError("[LevelManager] No Levels ScriptableObject assigned.");
            return;
        }

        GameObject prefab = levels.GetLevelByArea(
            areaIndex,
            levelIndexInArea,
            out int globalIndex,
            out AreaDefinition areaDef);

        if (prefab == null)
        {
            Debug.LogError($"[LevelManager] SpawnLevelByAreaAndIndex: invalid indices (area={areaIndex}, level={levelIndexInArea}).");
            return;
        }

        await SpawnLevelInternal(prefab, areaIndex, levelIndexInArea, globalIndex, areaDef);
    }

    /// <summary>
    /// Spawn level by global order index (0..TotalLevels-1).
    /// </summary>
    public async Task SpawnLevelByGlobalIndex(int globalIndex)
    {
        if (levels == null)
        {
            Debug.LogError("[LevelManager] No Levels ScriptableObject assigned.");
            return;
        }

        GameObject prefab = levels.GetLevelByGlobalIndex(
            globalIndex,
            out AreaDefinition areaDef,
            out int areaIndex,
            out int levelIndexInArea);

        if (prefab == null)
        {
            Debug.LogError($"[LevelManager] SpawnLevelByGlobalIndex: invalid globalIndex={globalIndex}.");
            return;
        }

        await SpawnLevelInternal(prefab, areaIndex, levelIndexInArea, globalIndex, areaDef);
    }

    private async Task SpawnLevelInternal(
        GameObject prefab,
        int areaIndex,
        int levelIndexInArea,
        int globalIndex,
        AreaDefinition areaDef)
    {
        if (levelPivot == null)
        {
            Debug.LogError("[LevelManager] LevelPivot is not assigned.");
            return;
        }

        // cleanup previous level
        ClearLevelPivot();

        await UniTask.WaitForEndOfFrame();

        // spawn new one
        GameObject instance = Instantiate(prefab, levelPivot);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        //instance.transform.localScale = Vector3.one;

        await instance.transform.DOScale(instance.transform.localScale * 0.95f, 0.25f).From().SetEase(Ease.OutBack).AsyncWaitForCompletion();

        CurrentAreaIndex = areaIndex;
        CurrentLevelIndexInArea = levelIndexInArea;
        CurrentGlobalLevelIndex = globalIndex;
        CurrentLevelInstance = instance;

        // Rebuild caches so reset + enemy tracking includes this new level.
        RebuildResettableCache();
        ResetAll();
        LevelTrackerMediator.Instance?.RefreshLevels();
        LevelTrackerMediator.Instance?.StartNewLevel();

        // Fire event hook
        var info = new LevelSpawnInfo
        {
            areaIndex = areaIndex,
            levelIndexInArea = levelIndexInArea,
            globalIndex = globalIndex,
            areaDefinition = areaDef,
            levelInstance = instance
        };

        NewLevelSpawned(info);
    }

    private void ClearLevelPivot()
    {
        if (levelPivot == null) return;

        for (int i = levelPivot.childCount - 1; i >= 0; i--)
        {
            var child = levelPivot.GetChild(i);
            if (Application.isPlaying)
            {
                Destroy(child.gameObject);
            }
            else
            {
                DestroyImmediate(child.gameObject);
            }
        }

        CurrentLevelInstance = null;
    }

    /// <summary>
    /// Called whenever a new level prefab has been spawned.
    /// Dispatches the NewLevelSpawnedEvent.
    /// </summary>
    private void NewLevelSpawned(LevelSpawnInfo info)
    {
        NewLevelSpawnedEvent?.Invoke(info);
        Debug.Log($"[LevelManager] New level spawned: Area={info.areaIndex}, LocalLevel={info.levelIndexInArea}, Global={info.globalIndex}");
    }

    // --------------------------------------------------
    // Level flow (your existing code)
    // --------------------------------------------------

    internal async Task StartLevel(SlingshotCinemachineBridge cameraBridge, GameUiHandler uiHandler)
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
            Debug.LogWarning("LevelManager.StartLevel: RefreshLevels() did not find any LevelEnemyTracker. Aborting StartLevel.");
            return;
        }

        // Start or resume.
        List<GameObject> stepEnemies = mediator.StartOrResumeLevel() ?? new List<GameObject>();

        int count = stepEnemies.Count;
        Debug.Log($"[LevelManager] StartLevel: stepEnemies.Count = {count}");

        if (count > 0)
        {
            cameraBridge.SetCameraMode(SlingshotCinemachineBridge.GameCameraMode.EnemyReveal);
            uiHandler.SetMode(UiMode.EnemyReveal);

            await LevelStepStarted(stepEnemies);
        }
    }

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
    }

    public void StartGame()
    {
        ResetAll();
    }

    public void EndGame()
    {
        // higher-level game-over logic
    }

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

    private async UniTask LevelStepStarted(List<GameObject> enemiesOnStep)
    {
        if (enemiesOnStep == null || enemiesOnStep.Count == 0)
        {
            return;
        }

        FindEnemyAppear();
        CancellationToken token = this.GetCancellationTokenOnDestroy();

        if (enemyAppearingOrchestrator != null)
        {
            await enemyAppearingOrchestrator.ShowStepEnemiesAsync(enemiesOnStep, token);
        }
        else
        {
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
