using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyAppearingOrchestrator : MonoBehaviour
{
    [Header("Timing")]
    [Tooltip("Delay before the first enemy appears (seconds).")]
    public float delayBeforeFirstEnemy = 0.25f;

    [Tooltip("Delay between each enemy appearing (seconds).")]
    public float delayBetweenEnemies = 0.1f;

    [Header("Activation")]
    [Tooltip("If true, enemies that are disabled will be SetActive(true) when they appear.")]
    public bool activateInactiveGameObjects = true;

    /// <summary>
    /// Called by LevelManager when a new level step starts.
    /// Shows/activates the given enemies asynchronously.
    /// </summary>
    public async UniTask ShowStepEnemiesAsync(List<GameObject> enemies, CancellationToken token)
    {
        if (enemies == null || enemies.Count == 0)
        {
            return;
        }

        try
        {
            if (delayBeforeFirstEnemy > 0f)
            {
                await UniTask.Delay(
                    TimeSpan.FromSeconds(delayBeforeFirstEnemy),
                    cancellationToken: token
                );
            }

            for (int i = 0; i < enemies.Count; i++)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                GameObject enemy = enemies[i];
                if (enemy == null)
                {
                    continue;
                }

                // Basic activation
                if (activateInactiveGameObjects && !enemy.activeSelf)
                {
                    enemy.SetActive(false);
                }

                // TODO: Plug in per-enemy appear effects here (tween, VFX, etc.)

                if (delayBetweenEnemies > 0f && i < enemies.Count - 1)
                {
                    await UniTask.Delay(
                        TimeSpan.FromSeconds(delayBetweenEnemies),
                        cancellationToken: token
                    );

                    enemy.SetActive(true);

                }
            }
        }
        catch (OperationCanceledException)
        {
            // Swallow cancellation so we don't spam errors when scene resets.
        }
    }
}
