using System;
using System.Threading;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class EndgameMinigameOrchestrator : MonoBehaviour
{
    [Header("Camera / Space")]
    [Tooltip("Camera used for facing targets. If null, will use Camera.main.")]
    public Camera targetCamera;
    public Canvas canvas;

    [Header("Spawn Origin")]
    [Tooltip("World-space spawn origin. Targets will spawn around this position.")]
    public Transform spawnOrigin;

    public Animator enemyAnimator;

    [Tooltip("Horizontal range (world units) around spawn origin on its local X axis.")]
    public float horizontalRange = 2f;

    [Tooltip("Depth range (world units) around spawn origin on its local Z/forward axis.")]
    public float depthRange = 0.5f;

    [Tooltip("Extra rotation offset in degrees applied after LookAt(camera).")]
    public Vector3 rotationOffsetEuler = Vector3.zero;

    [Header("Target Prefab")]
    [Tooltip("3D prefab with EndgameMinigameTarget + Collider.")]
    public EndgameMinigameTarget targetPrefab;

    public EndgameMinigameGun GunComponent;
    public EndgameGunInput EndgameGunInput;

    [Header("Gameplay")]
    [Tooltip("Total number of targets to spawn for this minigame run.")]
    public int totalTargetsToSpawn = 10;

    [Tooltip("Maximum targets at the same time on screen.")]
    public int maxSimultaneousTargets = 3;

    [Tooltip("Time between spawns in seconds.")]
    public float spawnInterval = 0.4f;

    [Header("Randomization")]
    [Tooltip("Lifetime range (seconds) for targets.")]
    public Vector2 lifetimeRange = new Vector2(2f, 3f);

    [Tooltip("Base speed range (world units per second) for targets.")]
    public Vector2 speedRange = new Vector2(4f, 6f);

    [Tooltip("Normal target uniform scale range.")]
    public Vector2 normalScaleRange = new Vector2(1.0f, 1.2f);

    [Tooltip("Small (harder) target uniform scale range.")]
    public Vector2 smallScaleRange = new Vector2(0.6f, 0.8f);

    public float baseScale = 6f;

    [Tooltip("Chance [0..1] that a spawned target is 'small' (worth more points).")]
    [Range(0f, 1f)]
    public float smallTargetChance = 0.3f;

    [Tooltip("Speed multiplier for small targets (on top of random speedRange).")]
    public float smallTargetSpeedMultiplier = 1.2f;

    [Header("Easing Curves")]
    [Tooltip("Multiplier for speed over normalized lifetime [0..1]. 1 = constant.")]
    public AnimationCurve speedOverLifetime = AnimationCurve.Linear(0f, 1f, 1f, 1f);

    [Tooltip("Multiplier for scale over normalized lifetime [0..1]. 1 = constant.")]
    public AnimationCurve scaleOverLifetime = AnimationCurve.Linear(0f, 1f, 1f, 1f);

    [Header("Rewards")]
    [Tooltip("Base reward for a normal-sized target hit.")]
    public int normalTargetReward = 10;

    [Tooltip("Base reward for a small-sized target hit.")]
    public int smallTargetReward = 25;

    [Tooltip("Optional multiplier applied at the very end to the total reward.")]
    public float finalRewardMultiplier = 1f;

    [Header("Combo")]
    [Tooltip("How much the multiplier increases per consecutive hit (no misses). " +
             "Total multiplier = 1 + (comboCount - 1) * comboStepMultiplier.")]
    public float comboStepMultiplier = 0.25f;

    [Tooltip("If true, missing a target resets the combo back to 0.")]
    public bool resetComboOnMiss = true;

    [Header("Pooling")]
    [Tooltip("Initial number of pooled targets to pre-create.")]
    public int initialPoolSize = 10;

    [Header("Spawn Collision Avoidance")]
    [Tooltip("Minimum distance between spawned targets to avoid overlaps.")]
    public float minSpawnDistance = 0.5f;

    [Tooltip("How many attempts to find a non-overlapping spawn position before giving up.")]
    public int maxSpawnPositionAttempts = 6;

    [Serializable]
    public class TargetEvent : UnityEvent<EndgameMinigameTarget> { }

    [Tooltip("Invoked when a target is hit (before it is returned to pool).")]
    public TargetEvent onTargetHit;

    [Tooltip("Invoked when a target is missed (lifetime expired / canceled).")]
    public TargetEvent onTargetMissed;

    [Header("Boss / Life UI")]
    [Tooltip("Component that will receive normalized progress (0..1) based on how many targets were hit.")]
    public EndgameMinigameBossHealth bossLifeBar;

    [Tooltip("Extra reward added if ALL targets are hit (boss killed).")]
    public int bossKillBonus = 0;

    [Header("Result / Summary UI")]
    [Tooltip("Component that shows how many targets were hit and animates the prize TMP text.")]
    public EndgameMinigameSummaryView summaryView;

    [Header("Debug")]
    public bool debugLogs = false;

    // ------------------------------------------
    // Runtime
    // ------------------------------------------

    private int _spawnedCount;
    private int _resolvedCount;   // hit or missed
    private int _hitCount;
    private bool _isRunning;
    private CancellationToken _linkedToken;

    private int _activeTargetsCount;
    private int _totalReward;
    private int _currentCombo;

    private readonly Queue<EndgameMinigameTarget> _pool = new Queue<EndgameMinigameTarget>();
    private readonly List<EndgameMinigameTarget> _activeTargets = new List<EndgameMinigameTarget>();
    private Transform _poolRoot;

    // ------------------------------------------
    // Pool
    // ------------------------------------------

    private void Awake()
    {
        canvas.gameObject.SetActive(false);
        EndgameGunInput.enabled = false;
        if (targetPrefab != null)
        {
            EnsurePool();
        }

        if (GunComponent != null)
        {
            GunComponent.gameObject.SetActive(false);
        }

        // Reset boss bar on awake just in case
        if (bossLifeBar != null)
        {
            bossLifeBar.SetNormalizedProgress(0f);

            bossLifeBar.gameObject.SetActive(false);
        }
    }

    private void EnsurePool()
    {
        if (targetPrefab == null)
        {
            return;
        }

        if (_poolRoot == null)
        {
            var go = new GameObject("EndgameMinigameTargetPool");
            go.transform.SetParent(transform);
            go.SetActive(false);
            _poolRoot = go.transform;
        }

        while (_pool.Count < initialPoolSize)
        {
            var t = Instantiate(targetPrefab, _poolRoot);
            t.gameObject.SetActive(false);
            _pool.Enqueue(t);
        }
    }

    private EndgameMinigameTarget GetFromPool()
    {
        EnsurePool();

        EndgameMinigameTarget inst;
        if (_pool.Count > 0)
        {
            inst = _pool.Dequeue();
            inst.transform.SetParent(null);
        }
        else
        {
            inst = Instantiate(targetPrefab);
        }

        _activeTargetsCount++;
        _activeTargets.Add(inst);

        return inst;
    }

    private void ReturnToPool(EndgameMinigameTarget t)
    {
        if (t == null)
        {
            return;
        }

        _activeTargetsCount = Mathf.Max(0, _activeTargetsCount - 1);
        _activeTargets.Remove(t);

        if (_poolRoot == null)
        {
            EnsurePool();
        }

        t.gameObject.SetActive(false);
        t.transform.SetParent(_poolRoot);
        _pool.Enqueue(t);
    }

    // ------------------------------------------
    // Public API
    // ------------------------------------------

    /// <summary>
    /// Plays the whole minigame:
    /// - Spawns & resolves targets
    /// - Updates boss life bar as targets are hit
    /// - Computes final reward (including boss kill bonus)
    /// - Shows summary UI (hits + animated reward text)
    /// </summary>
    public async UniTask<int> PlayMinigameAsync(CancellationToken externalToken)
    {
        canvas.gameObject.SetActive(true);
        bossLifeBar.gameObject.SetActive(true);
        EndgameGunInput.enabled = true;
        if (!isActiveAndEnabled)
        {
            if (debugLogs)
            {
                Debug.Log("[EndgameMinigameOrchestrator] Not active/enabled, returning 0 bonus.");
            }

            return 0;
        }

        if (enemyAnimator)
        {
            enemyAnimator.ResetTrigger("Die");
            enemyAnimator.ResetTrigger("Hit");
            enemyAnimator.Play("Idle");
        }

        if (targetPrefab == null)
        {
            Debug.LogWarning("[EndgameMinigameOrchestrator] Missing targetPrefab. Returning 0 bonus.");
            return 0;
        }

        if (spawnOrigin == null)
        {
            Debug.LogWarning("[EndgameMinigameOrchestrator] Missing spawnOrigin. Returning 0 bonus.");
            return 0;
        }

        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[EndgameMinigameOrchestrator] No camera assigned and Camera.main is null. Returning 0 bonus.");
            return 0;
        }

        if (_isRunning)
        {
            Debug.LogWarning("[EndgameMinigameOrchestrator] Already running. Returning 0 bonus.");
            return 0;
        }

        if (GunComponent != null)
        {
            GunComponent.gameObject.SetActive(true);
        }

        _isRunning = true;
        _spawnedCount = 0;
        _resolvedCount = 0;
        _hitCount = 0;
        _totalReward = 0;
        _currentCombo = 0;

        if (bossLifeBar != null)
        {
            bossLifeBar.SetNormalizedProgress(0f);
        }

        int finalReward = 0;
        bool bossKilled = false;

        using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken))
        {
            _linkedToken = linkedCts.Token;

            try
            {
                // Ensure at least 1 frame passes (gun appears, etc.)
                await UniTask.Yield(PlayerLoopTiming.Update, _linkedToken);

                // Main loop (spawning + waiting for resolution)
                await RunInternalAsync(cam, _linkedToken);

                // Boss kill check: player must hit ALL targets
                bossKilled = (_hitCount >= totalTargetsToSpawn && totalTargetsToSpawn > 0);

                int baseTotal = _totalReward;

                if (bossKilled && bossKillBonus > 0)
                {
                    baseTotal += bossKillBonus;
                }

                finalReward = Mathf.RoundToInt(baseTotal * finalRewardMultiplier);

                // Show summary UI (hits + total prize) before finishing
                if (summaryView != null && !_linkedToken.IsCancellationRequested)
                {
                    EndgameGunInput.enabled = false;
                    bossLifeBar.gameObject.SetActive(false);
                    await summaryView.PlaySummaryAsync(
                        hitCount: _hitCount,
                        totalTargets: totalTargetsToSpawn,
                        totalPrize: finalReward,
                        bossKilled: bossKilled,
                        token: _linkedToken
                    );
                }
            }
            catch (OperationCanceledException)
            {
                if (debugLogs)
                {
                    Debug.Log("[EndgameMinigameOrchestrator] Canceled.");
                }
            }
            finally
            {
                _isRunning = false;
            }
        }

        if (debugLogs)
        {
            Debug.Log(
                $"[EndgameMinigameOrchestrator] Finished. Hits={_hitCount}, " +
                $"BaseTotal={_totalReward}, BossKilled={bossKilled}, " +
                $"Final={finalReward}"
            );
        }

        if (GunComponent != null)
        {
            GunComponent.gameObject.SetActive(false);
        }

        canvas.gameObject.SetActive(false);
        return finalReward;
    }

    // ------------------------------------------
    // Internal run
    // ------------------------------------------

    private async UniTask RunInternalAsync(Camera cam, CancellationToken token)
    {
        while (_spawnedCount < totalTargetsToSpawn && !token.IsCancellationRequested)
        {
            if (_activeTargetsCount < maxSimultaneousTargets)
            {
                SpawnOneTarget(cam, token);
            }

            await UniTask.Delay(
                TimeSpan.FromSeconds(spawnInterval),
                DelayType.UnscaledDeltaTime,
                PlayerLoopTiming.Update,
                token
            );
        }

        int requiredResolved = Math.Min(_spawnedCount, totalTargetsToSpawn);

        while (_resolvedCount < requiredResolved && !token.IsCancellationRequested)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }
    }

    private void SpawnOneTarget(Camera cam, CancellationToken token)
    {
        if (targetPrefab == null || spawnOrigin == null)
        {
            return;
        }

        _spawnedCount++;

        // -------------------------
        // Choose spawn position with non-overlap attempts
        // -------------------------
        Vector3 chosenPos = spawnOrigin.position;
        float halfX = horizontalRange * 0.5f;
        float halfZ = depthRange * 0.5f;

        bool foundValid = false;

        for (int attempt = 0; attempt < maxSpawnPositionAttempts; attempt++)
        {
            float xOffset = UnityEngine.Random.Range(-halfX, halfX);
            float zOffset = UnityEngine.Random.Range(-halfZ, halfZ);

            Vector3 candidate =
                spawnOrigin.position +
                spawnOrigin.right * xOffset +
                spawnOrigin.forward * zOffset;

            bool overlaps = false;

            for (int i = 0; i < _activeTargets.Count; i++)
            {
                var at = _activeTargets[i];
                if (at == null)
                {
                    continue;
                }

                float dist = Vector3.Distance(candidate, at.transform.position);
                if (dist < minSpawnDistance)
                {
                    overlaps = true;
                    break;
                }
            }

            if (!overlaps)
            {
                chosenPos = candidate;
                foundValid = true;
                break;
            }
        }

        if (!foundValid)
        {
            // If we can't find a perfect spot, just use the last candidate / origin.
        }

        var instance = GetFromPool();
        instance.transform.position = chosenPos;

        // Face camera
        Vector3 toCam = cam.transform.position - instance.transform.position;
        if (toCam.sqrMagnitude > 0.0001f)
        {
            instance.transform.rotation = Quaternion.LookRotation(toCam.normalized, Vector3.up);
        }

        if (rotationOffsetEuler != Vector3.zero)
        {
            instance.transform.rotation *= Quaternion.Euler(rotationOffsetEuler);
        }

        // Movement direction: upwards (spawnOrigin up if available, otherwise world up)
        Vector3 moveDir = spawnOrigin != null ? spawnOrigin.up : Vector3.up;

        // Decide target type (normal vs small)
        bool isSmall = UnityEngine.Random.value < smallTargetChance;
        int rewardValue = isSmall ? smallTargetReward : normalTargetReward;

        float lifetime = UnityEngine.Random.Range(lifetimeRange.x, lifetimeRange.y);
        float speed = UnityEngine.Random.Range(speedRange.x, speedRange.y);

        if (isSmall)
        {
            speed *= smallTargetSpeedMultiplier;
        }

        float scale = isSmall
            ? UnityEngine.Random.Range(smallScaleRange.x, smallScaleRange.y)
            : UnityEngine.Random.Range(normalScaleRange.x, normalScaleRange.y);

        Vector3 _baseScale = Vector3.one * scale * baseScale;

        instance.gameObject.SetActive(true);

        instance.Init(
            owner: this,
            moveDirection: moveDir,
            speed: speed,
            lifetime: lifetime,
            token: token,
            rewardValue: rewardValue,
            speedCurve: speedOverLifetime,
            scaleCurve: scaleOverLifetime,
            baseScale: _baseScale
        );
    }

    // ------------------------------------------
    // Notifications from targets
    // ------------------------------------------

    public void NotifyTargetHit(EndgameMinigameTarget target)
    {
        _hitCount++;
        _resolvedCount++;

        _currentCombo++;
        float comboMultiplier = 1f + (_currentCombo - 1) * comboStepMultiplier;
        int rewardFromHit = Mathf.RoundToInt(target.RewardValue * comboMultiplier);
        _totalReward += rewardFromHit;

        // Normalized progress (0..1) based on how many targets we already hit.
        float normalized =
            (totalTargetsToSpawn > 0)
                ? Mathf.Clamp01((float)_hitCount / totalTargetsToSpawn)
                : 0f;
        if (bossLifeBar != null)
        {

            bossLifeBar.SetNormalizedProgress(normalized);
        }

        if (debugLogs)
        {
            Debug.Log(
                $"[EndgameMinigameOrchestrator] Target hit. " +
                $"Hits={_hitCount}, Resolved={_resolvedCount}, Combo={_currentCombo}, " +
                $"HitReward={rewardFromHit}, Total={_totalReward}"
            );
        }

        if (enemyAnimator != null)
        {
            if (normalized >= 1f)
            {
                enemyAnimator.SetTrigger("Die");
            }
            else
            {
                enemyAnimator.SetTrigger("Hit");
            }
        }

        onTargetHit?.Invoke(target);
        ReturnToPool(target);
    }

    public void NotifyTargetMissed(EndgameMinigameTarget target)
    {
        _resolvedCount++;

        if (resetComboOnMiss)
        {
            _currentCombo = 0;
        }

        if (debugLogs)
        {
            Debug.Log(
                $"[EndgameMinigameOrchestrator] Target missed. " +
                $"Resolved={_resolvedCount}, ComboReset={resetComboOnMiss}"
            );
        }

        onTargetMissed?.Invoke(target);
        ReturnToPool(target);
    }
}
