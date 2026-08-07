using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.World;

/// <summary>
/// Records multiple world mutations and commits them as one deterministic scene transaction.
/// A buffer is single-use and is safe to build off the simulation thread.
/// </summary>
public sealed class SceneCommandBuffer3D : IDisposable
{
    private readonly Scene3D _scene;
    private readonly List<BufferedCommand> _commands = new(16);
    private int _state;

    internal SceneCommandBuffer3D(Scene3D scene)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
    }

    public int Count => _commands.Count;
    public bool IsCommitted => Volatile.Read(ref _state) == 1;
    public bool IsDisposed => Volatile.Read(ref _state) == 2;

    public SceneCommandBuffer3D Add(Action<Scene3D> command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ThrowIfClosed();
        _commands.Add(new BufferedCommand(command, null));
        return this;
    }

    public SceneCommandBuffer3D Add(IReplayableSceneCommand3D command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ThrowIfClosed();
        _commands.Add(new BufferedCommand(command.Execute, command.CloneForReplay()));
        return this;
    }

    /// <summary>Queues the complete buffer and returns the queue sequence.</summary>
    public long Commit()
    {
        var batch = Seal();
        return _scene.Commands.EnqueueBatch(batch.Execute, batch.ReplayCommand);
    }

    /// <summary>Queues the complete buffer and completes after the simulation owner executes it.</summary>
    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);
        var batch = Seal();
        return _scene.Commands.EnqueueBatchAsync(batch.Execute, batch.ReplayCommand, cancellationToken);
    }

    internal int ExecuteImmediately(Scene3D scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var batch = Seal();
        batch.Execute(scene);
        if (batch.ReplayCommand is not null)
        {
            scene.World.Replay.RecordImmediate(batch.ReplayCommand, scene.UpdateLoop.SimulationTick);
        }
        return batch.Count;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _state, 2) == 2) return;
        _commands.Clear();
    }

    private SealedBatch Seal()
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
        {
            throw new InvalidOperationException("A scene command buffer can be committed only once.");
        }

        var commands = _commands.ToArray();
        _commands.Clear();
        var replay = BuildReplayCommand(commands);
        return new SealedBatch(commands, replay);
    }

    private static IReplayableSceneCommand3D? BuildReplayCommand(BufferedCommand[] commands)
    {
        if (commands.Length == 0) return null;
        var replayCommands = new List<IReplayableSceneCommand3D>(commands.Length);
        for (var i = 0; i < commands.Length; i++)
        {
            // A transaction is replayable only when every mutation has a deterministic clone.
            // Capturing a partial transaction would silently reproduce a different world state.
            if (commands[i].ReplayCommand is null) return null;
            replayCommands.Add(commands[i].ReplayCommand!);
        }
        return new CompositeReplayableSceneCommand3D(replayCommands.ToArray());
    }

    private void ThrowIfClosed()
    {
        if (Volatile.Read(ref _state) != 0)
        {
            throw new ObjectDisposedException(nameof(SceneCommandBuffer3D), "The command buffer is already committed or disposed.");
        }
    }

    private readonly record struct BufferedCommand(Action<Scene3D> Execute, IReplayableSceneCommand3D? ReplayCommand);

    private sealed class SealedBatch
    {
        private readonly BufferedCommand[] _commands;

        public SealedBatch(BufferedCommand[] commands, IReplayableSceneCommand3D? replayCommand)
        {
            _commands = commands;
            ReplayCommand = replayCommand;
        }

        public int Count => _commands.Length;
        public IReplayableSceneCommand3D? ReplayCommand { get; }

        public void Execute(Scene3D scene)
        {
            using var update = scene.BeginUpdate();
            for (var i = 0; i < _commands.Length; i++) _commands[i].Execute(scene);
        }
    }
}
