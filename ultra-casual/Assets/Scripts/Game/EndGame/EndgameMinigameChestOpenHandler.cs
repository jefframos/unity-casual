using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class EndgameMinigameChestOpenHandler : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Root GameObject that contains the chest visuals and (optionally) the label / claim button.")]
    public GameObject chestRoot;

    [Tooltip("RectTransform of the chest image.")]
    public RectTransform chestRect;
    public RectTransform prizeRect;

    [Tooltip("Label that shows the prize after the chest pops.")]
    public TMP_Text prizeText;

    [Tooltip("Optional coin rain spawner that will play behind the label.")]
    public EndgameCoinRainSpawner coinRainSpawner;

    [Header("Claim UI")]
    [Tooltip("Button that the player presses to claim the reward.")]
    public Button claimButton;

    [Tooltip("Optional container for claim UI; if null, only claimButton is toggled.")]
    public GameObject claimUiRoot;

    [Header("Chest Motion")]
    [Tooltip("Anchored position where the chest starts (optional, will snapshot at first run if left zero).")]
    public Vector2 startAnchoredPosition;

    [Tooltip("Anchored position in the middle of the screen to tween the chest to.")]
    public Vector2 centerAnchoredPosition = Vector2.zero;

    [Tooltip("Duration of the move from start to center (seconds).")]
    public float moveToCenterDuration = 0.4f;

    [Header("Chest Pop")]
    [Tooltip("Base scale of the chest before pop. If zero, will snapshot from current localScale.")]
    public Vector3 baseScale = Vector3.zero;

    [Tooltip("Peak pop scale before disappearing.")]
    public Vector3 popScale = new Vector3(1.2f, 1.2f, 1.2f);

    [Tooltip("Total duration of the pop animation (scale up then down to zero).")]
    public float popDuration = 0.35f;

    [Header("Prize Label / Coins")]
    [Tooltip("Duration for the prize count-up animation.")]
    public float rewardCountDuration = 0.7f;

    [Tooltip("Extra time (seconds) after the label reaches the final value.")]
    public float postLabelDelay = 0.6f;

    private bool _initialized;
    private Vector2 _cachedStartPos;
    private Vector3 _cachedBaseScale;
    void Awake()
    {
        prizeRect.gameObject.SetActive(false);
    }
    private void InitIfNeeded()
    {
        if (_initialized)
        {
            return;
        }

        if (chestRect != null)
        {
            _cachedStartPos =
                startAnchoredPosition == Vector2.zero
                    ? chestRect.anchoredPosition
                    : startAnchoredPosition;

            _cachedBaseScale =
                baseScale == Vector3.zero
                    ? chestRect.localScale
                    : baseScale;
        }
        else
        {
            _cachedStartPos = Vector2.zero;
            _cachedBaseScale = Vector3.one;
        }

        _initialized = true;
    }

    /// <summary>
    /// Runs the full chest sequence:
    /// - Ensure chest + claim UI are visible
    /// - Wait for claim click
    /// - Hide claim UI
    /// - Move chest to center
    /// - Pop chest and visually remove it
    /// - Show prize label and count 0..totalPrize
    /// - Play coin rain in parallel (optional)
    /// - Wait a small delay and finish
    /// </summary>
    public async UniTask PlayChestSequenceAsync(int totalPrize, CancellationToken token)
    {
        InitIfNeeded();

        if (totalPrize < 0)
        {
            totalPrize = 0;
        }

        // Fallback if no chestRect: just show prize
        if (chestRect == null)
        {
            if (chestRoot != null)
            {
                chestRoot.SetActive(true);
            }

            if (claimUiRoot != null)
            {
                claimUiRoot.SetActive(false);
            }

            if (claimButton != null)
            {
                claimButton.gameObject.SetActive(false);
            }

            if (prizeText != null)
            {
                prizeText.gameObject.SetActive(true);
                prizeText.text = totalPrize.ToString();
            }

            // if (coinRainSpawner != null)
            // {
            //     await coinRainSpawner.PlayCoinRainAsync(token);
            // }

            if (postLabelDelay > 0f)
            {
                await UniTask.Delay(
                    TimeSpan.FromSeconds(postLabelDelay),
                    DelayType.UnscaledDeltaTime,
                    PlayerLoopTiming.Update,
                    token
                );
            }

            return;
        }

        // ---------------------------
        // Initial state: chest visible, claim visible, prize hidden
        // ---------------------------
        if (chestRoot != null)
        {
            chestRoot.SetActive(true);
        }

        chestRect.gameObject.SetActive(true);
        chestRect.anchoredPosition = _cachedStartPos;
        chestRect.localScale = _cachedBaseScale;

        if (prizeText != null)
        {
            prizeText.gameObject.SetActive(false);
        }

        if (claimUiRoot != null)
        {
            claimUiRoot.SetActive(true);
        }

        if (claimButton != null)
        {
            claimButton.gameObject.SetActive(true);
        }

        // ---------------------------
        // Wait for claim click
        // ---------------------------
        if (claimButton != null)
        {
            bool clicked = false;

            void OnClicked()
            {
                clicked = true;
            }

            claimButton.onClick.AddListener(OnClicked);

            try
            {
                while (!clicked && !token.IsCancellationRequested)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                }
            }
            finally
            {
                claimButton.onClick.RemoveListener(OnClicked);
            }

            if (token.IsCancellationRequested)
            {
                return;
            }
        }

        // Hide claim UI; from here, we play chest animation and reveal prize
        if (claimUiRoot != null)
        {
            claimUiRoot.SetActive(false);
        }

        if (claimButton != null)
        {
            claimButton.gameObject.SetActive(false);
        }
        prizeRect.gameObject.SetActive(false);
        // ---------------------------
        // Move to center
        // ---------------------------
        if (moveToCenterDuration > 0f)
        {
            Vector2 start = chestRect.anchoredPosition;
            Vector2 end = centerAnchoredPosition;

            float t = 0f;

            while (t < moveToCenterDuration && !token.IsCancellationRequested)
            {
                t += Time.unscaledDeltaTime;
                float lerpT = Mathf.Clamp01(t / moveToCenterDuration);
                chestRect.anchoredPosition = Vector2.Lerp(start, end, lerpT);

                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }

            chestRect.anchoredPosition = end;
        }
        else
        {
            chestRect.anchoredPosition = centerAnchoredPosition;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        // ---------------------------
        // Pop animation (scale up then down to zero)
        // ---------------------------
        if (popDuration > 0f)
        {
            float t = 0f;

            while (t < popDuration && !token.IsCancellationRequested)
            {
                t += Time.unscaledDeltaTime;
                float normalized = Mathf.Clamp01(t / popDuration);

                Vector3 targetScale;

                if (normalized <= 0.5f)
                {
                    float halfT = normalized / 0.5f;
                    targetScale = Vector3.Lerp(_cachedBaseScale, popScale, halfT);
                }
                else
                {
                    float halfT = (normalized - 0.5f) / 0.5f;
                    targetScale = Vector3.Lerp(popScale, Vector3.zero, halfT);
                }

                chestRect.localScale = targetScale;

                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }

            chestRect.localScale = Vector3.zero;
        }
        else
        {
            chestRect.localScale = Vector3.zero;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        // Visually remove chest image, but keep root active so label can show
        // If your chest image uses a Graphic, you can also disable it here:
        // var graphic = chestRect.GetComponent<Graphic>();
        // if (graphic != null) graphic.enabled = false;

        // ---------------------------
        // Show prize label and coin rain
        // ---------------------------
        if (prizeText != null)
        {
            prizeRect.gameObject.SetActive(true);
            prizeText.gameObject.SetActive(true);
            prizeText.text = "0";
        }

        UniTask coinRainTask = UniTask.CompletedTask;

        // if (coinRainSpawner != null)
        // {
        //     coinRainTask = coinRainSpawner.PlayCoinRainAsync(token);
        // }

        if (prizeText != null && rewardCountDuration > 0f)
        {
            float t = 0f;

            while (t < rewardCountDuration && !token.IsCancellationRequested)
            {
                t += Time.unscaledDeltaTime;
                float lerpT = Mathf.Clamp01(t / rewardCountDuration);
                int value = Mathf.RoundToInt(Mathf.Lerp(0f, totalPrize, lerpT));
                prizeText.text = value.ToString();

                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }

            prizeText.text = totalPrize.ToString();
        }
        else if (prizeText != null)
        {
            prizeText.text = totalPrize.ToString();
        }

        await coinRainTask.AttachExternalCancellation(token);

        if (token.IsCancellationRequested)
        {
            return;
        }

        if (postLabelDelay > 0f)
        {
            await UniTask.Delay(
                TimeSpan.FromSeconds(postLabelDelay),
                DelayType.UnscaledDeltaTime,
                PlayerLoopTiming.Update,
                token
            );
        }
    }
}
