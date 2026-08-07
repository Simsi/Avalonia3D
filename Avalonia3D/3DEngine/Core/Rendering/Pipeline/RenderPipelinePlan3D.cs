using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Rendering.Pipeline;

internal sealed class RenderPipelinePlan3D
{
    private RenderPipelineMode3D _requestedMode;
    private RenderPipelineMode3D _activeMode;
    private ToneMappingMode3D _toneMappingMode;
    private IReadOnlyList<RenderPassDescriptor3D> _passes = Array.Empty<RenderPassDescriptor3D>();

    public RenderPipelineMode3D RequestedMode { get => _requestedMode; init => _requestedMode = Guard3D.Defined(value, nameof(RequestedMode)); }
    public RenderPipelineMode3D ActiveMode { get => _activeMode; init => _activeMode = Guard3D.Defined(value, nameof(ActiveMode)); }
    public bool DeferredRequested { get; init; }
    public bool DeferredActive { get; init; }
    public bool GBufferActive { get; init; }
    public bool SsaoRequested { get; init; }
    public bool SsaoActive { get; init; }
    public bool HdrRequested { get; init; }
    public bool HdrActive { get; init; }
    public ToneMappingMode3D ToneMappingMode { get => _toneMappingMode; init => _toneMappingMode = Guard3D.Defined(value, nameof(ToneMappingMode)); }
    public bool ToneMappingActive { get; init; }
    public bool MotionVectorsRequested { get; init; }
    public bool MotionVectorsActive { get; init; }
    public string Reason { get; init; } = string.Empty;

    public IReadOnlyList<RenderPassDescriptor3D> Passes
    {
        get => _passes;
        init
        {
            if (value is null) throw new ArgumentNullException(nameof(Passes));
            var array = value.ToArray();
            if (array.Any(static pass => pass is null))
            {
                throw new ArgumentException("Render pipeline passes cannot contain null entries.", nameof(Passes));
            }
            _passes = new ReadOnlyCollection<RenderPassDescriptor3D>(array);
        }
    }
}
