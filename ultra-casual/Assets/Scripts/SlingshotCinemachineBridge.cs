using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;

[DisallowMultipleComponent]
public class SlingshotCinemachineBridge : MonoBehaviour
{
    public static SlingshotCinemachineBridge Instance { get; private set; }

    // ---------- Enums ----------
    public enum GameCameraMode
    {
        OutGame = 0,
        PreGame = 1,
        InGame = 2,
        EndGame = 3,
        EnemyReveal = 4,
        MiniGame = 5
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
    public MonoBehaviour slingshotComponent;
    private IGameController slingshot;

    // ---------- State ----------
    public GameCameraMode CurrentMode = GameCameraMode.OutGame;

    private readonly Dictionary<GameCameraMode, CinemachineCamera> _camLookup = new();

    // Buffered targets
    private Transform _currentFollow;
    private Transform _currentLookAt;

    private Transform _previousFollow;
    private Transform _previousLookAt;

    void Start()
    {
        SetCameraMode(CurrentMode);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        slingshot = slingshotComponent as IGameController;

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

    /// <summary>
    /// Sets a camera mode and optionally follow/lookAt.
    /// Buffers the previous follow/lookAt so you can restore it later.
    /// </summary>
    public void SetCameraMode(GameCameraMode mode, Transform follow, Transform lookAt)
    {
        // Buffer current before changing
        _previousFollow = _currentFollow;
        _previousLookAt = _currentLookAt;

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

        bool hasTargets = (follow != null || lookAt != null);

        if (hasTargets)
        {
            ActivateFollowCam(cam, follow, lookAt);
        }
        else
        {
            ActivateFixedCam(cam);
        }
    }

    /// <summary>
    /// Re-assigns the camera to the previously buffered follow/lookAt.
    /// Uses the current mode's camera.
    /// </summary>
    public void SetCameraToPreviousTarget()
    {
        if (_previousFollow == null && _previousLookAt == null)
        {
            Debug.LogWarning("[SlingshotCinemachineBridge] No previous follow/lookAt buffered.");
            return;
        }

        if (!_camLookup.TryGetValue(CurrentMode, out var cam) || cam == null)
        {
            Debug.LogWarning($"[SlingshotCinemachineBridge] No camera registered for current mode {CurrentMode}");
            return;
        }

        // Swap: current becomes previous, previous becomes current
        var newFollow = _previousFollow;
        var newLookAt = _previousLookAt;

        _previousFollow = _currentFollow;
        _previousLookAt = _currentLookAt;

        ActivateFollowCam(cam, newFollow, newLookAt);
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
        // Hook if needed
    }

    private void OnShotStarted(Transform target)
    {
        // Hook if needed
    }

    // ---------- Internals ----------

    private void ActivateFixedCam(CinemachineCamera cam)
    {
        if (!cam) return;

        cam.Follow = null;
        cam.LookAt = null;
        cam.Priority = activePriority;

        _currentFollow = null;
        _currentLookAt = null;
    }

    private void ActivateFollowCam(CinemachineCamera cam, Transform follow, Transform lookAt)
    {
        if (!cam) return;

        if (assignFollowTargets)
        {
            if (follow != null)
            {
                cam.Follow = follow;
            }

            cam.LookAt = lookAt != null ? lookAt : follow;
        }

        cam.Priority = activePriority;

        // Track current targets
        _currentFollow = cam.Follow;
        _currentLookAt = cam.LookAt;
    }

    private void SetCamInactive(CinemachineCamera cam)
    {
        if (!cam) return;
        cam.Priority = inactivePriority;
    }
}
