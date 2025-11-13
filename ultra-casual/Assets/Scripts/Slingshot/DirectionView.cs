using UnityEngine;

public enum StretchAxis
{
    X,
    Y,
    Z
}

[DisallowMultipleComponent]
public class DirectionView : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("The root/player transform this indicator belongs to.")]
    public Transform playerRoot;

    [Tooltip("Optional child visual to move/rotate. If null, this GameObject is used.")]
    public Transform visual;

    [Header("Position & Rotation")]
    [Tooltip("Distance in front of the player where the indicator should appear.")]
    public float distanceFromPlayer = 1.5f;

    [Tooltip("If true, use the player's up as 'up' when rotating. Otherwise use world up.")]
    public bool usePlayerUp = true;

    [Header("Stretch")]
    [Tooltip("Minimum stretch factor when pullForce = 0.")]
    public float minStretch = 0.5f;

    [Tooltip("Maximum stretch factor when pullForce = 1.")]
    public float maxStretch = 2.0f;

    [Tooltip("Axis along which the visual will stretch.")]
    public StretchAxis stretchAxis = StretchAxis.Z;

    private Vector3 _currentDirection = Vector3.forward;
    private bool _visible;
    private float _currentPullForce; // normalized 0–1

    private Vector3 _baseScale;
    private bool _initialized;

    private Transform TargetTransform => visual != null ? visual : transform;

    private void Reset()
    {
        playerRoot = transform;
        visual = transform;
    }

    private void Awake()
    {
        CacheBaseScale();
    }

    private void OnValidate()
    {
        if (maxStretch < 0f) maxStretch = 0f;
        if (minStretch < 0f) minStretch = 0f;
        if (maxStretch < minStretch) maxStretch = minStretch;

        CacheBaseScale();
    }

    private void CacheBaseScale()
    {
        var t = TargetTransform;
        if (t == null) return;
        _baseScale = t.localScale;
        _initialized = true;
    }

    /// <summary>
    /// Set the direction (in world space) the indicator should point to,
    /// and how strong the pull is (0–1).
    /// Also makes the indicator visible.
    /// </summary>
    public void SetDirection(Vector3 worldDirection, float pullForce)
    {
        if (worldDirection.sqrMagnitude < 0.0001f)
            return;

        if (!_initialized)
        {
            CacheBaseScale();
        }

        _currentDirection = worldDirection.normalized;
        _currentPullForce = Mathf.Clamp01(pullForce);
        _visible = true;

        var t = TargetTransform;
        if (t != null)
            t.gameObject.SetActive(true);

        UpdateTransform();
    }

    /// <summary>
    /// Hide the indicator.
    /// </summary>
    public void Hide()
    {
        _visible = false;
        var t = TargetTransform;
        if (t != null)
            t.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (!_visible)
            return;

        UpdateTransform();
    }

    private void UpdateTransform()
    {
        if (playerRoot == null)
            return;

        var t = TargetTransform;
        if (t == null)
            return;

        if (!_initialized)
        {
            CacheBaseScale();
        }

        Vector3 origin = playerRoot.position;
        Vector3 up = usePlayerUp ? playerRoot.up : Vector3.up;

        // Position a bit in front of the player in the aim direction
        Vector3 pos = origin + _currentDirection * distanceFromPlayer;
        t.position = pos;
        t.rotation = Quaternion.LookRotation(_currentDirection, up);

        // --- Stretch based on pull force ---
        float stretch = Mathf.Lerp(minStretch, maxStretch, _currentPullForce);
        Vector3 scaled = _baseScale;

        switch (stretchAxis)
        {
            case StretchAxis.X:
                scaled.x = _baseScale.x * stretch;
                break;
            case StretchAxis.Y:
                scaled.y = _baseScale.y * stretch;
                break;
            case StretchAxis.Z:
                scaled.z = _baseScale.z * stretch;
                break;
        }

        t.localScale = scaled;
    }
}
