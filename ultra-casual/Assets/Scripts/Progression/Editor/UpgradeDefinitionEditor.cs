#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(UpgradeDefinition))]
public class UpgradeDefinitionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(10);

        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("Batch Tools", EditorStyles.boldLabel);

            if (GUILayout.Button("Apply Global Add Per Index → Level addValue"))
            {
                var def = (UpgradeDefinition)target;

                // Register the root asset for undo (covers nested serializable fields)
                Undo.RegisterCompleteObjectUndo(def, "Apply Global Add Per Index");
                def.ApplyGlobalAddsToLevels();

                // Mark only the root ScriptableObject as dirty
                EditorUtility.SetDirty(def);
                AssetDatabase.SaveAssets();
            }

            if (GUILayout.Button("Revert ALL Level addValue to 1"))
            {
                var def = (UpgradeDefinition)target;

                Undo.RegisterCompleteObjectUndo(def, "Revert addValue to 1");
                def.RevertAllAddValuesToOne();

                EditorUtility.SetDirty(def);
                AssetDatabase.SaveAssets();
            }
        }

        EditorGUILayout.Space(10);
        DrawPreviewTable((UpgradeDefinition)target);
    }

    private void DrawPreviewTable(UpgradeDefinition def)
    {
        if (def.steps == null || def.steps.Length == 0)
        {
            EditorGUILayout.HelpBox("No steps defined.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField("💡 Upgrade Preview", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical("box");

        GUIStyle header = new(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft };
        GUIStyle row = new(EditorStyles.label) { alignment = TextAnchor.MiddleLeft };

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Global", header, GUILayout.Width(55));
        EditorGUILayout.LabelField("Step", header, GUILayout.Width(100));
        EditorGUILayout.LabelField("Level", header, GUILayout.Width(50));
        EditorGUILayout.LabelField("Cost", header, GUILayout.Width(80));
        EditorGUILayout.LabelField("Value", header, GUILayout.Width(90));
        EditorGUILayout.LabelField("Added", header, GUILayout.Width(60));
        EditorGUILayout.LabelField("gAdd/Idx", header, GUILayout.Width(70));
        EditorGUILayout.EndHorizontal();

        int global = 0;
        foreach (var step in def.steps)
        {
            if (step == null || step.levels == null) continue;

            for (int i = 0; i < step.levels.Length; i++)
            {
                int cost = def.GetCostForLevel(global);
                float val = def.GetValueForLevel(global);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField((global + 1).ToString(), row, GUILayout.Width(55));
                EditorGUILayout.LabelField(step.name, row, GUILayout.Width(100));
                EditorGUILayout.LabelField((i + 1).ToString(), row, GUILayout.Width(50));
                EditorGUILayout.LabelField(cost.ToString(), row, GUILayout.Width(80));
                EditorGUILayout.LabelField(val.ToString("0.###"), row, GUILayout.Width(90));
                EditorGUILayout.LabelField("+" + step.addedCost, row, GUILayout.Width(60));
                EditorGUILayout.LabelField(step.globalAddPerIndex.ToString("0.###"), row, GUILayout.Width(70));
                EditorGUILayout.EndHorizontal();

                global++;
            }
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.LabelField($"Total Levels: {def.TotalLevels}");
    }
}
#endif
