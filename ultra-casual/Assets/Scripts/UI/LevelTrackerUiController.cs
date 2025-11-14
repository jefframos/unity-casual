using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure UI: subscribes to LevelTrackerMediator and builds/updates labels.
/// </summary>
public class LevelTrackerUiController : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("Where to put the instantiated enemy rows.")]
    public RectTransform container;

    [Tooltip("Prefab with UiLevelTrackerElement + UiLabelRef.")]
    public UiLevelTrackerElement rowPrefab;

    // grade -> instantiated UI row
    private readonly Dictionary<EnemyGrade, UiLevelTrackerElement> _rows = new();

    private void OnEnable()
    {
        var mediator = LevelTrackerMediator.Instance;
        if (mediator == null) return;

        mediator.OnStatsRebuilt += HandleStatsRebuilt;
        mediator.OnEnemyStatsChanged += HandleEnemyStatsChanged;

        mediator.RaiseStatsRebuilt();
    }

    private void OnDisable()
    {
        var mediator = LevelTrackerMediator.Instance;
        if (mediator != null)
        {
            mediator.OnStatsRebuilt -= HandleStatsRebuilt;
            mediator.OnEnemyStatsChanged -= HandleEnemyStatsChanged;
        }

        ClearAllRows();
    }

    private void HandleStatsRebuilt(List<LevelTrackerMediator.EnemyStatsSnapshot> stats)
    {
        ClearAllRows();

        Debug.Log("THIS");

        if (container == null || rowPrefab == null) return;

        foreach (var snapshot in stats)
        {
            if (snapshot.total <= 0) continue; // no need to show empty grades, unless you want to

            var row = Instantiate(rowPrefab, container);
            row.SetGrade(snapshot.grade);
            row.UpdateCounts(snapshot.grade, snapshot.dead, snapshot.total);

            _rows[snapshot.grade] = row;
        }
    }

    private void HandleEnemyStatsChanged(EnemyGrade grade, int total, int dead)
    {
        if (container == null || rowPrefab == null) return;

        // If we don't have a row for this grade yet and there is at least one enemy, create it.
        if (!_rows.TryGetValue(grade, out var row) || row == null)
        {
            if (total <= 0) return;

            row = Instantiate(rowPrefab, container);
            row.SetGrade(grade);
            _rows[grade] = row;
        }

        // If total is now zero, you *could* destroy the row, but that shouldn't really happen in your flow.
        row.UpdateCounts(grade, dead, total);
    }

    private void ClearAllRows()
    {
        foreach (var kvp in _rows)
        {
            if (kvp.Value != null)
            {
                Destroy(kvp.Value.gameObject);
            }
        }
        _rows.Clear();

        if (container != null)
        {
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                Destroy(container.GetChild(i).gameObject);
            }
        }
    }
}
