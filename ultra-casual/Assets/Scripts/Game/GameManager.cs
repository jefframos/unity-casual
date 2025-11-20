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

    void Start()
    {
        _ = StartAsync();
    }
    private async Task StartAsync()
    {
        CacheGameController();

        int startGlobalIndex = 0;

        if (ProgressionManager.Instance != null)
        {
            startGlobalIndex = ProgressionManager.Instance.CurrentGlobalLevelIndex;
        }

        await levelManager.SpawnLevelByGlobalIndex(startGlobalIndex);

        await StartGame();

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
    public async Task StartGame()
    {
        _gameController.DisableInput();

        _gameController.ResetGameState();



        //ResetAll();
        levelManager?.ResetAll();

        await levelManager.StartLevel(cameraBridge, uiHandler);

        cameraBridge.SetCameraMode(SlingshotCinemachineBridge.GameCameraMode.PreGame);
        _gameController.EnableInput();

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

            var snapshot = levelManager.EndLevel();

            EndGame();

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

            Debug.Log(levelManager.LastRunCompletedLevel);
            Debug.Log(levelManager.LastRunCompletedLevel);

            int nextLevelToSpawn = levelManager.CurrentGlobalLevelIndex; // fallback

            if (levelManager.LastRunCompletedLevel)
            {
                Debug.LogWarning("SHOW END LEVEL HERE");
                Debug.Log($"[LevelManager] Total killed so far: {snapshot.totalDead}/{snapshot.totalEnemies}");
                foreach (var g in snapshot.grades)
                {
                    Debug.Log($"[LevelManager] Grade {g.grade}: {g.dead}/{g.total} dead (current: {g.isCurrent})");
                }

                var nextLevelOrchestrator = FindObjectsByType<NextLevelOrchestrator>(FindObjectsSortMode.None)
                    .FirstOrDefault();

                ProgressionManager progression = ProgressionManager.Instance;

                int fromLevelDisplay = 1;
                int toLevelDisplay = 1;
                float finalGiftFill = 0f;
                bool giftIsFullNow = false;

                if (progression != null)
                {
                    // Before calling OnLevelCompleted, we are still on the "old" level.
                    fromLevelDisplay = progression.GetDisplayLevelNumber();

                    // Advance progression (level index + gift bar)
                    giftIsFullNow = progression.OnLevelCompleted();

                    toLevelDisplay = progression.GetDisplayLevelNumber();
                    finalGiftFill = progression.GiftFill;

                    nextLevelToSpawn = progression.CurrentGlobalLevelIndex;
                }

                if (nextLevelOrchestrator != null)
                {
                    await nextLevelOrchestrator.OrchestrateNextLevelAsync(
                        fromLevelDisplay,
                        toLevelDisplay,
                        finalGiftFill,
                        giftIsFullNow,
                        onClaimGift: async () =>
                        {
                            // Put your async gift claim logic here
                            // Example: give coins, powerup, etc.
                            if (progression != null && giftIsFullNow)
                            {
                                // e.g. reward coins for full gift
                                UpgradeSystem.Instance.AddCoins(100);


                            }

                            await UniTask.Yield();
                        }
                    );
                }
                await levelManager.SpawnLevelByGlobalIndex(nextLevelToSpawn);
            }
            else
            {
                // Level failed or not fully completed, no progression advance.
                ProgressionManager progression = ProgressionManager.Instance;
                if (progression != null)
                {
                    nextLevelToSpawn = progression.CurrentGlobalLevelIndex;
                }
            }


            await StartGame();
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
