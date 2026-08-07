namespace ThreeDEngine.Core.Scene;

/// <summary>Last completed deterministic tick timings. Values are CPU wall times.</summary>
public readonly record struct SceneSimulationMetrics3D(
    long Tick,
    int CommandsExecuted,
    double CommandsMilliseconds,
    int JobsExecuted,
    int JobCommandsCommitted,
    int ParallelJobBatches,
    double JobsSnapshotMilliseconds,
    double JobsExecutionMilliseconds,
    double JobsCommitMilliseconds,
    double JobsTotalMilliseconds,
    double UserUpdateMilliseconds,
    double AnimationMilliseconds,
    double PhysicsMilliseconds,
    double ParticleMilliseconds,
    double CompletionMilliseconds,
    double TotalMilliseconds)
{
    public static SceneSimulationMetrics3D Empty => default;
}
