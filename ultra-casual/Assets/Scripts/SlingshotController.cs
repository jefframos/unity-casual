using UnityEngine;

public class SlingshotController4Pt : MonoBehaviour
{
    [Header("Poles / Band")]
    public Transform leftPole;
    public Transform rightPole;
    [Tooltip("Max pull radius (meters) around the midpoint between the two poles, on the poles' height plane.")]
    public float maxPullDistance = 5f;
    public float minPullDistance = 1f;

    [Header("Car")]
    public Rigidbody carRigidbody;        // car rigidbody
    public Transform carParent;           // the transform you move while aiming (often the car root)
    public Transform carLeftAnchor;       // left hook on the car (child transform)
    public Transform carRightAnchor;      // right hook on the car (child transform)

    [Header("Launch")]
    public float impulsePerMeter = 300f;  // impulse scale
    [Tooltip("Optional: reference forward to bias launch direction (e.g., ramp forward).")]
    public Transform rampForwardRef;

    [Header("Orientation")]
    public Vector3 upAxis = Vector3.up;   // world up (or ramp normal if you have it)
    [Tooltip("Max yaw (left/right) from baseline forward while aiming & launching.")]
    public float maxYawDegrees = 60f;

    [Header("Tuning")]
    [Tooltip("If forward feels flipped for your pole layout, toggle this.")]
    public bool flipBaselineForward = false;

    [Header("Rendering (optional)")]
    public LineRenderer leftBand;         // draws leftPole -> carLeftAnchor
    public LineRenderer rightBand;        // draws rightPole -> carRightAnchor

    private bool isDragging;
    private bool isAiming;
    private float polesPlaneY;
    private Vector3 pullPoint;            // world-space point you drag to
    private Vector3 lastClampedDir = Vector3.forward;

    private void OnValidate()
    {
        if (maxPullDistance < 0f) maxPullDistance = 0f;
        if (minPullDistance < 0f) minPullDistance = 0f;
        if (minPullDistance > maxPullDistance) minPullDistance = maxPullDistance;
    }

    private void Awake()
    {
        if (!carRigidbody && carParent != null)
        {
            carRigidbody = carParent.GetComponent<Rigidbody>();
        }

        // Auto-create band renderers if not assigned
        if (leftBand == null)
        {
            var go = new GameObject("LeftBand");
            go.transform.SetParent(transform, false);
            leftBand = go.AddComponent<LineRenderer>();
            leftBand.positionCount = 2;
            leftBand.enabled = false;
        }
        else
        {
            leftBand.positionCount = 2;
            leftBand.enabled = false;
        }

        if (rightBand == null)
        {
            var go = new GameObject("RightBand");
            go.transform.SetParent(transform, false);
            rightBand = go.AddComponent<LineRenderer>();
            rightBand.positionCount = 2;
            rightBand.enabled = false;
        }
        else
        {
            rightBand.positionCount = 2;
            rightBand.enabled = false;
        }

        if (leftPole && rightPole)
        {
            polesPlaneY = 0.5f * (leftPole.position.y + rightPole.position.y);
        }
    }

    private void Start()
    {
        ResetToSlingshot();
    }

    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            isDragging = true;
            EnterAimingState();
            EnableBands(true);
        }

        if (Input.GetMouseButtonUp(0) && isDragging)
        {
            isDragging = false;
            EnableBands(false);
            ExitAimingAndLaunch();
        }

        if (!isDragging && !isAiming)
        {
            return;
        }

        // --- Update pull point on poles plane ---
        Vector3 center = GetBandCenter();
        Vector3 mouseWorld = GetMouseWorldOnPolesPlane();

        // Vector from center to mouse (projected to plane)
        Vector3 fromCenter = mouseWorld - center;
        fromCenter = Vector3.ProjectOnPlane(fromCenter, upAxis);

        // Baseline forward on the same plane
        Vector3 baselineFwd = GetPreferredForward(center);
        baselineFwd = Vector3.ProjectOnPlane(baselineFwd, upAxis).normalized;

        // --- Allow only back/side pull relative to baseline forward ---
        float sForward = Vector3.Dot(fromCenter, baselineFwd);
        if (sForward > 0f)
        {
            // Remove forward component; keep only back/side so you can’t pull forward
            fromCenter -= baselineFwd * sForward;
        }

        // --- Clamp pull radius BETWEEN min and max during drag ---
        float mag = fromCenter.magnitude;
        float minR = Mathf.Clamp(minPullDistance, 0f, maxPullDistance);
        float maxR = Mathf.Max(0.01f, maxPullDistance);

        Vector3 dir;
        if (mag < 1e-6f)
        {
            // If direction is degenerate (e.g., clicked near center or forward-only),
            // force a backward direction so min radius makes sense.
            dir = -baselineFwd;
        }
        else
        {
            dir = fromCenter / mag;
        }

        float clampedR = Mathf.Clamp(mag, minR, maxR);
        fromCenter = dir * clampedR;

        pullPoint = center + fromCenter;

        // --- Move car so the MIDPOINT of the two car anchors sits at pullPoint ---
        if (carParent != null && carLeftAnchor != null && carRightAnchor != null)
        {
            Vector3 currentMid = (carLeftAnchor.position + carRightAnchor.position) * 0.5f;
            Vector3 delta = pullPoint - currentMid;
            carParent.position += delta;
        }

        // --- Orient car with yaw clamp, store last clamped direction ---
        lastClampedDir = AlignCarToLaunchDirection(center, pullPoint);

        // --- Draw each band: pole -> corresponding car anchor ---
        if (leftPole && carLeftAnchor)
        {
            leftBand.SetPosition(0, leftPole.position);
            leftBand.SetPosition(1, carLeftAnchor.position);
        }

        if (rightPole && carRightAnchor)
        {
            rightBand.SetPosition(0, rightPole.position);
            rightBand.SetPosition(1, carRightAnchor.position);
        }
    }

    // ----------------- Public API -----------------

    // Snap the car to the rest position on the slingshot (no pull), anchors centered
    public void ResetToSlingshot()
    {
        if (!leftPole || !rightPole || !carParent || !carLeftAnchor || !carRightAnchor)
        {
            Debug.LogWarning("[SlingshotController4Pt] Missing references for ResetToSlingshot.");
            return;
        }

        polesPlaneY = 0.5f * (leftPole.position.y + rightPole.position.y);

        Vector3 center = GetBandCenter();
        pullPoint = center; // rest

        // Center the car so the midpoint of anchors sits at center
        Vector3 currentMid = (carLeftAnchor.position + carRightAnchor.position) * 0.5f;
        carParent.position += (center - currentMid);

        // Face a sensible forward
        Vector3 forward = GetPreferredForward(center);
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = carParent.forward;
        }
        carParent.rotation = Quaternion.LookRotation(forward, upAxis);
        lastClampedDir = forward;

        // Draw bands at rest
        EnableBands(true);
        leftBand.SetPosition(0, leftPole.position);
        leftBand.SetPosition(1, carLeftAnchor.position);
        rightBand.SetPosition(0, rightPole.position);
        rightBand.SetPosition(1, carRightAnchor.position);
    }

    // ----------------- Internals -----------------

    private void EnterAimingState()
    {
        isAiming = true;

        if (carRigidbody != null)
        {
            carRigidbody.isKinematic = true;
            carRigidbody.useGravity = false;
            carRigidbody.linearVelocity = Vector3.zero;
            carRigidbody.angularVelocity = Vector3.zero;
        }
    }

    private void ExitAimingAndLaunch()
    {
        isAiming = false;

        if (!carRigidbody)
        {
            return;
        }

        // Re-enable physics
        carRigidbody.isKinematic = false;
        carRigidbody.useGravity = true;

        Vector3 center = GetBandCenter();

        // Raw desired launch dir (snap back)
        Vector3 rawDir = (center - pullPoint);
        rawDir = Vector3.ProjectOnPlane(rawDir, upAxis);

        if (rawDir.sqrMagnitude < 0.0001f)
        {
            return;
        }

        rawDir.Normalize();

        // Baseline forward
        Vector3 baselineFwd = GetPreferredForward(center);
        baselineFwd = Vector3.ProjectOnPlane(baselineFwd, upAxis).normalized;

        // Clamp yaw to limit launch angle
        Vector3 clampedDir = ClampYawAroundUp(baselineFwd, rawDir, maxYawDegrees, upAxis);

        // Optional: bias along ramp forward
        if (rampForwardRef != null)
        {
            Vector3 rampFwd = Vector3.ProjectOnPlane(rampForwardRef.forward, upAxis).normalized;
            clampedDir = Vector3.Slerp(clampedDir, rampFwd, 0.25f).normalized;
        }

        // Clamp the *launch power* to min/max pull too
        float dist = Vector3.Distance(center, pullPoint);
        float pullDistance = Mathf.Clamp(dist, Mathf.Clamp(minPullDistance, 0f, maxPullDistance), maxPullDistance);
        float impulse = pullDistance * impulsePerMeter;

        // Clean & launch
        carRigidbody.linearVelocity = Vector3.zero;
        carRigidbody.angularVelocity = Vector3.zero;
        carRigidbody.AddForce(clampedDir * impulse, ForceMode.Impulse);
    }

    // Returns the clamped (used) forward dir
    private Vector3 AlignCarToLaunchDirection(Vector3 center, Vector3 currentPull)
    {
        if (!carParent)
        {
            return lastClampedDir;
        }

        // Desired launch direction (from pull to center)
        Vector3 desiredDir = center - currentPull;
        desiredDir = Vector3.ProjectOnPlane(desiredDir, upAxis);

        if (desiredDir.sqrMagnitude < 0.0001f)
        {
            desiredDir = GetPreferredForward(center);
            desiredDir = Vector3.ProjectOnPlane(desiredDir, upAxis);
        }

        if (desiredDir.sqrMagnitude < 0.0001f)
        {
            return lastClampedDir;
        }

        desiredDir.Normalize();

        // Baseline forward
        Vector3 baselineFwd = GetPreferredForward(center);
        baselineFwd = Vector3.ProjectOnPlane(baselineFwd, upAxis).normalized;

        // Clamp yaw
        Vector3 clampedDir = ClampYawAroundUp(baselineFwd, desiredDir, maxYawDegrees, upAxis);

        // Apply rotation
        Quaternion target = Quaternion.LookRotation(clampedDir, upAxis);
        carParent.rotation = target;

        return clampedDir;
    }

    private static Vector3 ClampYawAroundUp(Vector3 baselineFwd, Vector3 desiredDir, float maxYawDeg, Vector3 up)
    {
        if (baselineFwd.sqrMagnitude < 0.0001f)
        {
            return desiredDir;
        }

        float angle = Vector3.SignedAngle(baselineFwd, desiredDir, up);
        float clamped = Mathf.Clamp(angle, -maxYawDeg, maxYawDeg);
        Quaternion yawRot = Quaternion.AngleAxis(clamped, up);
        return (yawRot * baselineFwd).normalized;
    }

    private Vector3 GetPreferredForward(Vector3 center)
    {
        if (!leftPole || !rightPole)
        {
            return Vector3.forward;
        }

        // Across the poles on the aiming plane
        Vector3 across = rightPole.position - leftPole.position;
        Vector3 side = Vector3.ProjectOnPlane(across, upAxis);

        // Use Cross(side, upAxis) so forward points consistently "out" from the band
        Vector3 forward = Vector3.Cross(side, upAxis).normalized;

        if (flipBaselineForward)
        {
            forward = -forward;
        }

        if (rampForwardRef != null && forward.sqrMagnitude < 0.01f)
        {
            forward = Vector3.ProjectOnPlane(rampForwardRef.forward, upAxis).normalized;
        }

        return forward;
    }

    private void EnableBands(bool on)
    {
        if (leftBand != null) leftBand.enabled = on;
        if (rightBand != null) rightBand.enabled = on;
    }

    private Vector3 GetBandCenter()
    {
        if (!leftPole || !rightPole)
        {
            return transform.position;
        }

        return 0.5f * (leftPole.position + rightPole.position);
    }

    private Vector3 GetMouseWorldOnPolesPlane()
    {
        if (!leftPole || !rightPole)
        {
            return transform.position;
        }

        Vector3 planePoint = new Vector3(0f, polesPlaneY, 0f);
        Plane plane = new Plane(upAxis.normalized, planePoint);
        Ray ray = Camera.main != null ? Camera.main.ScreenPointToRay(Input.mousePosition) : new Ray(Vector3.zero, Vector3.forward);

        if (plane.Raycast(ray, out float enter))
        {
            return ray.GetPoint(enter);
        }

        return GetBandCenter();
    }
}
