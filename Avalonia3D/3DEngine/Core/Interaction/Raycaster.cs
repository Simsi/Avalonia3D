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
    private const int MaxCachedBvhs = 256;
    private static readonly object BvhCacheLock = new();
    private static readonly Dictionary<MeshBvhCacheKey, BvhCacheEntry> BvhCache = new();
    private static readonly LinkedList<MeshBvhCacheKey> BvhLru = new();

    [ThreadStatic]
    private static SpatialQueryScratch3D? _queryScratch;

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

        var scratch = _queryScratch ??= new SpatialQueryScratch3D();
        var candidates = scene.Registry.PickableIndex.QueryRay(ray, scratch);
        IReadOnlyList<Object3D> objects = candidates.Count == 0 && scene.Performance.AllowPickingFullScanFallback && scene.Registry.Pickables.Count <= scene.Performance.MaxPickingFullScanFallbackObjects
            ? scene.Registry.SnapshotPickables()
            : candidates;

        for (var objectIndex = 0; objectIndex < objects.Count; objectIndex++)
        {
            var obj = objects[objectIndex];
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

        var bvh = GetOrCreateBvh(mesh.ResourceKey, mesh.GeometryVersion, mesh.Positions, mesh.Indices);
        if (!bvh.Raycast(localRay, mesh.Positions, mesh.Indices, out var localDistance, out var localPoint, out _))
        {
            return null;
        }

        var worldPoint = Vector3.Transform(localPoint, model);
        var worldDistance = Vector3.Distance(worldRay.Origin, worldPoint);
        return new PickingResult(obj, worldPoint, worldDistance);
    }

    private static PickingResult? PickModelPart(ModelPart3D part, Ray worldRay)
    {
        var model = part.GetModelMatrix();
        if (!TryCreateLocalRay(worldRay, model, out var localRay))
        {
            return null;
        }

        var useSkinnedFallback = part.IsSkinned && part.CurrentSkinMatrices.Length > 0;
        if (useSkinnedFallback)
        {
            // CPU-skinned fallback meshes and their BVHs are intentionally deferred until the
            // cheap conservative world-bounds test passes. This keeps hover picking over large
            // animated models from deforming/rebuilding data for rays that cannot hit the part.
            var worldBounds = part.GetWorldBounds();
            if (worldBounds.IsValid && !IntersectsBounds(worldRay, worldBounds, out _, out _))
            {
                return null;
            }
        }

        var mesh = useSkinnedFallback ? part.GetCpuSkinnedFallbackMesh() : part.GetMesh();
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

        var bvh = GetOrCreateBvh(mesh.ResourceKey, mesh.GeometryVersion, positions, indices);
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
        var worldNormal = Vector3.TransformNormal(Vector3.Cross(p1 - p0, p2 - p0), model);
        if (worldNormal.LengthSquared() < 1e-12f && mesh.Normals.Length == positions.Length)
        {
            worldNormal = Vector3.TransformNormal(mesh.Normals[i0], model);
        }
        if (worldNormal.LengthSquared() > 1e-12f)
        {
            worldNormal = Vector3.Normalize(worldNormal);
        }

        var elementId = new ModelElementId3D(
            part.Model.Asset.AssetId,
            part.Node.Path,
            part.Node.Index,
            part.MeshAsset.Index,
            part.PrimitiveIndex,
            triangleIndex);
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

    private static TriangleBvh GetOrCreateBvh(string resourceKey, int geometryVersion, Vector3[] positions, int[] indices)
        => GetOrCreateBvh(new MeshBvhCacheKey(resourceKey, geometryVersion), positions, indices);

    private static TriangleBvh GetOrCreateBvh(MeshBvhCacheKey key, Vector3[] positions, int[] indices)
    {
        lock (BvhCacheLock)
        {
            if (BvhCache.TryGetValue(key, out var entry))
            {
                BvhLru.Remove(entry.Node);
                BvhLru.AddFirst(entry.Node);
                return entry.Bvh;
            }

            var bvh = TriangleBvh.Build(positions, indices);
            var node = new LinkedListNode<MeshBvhCacheKey>(key);
            BvhLru.AddFirst(node);
            BvhCache[key] = new BvhCacheEntry(bvh, node);
            while (BvhCache.Count > MaxCachedBvhs && BvhLru.Last is not null)
            {
                var evict = BvhLru.Last;
                BvhLru.RemoveLast();
                BvhCache.Remove(evict.Value);
            }

            return bvh;
        }
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

    private readonly struct MeshBvhCacheKey : IEquatable<MeshBvhCacheKey>
    {
        private readonly string _resourceKey;
        private readonly int _version;

        public MeshBvhCacheKey(string resourceKey, int version)
        {
            _resourceKey = resourceKey ?? string.Empty;
            _version = version;
        }

        public bool Equals(MeshBvhCacheKey other) => _version == other._version && string.Equals(_resourceKey, other._resourceKey, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is MeshBvhCacheKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_resourceKey, _version);
    }

    private sealed class BvhCacheEntry
    {
        public BvhCacheEntry(TriangleBvh bvh, LinkedListNode<MeshBvhCacheKey> node)
        {
            Bvh = bvh;
            Node = node;
        }

        public TriangleBvh Bvh { get; }
        public LinkedListNode<MeshBvhCacheKey> Node { get; }
    }

    private sealed class TriangleBvh
    {
        private const int LeafTriangleCount = 12;
        private readonly Node[] _nodes;
        private readonly int[] _triangleIndices;

        private TriangleBvh(Node[] nodes, int[] triangleIndices)
        {
            _nodes = nodes;
            _triangleIndices = triangleIndices;
        }

        public static TriangleBvh Build(Vector3[] positions, int[] indices)
        {
            var triangleCount = indices.Length / 3;
            if (triangleCount <= 0) return new TriangleBvh(Array.Empty<Node>(), Array.Empty<int>());
            var triangles = new int[triangleCount];
            for (var i = 0; i < triangles.Length; i++) triangles[i] = i;
            var nodes = new List<Node>(triangleCount / LeafTriangleCount + 1);
            BuildNode(positions, indices, triangles, 0, triangles.Length, nodes);
            return new TriangleBvh(nodes.ToArray(), triangles);
        }

        public bool Raycast(Ray ray, Vector3[] positions, int[] indices, out float distance, out Vector3 point, out int triangleIndex)
        {
            distance = float.PositiveInfinity;
            point = default;
            triangleIndex = -1;
            if (_nodes.Length == 0) return false;
            var hit = false;
            Span<int> stack = stackalloc int[64];
            var stackCount = 0;
            stack[stackCount++] = 0;
            while (stackCount > 0)
            {
                var nodeIndex = stack[--stackCount];
                var node = _nodes[nodeIndex];
                if (!IntersectsBounds(ray, node.Bounds, out var near, out _) || near > distance) continue;
                if (node.IsLeaf)
                {
                    for (var i = 0; i < node.Count; i++)
                    {
                        var tri = _triangleIndices[node.Start + i];
                        var baseIndex = tri * 3;
                        var i0 = indices[baseIndex];
                        var i1 = indices[baseIndex + 1];
                        var i2 = indices[baseIndex + 2];
                        if (!IsValidTriangleIndex(positions.Length, i0, i1, i2)) continue;
                        if (!IntersectTriangle(ray, positions[i0], positions[i1], positions[i2], out var triDistance, out var triPoint)) continue;
                        if (triDistance >= distance) continue;
                        distance = triDistance;
                        point = triPoint;
                        triangleIndex = tri;
                        hit = true;
                    }
                }
                else
                {
                    if (stackCount + 2 >= stack.Length)
                    {
                        continue;
                    }
                    stack[stackCount++] = node.Left;
                    stack[stackCount++] = node.Right;
                }
            }

            return hit;
        }

        private static int BuildNode(Vector3[] positions, int[] indices, int[] triangles, int start, int count, List<Node> nodes)
        {
            var nodeIndex = nodes.Count;
            nodes.Add(default);
            var bounds = ComputeBounds(positions, indices, triangles, start, count);
            if (count <= LeafTriangleCount)
            {
                nodes[nodeIndex] = new Node(bounds, start, count, -1, -1);
                return nodeIndex;
            }

            var centroidBounds = ComputeCentroidBounds(positions, indices, triangles, start, count);
            var axis = LongestAxis(centroidBounds.Size);
            Array.Sort(triangles, start, count, new TriangleCentroidComparer(positions, indices, axis));
            var leftCount = count / 2;
            var left = BuildNode(positions, indices, triangles, start, leftCount, nodes);
            var right = BuildNode(positions, indices, triangles, start + leftCount, count - leftCount, nodes);
            nodes[nodeIndex] = new Node(bounds, start, count, left, right);
            return nodeIndex;
        }

        private static Bounds3D ComputeBounds(Vector3[] positions, int[] indices, int[] triangles, int start, int count)
        {
            var bounds = Bounds3D.Empty;
            for (var i = 0; i < count; i++)
            {
                var tri = triangles[start + i] * 3;
                var i0 = indices[tri];
                var i1 = indices[tri + 1];
                var i2 = indices[tri + 2];
                if (!IsValidTriangleIndex(positions.Length, i0, i1, i2)) continue;
                bounds = bounds.Encapsulate(positions[i0]).Encapsulate(positions[i1]).Encapsulate(positions[i2]);
            }
            return bounds;
        }

        private static Bounds3D ComputeCentroidBounds(Vector3[] positions, int[] indices, int[] triangles, int start, int count)
        {
            var bounds = Bounds3D.Empty;
            for (var i = 0; i < count; i++)
            {
                bounds = bounds.Encapsulate(Centroid(positions, indices, triangles[start + i]));
            }
            return bounds;
        }

        private static Vector3 Centroid(Vector3[] positions, int[] indices, int triangle)
        {
            var baseIndex = triangle * 3;
            var i0 = indices[baseIndex];
            var i1 = indices[baseIndex + 1];
            var i2 = indices[baseIndex + 2];
            if (!IsValidTriangleIndex(positions.Length, i0, i1, i2)) return Vector3.Zero;
            return (positions[i0] + positions[i1] + positions[i2]) / 3f;
        }

        private static int LongestAxis(Vector3 size)
            => size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;

        private readonly struct Node
        {
            public Node(Bounds3D bounds, int start, int count, int left, int right)
            {
                Bounds = bounds;
                Start = start;
                Count = count;
                Left = left;
                Right = right;
            }

            public Bounds3D Bounds { get; }
            public int Start { get; }
            public int Count { get; }
            public int Left { get; }
            public int Right { get; }
            public bool IsLeaf => Left < 0 || Right < 0;
        }

        private sealed class TriangleCentroidComparer : IComparer<int>
        {
            private readonly Vector3[] _positions;
            private readonly int[] _indices;
            private readonly int _axis;

            public TriangleCentroidComparer(Vector3[] positions, int[] indices, int axis)
            {
                _positions = positions;
                _indices = indices;
                _axis = axis;
            }

            public int Compare(int x, int y)
            {
                var cx = Centroid(_positions, _indices, x);
                var cy = Centroid(_positions, _indices, y);
                var vx = _axis == 0 ? cx.X : _axis == 1 ? cx.Y : cx.Z;
                var vy = _axis == 0 ? cy.X : _axis == 1 ? cy.Y : cy.Z;
                return vx.CompareTo(vy);
            }
        }
    }
}
