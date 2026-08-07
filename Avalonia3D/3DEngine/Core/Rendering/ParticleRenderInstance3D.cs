using System.Numerics;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Backend-neutral packed particle instance. Billboard renderers use Center/Size/Color;
/// mesh particles use Model/Color. The Core particle stream owns this math so desktop
/// and browser paths cannot drift on scale/color/order rules.
/// </summary>
internal readonly struct ParticleRenderInstance3D
{
    public ParticleRenderInstance3D(Vector3 center, float size, Matrix4x4 model, ColorRgba color, float sortDistanceSquared)
    {
        Center = center;
        Size = size;
        Model = model;
        Color = color;
        SortDistanceSquared = sortDistanceSquared;
    }

    public Vector3 Center { get; }
    public float Size { get; }
    public Matrix4x4 Model { get; }
    public ColorRgba Color { get; }
    public float SortDistanceSquared { get; }
}
