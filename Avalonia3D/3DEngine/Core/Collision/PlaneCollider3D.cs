using System;
using System.Numerics;
using ThreeDEngine.Core.Math;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Collision;

public sealed class PlaneCollider3D : Collider3D
{
    public Vector3 LocalNormal { get; set; } = Vector3.UnitY;
    public float Offset { get; set; }
    public Vector2 Size { get; set; } = new Vector2(10f, 10f);
    public float Thickness { get; set; } = 0.01f;

    public override Bounds3D GetWorldBounds(Object3D owner)
    {
        var normal = GetSafeNormal();
        var thickness = System.MathF.Max(Thickness, 0.001f) * 0.5f;
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

    private Vector3 GetSafeNormal()
    {
        return LocalNormal.LengthSquared() < 0.000001f ? Vector3.UnitY : Vector3.Normalize(LocalNormal);
    }

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
