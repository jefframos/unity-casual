#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GridPiler))]
public class GridPilerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var piler = (GridPiler)target;

        EditorGUILayout.Space(8);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Generate", GUILayout.Height(28)))
            {
                piler.Generate();
            }

            if (GUILayout.Button("Clear Generated", GUILayout.Height(28)))
            {
                piler.ClearGenerated();
            }
        }

        EditorGUILayout.HelpBox(
            "Pattern is Columns across X and Rows stacked up along Y.\n" +
            "Set a bottom Y offset, optional XZ offset, and choose auto-spacing (from prefab bounds) or manual spacing.\n" +
            "Instances are placed under a child group named by 'groupName' so you can cleanly clear/regenerate.",
            MessageType.Info
        );
    }
}
#endif
