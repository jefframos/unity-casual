using UnityEngine;

[CreateAssetMenu(menuName = "Game/Effects/Explosive Effect Definition")]
public class ExplosiveEffectDefinition : ScriptableObject
{
    [Header("Explosion Shape")]
    [Min(0.01f)] public float radius = 5f;
    [Tooltip("If zero or <= radius, uses radius for falloff length.")]
    public float falloffRadius = 0f;

    [Header("Impact Filtering")]
    [Tooltip("Minimum relative speed to consider a hit as 'moving'. Prevents start-up overlaps from triggering.")]
    [Min(0f)] public float impactMinSpeed = 1.0f;

    [Tooltip("Seconds after spawn to ignore any collisions/triggers. Prevents instant startup explosions.")]
    [Min(0f)] public float startupGraceSeconds = 0.25f;

    [Tooltip("If true, only an impact from a moving object (or the player) can arm/explode.")]
    public bool requireMovementImpact = true;

    [Header("Explosion Application")]
    [Tooltip("If true, use AddExplosionForce when applying to generic rigidbodies. Otherwise uses AddForce in radial direction.")]
    public bool useAddExplosionForce = false;

    [Tooltip("Upwards modifier passed to AddExplosionForce when enabled.")]
    public float explosionUpwardsModifier = 0f;

    [Header("Force")]
    public float baseForce = 25f;
    public ForceMode forceMode = ForceMode.Impulse;
    public bool scaleByMass = true;
    [Range(0f, 30f)] public float randomAngleJitterDeg = 0f;

    [Header("Falloff")]
    [Tooltip("Input: 0 near center, 1 at falloff radius. Output scales force.")]
    public AnimationCurve falloff = AnimationCurve.EaseInOut(0, 1, 1, 0);

    [Header("Targeting")]
    public LayerMask affectLayers = ~0;          // who receives forces
    public string playerTag = "Player";
    public ForceApplicationTarget applicationTarget = ForceApplicationTarget.Auto;
    public bool forceSwitchToRagdollOnHit = true;

    [Header("Timing")]
    [Tooltip("Delay when hit indirectly (explosive hits explosive or any non-player).")]
    [Min(0f)] public float indirectExplosionDelay = 0.15f;
    [Tooltip("Extra delay for chain propagation when an explosion arms another.")]
    [Min(0f)] public float chainDelay = 0.05f;

    [Header("Lifecycle")]
    public bool destroyAfterExplode = true;
    public float destroyAfterSeconds = 0.0f; // 0 = immediate

    [Header("FX (optional)")]
    public GameObject vfxPrefab;
    public AudioClip sfx;
    [Range(0f, 1f)] public float sfxVolume = 1f;

    public float EffectiveFalloffRadius => (falloffRadius > 0f ? falloffRadius : radius);
}
