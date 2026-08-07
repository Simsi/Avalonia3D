using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Numerics;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.Materials;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Backend-neutral ordinary render batch. The core owns grouping and draw ordering;
/// backends only translate batches into their retained/runtime buffer formats.
/// </summary>
internal sealed class OrdinaryRenderBatch3D
{
    private readonly List<OrdinaryRenderItem3D> _items = new(64);
    private readonly ReadOnlyCollection<OrdinaryRenderItem3D> _itemsView;

    public OrdinaryRenderBatch3D()
    {
        _itemsView = _items.AsReadOnly();
    }

    public string BatchId { get; private set; } = string.Empty;
    public string LogicalMeshBatchKey { get; private set; } = string.Empty;
    public Mesh3D Mesh { get; private set; } = Mesh3D.Empty;
    public MaterialBinding3D Material { get; private set; }
    public IReadOnlyList<OrdinaryRenderItem3D> Items => _itemsView;
    public bool Transparent { get; private set; }
    public float SortDistanceSquared { get; private set; }

    public void Reset(string batchId, string logicalMeshBatchKey, Mesh3D mesh, MaterialBinding3D material)
    {
        BatchId = batchId ?? string.Empty;
        LogicalMeshBatchKey = logicalMeshBatchKey ?? string.Empty;
        Mesh = mesh;
        Material = material;
        Transparent = false;
        SortDistanceSquared = 0f;
        _items.Clear();
    }

    public void Add(OrdinaryRenderItem3D item, Vector3 cameraPosition)
    {
        _items.Add(item);
        Transparent |= item.Transparent;
        var worldCenter = item.Mesh.LocalBounds.IsValid
            ? Vector3.Transform(item.Mesh.LocalBounds.Center, item.Model)
            : new Vector3(item.Model.M41, item.Model.M42, item.Model.M43);
        var distanceSquared = Vector3.DistanceSquared(cameraPosition, worldCenter);
        if (distanceSquared > SortDistanceSquared)
        {
            SortDistanceSquared = distanceSquared;
        }
    }

    public static int CompareForDraw(OrdinaryRenderBatch3D? a, OrdinaryRenderBatch3D? b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a is null) return 1;
        if (b is null) return -1;
        if (a.Transparent != b.Transparent) return a.Transparent ? 1 : -1;
        if (a.Transparent)
        {
            var transparentCompare = b.SortDistanceSquared.CompareTo(a.SortDistanceSquared);
            if (transparentCompare != 0) return transparentCompare;
        }
        else
        {
            var materialCompare = string.CompareOrdinal(a.Material.Key, b.Material.Key);
            if (materialCompare != 0) return materialCompare;
        }

        var meshCompare = string.CompareOrdinal(a.Mesh.ResourceKey, b.Mesh.ResourceKey);
        if (meshCompare != 0) return meshCompare;
        return string.CompareOrdinal(a.BatchId, b.BatchId);
    }
}
