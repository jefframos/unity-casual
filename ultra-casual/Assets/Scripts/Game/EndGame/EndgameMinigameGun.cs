using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

[DisallowMultipleComponent]
public class EndgameMinigameGun : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Camera used to project screen taps into world.")]
    public Camera targetCamera;

    [Tooltip("Transform that will rotate to aim at the shot. If null, uses this.transform.")]
    public Transform gunTransform;

    [Tooltip("Where the projectile will spawn from (tip of the gun).")]
    public Transform gunTip;

    [Tooltip("Projectile prefab (must have EndgameMinigameProjectile).")]
    public EndgameMinigameProjectile projectilePrefab;

    [Header("Shooting")]
    [Tooltip("Speed of the projectile in world units per second.")]
    public float projectileSpeed = 40f;

    [Tooltip("Lifetime of each projectile before it is returned to pool.")]
    public float projectileLifetime = 1.0f;

    [Tooltip("Max ray distance when we do not hit anything (i.e. shoot into 'infinity').")]
    public float missDistance = 100f;

    [Tooltip("Layers that the gun can hit (targets, environment, etc.).")]
    public LayerMask hitLayers = ~0;

    public ParticleSystem shootVfx;

    [Header("Pooling")]
    [Tooltip("Initial number of pooled projectiles.")]
    public int initialPoolSize = 8;

    [Header("Debug")]
    public bool debugLogs = false;
    public GameObject EndgameGunInput;

    private readonly Queue<EndgameMinigameProjectile> _pool = new Queue<EndgameMinigameProjectile>();
    private Transform _poolRoot;

    void OnEnable()
    {
        EndgameGunInput.SetActive(true);
    }
    void OnDisable()
    {
        EndgameGunInput.SetActive(false);
    }
    private void Awake()
    {
        if (projectilePrefab != null)
        {
            EnsurePool();
        }

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (gunTransform == null)
        {
            gunTransform = transform;
        }

        EndgameGunInput.SetActive(false);
    }

    private void EnsurePool()
    {
        if (projectilePrefab == null)
        {
            return;
        }

        if (_poolRoot == null)
        {
            GameObject go = new GameObject("EndgameMinigameProjectilePool");
            go.transform.SetParent(transform);
            go.SetActive(false);
            _poolRoot = go.transform;
        }

        while (_pool.Count < initialPoolSize)
        {
            EndgameMinigameProjectile p = Instantiate(projectilePrefab, _poolRoot);
            p.gameObject.SetActive(false);
            _pool.Enqueue(p);
        }
    }

    private EndgameMinigameProjectile GetFromPool()
    {
        EnsurePool();

        EndgameMinigameProjectile inst;
        if (_pool.Count > 0)
        {
            inst = _pool.Dequeue();
            inst.transform.SetParent(null);
        }
        else
        {
            inst = Instantiate(projectilePrefab);
        }

        return inst;
    }

    public void ReturnToPool(EndgameMinigameProjectile projectile)
    {
        if (projectile == null)
        {
            return;
        }

        if (_poolRoot == null)
        {
            EnsurePool();
        }

        projectile.gameObject.SetActive(false);
        projectile.transform.SetParent(_poolRoot);
        _pool.Enqueue(projectile);
    }

    // -------------------------------------------------------
    // PUBLIC API
    // -------------------------------------------------------

    /// <summary>
    /// Shoot at a given screen position (e.g. from a tap).
    /// This will raycast from the camera through that point.
    /// If we hit something, bullet goes there; otherwise it flies into the distance.
    /// </summary>
    public void ShootAt(Vector2 screenPosition)
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera == null)
        {
            if (debugLogs)
            {
                Debug.LogWarning("[EndgameMinigameGun] No camera assigned to ShootAt.");
            }

            return;
        }

        if (gunTip == null)
        {
            if (debugLogs)
            {
                Debug.LogWarning("[EndgameMinigameGun] No gunTip assigned.");
            }

            return;
        }

        Ray ray = targetCamera.ScreenPointToRay(screenPosition);
        Vector3 targetPoint;
        bool hitSomething = Physics.Raycast(ray, out RaycastHit hit, missDistance, hitLayers);

        if (hitSomething)
        {
            targetPoint = hit.point;
        }
        else
        {
            targetPoint = ray.origin + ray.direction * missDistance;
        }

        ShootWorld(targetPoint);
    }

    /// <summary>
    /// Shoot directly at a world-space point.
    /// </summary>
    public void ShootWorld(Vector3 worldPoint)
    {
        if (gunTip == null || projectilePrefab == null)
        {
            return;
        }

        Vector3 start = gunTip.position;
        Vector3 dir = (worldPoint - start).normalized;

        if (dir.sqrMagnitude < 0.0001f)
        {
            if (debugLogs)
            {
                Debug.LogWarning("[EndgameMinigameGun] ShootWorld called with almost zero direction.");
            }

            return;
        }

        // Rotate the gun (or pivot) to point where we are shooting
        if (gunTransform != null)
        {
            gunTransform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        if (shootVfx)
        {
            shootVfx.Play();
        }

        EndgameMinigameProjectile proj = GetFromPool();
        proj.gameObject.SetActive(true);

        proj.transform.position = start;
        proj.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

        if (debugLogs)
        {
            Debug.Log($"[EndgameMinigameGun] Shooting projectile towards {worldPoint}.");
        }

        proj.Launch(
            owner: this,
            direction: dir,
            speed: projectileSpeed,
            lifetime: projectileLifetime
        );
    }
}
