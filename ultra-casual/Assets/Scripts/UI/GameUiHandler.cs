using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[DisallowMultipleComponent]
public class GameUiHandler : MonoBehaviour
{
    [System.Serializable]
    public class PanelGroup
    {
        public UiMode mode;
        [Tooltip("All panels that should be enabled when this mode is active.")]
        public List<GameObject> panels = new List<GameObject>();
    }

    [Header("Mode → Panels Mapping")]
    [Tooltip("Define which panels belong to each UI mode.")]
    public List<PanelGroup> groups = new List<PanelGroup>();

    [Header("Global Panels")]
    [Tooltip("These panels remain enabled regardless of the mode.")]
    public List<GameObject> alwaysOnPanels = new List<GameObject>();

    [Tooltip("These panels remain disabled regardless of the mode.")]
    public List<GameObject> alwaysOffPanels = new List<GameObject>();

    [Header("Options")]
    [Tooltip("If true, any panel listed in any group but not part of the active mode will be disabled automatically.")]
    public bool disableOtherModePanels = true;

    [Tooltip("When true, null or missing entries are automatically removed on Apply/Validate.")]
    public bool autoCleanLists = true;

    public UiMode CurrentMode { get; private set; }
    private readonly HashSet<GameObject> _allPanels = new HashSet<GameObject>();

    private void Awake()
    {
        RebuildCache();
        // Do not force a mode here; let the GameManager decide initial mode.
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (autoCleanLists)
        {
            foreach (var g in groups)
            {
                g.panels = g.panels.Where(p => p != null).Distinct().ToList();
            }
            alwaysOnPanels = alwaysOnPanels.Where(p => p != null).Distinct().ToList();
            alwaysOffPanels = alwaysOffPanels.Where(p => p != null).Distinct().ToList();
        }
    }
#endif

    public void RebuildCache()
    {
        _allPanels.Clear();

        foreach (var g in groups)
        {
            foreach (var p in g.panels)
            {
                if (p != null) _allPanels.Add(p);
            }
        }
        foreach (var p in alwaysOnPanels)
        {
            if (p != null) _allPanels.Add(p);
        }
        foreach (var p in alwaysOffPanels)
        {
            if (p != null) _allPanels.Add(p);
        }
    }

    /// <summary>
    /// Activates the given mode: enables its panels (plus always-on),
    /// disables other mode panels and always-off.
    /// </summary>
    public void SetMode(UiMode mode)
    {
        CurrentMode = mode;

        // Ensure cache is up-to-date (in case inspector changed at runtime).
        RebuildCache();

        // 1) Start from everything known and disable them
        if (disableOtherModePanels)
        {
            foreach (var panel in _allPanels)
            {
                if (!panel) continue;
                panel.SetActive(false);
            }
        }

        // 2) Enable panels of this mode
        var activeGroup = groups.FirstOrDefault(g => g.mode == mode);
        if (activeGroup != null)
        {
            foreach (var p in activeGroup.panels)
            {
                if (!p) continue;
                p.SetActive(true);
            }
        }

        // 3) Apply global rules
        foreach (var p in alwaysOnPanels)
        {
            if (!p) continue;
            p.SetActive(true);
        }

        foreach (var p in alwaysOffPanels)
        {
            if (!p) continue;
            p.SetActive(false);
        }
    }

    /// <summary>
    /// Utility: enables exactly the provided panels, disables all other known panels,
    /// then applies global AlwaysOn/AlwaysOff.
    /// </summary>
    public void ShowOnly(IEnumerable<GameObject> panelsToShow)
    {
        RebuildCache();

        var show = new HashSet<GameObject>(panelsToShow.Where(p => p != null));

        foreach (var panel in _allPanels)
        {
            if (!panel) continue;
            panel.SetActive(show.Contains(panel));
        }

        foreach (var p in alwaysOnPanels)
        {
            if (!p) continue;
            p.SetActive(true);
        }

        foreach (var p in alwaysOffPanels)
        {
            if (!p) continue;
            p.SetActive(false);
        }
    }
}
