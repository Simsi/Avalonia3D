using System.Collections.Generic;

namespace ThreeDEngine.Core.Rendering.Pipeline;

public sealed class RenderPipelinePlan3D
{
    public RenderPipelineMode3D RequestedMode { get; init; }
    public RenderPipelineMode3D ActiveMode { get; init; }
    public bool DeferredRequested { get; init; }
    public bool DeferredActive { get; init; }
    public bool GBufferActive { get; init; }
    public bool SsaoRequested { get; init; }
    public bool SsaoActive { get; init; }
    public bool HdrRequested { get; init; }
    public bool HdrActive { get; init; }
    public ToneMappingMode3D ToneMappingMode { get; init; }
    public bool ToneMappingActive { get; init; }
    public bool MotionVectorsRequested { get; init; }
    public bool MotionVectorsActive { get; init; }
    public string Reason { get; init; } = string.Empty;
    public IReadOnlyList<RenderPassDescriptor3D> Passes { get; init; } = new List<RenderPassDescriptor3D>();
}
