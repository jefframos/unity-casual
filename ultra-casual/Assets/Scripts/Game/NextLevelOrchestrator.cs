using System;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class NextLevelOrchestrator : MonoBehaviour
{
    [Header("Root")]
    public CanvasGroup rootCanvasGroup;

    [Header("Level UI")]
    public TextMeshProUGUI currentLevelLabel;
    public TextMeshProUGUI nextLevelLabel;
    [Tooltip("Image with Fill Method set to Filled.")]
    public Image levelProgressFill;

    [Header("Gift UI")]
    [Tooltip("Present fill image (Filled).")]
    public Image giftFillImage;
    [Tooltip("Optional FX shown when gift is full (shine, particles etc).")]
    public GameObject giftFullFx;
    public GameObject giftGo;

    [Header("Interaction UI")]
    [Tooltip("Button used when gift is full (player claims reward).")]
    public Button claimGiftButton;
    [Tooltip("Label that appears when gift is NOT full (e.g. 'Play more to fill the gift!').")]
    public GameObject giftNotFullLabel;
    [Tooltip("Generic 'tap to continue' label.")]
    public GameObject tapToContinueLabel;
    [Tooltip("Button or clickable area for the final player interaction.")]
    public Button continueButton;

    [Header("Timings")]
    public float fadeInDuration = 0.25f;
    public float fadeOutDuration = 0.2f;
    public float levelBarDuration = 0.6f;
    public float giftFillDuration = 0.6f;

    public bool IsBusy { get; private set; }

    void Awake()
    {
        rootCanvasGroup.alpha = 0;
    }
    /// <summary>
    /// Orchestrates the "Next Level" UI.
    /// Example: fromLevel = 2, toLevel = 3.
    /// giftFillNormalized: target fill for the present (0..1).
    /// giftIsFull: if true, show claim button; if false, show "play more" label.
    /// onClaimGift: optional async callback, awaited after the user taps Claim.
    /// </summary>
    public async UniTask OrchestrateNextLevelAsync(
        int fromLevel,
        int toLevel,
        float giftFillNormalized,
        bool giftIsFull,
        bool skipGift,
        Func<UniTask> onClaimGift = null
    )
    {
        giftGo.SetActive(!skipGift);

        if (IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            PrepareInitialState();

            // Set level labels
            if (currentLevelLabel != null)
            {
                currentLevelLabel.text = fromLevel.ToString();
            }

            if (nextLevelLabel != null)
            {
                nextLevelLabel.text = toLevel.ToString();
            }

            // Reset bar / gift visuals
            if (levelProgressFill != null)
            {
                levelProgressFill.fillAmount = 0f;
            }

            if (giftFillImage != null)
            {
                giftFillImage.fillAmount = 0f;
            }

            // Fade in root
            if (rootCanvasGroup != null)
            {
                rootCanvasGroup.alpha = 0f;
                gameObject.SetActive(true);

                await rootCanvasGroup
                    .DOFade(1f, fadeInDuration)
                    .SetUpdate(true)
                    .AsyncWaitForCompletion();
            }
            else
            {
                gameObject.SetActive(true);
            }

            // 1) Animate level progress bar from 0 -> 1
            if (levelProgressFill != null)
            {
                await levelProgressFill
                    .DOFillAmount(1f, levelBarDuration)
                    .SetEase(Ease.OutQuad)
                    .SetUpdate(true)
                    .AsyncWaitForCompletion();
            }

            if (!skipGift)
            {

                // 2) Animate gift fill to target value
                giftFillNormalized = Mathf.Clamp01(giftFillNormalized);

                if (giftFillImage != null)
                {
                    await giftFillImage
                        .DOFillAmount(giftFillNormalized, giftFillDuration)
                        .SetEase(Ease.OutQuad)
                        .SetUpdate(true)
                        .AsyncWaitForCompletion();
                }

                // 3) Branch: gift full vs not full
                if (giftIsFull)
                {
                    await HandleGiftFullAsync(onClaimGift);
                }
                else
                {
                    await HandleGiftNotFullAsync();
                }
            }
            else
            {
                await HandleGiftNotFullAsync();

            }

            // 4) Fade out and hide
            if (rootCanvasGroup != null)
            {
                await rootCanvasGroup
                    .DOFade(0f, fadeOutDuration)
                    .SetUpdate(true)
                    .AsyncWaitForCompletion();
            }

            //gameObject.SetActive(false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void PrepareInitialState()
    {
        // Reset interaction UI
        if (claimGiftButton != null)
        {
            claimGiftButton.gameObject.SetActive(false);
        }

        if (giftNotFullLabel != null)
        {
            giftNotFullLabel.SetActive(false);
        }

        if (tapToContinueLabel != null)
        {
            tapToContinueLabel.SetActive(false);
        }

        if (continueButton != null)
        {
            continueButton.gameObject.SetActive(false);
        }

        if (giftFullFx != null)
        {
            giftFullFx.SetActive(false);
        }
    }

    private async UniTask HandleGiftFullAsync(Func<UniTask> onClaimGift)
    {
        // Present is full: show FX + Claim button
        if (giftFullFx != null)
        {
            giftFullFx.SetActive(true);
        }

        if (giftNotFullLabel != null)
        {
            giftNotFullLabel.SetActive(false);
        }

        if (claimGiftButton != null)
        {
            claimGiftButton.gameObject.SetActive(true);
            await WaitForButtonClickAsync(claimGiftButton);
            claimGiftButton.gameObject.SetActive(false);
        }

        // Call async gift-claim function (can be empty)
        if (onClaimGift != null)
        {
            await onClaimGift();
        }

        // After claim: show "tap to continue" prompt and wait for final interaction
        if (tapToContinueLabel != null)
        {
            tapToContinueLabel.SetActive(true);
        }

        if (continueButton != null)
        {
            continueButton.gameObject.SetActive(true);
            await WaitForButtonClickAsync(continueButton);
        }
        else
        {
            // Fallback: if no button assigned, at least wait one frame
            await UniTask.Yield();
        }
    }

    private async UniTask HandleGiftNotFullAsync()
    {
        // No full gift: show label, then wait for *next* player interaction to finish.
        if (giftNotFullLabel != null)
        {
            giftNotFullLabel.SetActive(true);
        }

        if (tapToContinueLabel != null)
        {
            tapToContinueLabel.SetActive(true);
        }

        if (continueButton != null)
        {
            continueButton.gameObject.SetActive(true);
            await WaitForButtonClickAsync(continueButton);
        }
        else
        {
            await UniTask.Yield();
        }
    }

    private static async UniTask WaitForButtonClickAsync(Button button)
    {
        var tcs = new UniTaskCompletionSource();

        void Handler()
        {
            button.onClick.RemoveListener(Handler);
            tcs.TrySetResult();
        }

        button.onClick.AddListener(Handler);

        await tcs.Task;
    }
}
