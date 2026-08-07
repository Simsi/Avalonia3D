using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Math;

namespace ThreeDEngine.Core.Geometry;

/// <summary>Immutable triangle BVH owned lazily by a geometry resource.</summary>
internal sealed class MeshBvh3D
{
    private const int LeafTriangleCount = 12;
    private readonly Node[] _nodes;
    private readonly int[] _triangleIndices;

    private MeshBvh3D(Node[] nodes, int[] triangleIndices)
    {
        _nodes = nodes;
        _triangleIndices = triangleIndices;
    }

    public int NodeCount => _nodes.Length;
    public long EstimatedResidentBytes => _nodes.LongLength * 48L + _triangleIndices.LongLength * sizeof(int);

    public static MeshBvh3D Build(RenderGeometry3D geometry)
    {
        if (geometry is null) throw new ArgumentNullException(nameof(geometry));
        var triangleCount = geometry.Indices.Length / 3;
        if (triangleCount <= 0) return new MeshBvh3D(Array.Empty<Node>(), Array.Empty<int>());
        var triangles = new int[triangleCount];
        for (var i = 0; i < triangles.Length; i++) triangles[i] = i;
        var nodes = new List<Node>(triangleCount / LeafTriangleCount + 1);
        BuildNode(geometry.Positions, geometry.Indices, triangles, 0, triangles.Length, nodes);
        return new MeshBvh3D(nodes.ToArray(), triangles);
    }

    public bool Raycast(
        Ray ray,
        GeometryBuffer3D<Vector3> positions,
        GeometryIndexBuffer3D indices,
        out float distance,
        out Vector3 point,
        out int triangleIndex)
    {
        distance = float.PositiveInfinity;
        point = default;
        triangleIndex = -1;
        if (_nodes.Length == 0) return false;

        var rented = (int[]?)null;
        Span<int> stack = _nodes.Length <= 128 ? stackalloc int[128] : (rented = ArrayPool<int>.Shared.Rent(_nodes.Length));
        try
        {
            var hit = false;
            var stackCount = 0;
            stack[stackCount++] = 0;
            while (stackCount > 0)
            {
                var nodeIndex = stack[--stackCount];
                var node = _nodes[nodeIndex];
                if (!IntersectsBounds(ray, node.Bounds, out var near) || near > distance) continue;
                if (node.IsLeaf)
                {
                    for (var i = 0; i < node.Count; i++)
                    {
                        var triangle = _triangleIndices[node.Start + i];
                        var baseIndex = triangle * 3;
                        var i0 = indices[baseIndex];
                        var i1 = indices[baseIndex + 1];
                        var i2 = indices[baseIndex + 2];
                        if (!IntersectTriangle(ray, positions[i0], positions[i1], positions[i2], out var candidateDistance, out var candidatePoint)) continue;
                        if (candidateDistance >= distance) continue;
                        distance = candidateDistance;
                        point = candidatePoint;
                        triangleIndex = triangle;
                        hit = true;
                    }
                }
                else
                {
                    stack[stackCount++] = node.Left;
                    stack[stackCount++] = node.Right;
                }
            }
            return hit;
        }
        finally
        {
            if (rented is not null) ArrayPool<int>.Shared.Return(rented);
        }
    }

    private static int BuildNode(
        GeometryBuffer3D<Vector3> positions,
        GeometryIndexBuffer3D indices,
        int[] triangles,
        int start,
        int count,
        List<Node> nodes)
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

    private static Bounds3D ComputeBounds(GeometryBuffer3D<Vector3> positions, GeometryIndexBuffer3D indices, int[] triangles, int start, int count)
    {
        var bounds = Bounds3D.Empty;
        for (var i = 0; i < count; i++)
        {
            var triangle = triangles[start + i] * 3;
            bounds = bounds.Encapsulate(positions[indices[triangle]])
                .Encapsulate(positions[indices[triangle + 1]])
                .Encapsulate(positions[indices[triangle + 2]]);
        }
        return bounds;
    }

    private static Bounds3D ComputeCentroidBounds(GeometryBuffer3D<Vector3> positions, GeometryIndexBuffer3D indices, int[] triangles, int start, int count)
    {
        var bounds = Bounds3D.Empty;
        for (var i = 0; i < count; i++) bounds = bounds.Encapsulate(Centroid(positions, indices, triangles[start + i]));
        return bounds;
    }

    private static Vector3 Centroid(GeometryBuffer3D<Vector3> positions, GeometryIndexBuffer3D indices, int triangle)
    {
        var offset = triangle * 3;
        return (positions[indices[offset]] + positions[indices[offset + 1]] + positions[indices[offset + 2]]) / 3f;
    }

    private static int LongestAxis(Vector3 size) => size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;

    private static bool IntersectsBounds(Ray ray, Bounds3D bounds, out float near)
    {
        near = 0f;
        var far = float.PositiveInfinity;
        if (!IntersectAxis(ray.Origin.X, ray.Direction.X, bounds.Min.X, bounds.Max.X, ref near, ref far) ||
            !IntersectAxis(ray.Origin.Y, ray.Direction.Y, bounds.Min.Y, bounds.Max.Y, ref near, ref far) ||
            !IntersectAxis(ray.Origin.Z, ray.Direction.Z, bounds.Min.Z, bounds.Max.Z, ref near, ref far)) return false;
        return far >= global::System.MathF.Max(near, 0f);
    }

    private static bool IntersectAxis(float origin, float direction, float min, float max, ref float near, ref float far)
    {
        if (global::System.MathF.Abs(direction) < 1e-8f) return origin >= min && origin <= max;
        var inverse = 1f / direction;
        var first = (min - origin) * inverse;
        var second = (max - origin) * inverse;
        if (first > second) (first, second) = (second, first);
        near = global::System.MathF.Max(near, first);
        far = global::System.MathF.Min(far, second);
        return near <= far;
    }

    private static bool IntersectTriangle(Ray ray, Vector3 v0, Vector3 v1, Vector3 v2, out float distance, out Vector3 point)
    {
        const float epsilon = 1e-6f;
        var edge1 = v1 - v0;
        var edge2 = v2 - v0;
        var p = Vector3.Cross(ray.Direction, edge2);
        var determinant = Vector3.Dot(edge1, p);
        if (global::System.MathF.Abs(determinant) < epsilon)
        {
            distance = 0f;
            point = default;
            return false;
        }
        var inverse = 1f / determinant;
        var t = ray.Origin - v0;
        var u = Vector3.Dot(t, p) * inverse;
        if (u < 0f || u > 1f)
        {
            distance = 0f;
            point = default;
            return false;
        }
        var q = Vector3.Cross(t, edge1);
        var v = Vector3.Dot(ray.Direction, q) * inverse;
        if (v < 0f || u + v > 1f)
        {
            distance = 0f;
            point = default;
            return false;
        }
        distance = Vector3.Dot(edge2, q) * inverse;
        if (distance < epsilon)
        {
            point = default;
            return false;
        }
        point = ray.Origin + ray.Direction * distance;
        return true;
    }

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
        public bool IsLeaf => Left < 0;
    }

    private sealed class TriangleCentroidComparer : IComparer<int>
    {
        private readonly GeometryBuffer3D<Vector3> _positions;
        private readonly GeometryIndexBuffer3D _indices;
        private readonly int _axis;

        public TriangleCentroidComparer(GeometryBuffer3D<Vector3> positions, GeometryIndexBuffer3D indices, int axis)
        {
            _positions = positions;
            _indices = indices;
            _axis = axis;
        }

        public int Compare(int left, int right)
        {
            var a = Centroid(_positions, _indices, left);
            var b = Centroid(_positions, _indices, right);
            var comparison = (_axis == 0 ? a.X : _axis == 1 ? a.Y : a.Z).CompareTo(_axis == 0 ? b.X : _axis == 1 ? b.Y : b.Z);
            return comparison != 0 ? comparison : left.CompareTo(right);
        }
    }
}
