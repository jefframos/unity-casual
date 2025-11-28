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

    [Header("Optional Boss Graphic")]
    [Tooltip("Optional special graphic (e.g. boss death VFX / icon) shown only when boss is killed.")]
    public GameObject bossKillGraphic;

    [Header("Timings (seconds)")]
    public float fadeInDuration = 0.25f;
    [Tooltip("Time to hold stamp display before handing off to chest sequence.")]
    public float targetsHoldDuration = 0.5f;
    [Tooltip("Fade-out duration after everything is done (can be 0).")]
    public float fadeOutDuration = 0.25f;

    [Header("Chest Sequence")]
    [Tooltip("Component that handles chest tween, pop, prize label, and coin rain (including claim button).")]
    public EndgameMinigameChestOpenHandler chestOpenHandler;

    // runtime
    private readonly List<Image> _targetSlots = new List<Image>();
    private bool _targetsInitialized;

    private void Awake()
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
        }
    }

    /// <summary>
    /// Plays the whole summary:
    /// 1) Fade in
    /// 2) Reveal targets one by one (hit/miss)
    /// 3) Hold
    /// 4) Hide target UI and hand off to chestOpenHandler (chest + claim + prize + coins)
    /// 5) Fade out and finish
    /// </summary>
    public async UniTask PlaySummaryAsync(
        int hitCount,
        int totalTargets,
        int totalPrize,
        bool bossKilled,
        CancellationToken token
    )
    {
        chestOpenHandler.gameObject.SetActive(false);
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;
        }

        // Safety clamp
        if (totalTargets < 0)
        {
            totalTargets = 0;
        }

        if (hitCount < 0)
        {
            hitCount = 0;
        }

        if (hitCount > totalTargets)
        {
            hitCount = totalTargets;
        }

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

        // Prize label is managed by chestOpenHandler
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

        if (token.IsCancellationRequested)
        {
            return;
        }

        // ---------------------------
        // Reveal targets, one by one
        // ---------------------------
        await RevealTargetsSequenceAsync(hitCount, totalTargets, token);
        if (token.IsCancellationRequested)
        {
            return;
        }

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

        if (token.IsCancellationRequested)
        {
            return;
        }

        // ---------------------------
        // Hide targets UI before chest sequence
        // ---------------------------
        HideTargetsUI();

        // ---------------------------
        // Run chest + claim + prize + coin rain
        // ---------------------------
        if (chestOpenHandler != null)
        {
            await chestOpenHandler.PlayChestSequenceAsync(totalPrize, token);
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

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

        int desired = Mathf.Clamp(totalTargets, 0, maxTargetSlots);

        // Instantiate additional slots if needed
        while (_targetSlots.Count < desired)
        {
            Image img = Instantiate(targetSlotPrefab, targetsContainer);
            RectTransform rt = img.rectTransform;

            // Ensure middle-center alignment
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            img.gameObject.SetActive(false); // start disabled

            _targetSlots.Add(img);
        }

        // Disable extras
        for (int i = 0; i < _targetSlots.Count; i++)
        {
            if (_targetSlots[i] == null)
            {
                continue;
            }

            _targetSlots[i].gameObject.SetActive(i < desired ? false : false);
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
        if (_targetSlots.Count == 0)
        {
            return;
        }

        int desired = Mathf.Clamp(totalTargets, 0, Mathf.Min(maxTargetSlots, _targetSlots.Count));

        for (int i = 0; i < desired; i++)
        {
            if (token.IsCancellationRequested)
            {
                break;
            }

            Image img = _targetSlots[i];
            if (img == null)
            {
                continue;
            }

            bool isHit = i < hitCount;
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

    private void HideTargetsUI()
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
    }
}
