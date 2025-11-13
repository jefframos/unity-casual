using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class RagdollEnemy : MonoBehaviour, IResettable
{
    [Header("Rig & Animator")]
    public RagdollRig rig;
    public Animator animator; // optional

    [Header("Renderers & Materials")]
    public Renderer[] targetRenderers;
    public Material activeMaterial;
    public Material deathMaterial;

    [Header("Motion Source")]
    [Tooltip("The Rigidbody used for motion/velocity reference. If null, tries root rigidbody, then rig.Hips.")]
    public Rigidbody sourceBody;

    [Header("Ragdoll Settings")]
    public bool inheritSourceVelocityOnRagdoll = true;
    public float ragdollAngularDamping = 0.5f;

    [Header("Kill Rules")]
    public EnemyGrade grade = EnemyGrade.Standard;
    [Tooltip("Override the grade-based kill impulse. If <= 0, grade default is used.")]
    public float killImpulseOverride = -1f;

    [Tooltip("If true, any collision (non-trigger) kills instantly regardless of impulse. (Usually keep off)")]
    public bool killOnAnyCollision = false;

    [Tooltip("If true, entering any trigger kills instantly. (Usually keep off)")]
    public bool killOnAnyTrigger = false;

    [Header("Startup Safety")]
    [Tooltip("Ignore death checks for this many seconds after spawn/reset to avoid instant deaths from initial contacts.")]
    public float startupGraceTime = 0.2f;

    [Header("Optional Proximity Trigger Kill")]
    public Collider ownKillTrigger; // must be isTrigger = true

    [Header("Ground / Fall Detection (non-fatal ragdoll only)")]
    public LayerMask groundMask = ~0;
    public Vector3 groundProbeLocalOffset = new Vector3(0, 0.2f, 0);
    public float groundProbeRadius = 0.15f;
    public float groundProbeDistance = 0.5f;

    [Header("Optional Fall-Death (OFF by default)")]
    [Tooltip("If enabled, we arm during a real fall and only die if landing impact is hard enough.")]
    public bool enableFallDeath = false;
    public float minFallSpeed = 3.0f;         // downward speed to consider it a fall
    public float fallGraceTime = 0.12f;       // time falling before we 'arm'
    public bool requireWasGroundedBeforeFalling = true;
    public float fallImpactSpeedThreshold = 6f;

    [Header("Support (platform under me) → ragdoll triggers (non-fatal)")]
    public float supportMoveSpeedThreshold = 0.75f;
    public float supportAngularSpeedDegThreshold = 90f;
    public float supportSeparationDistance = 0.2f;

    [Header("Events")]
    public UnityEvent onDeath;
    public event Action<RagdollEnemy> OnDied;

    // --- runtime ---
    private bool _isDead;
    private bool _isRagdolled;
    private float _killImpulseThreshold;

    private bool _isGrounded, _wasGrounded;
    private float _ungroundedTimer;
    private bool _fallArmed; // only used if enableFallDeath

    private Vector3 _prevVelocity;
    private float _startupTimer;

    private Rigidbody _supportRb;
    private readonly HashSet<Rigidbody> _ownBodies = new();

    private Vector3 _startPos;
    private Quaternion _startRot;

    private const float WEAK_KILL_IMPULSE = 5.0f;
    private const float STANDARD_KILL_IMPULSE = 7.5f;
    private const float HEAVY_KILL_IMPULSE = 14.0f;

    void Reset()
    {
        if (!rig) rig = GetComponentInChildren<RagdollRig>(true);
        if (!animator) animator = GetComponentInChildren<Animator>(true);
        if (targetRenderers == null || targetRenderers.Length == 0)
            targetRenderers = GetComponentsInChildren<Renderer>(true);
        if (!sourceBody) sourceBody = GetComponent<Rigidbody>();
    }

    void Awake()
    {
        _startPos = transform.position;
        _startRot = transform.rotation;

        if (!rig) rig = GetComponentInChildren<RagdollRig>(true);
        if (rig != null)
        {
            if (rig.Bodies.Count == 0) rig.Collect();
            rig.BakePoseSnapshot();
            rig.SetKinematic(true);
            rig.ZeroVelocities();
        }

        if (!sourceBody)
        {
            sourceBody = GetComponent<Rigidbody>();
            if (!sourceBody && rig != null) sourceBody = rig.Hips;
        }

        CacheOwnRigidbodies();
        AutoAttachForwarders();

        _killImpulseThreshold = ComputeKillThreshold();

        ApplyAliveMaterial();
        _isDead = false;
        _isRagdolled = false;
        _fallArmed = false;
        _ungroundedTimer = 0f;
        _startupTimer = startupGraceTime;

        if (animator)
        {
            animator.enabled = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.ResetTrigger("jump");
            animator.SetTrigger("reset");
            animator.Update(0f);
        }

        if (ownKillTrigger) ownKillTrigger.isTrigger = true;

        ProbeSupportUnderfoot();
    }

    private void CacheOwnRigidbodies()
    {
        _ownBodies.Clear();
        var bodies = GetComponentsInChildren<Rigidbody>(true);
        foreach (var b in bodies) if (b) _ownBodies.Add(b);
    }

    private void AutoAttachForwarders()
    {
        var cols = GetComponentsInChildren<Collider>(true);
        foreach (var col in cols)
        {
            if (col.isTrigger) continue;
            if (!col.GetComponent<CollisionForwarder>())
            {
                var fwd = col.gameObject.AddComponent<CollisionForwarder>();
                fwd.owner = this;
            }
        }
    }

    private float ComputeKillThreshold()
    {
        if (killImpulseOverride > 0f) return killImpulseOverride;
        return grade switch
        {
            EnemyGrade.Weak => WEAK_KILL_IMPULSE,
            EnemyGrade.Heavy => HEAVY_KILL_IMPULSE,
            _ => STANDARD_KILL_IMPULSE
        };
    }

    void FixedUpdate()
    {
        if (_startupTimer > 0f) _startupTimer -= Time.fixedDeltaTime;
        if (_isDead) return;

        _prevVelocity = sourceBody ? sourceBody.linearVelocity : Vector3.zero;

        // Ground probe (for support + optional fall arming)
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

        if (_supportRb == null && hit.collider && hit.collider.attachedRigidbody)
            _supportRb = hit.collider.attachedRigidbody;

        // Support motion/separation → ragdoll (non-fatal)
        if (!_isRagdolled && _supportRb != null)
        {
            bool supportMoved = _supportRb.linearVelocity.sqrMagnitude >= (supportMoveSpeedThreshold * supportMoveSpeedThreshold);
            bool supportSpun = (_supportRb.angularVelocity.magnitude * Mathf.Rad2Deg) >= supportAngularSpeedDegThreshold;

            bool separated = false;
            if (hitGround)
            {
                var hitRb = hit.collider.attachedRigidbody;
                if (hitRb != _supportRb && hit.distance > supportSeparationDistance)
                    separated = true;
            }
            else separated = true;

            if (supportMoved || supportSpun || separated)
            {
                EnterRagdoll(); // not dead
            }
        }

        // Optional: arm fall (still not death; death is on landing if enabled)
        if (enableFallDeath)
        {
            if (!_isGrounded)
            {
                _ungroundedTimer += Time.fixedDeltaTime;

                float vy = sourceBody ? sourceBody.linearVelocity.y : 0f;
                bool fallingFast = vy <= -Mathf.Abs(minFallSpeed);
                bool validFallStart = requireWasGroundedBeforeFalling ? _wasGrounded : true;

                if (validFallStart && fallingFast && _ungroundedTimer >= fallGraceTime)
                {
                    _fallArmed = true;
                    EnterRagdoll();
                }
            }
            else
            {
                _ungroundedTimer = 0f;
            }
        }
        else
        {
            // ensure not armed if feature is off
            _fallArmed = false;
            _ungroundedTimer = 0f;
        }
    }

    private void EnterRagdoll()
    {
        if (_isRagdolled) return;
        _isRagdolled = true;

        Debug.LogWarning("RagdollEnemy EnterRagdoll");

        if (animator) animator.enabled = false;

        if (rig != null)
        {
            rig.SetKinematic(false);

            if (inheritSourceVelocityOnRagdoll && sourceBody != null)
            {
                var lin = sourceBody.linearVelocity;
                var ang = sourceBody.angularVelocity;
                foreach (var b in rig.Bodies)
                {
                    b.linearVelocity = lin;
                    b.angularVelocity = ang;
                    if (ragdollAngularDamping >= 0f) b.angularDamping = ragdollAngularDamping;
                }
            }
            else if (ragdollAngularDamping >= 0f)
            {
                foreach (var b in rig.Bodies) b.angularDamping = ragdollAngularDamping;
            }

            rig.ResetMassProps();
        }
    }

    // ---------------- Collisions & Triggers ----------------

    private void OnCollisionEnter(Collision collision)
    {
        if (_isDead) return;

        // Ignore startup noise
        if (_startupTimer > 0f) return;

        // If you explicitly want “any collision kills”, respect it — otherwise use impulse threshold.
        if (killOnAnyCollision)
        {
            Kill();
            return;
        }

        // If fall-death enabled and we were armed, we still require a *hard* landing.
        if (enableFallDeath && _fallArmed)
        {
            float landingSpeed = Mathf.Max(_prevVelocity.magnitude, collision.relativeVelocity.magnitude);
            if (landingSpeed >= fallImpactSpeedThreshold)
            {
                Kill();
                return;
            }
            _fallArmed = false; // touched ground but not hard enough
        }

        // Normal rule: only kill if impact impulse is strong enough.
        float impulseMag = collision.impulse.magnitude;
        if (impulseMag >= _killImpulseThreshold)
        {
            Kill();
        }
    }

    internal void OnCollisionFromChild(Collision c, Rigidbody childRb)
    {
        if (_isDead) return;
        if (_startupTimer > 0f) return;

        // Ignore self collisions
        var otherRb = c.rigidbody;
        if (otherRb != null && _ownBodies.Contains(otherRb)) return;

        if (killOnAnyCollision)
        {
            Kill();
            return;
        }

        if (enableFallDeath && _fallArmed)
        {
            float childSpeed = (childRb != null) ? childRb.linearVelocity.magnitude : 0f;
            float landingSpeed = Mathf.Max(_prevVelocity.magnitude, childSpeed, c.relativeVelocity.magnitude);
            if (landingSpeed >= fallImpactSpeedThreshold)
            {
                Kill();
                return;
            }
            _fallArmed = false;
        }

        float impulseMag = c.impulse.magnitude;
        if (impulseMag >= _killImpulseThreshold)
        {
            Kill();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_isDead) return;
        if (_startupTimer > 0f) return;

        if (other.transform.IsChildOf(transform)) return;
        if (killOnAnyTrigger)
        {
            Debug.Log(other.name);
            Kill();
            return;
        }

        var rb = other.attachedRigidbody;
        if (rb == null) return;
        if (rb == sourceBody) return;

        var myRb = sourceBody ? sourceBody : GetComponent<Rigidbody>();
        Vector3 relVel = myRb ? (rb.linearVelocity - myRb.linearVelocity) : rb.linearVelocity;

        // Treat trigger “impact” as pseudo impulse: only kill if strong enough.
        float pseudoImpulse = relVel.magnitude * rb.mass;
        if (pseudoImpulse >= _killImpulseThreshold)
        {
            Kill();
        }
    }

    // ---------------- Public API ----------------

    public void Kill()
    {
        if (_isDead) return;
        _isDead = true;

        EnterRagdoll();
        ApplyDeathMaterial();

        onDeath?.Invoke();
        OnDied?.Invoke(this);
    }

    public void ApplyImpact(float impulseMagnitude)
    {
        if (_isDead) return;
        if (killOnAnyCollision || impulseMagnitude >= _killImpulseThreshold)
            Kill();
    }

    // ---------------- Reset / Pooling ----------------

    public void ResetToInitial()
    {
        _isDead = false;
        _isRagdolled = false;
        _fallArmed = false;
        _ungroundedTimer = 0f;
        _startupTimer = startupGraceTime;

        _killImpulseThreshold = ComputeKillThreshold();

        ApplyAliveMaterial();

        if (rig != null)
        {
            rig.SetKinematic(true);
            rig.ZeroVelocities();
            rig.RestorePoseSnapshot();
            rig.ResetMassProps();
        }

        transform.SetPositionAndRotation(_startPos, _startRot);

        if (animator)
        {
            animator.enabled = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.ResetTrigger("jump");
            animator.SetTrigger("reset");
            animator.Update(0f);
        }

        _prevVelocity = Vector3.zero;
        _isGrounded = _wasGrounded = false;

        _supportRb = null;
        ProbeSupportUnderfoot();
    }

    // ---------------- Visuals ----------------

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
            if (hit.collider && hit.collider.attachedRigidbody)
                _supportRb = hit.collider.attachedRigidbody;
        }
    }
}

