using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem.Controls;

[System.Serializable] public class FloatEvent : UnityEvent<float> { }

[DisallowMultipleComponent]
public class LevelProgressTracker : MonoBehaviour
{
    public static LevelProgressTracker Instance { get; private set; }

    [Header("Refs")]
    public GameManager gameManager;
    public SlingshotController controller;
    public TargetMotionTracker motionTracker;

    [Header("Stats (runtime)")]
    public int runsCompleted;
    public float bestDistance;
    public float lastDistance;
    public float currentDistance;

    [Header("Events (for UI/FX)")]
    public UnityEvent OnRunStarted;
    public UnityEvent<float, float> OnDistanceUpdated;   // cumulative distance this run
    public FloatEvent OnRunEnded;          // final distance
    public FloatEvent OnNewRecord;         // fired when bestDistance improves

    private void Reset()
    {
        gameManager = FindAnyObjectByType<GameManager>();
        controller = FindAnyObjectByType<SlingshotController>();
        motionTracker = FindAnyObjectByType<TargetMotionTracker>();
    }

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

        if (!controller) controller = FindAnyObjectByType<SlingshotController>();
        if (!motionTracker) motionTracker = FindAnyObjectByType<TargetMotionTracker>();
        if (!gameManager) gameManager = FindAnyObjectByType<GameManager>();
    }

    private void OnEnable()
    {
        // Wire controller start
        if (controller != null)
            controller.OnLaunchStarted += HandleRunStarted;

        // Wire tracker updates
        if (motionTracker != null)
        {
            motionTracker.TotalDistanceChanged += HandleDistanceUpdated;
            motionTracker.Stopped += HandleRunStopped;
        }
    }

    private void OnDisable()
    {
        if (controller != null)
            controller.OnLaunchStarted -= HandleRunStarted;

        if (motionTracker != null)
        {
            motionTracker.TotalDistanceChanged -= HandleDistanceUpdated;
            motionTracker.Stopped -= HandleRunStopped;
        }
    }

    private void HandleRunStarted(Transform followTarget)
    {
        // Ensure the tracker starts from this target (if it wasn't auto-wired)
        if (motionTracker != null && motionTracker.trackedTransform != followTarget)
        {
            motionTracker.BeginTracking(followTarget);
        }

        OnRunStarted?.Invoke();
    }

    private void HandleDistanceUpdated(float cumulative, float delta)
    {
        currentDistance = cumulative;
        OnDistanceUpdated?.Invoke(cumulative, delta);
    }

    private void HandleRunStopped(float finalDistance, float delta)
    {
        runsCompleted++;
        lastDistance = finalDistance;

        if (finalDistance > bestDistance)
        {
            bestDistance = finalDistance;
            OnNewRecord?.Invoke(bestDistance);
        }


        OnRunEnded?.Invoke(finalDistance);

        // Optional: notify GameManager that a run ended
        // (You can call EndGame or transition state here if desired)
        // gameManager?.EndGame();
    }

    // Utility API for an external “Restart” button, etc.
    public void RestartRun()
    {
        gameManager?.RestartGame(0);
    }
}
