using UnityEngine;

public class PlayerSlingshotable : MonoBehaviour, ISlingshotable
{
    [Header("Physics / Ragdoll")]
    public Rigidbody mainRigidbody;
    public Animator animator;
    public GameObject ragdollRoot;         // hierarchy with ragdoll rigidbodies
    public Collider mainCollider;          // the non-ragdoll collider

    [Header("Ramp Path")]
    public Transform rampStart;
    public Transform rampEnd;

    [Tooltip("How far past rampEnd we still consider 'on the ramp' (meters).")]
    public float rampExtraDistance = 0.1f;

    public Transform Parent => transform;  // adapt if you use a different root
    public Transform LeftAnchor { get; set; }
    public Transform RightAnchor { get; set; }
    public Transform FollowTarget => transform;

    public bool IsLaunching { get; private set; }

    // Events (mirror your ISlingshotable if needed)
    public event System.Action OnLaunchStart;
    public event System.Action OnReleaseStart;

    // Internal state for deterministic flight
    private bool _inDeterministicFlight;
    private Vector3 _flightVelocity;   // constant velocity magnitude for ramp phase
    private float _rampLength;
    private float _distanceAlongRamp;

    private void Awake()
    {
        if (rampStart && rampEnd)
        {
            _rampLength = Vector3.Distance(rampStart.position, rampEnd.position);
        }

        SetRagdollEnabled(false);
    }

    public void SetKinematic(bool isKinematic)
    {
        if (mainRigidbody)
        {
            mainRigidbody.isKinematic = isKinematic;
            mainRigidbody.linearVelocity = Vector3.zero;
            mainRigidbody.angularVelocity = Vector3.zero;
        }
    }

    /// <summary>
    /// Called by SlingshotController with a deterministic launch velocity.
    /// </summary>
    public void BeginDeterministicFlight(Vector3 launchVelocity)
    {
        if (launchVelocity.sqrMagnitude < 0.0001f)
            return;

        _flightVelocity = launchVelocity; // keep direction and magnitude
        _distanceAlongRamp = 0f;
        _inDeterministicFlight = true;
        IsLaunching = true;

        // Put character at ramp start and align to ramp
        if (rampStart)
        {
            Parent.position = rampStart.position;
            Parent.rotation = rampStart.rotation;
        }

        // Disable ragdoll + physics sim for now (purely kinematic)
        SetRagdollEnabled(false);
        SetKinematic(true);

        OnLaunchStart?.Invoke();
    }

    private void FixedUpdate()
    {
        if (!_inDeterministicFlight)
            return;

        if (_rampLength <= 0.001f || rampStart == null || rampEnd == null)
        {
            // No valid ramp – just switch to ragdoll immediately
            SwitchToRagdoll();
            return;
        }

        float speed = _flightVelocity.magnitude;
        float step = speed * Time.fixedDeltaTime;

        _distanceAlongRamp += step;
        float t = Mathf.Clamp01(_distanceAlongRamp / _rampLength);

        // Move along straight ramp from start to end
        Vector3 pos = Vector3.Lerp(rampStart.position, rampEnd.position, t);
        Quaternion rot = Quaternion.Slerp(rampStart.rotation, rampEnd.rotation, t);

        Parent.position = pos;
        Parent.rotation = rot;

        // Once we pass the ramp end, switch to ragdoll and keep the same velocity
        if (_distanceAlongRamp >= _rampLength + rampExtraDistance)
        {
            SwitchToRagdoll();
        }
    }

    private void SwitchToRagdoll()
    {
        if (!_inDeterministicFlight)
            return;

        _inDeterministicFlight = false;
        IsLaunching = false;

        // Re-enable physics and ragdoll
        SetKinematic(false);
        SetRagdollEnabled(true);

        // Apply the stored velocity in the final forward direction
        if (mainRigidbody)
        {
            // Project velocity onto current forward to keep magnitude
            Vector3 dir = Parent.forward.normalized;
            float speed = _flightVelocity.magnitude;
            mainRigidbody.linearVelocity = dir * speed;
        }

        OnReleaseStart?.Invoke();
    }

    private void SetRagdollEnabled(bool enabled)
    {
        // Simple example: enable ragdoll colliders/rigidbodies, disable main collider/animator
        if (ragdollRoot != null)
        {
            foreach (var rb in ragdollRoot.GetComponentsInChildren<Rigidbody>())
            {
                if (rb == mainRigidbody) continue;
                rb.isKinematic = !enabled;
            }

            foreach (var col in ragdollRoot.GetComponentsInChildren<Collider>())
            {
                if (col == mainCollider) continue;
                col.enabled = enabled;
            }
        }

        if (mainCollider)
            mainCollider.enabled = !enabled;

        if (animator)
            animator.enabled = !enabled;
    }

    // If you still need the old Launch signature:
    public void Launch(Vector3 dir, float impulseAsSpeed)
    {
        // Interpret impulse as speed and reuse deterministic flow
        BeginDeterministicFlight(dir.normalized * impulseAsSpeed);
    }
}
