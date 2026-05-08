using System;
using System.Numerics;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.Assets.Models;

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
        Positions = positions is null || positions.Length == 0 ? Array.Empty<Vector3>() : (Vector3[])positions.Clone();
        Indices = indices is not null && indices.Length > 0 ? (int[])indices.Clone() : CreateSequentialIndices(Positions.Length);
        ValidatePrimitiveGeometry(Id, Positions, Indices);
        Normals = normals is not null && normals.Length == Positions.Length ? (Vector3[])normals.Clone() : GenerateNormals(Positions, Indices);
        TexCoords0 = texCoords0 is not null && texCoords0.Length == Positions.Length ? (Vector2[])texCoords0.Clone() : Array.Empty<Vector2>();
        MaterialIndex = materialIndex < 0 ? 0 : materialIndex;
        SkinWeights0 = skinWeights0 is not null && skinWeights0.Length == Positions.Length ? NormalizeSkinWeights(skinWeights0) : Array.Empty<VertexSkinWeights3D>();
        Bounds = ComputeBounds(Positions);
        var boneIndices0 = ToBoneIndices(SkinWeights0);
        var boneWeights0 = ToBoneWeights(SkinWeights0);
        RenderGeometry = new RenderGeometry3D(Positions, Normals, Indices, Id, TexCoords0, colors0: null, tangents: null, materialSlots: null, boneIndices0: boneIndices0, boneWeights0: boneWeights0);
    }

    public string Id { get; }
    public string Name { get; }
    public Vector3[] Positions { get; }
    public Vector3[] Normals { get; }
    public Vector2[] TexCoords0 { get; }
    public int[] Indices { get; }
    public int MaterialIndex { get; }
    public VertexSkinWeights3D[] SkinWeights0 { get; }
    public bool HasSkinWeights => SkinWeights0.Length == Positions.Length && Positions.Length > 0;
    public Bounds3D Bounds { get; }
    public RenderGeometry3D RenderGeometry { get; }
    public int TriangleCount => Indices.Length / 3;

    public Mesh3D ToMesh(string? resourceKey = null)
        => new Mesh3D(Positions, Normals, Indices, resourceKey ?? Id, texCoords0: TexCoords0, boneIndices0: ToBoneIndices(SkinWeights0), boneWeights0: ToBoneWeights(SkinWeights0));


    private static void ValidatePrimitiveGeometry(string id, Vector3[] positions, int[] indices)
    {
        if (positions.Length == 0 && indices.Length == 0) return;
        if (positions.Length == 0) throw new ArgumentException($"Primitive '{id}' has indices but no positions.");
        if (indices.Length == 0) throw new ArgumentException($"Primitive '{id}' has positions but no indices.");
        if (indices.Length % 3 != 0) throw new ArgumentException($"Primitive '{id}' index count must be divisible by 3.");
        for (var i = 0; i < positions.Length; i++)
        {
            var p = positions[i];
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z))
            {
                throw new ArgumentException($"Primitive '{id}' contains a non-finite position at vertex {i}.");
            }
        }
        for (var i = 0; i < indices.Length; i++)
        {
            if ((uint)indices[i] >= (uint)positions.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(indices), $"Primitive '{id}' index {i} points outside the vertex buffer.");
            }
        }
    }

    private static VertexSkinWeights3D[] NormalizeSkinWeights(VertexSkinWeights3D[] values)
    {
        var result = new VertexSkinWeights3D[values.Length];
        for (var i = 0; i < result.Length; i++) result[i] = values[i].Normalize();
        return result;
    }

    private static Vector4[] ToBoneIndices(VertexSkinWeights3D[] values)
    {
        if (values.Length == 0) return Array.Empty<Vector4>();
        var result = new Vector4[values.Length];
        for (var i = 0; i < result.Length; i++) result[i] = values[i].BoneIndices;
        return result;
    }

    private static Vector4[] ToBoneWeights(VertexSkinWeights3D[] values)
    {
        if (values.Length == 0) return Array.Empty<Vector4>();
        var result = new Vector4[values.Length];
        for (var i = 0; i < result.Length; i++) result[i] = values[i].Weights;
        return result;
    }

    private static int[] CreateSequentialIndices(int vertexCount)
    {
        var indices = new int[vertexCount];
        for (var i = 0; i < indices.Length; i++) indices[i] = i;
        return indices;
    }

    private static Bounds3D ComputeBounds(Vector3[] positions)
    {
        if (positions.Length == 0) return Bounds3D.Empty;
        var min = positions[0];
        var max = positions[0];
        for (var i = 1; i < positions.Length; i++)
        {
            min = Vector3.Min(min, positions[i]);
            max = Vector3.Max(max, positions[i]);
        }

        return new Bounds3D(min, max);
    }

    public static Vector3[] GenerateNormals(Vector3[] positions, int[] indices)
    {
        var normals = new Vector3[positions.Length];
        for (var i = 0; i + 2 < indices.Length; i += 3)
        {
            var i0 = indices[i];
            var i1 = indices[i + 1];
            var i2 = indices[i + 2];
            if ((uint)i0 >= positions.Length || (uint)i1 >= positions.Length || (uint)i2 >= positions.Length) continue;
            var edge1 = positions[i1] - positions[i0];
            var edge2 = positions[i2] - positions[i0];
            var normal = Vector3.Cross(edge1, edge2);
            if (normal.LengthSquared() < 0.0000001f) continue;
            normals[i0] += normal;
            normals[i1] += normal;
            normals[i2] += normal;
        }

        for (var i = 0; i < normals.Length; i++)
        {
            normals[i] = normals[i].LengthSquared() < 0.0000001f ? Vector3.UnitZ : Vector3.Normalize(normals[i]);
        }

        return normals;
    }
}
