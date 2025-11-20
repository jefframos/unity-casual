#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
#if UNITY_2021_2_OR_NEWER
using UnityEditor.Overlays;
using UnityEngine.UIElements;
#endif

public static class ProgressionEditorTools
{
    public static void ClearProgressionInternal()
    {
        PlayerPrefs.DeleteKey(ProgressionManager.PLAYER_PREFS_KEY);
        PlayerPrefs.Save();
        Debug.Log("[ProgressionEditorTools] Cleared saved progression (levels, coins, gift).");
    }

    [MenuItem("Game/Progression/Clear Saved Progression")]
    public static void ClearProgressionMenu()
    {
        ClearProgressionInternal();
    }
}

#if UNITY_2021_2_OR_NEWER
[Overlay(typeof(SceneView), "Progression Tools")]
public class ProgressionOverlay : Overlay
{
    public override VisualElement CreatePanelContent()
    {
        var root = new VisualElement
        {
            style =
            {
                paddingLeft = 4,
                paddingRight = 4,
                paddingTop = 4,
                paddingBottom = 4
            }
        };

        var btn = new Button(() =>
        {
            if (EditorUtility.DisplayDialog(
                "Clear Saved Progression",
                "Clear saved progression (levels, coins, gift)?",
                "Yes", "Cancel"))
            {
                ProgressionEditorTools.ClearProgressionInternal();
            }
        })
        {
            text = "Clear Progression"
        };

        root.Add(btn);
        return root;
    }
}
#endif
#endif
