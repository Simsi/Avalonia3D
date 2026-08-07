using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using ThreeDEngine.Core.Diagnostics;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.World;

/// <summary>
/// Dependency-aware deterministic job boundary. Read-only jobs in one topological level run in
/// parallel on threaded runtimes and publish command buffers in registration order. Browser
/// execution remains sequential while preserving exactly the same ordering semantics.
/// </summary>
public sealed class WorldJobScheduler3D
{
    private readonly Scene3D _scene;
    private readonly object _sync = new();
    private readonly List<Registration> _registrations = new();
    private readonly Dictionary<string, Registration> _byName = new(StringComparer.Ordinal);
    private List<Registration[]>? _levels;
    private readonly WorldSnapshot3D _snapshot = new();
    private long _snapshotVersion;

    internal WorldJobScheduler3D(Scene3D scene) => _scene = scene;

    public int Count { get { lock (_sync) return _registrations.Count; } }
    public WorldJobExecutionMetrics3D LastMetrics { get; private set; }

    public void Register(IWorldJob3D job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (string.IsNullOrWhiteSpace(job.Name)) throw new ArgumentException("World job name is required.", nameof(job));
        if (!Enum.IsDefined(typeof(WorldJobAccess3D), job.Access)) throw new ArgumentOutOfRangeException(nameof(job), job.Access, "Unknown world-job access mode.");
        var dependencies = CopyDependencies(job.Dependencies);
        lock (_sync)
        {
            if (_byName.ContainsKey(job.Name)) throw new InvalidOperationException($"A world job named '{job.Name}' is already registered.");
            var registration = new Registration(job, dependencies, _registrations.Count);
            _registrations.Add(registration);
            _byName.Add(job.Name, registration);
            _levels = null;
        }
        _scene.NotifyUpdateActivityChanged();
        EngineLog3D.Information("WorldJobs", $"Registered job '{job.Name}'; access={job.Access}; dependencies={dependencies.Count}.");
    }

    public bool Unregister(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        lock (_sync)
        {
            if (!_byName.Remove(name, out var registration)) return false;
            _registrations.Remove(registration);
            for (var i = 0; i < _registrations.Count; i++) _registrations[i].RegistrationOrder = i;
            _levels = null;
        }
        _scene.NotifyUpdateActivityChanged();
        return true;
    }

    public void Clear()
    {
        lock (_sync)
        {
            _registrations.Clear();
            _byName.Clear();
            _levels = null;
        }
        _scene.NotifyUpdateActivityChanged();
    }

    internal WorldJobExecutionMetrics3D Execute(in SceneFixedUpdateContext3D context)
    {
        Registration[][] levels;
        lock (_sync)
        {
            if (_registrations.Count == 0)
            {
                LastMetrics = WorldJobExecutionMetrics3D.Empty with { Tick = context.Tick };
                return LastMetrics;
            }
            levels = GetOrBuildLevelsCore().ToArray();
        }

        var totalStart = Stopwatch.GetTimestamp();
        var snapshotMs = 0d;
        var executionMs = 0d;
        var commitMs = 0d;
        var readOnlyCount = 0;
        var exclusiveCount = 0;
        var parallelBatches = 0;
        var commandsCommitted = 0;

        for (var levelIndex = 0; levelIndex < levels.Length; levelIndex++)
        {
            var level = levels[levelIndex];
            var readOnly = new List<Registration>(level.Length);
            var exclusive = new List<Registration>(level.Length);
            for (var i = 0; i < level.Length; i++)
            {
                if (level[i].Job.Access == WorldJobAccess3D.ReadOnly) readOnly.Add(level[i]);
                else exclusive.Add(level[i]);
            }

            if (readOnly.Count > 0)
            {
                var snapshotStart = Stopwatch.GetTimestamp();
                _snapshot.Capture(_scene, ++_snapshotVersion);
                snapshotMs += ElapsedMilliseconds(snapshotStart);

                var buffers = new SceneCommandBuffer3D?[readOnly.Count];
                try
                {
                    var executeStart = Stopwatch.GetTimestamp();
                    var fixedUpdate = context;
                    if (!OperatingSystem.IsBrowser() && readOnly.Count > 1)
                    {
                        parallelBatches++;
                        Parallel.For(0, readOnly.Count, i => ExecuteReadOnly(readOnly[i], buffers, i, fixedUpdate));
                    }
                    else
                    {
                        for (var i = 0; i < readOnly.Count; i++) ExecuteReadOnly(readOnly[i], buffers, i, fixedUpdate);
                    }
                    executionMs += ElapsedMilliseconds(executeStart);
                    readOnlyCount += readOnly.Count;

                    var commitStart = Stopwatch.GetTimestamp();
                    for (var i = 0; i < buffers.Length; i++)
                    {
                        var buffer = buffers[i] ?? throw new InvalidOperationException($"World job '{readOnly[i].Job.Name}' did not produce a command buffer.");
                        commandsCommitted += buffer.ExecuteImmediately(_scene);
                        buffer.Dispose();
                        buffers[i] = null;
                    }
                    commitMs += ElapsedMilliseconds(commitStart);
                }
                finally
                {
                    for (var i = 0; i < buffers.Length; i++) buffers[i]?.Dispose();
                }
            }

            for (var i = 0; i < exclusive.Count; i++)
            {
                // Exclusive jobs observe the state produced by every earlier dependency/job.
                var snapshotStart = Stopwatch.GetTimestamp();
                _snapshot.Capture(_scene, ++_snapshotVersion);
                snapshotMs += ElapsedMilliseconds(snapshotStart);

                using var buffer = _scene.World.CreateCommandBuffer();
                var executeStart = Stopwatch.GetTimestamp();
                var contextValue = new WorldJobContext3D(_scene, _snapshot, buffer, in context, WorldJobAccess3D.Exclusive);
                exclusive[i].Job.Execute(contextValue);
                executionMs += ElapsedMilliseconds(executeStart);

                var commitStart = Stopwatch.GetTimestamp();
                commandsCommitted += buffer.ExecuteImmediately(_scene);
                commitMs += ElapsedMilliseconds(commitStart);
                exclusiveCount++;
            }
        }

        LastMetrics = new WorldJobExecutionMetrics3D(
            context.Tick,
            readOnlyCount + exclusiveCount,
            readOnlyCount,
            exclusiveCount,
            parallelBatches,
            commandsCommitted,
            snapshotMs,
            executionMs,
            commitMs,
            ElapsedMilliseconds(totalStart));
        return LastMetrics;
    }

    private void ExecuteReadOnly(
        Registration registration,
        SceneCommandBuffer3D?[] buffers,
        int index,
        in SceneFixedUpdateContext3D fixedUpdate)
    {
        var buffer = _scene.World.CreateCommandBuffer();
        buffers[index] = buffer;
        using var readOnlyScope = _scene.World.EnterReadOnlyJobScope();
        var context = new WorldJobContext3D(_scene, _snapshot, buffer, in fixedUpdate, WorldJobAccess3D.ReadOnly);
        registration.Job.Execute(context);
    }

    private List<Registration[]> GetOrBuildLevelsCore()
    {
        if (_levels is not null) return _levels;
        var remaining = new HashSet<Registration>(_registrations);
        var completed = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<Registration[]>();

        while (remaining.Count > 0)
        {
            var ready = new List<Registration>();
            foreach (var registration in _registrations)
            {
                if (!remaining.Contains(registration)) continue;
                var dependencies = registration.Dependencies;
                var satisfied = true;
                for (var i = 0; i < dependencies.Count; i++)
                {
                    var dependency = dependencies[i];
                    if (!_byName.ContainsKey(dependency))
                    {
                        throw new InvalidOperationException($"World job '{registration.Job.Name}' depends on unknown job '{dependency}'.");
                    }
                    if (!completed.Contains(dependency)) { satisfied = false; break; }
                }
                if (satisfied) ready.Add(registration);
            }

            if (ready.Count == 0)
            {
                throw new InvalidOperationException("World job graph contains a dependency cycle.");
            }

            ready.Sort(static (a, b) => a.RegistrationOrder.CompareTo(b.RegistrationOrder));
            result.Add(ready.ToArray());
            for (var i = 0; i < ready.Count; i++)
            {
                remaining.Remove(ready[i]);
                completed.Add(ready[i].Job.Name);
            }
        }

        _levels = result;
        return result;
    }


    private static IReadOnlyList<string> CopyDependencies(IReadOnlyList<string>? dependencies)
    {
        if (dependencies is null || dependencies.Count == 0) return Array.Empty<string>();
        var copy = new string[dependencies.Count];
        for (var i = 0; i < copy.Length; i++)
        {
            var dependency = dependencies[i];
            if (string.IsNullOrWhiteSpace(dependency))
            {
                throw new ArgumentException("World-job dependency names cannot be empty.", nameof(dependencies));
            }
            copy[i] = dependency;
        }
        return copy;
    }

    private static double ElapsedMilliseconds(long start)
        => (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;

    private sealed class Registration
    {
        public Registration(IWorldJob3D job, IReadOnlyList<string> dependencies, int registrationOrder)
        {
            Job = job;
            Dependencies = dependencies;
            RegistrationOrder = registrationOrder;
        }

        public IWorldJob3D Job { get; }
        public IReadOnlyList<string> Dependencies { get; }
        public int RegistrationOrder { get; set; }
    }
}
