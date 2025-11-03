using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public class TilingSpriteScroller : MonoBehaviour
{
    [Header("Refs")]
    public SpriteRenderer spriteRenderer;

    [Header("Scroll Settings")]
    public Vector2 direction = Vector2.right;     // normalized dir of scroll
    public float speed = 1f;                      // units per second
    public bool useUnscaledTime = false;

    [Header("Tiling/Offset (optional)")]
    public Vector2 tiling = Vector2.one;          // extra per-axis tile
    public Vector2 offset = Vector2.zero;         // starting offset

    // Optional: if you want to lock direction in local space (e.g., rotate object to change scroll)
    public bool localSpaceDirection = false;

    static readonly int ID_ScrollDir = Shader.PropertyToID("_ScrollDir");
    static readonly int ID_ScrollSpeed = Shader.PropertyToID("_ScrollSpeed");
    static readonly int ID_Tiling = Shader.PropertyToID("_Tiling");
    static readonly int ID_Offset = Shader.PropertyToID("_Offset");

    MaterialPropertyBlock _mpb;

    void Reset()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    void OnEnable()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        Apply();
    }

    void OnValidate()
    {
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        Apply();
    }

    void Update()
    {
        Apply();
    }

    void Apply()
    {
        if (spriteRenderer == null || spriteRenderer.sharedMaterial == null) return;

        // Build direction (optionally rotate by transform)
        Vector2 dir = direction;
        if (localSpaceDirection)
        {
            // Rotate a unit vector by the object's Z rotation (2D style)
            float rad = transform.eulerAngles.z * Mathf.Deg2Rad;
            var rot = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            // Project the original direction onto rotated axes
            dir = new Vector2(
                direction.x * rot.x - direction.y * rot.y,
                direction.x * rot.y + direction.y * rot.x
            );
        }

        spriteRenderer.GetPropertyBlock(_mpb);

        _mpb.SetVector(ID_ScrollDir, new Vector4(dir.x, dir.y, 0f, 0f));
        _mpb.SetFloat(ID_ScrollSpeed, Mathf.Max(0f, speed));
        _mpb.SetVector(ID_Tiling, new Vector4(Mathf.Max(0.0001f, tiling.x), Mathf.Max(0.0001f, tiling.y), 0f, 0f));

        // If you want offset to accumulate with time externally, you could integrate here.
        _mpb.SetVector(ID_Offset, new Vector4(offset.x, offset.y, 0f, 0f));

        spriteRenderer.SetPropertyBlock(_mpb);

        // NOTE: The shader itself uses _Time.y to advance scroll.
        // If you need unscaled time, we can emulate by pushing our own time into Offset:
        if (useUnscaledTime)
        {
            float t = Time.unscaledTime;
            var extra = dir * speed * t;
            _mpb.SetVector(ID_Offset, new Vector4(offset.x + extra.x, offset.y + extra.y, 0f, 0f));
            spriteRenderer.SetPropertyBlock(_mpb);
        }
    }
}
