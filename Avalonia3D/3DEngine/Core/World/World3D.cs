using System;
using System.Threading;
using System.Threading.Tasks;
using ThreeDEngine.Core.Diagnostics;
using ThreeDEngine.Core.Scene;
using RuntimeEnvironment = global::System.Environment;

namespace ThreeDEngine.Core.World;

/// <summary>
/// Authoritative world boundary for mutation ownership, deterministic commands, jobs, replay and
/// immutable read publications. Scene3D remains the object-model façade; runtime code should use
/// this type for all cross-thread interaction.
/// </summary>
public sealed class World3D
{
    private readonly Scene3D _scene;
    private readonly WorldSnapshotPublisher3D _snapshots = new();
    private readonly object _ownerSync = new();
    private readonly AsyncLocal<int> _readOnlyJobDepth = new();
    private int _ownerThreadId;
    private string? _ownerThreadName;
    private int _transientOwnerDepth;
    private bool _persistentOwnerBound;
    private long _ownerEpoch;
    private long _dirtyVersion = 1;
    private long _publishedDirtyVersion;
    private long _publishedSimulationTick = long.MinValue;
    private int _publishedInterpolationVersion = int.MinValue;
    private bool _publishedPaused;
    private bool _publishedFaulted;
    private long _compatibilityMutationCount;
    private long _strictMutationRejectionCount;
    private long _lastCompatibilityWarningTimestamp;
    private bool _disposed;

    internal World3D(Scene3D scene, WorldMutationPolicy3D mutationPolicy)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        MutationPolicy = mutationPolicy;
        Jobs = new WorldJobScheduler3D(scene);
        Replay = new WorldReplayController3D(scene);
    }

    public Scene3D Scene => _scene;
    public WorldMutationPolicy3D MutationPolicy { get; }
    public WorldJobScheduler3D Jobs { get; }
    public WorldReplayController3D Replay { get; }
    public int OwnerThreadId => Volatile.Read(ref _ownerThreadId);
    public bool HasSimulationOwner => OwnerThreadId != 0;
    public bool IsCurrentThreadSimulationOwner => OwnerThreadId == RuntimeEnvironment.CurrentManagedThreadId;
    public long PublishedSnapshotVersion => _snapshots.PublicationVersion;
    public long DroppedSnapshotPublicationCount => _snapshots.DroppedPublicationCount;


    /// <summary>
    /// Advances the authoritative world on its owner and publishes a new immutable read snapshot.
    /// Headless/manual hosts should use this method instead of calling SceneUpdateLoop3D directly.
    /// </summary>
    public SceneUpdateResult3D Advance(double elapsedSeconds)
    {
        ThrowIfDisposed();
        if (HasSimulationOwner && !IsCurrentThreadSimulationOwner)
        {
            throw new InvalidOperationException(
                $"World advance must execute on owner thread {OwnerThreadId}; current={RuntimeEnvironment.CurrentManagedThreadId}.");
        }

        if (IsCurrentThreadSimulationOwner)
        {
            var ownedResult = _scene.UpdateLoop.Advance(elapsedSeconds);
            PublishSnapshot();
            return ownedResult;
        }

        using var owner = EnterTransientOwnerScope();
        var result = _scene.UpdateLoop.Advance(elapsedSeconds);
        PublishSnapshot();
        return result;
    }

    public SceneUpdateResult3D Advance(TimeSpan elapsed) => Advance(elapsed.TotalSeconds);

    /// <summary>Executes one deterministic fixed tick and publishes the resulting snapshot.</summary>
    public SceneUpdateResult3D StepOnce()
    {
        ThrowIfDisposed();
        if (HasSimulationOwner && !IsCurrentThreadSimulationOwner)
        {
            throw new InvalidOperationException(
                $"World step must execute on owner thread {OwnerThreadId}; current={RuntimeEnvironment.CurrentManagedThreadId}.");
        }

        if (IsCurrentThreadSimulationOwner)
        {
            var ownedResult = _scene.UpdateLoop.StepOnce();
            PublishSnapshot();
            return ownedResult;
        }

        using var owner = EnterTransientOwnerScope();
        var result = _scene.UpdateLoop.StepOnce();
        PublishSnapshot();
        return result;
    }

    /// <summary>Drains queued mutations without advancing simulation time and republishes state.</summary>
    public int PumpCommands()
    {
        ThrowIfDisposed();
        if (HasSimulationOwner && !IsCurrentThreadSimulationOwner)
        {
            throw new InvalidOperationException(
                $"World command pump must execute on owner thread {OwnerThreadId}; current={RuntimeEnvironment.CurrentManagedThreadId}.");
        }

        if (IsCurrentThreadSimulationOwner)
        {
            var ownedCount = _scene.UpdateLoop.PumpCommands();
            PublishSnapshot();
            return ownedCount;
        }

        using var owner = EnterTransientOwnerScope();
        var count = _scene.UpdateLoop.PumpCommands();
        PublishSnapshot();
        return count;
    }

    public SceneCommandBuffer3D CreateCommandBuffer()
    {
        ThrowIfDisposed();
        return new SceneCommandBuffer3D(_scene);
    }

    public long Mutate(Action<Scene3D> mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ThrowIfDisposed();
        return _scene.Commands.Enqueue(mutation);
    }

    public Task MutateAsync(Action<Scene3D> mutation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ThrowIfDisposed();
        return _scene.Commands.EnqueueAsync(mutation, cancellationToken);
    }

    public long Mutate(IReplayableSceneCommand3D command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ThrowIfDisposed();
        return _scene.Commands.EnqueueBatch(command.Execute, command.CloneForReplay());
    }

    public Task MutateAsync(IReplayableSceneCommand3D command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ThrowIfDisposed();
        return _scene.Commands.EnqueueBatchAsync(command.Execute, command.CloneForReplay(), cancellationToken);
    }

    public WorldReadSnapshotLease3D AcquireReadSnapshot()
    {
        ThrowIfDisposed();
        EnsureInitialSnapshot();
        return _snapshots.Acquire();
    }

    public bool TryAcquireReadSnapshot(out WorldReadSnapshotLease3D? lease)
    {
        if (_disposed)
        {
            lease = default;
            return false;
        }
        EnsureInitialSnapshot();
        return _snapshots.TryAcquire(out lease);
    }

    public WorldOwnershipSnapshot3D CaptureOwnershipSnapshot()
    {
        var owner = OwnerThreadId;
        return new WorldOwnershipSnapshot3D(
            MutationPolicy,
            owner,
            _ownerThreadName,
            Interlocked.Read(ref _ownerEpoch),
            owner != 0,
            owner == RuntimeEnvironment.CurrentManagedThreadId,
            Interlocked.Read(ref _compatibilityMutationCount),
            Interlocked.Read(ref _strictMutationRejectionCount),
            _snapshots.PublicationVersion,
            TryGetPublishedTick(),
            _snapshots.DroppedPublicationCount,
            Jobs.Count,
            Replay.IsCaptureEnabled,
            Replay.EntryCount);
    }

    internal void ValidateMutationAccess(string? operation = null)
    {
        ThrowIfDisposed();
        if (_readOnlyJobDepth.Value > 0)
        {
            throw new InvalidOperationException(
                $"Read-only world job attempted mutable scene access{FormatOperation(operation)}. " +
                "Publish changes through WorldJobContext3D.Commands.");
        }

        var owner = OwnerThreadId;
        var current = RuntimeEnvironment.CurrentManagedThreadId;
        if (owner == 0 || owner == current) return;

        if (MutationPolicy == WorldMutationPolicy3D.StrictSimulationOwner)
        {
            Interlocked.Increment(ref _strictMutationRejectionCount);
            throw new InvalidOperationException(
                $"World mutation{FormatOperation(operation)} was attempted from thread {current}, " +
                $"but simulation owner thread is {owner}. Use Scene3D.World.Mutate, MutateAsync or a SceneCommandBuffer3D.");
        }

        var count = Interlocked.Increment(ref _compatibilityMutationCount);
        var now = global::System.Diagnostics.Stopwatch.GetTimestamp();
        var previous = Volatile.Read(ref _lastCompatibilityWarningTimestamp);
        if (count == 1 || now - previous >= global::System.Diagnostics.Stopwatch.Frequency * 10L)
        {
            Volatile.Write(ref _lastCompatibilityWarningTimestamp, now);
            EngineLog3D.Warning(
                "WorldOwnership",
                $"Synchronized compatibility mutation{FormatOperation(operation)} executed on thread {current} while owner={owner}; count={count}. " +
                "This path is safe but serializes simulation/render access. Migrate runtime writes to World3D commands and enable StrictSimulationOwner.");
        }
    }

    internal WorldOwnerLease3D BindPersistentOwner()
    {
        ThrowIfDisposed();
        var threadId = RuntimeEnvironment.CurrentManagedThreadId;
        lock (_ownerSync)
        {
            if (_ownerThreadId != 0)
            {
                throw new InvalidOperationException($"World already has simulation owner thread {_ownerThreadId}; cannot bind thread {threadId}.");
            }
            _ownerThreadId = threadId;
            _ownerThreadName = Thread.CurrentThread.Name;
            _transientOwnerDepth = 0;
            _persistentOwnerBound = true;
            Interlocked.Increment(ref _ownerEpoch);
        }
        EngineLog3D.Information("WorldOwnership", $"Persistent simulation owner bound; thread={threadId}:{_ownerThreadName ?? "unnamed"}; epoch={_ownerEpoch}; policy={MutationPolicy}.");
        return new WorldOwnerLease3D(this, threadId, transient: false);
    }

    internal WorldOwnerLease3D EnterTransientOwnerScope()
    {
        ThrowIfDisposed();
        var threadId = RuntimeEnvironment.CurrentManagedThreadId;
        lock (_ownerSync)
        {
            if (_ownerThreadId != 0 && _ownerThreadId != threadId)
            {
                throw new InvalidOperationException($"World is owned by thread {_ownerThreadId}; host thread {threadId} cannot enter an owner scope.");
            }
            if (_persistentOwnerBound)
            {
                throw new InvalidOperationException("A transient owner scope cannot be nested inside the persistent simulation owner.");
            }
            if (_ownerThreadId == 0)
            {
                _ownerThreadId = threadId;
                _ownerThreadName = Thread.CurrentThread.Name;
                Interlocked.Increment(ref _ownerEpoch);
            }
            _transientOwnerDepth++;
        }
        return new WorldOwnerLease3D(this, threadId, transient: true);
    }

    internal void ExitOwnerScope(int threadId, bool transient)
    {
        lock (_ownerSync)
        {
            if (_ownerThreadId != threadId) return;
            if (transient)
            {
                if (_persistentOwnerBound) return;
                if (_transientOwnerDepth > 0) _transientOwnerDepth--;
                if (_transientOwnerDepth != 0) return;
            }
            else
            {
                _persistentOwnerBound = false;
            }
            _ownerThreadId = 0;
            _ownerThreadName = null;
            _transientOwnerDepth = 0;
            Interlocked.Increment(ref _ownerEpoch);
        }
        if (!transient)
        {
            EngineLog3D.Information("WorldOwnership", $"Simulation owner released; thread={threadId}; epoch={_ownerEpoch}.");
        }
    }

    internal IDisposable EnterReadOnlyJobScope()
    {
        _readOnlyJobDepth.Value++;
        return new ReadOnlyJobScope(this);
    }

    internal void RequireSimulationOwner(string operation)
    {
        var owner = OwnerThreadId;
        var current = RuntimeEnvironment.CurrentManagedThreadId;
        if (owner == 0 || owner != current)
        {
            throw new InvalidOperationException($"{operation} must execute on the simulation owner. owner={owner}; current={current}.");
        }
    }

    internal void MarkSnapshotDirty()
    {
        if (_disposed) return;
        Interlocked.Increment(ref _dirtyVersion);
    }

    internal bool PublishSnapshot(bool force = false)
    {
        ThrowIfDisposed();
        var dirty = Interlocked.Read(ref _dirtyVersion);
        var loop = _scene.UpdateLoop;
        var tick = loop.SimulationTick;
        var interpolationVersion = _scene.FrameInterpolator.RenderVersion;
        var paused = loop.IsPaused;
        var faulted = loop.IsFaulted;
        if (!force &&
            dirty == Interlocked.Read(ref _publishedDirtyVersion) &&
            tick == Interlocked.Read(ref _publishedSimulationTick) &&
            interpolationVersion == Volatile.Read(ref _publishedInterpolationVersion) &&
            paused == _publishedPaused &&
            faulted == _publishedFaulted &&
            _snapshots.PublicationVersion != 0)
        {
            return false;
        }

        using var access = _scene.EnterRenderReadScope();
        var published = _snapshots.Publish(_scene);
        if (published)
        {
            Interlocked.Exchange(ref _publishedDirtyVersion, dirty);
            Interlocked.Exchange(ref _publishedSimulationTick, tick);
            Volatile.Write(ref _publishedInterpolationVersion, interpolationVersion);
            _publishedPaused = paused;
            _publishedFaulted = faulted;
        }
        return published;
    }

    internal void DisposeFromScene()
    {
        _disposed = true;
        Jobs.Clear();
        lock (_ownerSync)
        {
            _ownerThreadId = 0;
            _ownerThreadName = null;
            _transientOwnerDepth = 0;
            _persistentOwnerBound = false;
        }
    }

    private void EnsureInitialSnapshot()
    {
        if (_snapshots.PublicationVersion != 0) return;
        var owner = OwnerThreadId;
        if (owner != 0 && owner != RuntimeEnvironment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("The simulation owner has not published the first immutable world snapshot yet.");
        }
        PublishSnapshot(force: true);
    }

    private long TryGetPublishedTick()
    {
        if (!_snapshots.TryAcquire(out var lease) || lease is null) return 0;
        using (lease) return lease.Snapshot.SimulationTick;
    }

    private void ExitReadOnlyJobScope()
    {
        if (_readOnlyJobDepth.Value > 0) _readOnlyJobDepth.Value--;
    }

    private static string FormatOperation(string? operation)
        => string.IsNullOrWhiteSpace(operation) ? string.Empty : $" ({operation})";

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed || _scene.IsDisposed, this);

    private sealed class ReadOnlyJobScope : IDisposable
    {
        private World3D? _world;

        public ReadOnlyJobScope(World3D world) => _world = world;

        public void Dispose()
        {
            var world = Interlocked.Exchange(ref _world, null);
            world?.ExitReadOnlyJobScope();
        }
    }
}
