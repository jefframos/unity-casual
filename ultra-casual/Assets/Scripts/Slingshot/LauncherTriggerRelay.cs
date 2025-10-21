using UnityEngine;

/// <summary>
/// Attach this to the same GameObject as the launcher's Collider.
/// It forwards trigger hits with tag "Starter" to the DelayedRagdollSwitcher.
/// </summary>
[DisallowMultipleComponent]
public class LauncherTriggerRelay : MonoBehaviour
{
    private DelayedRagdollSwitcher _switcher;

    public void Initialize(DelayedRagdollSwitcher switcher)
    {
        _switcher = switcher;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_switcher == null) return;
        if (!other || !other.CompareTag("Starter")) return;

        _switcher.OnLauncherHitStarterTrigger(other);
    }
}
