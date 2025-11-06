using System;
using System.Collections.Generic;
using UnityEngine;


[DisallowMultipleComponent]
public class LevelBuilder : MonoBehaviour
{
    [Header("Track / Player")]
    public Transform player;                   // object to track
    [Tooltip("World-space origin for the track projection.")]
    public Transform trackOrigin;              // if null, uses this.transform
    [Tooltip("World-space direction of the track (normalized internally).")]
    public Vector3 trackDirection = Vector3.forward;

    [Header("Grounding / Placement")]
    public LayerMask groundMask = ~0;          // which layers count as ground
    public float groundRayStartHeight = 50f;   // raycast start height above sample position
    public float despawnBehindDistance = 60f;  // if object is this many meters behind player track, despawn

    [Header("Rules")]
    public List<SpawnRule> rules = new List<SpawnRule>();

    [Header("Pooling")]
    public Transform poolContainer;            // optional parent for pooled instances

    // -------------------- Runtime state --------------------

    private readonly Dictionary<GameObject, Queue<GameObject>> _pool = new Dictionary<GameObject, Queue<GameObject>>();
    private readonly List<ActiveItem> _active = new List<ActiveItem>();
    private readonly List<RuleRuntime> _runtime = new List<RuleRuntime>();

    private Vector3 _dirNorm;
    private Quaternion _basis; // rotates +Z to trackDirection
    private Vector3 _right;    // lateral axis derived from basis

    private Transform _origin;

    private void Awake()
    {
        _origin = trackOrigin != null ? trackOrigin : transform;

        // Normalize direction and set basis
        _dirNorm = trackDirection.sqrMagnitude > 0.0001f ? trackDirection.normalized : Vector3.forward;
        _basis = Quaternion.FromToRotation(Vector3.forward, _dirNorm);
        _right = _basis * Vector3.right;

        _runtime.Clear();
        foreach (var rule in rules)
        {
            if (rule == null || rule.prefab == null)
            {
                _runtime.Add(new RuleRuntime()); // placeholder
                continue;
            }

            var rr = new RuleRuntime
            {
                rule = rule,
                nextSpawnTrack = rule.initialOffsetMeters
            };
            _runtime.Add(rr);

            // Prewarm pool if requested
            if (rule.prewarmCount > 0)
            {
                EnsurePool(rule.prefab);
                for (int i = 0; i < rule.prewarmCount; i++)
                {
                    var go = Instantiate(rule.prefab, poolContainer != null ? poolContainer : transform);
                    go.SetActive(false);
                    _pool[rule.prefab].Enqueue(go);
                }
            }
        }
    }
    private bool IsOverlapping(Vector3 candidatePos, float candidateRadius, bool horizontal, int ignoreRuleIndex = -1)
    {
        for (int i = 0; i < _active.Count; i++)
        {
            var ai = _active[i];
            if (ai.go == null) continue;

            if (ignoreRuleIndex >= 0 && ai.ruleIndex == ignoreRuleIndex)
            {
                // (optional) ignore same-rule overlaps; usually you DON'T want to ignore these
            }

            var otherPos = ai.go.transform.position;
            float otherR = rules[ai.ruleIndex].collisionRadius;

            float dist;
            if (horizontal)
            {
                Vector2 a = new Vector2(candidatePos.x, candidatePos.z);
                Vector2 b = new Vector2(otherPos.x, otherPos.z);
                dist = Vector2.Distance(a, b);
            }
            else
            {
                dist = Vector3.Distance(candidatePos, otherPos);
            }

            if (dist < (candidateRadius + otherR) - 0.0001f)
                return true;
        }
        return false;
    }

    private bool TryBuildSpawnTransform(
        SpawnRule rule,
        float spawnTrack,
        out Vector3 outPos,
        out Quaternion outRot)
    {
        // base along-track point
        Vector3 along = _origin.position + _dirNorm * spawnTrack;

        // we will try up to N times to find a non-overlapping spot
        for (int attempt = 0; attempt < Mathf.Max(1, rule.maxPlacementTries); attempt++)
        {
            // lateral within range + jitter
            float baseLat = UnityEngine.Random.Range(rule.lateralRange.x, rule.lateralRange.y);
            float jitter = (rule.lateralJitter > 0f) ? UnityEngine.Random.Range(-rule.lateralJitter, rule.lateralJitter) : 0f;
            float lateral = baseLat + jitter;

            Vector3 basePos = along + _right * lateral;
            Vector3 pos = basePos;

            if (rule.placeOnGround)
            {
                Vector3 rayStart = basePos + Vector3.up * groundRayStartHeight;
                if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, groundRayStartHeight * 2f, groundMask, QueryTriggerInteraction.Ignore))
                {
                    pos = hit.point + Vector3.up * rule.groundYOffset;
                }
                else
                {
                    // couldn't ground this attempt; try again with a different lateral
                    continue;
                }
            }
            else
            {
                float y = (attempt > 0 && rule.jitterFloatY)
                    ? UnityEngine.Random.Range(rule.floatYRange.x, rule.floatYRange.y)
                    : Mathf.Lerp(rule.floatYRange.x, rule.floatYRange.y, 0.5f);
                pos.y = y;
            }

            // Overlap check
            if (!IsOverlapping(pos, Mathf.Max(0f, rule.collisionRadius), rule.useHorizontalDistance))
            {
                // success
                outPos = pos;
                outRot = rule.alignToTrack
                    ? Quaternion.LookRotation(_dirNorm, Vector3.up) * Quaternion.Euler(rule.localEulerOffset)
                    : Quaternion.Euler(rule.worldEuler);
                return true;
            }
        }

        outPos = default;
        outRot = default;
        return false; // failed to find a non-overlapping placement
    }

    public float minSpawnDistance = 50f;
    /// <summary>
    /// Despawns all active items to the pool and resets rule cursors.
    /// If restartFromPlayer is true, rules resume just ahead of the player; otherwise from initialOffsetMeters.
    /// </summary>
    public void ResetLevel(bool restartFromPlayer = true)
    {
        // Despawn everything
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var ai = _active[i];
            if (ai?.go != null)
            {
                Despawn(ai.go);
            }
            _active.RemoveAt(i);
        }

        float playerTrack = 0f;
        if (restartFromPlayer && player != null)
        {
            playerTrack = ProjectToTrack(player.position);
        }

        // Reset per-rule cursors
        for (int i = 0; i < _runtime.Count; i++)
        {
            var rr = _runtime[i];
            var rule = rr.rule;
            if (rule == null) continue;

            if (restartFromPlayer)
            {
                // Start next spawn just after player within the allowed window, aligned to cadence
                float start = Mathf.Max(playerTrack + rule.spawnAheadMin, 0f);
                // align to cadence tick
                if (rule.everyMeters > 0.0001f)
                {
                    float ticks = Mathf.Floor(start / rule.everyMeters);
                    rr.nextSpawnTrack = (ticks + 1f) * rule.everyMeters;
                }
                else
                {
                    rr.nextSpawnTrack = start;
                }
            }
            else
            {
                rr.nextSpawnTrack = rule.initialOffsetMeters;
            }
        }
    }

    private void Update()
    {
        if (player == null)
        {
            return;
        }

        if (player.position.z < minSpawnDistance)
        {
            return;
        }
        // Keep basis fresh if direction or origin moves/rotates
        _dirNorm = trackDirection.sqrMagnitude > 0.0001f ? trackDirection.normalized : Vector3.forward;
        _basis = Quaternion.FromToRotation(Vector3.forward, _dirNorm);
        _right = _basis * Vector3.right;
        _origin = trackOrigin != null ? trackOrigin : transform;

        float playerTrack = ProjectToTrack(player.position);

        // 1) Spawn pass per rule
        for (int i = 0; i < _runtime.Count; i++)
        {
            var rr = _runtime[i];
            var rule = rr.rule;
            if (rule == null || rule.prefab == null)
            {
                continue;
            }

            // Spawn while the next spawn point is within the ahead window
            // Spawn while the next spawn point is within the ahead window
            float maxAhead = Mathf.Max(0f, rule.spawnAheadMax);
            while (rr.nextSpawnTrack <= playerTrack + maxAhead)
            {
                float spawnTrack = rr.nextSpawnTrack;

                // Ensure within min-ahead as well (optional guard if initialOffset could be behind)
                if (spawnTrack < playerTrack + rule.spawnAheadMin)
                {
                    rr.nextSpawnTrack += Mathf.Max(0.01f, rule.everyMeters);
                    continue;
                }

                // Try to find a non-overlapping placement
                if (!TryBuildSpawnTransform(rule, spawnTrack, out Vector3 spawnPos, out Quaternion rot))
                {
                    // Could not place without overlap this tick; still advance cadence to avoid stalling
                    rr.nextSpawnTrack += Mathf.Max(0.01f, rule.everyMeters);
                    continue;
                }

                // Spawn from pool
                GameObject go = GetFromPool(rule.prefab);
                go.transform.SetPositionAndRotation(spawnPos, rot);
                go.SetActive(true);

                if (go.TryGetComponent<IResettable>(out var resettable))
                {
                    resettable.ResetToInitial();
                }

                _active.Add(new ActiveItem
                {
                    go = go,
                    trackPos = spawnTrack,
                    ruleIndex = i
                });

                // Advance to next cadence tick
                rr.nextSpawnTrack += Mathf.Max(0.01f, rule.everyMeters);
            }

        }

        // 2) Despawn pass
        for (int k = _active.Count - 1; k >= 0; k--)
        {
            var ai = _active[k];
            if (ai.go == null)
            {
                _active.RemoveAt(k);
                continue;
            }

            float delta = playerTrack - ai.trackPos;
            if (delta > despawnBehindDistance)
            {
                Despawn(ai.go);
                _active.RemoveAt(k);
            }
        }
    }

    // -------------------- Pooling --------------------

    private void EnsurePool(GameObject prefab)
    {
        if (!_pool.ContainsKey(prefab))
        {
            _pool[prefab] = new Queue<GameObject>(64);
        }
    }

    private GameObject GetFromPool(GameObject prefab)
    {
        EnsurePool(prefab);

        if (_pool[prefab].Count > 0)
        {
            return _pool[prefab].Dequeue();
        }

        var go = Instantiate(prefab, poolContainer != null ? poolContainer : transform);
        go.SetActive(false);
        return go;
    }

    private void Despawn(GameObject go)
    {
        if (go == null)
        {
            return;
        }

        // Find which prefab bucket it belongs to:
        // Store a PooledMarker at instantiation time to avoid slow lookups.
        if (!go.TryGetComponent<PooledMarker>(out var marker))
        {
            // If the instance predates this script, add a marker the first time.
            marker = go.AddComponent<PooledMarker>();
            marker.prefabKey = FindPrefabKeyFor(go);
        }

        go.SetActive(false);

        if (marker.prefabKey != null)
        {
            EnsurePool(marker.prefabKey);
            _pool[marker.prefabKey].Enqueue(go);
        }
        else
        {
            // As a fallback, just keep it disabled under poolContainer
            if (poolContainer != null)
            {
                go.transform.SetParent(poolContainer, false);
            }
        }
    }

    private GameObject FindPrefabKeyFor(GameObject instance)
    {
        // If you register prefabs at build-time you can store a map <instanceId -> prefab>.
        // Here we try a best-effort: use name matching across known pools.
        foreach (var kv in _pool)
        {
            if (kv.Key != null && instance.name.StartsWith(kv.Key.name, StringComparison.Ordinal))
            {
                return kv.Key;
            }
        }
        // Not guaranteed—only used once to seed marker.
        return null;
    }

    // -------------------- Helpers --------------------

    private float ProjectToTrack(Vector3 worldPos)
    {
        Vector3 origin = _origin != null ? _origin.position : Vector3.zero;
        Vector3 v = worldPos - origin;
        return Vector3.Dot(v, _dirNorm);
    }

    private void OnDrawGizmosSelected()
    {
        // Draw track axis
        Transform org = trackOrigin != null ? trackOrigin : transform;
        Vector3 dir = trackDirection.sqrMagnitude > 0.0001f ? trackDirection.normalized : Vector3.forward;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(org.position, org.position + dir * 10f);
        Gizmos.DrawSphere(org.position, 0.1f);

        // Draw rule windows
        if (rules != null)
        {
            for (int i = 0; i < rules.Count; i++)
            {
                var r = rules[i];
                if (r == null)
                {
                    continue;
                }

                Vector3 minP = org.position + dir * r.spawnAheadMin;
                Vector3 maxP = org.position + dir * r.spawnAheadMax;

                Gizmos.color = new Color(0.2f, 1f, 0.2f, 0.35f);
                Gizmos.DrawCube((minP + maxP) * 0.5f, new Vector3(Mathf.Abs(r.lateralRange.y - r.lateralRange.x), 0.05f, Mathf.Abs(r.spawnAheadMax - r.spawnAheadMin)));
            }
        }
    }

    // -------------------- Types --------------------

    [Serializable]
    public class SpawnRule
    {
        [Header("Prefab")]
        public GameObject prefab;

        [Header("Cadence")]
        public float everyMeters = 50f;
        public float spawnAheadMin = 10f;
        public float spawnAheadMax = 80f;
        public float initialOffsetMeters = 0f;

        [Header("Placement")]
        public Vector2 lateralRange = new Vector2(-3f, 3f);
        public bool placeOnGround = true;
        public float groundYOffset = 0f;
        public Vector2 floatYRange = new Vector2(2f, 6f);
        public bool alignToTrack = true;
        public Vector3 localEulerOffset = Vector3.zero;
        public Vector3 worldEuler = Vector3.zero;

        [Header("Pooling")]
        public int prewarmCount = 0;

        // NEW ---------------------------
        [Header("Overlap Control")]
        [Tooltip("Collision radius (meters) used to avoid overlapping other active spawns.")]
        public float collisionRadius = 1f;

        [Tooltip("Use horizontal (XZ) distance for overlap checks. If false, uses full 3D distance.")]
        public bool useHorizontalDistance = true;

        [Tooltip("Placement jitter attempts to find a non-overlapping spot within lateral (and height) ranges.")]
        [Min(1)]
        public int maxPlacementTries = 6;

        [Tooltip("Extra random lateral jitter added per try (meters). 0 = only within lateralRange.")]
        public float lateralJitter = 0.5f;

        [Tooltip("When floating, also jitter Y within the floatYRange each try.")]
        public bool jitterFloatY = true;
    }


    private class RuleRuntime
    {
        public SpawnRule rule;
        public float nextSpawnTrack;
    }

    private class ActiveItem
    {
        public GameObject go;
        public float trackPos;  // projected track distance at spawn
        public int ruleIndex;
    }

    private sealed class PooledMarker : MonoBehaviour
    {
        public GameObject prefabKey;
    }
}
