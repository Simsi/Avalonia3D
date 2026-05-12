using ThreeDEngine.Core.HighScale;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Backend-neutral draw command. Backends translate these commands into API-specific
/// buffer uploads and draw calls; Core owns ordering and render-category decisions.
/// </summary>
public sealed class SceneRenderCommand3D
{
    private SceneRenderCommand3D(
        SceneRenderCommandKind3D kind,
        string id,
        bool transparent,
        float sortDistanceSquared,
        int sourceOrder,
        OrdinaryRenderBatch3D? ordinaryBatch = null,
        TransparentOrdinaryRenderItem3D? transparentOrdinary = null,
        TransparentOrdinaryBatch3D? transparentOrdinaryBatch = null,
        ParticleRenderItem3D? particle = null,
        HighScaleInstanceLayer3D? highScaleLayer = null)
    {
        Kind = kind;
        Id = id ?? string.Empty;
        Transparent = transparent;
        SortDistanceSquared = sortDistanceSquared;
        SourceOrder = sourceOrder;
        OrdinaryBatch = ordinaryBatch;
        TransparentOrdinary = transparentOrdinary;
        TransparentOrdinaryBatch = transparentOrdinaryBatch;
        Particle = particle;
        HighScaleLayer = highScaleLayer;
    }

    public SceneRenderCommandKind3D Kind { get; }
    public string Id { get; }
    public bool Transparent { get; }
    public float SortDistanceSquared { get; }
    public int SourceOrder { get; }
    public OrdinaryRenderBatch3D? OrdinaryBatch { get; }
    public TransparentOrdinaryRenderItem3D? TransparentOrdinary { get; }
    public TransparentOrdinaryBatch3D? TransparentOrdinaryBatch { get; }
    public ParticleRenderItem3D? Particle { get; }
    public HighScaleInstanceLayer3D? HighScaleLayer { get; }

    public static SceneRenderCommand3D ForOrdinaryBatch(OrdinaryRenderBatch3D batch, int sourceOrder)
        => new(
            SceneRenderCommandKind3D.OrdinaryBatch,
            batch.BatchId,
            transparent: false,
            sortDistanceSquared: batch.SortDistanceSquared,
            sourceOrder,
            ordinaryBatch: batch);

    public static SceneRenderCommand3D ForTransparentOrdinary(TransparentOrdinaryRenderItem3D item)
        => new(
            SceneRenderCommandKind3D.TransparentOrdinaryItem,
            item.DrawId,
            transparent: true,
            sortDistanceSquared: item.SortDistanceSquared,
            sourceOrder: item.SourceOrder,
            transparentOrdinary: item);

    public static SceneRenderCommand3D ForTransparentOrdinaryBatch(TransparentOrdinaryBatch3D batch)
        => new(
            SceneRenderCommandKind3D.TransparentOrdinaryBatch,
            batch.BatchId,
            transparent: true,
            sortDistanceSquared: batch.SortDistanceSquared,
            sourceOrder: batch.SourceOrder,
            transparentOrdinaryBatch: batch);

    public static SceneRenderCommand3D ForParticle(ParticleRenderItem3D item, int sourceOrder)
        => new(
            SceneRenderCommandKind3D.ParticleSystem,
            item.RetainedBatchId,
            item.Transparent,
            item.SortDistanceSquared,
            sourceOrder,
            particle: item);

    public static SceneRenderCommand3D ForHighScaleLayer(HighScaleInstanceLayer3D layer, int sourceOrder)
        => new(
            SceneRenderCommandKind3D.HighScaleLayer,
            layer.Id,
            transparent: false,
            sortDistanceSquared: 0f,
            sourceOrder,
            highScaleLayer: layer);

    public static int CompareForDraw(SceneRenderCommand3D? a, SceneRenderCommand3D? b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a is null) return 1;
        if (b is null) return -1;
        return SceneRenderDrawOrder3D.Compare(
            a.Transparent,
            a.SortDistanceSquared,
            a.SourceOrder,
            a.Id,
            b.Transparent,
            b.SortDistanceSquared,
            b.SourceOrder,
            b.Id);
    }
}
