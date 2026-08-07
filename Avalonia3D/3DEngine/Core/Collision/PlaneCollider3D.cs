using System;
using System.Numerics;
using ThreeDEngine.Core.Math;
using ThreeDEngine.Core.Scene;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Collision;

public sealed class PlaneCollider3D : Collider3D
{
    private Vector3 _localNormal = Vector3.UnitY;
    private float _offset;
    private Vector2 _size = new(10f, 10f);
    private float _thickness = 0.01f;

    public Vector3 LocalNormal
    {
        get => _localNormal;
        set
        {
            using var mutation = EnterMutationScope();
            value = Guard3D.Finite(value, nameof(value));
            if (value.LengthSquared() <= 0.000001f) throw new ArgumentOutOfRangeException(nameof(value), value, "Plane normal must be non-zero.");
            value = Vector3.Normalize(value);
            if (_localNormal == value) return;
            _localNormal = value;
            RaiseChanged();
        }
    }

    public float Offset
    {
        get => _offset;
        set { using var mutation = EnterMutationScope(); value = Guard3D.Finite(value, nameof(value)); if (MathF.Abs(_offset - value) < 0.000001f) return; _offset = value; RaiseChanged(); }
    }

    public Vector2 Size
    {
        get => _size;
        set
        {
            using var mutation = EnterMutationScope();
            value = Guard3D.Finite(value, nameof(value));
            if (value.X <= 0f || value.Y <= 0f) throw new ArgumentOutOfRangeException(nameof(value), value, "Plane dimensions must be positive.");
            if (_size == value) return;
            _size = value;
            RaiseChanged();
        }
    }

    public float Thickness
    {
        get => _thickness;
        set { using var mutation = EnterMutationScope(); value = Guard3D.Positive(value, nameof(value)); if (MathF.Abs(_thickness - value) < 0.000001f) return; _thickness = value; RaiseChanged(); }
    }

    public override Bounds3D GetWorldBounds(Object3D owner)
    {
        var normal = GetSafeNormal();
        var thickness = Thickness * 0.5f;
        Vector3 half;
        if (System.MathF.Abs(normal.Z) >= System.MathF.Abs(normal.X) && System.MathF.Abs(normal.Z) >= System.MathF.Abs(normal.Y))
        {
            half = new Vector3(Size.X * 0.5f, Size.Y * 0.5f, thickness);
        }
        else if (System.MathF.Abs(normal.X) >= System.MathF.Abs(normal.Y))
        {
            half = new Vector3(thickness, Size.X * 0.5f, Size.Y * 0.5f);
        }
        else
        {
            half = new Vector3(Size.X * 0.5f, thickness, Size.Y * 0.5f);
        }

        var center = -normal * Offset;
        return new Bounds3D(center - half, center + half).Transform(owner.GetModelMatrix());
    }

    public override bool Raycast(Object3D owner, Ray ray, out RaycastHit3D hit)
    {
        var model = owner.GetModelMatrix();
        if (!Matrix4x4.Invert(model, out var inverse))
        {
            hit = default;
            return false;
        }

        var localOrigin = Vector3.Transform(ray.Origin, inverse);
        var transformedDirection = Vector3.TransformNormal(ray.Direction, inverse);
        if (transformedDirection.LengthSquared() < 0.000001f)
        {
            hit = default;
            return false;
        }

        var localDirection = Vector3.Normalize(transformedDirection);
        var normal = GetSafeNormal();
        var denominator = Vector3.Dot(localDirection, normal);
        if (MathF.Abs(denominator) < 0.000001f)
        {
            hit = default;
            return false;
        }

        var t = -(Vector3.Dot(localOrigin, normal) + Offset) / denominator;
        if (t < 0f)
        {
            hit = default;
            return false;
        }

        var localPoint = localOrigin + localDirection * t;
        if (!IsInsidePlaneArea(localPoint, normal))
        {
            hit = default;
            return false;
        }

        var worldPoint = Vector3.Transform(localPoint, model);
        var worldNormalVector = Vector3.TransformNormal(normal, model);
        var worldNormal = worldNormalVector.LengthSquared() < 0.000001f ? Vector3.UnitY : Vector3.Normalize(worldNormalVector);
        hit = new RaycastHit3D(owner, worldPoint, worldNormal, Vector3.Distance(ray.Origin, worldPoint));
        return true;
    }

    private Vector3 GetSafeNormal() => LocalNormal;

    private bool IsInsidePlaneArea(Vector3 p, Vector3 normal)
    {
        if (System.MathF.Abs(normal.Z) >= System.MathF.Abs(normal.X) && System.MathF.Abs(normal.Z) >= System.MathF.Abs(normal.Y))
        {
            return MathF.Abs(p.X) <= Size.X * 0.5f && MathF.Abs(p.Y) <= Size.Y * 0.5f;
        }
        if (System.MathF.Abs(normal.X) >= System.MathF.Abs(normal.Y))
        {
            return MathF.Abs(p.Y) <= Size.X * 0.5f && MathF.Abs(p.Z) <= Size.Y * 0.5f;
        }

        return MathF.Abs(p.X) <= Size.X * 0.5f && MathF.Abs(p.Z) <= Size.Y * 0.5f;
    }
}
