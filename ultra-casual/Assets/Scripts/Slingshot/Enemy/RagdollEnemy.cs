using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class RagdollEnemy : MonoBehaviour, IResettable
{
    [Header("Core")]
    [Tooltip("Ragdoll switcher that owns the rig/launcher and knows how to switch/restore.")]
    public DelayedRagdollSwitcher ragdollSwitcher;

    [Tooltip("All renderers that should swap material on death (MeshRenderer / SkinnedMeshRenderer).")]
    public Renderer[] targetRenderers;

    [Tooltip("Optional: The Rigidbody used for movement/launching. If null, tries to use ragdollSwitcher.launcherBody.")]
    public Rigidbody sourceBody;

    [Header("Animator (will be disabled on ragdoll/death)")]
    public Animator animator;

    [Header("Materials")]
    public Material activeMaterial;
    public Material deathMaterial;

    [Header("Type & Kill Logic")]
    public EnemyGrade grade = EnemyGrade.Standard;

    [Tooltip("Override the grade-based kill impulse. If <= 0, grade default is used.")]
    public float killImpulseOverride = -1f;

    [Tooltip("If true, any collision (non-trigger) kills instantly regardless of impulse.")]
    public bool killOnAnyCollision = false;

    [Tooltip("If true, entering any trigger kills instantly (useful for 'hazard' volumes).")]
    public bool killOnAnyTrigger = false;

    [Header("Optional Proximity Trigger Kill")]
    [Tooltip("If assigned, entering THIS trigger kills (e.g., a big capsule/box surrounding the enemy).")]
    public Collider ownKillTrigger; // must be isTrigger=true

    [Header("Fall Detection")]
    [Tooltip("Layers considered 'ground' for edge/fall checks.")]
    public LayerMask groundMask = ~0;

    [Tooltip("Local offset from this transform for the ground probe origin.")]
    public Vector3 groundProbeLocalOffset = new Vector3(0, 0.2f, 0);

    [Tooltip("Sphere radius for ground probe.")]
    public float groundProbeRadius = 0.15f;

    [Tooltip("How far below the probe to search for ground.")]
    public float groundProbeDistance = 0.5f;

    [Tooltip("Minimum downward speed to consider it a fall (m/s).")]
    public float minFallSpeed = 3.0f;

    [Tooltip("How long we must be ungrounded and falling before killing (seconds).")]
    public float fallGraceTime = 0.12f;

    [Tooltip("Only auto-kill on fall if we were grounded just before losing ground.")]
    public bool requireWasGroundedBeforeFalling = true;

    [Header("Support (platform under me) → ragdoll triggers")]
    [Tooltip("If the rigidbody under me moves faster than this, go ragdoll (non-fatal).")]
    public float supportMoveSpeedThreshold = 0.75f;

    [Tooltip("If the rigidbody under me spins faster than this (deg/s), go ragdoll.")]
    public float supportAngularSpeedDegThreshold = 90f;

    [Tooltip("If I lose contact with my support by more than this distance immediately below, go ragdoll.")]
    public float supportSeparationDistance = 0.2f;

    [Header("Fall → Death on Landing")]
    [Tooltip("Minimum own speed just before contact to die on landing.")]
    public float fallImpactSpeedThreshold = 6f;

    private bool _fallArmed;          // we've been in a legit fall and next collision can kill
    private Vector3 _prevVelocity;    // cached before physics step resolves collisions



    [Header("Events")]
    public UnityEvent onDeath;                // inspector-friendly
    public event Action<RagdollEnemy> OnDied; // code-friendly

    private bool _isDead;
    private bool _isRagdolled;
    private float _killImpulseThreshold;

    // grade defaults
    private const float WEAK_KILL_IMPULSE = 3.0f;
    private const float STANDARD_KILL_IMPULSE = 7.5f;
    private const float HEAVY_KILL_IMPULSE = 14.0f;

    // fall state
    private bool _isGrounded;
    private bool _wasGrounded;
    private float _ungroundedTimer;

    // support tracking
    private Rigidbody _supportRb;
    private Transform _supportTf;
    private Vector3 _supportLastPos;
    private Quaternion _supportLastRot;
    private readonly HashSet<Rigidbody> _ownBodies = new();

    void CacheOwnRigidbodies()
    {
        _ownBodies.Clear();
        var bodies = GetComponentsInChildren<Rigidbody>(true);
        foreach (var b in bodies) if (b != null) _ownBodies.Add(b);
    }
    void Reset()
    {
        ragdollSwitcher = GetComponent<DelayedRagdollSwitcher>();
        targetRenderers = GetComponentsInChildren<Renderer>(true);
        if (!sourceBody) sourceBody = GetComponent<Rigidbody>();
        if (!animator) animator = GetComponentInChildren<Animator>();
    }

    void Awake()
    {
        AutoAttachForwarders();
        if (ragdollSwitcher == null)
            ragdollSwitcher = GetComponent<DelayedRagdollSwitcher>();

        if (sourceBody == null && ragdollSwitcher != null)
            sourceBody = ragdollSwitcher.launcherBody;

        _killImpulseThreshold = ComputeKillThreshold();

        ApplyAliveMaterial();
        _isDead = false;
        _isRagdolled = false;

        if (animator) animator.enabled = true;

        // safety: ensure ownKillTrigger is trigger
        if (ownKillTrigger != null) ownKillTrigger.isTrigger = true;

        // probe for supporting rigidbody right away
        ProbeSupportUnderfoot();
    }
    void AutoAttachForwarders()
    {
        var cols = GetComponentsInChildren<Collider>(true);
        foreach (var col in cols)
        {
            // Skip triggers; only physical contacts matter here
            if (col.isTrigger) continue;

            // Already has one?
            if (col.GetComponent<CollisionForwarder>() != null) continue;

            var fwd = col.gameObject.AddComponent<CollisionForwarder>();
            fwd.owner = this;
        }
    }
    void FixedUpdate()
    {
        if (_isDead) return;


        _prevVelocity = sourceBody ? sourceBody.linearVelocity : Vector3.zero;
        // --- Ground probe (spherecast) ---
        Vector3 origin = transform.TransformPoint(groundProbeLocalOffset);
        float castDist = Mathf.Max(groundProbeDistance, 0.01f);

        bool hitGround = Physics.SphereCast(
            origin,
            groundProbeRadius,
            Vector3.down,
            out var hit,
            castDist,
            groundMask,
            QueryTriggerInteraction.Ignore
        );

        _wasGrounded = _isGrounded;
        _isGrounded = hitGround;

        // If we had no support cached yet, try to capture from the ground hit
        if (_supportRb == null && hit.collider != null && hit.collider.attachedRigidbody != null)
        {
            CacheSupport(hit.collider.attachedRigidbody);
        }

        // --- Support move/fall/separation → enter ragdoll (non-fatal) ---
        if (!_isRagdolled && _supportRb != null)
        {
            bool supportMoved = _supportRb.linearVelocity.sqrMagnitude >= (supportMoveSpeedThreshold * supportMoveSpeedThreshold);
            bool supportSpun = _supportRb.angularVelocity.magnitude * Mathf.Rad2Deg >= supportAngularSpeedDegThreshold;

            // separation: check if immediate raycast down still finds that same support within small distance
            bool separated = false;
            if (hitGround)
            {
                var hitRb = hit.collider.attachedRigidbody;
                if (hitRb != _supportRb && hit.distance > supportSeparationDistance)
                    separated = true;
            }
            else
            {
                // not grounded at all = separated
                separated = true;
            }

            if (supportMoved || supportSpun || separated)
            {
                EnterRagdoll(); // animator off, physics takes over, but NOT dead yet
            }
        }

        // --- Falling logic → death only if falling fast for a short grace ---
        if (!_isGrounded)
        {
            _ungroundedTimer += Time.fixedDeltaTime;

            float vy = sourceBody ? sourceBody.linearVelocity.y : 0f;
            bool fallingFast = vy <= -Mathf.Abs(minFallSpeed);

            bool validFallStart = requireWasGroundedBeforeFalling ? _wasGrounded : true;

            if (validFallStart && fallingFast && _ungroundedTimer >= fallGraceTime)
            {
                Kill();
                return;
            }
        }
        else
        {
            _ungroundedTimer = 0f;
        }
    }

    // --- initial support detection ---
    private void ProbeSupportUnderfoot()
    {
        Vector3 origin = transform.TransformPoint(groundProbeLocalOffset);
        if (Physics.SphereCast(
            origin,
            groundProbeRadius,
            Vector3.down,
            out var hit,
            Mathf.Max(groundProbeDistance, 0.01f),
            groundMask,
            QueryTriggerInteraction.Ignore))
        {
            if (hit.collider != null && hit.collider.attachedRigidbody != null)
            {
                CacheSupport(hit.collider.attachedRigidbody);
            }
        }
    }

    private void CacheSupport(Rigidbody rb)
    {
        _supportRb = rb;
        _supportTf = rb.transform;
        _supportLastPos = _supportTf.position;
        _supportLastRot = _supportTf.rotation;
    }

    private void ClearSupport()
    {
        _supportRb = null;
        _supportTf = null;
    }

    private void EnterRagdoll()
    {
        if (_isRagdolled) return;
        _isRagdolled = true;

        if (animator) animator.enabled = false;
        if (ragdollSwitcher != null)
            ragdollSwitcher.ForceSwitchToRagdoll(); // pose into ragdoll, no material swap, no death flag
    }

    private float ComputeKillThreshold()
    {
        if (killImpulseOverride > 0f) return killImpulseOverride;

        switch (grade)
        {
            case EnemyGrade.Weak: return WEAK_KILL_IMPULSE;
            case EnemyGrade.Heavy: return HEAVY_KILL_IMPULSE;
            default: return STANDARD_KILL_IMPULSE;
        }
    }

    // --- Collision-based death (like Angry Birds)
    private void OnCollisionEnter(Collision collision)
    {
        if (_isDead) return;

        if (_fallArmed)
        {
            float mySpeed = _prevVelocity.magnitude;
            if (mySpeed >= fallImpactSpeedThreshold)
            {
                Kill();
                return;
            }
            // landed but not hard enough: disarm so tiny bumps after landing don’t kill
            _fallArmed = false;
        }

        if (killOnAnyCollision)
        {
            Kill();
            return;
        }

        // Use physics impulse magnitude as a simple damage proxy
        float impulseMag = collision.impulse.magnitude;
        if (impulseMag >= _killImpulseThreshold)
        {
            Kill();
        }
    }

    internal void OnCollisionFromChild(Collision c, Rigidbody childRb)
    {
        if (_isDead) return;

        // Ignore self-collisions (limb vs limb or with our root body)
        var otherRb = c.rigidbody; // the OTHER body’s RB (can be null if static)
        if (otherRb != null && _ownBodies.Contains(otherRb)) return;

        // 1) Death on landing: we armed a fall; use our own pre-collision speed OR the limb’s current speed.
        if (_fallArmed)
        {
            float childSpeed = (childRb != null) ? childRb.linearVelocity.magnitude : 0f;
            float mySpeed = (sourceBody != null) ? _prevVelocity.magnitude : childSpeed;
            float landingSpeed = Mathf.Max(mySpeed, childSpeed, c.relativeVelocity.magnitude);

            Debug.Log($"RagdollEnemy landed with speed {landingSpeed:F2} (threshold {fallImpactSpeedThreshold:F2})");

            if (landingSpeed >= fallImpactSpeedThreshold)
            {
                Kill();
                return;
            }

            // Landed but not hard enough — disarm so tiny bumps don’t chain-kill
            _fallArmed = false;
        }

        // 2) Non-fall impacts: use impulse threshold like your root collision path
        if (killOnAnyCollision)
        {
            Kill();
            return;
        }

        // c.impulse is summed impulse for this contact pair this step
        float impulseMag = c.impulse.magnitude;
        if (impulseMag >= _killImpulseThreshold)
        {
            Kill();
            return;
        }

        // else: light tap → ignore
    }
    private void OnTriggerEnter(Collider other)
    {
        if (_isDead) return;

        if (_fallArmed)
        {
            float mySpeed = _prevVelocity.magnitude;
            if (mySpeed >= fallImpactSpeedThreshold)
            {
                Kill();
                return;
            }
            // landed but not hard enough: disarm so tiny bumps after landing don’t kill
            _fallArmed = false;
        }

        // ignore self/children
        if (other.transform.IsChildOf(transform))
            return;

        // must have Rigidbody (only things that can apply force)
        var rb = other.attachedRigidbody;
        if (rb == null) return;

        // ignore if it's our own rigidbody
        if (rb == sourceBody) return;

        // relative motion toward me?
        var myRb = sourceBody ? sourceBody : GetComponent<Rigidbody>();
        Vector3 relativeVelocity = myRb ? (rb.linearVelocity - myRb.linearVelocity) : rb.linearVelocity;
        Vector3 toMe = transform.position - rb.position;
        float dot = Vector3.Dot(relativeVelocity.normalized, toMe.normalized);

        bool isImpacting = dot > 0.2f && relativeVelocity.magnitude > 1f;
        if (!isImpacting) return;

        if (killOnAnyTrigger)
        {
            Kill();
            return;
        }

        // pseudo impulse for triggers
        float impactMag = relativeVelocity.magnitude * rb.mass;
        if (impactMag >= _killImpulseThreshold)
        {
            Kill();
        }
    }

    /// <summary> External hook to kill by script (explosives, timers, etc.). </summary>
    public void Kill()
    {
        if (_isDead) return;
        _isDead = true;

        if (animator) animator.enabled = false;

        ApplyDeathMaterial();

        if (ragdollSwitcher != null)
            ragdollSwitcher.ForceSwitchToRagdoll();

        onDeath?.Invoke();
        OnDied?.Invoke(this);
    }

    /// <summary> Resets the enemy fully for pooling: visuals + ragdoll pose/state. </summary>
    public void ResetToInitial()
    {
        _isDead = false;
        _isRagdolled = false;
        _killImpulseThreshold = ComputeKillThreshold();

        ApplyAliveMaterial();

        if (ragdollSwitcher != null)
            ragdollSwitcher.ResetToInitial();

        // reset fall/support state
        _prevVelocity = Vector3.zero;
        _isGrounded = _wasGrounded = false;
        _ungroundedTimer = 0f;

        if (animator) animator.enabled = true;

        ClearSupport();
        ProbeSupportUnderfoot();
    }

    // --- Utilities
    private void ApplyAliveMaterial()
    {
        if (targetRenderers == null || activeMaterial == null) return;
        for (int i = 0; i < targetRenderers.Length; i++)
        {
            var r = targetRenderers[i];
            if (!r) continue;
            r.sharedMaterial = activeMaterial;
        }
    }

    private void ApplyDeathMaterial()
    {
        if (targetRenderers == null || deathMaterial == null) return;
        for (int i = 0; i < targetRenderers.Length; i++)
        {
            var r = targetRenderers[i];
            if (!r) continue;
            r.sharedMaterial = deathMaterial;
        }
    }

    /// <summary> Convenience if something delivers an 'impact strength' instead of a collision. </summary>
    public void ApplyImpact(float impulseMagnitude)
    {
        if (_isDead) return;
        if (killOnAnyCollision || impulseMagnitude >= _killImpulseThreshold)
            Kill();
    }

}

