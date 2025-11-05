using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("FX/Pulse Scale")]
public class PulseScale : MonoBehaviour
{
    [Header("Pulse Settings")]
    [Tooltip("Base scale (default = starting localScale).")]
    public Vector3 baseScale = Vector3.one;

    [Tooltip("Amplitude of the pulse for each axis.")]
    public Vector3 amplitude = new Vector3(0.1f, 0.1f, 0.1f);

    [Tooltip("Speed of the pulse oscillation.")]
    public float speed = 2f;

    [Header("Axis Mode")]
    [Tooltip("If true, uses cosine instead of sine for the X axis.")]
    public bool useCosineX = false;

    [Tooltip("If true, uses cosine instead of sine for the Y axis.")]
    public bool useCosineY = false;

    [Tooltip("If true, uses cosine instead of sine for the Z axis.")]
    public bool useCosineZ = false;

    private float _timeOffset;

    private void Awake()
    {
        // Start with the GameObject’s current scale as baseline
        if (baseScale == Vector3.one)
        {
            baseScale = transform.localScale;
        }

        // Add a random offset so multiple objects don’t pulse in sync
        _timeOffset = Random.value * Mathf.PI * 2f;
    }

    private void Update()
    {
        float t = Time.time * speed + _timeOffset;

        float sx = baseScale.x + amplitude.x * (useCosineX ? Mathf.Cos(t) : Mathf.Sin(t));
        float sy = baseScale.y + amplitude.y * (useCosineY ? Mathf.Cos(t) : Mathf.Sin(t));
        float sz = baseScale.z + amplitude.z * (useCosineZ ? Mathf.Cos(t) : Mathf.Sin(t));

        transform.localScale = new Vector3(sx, sy, sz);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Ensure amplitude never goes negative (not harmful but clearer)
        amplitude.x = Mathf.Max(amplitude.x, 0f);
        amplitude.y = Mathf.Max(amplitude.y, 0f);
        amplitude.z = Mathf.Max(amplitude.z, 0f);
        speed = Mathf.Max(speed, 0f);
    }
#endif
}
