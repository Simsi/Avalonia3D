using System;
using System.Numerics;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Geometry;

/// <summary>
/// Exact-size stream builder. Callers write directly into spans and Build transfers those arrays
/// into immutable preprocessing without a defensive copy. Enabled optimization may remap streams
/// once before the final geometry resource takes ownership.
/// </summary>
public sealed class MeshGeometryBuilder3D
{
    private Vector3[]? _positions;
    private Vector3[]? _normals;
    private Vector2[]? _texCoords0;
    private ColorRgba[]? _colors0;
    private Vector4[]? _tangents;
    private float[]? _materialSlots;
    private Vector4[]? _boneIndices0;
    private Vector4[]? _boneWeights0;
    private int[]? _indices;
    private bool _built;

    public MeshGeometryBuilder3D(int vertexCount, int indexCount, GeometryStreamMask3D streams = GeometryStreamMask3D.None)
    {
        VertexCount = Guard3D.NonNegative(vertexCount, nameof(vertexCount));
        IndexCount = Guard3D.NonNegative(indexCount, nameof(indexCount));
        if ((vertexCount == 0) != (indexCount == 0))
            throw new ArgumentException("Vertex and index counts must both be zero or both be non-zero.");
        if (indexCount % 3 != 0)
            throw new ArgumentOutOfRangeException(nameof(indexCount), indexCount, "Triangle-list index count must be divisible by three.");
        if ((streams & GeometryStreamMask3D.SkinWeights) != 0 && vertexCount == 0)
            throw new ArgumentException("Skin streams require a non-empty mesh.", nameof(streams));

        Streams = streams;
        _positions = vertexCount == 0 ? Array.Empty<Vector3>() : new Vector3[vertexCount];
        _indices = indexCount == 0 ? Array.Empty<int>() : new int[indexCount];
        if ((streams & GeometryStreamMask3D.Normals) != 0) _normals = new Vector3[vertexCount];
        if ((streams & GeometryStreamMask3D.TexCoords0) != 0) _texCoords0 = new Vector2[vertexCount];
        if ((streams & GeometryStreamMask3D.Colors0) != 0) _colors0 = new ColorRgba[vertexCount];
        if ((streams & GeometryStreamMask3D.Tangents) != 0) _tangents = new Vector4[vertexCount];
        if ((streams & GeometryStreamMask3D.MaterialSlots) != 0) _materialSlots = new float[vertexCount];
        if ((streams & GeometryStreamMask3D.SkinWeights) != 0)
        {
            _boneIndices0 = new Vector4[vertexCount];
            _boneWeights0 = new Vector4[vertexCount];
        }
    }

    public int VertexCount { get; }
    public int IndexCount { get; }
    public GeometryStreamMask3D Streams { get; }

    public Span<Vector3> Positions => GetSpan(_positions, nameof(Positions));
    public Span<int> Indices => GetSpan(_indices, nameof(Indices));
    public Span<Vector3> Normals => GetRequiredStream(_normals, GeometryStreamMask3D.Normals, nameof(Normals));
    public Span<Vector2> TexCoords0 => GetRequiredStream(_texCoords0, GeometryStreamMask3D.TexCoords0, nameof(TexCoords0));
    public Span<ColorRgba> Colors0 => GetRequiredStream(_colors0, GeometryStreamMask3D.Colors0, nameof(Colors0));
    public Span<Vector4> Tangents => GetRequiredStream(_tangents, GeometryStreamMask3D.Tangents, nameof(Tangents));
    public Span<float> MaterialSlots => GetRequiredStream(_materialSlots, GeometryStreamMask3D.MaterialSlots, nameof(MaterialSlots));
    public Span<Vector4> BoneIndices0 => GetRequiredStream(_boneIndices0, GeometryStreamMask3D.SkinWeights, nameof(BoneIndices0));
    public Span<Vector4> BoneWeights0 => GetRequiredStream(_boneWeights0, GeometryStreamMask3D.SkinWeights, nameof(BoneWeights0));

    public Mesh3D Build(
        string? resourceKey = null,
        GeometryBuildOptions3D? options = null,
        ReadOnlySpan<ColorRgba> materialSlotBaseColors = default)
    {
        EnsureWritable();
        _built = true;
        var geometry = RenderGeometry3D.TakeOwnership(
            Take(ref _positions),
            Take(ref _normals),
            Take(ref _indices),
            string.IsNullOrWhiteSpace(resourceKey) ? "custom:" + Guid.NewGuid().ToString("N") : resourceKey!,
            Take(ref _texCoords0),
            Take(ref _colors0),
            Take(ref _tangents),
            Take(ref _materialSlots),
            Take(ref _boneIndices0),
            Take(ref _boneWeights0),
            options);
        return new Mesh3D(geometry, materialSlotBaseColors.IsEmpty ? null : materialSlotBaseColors.ToArray());
    }

    private Span<T> GetRequiredStream<T>(T[]? stream, GeometryStreamMask3D flag, string name) where T : struct
    {
        EnsureWritable();
        if ((Streams & flag) == 0 || stream is null)
            throw new InvalidOperationException($"The builder was not created with stream '{flag}', so {name} is unavailable.");
        return stream;
    }

    private Span<T> GetSpan<T>(T[]? stream, string name) where T : struct
    {
        EnsureWritable();
        return stream ?? throw new InvalidOperationException($"{name} is unavailable after Build().");
    }

    private void EnsureWritable()
    {
        if (_built) throw new InvalidOperationException("The mesh builder has already transferred its storage to an immutable geometry resource.");
    }

    private static T[]? Take<T>(ref T[]? value)
    {
        var result = value;
        value = null;
        return result;
    }
}
