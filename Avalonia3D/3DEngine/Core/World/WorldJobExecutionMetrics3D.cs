namespace ThreeDEngine.Core.World;

public readonly record struct WorldJobExecutionMetrics3D(
    long Tick,
    int JobCount,
    int ReadOnlyJobCount,
    int ExclusiveJobCount,
    int ParallelBatchCount,
    int CommandsCommitted,
    double SnapshotMilliseconds,
    double ExecutionMilliseconds,
    double CommitMilliseconds,
    double TotalMilliseconds)
{
    public static WorldJobExecutionMetrics3D Empty => default;
}
