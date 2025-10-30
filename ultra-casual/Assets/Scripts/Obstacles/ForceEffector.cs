using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class ForceEffector : MonoBehaviour
{
    [Header("Definition")]
    public ForceEffectDefinition definition;

    [Header("Space / Reference")]
    [Tooltip("Used when vectorKind == LocalDirection. If null, uses this.transform.")]
    public Transform localReference;

    [Header("Target Recognition")]
    public string playerTag = "Player";

    private readonly Dictionary<DelayedRagdollSwitcher, float> _lastStayAppliedAt = new Dictionary<DelayedRagdollSwitcher, float>();

    private void Awake()
    {
        if (localReference == null)
        {
            localReference = transform;
        }
    }

    // ---------- Triggers ----------

    private void OnTriggerEnter(Collider other)
    {
        if (definition == null) { return; }
        if (!definition.applyOnEnter) { return; }

        TryApply(other, null);
    }

    private void OnTriggerStay(Collider other)
    {
        if (definition == null) { return; }
        if (!definition.applyOnStay) { return; }

        if (!ShouldApplyStayTo(other)) { return; }
        TryApply(other, null);
    }

    private void OnTriggerExit(Collider other)
    {
        if (definition == null) { return; }
        if (!definition.applyOnExit) { return; }

        TryApply(other, null);
        _lastStayAppliedAt.Remove(other.GetComponentInParent<DelayedRagdollSwitcher>());
    }

    // ---------- Collisions ----------

    private void OnCollisionEnter(Collision collision)
    {
        if (definition == null) { return; }
        if (!definition.applyOnEnter) { return; }

        var normal = collision.contactCount > 0 ? (Vector3?)collision.GetContact(0).normal : null;
        TryApply(collision.collider, normal);
    }

    private void OnCollisionStay(Collision collision)
    {
        if (definition == null) { return; }
        if (!definition.applyOnStay) { return; }

        if (!ShouldApplyStayTo(collision.collider)) { return; }
        var normal = collision.contactCount > 0 ? (Vector3?)collision.GetContact(0).normal : null;
        TryApply(collision.collider, normal);
    }

    private void OnCollisionExit(Collision collision)
    {
        if (definition == null) { return; }
        if (!definition.applyOnExit) { return; }

        var normal = collision.contactCount > 0 ? (Vector3?)collision.GetContact(0).normal : null;
        TryApply(collision.collider, normal);
        _lastStayAppliedAt.Remove(collision.collider.GetComponentInParent<DelayedRagdollSwitcher>());
    }

    // ---------- Core ----------

    private bool ShouldApplyStayTo(Collider other)
    {
        var sw = other.GetComponentInParent<DelayedRagdollSwitcher>();
        if (sw == null) { return false; }

        var now = Time.time;
        if (_lastStayAppliedAt.TryGetValue(sw, out var last))
        {
            if (now - last < definition.stayIntervalSeconds)
            {
                return false;
            }
        }
        _lastStayAppliedAt[sw] = now;
        return true;
    }

    private void TryApply(Collider other, Vector3? collisionNormal)
    {
        if (other == null) { return; }

        if (!string.IsNullOrEmpty(playerTag) && !other.CompareTag(playerTag))
        {
            // Check parents too (typical with ragdoll parts)
            var t = other.transform;
            bool ok = false;
            while (t != null)
            {
                if (t.CompareTag(playerTag))
                {
                    ok = true;
                    break;
                }
                t = t.parent;
            }
            if (!ok) { return; }
        }

        var switcher = other.GetComponentInParent<DelayedRagdollSwitcher>();
        if (switcher == null) { return; }

        // Direction
        Vector3 dir = ComputeDirection(switcher, collisionNormal);

        // Magnitude (+ falloff)
        float mag = definition.magnitude;
        if (definition.useDistanceFalloff)
        {
            float d = Vector3.Distance(GetEffectorOrigin(), switcher.transform.position);
            float n = Mathf.Clamp01(d / Mathf.Max(0.0001f, definition.falloffRadius));
            mag *= definition.falloff.Evaluate(n);
        }

        // Randomize direction a bit if needed
        if (definition.randomAngleJitterDeg > 0f)
        {
            dir = JitterDirection(dir, definition.randomAngleJitterDeg);
        }

        if (definition.scaleByMass)
        {
            var rb = FindTargetRigidbodyForMassScale(switcher);
            if (rb != null)
            {
                mag *= Mathf.Max(0.0001f, rb.mass);
            }
        }

        var worldForce = dir * mag;

        // Decide where to apply
        bool forceSwitch = definition.forceSwitchToRagdollOnHit;
        switch (definition.applicationTarget)
        {
            case ForceApplicationTarget.Auto:
                {
                    // Before switch -> launcher; after switch -> ragdoll (whole)
                    if (switcher.IsLaunching)
                    {
                        switcher.ApplyForce(worldForce, definition.forceMode, null, false, false, toLauncher: true);
                    }
                    else
                    {
                        switcher.ApplyForce(worldForce, definition.forceMode, null, forceSwitch, false, toLauncher: false);
                    }
                    break;
                }

            case ForceApplicationTarget.HipsOnly:
                {
                    switcher.ApplyForce(worldForce, definition.forceMode, null, forceSwitch, true, toLauncher: false);
                    break;
                }

            case ForceApplicationTarget.WholeRagdoll:
                {
                    switcher.ApplyForce(worldForce, definition.forceMode, null, forceSwitch, false, toLauncher: false);
                    break;
                }

            case ForceApplicationTarget.LauncherOnly:
                {
                    switcher.ApplyForce(worldForce, definition.forceMode, null, false, false, toLauncher: true);
                    break;
                }
        }
    }

    private Vector3 ComputeDirection(DelayedRagdollSwitcher target, Vector3? collisionNormal)
    {
        switch (definition.vectorKind)
        {
            case ForceVectorKind.WorldDirection:
                {
                    return definition.direction.normalized;
                }
            case ForceVectorKind.LocalDirection:
                {
                    return (localReference != null
                        ? localReference.TransformDirection(definition.direction)
                        : transform.TransformDirection(definition.direction)).normalized;
                }
            case ForceVectorKind.TowardThis:
                {
                    Vector3 v = GetEffectorOrigin() - target.transform.position;
                    return v.sqrMagnitude > 0f ? v.normalized : transform.forward;
                }
            case ForceVectorKind.AwayFromThis:
                {
                    Vector3 v = target.transform.position - GetEffectorOrigin();
                    return v.sqrMagnitude > 0f ? v.normalized : transform.forward;
                }
            case ForceVectorKind.CollisionNormal:
                {
                    if (collisionNormal.HasValue)
                    {
                        return collisionNormal.Value.normalized;
                    }
                    // Fallback to world/local direction if no contact normal (e.g., trigger)
                    return (localReference != null
                        ? localReference.TransformDirection(definition.direction)
                        : definition.direction).normalized;
                }
            default:
                {
                    return transform.forward;
                }
        }
    }

    private Vector3 GetEffectorOrigin()
    {
        return transform.position;
    }

    private static Vector3 JitterDirection(Vector3 dir, float degrees)
    {
        if (dir.sqrMagnitude < 1e-6f)
        {
            dir = Vector3.forward;
        }

        Quaternion q = Quaternion.AngleAxis(Random.Range(-degrees, degrees), Random.onUnitSphere);
        return (q * dir).normalized;
    }

    private static Rigidbody FindTargetRigidbodyForMassScale(DelayedRagdollSwitcher s)
    {
        // Prefer hips if available; else launcher; else first ragdoll body
        var hips = s.rig != null ? s.rig.Hips : null; // If your RagdollRig exposes Hips
        if (hips != null) { return hips; }

        if (s.launcherBody != null) { return s.launcherBody; }

        if (s.rig != null && s.rig.Bodies.Count > 0)
        {
            return s.rig.Bodies[0];
        }

        return null;
    }
}
