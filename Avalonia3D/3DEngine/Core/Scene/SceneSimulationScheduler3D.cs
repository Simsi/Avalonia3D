using System;
using System.Diagnostics;

namespace ThreeDEngine.Core.Scene;

/// <summary>
/// Owns deterministic stage ordering, command consumption and dependency-aware world jobs.
/// Read-only jobs may execute concurrently against immutable snapshots; their commands commit
/// in deterministic registration order before mutable user/animation/physics stages.
/// </summary>
internal sealed class SceneSimulationScheduler3D
{
    private readonly Scene3D _scene;
    private int _pendingCommandsExecuted;
    private double _pendingCommandsMilliseconds;

    public SceneSimulationScheduler3D(Scene3D scene) => _scene = scene ?? throw new ArgumentNullException(nameof(scene));

    public SceneSimulationMetrics3D LastMetrics { get; private set; }

    public void Execute(in SceneFixedUpdateContext3D context, bool animations, bool physics, bool particles)
    {
        var totalStart = Stopwatch.GetTimestamp();
        var commandsStart = totalStart;
        var commandCount = _pendingCommandsExecuted + _scene.Commands.Drain(_scene);
        var commandsMs = _pendingCommandsMilliseconds + ElapsedMilliseconds(commandsStart);
        _pendingCommandsExecuted = 0;
        _pendingCommandsMilliseconds = 0d;

        var jobs = _scene.World.Jobs.Execute(in context);

        var stage = Stopwatch.GetTimestamp();
        _scene.BeginScheduledFixedUpdate(in context);
        var userMs = ElapsedMilliseconds(stage);

        stage = Stopwatch.GetTimestamp();
        if (animations) _scene.AdvanceScheduledAnimations(context.DeltaSeconds);
        var animationMs = ElapsedMilliseconds(stage);

        stage = Stopwatch.GetTimestamp();
        if (physics) _scene.AdvanceScheduledPhysics(context.DeltaSeconds);
        var physicsMs = ElapsedMilliseconds(stage);

        stage = Stopwatch.GetTimestamp();
        if (particles) _scene.AdvanceScheduledParticles(context.DeltaSeconds);
        var particleMs = ElapsedMilliseconds(stage);

        stage = Stopwatch.GetTimestamp();
        _scene.CompleteScheduledFixedUpdate(in context);
        var completionMs = ElapsedMilliseconds(stage);

        LastMetrics = new SceneSimulationMetrics3D(
            context.Tick,
            commandCount,
            commandsMs,
            jobs.JobCount,
            jobs.CommandsCommitted,
            jobs.ParallelBatchCount,
            jobs.SnapshotMilliseconds,
            jobs.ExecutionMilliseconds,
            jobs.CommitMilliseconds,
            jobs.TotalMilliseconds,
            userMs,
            animationMs,
            physicsMs,
            particleMs,
            completionMs,
            ElapsedMilliseconds(totalStart));
    }

    public int PumpCommands()
    {
        var start = Stopwatch.GetTimestamp();
        var count = _scene.Commands.Drain(_scene);
        if (count > 0)
        {
            var milliseconds = ElapsedMilliseconds(start);
            _pendingCommandsExecuted += count;
            _pendingCommandsMilliseconds += milliseconds;
            LastMetrics = LastMetrics with
            {
                CommandsExecuted = _pendingCommandsExecuted,
                CommandsMilliseconds = _pendingCommandsMilliseconds
            };
        }
        return count;
    }

    private static double ElapsedMilliseconds(long start)
        => (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
}
