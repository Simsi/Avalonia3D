using System;
using System.Numerics;
using ThreeDEngine.Core.Math;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Interaction;

public static class Raycaster
{
    public static PickingResult? Pick(Scene3D scene, Vector2 viewportPosition, Vector2 viewportSize)
        => Pick(scene, viewportPosition, viewportSize, null);

    public static PickingResult? Pick(
        Scene3D scene,
        Vector2 viewportPosition,
        Vector2 viewportSize,
        Func<Object3D, bool>? predicate)
    {
        var ray = ProjectionHelper.CreateRay(scene.Camera, viewportPosition, viewportSize);
        PickingResult? closest = null;

        var candidates = scene.Registry.PickableIndex.QueryRay(ray);
        var objects = candidates.Count == 0 ? scene.Registry.Pickables : candidates;

        foreach (var obj in objects)
        {
            if (predicate is not null && !predicate(obj))
            {
                continue;
            }

            if (obj.Collider is not null)
            {
                if (obj.Collider.Raycast(obj, ray, out var colliderHit) &&
                    (closest is null || colliderHit.Distance < closest.Distance))
                {
                    closest = new PickingResult(obj, colliderHit.Point, colliderHit.Distance);
                }

                continue;
            }

            var mesh = obj.GetMesh();
            var model = obj.GetModelMatrix();

            var boundsCenter = Vector3.Transform(Vector3.Zero, model);

            if (!IntersectsBoundingSphere(ray, boundsCenter, mesh.BoundingRadius * GetAbsMax(model)))
            {
                continue;
            }

            for (var i = 0; i < mesh.Indices.Length; i += 3)
            {
                var p0 = Vector3.Transform(mesh.Positions[mesh.Indices[i]], model);
                var p1 = Vector3.Transform(mesh.Positions[mesh.Indices[i + 1]], model);
                var p2 = Vector3.Transform(mesh.Positions[mesh.Indices[i + 2]], model);

                if (!IntersectTriangle(ray, p0, p1, p2, out var distance, out var worldPoint))
                {
                    continue;
                }

                if (closest is null || distance < closest.Distance)
                {
                    closest = new PickingResult(obj, worldPoint, distance);
                }
            }
        }

        return closest;
    }

    private static bool IntersectsBoundingSphere(Ray ray, Vector3 center, float radius)
    {
        var oc = ray.Origin - center;
        var b = Vector3.Dot(oc, ray.Direction);
        var c = Vector3.Dot(oc, oc) - (radius * radius);
        return (b * b) - c >= 0f;
    }

    public static bool IntersectTriangle(
        Ray ray,
        Vector3 v0,
        Vector3 v1,
        Vector3 v2,
        out float distance,
        out Vector3 point)
    {
        const float epsilon = 1e-6f;

        var edge1 = v1 - v0;
        var edge2 = v2 - v0;
        var pvec = Vector3.Cross(ray.Direction, edge2);
        var det = Vector3.Dot(edge1, pvec);

        if (System.MathF.Abs(det) < epsilon)
        {
            distance = 0f;
            point = default;
            return false;
        }

        var invDet = 1f / det;
        var tvec = ray.Origin - v0;
        var u = Vector3.Dot(tvec, pvec) * invDet;
        if (u < 0f || u > 1f)
        {
            distance = 0f;
            point = default;
            return false;
        }

        var qvec = Vector3.Cross(tvec, edge1);
        var v = Vector3.Dot(ray.Direction, qvec) * invDet;
        if (v < 0f || (u + v) > 1f)
        {
            distance = 0f;
            point = default;
            return false;
        }

        distance = Vector3.Dot(edge2, qvec) * invDet;
        if (distance < epsilon)
        {
            point = default;
            return false;
        }

        point = ray.Origin + (ray.Direction * distance);
        return true;
    }

    private static float GetAbsMax(Matrix4x4 model)
    {
        var x = Vector3.TransformNormal(Vector3.UnitX, model).Length();
        var y = Vector3.TransformNormal(Vector3.UnitY, model).Length();
        var z = Vector3.TransformNormal(Vector3.UnitZ, model).Length();
        return System.Math.Max(x, System.Math.Max(y, z));
    }
}
