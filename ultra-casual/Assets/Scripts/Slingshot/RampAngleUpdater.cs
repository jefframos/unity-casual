using UnityEngine;

public class RampAngleUpdater : MonoBehaviour
{
    [Header("Ramp Reference")]
    public Transform Ramp;

    [Header("Upgrade-Based Rotation")]
    public bool useUpgrade = true;
    public float baseAngle = 0f;

    private float _uiAngle = 0f;

    private void OnEnable()
    {
        if (RampAngleMediator.Instance != null)
        {
            RampAngleMediator.Instance.OnAngleChanged += HandleUIAngleChanged;
            _uiAngle = RampAngleMediator.Instance.AdditionalUIAngle;
        }
    }

    private void OnDisable()
    {
        if (RampAngleMediator.Instance != null)
        {
            RampAngleMediator.Instance.OnAngleChanged -= HandleUIAngleChanged;
        }
    }

    private void HandleUIAngleChanged(float value)
    {
        _uiAngle = value;
    }

    private void Update()
    {
        if (!Ramp) return;

        float totalAngle = baseAngle;

        if (useUpgrade)
        {
            float upgradeAngle = UpgradeSystem.Instance.GetValue(UpgradeType.RAMP);
            totalAngle += upgradeAngle;
        }

        totalAngle += _uiAngle;

        Ramp.rotation = Quaternion.Euler(totalAngle, 0f, 0f);
    }
}
