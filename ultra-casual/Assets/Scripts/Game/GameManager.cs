using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Threading.Tasks;

public class GameManager : MonoBehaviour
{
    [Header("Controller")]
    [Tooltip("Assign a MonoBehaviour that implements IGameController. If left empty, the manager will try to find one at runtime.")]
    public MonoBehaviour gameControllerBehaviour; // must implement IGameController

    [Header("UI")]
    public SlingshotCinemachineBridge cameraBridge;
    public GameUiHandler uiHandler;
    public UiMode startMode = UiMode.MainMenu;
    public LevelManager levelManager;

    private IGameController _gameController;

    // Cancellation for the active restart flow
    private CancellationTokenSource _restartCts;

    private bool nextIsHighscore = false;

    private void Start()
    {
        CacheGameController();

        if (uiHandler != null)
        {
            uiHandler.SetMode(startMode);
        }

        StartGame();
    }

    private void OnDisable()
    {
        _restartCts?.Cancel();
        _restartCts?.Dispose();
        _restartCts = null;

        if (_gameController != null)
        {
            _gameController.OnEnterGameMode -= OnEnterGameMode;
            _gameController.OnReleaseStarted -= OnReleaseStarted;
        }
    }

    private void CacheGameController()
    {
        if (levelManager == null)
        {
            levelManager = FindAnyObjectByType<LevelManager>();
        }

        _gameController = null;

        if (gameControllerBehaviour != null)
        {
            _gameController = gameControllerBehaviour as IGameController;
            if (_gameController == null)
            {
                Debug.LogError("[GameManager] Assigned behaviour does not implement IGameController.");
            }
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
        {
            Debug.LogWarning("[GameManager] No IGameController found. Will still reset IResettable objects.");
            return;
        }

        _gameController.OnEnterGameMode += OnEnterGameMode;
        _gameController.OnReleaseStarted += OnReleaseStarted;
    }

    // --------------------------------------------------
    // Game flow hooks
    // --------------------------------------------------

    public void OnReleaseStarted(Transform launcher)
    {
        if (uiHandler != null)
        {
            uiHandler.SetMode(UiMode.InGame);
        }

        PokiBridge.GameplayStart();
    }

    public void OnEnterGameMode(Transform launcher)
    {
        if (uiHandler != null)
        {
            uiHandler.SetMode(UiMode.PreGame);
        }
    }

    /// <summary>Start a new run: reset everything and ask the controller to prep gameplay.</summary>
    public void StartGame()
    {
        cameraBridge.SetCameraMode(SlingshotCinemachineBridge.GameCameraMode.PreGame);
        _gameController?.ResetGameState();

        ResetAll();
        EnemyFallCoordinator.Instance?.ResetCoordinator();

        // Tell mediator to refresh its view of the world & UI
        LevelTrackerMediator.Instance.RefreshLevels();
    }

    /// <summary>End the current run.</summary>
    public void EndGame()
    {
        _gameController?.EndGame();
        PokiBridge.GameplayStop();
    }

    public void NewHighscore(float final)
    {
        nextIsHighscore = true;
    }

    public void RestartGame(float final)
    {
        _ = RestartGame((int)final);
    }

    /// <summary>Show final score, then reset and restart gameplay (UniTask flow).</summary>
    public async Task RestartGame(int final)
    {
        await UniTask.WaitForSeconds(1f);

        if (ExplosionCoordinator.Instance != null)
        {
            await ExplosionCoordinator.Instance.WaitForAllExplosionsAsync();
        }

        if (EnemyFallCoordinator.Instance != null)
        {
            await EnemyFallCoordinator.Instance.WaitForEnemiesToSettleAsync();
        }

        _restartCts?.Cancel();
        _restartCts?.Dispose();
        _restartCts = new CancellationTokenSource();

        if (uiHandler != null)
        {
            uiHandler.SetMode(UiMode.GameOver);
        }

        await RestartRoutineAsync(final, _restartCts.Token);

        if (nextIsHighscore && uiHandler != null)
        {
            uiHandler.SetNewHighscore(final);
        }

        if (uiHandler != null)
        {
            uiHandler.SetMode(UiMode.OutGame);
        }

        nextIsHighscore = false;
    }

    private async UniTask RestartRoutineAsync(int final, CancellationToken token)
    {
        try
        {
            EndGame();

            var deathsCopy = LevelTrackerMediator.Instance.GetDeathCountsSinceRefreshCopy();

            foreach (var kvp in deathsCopy)
            {
                EnemyGrade grade = kvp.Key;
                int deaths = kvp.Value;

                Debug.Log($"[DeathCounter] Grade: {grade} | Deaths this refresh: {deaths}");
            }


            var endGameOrchestrator = FindObjectsByType<EndGameOrchestrator>(FindObjectsSortMode.None)
                .FirstOrDefault();

            if (endGameOrchestrator != null)
            {
                await endGameOrchestrator.OrchestrateEnd(
                    runScoreDelta: 10,
                    startScoreValue: final,
                    nextIsHighscore
                );
            }
            else
            {
                Debug.LogWarning("[GameManager] No IFinalScorePresenter found. Restarting quickly.");
                await UniTask.Delay(
                    TimeSpan.FromSeconds(0.25),
                    DelayType.UnscaledDeltaTime,
                    PlayerLoopTiming.Update,
                    token
                );
            }

            StartGame();
        }
        catch (OperationCanceledException)
        {
            // expected
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private void ResetAll()
    {
        levelManager?.ResetAll();
    }

    // Quick keyboard test
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R)) _ = RestartGame(100);
        if (Input.GetKeyDown(KeyCode.E)) EndGame();
    }
}
