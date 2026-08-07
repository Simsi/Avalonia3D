using System;
using System.Collections.Generic;
using System.Threading;
using ThreeDEngine.Core.Diagnostics;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.World;

/// <summary>
/// Captures replayable commands with their deterministic execution tick and can re-apply them
/// to a reset scene while advancing only fixed simulation time.
/// </summary>
public sealed class WorldReplayController3D
{
    private readonly Scene3D _scene;
    private readonly object _sync = new();
    private readonly List<SceneReplayEntry3D> _entries = new(256);
    private bool _captureEnabled;
    private bool _replaying;

    internal WorldReplayController3D(Scene3D scene) => _scene = scene;

    public bool IsCaptureEnabled { get { lock (_sync) return _captureEnabled; } }
    public int EntryCount { get { lock (_sync) return _entries.Count; } }

    public void BeginCapture(bool clearExisting = true)
    {
        lock (_sync)
        {
            if (_replaying) throw new InvalidOperationException("Replay capture cannot begin while a replay is executing.");
            if (clearExisting) _entries.Clear();
            _captureEnabled = true;
        }
        EngineLog3D.Information("WorldReplay", $"Replay capture started; clear={clearExisting}; tick={_scene.UpdateLoop.SimulationTick}.");
    }

    public SceneReplayLog3D EndCapture()
    {
        lock (_sync)
        {
            _captureEnabled = false;
            return CreateLogCore();
        }
    }

    public SceneReplayLog3D CaptureLog()
    {
        lock (_sync) return CreateLogCore();
    }

    /// <summary>
    /// Replays synchronously on the simulation owner. The scene must not be inside a fixed tick
    /// and applications should pause external input/submission until this call returns.
    /// </summary>
    public void ReplayOffline(SceneReplayLog3D log, bool resetTimeline = true)
    {
        ArgumentNullException.ThrowIfNull(log);
        _scene.World.RequireSimulationOwner(nameof(ReplayOffline));
        lock (_sync)
        {
            if (_captureEnabled) throw new InvalidOperationException("Stop replay capture before applying a log.");
            if (_replaying) throw new InvalidOperationException("Replay is already executing.");
            _replaying = true;
        }

        try
        {
            if (resetTimeline) _scene.UpdateLoop.Reset(resetTimeline: true);
            var entries = log.Entries.Span;
            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                while (_scene.UpdateLoop.SimulationTick < entry.ExecutionTick)
                {
                    _scene.UpdateLoop.StepOnce();
                }
                entry.Command.CloneForReplay().Execute(_scene);
            }
            while (_scene.UpdateLoop.SimulationTick < log.FinalTick) _scene.UpdateLoop.StepOnce();
            _scene.World.PublishSnapshot(force: true);
            EngineLog3D.Information("WorldReplay", $"Replay completed; entries={entries.Length}; finalTick={_scene.UpdateLoop.SimulationTick}.");
        }
        finally
        {
            lock (_sync) _replaying = false;
        }
    }

    internal void RecordQueued(long sequence, IReplayableSceneCommand3D command, long tick)
    {
        lock (_sync)
        {
            if (!_captureEnabled || _replaying) return;
            _entries.Add(new SceneReplayEntry3D(tick, sequence, command.Name, command.CloneForReplay()));
        }
    }

    internal void RecordImmediate(IReplayableSceneCommand3D command, long tick)
        => RecordQueued(0, command, tick);

    private SceneReplayLog3D CreateLogCore()
    {
        var copy = new SceneReplayEntry3D[_entries.Count];
        for (var i = 0; i < copy.Length; i++)
        {
            var entry = _entries[i];
            copy[i] = entry with { Command = entry.Command.CloneForReplay() };
        }
        return new SceneReplayLog3D(copy, _scene.UpdateLoop.SimulationTick);
    }
}
