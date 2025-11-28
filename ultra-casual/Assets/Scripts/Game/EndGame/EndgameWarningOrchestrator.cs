using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class EndgameWarningOrchestrator : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("CanvasGroup controlling the whole warning UI (for fade in/out).")]
    public CanvasGroup canvasGroup;

    [Tooltip("TextMeshPro label where the warning message will be shown.")]
    public TMP_Text messageLabel;

    [Tooltip("Background image that will be color-cycled using the gradient (siren effect).")]
    public Image backgroundImage;

    [Header("Siren Gradient")]
    [Tooltip("Gradient used to drive the background color like a siren.")]
    public Gradient sirenGradient;

    [Tooltip("How fast to scroll along the gradient.")]
    public float gradientScrollSpeed = 2f;

    [Header("Timings (seconds)")]
    [Tooltip("Fade-in duration for the warning UI.")]
    public float fadeInDuration = 0.2f;

    [Tooltip("Fade-out duration for the warning UI.")]
    public float fadeOutDuration = 0.2f;

    private bool _initialized;

    private void Awake()
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        if (messageLabel != null)
        {
            messageLabel.text = string.Empty;
        }

        if (backgroundImage != null && sirenGradient != null)
        {
            backgroundImage.color = sirenGradient.Evaluate(0f);
        }

        gameObject.SetActive(false);
    }

    private void InitIfNeeded()
    {
        if (_initialized)
        {
            return;
        }

        // Nothing heavy for now, but kept for future expansion.
        _initialized = true;
    }

    /// <summary>
    /// Plays the warning banner:
    /// - Shows the UI
    /// - Fade in
    /// - Animate background color along gradient like a siren for "duration"
    /// - Fade out
    /// - Hides itself
    /// </summary>
    public async UniTask PlayWarningAsync(
        string message,
        float duration,
        CancellationToken token
    )
    {
        InitIfNeeded();

        if (duration <= 0f)
        {
            // No time? Just skip.
            return;
        }

        gameObject.SetActive(true);

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;
        }

        if (messageLabel != null)
        {
            messageLabel.text = message;
        }

        // ---------------------------
        // Fade in
        // ---------------------------
        if (canvasGroup != null && fadeInDuration > 0f)
        {
            float t = 0f;

            while (t < fadeInDuration && !token.IsCancellationRequested)
            {
                t += Time.unscaledDeltaTime;
                float a = Mathf.Clamp01(t / fadeInDuration);
                canvasGroup.alpha = a;

                UpdateSirenColor(t); // already animating color while fading in
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }

            canvasGroup.alpha = 1f;
        }
        else if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        // ---------------------------
        // Siren loop for "duration"
        // ---------------------------
        float elapsed = 0f;

        while (elapsed < duration && !token.IsCancellationRequested)
        {
            elapsed += Time.unscaledDeltaTime;
            UpdateSirenColor(elapsed);
            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        // ---------------------------
        // Fade out
        // ---------------------------
        if (canvasGroup != null && fadeOutDuration > 0f)
        {
            float t = 0f;

            while (t < fadeOutDuration && !token.IsCancellationRequested)
            {
                t += Time.unscaledDeltaTime;
                float a = Mathf.Clamp01(1f - (t / fadeOutDuration));
                canvasGroup.alpha = a;

                // You can keep siren moving during fade-out if desired:
                UpdateSirenColor(duration + t);

                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }

            canvasGroup.alpha = 0f;
        }

        if (canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        gameObject.SetActive(false);
    }

    /// <summary>
    /// Convenience overload without a cancellation token.
    /// </summary>
    public UniTask PlayWarningAsync(string message, float duration)
    {
        return PlayWarningAsync(message, duration, CancellationToken.None);
    }

    /// <summary>
    /// Evaluates the gradient over time to create a siren effect.
    /// </summary>
    private void UpdateSirenColor(float timeSeconds)
    {
        if (backgroundImage == null || sirenGradient == null)
        {
            return;
        }

        // Ping-pong 0..1 over time and sample the gradient.
        float t = Mathf.PingPong(timeSeconds * gradientScrollSpeed, 1f);
        Color c = sirenGradient.Evaluate(t);
        backgroundImage.color = c;
    }
}
