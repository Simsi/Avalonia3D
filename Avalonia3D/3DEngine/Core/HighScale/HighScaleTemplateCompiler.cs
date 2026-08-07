using System;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.HighScale;

public static class HighScaleTemplateCompiler
{
    public static CompositeTemplate3D Compile(int id, CompositeObject3D source, bool bakeDetailedMesh = true)
    {
        ArgumentNullException.ThrowIfNull(source);
        var parts = new List<CompositePartTemplate3D>();
        var rootWorld = source.GetModelMatrix();
        if (!Matrix4x4.Invert(rootWorld, out var inverseRootWorld))
            throw new InvalidOperationException($"Composite '{source.Name}' has a singular root transform and cannot be compiled.");

        foreach (var part in source.EnumerateDescendants(includeSelf: false))
        {
            if (!part.UseMeshRendering || !part.IsVisible) continue;
            var mesh = part.GetMesh();
            var material = part.Material;
            var partWorld = part.GetModelMatrix();
            var localToTemplate = partWorld * inverseRootWorld;
            parts.Add(new CompositePartTemplate3D(
                part.Name,
                mesh,
                new MeshResourceKey(mesh.ResourceKey),
                materialSlot: parts.Count,
                localTransform: localToTemplate,
                baseColor: material.EffectiveColor,
                lightingMode: material.Lighting));
        }

        if (parts.Count == 0)
            throw new InvalidOperationException($"Composite '{source.Name}' contains no visible mesh parts.");
        if (!bakeDetailedMesh || parts.Count == 1) return new CompositeTemplate3D(id, source.Name, parts);

        var baked = BakeDetailedParts(id, source.Name, parts);
        var detailedPart = baked[0];
        return new CompositeTemplate3D(id, source.Name, new Dictionary<HighScaleLodLevel3D, IReadOnlyList<CompositePartTemplate3D>>
        {
            [HighScaleLodLevel3D.Detailed] = baked,
            [HighScaleLodLevel3D.Simplified] = BuildLodPart(detailedPart, source.Name + " Simplified", 0.35f),
            [HighScaleLodLevel3D.Proxy] = BuildLodPart(detailedPart, source.Name + " Proxy", 0.08f)
        });
    }

    private static IReadOnlyList<CompositePartTemplate3D> BakeDetailedParts(int templateId, string name, IReadOnlyList<CompositePartTemplate3D> parts)
    {
        var vertexCount = 0;
        var indexCount = 0;
        var maxSlot = 0;
        var hasTexCoords = true;
        var hasColors = true;
        var hasTangents = true;
        var hasSkinWeights = true;
        for (var i = 0; i < parts.Count; i++)
        {
            var mesh = parts[i].Mesh;
            vertexCount = checked(vertexCount + mesh.Positions.Length);
            indexCount = checked(indexCount + mesh.Indices.Length);
            maxSlot = global::System.Math.Max(maxSlot, parts[i].MaterialSlot);
            hasTexCoords &= mesh.HasTexCoords0;
            hasColors &= mesh.HasVertexColors0;
            hasTangents &= mesh.HasTangents;
            hasSkinWeights &= mesh.HasSkinWeights;
        }

        var streams = GeometryStreamMask3D.Normals | GeometryStreamMask3D.MaterialSlots;
        if (hasTexCoords) streams |= GeometryStreamMask3D.TexCoords0;
        if (hasColors) streams |= GeometryStreamMask3D.Colors0;
        if (hasTangents) streams |= GeometryStreamMask3D.Tangents;
        if (hasSkinWeights) streams |= GeometryStreamMask3D.SkinWeights;
        var builder = new MeshGeometryBuilder3D(vertexCount, indexCount, streams);
        var baseColors = new ColorRgba[maxSlot + 1];
        var hasBaseColor = new bool[maxSlot + 1];
        var vertexOffset = 0;
        var indexOffset = 0;
        var lighting = LightingMode.Unlit;

        for (var partIndex = 0; partIndex < parts.Count; partIndex++)
        {
            var part = parts[partIndex];
            if ((int)part.LightingMode > (int)lighting) lighting = part.LightingMode;
            var mesh = part.Mesh;
            var local = part.LocalTransform;
            var normalMatrix = GeometryTransform3D.CreateNormalMatrix(local);
            for (var i = 0; i < mesh.Positions.Length; i++)
            {
                var destination = vertexOffset + i;
                builder.Positions[destination] = Vector3.Transform(mesh.Positions[i], local);
                builder.Normals[destination] = GeometryTransform3D.TransformNormal(mesh.Normals[i], normalMatrix);
                builder.MaterialSlots[destination] = part.MaterialSlot;
                if (hasTexCoords) builder.TexCoords0[destination] = mesh.TexCoords0[i];
                if (hasColors) builder.Colors0[destination] = mesh.VertexColors0[i];
                if (hasTangents) builder.Tangents[destination] = GeometryTransform3D.TransformTangent(mesh.Tangents[i], normalMatrix);
                if (hasSkinWeights)
                {
                    builder.BoneIndices0[destination] = mesh.BoneIndices0[i];
                    builder.BoneWeights0[destination] = mesh.BoneWeights0[i];
                }
            }
            for (var i = 0; i < mesh.Indices.Length; i++) builder.Indices[indexOffset + i] = vertexOffset + mesh.Indices[i];
            baseColors[part.MaterialSlot] = part.BaseColor;
            hasBaseColor[part.MaterialSlot] = true;
            vertexOffset += mesh.Positions.Length;
            indexOffset += mesh.Indices.Length;
        }
        for (var i = 0; i < baseColors.Length; i++) if (!hasBaseColor[i]) baseColors[i] = ColorRgba.White;

        var resourceKey = $"baked:{templateId}:{name}:{parts.Count}:{vertexCount}:{indexCount}";
        var bakedMesh = builder.Build(resourceKey, materialSlotBaseColors: baseColors);
        return Array.AsReadOnly(new[]
        {
            new CompositePartTemplate3D(
                name + " BakedDetailed",
                bakedMesh,
                new MeshResourceKey(bakedMesh.ResourceKey),
                materialSlot: 0,
                localTransform: Matrix4x4.Identity,
                baseColor: ColorRgba.White,
                lightingMode: lighting,
                materialSlotBaseColors: baseColors)
        });
    }

    private static IReadOnlyList<CompositePartTemplate3D> BuildLodPart(CompositePartTemplate3D detailed, string name, float ratio)
    {
        var mesh = MeshLodGenerator3D.Generate(
            detailed.Mesh,
            ratio,
            detailed.Mesh.ResourceKey + $":lod:{ratio:0.##}");
        return Array.AsReadOnly(new[]
        {
            new CompositePartTemplate3D(
                name,
                mesh,
                new MeshResourceKey(mesh.ResourceKey),
                detailed.MaterialSlot,
                Matrix4x4.Identity,
                detailed.BaseColor,
                detailed.LightingMode,
                detailed.MaterialSlotBaseColors)
        });
    }
}
