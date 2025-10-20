using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

[DisallowMultipleComponent]
public class DelayedRagdollSwitcher : MonoBehaviour, ISlingshotable, IResettable
{
    [Header("Scene Refs")]
    public Animator animator;          // pose while aiming
    public RagdollRig rig;             // hips/pelvis rig collector
    public Rigidbody launcherBody;     // rounded single body used for initial flight (Sphere/Capsule)
    public Collider launcherCollider;  // should be SphereCollider or CapsuleCollider

    [Header("Slingshot Anchors")]
    public Transform parent;
    public Transform leftAnchor;
    public Transform rightAnchor;
    public Transform followTarget;

    [Header("Timing")]
    [Tooltip("Delay before enabling the ragdoll (seconds).")]
    public float ragdollEnableDelay = 0.5f;

    [Header("Physics")]
    [Tooltip("Collision mode for launcher during the initial flight.")]
    public CollisionDetectionMode launcherCollisionMode = CollisionDetectionMode.ContinuousDynamic;

    [Tooltip("Angular damping while flying after switch. <0 to leave default.")]
    public float ragdollAngularDamping = 0.5f;

    [Tooltip("Copy the launcher's world velocity/angVel to all ragdoll bodies.")]
    public bool inheritLauncherVelocity = true;

    [Tooltip("Recompute mass props on switch.")]
    public bool resetMassPropsOnSwitch = true;

    // reset snapshot for the whole actor
    private Vector3 _startPos;
    private Vector3 _laucherBodyStartPos;
    private Quaternion _startRot;

    // cancellation for in-flight switch
    private CancellationTokenSource _switchCts;

    private void Reset()
    {
        parent = transform;
    }

    private void Awake()
    {
        if (rig == null) rig = GetComponentInChildren<RagdollRig>(true);
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (parent == null) parent = transform;

        _startPos = transform.position;
        _startRot = transform.rotation;

        if (rig != null)
        {
            if (rig.Bodies.Count == 0) rig.Collect();
            rig.BakePoseSnapshot();
        }

        // Ensure launcher references
        if (launcherBody == null && launcherCollider != null)
        {
            launcherBody = launcherCollider.attachedRigidbody;
            Debug.Log(launcherBody.transform.localPosition);
        }
        if (launcherBody)
        {
            _laucherBodyStartPos = launcherBody.transform.localPosition;
        }

        // Start in aiming state
        EnterAimingPose();
        EnableLauncher(false);
    }

    private void OnDisable()
    {
        _switchCts?.Cancel();
        _switchCts?.Dispose();
        _switchCts = null;
    }

    // ---------------- ISlingshotable ----------------

    public Transform Parent => parent;
    public Transform LeftAnchor => leftAnchor;
    public Transform RightAnchor => rightAnchor;
    public Transform FollowTarget => followTarget != null ? followTarget : parent;

    public void SetKinematic(bool isKinematic)
    {
        // Aiming phase uses kinematic ragdoll + animator on
        if (isKinematic)
        {
            EnterAimingPose();
        }
        else
        {
            // We keep the ragdoll kinematic until we launch; SetKinematic(false) here is a no-op
            // because launch flow controls exact timing.
        }
    }

    public void Launch(Vector3 direction, float impulse)
    {
        // Fire-and-forget the async flow
        LaunchRoutine(direction, impulse).Forget();
    }

    // ---------------- IResettable ----------------

    public void ResetToInitial()
    {
        _switchCts?.Cancel();

        // Freeze everything
        if (rig != null)
        {
            rig.SetKinematic(true);
            rig.ZeroVelocities();
            rig.RestorePoseSnapshot();
            rig.ResetMassProps();
        }

        // Reset transform
        transform.SetPositionAndRotation(_startPos, _startRot);

        // Reset launcher
        if (launcherBody != null)
        {
            launcherBody.isKinematic = true;
            launcherBody.useGravity = false;
            launcherBody.linearVelocity = Vector3.zero;
            launcherBody.angularVelocity = Vector3.zero;
            launcherBody.transform.localPosition = _laucherBodyStartPos;
            launcherBody.transform.rotation = Quaternion.identity;

            EnableLauncher(false);
        }

        // Back to aim
        EnterAimingPose();
    }

    // ---------------- Core flow ----------------

    private async UniTaskVoid LaunchRoutine(Vector3 direction, float impulse)
    {
        if (rig == null || launcherBody == null || launcherCollider == null)
        {
            Debug.LogWarning("[DelayedRagdollSwitcher] Missing rig/launcher references.");
            return;
        }

        _switchCts?.Cancel();
        _switchCts?.Dispose();
        _switchCts = new CancellationTokenSource();
        var token = _switchCts.Token;

        // 1) Prepare: animator OFF, ragdoll frozen (kinematic), launcher ON
        if (animator != null) animator.enabled = false;

        rig.SetKinematic(true);
        rig.ZeroVelocities();

        // Position launcher at root pose (so the shot starts aligned)
        launcherBody.isKinematic = true;
        launcherBody.useGravity = false;
        launcherBody.position = transform.position;
        launcherBody.rotation = transform.rotation;
        launcherBody.linearVelocity = Vector3.zero;
        launcherBody.angularVelocity = Vector3.zero;
        launcherBody.collisionDetectionMode = launcherCollisionMode;

        EnableLauncher(true);

        // 2) Fire the launcher
        var dir = direction.sqrMagnitude > 0f ? direction.normalized : transform.forward;
        launcherBody.isKinematic = false;
        launcherBody.useGravity = true;
        launcherBody.AddForce(dir * impulse, ForceMode.VelocityChange);

        // 3) Wait (fixed time) before switching to ragdoll
        try
        {
            // Use FixedUpdate timing so the handoff happens between physics steps
            await UniTask.Delay(TimeSpan.FromSeconds(Mathf.Max(0f, ragdollEnableDelay)),
                                DelayType.DeltaTime,
                                PlayerLoopTiming.FixedUpdate,
                                token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested) return;

        // 4) Handoff to ragdoll: snap root to launcher, enable physics on all bodies,
        //    and give them the launcher's current velocity/angVel.
        transform.SetPositionAndRotation(launcherBody.position, launcherBody.rotation);

        rig.SetKinematic(false);

        if (inheritLauncherVelocity)
        {
            var lin = launcherBody.linearVelocity;
            var ang = launcherBody.angularVelocity;

            foreach (var b in rig.Bodies)
            {
                b.linearVelocity = lin;
                b.angularVelocity = ang;
                if (ragdollAngularDamping >= 0f)
                {
                    b.angularDamping = ragdollAngularDamping; // Unity 6
                }
            }
        }

        if (resetMassPropsOnSwitch)
        {
            rig.ResetMassProps();
        }

        // 5) Turn off launcher so only the ragdoll collides
        launcherBody.isKinematic = true;
        launcherBody.useGravity = false;
        launcherBody.linearVelocity = Vector3.zero;
        launcherBody.angularVelocity = Vector3.zero;
        EnableLauncher(false);
    }

    // ---------------- Helpers ----------------

    private void EnterAimingPose()
    {
        if (rig != null)
        {
            rig.SetKinematic(true);
            rig.ZeroVelocities();
        }

        if (animator != null)
        {
            animator.enabled = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
    }

    private void EnableLauncher(bool on)
    {
        if (launcherCollider != null)
        {
            launcherCollider.enabled = on;
        }

        if (launcherBody != null)
        {
            // Keep launcher on a dedicated layer if you need special collision during aim
            launcherBody.collisionDetectionMode = on ? launcherCollisionMode : CollisionDetectionMode.Discrete;
        }
    }


}
