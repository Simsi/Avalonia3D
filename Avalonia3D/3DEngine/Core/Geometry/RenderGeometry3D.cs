using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Geometry.Surfaces;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.Geometry;

/// <summary>
/// Canonical immutable indexed triangle geometry shared by every backend. Input streams are
/// owned once, triangle/vertex order is optimized once, optional derived streams and spatial
/// acceleration are materialized lazily, and the desktop upload view uses a layout-specific
/// packed representation instead of a fixed 100-byte vertex.
/// </summary>
public sealed class RenderGeometry3D
{
    private static long _nextGeometryVersion;
    private readonly Lazy<GeometryBuffer3D<Vector3>> _normals;
    private readonly Lazy<GeometryBuffer3D<Vector4>> _tangents;
    private readonly Lazy<GeometryIndexBuffer3D> _wireframeIndices;
    private readonly Lazy<InterleavedVertexBuffer3D> _interleavedVertexBuffer;
    private readonly Lazy<WebGlGeometryPayload3D> _webGlPayload;
    private readonly Lazy<WebGlGeometryPayload3D> _webGlPayloadWithWireframe;
    private readonly Lazy<MeshletSet3D> _meshlets;
    private readonly Lazy<MeshBvh3D> _bvh;
    private readonly long _ownedBaseVertexBytes;
    private readonly bool _hasTangents;
    private readonly bool _hasSuppliedNormals;
    private readonly bool _hasSuppliedTangents;

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
        Vector4[]? boneWeights0 = null,
        GeometryBuildOptions3D? buildOptions = null)
        : this(
            Copy(positions),
            Copy(normals),
            Copy(indices),
            resourceKey,
            Copy(texCoords0),
            Copy(colors0),
            Copy(tangents),
            Copy(materialSlots),
            Copy(boneIndices0),
            Copy(boneWeights0),
            buildOptions,
            takeOwnership: true)
    {
    }

    private RenderGeometry3D(
        Vector3[]? positions,
        Vector3[]? normals,
        int[]? indices,
        string resourceKey,
        Vector2[]? texCoords0,
        ColorRgba[]? colors0,
        Vector4[]? tangents,
        float[]? materialSlots,
        Vector4[]? boneIndices0,
        Vector4[]? boneWeights0,
        GeometryBuildOptions3D? buildOptions,
        bool takeOwnership)
    {
        _ = takeOwnership;
        ResourceKey = string.IsNullOrWhiteSpace(resourceKey) ? "geometry:" + Guid.NewGuid().ToString("N") : resourceKey;
        BuildOptions = (buildOptions ?? GeometryBuildOptions3D.Default).Snapshot();

        var data = new GeometryMutableData3D(
            positions ?? Array.Empty<Vector3>(),
            normals ?? Array.Empty<Vector3>(),
            indices ?? Array.Empty<int>(),
            texCoords0 ?? Array.Empty<Vector2>(),
            colors0 ?? Array.Empty<ColorRgba>(),
            tangents ?? Array.Empty<Vector4>(),
            materialSlots ?? Array.Empty<float>(),
            boneIndices0 ?? Array.Empty<Vector4>(),
            boneWeights0 ?? Array.Empty<Vector4>());
        ValidateAndNormalize(data, ResourceKey, BuildOptions);
        MeshOptimizer3D.Optimize(data, BuildOptions);

        Positions = GeometryBuffer3D<Vector3>.TakeOwnership(data.Positions);
        TexCoords0 = GeometryBuffer3D<Vector2>.TakeOwnership(data.TexCoords0);
        Colors0 = GeometryBuffer3D<ColorRgba>.TakeOwnership(data.Colors0);
        MaterialSlots = GeometryBuffer3D<float>.TakeOwnership(data.MaterialSlots);
        BoneIndices0 = GeometryBuffer3D<Vector4>.TakeOwnership(data.BoneIndices0);
        BoneWeights0 = GeometryBuffer3D<Vector4>.TakeOwnership(data.BoneWeights0);
        Indices = GeometryIndexBuffer3D.TakeOwnership(data.Indices);
        SourceTriangleIndices = GeometryBuffer3D<int>.TakeOwnership(data.SourceTriangleIndices);

        var suppliedNormals = data.Normals.Length > 0 ? GeometryBuffer3D<Vector3>.TakeOwnership(data.Normals) : null;
        var suppliedTangents = data.Tangents.Length > 0 ? GeometryBuffer3D<Vector4>.TakeOwnership(data.Tangents) : null;
        _hasSuppliedNormals = suppliedNormals is not null;
        _hasSuppliedTangents = suppliedTangents is not null;
        _normals = new Lazy<GeometryBuffer3D<Vector3>>(
            () => suppliedNormals ?? GeometryBuffer3D<Vector3>.TakeOwnership(TangentGenerator3D.GenerateNormals(Positions.Storage, Indices)),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _hasTangents = suppliedTangents is not null || BuildOptions.GenerateMissingTangents && TexCoords0.Length == Positions.Length && Positions.Length > 0;
        _tangents = new Lazy<GeometryBuffer3D<Vector4>>(
            () => suppliedTangents ?? (_hasTangents
                ? GeometryBuffer3D<Vector4>.TakeOwnership(TangentGenerator3D.GenerateTangents(Positions.Storage, Normals.Storage, TexCoords0.Storage, Indices))
                : GeometryBuffer3D<Vector4>.Empty),
            LazyThreadSafetyMode.ExecutionAndPublication);

        Layout = VertexLayout3D.CreateForGeometry(
            BuildOptions.PackVertexStreams,
            BuildOptions.PackHalfPrecisionTexCoords,
            HasTexCoords0,
            _hasTangents,
            HasColors0,
            HasMaterialSlots,
            HasSkinWeights,
            data.TexCoords0,
            data.Colors0,
            data.MaterialSlots,
            data.BoneIndices0,
            data.BoneWeights0,
            suppliedNormals?.Storage,
            suppliedTangents?.Storage);
        LocalBounds = ComputeLocalBounds(data.Positions);
        BoundingRadius = ComputeBoundingRadius(data.Positions);
        GeometryVersion = Interlocked.Increment(ref _nextGeometryVersion);

        _ownedBaseVertexBytes = EstimateVector3Bytes(Positions) +
            (suppliedNormals?.LongLength ?? 0L) * sizeof(float) * 3L +
            EstimateVector2Bytes(TexCoords0) + EstimateColorBytes(Colors0) +
            (suppliedTangents?.LongLength ?? 0L) * sizeof(float) * 4L +
            EstimateVector4Bytes(BoneIndices0) + EstimateVector4Bytes(BoneWeights0) +
            MaterialSlots.LongLength * sizeof(float);
        EstimatedIndexUploadBytes = Indices.ByteCount;
        EstimatedWebGlVertexUploadBytes =
            Positions.LongLength * sizeof(float) * 3L + Positions.LongLength * sizeof(float) * 3L +
            TexCoords0.LongLength * sizeof(float) * 2L + (_hasTangents ? Positions.LongLength * sizeof(float) * 4L : 0L) +
            Colors0.LongLength * sizeof(float) * 4L + MaterialSlots.LongLength * sizeof(float) +
            BoneIndices0.LongLength * sizeof(float) * 4L + BoneWeights0.LongLength * sizeof(float) * 4L;

        _wireframeIndices = new Lazy<GeometryIndexBuffer3D>(BuildWireframeIndices, LazyThreadSafetyMode.ExecutionAndPublication);
        _interleavedVertexBuffer = new Lazy<InterleavedVertexBuffer3D>(() => PackedVertexBufferBuilder3D.Build(this), LazyThreadSafetyMode.ExecutionAndPublication);
        _webGlPayload = new Lazy<WebGlGeometryPayload3D>(() => BuildWebGlPayload(includeWireframe: false), LazyThreadSafetyMode.ExecutionAndPublication);
        _webGlPayloadWithWireframe = new Lazy<WebGlGeometryPayload3D>(() => BuildWebGlPayload(includeWireframe: true), LazyThreadSafetyMode.ExecutionAndPublication);
        _meshlets = new Lazy<MeshletSet3D>(
            () => BuildOptions.BuildMeshletsOnFirstUse
                ? MeshletSet3D.Build(this, BuildOptions.MeshletMaxVertices, BuildOptions.MeshletMaxTriangles)
                : throw new InvalidOperationException($"Meshlet generation is disabled for geometry '{ResourceKey}'."),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _bvh = new Lazy<MeshBvh3D>(
            () => BuildOptions.BuildBvhOnFirstUse
                ? MeshBvh3D.Build(this)
                : throw new InvalidOperationException($"BVH generation is disabled for geometry '{ResourceKey}'."),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public GeometryBuffer3D<Vector3> Positions { get; }
    public GeometryBuffer3D<Vector3> Normals => _normals.Value;
    public GeometryBuffer3D<Vector2> TexCoords0 { get; }
    public GeometryBuffer3D<ColorRgba> Colors0 { get; }
    public GeometryBuffer3D<Vector4> Tangents => _tangents.Value;
    public GeometryBuffer3D<Vector4> BoneIndices0 { get; }
    public GeometryBuffer3D<Vector4> BoneWeights0 { get; }
    public GeometryBuffer3D<float> MaterialSlots { get; }
    public GeometryIndexBuffer3D Indices { get; }
    /// <summary>Empty when optimized triangle order is identical to source order.</summary>
    public GeometryBuffer3D<int> SourceTriangleIndices { get; }
    public GeometryIndexBuffer3D WireframeIndices => _wireframeIndices.Value;
    public string ResourceKey { get; }
    public long GeometryVersion { get; }
    public GeometryBuildOptions3D BuildOptions { get; }
    public VertexLayout3D Layout { get; }
    public Bounds3D LocalBounds { get; }
    public float BoundingRadius { get; }
    public int VertexCount => Positions.Length;
    public int IndexCount => Indices.Length;
    public int TriangleCount => Indices.Length / 3;
    public int WireframeIndexCount => WireframeIndices.Length;
    public bool IsNormalsMaterialized => _normals.IsValueCreated;
    public bool IsTangentsMaterialized => _tangents.IsValueCreated;
    public bool IsWireframeMaterialized => _wireframeIndices.IsValueCreated;
    public bool IsInterleavedVertexBufferMaterialized => _interleavedVertexBuffer.IsValueCreated;
    public bool IsMeshletsMaterialized => _meshlets.IsValueCreated;
    public bool IsBvhMaterialized => _bvh.IsValueCreated;
    public bool HasNormals => Positions.Length > 0;
    public bool HasTexCoords0 => TexCoords0.Length == Positions.Length && Positions.Length > 0;
    public bool HasColors0 => Colors0.Length == Positions.Length && Positions.Length > 0;
    public bool HasTangents => _hasTangents;
    public bool HasTangentSpace => HasNormals && HasTexCoords0 && HasTangents;
    public bool HasSkinWeights => BoneIndices0.Length == Positions.Length && BoneWeights0.Length == Positions.Length && Positions.Length > 0;
    public bool HasMaterialSlots => MaterialSlots.Length == Positions.Length && Positions.Length > 0;
    public long EstimatedSourceVertexBytes => _ownedBaseVertexBytes +
        (_normals.IsValueCreated && !_hasSuppliedNormals ? Normals.LongLength * sizeof(float) * 3L : 0L) +
        (_tangents.IsValueCreated && !_hasSuppliedTangents ? Tangents.LongLength * sizeof(float) * 4L : 0L);
    public long EstimatedPackedVertexUploadBytes => (long)VertexCount * Layout.StrideBytes;
    public long EstimatedWebGlVertexUploadBytes { get; }
    public long EstimatedVertexUploadBytes => EstimatedPackedVertexUploadBytes;
    public long EstimatedIndexUploadBytes { get; }
    public long EstimatedWireframeIndexUploadBytes => WireframeIndices.ByteCount;
    public long EstimatedUploadBytes => EstimatedPackedVertexUploadBytes + EstimatedIndexUploadBytes;
    public long EstimatedResidentBytes => _ownedBaseVertexBytes + EstimatedIndexUploadBytes + SourceTriangleIndices.LongLength * sizeof(int) +
        (_normals.IsValueCreated && !_hasSuppliedNormals ? Normals.LongLength * sizeof(float) * 3L : 0L) +
        (_tangents.IsValueCreated && !_hasSuppliedTangents ? Tangents.LongLength * sizeof(float) * 4L : 0L) +
        (_interleavedVertexBuffer.IsValueCreated ? _interleavedVertexBuffer.Value.ByteCount : 0L) +
        (_wireframeIndices.IsValueCreated ? _wireframeIndices.Value.ByteCount : 0L) +
        (_webGlPayload.IsValueCreated ? _webGlPayload.Value.UploadByteCount : 0L) +
        (_webGlPayloadWithWireframe.IsValueCreated ? _webGlPayloadWithWireframe.Value.WireframeIndexStorage.LongLength : 0L) +
        (_meshlets.IsValueCreated ? _meshlets.Value.EstimatedResidentBytes : 0L) +
        (_bvh.IsValueCreated ? _bvh.Value.EstimatedResidentBytes : 0L);

    public InterleavedVertexBuffer3D GetInterleavedVertexBuffer() => _interleavedVertexBuffer.Value;
    public MeshletSet3D GetMeshlets() => _meshlets.Value;

    /// <summary>Maps an optimized runtime triangle index back to the caller/importer source triangle index.</summary>
    public int GetSourceTriangleIndex(int triangleIndex)
    {
        if ((uint)triangleIndex >= (uint)TriangleCount) throw new ArgumentOutOfRangeException(nameof(triangleIndex));
        return SourceTriangleIndices.Length == 0 ? triangleIndex : SourceTriangleIndices[triangleIndex];
    }

    internal MeshBvh3D GetBvh() => _bvh.Value;

    public float[] FlattenPositions() => FlattenVector3(Positions);
    public float[] FlattenNormals() => FlattenVector3(Normals);
    public float[] FlattenTexCoords0() => FlattenVector2(TexCoords0);
    public float[] FlattenTangents() => FlattenVector4(Tangents);
    public float[] FlattenBoneIndices0() => FlattenVector4(BoneIndices0);
    public float[] FlattenBoneWeights0() => FlattenVector4(BoneWeights0);

    public static RenderGeometry3D FromMesh(Mesh3D mesh)
        => (mesh ?? throw new ArgumentNullException(nameof(mesh))).RenderGeometry;

    public WebGlGeometryPayload3D GetWebGlPayload(bool includeWireframe)
        => includeWireframe ? _webGlPayloadWithWireframe.Value : _webGlPayload.Value;

    internal static RenderGeometry3D TakeOwnership(
        Vector3[]? positions,
        Vector3[]? normals,
        int[]? indices,
        string resourceKey,
        Vector2[]? texCoords0,
        ColorRgba[]? colors0,
        Vector4[]? tangents,
        float[]? materialSlots,
        Vector4[]? boneIndices0,
        Vector4[]? boneWeights0,
        GeometryBuildOptions3D? buildOptions)
        => new(positions, normals, indices, resourceKey, texCoords0, colors0, tangents, materialSlots, boneIndices0, boneWeights0, buildOptions, takeOwnership: true);

    private WebGlGeometryPayload3D BuildWebGlPayload(bool includeWireframe)
    {
        var wireframe = includeWireframe ? WireframeIndices : GeometryIndexBuffer3D.Empty;
        if (includeWireframe)
        {
            var shared = _webGlPayload.Value;
            return new WebGlGeometryPayload3D(
                VertexCount, IndexCount,
                shared.PositionStorage, shared.NormalStorage, shared.TexCoordStorage, shared.TangentStorage,
                shared.ColorStorage, shared.MaterialSlotStorage, shared.BoneIndexStorage, shared.BoneWeightStorage,
                shared.IndexStorage, Indices.ElementSizeBytes,
                wireframe.GetUploadBytes(), wireframe.ElementSizeBytes,
                HasTexCoords0, HasTangents, HasColors0, HasMaterialSlots, HasSkinWeights, Layout.ToString());
        }
        return new WebGlGeometryPayload3D(
            VertexCount, IndexCount,
            CopyStructBytes(Positions.Storage), CopyStructBytes(Normals.Storage),
            HasTexCoords0 ? CopyStructBytes(TexCoords0.Storage) : Array.Empty<byte>(),
            HasTangents ? CopyStructBytes(Tangents.Storage) : Array.Empty<byte>(),
            HasColors0 ? CopyStructBytes(Colors0.Storage) : Array.Empty<byte>(),
            HasMaterialSlots ? CopyFloatBytes(MaterialSlots.Storage) : Array.Empty<byte>(),
            HasSkinWeights ? CopyStructBytes(BoneIndices0.Storage) : Array.Empty<byte>(),
            HasSkinWeights ? CopyStructBytes(BoneWeights0.Storage) : Array.Empty<byte>(),
            Indices.GetUploadBytes(), Indices.ElementSizeBytes,
            wireframe.GetUploadBytes(), wireframe.ElementSizeBytes,
            HasTexCoords0, HasTangents, HasColors0, HasMaterialSlots, HasSkinWeights, Layout.ToString());
    }

    private GeometryIndexBuffer3D BuildWireframeIndices()
        => GeometryIndexBuffer3D.CopyFrom(TangentGenerator3D.BuildWireframeIndices(Indices));

    private static byte[] CopyFloatBytes(float[] values)
    {
        if (values.Length == 0) return Array.Empty<byte>();
        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static byte[] CopyStructBytes<T>(T[] values) where T : struct
        => values.Length == 0 ? Array.Empty<byte>() : MemoryMarshal.AsBytes(values.AsSpan()).ToArray();

    private static void ValidateAndNormalize(GeometryMutableData3D data, string key, GeometryBuildOptions3D options)
    {
        if (data.Positions.Length == 0 && data.Indices.Length == 0) return;
        if (data.Positions.Length == 0) throw new ArgumentException($"Geometry '{key}' has indices but no positions.");
        if (data.Indices.Length == 0) throw new ArgumentException($"Geometry '{key}' has positions but no indices.");
        if (data.Indices.Length % 3 != 0) throw new ArgumentException($"Geometry '{key}' index count must be divisible by 3.");
        for (var i = 0; i < data.Positions.Length; i++) if (!IsFinite(data.Positions[i])) throw new ArgumentException($"Geometry '{key}' contains a non-finite position at vertex {i}.");
        for (var i = 0; i < data.Indices.Length; i++)
        {
            if ((uint)data.Indices[i] >= (uint)data.Positions.Length) throw new ArgumentOutOfRangeException(nameof(data.Indices), $"Geometry '{key}' index {i} points outside the vertex buffer.");
        }

        ValidateOptionalStreamLength(data.Normals.Length, data.Positions.Length, key, "normals");
        ValidateOptionalStreamLength(data.TexCoords0.Length, data.Positions.Length, key, "texture coordinates");
        ValidateOptionalStreamLength(data.Colors0.Length, data.Positions.Length, key, "vertex colors");
        ValidateOptionalStreamLength(data.Tangents.Length, data.Positions.Length, key, "tangents");
        ValidateOptionalStreamLength(data.MaterialSlots.Length, data.Positions.Length, key, "material slots");
        ValidateOptionalStreamLength(data.BoneIndices0.Length, data.Positions.Length, key, "bone indices");
        ValidateOptionalStreamLength(data.BoneWeights0.Length, data.Positions.Length, key, "bone weights");
        if (data.BoneIndices0.Length != data.BoneWeights0.Length) throw new ArgumentException($"Geometry '{key}' must provide bone indices and weights together.");
        if (data.Normals.Length == 0 && !options.GenerateMissingNormals) throw new ArgumentException($"Geometry '{key}' has no normals and normal generation is disabled.");

        for (var i = 0; i < data.Normals.Length; i++)
        {
            var normal = data.Normals[i];
            if (!IsFinite(normal) || normal.LengthSquared() <= 1e-16f) throw new ArgumentException($"Geometry '{key}' contains an invalid normal at vertex {i}.");
            data.Normals[i] = Vector3.Normalize(normal);
        }
        for (var i = 0; i < data.TexCoords0.Length; i++) if (!IsFinite(data.TexCoords0[i])) throw new ArgumentException($"Geometry '{key}' contains a non-finite UV at vertex {i}.");
        for (var i = 0; i < data.Colors0.Length; i++)
        {
            var value = data.Colors0[i];
            if (!float.IsFinite(value.R) || !float.IsFinite(value.G) || !float.IsFinite(value.B) || !float.IsFinite(value.A)) throw new ArgumentException($"Geometry '{key}' contains a non-finite color at vertex {i}.");
        }
        for (var i = 0; i < data.Tangents.Length; i++)
        {
            var tangent = data.Tangents[i];
            var xyz = new Vector3(tangent.X, tangent.Y, tangent.Z);
            if (!IsFinite(tangent) || xyz.LengthSquared() <= 1e-16f || global::System.MathF.Abs(tangent.W) <= 1e-8f) throw new ArgumentException($"Geometry '{key}' contains an invalid tangent at vertex {i}.");
            xyz = Vector3.Normalize(xyz);
            data.Tangents[i] = new Vector4(xyz, tangent.W < 0f ? -1f : 1f);
        }
        for (var i = 0; i < data.MaterialSlots.Length; i++)
        {
            var value = data.MaterialSlots[i];
            if (!float.IsFinite(value) || value < 0f || global::System.MathF.Abs(value - global::System.MathF.Round(value)) > 0.0001f)
                throw new ArgumentException($"Geometry '{key}' contains an invalid material slot at vertex {i}.");
        }
        for (var i = 0; i < data.BoneIndices0.Length; i++)
        {
            var value = data.BoneIndices0[i];
            if (!IsFinite(value) || !IsNonNegativeInteger(value)) throw new ArgumentException($"Geometry '{key}' contains an invalid bone-index vector at vertex {i}.");
            var weights = data.BoneWeights0[i];
            if (!IsFinite(weights) || weights.X < 0f || weights.Y < 0f || weights.Z < 0f || weights.W < 0f) throw new ArgumentException($"Geometry '{key}' contains an invalid bone-weight vector at vertex {i}.");
            var sum = weights.X + weights.Y + weights.Z + weights.W;
            if (!float.IsFinite(sum) || sum <= 1e-8f) throw new ArgumentException($"Geometry '{key}' contains zero total bone weight at vertex {i}.");
            data.BoneWeights0[i] = weights / sum;
        }
    }

    private static bool IsNonNegativeInteger(Vector4 value)
        => value.X >= 0f && value.Y >= 0f && value.Z >= 0f && value.W >= 0f &&
           global::System.MathF.Abs(value.X - global::System.MathF.Round(value.X)) <= 0.0001f &&
           global::System.MathF.Abs(value.Y - global::System.MathF.Round(value.Y)) <= 0.0001f &&
           global::System.MathF.Abs(value.Z - global::System.MathF.Round(value.Z)) <= 0.0001f &&
           global::System.MathF.Abs(value.W - global::System.MathF.Round(value.W)) <= 0.0001f;

    private static void ValidateOptionalStreamLength(int streamLength, int vertexCount, string key, string streamName)
    {
        if (streamLength != 0 && streamLength != vertexCount) throw new ArgumentException($"Geometry '{key}' {streamName} count ({streamLength}) must match its position count ({vertexCount}).");
    }

    private static long EstimateVector3Bytes(GeometryBuffer3D<Vector3> values) => values.LongLength * sizeof(float) * 3L;
    private static long EstimateVector2Bytes(GeometryBuffer3D<Vector2> values) => values.LongLength * sizeof(float) * 2L;
    private static long EstimateVector4Bytes(GeometryBuffer3D<Vector4> values) => values.LongLength * sizeof(float) * 4L;
    private static long EstimateColorBytes(GeometryBuffer3D<ColorRgba> values) => values.LongLength * sizeof(float) * 4L;

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
        for (var i = 0; i < positions.Length; i++) radiusSquared = global::System.MathF.Max(radiusSquared, positions[i].LengthSquared());
        return global::System.MathF.Sqrt(radiusSquared);
    }

    private static float[] FlattenVector3(GeometryBuffer3D<Vector3> values)
    {
        var result = new float[values.Length * 3];
        for (var i = 0; i < values.Length; i++)
        {
            var offset = i * 3;
            result[offset] = values[i].X;
            result[offset + 1] = values[i].Y;
            result[offset + 2] = values[i].Z;
        }
        return result;
    }

    private static float[] FlattenVector2(GeometryBuffer3D<Vector2> values)
    {
        var result = new float[values.Length * 2];
        for (var i = 0; i < values.Length; i++)
        {
            var offset = i * 2;
            result[offset] = values[i].X;
            result[offset + 1] = values[i].Y;
        }
        return result;
    }

    private static float[] FlattenVector4(GeometryBuffer3D<Vector4> values)
    {
        var result = new float[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
        {
            var offset = i * 4;
            result[offset] = values[i].X;
            result[offset + 1] = values[i].Y;
            result[offset + 2] = values[i].Z;
            result[offset + 3] = values[i].W;
        }
        return result;
    }

    private static T[] Copy<T>(T[]? source) => source is null || source.Length == 0 ? Array.Empty<T>() : (T[])source.Clone();
    private static bool IsFinite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
    private static bool IsFinite(Vector4 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);
}
