using System;
using System.Numerics;
using ThreeDEngine.Core.Assets.Models;
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
        var objects = candidates.Count == 0 && scene.Performance.AllowPickingFullScanFallback && scene.Registry.Pickables.Count <= scene.Performance.MaxPickingFullScanFallbackObjects
            ? scene.Registry.SnapshotPickables()
            : candidates;

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
                    closest = new PickingResult(obj, colliderHit.Point, colliderHit.Distance, TryBuildBoundsModelHit(obj, colliderHit.Point, colliderHit.Distance));
                }

                continue;
            }

            var hit = obj is ModelPart3D modelPart
                ? PickModelPart(modelPart, ray)
                : PickObjectTriangles(obj, ray);

            if (hit is not null && (closest is null || hit.Distance < closest.Distance))
            {
                closest = hit;
            }
        }

        return closest;
    }

    private static PickingResult? PickObjectTriangles(Object3D obj, Ray ray)
    {
        var mesh = obj.GetMesh();
        if (mesh.Indices.Length < 3 || mesh.Positions.Length == 0)
        {
            return null;
        }

        var model = obj.GetModelMatrix();
        var sphereCenter = mesh.LocalBounds.IsValid
            ? Vector3.Transform(mesh.LocalBounds.Center, model)
            : Vector3.Transform(Vector3.Zero, model);

        if (!IntersectsBoundingSphere(ray, sphereCenter, mesh.BoundingRadius * GetAbsMax(model)))
        {
            return null;
        }

        PickingResult? closest = null;
        for (var i = 0; i + 2 < mesh.Indices.Length; i += 3)
        {
            var i0 = mesh.Indices[i];
            var i1 = mesh.Indices[i + 1];
            var i2 = mesh.Indices[i + 2];
            if (!IsValidTriangleIndex(mesh.Positions.Length, i0, i1, i2))
            {
                continue;
            }

            var p0 = Vector3.Transform(mesh.Positions[i0], model);
            var p1 = Vector3.Transform(mesh.Positions[i1], model);
            var p2 = Vector3.Transform(mesh.Positions[i2], model);

            if (!IntersectTriangle(ray, p0, p1, p2, out var distance, out var worldPoint))
            {
                continue;
            }

            if (closest is null || distance < closest.Distance)
            {
                closest = new PickingResult(obj, worldPoint, distance);
            }
        }

        return closest;
    }

    private static PickingResult? PickModelPart(ModelPart3D part, Ray ray)
    {
        var mesh = part.GetMesh();
        var model = part.GetModelMatrix();
        var sphereCenter = mesh.LocalBounds.IsValid
            ? Vector3.Transform(mesh.LocalBounds.Center, model)
            : Vector3.Transform(Vector3.Zero, model);

        if (!IntersectsBoundingSphere(ray, sphereCenter, mesh.BoundingRadius * GetAbsMax(model)))
        {
            return null;
        }

        PickingResult? closest = null;
        for (var i = 0; i + 2 < part.Primitive.Indices.Length; i += 3)
        {
            var i0 = part.Primitive.Indices[i];
            var i1 = part.Primitive.Indices[i + 1];
            var i2 = part.Primitive.Indices[i + 2];
            if (!IsValidTriangleIndex(part.Primitive.Positions.Length, i0, i1, i2))
            {
                continue;
            }

            var p0 = Vector3.Transform(part.Primitive.Positions[i0], model);
            var p1 = Vector3.Transform(part.Primitive.Positions[i1], model);
            var p2 = Vector3.Transform(part.Primitive.Positions[i2], model);

            if (!IntersectTriangle(ray, p0, p1, p2, out var distance, out var worldPoint))
            {
                continue;
            }

            if (closest is not null && distance >= closest.Distance)
            {
                continue;
            }

            var triangleIndex = i / 3;
            var worldNormal = Vector3.Cross(p1 - p0, p2 - p0);
            if (worldNormal.LengthSquared() < 1e-12f && part.Primitive.Normals.Length == part.Primitive.Positions.Length)
            {
                worldNormal = Vector3.TransformNormal(part.Primitive.Normals[i0], model);
            }

            var elementId = new ModelElementId3D(
                part.Model.Asset.AssetId,
                part.Node.Path,
                part.Node.Index,
                part.MeshAsset.Index,
                part.PrimitiveIndex,
                triangleIndex);
            var modelHit = new ModelHitResult3D(part.Model, part, elementId, worldPoint, worldNormal, distance);
            closest = new PickingResult(part, worldPoint, distance, modelHit);
        }

        return closest;
    }

    private static ModelHitResult3D? TryBuildBoundsModelHit(Object3D obj, Vector3 point, float distance)
    {
        if (obj is not ModelPart3D part)
        {
            return null;
        }

        var elementId = new ModelElementId3D(
            part.Model.Asset.AssetId,
            part.Node.Path,
            part.Node.Index,
            part.MeshAsset.Index,
            part.PrimitiveIndex);
        return new ModelHitResult3D(part.Model, part, elementId, point, Vector3.UnitY, distance);
    }

    private static bool IsValidTriangleIndex(int vertexCount, int i0, int i1, int i2)
        => i0 >= 0 && i0 < vertexCount &&
           i1 >= 0 && i1 < vertexCount &&
           i2 >= 0 && i2 < vertexCount;

    private static bool IntersectsBoundingSphere(Ray ray, Vector3 center, float radius)
    {
        if (radius <= 0f) return false;
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
