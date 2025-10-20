using UnityEngine;

public static class ResetHelpers
{
    public static void SetLinearVelocity(this Rigidbody rb, Vector3 v)
    {
#if UNITY_6000_0_OR_NEWER || UNITY_6_0_OR_NEWER || UNITY_6000 || UNITY_6
        rb.linearVelocity = v;
#else
        rb.velocity = v;
#endif
    }

    public static Vector3 GetLinearVelocity(this Rigidbody rb)
    {
#if UNITY_6000_0_OR_NEWER || UNITY_6_0_OR_NEWER || UNITY_6000 || UNITY_6
        return rb.linearVelocity;
#else
        return rb.velocity;
#endif
    }
}
