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
    public LineRenderer leftBand;   // draws leftPole -> slingshotable.LeftAnchor
    public LineRenderer rightBand;  // draws rightPole -> slingshotable.RightAnchor

    private float _polesPlaneY;

    private void Awake()
    {
        if (leftPole && rightPole)
        {
            _polesPlaneY = 0.5f * (leftPole.position.y + rightPole.position.y);
        }

        EnsureBand(ref leftBand, "LeftBand");
        EnsureBand(ref rightBand, "RightBand");
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
        lr.enabled = false;
    }

    public void SetBandsVisible(bool on)
    {
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

    public void DrawBands(ISlingshotable target)
    {
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
}
