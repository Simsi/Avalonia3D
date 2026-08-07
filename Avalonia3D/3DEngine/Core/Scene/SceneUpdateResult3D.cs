namespace ThreeDEngine.Core.Scene;

/// <summary>
/// Result of feeding elapsed host time into <see cref="SceneUpdateLoop3D"/>.
/// </summary>
public readonly record struct SceneUpdateResult3D(
    int ExecutedSteps,
    int DroppedSteps,
    double InputSeconds,
    double SimulatedSeconds,
    double DroppedSeconds,
    double InterpolationAlpha,
    long SimulationTick,
    double SimulationTimeSeconds)
{
    public static SceneUpdateResult3D Idle(
        double inputSeconds,
        double interpolationAlpha,
        long simulationTick,
        double simulationTimeSeconds)
        => new(
            0,
            0,
            inputSeconds,
            0d,
            0d,
            interpolationAlpha,
            simulationTick,
            simulationTimeSeconds);
}
