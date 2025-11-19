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

    [Header("Activation Trigger")]
    [Tooltip("If true, automatically creates/updates a BoxCollider trigger that wraps all rigidbodies.")]
    public bool autoCreateTrigger = true;

    [Tooltip("Extra padding added around the rigidbodies bounds (local space).")]
    public Vector3 triggerPaddingExt = new Vector3(5f, 5f, 5f);

    [Tooltip("Tag used to identify the player for activating this obstacle set.")]
    public string playerTag = "Player";

    [SerializeField, Tooltip("Found IResettable components in children (excluding this).")]
    private List<MonoBehaviour> _resettablesDebug = new(); // for inspector visibility only

    private readonly List<IResettable> _resettables = new();

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

    private BoxCollider _trigger;

    private void Awake()
    {
        RefreshResettables();

        if (rigidbodies.Count == 0 && autoCollectFromChildren)
        {
            // Collect all child RBs, but exclude any inside a (different) IResettable subtree.
            var all = GetComponentsInChildren<Rigidbody>(includeInactive: true);
            rigidbodies = FilterRigidbodiesExcludingResettables(all);
        }
        else
        {
            // If user provided a list, sanitize it with the same rule.
            rigidbodies = FilterRigidbodiesExcludingResettables(rigidbodies);
        }

        CaptureInitial();
        EnsureTriggerCollider();
    }

    private void RefreshResettables()
    {
        _resettables.Clear();

        // Find all IResettable under us (include inactive), EXCEPT this ObstacleSet instance.
        var found = GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
        foreach (var mb in found)
        {
            if (mb == null)
            {
                continue;
            }

            if (mb is IResettable resettable)
            {
                // Skip self if attached to the same GameObject.
                if (ReferenceEquals(resettable, this))
                {
                    continue;
                }

                _resettables.Add(resettable);
            }
        }

        // Optional: keep a debug list for inspector
        _resettablesDebug.Clear();
        foreach (var r in _resettables)
        {
            if (r is MonoBehaviour mb)
            {
                _resettablesDebug.Add(mb);
            }
        }
    }

    private List<Rigidbody> FilterRigidbodiesExcludingResettables(IEnumerable<Rigidbody> source)
    {
        var set = new HashSet<Rigidbody>();
        foreach (var rb in source)
        {
            if (!rb)
            {
                continue;
            }

            if (IsInsideOtherResettable(rb.transform))
            {
                continue;
            }

            set.Add(rb);
        }

        return new List<Rigidbody>(set);
    }

    private bool IsInsideOtherResettable(Transform t)
    {
        // True if any ancestor (excluding this ObstacleSet's transform root) has an IResettable that isn't 'this'.
        Transform current = t;
        while (current != null && current != transform)
        {
            if (current.TryGetComponent<MonoBehaviour>(out var mb) && mb is IResettable resettable)
            {
                if (!ReferenceEquals(resettable, this))
                {
                    return true;
                }
            }

            current = current.parent;
        }

        return false;
    }

    private void EnsureTriggerCollider()
    {
        if (!autoCreateTrigger)
        {
            return;
        }

        if (!TryGetComponent<BoxCollider>(out _trigger))
        {
            _trigger = gameObject.AddComponent<BoxCollider>();
        }

        _trigger.isTrigger = true;

        if (rigidbodies == null || rigidbodies.Count == 0)
        {
            return;
        }

        bool hasAny = false;
        Bounds localBounds = new Bounds();

        foreach (var rb in rigidbodies)
        {
            if (!rb)
            {
                continue;
            }

            // Use world position, convert to local to match BoxCollider space.
            Vector3 localPos = transform.InverseTransformPoint(rb.worldCenterOfMass);

            if (!hasAny)
            {
                localBounds = new Bounds(localPos, Vector3.zero);
                hasAny = true;
            }
            else
            {
                localBounds.Encapsulate(localPos);
            }
        }

        if (!hasAny)
        {
            return;
        }

        Vector3 size = localBounds.size + triggerPaddingExt;
        // Ensure non-zero size so trigger is usable.
        size.x = Mathf.Max(size.x, 0.1f);
        size.y = Mathf.Max(size.y, 0.1f);
        size.z = Mathf.Max(size.z, 0.1f);

        _trigger.center = localBounds.center;
        _trigger.size = size;
    }

    public void CaptureInitial()
    {
        _xforms.Clear();
        _bodies.Clear();

        foreach (var rb in rigidbodies)
        {
            if (!rb)
            {
                continue;
            }

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
            if (!rb)
            {
                continue;
            }

            var origKin = rb.isKinematic;

            // Zero dynamics.
            rb.isKinematic = false;
            rb.SetLinearVelocity(Vector3.zero);
            rb.angularVelocity = Vector3.zero;

            rb.gameObject.SetActive(true);

            // Restore transform under kinematic to avoid impulses.
            rb.isKinematic = true;

            if (_xforms.TryGetValue(rb, out var tx))
            {
                var tr = rb.transform;
                tr.localPosition = tx.localPos;
                tr.localRotation = tx.localRot;
                tr.localScale = tx.localScale;
            }

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
                rb.isKinematic = origKin;
            }

            // After reset, keep them asleep until the player enters the trigger.
            rb.Sleep();
            rb.isKinematic = true;
        }

        // Optionally re-sync trigger bounds in case things moved structurally.
        //EnsureTriggerCollider();
    }

    private void WakeAllRigidbodies()
    {
        Debug.Log("WEAK ALL");
        foreach (var rb in rigidbodies)
        {
            if (!rb)
            {
                continue;
            }

            rb.WakeUp();
            rb.isKinematic = false;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        if (!string.IsNullOrEmpty(playerTag) && other.CompareTag(playerTag))
        {
            WakeAllRigidbodies();
        }
    }
}
