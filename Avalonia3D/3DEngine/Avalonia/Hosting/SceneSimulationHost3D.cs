using System;
using System.Diagnostics;
using System.Threading;
using ThreeDEngine.Core.Diagnostics;
using ThreeDEngine.Core.Scene;
using ThreeDEngine.Core.World;

namespace ThreeDEngine.Avalonia.Hosting;

/// <summary>
/// Coalesces monotonic host deltas and advances a scene on either the host thread or one
/// dedicated simulation worker. Shutdown is bounded; a stalled user callback can no longer
/// block the Avalonia UI thread indefinitely.
/// </summary>
internal sealed class SceneSimulationHost3D : IDisposable
{
    private const int WorkerShutdownTimeoutMilliseconds = 1000;
    private readonly object _sync = new();
    private readonly AutoResetEvent _wake = new(false);
    private Scene3D _scene;
    private Thread? _thread;
    private double _pendingSeconds;
    private volatile bool _disposed;
    private volatile bool _stop;
    private volatile bool _shutdownTimedOut;
    private SceneSimulationExecutionMode3D _mode;
    private SceneSimulationExecutionMode3D _resolvedMode;
    private long _submitCount;
    private long _wakeCount;
    private long _advanceCount;
    private long _commandPumpCount;
    private long _successfulCycleCount;
    private long _faultCount;
    private long _lastSubmitTimestamp;
    private long _lastWakeTimestamp;
    private long _lastSuccessTimestamp;
    private long _lastFaultTimestamp;
    private Exception? _lastFault;

    public SceneSimulationHost3D(Scene3D scene, SceneSimulationExecutionMode3D mode)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        SetMode(mode);
    }

    public event EventHandler<SceneSimulationFaultedEventArgs3D>? Faulted;

    public SceneSimulationExecutionMode3D Mode => _mode;
    public bool UsesDedicatedThread => Volatile.Read(ref _thread) is not null;
    public bool IsCurrentThreadOwner => _scene.World.IsCurrentThreadSimulationOwner;

    public void SetScene(Scene3D scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfWorkerReconfiguration();
        var mode = _mode;
        StopWorker(throwOnTimeout: true);
        lock (_sync)
        {
            _pendingSeconds = 0d;
            _scene = scene;
            _lastFault = null;
        }
        SetMode(mode);
        EngineLog3D.Information("Simulation", $"Simulation host scene changed; mode={_mode}; resolved={_resolvedMode}.");
    }

    public void SetMode(SceneSimulationExecutionMode3D mode)
    {
        if (!Enum.IsDefined(typeof(SceneSimulationExecutionMode3D), mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfWorkerReconfiguration();
        var resolved = Resolve(mode);
        if (_mode == mode && _resolvedMode == resolved &&
            ((Volatile.Read(ref _thread) is not null) == (resolved == SceneSimulationExecutionMode3D.DedicatedThread))) return;
        StopWorker(throwOnTimeout: true);
        _mode = mode;
        _resolvedMode = resolved;
        if (resolved == SceneSimulationExecutionMode3D.DedicatedThread)
        {
            _stop = false;
            _shutdownTimedOut = false;
            var worker = new Thread(WorkerMain)
            {
                IsBackground = true,
                Name = "Avalonia3D Simulation"
            };
            Volatile.Write(ref _thread, worker);
            worker.Start();
            EngineLog3D.Information("Simulation", $"Dedicated simulation worker started; thread={worker.ManagedThreadId}; configured={mode}; resolved={resolved}.");
        }
        else
        {
            EngineLog3D.Information("Simulation", $"Simulation host uses the host thread; configured={mode}; resolved={resolved}; thread={Environment.CurrentManagedThreadId}.");
        }
    }

    public void Submit(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0d) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        ObjectDisposedException.ThrowIf(_disposed, this);
        Interlocked.Increment(ref _submitCount);
        Volatile.Write(ref _lastSubmitTimestamp, Stopwatch.GetTimestamp());

        var worker = Volatile.Read(ref _thread);
        if (worker is null)
        {
            try
            {
                Interlocked.Increment(ref _advanceCount);
                _scene.Update(elapsedSeconds);
                RecordSuccess();
            }
            catch (Exception exception)
            {
                RecordFault(exception, "Host-thread simulation faulted while advancing the scene.");
                throw;
            }
            return;
        }
        if (!worker.IsAlive)
        {
            var exception = new InvalidOperationException("The dedicated simulation worker is no longer alive.");
            RecordFault(exception, "A simulation submission targeted a dead dedicated worker.");
            throw exception;
        }

        lock (_sync)
        {
            _pendingSeconds += elapsedSeconds;
            if (!double.IsFinite(_pendingSeconds)) throw new InvalidOperationException("Pending simulation time overflowed.");
        }
        WakeWorker();
    }

    public bool TryWakeDedicatedWorker()
    {
        var thread = Volatile.Read(ref _thread);
        if (_disposed || thread is null || !thread.IsAlive) return false;
        return WakeWorker();
    }

    public void PumpCommands()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (TryWakeDedicatedWorker()) return;
        try
        {
            using var owner = _scene.World.EnterTransientOwnerScope();
            Interlocked.Increment(ref _commandPumpCount);
            _scene.UpdateLoop.PumpCommands();
            _scene.World.PublishSnapshot();
            RecordSuccess();
        }
        catch (Exception exception)
        {
            RecordFault(exception, "Host-thread simulation command pump faulted.");
            throw;
        }
    }

    public SceneSimulationHostSnapshot3D CaptureSnapshot()
    {
        var thread = Volatile.Read(ref _thread);
        double pending;
        Exception? fault;
        lock (_sync)
        {
            pending = _pendingSeconds;
            fault = _lastFault;
        }

        return new SceneSimulationHostSnapshot3D(
            _mode,
            _resolvedMode,
            thread is not null,
            thread?.IsAlive == true,
            thread?.ManagedThreadId ?? 0,
            thread?.Name,
            _stop,
            _shutdownTimedOut,
            _disposed,
            pending,
            Interlocked.Read(ref _submitCount),
            Interlocked.Read(ref _wakeCount),
            Interlocked.Read(ref _advanceCount),
            Interlocked.Read(ref _commandPumpCount),
            Interlocked.Read(ref _successfulCycleCount),
            Interlocked.Read(ref _faultCount),
            Volatile.Read(ref _lastSubmitTimestamp),
            Volatile.Read(ref _lastWakeTimestamp),
            Volatile.Read(ref _lastSuccessTimestamp),
            Volatile.Read(ref _lastFaultTimestamp),
            fault?.GetType().FullName,
            fault?.Message);
    }

    private bool WakeWorker()
    {
        try
        {
            Interlocked.Increment(ref _wakeCount);
            Volatile.Write(ref _lastWakeTimestamp, Stopwatch.GetTimestamp());
            _wake.Set();
            return true;
        }
        catch (ObjectDisposedException) when (_disposed)
        {
            return false;
        }
    }

    private void WorkerMain()
    {
        Scene3D scene;
        lock (_sync) scene = _scene;

        EngineLog3D.Information("Simulation", $"Simulation worker entered its loop; thread={Environment.CurrentManagedThreadId}.");
        try
        {
            using var owner = scene.World.BindPersistentOwner();
            scene.World.PublishSnapshot(force: true);

            while (!_stop)
            {
                _wake.WaitOne();
                if (_stop) break;

                double elapsed;
                lock (_sync)
                {
                    elapsed = _pendingSeconds;
                    _pendingSeconds = 0d;
                }

                try
                {
                    if (elapsed > 0d)
                    {
                        Interlocked.Increment(ref _advanceCount);
                        scene.Update(elapsed);
                    }
                    else
                    {
                        Interlocked.Increment(ref _commandPumpCount);
                        scene.UpdateLoop.PumpCommands();
                        scene.World.PublishSnapshot();
                    }
                    RecordSuccess();
                }
                catch (ObjectDisposedException) when (_stop || _disposed)
                {
                    break;
                }
                catch (Exception exception)
                {
                    RecordFault(exception, "Dedicated simulation worker faulted while advancing the scene.");
                }
            }
        }
        catch (ObjectDisposedException) when (_stop || _disposed)
        {
            // Normal teardown may dispose the scene before a newly started worker publishes.
        }
        catch (Exception exception)
        {
            RecordFault(exception, "Dedicated simulation worker failed to bind or initialize world ownership.");
        }
        finally
        {
            EngineLog3D.Information("Simulation", $"Simulation worker exited; thread={Environment.CurrentManagedThreadId}; disposed={_disposed}; stop={_stop}.");
        }
    }

    private void RecordSuccess()
    {
        Interlocked.Increment(ref _successfulCycleCount);
        Volatile.Write(ref _lastSuccessTimestamp, Stopwatch.GetTimestamp());
    }

    private void RecordFault(Exception exception, string message)
    {
        bool isNewFault;
        lock (_sync)
        {
            isNewFault = !ReferenceEquals(_lastFault, exception);
            _lastFault = exception;
        }
        Interlocked.Increment(ref _faultCount);
        Volatile.Write(ref _lastFaultTimestamp, Stopwatch.GetTimestamp());
        if (!isNewFault) return;

        EngineLog3D.Critical("Simulation", message, exception);
        var args = new SceneSimulationFaultedEventArgs3D(exception, CaptureSnapshot());
        try { Faulted?.Invoke(this, args); }
        catch (Exception subscriberException)
        {
            EngineLog3D.Error("Simulation", "Simulation Faulted event subscriber failed.", subscriberException);
        }
    }

    private static SceneSimulationExecutionMode3D Resolve(SceneSimulationExecutionMode3D mode)
    {
        if (OperatingSystem.IsBrowser())
        {
            if (mode == SceneSimulationExecutionMode3D.DedicatedThread)
                throw new PlatformNotSupportedException("Dedicated simulation threads require a threaded runtime and are not available in the browser backend.");
            return SceneSimulationExecutionMode3D.HostThread;
        }
        return mode == SceneSimulationExecutionMode3D.Automatic
            ? SceneSimulationExecutionMode3D.DedicatedThread
            : mode;
    }

    private bool StopWorker(bool throwOnTimeout)
    {
        var thread = Volatile.Read(ref _thread);
        if (thread is null) return true;
        _stop = true;
        WakeWorker();
        if (ReferenceEquals(Thread.CurrentThread, thread))
        {
            EngineLog3D.Information("Simulation", "Dedicated simulation worker stop requested from its owner thread.");
            return true;
        }

        if (!thread.Join(WorkerShutdownTimeoutMilliseconds))
        {
            _shutdownTimedOut = true;
            var exception = new TimeoutException($"Simulation worker did not stop within {WorkerShutdownTimeoutMilliseconds} ms. A fixed-update callback is blocked or running too long.");
            EngineLog3D.Critical("Simulation", "Bounded simulation shutdown timed out; UI-thread blocking was prevented.", exception);
            RecordFault(exception, "Simulation worker shutdown timed out.");
            if (throwOnTimeout) throw exception;
            return false;
        }

        Volatile.Write(ref _thread, null);
        _stop = false;
        _shutdownTimedOut = false;
        lock (_sync) _pendingSeconds = 0d;
        EngineLog3D.Information("Simulation", "Dedicated simulation worker stopped cleanly.");
        return true;
    }

    private void ThrowIfWorkerReconfiguration()
    {
        if (ReferenceEquals(Thread.CurrentThread, Volatile.Read(ref _thread)))
            throw new InvalidOperationException("The simulation host cannot change scene or execution mode from inside its dedicated worker callback.");
    }

    public void Dispose()
    {
        if (_disposed) return;

        // Wake and join before publishing the disposed state. WakeWorker intentionally rejects
        // calls after disposal, so reversing this order would leave an idle worker asleep until
        // the bounded join timed out on every normal control disposal.
        var stopped = StopWorker(throwOnTimeout: false);
        _disposed = true;
        if (stopped) _wake.Dispose();
        else EngineLog3D.Warning("Simulation", "Simulation wait handle was intentionally retained because the timed-out background worker may still exit later.");
    }
}
