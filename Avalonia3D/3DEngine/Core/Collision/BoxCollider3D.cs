using System;
using System.Numerics;
using ThreeDEngine.Core.Math;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Collision;

public sealed class BoxCollider3D : Collider3D
{
    public Vector3 Center { get; set; }
    public Vector3 Size { get; set; } = Vector3.One;

    public override Bounds3D GetWorldBounds(Object3D owner)
    {
        var half = Size * 0.5f;
        return new Bounds3D(Center - half, Center + half).Transform(owner.GetModelMatrix());
    }

    public override bool Raycast(Object3D owner, Ray ray, out RaycastHit3D hit)
    {
        if (!Matrix4x4.Invert(owner.GetModelMatrix(), out var inverse))
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
        var half = Size * 0.5f;
        var min = Center - half;
        var max = Center + half;
        var tMin = 0f;
        var tMax = float.MaxValue;
        var normal = Vector3.Zero;

        if (!IntersectAxis(localOrigin.X, localDirection.X, min.X, max.X, new Vector3(MathF.Sign(-localDirection.X), 0f, 0f), ref tMin, ref tMax, ref normal) ||
            !IntersectAxis(localOrigin.Y, localDirection.Y, min.Y, max.Y, new Vector3(0f, MathF.Sign(-localDirection.Y), 0f), ref tMin, ref tMax, ref normal) ||
            !IntersectAxis(localOrigin.Z, localDirection.Z, min.Z, max.Z, new Vector3(0f, 0f, MathF.Sign(-localDirection.Z)), ref tMin, ref tMax, ref normal))
        {
            hit = default;
            return false;
        }

        var localPoint = localOrigin + localDirection * tMin;
        var worldPoint = Vector3.Transform(localPoint, owner.GetModelMatrix());
        var worldNormalVector = Vector3.TransformNormal(normal == Vector3.Zero ? Vector3.UnitY : normal, owner.GetModelMatrix());
        var worldNormal = worldNormalVector.LengthSquared() < 0.000001f ? Vector3.UnitY : Vector3.Normalize(worldNormalVector);
        hit = new RaycastHit3D(owner, worldPoint, worldNormal, Vector3.Distance(ray.Origin, worldPoint));
        return true;
    }

    private static bool IntersectAxis(float origin, float direction, float min, float max, Vector3 axisNormal, ref float tMin, ref float tMax, ref Vector3 normal)
    {
        if (MathF.Abs(direction) < 0.000001f)
        {
            return origin >= min && origin <= max;
        }

        var t1 = (min - origin) / direction;
        var t2 = (max - origin) / direction;
        var axisNearNormal = axisNormal;
        if (t1 > t2)
        {
            (t1, t2) = (t2, t1);
        }

        if (t1 > tMin)
        {
            tMin = t1;
            normal = axisNearNormal;
        }
        tMax = MathF.Min(tMax, t2);
        return tMin <= tMax;
    }
}
