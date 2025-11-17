using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mediator that bridges LevelEnemyTracker(s) and UI / game flow.
/// - Finds trackers in the scene.
/// - Chooses one as the "current" level tracker.
/// - Lets you refresh, get a snapshot, and start/resume/advance steps.
/// UI should listen to events from this mediator instead of talking
/// directly to the trackers.
/// </summary>
public class LevelTrackerMediator : MonoBehaviour
{
    public static LevelTrackerMediator Instance { get; private set; }

    [Header("Refresh Behaviour")]
    [Tooltip("If false, trackers will NOT call Reset() when RefreshLevels() is invoked.")]
    public bool persistentLevels = false;

    [Tooltip("Optional: if true, mediator will automatically push a snapshot when something changes.")]
    public bool autoBroadcastSnapshot = true;

    /// <summary>
    /// Snapshot type mirrored from LevelEnemyTracker.
    /// (Alias to avoid typing full nested name everywhere.)
    /// </summary>
    public class LevelSnapshot : LevelEnemyTracker.LevelSnapshot { }

    /// <summary>
    /// Fired whenever we rebuild trackers (RefreshLevels).
    /// Useful if UI wants to know that a new level was found / selected.
    /// </summary>
    public event Action OnTrackersRefreshed;

    /// <summary>
    /// Fired whenever the mediator pushes a new snapshot.
    /// Includes per-grade totals, total killed so far,
    /// what grades exist, and whether it CAN progress to next step.
    /// </summary>
    public event Action<LevelEnemyTracker.LevelSnapshot> OnSnapshotUpdated;

    /// <summary>
    /// Fired when TryAdvanceStep() successfully moves to the next step.
    /// Provides the new snapshot after advancing.
    /// </summary>
    public event Action<LevelEnemyTracker.LevelSnapshot> OnAdvancedStep;
    public event Action OnResetStarted;

    // All level trackers currently known
    private readonly List<LevelEnemyTracker> _trackers = new List<LevelEnemyTracker>();

    // The "active" tracker this mediator drives.
    [SerializeField]
    private LevelEnemyTracker _currentTracker;

    // Cache of last snapshot for polling-style use.
    private LevelEnemyTracker.LevelSnapshot _lastSnapshot;

    /// <summary>
    /// Public read-only access to the current tracker.
    /// </summary>
    public LevelEnemyTracker CurrentTracker
    {
        get { return _currentTracker; }
    }

    // --------------------------------------------------
    // Singleton setup
    // --------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        // If you want this to survive scene loads:
        // DontDestroyOnLoad(gameObject);
    }

    private void OnDisable()
    {
        UnhookAllTrackers();
        _trackers.Clear();
        _currentTracker = null;
        _lastSnapshot = null;
    }

    // --------------------------------------------------
    // Public API
    // --------------------------------------------------

    /// <summary>
    /// Starts or resumes the current level according to the tracker state.
    /// Flow:
    /// - If there is no current tracker, returns an empty list.
    /// - If the current tracker has not started any step yet, it will
    ///   call StartLevelStepsAndGetNewEnemies() (fresh start).
    /// - If the current tracker has an active step:
    ///     * If that grade is cleared, it will advance to the next step
    ///       and return the GameObjects of newly activated enemies.
    ///     * If it cannot advance, it will re-show the current step's
    ///       alive enemies and return an empty list.
    /// After calling into the tracker, this will broadcast a snapshot
    /// if autoBroadcastSnapshot is true.
    /// </summary>
    public List<GameObject> StartOrResumeLevel()
    {
        var result = new List<GameObject>();

        if (_currentTracker == null)
        {
            return result;
        }

        if (_currentTracker.HasActiveStep)
        {
            _currentTracker.HideAllEnemies();
            result = _currentTracker.ResumeLevelAndTryAdvance();
        }
        else
        {
            _currentTracker.HideAllEnemies();
            result = _currentTracker.StartLevelStepsAndGetNewEnemies();
        }

        if (autoBroadcastSnapshot)
        {
            BroadcastSnapshot();
        }

        return result ?? new List<GameObject>();
    }

    /// <summary>
    /// Called by GameManager when a new level/run is starting.
    /// - Unhooks previous trackers.
    /// - Finds all LevelEnemyTracker in the scene.
    /// - Optionally calls Reset() on each (if !persistentLevels).
    /// - Selects one tracker as current (first by default).
    /// - Hooks events from trackers.
    /// - Broadcasts initial snapshot (no steps started yet).
    /// </summary>
    public void RefreshLevels()
    {

        UnhookAllTrackers();
        _trackers.Clear();
        _currentTracker = null;
        _lastSnapshot = null;

        var trackers = FindObjectsByType<LevelEnemyTracker>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (var tracker in trackers)
        {
            if (tracker == null)
            {
                continue;
            }

            _trackers.Add(tracker);

            if (!persistentLevels)
            {
                tracker.Reset();
            }

            // Hook events
            tracker.onEnemyRegistered.AddListener(OnEnemyRegistered);
            tracker.onEnemyDied.AddListener(OnEnemyDied);
            tracker.onEnemyCountsChanged.AddListener(OnCountsChanged);
            tracker.onStepStarted.AddListener(OnStepStarted);
            tracker.onAllStepsCompleted.AddListener(OnAllStepsCompleted);
        }

        // Pick a current tracker (you can later extend this to choose by some id)
        if (_trackers.Count > 0)
        {
            _currentTracker = _trackers[0];

            // IMPORTANT: do NOT start steps here.
            // We want LevelManager.StartLevel() to control when the first step begins.

            if (autoBroadcastSnapshot)
            {
                BroadcastSnapshot();
            }
        }

        if (OnTrackersRefreshed != null)
        {
            OnTrackersRefreshed.Invoke();
        }


    }

    public void StartNewLevel()
    {
        OnResetStarted?.Invoke();

    }
    /// <summary>
    /// Returns the current level snapshot (a copy) from the active tracker.
    /// If no tracker is active, returns null.
    /// </summary>
    public LevelEnemyTracker.LevelSnapshot GetSnapshot()
    {
        if (_currentTracker == null)
        {
            return null;
        }

        _lastSnapshot = _currentTracker.GetSnapshot();
        return _lastSnapshot;
    }

    /// <summary>
    /// Simple manual "advance" useful for buttons. If you want
    /// the list of enemies, use StartOrResumeLevel instead.
    /// </summary>
    public bool TryAdvanceStep()
    {
        if (_currentTracker == null)
        {
            return false;
        }

        bool advanced = _currentTracker.AdvanceToNextStep();
        if (!advanced)
        {
            return false;
        }

        _lastSnapshot = _currentTracker.GetSnapshot();

        if (autoBroadcastSnapshot && OnSnapshotUpdated != null)
        {
            OnSnapshotUpdated.Invoke(_lastSnapshot);
        }

        if (OnAdvancedStep != null)
        {
            OnAdvancedStep.Invoke(_lastSnapshot);
        }

        return true;
    }

    [ContextMenu("Force Broadcast Snapshot")]
    public void ForceBroadcastSnapshot()
    {
        BroadcastSnapshot();
    }

    // --------------------------------------------------
    // Tracker selection
    // --------------------------------------------------

    public void SetCurrentTracker(LevelEnemyTracker tracker)
    {
        if (tracker == null)
        {
            _currentTracker = null;
            _lastSnapshot = null;
            return;
        }

        if (!_trackers.Contains(tracker))
        {
            _trackers.Add(tracker);
            tracker.onEnemyRegistered.AddListener(OnEnemyRegistered);
            tracker.onEnemyDied.AddListener(OnEnemyDied);
            tracker.onEnemyCountsChanged.AddListener(OnCountsChanged);
            tracker.onStepStarted.AddListener(OnStepStarted);
            tracker.onAllStepsCompleted.AddListener(OnAllStepsCompleted);
        }

        _currentTracker = tracker;

        if (autoBroadcastSnapshot)
        {
            BroadcastSnapshot();
        }
    }

    // --------------------------------------------------
    // Events from trackers
    // --------------------------------------------------

    private void UnhookAllTrackers()
    {
        foreach (var tracker in _trackers)
        {
            if (tracker == null)
            {
                continue;
            }

            tracker.onEnemyRegistered.RemoveListener(OnEnemyRegistered);
            tracker.onEnemyDied.RemoveListener(OnEnemyDied);
            tracker.onEnemyCountsChanged.RemoveListener(OnCountsChanged);
            tracker.onStepStarted.RemoveListener(OnStepStarted);
            tracker.onAllStepsCompleted.RemoveListener(OnAllStepsCompleted);
        }
    }

    private void OnEnemyRegistered(RagdollEnemy enemy)
    {
        if (!autoBroadcastSnapshot || enemy == null)
        {
            return;
        }

        if (_currentTracker != null && enemy.gameObject.scene == _currentTracker.gameObject.scene)
        {
            BroadcastSnapshot();
        }
    }

    private void OnEnemyDied(RagdollEnemy enemy)
    {
        if (!autoBroadcastSnapshot || enemy == null)
        {
            return;
        }

        if (_currentTracker != null && enemy.gameObject.scene == _currentTracker.gameObject.scene)
        {
            BroadcastSnapshot();
        }
    }

    private void OnCountsChanged(int alive, int dead)
    {
        if (!autoBroadcastSnapshot)
        {
            return;
        }

        if (_currentTracker != null)
        {
            BroadcastSnapshot();
        }
    }

    private void OnStepStarted(int stepIndex, EnemyGrade grade)
    {
        if (!autoBroadcastSnapshot)
        {
            return;
        }

        if (_currentTracker != null)
        {
            BroadcastSnapshot();
        }
    }

    private void OnAllStepsCompleted()
    {

        if (!autoBroadcastSnapshot)
        {
            return;
        }

        if (_currentTracker != null)
        {
            BroadcastSnapshot();
        }
    }

    // --------------------------------------------------
    // Snapshot broadcaster
    // --------------------------------------------------

    private void BroadcastSnapshot()
    {
        if (_currentTracker == null)
        {
            return;
        }

        _lastSnapshot = _currentTracker.GetSnapshot();

        if (OnSnapshotUpdated != null)
        {
            OnSnapshotUpdated.Invoke(_lastSnapshot);
        }
    }
}
