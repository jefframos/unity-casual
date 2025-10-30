using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class RagdollRig : MonoBehaviour
{
    [Header("Rig Roots")]
    [Tooltip("The transform that contains all the ragdoll bones (hips/pelvis).")]
    public Transform ragdollRoot;

    [Tooltip("Main body to push (usually hips/pelvis). If null, impulse will be distributed across all bodies.")]
    public Rigidbody mainBody;

    [Header("Options")]
    public CollisionDetectionMode collisionModeWhenFlying = CollisionDetectionMode.ContinuousDynamic;
    public bool autoCollectOnAwake = true;

    [System.Serializable]
    private struct Snapshot
    {
        public Transform t;
        public Vector3 localPos;
        public Quaternion localRot;
        public Vector3 localScale;
    }

    private readonly List<Rigidbody> _bodies = new List<Rigidbody>();
    private readonly List<Collider> _colliders = new List<Collider>();
    private readonly List<Snapshot> _pose = new List<Snapshot>();

    public Rigidbody Hips => mainBody;
    public IReadOnlyList<Rigidbody> Bodies => _bodies;
    public IReadOnlyList<Collider> Colliders => _colliders;

    private void Awake()
    {
        if (autoCollectOnAwake)
        {
            Collect();
            BakePoseSnapshot();
        }
    }

    public void Collect()
    {
        _bodies.Clear();
        _colliders.Clear();

        if (ragdollRoot == null)
        {
            ragdollRoot = transform;
        }

        // Collect from ragdollRoot downwards
        var bodies = ragdollRoot.GetComponentsInChildren<Rigidbody>(true);
        foreach (var b in bodies)
        {
            _bodies.Add(b);
            var cols = b.GetComponents<Collider>();
            foreach (var c in cols)
            {
                _colliders.Add(c);
            }
        }

        if (mainBody == null && _bodies.Count > 0)
        {
            // Fall back: pick the heaviest body as main
            Rigidbody heaviest = _bodies[0];
            for (int i = 1; i < _bodies.Count; i++)
            {
                if (_bodies[i].mass > heaviest.mass)
                {
                    heaviest = _bodies[i];
                }
            }
            mainBody = heaviest;
        }
    }

    public void BakePoseSnapshot()
    {
        _pose.Clear();
        foreach (var tr in ragdollRoot.GetComponentsInChildren<Transform>(true))
        {
            _pose.Add(new Snapshot
            {
                t = tr,
                localPos = tr.localPosition,
                localRot = tr.localRotation,
                localScale = tr.localScale
            });
        }
    }

    public void RestorePoseSnapshot()
    {
        foreach (var s in _pose)
        {
            if (s.t == null) continue;
            s.t.localPosition = s.localPos;
            s.t.localRotation = s.localRot;
            s.t.localScale = s.localScale;
        }
    }

    public void SetKinematic(bool value)
    {
        foreach (var b in _bodies)
        {
            b.isKinematic = value;
            b.useGravity = !value;
            if (!value)
            {
                b.collisionDetectionMode = collisionModeWhenFlying;
            }
        }
    }

    public void ZeroVelocities()
    {
        foreach (var b in _bodies)
        {
            bool prevK = b.isKinematic;
            b.isKinematic = false;
            b.linearVelocity = Vector3.zero;    // Unity 6
            b.angularVelocity = Vector3.zero;
            b.isKinematic = prevK;

        }
    }

    public void ResetMassProps()
    {
        foreach (var b in _bodies)
        {
            b.ResetCenterOfMass();
            b.ResetInertiaTensor();
        }
    }

    public void ApplyImpulse(Vector3 dir, float impulse)
    {
        dir = dir.sqrMagnitude > 0f ? dir.normalized : Vector3.forward;

        if (mainBody != null)
        {
            mainBody.AddForce(dir * impulse, ForceMode.Impulse);
            return;
        }

        // Distribute proportionally by mass
        float totalMass = 0f;
        foreach (var b in _bodies) totalMass += b.mass;
        if (totalMass <= 0f) totalMass = _bodies.Count;

        foreach (var b in _bodies)
        {
            float share = (b.mass / totalMass) * impulse;
            b.AddForce(dir * share, ForceMode.Impulse);
        }
    }
}
