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

    [Header("Misc")]
    public bool destroyWorldCoinsOnConvert = true;
    public bool setUpdateUnscaled = true;    // tweens run with unscaled time

    // Cancellation of an in-flight orchestrated sequence
    private CancellationTokenSource _cts;

    private void Awake()
    {
        if (worldCamera == null) worldCamera = Camera.main;
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
    public async UniTask OrchestrateEnd(int runScoreDelta, int startScoreValue)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        // Await the task directly — no .Forget()
        await OrchestrateEndAsync(runScoreDelta, startScoreValue, _cts.Token);
    }


    private async UniTask OrchestrateEndAsync(int runScoreDelta, int startScoreValue, CancellationToken token)
    {
        if (player == null || worldCamera == null || uiCanvas == null || scoreTarget == null)
        {
            Debug.LogWarning("[EndGameOrchestrator] Missing references.");
            return;
        }

        // --- 1) Spawn world coins in a burst around the player ---
        var worldCoins = SpawnWorldCoins(player.position, coinCount);
        try
        {
            await BurstCoinsOutAsync(worldCoins, scatterExplodeSeconds, token);
        }
        catch (OperationCanceledException) { CleanupWorld(worldCoins); throw; }

        // --- 2) Start score counting (runs in parallel with coin flights) ---


        // --- 3) Convert each coin to a UI clone and fly to the scoreTarget sequentially ---
        float perCoinDelay = (coinCount <= 0) ? 0f : Mathf.Max(0f, totalCollectSeconds) / coinCount;
        for (int i = 0; i < worldCoins.Count; i++)
        {
            token.ThrowIfCancellationRequested();

            var wcoin = worldCoins[i];

            // Capture the coin's current screen position
            Vector2 startScreen = worldCamera.WorldToScreenPoint(wcoin.transform.position);

            // Optionally nudge the coin for a tiny "lift" before converting to UI
            var nudgeDuration = 0.08f;
            wcoin.transform.DOMove(wcoin.transform.position + Vector3.up * 0.15f, nudgeDuration)
                .SetEase(Ease.OutSine)
                .SetUpdate(setUpdateUnscaled);

            // Convert: spawn a UI coin at that screen pos
            var uiCoin = Instantiate(uiCoinPrefab, uiCanvas.transform, worldPositionStays: false);
            uiCoin.gameObject.SetActive(true);
            uiCoin.SetAsLastSibling();

            // Place UI coin at proper anchored position
            uiCoin.anchoredPosition = ScreenToCanvasAnchored(uiCanvas, startScreen);

            // Clean up world coin (optional) to avoid seeing both
            if (destroyWorldCoinsOnConvert && wcoin != null)
                Destroy(wcoin);

            // Travel time per coin with small variation
            float tTravel = Mathf.Lerp(coinTravelSecondsMin, coinTravelSecondsMax, UnityEngine.Random.value);
            // guarantee total pacing by inserting a delay before starting the next coin
            // (We start this coin immediately, and delay the loop for perCoinDelay below)
            var arc = UnityEngine.Random.Range(coinArcScreenPixelsMin, coinArcScreenPixelsMax);
            var coinTravelTask = FlyUICoinToTargetAsync(uiCoin, scoreTarget, tTravel, coinTravelEase, arc, setUpdateUnscaled, token);

            // Optional per-coin delay to string them out over totalCollectSeconds
            // NOTE: Delay does not block this coin's travel—just the start of the NEXT coin.
            //await UniTask.Delay(TimeSpan.FromSeconds(perCoinDelay), DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, token);

            // Fire-and-forget arrival punch + cleanup
            coinTravelTask.ContinueWith(() =>
            {
                if (punchOnArrive && uiCoin != null)
                {
                    // Tiny punch on the target (not the icon itself, but the coin clone then dispose)
                    uiCoin.DOKill();
                    uiCoin.DOScale(Vector3.one * 1.2f, 0.08f).SetUpdate(setUpdateUnscaled).OnComplete(() =>
                    {
                        if (uiCoin) Destroy(uiCoin.gameObject);
                    });
                }
                else
                {
                    if (uiCoin) Destroy(uiCoin.gameObject);
                }
            }).Forget();
        }

        if (scoreLabel != null)
        {

            if (scorePresenter)
            {
                await scorePresenter.ShowFinalScore(startScoreValue);
            }
            else
            {

                scoreLabel.text = $"{scorePrefix}{startScoreValue}";


                // Run score animation in the background
                AnimationHelper.AnimateScoreAsync(
                    scoreLabel,
                    startScoreValue,
                    startScoreValue + runScoreDelta,
                    scoreCountSeconds,
                    scorePrefix,
                    token
                ).Forget();
            }
            // Set starting text
        }
        // Final hedge: wait a tiny bit after the last coin delay so late tweens can complete nicely
        await UniTask.Delay(TimeSpan.FromSeconds(0.1f), DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, token);

        // Cleanup any remaining world coins if not destroyed earlier
        CleanupWorld(worldCoins);
    }

    // ---------- Helpers ----------

    private List<GameObject> SpawnWorldCoins(Vector3 center, int count)
    {
        var list = new List<GameObject>(count);
        for (int i = 0; i < count; i++)
        {
            if (worldCoinPrefab == null) break;

            var angle = UnityEngine.Random.value * Mathf.PI * 2f;
            var r = UnityEngine.Random.Range(0.2f, scatterRadius);
            var offset = new Vector3(Mathf.Cos(angle) * r, UnityEngine.Random.Range(0.0f, 0.3f), Mathf.Sin(angle) * r);
            var go = Instantiate(worldCoinPrefab, center + offset * 0.15f, Quaternion.identity);
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

        // END: use the target rect's center in canvas space (not scene/world camera!)
        Vector3 targetWorldCenter = target.TransformPoint(target.rect.center);
        Vector2 endScreen = RectTransformUtility.WorldToScreenPoint(canvasCam, targetWorldCenter);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, endScreen, canvasCam, out var endLocal);

        // Optional: clamp end to canvas rect (prevents flying off if anchors/pivots are odd)
        // var half = canvasRect.rect.size * 0.5f;
        // endLocal.x = Mathf.Clamp(endLocal.x, -half.x, half.x);
        // endLocal.y = Mathf.Clamp(endLocal.y, -half.y, half.y);

        // Bezier setup in CANVAS space (anchoredPosition)
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

        // Ensure the tween is killed if the provided cancellation token is triggered,
        // then await the tween's completion task (DOTween returns a System.Threading.Tasks.Task).
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


    private static void CleanupWorld(List<GameObject> worldCoins)
    {
        if (worldCoins == null) return;
        foreach (var c in worldCoins)
            if (c) Destroy(c);
        worldCoins.Clear();
    }

    private static Vector2 ScreenToCanvasAnchored(Canvas canvas, Vector2 screenPos)
    {
        if (canvas == null) return screenPos;

        var canvasRect = canvas.transform as RectTransform;
        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            // Overlay: screen coords to local anchored
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, screenPos, null, out var local);
            return local;
        }
        else
        {
            // ScreenSpace-Camera or WorldSpace
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, screenPos, canvas.worldCamera, out var local);
            return local;
        }
    }
}
