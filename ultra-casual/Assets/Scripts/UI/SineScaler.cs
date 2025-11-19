using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("FX/Sine Scaler")]
public class SineScaler : MonoBehaviour
{
    [Header("Scale Range")]
    public float minScaleX = 1f;
    public float maxScaleX = 1.2f;

    public float minScaleY = 1f;
    public float maxScaleY = 1.2f;

    public float minScaleZ = 1f;
    public float maxScaleZ = 1.2f;

    [Header("Speed")]
    public float speedX = 2f;
    public float speedY = 2f;
    public float speedZ = 2f;

    [Header("Axis Toggles")]
    public bool scaleX = true;
    public bool scaleY = true;
    public bool scaleZ = true;

    private float _time;

    private void Update()
    {
        _time += Time.deltaTime;

        float x = transform.localScale.x;
        float y = transform.localScale.y;
        float z = transform.localScale.z;

        if (scaleX)
        {
            float t = (Mathf.Sin(_time * speedX) + 1f) * 0.5f;
            x = Mathf.Lerp(minScaleX, maxScaleX, t);
        }

        if (scaleY)
        {
            float t = (Mathf.Sin(_time * speedY) + 1f) * 0.5f;
            y = Mathf.Lerp(minScaleY, maxScaleY, t);
        }

        if (scaleZ)
        {
            float t = (Mathf.Sin(_time * speedZ) + 1f) * 0.5f;
            z = Mathf.Lerp(minScaleZ, maxScaleZ, t);
        }

        transform.localScale = new Vector3(x, y, z);
    }
}
