using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ThreeDEngine.Core.World;

namespace ThreeDEngine.Core.Scene;

/// <summary>
/// Multi-producer command queue consumed by the deterministic simulation scheduler.
/// Applications using dedicated simulation should marshal scene mutations through this queue.
/// Commands execute in sequence order before the next fixed tick.
/// </summary>
public sealed class SceneCommandQueue3D
{
    private readonly ConcurrentQueue<Command> _commands = new();
    private readonly object _enqueueSync = new();
    private readonly Action? _onQueued;
    private long _postedSequence;
    private long _completedSequence;
    private int _count;
    private int _disposed;

    internal SceneCommandQueue3D(Action? onQueued = null)
    {
        _onQueued = onQueued;
    }

    public int PendingCount => Volatile.Read(ref _count);
    public long LastPostedSequence => Volatile.Read(ref _postedSequence);
    public long LastCompletedSequence => Volatile.Read(ref _completedSequence);

    public long Enqueue(Action<Scene3D> command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return EnqueueCore(command, completion: null, CancellationToken.None, replayCommand: null);
    }

    public Task EnqueueAsync(Action<Scene3D> command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EnqueueCore(command, completion, cancellationToken, replayCommand: null);
        return completion.Task;
    }

    internal long EnqueueBatch(Action<Scene3D> command, IReplayableSceneCommand3D? replayCommand)
    {
        ArgumentNullException.ThrowIfNull(command);
        return EnqueueCore(command, completion: null, CancellationToken.None, replayCommand);
    }

    internal Task EnqueueBatchAsync(
        Action<Scene3D> command,
        IReplayableSceneCommand3D? replayCommand,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EnqueueCore(command, completion, cancellationToken, replayCommand);
        return completion.Task;
    }

    internal int Drain(Scene3D scene, int maximumCommands = 65_536)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (maximumCommands <= 0) throw new ArgumentOutOfRangeException(nameof(maximumCommands));
        var executed = 0;
        while (executed < maximumCommands && _commands.TryDequeue(out var command))
        {
            Interlocked.Decrement(ref _count);
            if (command.CancellationToken.IsCancellationRequested)
            {
                command.Completion?.TrySetCanceled(command.CancellationToken);
                Volatile.Write(ref _completedSequence, command.Sequence);
                executed++;
                continue;
            }

            try
            {
                command.Action(scene);
                if (command.ReplayCommand is not null)
                {
                    scene.World.Replay.RecordQueued(
                        command.Sequence,
                        command.ReplayCommand,
                        scene.UpdateLoop.SimulationTick);
                }
                command.Completion?.TrySetResult(true);
            }
            catch (Exception exception)
            {
                command.Completion?.TrySetException(exception);
                if (command.Completion is null) throw;
            }
            finally
            {
                Volatile.Write(ref _completedSequence, command.Sequence);
            }
            executed++;
        }
        return executed;
    }

    internal void Dispose(Exception? reason = null)
    {
        lock (_enqueueSync)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            reason ??= new ObjectDisposedException(nameof(SceneCommandQueue3D));
            while (_commands.TryDequeue(out var command))
            {
                Interlocked.Decrement(ref _count);
                command.Completion?.TrySetException(reason);
            }
        }
    }

    private long EnqueueCore(
        Action<Scene3D> command,
        TaskCompletionSource<bool>? completion,
        CancellationToken cancellationToken,
        IReplayableSceneCommand3D? replayCommand)
    {
        long sequence;
        lock (_enqueueSync)
        {
            ThrowIfDisposed();
            sequence = ++_postedSequence;
            _commands.Enqueue(new Command(sequence, command, completion, cancellationToken, replayCommand));
            Interlocked.Increment(ref _count);
        }
        SignalQueued();
        return sequence;
    }

    private void SignalQueued()
    {
        try
        {
            _onQueued?.Invoke();
        }
        catch
        {
            // Enqueue has already committed the command. Activity notification is advisory;
            // consumers can still drain explicitly through SceneUpdateLoop3D.PumpCommands().
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private readonly record struct Command(
        long Sequence,
        Action<Scene3D> Action,
        TaskCompletionSource<bool>? Completion,
        CancellationToken CancellationToken,
        IReplayableSceneCommand3D? ReplayCommand);
}
