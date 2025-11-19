using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using DG.Tweening;
[DisallowMultipleComponent]
public class EnemyAppearingOrchestrator : MonoBehaviour
{
    [Header("Timing")]
    [Tooltip("Delay before the first enemy appears (seconds).")]
    public float delayBeforeFirstEnemy = 0.25f;

    [Tooltip("Delay between each enemy appearing (seconds).")]
    public float delayBetweenEnemies = 0.1f;
    public float afterAnimatingTimer = 0.75f;

    [Header("Activation")]
    [Tooltip("If true, enemies that are disabled will be SetActive(true) when they appear.")]
    public bool activateInactiveGameObjects = true;
    public GameObject vfxPrefab;
    public AudioClip sfx;
    public float sfxVolume;

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

        foreach (var item in enemies)
        {
            if (item != null)
            {
                item.SetActive(false);
            }
        }

        const float popDuration = 0.25f;   // tweak as you like

        try
        {
            if (delayBeforeFirstEnemy > 0f)
            {

                SlingshotCinemachineBridge.Instance.SetCameraMode(
                   SlingshotCinemachineBridge.GameCameraMode.EnemyReveal,
                   enemies[0].transform,
                   enemies[0].transform
               );

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

                // VFX/SFX
                if (vfxPrefab != null)
                {
                    ExplosionVfxPool.Instance?.Play(
                        vfxPrefab,
                        enemy.transform.position,
                        Quaternion.identity
                    );
                }

                if (sfx)
                {
                    AudioSource.PlayClipAtPoint(sfx, transform.position, sfxVolume);
                }

                SlingshotCinemachineBridge.Instance.SetCameraMode(
                    SlingshotCinemachineBridge.GameCameraMode.EnemyReveal,
                    enemy.transform,
                    enemy.transform
                );

                RagdollEnemy ragdoll = enemy.GetComponent<RagdollEnemy>();

                Rigidbody sourceBody = null;
                bool prevIsKinematic = false;

                if (ragdoll != null && ragdoll.sourceBody != null)
                {
                    sourceBody = ragdoll.sourceBody;
                    prevIsKinematic = sourceBody.isKinematic;

                    // Disable physics so it doesn't collide/push environment during pop
                    sourceBody.isKinematic = true;
                    // If you're using Rigidbody2D or something custom, adapt this line accordingly.
                }

                // ----- POP SCALE USING DOTWEEN -----
                // Start from zero scale (invisible), then pop to normal size.
                enemy.SetActive(true);
                enemy.transform.localScale = Vector3.zero;

                // Option A: basic pop, 0 -> 1
                // await enemy.transform
                //     .DOScale(Vector3.one, popDuration)
                //     .SetEase(Ease.OutBack)
                //     .ToUniTask(cancellationToken: token);

                // Option B: cartoony overshoot pop (0 -> 1.1 -> 1)
                await enemy.transform
                    .DOScale(Vector3.one, popDuration)
                    .SetEase(Ease.OutBack)//.AsyncWaitForCompletion();
                .OnComplete(() =>
                {
                    // settle back to normal size
                    enemy.transform.DOScale(Vector3.one, popDuration * 0.4f)
                        .SetEase(Ease.InOutSine);
                }).AsyncWaitForCompletion();     //          (cancellationToken: token);
                // -----------------------------------

                if (delayBetweenEnemies > 0f && i < enemies.Count - 1)
                {
                    await UniTask.Delay(
                        TimeSpan.FromSeconds(delayBetweenEnemies),
                        cancellationToken: token
                    );
                }

                if (sourceBody != null)
                {
                    sourceBody.isKinematic = prevIsKinematic;
                }
            }

            await UniTask.Delay(
                       TimeSpan.FromSeconds(afterAnimatingTimer),
                       cancellationToken: token
                   );
        }
        catch (OperationCanceledException)
        {
            // Swallow cancellation so we don't spam errors when scene resets.
        }
    }
}
