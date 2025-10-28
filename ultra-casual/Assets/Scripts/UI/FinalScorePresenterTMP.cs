using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class FinalScorePresenterTMP : FinalScorePresenter
{
    [Header("UI")]
    public CanvasGroup canvasGroup;
    public TextMeshProUGUI scoreLabel;

    [Header("Flow")]
    [Tooltip("Minimum time in seconds the score stays visible (unscaled).")]
    public float minShowSeconds = 1.5f;

    [Tooltip("If true, wait for player input to continue.")]
    public bool waitForAnyKey = true;

    [Tooltip("Optional override key. If None, any key will continue.")]
    public KeyCode continueKey = KeyCode.None;

    [Header("FX")]
    public float fadeInSeconds = 0.25f;
    public float fadeOutSeconds = 0.25f;
    public float scoreCountSeconds = 1.0f;

    private CancellationTokenSource _cts;

    private void Awake()
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }
    public override async UniTask ShowFinalScore(float score)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        // Await the task directly — no .Forget()
        await ShowFinalScoreAsync(score, _cts.Token);
    }
    public override async UniTask AwaitEnd(CancellationToken token)
    {
        if (waitForAnyKey)
        {
            await WaitForKeyAsync(continueKey, token);
        }

        // --- Fade Out ---
        if (canvasGroup != null)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            await AnimationHelper.FadeCanvasAsync(canvasGroup, 1f, 0f, fadeOutSeconds, token);
        }

    }

    public override async UniTask ShowFinalScoreAsync(float score, CancellationToken token)
    {
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        // --- Fade In ---
        if (canvasGroup != null)
        {
            await AnimationHelper.FadeCanvasAsync(canvasGroup, 0f, 1f, fadeInSeconds, token);
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        // --- Animate Score ---
        if (scoreLabel != null)
        {
            scoreLabel.text = "0";
            _ = AnimationHelper.ScaleTransformAsync(scoreLabel.transform, Vector3.one, Vector3.one * 1.2f, scoreCountSeconds);
            await AnimationHelper.AnimateScoreAsync(scoreLabel, 0f, score, scoreCountSeconds, "", token);
        }

        // --- Hold Minimum Display Time ---
        var until = Time.unscaledTime + Mathf.Max(0f, minShowSeconds);
        while (Time.unscaledTime < until)
            await UniTask.Yield(token);

        // --- Optional Wait for Input ---



        // Optionally disable UI
        // gameObject.SetActive(false);
    }

    private static async UniTask WaitForKeyAsync(KeyCode key, CancellationToken token)
    {
        if (key == KeyCode.None)
        {
            while (!Input.anyKeyDown)
                await UniTask.Yield(token);
        }
        else
        {
            while (!Input.GetKeyDown(key))
                await UniTask.Yield(token);
        }
    }

}
