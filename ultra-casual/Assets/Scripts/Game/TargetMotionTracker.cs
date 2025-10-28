using System;
using UnityEngine;

[DisallowMultipleComponent]
public class TargetMotionTracker : MonoBehaviour
{
    [Header("Bindings")]
    public SlingshotController controller; // assign in inspector (or Find in Awake)
    public Transform trackedTransform;     // will be set from controller's LaunchStarted
    public Rigidbody trackedBody;          // optional but recommended (for velocity-based stop)
    public bool ignoreYAxis = false;       // distance in XZ only

    [Header("Stop Detection")]
    [Tooltip("Speed (m/s) considered as nearly stopped.")]
    public float stopSpeedThreshold = 0.2f;

    [Tooltip("How long the speed must stay under the threshold to be considered stopped.")]
    public float stopHoldSeconds = 0.35f;

    // ---- Runtime ----
    public bool IsTracking { get; private set; }
    public float DistanceTravelled { get; private set; }

    // New: straight-line distance from start point to current position
    public float totalDistance { get; private set; }

    // Events
    public event Action<float> DistanceChanged;  // cumulative distance updates
    public event Action<float> Stopped;          // final cumulative distance on stop
    public event Action<float> TotalDistanceChanged;
    private Vector3 _lastPos;
    private Vector3 _startPos;
    private float _belowThresholdTimer;

    private void Awake()
    {
        if (!controller)
        {
            controller = FindAnyObjectByType<SlingshotController>();
        }
    }

    private void OnEnable()
    {
        if (controller != null)
            controller.OnLaunchStarted += HandleLaunchStarted; // keeping your event name as-is
    }

    private void OnDisable()
    {
        if (controller != null)
            controller.OnLaunchStarted -= HandleLaunchStarted;
    }

    private void HandleLaunchStarted(Transform followTarget)
    {
        BeginTracking(followTarget);
    }

    public void BeginTracking(Transform t)
    {
        trackedTransform = t;
        if (trackedTransform != null && trackedBody == null)
        {
            trackedBody = trackedTransform.GetComponentInParent<Rigidbody>();
        }

        DistanceTravelled = 0f;
        totalDistance = 0f;            // start straight-line distance at 0
        _belowThresholdTimer = 0f;
        IsTracking = trackedTransform != null;

        if (IsTracking)
        {
            _lastPos = trackedTransform.position;
            _startPos = _lastPos;      // capture start point
            DistanceChanged?.Invoke(DistanceTravelled);
            TotalDistanceChanged?.Invoke(totalDistance);
        }
    }

    public void StopTracking(bool fireStoppedEvent = true)
    {
        if (!IsTracking) return;
        IsTracking = false;
        if (fireStoppedEvent)
            Stopped?.Invoke(DistanceTravelled);
    }

    private void FixedUpdate()
    {
        if (!IsTracking || trackedTransform == null) return;

        // Distance accumulate (segment-by-segment)
        Vector3 current = trackedTransform.position;
        Vector3 a = _lastPos;
        Vector3 b = current;

        if (ignoreYAxis)
        {
            a.y = 0f; b.y = 0f;
        }

        float step = Vector3.Distance(a, b);
        if (step > 0f)
        {
            DistanceTravelled += step;
            DistanceChanged?.Invoke(DistanceTravelled);
            _lastPos = current;
        }

        // Update straight-line distance from start
        Vector3 s = _startPos;
        Vector3 c = current;
        if (ignoreYAxis)
        {
            s.y = 0f; c.y = 0f;
        }
        totalDistance = Vector3.Distance(s, c);
        TotalDistanceChanged?.Invoke(totalDistance);

        // Stop detection (Unity 6: use linearVelocity when Rigidbody is available)
        float speed;
        if (trackedBody != null)
        {
            speed = trackedBody.linearVelocity.magnitude;
        }
        else
        {
            // Heuristic from positional delta per fixed step
            speed = step / Time.fixedDeltaTime;
        }

        if (speed <= stopSpeedThreshold)
        {
            _belowThresholdTimer += Time.fixedDeltaTime;
            if (_belowThresholdTimer >= stopHoldSeconds)
            {
                StopTracking(true); // fires Stopped event
            }
        }
        else
        {
            _belowThresholdTimer = 0f;
        }
    }
}
