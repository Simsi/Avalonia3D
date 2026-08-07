using System;
using System.Numerics;
using ThreeDEngine.Core.Math;
using ThreeDEngine.Core.Scene;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Collision;

public sealed class SphereCollider3D : Collider3D
{
    private Vector3 _center;
    private float _radius = 0.5f;

    public Vector3 Center
    {
        get => _center;
        set { using var mutation = EnterMutationScope(); value = Guard3D.Finite(value, nameof(value)); if (_center == value) return; _center = value; RaiseChanged(); }
    }

    public float Radius
    {
        get => _radius;
        set { using var mutation = EnterMutationScope(); value = Guard3D.Positive(value, nameof(value)); if (MathF.Abs(_radius - value) < 0.000001f) return; _radius = value; RaiseChanged(); }
    }

    public override Bounds3D GetWorldBounds(Object3D owner)
    {
        var model = owner.GetModelMatrix();
        var center = Vector3.Transform(Center, model);
        var r = Radius * GetMaxAbsScale(model);
        return new Bounds3D(center - new Vector3(r), center + new Vector3(r));
    }

    public override bool Raycast(Object3D owner, Ray ray, out RaycastHit3D hit)
    {
        if (ray.Direction.LengthSquared() < 0.000001f)
        {
            hit = default;
            return false;
        }

        var direction = Vector3.Normalize(ray.Direction);
        var model = owner.GetModelMatrix();
        var center = Vector3.Transform(Center, model);
        var radius = Radius * GetMaxAbsScale(model);
        var oc = ray.Origin - center;
        var a = 1f;
        var b = 2f * Vector3.Dot(oc, direction);
        var c = Vector3.Dot(oc, oc) - radius * radius;
        var discriminant = b * b - 4f * a * c;
        if (discriminant < 0f)
        {
            hit = default;
            return false;
        }

        var sqrt = MathF.Sqrt(discriminant);
        var t = (-b - sqrt) / (2f * a);
        if (t < 0f)
        {
            t = (-b + sqrt) / (2f * a);
        }
        if (t < 0f)
        {
            hit = default;
            return false;
        }

        var point = ray.Origin + direction * t;
        var normalVector = point - center;
        var normal = normalVector.LengthSquared() < 0.000001f ? Vector3.UnitY : Vector3.Normalize(normalVector);
        hit = new RaycastHit3D(owner, point, normal, t);
        return true;
    }

    private static float GetMaxAbsScale(Matrix4x4 model)
    {
        var x = Vector3.TransformNormal(Vector3.UnitX, model).Length();
        var y = Vector3.TransformNormal(Vector3.UnitY, model).Length();
        var z = Vector3.TransformNormal(Vector3.UnitZ, model).Length();
        return MathF.Max(x, MathF.Max(y, z));
    }
}
