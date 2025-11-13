using System;
using UnityEngine;

[DisallowMultipleComponent]
public class DelayedRagdollSwitcher : MonoBehaviour, ISlingshotable, IResettable
{
    [Header("Scene Refs")]
    public Animator animator;          // pose while aiming
    public RagdollRig rig;             // hips/pelvis rig collector
    public Rigidbody launcherBody;     // rounded single body used for initial flight (Sphere/Capsule)
    public Collider launcherCollider;  // should be SphereCollider or CapsuleCollider

    [Header("Slingshot Anchors")]
    public Transform parent;
    public Transform leftAnchor;
    public Transform rightAnchor;
    public Transform followTarget;

    [Header("Physics")]
    [Tooltip("Collision mode for launcher during the initial flight.")]
    public CollisionDetectionMode launcherCollisionMode = CollisionDetectionMode.ContinuousDynamic;

    [Tooltip("Angular damping while flying after switch. <0 to leave default.")]
    public float ragdollAngularDamping = 0.5f;

    [Tooltip("Copy the launcher's world velocity/angVel to all ragdoll bodies (when not using deterministic ramp).")]
    public bool inheritLauncherVelocity = true;

    [Tooltip("Recompute mass props on switch.")]
    public bool resetMassPropsOnSwitch = true;

    [Header("Deterministic Ramp Flight")]
    [Tooltip("Enable deterministic kinematic motion along a ramp when BeginDeterministicFlight is used.")]
    public bool useDeterministicRamp = true;

    [Tooltip("BoxCollider that represents the ramp volume.")]
    public BoxCollider rampCollider;

    [Tooltip("Where along the ramp the deterministic motion should start.")]
    public Transform rampStartTransform;

    [Tooltip("Extra distance past the ramp end before switching to ragdoll (in local Z of the ramp).")]
    public float rampExtraDistance = 0.1f;

    [Tooltip("Multiplier for how fast we move along the ramp. 0.1 = 10% of the launch speed.")]
    [Range(0.01f, 1f)]
    public float rampSpeedMultiplier = 1f;

    [Tooltip("Optional visual rotation for the launcher after switch.")]
    public float launchAngleOffset = 0f;

    // reset snapshot for the whole actor
    private Vector3 _startPos;
    private Vector3 _launcherBodyStartLocalPos;
    private Quaternion _startRot;
    private bool _hasSwitched;

    public Transform Parent => parent;
    public Transform LeftAnchor => leftAnchor;
    public Transform RightAnchor => rightAnchor;
    public Transform FollowTarget => followTarget != null ? followTarget : parent;

    public bool IsLaunching => _isLaunching;
    private bool _isLaunching;

    public event Action OnReleaseStart;
    public event Action OnLaunchStart;

    // -------- deterministic state using ramp collider --------
    private bool _inDeterministicFlight;
    private Vector3 _deterministicVelocity;   // scaled velocity used for ramp travel
    private float _localZ;                    // position along ramp Z (local space)
    private float _localX;                    // lateral offset along ramp X (local space)
    private float _forwardSpeed;              // component of launch velocity along ramp forward
    private float _sideSpeed;                 // component along ramp right
    private float _upSpeed;                   // component along ramp up
    private Vector3 _rampHalfExtents;         // half size of rampCollider
    public DirectionView directionView;         // half size of rampCollider

    private void Reset()
    {
        parent = transform;
    }

    private void Awake()
    {
        if (rig == null) rig = GetComponentInChildren<RagdollRig>(true);
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (parent == null) parent = transform;

        if (directionView == null)
            directionView = GetComponentInChildren<DirectionView>(true);

        _startPos = transform.position;
        _startRot = transform.rotation;

        if (rig != null)
        {
            if (rig.Bodies.Count == 0) rig.Collect();
            rig.BakePoseSnapshot();
        }

        // Ensure launcher references
        if (launcherBody == null && launcherCollider != null)
        {
            launcherBody = launcherCollider.attachedRigidbody;
        }
        if (launcherBody != null)
        {
            _launcherBodyStartLocalPos = launcherBody.transform.localPosition;
        }

        if (rampCollider != null)
        {
            _rampHalfExtents = 0.5f * rampCollider.size;
        }

        // Hook relay on the launcher collider so we can hear Starter triggers
        AttachOrConfigureRelay();

        // Start in aiming state
        EnterAimingPose();
        EnableLauncher(false);

        if (animator != null)
        {
            animator.SetTrigger("reset");
        }
    }

    private void FixedUpdate()
    {
        if (_inDeterministicFlight)
        {
            UpdateDeterministicRampMotion();
        }
    }

    private void AttachOrConfigureRelay()
    {
        if (launcherCollider == null) return;

        var relay = launcherCollider.GetComponent<LauncherTriggerRelay>();
        if (relay == null)
        {
            relay = launcherCollider.gameObject.AddComponent<LauncherTriggerRelay>();
        }
        relay.Initialize(this);
    }

    // ---------------- ISlingshotable ----------------

    public void SetKinematic(bool isKinematic)
    {
        // Aiming phase uses kinematic ragdoll + animator on
        if (isKinematic)
        {
            EnterAimingPose();
        }
        // Non-kinematic is controlled by Launch + switch or deterministic flight
    }

    /// <summary>
    /// LEGACY physics-based launch: uses the launcherBody rigidbody and AddForce.
    /// Still available if you want full-physics randomness or as fallback.
    /// </summary>
    public void Launch(Vector3 direction, float impulse)
    {
        if (rig == null || launcherBody == null || launcherCollider == null)
        {
            Debug.LogWarning("[RagdollSwitcher] Missing rig/launcher references.");
            return;
        }

        _hasSwitched = false;
        _inDeterministicFlight = false;
        _deterministicVelocity = Vector3.zero;

        // 1) Prepare: animator OFF, ragdoll frozen (kinematic), launcher ON
        if (animator != null) animator.enabled = false;

        rig.SetKinematic(true);
        rig.ZeroVelocities();

        // Position launcher at root pose (so the shot starts aligned)
        launcherBody.isKinematic = false;
        launcherBody.useGravity = false;
        launcherBody.position = transform.position;
        launcherBody.rotation = transform.rotation;
        launcherBody.linearVelocity = Vector3.zero;
        launcherBody.angularVelocity = Vector3.zero;
        launcherBody.isKinematic = true;
        launcherBody.collisionDetectionMode = launcherCollisionMode;

        EnableLauncher(true);

        if (directionView != null)
            directionView.Hide();

        // 2) Fire the launcher
        var dir = direction.sqrMagnitude > 0f ? direction.normalized : transform.forward;
        launcherBody.isKinematic = false;
        launcherBody.useGravity = true;
        launcherBody.AddForce(dir * impulse, ForceMode.VelocityChange);

        _isLaunching = true;

        OnReleaseStart?.Invoke();
    }

    /// <summary>
    /// NEW deterministic launch: no physics during ramp, pure kinematic movement.
    /// Called by the SlingshotController with a velocity vector.
    /// </summary>
    public void BeginDeterministicFlight(Vector3 launchVelocity)
    {
        if (!useDeterministicRamp || rampCollider == null)
        {
            // Fallback: interpret magnitude as impulse in old Launch
            Launch(launchVelocity.normalized, launchVelocity.magnitude);
            return;
        }

        if (rig == null)
        {
            Debug.LogWarning("[DelayedRagdollSwitcher] Rig is missing; cannot do deterministic flight.");
            return;
        }

        if (launchVelocity.sqrMagnitude < 0.0001f)
        {
            Debug.LogWarning("[DelayedRagdollSwitcher] Launch velocity is too small.");
            return;
        }

        _hasSwitched = false;
        _inDeterministicFlight = true;
        _isLaunching = true;

        // Slower velocity only for sliding along ramp
        Vector3 scaledVel = launchVelocity * rampSpeedMultiplier;
        _deterministicVelocity = scaledVel;

        var rampTr = rampCollider.transform;
        Vector3 fwd = rampTr.forward.normalized;
        Vector3 right = rampTr.right.normalized;
        Vector3 up = rampTr.up.normalized;

        _forwardSpeed = Vector3.Dot(scaledVel, fwd);
        _sideSpeed = Vector3.Dot(scaledVel, right);
        _upSpeed = Vector3.Dot(scaledVel, up);

        // Ensure we move forward along the ramp; if backwards, flip
        if (_forwardSpeed <= 0f)
        {
            _forwardSpeed = Mathf.Abs(_forwardSpeed);
            _sideSpeed = -_sideSpeed;
            _upSpeed = -_upSpeed;
        }

        // Turn off animator, keep ragdoll bodies frozen
        if (animator != null)
        {
            animator.enabled = false;
        }

        rig.SetKinematic(true);
        rig.ZeroVelocities();

        if (directionView != null)
            directionView.Hide();

        // Disable launcher during deterministic phase
        EnableLauncher(false);
        if (launcherBody != null)
        {
            launcherBody.useGravity = false;
            launcherBody.isKinematic = true;
            launcherBody.linearVelocity = Vector3.zero;
            launcherBody.angularVelocity = Vector3.zero;
        }

        // Compute starting local X/Z from rampStartTransform (or fallback)
        float margin = 0.05f;
        if (rampStartTransform != null)
        {
            // Get local position of the start transform inside the ramp collider space
            Vector3 local = rampTr.InverseTransformPoint(rampStartTransform.position) - rampCollider.center;

            _localX = Mathf.Clamp(local.x, -_rampHalfExtents.x + margin, _rampHalfExtents.x - margin);
            _localZ = Mathf.Clamp(local.z, -_rampHalfExtents.z, _rampHalfExtents.z);
        }
        else
        {
            // Fallback: start at back center if no transform assigned
            _localZ = -_rampHalfExtents.z;
            _localX = 0f;
        }

        // Place character at that point on the ramp surface (Y=0 in ramp local)
        Vector3 localStart = new Vector3(_localX, 0f, _localZ);
        Vector3 worldStart = rampTr.TransformPoint(localStart + rampCollider.center);
        parent.position = worldStart;

        // Orientation based on launch direction projected on ramp plane
        Vector3 planarVel = fwd * _forwardSpeed + right * _sideSpeed;
        Vector3 dir = planarVel.sqrMagnitude > 0.0001f ? planarVel.normalized : fwd;
        parent.rotation = Quaternion.LookRotation(dir, up);

        OnReleaseStart?.Invoke();
    }

    public void ApplyForce(
        Vector3 worldForce,
        ForceMode mode = ForceMode.Impulse,
        Vector3? applicationPoint = null,
        bool forceSwitchToRagdoll = false,
        bool hipsOnly = false,
        bool toLauncher = false
    )
    {
        // If deterministic ramp is active and we want to force switch, do it first
        if (forceSwitchToRagdoll && !_hasSwitched)
        {
            ForceSwitchToRagdoll();
        }

        if (toLauncher && launcherBody != null && !_hasSwitched)
        {
            if (applicationPoint.HasValue)
            {
                launcherBody.AddForceAtPosition(worldForce, applicationPoint.Value, mode);
            }
            else
            {
                launcherBody.AddForce(worldForce, mode);
            }
            return;
        }

        // If we still haven't switched and not targeting launcher, default to launcher (legacy flow)
        if (!_hasSwitched)
        {
            if (launcherBody != null)
            {
                if (applicationPoint.HasValue)
                {
                    launcherBody.AddForceAtPosition(worldForce, applicationPoint.Value, mode);
                }
                else
                {
                    launcherBody.AddForce(worldForce, mode);
                }
            }
            return;
        }

        // Ragdoll application
        if (rig == null || rig.Bodies == null || rig.Bodies.Count == 0)
        {
            return;
        }

        if (hipsOnly)
        {
            var hips = rig.Hips != null ? rig.Hips : rig.Bodies[0];
            if (applicationPoint.HasValue)
            {
                hips.AddForceAtPosition(worldForce, applicationPoint.Value, mode);
            }
            else
            {
                hips.AddForce(worldForce, mode);
            }
            return;
        }

        // Spread the force across all bodies to avoid extreme impulses on a single link
        int count = rig.Bodies.Count;
        if (count <= 0)
        {
            return;
        }

        Vector3 perBody = worldForce / count;
        if (applicationPoint.HasValue)
        {
            Vector3 p = applicationPoint.Value;
            for (int i = 0; i < count; i++)
            {
                var b = rig.Bodies[i];
                b.AddForceAtPosition(perBody, p, mode);
            }
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                var b = rig.Bodies[i];
                b.AddForce(perBody, mode);
            }
        }
    }
    public void UpdateAimDirection(Vector3 worldDirection, float pullForce)
    {
        if (directionView == null)
            return;

        // Only show while we're not in flight / ragdolled
        if (!_hasSwitched && !_inDeterministicFlight)
        {
            directionView.SetDirection(worldDirection, pullForce);
        }
    }
    private void UpdateDeterministicRampMotion()
    {
        if (rampCollider == null)
        {
            _inDeterministicFlight = false;
            ForceSwitchToRagdoll();
            return;
        }

        var rampTr = rampCollider.transform;
        Vector3 fwd = rampTr.forward.normalized;
        Vector3 right = rampTr.right.normalized;
        Vector3 up = rampTr.up.normalized;

        float dt = Time.fixedDeltaTime;

        // Move along ramp Z and sideways X using the (scaled) speeds
        _localZ += _forwardSpeed * dt;
        _localX += _sideSpeed * dt;

        float margin = 0.05f;
        float maxX = _rampHalfExtents.x - margin;
        _localX = Mathf.Clamp(_localX, -maxX, maxX);

        Vector3 localPos = new Vector3(_localX, 0f, _localZ);
        Vector3 worldPos = rampTr.TransformPoint(localPos + rampCollider.center);
        parent.position = worldPos;

        Vector3 planarVel = fwd * _forwardSpeed + right * _sideSpeed;
        Vector3 dir = planarVel.sqrMagnitude > 0.0001f ? planarVel.normalized : fwd;
        parent.rotation = Quaternion.LookRotation(dir, up);

        // When we pass front of ramp + extra, switch to ragdoll
        if (_localZ >= _rampHalfExtents.z + rampExtraDistance)
        {
            _inDeterministicFlight = false;
            ForceSwitchToRagdoll();
        }
    }

    // ---------------- External switch trigger ----------------

    /// <summary>
    /// Called by LauncherTriggerRelay when the launcher enters a trigger tagged "Starter".
    /// </summary>
    public void OnLauncherHitStarterTrigger(Collider starterTrigger)
    {
        ForceSwitchToRagdoll();
    }

    /// <summary>
    /// Public API to switch immediately from launcher/deterministic to ragdoll.
    /// Safe to call once; no-ops if already switched or if references are missing.
    /// </summary>
    public void ForceSwitchToRagdoll()
    {
        if (_hasSwitched || rig == null) return;

        if (animator != null)
        {
            animator.SetTrigger("jump");
        }

        _hasSwitched = true;
        _inDeterministicFlight = false;

        // Snap root to current pose
        rig.transform.localPosition = Vector3.zero;

        // Enable physics on ragdoll
        rig.SetKinematic(false);

        Vector3 lin = Vector3.zero;
        Vector3 ang = Vector3.zero;

        // If we came from deterministic ramp, reconstruct velocity from components
        if (_deterministicVelocity != Vector3.zero && useDeterministicRamp && rampCollider != null)
        {
            var rampTr = rampCollider.transform;
            Vector3 fwd = rampTr.forward.normalized;
            Vector3 right = rampTr.right.normalized;
            Vector3 up = rampTr.up.normalized;

            Vector3 planar = fwd * _forwardSpeed + right * _sideSpeed;
            Vector3 vertical = up * _upSpeed;

            lin = planar;// + vertical;
            ang = Vector3.zero; // you can add some spin if you want
        }
        // Otherwise fall back to launcher-based velocity inheritance
        else if (inheritLauncherVelocity && launcherBody != null)
        {
            lin = launcherBody.linearVelocity;
            ang = launcherBody.angularVelocity;
        }

        if (inheritLauncherVelocity)
        {
            foreach (var b in rig.Bodies)
            {
                b.linearVelocity = lin;
                b.angularVelocity = ang;
                if (ragdollAngularDamping >= 0f)
                {
                    b.angularDamping = ragdollAngularDamping; // Unity 6 property
                }
            }
        }

        if (resetMassPropsOnSwitch)
        {
            rig.ResetMassProps();
        }

        // Disable launcher so only the ragdoll collides
        if (launcherBody != null)
        {
            launcherBody.useGravity = false;
            launcherBody.linearVelocity = Vector3.zero;
            launcherBody.angularVelocity = Vector3.zero;
            launcherBody.isKinematic = true;

            // Optional: rotate launcher visual if you still want it
            launcherBody.transform.rotation = Quaternion.Euler(launchAngleOffset, 0, 0);
        }

        EnableLauncher(false);

        OnLaunchStart?.Invoke();
    }

    // ---------------- IResettable ----------------

    public void ResetToInitial()
    {
        _hasSwitched = false;
        _isLaunching = false;
        _inDeterministicFlight = false;
        _deterministicVelocity = Vector3.zero;

        // Freeze everything
        if (rig != null)
        {
            rig.SetKinematic(true);
            rig.ZeroVelocities();
            rig.RestorePoseSnapshot();
            rig.ResetMassProps();
        }

        // Reset transform
        if (rig != null)
            rig.transform.localPosition = Vector3.zero;

        transform.SetPositionAndRotation(_startPos, _startRot);

        // Reset launcher
        if (launcherBody != null)
        {
            launcherBody.useGravity = false;
            launcherBody.linearVelocity = Vector3.zero;
            launcherBody.angularVelocity = Vector3.zero;
            launcherBody.isKinematic = true;
            launcherBody.transform.localPosition = _launcherBodyStartLocalPos;
            launcherBody.transform.rotation = Quaternion.identity;

            EnableLauncher(false);
        }

        if (directionView != null)
            directionView.Hide();

        // Re-arm relay (in case object was disabled or swapped)
        AttachOrConfigureRelay();

        // Back to aim
        EnterAimingPose();
    }

    // ---------------- Helpers ----------------

    private void EnterAimingPose()
    {
        if (rig != null)
        {
            rig.SetKinematic(true);
            rig.ZeroVelocities();
        }

        if (animator != null)
        {
            animator.enabled = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.ResetTrigger("jump");
            animator.SetTrigger("reset");
            animator.Update(1f);
        }
    }

    private void EnableLauncher(bool on)
    {
        if (launcherCollider != null)
        {
            launcherCollider.enabled = on;

            if (on)
            {
                _isLaunching = true;
            }
        }

        if (launcherBody != null)
        {
            launcherBody.collisionDetectionMode =
                on ? launcherCollisionMode : CollisionDetectionMode.Discrete;
        }
    }
}
