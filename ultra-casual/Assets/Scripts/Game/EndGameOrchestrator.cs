using System;
using System.Threading;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class EndGameOrchestrator : MonoBehaviour
{
    [Header("Scene Refs")]
    public Transform player;
    public Camera worldCamera;
    public Canvas uiCanvas;                  // Screen Space Overlay or Screen Space - Camera
    public RectTransform scoreTarget;        // e.g. the score/icon slot in HUD
    public TextMeshProUGUI scoreLabel;       // the label we animate
    public FinalScorePresenter scorePresenter;     // optional reference if using another presenter

    [Header("Prefabs")]
    [Tooltip("A small world-space coin visual (SpriteRenderer / Mesh).")]
    public GameObject worldCoinPrefab;
    [Tooltip("A small UI Image under the HUD canvas, used to fly to the scoreTarget.")]
    public RectTransform uiCoinPrefab;

    [Header("Coin Burst")]
    public int coinCount = 10;
    public float scatterRadius = 1.5f;            // world units
    public float scatterExplodeSeconds = 0.35f;   // outward burst time
    public Ease scatterEase = Ease.OutQuad;

    [Header("Coin Collect")]
    [Tooltip("Total time (unscaled) for all coins to arrive at the UI (sequential).")]
    public float totalCollectSeconds = 1.4f;
    public float coinTravelSecondsMin = 0.35f;    // per-coin clamp
    public float coinTravelSecondsMax = 0.6f;
    public Ease coinTravelEase = Ease.InCubic;
    public float coinArcScreenPixelsMax = 120f;      // arc height in canvas space
    public float coinArcScreenPixelsMin = -120;      // arc height in canvas space
    public bool punchOnArrive = true;

    [Header("Score Animation")]
    public float scoreCountSeconds = 1.0f;   // AnimateScore duration
    public string scorePrefix = "Final Score: ";

    [Header("Pooling (World Coins)")]
    [Tooltip("Parent transform to keep pooled world coins organized (optional).")]
    public Transform worldCoinPoolParent;
    [Tooltip("Initial number of world coins to pre-instantiate in the pool.")]
    public int prewarmWorldCoins = 20;
    [Tooltip("When converting to UI, return the world coin to the pool instead of keeping it visible.")]
    public bool returnWorldCoinsOnConvert = true;

    [Header("Misc")]
    public bool setUpdateUnscaled = true;    // tweens run with unscaled time

    // Cancellation of an in-flight orchestrated sequence
    private CancellationTokenSource _cts;

    // --- simple stack-based pool for world coins ---
    private readonly Stack<GameObject> _worldCoinPool = new Stack<GameObject>();
    private readonly HashSet<GameObject> _activeWorldCoins = new HashSet<GameObject>();

    private void Awake()
    {
        if (worldCamera == null) worldCamera = Camera.main;
        PrewarmWorldCoinPool();
    }

    private void OnDisable()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>
    /// Call this when the player dies / run ends.
    /// </summary>
    /// <param name="runScoreDelta">How many points/coins to add/animate this end.</param>
    /// <param name="startScoreValue">Starting value to show in the label before the animation.</param>
    public async UniTask OrchestrateEnd(int runScoreDelta, int startScoreValue, bool nextIsHighscore)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();


        if (nextIsHighscore)
        {
            Debug.LogWarning("New High Score Triggered");
            if (scorePresenter)
            {
                // Start but don't await; coin flights run while the presenter handles UI.
                scorePresenter.ShowHighscore(startScoreValue);
            }
            //await OrchestrateNewHighScore(startScoreValue, _cts.Token);
        }
        await OrchestrateEndAsync(runScoreDelta, startScoreValue, _cts.Token);
    }

    private async UniTask OrchestrateEndAsync(int runScoreDelta, int finalScoreValue, CancellationToken token)
    {
        if (player == null || worldCamera == null || uiCanvas == null || scoreTarget == null)
        {
            Debug.LogWarning("[EndGameOrchestrator] Missing references.");
            return;
        }

        var multiplier = UpgradeSystem.Instance.GetValue(UpgradeType.COIN);

        finalScoreValue = Mathf.CeilToInt(finalScoreValue * multiplier);
        // --- 1) Spawn world coins in a burst around the player ---
        var worldCoins = SpawnWorldCoins(player.position, coinCount);
        try
        {
            await BurstCoinsOutAsync(worldCoins, scatterExplodeSeconds, token);
        }
        catch (OperationCanceledException)
        {
            CleanupWorld(worldCoins);
            throw;
        }

        // --- 2) Start score counting (runs in parallel with coin flights) ---
        if (scoreLabel != null)
        {
            if (scorePresenter)
            {
                // Start but don't await; coin flights run while the presenter handles UI.
                scorePresenter.ShowFinalScore(finalScoreValue).Forget();
            }
            else
            {
                scoreLabel.text = $"{scorePrefix}{finalScoreValue}";
                AnimationHelper.AnimateScoreAsync(
                    scoreLabel,
                    finalScoreValue,
                    finalScoreValue + runScoreDelta,
                    scoreCountSeconds,
                    scorePrefix,
                    token
                ).Forget();
            }
        }



        UpgradeSystem.Instance.AddCoins(finalScoreValue);


        // --- 3) Convert each coin to a UI clone and fly to the scoreTarget sequentially ---
        float perCoinDelay = (coinCount <= 0) ? 0f : Mathf.Max(0f, totalCollectSeconds) / coinCount;

        for (int i = 0; i < worldCoins.Count; i++)
        {
            token.ThrowIfCancellationRequested();

            var wcoin = worldCoins[i];
            if (wcoin == null) continue;

            // Capture the coin's current screen position
            Vector2 startScreen = worldCamera.WorldToScreenPoint(wcoin.transform.position);

            // Tiny lift nudge before converting to UI
            var nudgeDuration = 0.08f;
            wcoin.transform.DOMove(wcoin.transform.position + Vector3.up * 0.15f, nudgeDuration)
                .SetEase(Ease.OutSine)
                .SetUpdate(setUpdateUnscaled);

            // Convert: spawn a UI coin at that screen pos
            var uiCoin = Instantiate(uiCoinPrefab, uiCanvas.transform, worldPositionStays: false);
            uiCoin.gameObject.SetActive(true);
            uiCoin.SetAsLastSibling();
            uiCoin.anchoredPosition = ScreenToCanvasAnchored(uiCanvas, startScreen);

            // Return world coin to pool so you don't see both
            if (returnWorldCoinsOnConvert)
            {
                ReturnWorldCoin(wcoin);
            }

            // Travel time per coin with small variation
            float tTravel = Mathf.Lerp(coinTravelSecondsMin, coinTravelSecondsMax, UnityEngine.Random.value);
            var arc = UnityEngine.Random.Range(coinArcScreenPixelsMin, coinArcScreenPixelsMax);

            var coinTravelTask = FlyUICoinToTargetAsync(
                uiCoin,
                scoreTarget,
                tTravel,
                coinTravelEase,
                arc,
                setUpdateUnscaled,
                token
            );

            // Stagger next coin start (without blocking this coin's flight)
            if (perCoinDelay > 0f)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(perCoinDelay), DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, token);
            }

            // Cleanup UI coin on arrival (punch optional)
            coinTravelTask.ContinueWith(() =>
            {
                if (uiCoin == null) return;

                uiCoin.DOKill();
                if (punchOnArrive)
                {
                    uiCoin.DOScale(Vector3.one * 1.2f, 0.08f).SetUpdate(setUpdateUnscaled).OnComplete(() =>
                    {
                        if (uiCoin) Destroy(uiCoin.gameObject);
                    });
                }
                else
                {
                    Destroy(uiCoin.gameObject);
                }
            }).Forget();
        }


        if (scorePresenter)
        {
            // Start but don't await; coin flights run while the presenter handles UI.
            await scorePresenter.AwaitEnd(token);
        }
        // Small hedge so late tweens can complete nicely
        await UniTask.Delay(TimeSpan.FromSeconds(0.1f), DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, token);


        // Cleanup any remaining world coins if still active
        CleanupWorld(worldCoins);
    }

    // ---------- Pool (World Coins) ----------

    private void PrewarmWorldCoinPool()
    {
        if (worldCoinPrefab == null || prewarmWorldCoins <= 0) return;

        for (int i = 0; i < prewarmWorldCoins; i++)
        {
            var go = Instantiate(worldCoinPrefab, worldCoinPoolParent != null ? worldCoinPoolParent : transform);
            PreparePooledWorldCoin(go);
            go.SetActive(false);
            _worldCoinPool.Push(go);
        }
    }

    private GameObject RentWorldCoin()
    {
        GameObject go = (_worldCoinPool.Count > 0) ? _worldCoinPool.Pop() : null;
        if (go == null)
        {
            if (worldCoinPrefab == null)
            {
                Debug.LogWarning("[EndGameOrchestrator] No worldCoinPrefab assigned; cannot rent.");
                return null;
            }
            go = Instantiate(worldCoinPrefab, worldCoinPoolParent != null ? worldCoinPoolParent : transform);
            PreparePooledWorldCoin(go);
        }

        go.SetActive(true);
        _activeWorldCoins.Add(go);
        return go;
    }

    private void ReturnWorldCoin(GameObject go)
    {
        if (go == null) return;

        // Kill tweens safely
        var t = go.transform;
        t.DOKill();

        // Reset basic transform state (optional — tweak as needed)
        t.localScale = Vector3.one;
        // keep rotation/position set by caller when re-renting

        // Deactivate and parent under pool
        if (worldCoinPoolParent != null) t.SetParent(worldCoinPoolParent, worldPositionStays: false);
        go.SetActive(false);

        _activeWorldCoins.Remove(go);
        _worldCoinPool.Push(go);
    }

    private static void PreparePooledWorldCoin(GameObject go)
    {
        // Optional hook: configure layers/rigidbody/collider if needed
        // e.g., disable physics while pooled, etc.
        // For now, nothing special required.
    }

    // ---------- Existing Helpers (adapted to use pool) ----------

    private List<GameObject> SpawnWorldCoins(Vector3 center, int count)
    {
        var list = new List<GameObject>(count);
        for (int i = 0; i < count; i++)
        {
            var go = RentWorldCoin();
            if (go == null) break;

            var angle = UnityEngine.Random.value * Mathf.PI * 2f;
            var r = UnityEngine.Random.Range(0.2f, scatterRadius);
            var offset = new Vector3(
                Mathf.Cos(angle) * r,
                UnityEngine.Random.Range(0.0f, 0.3f),
                Mathf.Sin(angle) * r
            );

            var t = go.transform;
            // Place in world where it will burst from
            t.position = center + offset * 0.15f;
            t.rotation = Quaternion.identity;

            list.Add(go);
        }
        return list;
    }

    private async UniTask BurstCoinsOutAsync(List<GameObject> coins, float seconds, CancellationToken token)
    {
        if (coins == null || coins.Count == 0 || seconds <= 0f) return;

        var tasks = new List<System.Threading.Tasks.Task>(coins.Count);
        foreach (var c in coins)
        {
            if (c == null) continue;
            var t = c.transform;
            var dir = (t.position - player.position).normalized;
            if (dir.sqrMagnitude < 0.0001f) dir = UnityEngine.Random.insideUnitSphere.normalized;

            var target = t.position + dir * scatterRadius * 0.5f + Vector3.up * UnityEngine.Random.Range(0.05f, 0.25f);

            Tween tw = t.DOMove(target, seconds)
                .SetEase(scatterEase)
                .SetUpdate(setUpdateUnscaled);

            tasks.Add(tw.AsyncWaitForCompletion());
        }

        await System.Threading.Tasks.Task.WhenAll(tasks);
    }

    private async UniTask FlyUICoinToTargetAsync(
        RectTransform coin,
        RectTransform target,
        float seconds,
        Ease ease,
        float arcPixels,
        bool unscaled,
        CancellationToken token)
    {
        if (coin == null || target == null || uiCanvas == null) return;

        // DOTween safety
        coin.DOKill();

        // Canvas + camera to use for UI conversions
        var canvasRect = uiCanvas.transform as RectTransform;
        var canvasCam = uiCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : uiCanvas.worldCamera;

        // START: coin is already under uiCanvas, so anchoredPosition is correct
        Vector2 startLocal = coin.anchoredPosition;

        // END: use the target rect's center in canvas space
        Vector3 targetWorldCenter = target.TransformPoint(target.rect.center);
        Vector2 endScreen = RectTransformUtility.WorldToScreenPoint(canvasCam, targetWorldCenter);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, endScreen, canvasCam, out var endLocal);

        float tParam = 0f;
        Vector3 a = startLocal;                  // start
        Vector3 b = endLocal;                    // end
        Vector3 m = (a + b) * 0.5f + Vector3.up * arcPixels;  // mid with arc

        Tween tw = DOTween.To(() => tParam, v =>
        {
            tParam = v;
            float u = 1f - tParam; // quadratic Bezier
            Vector3 p = (u * u) * a + (2f * u * tParam) * m + (tParam * tParam) * b;
            coin.anchoredPosition = p;
        }, 1f, seconds)
        .SetEase(ease)
        .SetUpdate(unscaled);

        CancellationTokenRegistration ctr = default;
        if (token.CanBeCanceled)
            ctr = token.Register(() => { if (tw != null && tw.IsActive()) tw.Kill(); });

        try
        {
            await tw.AsyncWaitForCompletion();
        }
        finally
        {
            ctr.Dispose();
        }
    }

    private void CleanupWorld(List<GameObject> worldCoins)
    {
        if (worldCoins == null) return;
        foreach (var c in worldCoins)
        {
            if (c != null && _activeWorldCoins.Contains(c))
            {
                ReturnWorldCoin(c);
            }
        }
        worldCoins.Clear();
    }

    private static Vector2 ScreenToCanvasAnchored(Canvas canvas, Vector2 screenPos)
    {
        if (canvas == null) return screenPos;

        var canvasRect = canvas.transform as RectTransform;
        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, screenPos, null, out var local);
            return local;
        }
        else
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, screenPos, canvas.worldCamera, out var local);
            return local;
        }
    }
}
