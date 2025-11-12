using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Tracks all active explosions and provides async waiting until all explosions are resolved.
/// </summary>
public class ExplosionCoordinator : MonoBehaviour
{
    public static ExplosionCoordinator Instance { get; private set; }

    [Tooltip("Invoked whenever explosion resolving state changes (true = explosions active).")]
    public UnityEvent<bool> onResolvingStateChanged;

    [SerializeField] private int _activeCount;
    public bool IsResolvingExplosions => _activeCount > 0;

    private CancellationTokenSource _cts = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        // Optional: persist across scenes
        // DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }
    }

    public void NotifyOneStarted()
    {
        int prev = _activeCount;
        _activeCount++;
        if (prev == 0 && _activeCount == 1)
        {
            onResolvingStateChanged?.Invoke(true);
            // reset CTS so waiters can re-arm
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
        }
    }

    public void NotifyOneFinished()
    {
        int prev = _activeCount;
        _activeCount = Mathf.Max(0, _activeCount - 1);
        if (prev > 0 && _activeCount == 0)
        {
            onResolvingStateChanged?.Invoke(false);
            // trigger completion for awaiters
            _cts?.Cancel();
        }
    }

    /// <summary>
    /// Waits asynchronously until all explosions are finished.
    /// If no explosions are active, returns immediately.
    /// </summary>
    public async UniTask WaitForAllExplosionsAsync()
    {
        // if nothing is active, no need to wait
        if (_activeCount <= 0)
            return;

        var localCts = _cts;
        try
        {
            // Wait until this CTS is canceled when explosions end
            await UniTask.WaitUntil(() => !IsResolvingExplosions, cancellationToken: localCts.Token);
        }
        catch (OperationCanceledException)
        {
            // expected when explosions complete
        }
    }
}
