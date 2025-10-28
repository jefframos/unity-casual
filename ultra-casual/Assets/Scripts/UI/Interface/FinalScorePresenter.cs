using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public abstract class FinalScorePresenter : MonoBehaviour
{
    /// <summary>
    /// Show the final score UI and complete when the game can restart.
    /// Use the provided token to cancel if needed (scene unload, etc.).
    /// </summary>
    public abstract UniTask ShowFinalScore(float score);
    public abstract UniTask ShowFinalScoreAsync(float score, CancellationToken token);
    public abstract UniTask AwaitEnd(CancellationToken token);
}
