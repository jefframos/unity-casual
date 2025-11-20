using UnityEngine;
using TMPro;

[DisallowMultipleComponent]
public class CurrentLevelHook : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("TextMeshProUGUI where the level will be displayed. If left null, the component will try to use the one on this GameObject.")]
    public TextMeshProUGUI targetLevelLabel;
    public TextMeshProUGUI targetCoinLabel;

    [Header("Formatting")]
    [Tooltip("Text shown before the level number.")]
    public string prefix = "Level ";
    [Tooltip("Text shown after the level number.")]
    public string suffix = "";


    private void OnEnable()
    {
        if (ProgressionManager.Instance != null)
        {
            ProgressionManager.Instance.OnCoinChange += HandleCoinChange;
            ProgressionManager.Instance.OnLevelChange += HandleLevelChange;
        }

        Refresh();
    }

    private void OnDisable()
    {
        if (ProgressionManager.Instance != null)
        {
            ProgressionManager.Instance.OnCoinChange -= HandleCoinChange;
            ProgressionManager.Instance.OnLevelChange -= HandleLevelChange;
        }
    }

    private void HandleCoinChange(int oldCoins, int newCoins)
    {
        Refresh();
    }

    private void HandleLevelChange(int newDisplayLevel)
    {
        Refresh();
    }


    /// <summary>
    /// Force refresh level text (you can also call this from events).
    /// </summary>
    public void Refresh()
    {


        int displayLevel = 1;
        int displayCoins = 0;

        var progression = ProgressionManager.Instance;
        if (progression != null)
        {
            // If you added GetDisplayLevelNumber() you can swap this line to that.
            displayLevel = progression.CurrentGlobalLevelIndex + 1;
            displayCoins = progression.Coins;
        }

        targetLevelLabel.text = prefix + displayLevel.ToString() + suffix;
        targetCoinLabel.text = displayCoins.ToString();
    }
}
