using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class EndgameMinigameTarget : MonoBehaviour
{
    private EndgameMinigameOrchestrator _owner;
    private Vector3 _moveDirection;
    private float _speed;
    private float _lifetime;
    private bool _resolved;
    private CancellationToken _token;

    private int _rewardValue;
    private AnimationCurve _speedCurve;
    private AnimationCurve _scaleCurve;
    private Vector3 _baseScale;

    private CoinsOnHit _coinsOnHit;

    public int RewardValue
    {
        get { return _rewardValue; }
    }

    public void Init(
        EndgameMinigameOrchestrator owner,
        Vector3 moveDirection,
        float speed,
        float lifetime,
        CancellationToken token,
        int rewardValue,
        AnimationCurve speedCurve,
        AnimationCurve scaleCurve,
        Vector3 baseScale
    )
    {
        _owner = owner;
        _moveDirection = moveDirection.normalized;
        _speed = speed;
        _lifetime = lifetime;
        _token = token;

        _rewardValue = rewardValue;
        _speedCurve = speedCurve;
        _scaleCurve = scaleCurve;
        _baseScale = baseScale;

        _resolved = false;

        if (_coinsOnHit == null)
        {
            _coinsOnHit = GetComponent<CoinsOnHit>();
        }

        var targetScale = _baseScale;
        targetScale.y = 0.05f;
        transform.localScale = _baseScale;

        MoveAndLifetimeAsync().Forget();
    }

    private async UniTaskVoid MoveAndLifetimeAsync()
    {
        float elapsed = 0f;
        Camera cam = (_owner != null && _owner.targetCamera != null)
            ? _owner.targetCamera
            : Camera.main;

        try
        {
            while (!_resolved && elapsed < _lifetime && !_token.IsCancellationRequested)
            {
                float dt = Time.unscaledDeltaTime;
                elapsed += dt;

                float tNorm = _lifetime > 0f ? Mathf.Clamp01(elapsed / _lifetime) : 0f;

                float speedFactor = 1f;
                if (_speedCurve != null)
                {
                    speedFactor = _speedCurve.Evaluate(tNorm);
                }

                transform.position += _moveDirection * _speed * speedFactor * dt;

                if (_scaleCurve != null)
                {
                    float scaleFactor = _scaleCurve.Evaluate(tNorm);
                    var targetScale = _baseScale * scaleFactor;
                    targetScale.y = 0.05f;
                    transform.localScale = targetScale;
                }

                if (cam != null)
                {
                    transform.LookAt(cam.transform);

                    if (_owner != null && _owner.rotationOffsetEuler != Vector3.zero)
                    {
                        transform.rotation *= Quaternion.Euler(_owner.rotationOffsetEuler);
                    }
                }

                await UniTask.Yield(PlayerLoopTiming.Update, _token);
            }
        }
        catch (OperationCanceledException)
        {
            // ignore
        }

        if (!_resolved && _owner != null)
        {
            _resolved = true;
            _owner.NotifyTargetMissed(this);
        }
    }

    private void OnMouseDown()
    {
        if (_resolved)
        {
            return;
        }

        _resolved = true;

        if (_coinsOnHit != null)
        {
            _coinsOnHit.ForceAward(_rewardValue);
        }

        if (_owner != null)
        {
            _owner.NotifyTargetHit(this);
        }
    }
}
