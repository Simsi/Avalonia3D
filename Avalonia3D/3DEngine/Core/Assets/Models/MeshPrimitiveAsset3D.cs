using System;
using System.Numerics;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Geometry;

namespace ThreeDEngine.Core.Assets.Models;

/// <summary>Immutable imported primitive backed by the same canonical geometry as its Mesh3D handles.</summary>
public sealed class MeshPrimitiveAsset3D
{
    public MeshPrimitiveAsset3D(
        string id,
        Vector3[] positions,
        Vector3[]? normals,
        Vector2[]? texCoords0,
        int[]? indices,
        int materialIndex,
        string? name = null,
        VertexSkinWeights3D[]? skinWeights0 = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? "primitive:" + Guid.NewGuid().ToString("N") : id;
        Name = string.IsNullOrWhiteSpace(name) ? Id : name;
        var sourcePositions = positions ?? Array.Empty<Vector3>();
        var sourceIndices = indices is not null && indices.Length > 0 ? indices : CreateSequentialIndices(sourcePositions.Length);
        BuildSkinStreams(skinWeights0, sourcePositions.Length, out var boneIndices0, out var boneWeights0);

        RenderGeometry = new RenderGeometry3D(
            sourcePositions,
            normals ?? Array.Empty<Vector3>(),
            sourceIndices,
            Id,
            texCoords0,
            colors0: null,
            tangents: null,
            materialSlots: null,
            boneIndices0: boneIndices0,
            boneWeights0: boneWeights0);
        MaterialIndex = materialIndex < 0 ? 0 : materialIndex;
    }

    public string Id { get; }
    public string Name { get; }
    public GeometryBuffer3D<Vector3> Positions => RenderGeometry.Positions;
    public GeometryBuffer3D<Vector3> Normals => RenderGeometry.Normals;
    public GeometryBuffer3D<Vector2> TexCoords0 => RenderGeometry.TexCoords0;
    public GeometryBuffer3D<Vector4> BoneIndices0 => RenderGeometry.BoneIndices0;
    public GeometryBuffer3D<Vector4> BoneWeights0 => RenderGeometry.BoneWeights0;
    public GeometryIndexBuffer3D Indices => RenderGeometry.Indices;
    public int MaterialIndex { get; }
    public bool HasSkinWeights => RenderGeometry.HasSkinWeights;
    public Bounds3D Bounds => RenderGeometry.LocalBounds;
    public RenderGeometry3D RenderGeometry { get; }
    public int TriangleCount => RenderGeometry.TriangleCount;

    public Mesh3D ToMesh() => new(RenderGeometry);

    private static void BuildSkinStreams(
        VertexSkinWeights3D[]? values,
        int vertexCount,
        out Vector4[] boneIndices,
        out Vector4[] boneWeights)
    {
        if (values is null || values.Length == 0)
        {
            boneIndices = Array.Empty<Vector4>();
            boneWeights = Array.Empty<Vector4>();
            return;
        }
        if (values.Length != vertexCount)
        {
            throw new ArgumentException("Skin-weight count must match the position count.", nameof(values));
        }

        boneIndices = new Vector4[values.Length];
        boneWeights = new Vector4[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var normalized = values[i].Normalize();
            boneIndices[i] = normalized.BoneIndices;
            boneWeights[i] = normalized.Weights;
        }
    }

    private static int[] CreateSequentialIndices(int vertexCount)
    {
        var indices = new int[vertexCount];
        for (var i = 0; i < indices.Length; i++) indices[i] = i;
        return indices;
    }
}
