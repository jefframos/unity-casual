using System;
using UnityEngine;

public interface ISlingshotable
{
    // The transform you want the controller to rotate during aim.
    Transform Parent { get; }

    // Left/right hooks on the object (used to align midpoint to the pull point while aiming).
    Transform LeftAnchor { get; }
    Transform RightAnchor { get; }

    // A transform for cameras to follow (often the rigidbody root or a CoM pivot).
    Transform FollowTarget { get; }
    bool IsLaunching { get; }

    // Toggle physics kinematic mode while aiming.
    void SetKinematic(bool isKinematic);

    // Controller computes direction & impulse; object applies the force however it wants.
    void Launch(Vector3 direction, float impulse);

    event Action OnLaunchStart;
    event Action OnReleaseStart;
}
