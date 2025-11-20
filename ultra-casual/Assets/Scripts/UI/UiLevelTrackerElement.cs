using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum UiLevelTrackerState
{
    Hidden,
    Inactive,
    Active,
    Completed
}

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
    public Transform disabledContainer;
    public Transform activeContainer;

    private UiLevelTrackerState currentState = UiLevelTrackerState.Inactive;

    public void SetGrade(EnemyGrade grade)
    {
        _enemyTypeDefinition = EnemyTypeDatabase.Instance.GetDefinition(grade);
        _grade = grade;
        backShape.color = _enemyTypeDefinition.color;
        icon.sprite = _enemyTypeDefinition.icon;
        transform.localScale = Vector3.one;
        //currentState = UiLevelTrackerState.Inactive;
    }

    public void UpdateCounts(EnemyGrade type, int dead, int total)
    {
        //if (labelRef == null || labelRef.Label == null) return;
        //labelRef.text = $"{dead} / {total}";
        labelRef.text = $"{total - dead}";

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

    // NEW: you can keep this empty or swap graphics based on state later
    public void SetState(UiLevelTrackerState state)
    {
        if (currentState == state)
        {
            return;
        }

        activeContainer.gameObject.SetActive(false);
        if (state == UiLevelTrackerState.Hidden)
        {
            disabledContainer.gameObject.SetActive(true);
            counterContainer.gameObject.SetActive(false);

        }
        else if (state == UiLevelTrackerState.Completed)
        {
            disabledContainer.gameObject.SetActive(false);
            activeContainer.gameObject.SetActive(true);
            // transform.DOKill();
            // transform.localScale = Vector3.one;
            // transform.DOScale(Vector3.one * 1.1f, 0.75f).SetEase(Ease.OutBack);
        }
        else if (state == UiLevelTrackerState.Active)
        {
            disabledContainer.gameObject.SetActive(false);
            transform.DOKill();
            transform.localScale = Vector3.one;
            transform.DOScale(Vector3.one * 0.75f, 0.75f).From().SetEase(Ease.OutBack);
        }
    }
}
