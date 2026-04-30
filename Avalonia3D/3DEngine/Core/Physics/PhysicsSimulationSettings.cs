using System;

namespace ThreeDEngine.Core.Physics;

public sealed class PhysicsSimulationSettings
{
    public PhysicsSimulationMode Mode { get; set; } = PhysicsSimulationMode.Manual;
    public float FixedDeltaSeconds { get; set; } = 1f / 60f;
    public float MaxAccumulatedSeconds { get; set; } = 0.25f;

    public float ClampDelta(float deltaSeconds)
    {
        if (deltaSeconds <= 0f) return 0f;
        return MathF.Min(deltaSeconds, MathF.Max(FixedDeltaSeconds, MaxAccumulatedSeconds));
    }
}
