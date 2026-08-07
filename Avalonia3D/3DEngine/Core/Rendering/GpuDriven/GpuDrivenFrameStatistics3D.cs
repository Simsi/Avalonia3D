namespace ThreeDEngine.Core.Rendering.GpuDriven;

/// <summary>Last submitted GPU-driven frame counters. Values describe encoded GPU work, not CPU estimates.</summary>
public readonly record struct GpuDrivenFrameStatistics3D(
    long FrameIndex,
    int ObjectCount,
    int MeshCount,
    int MaterialCount,
    int MeshletCount,
    int ParticleCapacity,
    int ComputePassCount,
    int RenderPassCount,
    int BarrierCount,
    int IndirectCommandCapacity,
    int UploadedBytes,
    int RenderGraphPhysicalResources,
    int RenderGraphAliasedResources,
    bool OcclusionCullingEnabled,
    bool GpuParticlesEnabled,
    bool ClusteredLightingEnabled,
    double GpuMilliseconds);
