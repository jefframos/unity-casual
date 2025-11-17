using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using DG.Tweening;

[DisallowMultipleComponent]
public class UiLevelProgression : MonoBehaviour
{
    [Header("Layout")]
    [Tooltip("Parent container for trackers + bars. Anchor pivot at middle center.")]
    public RectTransform container;

    [Tooltip("Prefab for each tracker element (UiLevelTrackerElement).")]
    public UiLevelTrackerElement trackerPrefab;

    [Tooltip("Prefab for the fill bar between trackers (Image with Filled type).")]
    public Image fillBarPrefab;

    [Tooltip("Horizontal spacing between consecutive trackers in local units.")]
    public float trackerSpacing = 150f;

    [Header("Animation")]
    [Tooltip("Base duration of the fill tween in seconds.")]
    public float fillTweenDuration = 0.25f;

    private readonly List<UiLevelTrackerElement> _trackers = new();
    private readonly List<Image> _fillBars = new(); // bar i connects tracker i -> i+1
    private readonly Dictionary<EnemyGrade, int> _gradeToIndex = new();

    private CancellationTokenSource _cts;

    // --------------------------------------------------
    // Public API
    // --------------------------------------------------

    /// <summary>
    /// Build the progression UI given an ordered list of grades.
    /// The first grade becomes Active, others Hidden.
    /// </summary>
    public void Initialize(IReadOnlyList<EnemyGrade> orderedGrades)
    {
        ClearAll();

        if (container == null || trackerPrefab == null || fillBarPrefab == null)
        {
            Debug.LogWarning("UiLevelProgression: container/prefabs not assigned.");
            return;
        }

        if (orderedGrades == null || orderedGrades.Count == 0)
        {
            return;
        }

        _cts = new CancellationTokenSource();

        // Compute centered positions
        int count = orderedGrades.Count;
        float totalWidth = (count - 1) * trackerSpacing;
        float startX = -totalWidth * 0.5f;

        // Create trackers
        for (int i = 0; i < count; i++)
        {
            var tracker = Instantiate(trackerPrefab, container);
            var rt = tracker.transform as RectTransform;

            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(startX + i * trackerSpacing, 0f);
            rt.localScale = Vector3.one;

            var grade = orderedGrades[i];
            tracker.SetGrade(grade);
            tracker.SetState(i == 0 ? UiLevelTrackerState.Active : UiLevelTrackerState.Hidden);

            _trackers.Add(tracker);
            _gradeToIndex[grade] = i;
        }

        // Create fill bars between trackers
        _fillBars.Clear();
        for (int i = 0; i < _trackers.Count - 1; i++)
        {
            var a = (RectTransform)_trackers[i].transform;
            var b = (RectTransform)_trackers[i + 1].transform;

            var bar = Instantiate(fillBarPrefab, container);
            var brt = bar.transform as RectTransform;

            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.pivot = new Vector2(0.5f, 0.5f);

            // midpoint between trackers
            float midX = (a.anchoredPosition.x + b.anchoredPosition.x) * 0.5f;
            brt.anchoredPosition = new Vector2(midX, 0f);
            brt.localScale = Vector3.one;

            // optional: adjust bar width based on spacing
            brt.sizeDelta = new Vector2(trackerSpacing * 0.7f, brt.sizeDelta.y);

            bar.fillAmount = 0f;

            _fillBars.Add(bar);
        }
    }

    /// <summary>
    /// Call this whenever the kill count changes for a given grade.
    /// This will tween the bar between this grade and the next one.
    /// When the bar hits 100%, it activates the next tracker.
    /// </summary>
    public void UpdateGradeProgress(EnemyGrade grade, int dead, int total)
    {
        if (!_gradeToIndex.TryGetValue(grade, out var index))
        {
            return;
        }

        if (total <= 0)
        {
            return;
        }

        // Last tracker has no forward bar
        if (index < 0 || index >= _fillBars.Count)
        {
            return;
        }

        float targetFill = Mathf.Clamp01((float)dead / total);
        var bar = _fillBars[index];

        // If going backwards or same, just clamp without animation
        if (targetFill <= bar.fillAmount + 0.0001f)
        {
            bar.fillAmount = targetFill;
            return;
        }

        bool willReachFull = targetFill >= 1f - 0.0001f;
        AnimateBarAsync(index, targetFill, willReachFull).Forget();
    }

    // --------------------------------------------------
    // Internals
    // --------------------------------------------------

    private async UniTask AnimateBarAsync(int barIndex, float targetFill, bool willReachFull)
    {
        if (barIndex < 0 || barIndex >= _fillBars.Count)
        {
            return;
        }

        var token = _cts != null ? _cts.Token : CancellationToken.None;
        var bar = _fillBars[barIndex];

        bar.DOKill();

        await bar.DOFillAmount(targetFill, fillTweenDuration)
                 .SetEase(Ease.Linear)
                 .AsyncWaitForCompletion();

        if (token.IsCancellationRequested)
        {
            return;
        }

        if (willReachFull)
        {
            OnBarReachedFull(barIndex);
        }
    }

    private void OnBarReachedFull(int barIndex)
    {
        int currentTrackerIndex = barIndex;
        int nextTrackerIndex = barIndex + 1;

        if (currentTrackerIndex >= 0 && currentTrackerIndex < _trackers.Count)
        {
            _trackers[currentTrackerIndex].SetState(UiLevelTrackerState.Completed);
        }

        if (nextTrackerIndex >= 0 && nextTrackerIndex < _trackers.Count)
        {
            _trackers[nextTrackerIndex].SetState(UiLevelTrackerState.Active);
        }
    }

    private void ClearAll()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        for (int i = 0; i < _trackers.Count; i++)
        {
            if (_trackers[i] != null)
            {
                Destroy(_trackers[i].gameObject);
            }
        }
        _trackers.Clear();

        for (int i = 0; i < _fillBars.Count; i++)
        {
            if (_fillBars[i] != null)
            {
                Destroy(_fillBars[i].gameObject);
            }
        }
        _fillBars.Clear();

        _gradeToIndex.Clear();
    }

    private void OnDisable()
    {
        ClearAll();
    }
}
