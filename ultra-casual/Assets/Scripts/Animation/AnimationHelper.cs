using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;

public static class AnimationHelper
{
    /// <summary>Fade a CanvasGroup from 'from' to 'to' in 'seconds'.</summary>
    public static async UniTask FadeCanvasAsync(
        CanvasGroup cg,
        float from,
        float to,
        float seconds,
        CancellationToken token)
    {
        if (cg == null)
            return;

        cg.alpha = from;
        cg.DOKill();
        var tween = cg.DOFade(to, seconds)
            .SetEase(Ease.OutSine)
            .SetUpdate(true); // unscaled time

        await tween.AsyncWaitForCompletion().AsUniTask().AttachExternalCancellation(token);
        cg.alpha = to;
    }

    /// <summary>Animate a numeric score from startValue → endValue in seconds.</summary>
    public static async UniTask AnimateScoreAsync(
        TextMeshProUGUI label,
        float startValue,
        float endValue,
        float seconds,
        string prefix = "",
        CancellationToken token = default)
    {
        if (label == null)
            return;

        float value = startValue;
        label.DOKill();

        var tween = DOTween.To(() => value, v =>
        {
            value = v;
            label.text = $"{prefix}{Mathf.RoundToInt(value)}";
        },
        endValue, seconds)
        .SetEase(Ease.OutCubic)
        .SetUpdate(true);

        await tween.AsyncWaitForCompletion().AsUniTask().AttachExternalCancellation(token);
        label.text = $"{prefix}{Mathf.RoundToInt(endValue)}";
    }

    /// <summary>Scale a transform from start → end in seconds (localScale).</summary>
    public static async UniTask ScaleTransformAsync(
        Transform target,
        Vector3 from,
        Vector3 to,
        float seconds,
        Ease ease = Ease.OutBack,
        CancellationToken token = default)
    {
        if (target == null)
            return;

        target.localScale = from;
        target.DOKill();

        var tween = target.DOScale(to, seconds)
            .SetEase(ease)
            .SetUpdate(true);

        await tween.AsyncWaitForCompletion().AsUniTask().AttachExternalCancellation(token);
        target.localScale = to;
    }
}
