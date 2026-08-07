using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThreeDEngine.Core.Geometry;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Approximate transparent ordinary batch used when object-level transparency would create
/// too many draw calls. Items are grouped by depth bin and material/mesh while Core still
/// owns the command order. Small transparent scenes keep exact object-level sorting.
/// </summary>
internal sealed class TransparentOrdinaryBatch3D
{
    private readonly List<OrdinaryRenderItem3D> _items = new(16);
    private readonly ReadOnlyCollection<OrdinaryRenderItem3D> _itemsView;

    public TransparentOrdinaryBatch3D()
    {
        _itemsView = _items.AsReadOnly();
    }

    public string BatchId { get; private set; } = string.Empty;
    public string LogicalMeshBatchKey { get; private set; } = string.Empty;
    public Mesh3D Mesh { get; private set; } = Mesh3D.Empty;
    public MaterialBinding3D Material { get; private set; }
    public IReadOnlyList<OrdinaryRenderItem3D> Items => _itemsView;
    public float SortDistanceSquared { get; private set; }
    public int SourceOrder { get; private set; }
    public int DepthBin { get; private set; }

    public void Reset(
        string batchId,
        string logicalMeshBatchKey,
        Mesh3D mesh,
        MaterialBinding3D material,
        int sourceOrder,
        int depthBin)
    {
        BatchId = batchId ?? string.Empty;
        LogicalMeshBatchKey = logicalMeshBatchKey ?? string.Empty;
        Mesh = mesh;
        Material = material;
        SortDistanceSquared = 0f;
        SourceOrder = sourceOrder;
        DepthBin = depthBin;
        _items.Clear();
    }

    public void Add(TransparentOrdinaryRenderItem3D transparent)
    {
        _items.Add(transparent.Item);
        if (transparent.SortDistanceSquared > SortDistanceSquared)
        {
            SortDistanceSquared = transparent.SortDistanceSquared;
        }
    }

    public static int CompareForDraw(TransparentOrdinaryBatch3D? a, TransparentOrdinaryBatch3D? b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a is null) return 1;
        if (b is null) return -1;
        return SceneRenderDrawOrder3D.Compare(
            true,
            a.SortDistanceSquared,
            a.SourceOrder,
            a.BatchId,
            true,
            b.SortDistanceSquared,
            b.SourceOrder,
            b.BatchId);
    }
}
