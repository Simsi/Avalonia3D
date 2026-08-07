using System;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Geometry;

public sealed class MeshLodChain3D
{
    internal MeshLodChain3D(Mesh3D source, Mesh3D[] levels, float[] ratios)
    {
        Source = source;
        Levels = Array.AsReadOnly(levels);
        Ratios = Array.AsReadOnly(ratios);
    }

    public Mesh3D Source { get; }
    public IReadOnlyList<Mesh3D> Levels { get; }
    public IReadOnlyList<float> Ratios { get; }
}

/// <summary>
/// Deterministic offline vertex-clustering LOD generation. The result preserves source streams
/// and material slots; it never substitutes bounds boxes or other proxy geometry.
/// </summary>
public static class MeshLodGenerator3D
{
    public static Mesh3D Generate(Mesh3D source, float targetRatio, string? resourceKey = null, GeometryBuildOptions3D? buildOptions = null)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        targetRatio = Guard3D.Range(targetRatio, 0.01f, 0.99f, nameof(targetRatio));
        if (source.RenderGeometry.TriangleCount < 2)
            throw new InvalidOperationException(
                $"LOD generation for '{source.ResourceKey}' requires at least two source triangles; " +
                "returning the original mesh would violate the explicit lower-detail contract.");

        var targetTriangles = global::System.Math.Max(1, (int)global::System.MathF.Round(source.RenderGeometry.TriangleCount * targetRatio));
        var best = BuildClusterMap(source, targetTriangles);
        if (best.Indices.Count < 3)
            throw new InvalidOperationException($"LOD generation for '{source.ResourceKey}' collapsed all triangles at ratio {targetRatio:0.###}.");

        var streams = GeometryStreamMask3D.Normals;
        if (source.HasTexCoords0) streams |= GeometryStreamMask3D.TexCoords0;
        if (source.HasVertexColors0) streams |= GeometryStreamMask3D.Colors0;
        if (source.HasTangents) streams |= GeometryStreamMask3D.Tangents;
        if (source.HasMaterialSlots) streams |= GeometryStreamMask3D.MaterialSlots;
        if (source.HasSkinWeights) streams |= GeometryStreamMask3D.SkinWeights;
        var builder = new MeshGeometryBuilder3D(best.Representatives.Count, best.Indices.Count, streams);
        var sourceNormals = source.Normals;
        var sourceTangents = source.HasTangents ? source.Tangents : GeometryBuffer3D<Vector4>.Empty;
        for (var i = 0; i < best.Representatives.Count; i++)
        {
            var old = best.Representatives[i];
            builder.Positions[i] = source.Positions[old];
            builder.Normals[i] = sourceNormals[old];
            if (source.HasTexCoords0) builder.TexCoords0[i] = source.TexCoords0[old];
            if (source.HasVertexColors0) builder.Colors0[i] = source.VertexColors0[old];
            if (source.HasTangents) builder.Tangents[i] = sourceTangents[old];
            if (source.HasMaterialSlots) builder.MaterialSlots[i] = source.MaterialSlots[old];
            if (source.HasSkinWeights)
            {
                builder.BoneIndices0[i] = source.BoneIndices0[old];
                builder.BoneWeights0[i] = source.BoneWeights0[old];
            }
        }
        for (var i = 0; i < best.Indices.Count; i++) builder.Indices[i] = best.Indices[i];
        var key = string.IsNullOrWhiteSpace(resourceKey)
            ? $"{source.ResourceKey}:lod:{targetRatio:0.####}:{best.Representatives.Count}:{best.Indices.Count / 3}"
            : resourceKey;
        return builder.Build(key, buildOptions, source.MaterialSlotBaseColors.Span);
    }

    public static MeshLodChain3D GenerateChain(Mesh3D source, ReadOnlySpan<float> targetRatios, GeometryBuildOptions3D? buildOptions = null)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (targetRatios.IsEmpty) throw new ArgumentException("At least one LOD ratio is required.", nameof(targetRatios));
        var ratios = targetRatios.ToArray();
        for (var i = 0; i < ratios.Length; i++)
        {
            Guard3D.Range(ratios[i], 0.01f, 0.99f, nameof(targetRatios));
            if (i > 0 && ratios[i] >= ratios[i - 1])
                throw new ArgumentException("LOD ratios must be strictly descending.", nameof(targetRatios));
        }
        var levels = new Mesh3D[ratios.Length];
        for (var i = 0; i < levels.Length; i++) levels[i] = Generate(source, ratios[i], buildOptions: buildOptions);
        return new MeshLodChain3D(source, levels, ratios);
    }

    private static ClusterResult BuildClusterMap(Mesh3D source, int targetTriangles)
    {
        var positions = source.Positions;
        var bounds = source.LocalBounds;
        var targetVertices = global::System.Math.Max(4, (int)global::System.MathF.Ceiling(positions.Length * targetTriangles / (float)global::System.Math.Max(1, source.RenderGeometry.TriangleCount)));
        var initialResolution = global::System.Math.Max(1, (int)global::System.MathF.Ceiling(global::System.MathF.Pow(targetVertices, 1f / 3f)));
        ClusterResult? best = null;
        var bestDelta = int.MaxValue;
        for (var resolution = initialResolution; resolution >= 1; resolution--)
        {
            var candidate = Cluster(source, bounds, resolution);
            if (candidate.Indices.Count < 3) continue;
            var triangleCount = candidate.Indices.Count / 3;
            var delta = global::System.Math.Abs(triangleCount - targetTriangles);
            if (delta < bestDelta)
            {
                best = candidate;
                bestDelta = delta;
            }
            if (triangleCount <= targetTriangles) break;
        }
        return best ?? throw new InvalidOperationException($"Unable to generate a valid LOD for '{source.ResourceKey}'.");
    }

    private static ClusterResult Cluster(Mesh3D source, ThreeDEngine.Core.Collision.Bounds3D bounds, int resolution)
    {
        var positions = source.Positions;
        var map = new Dictionary<ClusterKey, int>(positions.Length);
        var oldToCluster = new int[positions.Length];
        var representatives = new List<int>(positions.Length);
        var size = bounds.Size;
        for (var vertex = 0; vertex < positions.Length; vertex++)
        {
            var position = positions[vertex];
            var x = Quantize(position.X, bounds.Min.X, size.X, resolution);
            var y = Quantize(position.Y, bounds.Min.Y, size.Y, resolution);
            var z = Quantize(position.Z, bounds.Min.Z, size.Z, resolution);
            var materialSlot = source.HasMaterialSlots ? checked((int)MathF.Round(source.MaterialSlots[vertex])) : 0;
            var bone = source.HasSkinWeights ? source.BoneIndices0[vertex] : Vector4.Zero;
            var key = new ClusterKey(x, y, z, materialSlot,
                checked((int)MathF.Round(bone.X)), checked((int)MathF.Round(bone.Y)),
                checked((int)MathF.Round(bone.Z)), checked((int)MathF.Round(bone.W)));
            if (!map.TryGetValue(key, out var cluster))
            {
                cluster = representatives.Count;
                map.Add(key, cluster);
                representatives.Add(vertex);
            }
            oldToCluster[vertex] = cluster;
        }

        var indices = new List<int>(source.Indices.Length);
        var uniqueTriangles = new HashSet<TriangleKey>();
        for (var triangle = 0; triangle < source.Indices.Length; triangle += 3)
        {
            var a = oldToCluster[source.Indices[triangle]];
            var b = oldToCluster[source.Indices[triangle + 1]];
            var c = oldToCluster[source.Indices[triangle + 2]];
            if (a == b || b == c || a == c) continue;
            var key = TriangleKey.Create(a, b, c);
            if (!uniqueTriangles.Add(key)) continue;
            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }

        if (indices.Count == 0) return new ClusterResult(representatives, indices);
        var used = new bool[representatives.Count];
        for (var i = 0; i < indices.Count; i++) used[indices[i]] = true;
        var compactMap = new int[representatives.Count];
        Array.Fill(compactMap, -1);
        var compactRepresentatives = new List<int>(representatives.Count);
        for (var i = 0; i < representatives.Count; i++)
        {
            if (!used[i]) continue;
            compactMap[i] = compactRepresentatives.Count;
            compactRepresentatives.Add(representatives[i]);
        }
        for (var i = 0; i < indices.Count; i++) indices[i] = compactMap[indices[i]];
        return new ClusterResult(compactRepresentatives, indices);
    }

    private static int Quantize(float value, float min, float size, int resolution)
    {
        if (size <= 1e-12f) return 0;
        var normalized = global::System.Math.Clamp((value - min) / size, 0f, 0.99999994f);
        return global::System.Math.Min(resolution - 1, (int)(normalized * resolution));
    }

    private sealed record ClusterResult(List<int> Representatives, List<int> Indices);
    private readonly record struct ClusterKey(int X, int Y, int Z, int MaterialSlot, int Bone0, int Bone1, int Bone2, int Bone3);
    private readonly record struct TriangleKey(int A, int B, int C)
    {
        public static TriangleKey Create(int a, int b, int c)
        {
            if (a > b) (a, b) = (b, a);
            if (b > c) (b, c) = (c, b);
            if (a > b) (a, b) = (b, a);
            return new TriangleKey(a, b, c);
        }
    }
}
