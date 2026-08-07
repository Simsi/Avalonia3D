using ThreeDEngine.Core.Rendering.Rhi;

namespace ThreeDEngine.Core.Rendering;

/// <summary>Read-only application diagnostics exposed by a presenter-owned GPU device.</summary>
public interface IRenderDeviceDiagnostics3D
{
    RhiDeviceCapabilities3D Capabilities { get; }
    long FrameIndex { get; }
    long ValidationCount { get; }
    bool GpuTimingSupported { get; }
    double LastGpuFrameMilliseconds { get; }
    RhiResourceSnapshot3D CaptureResourceSnapshot();
}
