using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Header("Controller")]
    [Tooltip("Assign a MonoBehaviour that implements IGameController. If left empty, the manager will try to find one at runtime.")]
    public MonoBehaviour gameControllerBehaviour; // must implement IGameController

    private IGameController _gameController;
    private readonly List<IResettable> _resettableCache = new();

    private void Start()
    {
        CacheGameController();
        RebuildResettableCache();
    }

    private void CacheGameController()
    {
        _gameController = null;

        if (gameControllerBehaviour != null)
        {
            _gameController = gameControllerBehaviour as IGameController;
            if (_gameController == null)
            {
                Debug.LogError("[GameManager] Assigned behaviour does not implement IGameController.");
            }
        }

        if (_gameController == null)
        {
            // Auto-find any behaviour that implements IGameController
            var all = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var mb in all)
            {
                if (mb is IGameController gc)
                {
                    _gameController = gc;
                    gameControllerBehaviour = mb; // keep inspector in sync for visibility
                    break;
                }
            }
        }

        if (_gameController == null)
        {
            Debug.LogWarning("[GameManager] No IGameController found. The game will still reset IResettable objects, but controller hooks won't run.");
        }
    }

    /// <summary>Build the list of scene objects that implement IResettable.</summary>
    public void RebuildResettableCache()
    {
        _resettableCache.Clear();

        var all = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        foreach (var mb in all)
        {
            if (!mb || !mb.isActiveAndEnabled) continue;
            if (mb is IResettable r)
            {
                _resettableCache.Add(r);
            }
        }
    }

    /// <summary>Start a new run: reset everything and ask the controller to prep gameplay.</summary>
    public void StartGame()
    {
        _gameController?.ResetGameState();
        ResetAll();
    }

    /// <summary>End the current run (optional; currently no-op in controller).</summary>
    public void EndGame()
    {
        _gameController?.EndGame();
    }

    /// <summary>Convenience: reset and restart gameplay loop.</summary>
    public void RestartGame()
    {
        StartGame();
    }

    private void ResetAll()
    {
        // If the scene structure can change, uncomment:
        // RebuildResettableCache();

        foreach (var r in _resettableCache.ToList())
        {
            if (r is Object unityObj && unityObj == null) continue; // destroyed
            r.ResetToInitial();
        }
    }

    // Quick keyboard test
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R)) RestartGame();
        if (Input.GetKeyDown(KeyCode.E)) EndGame();
    }
}
