using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class EndgameMinigameBossHealth : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("Sliced Image whose width will represent the boss life/progress.")]
    public Image lifeFillImage;

    [Tooltip("If true, 0 = full bar and 1 = empty bar (invert). " +
             "If false, 0 = empty and 1 = full.")]
    public bool invert = true;

    public RectTransform _rectTransform;
    private Vector2 _originalSizeDelta;
    private bool _initialized;

    private void Awake()
    {
        InitIfNeeded();
    }

    private void OnEnable()
    {
        // Optional: keep this to ensure it's initialized if Awake timing is weird
        InitIfNeeded();
    }

    private void InitIfNeeded()
    {
        if (_initialized)
        {
            return;
        }

        if (lifeFillImage == null)
        {
            return;
        }

        //_rectTransform = lifeFillImage.rectTransform;
        _originalSizeDelta = _rectTransform.sizeDelta;
        _initialized = true;
    }

    /// <summary>
    /// Called by EndgameMinigameOrchestrator when a target is hit.
    /// normalizedHitProgress is 0..1 based on how many targets were hit.
    /// </summary>
    public void SetNormalizedProgress(float normalizedHitProgress)
    {

        Debug.Log("normalizedHitProgress");


        if (!_initialized)
        {
            InitIfNeeded();
        }

        // if (_rectTransform == null)
        // {
        //     return;
        // }

        normalizedHitProgress = Mathf.Clamp01(normalizedHitProgress);

        // 0..1 "visual" value depending on invert
        float value = invert
            ? 1f - normalizedHitProgress   // 0 hits = full bar, all hits = empty
            : normalizedHitProgress;       // 0 hits = empty, all hits = full

        float newWidth = _originalSizeDelta.x * value;

        Debug.Log(normalizedHitProgress);

        var size = _rectTransform.sizeDelta;
        size.x = newWidth;
        _rectTransform.sizeDelta = size;
    }
}
