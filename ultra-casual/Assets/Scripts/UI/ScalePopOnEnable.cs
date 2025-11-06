using UnityEngine;
using DG.Tweening;

/// <summary>
/// When this GameObject is enabled, plays a pop tween on local scale.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("FX/Scale Pop On Enable")]
public class ScalePopOnEnable : MonoBehaviour
{
    [Header("Scale Pop Settings")]
    [Tooltip("Target scale when fully popped (relative to start).")]
    public float popScaleMultiplier = 1.2f;

    [Tooltip("Duration of the pop animation in seconds.")]
    public float popDuration = 0.25f;

    [Tooltip("Ease type for the pop motion.")]
    public Ease popEase = Ease.OutBack;

    [Tooltip("If true, starts from zero scale instead of current scale.")]
    public bool startFromZero = false;

    [Tooltip("If true, automatically plays again every time it is re-enabled.")]
    public bool replayOnEachEnable = true;

    private Vector3 _baseScale;
    private Tween _tween;
    public bool playOnEnable = true;

    private void Awake()
    {
        _baseScale = transform.localScale;
    }

    private void OnEnable()
    {
        if (!playOnEnable)
        {
            return;
        }
        if (!replayOnEachEnable && _tween != null && _tween.IsPlaying()) return;

        PlayPopTween();
    }

    private void OnDisable()
    {
        _tween?.Kill();
        transform.localScale = _baseScale;
    }

    public void PlayPopTween()
    {
        _tween?.Kill();

        if (startFromZero)
        {
            transform.localScale = Vector3.zero;
        }
        else
        {
            transform.localScale = _baseScale;
        }

        _tween = transform
            .DOScale(_baseScale * popScaleMultiplier, popDuration)
            .SetEase(popEase)
            .OnComplete(() =>
            {
                // Return smoothly to base scale
                transform.DOScale(_baseScale, popDuration * 0.5f)
                         .SetEase(Ease.OutQuad);
            });
    }
}
