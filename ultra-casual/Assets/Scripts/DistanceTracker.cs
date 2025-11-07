using UnityEngine;

public class DistanceTracker : MonoBehaviour
{
    public Transform car;
    private float maxDistance;

    void Update()
    {
        if (car)
            maxDistance = Mathf.Max(maxDistance, car.position.z);
    }

    void OnGUI()
    {
        //GUI.Label(new Rect(20, 20, 200, 40), $"Distance: {maxDistance:F2}m");
    }
}
