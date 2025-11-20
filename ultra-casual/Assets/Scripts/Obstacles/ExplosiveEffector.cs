using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class ExplosiveEffector : MonoBehaviour
{
    [Header("Definition")]
    public ExplosiveEffectDefinition definition;

    [Header("State (read-only)")]
    [SerializeField] private bool _isBusy;
    [SerializeField] private bool _exploded;
    public bool IsBusy => _isBusy;
    public bool IsSpent => _exploded;

    [Header("Events")]
    public UnityEvent onArmed;
    public UnityEvent onExplodeStarted;
    public UnityEvent onExplodeFinished;

    private Collider _col;
    private bool _armedPending;
    private float _armedExplodeAt;
    private float _spawnTime;

    private Rigidbody _selfRb;              // in case explosive itself moves
    private Vector3 _lastPos;
    private bool _hasLastPos;

    private static readonly HashSet<ExplosiveEffector> _tempVisited = new HashSet<ExplosiveEffector>();

    private void Awake()
    {
        _col = GetComponent<Collider>();
        _selfRb = GetComponent<Rigidbody>();
        _spawnTime = Time.time;
    }

    void OnEnable()
    {
        _exploded = false;
        _isBusy = false;
        _spawnTime = Time.time;
    }

    private void Update()
    {
        if (_armedPending && Time.time >= _armedExplodeAt && !_exploded)
        {
            _armedPending = false;
            ExplodeInternal();
        }

        if (!_hasLastPos)
        {
            _lastPos = transform.position;
            _hasLastPos = true;
        }
    }

    private void OnDisable()
    {
        if (_isBusy)
        {
            _isBusy = false;
            ExplosionCoordinator.Instance?.NotifyOneFinished();
        }
    }

    // --------- Contact routing ---------

    private void OnCollisionEnter(Collision c)
    {
        Vector3? normal = c.contactCount > 0 ? c.GetContact(0).normal : (Vector3?)null;
        HandleContact(c.collider, c);
    }

    private void HandleContact(Collider other, Collision collision)
    {
        if (definition == null || _exploded) return;

        // Startup grace: ignore early physics jitters/overlaps.
        if (Time.time - _spawnTime < definition.startupGraceSeconds)
            return;

        // Player → instant explode.
        if (!string.IsNullOrEmpty(definition.playerTag) && MatchesTagOrParent(other.transform, definition.playerTag))
        {
            ExplodeNow();
            return;
        }

        // Non-player contacts must come from movement if required.
        if (definition.requireMovementImpact && !IsMovingImpact(other, collision, definition.impactMinSpeed))
            return;

        // Non-player moving impact → delayed arm (chain logic).
        if (!_isBusy && !_exploded)
        {
            ArmForDelayedExplosion(definition.indirectExplosionDelay);
        }
    }

    private static bool MatchesTagOrParent(Transform t, string tag)
    {
        var p = t;
        while (p != null)
        {
            if (p.CompareTag(tag)) return true;
            p = p.parent;
        }
        return false;
    }

    private bool IsMovingImpact(Collider other, Collision collision, float minSpeed)
    {
        float otherSpeed = 0f;

        if (collision != null)
        {
            otherSpeed = collision.relativeVelocity.magnitude;
        }
        else
        {
            var rb = other.attachedRigidbody;
            if (rb != null)
            {
                otherSpeed = rb.linearVelocity.magnitude;
            }
            else
            {
                otherSpeed = GetSelfSpeedEstimate();
            }
        }

        return otherSpeed >= minSpeed;
    }

    private float GetSelfSpeedEstimate()
    {
        if (_selfRb != null) return _selfRb.linearVelocity.magnitude;

        if (_hasLastPos)
        {
            float spd = (transform.position - _lastPos).magnitude / Mathf.Max(Time.deltaTime, 1e-6f);
            _lastPos = transform.position;
            return spd;
        }

        _lastPos = transform.position;
        _hasLastPos = true;
        return 0f;
    }

    // --------- External API ---------
    [ContextMenu("Explode Now")]
    public void ExplodeNow()
    {
        if (definition == null || _exploded) return;
        if (_isBusy && _armedPending) _armedPending = false;
        ExplodeInternal();
    }

    public void ArmForDelayedExplosion(float delay)
    {
        if (definition == null || _exploded || _isBusy) return;

        _isBusy = true;
        _armedPending = true;
        _armedExplodeAt = Time.time + Mathf.Max(0f, delay);

        ExplosionCoordinator.Instance?.NotifyOneStarted();
        onArmed?.Invoke();
    }

    // --------- Explosion core ---------

    private void ExplodeInternal()
    {
        if (_exploded) return;

        _isBusy = true;
        onExplodeStarted?.Invoke();

        // FX (POOL-BASED NOW)
        if (definition.vfxPrefab != null)
        {
            ExplosionVfxPool.Instance?.Play(
                definition.vfxPrefab,
                transform.position,
                Quaternion.identity
            );
        }

        if (definition.sfx)
        {
            AudioSource.PlayClipAtPoint(definition.sfx, transform.position, definition.sfxVolume);
        }

        // Apply to EVERYTHING with a Rigidbody in radius
        DoExplosionToWorldAndChain();

        _exploded = true;
        _isBusy = false;
        onExplodeFinished?.Invoke();
        ExplosionCoordinator.Instance.NotifyOneFinished();

        // Cleanup
        if (definition.destroyAfterExplode)
        {
            if (definition.destroyAfterSeconds <= 0f) gameObject.SetActive(false);
            else
            {
                Cysharp.Threading.Tasks.UniTask.Void(async () =>
                {
                    await Cysharp.Threading.Tasks.UniTask.Delay(
                        System.TimeSpan.FromSeconds(definition.destroyAfterSeconds));
                    if (this != null) gameObject.SetActive(false);
                });
            }
        }
        else if (_col) _col.enabled = false;
    }

    private void DoExplosionToWorldAndChain()
    {
        float radius = Mathf.Max(0.01f, definition.radius);
        float falloffR = Mathf.Max(0.01f, definition.EffectiveFalloffRadius);

        Collider[] hits = Physics.OverlapSphere(transform.position, radius, definition.affectLayers, QueryTriggerInteraction.Collide);
        if (hits == null || hits.Length == 0) return;

        _tempVisited.Clear();
        _tempVisited.Add(this);

        foreach (var h in hits)
        {
            if (h == null) continue;

            // (A) Chain other explosives
            var otherExplosive = h.GetComponentInParent<ExplosiveEffector>();
            if (otherExplosive != null && otherExplosive != this && !otherExplosive.IsSpent)
            {
                if (!_tempVisited.Contains(otherExplosive))
                {
                    _tempVisited.Add(otherExplosive);
                    if (!otherExplosive.IsBusy && !otherExplosive.IsSpent)
                    {
                        otherExplosive.ArmForDelayedExplosion(definition.chainDelay);
                    }
                }
            }

            // (B) Apply forces to ANY Rigidbody
            var switcher = h.GetComponentInParent<DelayedRagdollSwitcher>();
            Vector3 toTarget = h.transform.position - transform.position;
            float d = toTarget.magnitude;
            if (d > radius + 0.0001f) continue;

            Vector3 dir = (d > 1e-5f ? toTarget / d : Random.onUnitSphere);
            if (definition.randomAngleJitterDeg > 0f)
            {
                Quaternion q = Quaternion.AngleAxis(
                    Random.Range(-definition.randomAngleJitterDeg, definition.randomAngleJitterDeg),
                    Random.onUnitSphere);
                dir = (q * dir).normalized;
            }

            float normalizedDist = Mathf.Clamp01(d / falloffR);
            float forceMag = definition.baseForce * definition.falloff.Evaluate(normalizedDist);

            if (normalizedDist < 0.7f)
            {
                var enemy = h.GetComponentInParent<RagdollEnemy>();
                if (enemy != null)
                {
                    enemy.Kill();
                }
            }

            if (switcher != null)
            {
                bool forceSwitch = definition.forceSwitchToRagdollOnHit;
                Vector3 worldForce = dir * forceMag;

                switch (definition.applicationTarget)
                {
                    case ForceApplicationTarget.Auto:
                        if (switcher.IsLaunching)
                            switcher.ApplyForce(worldForce, definition.forceMode, null, false, false, toLauncher: true);
                        else
                            switcher.ApplyForce(worldForce, definition.forceMode, null, forceSwitch, false, toLauncher: false);
                        break;

                    case ForceApplicationTarget.HipsOnly:
                        switcher.ApplyForce(worldForce, definition.forceMode, null, forceSwitch, true, toLauncher: false);
                        break;

                    case ForceApplicationTarget.WholeRagdoll:
                        switcher.ApplyForce(worldForce, definition.forceMode, null, forceSwitch, false, toLauncher: false);
                        break;

                    case ForceApplicationTarget.LauncherOnly:
                        switcher.ApplyForce(worldForce, definition.forceMode, null, false, false, toLauncher: true);
                        break;
                }
            }
            else
            {
                var rb = h.attachedRigidbody ?? h.GetComponent<Rigidbody>();

                if (rb != null && rb.isKinematic == false)
                {
                    rb.WakeUp();
                    if (definition.useAddExplosionForce)
                    {
                        rb.AddExplosionForce(
                            forceMag,
                            transform.position,
                            radius,
                            definition.explosionUpwardsModifier,
                            definition.forceMode == ForceMode.Impulse ? ForceMode.Impulse : ForceMode.Force);
                    }
                    else
                    {
                        rb.AddForce(dir * forceMag, definition.forceMode);
                    }
                }
            }
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (definition == null) return;
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.25f);
        Gizmos.DrawSphere(transform.position, definition.radius);
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 1f);
        Gizmos.DrawWireSphere(transform.position, definition.radius);
    }
#endif
}
