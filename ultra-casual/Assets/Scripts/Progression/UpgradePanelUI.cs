using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

[DisallowMultipleComponent]
public class UpgradePanelUI : MonoBehaviour
{
    [Header("Refs")]
    public UpgradeSystem upgradeSystem;           // if null, will auto-find Instance
    public UpgradeCardView upgradeCardPrefab;     // your card prefab
    public Transform cardsParent;                 // grid/vertical layout content
    public Button addCoinsButton;
    public TMP_Text coinsLabel;

    [Header("Settings")]
    [Tooltip("Default duration (seconds) for the coin count animation to reach the target.")]
    public float coinFillDuration = 2f;

    [Tooltip("How many coins per frame (auto-calculated when value changes).")]
    public int defaultStepPerTick = 25;

    [Tooltip("Optional: coins added when pressing the add button.")]
    public int addCoinsAmount = 500;

    private readonly List<UpgradeCardView> _cards = new();

    private int _displayedCoins;             // what UI shows
    private int _lastSystemCoins;            // last known system coin value
    private int? _stepOverridePerTick;       // optional temporary override for speed
    private float _targetStepPerTick;        // recalculated when values change

    private void Awake()
    {
        if (upgradeSystem == null)
            upgradeSystem = UpgradeSystem.Instance;
    }

    private void Start()
    {
        BuildCards();
        WireButtons();

        _lastSystemCoins = upgradeSystem != null ? upgradeSystem.coins : 0;
        _displayedCoins = _lastSystemCoins;
        UpdateCoinsLabel();

        upgradeSystem.OnUpgradePurchased += OnUpgradePurchased;
        upgradeSystem.OnReachedMaxLevel += OnReachedMaxLevel;
        upgradeSystem.OnReachedNextStep += OnReachedNextStep;
    }

    private void OnUpgradePurchased(UpgradeType type, int arg2, int arg3)
    {
        Debug.Log("OnUpgradePurchased" + type);
    }

    private void OnReachedNextStep(UpgradeType type, int arg2)
    {
        Debug.Log("OnReachedNextStep" + type);
    }

    private void OnReachedMaxLevel(UpgradeType type, int arg2)
    {
        Debug.Log("OnReachedMaxLevel" + type);
    }

    private void Update()
    {
        if (!upgradeSystem || !coinsLabel) return;

        int target = upgradeSystem.coins;

        // If equal: nothing to do
        if (_displayedCoins == target)
        {
            _lastSystemCoins = target;
            _stepOverridePerTick = null;
            return;
        }

        // Detect a fresh change from last system value (new target)
        if (target != _lastSystemCoins)
        {
            _lastSystemCoins = target;

            // Recalculate step so that it fills in ~2 seconds
            float diff = Mathf.Abs(target - _displayedCoins);
            if (coinFillDuration > 0f)
            {
                // steps per frame assuming 60fps average
                float frames = coinFillDuration / Time.unscaledDeltaTime;
                _targetStepPerTick = Mathf.Max(1f, diff / frames);
            }
            else
            {
                _targetStepPerTick = defaultStepPerTick;
            }
        }

        // Use override if set, otherwise dynamic target step
        int step = Mathf.Max(1, _stepOverridePerTick ?? Mathf.RoundToInt(_targetStepPerTick));

        // Animate toward target
        if (_displayedCoins < target)
            _displayedCoins = Mathf.Min(_displayedCoins + step, target);
        else
            _displayedCoins = Mathf.Max(_displayedCoins - step, target);

        UpdateCoinsLabel();

        // Reached target: clear override
        if (_displayedCoins == target)
            _stepOverridePerTick = null;
    }

    private void WireButtons()
    {
        if (addCoinsButton != null)
        {
            addCoinsButton.onClick.AddListener(() =>
            {
                if (upgradeSystem == null) return;
                upgradeSystem.AddCoins(addCoinsAmount);
                RefreshCoins();
                RefreshAllCards();
            });
        }
    }

    private void BuildCards()
    {
        if (upgradeSystem == null || upgradeCardPrefab == null || cardsParent == null) return;

        // Clear old
        for (int i = cardsParent.childCount - 1; i >= 0; i--)
            Destroy(cardsParent.GetChild(i).gameObject);
        _cards.Clear();

        var defs = upgradeSystem.upgradeDefinitions;
        if (defs == null || defs.Count == 0) return;

        foreach (var def in defs)
        {
            if (!def) continue;

            var card = Instantiate(upgradeCardPrefab, cardsParent);
            card.Setup(upgradeSystem, def, OnCardChanged);
            _cards.Add(card);
        }
    }

    private void OnCardChanged()
    {
        RefreshCoins();
        RefreshAllCards();
    }

    private void RefreshAllCards()
    {
        foreach (var c in _cards) c.Refresh();
    }

    /// <summary>
    /// Optionally set a custom per-frame step for the next convergence.
    /// If not provided, the step is auto-calculated for ~2 seconds to reach the target.
    /// </summary>
    public void RefreshCoins(int? stepPerTick = null)
    {
        if (stepPerTick.HasValue)
            _stepOverridePerTick = Mathf.Max(1, stepPerTick.Value);
    }

    private void UpdateCoinsLabel()
    {
        coinsLabel.text = $"{_displayedCoins}";
    }
}
