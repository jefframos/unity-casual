using UnityEngine;

public class RampAngleUpdater : MonoBehaviour
{
    public Transform Ramp;
    public bool useUpgrade;
    public float baseAngle;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

        if (useUpgrade)
        {
            var rampAngle = UpgradeSystem.Instance.GetValue(UpgradeType.RAMP);

            Ramp.rotation = Quaternion.Euler(baseAngle + rampAngle, 0, 0);
        }
    }
}
