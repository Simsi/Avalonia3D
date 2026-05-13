using System.Collections.Generic;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Rendering.Shadows;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Backend-neutral frame render plan. It deliberately contains engine-level decisions
/// only; OpenGL/WebGL-specific buffers, JS interop and GL state remain in backends.
/// </summary>
public sealed class SceneRenderPlan3D
{
    internal SceneRenderPlan3D()
    {
        Frame = null!;
        Shadow = DirectionalShadowSnapshot3D.Disabled;
        OrdinaryBatches = new List<OrdinaryRenderBatch3D>();
        TransparentOrdinaryItems = new List<TransparentOrdinaryRenderItem3D>();
        TransparentOrdinaryBatches = new List<TransparentOrdinaryBatch3D>();
        ParticleItems = new List<ParticleRenderItem3D>();
        HighScaleLayers = new List<HighScaleInstanceLayer3D>();
        DrawCommands = new List<SceneRenderCommand3D>();
        ShadowCommands = new List<SceneRenderCommand3D>();
        Resources = new RenderResourcePlan3D();
    }

    internal SceneRenderPlan3D(
        SceneRenderFrameContext3D frame,
        DirectionalShadowSnapshot3D shadow,
        List<OrdinaryRenderBatch3D> ordinaryBatches,
        List<TransparentOrdinaryRenderItem3D> transparentOrdinaryItems,
        List<TransparentOrdinaryBatch3D> transparentOrdinaryBatches,
        List<ParticleRenderItem3D> particleItems,
        List<HighScaleInstanceLayer3D> highScaleLayers,
        List<SceneRenderCommand3D> drawCommands,
        List<SceneRenderCommand3D> shadowCommands,
        RenderResourcePlan3D resources,
        bool includesOrdinary,
        bool includesParticles,
        bool includesHighScale)
    {
        Frame = frame;
        Shadow = shadow;
        OrdinaryBatches = ordinaryBatches;
        TransparentOrdinaryItems = transparentOrdinaryItems;
        TransparentOrdinaryBatches = transparentOrdinaryBatches;
        ParticleItems = particleItems;
        HighScaleLayers = highScaleLayers;
        DrawCommands = drawCommands;
        ShadowCommands = shadowCommands;
        Resources = resources;
        IncludesOrdinary = includesOrdinary;
        IncludesParticles = includesParticles;
        IncludesHighScale = includesHighScale;
    }

    internal void Reset(
        SceneRenderFrameContext3D frame,
        DirectionalShadowSnapshot3D shadow,
        List<OrdinaryRenderBatch3D> ordinaryBatches,
        List<TransparentOrdinaryRenderItem3D> transparentOrdinaryItems,
        List<TransparentOrdinaryBatch3D> transparentOrdinaryBatches,
        List<ParticleRenderItem3D> particleItems,
        List<HighScaleInstanceLayer3D> highScaleLayers,
        List<SceneRenderCommand3D> drawCommands,
        List<SceneRenderCommand3D> shadowCommands,
        RenderResourcePlan3D resources,
        bool includesOrdinary,
        bool includesParticles,
        bool includesHighScale)
    {
        Frame = frame;
        Shadow = shadow;
        OrdinaryBatches = ordinaryBatches;
        TransparentOrdinaryItems = transparentOrdinaryItems;
        TransparentOrdinaryBatches = transparentOrdinaryBatches;
        ParticleItems = particleItems;
        HighScaleLayers = highScaleLayers;
        DrawCommands = drawCommands;
        ShadowCommands = shadowCommands;
        Resources = resources;
        IncludesOrdinary = includesOrdinary;
        IncludesParticles = includesParticles;
        IncludesHighScale = includesHighScale;
    }

    public SceneRenderFrameContext3D Frame { get; private set; }
    public DirectionalShadowSnapshot3D Shadow { get; private set; }

    /// <summary>Opaque ordinary batches. Transparent ordinary objects are intentionally not stored here.</summary>
    public List<OrdinaryRenderBatch3D> OrdinaryBatches { get; private set; }

    /// <summary>Object-level transparent ordinary queue, sorted by the Core pipeline.</summary>
    public List<TransparentOrdinaryRenderItem3D> TransparentOrdinaryItems { get; private set; }

    /// <summary>Approximate transparent ordinary batches used for large transparent scenes.</summary>
    public List<TransparentOrdinaryBatch3D> TransparentOrdinaryBatches { get; private set; }

    public List<ParticleRenderItem3D> ParticleItems { get; private set; }
    public List<HighScaleInstanceLayer3D> HighScaleLayers { get; private set; }

    /// <summary>Canonical cross-backend draw-command order for all non-overlay 3D work.</summary>
    public List<SceneRenderCommand3D> DrawCommands { get; private set; }

    /// <summary>Canonical backend-neutral caster stream for directional shadow passes.</summary>
    public List<SceneRenderCommand3D> ShadowCommands { get; private set; }

    /// <summary>Exact frame resource upload/liveness plan derived from this render plan.</summary>
    public RenderResourcePlan3D Resources { get; private set; }

    public bool IncludesOrdinary { get; private set; }
    public bool IncludesParticles { get; private set; }
    public bool IncludesHighScale { get; private set; }

    public bool HasVisibleOrdinary => OrdinaryBatches.Count != 0 || TransparentOrdinaryItems.Count != 0 || TransparentOrdinaryBatches.Count != 0;
    public bool HasVisibleOpaqueOrdinary => OrdinaryBatches.Count != 0;
    public bool HasVisibleTransparentOrdinary => TransparentOrdinaryItems.Count != 0 || TransparentOrdinaryBatches.Count != 0;
    public bool HasVisibleParticles => ParticleItems.Count != 0;
    public bool HasVisibleHighScale => HighScaleLayers.Count != 0;
}
