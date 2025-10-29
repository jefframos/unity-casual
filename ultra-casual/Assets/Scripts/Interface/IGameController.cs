using System;
using UnityEngine;

public interface IGameController
{
    /// <summary>
    /// Prepare gameplay state for a new run (e.g., position player, enable input, etc.).
    /// </summary>
    void ResetGameState();

    /// <summary>
    /// Handle end-of-run logic (score tally, UI, disable input, etc.).
    /// </summary>
    void EndGame();

    /// <summary>
    /// Fired when the launch (shot) starts — passes the follow target.
    /// </summary>
    event Action<Transform> OnEnterEndMode;
    event Action<Transform> OnEnterGameMode;
    event Action<Transform> OnShotStarted;

    /// <summary>
    /// Fired when the launch actually begins (same as above if not differentiated).
    /// </summary>
    event Action<Transform> OnReleaseStarted;
    event Action<Transform> OnLaunchStarted;
}
