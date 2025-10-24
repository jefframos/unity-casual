using UnityEngine;

[DisallowMultipleComponent]
public class DebugUpgradeMiniPanel : MonoBehaviour
{
    public Rect windowRect = new Rect(10, 10, 360, 280);
    public int addCoinsAmount = 500;
    public float tileWidth = 110f;
    public float tileHeight = 140f;

    private UpgradeSystem _sys;
    private Vector2 _scroll;

    void Awake()
    {
        _sys = UpgradeSystem.Instance ?? FindAnyObjectByType<UpgradeSystem>(FindObjectsInactive.Include);

        _sys.AddCoins(500 * 20);
    }

    void OnGUI()
    {
        windowRect = GUI.Window(43210, windowRect, DrawWindow, "Upgrades");
    }

    void DrawWindow(int id)
    {
        if (_sys == null)
        {
            GUILayout.Label("UpgradeSystem not found.");
            if (GUILayout.Button("Find System")) _sys = UpgradeSystem.Instance ?? FindAnyObjectByType<UpgradeSystem>(FindObjectsInactive.Include);
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
            return;
        }

        // Top bar: coins + add coins
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Coins: {_sys.coins}");
        GUILayout.FlexibleSpace();
        if (GUILayout.Button($"+{addCoinsAmount}", GUILayout.Width(70)))
            _sys.AddCoins(addCoinsAmount);
        GUILayout.EndHorizontal();

        GUILayout.Space(4);

        // Grid of tiny cards
        _scroll = GUILayout.BeginScrollView(_scroll);
        float w = windowRect.width - 20f; // padding
        int perRow = Mathf.Max(1, Mathf.FloorToInt(w / tileWidth));
        int i = 0;

        var defs = _sys.upgradeDefinitions;
        if (defs == null || defs.Count == 0) GUILayout.Label("No upgrades registered.");

        while (defs != null && i < defs.Count)
        {
            GUILayout.BeginHorizontal();
            for (int c = 0; c < perRow && i < defs.Count; c++, i++)
            {
                var def = defs[i];
                if (def == null) continue;

                DrawTile(def);
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(2);
        }

        GUILayout.EndScrollView();

        GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }

    void DrawTile(UpgradeDefinition def)
    {
        var type = def.type;
        int level = _sys.GetLevel(type);
        float value = def.GetValueForLevel(level);
        int nextCost = def.GetCostForLevel(level);

        // Get current step (name, icon/prefab come from step or def)
        UpgradeStepData step;
        def.GetLevelData(level, out step);

        GUILayout.BeginVertical(GUILayout.Width(tileWidth), GUILayout.Height(tileHeight));
        GUILayout.BeginVertical("box", GUILayout.Width(tileWidth - 6), GUILayout.Height(tileHeight - 6));

        // Icon (step icon takes priority, then def icon)
        Texture iconTex = null;
        if (step != null && step.icon != null) iconTex = step.icon.texture;
        else if (def.icon != null) iconTex = def.icon.texture;

        if (iconTex != null)
            GUILayout.Label(iconTex, GUILayout.Width(32), GUILayout.Height(32));
        else
            GUILayout.Space(34);

        // Name (step name if available, else enum)
        string title = (step != null && !string.IsNullOrEmpty(step.name)) ? step.name : type.ToString();
        GUILayout.Label(title, SmallBold(), GUILayout.MaxWidth(tileWidth - 12));

        // Value + Next Cost (tight)
        GUILayout.Label($"Val: {value:0.###}", Small(), GUILayout.MaxWidth(tileWidth - 12));
        GUILayout.Label($"Cost: {nextCost}", Small(), GUILayout.MaxWidth(tileWidth - 12));

        GUILayout.FlexibleSpace();

        // [-]  [+]
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("-", GUILayout.Width(40), GUILayout.Height(22)))
            _sys.DecreaseLevel(type);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("+", GUILayout.Width(40), GUILayout.Height(22)))
            _sys.TryUpgrade(type);
        GUILayout.EndHorizontal();

        GUILayout.EndVertical();
        GUILayout.EndVertical();
    }

    GUIStyle Small()
    {
        var s = new GUIStyle(GUI.skin.label);
        s.fontSize = 11;
        return s;
    }

    GUIStyle SmallBold()
    {
        var s = Small();
        s.fontStyle = FontStyle.Bold;
        return s;
    }
}
