using UnityEngine;

[DisallowMultipleComponent]
public class EndgameGunInput : MonoBehaviour
{
    public EndgameMinigameGun gun;

    private void Update()
    {
        if (gun == null)
        {
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            Vector2 screenPos = Input.mousePosition;
            gun.ShootAt(screenPos);
        }
    }
}
