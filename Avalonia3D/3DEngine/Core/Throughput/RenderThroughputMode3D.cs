namespace ThreeDEngine.Core.Throughput;

public enum RenderThroughputMode3D
{
    Automatic = 0,
    CpuMeshFallback = 1,
    InstancedMesh = 2,
    HighScaleRetained = 3,
    GpuParticles = 4,
    IndirectDraw = 5
}
