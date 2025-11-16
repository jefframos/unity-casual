using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure UI: subscribes to LevelTrackerMediator and builds/updates labels.
/// Uses simple object pooling for the UI rows.
/// </summary>
public class LevelTrackerUiController : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("Where to put the instantiated enemy rows.")]
    public RectTransform container;

    [Tooltip("Prefab with UiLevelTrackerElement + UiLabelRef.")]
    public UiLevelTrackerElement rowPrefab;

    [Tooltip("Mediator that provides level snapshots.")]
    public LevelTrackerMediator levelTrackerMediator;

    // grade -> active instantiated UI row
    private readonly Dictionary<EnemyGrade, UiLevelTrackerElement> _rows = new();

    // pooled (inactive) rows ready to be reused
    private readonly Stack<UiLevelTrackerElement> _pool = new();

    private void OnEnable()
    {
        if (levelTrackerMediator == null)
        {
            return;
        }

        levelTrackerMediator.OnSnapshotUpdated += HandleSnapshotUpdated;
        levelTrackerMediator.OnTrackersRefreshed += HandleTrackersRefreshed;

        // Ask mediator to push a snapshot so we can build initial UI
        levelTrackerMediator.ForceBroadcastSnapshot();
    }

    private void OnDisable()
    {
        if (levelTrackerMediator != null)
        {
            levelTrackerMediator.OnSnapshotUpdated -= HandleSnapshotUpdated;
            levelTrackerMediator.OnTrackersRefreshed -= HandleTrackersRefreshed;
        }

        // We only clear active rows into the pool; pooled instances stay around.
        ClearAllRows();
    }

    // --------------------------------------------------
    // Handlers
    // --------------------------------------------------

    private void HandleTrackersRefreshed()
    {
        // New level / trackers: clear current UI
        ClearAllRows();
    }

    private void HandleSnapshotUpdated(LevelEnemyTracker.LevelSnapshot snapshot)
    {
        ClearAllRows();

        if (container == null || rowPrefab == null)
        {
            return;
        }

        if (snapshot == null || snapshot.grades == null)
        {
            return;
        }

        foreach (var gradeSnapshot in snapshot.grades)
        {
            if (gradeSnapshot == null)
            {
                continue;
            }

            // Don’t show empty grades unless you want that
            if (gradeSnapshot.total <= 0)
            {
                continue;
            }

            var row = GetRow();
            row.SetGrade(gradeSnapshot.grade);
            row.UpdateCounts(gradeSnapshot.grade, gradeSnapshot.dead, gradeSnapshot.total);

            _rows[gradeSnapshot.grade] = row;

            // If you want to visually mark current grade or cleared grades,
            // you could add calls here (e.g. row.SetCurrent(gradeSnapshot.isCurrent))
        }
    }

    // --------------------------------------------------
    // Pool + row helpers
    // --------------------------------------------------

    /// <summary>
    /// Clears all active rows (returns them to the pool).
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
    }

    /// <summary>
    /// Gets a row from the pool or instantiates a new one if pool is empty.
    /// </summary>
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

    /// <summary>
    /// Returns a row to the pool (disables it).
    /// </summary>
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
}
