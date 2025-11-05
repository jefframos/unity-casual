using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("FX/Simple Rotator")]
public class SimpleRotator : MonoBehaviour
{
    [Header("Rotation Speed (degrees per second)")]
    public float speedX = 0f;
    public float speedY = 90f;
    public float speedZ = 0f;

    [Header("Axis Toggles")]
    public bool rotateX = false;
    public bool rotateY = true;
    public bool rotateZ = false;

    [Header("Space")]
    [Tooltip("Choose if rotation happens in local or world space.")]
    public Space rotationSpace = Space.Self;

    private void Update()
    {
        // calculate rotation delta based on enabled axes
        float x = rotateX ? speedX * Time.deltaTime : 0f;
        float y = rotateY ? speedY * Time.deltaTime : 0f;
        float z = rotateZ ? speedZ * Time.deltaTime : 0f;

        // apply rotation
        transform.Rotate(x, y, z, rotationSpace);
    }
}
