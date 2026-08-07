using System;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Geometry;

/// <summary>
/// Immutable preprocessing policy applied once while a mesh resource is created.
/// The policy never introduces a runtime CPU rendering path.
/// </summary>
public sealed class GeometryBuildOptions3D
{
    public static GeometryBuildOptions3D Default { get; } = new();
    public static GeometryBuildOptions3D PreserveInputOrder { get; } = new()
    {
        OptimizeVertexCache = false,
        OptimizeVertexFetch = false,
        OptimizeOverdraw = false
    };

    private int _postTransformCacheSize = 32;
    private int _overdrawClusterTriangleCount = 64;
    private int _meshletMaxVertices = 64;
    private int _meshletMaxTriangles = 126;

    public bool GenerateMissingNormals { get; init; } = true;
    public bool GenerateMissingTangents { get; init; } = true;
    public bool OptimizeVertexCache { get; init; } = true;
    public bool OptimizeVertexFetch { get; init; } = true;
    public bool OptimizeOverdraw { get; init; }
    public bool PackVertexStreams { get; init; } = true;
    public bool PackHalfPrecisionTexCoords { get; init; }
    public bool BuildMeshletsOnFirstUse { get; init; } = true;
    public bool BuildBvhOnFirstUse { get; init; } = true;

    public int PostTransformCacheSize
    {
        get => _postTransformCacheSize;
        init => _postTransformCacheSize = Guard3D.Range(value, 8, 64, nameof(PostTransformCacheSize));
    }

    public int OverdrawClusterTriangleCount
    {
        get => _overdrawClusterTriangleCount;
        init => _overdrawClusterTriangleCount = Guard3D.Range(value, 16, 512, nameof(OverdrawClusterTriangleCount));
    }

    public int MeshletMaxVertices
    {
        get => _meshletMaxVertices;
        init => _meshletMaxVertices = Guard3D.Range(value, 16, byte.MaxValue, nameof(MeshletMaxVertices));
    }

    public int MeshletMaxTriangles
    {
        get => _meshletMaxTriangles;
        init => _meshletMaxTriangles = Guard3D.Range(value, 16, 256, nameof(MeshletMaxTriangles));
    }

    internal GeometryBuildOptions3D Snapshot()
        => new()
        {
            GenerateMissingNormals = GenerateMissingNormals,
            GenerateMissingTangents = GenerateMissingTangents,
            OptimizeVertexCache = OptimizeVertexCache,
            OptimizeVertexFetch = OptimizeVertexFetch,
            OptimizeOverdraw = OptimizeOverdraw,
            PackVertexStreams = PackVertexStreams,
            PackHalfPrecisionTexCoords = PackHalfPrecisionTexCoords,
            BuildMeshletsOnFirstUse = BuildMeshletsOnFirstUse,
            BuildBvhOnFirstUse = BuildBvhOnFirstUse,
            PostTransformCacheSize = PostTransformCacheSize,
            OverdrawClusterTriangleCount = OverdrawClusterTriangleCount,
            MeshletMaxVertices = MeshletMaxVertices,
            MeshletMaxTriangles = MeshletMaxTriangles
        };
}
