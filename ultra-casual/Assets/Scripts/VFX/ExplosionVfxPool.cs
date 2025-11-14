using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simple singleton pool for explosion VFX.
/// Uses prefab.name as the key by default. Each instance gets a PooledExplosionVfx
/// that returns it to the pool when its particle systems finish.
/// </summary>
public class ExplosionVfxPool : MonoBehaviour
{
    public static ExplosionVfxPool Instance { get; private set; }

    // Pools per prefab-key
    private readonly Dictionary<string, Queue<GameObject>> _pools = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        // Optional:
        // DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Plays an explosion VFX using pooling.
    /// Prefab must contain at least one ParticleSystem (in root or children).
    /// </summary>
    public GameObject Play(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null)
        {
            Debug.LogWarning("[ExplosionVfxPool] Tried to play null prefab.");
            return null;
        }

        string key = GetKeyForPrefab(prefab);

        var go = GetFromPoolOrInstantiate(prefab, key, position, rotation);
        if (go == null) return null;

        go.transform.SetPositionAndRotation(position, rotation);
        go.SetActive(true);

        var pooled = go.GetComponent<PooledExplosionVfx>();
        if (pooled == null)
        {
            pooled = go.AddComponent<PooledExplosionVfx>();
        }

        pooled.Initialize(this, key);
        pooled.Play();

        return go;
    }

    private string GetKeyForPrefab(GameObject prefab)
    {
        // Runtime-safe key is just the name.
        // If you want a GUID in-editor, you could add a UNITY_EDITOR block here
        // that uses AssetDatabase to look up the GUID.
        return prefab.name;
    }

    private GameObject GetFromPoolOrInstantiate(GameObject prefab, string key, Vector3 pos, Quaternion rot)
    {
        if (!_pools.TryGetValue(key, out var queue))
        {
            queue = new Queue<GameObject>();
            _pools[key] = queue;
        }

        GameObject instance;
        if (queue.Count > 0)
        {
            instance = queue.Dequeue();
        }
        else
        {
            instance = Instantiate(prefab, pos, rot, transform);
        }

        return instance;
    }

    /// <summary>
    /// Called by PooledExplosionVfx when all particles are done.
    /// </summary>
    public void Recycle(string key, GameObject instance)
    {
        if (instance == null) return;

        if (!_pools.TryGetValue(key, out var queue))
        {
            queue = new Queue<GameObject>();
            _pools[key] = queue;
        }

        // Make sure everything is clean for next use
        var systems = instance.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in systems)
        {
            if (ps == null) continue;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        instance.SetActive(false);
        instance.transform.SetParent(transform, worldPositionStays: false);
        queue.Enqueue(instance);
    }
}
