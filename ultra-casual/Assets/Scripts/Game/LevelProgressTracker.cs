using UnityEngine;
using UnityEngine.Events;

[System.Serializable] public class FloatEvent : UnityEvent<float> { }

[DisallowMultipleComponent]
public class LevelProgressTracker : MonoBehaviour
{
    [Header("Refs")]
    public GameManager gameManager;
    public SlingshotController controller;
    public TargetMotionTracker motionTracker;

    [Header("Stats (runtime)")]
    public int runsCompleted;
    public float bestDistance;
    public float lastDistance;

    [Header("Events (for UI/FX)")]
    public UnityEvent OnRunStarted;
    public FloatEvent OnDistanceUpdated;   // cumulative distance this run
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

    private void HandleDistanceUpdated(float cumulative)
    {
        OnDistanceUpdated?.Invoke(cumulative);
    }

    private void HandleRunStopped(float finalDistance)
    {
        runsCompleted++;
        lastDistance = finalDistance;

        if (finalDistance > bestDistance)
        {
            bestDistance = finalDistance;
            OnNewRecord?.Invoke(bestDistance);
        }

        Debug.Log(finalDistance);

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
