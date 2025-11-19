using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


[DisallowMultipleComponent]
public class TrophyTracker : MonoBehaviour
{
    public Transform trophyContainer;
    public Transform disabledContainer;


    // NEW: you can keep this empty or swap graphics based on state later
    public void SetState(UiLevelTrackerState state)
    {

        if (state == UiLevelTrackerState.Completed)
        {
            disabledContainer.gameObject.SetActive(false);
            trophyContainer.gameObject.SetActive(true);
            transform.DOKill();
            transform.localScale = Vector3.one;
            transform.DOScale(Vector3.one * 1.2f, 0.75f).SetEase(Ease.OutElastic);
        }
        else if (state == UiLevelTrackerState.Active)
        {
            disabledContainer.gameObject.SetActive(true);
            trophyContainer.gameObject.SetActive(false);
            transform.DOKill();
            transform.localScale = Vector3.one;
            transform.DOScale(Vector3.one * 1.1f, 0.75f).SetEase(Ease.OutBack);
        }

    }
}
