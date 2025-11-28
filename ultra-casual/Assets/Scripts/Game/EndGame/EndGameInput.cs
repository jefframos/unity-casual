using UnityEngine;

[DisallowMultipleComponent]
public class EndgameGunInput : MonoBehaviour
{
    [Header("References")]
    public EndgameMinigameGun gun;
    public Camera targetCamera;

    [Tooltip("Layers that contain the targets (SphereColliders).")]
    public LayerMask targetLayerMask;

    [Tooltip("Max ray distance.")]
    public float maxRayDistance = 200f;

    void Awake()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
    }
    private void Reset()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
    }

    private void Update()
    {
        // Mouse (editor / desktop)
        if (Input.GetMouseButtonDown(0))
        {
            HandlePointer(Input.mousePosition);
        }

        // Touch (mobile)
        if (Input.touchCount > 0)
        {
            var touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began)
            {
                HandlePointer(touch.position);
            }
        }
    }

    private void HandlePointer(Vector2 screenPos)
    {
        if (targetCamera == null)
        {
            return;
        }

        gun.ShootAt(screenPos);

        Ray ray = targetCamera.ScreenPointToRay(screenPos);

        if (Physics.Raycast(
                ray,
                out RaycastHit hit,
                maxRayDistance,
                targetLayerMask,
                QueryTriggerInteraction.Ignore
            ))
        {
            var target = hit.collider.GetComponentInParent<EndgameMinigameTarget>();
            if (target != null)
            {
                target.ResolveHit();
            }
        }
    }
}
