using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.Particles;

public sealed class ParticleSystemSettings3D
{
    public int Capacity { get; set; } = 1024;
    public float EmissionRatePerSecond { get; set; } = 64f;
    public float ParticleLifetimeSeconds { get; set; } = 2.5f;
    public float StartSize { get; set; } = 0.08f;
    public float EndSize { get; set; } = 0.02f;
    public ColorRgba StartColor { get; set; } = new(1f, 1f, 1f, 1f);
    public ColorRgba EndColor { get; set; } = new(1f, 1f, 1f, 0f);
    public float InitialSpeed { get; set; } = 1.5f;
    public float Spread { get; set; } = 0.35f;
    public ParticleSimulationSpace3D SimulationSpace { get; set; } = ParticleSimulationSpace3D.Local;
    public bool Looping { get; set; } = true;
    public bool Prewarm { get; set; }
    public ParticleRenderMode3D RenderMode { get; set; } = ParticleRenderMode3D.CameraFacingQuad;

    public ParticleSystemSettings3D Clone()
    {
        return new ParticleSystemSettings3D
        {
            Capacity = Capacity,
            EmissionRatePerSecond = EmissionRatePerSecond,
            ParticleLifetimeSeconds = ParticleLifetimeSeconds,
            StartSize = StartSize,
            EndSize = EndSize,
            StartColor = StartColor,
            EndColor = EndColor,
            InitialSpeed = InitialSpeed,
            Spread = Spread,
            SimulationSpace = SimulationSpace,
            Looping = Looping,
            Prewarm = Prewarm,
            RenderMode = RenderMode
        };
    }
}
