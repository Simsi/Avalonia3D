namespace ThreeDEngine.Core.Scene;

/// <summary>
/// Alias primitive for an axis-aligned box. Rectangle3D is kept for backward compatibility.
/// </summary>
public sealed class Box3D : Rectangle3D
{
    public Box3D()
    {
        Name = "Box";
    }
}
