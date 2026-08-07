using System;
using System.Numerics;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Geometry;

/// <summary>Correct transform helpers for geometry preprocessing.</summary>
public static class GeometryTransform3D
{
    public static Matrix4x4 CreateNormalMatrix(Matrix4x4 transform)
    {
        Guard3D.FiniteMatrix(transform, nameof(transform));
        if (!Matrix4x4.Invert(transform, out var inverse))
            throw new ArgumentException("A singular transform cannot be used to transform normals.", nameof(transform));
        return Matrix4x4.Transpose(inverse);
    }

    public static Vector3 TransformNormal(Vector3 normal, Matrix4x4 normalMatrix)
    {
        Guard3D.Finite(normal, nameof(normal));
        Guard3D.FiniteMatrix(normalMatrix, nameof(normalMatrix));
        var transformed = Vector3.TransformNormal(normal, normalMatrix);
        if (!float.IsFinite(transformed.X) || !float.IsFinite(transformed.Y) || !float.IsFinite(transformed.Z) || transformed.LengthSquared() <= 1e-16f)
            throw new ArgumentException("The transformed normal is non-finite or degenerate.", nameof(normal));
        return Vector3.Normalize(transformed);
    }

    public static Vector4 TransformTangent(Vector4 tangent, Matrix4x4 normalMatrix)
    {
        Guard3D.Finite(tangent, nameof(tangent));
        if (global::System.MathF.Abs(tangent.W) <= 1e-8f)
            throw new ArgumentException("Tangent handedness must be non-zero.", nameof(tangent));
        var direction = TransformNormal(new Vector3(tangent.X, tangent.Y, tangent.Z), normalMatrix);
        return new Vector4(direction, tangent.W < 0f ? -1f : 1f);
    }
}
