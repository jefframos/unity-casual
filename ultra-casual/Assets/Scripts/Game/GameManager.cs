
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using System;

public class GameManager : MonoBehaviour
{
    [Header("Controller")]
    [Tooltip("Assign a MonoBehaviour that implements IGameController. If left empty, the manager will try to find one at runtime.")]
    public MonoBehaviour gameControllerBehaviour; // must implement IGameController

    private IGameController _gameController;
    private readonly List<IResettable> _resettableCache = new();

    // Cancellation for the active restart flow
    private CancellationTokenSource _restartCts;

    private void Start()
    {
        CacheGameController();
        RebuildResettableCache();
    }

    private void OnDisable()
    {
        _restartCts?.Cancel();
        _restartCts?.Dispose();
        _restartCts = null;
    }

    private void CacheGameController()
    {
        _gameController = null;

        if (gameControllerBehaviour != null)
        {
            _gameController = gameControllerBehaviour as IGameController;
            if (_gameController == null)
                Debug.LogError("[GameManager] Assigned behaviour does not implement IGameController.");
        }

        if (_gameController == null)
        {
            var all = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var mb in all)
            {
                if (mb is IGameController gc)
                {
                    _gameController = gc;
                    gameControllerBehaviour = mb; // keep inspector visible
                    break;
                }
            }
        }

        if (_gameController == null)
            Debug.LogWarning("[GameManager] No IGameController found. Will still reset IResettable objects.");
    }

    /// <summary>Build the list of scene objects that implement IResettable.</summary>
    public void RebuildResettableCache()
    {
        _resettableCache.Clear();

        var all = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        foreach (var mb in all)
        {
            if (!mb || !mb.isActiveAndEnabled) continue;
            if (mb is IResettable r)
                _resettableCache.Add(r);
        }
    }

    /// <summary>Start a new run: reset everything and ask the controller to prep gameplay.</summary>
    public void StartGame()
    {
        _gameController?.ResetGameState();
        ResetAll();
    }

    /// <summary>End the current run (optional; currently no-op in controller).</summary>
    public void EndGame()
    {
        _gameController?.EndGame();
    }

    /// <summary>Show final score, then reset and restart gameplay (UniTask flow).</summary>
    public void RestartGame(float final)
    {
        // Cancel any previous flow
        _restartCts?.Cancel();
        _restartCts?.Dispose();
        _restartCts = new CancellationTokenSource();

        RestartRoutineAsync(final, _restartCts.Token).Forget();
    }

    private async UniTask RestartRoutineAsync(float final, CancellationToken token)
    {
        try
        {
            // 1) End current run (optional)
            EndGame();

            // 2) Find any presenter and show the score
            var endGameOrchestrator = FindObjectsByType<EndGameOrchestrator>(FindObjectsSortMode.None)
                .FirstOrDefault();

            if (endGameOrchestrator != null)
            {
                await endGameOrchestrator.OrchestrateEnd(runScoreDelta: 10, startScoreValue: 200);
            }
            else
            {
                Debug.LogWarning("[GameManager] No IFinalScorePresenter found. Restarting quickly.");
                await UniTask.Delay(TimeSpan.FromSeconds(0.25),
                    DelayType.UnscaledDeltaTime,
                    PlayerLoopTiming.Update,
                    token);
            }

            // 3) Start new run
            StartGame();
        }
        catch (OperationCanceledException)
        {
            // Swallow if we canceled due to disable/restart spam
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private void ResetAll()
    {
        foreach (var r in _resettableCache.ToList())
        {
            //if (r is GameObject unityObj && unityObj == null) continue; // destroyed
            r.ResetToInitial();
        }
    }

    // Quick keyboard test
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R)) RestartGame(100f);
        if (Input.GetKeyDown(KeyCode.E)) EndGame();
    }
}
