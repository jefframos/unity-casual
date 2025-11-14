using System.Collections;
using UnityEngine;

/// <summary>
/// Helper living on pooled explosion VFX instances.
/// Plays all ParticleSystems and returns to the pool when they finish.
/// </summary>
[DisallowMultipleComponent]
public class PooledExplosionVfx : MonoBehaviour
{
    private ExplosionVfxPool _pool;
    private string _key;
    private ParticleSystem[] _systems;
    private Coroutine _watchRoutine;

    private bool _initialized;

    public void Initialize(ExplosionVfxPool pool, string key)
    {
        _pool = pool;
        _key = key;

        if (_systems == null || _systems.Length == 0)
        {
            _systems = GetComponentsInChildren<ParticleSystem>(true);
            if (_systems == null || _systems.Length == 0)
            {
                Debug.LogWarning($"[PooledExplosionVfx] No ParticleSystem found on {name}.");
            }
        }

        _initialized = true;
    }

    public void Play()
    {
        if (!_initialized)
        {
            Debug.LogWarning("[PooledExplosionVfx] Play called before Initialize().");
        }

        // Play all particle systems
        if (_systems != null)
        {
            foreach (var ps in _systems)
            {
                if (ps == null) continue;
                ps.Clear(true);
                ps.Play(true);
            }
        }

        if (_watchRoutine != null)
        {
            StopCoroutine(_watchRoutine);
        }

        _watchRoutine = StartCoroutine(WatchRoutine());
    }

    private IEnumerator WatchRoutine()
    {
        // Wait while any system is alive
        bool anyAlive;
        do
        {
            anyAlive = false;
            if (_systems != null)
            {
                foreach (var ps in _systems)
                {
                    if (ps != null && ps.IsAlive(true))
                    {
                        anyAlive = true;
                        break;
                    }
                }
            }

            if (anyAlive)
                yield return null;

        } while (anyAlive);

        // Return to pool when done
        if (_pool != null && !string.IsNullOrEmpty(_key))
        {
            _pool.Recycle(_key, gameObject);
        }
        else
        {
            // Fallback if pool missing
            gameObject.SetActive(false);
        }
    }
}
