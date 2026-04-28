using System;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.HighScale;

public static class HighScaleTemplateCompiler
{
    public static CompositeTemplate3D Compile(int id, CompositeObject3D source, bool bakeDetailedMesh = true)
    {
        var parts = new List<CompositePartTemplate3D>();
        var rootWorld = source.GetModelMatrix();
        if (!Matrix4x4.Invert(rootWorld, out var inverseRootWorld))
        {
            inverseRootWorld = Matrix4x4.Identity;
        }

        foreach (var part in source.EnumerateDescendants(includeSelf: false))
        {
            if (!part.UseMeshRendering || !part.IsVisible)
            {
                continue;
            }

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

        if (bakeDetailedMesh && parts.Count > 1)
        {
            var baked = BakeDetailedParts(id, source.Name, parts);
            return new CompositeTemplate3D(id, source.Name, new Dictionary<HighScaleLodLevel3D, IReadOnlyList<CompositePartTemplate3D>>
            {
                [HighScaleLodLevel3D.Detailed] = baked,
                [HighScaleLodLevel3D.Simplified] = BuildSimplifiedFromOriginal(source.Name, parts),
                [HighScaleLodLevel3D.Proxy] = BuildProxyFromOriginal(source.Name, parts)
            });
        }

        return new CompositeTemplate3D(id, source.Name, parts);
    }

    private static IReadOnlyList<CompositePartTemplate3D> BakeDetailedParts(int templateId, string name, IReadOnlyList<CompositePartTemplate3D> parts)
    {
        var vertexCount = 0;
        var indexCount = 0;
        var maxSlot = 0;
        for (var i = 0; i < parts.Count; i++)
        {
            vertexCount += parts[i].Mesh.Positions.Length;
            indexCount += parts[i].Mesh.Indices.Length;
            if (parts[i].MaterialSlot > maxSlot) maxSlot = parts[i].MaterialSlot;
        }

        var positions = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        var materialSlots = new float[vertexCount];
        var indices = new int[indexCount];
        var baseColors = new ColorRgba[maxSlot + 1];
        var hasBaseColor = new bool[maxSlot + 1];
        var vertexOffset = 0;
        var indexOffset = 0;
        var lighting = LightingMode.Unlit;

        for (var partIndex = 0; partIndex < parts.Count; partIndex++)
        {
            var part = parts[partIndex];
            if (part.LightingMode == LightingMode.Lambert) lighting = LightingMode.Lambert;

            var mesh = part.Mesh;
            var local = part.LocalTransform;
            for (var i = 0; i < mesh.Positions.Length; i++)
            {
                positions[vertexOffset + i] = Vector3.Transform(mesh.Positions[i], local);
                var normal = mesh.Normals.Length == mesh.Positions.Length ? mesh.Normals[i] : Vector3.UnitZ;
                normal = Vector3.TransformNormal(normal, local);
                normals[vertexOffset + i] = normal.LengthSquared() > 0.000001f ? Vector3.Normalize(normal) : Vector3.UnitY;
                materialSlots[vertexOffset + i] = part.MaterialSlot;
            }

            for (var i = 0; i < mesh.Indices.Length; i++)
            {
                indices[indexOffset + i] = vertexOffset + mesh.Indices[i];
            }

            baseColors[part.MaterialSlot] = part.BaseColor;
            hasBaseColor[part.MaterialSlot] = true;
            vertexOffset += mesh.Positions.Length;
            indexOffset += mesh.Indices.Length;
        }

        for (var i = 0; i < baseColors.Length; i++)
        {
            if (!hasBaseColor[i]) baseColors[i] = ColorRgba.White;
        }

        var resourceKey = $"baked:{templateId}:{name}:{parts.Count}:{vertexCount}:{indexCount}";
        var bakedMesh = new Mesh3D(positions, normals, indices, resourceKey, materialSlots, baseColors);
        return new[]
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
        };
    }

    private static IReadOnlyList<CompositePartTemplate3D> BuildSimplifiedFromOriginal(string name, IReadOnlyList<CompositePartTemplate3D> parts)
    {
        var bounds = ComputeBounds(parts);
        if (!bounds.IsValid) return parts;
        var size = bounds.Size;
        if (size.X <= 0f || size.Y <= 0f || size.Z <= 0f) return parts;
        var mesh = MeshFactory.CreateExtrudedRectangle(size.X, size.Y, size.Z);
        return new[]
        {
            new CompositePartTemplate3D(
                name + " Simplified",
                mesh,
                new MeshResourceKey(mesh.ResourceKey),
                materialSlot: 0,
                localTransform: Matrix4x4.CreateTranslation(bounds.Center),
                baseColor: new ColorRgba(0.40f, 0.46f, 0.54f, 1f),
                lightingMode: LightingMode.Lambert)
        };
    }

    private static IReadOnlyList<CompositePartTemplate3D> BuildProxyFromOriginal(string name, IReadOnlyList<CompositePartTemplate3D> parts)
    {
        var bounds = ComputeBounds(parts);
        if (!bounds.IsValid) return parts;
        var size = bounds.Size;
        if (size.X <= 0f || size.Y <= 0f || size.Z <= 0f) return parts;
        var mesh = MeshFactory.CreateExtrudedRectangle(size.X, size.Y, size.Z);
        return new[]
        {
            new CompositePartTemplate3D(
                name + " Proxy",
                mesh,
                new MeshResourceKey(mesh.ResourceKey),
                materialSlot: 0,
                localTransform: Matrix4x4.CreateTranslation(bounds.Center),
                baseColor: new ColorRgba(0.55f, 0.60f, 0.66f, 1f),
                lightingMode: LightingMode.Lambert)
        };
    }

    private static Bounds3D ComputeBounds(IReadOnlyList<CompositePartTemplate3D> parts)
    {
        var bounds = Bounds3D.Empty;
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            if (part.Mesh.LocalBounds.IsValid)
            {
                bounds = bounds.Encapsulate(part.Mesh.LocalBounds.Transform(part.LocalTransform));
            }
        }

        return bounds;
    }
}
