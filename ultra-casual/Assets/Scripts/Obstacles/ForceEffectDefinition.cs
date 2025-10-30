using UnityEngine;

public enum ForceVectorKind
{
    WorldDirection,     // Use 'direction' in world space
    LocalDirection,     // Use 'direction' in this effector's local space
    TowardThis,         // From player -> effector.position
    AwayFromThis,       // From effector.position -> player
    CollisionNormal     // Use contact normal if available; falls back to World/Local dir
}

public enum ForceApplicationTarget
{
    Auto,           // Before switch -> launcher; after switch -> ragdoll (whole)
    HipsOnly,       // Apply only to hips/main body when ragdoll
    WholeRagdoll,   // Spread across all ragdoll bodies
    LauncherOnly    // Always to launcher body (even after switch)
}

[CreateAssetMenu(fileName = "ForceEffectDefinition", menuName = "Gameplay/Force Effect")]
public class ForceEffectDefinition : ScriptableObject
{
    [Header("Vector")]
    public ForceVectorKind vectorKind = ForceVectorKind.WorldDirection;
    public Vector3 direction = Vector3.forward; // used by World/LocalDirection

    [Header("Magnitude & Mode")]
    [Min(0f)] public float magnitude = 20f;
    public ForceMode forceMode = ForceMode.Impulse;

    [Header("Distance Falloff (0..1)")]
    public bool useDistanceFalloff = false;
    [Tooltip("Distance in meters at which force reaches 0 (mapped via curve).")]
    [Min(0.01f)] public float falloffRadius = 5f;
    [Tooltip("x: normalized distance (0 near .. 1 far), y: force multiplier.")]
    public AnimationCurve falloff = AnimationCurve.Linear(0, 1, 1, 0);

    [Header("Timing / Re-apply")]
    public bool applyOnEnter = true;
    public bool applyOnStay = false;
    public bool applyOnExit = false;
    [Tooltip("Minimum seconds between 'stay' applications per target.")]
    [Min(0f)] public float stayIntervalSeconds = 0.1f;

    [Header("Target")]
    public ForceApplicationTarget applicationTarget = ForceApplicationTarget.Auto;
    public bool forceSwitchToRagdollOnHit = false;

    [Header("Misc")]
    public bool scaleByMass = false;     // multiply force by rigidbody.mass
    public float randomAngleJitterDeg = 0f; // randomize direction slightly
}
