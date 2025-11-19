using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("FX/Sine Rotator")]
public class SineRotator : MonoBehaviour
{
    [Header("Rotation")]
    [Tooltip("Maximum angle from center (rotation oscillates between -angle and +angle).")]
    public float angleLimitX = 15f;
    public float angleLimitY = 15f;
    public float angleLimitZ = 15f;

    [Header("Speed")]
    [Tooltip("How fast the oscillation happens.")]
    public float speedX = 1f;
    public float speedY = 1f;
    public float speedZ = 1f;

    [Header("Axis Toggles")]
    public bool rotateX = true;
    public bool rotateY = false;
    public bool rotateZ = false;

    private float _time;

    private void Update()
    {
        _time += Time.deltaTime;

        float x = rotateX ? Mathf.Sin(_time * speedX) * angleLimitX : 0f;
        float y = rotateY ? Mathf.Sin(_time * speedY) * angleLimitY : 0f;
        float z = rotateZ ? Mathf.Sin(_time * speedZ) * angleLimitZ : 0f;

        transform.localRotation = Quaternion.Euler(x, y, z);
    }
}
