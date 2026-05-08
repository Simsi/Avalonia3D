namespace ThreeDEngine.Core.Throughput;

public sealed class ThroughputDiagnostics3D
{
    public int ParticleSystemCount { get; set; }
    public int ParticleCount { get; set; }
    public int ParticleVertexCount { get; set; }
    public int InstancedMeshLayerCount { get; set; }
    public int InstancedMeshInstanceCount { get; set; }
    public int CpuFallbackDrawCount { get; set; }
    public int RetainedBufferDrawCount { get; set; }
    public long ParticleMeshUploadBytes { get; set; }
    public string Mode { get; set; } = string.Empty;
}
