using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

[DisallowMultipleComponent]
public class UpgradeCardView : MonoBehaviour
{
    [Header("UI Refs (Prefab)")]
    public Image iconImage;
    public TMP_Text nameText;
    public TMP_Text costText;
    public TMP_Text levelText;               // shows "Level X"
    public TMP_Text valueText;               // optional: shows current value
    public Button minusButton;
    public Button plusButton;

    [Tooltip("Five small images to represent step progression (e.g., 5 pips).")]
    public Image[] stepPips = new Image[5];

    [Header("Colors")]
    public Color pipOnColor = Color.green;
    public Color pipOffColor = Color.red;

    [Header("Display")]
    [Tooltip("How many sub-steps per visual 'Level X' (UI only).")]
    public int stepsPerLevelForUI = 5;

    private UpgradeSystem _sys;
    private UpgradeDefinition _def;
    private Action _onChanged;

    public void Setup(UpgradeSystem sys, UpgradeDefinition def, Action onChanged)
    {
        _sys = sys;
        _def = def;
        _onChanged = onChanged;

        if (_def != null)
        {
            // Prefer step icon if available for the CURRENT level; else fallback to def icon in Refresh()
            if (nameText) nameText.text = GetDisplayNameForCurrentStep();
        }

        if (minusButton)
        {
            minusButton.onClick.RemoveAllListeners();
            minusButton.onClick.AddListener(() =>
            {
                if (_sys == null || _def == null) return;
                _sys.DecreaseLevel(_def.type);
                Refresh();
                _onChanged?.Invoke();
            });
        }

        if (plusButton)
        {
            plusButton.onClick.RemoveAllListeners();
            plusButton.onClick.AddListener(() =>
            {
                if (_sys == null || _def == null) return;
                // normal upgrade (respects cost). For debug “free” path, call IncreaseLevelForce from a different UI.
                _sys.TryUpgrade(_def.type);
                Refresh();
                _onChanged?.Invoke();
            });
        }

        Refresh();
    }

    public void Refresh()
    {
        if (_sys == null || _def == null) return;

        int level = _sys.GetLevel(_def.type);
        float value = _def.GetValueForLevel(level);
        int nextCost = _def.GetCostForLevel(level);

        // Icon: prefer current step icon; else def icon
        UpdateIconForCurrentStep(level);

        // Name: prefer current step name; else enum name
        if (nameText) nameText.text = GetDisplayNameForCurrentStep();

        // Value & Cost
        if (valueText) valueText.text = $"{value:0.###}";
        if (costText) costText.text = $"{nextCost}";

        // Level label: UI representation (e.g., 5 sub-steps per 'Level X')
        if (levelText)
        {
            int major = (stepsPerLevelForUI > 0) ? (level / stepsPerLevelForUI) + 1 : (level + 1);
            levelText.text = $"{major}";
        }

        // Step pips
        UpdatePips(level);
    }

    private string GetDisplayNameForCurrentStep()
    {
        if (_def == null) return string.Empty;

        int level = _sys != null ? _sys.GetLevel(_def.type) : 0;
        UpgradeStepData parentStep;
        _def.GetLevelData(level, out parentStep);

        if (parentStep != null && !string.IsNullOrEmpty(parentStep.name))
            return parentStep.name;

        return _def.type.ToString();
    }

    private void UpdateIconForCurrentStep(int level)
    {
        if (iconImage == null) return;

        UpgradeStepData parentStep;
        _def.GetLevelData(level, out parentStep);

        if (parentStep != null && parentStep.icon != null)
        {
            iconImage.sprite = parentStep.icon;
            iconImage.enabled = true;
            return;
        }

        if (_def.icon != null)
        {
            iconImage.sprite = _def.icon;
            iconImage.enabled = true;
        }
        else
        {
            iconImage.enabled = false;
        }
    }

    /// <summary>
    /// Colors 5 pips based on sub-step progress. By default we visualize 5 sub-steps per UI level.
    /// If your actual data has a different number of levels in the current step, we map its fractional progress into 5 pips.
    /// Example: level 1.4 → 4 green, 1 red.
    /// </summary>
    private void UpdatePips(int globalLevel)
    {
        if (stepPips == null || stepPips.Length == 0) return;

        // Determine local progress inside the current "tier step" (data-defined)
        int localIndex = 0;          // 0-based within the parentStep
        int localCount = 1;          // how many entries in this parentStep
        {
            int traversed = 0;
            for (int s = 0; s < _def.steps.Length; s++)
            {
                var step = _def.steps[s];
                if (step == null || step.levels == null) continue;

                int len = step.levels.Length;
                if (globalLevel < traversed + len)
                {
                    localIndex = globalLevel - traversed;
                    localCount = Mathf.Max(1, len);
                    break;
                }
                traversed += len;
            }
        }

        // Map local progress (0..localCount-1) → how many of the fixed pips should light up
        int litCount;
        if (localCount <= 1)
        {
            litCount = (globalLevel > 0) ? stepPips.Length : 0;
        }
        else
        {
            // fraction of progress inclusive of current index (e.g., if localIndex=3 of 5 -> 4/5)
            float frac = Mathf.Clamp01((localIndex + 1) / (float)localCount);
            litCount = Mathf.Clamp(Mathf.RoundToInt(frac * stepPips.Length), 0, stepPips.Length);
        }

        for (int i = 0; i < stepPips.Length; i++)
        {
            if (!stepPips[i]) continue;
            stepPips[i].color = (i < litCount) ? pipOnColor : pipOffColor;
        }
    }
}
