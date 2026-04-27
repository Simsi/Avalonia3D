using System;
using System.Numerics;

namespace ThreeDEngine.Core.Collision;

public readonly struct Bounds3D
{
    private Bounds3D(Vector3 min, Vector3 max, bool normalize)
    {
        if (normalize)
        {
            Min = Vector3.Min(min, max);
            Max = Vector3.Max(min, max);
        }
        else
        {
            Min = min;
            Max = max;
        }
    }

    public Bounds3D(Vector3 min, Vector3 max)
        : this(min, max, normalize: true)
    {
    }

    public Vector3 Min { get; }
    public Vector3 Max { get; }
    public Vector3 Center => IsValid ? (Min + Max) * 0.5f : Vector3.Zero;
    public Vector3 Size => IsValid ? Max - Min : Vector3.Zero;
    public bool IsValid => Min.X <= Max.X && Min.Y <= Max.Y && Min.Z <= Max.Z;

    public static Bounds3D Empty => new(
        new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity),
        new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity),
        normalize: false);

    public bool Intersects(Bounds3D other)
    {
        if (!IsValid || !other.IsValid)
        {
            return false;
        }

        return Min.X <= other.Max.X && Max.X >= other.Min.X &&
               Min.Y <= other.Max.Y && Max.Y >= other.Min.Y &&
               Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;
    }

    public bool Contains(Vector3 point)
    {
        if (!IsValid)
        {
            return false;
        }

        return point.X >= Min.X && point.X <= Max.X &&
               point.Y >= Min.Y && point.Y <= Max.Y &&
               point.Z >= Min.Z && point.Z <= Max.Z;
    }

    public Bounds3D Encapsulate(Vector3 point)
    {
        return IsValid
            ? new Bounds3D(Vector3.Min(Min, point), Vector3.Max(Max, point))
            : new Bounds3D(point, point);
    }

    public Bounds3D Encapsulate(Bounds3D bounds)
    {
        if (!bounds.IsValid)
        {
            return this;
        }

        if (!IsValid)
        {
            return bounds;
        }

        return new Bounds3D(Vector3.Min(Min, bounds.Min), Vector3.Max(Max, bounds.Max));
    }

    public Bounds3D Transform(Matrix4x4 matrix)
    {
        if (!IsValid)
        {
            return Empty;
        }

        var p0 = Vector3.Transform(new Vector3(Min.X, Min.Y, Min.Z), matrix);
        var min = p0;
        var max = p0;

        EncapsulateTransformed(new Vector3(Max.X, Min.Y, Min.Z), matrix, ref min, ref max);
        EncapsulateTransformed(new Vector3(Min.X, Max.Y, Min.Z), matrix, ref min, ref max);
        EncapsulateTransformed(new Vector3(Max.X, Max.Y, Min.Z), matrix, ref min, ref max);
        EncapsulateTransformed(new Vector3(Min.X, Min.Y, Max.Z), matrix, ref min, ref max);
        EncapsulateTransformed(new Vector3(Max.X, Min.Y, Max.Z), matrix, ref min, ref max);
        EncapsulateTransformed(new Vector3(Min.X, Max.Y, Max.Z), matrix, ref min, ref max);
        EncapsulateTransformed(new Vector3(Max.X, Max.Y, Max.Z), matrix, ref min, ref max);

        return new Bounds3D(min, max);
    }

    private static void EncapsulateTransformed(Vector3 point, Matrix4x4 matrix, ref Vector3 min, ref Vector3 max)
    {
        var transformed = Vector3.Transform(point, matrix);
        min = Vector3.Min(min, transformed);
        max = Vector3.Max(max, transformed);
    }
}
