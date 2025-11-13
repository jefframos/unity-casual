using System;
using UnityEngine;

[DisallowMultipleComponent]
public class SlingshotCar : MonoBehaviour, ISlingshotable, IResettable
{
    [Header("Refs")]
    public Rigidbody rb;
    public Transform parent;       // usually the same as rb.transform
    public Transform leftAnchor;
    public Transform rightAnchor;
    public Transform followTarget; // optional: used to place CoM in world

    [Header("Physics Tweaks")]
    [Tooltip("If true, uses followTarget to set centerOfMass (converted to local space).")]
    public bool useFollowTargetAsCenterOfMass = false;

    [Tooltip("Recompute inertia tensor & CoM once at Start (after all colliders settle).")]
    public bool resetMassPropsOnStart = true;

    [Tooltip("Continuous mode helps fast-moving bodies avoid tunneling.")]
    public bool useContinuousCollision = true;

    [Tooltip("Extra damping for post-impact spin control.")]
    public float angularDragOverride = 0.5f;

    [Tooltip("Solver iterations (position, velocity) for more stable impacts.")]
    public int solverIterations = 12;
    public int solverVelocityIterations = 12;

    public event Action OnLaunchStart;
    public event Action OnReleaseStart;

    private void Reset()
    {
        rb = GetComponent<Rigidbody>();
        parent = transform;
    }

    private void OnValidate()
    {
        if (!rb) rb = GetComponent<Rigidbody>();
        if (!parent) parent = transform;
    }

    private void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody>();
        if (!parent) parent = transform;

        Debug.Log(OnLaunchStart);
        Debug.Log(OnReleaseStart);
        // Optional runtime stability settings
        if (rb)
        {
            if (useContinuousCollision)
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            if (angularDragOverride >= 0f)
                rb.angularDamping = angularDragOverride;

            if (solverIterations > 0) rb.solverIterations = solverIterations;
            if (solverVelocityIterations > 0) rb.solverVelocityIterations = solverVelocityIterations;
        }
    }

    private void Start()
    {
        if (!rb) return;

        if (resetMassPropsOnStart)
        {
            // If colliders were moved/added in Awake or by other scripts, bake mass props now.
            ResetMassProps();
        }

        if (useFollowTargetAsCenterOfMass && followTarget)
        {
            SetCenterOfMassFromWorld(followTarget.position);
            // Changing CoM affects inertia alignment—good practice to refresh:
            rb.ResetInertiaTensor();
        }
    }
    public void ResetToInitial()
    {
        rb.isKinematic = false;
        ZeroVelocities();
        // Make kinematic to safely warp.
        rb.isKinematic = true;

        ResetMassProps();

    }
    public Transform Parent => parent;
    public Transform LeftAnchor => leftAnchor;
    public Transform RightAnchor => rightAnchor;
    public Transform FollowTarget => followTarget ? followTarget : parent;

    public bool IsLaunching => throw new System.NotImplementedException();

    public void SetKinematic(bool isKinematic)
    {
        if (!rb) return;

        rb.isKinematic = isKinematic;
        rb.useGravity = !isKinematic;

        if (!isKinematic)
        {
            ZeroVelocities();
        }
    }

    public void Launch(Vector3 direction, float impulse)
    {
        if (!rb) return;

        // Clean state
        ZeroVelocities();

        // (1) Ensure Transform is aligned properly before we go dynamic.
        // Move Rigidbody to match current transform (forces internal sync).
        Debug.Log(rb.position);
        Debug.Log(transform.position);

        rb.position = transform.position;
        rb.rotation = transform.rotation;

        // (2) Optional: Recompute inertia & CoM if necessary
        ResetMassProps();

        // (3) Apply force
        rb.AddForce(direction.normalized * impulse, ForceMode.Impulse);
    }

    /// <summary>
    /// Recomputes mass properties from current colliders.
    /// Call this after you add/move/resize colliders at runtime.
    /// </summary>
    public void ResetMassProps()
    {
        if (!rb) return;
        rb.ResetCenterOfMass();
        rb.ResetInertiaTensor();
        //SetCenterOfMassFromWorld(transform.position);
    }

    /// <summary>
    /// Sets centerOfMass using a world position (converted to local).
    /// </summary>
    public void SetCenterOfMassFromWorld(Vector3 worldPoint)
    {
        if (!rb) return;
        rb.centerOfMass = rb.transform.InverseTransformPoint(worldPoint);
    }
    public void SnapCenterOfMassToAnchorMidpoint()
    {
        if (!rb || !LeftAnchor || !RightAnchor) return;
        Vector3 midWorld = (LeftAnchor.position + RightAnchor.position) * 0.5f;
        rb.centerOfMass = rb.transform.InverseTransformPoint(midWorld);
        rb.ResetInertiaTensor(); // keep inertia consistent with the new CoM
    }

    /// <summary>
    /// Draw a gizmo at PhysX world CoM to verify it matches expectations.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!rb) rb = GetComponent<Rigidbody>();
        if (!rb) return;

        Gizmos.color = Color.yellow;
        var wcom = rb.worldCenterOfMass;
        Gizmos.DrawSphere(wcom, 0.05f);
        Gizmos.DrawLine(wcom, wcom + transform.up * 0.5f);
    }

    // ------------------------------
    // helpers for linear/angular velocity
    // Keep linearVelocity for Unity 6; fall back to velocity for earlier versions.
    // ------------------------------

    private void ZeroVelocities()
    {
        SetLinearVelocity(Vector3.zero);
        rb.angularVelocity = Vector3.zero;
    }

    private void SetLinearVelocity(Vector3 v)
    {
        if (!rb.isKinematic)
        {
            rb.linearVelocity = v;
        }
    }

    public Vector3 GetLinearVelocity()
    {
        return rb.linearVelocity;
    }

    public void BeginDeterministicFlight(Vector3 launchVelocity)
    {
        throw new NotImplementedException();
    }
}
