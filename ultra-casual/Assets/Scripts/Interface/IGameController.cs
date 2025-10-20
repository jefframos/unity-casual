public interface IGameController
{
    /// <summary>Prepare gameplay state for a new run (e.g., position player, enable input, etc.).</summary>
    void ResetGameState();

    /// <summary>Handle end-of-run logic (score tally, UI, disable input, etc.).</summary>
    void EndGame();
}
