using UnityEngine;

[DisallowMultipleComponent]
public class UiLevelTrackerElement : MonoBehaviour
{
    [Header("Enemy Type")]
    [SerializeField] private EnemyGrade _grade;

    [Header("Label")]
    public UiLabelRef labelRef;

    public void SetGrade(EnemyGrade grade)
    {
        _grade = grade;
    }

    public void UpdateCounts(EnemyGrade type, int dead, int total)
    {
        if (labelRef == null || labelRef.Label == null) return;

        labelRef.Label.text = $"{type}: {dead} / {total}";
    }
}
