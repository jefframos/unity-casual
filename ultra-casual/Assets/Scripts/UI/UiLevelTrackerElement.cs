using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class UiLevelTrackerElement : MonoBehaviour
{
    [Header("Enemy Type")]
    [SerializeField] private EnemyGrade _grade;
    private EnemyTypeDefinition _enemyTypeDefinition;

    [Header("Label")]
    public TextMeshProUGUI labelRef;
    public Image backShape;
    public Image icon;
    public Image checker;
    public Transform counterContainer;

    public void SetGrade(EnemyGrade grade)
    {
        _enemyTypeDefinition = EnemyTypeDatabase.Instance.GetDefinition(grade);
        _grade = grade;
        backShape.color = _enemyTypeDefinition.color;
        icon.sprite = _enemyTypeDefinition.icon;
    }

    public void UpdateCounts(EnemyGrade type, int dead, int total)
    {
        //if (labelRef == null || labelRef.Label == null) return;
        labelRef.text = $"{dead} / {total}";

        if (dead == total)
        {
            checker.enabled = true;
            counterContainer.gameObject.SetActive(false);
        }
        else
        {
            checker.enabled = false;
            counterContainer.gameObject.SetActive(true);

        }
    }
}
