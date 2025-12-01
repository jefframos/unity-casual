using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class UiCountdownPanel : MonoBehaviour
{
    [Header("References")]
    [Tooltip("CanvasGroup used for fading the panel in/out.")]
    public CanvasGroup canvasGroup;

    [Tooltip("Label that shows the countdown value / GO.")]
    public TextMeshProUGUI label;

    [Tooltip("Target transform for the pop scale effect (usually the label or a child container).")]
    public Transform popTarget;

    [Header("Timings")]
    [Tooltip("Fade-in duration for the panel.")]
    public float fadeInDuration = 0.25f;

    [Tooltip("Fade-out duration for the panel.")]
    public float fadeOutDuration = 0.25f;

    [Tooltip("Time each number / GO text stays on screen (seconds).")]
    public float stepDuration = 1.0f;

    [Header("Pop Effect")]
    [Tooltip("Scale multiplier for the pop effect.")]
    public float popScale = 1.2f;

    [Tooltip("Total duration of the pop (scale up + scale down).")]
    public float popDuration = 0.25f;

    [Tooltip("Ease used when scaling up.")]
    public Ease popUpEase = Ease.OutBack;

    [Tooltip("Ease used when scaling back down.")]
    public Ease popDownEase = Ease.InOutSine;

    public bool IsRunning { get; private set; }

    private Tween _fadeTween;
    private Tween _popTween;

    private void Reset()
    {
        canvasGroup = GetComponent<CanvasGroup>();

        if (label == null)
        {
            label = GetComponentInChildren<TextMeshProUGUI>();
        }

        if (popTarget == null && label != null)
        {
            popTarget = label.transform;
        }
    }

    private void Awake()
    {
        if (popTarget == null && label != null)
        {
            popTarget = label.transform;
        }
    }

    private void OnDisable()
    {
        KillTweens();
        IsRunning = false;
    }

    private void KillTweens()
    {
        if (_fadeTween != null && _fadeTween.IsActive())
        {
            _fadeTween.Kill();
        }

        if (_popTween != null && _popTween.IsActive())
        {
            _popTween.Kill();
        }
    }

    /// <summary>
    /// Plays a countdown from startValue down to 1, then shows "GO!" and hides the panel.
    /// Example: await countdownPanel.PlayCountdownAsync(3, this.GetCancellationTokenOnDestroy());
    /// </summary>
    public async UniTask PlayCountdownAsync(
        int startValue,
        CancellationToken cancellationToken = default
    )
    {
        if (startValue < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startValue), "startValue must be >= 0.");
        }

        if (IsRunning)
        {
            // Already running; you can choose to cancel, wait, or just bail out.
            return;
        }

        IsRunning = true;

        gameObject.SetActive(true);

        // Reset visual state.
        KillTweens();
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
        }

        if (popTarget != null)
        {
            popTarget.localScale = Vector3.one;
        }

        try
        {
            // Fade in panel.
            if (canvasGroup != null)
            {
                _fadeTween = canvasGroup
                    .DOFade(1f, fadeInDuration)
                    .SetUpdate(true); // ignore timescale if you want

                await _fadeTween
                    .AsyncWaitForCompletion()
                    .AsUniTask()
                    .AttachExternalCancellation(cancellationToken);
            }

            // Count numbers down.
            for (int value = startValue; value > 0; value--)
            {
                SetLabelText(value.ToString());
                await PlayPopAsync(cancellationToken);

                await UniTask.Delay(
                    TimeSpan.FromSeconds(stepDuration),
                    cancellationToken: cancellationToken
                );
            }

            // Show "GO!"
            SetLabelText("GO!");
            await PlayPopAsync(cancellationToken);

            await UniTask.Delay(
                TimeSpan.FromSeconds(stepDuration),
                cancellationToken: cancellationToken
            );

            // Fade out panel.
            if (canvasGroup != null)
            {
                _fadeTween = canvasGroup
                    .DOFade(0f, fadeOutDuration)
                    .SetUpdate(true);

                await _fadeTween
                    .AsyncWaitForCompletion()
                    .AsUniTask()
                    .AttachExternalCancellation(cancellationToken);
            }

            gameObject.SetActive(false);
        }
        catch (OperationCanceledException)
        {
            // Swallow cancellation; just ensure we clean up below.
        }
        finally
        {
            KillTweens();
            IsRunning = false;
        }
    }

    private void SetLabelText(string text)
    {
        if (label != null)
        {
            label.text = text;
        }
    }

    private async UniTask PlayPopAsync(CancellationToken cancellationToken)
    {
        if (popTarget == null)
        {
            return;
        }

        // Kill previous pop if still going.
        if (_popTween != null && _popTween.IsActive())
        {
            _popTween.Kill();
        }

        popTarget.localScale = Vector3.one;

        float halfDuration = popDuration * 0.5f;
        if (halfDuration <= 0f)
        {
            return;
        }

        // Scale up.
        _popTween = popTarget
            .DOScale(popScale, halfDuration)
            .SetEase(popUpEase)
            .SetUpdate(true);

        await _popTween
            .AsyncWaitForCompletion()
            .AsUniTask()
            .AttachExternalCancellation(cancellationToken);

        // Scale back down.
        _popTween = popTarget
            .DOScale(1f, halfDuration)
            .SetEase(popDownEase)
            .SetUpdate(true);

        await _popTween
            .AsyncWaitForCompletion()
            .AsUniTask()
            .AttachExternalCancellation(cancellationToken);
    }
}
