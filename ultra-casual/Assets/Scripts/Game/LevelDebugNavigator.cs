using UnityEngine;

/// <summary>
/// Simple in-game level navigator for debugging.
///
/// - Attach to any GameObject in your scene.
/// - Assign the Levels ScriptableObject in the inspector.
/// - During play, use the small window in the Game view to jump between levels.
/// </summary>
public class LevelDebugNavigator : MonoBehaviour
{
    [Header("Level Data")]
    public Levels levels;            // Assign your Levels asset here.

    [Header("UI Settings")]
    public bool showGui = true;
    public Rect windowRect = new Rect(10, 10, 280, 100);

    [Tooltip("Current level global index (0..TotalLevels-1).")]
    public int currentGlobalLevel = 0;

    private string _inputBuffer;
    private GameManager _cachedGameManager;

    private void OnEnable()
    {
        if (levels != null && levels.TotalLevels > 0)
        {
            currentGlobalLevel = Mathf.Clamp(currentGlobalLevel, 0, levels.TotalLevels - 1);
        }
        else
        {
            currentGlobalLevel = 0;
        }

        _inputBuffer = currentGlobalLevel.ToString();
    }

    private void OnGUI()
    {
        if (!showGui) return;

        if (levels == null)
        {
            windowRect = GUI.Window(GetInstanceID(), windowRect, DrawNoLevelsWindow, "Level Navigator");
        }
        else
        {
            windowRect = GUI.Window(GetInstanceID(), windowRect, DrawWindow, "Level Navigator");
        }
    }

    private void DrawNoLevelsWindow(int id)
    {
        GUI.DragWindow(new Rect(0, 0, 10000, 20));
        GUILayout.Label("Assign a Levels asset in the inspector.");
    }

    private void DrawWindow(int id)
    {
        GUI.DragWindow(new Rect(0, 0, 10000, 20));

        int maxIndex = Mathf.Max(0, levels.TotalLevels - 1);

        GUILayout.Label($"Global Level (0 .. {maxIndex})");

        GUILayout.BeginHorizontal();

        if (GUILayout.Button("-", GUILayout.Width(30)))
        {
            ChangeLevel(-1);
        }

        // Text field for direct level input
        _inputBuffer = GUILayout.TextField(_inputBuffer, GUILayout.Width(60));

        if (GUILayout.Button("+", GUILayout.Width(30)))
        {
            ChangeLevel(1);
        }

        if (GUILayout.Button("Go", GUILayout.Width(60)))
        {
            ApplyInputAndReload();
        }

        GUILayout.EndHorizontal();
    }

    private void ChangeLevel(int delta)
    {
        if (levels == null || levels.TotalLevels <= 0)
            return;

        int maxIndex = levels.TotalLevels - 1;

        currentGlobalLevel = Mathf.Clamp(currentGlobalLevel + delta, 0, maxIndex);
        _inputBuffer = currentGlobalLevel.ToString();

        //ReloadCurrentLevel();
    }

    private void ApplyInputAndReload()
    {
        if (levels == null || levels.TotalLevels <= 0)
            return;

        if (!int.TryParse(_inputBuffer, out int parsed))
        {
            // Reset to current valid value if parse fails
            _inputBuffer = currentGlobalLevel.ToString();
            return;
        }

        int maxIndex = levels.TotalLevels - 1;
        parsed = Mathf.Clamp(parsed, 0, maxIndex);

        currentGlobalLevel = parsed;
        _inputBuffer = currentGlobalLevel.ToString();

        ReloadCurrentLevel();
    }

    private void ReloadCurrentLevel()
    {
        if (levels == null || levels.TotalLevels <= 0)
            return;

        int maxIndex = levels.TotalLevels - 1;
        currentGlobalLevel = Mathf.Clamp(currentGlobalLevel, 0, maxIndex);

        if (_cachedGameManager == null)
        {
            _cachedGameManager = FindObjectOfType<GameManager>();
        }

        if (_cachedGameManager == null)
        {
            Debug.LogWarning("[LevelDebugNavigator] No GameManager found in scene.");
            return;
        }

        // Call your GameManager method with the global level id
        _cachedGameManager.reloadLevelByGlobalIdAsync(currentGlobalLevel);
        Debug.Log($"[LevelDebugNavigator] Reloading level with global id: {currentGlobalLevel}");
    }
}
