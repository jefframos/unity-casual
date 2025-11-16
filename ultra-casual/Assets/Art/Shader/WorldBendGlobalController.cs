using UnityEngine;

[ExecuteAlways, DisallowMultipleComponent]
public class WorldBendGlobalController : MonoBehaviour
{
    [Header("Enable")]
    public bool applyGlobally = true;
    public bool applyGameModeOnly = true;
    [Tooltip("If true, disables bending entirely (sets strength=0).")]
    public bool disableBend = false;
    [Tooltip("If true, use globe (radial) bend instead of cylindrical.")]
    public bool bendGlobe = false;

    [Header("Targeted Origin")]
    public Transform originTarget;
    public Vector3 originOffset;
    public Vector3 componentMask = new Vector3(0, 0, 1);

    [Header("Axis Source (for cylinder mode)")]
    public bool useThisForwardAsAxis = true;
    public Vector3 fixedAxis = new Vector3(0, 0, 1);

    [Header("Bend Params")]
    [Range(0, 0.5f)] public float strength = 0.0005f;
    [Min(0.01f)] public float radius = 2000f;
    public float maxYDrop = 50f;
    public float bendStart = 0f;
    public float bendEnd = 0f;

    [Header("Edge Fade")]
    [Range(0f, 1f)] public float edgeFadeStartPct = 0.85f;
    public bool transparentBlend = false;

    [Header("Tracking Offset")]
    [Tooltip("Extra world-space offset used before computing bending.\n" +
             "E.g. set to -playerXZ (y=0) to keep curvature under player.")]
    public Vector3 trackingOffset = Vector3.zero;

    [Header("Toon / Outline (Global)")]
    public Color outlineColor = Color.black;
    [Range(0f, 0.3f)] public float outlineThickness = 0.03f;
    [Range(1f, 8f)] public float toonLightSteps = 3f;
    [Range(0f, 32f)] public float colorSteps = 0f;   // 0 = no posterize

    // Global property IDs (bend)
    static readonly int ID_Strength = Shader.PropertyToID("_WB_Strength_G");
    static readonly int ID_Radius = Shader.PropertyToID("_WB_Radius_G");
    static readonly int ID_Axis = Shader.PropertyToID("_WB_Axis_G");
    static readonly int ID_Origin = Shader.PropertyToID("_WB_Origin_G");
    static readonly int ID_FadeStart = Shader.PropertyToID("_WB_EdgeFadeStartPct_G");
    static readonly int ID_MaxDrop = Shader.PropertyToID("_WB_MaxYDrop_G");
    static readonly int ID_BendStart = Shader.PropertyToID("_WB_BendStart_G");
    static readonly int ID_BendEnd = Shader.PropertyToID("_WB_BendEnd_G");
    static readonly int ID_Mask = Shader.PropertyToID("_WB_ComponentMask_G");
    static readonly int ID_TrackOffset = Shader.PropertyToID("_WB_TrackOffset_G");

    static readonly int ID_Disable = Shader.PropertyToID("_WB_DisableBend");
    static readonly int ID_Globe = Shader.PropertyToID("_WB_BendGlobe");

    // Global property IDs (toon / outline)
    static readonly int ID_OutlineColor = Shader.PropertyToID("_WB_OutlineColor_G");
    static readonly int ID_OutlineThickness = Shader.PropertyToID("_WB_OutlineThickness_G");
    static readonly int ID_ToonSteps = Shader.PropertyToID("_WB_ToonSteps_G");
    static readonly int ID_ColorSteps = Shader.PropertyToID("_WB_ColorSteps_G");

    void OnEnable()
    {
        Apply();
    }

    void OnValidate()
    {
        Apply();
    }

    void Update()
    {
        Apply();
    }

    void Apply()
    {
        if (!applyGlobally || (applyGameModeOnly && !Application.isPlaying))
        {
            DisableAllKeywords();
            return;
        }

        // disable full bend
        if (disableBend)
        {
            Shader.SetGlobalFloat(ID_Strength, 0f);
            Shader.SetGlobalFloat(ID_Disable, 1f);
            DisableAllKeywords();
            ApplyToonOutlineGlobals(); // still allow toon/outline globals even if bend is off
            return;
        }

        Shader.SetGlobalFloat(ID_Disable, 0f);

        // Origin
        var src = originTarget != null ? originTarget : transform;
        Vector3 rawOrigin = src.position + originOffset;

        Vector3 mask = new Vector3(
            Mathf.Clamp01(componentMask.x),
            Mathf.Clamp01(componentMask.y),
            Mathf.Clamp01(componentMask.z)
        );

        Vector3 originWS = new Vector3(
            rawOrigin.x * mask.x,
            rawOrigin.y * mask.y,
            rawOrigin.z * mask.z
        );

        // Axis (for cylinder mode)
        Vector3 axisWS = useThisForwardAsAxis ? transform.forward : fixedAxis;
        if (axisWS.sqrMagnitude < 1e-8f)
        {
            axisWS = Vector3.forward;
        }

        axisWS.Normalize();

        // Push bend globals
        Shader.SetGlobalFloat(ID_Globe, bendGlobe ? 1f : 0f);

        Shader.SetGlobalFloat(ID_Strength, strength);
        Shader.SetGlobalFloat(ID_Radius, Mathf.Max(0.01f, radius));
        Shader.SetGlobalVector(ID_Axis, new Vector4(axisWS.x, axisWS.y, axisWS.z, 0));
        Shader.SetGlobalVector(ID_Origin, new Vector4(originWS.x, originWS.y, originWS.z, 1));
        Shader.SetGlobalFloat(ID_FadeStart, Mathf.Clamp01(edgeFadeStartPct));
        Shader.SetGlobalFloat(ID_MaxDrop, maxYDrop);
        Shader.SetGlobalFloat(ID_BendStart, bendStart);
        Shader.SetGlobalFloat(ID_BendEnd, bendEnd);
        Shader.SetGlobalVector(ID_Mask, new Vector4(mask.x, mask.y, mask.z, 0));

        Shader.SetGlobalVector(
            ID_TrackOffset,
            new Vector4(trackingOffset.x, trackingOffset.y, trackingOffset.z, 0)
        );

        Shader.EnableKeyword("_BEND_USE_GLOBAL");

        if (transparentBlend)
        {
            Shader.EnableKeyword("_EDGE_FADE_TRANSPARENT");
            Shader.DisableKeyword("_EDGE_FADE_DITHER");
        }
        else
        {
            Shader.EnableKeyword("_EDGE_FADE_DITHER");
            Shader.DisableKeyword("_EDGE_FADE_TRANSPARENT");
        }

        // Push toon / outline globals
        ApplyToonOutlineGlobals();
    }

    void ApplyToonOutlineGlobals()
    {
        Shader.SetGlobalColor(ID_OutlineColor, outlineColor);
        Shader.SetGlobalFloat(ID_OutlineThickness, outlineThickness);
        Shader.SetGlobalFloat(ID_ToonSteps, toonLightSteps);
        Shader.SetGlobalFloat(ID_ColorSteps, colorSteps);
    }

    void DisableAllKeywords()
    {
        Shader.DisableKeyword("_BEND_USE_GLOBAL");
        Shader.DisableKeyword("_EDGE_FADE_TRANSPARENT");
        Shader.DisableKeyword("_EDGE_FADE_DITHER");
    }
}
