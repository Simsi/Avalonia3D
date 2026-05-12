using System;
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
    /// <summary>
    /// Plane representation extracted from the engine row-vector view-projection matrix.
    /// It intentionally omits the near plane for conservative camera-inside/near-crossing
    /// behavior, matching the browser retained ordinary culler.
    /// </summary>
    public readonly struct ClipFrustum3D
    {
        private readonly Vector4 _left;
        private readonly Vector4 _right;
        private readonly Vector4 _bottom;
        private readonly Vector4 _top;
        private readonly Vector4 _far;

        public ClipFrustum3D(Matrix4x4 viewProjection)
        {
            _left = new Vector4(viewProjection.M14 + viewProjection.M11, viewProjection.M24 + viewProjection.M21, viewProjection.M34 + viewProjection.M31, viewProjection.M44 + viewProjection.M41);
            _right = new Vector4(viewProjection.M14 - viewProjection.M11, viewProjection.M24 - viewProjection.M21, viewProjection.M34 - viewProjection.M31, viewProjection.M44 - viewProjection.M41);
            _bottom = new Vector4(viewProjection.M14 + viewProjection.M12, viewProjection.M24 + viewProjection.M22, viewProjection.M34 + viewProjection.M32, viewProjection.M44 + viewProjection.M42);
            _top = new Vector4(viewProjection.M14 - viewProjection.M12, viewProjection.M24 - viewProjection.M22, viewProjection.M34 - viewProjection.M32, viewProjection.M44 - viewProjection.M42);
            _far = new Vector4(viewProjection.M14 - viewProjection.M13, viewProjection.M24 - viewProjection.M23, viewProjection.M34 - viewProjection.M33, viewProjection.M44 - viewProjection.M43);
        }

        public bool IntersectsWorldAabb(Vector3 center, Vector3 extents)
            => !IsOutside(_left, center, extents) &&
               !IsOutside(_right, center, extents) &&
               !IsOutside(_bottom, center, extents) &&
               !IsOutside(_top, center, extents) &&
               !IsOutside(_far, center, extents);

        private static bool IsOutside(Vector4 plane, Vector3 center, Vector3 extents)
        {
            var radius = MathF.Abs(plane.X) * extents.X + MathF.Abs(plane.Y) * extents.Y + MathF.Abs(plane.Z) * extents.Z;
            var distance = plane.X * center.X + plane.Y * center.Y + plane.Z * center.Z + plane.W;
            return distance + radius < 0f;
        }
    }

    public static ClipFrustum3D ExtractClipFrustum(Matrix4x4 viewProjection) => new(viewProjection);

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

        // Be conservative around the near plane and camera-inside cases. Matrix4x4.CreatePerspectiveFieldOfView
        // uses a DirectX-style depth range (z in [0, w] after projection), not OpenGL's [-w, w].
        // The previous test used [-w, w] for Z and could incorrectly reject chunks/meshes depending on
        // camera angle. If any corner falls behind the eye (w <= 0), keep the object visible instead of
        // risking a false negative; high-scale chunk culling must prefer stability over aggressiveness.
        const float epsilon = 0.00001f;
        var allInFront =
            p0.W > epsilon && p1.W > epsilon && p2.W > epsilon && p3.W > epsilon &&
            p4.W > epsilon && p5.W > epsilon && p6.W > epsilon && p7.W > epsilon;

        if (!allInFront)
        {
            return true;
        }

        if (p0.X < -p0.W && p1.X < -p1.W && p2.X < -p2.W && p3.X < -p3.W && p4.X < -p4.W && p5.X < -p5.W && p6.X < -p6.W && p7.X < -p7.W) return false;
        if (p0.X > p0.W && p1.X > p1.W && p2.X > p2.W && p3.X > p3.W && p4.X > p4.W && p5.X > p5.W && p6.X > p6.W && p7.X > p7.W) return false;
        if (p0.Y < -p0.W && p1.Y < -p1.W && p2.Y < -p2.W && p3.Y < -p3.W && p4.Y < -p4.W && p5.Y < -p5.W && p6.Y < -p6.W && p7.Y < -p7.W) return false;
        if (p0.Y > p0.W && p1.Y > p1.W && p2.Y > p2.W && p3.Y > p3.W && p4.Y > p4.W && p5.Y > p5.W && p6.Y > p6.W && p7.Y > p7.W) return false;
        if (p0.Z < 0f && p1.Z < 0f && p2.Z < 0f && p3.Z < 0f && p4.Z < 0f && p5.Z < 0f && p6.Z < 0f && p7.Z < 0f) return false;
        if (p0.Z > p0.W && p1.Z > p1.W && p2.Z > p2.W && p3.Z > p3.W && p4.Z > p4.W && p5.Z > p5.W && p6.Z > p6.W && p7.Z > p7.W) return false;

        return true;
    }

    public static bool IntersectsLocalBounds(Bounds3D localBounds, Matrix4x4 model, Matrix4x4 viewProjection)
        => Intersects(localBounds, model * viewProjection);


    public static bool IntersectsLocalBounds(Bounds3D localBounds, Matrix4x4 model, ClipFrustum3D frustum)
    {
        if (!localBounds.IsValid)
        {
            return true;
        }

        var center = localBounds.Center;
        var extents = localBounds.Size * 0.5f;
        var worldCenter = new Vector3(
            model.M11 * center.X + model.M21 * center.Y + model.M31 * center.Z + model.M41,
            model.M12 * center.X + model.M22 * center.Y + model.M32 * center.Z + model.M42,
            model.M13 * center.X + model.M23 * center.Y + model.M33 * center.Z + model.M43);
        var worldExtents = new Vector3(
            MathF.Abs(model.M11) * extents.X + MathF.Abs(model.M21) * extents.Y + MathF.Abs(model.M31) * extents.Z,
            MathF.Abs(model.M12) * extents.X + MathF.Abs(model.M22) * extents.Y + MathF.Abs(model.M32) * extents.Z,
            MathF.Abs(model.M13) * extents.X + MathF.Abs(model.M23) * extents.Y + MathF.Abs(model.M33) * extents.Z);
        return frustum.IntersectsWorldAabb(worldCenter, worldExtents);
    }

    public static bool IntersectsLocalBounds(Bounds3D localBounds, float[] matrixData, int matrixOffset, ClipFrustum3D frustum)
    {
        if (!localBounds.IsValid)
        {
            return true;
        }

        if (matrixData is null || matrixOffset < 0 || matrixOffset + 15 >= matrixData.Length)
        {
            return true;
        }

        var center = localBounds.Center;
        var extents = localBounds.Size * 0.5f;
        var m11 = matrixData[matrixOffset];
        var m12 = matrixData[matrixOffset + 1];
        var m13 = matrixData[matrixOffset + 2];
        var m21 = matrixData[matrixOffset + 4];
        var m22 = matrixData[matrixOffset + 5];
        var m23 = matrixData[matrixOffset + 6];
        var m31 = matrixData[matrixOffset + 8];
        var m32 = matrixData[matrixOffset + 9];
        var m33 = matrixData[matrixOffset + 10];
        var m41 = matrixData[matrixOffset + 12];
        var m42 = matrixData[matrixOffset + 13];
        var m43 = matrixData[matrixOffset + 14];
        var worldCenter = new Vector3(
            m11 * center.X + m21 * center.Y + m31 * center.Z + m41,
            m12 * center.X + m22 * center.Y + m32 * center.Z + m42,
            m13 * center.X + m23 * center.Y + m33 * center.Z + m43);
        var worldExtents = new Vector3(
            MathF.Abs(m11) * extents.X + MathF.Abs(m21) * extents.Y + MathF.Abs(m31) * extents.Z,
            MathF.Abs(m12) * extents.X + MathF.Abs(m22) * extents.Y + MathF.Abs(m32) * extents.Z,
            MathF.Abs(m13) * extents.X + MathF.Abs(m23) * extents.Y + MathF.Abs(m33) * extents.Z);
        return frustum.IntersectsWorldAabb(worldCenter, worldExtents);
    }
}
