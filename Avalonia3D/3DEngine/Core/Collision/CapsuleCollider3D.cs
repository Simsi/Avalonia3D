using System;
using System.Numerics;
using ThreeDEngine.Core.Math;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Collision;

public sealed class CapsuleCollider3D : Collider3D
{
    public Vector3 Center { get; set; }
    public float Radius { get; set; } = 0.25f;
    public float Height { get; set; } = 1.8f;

    public override Bounds3D GetWorldBounds(Object3D owner)
    {
        var capsule = GetWorldCapsule(owner);
        var radius = new Vector3(capsule.Radius);
        return new Bounds3D(Vector3.Min(capsule.A, capsule.B) - radius, Vector3.Max(capsule.A, capsule.B) + radius);
    }

    public override bool Raycast(Object3D owner, Ray ray, out RaycastHit3D hit)
    {
        hit = default;
        if (ray.Direction.LengthSquared() < 0.000001f) return false;

        var capsule = GetWorldCapsule(owner);
        var direction = Vector3.Normalize(ray.Direction);
        var bestT = float.PositiveInfinity;
        var bestNormal = Vector3.UnitY;

        TryRaySphere(ray.Origin, direction, capsule.A, capsule.Radius, ref bestT, ref bestNormal);
        TryRaySphere(ray.Origin, direction, capsule.B, capsule.Radius, ref bestT, ref bestNormal);
        TryRayCylinder(ray.Origin, direction, capsule.A, capsule.B, capsule.Radius, ref bestT, ref bestNormal);

        if (!float.IsFinite(bestT) || bestT < 0f) return false;
        var point = ray.Origin + direction * bestT;
        hit = new RaycastHit3D(owner, point, bestNormal, bestT);
        return true;
    }

    private WorldCapsule GetWorldCapsule(Object3D owner)
    {
        var model = owner.GetModelMatrix();
        var center = Vector3.Transform(Center, model);
        var axisVector = Vector3.TransformNormal(Vector3.UnitY, model);
        var yScale = MathF.Max(axisVector.Length(), 0.0001f);
        var axis = axisVector.LengthSquared() > 0.000001f ? Vector3.Normalize(axisVector) : Vector3.UnitY;
        var sx = Vector3.TransformNormal(Vector3.UnitX, model).Length();
        var sz = Vector3.TransformNormal(Vector3.UnitZ, model).Length();
        var radius = MathF.Max(0.0001f, Radius * MathF.Max(sx, sz));
        var height = MathF.Max(radius * 2f, Height * yScale);
        var halfSegment = MathF.Max(0f, height * 0.5f - radius);
        return new WorldCapsule(center - axis * halfSegment, center + axis * halfSegment, radius);
    }

    private static void TryRaySphere(Vector3 origin, Vector3 direction, Vector3 center, float radius, ref float bestT, ref Vector3 bestNormal)
    {
        var oc = origin - center;
        var b = Vector3.Dot(oc, direction);
        var c = Vector3.Dot(oc, oc) - radius * radius;
        var discriminant = b * b - c;
        if (discriminant < 0f) return;
        var sqrt = MathF.Sqrt(discriminant);
        var t = -b - sqrt;
        if (t < 0f) t = -b + sqrt;
        if (t < 0f || t >= bestT) return;
        bestT = t;
        var point = origin + direction * t;
        var n = point - center;
        bestNormal = n.LengthSquared() > 0.000001f ? Vector3.Normalize(n) : Vector3.UnitY;
    }

    private static void TryRayCylinder(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, float radius, ref float bestT, ref Vector3 bestNormal)
    {
        var axisVector = b - a;
        var height = axisVector.Length();
        if (height <= 0.000001f) return;
        var axis = axisVector / height;
        var m = origin - a;
        var md = Vector3.Dot(m, axis);
        var nd = Vector3.Dot(direction, axis);
        var q = m - axis * md;
        var r = direction - axis * nd;
        var aa = Vector3.Dot(r, r);
        if (aa <= 0.000001f) return;
        var bb = 2f * Vector3.Dot(q, r);
        var cc = Vector3.Dot(q, q) - radius * radius;
        var discriminant = bb * bb - 4f * aa * cc;
        if (discriminant < 0f) return;
        var sqrt = MathF.Sqrt(discriminant);
        var t0 = (-bb - sqrt) / (2f * aa);
        var t1 = (-bb + sqrt) / (2f * aa);
        TryCylinderT(origin, direction, a, axis, height, t0, ref bestT, ref bestNormal);
        TryCylinderT(origin, direction, a, axis, height, t1, ref bestT, ref bestNormal);
    }

    private static void TryCylinderT(Vector3 origin, Vector3 direction, Vector3 a, Vector3 axis, float height, float t, ref float bestT, ref Vector3 bestNormal)
    {
        if (t < 0f || t >= bestT) return;
        var point = origin + direction * t;
        var y = Vector3.Dot(point - a, axis);
        if (y < 0f || y > height) return;
        var axisPoint = a + axis * y;
        var n = point - axisPoint;
        if (n.LengthSquared() <= 0.000001f) return;
        bestT = t;
        bestNormal = Vector3.Normalize(n);
    }

    private readonly record struct WorldCapsule(Vector3 A, Vector3 B, float Radius);
}
