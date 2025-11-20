using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using Cysharp.Threading.Tasks;
using System.Data.Common;

/// <summary>
/// Pure UI: subscribes to LevelTrackerMediator and builds/updates labels.
/// Uses simple object pooling for the UI rows.
/// Also handles a horizontal progression bar between trackers.
/// </summary>
public class LevelTrackerUiController : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("Where to put the instantiated enemy rows.")]
    public RectTransform container;

    [Tooltip("Prefab with UiLevelTrackerElement + UiLabelRef.")]
    public UiLevelTrackerElement rowPrefab;

    [Tooltip("Prefab for the fill bar between trackers (with UiLevelProgressFillBar).")]
    public UiLevelProgressFillBar progressBarPrefab;

    [Tooltip("Prefab shown after the final grade (e.g. trophy).")]
    public TrophyTracker trophyPrefab;

    [Tooltip("Mediator that provides level snapshots.")]
    public LevelTrackerMediator levelTrackerMediator;

    [Header("Layout")]
    [Tooltip("Horizontal spacing between trackers (in local units).")]
    public float maxTrackerMax = 350f;
    public float trackerMax = 350f;
    public float maxTrackerSpacing = 150f;
    public float trackerSpacing = 150f;

    [Tooltip("Duration of fill tween for the current grade.")]
    public float fillTweenDuration = 0.25f;

    // grade -> active instantiated UI row
    private readonly Dictionary<EnemyGrade, UiLevelTrackerElement> _rows = new();

    // pooled (inactive) rows ready to be reused
    private readonly Stack<UiLevelTrackerElement> _pool = new();

    // ordered view of rows / grades for layout & progression
    private readonly List<UiLevelTrackerElement> _orderedRows = new();
    private readonly List<EnemyGrade> _orderedGrades = new();

    // bars between trackers
    private readonly List<UiLevelProgressFillBar> _progressBars = new();
    private readonly Stack<UiLevelProgressFillBar> _barPool = new();

    // trophy instance
    private TrophyTracker _trophyInstance;

    // which tracker index is currently "active" in progression terms
    // 0..N-1 = that grade index is active, N = trophy
    private int _currentIndex = -1;

    // cache of the last grade data to avoid redundant rebuilds/animations
    private List<GradeViewData> _lastGradeData = new List<GradeViewData>();

    // NEW: did we already build UI at least once for this level?
    private bool _hasSnapshotForThisLevel = false;


    // small struct to keep only the data we need per grade
    private struct GradeViewData
    {
        public EnemyGrade grade;
        public int dead;
        public int total;
    }

    private void OnEnable()
    {
        if (levelTrackerMediator == null)
        {
            return;
        }

        levelTrackerMediator.OnMoveEnded += HandleSnapshotUpdated;
        levelTrackerMediator.OnTrackersRefreshed += HandleTrackersRefreshed;
        levelTrackerMediator.OnResetStarted += ResetLevel;

        // Ask mediator to push a snapshot so we can build initial UI
        levelTrackerMediator.ForceBroadcastSnapshot();
    }

    private void OnDisable()
    {
        if (levelTrackerMediator != null)
        {
            levelTrackerMediator.OnMoveEnded -= HandleSnapshotUpdated;
            levelTrackerMediator.OnTrackersRefreshed -= HandleTrackersRefreshed;
            levelTrackerMediator.OnResetStarted -= ResetLevel;
        }

        ClearAllRows();
    }

    // --------------------------------------------------
    // Public API
    // --------------------------------------------------

    /// <summary>
    /// Fully resets the level UI so a new level can start clean.
    /// - Clears all rows & bars.
    /// - Hides trophy.
    /// - Resets current index and cached data.
    /// </summary>
    public void ResetLevel()
    {
        _currentIndex = -1;
        _lastGradeData.Clear();
        _hasSnapshotForThisLevel = false;

        ClearAllRows();
        ClearAllBars();

        if (_trophyInstance != null)
        {
            _trophyInstance.gameObject.SetActive(false);
        }

    }

    // --------------------------------------------------
    // Handlers
    // --------------------------------------------------

    private void HandleTrackersRefreshed()
    {
        // New level / trackers coming in: reset everything.
        ResetLevel();
    }

    private bool HasGradeDataChanged(List<GradeViewData> current)
    {
        if (_lastGradeData == null || _lastGradeData.Count != current.Count)
        {
            return true;
        }

        for (int i = 0; i < current.Count; i++)
        {
            var a = current[i];
            var b = _lastGradeData[i];

            if (a.grade != b.grade ||
                a.dead != b.dead ||
                a.total != b.total)
            {
                return true;
            }
        }

        return false;
    }

    private void HandleSnapshotUpdated(LevelEnemyTracker.LevelSnapshot snapshot)
    {
        HandleSnapshotUpdatedAsync(snapshot).Forget();
    }

    private async UniTaskVoid HandleSnapshotUpdatedAsync(LevelEnemyTracker.LevelSnapshot snapshot)
    {
        if (container == null || rowPrefab == null || progressBarPrefab == null)
        {
            return;
        }

        if (snapshot == null || snapshot.grades == null)
        {
            return;
        }

        // Build a sorted list of grade data (only what we need)
        var gradeData = new List<GradeViewData>();

        foreach (var gradeSnapshot in snapshot.grades)
        {
            if (gradeSnapshot == null)
            {
                continue;
            }

            if (gradeSnapshot.total <= 0)
            {
                continue;
            }

            gradeData.Add(new GradeViewData
            {
                grade = gradeSnapshot.grade,
                dead = gradeSnapshot.dead,
                total = gradeSnapshot.total
            });
        }

        if (gradeData.Count == 0)
        {
            return;
        }

        // Sort by grade enum value
        gradeData.Sort((a, b) => ((int)a.grade).CompareTo((int)b.grade));

        bool isFirstSnapshotForThisLevel = !_hasSnapshotForThisLevel;

        // Early-out if nothing changed AND this is not our first snapshot for this level
        if (!isFirstSnapshotForThisLevel && !HasGradeDataChanged(gradeData))
        {
            return;
        }

        // Cache new data and mark that this level now has an active snapshot
        _lastGradeData = new List<GradeViewData>(gradeData);
        _hasSnapshotForThisLevel = true;

        // Decide which grade is "current" from this snapshot:
        // latest index that has any progress (dead > 0). If none, default to 0.
        int latestProgressIndex = 0;
        bool foundProgress = false;
        for (int i = 0; i < gradeData.Count; i++)
        {
            if (gradeData[i].dead > 0)
            {
                latestProgressIndex = i;
                foundProgress = true;
            }
        }
        if (!foundProgress)
        {
            latestProgressIndex = 0;
        }

        if (isFirstSnapshotForThisLevel)
        {
            // First time UI appears for this level:
            // current index is the grade where we have latest progress.
            _currentIndex = Mathf.Clamp(latestProgressIndex, 0, gradeData.Count - 1);
        }
        else
        {
            // Subsequent snapshots: keep current index but clamp to valid range.
            if (_currentIndex < 0 || _currentIndex > gradeData.Count)
            {
                _currentIndex = Mathf.Clamp(_currentIndex, 0, gradeData.Count);
            }
        }

        // Rebuild rows
        ClearAllRows();

        for (int i = 0; i < gradeData.Count; i++)
        {
            var data = gradeData[i];

            var row = GetRow();
            row.SetGrade(data.grade);
            row.UpdateCounts(data.grade, data.dead, data.total);

            _rows[data.grade] = row;
            _orderedRows.Add(row);
            _orderedGrades.Add(data.grade);

            if (i < _currentIndex)
            {
                row.SetState(UiLevelTrackerState.Completed);
            }
            else if (i == _currentIndex)
            {
                row.SetState(UiLevelTrackerState.Active);
            }
            else
            {
                row.SetState(UiLevelTrackerState.Hidden);
            }
        }

        // Layout trackers horizontally, centered
        LayoutTrackers();

        // Create/update progress bars between trackers (including last -> trophy)
        BuildProgressBars();

        if (isFirstSnapshotForThisLevel)
        {
            // FIRST TIME THIS LEVEL:
            // - bars before current grade: instantly full
            // - current grade bar: animate 0 -> current fraction
            // - bars after: 0
            ApplyInitialStaticBars(gradeData);
            await AnimateInitialCurrentBarAsync(gradeData);
        }
        else
        {
            // LATER UPDATES:
            // Only animate the current bar forward from its existing fill,
            // and let OnBarReachedFull handle stepping when it hits 100%.
            await UpdateProgressBarsAsync(gradeData);
        }
    }
    /// <summary>
    /// First-snapshot path: animate ONLY the current grade bar from 0 to its
    /// current fraction, without advancing to the next grade (no OnBarReachedFull).
    /// </summary>
    private async UniTask AnimateInitialCurrentBarAsync(List<GradeViewData> gradeData)
    {
        if (_currentIndex < 0 ||
            _currentIndex >= gradeData.Count ||
            _currentIndex >= _progressBars.Count)
        {
            return;
        }

        var token = this.GetCancellationTokenOnDestroy();

        var bar = _progressBars[_currentIndex];
        if (bar == null)
        {
            return;
        }

        var data = gradeData[_currentIndex];

        float targetFill = (data.total > 0)
            ? Mathf.Clamp01((float)data.dead / data.total)
            : 0f;

        // No progress? nothing to animate
        if (targetFill <= 0f)
        {
            bar.SetInstantFill(0f);
            return;
        }

        var dataView = EnemyTypeDatabase.Instance.GetDefinition(data.grade);
        if (dataView)
        {
            bar.SetColor(dataView.color);
        }

        // Start from 0 (already enforced in ApplyInitialStaticBars).
        // IMPORTANT: onCompleted is null, so we DO NOT call OnBarReachedFull
        // and we do NOT auto-advance to the next grade on this intro animation.

        bool reachFull = targetFill >= 0.999f;

        await bar.AnimateToAsync(
            targetFill,
            fillTweenDuration,
            onCompleted: reachFull ? () => OnBarReachedFull(_currentIndex) : null,
            ct: token
        );

        // await bar.AnimateToAsync(
        //     targetFill,
        //     fillTweenDuration,
        //     onCompleted: null,
        //     ct: token
        // );
    }

    /// <summary>
    /// First-snapshot path: instantly set all bars EXCEPT the current one
    /// to their correct static state (previous = full, future = empty).
    /// The current bar will be animated separately from 0 in AnimateInitialCurrentBarAsync.
    /// </summary>
    private void ApplyInitialStaticBars(List<GradeViewData> gradeData)
    {
        if (_progressBars.Count == 0)
        {
            return;
        }

        for (int i = 0; i < _progressBars.Count && i < gradeData.Count; i++)
        {
            var bar = _progressBars[i];
            if (bar == null)
            {
                continue;
            }

            var data = gradeData[i];

            var dataView = EnemyTypeDatabase.Instance.GetDefinition(data.grade);
            if (dataView)
            {
                bar.SetColor(dataView.color);
            }

            if (i < _currentIndex)
            {
                // previous grades are already done
                bar.SetInstantFill(1f);
            }
            else if (i > _currentIndex)
            {
                // future grades: empty
                bar.SetInstantFill(0f);
            }
            else
            {
                // current grade: force to 0 now, will animate from 0 in AnimateInitialCurrentBarAsync
                bar.SetInstantFill(0f);
            }
        }
    }


    // --------------------------------------------------
    // Layout + bars
    // --------------------------------------------------

    private void LayoutTrackers()
    {
        int count = _orderedRows.Count;
        if (count == 0)
        {
            return;
        }

        int gaps = Mathf.Max(0, count);

        float spacing = gaps > 0 ? trackerMax / gaps : 0f;

        if (spacing > maxTrackerSpacing)
        {
            spacing = maxTrackerSpacing;
        }

        trackerSpacing = spacing;

        float totalWidth = gaps * spacing;
        float startX = -totalWidth * 0.5f;

        for (int i = 0; i < count; i++)
        {
            var rt = _orderedRows[i].transform as RectTransform;
            if (rt == null)
            {
                continue;
            }

            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            rt.anchoredPosition = new Vector2(startX + i * spacing, 0f);
            rt.localScale = Vector3.one;
        }
    }

    private void BuildProgressBars()
    {
        int count = _orderedRows.Count;
        if (count == 0)
        {
            return;
        }

        // Ensure / position trophy
        RectTransform trophyRt = null;
        if (trophyPrefab != null)
        {
            if (_trophyInstance == null)
            {
                _trophyInstance = Instantiate(trophyPrefab, container);
            }
            _trophyInstance.SetState(UiLevelTrackerState.Active);

            trophyRt = _trophyInstance.GetComponent<RectTransform>();
            trophyRt.gameObject.SetActive(true);

            trophyRt.anchorMin = trophyRt.anchorMax = new Vector2(0.5f, 0.5f);
            trophyRt.pivot = new Vector2(0.5f, 0.5f);

            var lastRowRt = (RectTransform)_orderedRows[count - 1].transform;
            float trophyX = lastRowRt.anchoredPosition.x + trackerSpacing;
            trophyRt.anchoredPosition = new Vector2(trophyX, 0f);
            trophyRt.localScale = Vector3.one;
        }

        int neededBars = count;

        while (_progressBars.Count > neededBars)
        {
            var lastBar = _progressBars[_progressBars.Count - 1];
            if (lastBar != null)
            {
                lastBar.gameObject.SetActive(false);
                lastBar.SetInstantFill(0f);
                _barPool.Push(lastBar);
            }
            _progressBars.RemoveAt(_progressBars.Count - 1);
        }

        while (_progressBars.Count < neededBars)
        {
            var newBar = GetBar();
            if (newBar != null)
            {
                _progressBars.Add(newBar);
            }
            else
            {
                break;
            }
        }

        // Position all active bars (do NOT reset fill here)
        for (int i = 0; i < _progressBars.Count && i < count; i++)
        {
            var bar = _progressBars[i];
            if (bar == null)
            {
                continue;
            }

            var start = (RectTransform)_orderedRows[i].transform;
            RectTransform end;

            if (i + 1 < count)
            {
                end = (RectTransform)_orderedRows[i + 1].transform;
            }
            else
            {
                end = trophyRt; // last grade -> trophy
            }

            if (end == null)
            {
                bar.gameObject.SetActive(false);
                continue;
            }

            var brt = bar.RectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.pivot = new Vector2(0.5f, 0.5f);

            float midX = (start.anchoredPosition.x + end.anchoredPosition.x) * 0.5f;
            brt.anchoredPosition = new Vector2(midX, 0f);
            brt.localScale = Vector3.one;

            float dx = Mathf.Abs(end.anchoredPosition.x - start.anchoredPosition.x);
            var size = brt.sizeDelta;
            size.x = dx * 0.7f;
            brt.sizeDelta = size;

            bar.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// First-snapshot path: snap bars directly to target fill from snapshot,
    /// no animation, so mid-level UI doesn't replay from zero.
    /// </summary>
    private void ApplyInitialBarFill(List<GradeViewData> gradeData)
    {
        if (_progressBars.Count == 0)
        {
            return;
        }

        for (int i = 0; i < _progressBars.Count && i < gradeData.Count; i++)
        {
            var bar = _progressBars[i];
            var data = gradeData[i];

            var dataView = EnemyTypeDatabase.Instance.GetDefinition(data.grade);
            if (dataView)
            {
                bar.SetColor(dataView.color);
            }

            float targetFill = (data.total > 0)
                ? Mathf.Clamp01((float)data.dead / data.total)
                : 0f;

            bar.SetInstantFill(targetFill);
        }
    }

    private async UniTask UpdateProgressBarsAsync(List<GradeViewData> gradeData)
    {
        if (_progressBars.Count == 0)
        {
            return;
        }

        var token = this.GetCancellationTokenOnDestroy();

        // One bar per grade (including last grade -> trophy)
        for (int i = 0; i < _progressBars.Count && i < gradeData.Count; i++)
        {
            var bar = _progressBars[i];
            var data = gradeData[i];

            var dataView = EnemyTypeDatabase.Instance.GetDefinition(data.grade);
            if (dataView)
            {
                bar.SetColor(dataView.color);
            }

            float targetFill = (data.total > 0)
                ? Mathf.Clamp01((float)data.dead / data.total)
                : 0f;

            // Bars before current index: full
            if (i < _currentIndex)
            {
                bar.SetInstantFill(1f);
            }
            // Bar for the current grade: animate
            else if (i == _currentIndex)
            {
                if (targetFill <= bar.CurrentFill + 0.0001f)
                {
                    bar.SetInstantFill(targetFill);
                }
                else
                {
                    bool reachFull = targetFill >= 0.999f;

                    await bar.AnimateToAsync(
                        targetFill,
                        fillTweenDuration,
                        onCompleted: reachFull ? () => OnBarReachedFull(i) : null,
                        ct: token
                    );
                }
            }
            // Future bars: empty
            else
            {
                bar.SetInstantFill(0f);
            }
        }
    }

    /// <summary>
    /// Called when the bar for a grade reaches 100%.
    /// Only then do we advance to the next tracker visually.
    /// </summary>
    private void OnBarReachedFull(int barIndex)
    {
        Debug.LogWarning("OnBarReachedFull");
        if (barIndex != _currentIndex)
        {
            return;
        }


        Debug.LogWarning("OnBarReachedFull2");

        int currentTrackerIndex = barIndex;
        int nextTrackerIndex = barIndex + 1;

        if (currentTrackerIndex >= _orderedRows.Count - 1)
        {
            if (_trophyInstance != null)
            {
                _trophyInstance.SetState(UiLevelTrackerState.Completed);
            }
        }

        if (currentTrackerIndex >= 0 && currentTrackerIndex < _orderedRows.Count)
        {
            _orderedRows[currentTrackerIndex].SetState(UiLevelTrackerState.Completed);
        }

        if (nextTrackerIndex >= 0 && nextTrackerIndex < _orderedRows.Count)
        {
            _orderedRows[nextTrackerIndex].SetState(UiLevelTrackerState.Active);
        }

        _currentIndex = nextTrackerIndex;
    }

    // --------------------------------------------------
    // Pool + row helpers
    // --------------------------------------------------

    private void ClearAllRows()
    {
        foreach (var kvp in _rows)
        {
            if (kvp.Value != null)
            {
                ReleaseRow(kvp.Value);
            }
        }

        _rows.Clear();
        _orderedRows.Clear();
        _orderedGrades.Clear();
    }

    private void ClearAllBars()
    {
        foreach (var bar in _progressBars)
        {
            if (bar != null)
            {
                bar.gameObject.SetActive(false);
                bar.SetInstantFill(0f);
                _barPool.Push(bar);
            }
        }

        _progressBars.Clear();
    }

    private UiLevelTrackerElement GetRow()
    {
        UiLevelTrackerElement row;

        if (_pool.Count > 0)
        {
            row = _pool.Pop();
            if (row == null)
            {
                return InstantiateRow();
            }
        }
        else
        {
            row = InstantiateRow();
        }

        var t = row.transform as RectTransform;
        if (t != null && container != null)
        {
            t.SetParent(container, false);
            t.SetAsLastSibling();
        }

        row.gameObject.SetActive(true);
        return row;
    }

    private void ReleaseRow(UiLevelTrackerElement row)
    {
        if (row == null)
        {
            return;
        }

        if (container != null)
        {
            row.transform.SetParent(container, false);
            row.transform.gameObject.SetActive(false);
        }

        _pool.Push(row);
    }

    private UiLevelTrackerElement InstantiateRow()
    {
        if (rowPrefab == null || container == null)
        {
            Debug.LogWarning("LevelTrackerUiController: rowPrefab or container is not set.");
            return null;
        }

        return Instantiate(rowPrefab, container);
    }

    // --------------------------------------------------
    // Bar pool helpers
    // --------------------------------------------------

    private UiLevelProgressFillBar GetBar()
    {
        UiLevelProgressFillBar bar;

        if (_barPool.Count > 0)
        {
            bar = _barPool.Pop();
            if (bar == null)
            {
                return InstantiateBar();
            }
        }
        else
        {
            bar = InstantiateBar();
        }

        if (bar != null)
        {
            var t = bar.RectTransform;
            if (t != null && container != null)
            {
                t.SetParent(container, false);
                t.SetAsFirstSibling();
            }

            bar.gameObject.SetActive(true);
        }

        return bar;
    }

    private UiLevelProgressFillBar InstantiateBar()
    {
        if (progressBarPrefab == null || container == null)
        {
            Debug.LogWarning("LevelTrackerUiController: progressBarPrefab or container is not set.");
            return null;
        }

        return Instantiate(progressBarPrefab, container);
    }
}
