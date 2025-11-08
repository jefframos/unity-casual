using UnityEngine;

[ExecuteAlways, DisallowMultipleComponent]
public class WorldBendGlobalController : MonoBehaviour
{
    [Header("Enable")]
    public bool applyGlobally = true;

    [Header("Targeted Origin")]
    public Transform originTarget;           // optional (falls back to this.transform)
    public Vector3 originOffset;             // add on top of target
    [Tooltip("Choose which components from the target to use for the origin.")]
    public Vector3 componentMask = new Vector3(0, 0, 1); // e.g., only Z

    [Header("Axis Source")]
    public bool useThisForwardAsAxis = true; // else use fixedAxis
    public Vector3 fixedAxis = new Vector3(0, 0, 1);

    [Header("Bend Params")]
    [Range(0, 0.5f)] public float strength = 0.0005f;
    [Min(0.01f)] public float radius = 2000f;
    public float maxYDrop = 50f;
    [Tooltip("Optional near-origin no-bend fade (world units along axis).")]
    public float bendStart = 0f;
    public float bendEnd   = 0f;

    [Header("Edge Fade")]
    [Range(0f, 1f)] public float edgeFadeStartPct = 0.85f; // start fading at 85% of radius
    public bool transparentBlend = false; // else dithered cutout

    static readonly int ID_Strength  = Shader.PropertyToID("_WB_Strength_G");
    static readonly int ID_Radius    = Shader.PropertyToID("_WB_Radius_G");
    static readonly int ID_Axis      = Shader.PropertyToID("_WB_Axis_G");
    static readonly int ID_Origin    = Shader.PropertyToID("_WB_Origin_G");
    static readonly int ID_FadeStart = Shader.PropertyToID("_WB_EdgeFadeStartPct_G");
    static readonly int ID_MaxDrop   = Shader.PropertyToID("_WB_MaxYDrop_G");
    static readonly int ID_BendStart = Shader.PropertyToID("_WB_BendStart_G");
    static readonly int ID_BendEnd   = Shader.PropertyToID("_WB_BendEnd_G");
    static readonly int ID_Mask      = Shader.PropertyToID("_WB_ComponentMask_G");

    void OnEnable()  => Apply();
    void OnValidate()=> Apply();
    void Update()    => Apply();

    void Apply()
    {
        if (!applyGlobally)
        {
            Shader.DisableKeyword("_BEND_USE_GLOBAL");
            Shader.DisableKeyword("_EDGE_FADE_TRANSPARENT");
            Shader.DisableKeyword("_EDGE_FADE_DITHER");
            return;
        }

        var src = originTarget != null ? originTarget : transform;

        Vector3 rawOrigin = src.position + originOffset;
        Vector3 mask = new Vector3(
            Mathf.Clamp01(componentMask.x),
            Mathf.Clamp01(componentMask.y),
            Mathf.Clamp01(componentMask.z)
        );
        // Use only selected components from the target; others come from 0 (world origin)
        Vector3 originWS = new Vector3(rawOrigin.x * mask.x, rawOrigin.y * mask.y, rawOrigin.z * mask.z);

        Vector3 axisWS = useThisForwardAsAxis ? transform.forward : fixedAxis;
        if (axisWS.sqrMagnitude < 1e-8f) axisWS = Vector3.forward;
        axisWS.Normalize();

        Shader.SetGlobalFloat(ID_Strength, strength);
        Shader.SetGlobalFloat(ID_Radius,   Mathf.Max(0.01f, radius));
        Shader.SetGlobalVector(ID_Axis,    new Vector4(axisWS.x, axisWS.y, axisWS.z, 0));
        Shader.SetGlobalVector(ID_Origin,  new Vector4(originWS.x, originWS.y, originWS.z, 1));
        Shader.SetGlobalFloat(ID_FadeStart, Mathf.Clamp01(edgeFadeStartPct));
        Shader.SetGlobalFloat(ID_MaxDrop,   maxYDrop);
        Shader.SetGlobalFloat(ID_BendStart, bendStart);
        Shader.SetGlobalFloat(ID_BendEnd,   bendEnd);
        Shader.SetGlobalVector(ID_Mask,     new Vector4(mask.x, mask.y, mask.z, 0));

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
    }
}
