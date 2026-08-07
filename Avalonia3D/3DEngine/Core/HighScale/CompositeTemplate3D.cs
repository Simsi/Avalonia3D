using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Numerics;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.HighScale;

public sealed class CompositeTemplate3D
{
    private readonly Dictionary<HighScaleLodLevel3D, IReadOnlyList<CompositePartTemplate3D>> _partsByLod;
    private readonly Dictionary<int, HighScaleMaterialVariant3D> _materialVariants = new();
    private readonly ReadOnlyDictionary<int, HighScaleMaterialVariant3D> _materialVariantsView;
    private int _materialVersion;

    public CompositeTemplate3D(int id, string name, IReadOnlyList<CompositePartTemplate3D> parts)
        : this(id, name, new Dictionary<HighScaleLodLevel3D, IReadOnlyList<CompositePartTemplate3D>>
        {
            [HighScaleLodLevel3D.Detailed] = CopyParts(parts, nameof(parts))
        })
    {
    }

    public CompositeTemplate3D(int id, string name, Dictionary<HighScaleLodLevel3D, IReadOnlyList<CompositePartTemplate3D>> partsByLod)
    {
        Id = Guard3D.NonNegative(id, nameof(id));
        Name = Guard3D.RequiredText(name, nameof(name));
        if (partsByLod is null) throw new ArgumentNullException(nameof(partsByLod));

        _partsByLod = new Dictionary<HighScaleLodLevel3D, IReadOnlyList<CompositePartTemplate3D>>(partsByLod.Count);
        foreach (var pair in partsByLod)
        {
            Guard3D.Defined(pair.Key, nameof(partsByLod));
            _partsByLod.Add(pair.Key, CopyParts(pair.Value, nameof(partsByLod)));
        }

        if (!_partsByLod.TryGetValue(HighScaleLodLevel3D.Detailed, out var detailed) || detailed.Count == 0)
            throw new ArgumentException("A composite template requires at least one detailed part.", nameof(partsByLod));

        Parts = detailed;
        LocalBounds = ComputeBounds(Parts);
        if (!LocalBounds.IsValid) throw new ArgumentException("Composite template parts must produce valid local bounds.", nameof(partsByLod));

        _materialVariantsView = new ReadOnlyDictionary<int, HighScaleMaterialVariant3D>(_materialVariants);
        AddVariantCore(new HighScaleMaterialVariant3D(0, "Default"));
    }

    public event EventHandler? MaterialVariantsChanged;

    public int Id { get; }
    public string Name { get; }
    public IReadOnlyList<CompositePartTemplate3D> Parts { get; }
    public Bounds3D LocalBounds { get; }
    public int MaterialVersion => _materialVersion;
    public IReadOnlyDictionary<int, HighScaleMaterialVariant3D> MaterialVariants => _materialVariantsView;

    public IReadOnlyList<CompositePartTemplate3D> ResolveParts(HighScaleLodLevel3D lod)
    {
        Guard3D.Defined(lod, nameof(lod));
        if (_partsByLod.TryGetValue(lod, out var parts) && parts.Count > 0) return parts;
        if (lod == HighScaleLodLevel3D.Billboard && _partsByLod.TryGetValue(HighScaleLodLevel3D.Proxy, out var proxyParts)) return proxyParts;
        if (_partsByLod.TryGetValue(HighScaleLodLevel3D.Simplified, out var simplified) && simplified.Count > 0) return simplified;
        return Parts;
    }

    public HighScaleMaterialVariant3D AddMaterialVariant(int id, string name)
    {
        id = Guard3D.NonNegative(id, nameof(id));
        if (_materialVariants.ContainsKey(id)) throw new ArgumentException($"Material variant ID {id} is already registered.", nameof(id));
        var variant = new HighScaleMaterialVariant3D(id, name);
        AddVariantCore(variant);
        NotifyMaterialChanged();
        return variant;
    }

    public bool RemoveMaterialVariant(int id)
    {
        if (id == 0) throw new InvalidOperationException("The default material variant cannot be removed.");
        if (!_materialVariants.Remove(id, out var variant)) return false;
        variant.Changed -= OnVariantChanged;
        NotifyMaterialChanged();
        return true;
    }

    public ColorRgba ResolveColor(CompositePartTemplate3D part, int materialVariantId)
    {
        if (part is null) throw new ArgumentNullException(nameof(part));
        return _materialVariants.TryGetValue(materialVariantId, out var variant) ? variant.Resolve(part) : part.BaseColor;
    }

    public ColorRgba ResolveColor(int materialSlot, ColorRgba baseColor, int materialVariantId)
        => _materialVariants.TryGetValue(materialVariantId, out var variant) ? variant.Resolve(materialSlot, baseColor) : baseColor;

    private void AddVariantCore(HighScaleMaterialVariant3D variant)
    {
        _materialVariants.Add(variant.Id, variant);
        variant.Changed += OnVariantChanged;
    }

    private void OnVariantChanged(object? sender, EventArgs e) => NotifyMaterialChanged();

    private void NotifyMaterialChanged()
    {
        unchecked { _materialVersion++; }
        MaterialVariantsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static IReadOnlyList<CompositePartTemplate3D> CopyParts(IReadOnlyList<CompositePartTemplate3D>? parts, string name)
    {
        if (parts is null) throw new ArgumentNullException(name);
        var copy = new CompositePartTemplate3D[parts.Count];
        for (var i = 0; i < copy.Length; i++) copy[i] = parts[i] ?? throw new ArgumentException("Composite part collections cannot contain null.", name);
        return Array.AsReadOnly(copy);
    }

    private static Bounds3D ComputeBounds(IReadOnlyList<CompositePartTemplate3D> parts)
    {
        var bounds = Bounds3D.Empty;
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            if (part.Mesh.LocalBounds.IsValid) bounds = bounds.Encapsulate(part.Mesh.LocalBounds.Transform(part.LocalTransform));
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
        LightingMode lightingMode,
        IReadOnlyList<ColorRgba>? materialSlotBaseColors = null)
    {
        Name = Guard3D.RequiredText(name, nameof(name));
        Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
        MeshKey = meshKey;
        MaterialSlot = Guard3D.NonNegative(materialSlot, nameof(materialSlot));
        LocalTransform = Guard3D.FiniteMatrix(localTransform, nameof(localTransform), requireInvertible: true);
        BaseColor = Guard3D.Color(baseColor, nameof(baseColor));
        LightingMode = Guard3D.Defined(lightingMode, nameof(lightingMode));
        if (materialSlotBaseColors is null || materialSlotBaseColors.Count == 0)
        {
            MaterialSlotBaseColors = Array.Empty<ColorRgba>();
        }
        else
        {
            var copy = new ColorRgba[materialSlotBaseColors.Count];
            for (var i = 0; i < copy.Length; i++) copy[i] = Guard3D.Color(materialSlotBaseColors[i], nameof(materialSlotBaseColors));
            MaterialSlotBaseColors = Array.AsReadOnly(copy);
        }
    }

    public string Name { get; }
    public Mesh3D Mesh { get; }
    public MeshResourceKey MeshKey { get; }
    public int MaterialSlot { get; }
    public Matrix4x4 LocalTransform { get; }
    public ColorRgba BaseColor { get; }
    public LightingMode LightingMode { get; }
    public IReadOnlyList<ColorRgba> MaterialSlotBaseColors { get; }
    public bool UsesVertexMaterialSlots => Mesh.HasMaterialSlots && MaterialSlotBaseColors.Count > 0;
}
