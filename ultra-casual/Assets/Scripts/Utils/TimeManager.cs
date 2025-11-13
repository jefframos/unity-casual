using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading;

public class TimeManager : MonoBehaviour
{
    public static TimeManager Instance { get; private set; }

    [Header("Time Settings")]
    [Range(0f, 2f)]
    public float editorTimeScale = 1f; // Settable in inspector

    private float originalTimeScale;
    private float originalFixedDeltaTime;

    private CancellationTokenSource fadeCTS;

    private void Awake()
    {
        // Singleton setup
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        originalTimeScale = Time.timeScale;
        originalFixedDeltaTime = Time.fixedDeltaTime;

        ApplyTimeScale(editorTimeScale);
    }

    private void OnValidate()
    {
        // React to inspector changes while play mode is active
        if (Application.isPlaying && Instance == this)
        {
            SetTimeScale(editorTimeScale);
        }
    }

    // ----------------------------------------------------------
    //  Public API
    // ----------------------------------------------------------

    public void SetTimeScale(float newScale)
    {
        CancelFade();
        editorTimeScale = newScale;
        ApplyTimeScale(newScale);
    }

    public void ResetTimeScale()
    {
        SetTimeScale(originalTimeScale);
    }

    public void FadeTimeScale(float targetScale, float duration)
    {
        CancelFade();
        fadeCTS = new CancellationTokenSource();
        FadeTimeScaleAsync(targetScale, duration, fadeCTS.Token).Forget();
    }

    public void ResetTimeScaleWithFade(float duration)
    {
        FadeTimeScale(originalTimeScale, duration);
    }

    // ----------------------------------------------------------
    //  Internal
    // ----------------------------------------------------------

    private void CancelFade()
    {
        if (fadeCTS != null)
        {
            fadeCTS.Cancel();
            fadeCTS.Dispose();
            fadeCTS = null;
        }
    }

    private void ApplyTimeScale(float scale)
    {
        scale = Mathf.Max(scale, 0f);

        Time.timeScale = scale;
        Time.fixedDeltaTime = originalFixedDeltaTime * scale;

        editorTimeScale = scale; // keep inspector synced
    }

    private async UniTask FadeTimeScaleAsync(float target, float duration, CancellationToken ct)
    {
        if (duration <= 0f)
        {
            ApplyTimeScale(target);
            return;
        }

        float start = Time.timeScale;
        float time = 0f;

        while (time < duration)
        {
            ct.ThrowIfCancellationRequested();

            time += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(time / duration);
            float newScale = Mathf.Lerp(start, target, t);

            ApplyTimeScale(newScale);

            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }

        ApplyTimeScale(target);
    }
}
