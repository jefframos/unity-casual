#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class ProgressionEditorTools
{
    [MenuItem("Game/Progression/Clear Saved Progression")]
    public static void ClearProgression()
    {
        PlayerPrefs.DeleteKey(ProgressionManager.PLAYER_PREFS_KEY);
        PlayerPrefs.Save();
        Debug.Log("[ProgressionEditorTools] Cleared saved progression (levels, coins, gift).");
    }
}
#endif
