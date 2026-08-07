using System;
using System.Numerics;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.Geometry;

/// <summary>
/// Immutable mesh handle. All vertex/index storage is owned exactly once by RenderGeometry;
/// the properties below are zero-copy read-only views over that canonical resource.
/// </summary>
public sealed class Mesh3D
{
    public static Mesh3D Empty { get; } = new(Array.Empty<Vector3>(), Array.Empty<Vector3>(), Array.Empty<int>(), "empty");

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
        Vector4[]? boneWeights0 = null,
        GeometryBuildOptions3D? buildOptions = null)
    {
        ResourceKey = string.IsNullOrWhiteSpace(resourceKey) ? "custom:" + Guid.NewGuid().ToString("N") : resourceKey;
        RenderGeometry = new RenderGeometry3D(
            positions,
            normals,
            indices,
            ResourceKey,
            texCoords0,
            vertexColors0,
            tangents,
            materialSlots,
            boneIndices0,
            boneWeights0,
            buildOptions);
        MaterialSlotBaseColors = GeometryBuffer3D<ColorRgba>.CopyFrom(materialSlotBaseColors);
        GeometryVersion = RenderGeometry.GeometryVersion;
        LocalBounds = RenderGeometry.LocalBounds;
        BoundingRadius = RenderGeometry.BoundingRadius;
    }

    /// <summary>Creates a mesh handle over an existing immutable geometry resource without copying its streams.</summary>
    public Mesh3D(
        RenderGeometry3D renderGeometry,
        ColorRgba[]? materialSlotBaseColors = null)
    {
        RenderGeometry = renderGeometry ?? throw new ArgumentNullException(nameof(renderGeometry));
        ResourceKey = renderGeometry.ResourceKey;
        MaterialSlotBaseColors = GeometryBuffer3D<ColorRgba>.CopyFrom(materialSlotBaseColors);
        GeometryVersion = renderGeometry.GeometryVersion;
        LocalBounds = renderGeometry.LocalBounds;
        BoundingRadius = renderGeometry.BoundingRadius;
    }

    public GeometryBuffer3D<Vector3> Positions => RenderGeometry.Positions;
    public GeometryBuffer3D<Vector3> Normals => RenderGeometry.Normals;
    public GeometryBuffer3D<Vector2> TexCoords0 => RenderGeometry.TexCoords0;
    public GeometryBuffer3D<ColorRgba> VertexColors0 => RenderGeometry.Colors0;
    public GeometryBuffer3D<Vector4> Tangents => RenderGeometry.Tangents;
    public GeometryBuffer3D<Vector4> BoneIndices0 => RenderGeometry.BoneIndices0;
    public GeometryBuffer3D<Vector4> BoneWeights0 => RenderGeometry.BoneWeights0;
    public GeometryIndexBuffer3D Indices => RenderGeometry.Indices;
    public GeometryBuffer3D<float> MaterialSlots => RenderGeometry.MaterialSlots;
    public GeometryBuffer3D<ColorRgba> MaterialSlotBaseColors { get; }
    public bool HasTexCoords0 => RenderGeometry.HasTexCoords0;
    public bool HasVertexColors0 => RenderGeometry.HasColors0;
    public bool HasTangents => RenderGeometry.HasTangents;
    public bool HasSkinWeights => RenderGeometry.HasSkinWeights;
    public bool HasMaterialSlots => RenderGeometry.HasMaterialSlots;
    public int MaterialSlotCount => MaterialSlotBaseColors.Length > 0 ? MaterialSlotBaseColors.Length : ComputeMaterialSlotCount(MaterialSlots);
    public string ResourceKey { get; }
    public long GeometryVersion { get; }
    public RenderGeometry3D RenderGeometry { get; }
    public Bounds3D LocalBounds { get; }
    public float BoundingRadius { get; }

    private static int ComputeMaterialSlotCount(GeometryBuffer3D<float> slots)
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
