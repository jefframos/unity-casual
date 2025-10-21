using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Minimal UI bridge for showing current distance during a run.
/// Prefers LevelProgressTracker events; falls back to TargetMotionTracker if needed.
/// </summary>
[DisallowMultipleComponent]
public class GameplayUIBridge : MonoBehaviour
{
    [Header("Sources (wire at least one)")]
    public LevelProgressTracker progress;       // preferred
    public TargetMotionTracker motionTracker;   // optional fallback

    [Header("UI")]
    public TextMeshProUGUI distanceText;

    [Tooltip("Optional progress slider (0..bestDistance). Leave null to ignore.")]
    public Slider distanceSlider;

    [Header("Format")]
    [Tooltip("e.g. \"{0:0.0} m\" or \"{0:0} meters\"")]
    public string distanceFormat = "{0:0.0} m";

    [Tooltip("When true, auto-hides the UI when not tracking.")]
    public bool autoHideWhenIdle = true;

    // --- runtime ---
    private bool _wiredToProgress;
    private bool _wiredToMotion;

    private void Reset()
    {
        progress = FindAnyObjectByType<LevelProgressTracker>();
        motionTracker = FindAnyObjectByType<TargetMotionTracker>();

    }

    private void Awake()
    {

    }

    private void OnEnable()
    {
        WireEvents();
        if (autoHideWhenIdle) SetVisible(false);
        SetDistance(0f);
    }

    private void OnDisable()
    {
        UnwireEvents();
    }

    private void WireEvents()
    {
        UnwireEvents();

        if (progress != null)
        {
            progress.OnRunStarted.AddListener(HandleRunStarted);
            progress.OnDistanceUpdated.AddListener(HandleDistanceUpdated);
            progress.OnRunEnded.AddListener(HandleRunEnded);
            // OnNewRecord optional for confetti/VFX
            _wiredToProgress = true;
        }
        else if (motionTracker != null)
        {
            motionTracker.DistanceChanged += HandleDistanceChanged_Fallback;
            motionTracker.TotalDistanceChanged += TotalDistanceChanged;
            motionTracker.Stopped += HandleStopped_Fallback;
            // For fallback start visibility, try current state:
            if (autoHideWhenIdle) SetVisible(motionTracker.IsTracking);
            _wiredToMotion = true;
        }
        else
        {
            Debug.LogWarning("[GameplayUIBridge] No LevelProgressTracker or TargetMotionTracker found. UI will not update.");
        }
    }

    private void UnwireEvents()
    {
        if (_wiredToProgress && progress != null)
        {
            progress.OnRunStarted.RemoveListener(HandleRunStarted);
            progress.OnDistanceUpdated.RemoveListener(HandleDistanceUpdated);
            progress.OnRunEnded.RemoveListener(HandleRunEnded);
        }
        _wiredToProgress = false;

        if (_wiredToMotion && motionTracker != null)
        {
            motionTracker.DistanceChanged -= HandleDistanceChanged_Fallback;
            motionTracker.Stopped -= HandleStopped_Fallback;
        }
        _wiredToMotion = false;
    }

    // -------- Progress (preferred path) --------

    private void HandleRunStarted()
    {
        if (autoHideWhenIdle) SetVisible(true);
        SetDistance(0f);
        if (distanceSlider) distanceSlider.value = 0f;
    }

    private void HandleDistanceUpdated(float cumulative)
    {
        SetDistance(cumulative);
        if (distanceSlider)
        {
            // Simple heuristic: slider max tracks current best (use your own goal if needed)
            float max = Mathf.Max(distanceSlider.maxValue, Mathf.Max(cumulative, progress != null ? progress.bestDistance : 0f));
            distanceSlider.maxValue = max;
            distanceSlider.value = Mathf.Clamp(cumulative, 0f, max);
        }
    }

    private void HandleRunEnded(float finalDistance)
    {
        SetDistance(finalDistance);
        if (autoHideWhenIdle) SetVisible(false);
    }

    // -------- Fallback direct to motion tracker --------
    private void TotalDistanceChanged(float distance)
    {
        if (autoHideWhenIdle) SetVisible(true);

        SetDistance(distance);
        if (distanceSlider)
        {
            float max = Mathf.Max(distanceSlider.maxValue, distance);
            distanceSlider.maxValue = max;
            distanceSlider.value = Mathf.Clamp(distance, 0f, max);
        }
    }

    private void HandleDistanceChanged_Fallback(float cumulative)
    {

    }

    private void HandleStopped_Fallback(float final)
    {
        SetDistance(final);
        if (autoHideWhenIdle) SetVisible(false);
    }

    // -------- UI helpers --------

    private void SetDistance(float meters)
    {
        if (distanceText == null) return;

        // Protect against bad formats
        string formatted;
        try
        {
            formatted = string.IsNullOrEmpty(distanceFormat) ? meters.ToString("0.0") : string.Format(distanceFormat, meters);
        }
        catch
        {
            formatted = meters.ToString("0.0");
        }

        distanceText.text = formatted;
    }

    private void SetVisible(bool on)
    {
        // Toggle this GameObject for simplicity; swap to CanvasGroup if you need fades
        gameObject.SetActive(true); // keep the bridge itself alive
        var root = distanceText ? distanceText.gameObject : null;
        if (root != null) root.SetActive(on);
        if (distanceSlider) distanceSlider.gameObject.SetActive(on);
    }
}
