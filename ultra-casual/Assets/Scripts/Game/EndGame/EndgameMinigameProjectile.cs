using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

[DisallowMultipleComponent]
public class EndgameMinigameProjectile : MonoBehaviour
{
    [Header("Visuals")]
    [Tooltip("Trail renderers that should be cleared when the projectile is (re)spawned.")]
    public TrailRenderer[] trailRenderers;

    private EndgameMinigameGun _owner;
    private Vector3 _direction;
    private float _speed;
    private float _lifetime;

    private bool _isActive;
    private CancellationTokenSource _cts;

    public void Launch(
        EndgameMinigameGun owner,
        Vector3 direction,
        float speed,
        float lifetime
    )
    {
        _owner = owner;
        _direction = direction.normalized;
        _speed = speed;
        _lifetime = lifetime;

        _isActive = true;

        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
        }

        _cts = new CancellationTokenSource();

        // Very important: reset the trail before we start moving
        ResetTrails();

        MoveRoutineAsync(_cts.Token).Forget();
    }

    private void ResetTrails()
    {
        if (trailRenderers == null)
        {
            return;
        }

        for (int i = 0; i < trailRenderers.Length; i++)
        {
            var tr = trailRenderers[i];
            if (tr == null)
            {
                continue;
            }

            // Turn off emission, clear, then turn back on.
            // This guarantees no old segments survive pooling.
            bool wasEmitting = tr.emitting;
            tr.emitting = false;
            tr.Clear();
            tr.emitting = wasEmitting;
        }
    }

    private async UniTaskVoid MoveRoutineAsync(CancellationToken token)
    {
        float elapsed = 0f;

        try
        {
            while (_isActive && elapsed < _lifetime && !token.IsCancellationRequested)
            {
                float dt = Time.unscaledDeltaTime;
                elapsed += dt;

                transform.position += _direction * _speed * dt;

                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on cancel
        }

        Despawn();
    }

    private void Despawn()
    {
        if (!_isActive)
        {
            return;
        }

        _isActive = false;

        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }

        if (_owner != null)
        {
            _owner.ReturnToPool(this);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    private void OnDisable()
    {
        // Make sure no coroutine keeps running
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }

        _isActive = false;

        // Also clear trails when going back to pool, so they are clean even
        // if something deactivated the projectile without going through Despawn.
        ResetTrails();
    }
}
