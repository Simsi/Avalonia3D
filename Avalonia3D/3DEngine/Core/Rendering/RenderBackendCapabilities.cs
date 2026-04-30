namespace ThreeDEngine.Core.Rendering;

public readonly record struct RenderBackendCapabilities(
    BackendKind Kind,
    bool SupportsRetainedResources,
    bool SupportsHighScaleRuntime,
    bool SupportsGpuInstancing,
    bool SupportsDebugDraw,
    bool SupportsTransparentSorting)
{
    public static RenderBackendCapabilities CpuFallback { get; } = new(
        BackendKind.Cpu,
        SupportsRetainedResources: false,
        SupportsHighScaleRuntime: false,
        SupportsGpuInstancing: false,
        SupportsDebugDraw: true,
        SupportsTransparentSorting: false);
}
