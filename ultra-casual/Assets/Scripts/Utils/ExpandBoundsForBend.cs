using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public class ExpandBoundsForBend : MonoBehaviour
{
    [Tooltip("Extra local-space padding added to the mesh bounds (x,y,z).")]
    public Vector3 extraExtents = new Vector3(2, 10, 2); // tweak for your max bend drop/reach

    [Tooltip("If true, tries to derive Y padding from your shader globals (_WB_MaxYDrop_G).")]
    public bool deriveFromShader = true;

    // cache
    Mesh _instancedMesh;
    Bounds _originalBounds;
    bool _haveOriginal;

    void OnEnable() => Apply();

    void OnValidate() => Apply();

    void Update()
    {
        // Keep it robust in Scene view while editing
        Apply();
    }

    void Apply()
    {
        // Try Skinned first
        var smr = GetComponent<SkinnedMeshRenderer>();
        if (smr != null)
        {
            var b = smr.sharedMesh ? smr.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
            var ext = ComputeExtents(b.extents);
            smr.localBounds = new Bounds(b.center, ext * 2f);
            // If animations stop when offscreen, this helps, though not required:
            smr.updateWhenOffscreen = true;
            return;
        }

        // Regular MeshRenderer + MeshFilter
        var mf = GetComponent<MeshFilter>();
        if (mf == null || (mf.sharedMesh == null && mf.mesh == null)) return;

        // Use an instanced mesh so we don't mutate a shared asset
        if (_instancedMesh == null)
        {
            _instancedMesh = mf.sharedMesh; // this duplicates sharedMesh if needed
            if (_instancedMesh != null)
            {
                _originalBounds = _instancedMesh.bounds;
                _haveOriginal = true;
            }
        }

        if (_instancedMesh == null) return;

        var baseBounds = _haveOriginal ? _originalBounds : _instancedMesh.bounds;
        var newExtents = ComputeExtents(baseBounds.extents);
        _instancedMesh.bounds = new Bounds(baseBounds.center, newExtents * 2f);
    }

    Vector3 ComputeExtents(Vector3 baseExtents)
    {
        float yPad = extraExtents.y;
        if (deriveFromShader)
        {
            // pull the same global your shader uses for max vertical sag
            float maxDrop = Shader.GetGlobalFloat("_WB_MaxYDrop_G");
            if (maxDrop > 0f) yPad = Mathf.Max(yPad, maxDrop * 1.05f); // small safety factor
        }
        // X/Z padding: how far you can bend along your axis — usually much smaller than Y.
        return new Vector3(
            baseExtents.x + extraExtents.x,
            baseExtents.y + yPad,
            baseExtents.z + extraExtents.z
        );
    }

    void OnDisable()
    {
        // optional: restore original bounds when disabling
        if (_instancedMesh != null && _haveOriginal)
            _instancedMesh.bounds = _originalBounds;
    }
}
