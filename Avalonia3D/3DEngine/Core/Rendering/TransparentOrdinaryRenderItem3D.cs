using System.Numerics;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Object-level transparent ordinary item. Opaque ordinary objects stay batch-grouped;
/// transparent ordinary objects are kept as independent ordered commands so desktop and
/// browser renderers do not maintain separate, approximate batch-level transparency logic.
/// </summary>
public readonly struct TransparentOrdinaryRenderItem3D
{
    public TransparentOrdinaryRenderItem3D(OrdinaryRenderItem3D item, float sortDistanceSquared, int sourceOrder)
    {
        Item = item;
        SortDistanceSquared = sortDistanceSquared;
        SourceOrder = sourceOrder;
        DrawId = RenderId3D.StableHash(item.RetainedBatchId + ":transparent:" + item.Owner.Id + ":" + sourceOrder.ToString());
    }

    public OrdinaryRenderItem3D Item { get; }
    public float SortDistanceSquared { get; }
    public int SourceOrder { get; }
    public string DrawId { get; }

    public static TransparentOrdinaryRenderItem3D FromItem(OrdinaryRenderItem3D item, Vector3 cameraPosition, int sourceOrder)
    {
        var worldCenter = item.Mesh.LocalBounds.IsValid
            ? Vector3.Transform(item.Mesh.LocalBounds.Center, item.Model)
            : new Vector3(item.Model.M41, item.Model.M42, item.Model.M43);
        return new TransparentOrdinaryRenderItem3D(item, Vector3.DistanceSquared(cameraPosition, worldCenter), sourceOrder);
    }

    public static int CompareForDraw(TransparentOrdinaryRenderItem3D a, TransparentOrdinaryRenderItem3D b)
        => SceneRenderDrawOrder3D.Compare(
            true,
            a.SortDistanceSquared,
            a.SourceOrder,
            a.DrawId,
            true,
            b.SortDistanceSquared,
            b.SourceOrder,
            b.DrawId);
}
