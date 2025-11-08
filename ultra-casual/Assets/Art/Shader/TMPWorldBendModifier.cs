using UnityEngine;
using TMPro;

/// <summary>
/// Bends TextMeshPro geometry in world space using the same global uniforms
/// set by WorldBendGlobalController. Attach to any TMP object.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class TMPWorldBendModifier : MonoBehaviour
{
    public bool enableBend = true;
    [Tooltip("Extra bounds inflation in world units to avoid CPU culling when bent.")]
    public Vector3 inflateBounds = new Vector3(5, 5, 5);

    TMP_Text _tmp;
    Matrix4x4 _localToWorld, _worldToLocal;

    // Match the controller’s global property IDs
    static readonly int ID_Strength   = Shader.PropertyToID("_WB_Strength_G");
    static readonly int ID_Radius     = Shader.PropertyToID("_WB_Radius_G");
    static readonly int ID_Axis       = Shader.PropertyToID("_WB_Axis_G");
    static readonly int ID_Origin     = Shader.PropertyToID("_WB_Origin_G");
    static readonly int ID_MaxDrop    = Shader.PropertyToID("_WB_MaxYDrop_G");
    static readonly int ID_BendStart  = Shader.PropertyToID("_WB_BendStart_G");
    static readonly int ID_BendEnd    = Shader.PropertyToID("_WB_BendEnd_G");
    static readonly int ID_Mask       = Shader.PropertyToID("_WB_ComponentMask_G");

    void OnEnable()
    {
        _tmp = GetComponent<TMP_Text>();
        if (_tmp == null) enabled = false;
        else _tmp.ForceMeshUpdate();
        ApplyInflatedBounds();
    }

    void OnValidate() => ApplyInflatedBounds();

    void LateUpdate()
    {
        if (!enableBend || _tmp == null) return;

        _localToWorld = transform.localToWorldMatrix;
        _worldToLocal = transform.worldToLocalMatrix;

        // Read globals (set by WorldBendGlobalController)
        float strength  = Shader.GetGlobalFloat(ID_Strength);
        float radius    = Mathf.Max(0.01f, Shader.GetGlobalFloat(ID_Radius));
        Vector3 axisWS  = Shader.GetGlobalVector(ID_Axis);
        axisWS = axisWS.sqrMagnitude > 1e-12f ? axisWS.normalized : Vector3.forward;
        Vector3 origin  = Shader.GetGlobalVector(ID_Origin);
        float maxDrop   = Shader.GetGlobalFloat(ID_MaxDrop);
        float bendStart = Shader.GetGlobalFloat(ID_BendStart);
        float bendEnd   = Shader.GetGlobalFloat(ID_BendEnd);
        Vector3 mask    = Shader.GetGlobalVector(ID_Mask);

        // Get current mesh data (all submeshes)
        _tmp.ForceMeshUpdate();
        var textInfo = _tmp.textInfo;

        for (int mi = 0; mi < textInfo.meshInfo.Length; mi++)
        {
            var meshInfo = textInfo.meshInfo[mi];
            var verts = meshInfo.vertices;
            if (verts == null || verts.Length == 0) continue;

            for (int i = 0; i < verts.Length; i++)
            {
                // local -> world
                Vector3 pWS = _localToWorld.MultiplyPoint3x4(verts[i]);

                // bend in world
                Vector3 delta = Vector3.Scale(pWS - origin, mask);
                float d  = Vector3.Dot(delta, axisWS);
                float ad = Mathf.Abs(d);

                float originFadeT = 1f;
                if (bendEnd > bendStart)
                    originFadeT = Mathf.InverseLerp(bendStart, bendEnd, ad);

                float sag = ArcSag(ad, radius) * strength;
                sag = Mathf.Min(sag, maxDrop);
                pWS.y -= sag * originFadeT;

                // world -> local
                verts[i] = _worldToLocal.MultiplyPoint3x4(pWS);
            }

            // push back to the mesh
            meshInfo.mesh.vertices = verts;
            meshInfo.mesh.RecalculateBounds(); // keep renderer from culling
            _tmp.UpdateGeometry(meshInfo.mesh, mi);
        }
    }

    static float ArcSag(float dist, float radius)
    {
        float R = Mathf.Max(0.001f, radius);
        float d = Mathf.Min(dist, R - 0.0001f);
        return R - Mathf.Sqrt(R * R - d * d);
    }

    void ApplyInflatedBounds()
    {
        // Try to inflate bounds on a MeshRenderer for world-space culling safety
        var mr = GetComponent<MeshRenderer>();
        if (mr)
        {
            var b = mr.localBounds;
            b.extents += inflateBounds;
            mr.localBounds = b;
        }
    }
}
