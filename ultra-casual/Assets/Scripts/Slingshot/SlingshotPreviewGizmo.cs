using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SlingshotPreviewGizmo : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("Slingshot controller that provides direction & speed during drag.")]
    public SimpleSlingshotController slingshot;

    [Tooltip("Optional override for the start position.\n" +
             "If null, SlingshotPreviewGizmo uses slingshot.PreviewOrigin.")]
    public Transform startOverride;

    [Header("Prefabs")]
    [Tooltip("Prefab used for each segment of the dotted line (e.g. small sphere).")]
    public GameObject segmentPrefab;

    [Tooltip("Optional prefab used only for the tip of the path (e.g. arrow head).")]
    public GameObject tipPrefab;

    [Header("Path Settings")]
    [Tooltip("Maximum number of segments that can be shown (pool size).")]
    public int maxSegments = 20;

    [Tooltip("Time step (seconds) between trajectory samples.")]
    public float timeStep = 0.05f;

    [Tooltip("Maximum simulated time (seconds). Path is clamped to this duration.")]
    public float maxSimTime = 2.0f;

    [Tooltip("Safety cap for max path length (world units). 0 or less = no cap.")]
    public float maxPathLength = 30f;

    [Tooltip("Rotate segments/tip to face along the path direction.")]
    public bool autoRotateSegments = true;

    [Header("Collision")]
    [Tooltip("Layers considered for collision when predicting the path.")]
    public LayerMask collisionLayers = ~0;

    [Tooltip("Whether to stop the preview at the first collision hit.")]
    public bool stopOnHit = true;

    [Header("Visual Scale Curve")]
    [Tooltip("Curve over [0..1] along the path that controls scale of each segment.\n" +
             "If value is 0 at some point, that segment is disabled.")]
    public AnimationCurve pathScaleCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);

    [Tooltip("Apply the same curve at t=1 to the tip scale (only when we hit something).")]
    public bool applyCurveToTip = true;

    private readonly List<Transform> _segmentPool = new List<Transform>();
    private Transform _tipInstance;
    private bool _poolInitialized;

    private Vector3 _segmentBaseScale = Vector3.one;
    private Vector3 _tipBaseScale = Vector3.one;

    private void OnDisable()
    {
        HideAllInstances();
    }

    private void LateUpdate()
    {
        if (slingshot == null || !slingshot.HasValidPreview)
        {
            HideAllInstances();
            return;
        }

        Vector3 origin = startOverride != null
            ? startOverride.position
            : slingshot.PreviewOrigin;

        Vector3 dir = slingshot.PreviewDirection;
        float speed = slingshot.PreviewSpeed;

        if (dir.sqrMagnitude < 0.0001f || speed <= 0.0001f)
        {
            HideAllInstances();
            return;
        }

        dir.Normalize();

        EnsurePool();
        UpdatePath(origin, dir, speed);
    }

    // ----------------- Pool & Path Logic -----------------

    private void EnsurePool()
    {
        if (_poolInitialized)
            return;

        _segmentPool.Clear();

        if (segmentPrefab != null && maxSegments > 0)
        {
            for (int i = 0; i < maxSegments; i++)
            {
                GameObject go = Instantiate(segmentPrefab, transform);
                if (i == 0)
                {
                    _segmentBaseScale = go.transform.localScale;
                }

                go.SetActive(false);
                _segmentPool.Add(go.transform);
            }
        }

        if (tipPrefab != null)
        {
            GameObject tip = Instantiate(tipPrefab, transform);
            _tipBaseScale = tip.transform.localScale;
            tip.SetActive(false);
            _tipInstance = tip.transform;
        }

        _poolInitialized = true;
    }

    private void HideAllInstances()
    {
        for (int i = 0; i < _segmentPool.Count; i++)
        {
            Transform seg = _segmentPool[i];
            if (seg == null)
                continue;

            if (seg.gameObject.activeSelf)
                seg.gameObject.SetActive(false);
        }

        if (_tipInstance != null && _tipInstance.gameObject.activeSelf)
        {
            _tipInstance.gameObject.SetActive(false);
        }
    }

    private void UpdatePath(Vector3 origin, Vector3 dir, float speed)
    {
        EnsurePool();

        Vector3 gravity = Physics.gravity;
        float t = 0f;

        Vector3 prevPos = origin;
        float totalLength = 0f;

        int usedSegments = 0;
        bool hitSomething = false;
        RaycastHit hitInfo = default;

        // Simulate the trajectory in time steps.
        for (int i = 0; i < maxSegments; i++)
        {
            t += timeStep;
            if (t > maxSimTime)
                break;

            // p(t) = origin + v0 * t + 0.5 * g * t^2
            Vector3 pos = origin + dir * (speed * t) + 0.5f * gravity * (t * t);

            // Limit by total path length if needed
            float segmentLen = Vector3.Distance(prevPos, pos);
            if (maxPathLength > 0f && totalLength + segmentLen > maxPathLength)
            {
                float remaining = maxPathLength - totalLength;
                if (segmentLen > 0.0001f)
                {
                    float ratio = remaining / segmentLen;
                    pos = Vector3.Lerp(prevPos, pos, ratio);
                    segmentLen = remaining;
                }
            }

            // Collision raycast between prevPos and pos
            if (collisionLayers.value != 0)
            {
                Vector3 dirSeg = pos - prevPos;
                float segDist = dirSeg.magnitude;
                if (segDist > 0.0001f)
                {
                    dirSeg /= segDist;
                    if (Physics.Raycast(prevPos, dirSeg, out hitInfo, segDist, collisionLayers, QueryTriggerInteraction.Ignore))
                    {
                        pos = hitInfo.point;
                        hitSomething = true;
                        totalLength += Vector3.Distance(prevPos, pos);

                        PlaceSegment(usedSegments, pos, prevPos);
                        usedSegments++;

                        if (stopOnHit)
                        {
                            break; // stop on first hit
                        }
                    }
                }
            }

            totalLength += segmentLen;
            if (maxPathLength > 0f && totalLength >= maxPathLength)
            {
                PlaceSegment(usedSegments, pos, prevPos);
                usedSegments++;
                break;
            }

            PlaceSegment(usedSegments, pos, prevPos);
            usedSegments++;
            prevPos = pos;
        }

        // Hide unused segments
        for (int i = usedSegments; i < _segmentPool.Count; i++)
        {
            Transform seg = _segmentPool[i];
            if (seg != null && seg.gameObject.activeSelf)
                seg.gameObject.SetActive(false);
        }

        // Tip placement: only show if we actually hit something
        if (_tipInstance != null)
        {
            if (hitSomething && usedSegments > 0)
            {
                _tipInstance.position = hitInfo.point;

                // Rotate tip along last part of the path
                if (autoRotateSegments && usedSegments > 1)
                {
                    Transform lastSeg = _segmentPool[usedSegments - 1];
                    if (lastSeg != null)
                    {
                        Vector3 prev = lastSeg.position;
                        Vector3 tipDir = (_tipInstance.position - prev).normalized;
                        if (tipDir.sqrMagnitude > 0.0001f)
                        {
                            Vector3 up = slingshot != null ? slingshot.transform.up : Vector3.up;
                            _tipInstance.rotation = Quaternion.LookRotation(tipDir, up);
                        }
                    }
                }

                // Scale tip using curve at t=1 if requested
                if (applyCurveToTip && pathScaleCurve != null)
                {
                    float s = pathScaleCurve.Evaluate(1f);
                    if (s <= 0f)
                    {
                        _tipInstance.gameObject.SetActive(false);
                    }
                    else
                    {
                        _tipInstance.localScale = _tipBaseScale * s;
                        if (!_tipInstance.gameObject.activeSelf)
                            _tipInstance.gameObject.SetActive(true);
                    }
                }
                else
                {
                    _tipInstance.localScale = _tipBaseScale;
                    if (!_tipInstance.gameObject.activeSelf)
                        _tipInstance.gameObject.SetActive(true);
                }
            }
            else
            {
                if (_tipInstance.gameObject.activeSelf)
                    _tipInstance.gameObject.SetActive(false);
            }
        }
    }

    private void PlaceSegment(int index, Vector3 pos, Vector3 prevPos)
    {
        if (segmentPrefab == null || index < 0 || index >= _segmentPool.Count)
            return;

        Transform seg = _segmentPool[index];
        if (seg == null)
            return;

        // Evaluate curve based on normalized index along the path [0..1].
        float t = (maxSegments <= 1) ? 1f : (float)index / (float)(maxSegments - 1);
        float scale = pathScaleCurve != null ? pathScaleCurve.Evaluate(t) : 1f;

        // If curve says 0 → disable this segment and bail.
        if (scale <= 0f)
        {
            if (seg.gameObject.activeSelf)
                seg.gameObject.SetActive(false);
            return;
        }

        seg.position = pos;
        seg.localScale = _segmentBaseScale * scale;

        if (autoRotateSegments)
        {
            Vector3 dir = (pos - prevPos).normalized;
            if (dir.sqrMagnitude > 0.0001f)
            {
                Vector3 up = slingshot != null ? slingshot.transform.up : Vector3.up;
                seg.rotation = Quaternion.LookRotation(dir, up);
            }
        }

        if (!seg.gameObject.activeSelf)
            seg.gameObject.SetActive(true);
    }
}
