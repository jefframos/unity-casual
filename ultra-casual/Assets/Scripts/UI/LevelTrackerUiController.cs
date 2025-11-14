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
    public LevelTrackerMediator levelTrackerMediator;

    // grade -> active instantiated UI row
    private readonly Dictionary<EnemyGrade, UiLevelTrackerElement> _rows = new();

    // pooled (inactive) rows ready to be reused
    private readonly Stack<UiLevelTrackerElement> _pool = new();

    private void OnEnable()
    {
        if (levelTrackerMediator == null) return;

        levelTrackerMediator.OnStatsRebuilt += HandleStatsRebuilt;
        levelTrackerMediator.OnEnemyStatsChanged += HandleEnemyStatsChanged;

        levelTrackerMediator.RaiseStatsRebuilt();
    }

    private void OnDisable()
    {
        if (levelTrackerMediator != null)
        {
            levelTrackerMediator.OnStatsRebuilt -= HandleStatsRebuilt;
            levelTrackerMediator.OnEnemyStatsChanged -= HandleEnemyStatsChanged;
        }

        // We only clear active rows into the pool; pooled instances stay around.
        ClearAllRows();
    }

    private void HandleStatsRebuilt(List<LevelTrackerMediator.EnemyStatsSnapshot> stats)
    {
        ClearAllRows();

        if (container == null || rowPrefab == null) return;

        foreach (var snapshot in stats)
        {
            if (snapshot.total <= 0) continue; // no need to show empty grades, unless you want to

            var row = GetRow();
            row.SetGrade(snapshot.grade);
            row.UpdateCounts(snapshot.grade, snapshot.dead, snapshot.total);

            _rows[snapshot.grade] = row;
        }
    }

    private void HandleEnemyStatsChanged(EnemyGrade grade, int total, int dead)
    {
        if (container == null || rowPrefab == null) return;

        // If we don't have a row for this grade yet and there is at least one enemy, create (or reuse) it.
        if (!_rows.TryGetValue(grade, out var row) || row == null)
        {
            if (total <= 0) return;

            row = GetRow();
            row.SetGrade(grade);
            _rows[grade] = row;
        }

        // If total is now zero, return the row to the pool and remove it from active rows.
        if (total <= 0)
        {
            ReleaseRow(row);
            _rows.Remove(grade);
            return;
        }

        row.UpdateCounts(grade, dead, total);
    }

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
        if (row == null) return;

        row.gameObject.SetActive(false);

        // Optionally keep them under the same container, just inactive.
        // If you want a separate pool parent, assign it here instead.
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
