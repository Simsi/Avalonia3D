using System;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Core.Assets.Models;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Math;
using ThreeDEngine.Core.Scene;
using ThreeDEngine.Core.Spatial;

namespace ThreeDEngine.Core.Interaction;

public static class Raycaster
{
    [ThreadStatic]
    private static SpatialQueryScratch3D? _queryScratch;

    public static PickingResult? Pick(Scene3D scene, Vector2 viewportPosition, Vector2 viewportSize)
        => PickCore(scene, viewportPosition, viewportSize, null, null);

    public static PickingResult? Pick(
        Scene3D scene,
        Vector2 viewportPosition,
        Vector2 viewportSize,
        Func<Object3D, bool>? predicate)
        => PickCore(scene, viewportPosition, viewportSize, predicate, null);

    public static PickingResult? PickExcluding(
        Scene3D scene,
        Vector2 viewportPosition,
        Vector2 viewportSize,
        ISet<Object3D>? excludedObjects)
        => PickCore(scene, viewportPosition, viewportSize, null, excludedObjects);

    private static PickingResult? PickCore(
        Scene3D scene,
        Vector2 viewportPosition,
        Vector2 viewportSize,
        Func<Object3D, bool>? predicate,
        ISet<Object3D>? excludedObjects)
    {
        var ray = ProjectionHelper.CreateRay(scene.Camera, viewportPosition, viewportSize);
        PickingResult? closest = null;

        var scratch = _queryScratch ??= new SpatialQueryScratch3D();
        IReadOnlyList<Object3D> objects = scene.Registry.PickableIndex.QueryRay(ray, scratch);

        for (var objectIndex = 0; objectIndex < objects.Count; objectIndex++)
        {
            var obj = objects[objectIndex];
            if (excludedObjects is not null && excludedObjects.Contains(obj))
            {
                continue;
            }

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
                ? PickModelPart(modelPart, ray, scene.Performance)
                : PickObjectTriangles(obj, ray);

            if (hit is not null && (closest is null || hit.Distance < closest.Distance))
            {
                closest = hit;
            }
        }

        return closest;
    }

    private static PickingResult? PickObjectTriangles(Object3D obj, Ray worldRay)
    {
        var mesh = obj.GetMesh();
        if (mesh.Indices.Length < 3 || mesh.Positions.Length == 0)
        {
            return null;
        }

        var model = obj.GetModelMatrix();
        if (!TryCreateLocalRay(worldRay, model, out var localRay))
        {
            return null;
        }

        if (mesh.LocalBounds.IsValid && !IntersectsBounds(localRay, mesh.LocalBounds, out _, out _))
        {
            return null;
        }

        var bvh = mesh.RenderGeometry.GetBvh();
        if (!bvh.Raycast(localRay, mesh.Positions, mesh.Indices, out var localDistance, out var localPoint, out _))
        {
            return null;
        }

        var worldPoint = Vector3.Transform(localPoint, model);
        var worldDistance = Vector3.Distance(worldRay.Origin, worldPoint);
        return new PickingResult(obj, worldPoint, worldDistance);
    }

    private static PickingResult? PickModelPart(ModelPart3D part, Ray worldRay, ScenePerformanceOptions performance)
    {
        var model = part.GetModelMatrix();
        if (!TryCreateLocalRay(worldRay, model, out var localRay))
        {
            return null;
        }

        var useSkinnedPickingMesh = part.IsSkinned && part.CurrentSkinMatricesInternal.Length > 0;
        if (useSkinnedPickingMesh)
        {
            // CPU raycast meshes and their BVHs are intentionally deferred until the
            // cheap conservative world-bounds test passes. This keeps hover picking over large
            // animated models from deforming/rebuilding data for rays that cannot hit the part.
            var worldBounds = part.GetWorldBounds();
            if (!worldBounds.IsValid || !IntersectsBounds(worldRay, worldBounds, out var boundsNear, out var boundsFar))
            {
                return null;
            }

            if (performance.UseConservativeSkinnedPicking)
            {
                var distance = boundsNear >= 0f ? boundsNear : boundsFar;
                if (!float.IsFinite(distance) || distance < 0f)
                {
                    return null;
                }

                var worldPoint1 = worldRay.Origin + worldRay.Direction * distance;
                return new PickingResult(part, worldPoint1, distance, TryBuildBoundsModelHit(part, worldPoint1, distance));
            }
        }

        var mesh = useSkinnedPickingMesh ? part.GetCpuSkinnedPickingMesh() : part.GetMesh();
        var positions = mesh.Positions;
        var indices = mesh.Indices;
        if (indices.Length < 3 || positions.Length == 0)
        {
            return null;
        }

        if (mesh.LocalBounds.IsValid && !IntersectsBounds(localRay, mesh.LocalBounds, out _, out _))
        {
            return null;
        }

        var bvh = mesh.RenderGeometry.GetBvh();
        if (!bvh.Raycast(localRay, positions, indices, out _, out var localPoint, out var triangleIndex))
        {
            return null;
        }

        var worldPoint = Vector3.Transform(localPoint, model);
        var worldDistance = Vector3.Distance(worldRay.Origin, worldPoint);
        var baseIndex = triangleIndex * 3;
        var i0 = indices[baseIndex];
        var i1 = indices[baseIndex + 1];
        var i2 = indices[baseIndex + 2];
        var p0 = positions[i0];
        var p1 = positions[i1];
        var p2 = positions[i2];
        var normalMatrix = GeometryTransform3D.CreateNormalMatrix(model);
        var localNormal = Vector3.Cross(p1 - p0, p2 - p0);
        if (localNormal.LengthSquared() < 1e-12f && mesh.Normals.Length == positions.Length) localNormal = mesh.Normals[i0];
        var worldNormal = localNormal.LengthSquared() > 1e-12f
            ? GeometryTransform3D.TransformNormal(localNormal, normalMatrix)
            : Vector3.UnitY;

        var elementId = new ModelElementId3D(
            part.Model.Asset.AssetId,
            part.Node.Path,
            part.Node.Index,
            part.MeshAsset.Index,
            part.PrimitiveIndex,
            mesh.RenderGeometry.GetSourceTriangleIndex(triangleIndex));
        var modelHit = new ModelHitResult3D(part.Model, part, elementId, worldPoint, worldNormal, worldDistance);
        return new PickingResult(part, worldPoint, worldDistance, modelHit);
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

    private static bool TryCreateLocalRay(Ray worldRay, Matrix4x4 model, out Ray localRay)
    {
        if (!Matrix4x4.Invert(model, out var inverse))
        {
            localRay = default;
            return false;
        }

        var origin = Vector3.Transform(worldRay.Origin, inverse);
        var direction = Vector3.TransformNormal(worldRay.Direction, inverse);
        if (direction.LengthSquared() < 0.000001f || !IsFinite(origin) || !IsFinite(direction))
        {
            localRay = default;
            return false;
        }

        localRay = new Ray(origin, Vector3.Normalize(direction));
        return true;
    }

    private static bool IsValidTriangleIndex(int vertexCount, int i0, int i1, int i2)
        => i0 >= 0 && i0 < vertexCount &&
           i1 >= 0 && i1 < vertexCount &&
           i2 >= 0 && i2 < vertexCount;

    private static bool IntersectsBounds(Ray ray, Bounds3D bounds, out float near, out float far)
    {
        near = 0f;
        far = float.PositiveInfinity;
        if (!Slab(ray.Origin.X, ray.Direction.X, bounds.Min.X, bounds.Max.X, ref near, ref far)) return false;
        if (!Slab(ray.Origin.Y, ray.Direction.Y, bounds.Min.Y, bounds.Max.Y, ref near, ref far)) return false;
        if (!Slab(ray.Origin.Z, ray.Direction.Z, bounds.Min.Z, bounds.Max.Z, ref near, ref far)) return false;
        return far >= 0f && near <= far;
    }

    private static bool Slab(float origin, float direction, float min, float max, ref float near, ref float far)
    {
        if (System.MathF.Abs(direction) < 1e-6f)
        {
            return origin >= min && origin <= max;
        }

        var inv = 1f / direction;
        var t0 = (min - origin) * inv;
        var t1 = (max - origin) * inv;
        if (t0 > t1) (t0, t1) = (t1, t0);
        if (t0 > near) near = t0;
        if (t1 < far) far = t1;
        return near <= far;
    }

    private static bool IsFinite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);

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

}
