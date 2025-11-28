using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

[DisallowMultipleComponent]
public class EndgameCoinRainSpawner : MonoBehaviour
{
    [Header("Coin Rain")]
    [Tooltip("Particle system used for the coin rain effect.")]
    public ParticleSystem coinRainParticles;

    [Tooltip("How long the rain should last (seconds).")]
    public float rainDuration = 1.0f;

    public async UniTask PlayCoinRainAsync(CancellationToken token)
    {
        if (coinRainParticles == null)
        {
            // Nothing to play
            if (rainDuration > 0f)
            {
                await UniTask.Delay(
                    TimeSpan.FromSeconds(rainDuration),
                    DelayType.UnscaledDeltaTime,
                    PlayerLoopTiming.Update,
                    token
                );
            }

            return;
        }

        coinRainParticles.gameObject.SetActive(true);
        coinRainParticles.Play(true);

        if (rainDuration > 0f)
        {
            await UniTask.Delay(
                TimeSpan.FromSeconds(rainDuration),
                DelayType.UnscaledDeltaTime,
                PlayerLoopTiming.Update,
                token
            );
        }

        // Let particles stop naturally; you can also Stop() forcefully if needed.
        coinRainParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }
}
