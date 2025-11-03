using UnityEngine;

[DisallowMultipleComponent]
public class ProgressMarker : MonoBehaviour
{
    [Header("References")]
    public Transform startPoint;
    public Transform endPoint;
    public Transform marker; // The moving object

    [Header("Progress")]
    [Range(0f, 1f)]
    public float normalizedProgress;

    void Update()
    {
        if (startPoint == null || endPoint == null || marker == null)
            return;

        // Lerp position based on normalizedProgress
        marker.position = Vector3.Lerp(startPoint.position, endPoint.position, normalizedProgress);
    }

    // Optional helper to update from code (e.g. timer)
    public void SetProgress(float normalized)
    {
        normalizedProgress = Mathf.Clamp01(normalized);
    }
}
