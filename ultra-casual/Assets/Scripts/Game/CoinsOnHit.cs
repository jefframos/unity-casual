using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class CoinsOnHit : MonoBehaviour, IResettable
{
    [Header("Coins")]
    [Tooltip("How many coins to award when the player collides.")]
    public float value = 1;
    public bool isMultiplier;

    [Header("Player Recognition")]
    [Tooltip("Tag used to recognize the player (works on child colliders too).")]
    public string playerTag = "Player";

    [Header("Plumbing (optional)")]
    [Tooltip("If left null, will try to use UpgradeSystem.Instance.")]
    public UpgradeSystem upgradeSystem;

    [Header("Collision Mode")]
    [Tooltip("If true, award on trigger enter. If false, award on collision enter.")]
    public bool autoDisable = true;
    public bool useTrigger = true;
    private bool awarded = false;

    public TextMeshPro targetTMP;
    /// <summary>
    /// Public helper if you want to award programmatically (e.g., from another script).
    /// </summary>
    void OnValidate()
    {
        if (targetTMP != null)
        {
            if (isMultiplier)
            {
                targetTMP.text = "x" + value.ToString();
            }
            else
            {
                targetTMP.text = "+" + ((int)value).ToString();
            }
        }
    }
    public void AddCoins(int amount, Vector3 worldPos)
    {
        var sys = upgradeSystem != null ? upgradeSystem : UpgradeSystem.Instance;
        if (sys == null)
        {
            Debug.LogWarning("[CoinsOnHit] No UpgradeSystem available to add coins.");
            return;
        }

        int add = Mathf.Max(0, amount);
        sys.coins += add;

        var orch = CoinOrchestrator.Instance != null
            ? CoinOrchestrator.Instance
            : FindFirstObjectByType<CoinOrchestrator>(FindObjectsInactive.Exclude);

        if (orch != null)
        {
            orch.PopCoinsAt(add, worldPos);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!useTrigger) { return; }
        TryAward(other);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (useTrigger) { return; }
        TryAward(collision.collider);
    }

    private void TryAward(Collider other)
    {
        if (awarded)
        {
            return;
        }
        if (!IsPlayer(other)) { return; }
        var addedValue = (int)value;
        if (isMultiplier)
        {
            addedValue = (int)(LevelProgressTracker.Instance.currentDistance * value);
        }
        AddCoins(addedValue, transform.position);

        if (autoDisable)
        {
            gameObject.SetActive(false);
        }

        awarded = true;
        // Optional: disable/pool this pickup after awarding
        // gameObject.SetActive(false);
    }


    public void ForceAward(int forcedValue)
    {
        if (awarded)
        {
            return;
        }
        AddCoins(forcedValue, transform.position);

        if (autoDisable)
        {
            gameObject.SetActive(false);
        }

        awarded = true;
        // Optional: disable/pool this pickup after awarding
        // gameObject.SetActive(false);
    }

    private bool IsPlayer(Collider col)
    {
        if (string.IsNullOrEmpty(playerTag)) { return true; }
        if (col.CompareTag(playerTag)) { return true; }

        // Handle ragdoll / child colliders
        var t = col.transform.parent;
        while (t != null)
        {
            if (t.CompareTag(playerTag)) { return true; }
            t = t.parent;
        }
        return false;
    }

    public void ResetToInitial()
    {
        gameObject.SetActive(true);

        awarded = false;
    }
}
