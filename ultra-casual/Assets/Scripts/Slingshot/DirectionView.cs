using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class DirectionView : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("The root/player transform this indicator belongs to.")]
    public Transform playerRoot;

    [Tooltip("Transform that defines the depth (world Z) where the tip will be.")]
    public Transform target;

    [Tooltip("Distance in front of the player where the path starts.")]
    public float distanceFromPlayer = 1.5f;

    [Tooltip("If true, use the player's up as 'up' when rotating. Otherwise use world up.")]
    public bool usePlayerUp = true;

    [Header("Prefabs")]
    [Tooltip("Prefab used for each segment of the dotted line (e.g. small sphere).")]
    public GameObject segmentPrefab;

    [Tooltip("Optional prefab used only for the tip of the path (e.g. arrow head).")]
    public GameObject tipPrefab;

    [Header("Path Settings")]
    [Tooltip("Maximum number of segments that can be shown (pool size).")]
    public int maxSegments = 20;

    [Tooltip("Approximate distance between each segment along the path.")]
    public float segmentSpacing = 0.5f;

    [Tooltip("Safety cap for max path length from start to tip.\n" +
             "If <= 0, no extra cap is applied.")]
    public float maxPathLength = 20f;

    [Tooltip("Rotate segments/tip to face along the path direction.")]
    public bool autoRotateSegments = true;

    private readonly List<Transform> _segmentPool = new List<Transform>();
    private Transform _tipInstance;

    private Vector3 _currentDirection = Vector3.forward;
    private float _currentPullForce;
    private bool _visible;
    private bool _poolInitialized;

    private void Reset()
    {
        if (playerRoot == null)
        {
            playerRoot = transform;
        }
    }

    private void Awake()
    {
        if (playerRoot == null)
        {
            playerRoot = transform;
        }
    }

    private void OnDisable()
    {
        HideAllInstances();
    }

    /// <summary>
    /// Set the direction (in world space) the indicator should point to,
    /// and how strong the pull is (0–1). Also makes the indicator visible.
    /// </summary>
    public void SetDirection(Vector3 worldDirection, float pullForce)
    {
        if (worldDirection.sqrMagnitude < 0.0001f)
        {
            Hide();
            return;
        }

        _currentDirection = worldDirection.normalized;
        _currentPullForce = Mathf.Clamp01(pullForce);


        _currentPullForce = 1f;
        _visible = true;

        EnsurePool();
        UpdatePath();
    }

    /// <summary>
    /// Hide the indicator (deactivates all pooled instances).
    /// </summary>
    public void Hide()
    {
        _visible = false;
        HideAllInstances();
    }

    private void LateUpdate()
    {
        if (!_visible)
        {
            return;
        }

        UpdatePath();
    }

    // ----------------- Pool & Path Logic -----------------

    private void EnsurePool()
    {
        if (_poolInitialized)
        {
            return;
        }

        _segmentPool.Clear();

        if (segmentPrefab != null && maxSegments > 0)
        {
            for (int i = 0; i < maxSegments; i++)
            {
                GameObject go = Instantiate(segmentPrefab, transform);
                go.SetActive(false);
                _segmentPool.Add(go.transform);
            }
        }

        if (tipPrefab != null)
        {
            GameObject tip = Instantiate(tipPrefab, transform);
            tip.SetActive(false);
            _tipInstance = tip.transform;
        }

        _poolInitialized = true;
    }

    private void HideAllInstances()
    {
        for (int i = 0; i < _segmentPool.Count; i++)
        {
            Transform seg = _segmentPool[i];
            if (seg == null)
            {
                continue;
            }

            if (seg.gameObject.activeSelf)
            {
                seg.gameObject.SetActive(false);
            }
        }

        if (_tipInstance != null && _tipInstance.gameObject.activeSelf)
        {
            _tipInstance.gameObject.SetActive(false);
        }
    }

    private void UpdatePath()
    {
        if (playerRoot == null)
        {
            return;
        }

        if (target == null)
        {
            return;
        }

        EnsurePool();

        Vector3 origin = playerRoot.position;
        Vector3 up = usePlayerUp ? playerRoot.up : Vector3.up;
        Vector3 dir = _currentDirection;

        // Path starts slightly in front of the player, along the aim direction.
        Vector3 startPoint = origin + dir * distanceFromPlayer;

        // Where this direction hits the target's Z plane.
        Vector3 maxTipPoint = ComputeMaxTipPointOnTargetZ(startPoint, dir);

        // Pull force lerps between start and that max point.
        Vector3 currentTipPoint = Vector3.Lerp(startPoint, maxTipPoint, _currentPullForce);

        float pathLength = Vector3.Distance(startPoint, currentTipPoint);
        if (pathLength <= 0.0001f)
        {
            HideAllInstances();
            return;
        }

        if (maxPathLength > 0.0f && pathLength > maxPathLength)
        {
            Vector3 pathDirClamp = (currentTipPoint - startPoint).normalized;
            currentTipPoint = startPoint + pathDirClamp * maxPathLength;
            pathLength = maxPathLength;
        }

        Vector3 pathDir = (currentTipPoint - startPoint).normalized;
        Quaternion rot = Quaternion.LookRotation(pathDir, up);

        // Determine how many segments we can fit.
        int segmentCount = 0;
        if (segmentPrefab != null && maxSegments > 0 && segmentSpacing > 0.0f)
        {
            segmentCount = Mathf.Min(
                maxSegments,
                Mathf.FloorToInt(pathLength / segmentSpacing)
            );
        }

        // Place segments evenly along the line from startPoint to currentTipPoint.
        for (int i = 0; i < _segmentPool.Count; i++)
        {
            Transform seg = _segmentPool[i];
            if (seg == null)
            {
                continue;
            }

            bool shouldBeActive = i < segmentCount;

            if (shouldBeActive)
            {
                float t = (float)(i + 1) / (float)(segmentCount + 1);
                Vector3 segPos = Vector3.Lerp(startPoint, currentTipPoint, t);

                seg.position = segPos;

                if (autoRotateSegments)
                {
                    seg.rotation = rot;
                }

                if (!seg.gameObject.activeSelf)
                {
                    seg.gameObject.SetActive(true);
                }
            }
            else
            {
                if (seg.gameObject.activeSelf)
                {
                    seg.gameObject.SetActive(false);
                }
            }
        }

        // Place tip at the end.
        if (_tipInstance != null)
        {
            if (pathLength > 0.0f)
            {
                _tipInstance.position = currentTipPoint;

                if (autoRotateSegments)
                {
                    _tipInstance.rotation = rot;
                }

                if (!_tipInstance.gameObject.activeSelf)
                {
                    _tipInstance.gameObject.SetActive(true);
                }
            }
            else
            {
                if (_tipInstance.gameObject.activeSelf)
                {
                    _tipInstance.gameObject.SetActive(false);
                }
            }
        }
    }

    /// <summary>
    /// Computes where the ray starting at startPoint in dir hits the Z plane of target.
    /// The resulting point always has worldZ == target.position.z.
    /// If the direction never reaches that Z (e.g., pointing away), we fall back
    /// to a straight line in front of the start point.
    /// </summary>
    private Vector3 ComputeMaxTipPointOnTargetZ(Vector3 startPoint, Vector3 dir)
    {
        float targetZ = target.position.z;

        // Default fallback: straight ahead a bit.
        Vector3 fallback = startPoint + dir * maxPathLength;

        float dirZ = dir.z;
        if (Mathf.Abs(dirZ) < 0.0001f)
        {
            return fallback;
        }

        // Solve startPoint.z + dir.z * t = targetZ
        float t = (targetZ - startPoint.z) / dirZ;
        if (t <= 0.0f)
        {
            // Intersection behind start or invalid.
            return fallback;
        }

        Vector3 hit = startPoint + dir * t;
        hit.z = targetZ; // ensure exact depth

        return hit;
    }
}
