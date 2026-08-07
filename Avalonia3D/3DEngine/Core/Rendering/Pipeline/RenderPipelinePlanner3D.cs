using System;
using System.Collections.Generic;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Rendering.Pipeline;

internal static class RenderPipelinePlanner3D
{
    public static RenderPipelinePlan3D Plan(Scene3D scene, BackendKind backend)
    {
        var settings = scene.RenderPipeline;
        var cached = settings.GetCachedPlan(backend);
        if (cached is not null) return cached;
        if (settings.Mode is not RenderPipelineMode3D.Forward and not RenderPipelineMode3D.Deferred)
        {
            throw new ArgumentOutOfRangeException(nameof(settings.Mode), settings.Mode, "Unknown render pipeline mode.");
        }

        if (settings.ToneMapping.Enabled && settings.ToneMapping.Mode == ToneMappingMode3D.None)
        {
            throw new InvalidOperationException("Tone mapping cannot be enabled with ToneMappingMode3D.None.");
        }

        if (settings.ToneMapping.Mode is < ToneMappingMode3D.None or > ToneMappingMode3D.AcesApproximation)
        {
            throw new ArgumentOutOfRangeException(nameof(settings.ToneMapping.Mode), settings.ToneMapping.Mode, "Unknown tone-mapping mode.");
        }

        var deferredRequested = settings.EnableDeferredLighting || settings.Mode == RenderPipelineMode3D.Deferred;
        var hdrRequested = settings.EnableHdr;
        var ssaoRequested = settings.Ssao.Enabled;
        var motionRequested = settings.EnableMotionVectorMetadata;

        RejectUnsupported(deferredRequested, "deferred lighting/G-buffer", backend);
        RejectUnsupported(ssaoRequested, "SSAO", backend);
        RejectUnsupported(hdrRequested, "HDR render targets", backend);
        RejectUnsupported(motionRequested, "motion-vector targets", backend);

        var passes = new List<RenderPassDescriptor3D>
        {
            new RenderPassDescriptor3D
            {
                Kind = RenderPassKind3D.ForwardOpaque,
                Name = "Forward Opaque"
            }
        };

        if (settings.ToneMapping.Enabled)
        {
            passes.Add(new RenderPassDescriptor3D
            {
                Kind = RenderPassKind3D.HdrToneMapping,
                Name = "Forward Tone Mapping"
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

        var plan = new RenderPipelinePlan3D
        {
            RequestedMode = settings.Mode,
            ActiveMode = RenderPipelineMode3D.Forward,
            DeferredRequested = deferredRequested,
            DeferredActive = false,
            GBufferActive = false,
            SsaoRequested = ssaoRequested,
            SsaoActive = false,
            HdrRequested = hdrRequested,
            HdrActive = false,
            ToneMappingMode = settings.ToneMapping.Enabled ? settings.ToneMapping.Mode : ToneMappingMode3D.None,
            ToneMappingActive = settings.ToneMapping.Enabled,
            MotionVectorsRequested = motionRequested,
            MotionVectorsActive = false,
            Reason = "forward",
            Passes = passes.AsReadOnly()
        };
        settings.CachePlan(backend, plan);
        return plan;
    }

    private static void RejectUnsupported(bool requested, string feature, BackendKind backend)
    {
        if (!requested) return;
        throw new NotSupportedException(
            $"The {backend} renderer does not implement a complete GPU {feature} path. " +
            "The engine rejects the request instead of substituting a forward, metadata-only, or CPU fallback.");
    }
}
