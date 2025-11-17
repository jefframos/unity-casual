using System;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using Cysharp.Threading.Tasks;

[DisallowMultipleComponent]
public class UiLevelProgressFillBar : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Foreground fill image (Image type = Sliced).")]
    [SerializeField] private Image fillImage;

    [Tooltip("Control rect that defines position, height and max width.")]
    [SerializeField] private RectTransform controlRect;

    [Header("Behaviour")]
    [Tooltip("Minimum width before the fill becomes visible.")]
    [SerializeField] private float minVisibleWidth = 20f;

    public RectTransform RectTransform => (RectTransform)transform;

    private RectTransform _fillRect;
    private Tween _tween;

    /// <summary>
    /// Returns current fill ratio based on rect width / max width.
    /// </summary>
    public float CurrentFill
    {
        get
        {
            EnsureInitialized();

            float maxWidth = GetMaxWidth();
            if (_fillRect == null || maxWidth <= 0f) return 0f;

            return Mathf.Clamp01(_fillRect.sizeDelta.x / maxWidth);
        }
    }

    private void Awake()
    {
        EnsureInitialized();
    }

    private void EnsureInitialized()
    {
        if (fillImage != null && _fillRect == null)
        {
            _fillRect = fillImage.rectTransform;
            // Fill grows from left-middle of the control
            _fillRect.pivot = new Vector2(0f, 0.5f);
        }

        SyncGeometryFromControl();
    }

    /// <summary>
    /// Get the maximum width based on the control rect.
    /// </summary>
    private float GetMaxWidth()
    {
        if (controlRect == null)
            return 0f;

        float w = controlRect.rect.width;
        if (w <= 0f) w = controlRect.sizeDelta.x;
        return w;
    }

    /// <summary>
    /// Sync the fill's position and height to the control rect.
    /// Left-middle of control -> pivot of fill.
    /// </summary>
    private void SyncGeometryFromControl()
    {
        if (_fillRect == null || controlRect == null)
            return;

        Rect rect = controlRect.rect;

        // Local position of the control (its pivot)
        Vector2 controlPos = controlRect.anchoredPosition;

        // Compute left-middle point in local space of the parent
        float leftX = 0f;//controlPos.x + (0f - controlRect.pivot.x) * rect.width;
        float midY = controlPos.y + (0.5f - controlRect.pivot.y) * rect.height;

        _fillRect.anchoredPosition = new Vector2(leftX, midY);

        // Match height
        float height = rect.height;
        if (Mathf.Approximately(height, 0f))
            height = controlRect.sizeDelta.y;

        var size = _fillRect.sizeDelta;
        size.y = height;
        _fillRect.sizeDelta = size;
    }

    /// <summary>
    /// Set the fill instantly (no tween) by resizing the sliced image width.
    /// </summary>
    public void SetInstantFill(float value)
    {
        EnsureInitialized();
        value = Mathf.Clamp01(value);

        if (_fillRect == null)
            return;

        float maxWidth = GetMaxWidth();
        if (maxWidth <= 0f)
            return;

        _tween?.Kill();

        float targetWidth = maxWidth * value;
        ApplyWidthAndVisibility(targetWidth);
    }

    /// <summary>
    /// Tween the fill to the target value by resizing the sliced image.
    /// When finished and not cancelled, invoke onCompleted.
    /// </summary>
    public async UniTask AnimateToAsync(
        float target,
        float duration,
        Action onCompleted = null,
        CancellationToken ct = default)
    {
        EnsureInitialized();

        if (_fillRect == null)
            return;

        float maxWidth = GetMaxWidth();
        if (maxWidth <= 0f)
            return;

        target = Mathf.Clamp01(target);
        float targetWidth = maxWidth * target;

        var startSize = _fillRect.sizeDelta;

        _tween?.Kill();
        _tween = _fillRect
            .DOSizeDelta(new Vector2(targetWidth, startSize.y), duration)
            .SetEase(Ease.Linear)
            .OnUpdate(() =>
            {
                // Keep following the control’s position/height while animating
                SyncGeometryFromControl();
                ApplyWidthAndVisibility(_fillRect.sizeDelta.x);
            });

        await _tween.AsyncWaitForCompletion();

        if (!ct.IsCancellationRequested)
        {
            SyncGeometryFromControl();
            ApplyWidthAndVisibility(_fillRect.sizeDelta.x);
            onCompleted?.Invoke();
        }
    }

    /// <summary>
    /// Apply width to the fill rect and handle visibility threshold.
    /// </summary>
    private void ApplyWidthAndVisibility(float width)
    {
        if (_fillRect == null || fillImage == null)
            return;

        var size = _fillRect.sizeDelta;
        size.x = Mathf.Max(0f, width);
        _fillRect.sizeDelta = size;

        // Only show once it passes the minimum visible width
        fillImage.enabled = width >= minVisibleWidth;
    }
}
