using System;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.HighScale;

public sealed class CompositeTemplate3D
{
    private readonly Dictionary<HighScaleLodLevel3D, IReadOnlyList<CompositePartTemplate3D>> _partsByLod;
    private readonly Dictionary<int, HighScaleMaterialVariant3D> _materialVariants = new();

    public CompositeTemplate3D(int id, string name, IReadOnlyList<CompositePartTemplate3D> parts)
        : this(id, name, new Dictionary<HighScaleLodLevel3D, IReadOnlyList<CompositePartTemplate3D>>
        {
            [HighScaleLodLevel3D.Detailed] = parts,
            [HighScaleLodLevel3D.Simplified] = parts,
            [HighScaleLodLevel3D.Proxy] = BuildProxyParts(name, parts)
        })
    {
    }

    public CompositeTemplate3D(int id, string name, Dictionary<HighScaleLodLevel3D, IReadOnlyList<CompositePartTemplate3D>> partsByLod)
    {
        Id = id;
        Name = name;
        _partsByLod = partsByLod;
        Parts = ResolveParts(HighScaleLodLevel3D.Detailed);
        LocalBounds = ComputeBounds(Parts);
        _materialVariants[0] = new HighScaleMaterialVariant3D(0, "Default");
    }

    public int Id { get; }
    public string Name { get; }
    public IReadOnlyList<CompositePartTemplate3D> Parts { get; }
    public Bounds3D LocalBounds { get; }
    public IReadOnlyDictionary<int, HighScaleMaterialVariant3D> MaterialVariants => _materialVariants;

    public IReadOnlyList<CompositePartTemplate3D> ResolveParts(HighScaleLodLevel3D lod)
    {
        if (_partsByLod.TryGetValue(lod, out var parts) && parts.Count > 0)
        {
            return parts;
        }

        if (lod == HighScaleLodLevel3D.Billboard && _partsByLod.TryGetValue(HighScaleLodLevel3D.Proxy, out var proxyParts))
        {
            return proxyParts;
        }

        if (_partsByLod.TryGetValue(HighScaleLodLevel3D.Simplified, out var simplified) && simplified.Count > 0)
        {
            return simplified;
        }

        return Parts;
    }

    public HighScaleMaterialVariant3D AddMaterialVariant(int id, string name)
    {
        var variant = new HighScaleMaterialVariant3D(id, name);
        _materialVariants[id] = variant;
        return variant;
    }

    public ColorRgba ResolveColor(CompositePartTemplate3D part, int materialVariantId)
    {
        if (_materialVariants.TryGetValue(materialVariantId, out var variant))
        {
            return variant.Resolve(part);
        }

        return part.BaseColor;
    }

    private static IReadOnlyList<CompositePartTemplate3D> BuildProxyParts(string name, IReadOnlyList<CompositePartTemplate3D> parts)
    {
        var bounds = ComputeBounds(parts);
        if (!bounds.IsValid)
        {
            return parts;
        }

        var size = bounds.Size;
        if (size.X <= 0f || size.Y <= 0f || size.Z <= 0f)
        {
            return parts;
        }

        var mesh = MeshFactory.CreateExtrudedRectangle(size.X, size.Y, size.Z);
        var center = bounds.Center;
        var local = Matrix4x4.CreateTranslation(center);
        return new[]
        {
            new CompositePartTemplate3D(
                name + " Proxy",
                mesh,
                new MeshResourceKey(mesh.ResourceKey),
                materialSlot: 0,
                localTransform: local,
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

public sealed class CompositePartTemplate3D
{
    public CompositePartTemplate3D(
        string name,
        Mesh3D mesh,
        MeshResourceKey meshKey,
        int materialSlot,
        Matrix4x4 localTransform,
        ColorRgba baseColor,
        LightingMode lightingMode)
    {
        Name = name;
        Mesh = mesh;
        MeshKey = meshKey;
        MaterialSlot = materialSlot;
        LocalTransform = localTransform;
        BaseColor = baseColor;
        LightingMode = lightingMode;
    }

    public string Name { get; }
    public Mesh3D Mesh { get; }
    public MeshResourceKey MeshKey { get; }
    public int MaterialSlot { get; }
    public Matrix4x4 LocalTransform { get; }
    public ColorRgba BaseColor { get; }
    public LightingMode LightingMode { get; }
}
