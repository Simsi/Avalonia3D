using System;
using System.Collections.Generic;
using ThreeDEngine.Core.Collections;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Rendering.Rhi;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Backend-neutral retained frame plan. Public collection views cannot be cast
/// back to the mutable scratch lists used by the renderer.
/// </summary>
internal sealed class SceneRenderPlan3D
{
    private readonly ReadOnlyListView3D<OrdinaryRenderBatch3D> _ordinaryBatches = new();
    private readonly ReadOnlyListView3D<TransparentOrdinaryRenderItem3D> _transparentOrdinaryItems = new();
    private readonly ReadOnlyListView3D<TransparentOrdinaryBatch3D> _transparentOrdinaryBatches = new();
    private readonly ReadOnlyListView3D<ParticleRenderItem3D> _particleItems = new();
    private readonly ReadOnlyListView3D<HighScaleInstanceLayer3D> _highScaleLayers = new();
    private readonly ReadOnlyListView3D<SceneRenderCommand3D> _drawCommands = new();

    internal SceneRenderPlan3D()
    {
        Frame = null!;
        Resources = new RenderResourcePlan3D();
        RhiSubmission = new RhiFrameSubmission3D();
    }

    internal void Reset(
        SceneRenderFrameContext3D frame,
        List<OrdinaryRenderBatch3D> ordinaryBatches,
        List<TransparentOrdinaryRenderItem3D> transparentOrdinaryItems,
        List<TransparentOrdinaryBatch3D> transparentOrdinaryBatches,
        List<ParticleRenderItem3D> particleItems,
        List<HighScaleInstanceLayer3D> highScaleLayers,
        List<SceneRenderCommand3D> drawCommands,
        RenderResourcePlan3D resources,
        bool includesOrdinary,
        bool includesParticles,
        bool includesHighScale)
    {
        Frame = frame ?? throw new ArgumentNullException(nameof(frame));
        Resources = resources ?? throw new ArgumentNullException(nameof(resources));
        ResetViews(ordinaryBatches, transparentOrdinaryItems, transparentOrdinaryBatches, particleItems, highScaleLayers, drawCommands);
        IncludesOrdinary = includesOrdinary;
        IncludesParticles = includesParticles;
        IncludesHighScale = includesHighScale;
        RhiSubmission.Build(this);
    }

    private void ResetViews(
        IReadOnlyList<OrdinaryRenderBatch3D> ordinaryBatches,
        IReadOnlyList<TransparentOrdinaryRenderItem3D> transparentOrdinaryItems,
        IReadOnlyList<TransparentOrdinaryBatch3D> transparentOrdinaryBatches,
        IReadOnlyList<ParticleRenderItem3D> particleItems,
        IReadOnlyList<HighScaleInstanceLayer3D> highScaleLayers,
        IReadOnlyList<SceneRenderCommand3D> drawCommands)
    {
        _ordinaryBatches.Reset(ordinaryBatches);
        _transparentOrdinaryItems.Reset(transparentOrdinaryItems);
        _transparentOrdinaryBatches.Reset(transparentOrdinaryBatches);
        _particleItems.Reset(particleItems);
        _highScaleLayers.Reset(highScaleLayers);
        _drawCommands.Reset(drawCommands);
    }

    public SceneRenderFrameContext3D Frame { get; private set; }
    public IReadOnlyList<OrdinaryRenderBatch3D> OrdinaryBatches => _ordinaryBatches;
    public IReadOnlyList<TransparentOrdinaryRenderItem3D> TransparentOrdinaryItems => _transparentOrdinaryItems;
    public IReadOnlyList<TransparentOrdinaryBatch3D> TransparentOrdinaryBatches => _transparentOrdinaryBatches;
    public IReadOnlyList<ParticleRenderItem3D> ParticleItems => _particleItems;
    public IReadOnlyList<HighScaleInstanceLayer3D> HighScaleLayers => _highScaleLayers;
    public IReadOnlyList<SceneRenderCommand3D> DrawCommands => _drawCommands;
    public RenderResourcePlan3D Resources { get; private set; }
    public RhiFrameSubmission3D RhiSubmission { get; }
    public bool IncludesOrdinary { get; private set; }
    public bool IncludesParticles { get; private set; }
    public bool IncludesHighScale { get; private set; }
    public bool HasVisibleOrdinary => OrdinaryBatches.Count != 0 || TransparentOrdinaryItems.Count != 0 || TransparentOrdinaryBatches.Count != 0;
    public bool HasVisibleOpaqueOrdinary => OrdinaryBatches.Count != 0;
    public bool HasVisibleTransparentOrdinary => TransparentOrdinaryItems.Count != 0 || TransparentOrdinaryBatches.Count != 0;
    public bool HasVisibleParticles => ParticleItems.Count != 0;
    public bool HasVisibleHighScale => HighScaleLayers.Count != 0;
}
