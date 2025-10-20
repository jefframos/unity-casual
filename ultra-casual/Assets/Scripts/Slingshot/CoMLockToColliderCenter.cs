using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class CoMLockToColliderCenter : MonoBehaviour
{
    [Tooltip("If left empty, the first collider on this GameObject will be used.")]
    public Collider mainCollider;

    [Tooltip("Re-apply every FixedUpdate (useful if you move/scale colliders at runtime).")]
    public bool keepSynced = false;

    private Rigidbody _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (mainCollider == null)
        {
            mainCollider = GetComponent<Collider>();
        }
        Apply();
    }

    private void OnValidate()
    {
        if (_rb == null) _rb = GetComponent<Rigidbody>();
        if (Application.isPlaying == false) Apply();
    }

    private void FixedUpdate()
    {
        if (keepSynced) Apply();
    }

    public void Apply()
    {
        if (_rb == null || mainCollider == null)
        {
            return;
        }

        // World-space AABB center of this collider
        Vector3 worldCenter = mainCollider.bounds.center;
        // Convert to rigidbody local space
        Vector3 localCenter = transform.InverseTransformPoint(worldCenter);

        _rb.centerOfMass = localCenter;

        // Optional: refresh inertia if colliders changed at runtime
        _rb.ResetInertiaTensor();
    }
}
