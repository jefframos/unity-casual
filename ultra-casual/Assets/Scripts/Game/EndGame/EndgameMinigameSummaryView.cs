using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class EndgameMinigameSummaryView : MonoBehaviour
{
    [Header("Root")]
    [Tooltip("CanvasGroup used for fading the entire summary in/out.")]
    public CanvasGroup canvasGroup;

    [Header("Targets Row (Dynamic)")]
    [Tooltip("Container where target icons will be spawned as children.")]
    public RectTransform targetsContainer;

    [Tooltip("Prefab for a single target icon Image. Will be instantiated as needed.")]
    public Image targetSlotPrefab;

    [Tooltip("Maximum number of target slots to create (e.g. 10).")]
    public int maxTargetSlots = 10;

    [Tooltip("Color for targets that were hit.")]
    public Color hitColor = Color.white;

    [Tooltip("Color for targets that were missed (greyed out).")]
    public Color missColor = Color.gray;

    [Tooltip("Delay (seconds) between each target appearing.")]
    public float targetAppearInterval = 0.05f;

    [Tooltip("Optional text showing hits (e.g. '5 / 10'). Can be left null.")]
    public TMP_Text hitsText;

    [Header("Chest & Claim")]
    [Tooltip("Chest image shown after targets are displayed.")]
    public GameObject chestRoot;

    [Tooltip("Button that the player presses to claim the reward.")]
    public Button claimButton;

    [Tooltip("Optional special graphic (e.g. boss death VFX / icon) shown only when boss is killed.")]
    public GameObject bossKillGraphic;

    [Header("Timings (seconds)")]
    public float fadeInDuration = 0.25f;
    [Tooltip("Time to hold stamp display before showing chest (after reveal finished).")]
    public float targetsHoldDuration = 0.5f;
    [Tooltip("Delay between showing chest and showing the claim button.")]
    public float claimButtonDelay = 0.3f;
    [Tooltip("Fade-out duration after chest sequence (can be 0).")]
    public float fadeOutDuration = 0.25f;

    [Header("Chest Sequence")]
    [Tooltip("Component that handles chest tween, pop, prize label, and coin rain.")]
    public EndgameMinigameChestOpenHandler chestOpenHandler;

    // runtime
    private readonly List<Image> _targetSlots = new List<Image>();
    private bool _targetsInitialized;

    /// <summary>
    /// Plays the whole summary:
    /// 1) Fade in
    /// 2) Reveal targets one by one (hit/miss)
    /// 3) Hold
    /// 4) Show chest, then claim button
    /// 5) Wait for claim
    /// 6) Hide stamps/UI and let chest handler run chest → prize → coin rain
    /// 7) Fade out and finish
    /// </summary>
    public async UniTask PlaySummaryAsync(
        int hitCount,
        int totalTargets,
        int totalPrize,
        bool bossKilled,
        CancellationToken token
    )
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;
        }

        // Safety clamp
        if (totalTargets < 0) totalTargets = 0;
        if (hitCount < 0) hitCount = 0;
        if (hitCount > totalTargets) hitCount = totalTargets;

        gameObject.SetActive(true);

        if (bossKillGraphic != null)
        {
            bossKillGraphic.SetActive(bossKilled);
        }

        // Prepare dynamic target slots (instantiate first time)
        EnsureTargetSlots(totalTargets);

        // Reset them all to inactive before reveal
        ResetTargetSlotsVisibility();

        if (hitsText != null)
        {
            hitsText.gameObject.SetActive(true);
            hitsText.text = $"{hitCount} / {totalTargets}";
        }

        if (chestRoot != null) chestRoot.SetActive(false);
        if (claimButton != null) claimButton.gameObject.SetActive(false);

        // Prize label will be shown later by the chest handler
        if (chestOpenHandler != null && chestOpenHandler.prizeText != null)
        {
            chestOpenHandler.prizeText.gameObject.SetActive(false);
        }

        // ---------------------------
        // Fade in summary
        // ---------------------------
        if (canvasGroup != null && fadeInDuration > 0f)
        {
            float t = 0f;

            while (t < fadeInDuration && !token.IsCancellationRequested)
            {
                t += Time.unscaledDeltaTime;
                float a = Mathf.Clamp01(t / fadeInDuration);
                canvasGroup.alpha = a;

                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }

            canvasGroup.alpha = 1f;
        }
        else if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
        }

        if (token.IsCancellationRequested) return;

        // ---------------------------
        // Reveal targets, one by one
        // ---------------------------
        await RevealTargetsSequenceAsync(hitCount, totalTargets, token);
        if (token.IsCancellationRequested) return;

        // Hold with all stamps visible
        if (targetsHoldDuration > 0f)
        {
            await UniTask.Delay(
                TimeSpan.FromSeconds(targetsHoldDuration),
                DelayType.UnscaledDeltaTime,
                PlayerLoopTiming.Update,
                token
            );
        }

        if (token.IsCancellationRequested) return;

        // ---------------------------
        // Show chest, then claim button
        // ---------------------------
        if (chestRoot != null)
        {
            chestRoot.SetActive(true);
        }

        if (claimButtonDelay > 0f)
        {
            await UniTask.Delay(
                TimeSpan.FromSeconds(claimButtonDelay),
                DelayType.UnscaledDeltaTime,
                PlayerLoopTiming.Update,
                token
            );
        }

        if (token.IsCancellationRequested) return;

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

            void OnClicked() => clicked = true;

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

            if (token.IsCancellationRequested) return;
        }

        // ---------------------------
        // Claim phase:
        // Hide everything except the chest, then run chest sequence
        // ---------------------------
        HideTargetsAndClaimUI();

        if (chestOpenHandler != null)
        {
            if (chestRoot != null)
            {
                chestRoot.SetActive(true);
            }

            await chestOpenHandler.PlayChestSequenceAsync(totalPrize, token);
        }

        if (token.IsCancellationRequested) return;

        // ---------------------------
        // Optionally fade out the whole summary
        // ---------------------------
        if (canvasGroup != null && fadeOutDuration > 0f)
        {
            float t = 0f;

            while (t < fadeOutDuration && !token.IsCancellationRequested)
            {
                t += Time.unscaledDeltaTime;
                float a = Mathf.Clamp01(1f - (t / fadeOutDuration));
                canvasGroup.alpha = a;

                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }

            canvasGroup.alpha = 0f;
        }

        gameObject.SetActive(false);
    }

    // ------------------------------------------
    // Target slots: instantiate and manage
    // ------------------------------------------

    private void EnsureTargetSlots(int totalTargets)
    {
        if (targetsContainer == null || targetSlotPrefab == null)
        {
            return;
        }

        if (!_targetsInitialized)
        {
            _targetSlots.Clear();
            _targetsInitialized = true;
        }

        // How many we actually need for this run
        int desired = Mathf.Clamp(totalTargets, 0, maxTargetSlots);

        // Instantiate additional slots if needed
        while (_targetSlots.Count < desired)
        {
            var img = Instantiate(targetSlotPrefab, targetsContainer);
            var rt = img.rectTransform;

            // Ensure middle-center alignment
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            img.gameObject.SetActive(false); // start disabled

            _targetSlots.Add(img);
        }

        // If we had more from a previous run than we need now, disable extras
        for (int i = 0; i < _targetSlots.Count; i++)
        {
            if (_targetSlots[i] == null) continue;

            if (i < desired)
            {
                // Leave them disabled; reveal step will show them
                _targetSlots[i].gameObject.SetActive(false);
            }
            else
            {
                // Not used in this run
                _targetSlots[i].gameObject.SetActive(false);
            }
        }
    }

    private void ResetTargetSlotsVisibility()
    {
        for (int i = 0; i < _targetSlots.Count; i++)
        {
            if (_targetSlots[i] != null)
            {
                _targetSlots[i].gameObject.SetActive(false);
            }
        }
    }

    private async UniTask RevealTargetsSequenceAsync(
        int hitCount,
        int totalTargets,
        CancellationToken token
    )
    {
        if (_targetSlots.Count == 0) return;

        int desired = Mathf.Clamp(totalTargets, 0, Mathf.Min(maxTargetSlots, _targetSlots.Count));

        for (int i = 0; i < desired; i++)
        {
            if (token.IsCancellationRequested) break;

            var img = _targetSlots[i];
            if (img == null) continue;

            bool isHit = (i < hitCount);
            img.color = isHit ? hitColor : missColor;

            img.gameObject.SetActive(true);

            if (targetAppearInterval > 0f && i < desired - 1)
            {
                await UniTask.Delay(
                    TimeSpan.FromSeconds(targetAppearInterval),
                    DelayType.UnscaledDeltaTime,
                    PlayerLoopTiming.Update,
                    token
                );
            }
        }
    }

    private void HideTargetsAndClaimUI()
    {
        // Hide target stamps
        for (int i = 0; i < _targetSlots.Count; i++)
        {
            if (_targetSlots[i] != null)
            {
                _targetSlots[i].gameObject.SetActive(false);
            }
        }

        // Hide hits text
        if (hitsText != null)
        {
            hitsText.gameObject.SetActive(false);
        }

        // Hide boss graphic
        if (bossKillGraphic != null)
        {
            bossKillGraphic.SetActive(false);
        }

        // Hide claim button
        if (claimButton != null)
        {
            claimButton.gameObject.SetActive(false);
        }

        // Chest stays visible – chest handler will deal with it.
    }
}
