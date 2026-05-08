using System;
using System.Numerics;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.Geometry;

public sealed class Mesh3D
{
    public static Mesh3D Empty { get; } = new Mesh3D(Array.Empty<Vector3>(), Array.Empty<Vector3>(), Array.Empty<int>(), "empty");

    public Mesh3D(
        Vector3[] positions,
        Vector3[] normals,
        int[] indices,
        string? resourceKey = null,
        float[]? materialSlots = null,
        ColorRgba[]? materialSlotBaseColors = null,
        Vector2[]? texCoords0 = null,
        ColorRgba[]? vertexColors0 = null,
        Vector4[]? tangents = null,
        Vector4[]? boneIndices0 = null,
        Vector4[]? boneWeights0 = null)
    {
        Positions = CloneVector3(positions);
        Normals = NormalizeVector3(normals, Positions.Length);
        Indices = CloneIndices(indices);
        TexCoords0 = NormalizeVector2(texCoords0, Positions.Length);
        VertexColors0 = NormalizeColors(vertexColors0, Positions.Length);
        Tangents = NormalizeVector4(tangents, Positions.Length);
        BoneIndices0 = NormalizeVector4(boneIndices0, Positions.Length);
        BoneWeights0 = NormalizeVector4(boneWeights0, Positions.Length);
        MaterialSlots = NormalizeFloats(materialSlots, Positions.Length);
        MaterialSlotBaseColors = materialSlotBaseColors is null ? Array.Empty<ColorRgba>() : (ColorRgba[])materialSlotBaseColors.Clone();
        ResourceKey = string.IsNullOrWhiteSpace(resourceKey) ? "custom:" + Guid.NewGuid().ToString("N") : resourceKey;

        ValidateGeometry(Positions, Normals, Indices, TexCoords0, VertexColors0, Tangents, BoneIndices0, BoneWeights0, MaterialSlots, ResourceKey);
        GeometryVersion = ComputeGeometryVersion(Positions, Normals, Indices, TexCoords0, VertexColors0, Tangents, BoneIndices0, BoneWeights0, MaterialSlots);
        RenderGeometry = new RenderGeometry3D(Positions, Normals, Indices, ResourceKey, TexCoords0, VertexColors0, Tangents, MaterialSlots, BoneIndices0, BoneWeights0);
        LocalBounds = RenderGeometry.LocalBounds;
        BoundingRadius = RenderGeometry.BoundingRadius;
    }

    public Vector3[] Positions { get; }
    public Vector3[] Normals { get; }
    public Vector2[] TexCoords0 { get; }
    public ColorRgba[] VertexColors0 { get; }
    public Vector4[] Tangents { get; }
    public Vector4[] BoneIndices0 { get; }
    public Vector4[] BoneWeights0 { get; }
    public int[] Indices { get; }
    public float[] MaterialSlots { get; }
    public ColorRgba[] MaterialSlotBaseColors { get; }
    public bool HasTexCoords0 => TexCoords0.Length == Positions.Length && Positions.Length > 0;
    public bool HasVertexColors0 => VertexColors0.Length == Positions.Length && Positions.Length > 0;
    public bool HasTangents => Tangents.Length == Positions.Length && Positions.Length > 0;
    public bool HasSkinWeights => BoneIndices0.Length == Positions.Length && BoneWeights0.Length == Positions.Length && Positions.Length > 0;
    public bool HasMaterialSlots => MaterialSlots.Length == Positions.Length && Positions.Length > 0;
    public int MaterialSlotCount => MaterialSlotBaseColors.Length > 0 ? MaterialSlotBaseColors.Length : ComputeMaterialSlotCount(MaterialSlots);
    public string ResourceKey { get; }
    public int GeometryVersion { get; }
    public RenderGeometry3D RenderGeometry { get; }
    public Bounds3D LocalBounds { get; }
    public float BoundingRadius { get; }

    private static Vector3[] CloneVector3(Vector3[]? values)
        => values is null || values.Length == 0 ? Array.Empty<Vector3>() : (Vector3[])values.Clone();

    private static int[] CloneIndices(int[]? values)
        => values is null || values.Length == 0 ? Array.Empty<int>() : (int[])values.Clone();

    private static Vector3[] NormalizeVector3(Vector3[]? values, int vertexCount)
        => values is not null && values.Length == vertexCount ? (Vector3[])values.Clone() : Array.Empty<Vector3>();

    private static Vector2[] NormalizeVector2(Vector2[]? values, int vertexCount)
        => values is not null && values.Length == vertexCount ? (Vector2[])values.Clone() : Array.Empty<Vector2>();

    private static Vector4[] NormalizeVector4(Vector4[]? values, int vertexCount)
        => values is not null && values.Length == vertexCount ? (Vector4[])values.Clone() : Array.Empty<Vector4>();

    private static ColorRgba[] NormalizeColors(ColorRgba[]? values, int vertexCount)
        => values is not null && values.Length == vertexCount ? (ColorRgba[])values.Clone() : Array.Empty<ColorRgba>();

    private static float[] NormalizeFloats(float[]? values, int vertexCount)
        => values is not null && values.Length == vertexCount ? (float[])values.Clone() : Array.Empty<float>();

    private static void ValidateGeometry(
        Vector3[] positions,
        Vector3[] normals,
        int[] indices,
        Vector2[] texCoords0,
        ColorRgba[] vertexColors0,
        Vector4[] tangents,
        Vector4[] boneIndices0,
        Vector4[] boneWeights0,
        float[] materialSlots,
        string resourceKey)
    {
        if (positions.Length == 0 && indices.Length == 0)
        {
            return;
        }

        if (positions.Length == 0)
        {
            throw new ArgumentException($"Mesh '{resourceKey}' has indices but no positions.");
        }

        if (indices.Length == 0)
        {
            throw new ArgumentException($"Mesh '{resourceKey}' has positions but no indices.");
        }

        if (indices.Length % 3 != 0)
        {
            throw new ArgumentException($"Mesh '{resourceKey}' index count must be divisible by 3.");
        }

        for (var i = 0; i < positions.Length; i++)
        {
            if (!IsFinite(positions[i])) throw new ArgumentException($"Mesh '{resourceKey}' contains a non-finite position at vertex {i}.");
        }

        for (var i = 0; i < normals.Length; i++)
        {
            if (!IsFinite(normals[i])) throw new ArgumentException($"Mesh '{resourceKey}' contains a non-finite normal at vertex {i}.");
        }

        for (var i = 0; i < texCoords0.Length; i++)
        {
            if (!IsFinite(texCoords0[i])) throw new ArgumentException($"Mesh '{resourceKey}' contains a non-finite UV at vertex {i}.");
        }

        for (var i = 0; i < tangents.Length; i++)
        {
            if (!IsFinite(tangents[i])) throw new ArgumentException($"Mesh '{resourceKey}' contains a non-finite tangent at vertex {i}.");
        }

        for (var i = 0; i < boneIndices0.Length; i++)
        {
            if (!IsFinite(boneIndices0[i])) throw new ArgumentException($"Mesh '{resourceKey}' contains a non-finite bone index vector at vertex {i}.");
        }

        for (var i = 0; i < boneWeights0.Length; i++)
        {
            if (!IsFinite(boneWeights0[i])) throw new ArgumentException($"Mesh '{resourceKey}' contains a non-finite bone weight vector at vertex {i}.");
        }

        for (var i = 0; i < materialSlots.Length; i++)
        {
            if (!float.IsFinite(materialSlots[i])) throw new ArgumentException($"Mesh '{resourceKey}' contains a non-finite material slot at vertex {i}.");
        }

        for (var i = 0; i < indices.Length; i++)
        {
            if ((uint)indices[i] >= (uint)positions.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(indices), $"Mesh '{resourceKey}' index {i} points outside the vertex buffer.");
            }
        }
    }

    private static int ComputeGeometryVersion(
        Vector3[] positions,
        Vector3[] normals,
        int[] indices,
        Vector2[] texCoords0,
        ColorRgba[] vertexColors0,
        Vector4[] tangents,
        Vector4[] boneIndices0,
        Vector4[] boneWeights0,
        float[] materialSlots)
    {
        unchecked
        {
            var hash = 17;
            hash = HashVector3(hash, positions);
            hash = HashVector3(hash, normals);
            hash = HashVector2(hash, texCoords0);
            hash = HashColors(hash, vertexColors0);
            hash = HashVector4(hash, tangents);
            hash = HashVector4(hash, boneIndices0);
            hash = HashVector4(hash, boneWeights0);
            hash = HashFloats(hash, materialSlots);
            for (var i = 0; i < indices.Length; i++) hash = hash * 31 + indices[i];
            return hash == 0 ? 1 : hash;
        }
    }

    private static int HashVector3(int hash, Vector3[] values)
    {
        unchecked
        {
            hash = hash * 31 + values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                hash = hash * 31 + values[i].X.GetHashCode();
                hash = hash * 31 + values[i].Y.GetHashCode();
                hash = hash * 31 + values[i].Z.GetHashCode();
            }
            return hash;
        }
    }

    private static int HashVector2(int hash, Vector2[] values)
    {
        unchecked
        {
            hash = hash * 31 + values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                hash = hash * 31 + values[i].X.GetHashCode();
                hash = hash * 31 + values[i].Y.GetHashCode();
            }
            return hash;
        }
    }

    private static int HashVector4(int hash, Vector4[] values)
    {
        unchecked
        {
            hash = hash * 31 + values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                hash = hash * 31 + values[i].X.GetHashCode();
                hash = hash * 31 + values[i].Y.GetHashCode();
                hash = hash * 31 + values[i].Z.GetHashCode();
                hash = hash * 31 + values[i].W.GetHashCode();
            }
            return hash;
        }
    }

    private static int HashColors(int hash, ColorRgba[] values)
    {
        unchecked
        {
            hash = hash * 31 + values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                hash = hash * 31 + values[i].R.GetHashCode();
                hash = hash * 31 + values[i].G.GetHashCode();
                hash = hash * 31 + values[i].B.GetHashCode();
                hash = hash * 31 + values[i].A.GetHashCode();
            }
            return hash;
        }
    }

    private static int HashFloats(int hash, float[] values)
    {
        unchecked
        {
            hash = hash * 31 + values.Length;
            for (var i = 0; i < values.Length; i++) hash = hash * 31 + values[i].GetHashCode();
            return hash;
        }
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static bool IsFinite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);
    private static bool IsFinite(Vector4 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) && float.IsFinite(v.W);

    private static int ComputeMaterialSlotCount(float[] slots)
    {
        var max = -1;
        for (var i = 0; i < slots.Length; i++)
        {
            var slot = (int)MathF.Round(slots[i]);
            if (slot > max) max = slot;
        }

        return max + 1;
    }
}
