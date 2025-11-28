using UnityEngine;

/// <summary>
/// Asset preview grid component.
/// This component gives you options for rearranging its children.
/// </summary>
public class AssetPreviewGrid : MonoBehaviour
{
    // Grid width.
    [SerializeField]
    private int gridWidth = 10;

    // Spacing between elements.
    [SerializeField]
    private float offset = 3;

    /// <summary>
    /// Call this method to rearrange elements under the game object that has this script attached.
    /// </summary>
    public void RearrangeElements()
    {
        Debug.Log("Rearranging elements");

        // Iterate over all children.
        for (int i = 0; i < transform.childCount; i++)
        {
            var child = transform.GetChild(i);
            child.position = new Vector3(i % gridWidth * offset, 0, (i / gridWidth) * offset);
        }
    }
}
