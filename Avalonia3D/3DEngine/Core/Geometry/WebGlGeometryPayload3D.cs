using System;

namespace ThreeDEngine.Core.Geometry;

/// <summary>
/// Versionless immutable WebGL binary upload payload for a RenderGeometry3D.
/// RenderGeometry3D is immutable, so this payload is built once per wireframe mode and reused
/// by browser presenters without re-flattening managed vectors on every resource upload pass.
/// </summary>
public sealed class WebGlGeometryPayload3D
{
    public WebGlGeometryPayload3D(
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
        Positions = positions;
        Normals = normals;
        TexCoords0 = texCoords0;
        Tangents = tangents;
        Colors0 = colors0;
        MaterialSlots = materialSlots;
        BoneIndices0 = boneIndices0;
        BoneWeights0 = boneWeights0;
        Indices = indices;
        IndexElementSize = indexElementSize;
        WireframeIndices = wireframeIndices;
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
    public byte[] Positions { get; }
    public byte[] Normals { get; }
    public byte[] TexCoords0 { get; }
    public byte[] Tangents { get; }
    public byte[] Colors0 { get; }
    public byte[] MaterialSlots { get; }
    public byte[] BoneIndices0 { get; }
    public byte[] BoneWeights0 { get; }
    public byte[] Indices { get; }
    public int IndexElementSize { get; }
    public byte[] WireframeIndices { get; }
    public int WireframeIndexElementSize { get; }
    public bool HasTexCoords0 { get; }
    public bool HasTangents { get; }
    public bool HasColors0 { get; }
    public bool HasMaterialSlots { get; }
    public bool HasSkinWeights { get; }
    public string VertexLayout { get; }

    public long UploadByteCount =>
        Positions.LongLength +
        Normals.LongLength +
        TexCoords0.LongLength +
        Tangents.LongLength +
        Colors0.LongLength +
        MaterialSlots.LongLength +
        BoneIndices0.LongLength +
        BoneWeights0.LongLength +
        Indices.LongLength +
        WireframeIndices.LongLength;
}
