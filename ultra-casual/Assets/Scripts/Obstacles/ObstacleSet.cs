using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class ObstacleSet : MonoBehaviour, IResettable
{
    [Tooltip("Explicit list of obstacle rigidbodies. Leave empty to auto-collect from children.")]
    public List<Rigidbody> rigidbodies = new();

    [Tooltip("If true and list is empty, collect all child rigidbodies at Awake().")]
    public bool autoCollectFromChildren = true;

    [Serializable]
    private struct TransformSnapshot
    {
        public Vector3 localPos;
        public Quaternion localRot;
        public Vector3 localScale;
    }

    [Serializable]
    private struct RigidbodySnapshot
    {
        public bool isKinematic;
        public bool useGravity;
        public RigidbodyInterpolation interpolation;
        public CollisionDetectionMode collisionMode;
        public float drag;
        public float angularDrag;
        public float mass;
    }

    private readonly Dictionary<Rigidbody, TransformSnapshot> _xforms = new();
    private readonly Dictionary<Rigidbody, RigidbodySnapshot> _bodies = new();

    private void Awake()
    {
        if (rigidbodies.Count == 0 && autoCollectFromChildren)
        {
            rigidbodies.AddRange(GetComponentsInChildren<Rigidbody>(includeInactive: true));
        }

        CaptureInitial();
    }

    public void CaptureInitial()
    {
        _xforms.Clear();
        _bodies.Clear();

        foreach (var rb in rigidbodies)
        {
            if (!rb) continue;
            var t = rb.transform;
            _xforms[rb] = new TransformSnapshot
            {
                localPos = t.localPosition,
                localRot = t.localRotation,
                localScale = t.localScale
            };
            _bodies[rb] = new RigidbodySnapshot
            {
                isKinematic = rb.isKinematic,
                useGravity = rb.useGravity,
                interpolation = rb.interpolation,
                collisionMode = rb.collisionDetectionMode,
                drag = rb.linearDamping,
                angularDrag = rb.angularDamping,
                mass = rb.mass
            };
        }
    }

    public void ResetToInitial()
    {
        foreach (var rb in rigidbodies)
        {
            if (!rb) continue;

            // Temporarily kinematic so we can safely warp transforms.
            var origKin = rb.isKinematic;

            // Zero dynamics.
            rb.isKinematic = false;
            rb.SetLinearVelocity(Vector3.zero);
            rb.angularVelocity = Vector3.zero;

            rb.isKinematic = true;
            // Restore transform.
            if (_xforms.TryGetValue(rb, out var tx))
            {
                var tr = rb.transform;
                tr.localPosition = tx.localPos;
                tr.localRotation = tx.localRot;
                tr.localScale = tx.localScale;
            }

            // Restore RB settings.
            if (_bodies.TryGetValue(rb, out var bs))
            {
                rb.useGravity = bs.useGravity;
                rb.interpolation = bs.interpolation;
                rb.collisionDetectionMode = bs.collisionMode;
                rb.linearDamping = bs.drag;
                rb.angularDamping = bs.angularDrag;
                rb.mass = bs.mass;
                rb.isKinematic = bs.isKinematic;
            }
            else
            {
                // Fallback to original kinematic state if snapshot missing (shouldn't happen).
                rb.isKinematic = origKin;
            }

            // Clear any accumulated forces.
            rb.Sleep();
            rb.WakeUp();
        }
    }
}
