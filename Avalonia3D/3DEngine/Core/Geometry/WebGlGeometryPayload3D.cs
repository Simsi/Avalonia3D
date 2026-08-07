using System;

namespace ThreeDEngine.Core.Geometry;

/// <summary>
/// Versionless immutable WebGL binary upload payload for a RenderGeometry3D.
/// RenderGeometry3D is immutable, so this payload is built once per wireframe mode and reused
/// by browser presenters without re-flattening managed vectors on every resource upload pass.
/// </summary>
public sealed class WebGlGeometryPayload3D
{
    internal WebGlGeometryPayload3D(
        int vertexCount,
        int indexCount,
        byte[] positions,
        byte[] normals,
        byte[] texCoords0,
        byte[] tangents,
        byte[] colors0,
        byte[] materialSlots,
        byte[] boneIndices0,
        byte[] boneWeights0,
        byte[] indices,
        int indexElementSize,
        byte[] wireframeIndices,
        int wireframeIndexElementSize,
        bool hasTexCoords0,
        bool hasTangents,
        bool hasColors0,
        bool hasMaterialSlots,
        bool hasSkinWeights,
        string vertexLayout)
    {
        VertexCount = vertexCount;
        IndexCount = indexCount;
        PositionStorage = positions;
        NormalStorage = normals;
        TexCoordStorage = texCoords0;
        TangentStorage = tangents;
        ColorStorage = colors0;
        MaterialSlotStorage = materialSlots;
        BoneIndexStorage = boneIndices0;
        BoneWeightStorage = boneWeights0;
        IndexStorage = indices;
        IndexElementSize = indexElementSize;
        WireframeIndexStorage = wireframeIndices;
        WireframeIndexElementSize = wireframeIndexElementSize;
        HasTexCoords0 = hasTexCoords0;
        HasTangents = hasTangents;
        HasColors0 = hasColors0;
        HasMaterialSlots = hasMaterialSlots;
        HasSkinWeights = hasSkinWeights;
        VertexLayout = vertexLayout;
    }

    public int VertexCount { get; }
    public int IndexCount { get; }
    public ReadOnlyMemory<byte> Positions => PositionStorage.Length == 0 ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>((byte[])PositionStorage.Clone());
    public ReadOnlyMemory<byte> Normals => NormalStorage.Length == 0 ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>((byte[])NormalStorage.Clone());
    public ReadOnlyMemory<byte> TexCoords0 => TexCoordStorage.Length == 0 ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>((byte[])TexCoordStorage.Clone());
    public ReadOnlyMemory<byte> Tangents => TangentStorage.Length == 0 ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>((byte[])TangentStorage.Clone());
    public ReadOnlyMemory<byte> Colors0 => ColorStorage.Length == 0 ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>((byte[])ColorStorage.Clone());
    public ReadOnlyMemory<byte> MaterialSlots => MaterialSlotStorage.Length == 0 ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>((byte[])MaterialSlotStorage.Clone());
    public ReadOnlyMemory<byte> BoneIndices0 => BoneIndexStorage.Length == 0 ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>((byte[])BoneIndexStorage.Clone());
    public ReadOnlyMemory<byte> BoneWeights0 => BoneWeightStorage.Length == 0 ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>((byte[])BoneWeightStorage.Clone());
    public ReadOnlyMemory<byte> Indices => IndexStorage.Length == 0 ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>((byte[])IndexStorage.Clone());
    public int IndexElementSize { get; }
    public ReadOnlyMemory<byte> WireframeIndices => WireframeIndexStorage.Length == 0 ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>((byte[])WireframeIndexStorage.Clone());
    public int WireframeIndexElementSize { get; }
    public bool HasTexCoords0 { get; }
    public bool HasTangents { get; }
    public bool HasColors0 { get; }
    public bool HasMaterialSlots { get; }
    public bool HasSkinWeights { get; }
    public string VertexLayout { get; }

    internal byte[] PositionStorage { get; }
    internal byte[] NormalStorage { get; }
    internal byte[] TexCoordStorage { get; }
    internal byte[] TangentStorage { get; }
    internal byte[] ColorStorage { get; }
    internal byte[] MaterialSlotStorage { get; }
    internal byte[] BoneIndexStorage { get; }
    internal byte[] BoneWeightStorage { get; }
    internal byte[] IndexStorage { get; }
    internal byte[] WireframeIndexStorage { get; }

    public long VertexUploadByteCount =>
        PositionStorage.LongLength +
        NormalStorage.LongLength +
        TexCoordStorage.LongLength +
        TangentStorage.LongLength +
        ColorStorage.LongLength +
        MaterialSlotStorage.LongLength +
        BoneIndexStorage.LongLength +
        BoneWeightStorage.LongLength;

    public long UploadByteCount =>
        PositionStorage.LongLength +
        NormalStorage.LongLength +
        TexCoordStorage.LongLength +
        TangentStorage.LongLength +
        ColorStorage.LongLength +
        MaterialSlotStorage.LongLength +
        BoneIndexStorage.LongLength +
        BoneWeightStorage.LongLength +
        IndexStorage.LongLength +
        WireframeIndexStorage.LongLength;
}
