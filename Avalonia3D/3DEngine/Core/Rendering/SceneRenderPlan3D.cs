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

    public SceneRenderFrameContext3D Frame { get; }
    public DirectionalShadowSnapshot3D Shadow { get; }

    /// <summary>Opaque ordinary batches. Transparent ordinary objects are intentionally not stored here.</summary>
    public List<OrdinaryRenderBatch3D> OrdinaryBatches { get; }

    /// <summary>Object-level transparent ordinary queue, sorted by the Core pipeline.</summary>
    public List<TransparentOrdinaryRenderItem3D> TransparentOrdinaryItems { get; }

    /// <summary>Approximate transparent ordinary batches used for large transparent scenes.</summary>
    public List<TransparentOrdinaryBatch3D> TransparentOrdinaryBatches { get; }

    public List<ParticleRenderItem3D> ParticleItems { get; }
    public List<HighScaleInstanceLayer3D> HighScaleLayers { get; }

    /// <summary>Canonical cross-backend draw-command order for all non-overlay 3D work.</summary>
    public List<SceneRenderCommand3D> DrawCommands { get; }

    /// <summary>Canonical backend-neutral caster stream for directional shadow passes.</summary>
    public List<SceneRenderCommand3D> ShadowCommands { get; }

    /// <summary>Exact frame resource upload/liveness plan derived from this render plan.</summary>
    public RenderResourcePlan3D Resources { get; }

    public bool IncludesOrdinary { get; }
    public bool IncludesParticles { get; }
    public bool IncludesHighScale { get; }

    public bool HasVisibleOrdinary => OrdinaryBatches.Count != 0 || TransparentOrdinaryItems.Count != 0 || TransparentOrdinaryBatches.Count != 0;
    public bool HasVisibleOpaqueOrdinary => OrdinaryBatches.Count != 0;
    public bool HasVisibleTransparentOrdinary => TransparentOrdinaryItems.Count != 0 || TransparentOrdinaryBatches.Count != 0;
    public bool HasVisibleParticles => ParticleItems.Count != 0;
    public bool HasVisibleHighScale => HighScaleLayers.Count != 0;
}
