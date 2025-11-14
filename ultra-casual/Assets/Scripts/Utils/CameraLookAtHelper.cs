using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[AddComponentMenu("Tools/Camera Look At Helper")]
public class CameraLookAtHelper : MonoBehaviour
{
    public Transform target;

    [Tooltip("If false, only updates in the Editor, not during Play Mode.")]
    public bool enableInPlayMode = false;

    private Camera cam;

    void OnEnable()
    {
        cam = GetComponent<Camera>();
    }

    void LateUpdate()
    {
        if (target == null) return;

        // Only rotate in play mode if enabled
        if (Application.isPlaying && !enableInPlayMode)
            return;

        transform.LookAt(target);
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (target == null)
            return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, target.position);
        Gizmos.DrawSphere(target.position, 0.1f);
    }

    // Ensures the camera continues looking at the target
    // while the user moves the camera in Scene View.
    private void OnSceneGUI()
    {
        if (target == null)
            return;

        // Only run in editor when not in play mode
        if (!Application.isPlaying)
        {
            transform.LookAt(target);
        }
    }
#endif
}
