using System.Collections.Generic;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Rendering.Pipeline;

public static class RenderPipelinePlanner3D
{
    public static RenderPipelinePlan3D Plan(Scene3D scene, BackendKind backend)
    {
        var settings = scene.RenderPipeline;
        var deferredRequested = settings.EnableDeferredLighting || settings.Mode is RenderPipelineMode3D.Deferred or RenderPipelineMode3D.DeferredIfSupported;
        var hdrRequested = settings.EnableHdr || settings.ToneMapping.Enabled;
        var ssaoRequested = settings.Ssao.Enabled;
        var motionRequested = settings.EnableMotionVectorMetadata;

        // Stage 8 establishes the engine-level pipeline contract and enables forward HDR tone mapping.
        // Full G-buffer/deferred/SSAO render targets are intentionally capability-gated until the
        // renderers are migrated to a multi-render-target pass graph.
        var deferredActive = false;
        var ssaoActive = false;
        var motionActive = false;
        var hdrActive = hdrRequested;
        var activeMode = deferredActive ? RenderPipelineMode3D.Deferred : RenderPipelineMode3D.Forward;
        var reason = deferredRequested
            ? backend == BackendKind.WebGlBrowser
                ? "deferred-fallback-webgl-mrt-not-enabled"
                : "deferred-fallback-forward-tonemap-stage8"
            : "forward";

        var passes = new List<RenderPassDescriptor3D>
        {
            new RenderPassDescriptor3D
            {
                Kind = RenderPassKind3D.ForwardOpaque,
                Name = "Forward Opaque"
            }
        };

        if (ssaoRequested)
        {
            passes.Add(new RenderPassDescriptor3D
            {
                Kind = RenderPassKind3D.Ssao,
                Name = ssaoActive ? "SSAO" : "SSAO (fallback metadata)"
            });
        }

        if (hdrActive || settings.ToneMapping.Enabled)
        {
            passes.Add(new RenderPassDescriptor3D
            {
                Kind = RenderPassKind3D.HdrToneMapping,
                Name = "Forward HDR Tone Mapping"
            });
        }

        if (settings.EnableTransparentForwardPass)
        {
            passes.Add(new RenderPassDescriptor3D
            {
                Kind = RenderPassKind3D.TransparentForward,
                Name = "Transparent Forward"
            });
        }

        passes.Add(new RenderPassDescriptor3D
        {
            Kind = RenderPassKind3D.Overlay,
            Name = "Overlay"
        });

        return new RenderPipelinePlan3D
        {
            RequestedMode = settings.Mode,
            ActiveMode = activeMode,
            DeferredRequested = deferredRequested,
            DeferredActive = deferredActive,
            GBufferActive = deferredActive,
            SsaoRequested = ssaoRequested,
            SsaoActive = ssaoActive,
            HdrRequested = hdrRequested,
            HdrActive = hdrActive,
            ToneMappingMode = settings.ToneMapping.Enabled ? settings.ToneMapping.Mode : ToneMappingMode3D.None,
            ToneMappingActive = settings.ToneMapping.Enabled,
            MotionVectorsRequested = motionRequested,
            MotionVectorsActive = motionActive,
            Reason = reason,
            Passes = passes
        };
    }
}
