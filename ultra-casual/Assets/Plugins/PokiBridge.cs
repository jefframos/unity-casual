// PokiBridge.cs
// Place anywhere in Assets. No scene setup needed.
// Requires PokiUnitySDK.cs + WebGL template per Poki docs.

using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public static class PokiBridge
{
    // ---- Public events (subscribe anywhere) ----
    public static event Action OnInitialized;
    public static event Action OnGameplayStart;
    public static event Action OnGameplayStop;
    public static event Action OnAdStarted;                  // commercial or rewarded begins
    public static event Action OnAdEnded;                    // commercial finished
    public static event Action<bool> OnRewardedEnded;        // rewarded finished: withReward
    public static event Action<string> OnShareableUrlReady;  // share URL resolved
    public static event Action OnShareableUrlRejected;



    // ---- Initialize ASAP ----
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (_host != null) return;

        var go = new GameObject("[PokiBridgeHost]");
        UnityEngine.Object.DontDestroyOnLoad(go);
        _host = go.AddComponent<PokiBridgeHost>();

        // Fire up Poki as early as we can.
        _host.SafeInitPoki();
    }

    // ---- Public wrappers ----

    /// <summary>Call when your loading has fully finished (assets ready, first frame about to play).</summary>
    public static void GameLoadingFinished()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        try { PokiUnitySDK.Instance.gameLoadingFinished(); } catch { }
#endif
    }

    /// <summary>Wraps Poki gameplayStart().</summary>
    public static void GameplayStart()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        try { PokiUnitySDK.Instance.gameplayStart(); } catch { }
#endif
        OnGameplayStart?.Invoke();
    }

    /// <summary>Wraps Poki gameplayStop().</summary>
    public static void GameplayStop()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        try { PokiUnitySDK.Instance.gameplayStop(); } catch { }
#endif
        OnGameplayStop?.Invoke();
    }

    /// <summary>Shows an interstitial/commercial break and completes when it ends.</summary>
    public static Task CommercialBreakAsync(CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<bool>();

        void Complete()
        {
            if (tcs.TrySetResult(true))
            {
                OnAdEnded?.Invoke();
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        try
        {
            OnAdStarted?.Invoke();
            PokiUnitySDK.Instance.commercialBreakCallBack = Complete;
            PokiUnitySDK.Instance.commercialBreak();
        }
        catch (Exception e)
        {
            tcs.TrySetException(e);
        }
#else
        // Editor fallback: simulate instantly.
        OnAdStarted?.Invoke();
        Complete();
#endif

        if (ct.CanBeCanceled)
        {
            ct.Register(() => tcs.TrySetCanceled());
        }

        return tcs.Task;
    }

    /// <summary>Shows a rewarded ad. Resolves true if reward should be granted.</summary>
    public static Task<bool> RewardedBreakAsync(CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<bool>();

        void Complete(bool withReward)
        {
            OnRewardedEnded?.Invoke(withReward);
            OnAdEnded?.Invoke();
            tcs.TrySetResult(withReward);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        try
        {
            OnAdStarted?.Invoke();
            PokiUnitySDK.Instance.rewardedBreakCallBack = Complete;
            PokiUnitySDK.Instance.rewardedBreak();
        }
        catch (Exception e)
        {
            tcs.TrySetException(e);
        }
#else
        // Editor fallback: pretend user accepted reward.
        OnAdStarted?.Invoke();
        Complete(true);
#endif

        if (ct.CanBeCanceled)
        {
            ct.Register(() => tcs.TrySetCanceled());
        }

        return tcs.Task;
    }

    /// <summary>Gets a URL param (after you generated a shareable URL).</summary>
    public static string GetUrlParam(string key)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        try { return PokiUnitySDK.Instance.getURLParam(key); } catch { return string.Empty; }
#else
        return string.Empty;
#endif
    }

    /// <summary>Asks Poki to generate a shareable URL. Completes with the URL string.</summary>
    public static Task<string> CreateShareableUrlAsync(ScriptableObject optionalPayload = null, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<string>();

#if UNITY_WEBGL && !UNITY_EDITOR
        try
        {
            PokiUnitySDK.Instance.shareableURLResolvedCallback = url =>
            {
                OnShareableUrlReady?.Invoke(url);
                tcs.TrySetResult(url);
            };
            PokiUnitySDK.Instance.shareableURLRejectedCallback = () =>
            {
                OnShareableUrlRejected?.Invoke();
                tcs.TrySetException(new Exception("Shareable URL rejected"));
            };

            // The SDK page shows using a ScriptableObject class for params.
            // If you have one, assign it here; otherwise this call may be parameterless in your template.
            _host.TriggerShareableURL(optionalPayload);
        }
        catch (Exception e)
        {
            tcs.TrySetException(e);
        }
#else
        // Editor fallback: return a dummy URL.
        var dummy = "https://example.com/?poki=dev";
        OnShareableUrlReady?.Invoke(dummy);
        tcs.TrySetResult(dummy);
#endif

        if (ct.CanBeCanceled)
        {
            ct.Register(() => tcs.TrySetCanceled());
        }

        return tcs.Task;
    }

    // ---- Private bits ----
    private static PokiBridgeHost _host;
#if !UNITY_WEBGL || UNITY_EDITOR
    private static bool _editorInitialized;
#endif

    // Host MonoBehaviour to call into the SDK safely on main thread.
    private sealed class PokiBridgeHost : MonoBehaviour
    {
        // Keep a reference to the payload if your template expects it on a known object.
        private ScriptableObject _sharePayload;

        internal void SafeInitPoki()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                // Initialize Poki as early as possible per docs.
                // https://sdk.poki.com/unity.html
                PokiUnitySDK.Instance.init();

                // Notify
                OnInitialized?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Poki init failed (continuing): {e.Message}");
            }
#else
            // Editor / non-WebGL: mark as initialized to keep game flow happy.
            _editorInitialized = true;
            OnInitialized?.Invoke();
#endif
        }

        internal void TriggerShareableURL(ScriptableObject payload)
        {
            _sharePayload = payload;
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                // In the Poki docs, shareable URL generation is triggered by a method you implement
                // that then calls into PokiUnitySDK (example shows UI hookups).
                // If your PokiUnitySDK exposes a direct trigger, call it here.
                // For generality, we invoke via SendMessage so your sample scene’s method can handle it:
                //   public void triggerShareableURL() { PokiUnitySDK.Instance.triggerShareableURL(...); }
                // If you've wrapped this in your own method, replace below accordingly.
                gameObject.SendMessage("triggerShareableURL", SendMessageOptions.DontRequireReceiver);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Shareable URL trigger failed: {e.Message}");
                OnShareableUrlRejected?.Invoke();
            }
#else
            // No-op in Editor; CreateShareableUrlAsync returns dummy.
#endif
        }
    }
}
