using ThreeDEngine.Core.HighScale;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Backend-neutral draw command. Backends translate these commands into API-specific
/// buffer uploads and draw calls; Core owns ordering and render-category decisions.
/// </summary>
internal sealed class SceneRenderCommand3D
{
    internal SceneRenderCommand3D()
    {
        Id = string.Empty;
    }

    public SceneRenderCommandKind3D Kind { get; private set; }
    public string Id { get; private set; }
    public bool Transparent { get; private set; }
    public float SortDistanceSquared { get; private set; }
    public int SourceOrder { get; private set; }
    public OrdinaryRenderBatch3D? OrdinaryBatch { get; private set; }
    public TransparentOrdinaryRenderItem3D? TransparentOrdinary { get; private set; }
    public TransparentOrdinaryBatch3D? TransparentOrdinaryBatch { get; private set; }
    public ParticleRenderItem3D? Particle { get; private set; }
    public HighScaleInstanceLayer3D? HighScaleLayer { get; private set; }

    internal void Reset(
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
