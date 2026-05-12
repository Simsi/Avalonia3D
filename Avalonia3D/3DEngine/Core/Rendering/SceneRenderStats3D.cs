using System;
using ThreeDEngine.Core.Rendering.Pipeline;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Shared render-stat population helpers. Keep backend-neutral counters here so
/// desktop/browser presenters do not drift when pipeline fields are added.
/// </summary>
public static class SceneRenderStats3D
{
    public static void ApplyPipelineStats(RenderStats stats, Scene3D scene, RenderPipelinePlan3D pipeline)
    {
        if (stats is null) throw new ArgumentNullException(nameof(stats));
        if (scene is null) throw new ArgumentNullException(nameof(scene));
        if (pipeline is null) throw new ArgumentNullException(nameof(pipeline));

        stats.RenderPipelineMode = (int)pipeline.ActiveMode;
        stats.DeferredRequested = pipeline.DeferredRequested;
        stats.DeferredActive = pipeline.DeferredActive;
        stats.GBufferActive = pipeline.GBufferActive;
        stats.GBufferTargetCount = pipeline.GBufferActive ? 4 : 0;
        stats.SsaoRequested = pipeline.SsaoRequested;
        stats.SsaoActive = pipeline.SsaoActive;
        stats.SsaoSampleCount = scene.RenderPipeline.Ssao.SampleCount;
        stats.HdrRequested = pipeline.HdrRequested;
        stats.HdrActive = pipeline.HdrActive;
        stats.ToneMappingMode = (int)pipeline.ToneMappingMode;
        stats.ToneMappingActive = pipeline.ToneMappingActive;
        stats.ToneMappingExposure = scene.RenderPipeline.ToneMapping.Exposure;
        stats.ToneMappingGamma = scene.RenderPipeline.ToneMapping.Gamma;
        stats.RenderPassCount = pipeline.Passes.Count;
        stats.MotionVectorsRequested = pipeline.MotionVectorsRequested;
        stats.MotionVectorsActive = pipeline.MotionVectorsActive;
        stats.RenderPipelineReason = pipeline.Reason;
    }
}
