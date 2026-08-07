using System;
using ThreeDEngine.Core.Diagnostics;

namespace ThreeDEngine.Core.Scene;

/// <summary>
/// The scene's single deterministic simulation timeline. A host supplies monotonic elapsed
/// time; this loop converts it to fixed ticks, bounds catch-up work and publishes render alpha.
/// It never reads a wall clock and therefore produces identical ticks for identical inputs.
/// </summary>
public sealed class SceneUpdateLoop3D
{
    public const double DefaultFixedDeltaSeconds = 1d / 60d;
    public const int DefaultMaximumCatchUpSteps = 4;
    public const double DefaultMaximumFrameDeltaSeconds = 0.25d;

    private readonly Scene3D _scene;
    private double _fixedDeltaSeconds = DefaultFixedDeltaSeconds;
    private int _maximumCatchUpSteps = DefaultMaximumCatchUpSteps;
    private double _maximumFrameDeltaSeconds = DefaultMaximumFrameDeltaSeconds;
    private double _timeScale = 1d;
    private double _accumulatorSeconds;
    private double _simulationTimeSeconds;
    private double _totalDroppedSeconds;
    private long _simulationTick;
    private long _lastDropLogTick = long.MinValue;
    private int _suppressedDropReports;
    private double _suppressedDroppedSeconds;
    private bool _isPaused;
    private bool _isAdvancing;
    private bool _isDisposed;
    private bool _advanceAnimations = true;
    private bool _advancePhysics = true;
    private bool _advanceParticles = true;
    private Exception? _fault;
    private SceneUpdateResult3D _lastResult;

    internal SceneUpdateLoop3D(Scene3D scene)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _lastResult = SceneUpdateResult3D.Idle(0d, 1d, 0, 0d);
    }

    /// <summary>Raised when pause, configuration or fault state changes.</summary>
    public event EventHandler? StateChanged;

    public double FixedDeltaSeconds
    {
        get => _fixedDeltaSeconds;
        set
        {
            ThrowIfDisposed();
            using var access = _scene.EnterMutationScope();
            if (!double.IsFinite(value) || value <= 0d || value > 1d)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Fixed delta must be finite and in the (0, 1] second range.");
            }

            if (global::System.Math.Abs(_fixedDeltaSeconds - value) <= 1e-12d) return;
            ThrowIfConfigurationChangeDuringAdvance();
            DiscardAccumulatorCore(countAsDropped: true);
            _fixedDeltaSeconds = value;
            PublishInterpolationAlpha(_isPaused ? 1d : 0d);
            OnStateChanged();
        }
    }

    public double FixedUpdatesPerSecond
    {
        get => 1d / _fixedDeltaSeconds;
        set
        {
            if (!double.IsFinite(value) || value < 1d || value > 1000d)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Fixed update frequency must be finite and between 1 and 1000 Hz.");
            }
            FixedDeltaSeconds = 1d / value;
        }
    }

    public int MaximumCatchUpSteps
    {
        get => _maximumCatchUpSteps;
        set
        {
            ThrowIfDisposed();
            using var access = _scene.EnterMutationScope();
            if (value < 1 || value > 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Maximum catch-up steps must be between 1 and 1024.");
            }
            if (_maximumCatchUpSteps == value) return;
            ThrowIfConfigurationChangeDuringAdvance();
            _maximumCatchUpSteps = value;
            OnStateChanged();
        }
    }

    public double MaximumFrameDeltaSeconds
    {
        get => _maximumFrameDeltaSeconds;
        set
        {
            ThrowIfDisposed();
            using var access = _scene.EnterMutationScope();
            if (!double.IsFinite(value) || value <= 0d || value > 60d)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Maximum frame delta must be finite and in the (0, 60] second range.");
            }
            if (global::System.Math.Abs(_maximumFrameDeltaSeconds - value) <= 1e-12d) return;
            ThrowIfConfigurationChangeDuringAdvance();
            _maximumFrameDeltaSeconds = value;
            OnStateChanged();
        }
    }

    public double TimeScale
    {
        get => _timeScale;
        set
        {
            ThrowIfDisposed();
            using var access = _scene.EnterMutationScope();
            if (!double.IsFinite(value) || value < 0d || value > 100d)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Time scale must be finite and between 0 and 100.");
            }
            if (global::System.Math.Abs(_timeScale - value) <= 1e-12d) return;
            ThrowIfConfigurationChangeDuringAdvance();
            _timeScale = value;
            OnStateChanged();
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            ThrowIfDisposed();
            using var access = _scene.EnterMutationScope();
            if (_isPaused == value) return;
            _isPaused = value;
            PublishInterpolationAlpha(value ? 1d : CalculateInterpolationAlpha());
            EngineLog3D.Information("UpdateLoop", value
                ? $"Scene simulation paused at tick {_simulationTick}."
                : $"Scene simulation resumed at tick {_simulationTick}.");
            OnStateChanged();
        }
    }

    public bool AdvanceAnimations
    {
        get => _advanceAnimations;
        set
        {
            ThrowIfDisposed();
            using var access = _scene.EnterMutationScope();
            if (_advanceAnimations == value) return;
            ThrowIfConfigurationChangeDuringAdvance();
            _advanceAnimations = value;
            _scene.RefreshActiveUpdateWorkHint();
            OnStateChanged();
        }
    }

    public bool AdvancePhysics
    {
        get => _advancePhysics;
        set
        {
            ThrowIfDisposed();
            using var access = _scene.EnterMutationScope();
            if (_advancePhysics == value) return;
            ThrowIfConfigurationChangeDuringAdvance();
            _advancePhysics = value;
            _scene.RefreshActiveUpdateWorkHint();
            OnStateChanged();
        }
    }

    public bool AdvanceParticles
    {
        get => _advanceParticles;
        set
        {
            ThrowIfDisposed();
            using var access = _scene.EnterMutationScope();
            if (_advanceParticles == value) return;
            ThrowIfConfigurationChangeDuringAdvance();
            _advanceParticles = value;
            _scene.RefreshActiveUpdateWorkHint();
            OnStateChanged();
        }
    }

    public bool IsFaulted => _fault is not null;
    public Exception? Fault => _fault;
    public long SimulationTick => _simulationTick;
    public double SimulationTimeSeconds => _simulationTimeSeconds;
    public double AccumulatorSeconds => _accumulatorSeconds;
    public double InterpolationAlpha => _scene.FrameInterpolator.Alpha;
    /// <summary>
    /// Presentation timeline consumed by shader-side animation. With interpolation enabled it
    /// matches the previous-to-current transform interval; otherwise it is the latest fixed state.
    /// </summary>
    public double RenderTimeSeconds =>
        !_isPaused && _scene.FrameInterpolator.Enabled && _simulationTick > 0
            ? global::System.Math.Max(0d, _simulationTimeSeconds - _fixedDeltaSeconds + _accumulatorSeconds)
            : _simulationTimeSeconds;
    public double TotalDroppedSeconds => _totalDroppedSeconds;
    public SceneUpdateResult3D LastResult => _lastResult;

    public SceneUpdateResult3D Advance(TimeSpan elapsed)
        => Advance(elapsed.TotalSeconds);

    /// <summary>
    /// Feeds elapsed monotonic host time into the accumulator. Long frames are bounded by
    /// <see cref="MaximumFrameDeltaSeconds"/> and <see cref="MaximumCatchUpSteps"/>; excess
    /// whole ticks are reported and discarded instead of causing an unbounded spiral of death.
    /// </summary>
    public SceneUpdateResult3D Advance(double elapsedSeconds)
    {
        ThrowIfDisposed();
        using var access = _scene.EnterMutationScope();
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), "Elapsed time must be finite and non-negative.");
        }
        if (_isAdvancing)
        {
            throw new InvalidOperationException("SceneUpdateLoop3D.Advance cannot be called recursively.");
        }

        // Commands are consumed before the sticky-fault check so a queued recovery command can
        // inspect the fault, repair scene state and call ResetFault without requiring an unsafe
        // direct mutation from the host thread.
        PumpQueuedCommandsWithFaultCapture();
        ThrowIfFaulted();

        if (_isPaused || elapsedSeconds == 0d || _timeScale == 0d)
        {
            var idleAlpha = _isPaused ? 1d : CalculateInterpolationAlpha();
            PublishInterpolationAlpha(idleAlpha);
            _lastResult = SceneUpdateResult3D.Idle(elapsedSeconds, idleAlpha, _simulationTick, _simulationTimeSeconds);
            return _lastResult;
        }

        var acceptedHostSeconds = global::System.Math.Min(elapsedSeconds, _maximumFrameDeltaSeconds);
        var scaledSeconds = acceptedHostSeconds * _timeScale;
        if (!double.IsFinite(scaledSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), "Scaled elapsed time must remain finite.");
        }
        var droppedSeconds = global::System.Math.Max(0d, elapsedSeconds - acceptedHostSeconds) * _timeScale;
        if (!double.IsFinite(droppedSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), "Scaled dropped time must remain finite.");
        }
        _accumulatorSeconds += scaledSeconds;

        var executedSteps = 0;
        _isAdvancing = true;
        try
        {
            var epsilon = _fixedDeltaSeconds * 1e-9d;
            while (!_isPaused && executedSteps < _maximumCatchUpSteps && _accumulatorSeconds + epsilon >= _fixedDeltaSeconds)
            {
                ExecuteFixedStep();
                _accumulatorSeconds -= _fixedDeltaSeconds;
                if (_accumulatorSeconds < 0d && _accumulatorSeconds > -epsilon) _accumulatorSeconds = 0d;
                executedSteps++;
            }
        }
        catch (Exception exception)
        {
            _fault = exception;
            PublishInterpolationAlpha(1d);
            EngineLog3D.Error("UpdateLoop", $"Scene simulation faulted at tick {_simulationTick + 1}; automatic updates stopped.", exception);
            OnStateChanged();
            throw;
        }
        finally
        {
            _isAdvancing = false;
        }

        var droppedSteps = 0;
        if (_accumulatorSeconds >= _fixedDeltaSeconds)
        {
            var epsilon = _fixedDeltaSeconds * 1e-9d;
            droppedSteps = (int)global::System.Math.Floor((_accumulatorSeconds + epsilon) / _fixedDeltaSeconds);
            var droppedCatchUpSeconds = droppedSteps * _fixedDeltaSeconds;
            _accumulatorSeconds -= droppedCatchUpSeconds;
            if (_accumulatorSeconds < 0d && _accumulatorSeconds > -epsilon) _accumulatorSeconds = 0d;
            droppedSeconds += droppedCatchUpSeconds;
        }

        _totalDroppedSeconds += droppedSeconds;
        var alpha = _isPaused ? 1d : CalculateInterpolationAlpha();
        PublishInterpolationAlpha(alpha);
        _lastResult = new SceneUpdateResult3D(
            executedSteps,
            droppedSteps,
            elapsedSeconds,
            executedSteps * _fixedDeltaSeconds,
            droppedSeconds,
            alpha,
            _simulationTick,
            _simulationTimeSeconds);

        if (droppedSeconds > 0d)
        {
            ReportDroppedTime(executedSteps, droppedSteps, droppedSeconds);
        }

        return _lastResult;
    }

    /// <summary>Executes exactly one fixed tick, including while paused.</summary>
    public SceneUpdateResult3D StepOnce()
    {
        ThrowIfDisposed();
        using var access = _scene.EnterMutationScope();
        if (_isAdvancing)
        {
            throw new InvalidOperationException("SceneUpdateLoop3D.StepOnce cannot be called recursively.");
        }

        PumpQueuedCommandsWithFaultCapture();
        ThrowIfFaulted();

        _isAdvancing = true;
        try
        {
            ExecuteFixedStep();
        }
        catch (Exception exception)
        {
            _fault = exception;
            PublishInterpolationAlpha(1d);
            EngineLog3D.Error("UpdateLoop", $"Scene simulation faulted at tick {_simulationTick + 1}; automatic updates stopped.", exception);
            OnStateChanged();
            throw;
        }
        finally
        {
            _isAdvancing = false;
        }

        PublishInterpolationAlpha(1d);
        _lastResult = new SceneUpdateResult3D(
            1,
            0,
            0d,
            _fixedDeltaSeconds,
            0d,
            1d,
            _simulationTick,
            _simulationTimeSeconds);
        return _lastResult;
    }

    /// <summary>
    /// Clears fractional accumulated time. Simulation tick/time are retained unless
    /// <paramref name="resetTimeline"/> is true.
    /// </summary>
    public void Reset(bool resetTimeline = true)
    {
        ThrowIfDisposed();
        using var access = _scene.EnterMutationScope();
        ThrowIfConfigurationChangeDuringAdvance();
        _accumulatorSeconds = 0d;
        _fault = null;
        if (resetTimeline)
        {
            _simulationTick = 0;
            _simulationTimeSeconds = 0d;
            _totalDroppedSeconds = 0d;
            _lastDropLogTick = long.MinValue;
            _suppressedDropReports = 0;
            _suppressedDroppedSeconds = 0d;
        }
        _scene.FrameInterpolator.Reset();
        _lastResult = SceneUpdateResult3D.Idle(0d, 1d, _simulationTick, _simulationTimeSeconds);
        OnStateChanged();
    }

    public void ResetFault(bool discardAccumulatedTime = true)
    {
        ThrowIfDisposed();
        using var access = _scene.EnterMutationScope();
        ThrowIfConfigurationChangeDuringAdvance();
        if (_fault is null) return;
        _fault = null;
        if (discardAccumulatedTime) DiscardAccumulatorCore(countAsDropped: true);
        EngineLog3D.Information("UpdateLoop", $"Scene simulation fault state reset at tick {_simulationTick}.");
        OnStateChanged();
    }

    public void DiscardAccumulatedTime()
    {
        ThrowIfDisposed();
        using var access = _scene.EnterMutationScope();
        ThrowIfConfigurationChangeDuringAdvance();
        DiscardAccumulatorCore(countAsDropped: true);
        PublishInterpolationAlpha(_isPaused ? 1d : 0d);
    }

    /// <summary>Executes queued scene commands without advancing simulation time.</summary>
    public int PumpCommands()
    {
        ThrowIfDisposed();
        using var access = _scene.EnterMutationScope();
        ThrowIfConfigurationChangeDuringAdvance();
        return PumpQueuedCommandsWithFaultCapture();
    }

    internal void NotifyActivityChanged() => OnStateChanged();

    internal void DisposeFromScene()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        StateChanged = null;
    }


    private int PumpQueuedCommandsWithFaultCapture()
    {
        try
        {
            return _scene.PumpQueuedCommands();
        }
        catch (Exception exception)
        {
            _fault = exception;
            PublishInterpolationAlpha(1d);
            EngineLog3D.Error("UpdateLoop", $"A queued scene command faulted simulation at tick {_simulationTick}.", exception);
            OnStateChanged();
            throw;
        }
    }

    private void ExecuteFixedStep()
    {
        var nextTick = checked(_simulationTick + 1);
        var nextTime = _simulationTimeSeconds + _fixedDeltaSeconds;
        _scene.ExecuteFixedUpdate(
            new SceneFixedUpdateContext3D(nextTick, (float)_fixedDeltaSeconds, nextTime),
            AdvanceAnimations,
            AdvancePhysics,
            AdvanceParticles);
        _simulationTick = nextTick;
        _simulationTimeSeconds = nextTime;
    }

    private double CalculateInterpolationAlpha()
        => global::System.Math.Clamp(_accumulatorSeconds / _fixedDeltaSeconds, 0d, 1d);

    private void PublishInterpolationAlpha(double alpha)
        => _scene.FrameInterpolator.SetAlpha(alpha);

    private void DiscardAccumulatorCore(bool countAsDropped)
    {
        if (countAsDropped) _totalDroppedSeconds += _accumulatorSeconds;
        _accumulatorSeconds = 0d;
    }

    private void ThrowIfFaulted()
    {
        if (_fault is not null)
        {
            throw new InvalidOperationException(
                "Scene update loop is faulted. Inspect Fault and call ResetFault only after correcting the cause.",
                _fault);
        }
    }

    private void ReportDroppedTime(int executedSteps, int droppedSteps, double droppedSeconds)
    {
        const long minimumTicksBetweenLogs = 120;
        if (_lastDropLogTick != long.MinValue && _simulationTick - _lastDropLogTick < minimumTicksBetweenLogs)
        {
            _suppressedDropReports++;
            _suppressedDroppedSeconds += droppedSeconds;
            return;
        }

        var suppressedSuffix = _suppressedDropReports == 0
            ? string.Empty
            : $", suppressedReports={_suppressedDropReports}, suppressedDropped={_suppressedDroppedSeconds:0.######}s";
        EngineLog3D.Warning(
            "UpdateLoop",
            $"Simulation catch-up bounded: executed={executedSteps}, droppedSteps={droppedSteps}, dropped={droppedSeconds:0.######}s, tick={_simulationTick}{suppressedSuffix}.");
        _lastDropLogTick = _simulationTick;
        _suppressedDropReports = 0;
        _suppressedDroppedSeconds = 0d;
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_isDisposed || _scene.IsDisposed, this);

    private void ThrowIfConfigurationChangeDuringAdvance()
    {
        if (_isAdvancing)
        {
            throw new InvalidOperationException("Update-loop configuration cannot change from inside a fixed tick.");
        }
    }

    private void OnStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
