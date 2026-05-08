using ThreeDEngine.Core.Rendering;

namespace ThreeDEngine.Core.Throughput;

public sealed class RenderThroughputCapabilities3D
{
    public BackendKind Backend { get; init; }
    public bool SupportsInstancing { get; init; }
    public bool SupportsHighScaleRetainedBuffers { get; init; }
    public bool SupportsGpuParticles { get; init; }
    public bool SupportsIndirectDraw { get; init; }
    public bool SupportsBindlessTextures { get; init; }
    public bool SupportsVertexPulling { get; init; }
    public string Notes { get; init; } = string.Empty;

    public static RenderThroughputCapabilities3D ForBackend(BackendKind backend)
    {
        return backend switch
        {
            BackendKind.OpenGlDesktop => new RenderThroughputCapabilities3D
            {
                Backend = backend,
                SupportsInstancing = true,
                SupportsHighScaleRetainedBuffers = true,
                SupportsGpuParticles = false,
                SupportsIndirectDraw = false,
                SupportsBindlessTextures = false,
                SupportsVertexPulling = false,
                Notes = "OpenGL backend uses instanced batches and retained high-scale buffers. Compute/indirect/bindless paths are capability-gated future extensions."
            },
            BackendKind.WebGlBrowser => new RenderThroughputCapabilities3D
            {
                Backend = backend,
                SupportsInstancing = true,
                SupportsHighScaleRetainedBuffers = true,
                SupportsGpuParticles = false,
                SupportsIndirectDraw = false,
                SupportsBindlessTextures = false,
                SupportsVertexPulling = false,
                Notes = "WebGL backend uses packet/retained high-scale fallbacks; compute and indirect rendering are not assumed."
            },
            _ => new RenderThroughputCapabilities3D
            {
                Backend = backend,
                SupportsInstancing = false,
                SupportsHighScaleRetainedBuffers = false,
                SupportsGpuParticles = false,
                SupportsIndirectDraw = false,
                SupportsBindlessTextures = false,
                SupportsVertexPulling = false,
                Notes = "Software/unknown backend uses CPU mesh fallback."
            }
        };
    }
}
