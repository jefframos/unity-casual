using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Tracks all enemies that can fall and provides async waiting until
/// all falling enemies have finished falling (or died).
/// </summary>
public class EnemyFallCoordinator : MonoBehaviour
{
    public static EnemyFallCoordinator Instance { get; private set; }

    [Tooltip("Invoked whenever enemy-fall resolving state changes (true = some enemy is falling).")]
    public UnityEvent<bool> onResolvingStateChanged;

    [SerializeField] private int _fallingCount;
    public bool IsResolvingEnemyFalls => _fallingCount > 0;

    private CancellationTokenSource _cts = new();

    // All hooked enemies (so we can clean/remove when resetting a scene)
    private readonly HashSet<RagdollEnemy> _hookedEnemies = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    // ============================================================
    //  RESET
    // ============================================================

    /// <summary>
    /// Fully resets the coordinator: unhooks all enemies, clears internal state,
    /// resets CTS, then hooks all enemies currently in the scene.
    /// Call this every time a new level is instantiated.
    /// </summary>
    public void ResetCoordinator()
    {
        // 1) Unhook all previous enemies (no need to remove listeners manually because they go null)
        _hookedEnemies.Clear();

        // 2) Reset counters
        _fallingCount = 0;

        // If we were previously resolving falls, fire false
        onResolvingStateChanged?.Invoke(false);

        // 3) Reset cancellation token
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        // 4) Re-hook all enemies in the scene
        HookAllEnemies();
    }

    // ============================================================
    //  ENEMY HOOKING
    // ============================================================

    public void HookAllEnemies()
    {
        var enemies = FindObjectsByType<RagdollEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var enemy in enemies)
            RegisterEnemy(enemy);
    }

    public void RegisterEnemy(RagdollEnemy enemy)
    {
        if (enemy == null) return;
        if (_hookedEnemies.Contains(enemy)) return;

        _hookedEnemies.Add(enemy);

        enemy.onFallStarted.AddListener(() => OnEnemyFallStarted(enemy));
        enemy.onFallEnded.AddListener(() => OnEnemyFallEnded(enemy));

        // If already falling when we register it:
        if (enemy.IsFalling)
            OnEnemyFallStarted(enemy);
    }

    // ============================================================
    //  FALL TRACKING
    // ============================================================

    private void OnEnemyFallStarted(RagdollEnemy enemy)
    {
        int prev = _fallingCount;
        _fallingCount++;

        if (prev == 0 && _fallingCount == 1)
        {
            onResolvingStateChanged?.Invoke(true);

            // new token for this wave
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
        }
    }

    private void OnEnemyFallEnded(RagdollEnemy enemy)
    {
        int prev = _fallingCount;
        _fallingCount = Mathf.Max(0, _fallingCount - 1);

        if (prev > 0 && _fallingCount == 0)
        {
            onResolvingStateChanged?.Invoke(false);

            // wake awaiters
            _cts?.Cancel();
        }
    }

    // ============================================================
    //  ASYNC WAIT
    // ============================================================

    public async UniTask WaitForEnemiesToSettleAsync()
    {
        if (_fallingCount <= 0)
            return;

        var localCts = _cts;
        try
        {
            await UniTask.WaitUntil(() => !IsResolvingEnemyFalls, cancellationToken: localCts.Token);
        }
        catch (OperationCanceledException)
        {
            // expected
        }
    }

    /// <summary>
    /// Convenience: hook all enemies then wait.
    /// </summary>
    public async UniTask HookAndWaitForEnemiesToSettleAsync()
    {
        HookAllEnemies();
        await WaitForEnemiesToSettleAsync();
    }
}
