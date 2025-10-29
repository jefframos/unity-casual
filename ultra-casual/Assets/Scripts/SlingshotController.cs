using System;
using UnityEngine;

[DisallowMultipleComponent]
public class SlingshotController : MonoBehaviour, IGameController
{
    [Header("Refs")]
    public SlingshotView view;                 // handles poles, plane math, bands
    public MonoBehaviour slingshotableObject;  // must implement ISlingshotable
    public SlingshotUIBridge uiBridge;         // blocks input if pointer is over UI

    private ISlingshotable _target;

    [Header("Distances")]
    [Tooltip("Pull radius clamp in meters.")]
    public float maxPullDistance = 5f;
    public float minPullDistance = 1f;
    public float polesEndInset = 0.05f;
    public bool capWithinPoles = true;

    [Header("Aim Activation")]
    [Tooltip("How far (in meters) you must pull BACK along the baseline axis before real aiming activates.")]
    public float aimActivationDistance = 0.25f;

    [Header("Angles")]
    [Tooltip("Max yaw (left/right) from baseline forward while aiming & launching.")]
    public float maxYawDegrees = 60f;

    [Header("Launch")]
    public bool useUpgradeData = true;  // impulse scale
    public UpgradeType upgradeType = UpgradeType.SLINGSHOT;  // impulse scale
    public float impulsePerMeter = 10f;  // impulse scale
    [Tooltip("Optional: reference forward to bias launch direction (e.g., ramp forward).")]
    public Transform rampForwardRef;

    // Events (kept as you had)
    public event Action<Transform> OnEnterEndMode;
    public event Action<Transform> OnEnterGameMode;
    public event Action<Transform> OnLaunchStarted;
    public event Action<Transform> OnShotStarted;
    public event Action<Transform> OnReleaseStarted;

    // State
    private bool _isPointerDown;
    private bool _isAiming;             // true only AFTER threshold is met
    private Vector3 _pullPoint;
    private Vector3 _lastClampedDir = Vector3.forward;

    // Pre-aim (threshold) bookkeeping
    private Vector3 _pressCenter;       // band center at press
    private Vector3 _pressBaselineFwd;  // projected on plane
    private Vector3 _startParentPos;    // where we must reset to if threshold not reached
    private Quaternion _startParentRot; // initial facing
    private Vector3 _startMid;          // mid of anchors at press (for accuracy)

    private void OnValidate()
    {
        if (maxPullDistance < 0f) maxPullDistance = 0f;
        if (minPullDistance < 0f) minPullDistance = 0f;
        if (minPullDistance > maxPullDistance) minPullDistance = maxPullDistance;
        if (aimActivationDistance < 0f) aimActivationDistance = 0f;
    }

    private void OnDisable()
    {
        if (_target != null)
        {
            _target.OnLaunchStart -= LaunchStarted;
            _target.OnReleaseStart -= ReleaseStarted;
        }
    }

    private void OnEnable()
    {
        _target = slingshotableObject as ISlingshotable;
        if (_target == null)
        {
            Debug.LogError("[SlingshotController] slingshotableObject must implement ISlingshotable.");
        }
        else
        {
            _target.OnLaunchStart += LaunchStarted;
            _target.OnReleaseStart += ReleaseStarted;
        }

        if (!view)
        {
            Debug.LogError("[SlingshotController] Missing SlingshotView reference.");
        }
    }

    private void ReleaseStarted()
    {
        var t = _target != null ? (_target.FollowTarget ? _target.FollowTarget : _target.Parent) : null;
        OnReleaseStarted?.Invoke(t);
    }

    private void LaunchStarted()
    {
        var t = _target != null ? (_target.FollowTarget ? _target.FollowTarget : _target.Parent) : null;
        OnLaunchStarted?.Invoke(t);
    }

    private void Start()
    {
        ResetToSlingshot();
    }

    private void Update()
    {
        if (_target == null || view == null) return;
        if (_target.IsLaunching) return;

        // --- Pointer Down ---
        if (Input.GetMouseButtonDown(0))
        {
            if (uiBridge != null && uiBridge.IsBlockedNow())
            {
                return; // ignore press when over UI
            }

            _isPointerDown = true;
            BeginPreAim();
        }

        // --- Pointer Up ---
        if (Input.GetMouseButtonUp(0) && _isPointerDown)
        {
            _isPointerDown = false;

            if (_isAiming)
            {
                view.SetBandsVisible(false);
                ExitAimingAndLaunch();
            }
            else
            {
                // Didn’t reach threshold: fully reset pose
                HardResetToStartPose();
                view.SetBandsVisible(false);
            }
        }

        if (!_isPointerDown && !_isAiming)
        {
            return;
        }

        // --- While pointer is down: compute pull vector from current mouse ---
        Vector3 center = view.GetBandCenter();
        Vector3 mouseWorld = view.GetMouseWorldOnPolesPlane(Camera.main);

        // Raw pull on plane
        Vector3 fromCenter = mouseWorld - center;
        fromCenter = Vector3.ProjectOnPlane(fromCenter, view.upAxis);

        // Baseline forward at press (stable)
        Vector3 baselineFwd = _pressBaselineFwd;

        // Forbid forward pull (only allow back / side)
        float fwdDot = Vector3.Dot(fromCenter, baselineFwd);
        if (fwdDot > 0f)
        {
            fromCenter -= baselineFwd * fwdDot; // remove forward component
        }

        // ---- Axis-based clamp (NOT radial) ----
        // Pull axis is straight back from the center along -baselineFwd
        Vector3 pullAxis = -baselineFwd;

        // Decompose into back (axis) + lateral
        float back = Mathf.Max(0f, Vector3.Dot(fromCenter, pullAxis));        // >= 0 if pulling back
        Vector3 backVec = pullAxis * back;
        Vector3 lateral = fromCenter - backVec;
        float lateralMag = lateral.magnitude;
        Vector3 lateralDir = lateralMag > 1e-6f ? (lateral / lateralMag) : Vector3.zero;

        // Clamp axis distance to [min, max]  (this is your new minPullDistance meaning)
        float minBack = Mathf.Clamp(minPullDistance, 0f, maxPullDistance);
        float clampedBack = Mathf.Clamp(back, minBack, maxPullDistance);

        // Also clamp lateral so total radius doesn't exceed maxPullDistance
        float maxLat = Mathf.Sqrt(Mathf.Max(0f, maxPullDistance * maxPullDistance - clampedBack * clampedBack));
        float clampedLat = Mathf.Min(lateralMag, maxLat);

        // Rebuild the pull with axis-min enforced
        fromCenter = pullAxis * clampedBack + lateralDir * clampedLat;
        _pullPoint = center + fromCenter;

        // --- Threshold check strictly along the back axis ---
        float backAmount = clampedBack; // distance from the start along the axis
        bool reachedThreshold = backAmount >= aimActivationDistance;

        if (!_isAiming)
        {
            if (reachedThreshold)
            {
                // Now we truly enter aiming
                EnterAimingState();
                view.SetBandsVisible(true);
            }
            else
            {
                // Not enough tension: keep resetting the object to the start pose
                SoftResetTowardStartPose();
                return; // don’t proceed to aiming logic
            }
        }


        if (capWithinPoles) // your bool toggle
        {
            _pullPoint = view.ClampPointBetweenPoles(_pullPoint, polesEndInset); // e.g. polesEndInset = 0.05f
        }
        // --- Real aiming logic (only when threshold met) ---

        // Move so mid of anchors sits at pullPoint (smoothed)
        if (_target.LeftAnchor && _target.RightAnchor && _target.Parent)
        {
            Vector3 currentMid = (_target.LeftAnchor.position + _target.RightAnchor.position) * 0.5f;
            Vector3 delta = _pullPoint - currentMid;
            const float lerpSpeed = 0.1f;
            _target.Parent.position = Vector3.Lerp(_target.Parent.position, _target.Parent.position + delta, lerpSpeed);
        }

        // Orient with yaw clamp
        _lastClampedDir = AlignToLaunchDirection(center, _pullPoint, baselineFwd);

        // Draw bands
        view.DrawBands(_target);

    }

    // ----------- Pre-aim helpers -----------
    private void BeginPreAim()
    {
        // Cache stable references at press time
        _pressCenter = view.GetBandCenter();
        _pressBaselineFwd = Vector3.ProjectOnPlane(view.GetPreferredForward(), view.upAxis).normalized;

        // Store initial pose to reset if threshold not met
        _startParentPos = _target.Parent.position;
        _startParentRot = _target.Parent.rotation;
        _startMid = (_target.LeftAnchor.position + _target.RightAnchor.position) * 0.5f;

        // Bands hidden until we actually enter aiming
        view.SetBandsVisible(false);
        _isAiming = false;
    }

    private void SoftResetTowardStartPose()
    {
        if (_target?.Parent == null) return;

        // Pull back to where we started (keeps character from drifting without enough tension)
        const float posLerp = 0.25f;
        const float rotLerp = 0.25f;
        _target.Parent.position = Vector3.Lerp(_target.Parent.position, _startParentPos, posLerp);

        // Rotate toward initial baseline forward (press-time)
        Quaternion targetRot = Quaternion.LookRotation(_pressBaselineFwd, view.upAxis);
        _target.Parent.rotation = Quaternion.Slerp(_target.Parent.rotation, targetRot, rotLerp);
    }

    private void HardResetToStartPose()
    {
        if (_target?.Parent == null) return;

        _target.Parent.position = _startParentPos;
        _target.Parent.rotation = _startParentRot;
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

        view.SetBandsVisible(false);
        view.DrawBands(_target);
    }

    private void EnterAimingState()
    {
        _isAiming = true;
        _target?.SetKinematic(true);

        var t = _target.FollowTarget ? _target.FollowTarget : _target.Parent;
        OnEnterGameMode?.Invoke(t);
    }

    private void ExitAimingAndLaunch()
    {
        _isAiming = false;
        if (_target == null) return;

        Vector3 center = view.GetBandCenter();
        Vector3 rawDir = Vector3.ProjectOnPlane(center - _pullPoint, view.upAxis);
        if (rawDir.sqrMagnitude < 0.0001f) return;

        rawDir.Normalize();

        Vector3 baselineFwd = Vector3.ProjectOnPlane(view.GetPreferredForward(), view.upAxis).normalized;

        // Yaw clamp
        Vector3 clampedDir = ClampYawAroundUp(baselineFwd, rawDir, maxYawDegrees, view.upAxis);

        // Optional bias along ramp forward
        if (rampForwardRef != null)
        {
            Vector3 rampFwd = Vector3.ProjectOnPlane(rampForwardRef.forward, view.upAxis).normalized;
            clampedDir = Vector3.Slerp(clampedDir, rampFwd, 0.25f).normalized;
        }

        // float dist = Vector3.Distance(center, _pullPoint);
        // float pullDistance = Mathf.Clamp(dist, Mathf.Clamp(minPullDistance, 0f, maxPullDistance), maxPullDistance);


        // Use axis-based back distance
        //Vector3 baselineFwd = Vector3.ProjectOnPlane(view.GetPreferredForward(), view.upAxis).normalized;
        float backAxis = Mathf.Max(0f, Vector3.Dot(center - _pullPoint, baselineFwd)); // same as clampedBack at release time
        float pullDistance = Mathf.Clamp(backAxis, Mathf.Clamp(minPullDistance, 0f, maxPullDistance), maxPullDistance);

        var impulseValue = impulsePerMeter;

        if (useUpgradeData)
        {
            impulseValue = UpgradeSystem.Instance.GetValue(upgradeType);
        }

        float impulse = pullDistance * impulseValue;
        Debug.Log(impulseValue);
        Debug.Log(impulse);

        _target.SetKinematic(false);
        _target.Launch(clampedDir, impulse);

        var followT = _target.FollowTarget ? _target.FollowTarget : _target.Parent;
        OnShotStarted?.Invoke(followT);
    }

    private Vector3 AlignToLaunchDirection(Vector3 center, Vector3 currentPull, Vector3 baselineFwd)
    {
        if (_target?.Parent == null) return _lastClampedDir;

        Vector3 desiredDir = Vector3.ProjectOnPlane(center - currentPull, view.upAxis);
        if (desiredDir.sqrMagnitude < 0.0001f)
        {
            desiredDir = baselineFwd;
        }

        desiredDir.Normalize();

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
        ResetToSlingshot();
    }

    public void EndGame()
    {
        var t = _target != null ? (_target.FollowTarget ? _target.FollowTarget : _target.Parent) : null;
        OnEnterEndMode?.Invoke(t);
    }
}
