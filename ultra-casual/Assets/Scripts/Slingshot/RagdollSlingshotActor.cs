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

    [Tooltip("Copy the launcher's world velocity/angVel to all ragdoll bodies.")]
    public bool inheritLauncherVelocity = true;

    [Tooltip("Recompute mass props on switch.")]
    public bool resetMassPropsOnSwitch = true;

    // reset snapshot for the whole actor
    private Vector3 _startPos;
    private Vector3 _launcherBodyStartLocalPos;
    private Quaternion _startRot;
    private bool _followLauncher;
    private bool _hasSwitched;


    public float launchAngleOffset = 0f;

    public Transform Parent => parent;
    public Transform LeftAnchor => leftAnchor;
    public Transform RightAnchor => rightAnchor;
    public Transform FollowTarget => followTarget != null ? followTarget : parent;

    public bool IsLaunching => _isLaunching;

    private bool _isLaunching = false;

    public event Action OnReleaseStart;
    public event Action OnLaunchStart;

    private void Reset()
    {
        parent = transform;
    }

    private void Awake()
    {
        if (rig == null) rig = GetComponentInChildren<RagdollRig>(true);
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (parent == null) parent = transform;

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

        // Hook relay on the launcher collider so we can hear Starter triggers
        AttachOrConfigureRelay();

        // Start in aiming state
        EnterAimingPose();
        EnableLauncher(false);

        animator.SetTrigger("reset");

    }

    private void LateUpdate()
    {
        // While we haven't switched, keep the character glued to the launcher
        if (_followLauncher && !_hasSwitched && launcherBody != null)
        {
            //rig.transform.localPosition = Vector3.Lerp(rig.transform.localPosition, launcherBody.transform.localPosition, 0.15f);//(launcherBody.position, Quaternion.identity);
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
        // Non-kinematic is controlled by Launch + switch
    }

    public void Launch(Vector3 direction, float impulse)
    {
        if (rig == null || launcherBody == null || launcherCollider == null)
        {
            Debug.LogWarning("[RagdollSwitcher] Missing rig/launcher references.");
            return;
        }

        _hasSwitched = false;
        _followLauncher = true;
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

        // 2) Fire the launcher
        var dir = direction.sqrMagnitude > 0f ? direction.normalized : transform.forward;
        launcherBody.isKinematic = false;
        launcherBody.useGravity = true;
        launcherBody.AddForce(dir * impulse, ForceMode.VelocityChange);

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
        if (toLauncher && launcherBody != null)
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

        // If requested, switch before applying to ragdoll
        if (forceSwitchToRagdoll && !_hasSwitched)
        {
            ForceSwitchToRagdoll();
        }

        // If we still haven't switched and not targeting launcher, default to launcher
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
    // ---------------- External switch trigger ----------------

    /// <summary>
    /// Called by LauncherTriggerRelay when the launcher enters a trigger tagged "Starter".
    /// </summary>
    public void OnLauncherHitStarterTrigger(Collider starterTrigger)
    {
        ForceSwitchToRagdoll();
    }

    /// <summary>
    /// Public API to switch immediately from launcher to ragdoll.
    /// Safe to call once; no-ops if already switched or if references are missing.
    /// </summary>
    public void ForceSwitchToRagdoll()
    {
        if (_hasSwitched) return;
        if (rig == null || launcherBody == null) return;

        animator.SetTrigger("jump");

        _hasSwitched = true;
        _followLauncher = false;
        // Snap root to launcher pose
        rig.transform.localPosition = Vector3.zero;
        //transform.SetPositionAndRotation(launcherBody.position, Quaternion.identity);

        // Enable physics on ragdoll
        rig.SetKinematic(false);

        if (inheritLauncherVelocity)
        {
            var lin = launcherBody.linearVelocity;
            var ang = launcherBody.angularVelocity;

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
        launcherBody.isKinematic = false;
        launcherBody.useGravity = false;
        launcherBody.linearVelocity = Vector3.zero;
        launcherBody.angularVelocity = Vector3.zero;
        launcherBody.isKinematic = true;
        EnableLauncher(false);

        //launcherBody.MoveRotation(Quaternion.Euler(launchAngleOffset, 0, 0));
        launcherBody.transform.rotation = (Quaternion.Euler(launchAngleOffset, 0, 0));

        OnLaunchStart?.Invoke();
    }

    // ---------------- IResettable ----------------

    public void ResetToInitial()
    {
        _hasSwitched = false;
        _isLaunching = false;
        // Freeze everything
        if (rig != null)
        {
            rig.SetKinematic(true);
            rig.ZeroVelocities();
            rig.RestorePoseSnapshot();
            rig.ResetMassProps();
        }

        // Reset transform
        rig.transform.localPosition = Vector3.zero;
        transform.SetPositionAndRotation(_startPos, _startRot);
        // Reset launcher
        if (launcherBody != null)
        {
            launcherBody.useGravity = false;
            launcherBody.isKinematic = false;
            launcherBody.linearVelocity = Vector3.zero;
            launcherBody.angularVelocity = Vector3.zero;
            launcherBody.isKinematic = true;
            launcherBody.transform.localPosition = _launcherBodyStartLocalPos;
            launcherBody.transform.rotation = Quaternion.identity;

            EnableLauncher(false);
        }

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
            // Make sure the collider is set as a *non-trigger* for flight if you expect physics collisions,
            // but it still receives OnTriggerEnter when overlapping trigger volumes.
            // (i.e., launcherCollider.isTrigger = false;)
        }

        if (launcherBody != null)
        {
            launcherBody.collisionDetectionMode = on ? launcherCollisionMode : CollisionDetectionMode.Discrete;
        }
    }
}

