using System;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.Geometry;

internal sealed class GeometryMutableData3D
{
    public GeometryMutableData3D(
        Vector3[] positions,
        Vector3[] normals,
        int[] indices,
        Vector2[] texCoords0,
        ColorRgba[] colors0,
        Vector4[] tangents,
        float[] materialSlots,
        Vector4[] boneIndices0,
        Vector4[] boneWeights0)
    {
        Positions = positions;
        Normals = normals;
        Indices = indices;
        TexCoords0 = texCoords0;
        Colors0 = colors0;
        Tangents = tangents;
        MaterialSlots = materialSlots;
        BoneIndices0 = boneIndices0;
        BoneWeights0 = boneWeights0;
        SourceTriangleIndices = Array.Empty<int>();
    }

    public Vector3[] Positions { get; set; }
    public Vector3[] Normals { get; set; }
    public int[] Indices { get; set; }
    public Vector2[] TexCoords0 { get; set; }
    public ColorRgba[] Colors0 { get; set; }
    public Vector4[] Tangents { get; set; }
    public float[] MaterialSlots { get; set; }
    public Vector4[] BoneIndices0 { get; set; }
    public Vector4[] BoneWeights0 { get; set; }
    /// <summary>Empty means the current triangle order still matches source order.</summary>
    public int[] SourceTriangleIndices { get; set; }
}

/// <summary>Deterministic offline triangle and vertex-fetch optimization.</summary>
internal static class MeshOptimizer3D
{
    public static void Optimize(GeometryMutableData3D data, GeometryBuildOptions3D options)
    {
        if (data.Indices.Length < 6 || data.Positions.Length == 0) return;
        if (options.OptimizeVertexCache)
        {
            ApplyTriangleOrder(data, OptimizeTriangleOrder(data.Indices, data.Positions.Length, options.PostTransformCacheSize));
        }
        if (options.OptimizeOverdraw)
        {
            ApplyTriangleOrder(data, OptimizeOverdrawOrder(data.Indices, data.Positions, options.OverdrawClusterTriangleCount));
        }
        if (options.OptimizeVertexFetch)
        {
            OptimizeVertexFetch(data);
        }
    }

    private static int[] OptimizeTriangleOrder(int[] source, int vertexCount, int cacheSize)
    {
        var triangleCount = source.Length / 3;
        var adjacency = new List<int>[vertexCount];
        var liveTriangles = new int[vertexCount];
        for (var triangle = 0; triangle < triangleCount; triangle++)
        {
            var offset = triangle * 3;
            Add(source[offset], triangle);
            Add(source[offset + 1], triangle);
            Add(source[offset + 2], triangle);
        }

        var order = new int[triangleCount];
        var emitted = new bool[triangleCount];
        var candidateMarked = new bool[triangleCount];
        var candidates = new List<int>(global::System.Math.Min(triangleCount, 4096));
        var cache = new List<int>(cacheSize);
        var nextUnemitted = 0;

        for (var emittedCount = 0; emittedCount < triangleCount; emittedCount++)
        {
            var selected = SelectCandidate();
            if (selected < 0)
            {
                while (nextUnemitted < triangleCount && emitted[nextUnemitted]) nextUnemitted++;
                selected = nextUnemitted;
            }

            emitted[selected] = true;
            order[emittedCount] = selected;
            var sourceOffset = selected * 3;
            var i0 = source[sourceOffset];
            var i1 = source[sourceOffset + 1];
            var i2 = source[sourceOffset + 2];
            liveTriangles[i0]--;
            liveTriangles[i1]--;
            liveTriangles[i2]--;
            Touch(i2);
            Touch(i1);
            Touch(i0);
            AddCandidates(i0);
            AddCandidates(i1);
            AddCandidates(i2);

            if (candidates.Count > 16384 && emittedCount % 1024 == 0)
            {
                candidates.Clear();
                Array.Clear(candidateMarked, 0, candidateMarked.Length);
                for (var i = 0; i < cache.Count; i++) AddCandidates(cache[i]);
            }
        }

        return order;

        void Add(int vertex, int triangle)
        {
            (adjacency[vertex] ??= new List<int>(6)).Add(triangle);
            liveTriangles[vertex]++;
        }

        void AddCandidates(int vertex)
        {
            var list = adjacency[vertex];
            if (list is null) return;
            for (var i = 0; i < list.Count; i++)
            {
                var triangle = list[i];
                if (emitted[triangle] || candidateMarked[triangle]) continue;
                candidateMarked[triangle] = true;
                candidates.Add(triangle);
            }
        }

        int SelectCandidate()
        {
            var best = -1;
            var bestScore = int.MinValue;
            for (var i = 0; i < candidates.Count; i++)
            {
                var triangle = candidates[i];
                if (emitted[triangle]) continue;
                var offset = triangle * 3;
                var a = source[offset];
                var b = source[offset + 1];
                var c = source[offset + 2];
                var reuse = Contains(a) + Contains(b) + Contains(c);
                var valence = liveTriangles[a] + liveTriangles[b] + liveTriangles[c];
                var score = reuse * 1_000_000 - valence * 100 - triangle;
                if (score <= bestScore) continue;
                bestScore = score;
                best = triangle;
            }
            return best;
        }

        int Contains(int vertex) => cache.Contains(vertex) ? 1 : 0;

        void Touch(int vertex)
        {
            var existing = cache.IndexOf(vertex);
            if (existing >= 0) cache.RemoveAt(existing);
            cache.Insert(0, vertex);
            if (cache.Count > cacheSize) cache.RemoveAt(cache.Count - 1);
        }
    }

    private static int[] OptimizeOverdrawOrder(int[] source, Vector3[] positions, int clusterTriangleCount)
    {
        var triangleCount = source.Length / 3;
        if (triangleCount <= clusterTriangleCount) return CreateIdentityOrder(triangleCount);
        var min = positions[0];
        var max = positions[0];
        for (var i = 1; i < positions.Length; i++)
        {
            min = Vector3.Min(min, positions[i]);
            max = Vector3.Max(max, positions[i]);
        }
        var size = max - min;
        var axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
        var clusterCount = (triangleCount + clusterTriangleCount - 1) / clusterTriangleCount;
        var clusters = new TriangleCluster[clusterCount];
        for (var cluster = 0; cluster < clusterCount; cluster++)
        {
            var startTriangle = cluster * clusterTriangleCount;
            var count = global::System.Math.Min(clusterTriangleCount, triangleCount - startTriangle);
            var centroid = Vector3.Zero;
            for (var triangle = 0; triangle < count; triangle++)
            {
                var offset = (startTriangle + triangle) * 3;
                centroid += (positions[source[offset]] + positions[source[offset + 1]] + positions[source[offset + 2]]) / 3f;
            }
            centroid /= count;
            var depth = axis == 0 ? centroid.X : axis == 1 ? centroid.Y : centroid.Z;
            clusters[cluster] = new TriangleCluster(startTriangle, count, depth);
        }
        Array.Sort(clusters, static (left, right) =>
        {
            var comparison = left.Depth.CompareTo(right.Depth);
            return comparison != 0 ? comparison : left.StartTriangle.CompareTo(right.StartTriangle);
        });
        var order = new int[triangleCount];
        var destination = 0;
        for (var cluster = 0; cluster < clusters.Length; cluster++)
        {
            var item = clusters[cluster];
            for (var triangle = 0; triangle < item.TriangleCount; triangle++)
            {
                order[destination++] = item.StartTriangle + triangle;
            }
        }
        return order;
    }

    private static void ApplyTriangleOrder(GeometryMutableData3D data, int[] order)
    {
        var triangleCount = data.Indices.Length / 3;
        if (order.Length != triangleCount) throw new InvalidOperationException("Triangle optimizer returned an invalid permutation length.");
        var identity = true;
        for (var i = 0; i < order.Length; i++)
        {
            if ((uint)order[i] >= (uint)triangleCount) throw new InvalidOperationException("Triangle optimizer returned an out-of-range triangle.");
            if (order[i] != i) identity = false;
        }
        if (identity) return;

        var seen = new bool[triangleCount];
        var reorderedIndices = new int[data.Indices.Length];
        var reorderedSources = new int[triangleCount];
        for (var destinationTriangle = 0; destinationTriangle < order.Length; destinationTriangle++)
        {
            var sourceTriangle = order[destinationTriangle];
            if (seen[sourceTriangle]) throw new InvalidOperationException("Triangle optimizer returned a duplicate triangle.");
            seen[sourceTriangle] = true;
            var sourceOffset = sourceTriangle * 3;
            var destinationOffset = destinationTriangle * 3;
            reorderedIndices[destinationOffset] = data.Indices[sourceOffset];
            reorderedIndices[destinationOffset + 1] = data.Indices[sourceOffset + 1];
            reorderedIndices[destinationOffset + 2] = data.Indices[sourceOffset + 2];
            reorderedSources[destinationTriangle] = data.SourceTriangleIndices.Length == 0
                ? sourceTriangle
                : data.SourceTriangleIndices[sourceTriangle];
        }
        data.Indices = reorderedIndices;
        data.SourceTriangleIndices = reorderedSources;
    }

    private static int[] CreateIdentityOrder(int triangleCount)
    {
        var order = new int[triangleCount];
        for (var i = 0; i < order.Length; i++) order[i] = i;
        return order;
    }

    private static void OptimizeVertexFetch(GeometryMutableData3D data)
    {
        var oldCount = data.Positions.Length;
        var oldToNew = new int[oldCount];
        Array.Fill(oldToNew, -1);
        var representatives = new int[oldCount];
        var next = 0;
        for (var i = 0; i < data.Indices.Length; i++)
        {
            var old = data.Indices[i];
            var mapped = oldToNew[old];
            if (mapped < 0)
            {
                mapped = next;
                oldToNew[old] = next;
                representatives[next] = old;
                next++;
            }
            data.Indices[i] = mapped;
        }
        var identity = next == oldCount;
        if (identity)
        {
            for (var i = 0; i < next; i++)
            {
                if (representatives[i] == i) continue;
                identity = false;
                break;
            }
        }
        if (identity) return;

        data.Positions = Remap(data.Positions, representatives, next);
        data.Normals = RemapOptional(data.Normals, representatives, next, oldCount);
        data.TexCoords0 = RemapOptional(data.TexCoords0, representatives, next, oldCount);
        data.Colors0 = RemapOptional(data.Colors0, representatives, next, oldCount);
        data.Tangents = RemapOptional(data.Tangents, representatives, next, oldCount);
        data.MaterialSlots = RemapOptional(data.MaterialSlots, representatives, next, oldCount);
        data.BoneIndices0 = RemapOptional(data.BoneIndices0, representatives, next, oldCount);
        data.BoneWeights0 = RemapOptional(data.BoneWeights0, representatives, next, oldCount);
    }

    private static T[] Remap<T>(T[] source, int[] representatives, int count)
    {
        var result = new T[count];
        for (var i = 0; i < count; i++) result[i] = source[representatives[i]];
        return result;
    }

    private static T[] RemapOptional<T>(T[] source, int[] representatives, int count, int oldCount)
        => source.Length == oldCount ? Remap(source, representatives, count) : source;

    private readonly record struct TriangleCluster(int StartTriangle, int TriangleCount, float Depth);
}
