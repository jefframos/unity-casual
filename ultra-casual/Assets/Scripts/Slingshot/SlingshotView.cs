using UnityEngine;

[DisallowMultipleComponent]
public class SlingshotView : MonoBehaviour
{
    [Header("Poles / Band")]
    public Transform leftPole;
    public Transform rightPole;

    [Header("Orientation")]
    public Vector3 upAxis = Vector3.up;
    [Tooltip("If forward feels flipped for your pole layout, toggle this.")]
    public bool flipBaselineForward = false;

    [Header("Rendering (optional)")]
    public LineRenderer leftBand;   // draws leftPole -> pouch/anchor
    public LineRenderer rightBand;  // draws rightPole -> pouch/anchor

    [Header("Band Visibility")]
    public bool bandsAlwaysVisible = true;

    [Header("Idle Pouch")]
    [Tooltip("Optional forward offset (in meters) from band center when idle.")]
    public float idlePouchForwardOffset = 0.0f;

    [Header("Snap Animation")]
    [Tooltip("Seconds for the snap-back animation.")]
    public float snapDuration = 0.15f;
    [Tooltip("Overshoot amount (0 = none, 0.1 = small).")]
    [Range(0f, 0.5f)] public float snapOvershoot = 0.08f;
    public AnimationCurve snapCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private float _polesPlaneY;

    // --- Transient pouch override state ---
    private bool _overridePouch;
    private Vector3 _pouchPos;     // current animated pouch position
    private Vector3 _snapStart;    // where we were pulled to
    private Vector3 _snapEnd;      // where we should end (idle)
    private float _snapClock;
    private float _snapTotal;

    private void Awake()
    {
        if (leftPole && rightPole)
        {
            _polesPlaneY = 0.5f * (leftPole.position.y + rightPole.position.y);
        }

        EnsureBand(ref leftBand, "LeftBand");
        EnsureBand(ref rightBand, "RightBand");

        // Keep the bands visible at all times if requested.
        if (bandsAlwaysVisible)
        {
            SetBandsVisible(true);
        }
    }

    private void Update()
    {
        // Advance snap animation if active
        if (_overridePouch)
        {
            _snapClock += Time.deltaTime;
            float t = (_snapTotal <= 1e-5f) ? 1f : Mathf.Clamp01(_snapClock / _snapTotal);
            float w = snapCurve != null ? snapCurve.Evaluate(t) : t;
            _pouchPos = Vector3.LerpUnclamped(_snapStart, _snapEnd, w);

            if (t >= 1f)
            {
                _overridePouch = false; // resume drawing from real anchors
            }
        }
    }

    private void EnsureBand(ref LineRenderer lr, string name)
    {
        if (lr == null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            lr = go.AddComponent<LineRenderer>();
        }
        lr.positionCount = 2;
        lr.enabled = bandsAlwaysVisible; // default to visible if always-on
    }

    public void SetBandsVisible(bool on)
    {
        // If we must keep them always visible, ignore attempts to hide.
        if (bandsAlwaysVisible) on = true;

        if (leftBand) leftBand.enabled = on;
        if (rightBand) rightBand.enabled = on;
    }

    public Vector3 GetBandCenter()
    {
        if (!leftPole || !rightPole)
        {
            return transform.position;
        }
        return 0.5f * (leftPole.position + rightPole.position);
    }

    public Vector3 GetPreferredForward()
    {
        if (!leftPole || !rightPole)
        {
            return Vector3.forward;
        }

        Vector3 across = rightPole.position - leftPole.position;
        Vector3 side = Vector3.ProjectOnPlane(across, upAxis);
        Vector3 forward = Vector3.Cross(side, upAxis).normalized; // stable “out of band”
        if (flipBaselineForward) forward = -forward;
        return forward.sqrMagnitude > 1e-6f ? forward : Vector3.forward;
    }

    public Vector3 GetMouseWorldOnPolesPlane(Camera cam)
    {
        if (!leftPole || !rightPole)
        {
            return transform.position;
        }

        Vector3 planePoint = new Vector3(0f, _polesPlaneY, 0f);
        Plane plane = new Plane(upAxis.normalized, planePoint);
        Ray ray = cam != null ? cam.ScreenPointToRay(Input.mousePosition) : new Ray(Vector3.zero, Vector3.forward);

        if (plane.Raycast(ray, out float enter))
        {
            return ray.GetPoint(enter);
        }

        return GetBandCenter();
    }

    public Vector3 ClampPointBetweenPoles(Vector3 point, float endInset = 0f)
    {
        // Ensure we work on the slingshot plane
        Vector3 up = upAxis.normalized;

        Vector3 A = leftPole.position;
        Vector3 B = rightPole.position;

        // Project everything to plane
        Vector3 AP = Vector3.ProjectOnPlane(point - A, up);
        Vector3 AB = Vector3.ProjectOnPlane(B - A, up);

        float len = AB.magnitude;
        if (len < 1e-4f) return point; // degenerate poles

        Vector3 side = AB / len;

        // Scalar position along segment AB from A
        float t = Vector3.Dot(AP, side);

        // Optional inset to keep off the exact tips (e.g. rope thickness)
        float minT = Mathf.Clamp(endInset, 0f, len);
        float maxT = Mathf.Clamp(len - endInset, 0f, len);
        t = Mathf.Clamp(t, minT, maxT);

        // Rebuild: keep the component perpendicular to AB (i.e., your "back" distance)
        Vector3 along = A + side * t;
        Vector3 perp = Vector3.ProjectOnPlane((A + AP) - along, side); // remove along-side, keep perpendicular/back
        return along + perp;
    }

    /// <summary>
    /// Draws bands either to the slingshotable anchors (normal) or to an animated pouch (during snap).
    /// </summary>
    public void DrawBands(ISlingshotable target)
    {
        if (!leftPole || !rightPole || leftBand == null || rightBand == null)
        {
            return;
        }

        if (_overridePouch)
        {
            // During snap animation, both bands end at the transient pouch position
            Vector3 pouch = _pouchPos;

            Debug.Log(pouch);

            leftBand.SetPosition(0, leftPole.position);
            leftBand.SetPosition(1, pouch);

            rightBand.SetPosition(0, rightPole.position);
            rightBand.SetPosition(1, pouch);
            return;
        }

        // Normal drawing to anchors
        if (leftPole && target?.LeftAnchor)
        {
            leftBand.SetPosition(0, leftPole.position);
            leftBand.SetPosition(1, target.LeftAnchor.position);
        }

        if (rightPole && target?.RightAnchor)
        {
            rightBand.SetPosition(0, rightPole.position);
            rightBand.SetPosition(1, target.RightAnchor.position);
        }
    }

    /// <summary>
    /// Kicks off a short snap-back animation from the pulled pouch position back to idle center (+forward offset).
    /// Call this right after Launch.
    /// </summary>
    public void PlaySnapFrom(Vector3 pulledPouchWorldPos)
    {
        Debug.Log("PlaySnapFrom");
        Vector3 center = GetBandCenter();

        // Idle slightly forward of the band plane if desired.
        Vector3 fwd = GetPreferredForward();
        Vector3 end = center + fwd * Mathf.Max(0f, idlePouchForwardOffset);

        // A tiny overshoot past the end, then the curve eases it in.
        Vector3 overshootDir = (end - center).sqrMagnitude < 1e-6f ? fwd : (end - center).normalized;
        Vector3 endWithOvershoot = end + overshootDir * snapOvershoot;

        _snapStart = pulledPouchWorldPos;
        _snapEnd = endWithOvershoot;

        _pouchPos = _snapStart;
        _snapClock = 0f;
        _snapTotal = Mathf.Max(0.01f, snapDuration);
        _overridePouch = true;

        // Ensure visible
        SetBandsVisible(true);
    }
}
