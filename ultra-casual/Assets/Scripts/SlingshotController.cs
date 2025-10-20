using UnityEngine;
using UnityEngine.Events;

[System.Serializable]
public class TransformEvent : UnityEvent<Transform> { }

[DisallowMultipleComponent]
public class SlingshotController : MonoBehaviour, IGameController
{
    [Header("Refs")]
    public SlingshotView view;                 // handles poles, plane math, bands
    public MonoBehaviour slingshotableObject;  // must implement ISlingshotable

    private ISlingshotable _target;

    [Header("Distances")]
    [Tooltip("Pull radius clamp in meters.")]
    public float maxPullDistance = 5f;
    public float minPullDistance = 1f;

    [Header("Angles")]
    [Tooltip("Max yaw (left/right) from baseline forward while aiming & launching.")]
    public float maxYawDegrees = 60f;

    [Header("Launch")]
    public float impulsePerMeter = 300f;  // impulse scale
    [Tooltip("Optional: reference forward to bias launch direction (e.g., ramp forward).")]
    public Transform rampForwardRef;

    [Header("Cinemachine Events")]
    public UnityEvent OnEnterSlingshotMode; // fired when aiming starts
    public TransformEvent OnShotStarted;    // fired when launch happens (passes follow target)

    // State
    private bool _isDragging;
    private bool _isAiming;
    private Vector3 _pullPoint;
    private Vector3 _lastClampedDir = Vector3.forward;

    private void OnValidate()
    {
        if (maxPullDistance < 0f) maxPullDistance = 0f;
        if (minPullDistance < 0f) minPullDistance = 0f;
        if (minPullDistance > maxPullDistance) minPullDistance = maxPullDistance;
    }

    private void Awake()
    {
        _target = slingshotableObject as ISlingshotable;
        if (_target == null)
        {
            Debug.LogError("[SlingshotController] slingshotableObject must implement ISlingshotable.");
        }
        if (!view)
        {
            Debug.LogError("[SlingshotController] Missing SlingshotView reference.");
        }
    }

    private void Start()
    {
        ResetToSlingshot();
    }

    private void Update()
    {
        if (_target == null || view == null) return;

        if (Input.GetMouseButtonDown(0))
        {
            _isDragging = true;
            EnterAimingState();
            view.SetBandsVisible(true);
        }

        if (Input.GetMouseButtonUp(0) && _isDragging)
        {
            _isDragging = false;
            view.SetBandsVisible(false);
            ExitAimingAndLaunch();
        }

        if (!_isDragging && !_isAiming)
        {
            return;
        }

        // --- Pull compute ---
        Vector3 center = view.GetBandCenter();
        Vector3 mouseWorld = view.GetMouseWorldOnPolesPlane(Camera.main);

        Vector3 fromCenter = mouseWorld - center;
        fromCenter = Vector3.ProjectOnPlane(fromCenter, view.upAxis);

        Vector3 baselineFwd = view.GetPreferredForward();
        baselineFwd = Vector3.ProjectOnPlane(baselineFwd, view.upAxis).normalized;

        // Only back/side pull (forbid forward)
        float sForward = Vector3.Dot(fromCenter, baselineFwd);
        if (sForward > 0f)
        {
            fromCenter -= baselineFwd * sForward;
        }

        // Clamp radius to [min, max]
        float mag = fromCenter.magnitude;
        float minR = Mathf.Clamp(minPullDistance, 0f, maxPullDistance);
        float maxR = Mathf.Max(0.01f, maxPullDistance);

        Vector3 dir = mag < 1e-6f ? -baselineFwd : (fromCenter / Mathf.Max(mag, 1e-6f));
        float clampedR = Mathf.Clamp(mag, minR, maxR);
        fromCenter = dir * clampedR;

        _pullPoint = center + fromCenter;

        // --- Move the object: align anchors midpoint to pull point ---
        if (_target.LeftAnchor && _target.RightAnchor && _target.Parent)
        {
            Vector3 currentMid = (_target.LeftAnchor.position + _target.RightAnchor.position) * 0.5f;
            Vector3 delta = _pullPoint - currentMid;
            _target.Parent.position += delta;
        }

        // --- Orient object with yaw clamp, store last clamped direction ---
        _lastClampedDir = AlignToLaunchDirection(center, _pullPoint, baselineFwd);

        // --- Draw bands ---
        view.DrawBands(_target);
    }

    public void ResetToSlingshot()
    {
        if (_target == null || view == null || _target.LeftAnchor == null || _target.RightAnchor == null || _target.Parent == null)
        {
            Debug.LogWarning("[SlingshotController] Missing references for ResetToSlingshot.");
            return;
        }

        Vector3 center = view.GetBandCenter();
        _pullPoint = center;

        Vector3 currentMid = (_target.LeftAnchor.position + _target.RightAnchor.position) * 0.5f;
        _target.Parent.position += (center - currentMid);

        Vector3 forward = view.GetPreferredForward();
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = _target.Parent.forward;
        }
        _target.Parent.rotation = Quaternion.LookRotation(forward, view.upAxis);
        _lastClampedDir = forward;

        view.SetBandsVisible(true);
        view.DrawBands(_target);
    }

    private void EnterAimingState()
    {
        _isAiming = true;
        _target?.SetKinematic(true);
        OnEnterSlingshotMode?.Invoke();
    }

    private void ExitAimingAndLaunch()
    {
        _isAiming = false;
        if (_target == null) return;

        Vector3 center = view.GetBandCenter();
        Vector3 rawDir = Vector3.ProjectOnPlane(center - _pullPoint, view.upAxis);
        if (rawDir.sqrMagnitude < 0.0001f) return;

        rawDir.Normalize();

        Vector3 baselineFwd = view.GetPreferredForward();
        baselineFwd = Vector3.ProjectOnPlane(baselineFwd, view.upAxis).normalized;

        // Yaw clamp
        Vector3 clampedDir = ClampYawAroundUp(baselineFwd, rawDir, maxYawDegrees, view.upAxis);

        // Optional bias along ramp forward
        if (rampForwardRef != null)
        {
            Vector3 rampFwd = Vector3.ProjectOnPlane(rampForwardRef.forward, view.upAxis).normalized;
            clampedDir = Vector3.Slerp(clampedDir, rampFwd, 0.25f).normalized;
        }

        float dist = Vector3.Distance(center, _pullPoint);
        float pullDistance = Mathf.Clamp(dist, Mathf.Clamp(minPullDistance, 0f, maxPullDistance), maxPullDistance);
        float impulse = pullDistance * impulsePerMeter;

        // Tell the object to handle its own force application
        _target.SetKinematic(false);
        _target.Launch(clampedDir, impulse);

        // Camera follow target
        OnShotStarted?.Invoke(_target.FollowTarget ? _target.FollowTarget : _target.Parent);
    }

    private Vector3 AlignToLaunchDirection(Vector3 center, Vector3 currentPull, Vector3 baselineFwd)
    {
        if (_target?.Parent == null) return _lastClampedDir;

        Vector3 desiredDir = Vector3.ProjectOnPlane(center - currentPull, view.upAxis);
        if (desiredDir.sqrMagnitude < 0.0001f)
        {
            desiredDir = Vector3.ProjectOnPlane(view.GetPreferredForward(), view.upAxis);
        }
        if (desiredDir.sqrMagnitude < 0.0001f)
        {
            return _lastClampedDir;
        }

        desiredDir.Normalize();
        baselineFwd = baselineFwd.sqrMagnitude > 0.0001f ? baselineFwd : view.GetPreferredForward();

        Vector3 clampedDir = ClampYawAroundUp(baselineFwd, desiredDir, maxYawDegrees, view.upAxis);

        Quaternion targetRot = Quaternion.LookRotation(clampedDir, view.upAxis);
        _target.Parent.rotation = targetRot;

        return clampedDir;
    }

    private static Vector3 ClampYawAroundUp(Vector3 baselineFwd, Vector3 desiredDir, float maxYawDeg, Vector3 up)
    {
        if (baselineFwd.sqrMagnitude < 0.0001f) return desiredDir;

        float angle = Vector3.SignedAngle(baselineFwd, desiredDir, up);
        float clamped = Mathf.Clamp(angle, -maxYawDeg, maxYawDeg);
        Quaternion yawRot = Quaternion.AngleAxis(clamped, up);
        return (yawRot * baselineFwd).normalized;
    }

    // ---------------- IGameController ----------------
    public void ResetGameState()
    {
        // Put the slingshot back to its ready state.
        ResetToSlingshot();
    }

    public void EndGame()
    {
        // Intentionally blank for now (disable input, play VFX, etc. later)
    }
}
