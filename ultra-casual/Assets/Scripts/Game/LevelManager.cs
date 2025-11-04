using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Threading.Tasks;
using UnityEngine.Rendering;

public class LevelManager : MonoBehaviour
{

    private readonly List<IResettable> _resettableCache = new();

    // Cancellation for the active restart flow

    private void Start()
    {
        RebuildResettableCache();

    }

    private void OnDisable()
    {
    }


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

        ResetAll();
    }

    /// <summary>End the current run.</summary>
    public void EndGame()
    {
    }


    public void ResetAll()
    {
        foreach (var r in _resettableCache.ToList())
        {
            r.ResetToInitial();
        }
    }

}
