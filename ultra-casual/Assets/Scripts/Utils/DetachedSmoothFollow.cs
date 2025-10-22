using UnityEngine;

/// <summary>
/// Detaches to the scene on start and smoothly follows a target.
/// Attach this to any GameObject, assign a Target, and hit Play.
/// </summary>
[DisallowMultipleComponent]
public class DetachedSmoothFollow : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;

    [Tooltip("Local-space offset from the target (uses target's rotation).")]
    [SerializeField] private Vector3 positionOffset = Vector3.zero;

    [Header("Follow Mode")]
    [Tooltip("Follow position every frame.")]
    [SerializeField] private bool followPosition = true;

    [Tooltip("Follow rotation every frame.")]
    [SerializeField] private bool followRotation = false;

    [Tooltip("Extra rotation applied on top of target rotation (Euler degrees).")]
    [SerializeField] private Vector3 rotationOffsetEuler = Vector3.zero;

    [Header("Smoothing")]
    [Tooltip("If true, uses SmoothDamp for position (time-based). If false, uses Lerp (speed-based).")]
    [SerializeField] private bool useSmoothDamp = true;

    [Tooltip("For SmoothDamp: approximate time to reach the target.")]
    [SerializeField] private float smoothTime = 0.15f;

    [Tooltip("For Lerp: higher is faster (units per second factor).")]
    [SerializeField] private float lerpSpeed = 8f;

    [Tooltip("For rotation: higher is faster (slerp speed per second).")]
    [SerializeField] private float rotationLerpSpeed = 10f;

    // SmoothDamp velocity cache
    private Vector3 _posVel;

    /// <summary>Set/replace the target at runtime.</summary>
    public void SetTarget(Transform newTarget) => target = newTarget;

    private void Start()
    {
        // Detach to the scene root on start (keep world position/rotation)
        transform.SetParent(null, true);
    }

    private void LateUpdate()
    {
        if (!target) return;

        // Desired position in world space with target-relative offset
        if (followPosition)
        {
            Vector3 desiredPos = target.position + target.TransformDirection(positionOffset);

            if (useSmoothDamp)
            {
                transform.position = Vector3.SmoothDamp(
                    transform.position,
                    desiredPos,
                    ref _posVel,
                    Mathf.Max(0.0001f, smoothTime)
                );
            }
            else
            {
                float t = 1f - Mathf.Exp(-Mathf.Max(0f, lerpSpeed) * Time.deltaTime); // framerate independent
                transform.position = Vector3.Lerp(transform.position, desiredPos, t);
            }
        }

        // Desired rotation with optional extra offset
        if (followRotation)
        {
            Quaternion desiredRot = target.rotation * Quaternion.Euler(rotationOffsetEuler);
            float tRot = 1f - Mathf.Exp(-Mathf.Max(0f, rotationLerpSpeed) * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, tRot);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        smoothTime = Mathf.Max(0.0f, smoothTime);
        lerpSpeed = Mathf.Max(0.0f, lerpSpeed);
        rotationLerpSpeed = Mathf.Max(0.0f, rotationLerpSpeed);
    }
#endif
}
