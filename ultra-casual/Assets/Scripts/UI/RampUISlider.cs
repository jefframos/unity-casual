using UnityEngine;
using UnityEngine.UI;

public class RampUISlider : MonoBehaviour
{
    public Slider slider;

    private void Awake()
    {
        if (slider != null)
            slider.onValueChanged.AddListener(OnSliderValueChanged);
    }

    private void OnSliderValueChanged(float value)
    {
        if (RampAngleMediator.Instance != null)
            RampAngleMediator.Instance.AdditionalUIAngle = value;
    }
}
