using UnityEngine;
using System;

public class RampAngleMediator : MonoBehaviour
{
    public static RampAngleMediator Instance { get; private set; }

    // Fired whenever the angle changes
    public event Action<float> OnAngleChanged;

    public float _additionalUIAngle;

    public float minAngle = 5;
    public float maxAngle = -30;


    public float AdditionalUIAngle
    {
        get => Mathf.Lerp(minAngle, maxAngle, _additionalUIAngle);
        set
        {
            _additionalUIAngle = value;
            OnAngleChanged?.Invoke(AdditionalUIAngle);
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
}
