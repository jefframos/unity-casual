using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using Cysharp.Threading.Tasks;

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
    public RectTransform trophyPrefab;

    [Tooltip("Mediator that provides level snapshots.")]
    public LevelTrackerMediator levelTrackerMediator;

    [Header("Layout")]
    [Tooltip("Horizontal spacing between trackers (in local units).")]
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
    private RectTransform _trophyInstance;

    // which tracker index is currently "active" in progression terms
    private int _currentIndex = -1;

    // cache of the last grade data to avoid redundant rebuilds/animations
    private List<GradeViewData> _lastGradeData = new List<GradeViewData>();

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

        levelTrackerMediator.OnSnapshotUpdated += HandleSnapshotUpdated;
        levelTrackerMediator.OnTrackersRefreshed += HandleTrackersRefreshed;
        levelTrackerMediator.OnResetStarted += ResetLevel;

        // Ask mediator to push a snapshot so we can build initial UI
        levelTrackerMediator.ForceBroadcastSnapshot();
    }

    private void OnDisable()
    {
        if (levelTrackerMediator != null)
        {
            levelTrackerMediator.OnSnapshotUpdated -= HandleSnapshotUpdated;
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
        ClearAllRows();

        Debug.Log("ResetLevel");
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

        // 🔍 Detect if this looks like the *start of a fresh level*
        bool isFreshLevelStart = true;
        for (int i = 0; i < gradeData.Count; i++)
        {
            if (gradeData[i].dead > 0)
            {
                isFreshLevelStart = false;
                break;
            }
        }

        // 🔍 Early-out if nothing changed in data
        if (!HasGradeDataChanged(gradeData))
        {
            return;
        }

        // Cache the new data (make a copy)
        _lastGradeData = new List<GradeViewData>(gradeData);

        // From here on, we know there *was* a change
        ClearAllRows();

        // ✅ FIX: ensure current index is valid for this snapshot
        // - Reset if it was never set
        // - Reset if it came from a previous level and is out of range
        // - Reset if this snapshot looks like a brand-new level (all dead == 0)
        if (_currentIndex < 0 || _currentIndex >= gradeData.Count || isFreshLevelStart)
        {
            _currentIndex = 0;
        }

        // Create rows in order & set states
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
                // Future grades: hidden/locked look
                row.SetState(UiLevelTrackerState.Hidden);
            }
        }

        // Layout trackers horizontally, centered
        LayoutTrackers();

        // Create/update progress bars between trackers (including last -> trophy)
        BuildProgressBars();

        // Apply bar fill (with tween on the current bar)
        await UpdateProgressBarsAsync(gradeData);
    }


    // --------------------------------------------------
    // Layout + bars
    // --------------------------------------------------

    private void LayoutTrackers()
    {
        int count = _orderedRows.Count;
        if (count == 0) return;

        float totalWidth = (count - 1) * trackerSpacing;
        float startX = -totalWidth * 0.5f;

        for (int i = 0; i < count; i++)
        {
            var rt = _orderedRows[i].transform as RectTransform;
            if (rt == null) continue;

            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(startX + i * trackerSpacing, 0f);
            rt.localScale = Vector3.one;
        }
    }

    private void BuildProgressBars()
    {
        ClearAllBars();

        int count = _orderedRows.Count;
        if (count == 0) return;

        // Ensure / position trophy
        RectTransform trophyRt = null;
        if (trophyPrefab != null)
        {
            if (_trophyInstance == null)
            {
                _trophyInstance = Instantiate(trophyPrefab, container);
            }

            trophyRt = _trophyInstance;
            trophyRt.gameObject.SetActive(true);

            trophyRt.anchorMin = trophyRt.anchorMax = new Vector2(0.5f, 0.5f);
            trophyRt.pivot = new Vector2(0.5f, 0.5f);

            var lastRowRt = (RectTransform)_orderedRows[count - 1].transform;
            float trophyX = lastRowRt.anchoredPosition.x + trackerSpacing;
            trophyRt.anchoredPosition = new Vector2(trophyX, 0f);
            trophyRt.localScale = Vector3.one;
        }

        // One bar per grade:
        // bar i connects row[i] -> row[i+1] for i < count - 1
        // and row[last] -> trophy for i == count - 1 (if trophy exists)
        for (int i = 0; i < count; i++)
        {
            var start = (RectTransform)_orderedRows[i].transform;
            RectTransform end;

            if (i + 1 < count)
            {
                end = (RectTransform)_orderedRows[i + 1].transform;
            }
            else
            {
                // last grade -> trophy
                end = trophyRt;
            }

            if (end == null)
            {
                // No valid endpoint (e.g. no trophy assigned) -> skip last bar
                continue;
            }

            var bar = GetBar();
            if (bar == null) continue;

            var brt = bar.RectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.pivot = new Vector2(0.5f, 0.5f);

            float midX = (start.anchoredPosition.x + end.anchoredPosition.x) * 0.5f;
            brt.anchoredPosition = new Vector2(midX, 0f);
            brt.localScale = Vector3.one;

            // Adjust width based on distance between start and end
            float dx = Mathf.Abs(end.anchoredPosition.x - start.anchoredPosition.x);
            var size = brt.sizeDelta;
            size.x = dx * 0.7f; // tweak factor to taste
            brt.sizeDelta = size;

            // Bar is always visible; just reset the fill
            bar.SetInstantFill(0f);

            _progressBars.Add(bar);
        }
    }

    private async UniTask UpdateProgressBarsAsync(List<GradeViewData> gradeData)
    {
        if (_progressBars.Count == 0) return;

        var token = this.GetCancellationTokenOnDestroy();

        // One bar per grade (including last grade -> trophy)
        for (int i = 0; i < _progressBars.Count && i < gradeData.Count; i++)
        {
            var bar = _progressBars[i];
            var data = gradeData[i];

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
                    // no forward movement -> just clamp
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
        // Only advance if this bar is the current one
        if (barIndex != _currentIndex)
            return;

        int currentTrackerIndex = barIndex;
        int nextTrackerIndex = barIndex + 1;

        // Mark current tracker as completed
        if (currentTrackerIndex >= 0 && currentTrackerIndex < _orderedRows.Count)
        {
            _orderedRows[currentTrackerIndex].SetState(UiLevelTrackerState.Completed);
        }

        // If there is a next tracker, activate it.
        // If nextTrackerIndex == _orderedRows.Count, we're at the trophy: level fully completed.
        if (nextTrackerIndex >= 0 && nextTrackerIndex < _orderedRows.Count)
        {
            _orderedRows[nextTrackerIndex].SetState(UiLevelTrackerState.Active);
        }

        _currentIndex = nextTrackerIndex;
    }

    // --------------------------------------------------
    // Pool + row helpers
    // --------------------------------------------------

    /// <summary>
    /// Clears all active rows (returns them to the pool) and bars.
    /// Does NOT destroy pooled instances.
    /// </summary>
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

        ClearAllBars();

        if (_trophyInstance != null)
        {
            _trophyInstance.gameObject.SetActive(false);
        }
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
                // In case something external destroyed the pooled object.
                return InstantiateRow();
            }
        }
        else
        {
            row = InstantiateRow();
        }

        // Ensure it is correctly parented and active
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

        row.gameObject.SetActive(false);

        // Keep them under the same container, just inactive.
        if (container != null)
        {
            row.transform.SetParent(container, false);
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
