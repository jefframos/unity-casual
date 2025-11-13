using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Threading.Tasks;
using UnityEngine.Rendering;

public class GameManager : MonoBehaviour
{
    [Header("Controller")]
    [Tooltip("Assign a MonoBehaviour that implements IGameController. If left empty, the manager will try to find one at runtime.")]
    public MonoBehaviour gameControllerBehaviour; // must implement IGameController

    [Header("UI")]
    public SlingshotCinemachineBridge cameraBridge;
    public GameUiHandler uiHandler;
    public UiMode startMode = UiMode.MainMenu; // optional: initial state at play
    public LevelManager levelManager; // optional: initial state at play

    private IGameController _gameController;

    // Cancellation for the active restart flow
    private CancellationTokenSource _restartCts;

    private void Start()
    {
        CacheGameController();

        if (uiHandler != null)
        {
            uiHandler.SetMode(startMode);
        }

    }

    private void OnDisable()
    {
        _restartCts?.Cancel();
        _restartCts?.Dispose();
        _restartCts = null;
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
        }

        _gameController.OnEnterGameMode += OnEnterGameMode;
        _gameController.OnReleaseStarted += OnReleaseStarted;
    }

    public void OnReleaseStarted(Transform launcher)
    {
        // Switch UI to gameplay HUD
        if (uiHandler != null)
        {
            uiHandler.SetMode(UiMode.InGame);
        }

        PokiBridge.GameplayStart();
    }
    /// <summary>Build the list of scene objects that implement IResettable.</summary>
    public void OnEnterGameMode(Transform launcher)
    {
        // Switch UI to gameplay HUD
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


    }

    /// <summary>End the current run.</summary>
    public void EndGame()
    {
        _gameController?.EndGame();

        PokiBridge.GameplayStop();


    }

    private bool nextIsHighscore = false;
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

        // Wait for all chain reactions to finish before ending the level
        if (ExplosionCoordinator.Instance != null)
        {
            await ExplosionCoordinator.Instance.WaitForAllExplosionsAsync();
        }


        _restartCts?.Cancel();
        _restartCts?.Dispose();
        _restartCts = new CancellationTokenSource();


        if (uiHandler != null)
        {
            uiHandler.SetMode(UiMode.GameOver); // or UiMode.GameOver / Results
        }
        await RestartRoutineAsync(final, _restartCts.Token);


        if (nextIsHighscore)
        {
            uiHandler.SetNewHighscore(final);
        }
        // Swap UI to out-of-game or results/menu as you prefer
        if (uiHandler != null)
        {
            uiHandler.SetMode(UiMode.OutGame); // or UiMode.GameOver / Results
        }

        nextIsHighscore = false;
    }

    private async UniTask RestartRoutineAsync(int final, CancellationToken token)
    {
        try
        {
            // 1) End current run (optional)
            EndGame();

            // 2) Show result/score while in OutGame/Results UI
            var endGameOrchestrator = FindObjectsByType<EndGameOrchestrator>(FindObjectsSortMode.None)
                .FirstOrDefault();


            if (endGameOrchestrator != null)
            {
                await endGameOrchestrator.OrchestrateEnd(runScoreDelta: 10, startScoreValue: final, nextIsHighscore);


            }
            else
            {
                Debug.LogWarning("[GameManager] No IFinalScorePresenter found. Restarting quickly.");
                await UniTask.Delay(TimeSpan.FromSeconds(0.25),
                    DelayType.UnscaledDeltaTime,
                    PlayerLoopTiming.Update,
                    token);
            }



            // 3) Start new run + switch UI back to InGame
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
        levelManager.ResetAll();
    }

    // Quick keyboard test
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R)) _ = RestartGame(100);
        if (Input.GetKeyDown(KeyCode.E)) EndGame();
    }
}
