using UnityEngine;
using Unity.Cinemachine;

[DisallowMultipleComponent]
public class SlingshotCinemachineBridge : MonoBehaviour
{
    [Header("Refs")]
    public SlingshotController slingshot;

    [Tooltip("Fixed camera used while aiming the slingshot.")]
    public CinemachineCamera aimVcam;

    [Tooltip("Camera that follows the car after launch.")]
    public CinemachineCamera followVcam;

    [Header("Aim Camera Pose")]
    [Tooltip("Optional: a transform to position/orient the AimVcam on enter. If null, AimVcam stays where it is.")]
    public Transform aimPose; // a dummy transform in scene with your desired fixed pose

    [Header("Priorities")]
    public int aimPriority = 20;
    public int followPriority = 30;

    [Header("Follow Settings")]
    [Tooltip("If true, set Follow and LookAt to the launched target on shot.")]
    public bool assignFollowTargets = true;

    [Tooltip("Optional: a different transform to LookAt (e.g., a look-ahead). Leave null to LookAt the target.")]
    public Transform customLookAt;

    private void OnEnable()
    {
        if (slingshot != null)
        {
            slingshot.OnEnterSlingshotMode.AddListener(OnEnterSlingshotMode);
            slingshot.OnShotStarted += OnShotStarted;
        }
    }

    private void OnDisable()
    {
        if (slingshot != null)
        {
            slingshot.OnEnterSlingshotMode.RemoveListener(OnEnterSlingshotMode);
            slingshot.OnShotStarted -= OnShotStarted;
        }
    }

    private void OnEnterSlingshotMode()
    {
        if (aimVcam != null)
        {
            if (aimPose != null)
            {
                aimVcam.transform.SetPositionAndRotation(aimPose.position, aimPose.rotation);
            }

            // Fixed cam: no Follow/LookAt
            aimVcam.Follow = null;
            aimVcam.LookAt = null;

            aimVcam.Priority = aimPriority;
        }

        if (followVcam != null)
        {
            // Keep follow vcam “armed” but lower priority
            followVcam.Priority = Mathf.Min(aimPriority - 1, followPriority - 1);
        }
    }

    private void OnShotStarted(Transform target)
    {
        if (followVcam != null)
        {
            if (assignFollowTargets && target != null)
            {
                followVcam.Follow = target;
                followVcam.LookAt = customLookAt != null ? customLookAt : target;
            }

            followVcam.Priority = followPriority;
        }

        if (aimVcam != null)
        {
            // Lower the aim cam so the follow cam takes over
            aimVcam.Priority = Mathf.Min(aimPriority - 1, followPriority - 1);
        }
    }
}
