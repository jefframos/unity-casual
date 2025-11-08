using UnityEngine;

[ExecuteAlways]
public class InflateBounds : MonoBehaviour
{
    public Vector3 extraExtent = new Vector3(200f, 200f, 200f);

    void OnEnable()
    {
        var mf = GetComponent<MeshFilter>();
        if (mf && mf.sharedMesh)
        {
            var b = mf.sharedMesh.bounds;
            b.extents += extraExtent;
            mf.sharedMesh.bounds = b;
        }
    }
}
