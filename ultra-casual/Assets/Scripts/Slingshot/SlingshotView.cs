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
    public Vector3 bandOffset;  // draws rightPole -> pouch/anchor

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

    [Header("Pouch Connector (optional)")]
    [Tooltip("Optional transform that will be kept at the midpoint between the two band endpoints.")]
    public Transform bandConnector;

    private float _polesPlaneY;

    // --- Transient pouch override state (SNAP mode) ---
    private ISlingshotable _currentTarget;
    private bool _overridePouch;

    // We animate the MIDPOINT; the half-offset keeps left/right spacing constant.
    private Vector3 _snapStartMid;          // midpoint between anchors at launch time
    private Vector3 _snapEndMid;            // target idle midpoint (+overshoot)
    private Vector3 _halfOffset;            // (right - left) * 0.5 at launch time (constant during snap)

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

        if (bandsAlwaysVisible)
        {
            SetBandsVisible(true);
        }
    }

    private void Update()
    {
        if (!_overridePouch)
        {
            // if (_currentTarget != null)
            // {
            //     DrawBands(_currentTarget);
            // }
            return;
        }

        // If we have a current target + both anchors, we can gate between the two states.
        if (_currentTarget != null && _currentTarget.LeftAnchor && _currentTarget.RightAnchor)
        {
            Vector3 leftNow = _currentTarget.LeftAnchor.position;
            Vector3 rightNow = _currentTarget.RightAnchor.position;

            // --- STATE 1: stick to live anchors until we pass your Z threshold ---
            // (old: _snapEnd.z > _currentTarget.LeftAnchor.position.z - 2f)
            if (_snapEndMid.z > leftNow.z - 2f)
            {
                if (leftPole) { leftBand.SetPosition(0, leftPole.position); leftBand.SetPosition(1, leftNow + bandOffset); }
                if (rightPole) { rightBand.SetPosition(0, rightPole.position); rightBand.SetPosition(1, rightNow + bandOffset); }

                // Keep start and spacing fresh so when we flip to animation it's seamless.
                _snapStartMid = 0.5f * (leftNow + rightNow);
                _halfOffset = (rightNow - leftNow) * 0.5f;

                if (bandConnector != null) bandConnector.position = _snapStartMid + bandOffset;
                return;
            }
        }

        // --- STATE 2: animate midpoint back to idle, preserving spacing with _halfOffset ---
        _snapClock += Time.deltaTime;
        float t = (_snapTotal <= 1e-5f) ? 1f : Mathf.Clamp01(_snapClock / _snapTotal);
        float w = snapCurve != null ? snapCurve.Evaluate(t) : t;

        Vector3 mid = Vector3.LerpUnclamped(_snapStartMid, _snapEndMid, w);
        Vector3 leftEnd = mid - _halfOffset;
        Vector3 rightEnd = mid + _halfOffset;

        if (leftPole) { leftBand.SetPosition(0, leftPole.position); leftBand.SetPosition(1, leftEnd + bandOffset); }
        if (rightPole) { rightBand.SetPosition(0, rightPole.position); rightBand.SetPosition(1, rightEnd + bandOffset); }

        if (bandConnector != null) bandConnector.position = mid + bandOffset;

        if (t >= 1f)
        {
            _overridePouch = false; // resume normal DrawBands() next frame
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
        lr.enabled = bandsAlwaysVisible;
    }

    public void SetBandsVisible(bool on)
    {
        if (bandsAlwaysVisible) on = true;
        if (leftBand) leftBand.enabled = on;
        if (rightBand) rightBand.enabled = on;
    }

    public Vector3 GetBandCenter()
    {
        if (!leftPole || !rightPole) return transform.position;
        return 0.5f * (leftPole.position + rightPole.position);
    }

    public Vector3 GetPreferredForward()
    {
        if (!leftPole || !rightPole) return Vector3.forward;

        Vector3 across = rightPole.position - leftPole.position;
        Vector3 side = Vector3.ProjectOnPlane(across, upAxis);
        Vector3 forward = Vector3.Cross(side, upAxis).normalized;
        if (flipBaselineForward) forward = -forward;
        return forward.sqrMagnitude > 1e-6f ? forward : Vector3.forward;
    }

    public Vector3 GetMouseWorldOnPolesPlane(Camera cam)
    {
        if (!leftPole || !rightPole) return transform.position;

        Vector3 planePoint = new Vector3(0f, _polesPlaneY, 0f);
        Plane plane = new Plane(upAxis.normalized, planePoint);
        Ray ray = cam != null ? cam.ScreenPointToRay(Input.mousePosition) : new Ray(Vector3.zero, Vector3.forward);

        if (plane.Raycast(ray, out float enter)) return ray.GetPoint(enter);
        return GetBandCenter();
    }

    public Vector3 ClampPointBetweenPoles(Vector3 point, float endInset = 0f)
    {
        Vector3 up = upAxis.normalized;
        Vector3 A = leftPole.position;
        Vector3 B = rightPole.position;

        Vector3 AP = Vector3.ProjectOnPlane(point - A, up);
        Vector3 AB = Vector3.ProjectOnPlane(B - A, up);

        float len = AB.magnitude;
        if (len < 1e-4f) return point;

        Vector3 side = AB / len;
        float t = Vector3.Dot(AP, side);

        float minT = Mathf.Clamp(endInset, 0f, len);
        float maxT = Mathf.Clamp(len - endInset, 0f, len);
        t = Mathf.Clamp(t, minT, maxT);

        Vector3 along = A + side * t;
        Vector3 perp = Vector3.ProjectOnPlane((A + AP) - along, side);
        return along + perp;
    }

    /// <summary>
    /// Draws bands either to the slingshotable anchors (normal) or (if snapping) from poles to animated endpoints.
    /// Call this each frame while aiming/attached.
    /// </summary>
    public void DrawBands(ISlingshotable target)
    {
        if (!leftPole || !rightPole || leftBand == null || rightBand == null) return;

        _currentTarget = target;

        if (!_overridePouch && target != null && target.LeftAnchor && target.RightAnchor)
        {
            Debug.Log("DRAW BANDS");
            Vector3 leftEnd = target.LeftAnchor.position;
            Vector3 rightEnd = target.RightAnchor.position;

            leftBand.SetPosition(0, leftPole.position);
            leftBand.SetPosition(1, leftEnd + bandOffset);

            rightBand.SetPosition(0, rightPole.position);
            rightBand.SetPosition(1, rightEnd + bandOffset);

            if (bandConnector != null) bandConnector.position = 0.5f * (leftEnd + rightEnd) + bandOffset;
        }
        // When _overridePouch is true, Update() is drawing and placing the connector already.
    }

    /// <summary>
    /// Kicks off a short snap-back animation. Keeps the spacing between left/right band endpoints
    /// equal to the spacing that existed at launch time, and animates the midpoint back to idle (+overshoot).
    /// Call this right after Launch.
    /// </summary>
    public void PlaySnapFrom(Vector3 pulledPouchWorldPos)
    {
        // Fallback: if we have a current target, capture spacing from its anchors right now.
        if (_currentTarget != null && _currentTarget.LeftAnchor && _currentTarget.RightAnchor)
        {
            PlaySnapFromAnchors(_currentTarget.LeftAnchor.position, _currentTarget.RightAnchor.position);
            return;
        }

        // No target anchors available: treat both ends as the same point (legacy behavior),
        // which will visually collapse, but preserves functionality.
        Vector3 center = GetBandCenter();
        Vector3 fwd = GetPreferredForward();
        Vector3 endMid = center + fwd * Mathf.Max(0f, idlePouchForwardOffset);
        Vector3 overshootDir = (endMid - center).sqrMagnitude < 1e-6f ? fwd : (endMid - center).normalized;
        endMid += overshootDir * snapOvershoot;

        _snapStartMid = pulledPouchWorldPos;
        _snapEndMid = endMid;
        _halfOffset = Vector3.zero;

        _snapClock = 0f;
        _snapTotal = Mathf.Max(0.01f, snapDuration);
        _overridePouch = true;

        SetBandsVisible(true);
    }

    /// <summary>
    /// Explicit version: start snap using the actual left/right endpoints at launch time.
    /// </summary>
    public void PlaySnapFromAnchors(Vector3 leftAtLaunch, Vector3 rightAtLaunch)
    {
        Vector3 startMid = 0.5f * (leftAtLaunch + rightAtLaunch);
        Vector3 center = GetBandCenter();
        Vector3 fwd = GetPreferredForward();

        Vector3 endMid = center + fwd * Mathf.Max(0f, idlePouchForwardOffset);
        Vector3 overshootDir = (endMid - center).sqrMagnitude < 1e-6f ? fwd : (endMid - center).normalized;
        endMid += overshootDir * snapOvershoot;

        _snapStartMid = startMid;
        _snapEndMid = endMid;

        // Keep constant spacing: half vector from mid to each side at launch.
        _halfOffset = (rightAtLaunch - leftAtLaunch) * 0.5f;

        _snapClock = 0f;
        _snapTotal = Mathf.Max(0.01f, snapDuration);
        _overridePouch = true;

        SetBandsVisible(true);

        // Place connector immediately on start mid if present.
        if (bandConnector != null) bandConnector.position = _snapStartMid + bandOffset;
    }
}
