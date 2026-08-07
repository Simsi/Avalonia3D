using System.Numerics;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Object-level transparent ordinary item. Opaque ordinary objects stay batch-grouped;
/// transparent ordinary objects are kept as independent ordered commands so desktop and
/// browser renderers do not maintain separate, approximate batch-level transparency logic.
/// </summary>
internal readonly struct TransparentOrdinaryRenderItem3D
{
    public TransparentOrdinaryRenderItem3D(OrdinaryRenderItem3D item, float sortDistanceSquared, int sourceOrder, string drawId)
    {
        Item = item;
        SortDistanceSquared = sortDistanceSquared;
        SourceOrder = sourceOrder;
        DrawId = drawId ?? throw new System.ArgumentNullException(nameof(drawId));
    }

    public OrdinaryRenderItem3D Item { get; }
    public float SortDistanceSquared { get; }
    public int SourceOrder { get; }
    public string DrawId { get; }

    public static TransparentOrdinaryRenderItem3D FromItem(
        OrdinaryRenderItem3D item,
        Vector3 cameraPosition,
        int sourceOrder,
        SceneRenderPlanScratch3D scratch)
    {
        var worldCenter = item.Mesh.LocalBounds.IsValid
            ? Vector3.Transform(item.Mesh.LocalBounds.Center, item.Model)
            : new Vector3(item.Model.M41, item.Model.M42, item.Model.M43);
        if (scratch is null) throw new System.ArgumentNullException(nameof(scratch));
        var drawId = scratch.GetTransparentDrawId(item.RetainedBatchId, item.Owner.Id);
        return new TransparentOrdinaryRenderItem3D(item, Vector3.DistanceSquared(cameraPosition, worldCenter), sourceOrder, drawId);
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
