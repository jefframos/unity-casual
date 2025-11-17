using UnityEngine;

/// <summary>
/// Per-renderer override for the world bend camera cutout.
/// Attach this to a player mesh Renderer to make that instance ignore
/// the global camera cutout (by setting _CutoutIgnore = 1 via MaterialPropertyBlock).
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Renderer))]
public class WorldBendCutoutIgnore : MonoBehaviour
{
    [Tooltip("If true, this renderer will ignore the global camera cutout (per-instance).")]
    public bool ignoreCutout = true;

    // Name used in the shader: float _CutoutIgnore;
    private static readonly int CutoutIgnoreID = Shader.PropertyToID("_CutoutIgnore");

    private Renderer _renderer;
    private MaterialPropertyBlock _mpb;

    private void OnEnable()
    {
        EnsureRenderer();
        Apply();
    }

    private void OnValidate()
    {
        EnsureRenderer();
        Apply();
    }

    private void Reset()
    {
        EnsureRenderer();
        ignoreCutout = true;
        Apply();
    }

    private void OnDisable()
    {
        // When disabled, reset flag to 0 so this renderer behaves normally again.
        EnsureRenderer();
        SetCutoutIgnoreValue(0f);
    }

    private void EnsureRenderer()
    {
        if (_renderer == null)
            _renderer = GetComponent<Renderer>();

        if (_mpb == null)
            _mpb = new MaterialPropertyBlock();
    }

    private void Apply()
    {
        if (_renderer == null)
            return;

        SetCutoutIgnoreValue(ignoreCutout ? 1f : 0f);
    }

    private void SetCutoutIgnoreValue(float value)
    {
        if (_renderer == null)
            return;

        // Get existing MPB (so we don't clobber other per-object properties)
        _renderer.GetPropertyBlock(_mpb);
        _mpb.SetFloat(CutoutIgnoreID, value);
        _renderer.SetPropertyBlock(_mpb);
    }
}
