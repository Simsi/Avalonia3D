using System;

namespace ThreeDEngine.Core.Rendering.GpuDriven;

/// <summary>
/// Immutable capacity and quality contract for the GPU-driven renderer. Capacity exhaustion is
/// reported explicitly; the renderer never drops objects, lowers quality or switches to CPU
/// visibility/particle simulation.
/// </summary>
public sealed class GpuDrivenRenderSettings3D
{
    public static GpuDrivenRenderSettings3D Default { get; } = new();

    public GpuDrivenRenderSettings3D(
        int maximumObjects = 1_000_000,
        int maximumMeshes = 65_536,
        int maximumMeshlets = 4_000_000,
        int maximumMaterials = 65_536,
        int maximumIndirectCommands = 1_000_000,
        int maximumParticles = 4_000_000,
        int cullingWorkgroupSize = 128,
        int particleWorkgroupSize = 128,
        int clusterCountX = 16,
        int clusterCountY = 9,
        int clusterCountZ = 24,
        bool enableOcclusionCulling = false,
        bool enableMeshletConeCulling = true,
        bool enableGpuParticles = true,
        bool enableClusteredLighting = true,
        bool enableHdr = true,
        int maximumLightsPerCluster = 128)
    {
        MaximumObjects = Positive(maximumObjects, nameof(maximumObjects));
        MaximumMeshes = Positive(maximumMeshes, nameof(maximumMeshes));
        MaximumMeshlets = Positive(maximumMeshlets, nameof(maximumMeshlets));
        MaximumMaterials = Positive(maximumMaterials, nameof(maximumMaterials));
        MaximumIndirectCommands = Positive(maximumIndirectCommands, nameof(maximumIndirectCommands));
        MaximumParticles = Positive(maximumParticles, nameof(maximumParticles));
        CullingWorkgroupSize = FixedWorkgroup(cullingWorkgroupSize, 128, nameof(cullingWorkgroupSize));
        ParticleWorkgroupSize = FixedWorkgroup(particleWorkgroupSize, 128, nameof(particleWorkgroupSize));
        ClusterCountX = Positive(clusterCountX, nameof(clusterCountX));
        ClusterCountY = Positive(clusterCountY, nameof(clusterCountY));
        ClusterCountZ = Positive(clusterCountZ, nameof(clusterCountZ));
        EnableOcclusionCulling = enableOcclusionCulling;
        EnableMeshletConeCulling = enableMeshletConeCulling;
        EnableGpuParticles = enableGpuParticles;
        EnableClusteredLighting = enableClusteredLighting;
        EnableHdr = enableHdr;
        MaximumLightsPerCluster = Positive(maximumLightsPerCluster, nameof(maximumLightsPerCluster));
    }

    public int MaximumObjects { get; }
    public int MaximumMeshes { get; }
    public int MaximumMeshlets { get; }
    public int MaximumMaterials { get; }
    public int MaximumIndirectCommands { get; }
    public int MaximumParticles { get; }
    public int CullingWorkgroupSize { get; }
    public int ParticleWorkgroupSize { get; }
    public int ClusterCountX { get; }
    public int ClusterCountY { get; }
    public int ClusterCountZ { get; }
    public bool EnableOcclusionCulling { get; }
    public bool EnableMeshletConeCulling { get; }
    public bool EnableGpuParticles { get; }
    public bool EnableClusteredLighting { get; }
    public bool EnableHdr { get; }
    public int MaximumLightsPerCluster { get; }
    public int ClusterCount => checked(ClusterCountX * ClusterCountY * ClusterCountZ);

    private static int Positive(int value, string name)
        => value > 0 ? value : throw new ArgumentOutOfRangeException(name);

    private static int FixedWorkgroup(int value, int required, string name)
    {
        if (value != required)
            throw new ArgumentOutOfRangeException(name, value, $"The compiled GPU-driven shader contract requires workgroup size {required}.");
        return value;
    }
}
