using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class GridLevelEditorWindow : EditorWindow
{
    private const float LEFT_PANE_WIDTH = 280f;
    private const float GRID_MARGIN = 80f;
    private float CELL_DRAW_SIZE = 24f;
    private const float LAYER_GAP = 24f;          // pixels between layers in side-by-side mode
    private bool _showAllLayersSideBySide = false;
    private float _baseScale = 1.0f;
    private float _baseDepthScale = 1.0f;

    private LevelEditorSettings _settings;
    private LevelGridData _level;
    private Vector2 _scrollPalette;
    private Vector2 _scrollGrid;

    private int _selectedPaletteIndex = -1;

    // Z-layer being edited now
    private int _currentLayer = 0;

    // Occupancy map: (x,y,z) -> item
    private readonly Dictionary<Vector3Int, LevelGridData.PlacedItem> _cellToItem =
        new Dictionary<Vector3Int, LevelGridData.PlacedItem>();

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

    // ---------- Settings ----------
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

    // ---------- Left Pane ----------
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

                // Depth controls
                int newDepth = Mathf.Max(1, EditorGUILayout.IntField("Depth (Z layers)", Mathf.Max(1, _level.depth)));
                if (newDepth != _level.depth)
                {
                    _level.depth = newDepth;
                    if (_currentLayer >= _level.depth)
                    {
                        _currentLayer = _level.depth - 1;
                    }
                }

                CELL_DRAW_SIZE = EditorGUILayout.FloatField("CELL_DRAW_SIZE", Mathf.Max(12f, CELL_DRAW_SIZE));
                EditorGUILayout.Space(4);
                _showAllLayersSideBySide = EditorGUILayout.Toggle("Show All Layers Side-by-Side", _showAllLayersSideBySide);
                _baseScale = EditorGUILayout.FloatField("Base Scale", Mathf.Max(0.5f, _baseScale));
                _baseDepthScale = EditorGUILayout.FloatField("Base Depth Scale", Mathf.Max(0.5f, _baseDepthScale));

                // Layer selector
                int newLayer = EditorGUILayout.IntSlider("Current Layer", _currentLayer, 0, Mathf.Max(0, _level.depth - 1));
                if (newLayer != _currentLayer)
                {
                    _currentLayer = newLayer;
                }

                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(_level);
                    RebuildOccupancy();
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

    // ---------- Right Pane (Grid) ----------
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

                        // --- Content size depends on mode ---
                        float layerWidthPx = Mathf.Max(1, _level.gridSize.x) * CELL_DRAW_SIZE;
                        float layerHeightPx = Mathf.Max(1, _level.gridSize.y) * CELL_DRAW_SIZE;

                        float totalWidthPx = layerWidthPx;
                        if (_showAllLayersSideBySide)
                        {
                            totalWidthPx = layerWidthPx * _level.depth + LAYER_GAP * Mathf.Max(0, _level.depth - 1);
                        }

                        Rect contentRect = GUILayoutUtility.GetRect(totalWidthPx, layerHeightPx,
                            GUIStyle.none,
                            GUILayout.Width(totalWidthPx),
                            GUILayout.Height(layerHeightPx));

                        // background
                        EditorGUI.DrawRect(contentRect, new Color(0.10f, 0.10f, 0.10f, 0.85f));
                        GUI.Box(contentRect, GUIContent.none);

                        if (_showAllLayersSideBySide)
                        {
                            // Draw each layer, side-by-side
                            for (int z = 0; z < _level.depth; z++)
                            {
                                Rect layerRect = GetLayerRect(contentRect, z, layerWidthPx);
                                DrawGridLines(layerRect, _level.gridSize);
                                DrawCenterXMarker(layerRect, _level.gridSize);
                                DrawLayerHeader(layerRect, z);
                                DrawPlacedItems(layerRect, z);
                            }

                            HandleGridInputSideBySide(contentRect, layerWidthPx);
                        }
                        else
                        {
                            // Single-layer view (current)
                            DrawGridLines(contentRect, _level.gridSize);
                            DrawCenterXMarker(contentRect, _level.gridSize);
                            DrawLayerHeader(contentRect, _currentLayer);
                            DrawPlacedItems(contentRect, _currentLayer);
                            HandleGridInputSingle(contentRect);
                        }

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

        // horizontals (flip Y for IMGUI)
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
        float centerXPx = rect.x + (gridSize.x * 0.5f) * CELL_DRAW_SIZE;
        Handles.color = new Color(1f, 0.2f, 0.8f, 0.9f);
        Handles.DrawLine(new Vector3(centerXPx, rect.y),
                         new Vector3(centerXPx, rect.y + rect.height));

        var style = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = Color.white } };
        GUI.Label(new Rect(centerXPx + 4, rect.y + 2, 100, 16), $"Layer Z={_currentLayer}", style);
    }

    private void DrawPlacedItems(Rect rect, int layerZ)
    {
        foreach (var item in _level.placed)
        {
            if (item == null || item.def == null) continue;
            if (item.layerZ != layerZ) continue;

            Rect r = CellRectToPixelsTop(rect, item.origin, item.def.size);

            Color fill = (item.def.editorColor.a > 0f) ? item.def.editorColor : new Color(0.2f, 0.8f, 0.4f, 0.35f);
            EditorGUI.DrawRect(r, fill);

            Handles.color = new Color(fill.r, fill.g, fill.b, 0.95f);
            Handles.DrawAAPolyLine(2f, new Vector3[]
            {
                new Vector3(r.x, r.y),
                new Vector3(r.x + r.width, r.y),
                new Vector3(r.x + r.width, r.y + r.height),
                new Vector3(r.x, r.y + r.height),
                new Vector3(r.x, r.y),
            });

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

            Vector3Int key = new Vector3Int(cell.x, cell.y, _currentLayer);

            if (_cellToItem.TryGetValue(key, out var hitItem))
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
                TryPlace(cell, def, _currentLayer);
                e.Use();
            }
        }
    }

    // ---------- GUI math ----------
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

    // ---------- Placement ----------
    private void TryPlace(Vector2Int origin, PlaceableObjectDef def, int layerZ)
    {
        // Ensure grid fits in X/Y
        Vector2Int maxCell = new Vector2Int(origin.x + def.size.x - 1, origin.y + def.size.y - 1);
        _level.EnsureFits(origin, maxCell);

        // Ensure depth fits
        _level.EnsureDepth(layerZ);

        // Overlap check in this layer only
        if (!IsAreaFree(origin, def.size, layerZ))
        {
            ShowNotification(new GUIContent("Cannot place: area overlaps another item."));
            return;
        }

        Undo.RecordObject(_level, "Place Item");
        var item = new LevelGridData.PlacedItem
        {
            def = def,
            origin = origin,
            layerZ = layerZ
        };
        _level.placed.Add(item);
        EditorUtility.SetDirty(_level);
        RebuildOccupancy();
    }

    private bool IsAreaFree(Vector2Int origin, Vector2Int size, int layerZ)
    {
        for (int y = 0; y < size.y; y++)
        {
            for (int x = 0; x < size.x; x++)
            {
                Vector3Int key = new Vector3Int(origin.x + x, origin.y + y, layerZ);
                if (_cellToItem.ContainsKey(key))
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
                    Vector3Int key = new Vector3Int(item.origin.x + x, item.origin.y + y, item.layerZ);
                    _cellToItem[key] = item;
                }
            }
        }

        if (_level.depth < 1) _level.depth = 1;
        if (_currentLayer >= _level.depth) _currentLayer = _level.depth - 1;
        if (_currentLayer < 0) _currentLayer = 0;
    }
    // Single-layer input (original behavior)
    private void HandleGridInputSingle(Rect rect)
    {
        var e = Event.current;
        if (e == null) return;

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            if (!MouseToGridCell(rect, e.mousePosition, _level.gridSize, out var cell))
                return;

            Vector3Int key = new Vector3Int(cell.x, cell.y, _currentLayer);

            if (_cellToItem.TryGetValue(key, out var hitItem))
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
                TryPlace(cell, def, _currentLayer);
                e.Use();
            }
        }
    }

    // Side-by-side input (figure out which layer was clicked)
    private void HandleGridInputSideBySide(Rect fullContent, float layerWidthPx)
    {
        var e = Event.current;
        if (e == null) return;
        if (e.type != EventType.MouseDown || e.button != 0) return;
        if (!fullContent.Contains(e.mousePosition)) return;

        // Find the layer sub-rect that contains the mouse
        for (int z = 0; z < _level.depth; z++)
        {
            Rect layerRect = GetLayerRect(fullContent, z, layerWidthPx);
            if (!layerRect.Contains(e.mousePosition)) continue;

            if (!MouseToGridCell(layerRect, e.mousePosition, _level.gridSize, out var cell))
                continue;

            Vector3Int key = new Vector3Int(cell.x, cell.y, z);

            if (_cellToItem.TryGetValue(key, out var hitItem))
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
                TryPlace(cell, def, z);
                e.Use();
                return;
            }
        }
    }

    // ---------- Asset helpers ----------
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
        data.depth = 1;

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
    private Rect GetLayerRect(Rect contentRect, int layerZ, float layerWidthPx)
    {
        float x = contentRect.x + layerZ * (layerWidthPx + LAYER_GAP);
        return new Rect(x, contentRect.y, layerWidthPx, contentRect.height);
    }

    private void DrawLayerHeader(Rect layerRect, int z)
    {
        var style = new GUIStyle(EditorStyles.miniBoldLabel)
        {
            alignment = TextAnchor.UpperLeft,
            normal = { textColor = Color.white }
        };
        GUI.Label(new Rect(layerRect.x + 4, layerRect.y + 4, 150, 18), $"Layer Z={z}", style);
    }

    // ---------- Bake (bottom-center X origin, Z = layer * cellSize) ----------
    private void BakeLevelPrefab()
    {
        if (_level == null || _settings == null)
        {
            EditorUtility.DisplayDialog("Bake Prefab", "Missing Level or Settings.", "OK");
            return;
        }

        EnsureSettingsAndFolders();

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

                // X: center across item width, then shift by grid center
                float worldX = (item.origin.x + item.def.size.x * 0.5f - halfGridX) * cs;

                // Y: bottom-up
                float worldY = (item.origin.y) * cs;

                // Z: per-layer depth
                float worldZ = (item.layerZ) * cs * _baseDepthScale;

                var go = (GameObject)PrefabUtility.InstantiatePrefab(item.def.prefab);
                if (go == null) go = Instantiate(item.def.prefab);
                go.transform.SetParent(root.transform, true);
                go.transform.localPosition = new Vector3(worldX, worldY, worldZ) + item.def.prefabOffset;
                go.name = $"{item.def.objectId}_{item.origin.x}_{item.origin.y}_z{item.layerZ}";
            }

            // Optional: add a component at the root (use your own if needed)
            if (root.GetComponent<ObstacleSet>() == null) { root.AddComponent<ObstacleSet>(); }

            root.transform.localScale = Vector3.one * _baseScale;

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
