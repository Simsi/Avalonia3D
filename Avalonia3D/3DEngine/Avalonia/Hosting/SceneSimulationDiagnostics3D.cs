using System;

namespace ThreeDEngine.Avalonia.Hosting;

public sealed class SceneSimulationFaultedEventArgs3D : EventArgs
{
    public SceneSimulationFaultedEventArgs3D(Exception exception, SceneSimulationHostSnapshot3D snapshot)
    {
        Exception = exception ?? throw new ArgumentNullException(nameof(exception));
        Snapshot = snapshot;
    }

    public Exception Exception { get; }
    public SceneSimulationHostSnapshot3D Snapshot { get; }
}

public readonly record struct SceneSimulationHostSnapshot3D(
    SceneSimulationExecutionMode3D ConfiguredMode,
    SceneSimulationExecutionMode3D ResolvedMode,
    bool UsesDedicatedThread,
    bool WorkerAlive,
    int WorkerManagedThreadId,
    string? WorkerName,
    bool StopRequested,
    bool ShutdownTimedOut,
    bool Disposed,
    double PendingSeconds,
    long SubmitCount,
    long WakeCount,
    long AdvanceCount,
    long CommandPumpCount,
    long SuccessfulCycleCount,
    long FaultCount,
    long LastSubmitTimestamp,
    long LastWakeTimestamp,
    long LastSuccessTimestamp,
    long LastFaultTimestamp,
    string? LastFaultType,
    string? LastFaultMessage);
