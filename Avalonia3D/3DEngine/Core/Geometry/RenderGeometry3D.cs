using System;
using System.Numerics;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Geometry.Surfaces;

namespace ThreeDEngine.Core.Geometry;

/// <summary>
/// Backend-neutral indexed triangle geometry. Importers and primitive generators should normalize
/// their data into this shape before any OpenGL/WebGL resource upload.
/// </summary>
public sealed class RenderGeometry3D
{
    public RenderGeometry3D(
        Vector3[] positions,
        Vector3[] normals,
        int[] indices,
        string resourceKey,
        Vector2[]? texCoords0 = null,
        ColorRgba[]? colors0 = null,
        Vector4[]? tangents = null,
        float[]? materialSlots = null,
        Vector4[]? boneIndices0 = null,
        Vector4[]? boneWeights0 = null)
    {
        Positions = positions is null || positions.Length == 0 ? Array.Empty<Vector3>() : (Vector3[])positions.Clone();
        Indices = indices is null || indices.Length == 0 ? Array.Empty<int>() : (int[])indices.Clone();
        ValidateBaseGeometry(Positions, Indices, resourceKey);
        Normals = ResolveNormals(normals, Positions, Indices);
        TexCoords0 = NormalizeTexCoords(texCoords0, Positions.Length);
        Colors0 = NormalizeColors(colors0, Positions.Length);
        Tangents = ResolveTangents(tangents, Positions, Normals, TexCoords0, Indices);
        MaterialSlots = materialSlots is not null && materialSlots.Length == Positions.Length ? (float[])materialSlots.Clone() : Array.Empty<float>();
        BoneIndices0 = NormalizeVector4(boneIndices0, Positions.Length);
        BoneWeights0 = NormalizeVector4(boneWeights0, Positions.Length);
        ResourceKey = string.IsNullOrWhiteSpace(resourceKey) ? "geometry:" + Guid.NewGuid().ToString("N") : resourceKey;
        ValidateAuxiliaryGeometry(ResourceKey, Normals, TexCoords0, Colors0, Tangents, MaterialSlots, BoneIndices0, BoneWeights0);
        LocalBounds = ComputeLocalBounds(Positions);
        BoundingRadius = ComputeBoundingRadius(Positions);
        WireframeIndices = TangentGenerator3D.BuildWireframeIndices(Indices);
        Layout = ResolveLayout();
        EstimatedVertexUploadBytes = EstimateVector3Bytes(Positions) + EstimateVector3Bytes(Normals) + EstimateVector2Bytes(TexCoords0) + EstimateColorBytes(Colors0) + EstimateVector4Bytes(Tangents) + EstimateVector4Bytes(BoneIndices0) + EstimateVector4Bytes(BoneWeights0) + MaterialSlots.Length * sizeof(float);
        EstimatedIndexUploadBytes = Indices.Length * sizeof(int);
    }

    public Vector3[] Positions { get; }
    public Vector3[] Normals { get; }
    public Vector2[] TexCoords0 { get; }
    public ColorRgba[] Colors0 { get; }
    public Vector4[] Tangents { get; }
    public Vector4[] BoneIndices0 { get; }
    public Vector4[] BoneWeights0 { get; }
    public float[] MaterialSlots { get; }
    public int[] Indices { get; }
    public int[] WireframeIndices { get; }
    public string ResourceKey { get; }
    public VertexLayout3D Layout { get; }
    public Bounds3D LocalBounds { get; }
    public float BoundingRadius { get; }
    public int VertexCount => Positions.Length;
    public int IndexCount => Indices.Length;
    public int TriangleCount => Indices.Length / 3;
    public int WireframeIndexCount => WireframeIndices.Length;
    public bool HasNormals => Normals.Length == Positions.Length && Positions.Length > 0;
    public bool HasTexCoords0 => TexCoords0.Length == Positions.Length && Positions.Length > 0;
    public bool HasColors0 => Colors0.Length == Positions.Length && Positions.Length > 0;
    public bool HasTangents => Tangents.Length == Positions.Length && Positions.Length > 0;
    public bool HasTangentSpace => HasNormals && HasTexCoords0 && HasTangents;
    public bool HasSkinWeights => BoneIndices0.Length == Positions.Length && BoneWeights0.Length == Positions.Length && Positions.Length > 0;
    public bool HasMaterialSlots => MaterialSlots.Length == Positions.Length && Positions.Length > 0;
    public long EstimatedVertexUploadBytes { get; }
    public long EstimatedIndexUploadBytes { get; }
    public long EstimatedWireframeIndexUploadBytes => WireframeIndices.Length * sizeof(int);
    public long EstimatedUploadBytes => EstimatedVertexUploadBytes + EstimatedIndexUploadBytes;

    public float[] FlattenPositions() => FlattenVector3(Positions);
    public float[] FlattenNormals() => FlattenVector3(HasNormals ? Normals : CreateDefaultNormals(Positions.Length));
    public float[] FlattenTexCoords0() => FlattenVector2(TexCoords0);
    public float[] FlattenTangents() => FlattenVector4(Tangents);
    public float[] FlattenBoneIndices0() => FlattenVector4(BoneIndices0);
    public float[] FlattenBoneWeights0() => FlattenVector4(BoneWeights0);

    public static RenderGeometry3D FromMesh(Mesh3D mesh)
        => new(mesh.Positions, mesh.Normals, mesh.Indices, mesh.ResourceKey, mesh.TexCoords0, mesh.VertexColors0, mesh.Tangents, mesh.MaterialSlots, mesh.BoneIndices0, mesh.BoneWeights0);


    private static void ValidateBaseGeometry(Vector3[] positions, int[] indices, string resourceKey)
    {
        if (positions.Length == 0 && indices.Length == 0) return;
        var key = string.IsNullOrWhiteSpace(resourceKey) ? "<anonymous>" : resourceKey;
        if (positions.Length == 0) throw new ArgumentException($"Geometry '{key}' has indices but no positions.");
        if (indices.Length == 0) throw new ArgumentException($"Geometry '{key}' has positions but no indices.");
        if (indices.Length % 3 != 0) throw new ArgumentException($"Geometry '{key}' index count must be divisible by 3.");
        for (var i = 0; i < positions.Length; i++)
        {
            if (!IsFinite(positions[i])) throw new ArgumentException($"Geometry '{key}' contains a non-finite position at vertex {i}.");
        }
        for (var i = 0; i < indices.Length; i++)
        {
            if ((uint)indices[i] >= (uint)positions.Length) throw new ArgumentOutOfRangeException(nameof(indices), $"Geometry '{key}' index {i} points outside the vertex buffer.");
        }
    }

    private static void ValidateAuxiliaryGeometry(string key, Vector3[] normals, Vector2[] texCoords, ColorRgba[] colors, Vector4[] tangents, float[] materialSlots, Vector4[] boneIndices, Vector4[] boneWeights)
    {
        for (var i = 0; i < normals.Length; i++) if (!IsFinite(normals[i])) throw new ArgumentException($"Geometry '{key}' contains a non-finite normal at vertex {i}.");
        for (var i = 0; i < texCoords.Length; i++) if (!IsFinite(texCoords[i])) throw new ArgumentException($"Geometry '{key}' contains a non-finite UV at vertex {i}.");
        for (var i = 0; i < colors.Length; i++) if (!float.IsFinite(colors[i].R) || !float.IsFinite(colors[i].G) || !float.IsFinite(colors[i].B) || !float.IsFinite(colors[i].A)) throw new ArgumentException($"Geometry '{key}' contains a non-finite color at vertex {i}.");
        for (var i = 0; i < tangents.Length; i++) if (!IsFinite(tangents[i])) throw new ArgumentException($"Geometry '{key}' contains a non-finite tangent at vertex {i}.");
        for (var i = 0; i < boneIndices.Length; i++) if (!IsFinite(boneIndices[i])) throw new ArgumentException($"Geometry '{key}' contains a non-finite bone-index vector at vertex {i}.");
        for (var i = 0; i < boneWeights.Length; i++) if (!IsFinite(boneWeights[i])) throw new ArgumentException($"Geometry '{key}' contains a non-finite bone-weight vector at vertex {i}.");
        for (var i = 0; i < materialSlots.Length; i++) if (!float.IsFinite(materialSlots[i])) throw new ArgumentException($"Geometry '{key}' contains a non-finite material slot at vertex {i}.");
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static bool IsFinite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);
    private static bool IsFinite(Vector4 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) && float.IsFinite(v.W);

    private VertexLayout3D ResolveLayout()
    {
        if (HasSkinWeights && HasTangents && HasTexCoords0) return VertexLayout3D.PositionNormalTexCoordTangentSkin;
        if (HasSkinWeights && HasTexCoords0) return VertexLayout3D.PositionNormalTexCoordSkin;
        if (HasTangents && HasColors0 && HasTexCoords0) return VertexLayout3D.PositionNormalTexCoordTangentColor;
        if (HasTangents && HasTexCoords0) return VertexLayout3D.PositionNormalTexCoordTangent;
        if (HasColors0 && HasTexCoords0) return VertexLayout3D.PositionNormalTexCoordColor;
        if (HasColors0) return VertexLayout3D.PositionNormalColor;
        if (HasTexCoords0) return VertexLayout3D.PositionNormalTexCoord;
        return VertexLayout3D.PositionNormal;
    }

    private static Vector3[] ResolveNormals(Vector3[]? normals, Vector3[] positions, int[] indices)
    {
        if (normals is not null && normals.Length == positions.Length) return (Vector3[])normals.Clone();
        return TangentGenerator3D.GenerateNormals(positions, indices);
    }

    private static Vector2[] NormalizeTexCoords(Vector2[]? texCoords, int vertexCount)
    {
        if (texCoords is not null && texCoords.Length == vertexCount) return (Vector2[])texCoords.Clone();
        return Array.Empty<Vector2>();
    }

    private static ColorRgba[] NormalizeColors(ColorRgba[]? colors, int vertexCount)
    {
        if (colors is not null && colors.Length == vertexCount) return (ColorRgba[])colors.Clone();
        return Array.Empty<ColorRgba>();
    }

    private static Vector4[] NormalizeVector4(Vector4[]? values, int vertexCount)
    {
        if (values is not null && values.Length == vertexCount) return (Vector4[])values.Clone();
        return Array.Empty<Vector4>();
    }

    private static Vector4[] ResolveTangents(Vector4[]? tangents, Vector3[] positions, Vector3[] normals, Vector2[] texCoords, int[] indices)
    {
        if (tangents is not null && tangents.Length == positions.Length) return (Vector4[])tangents.Clone();
        if (texCoords.Length != positions.Length) return Array.Empty<Vector4>();
        return TangentGenerator3D.GenerateTangents(positions, normals, texCoords, indices);
    }

    private static long EstimateVector3Bytes(Vector3[] values) => values.LongLength * sizeof(float) * 3L;
    private static long EstimateVector2Bytes(Vector2[] values) => values.LongLength * sizeof(float) * 2L;
    private static long EstimateVector4Bytes(Vector4[] values) => values.LongLength * sizeof(float) * 4L;
    private static long EstimateColorBytes(ColorRgba[] values) => values.LongLength * sizeof(float) * 4L;

    private static Bounds3D ComputeLocalBounds(Vector3[] positions)
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

    private static float ComputeBoundingRadius(Vector3[] positions)
    {
        var radiusSquared = 0f;
        for (var i = 0; i < positions.Length; i++)
        {
            radiusSquared = MathF.Max(radiusSquared, positions[i].LengthSquared());
        }

        return MathF.Sqrt(radiusSquared);
    }

    private static Vector3[] CreateDefaultNormals(int count)
    {
        var normals = new Vector3[count];
        for (var i = 0; i < normals.Length; i++) normals[i] = Vector3.UnitZ;
        return normals;
    }

    private static float[] FlattenVector3(Vector3[] values)
    {
        var result = new float[values.Length * 3];
        for (var i = 0; i < values.Length; i++)
        {
            var baseIndex = i * 3;
            result[baseIndex] = values[i].X;
            result[baseIndex + 1] = values[i].Y;
            result[baseIndex + 2] = values[i].Z;
        }

        return result;
    }

    private static float[] FlattenVector2(Vector2[] values)
    {
        var result = new float[values.Length * 2];
        for (var i = 0; i < values.Length; i++)
        {
            var baseIndex = i * 2;
            result[baseIndex] = values[i].X;
            result[baseIndex + 1] = values[i].Y;
        }

        return result;
    }

    private static float[] FlattenVector4(Vector4[] values)
    {
        var result = new float[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
        {
            var baseIndex = i * 4;
            result[baseIndex] = values[i].X;
            result[baseIndex + 1] = values[i].Y;
            result[baseIndex + 2] = values[i].Z;
            result[baseIndex + 3] = values[i].W;
        }

        return result;
    }
}
