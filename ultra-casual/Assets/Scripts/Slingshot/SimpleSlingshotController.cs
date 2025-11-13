using UnityEngine;

/// <summary>
/// Very simple slingshot controller:
/// - Click & drag from the slingshot center to pull back.
/// - Release to launch with deterministic velocity.
/// - Calls ISlingshotable.BeginDeterministicFlight(velocity).
/// </summary>
[DisallowMultipleComponent]
public class SimpleSlingshotController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("World-space point that represents the slingshot origin (the band center).")]
    public Transform slingshotCenter;

    [Tooltip("Object that can be launched. Must implement ISlingshotable.")]
    public MonoBehaviour slingshotableObject; // cast to ISlingshotable

    [Tooltip("Camera used to raycast the mouse onto a plane.")]
    public Camera mainCamera;

    [Header("Pull Settings")]
    [Tooltip("Maximum distance you can pull back (in meters).")]
    public float maxPullDistance = 5f;

    [Tooltip("Minimum distance required to actually fire (in meters).")]
    public float minPullDistance = 0.5f;

    [Tooltip("Virtual distance used for scaling speed (can be > maxPullDistance).")]
    public float maxPullVirtualDistance = 10f;

    [Header("Angles")]
    [Tooltip("Max yaw (left/right) away from slingshot forward.")]
    public float maxYawDegrees = 60f;

    [Header("Launch Speed")]
    [Tooltip("Speed gain per unit pull (before scaling).")]
    public float impulsePerMeter = 10f;

    [Tooltip("Minimum speed even for very small pulls.")]
    public float minImpulse = 5f;

    [Tooltip("Final scale applied to the impulsePerMeter result.")]
    public float impulsePerMeterScale = 1f;

    [Header("Optional Ramp Bias")]
    [Tooltip("If assigned, yaw will be biased towards this forward (e.g. ramp direction).")]
    public Transform rampForwardRef;

    private ISlingshotable _target;
    private bool _isDragging;
    private Vector3 _pullPointWorld;  // Where the player is pulling to (on the plane)

    private void Awake()
    {
        if (!mainCamera)
            mainCamera = Camera.main;

        _target = slingshotableObject as ISlingshotable;
        if (_target == null)
        {
            Debug.LogError("[SlingshotController] slingshotableObject must implement ISlingshotable.");
        }

        if (!slingshotCenter)
        {
            Debug.LogError("[SlingshotController] Missing slingshotCenter reference.");
        }
    }

    private void Update()
    {
        if (_target == null || slingshotCenter == null || mainCamera == null)
            return;

        if (Input.GetMouseButtonDown(0))
        {
            _isDragging = true;
        }

        if (Input.GetMouseButton(0) && _isDragging)
        {
            UpdatePullPoint();
            DebugDrawPull();
        }

        if (Input.GetMouseButtonUp(0) && _isDragging)
        {
            _isDragging = false;
            LaunchFromCurrentPull();
        }
    }

    /// <summary>
    /// Projects the mouse position onto a horizontal plane through the slingshot center.
    /// </summary>
    private void UpdatePullPoint()
    {
        Plane plane = new Plane(Vector3.up, slingshotCenter.position);
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);

        if (plane.Raycast(ray, out float enter))
        {
            _pullPointWorld = ray.GetPoint(enter);
        }
    }

    /// <summary>
    /// Debug line from center to pull point so you can see the pull in Scene/Game view.
    /// </summary>
    private void DebugDrawPull()
    {
        Vector3 center = slingshotCenter.position;
        Debug.DrawLine(center, _pullPointWorld, Color.yellow);
    }

    private void LaunchFromCurrentPull()
    {
        Vector3 center = slingshotCenter.position;

        // Pull vector from center to current pull point on the plane
        Vector3 pullVec = _pullPointWorld - center;

        // Only use horizontal (XZ) component
        pullVec = new Vector3(pullVec.x, 0f, pullVec.z);

        // If too small, do nothing
        if (pullVec.sqrMagnitude < 0.0001f)
            return;

        // We want direction FROM pull point TOWARDS center (like a real slingshot)
        Vector3 rawDir = -pullVec.normalized;

        // Baseline forward is the slingshot's forward (flattened on XZ)
        Vector3 baselineFwd = slingshotCenter.forward;
        baselineFwd = new Vector3(baselineFwd.x, 0f, baselineFwd.z).normalized;
        if (baselineFwd.sqrMagnitude < 0.0001f)
            baselineFwd = rawDir;

        // Clamp yaw relative to baseline
        Vector3 clampedDir = ClampYawAroundUp(baselineFwd, rawDir, maxYawDegrees, Vector3.up);

        // Optional bias towards ramp forward
        if (rampForwardRef != null)
        {
            Vector3 rampFwd = rampForwardRef.forward;
            rampFwd = new Vector3(rampFwd.x, 0f, rampFwd.z).normalized;
            if (rampFwd.sqrMagnitude > 0.0001f)
            {
                clampedDir = Vector3.Slerp(clampedDir, rampFwd, 0.25f).normalized;
            }
        }

        // Distance pulled (only back component along baseline forward)
        float backAmount = Mathf.Max(0f, Vector3.Dot(-pullVec.normalized, baselineFwd)) * pullVec.magnitude;
        float pullDistance = Mathf.Clamp(
            backAmount,
            Mathf.Clamp(minPullDistance, 0f, maxPullVirtualDistance),
            maxPullVirtualDistance
        );

        if (pullDistance < minPullDistance)
        {
            // Not enough pull to launch
            return;
        }

        // Convert pull distance to speed
        float baseImpulse = impulsePerMeter * pullDistance;
        float scaledImpulse = baseImpulse * impulsePerMeterScale;
        float finalSpeed = Mathf.Max(minImpulse, scaledImpulse);

        // Deterministic launch velocity
        Vector3 launchVelocity = clampedDir * finalSpeed;

        // Tell the target to start deterministic flight along the ramp
        _target.BeginDeterministicFlight(launchVelocity);
    }

    /// <summary>
    /// Clamp desiredDir around baselineFwd by a max yaw angle around 'up'.
    /// </summary>
    private static Vector3 ClampYawAroundUp(Vector3 baselineFwd, Vector3 desiredDir, float maxYawDeg, Vector3 up)
    {
        baselineFwd = baselineFwd.normalized;
        desiredDir = desiredDir.normalized;

        if (baselineFwd.sqrMagnitude < 0.0001f)
            return desiredDir;

        float angle = Vector3.SignedAngle(baselineFwd, desiredDir, up);
        float clamped = Mathf.Clamp(angle, -maxYawDeg, maxYawDeg);
        Quaternion yawRot = Quaternion.AngleAxis(clamped, up);
        return (yawRot * baselineFwd).normalized;
    }
}


