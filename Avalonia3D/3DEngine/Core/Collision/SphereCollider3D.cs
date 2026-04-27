using System;
using System.Numerics;
using ThreeDEngine.Core.Math;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Collision;

public sealed class SphereCollider3D : Collider3D
{
    public Vector3 Center { get; set; }
    public float Radius { get; set; } = 0.5f;

    public override Bounds3D GetWorldBounds(Object3D owner)
    {
        var model = owner.GetModelMatrix();
        var center = Vector3.Transform(Center, model);
        var r = MathF.Max(0f, Radius * GetMaxAbsScale(model));
        return new Bounds3D(center - new Vector3(r), center + new Vector3(r));
    }

    public override bool Raycast(Object3D owner, Ray ray, out RaycastHit3D hit)
    {
        var model = owner.GetModelMatrix();
        var center = Vector3.Transform(Center, model);
        var radius = MathF.Max(0f, Radius * GetMaxAbsScale(model));
        var oc = ray.Origin - center;
        var a = Vector3.Dot(ray.Direction, ray.Direction);
        var b = 2f * Vector3.Dot(oc, ray.Direction);
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

        var point = ray.Origin + ray.Direction * t;
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
