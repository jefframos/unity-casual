using UnityEngine;

public class CollisionForwarder : MonoBehaviour
{
    public RagdollEnemy owner;   // auto-filled if left null
    Rigidbody _rb;

    void Awake()
    {
        if (owner == null) owner = GetComponentInParent<RagdollEnemy>();
        _rb = GetComponent<Rigidbody>(); // limb’s RB (may be null if collider is on a different RB)
    }

    void OnCollisionEnter(Collision c)
    {
        // Only forward real (non-trigger) contacts
        if (owner != null && !c.collider.isTrigger)
            owner.OnCollisionFromChild(c, _rb);
    }
}
