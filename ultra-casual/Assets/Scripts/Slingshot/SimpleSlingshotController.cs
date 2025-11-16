using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

[DisallowMultipleComponent]
public class SimpleSlingshotController : MonoBehaviour, IGameController
{
    [Header("Refs")]
    public SlingshotView view;
    public MonoBehaviour slingshotableObject;   // must implement ISlingshotable
    public SlingshotUIBridge uiBridge;          // optional: blocks input when over UI

    private ISlingshotable _target;

    [Header("Pull Settings")]
    [Tooltip("Max distance (meters) from the center you can pull.")]
    public float maxPullDistance = 5f;

    [Header("Movement Limits")]
    [Tooltip("Defines the Z cap and one end of the Y range for the player movement while aiming.")]
    public Transform movementStartLimit;

    [Tooltip("Defines the other end of the Y range for the player movement while aiming.")]
    public Transform movementEndYLimit;


    [Tooltip("Minimum distance (meters) required to actually fire on release.")]
    public float minPullDistance = 0.25f;

    [Tooltip("Clamp pull position within the poles span.")]
    public bool capWithinPoles = true;

    [Tooltip("Inset from the ends of the poles when clamping.")]
    public float polesEndInset = 0.05f;

    [Header("Launch Settings")]
    [Tooltip("Impulse strength per meter of pull, before angle-based multiplier.")]
    public float impulsePerMeter = 10f;

    [Tooltip("Minimum impulse applied, even for small pulls (if above minPullDistance).")]
    public float minImpulse = 5f;

    [Tooltip("Maximum impulse allowed (0 = no cap).")]
    public float maxImpulse = 0f;

    [Header("Yaw Limits")]
    [Tooltip("Maximum yaw (left/right) allowed at full pull (in degrees). " +
         "Near the start point, the allowed yaw is scaled down towards 0.")]
    public float maxYawAtFullPull = 60f;


    [Header("Arc / Angle")]
    [Tooltip("Launch angle (degrees above horizontal) when not pulling down at all.")]
    public float minLaunchAngleDeg = 10f;

    [Tooltip("Launch angle (degrees above horizontal) when pulling down to maxDownPullForMaxAngle.")]
    public float maxLaunchAngleDeg = 60f;

    [Tooltip("How far (world units on Y) you must pull DOWN from center to reach maxLaunchAngleDeg.")]
    public float maxDownPullForMaxAngle = 2f;

    [Header("Force vs Angle")]
    [Tooltip("Force multiplier when at minLaunchAngleDeg (typically 1).")]
    public float forceMultiplierAtMinAngle = 1f;

    [Tooltip("Force multiplier when at maxLaunchAngleDeg (typically < 1 to reduce force at high arc).")]
    public float forceMultiplierAtMaxAngle = 0.5f;

    [Header("Collision Preview")]
    [Tooltip("Enable simple collision preview using a raycast along the launch direction.")]
    public bool enableCollisionPreview = true;

    [Tooltip("Layers considered for preview collision.")]
    public LayerMask previewLayers = ~0;

    [Tooltip("Max distance of the preview raycast.")]
    public float previewMaxDistance = 50f;

    // Events
    public event Action<Transform> OnEnterEndMode;
    public event Action<Transform> OnEnterGameMode;
    public event Action<Transform> OnLaunchStarted;
    public event Action<Transform> OnShotStarted;
    public event Action<Transform> OnReleaseStarted;

    /// <summary>
    /// Called every time the preview collision changes.
    /// bool hasHit = any collision?
    /// RaycastHit hit = collision info if hasHit == true, otherwise default.
    /// </summary>
    public event Action<bool, RaycastHit> OnPreviewUpdated;

    // State
    private bool _isDragging;
    private Vector3 _centerOnPlane;        // band center on the plane
    private Vector3 _pullPoint;            // current pull point on the plane
    private Vector3 _launchDir;            // current launch direction (with arc)
    private float _launchImpulse;          // current launch impulse magnitude
    public float _currentLaunchAngleDeg;  // for debug
    private Vector3 _baselineForward;      // forward direction on the plane at drag start

    private Vector3 _startParentPos;
    private Quaternion _startParentRot;

    private bool enableInput = true;

    private void OnValidate()
    {
        if (maxPullDistance < 0f) maxPullDistance = 0f;
        if (minPullDistance < 0f) minPullDistance = 0f;
        if (minPullDistance > maxPullDistance) minPullDistance = maxPullDistance;
        if (previewMaxDistance < 0f) previewMaxDistance = 0f;

        // if (maxDownPullForMaxAngle < 0.01f) maxDownPullForMaxAngle = 0.01f;

        // // keep angles sane
        // if (maxLaunchAngleDeg < minLaunchAngleDeg)
        // {
        //     float tmp = maxLaunchAngleDeg;
        //     maxLaunchAngleDeg = minLaunchAngleDeg;
        //     minLaunchAngleDeg = tmp;
        // }
    }

    private void OnEnable()
    {
        _target = slingshotableObject as ISlingshotable;
        if (_target == null)
        {
            Debug.LogError("[SimpleSlingshotController] slingshotableObject must implement ISlingshotable.");
        }

        if (!view)
        {
            Debug.LogError("[SimpleSlingshotController] Missing SlingshotView reference.");
        }
    }

    private void Start()
    {
        _ = ResetToSlingshot();
        ApplyMovementLimits();
    }

    private void Update()
    {
        if (_target == null || view == null || !enableInput) return;
        if (_target.IsLaunching) return;

        HandleInput();
        if (_isDragging)
        {
            UpdateDrag();
        }
    }
    public void DisableInput()
    {
        enableInput = false;
    }
    public void EnableInput()
    {
        enableInput = true;
    }
    private void HandleInput()
    {
        // Pointer down
        if (Input.GetMouseButtonDown(0))
        {
            if (uiBridge != null && uiBridge.IsBlockedNow())
                return;

            BeginDrag();
        }

        // Pointer up
        if (Input.GetMouseButtonUp(0) && _isDragging)
        {
            EndDrag();
        }
    }

    private void BeginDrag()
    {
        if (_target.Parent == null || _target.LeftAnchor == null || _target.RightAnchor == null)
        {
            Debug.LogWarning("[SimpleSlingshotController] Missing target anchors/parent for drag.");
            return;
        }

        _isDragging = true;

        _centerOnPlane = view.GetBandCenter();   // where bands meet
        _pullPoint = _centerOnPlane;             // start at center

        // Baseline forward (used to forbid forward pulls)
        _baselineForward = Vector3.ProjectOnPlane(view.GetPreferredForward(), view.upAxis).normalized;
        if (_baselineForward.sqrMagnitude < 1e-4f)
        {
            _baselineForward = Vector3.ProjectOnPlane(_target.Parent.forward, view.upAxis).normalized;
        }
        if (_baselineForward.sqrMagnitude < 1e-4f)
        {
            _baselineForward = Vector3.forward; // last resort
        }

        // Cache original pose so we can cancel
        _startParentPos = _target.Parent.position;
        _startParentRot = _target.Parent.rotation;

        _target.SetKinematic(true);

        view.SetBandsVisible(true);
        view.DrawBands(_target);

        var t = _target.FollowTarget ? _target.FollowTarget : _target.Parent;
        OnEnterGameMode?.Invoke(t);
    }

    private void UpdateDrag()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        // 1. Mouse position on the slingshot plane
        Vector3 mouseOnPlane = view.GetMouseWorldOnPolesPlane(cam);

        // 2. Pull vector from center, constrained to plane
        Vector3 fromCenter = mouseOnPlane - _centerOnPlane;
        fromCenter = Vector3.ProjectOnPlane(fromCenter, view.upAxis);

        // --- ONLY ALLOW BACKWARDS / SIDEWAYS PULLS ---
        float forwardDot = Vector3.Dot(fromCenter, _baselineForward);
        if (forwardDot > 0f)
        {
            // Remove forward component so we never go "in front" of the center
            fromCenter -= _baselineForward * forwardDot;
        }

        float dist = fromCenter.magnitude;
        if (dist > 1e-4f)
        {
            // Radial clamp to max pull distance (after removing forward component)
            if (dist > maxPullDistance)
            {
                fromCenter = fromCenter / dist * maxPullDistance;
                dist = maxPullDistance;
            }
        }

        _pullPoint = _centerOnPlane + fromCenter;

        // Optional clamp between poles
        if (capWithinPoles)
        {
            _pullPoint = view.ClampPointBetweenPoles(_pullPoint, polesEndInset);
            // Recompute fromCenter/dist after clamp
            fromCenter = _pullPoint - _centerOnPlane;
            fromCenter = Vector3.ProjectOnPlane(fromCenter, view.upAxis);

            // Also make sure the clamp didn't push us forward
            forwardDot = Vector3.Dot(fromCenter, _baselineForward);
            if (forwardDot > 0f)
            {
                fromCenter -= _baselineForward * forwardDot;
                _pullPoint = _centerOnPlane + fromCenter;
            }

            dist = fromCenter.magnitude;
        }


        // 3. Move the slingshot object so the mid of the anchors is at the pull point
        if (_target.LeftAnchor && _target.RightAnchor && _target.Parent)
        {
            Vector3 currentMid = (_target.LeftAnchor.position + _target.RightAnchor.position) * 0.5f;
            Vector3 delta = _pullPoint - currentMid;
            _target.Parent.position += delta;
        }

        // >>> ADD THIS BLOCK HERE <<<
        ApplyMovementLimits();
        // <<< END NEW BLOCK >>>
        // 4. Compute launch direction with arc from down-pull + impulse with angle-based multiplier
        // 4. Compute launch direction with arc from down-pull + impulse with angle-based multiplier
        if (dist > 1e-4f)
        {
            // Horizontal direction (no vertical) from pull to center
            Vector3 flatDir = Vector3.ProjectOnPlane(_centerOnPlane - _pullPoint, view.upAxis);
            if (flatDir.sqrMagnitude < 1e-4f)
            {
                _launchDir = Vector3.zero;
                _launchImpulse = 0f;
            }
            else
            {
                flatDir.Normalize();

                // ---------- NEW: yaw clamp based on pull amount ----------
                // baseline forward is what we used to forbid forward pulls
                Vector3 baselineFwd = _baselineForward;
                if (baselineFwd.sqrMagnitude < 1e-4f)
                {
                    baselineFwd = flatDir;
                }

                // angle between baseline and our current flatDir
                float yawAngle = Vector3.SignedAngle(baselineFwd, flatDir, view.upAxis);

                // how much we pulled back relative to max
                float pull01 = Mathf.Clamp01(dist / maxPullDistance);

                // max yaw allowed for this pull amount
                float allowedYaw = maxYawAtFullPull * pull01;

                float clampedYaw = Mathf.Clamp(yawAngle, -allowedYaw, allowedYaw);
                Quaternion yawRot = Quaternion.AngleAxis(clampedYaw, view.upAxis);
                flatDir = (yawRot * baselineFwd).normalized;
                // ---------- END NEW YAW CLAMP ----------

                // How far did we pull DOWN from the center (world Y)? (center.y - pull.y)
                float verticalDown = Mathf.Max(0f, _centerOnPlane.y - _pullPoint.y);

                // 0 → maxDownPullForMaxAngle maps to minAngle → maxAngle
                float t = pull01;//Mathf.Clamp01(verticalDown / maxDownPullForMaxAngle);
                //Debug.Log(t);
                _currentLaunchAngleDeg = Mathf.Lerp(minLaunchAngleDeg, maxLaunchAngleDeg, t);

                // Angle-based force multiplier (high arc usually = less force)
                float forceMultiplier = Mathf.Lerp(
                    forceMultiplierAtMinAngle,
                    forceMultiplierAtMaxAngle,
                    t
                );

                // Tilt the flat direction UP by that angle around the right axis
                Vector3 rightAxis = Vector3.Cross(view.upAxis, flatDir);
                if (rightAxis.sqrMagnitude < 1e-4f)
                {
                    rightAxis = Vector3.right; // fallback
                }

                Quaternion tilt = Quaternion.AngleAxis(_currentLaunchAngleDeg, rightAxis.normalized);
                _launchDir = (tilt * flatDir).normalized;

                // impulse from pull distance, scaled by angle multiplier
                float impulse = dist * impulsePerMeter * forceMultiplier;
                impulse = Mathf.Max(impulse, minImpulse);
                if (maxImpulse > 0f)
                    impulse = Mathf.Min(impulse, maxImpulse);

                _launchImpulse = impulse;
            }
        }
        else
        {
            _launchDir = Vector3.zero;
            _launchImpulse = 0f;
        }

        // Optional: orient the character so its forward matches the horizontal part of launch dir
        if (_target.Parent && _launchDir != Vector3.zero)
        {
            Vector3 flatForward = Vector3.ProjectOnPlane(_launchDir, view.upAxis);
            if (flatForward.sqrMagnitude > 1e-4f)
            {
                flatForward.Normalize();
                _target.Parent.rotation = Quaternion.LookRotation(flatForward, view.upAxis);
            }
        }

        // 5. Draw bands
        view.DrawBands(_target);

        // 6. Collision preview (uses full _launchDir including arc)
        UpdateCollisionPreview();
    }

    /// <summary>
    /// Clamps the player parent position based on movementStartLimit (Z + Y)
    /// and movementEndYLimit (Y). 
    /// - Z is capped so you can never be in front of movementStartLimit.z
    /// - Y is clamped between startLimit.y and endYLimit.y
    /// </summary>
    private void ApplyMovementLimits()
    {
        if (_target?.Parent == null || movementStartLimit == null)
            return;

        Vector3 pos = _target.Parent.position;

        // --- Z cap: anything in front of this Z is ignored (clamped back) ---
        float startZ = movementStartLimit.position.z;
        if (pos.z > startZ)
        {
            pos.z = startZ;
        }

        // --- Y range between startLimit.y and endYLimit.y ---
        if (movementEndYLimit != null)
        {
            float yA = movementStartLimit.position.y;
            float yB = movementEndYLimit.position.y;
            float minY = Mathf.Min(yA, yB);
            float maxY = Mathf.Max(yA, yB);
            pos.y = Mathf.Clamp(pos.y, minY, maxY);
        }
        else
        {
            // If no end limit is provided, just don't go above the start Y.
            float startY = movementStartLimit.position.y;
            if (pos.y > startY)
            {
                pos.y = startY;
            }
        }

        _target.Parent.position = pos;
    }


    private void EndDrag()
    {
        _isDragging = false;
        view.SetBandsVisible(false);

        float pullDistance = Vector3.Distance(_centerOnPlane, _pullPoint);

        // Not enough pull: cancel and reset
        if (pullDistance < minPullDistance || _launchDir == Vector3.zero)
        {
            CancelShot();
            ClearPreview();
            return;
        }

        // Valid shot -> launch
        PerformLaunch();
    }

    private void PerformLaunch()
    {
        if (_target == null || _target.Parent == null)
            return;

        _target.SetKinematic(false);

        // Actually launch
        _target.Launch(_launchDir, _launchImpulse);

        view.PlaySnapFrom(_pullPoint);

        var followT = _target.FollowTarget ? _target.FollowTarget : _target.Parent;
        OnShotStarted?.Invoke(followT);
        OnReleaseStarted?.Invoke(followT);
        OnLaunchStarted?.Invoke(followT);

        ClearPreview();

        DisableInput();
    }

    private void CancelShot()
    {
        if (_target?.Parent == null) return;

        _target.Parent.position = _startParentPos;
        _target.Parent.rotation = _startParentRot;
        _target.SetKinematic(false);
    }

    private void UpdateCollisionPreview()
    {
        if (!enableCollisionPreview || _launchDir == Vector3.zero || _target?.Parent == null)
        {
            ClearPreview();
            return;
        }

        Vector3 origin = _target.Parent.position;
        RaycastHit hit;

        if (Physics.Raycast(origin, _launchDir, out hit, previewMaxDistance, previewLayers, QueryTriggerInteraction.Ignore))
        {
            OnPreviewUpdated?.Invoke(true, hit);
        }
        else
        {
            ClearPreview();
        }
    }

    private void ClearPreview()
    {
        OnPreviewUpdated?.Invoke(false, default);
    }

    // ---------------- IGameController ----------------

    public async UniTask ResetToSlingshot()
    {
        if (_target == null || view == null || _target.LeftAnchor == null || _target.RightAnchor == null || _target.Parent == null)
        {
            Debug.LogWarning("[SimpleSlingshotController] Missing references for ResetToSlingshot.");
            return;
        }

        _isDragging = false;

        // Put the mid of the anchors at the band center
        Vector3 center = view.GetBandCenter();
        Vector3 currentMid = (_target.LeftAnchor.position + _target.RightAnchor.position) * 0.5f;
        Vector3 delta = center - currentMid;
        _target.Parent.position += delta;

        // Face along view's preferred forward (flat)
        Vector3 forward = view.GetPreferredForward();
        if (forward.sqrMagnitude < 1e-4f)
            forward = _target.Parent.forward;

        forward = Vector3.ProjectOnPlane(forward, view.upAxis).normalized;
        if (forward.sqrMagnitude < 1e-4f) forward = _target.Parent.forward;

        _target.Parent.rotation = Quaternion.LookRotation(forward, view.upAxis);

        view.SetBandsVisible(true);

        await UniTask.Yield(PlayerLoopTiming.LastUpdate);
        view.DrawBands(_target);
    }

    public void ResetGameState()
    {
        _ = ResetToSlingshot();
    }

    public void EndGame()
    {
        var t = _target != null ? (_target.FollowTarget ? _target.FollowTarget : _target.Parent) : null;
        OnEnterEndMode?.Invoke(t);
    }

    // Preview accessors (read-only) -------------------------
    public bool HasValidPreview
    {
        get { return _isDragging && _launchDir != Vector3.zero; }
    }

    /// <summary>
    /// World-space origin of the shot used for preview.
    /// </summary>
    public Vector3 PreviewOrigin
    {
        get
        {
            if (_target != null && _target.Parent != null)
                return _target.Parent.position;

            return transform.position;
        }
    }

    /// <summary>
    /// World-space launch direction (already including arc).
    /// </summary>
    public Vector3 PreviewDirection
    {
        get { return _launchDir; }
    }

    /// <summary>
    /// Scalar "speed" used for the launch (same value you pass to Launch).
    /// </summary>
    public float PreviewSpeed
    {
        get { return _launchImpulse; }
    }

}
