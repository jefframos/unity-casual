using System;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class TargetMotionTracker : MonoBehaviour
{
    [Header("Bindings")]
    public MonoBehaviour slingshotComponent;
    private IGameController controller;

    public Transform trackedTransform;     // will be set from controller's LaunchStarted
    public Rigidbody trackedBody;          // optional but recommended (for velocity-based stop)
    public bool ignoreYAxis = false;       // distance in XZ only

    [Header("Stop Detection")]
    [Tooltip("Speed (m/s) considered as nearly stopped while inside stop zone.")]
    public float stopSpeedThreshold = 0.2f;

    [Tooltip("How long the speed must stay under the threshold to be considered stopped.")]
    public float stopHoldSeconds = 0.35f;

    [Header("Stop Zone (Trigger)")]
    [Tooltip("Extra time allowed after leaving the trigger before we force a stop.")]
    public float exitGraceSeconds = 0.5f;

    [Tooltip("Multiplier applied to stopSpeedThreshold after grace, to force stop more aggressively.")]
    public float forcedThresholdMultiplier = 5f;

    // ---- Runtime ----
    public bool IsTracking { get; private set; }
    public float DistanceTravelled { get; private set; }

    // straight-line distance from start point to current position
    public float totalDistance { get; private set; }

    // Events
    public event Action<float, float> DistanceChanged;  // cumulative distance updates
    public event Action<float, float> Stopped;          // final cumulative distance on stop
    public event Action<float, float> TotalDistanceChanged;

    public float MaxDistance = 1000f;

    private Vector3 _lastPos;
    private Vector3 _startPos;
    private float _belowThresholdTimer;

    // Trigger state
    public bool _inStopZone;             // currently inside trigger
    public bool _stopZoneEnteredThisLaunch; // has ever entered trigger for this launch
    public float _exitTimer;             // time since leaving trigger
    public float _baseStopSpeedThreshold;
    public float effectiveThreshold_;

    [Header("Player Filter")]
    [SerializeField] private string playerBodyLayerName = "playerBodyTrigger";

    private int _playerBodyLayer = -1;


    private void Awake()
    {
        controller = slingshotComponent as IGameController;
        _baseStopSpeedThreshold = stopSpeedThreshold;

        _playerBodyLayer = LayerMask.NameToLayer(playerBodyLayerName);
        if (_playerBodyLayer == -1)
        {
            Debug.LogError($"[TargetMotionTracker] Layer '{playerBodyLayerName}' not found. " +
                           $"Check that the layer exists in Project Settings > Tags & Layers.");
        }
    }


    private void OnEnable()
    {
        if (controller != null)
            controller.OnLaunchStarted += HandleLaunchStarted;
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

        // Reset all state for this launch
        DistanceTravelled = 0f;
        totalDistance = 0f;
        _belowThresholdTimer = 0f;
        _inStopZone = false;
        _stopZoneEnteredThisLaunch = false;
        _exitTimer = 0f;
        stopSpeedThreshold = _baseStopSpeedThreshold;

        IsTracking = trackedTransform != null;

        if (IsTracking)
        {
            _lastPos = trackedTransform.position;
            _startPos = _lastPos;

            DistanceChanged?.Invoke(DistanceTravelled, DistanceTravelled / MaxDistance);
            TotalDistanceChanged?.Invoke(totalDistance, totalDistance / MaxDistance);
        }
    }

    public void StopTracking(bool fireStoppedEvent = true)
    {
        if (!IsTracking) return;

        IsTracking = false;
        if (fireStoppedEvent)
            Stopped?.Invoke(totalDistance, totalDistance / MaxDistance);
    }

    private void FixedUpdate()
    {
        if (!IsTracking || trackedTransform == null) return;

        // ---- Distance accumulate (segment-by-segment) ----
        Vector3 current = trackedTransform.position;
        Vector3 a = _lastPos;
        Vector3 b = current;

        if (ignoreYAxis)
        {
            a.y = 0f;
            b.y = 0f;
        }

        float step = Vector3.Distance(a, b);
        if (step > 0f)
        {
            DistanceTravelled += step;
            DistanceChanged?.Invoke(DistanceTravelled, DistanceTravelled / MaxDistance);
            _lastPos = current;
        }

        // ---- Straight-line distance from start ----
        Vector3 s = _startPos;
        Vector3 c = current;
        if (ignoreYAxis)
        {
            s.y = 0f;
            c.y = 0f;
        }

        totalDistance = Vector3.Distance(s, c);
        TotalDistanceChanged?.Invoke(totalDistance, totalDistance / MaxDistance);

        // ---- Stop detection ----
        float speed;
#if UNITY_6_0_OR_NEWER
        if (trackedBody != null)
        {
            speed = trackedBody.linearVelocity.magnitude;
        }
        else
        {
            speed = step / Time.fixedDeltaTime;
        }
#else
        if (trackedBody != null)
        {
            speed = trackedBody.linearVelocity.magnitude;
        }
        else
        {
            speed = step / Time.fixedDeltaTime;
        }
#endif

        // If we have not yet entered the stop zone this launch, we never stop.
        if (!_stopZoneEnteredThisLaunch)
        {
            _belowThresholdTimer = 0f;
            return;
        }

        // Handle "left trigger" behaviour: grace time then forced stop
        float effectiveThreshold = stopSpeedThreshold;



        if (_inStopZone)
        {
            // Inside trigger: normal behaviour
            _exitTimer = 0f;
            stopSpeedThreshold = _baseStopSpeedThreshold;
            effectiveThreshold = stopSpeedThreshold;
        }
        else
        {
            // Outside trigger AFTER having entered it once
            _exitTimer += Time.fixedDeltaTime;

            if (_exitTimer >= exitGraceSeconds)
            {
                // Make it easier to be considered "stopped" (higher threshold)
                effectiveThreshold = _baseStopSpeedThreshold * forcedThresholdMultiplier;
            }
        }


        effectiveThreshold_ = effectiveThreshold;

        if (speed <= effectiveThreshold)
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

    // ----------------------------------------------------------------------
    // Trigger callbacks
    // ----------------------------------------------------------------------

    private void OnTriggerEnter(Collider other)
    {
        if (!IsTracking || trackedTransform == null) return;

        // 1) Layer check
        if (_playerBodyLayer != -1 && other.gameObject.layer != _playerBodyLayer)
        {
            Debug.Log($"[TargetMotionTracker] OnTriggerEnter ignored '{other.name}' on layer {LayerMask.LayerToName(other.gameObject.layer)}");
            return;
        }

        // // 2) Make sure this collider belongs to the tracked object
        // bool isTracked =
        //     other.transform == trackedTransform ||
        //     other.transform.IsChildOf(trackedTransform);

        // if (!isTracked)
        // {
        //     Debug.Log($"[TargetMotionTracker] OnTriggerEnter layer OK but not tracked object: '{other.name}'");
        //     return;
        // }

        //Debug.Log($"[TargetMotionTracker] OnTriggerEnter VALID from '{other.name}'");

        _inStopZone = true;
        _stopZoneEnteredThisLaunch = true;
        _exitTimer = 0f;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsTracking || trackedTransform == null) return;

        // 1) Layer check
        if (_playerBodyLayer != -1 && other.gameObject.layer != _playerBodyLayer)
            return;

        // // 2) Ownership check
        // bool isTracked =
        //     other.transform == trackedTransform ||
        //     other.transform.IsChildOf(trackedTransform);

        // if (!isTracked) return;

        //Debug.Log($"[TargetMotionTracker] OnTriggerExit from '{other.name}'");

        _inStopZone = false;
    }

}
