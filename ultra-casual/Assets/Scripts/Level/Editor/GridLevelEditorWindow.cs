using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class GridLevelEditorWindow : EditorWindow
{
    private const float LEFT_PANE_WIDTH = 280f;
    private const float GRID_MARGIN = 80f;
    private const float CELL_DRAW_SIZE = 32f;

    private LevelEditorSettings _settings;
    private LevelGridData _level;
    private Vector2 _scrollPalette;
    private Vector2 _scrollGrid;

    private int _selectedPaletteIndex = -1;

    // cell -> item for overlap/erase
    private readonly Dictionary<Vector2Int, LevelGridData.PlacedItem> _cellToItem =
        new Dictionary<Vector2Int, LevelGridData.PlacedItem>();

    [MenuItem("Tools/Grid Level Editor")]
    public static void Open()
    {
        var w = GetWindow<GridLevelEditorWindow>("Grid Level Editor");
        w.minSize = new Vector2(900, 500);
        w.Show();
    }

    private void OnEnable()
    {
        LoadOrCreateSettings();
        RebuildOccupancy();
    }

    private void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawLeftPane();
            DrawRightPane();
        }
    }

    // ------------------- Settings -------------------
    private void LoadOrCreateSettings()
    {
        if (_settings != null) return;

        string[] guids = AssetDatabase.FindAssets("t:LevelEditorSettings");
        if (guids.Length > 0)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            _settings = AssetDatabase.LoadAssetAtPath<LevelEditorSettings>(path);
        }
        else
        {
            CreateSettingsAsset();
        }
    }

    private void CreateSettingsAsset()
    {
        string folder = "Assets/Levels";
        if (!AssetDatabase.IsValidFolder(folder))
        {
            AssetDatabase.CreateFolder("Assets", "Levels");
        }

        var settings = ScriptableObject.CreateInstance<LevelEditorSettings>();
        settings.levelsFolder = "Assets/Levels/Data";
        settings.prefabsFolder = "Assets/Levels/Prefabs";

        if (!AssetDatabase.IsValidFolder("Assets/Levels/Data"))
        {
            AssetDatabase.CreateFolder("Assets/Levels", "Data");
        }
        if (!AssetDatabase.IsValidFolder("Assets/Levels/Prefabs"))
        {
            AssetDatabase.CreateFolder("Assets/Levels", "Prefabs");
        }

        string path = "Assets/Levels/LevelEditorSettings.asset";
        AssetDatabase.CreateAsset(settings, path);
        AssetDatabase.SaveAssets();
        _settings = settings;
        EditorGUIUtility.PingObject(_settings);
    }

    // ------------------- Left Pane -------------------
    private void DrawLeftPane()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(LEFT_PANE_WIDTH)))
        {
            EditorGUILayout.Space(6);

            _settings = (LevelEditorSettings)EditorGUILayout.ObjectField("Settings", _settings, typeof(LevelEditorSettings), false);
            if (_settings == null)
            {
                if (GUILayout.Button("Create Editor Settings"))
                {
                    CreateSettingsAsset();
                }
                return;
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Paths", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _settings.levelsFolder = EditorGUILayout.TextField("Levels Folder", _settings.levelsFolder);
            _settings.prefabsFolder = EditorGUILayout.TextField("Prefabs Folder", _settings.prefabsFolder);
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_settings);
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Level Asset", EditorStyles.boldLabel);
            _level = (LevelGridData)EditorGUILayout.ObjectField("Active Level", _level, typeof(LevelGridData), false);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("New Level"))
                {
                    CreateNewLevelAsset();
                }
                if (GUILayout.Button("Save Level"))
                {
                    if (_level != null)
                    {
                        EditorUtility.SetDirty(_level);
                        AssetDatabase.SaveAssets();
                    }
                }
            }

            EditorGUILayout.Space(8);
            if (_level != null)
            {
                EditorGUILayout.LabelField("Level Settings", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                _level.levelName = EditorGUILayout.TextField("Level Name", _level.levelName);
                _level.cellSize = EditorGUILayout.FloatField("Cell Size", Mathf.Max(0.0001f, _level.cellSize));
                _level.gridSize = EditorGUILayout.Vector2IntField("Grid Size",
                    new Vector2Int(Mathf.Max(1, _level.gridSize.x), Mathf.Max(1, _level.gridSize.y)));
                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(_level);
                }

                EditorGUILayout.Space(6);
                if (GUILayout.Button("Bake Prefab"))
                {
                    BakeLevelPrefab();
                }
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Palette", EditorStyles.boldLabel);

            using (var sv = new EditorGUILayout.ScrollViewScope(_scrollPalette, GUILayout.Height(position.height * 0.4f)))
            {
                _scrollPalette = sv.scrollPosition;

                if (_settings.palette != null)
                {
                    for (int i = 0; i < _settings.palette.Count; i++)
                    {
                        var def = _settings.palette[i];
                        using (new EditorGUILayout.HorizontalScope(GUI.skin.box))
                        {
                            bool selected = _selectedPaletteIndex == i;
                            var style = new GUIStyle(EditorStyles.miniButton) { fixedWidth = 24 };

                            if (GUILayout.Toggle(selected, selected ? "●" : "○", style))
                            {
                                _selectedPaletteIndex = i;
                            }
                            else
                            {
                                if (selected == true && Event.current.type == EventType.MouseDown)
                                {
                                    _selectedPaletteIndex = i;
                                }
                            }

                            _settings.palette[i] = (PlaceableObjectDef)EditorGUILayout.ObjectField(def, typeof(PlaceableObjectDef), false);

                            if (GUILayout.Button("X", GUILayout.Width(22)))
                            {
                                _settings.palette.RemoveAt(i);
                                if (_selectedPaletteIndex == i) _selectedPaletteIndex = -1;
                                GUI.FocusControl(null);
                                break;
                            }
                        }
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Palette Slot"))
                {
                    _settings.palette.Add(null);
                    EditorUtility.SetDirty(_settings);
                }
                if (GUILayout.Button("New Placeable"))
                {
                    CreatePlaceableAsset();
                }
            }
        }
    }

    // ------------------- Right Pane (Grid) -------------------
    private void DrawRightPane()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
        {
            EditorGUILayout.Space(6);

            if (_level == null)
            {
                EditorGUILayout.HelpBox("Create or select a Level asset on the left.", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(GRID_MARGIN);

                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
                {
                    GUILayout.Space(GRID_MARGIN);

                    using (var sv = new EditorGUILayout.ScrollViewScope(_scrollGrid, false, false,
                               GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
                    {
                        _scrollGrid = sv.scrollPosition;

                        float fullWidth = Mathf.Max(1, _level.gridSize.x) * CELL_DRAW_SIZE;
                        float fullHeight = Mathf.Max(1, _level.gridSize.y) * CELL_DRAW_SIZE;

                        Rect contentRect = GUILayoutUtility.GetRect(fullWidth, fullHeight,
                            GUIStyle.none,
                            GUILayout.Width(fullWidth),
                            GUILayout.Height(fullHeight));

                        EditorGUI.DrawRect(contentRect, new Color(0.10f, 0.10f, 0.10f, 0.85f));
                        GUI.Box(contentRect, GUIContent.none);

                        DrawGridLines(contentRect, _level.gridSize);
                        DrawPlacedItems(contentRect);
                        DrawCenterXMarker(contentRect, _level.gridSize);

                        HandleGridInput(contentRect);

                        if (contentRect.Contains(Event.current.mousePosition))
                        {
                            Repaint();
                        }
                    }

                    GUILayout.Space(GRID_MARGIN);
                }

                GUILayout.Space(GRID_MARGIN);
            }
        }
    }

    private void DrawGridLines(Rect rect, Vector2Int gridSize)
    {
        Handles.color = new Color(1f, 1f, 1f, 0.15f);

        // verticals
        for (int x = 0; x <= gridSize.x; x++)
        {
            float xPos = rect.x + x * CELL_DRAW_SIZE;
            Handles.DrawLine(new Vector3(xPos, rect.y),
                             new Vector3(xPos, rect.y + rect.height));
        }

        // horizontals (IMGUI top-left -> flip Y)
        for (int y = 0; y <= gridSize.y; y++)
        {
            float yPos = rect.y + rect.height - y * CELL_DRAW_SIZE;
            Handles.DrawLine(new Vector3(rect.x, yPos),
                             new Vector3(rect.x + rect.width, yPos));
        }

        // origin crosshair (data bottom-left)
        float ox = rect.x;
        float oy = rect.y + rect.height - CELL_DRAW_SIZE;
        Handles.color = new Color(1f, 1f, 0f, 0.9f);
        Handles.DrawLine(new Vector3(ox, oy), new Vector3(ox + 10f, oy));
        Handles.DrawLine(new Vector3(ox, oy), new Vector3(ox, oy + 10f));
    }

    private void DrawCenterXMarker(Rect rect, Vector2Int gridSize)
    {
        // Visual center of the grid in X (between columns when even).
        float centerXPx = rect.x + (gridSize.x * 0.5f) * CELL_DRAW_SIZE;
        Handles.color = new Color(1f, 0.2f, 0.8f, 0.9f);
        Handles.DrawLine(new Vector3(centerXPx, rect.y),
                         new Vector3(centerXPx, rect.y + rect.height));

        var style = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = Color.white } };
        GUI.Label(new Rect(centerXPx + 4, rect.y + 2, 100, 16), "X Center", style);
    }

    private void DrawPlacedItems(Rect rect)
    {
        foreach (var item in _level.placed)
        {
            if (item == null || item.def == null) continue;

            Rect r = CellRectToPixelsTop(rect, item.origin, item.def.size);

            // fill
            Color fill = (item.def.editorColor.a > 0f) ? item.def.editorColor : new Color(0.2f, 0.8f, 0.4f, 0.35f);
            EditorGUI.DrawRect(r, fill);

            // outline
            Handles.color = new Color(fill.r, fill.g, fill.b, 0.95f);
            Handles.DrawAAPolyLine(2f, new Vector3[]
            {
                new Vector3(r.x, r.y),
                new Vector3(r.x + r.width, r.y),
                new Vector3(r.x + r.width, r.y + r.height),
                new Vector3(r.x, r.y + r.height),
                new Vector3(r.x, r.y),
            });

            // label
            var label = !string.IsNullOrEmpty(item.def.objectId) ? item.def.objectId : item.def.name;
            var centered = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            GUI.Label(r, $"{label} ({item.def.size.x}x{item.def.size.y})", centered);
        }
    }

    private void HandleGridInput(Rect fullRect)
    {
        var e = Event.current;
        if (e == null) return;

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            if (!MouseToGridCell(fullRect, e.mousePosition, _level.gridSize, out var cell))
                return;

            if (_cellToItem.TryGetValue(cell, out var hitItem))
            {
                Undo.RecordObject(_level, "Remove Placed Item");
                _level.placed.Remove(hitItem);
                EditorUtility.SetDirty(_level);
                RebuildOccupancy();
                e.Use();
                return;
            }

            var def = GetSelectedDef();
            if (def != null)
            {
                TryPlace(cell, def);
                e.Use();
            }
        }
    }

    // ------- GUI math (IMGUI is top-left, our data is bottom-left) -------
    private Rect CellRectToPixelsTop(Rect fullRect, Vector2Int origin, Vector2Int size)
    {
        float x = fullRect.x + origin.x * CELL_DRAW_SIZE;
        float w = size.x * CELL_DRAW_SIZE;

        float yTop = fullRect.y + fullRect.height - (origin.y + size.y) * CELL_DRAW_SIZE;
        float h = size.y * CELL_DRAW_SIZE;

        return new Rect(x, yTop, w, h);
    }

    private bool MouseToGridCell(Rect fullRect, Vector2 mousePos, Vector2Int gridSize, out Vector2Int cell)
    {
        cell = default;
        if (!fullRect.Contains(mousePos)) return false;

        float localX = mousePos.x - fullRect.x;
        float localYFromTop = mousePos.y - fullRect.y;

        int cx = Mathf.FloorToInt(localX / CELL_DRAW_SIZE);
        int cy = gridSize.y - 1 - Mathf.FloorToInt(localYFromTop / CELL_DRAW_SIZE);

        if (cx < 0 || cy < 0 || cx >= gridSize.x || cy >= gridSize.y) return false;
        cell = new Vector2Int(cx, cy);
        return true;
    }

    // ------------------- Placement & Data -------------------
    private void TryPlace(Vector2Int origin, PlaceableObjectDef def)
    {
        Vector2Int maxCell = new Vector2Int(origin.x + def.size.x - 1, origin.y + def.size.y - 1);
        _level.EnsureFits(origin, maxCell);

        if (!IsAreaFree(origin, def.size))
        {
            ShowNotification(new GUIContent("Cannot place: area overlaps another item."));
            return;
        }

        Undo.RecordObject(_level, "Place Item");
        var item = new LevelGridData.PlacedItem { def = def, origin = origin };
        _level.placed.Add(item);
        EditorUtility.SetDirty(_level);
        RebuildOccupancy();
    }

    private bool IsAreaFree(Vector2Int origin, Vector2Int size)
    {
        for (int y = 0; y < size.y; y++)
        {
            for (int x = 0; x < size.x; x++)
            {
                Vector2Int c = new Vector2Int(origin.x + x, origin.y + y);
                if (_cellToItem.ContainsKey(c))
                {
                    return false;
                }
            }
        }
        return true;
    }

    private PlaceableObjectDef GetSelectedDef()
    {
        if (_settings == null || _settings.palette == null) return null;
        if (_selectedPaletteIndex < 0 || _selectedPaletteIndex >= _settings.palette.Count) return null;
        return _settings.palette[_selectedPaletteIndex];
    }

    private void RebuildOccupancy()
    {
        _cellToItem.Clear();
        if (_level == null) return;

        foreach (var item in _level.placed)
        {
            if (item == null || item.def == null) continue;
            for (int y = 0; y < item.def.size.y; y++)
            {
                for (int x = 0; x < item.def.size.x; x++)
                {
                    Vector2Int c = new Vector2Int(item.origin.x + x, item.origin.y + y);
                    _cellToItem[c] = item;
                }
            }
        }
    }

    // ------------------- Asset helpers -------------------
    private void CreatePlaceableAsset()
    {
        string folder = "Assets/Levels/Placeables";
        if (!AssetDatabase.IsValidFolder("Assets/Levels"))
        {
            AssetDatabase.CreateFolder("Assets", "Levels");
        }
        if (!AssetDatabase.IsValidFolder(folder))
        {
            AssetDatabase.CreateFolder("Assets/Levels", "Placeables");
        }

        var def = ScriptableObject.CreateInstance<PlaceableObjectDef>();
        def.objectId = "NewPlaceable";
        def.size = new Vector2Int(1, 1);

        string unique = AssetDatabase.GenerateUniqueAssetPath($"{folder}/Placeable.asset");
        AssetDatabase.CreateAsset(def, unique);
        AssetDatabase.SaveAssets();
        Selection.activeObject = def;
        EditorGUIUtility.PingObject(def);

        if (_settings != null)
        {
            _settings.palette.Add(def);
            EditorUtility.SetDirty(_settings);
        }
    }

    private void CreateNewLevelAsset()
    {
        EnsureSettingsAndFolders();

        string baseFolder = _settings.levelsFolder;
        string name = "LevelGridData.asset";
        string unique = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(baseFolder, name).Replace("\\", "/"));

        var data = ScriptableObject.CreateInstance<LevelGridData>();
        data.levelName = "NewLevel";
        data.gridSize = new Vector2Int(8, 8);
        data.cellSize = 1.0f;

        AssetDatabase.CreateAsset(data, unique);
        AssetDatabase.SaveAssets();

        _level = data;
        Selection.activeObject = data;
        EditorGUIUtility.PingObject(data);

        RebuildOccupancy();
    }

    private void EnsureSettingsAndFolders()
    {
        if (_settings == null)
        {
            CreateSettingsAsset();
        }

        if (!AssetDatabase.IsValidFolder(_settings.levelsFolder))
        {
            CreateFolderPath(_settings.levelsFolder);
        }

        if (!AssetDatabase.IsValidFolder(_settings.prefabsFolder))
        {
            CreateFolderPath(_settings.prefabsFolder);
        }
    }

    private void CreateFolderPath(string path)
    {
        var parts = path.Split('/');
        if (parts.Length == 0) return;

        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = parts[i];
            if (!AssetDatabase.IsValidFolder($"{current}/{next}"))
            {
                AssetDatabase.CreateFolder(current, next);
            }
            current = $"{current}/{next}";
        }
    }

    // ------------------- Bake (bottom-center origin, Z=0) -------------------
    private void BakeLevelPrefab()
    {
        if (_level == null || _settings == null)
        {
            EditorUtility.DisplayDialog("Bake Prefab", "Missing Level or Settings.", "OK");
            return;
        }

        EnsureSettingsAndFolders();

        // Fixed path (no GenerateUnique...) so we can replace the existing prefab.
        string prefabPath = (_settings.prefabsFolder.TrimEnd('/', '\\') + "/" + _level.levelName + ".prefab")
            .Replace("\\", "/");

        var root = new GameObject($"Level_{_level.levelName}");
        try
        {
            float cs = _level.cellSize;
            float halfGridX = _level.gridSize.x * 0.5f;

            foreach (var item in _level.placed)
            {
                if (item == null || item.def == null || item.def.prefab == null) continue;

                // Bottom-center mapping, Z = 0
                float worldX = (item.origin.x + item.def.size.x * 0.5f - halfGridX) * cs;
                float worldY = (item.origin.y) * cs;

                var go = (GameObject)PrefabUtility.InstantiatePrefab(item.def.prefab);
                if (go == null) go = Instantiate(item.def.prefab);
                go.transform.SetParent(root.transform, true);
                go.transform.localPosition = new Vector3(worldX, worldY, 0f) + item.def.prefabOffset;
                go.name = $"{item.def.objectId}_{item.origin.x}_{item.origin.y}";
            }

            if (root.GetComponent<ObstacleSet>() == null)
            {
                var added = root.AddComponent<ObstacleSet>();
            }

            // Save (create or REPLACE if it exists). Keeps existing prefab GUID intact.
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool success);
            if (!success)
            {
                EditorUtility.DisplayDialog("Bake Prefab", "Failed to save prefab.", "OK");
            }
            else
            {
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
            }
        }
        finally
        {
            DestroyImmediate(root);
        }
    }

}
