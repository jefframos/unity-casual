using System;
using UnityEngine;

[DisallowMultipleComponent]
public class ProgressionManager : MonoBehaviour
{
    // Bump this if you ever change the save format.
    public const string PLAYER_PREFS_KEY = "MyGame_Progression_v1";

    public static ProgressionManager Instance { get; private set; }

    // ------------------------
    // Events
    // ------------------------

    /// <summary>
    /// oldCoins, newCoins
    /// </summary>
    public event Action<int, int> OnCoinChange;

    /// <summary>
    /// newDisplayLevel (1-based)
    /// </summary>
    public event Action<int> OnLevelChange;

    [Header("References")]
    [Tooltip("Levels asset so we know how many levels exist.")]
    public Levels levels;

    [Header("Gift Settings")]
    [Tooltip("How much the gift bar fills per completed level (0..1).")]
    [Range(0.01f, 1.0f)]
    public float giftFillPerLevel = 0.25f;

    [Header("Runtime State (read-only)")]
    [SerializeField]
    private int _currentGlobalLevelIndex = 0;

    [SerializeField]
    private int _maxUnlockedGlobalLevelIndex = 0;

    [SerializeField]
    private int _coins = 0;

    [SerializeField, Range(0f, 1f)]
    private float _giftFill = 0f;

    [SerializeField]
    private int _giftCompletedCount = 0;

    public int CurrentGlobalLevelIndex
    {
        get { return _currentGlobalLevelIndex; }
    }

    public int MaxUnlockedGlobalLevelIndex
    {
        get { return _maxUnlockedGlobalLevelIndex; }
    }

    public int Coins
    {
        get { return _coins; }
    }

    public float GiftFill
    {
        get { return _giftFill; }
    }

    public int GiftCompletedCount
    {
        get { return _giftCompletedCount; }
    }

    public bool HasNextLevel
    {
        get
        {
            int total = GetTotalLevels();
            if (total <= 0)
            {
                return false;
            }

            return _currentGlobalLevelIndex < total - 1;
        }
    }

    [Serializable]
    private class ProgressionSaveData
    {
        public int currentGlobalLevelIndex;
        public int maxUnlockedGlobalLevelIndex;
        public int coins;
        public float giftFill;
        public int giftCompletedCount;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        LoadFromPrefs();
        ClampToLevels();
    }

    // ------------------------
    // Public API
    // ------------------------

    public int GetDisplayLevelNumber()
    {
        // 1-based for UI
        return _currentGlobalLevelIndex + 1;
    }

    public int GetNextDisplayLevelNumber()
    {
        return _currentGlobalLevelIndex + 2;
    }

    public int GetNextGlobalLevelIndex()
    {
        int total = GetTotalLevels();
        if (total <= 0)
        {
            return 0;
        }

        int next = _currentGlobalLevelIndex + 1;
        if (next >= total)
        {
            next = total - 1;
        }

        return next;
    }

    public void AddCoins(int amount)
    {
        int oldCoins = _coins;

        _coins += amount;
        if (_coins < 0)
        {
            _coins = 0;
        }

        ApplyCoinChange(oldCoins);
        SaveToPrefs();
    }

    public bool TrySpendCoins(int amount)
    {
        if (amount < 0)
        {
            return false;
        }

        if (_coins < amount)
        {
            return false;
        }

        int oldCoins = _coins;

        _coins -= amount;
        if (_coins < 0)
        {
            _coins = 0;
        }

        ApplyCoinChange(oldCoins);
        SaveToPrefs();
        return true;
    }

    /// <summary>
    /// Call this when a level is completed successfully.
    /// Returns whether a gift just became full.
    /// </summary>
    public bool OnLevelCompleted()
    {
        int oldLevelIndex = _currentGlobalLevelIndex;

        // Advance level
        int total = GetTotalLevels();
        if (total > 0)
        {
            if (_currentGlobalLevelIndex < total - 1)
            {
                _currentGlobalLevelIndex++;
            }

            if (_currentGlobalLevelIndex > _maxUnlockedGlobalLevelIndex)
            {
                _maxUnlockedGlobalLevelIndex = _currentGlobalLevelIndex;
            }
        }

        ApplyLevelChange(oldLevelIndex);

        // Gift fill logic
        bool giftJustCompleted = false;

        _giftFill += giftFillPerLevel;
        if (_giftFill >= 1f)
        {
            _giftFill -= 1f;
            _giftCompletedCount++;
            giftJustCompleted = true;
        }

        SaveToPrefs();
        return giftJustCompleted;
    }

    /// <summary>
    /// Use this if you want to manually set which level to load (debug).
    /// </summary>
    public void SetCurrentGlobalLevelIndex(int index)
    {
        int oldLevelIndex = _currentGlobalLevelIndex;

        int total = GetTotalLevels();
        if (total <= 0)
        {
            _currentGlobalLevelIndex = 0;
        }
        else
        {
            _currentGlobalLevelIndex = Mathf.Clamp(index, 0, total - 1);
        }

        if (_currentGlobalLevelIndex > _maxUnlockedGlobalLevelIndex)
        {
            _maxUnlockedGlobalLevelIndex = _currentGlobalLevelIndex;
        }

        ApplyLevelChange(oldLevelIndex);
        SaveToPrefs();
    }

    public void ResetProgression()
    {
        int oldLevelIndex = _currentGlobalLevelIndex;
        int oldCoins = _coins;

        _currentGlobalLevelIndex = 0;
        _maxUnlockedGlobalLevelIndex = 0;
        _coins = 0;
        _giftFill = 0f;
        _giftCompletedCount = 0;

        ApplyCoinChange(oldCoins);
        ApplyLevelChange(oldLevelIndex);

        SaveToPrefs();
    }

    // ------------------------
    // Persistence
    // ------------------------

    private void SaveToPrefs()
    {
        ProgressionSaveData data = new ProgressionSaveData
        {
            currentGlobalLevelIndex = _currentGlobalLevelIndex,
            maxUnlockedGlobalLevelIndex = _maxUnlockedGlobalLevelIndex,
            coins = _coins,
            giftFill = _giftFill,
            giftCompletedCount = _giftCompletedCount
        };

        string json = JsonUtility.ToJson(data);
        PlayerPrefs.SetString(PLAYER_PREFS_KEY, json);
        PlayerPrefs.Save();
    }

    private void LoadFromPrefs()
    {
        string json = PlayerPrefs.GetString(PLAYER_PREFS_KEY, string.Empty);
        if (string.IsNullOrEmpty(json))
        {
            // first run: default values already set
            return;
        }

        try
        {
            ProgressionSaveData data = JsonUtility.FromJson<ProgressionSaveData>(json);
            if (data != null)
            {
                _currentGlobalLevelIndex = data.currentGlobalLevelIndex;
                _maxUnlockedGlobalLevelIndex = data.maxUnlockedGlobalLevelIndex;
                _coins = data.coins;
                _giftFill = data.giftFill;
                _giftCompletedCount = data.giftCompletedCount;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ProgressionManager] Failed to parse save data, resetting. {e}");
            ResetProgression();
        }

        ClampToLevels();
    }

    private int GetTotalLevels()
    {
        if (levels == null)
        {
            return 0;
        }

        return levels.TotalLevels;
    }

    private void ClampToLevels()
    {
        int total = GetTotalLevels();
        if (total <= 0)
        {
            _currentGlobalLevelIndex = 0;
            _maxUnlockedGlobalLevelIndex = 0;
            return;
        }

        _currentGlobalLevelIndex = Mathf.Clamp(_currentGlobalLevelIndex, 0, total - 1);
        _maxUnlockedGlobalLevelIndex = Mathf.Clamp(_maxUnlockedGlobalLevelIndex, 0, total - 1);

        if (_currentGlobalLevelIndex > _maxUnlockedGlobalLevelIndex)
        {
            _currentGlobalLevelIndex = _maxUnlockedGlobalLevelIndex;
        }
    }

    internal void SetCoins(int value)
    {
        int oldCoins = _coins;

        if (value < 0)
        {
            value = 0;
        }

        _coins = value;

        ApplyCoinChange(oldCoins);
        SaveToPrefs();
    }

    // ------------------------
    // Helpers to raise events
    // ------------------------

    private void ApplyCoinChange(int oldCoins)
    {
        if (_coins != oldCoins)
        {
            OnCoinChange?.Invoke(oldCoins, _coins);
        }
    }

    private void ApplyLevelChange(int oldLevelIndex)
    {
        if (_currentGlobalLevelIndex != oldLevelIndex)
        {
            // Send 1-based level for UI
            OnLevelChange?.Invoke(GetDisplayLevelNumber());
        }
    }
}
