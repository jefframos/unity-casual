using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public class SlingshotUIBridge : MonoBehaviour
{
    [Tooltip("If true, blocks slingshot input when pointer/touch is over UI.")]
    public bool blockWhenPointerOverUI = true;

    public bool IsBlockedNow()
    {
        if (!blockWhenPointerOverUI)
        {
            return false;
        }

        // Mouse
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            return true;
        }

        // Touch (check first finger)
        if (Input.touchCount > 0 && EventSystem.current != null)
        {
            var t0 = Input.GetTouch(0);
            if (EventSystem.current.IsPointerOverGameObject(t0.fingerId))
            {
                return true;
            }
        }

        return false;
    }
}
