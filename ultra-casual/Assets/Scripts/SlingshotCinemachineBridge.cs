using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;

[DisallowMultipleComponent]
public class SlingshotCinemachineBridge : MonoBehaviour
{
    // ---------- Enums ----------
    public enum GameCameraMode
    {
        OutGame,
        PreGame,
        InGame,
        EndGame
    }

    [Serializable]
    public struct CameraModeEntry
    {
        public GameCameraMode mode;
        public CinemachineCamera camera;
    }

    // ---------- Serialized fields ----------
    [Header("Camera Table")]
    [Tooltip("List of mode-camera pairs. Works like a dictionary but visible in the editor.")]
    public List<CameraModeEntry> cameraTable = new List<CameraModeEntry>();

    [Header("General Settings")]
    [Tooltip("Priority for the active camera.")]
    public int activePriority = 30;

    [Tooltip("Priority for all inactive cameras.")]
    public int inactivePriority = 10;

    [Tooltip("If true, InGame cameras will auto-assign Follow/LookAt.")]
    public bool assignFollowTargets = true;

    [Tooltip("Optional: Override LookAt for gameplay camera (e.g., look-ahead point).")]
    public Transform customLookAt;

    [Header("Optional Slingshot Hook")]
    public SlingshotController slingshot;

    // ---------- State ----------
    //public GameCameraMode CurrentMode { get; private set; } = GameCameraMode.OutGame;
    public GameCameraMode CurrentMode = GameCameraMode.OutGame;

    private readonly Dictionary<GameCameraMode, CinemachineCamera> _camLookup = new();

    void Start()
    {
        SetCameraMode(CurrentMode);
    }
    private void Awake()
    {
        // Build lookup dictionary from editor list
        _camLookup.Clear();
        foreach (var entry in cameraTable)
        {
            if (entry.camera == null) continue;
            if (_camLookup.ContainsKey(entry.mode)) continue;
            _camLookup.Add(entry.mode, entry.camera);
        }
    }

    private void OnEnable()
    {
        if (slingshot != null)
        {
            slingshot.OnEnterGameMode += OnEnterSlingshotMode;
            slingshot.OnEnterEndMode += OnEndStart;
            slingshot.OnShotStarted += OnShotStarted;
            slingshot.OnLaunchStarted += OnLaunchSarted;
            slingshot.OnReleaseStarted += OnReleaseStarted;
        }
    }

    private void OnDisable()
    {
        if (slingshot != null)
        {
            slingshot.OnEnterGameMode -= OnEnterSlingshotMode;
            slingshot.OnEnterEndMode -= OnEndStart;
            slingshot.OnShotStarted -= OnShotStarted;
            slingshot.OnLaunchStarted -= OnLaunchSarted;
            slingshot.OnReleaseStarted -= OnReleaseStarted;

        }
    }

    // ---------- Public API ----------
    public void SetCameraMode(GameCameraMode mode)
    {
        SetCameraMode(mode, null, null);
    }


    public void SetCameraMode(GameCameraMode mode, Transform follow, Transform lookAt)
    {
        CurrentMode = mode;

        // Set all inactive first
        foreach (var kvp in _camLookup)
        {
            SetCamInactive(kvp.Value);
        }

        if (!_camLookup.TryGetValue(mode, out var cam) || cam == null)
        {
            Debug.LogWarning($"[SlingshotCinemachineBridge] No camera registered for mode {mode}");
            return;
        }

        // Activate current one
        if (follow || lookAt)
        {
            ActivateFollowCam(cam, follow, lookAt);
        }
        else
        {
            ActivateFixedCam(cam);
        }
    }

    public void OnReleaseStarted(Transform target)
    {
        Debug.Log("OnReleaseStarted");

        Transform follow = assignFollowTargets ? target : null;
        Transform lookAt = assignFollowTargets ? (customLookAt != null ? customLookAt : target) : null;
        SetCameraMode(GameCameraMode.InGame, follow, lookAt);
    }
    public void OnEndStart(Transform target)
    {
        Transform follow = assignFollowTargets ? target : null;
        Transform lookAt = assignFollowTargets ? (customLookAt != null ? customLookAt : target) : null;
        SetCameraMode(GameCameraMode.EndGame, follow, lookAt);
    }
    // ---------- Slingshot hooks ----------
    private void OnEnterSlingshotMode(Transform target)
    {

        Debug.Log("OnEnterSlingshotMode");

        SetCameraMode(GameCameraMode.PreGame);
    }

    private void OnLaunchSarted(Transform target)
    {
        //return;
        // Transform follow = assignFollowTargets ? target : null;
        // Transform lookAt = assignFollowTargets ? (customLookAt != null ? customLookAt : target) : null;
        // SetCameraMode(GameCameraMode.InGame, follow, lookAt);
    }
    private void OnShotStarted(Transform target)
    {
        //Debug.Log("OnShotStarted");

        // Transform follow = assignFollowTargets ? target : null;
        // Transform lookAt = assignFollowTargets ? (customLookAt != null ? customLookAt : target) : null;
        //SetCameraMode(GameCameraMode.PreGame);
    }

    // ---------- Internals ----------
    private void ActivateFixedCam(CinemachineCamera cam)
    {
        if (!cam) return;
        cam.Follow = null;
        cam.LookAt = null;
        cam.Priority = activePriority;
    }

    private void ActivateFollowCam(CinemachineCamera cam, Transform follow, Transform lookAt)
    {
        if (!cam) return;

        if (assignFollowTargets)
        {
            if (follow) cam.Follow = follow;
            cam.LookAt = lookAt != null ? lookAt : follow;
        }

        cam.Priority = activePriority;
    }

    private void SetCamInactive(CinemachineCamera cam)
    {
        if (!cam) return;
        cam.Priority = inactivePriority;
    }
}
