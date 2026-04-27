using System.Numerics;
using ThreeDEngine.Core.Collision;

namespace ThreeDEngine.Core.Culling;

/// <summary>
/// Allocation-free clip-space frustum culler for renderer hot paths.
/// It intentionally works on the engine's existing row-vector Matrix4x4 pipeline:
/// Vector4.Transform(position, world * view * projection).
/// </summary>
public static class FrustumCuller3D
{
    public static bool Intersects(Bounds3D bounds, Matrix4x4 worldViewProjection)
    {
        if (!bounds.IsValid)
        {
            return true;
        }

        var min = bounds.Min;
        var max = bounds.Max;

        var p0 = Vector4.Transform(new Vector4(min.X, min.Y, min.Z, 1f), worldViewProjection);
        var p1 = Vector4.Transform(new Vector4(max.X, min.Y, min.Z, 1f), worldViewProjection);
        var p2 = Vector4.Transform(new Vector4(min.X, max.Y, min.Z, 1f), worldViewProjection);
        var p3 = Vector4.Transform(new Vector4(max.X, max.Y, min.Z, 1f), worldViewProjection);
        var p4 = Vector4.Transform(new Vector4(min.X, min.Y, max.Z, 1f), worldViewProjection);
        var p5 = Vector4.Transform(new Vector4(max.X, min.Y, max.Z, 1f), worldViewProjection);
        var p6 = Vector4.Transform(new Vector4(min.X, max.Y, max.Z, 1f), worldViewProjection);
        var p7 = Vector4.Transform(new Vector4(max.X, max.Y, max.Z, 1f), worldViewProjection);

        if (p0.X < -p0.W && p1.X < -p1.W && p2.X < -p2.W && p3.X < -p3.W && p4.X < -p4.W && p5.X < -p5.W && p6.X < -p6.W && p7.X < -p7.W) return false;
        if (p0.X > p0.W && p1.X > p1.W && p2.X > p2.W && p3.X > p3.W && p4.X > p4.W && p5.X > p5.W && p6.X > p6.W && p7.X > p7.W) return false;
        if (p0.Y < -p0.W && p1.Y < -p1.W && p2.Y < -p2.W && p3.Y < -p3.W && p4.Y < -p4.W && p5.Y < -p5.W && p6.Y < -p6.W && p7.Y < -p7.W) return false;
        if (p0.Y > p0.W && p1.Y > p1.W && p2.Y > p2.W && p3.Y > p3.W && p4.Y > p4.W && p5.Y > p5.W && p6.Y > p6.W && p7.Y > p7.W) return false;
        if (p0.Z < -p0.W && p1.Z < -p1.W && p2.Z < -p2.W && p3.Z < -p3.W && p4.Z < -p4.W && p5.Z < -p5.W && p6.Z < -p6.W && p7.Z < -p7.W) return false;
        if (p0.Z > p0.W && p1.Z > p1.W && p2.Z > p2.W && p3.Z > p3.W && p4.Z > p4.W && p5.Z > p5.W && p6.Z > p6.W && p7.Z > p7.W) return false;

        return true;
    }

    public static bool IntersectsLocalBounds(Bounds3D localBounds, Matrix4x4 model, Matrix4x4 viewProjection)
        => Intersects(localBounds, model * viewProjection);
}
