using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace ThreeDEngine.Core.Interaction;

public readonly record struct GpuPickRequest3D(
    long RequestId,
    float NormalizedX,
    float NormalizedY,
    float MaximumDistance,
    uint LayerMask = uint.MaxValue)
{
    public GpuPickRequest3D Validate()
    {
        if (RequestId <= 0) throw new ArgumentOutOfRangeException(nameof(RequestId));
        if (!float.IsFinite(NormalizedX) || NormalizedX < 0f || NormalizedX > 1f) throw new ArgumentOutOfRangeException(nameof(NormalizedX));
        if (!float.IsFinite(NormalizedY) || NormalizedY < 0f || NormalizedY > 1f) throw new ArgumentOutOfRangeException(nameof(NormalizedY));
        if (!float.IsFinite(MaximumDistance) || MaximumDistance <= 0f) throw new ArgumentOutOfRangeException(nameof(MaximumDistance));
        return this;
    }
}

public readonly record struct GpuPickResult3D(
    long RequestId,
    bool HasHit,
    string? ObjectId,
    float Distance,
    Vector3 WorldPosition,
    Vector3 WorldNormal,
    int PrimitiveIndex,
    int InstanceIndex)
{
    public static GpuPickResult3D Miss(long requestId)
        => new(requestId, false, null, float.PositiveInfinity, default, default, -1, -1);

    public GpuPickResult3D Validate()
    {
        if (RequestId <= 0) throw new ArgumentOutOfRangeException(nameof(RequestId));
        if (!HasHit) return this with { ObjectId = null, Distance = float.PositiveInfinity, PrimitiveIndex = -1, InstanceIndex = -1 };
        if (string.IsNullOrWhiteSpace(ObjectId)) throw new InvalidOperationException("A GPU pick hit requires a stable object id.");
        if (!float.IsFinite(Distance) || Distance < 0f) throw new InvalidOperationException("A GPU pick hit requires a finite non-negative distance.");
        if (!IsFinite(WorldPosition) || !IsFinite(WorldNormal)) throw new InvalidOperationException("GPU pick coordinates must be finite.");
        var normalLengthSquared = WorldNormal.LengthSquared();
        if (!float.IsFinite(normalLengthSquared) || normalLengthSquared < 0.000001f)
            throw new InvalidOperationException("A GPU pick hit requires a finite non-zero world normal.");
        return this with { ObjectId = ObjectId.Trim(), WorldNormal = Vector3.Normalize(WorldNormal) };
    }

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}

/// <summary>
/// Native GPU picking backend. Implementations must use a GPU visibility/id/depth readback path;
/// CPU ray tests and registry scans are explicitly outside this contract.
/// </summary>
public interface IGpuPickingBackend3D
{
    string Name { get; }
    int MaximumBatchSize { get; }
    ValueTask<IReadOnlyList<GpuPickResult3D>> ExecuteAsync(
        IReadOnlyList<GpuPickRequest3D> requests,
        CancellationToken cancellationToken = default);
}

public readonly record struct GpuPickingStatistics3D(
    string Backend,
    int PendingRequests,
    long SubmittedRequests,
    long CompletedRequests,
    long CancelledRequests,
    long FailedRequests,
    long BatchCount,
    int MaximumObservedBatchSize,
    double LastBatchMilliseconds);

/// <summary>
/// Engine-scoped asynchronous GPU picking queue. No backend means picking fails explicitly;
/// the service never redirects requests to CPU raycasting.
/// </summary>
public sealed class GpuPickingService3D : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<long, PendingRequest> _pending = new();
    private readonly SemaphoreSlim _dispatchGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TaskCompletionSource<bool> _shutdownCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IGpuPickingBackend3D? _backend;
    private long _nextRequestId;
    private long _submitted;
    private long _completed;
    private long _cancelled;
    private long _failed;
    private long _batches;
    private int _maximumBatch;
    private double _lastBatchMilliseconds;
    private bool _dispatchScheduled;
    private int _synchronizationDisposed;
    private bool _disposed;

    internal Task ShutdownCompletion => _shutdownCompletion.Task;
    public bool IsAvailable { get { lock (_gate) return _backend is not null && !_disposed; } }
    public string BackendName { get { lock (_gate) return _backend?.Name ?? "unavailable"; } }

    public GpuPickingStatistics3D Statistics
    {
        get
        {
            lock (_gate)
                return new GpuPickingStatistics3D(_backend?.Name ?? "unavailable", _pending.Count, _submitted, _completed, _cancelled, _failed, _batches, _maximumBatch, _lastBatchMilliseconds);
        }
    }

    public void AttachBackend(IGpuPickingBackend3D backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        if (string.IsNullOrWhiteSpace(backend.Name)) throw new ArgumentException("GPU picking backend name cannot be empty.", nameof(backend));
        if (backend.MaximumBatchSize <= 0) throw new ArgumentOutOfRangeException(nameof(backend), "GPU picking backend batch size must be positive.");
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_backend is not null) throw new InvalidOperationException($"GPU picking backend '{_backend.Name}' is already attached.");
            _backend = backend;
        }
    }

    public void DetachBackend(IGpuPickingBackend3D backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!ReferenceEquals(_backend, backend)) throw new InvalidOperationException("The supplied GPU picking backend is not attached.");
            if (_pending.Count != 0) throw new InvalidOperationException("Cannot detach a GPU picking backend while requests are pending.");
            _backend = null;
        }
    }

    public ValueTask<GpuPickResult3D> PickAsync(
        float normalizedX,
        float normalizedY,
        float maximumDistance = float.MaxValue,
        uint layerMask = uint.MaxValue,
        CancellationToken cancellationToken = default)
    {
        IGpuPickingBackend3D backend;
        PendingRequest pending;
        var startDispatch = false;
        lock (_gate)
        {
            ThrowIfDisposed();
            backend = _backend ?? throw new InvalidOperationException("GPU picking is unavailable because no native GPU picking backend is attached. CPU fallback is prohibited.");
            var request = new GpuPickRequest3D(checked(++_nextRequestId), normalizedX, normalizedY, maximumDistance, layerMask).Validate();
            pending = new PendingRequest(request, cancellationToken);
            _pending.Add(request.RequestId, pending);
            _submitted++;
            if (!_dispatchScheduled)
            {
                _dispatchScheduled = true;
                startDispatch = true;
            }
        }
        if (startDispatch) _ = DispatchAsync(backend);
        return new ValueTask<GpuPickResult3D>(pending.Completion.Task);
    }

    public void Dispose()
    {
        PendingRequest[] pending;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _backend = null;
            pending = new PendingRequest[_pending.Count];
            _pending.Values.CopyTo(pending, 0);
            _pending.Clear();
        }
        try
        {
            _shutdown.Cancel();
        }
        catch (AggregateException exception)
        {
            ThreeDEngine.Core.Diagnostics.EngineLog3D.Warning("GpuPicking", $"{exception.InnerExceptions.Count} cancellation callback(s) failed during shutdown; cleanup continues.");
        }
        for (var i = 0; i < pending.Length; i++) pending[i].Cancel(new ObjectDisposedException(nameof(GpuPickingService3D)));
        TryDisposeSynchronization();
    }

    private async Task DispatchAsync(IGpuPickingBackend3D expectedBackend)
    {
        try
        {
            await _dispatchGate.WaitAsync(_shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            lock (_gate) _dispatchScheduled = false;
            TryDisposeSynchronization();
            return;
        }

        try
        {
            while (true)
            {
                PendingRequest[] batch;
                lock (_gate)
                {
                    if (_disposed || !ReferenceEquals(_backend, expectedBackend)) return;
                    var capacity = global::System.Math.Min(expectedBackend.MaximumBatchSize, _pending.Count);
                    if (capacity == 0) return;
                    batch = new PendingRequest[capacity];
                    var index = 0;
                    foreach (var item in _pending.Values)
                    {
                        if (item.IsDispatched || item.IsCancellationRequested) continue;
                        item.IsDispatched = true;
                        batch[index++] = item;
                        if (index == capacity) break;
                    }
                    if (index == 0)
                    {
                        CompleteCancelledLocked();
                        return;
                    }
                    if (index != batch.Length) Array.Resize(ref batch, index);
                    _batches++;
                    if (batch.Length > _maximumBatch) _maximumBatch = batch.Length;
                }

                var requests = new GpuPickRequest3D[batch.Length];
                for (var i = 0; i < batch.Length; i++) requests[i] = batch[i].Request;
                var started = global::System.Diagnostics.Stopwatch.GetTimestamp();
                IReadOnlyList<GpuPickResult3D> results;
                try
                {
                    results = await expectedBackend.ExecuteAsync(requests, _shutdown.Token).ConfigureAwait(false);
                    if (results is null || results.Count != requests.Length)
                        throw new InvalidOperationException($"GPU picking backend '{expectedBackend.Name}' returned {results?.Count ?? -1} results for {requests.Length} requests.");
                }
                catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
                {
                    lock (_gate)
                    {
                        for (var i = 0; i < batch.Length; i++)
                        {
                            _pending.Remove(batch[i].Request.RequestId);
                            batch[i].Cancel(new ObjectDisposedException(nameof(GpuPickingService3D)));
                        }
                        _lastBatchMilliseconds = global::System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    }
                    return;
                }
                catch (Exception exception)
                {
                    lock (_gate)
                    {
                        for (var i = 0; i < batch.Length; i++)
                        {
                            _pending.Remove(batch[i].Request.RequestId);
                            batch[i].Fail(exception);
                            _failed++;
                        }
                        _lastBatchMilliseconds = global::System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    }
                    continue;
                }

                var validated = new GpuPickResult3D[results.Count];
                Exception? validationFailure = null;
                for (var i = 0; i < batch.Length; i++)
                {
                    try
                    {
                        validated[i] = results[i].Validate();
                        if (validated[i].RequestId != batch[i].Request.RequestId)
                            throw new InvalidOperationException($"GPU picking backend '{expectedBackend.Name}' returned out-of-order request id {validated[i].RequestId}; expected {batch[i].Request.RequestId}.");
                    }
                    catch (Exception exception)
                    {
                        validationFailure = exception;
                        break;
                    }
                }

                lock (_gate)
                {
                    for (var i = 0; i < batch.Length; i++)
                    {
                        _pending.Remove(batch[i].Request.RequestId);
                        if (validationFailure is not null)
                        {
                            batch[i].Fail(validationFailure);
                            _failed++;
                        }
                        else if (batch[i].IsCancellationRequested)
                        {
                            batch[i].Cancel();
                            _cancelled++;
                        }
                        else
                        {
                            batch[i].Complete(validated[i]);
                            _completed++;
                        }
                    }
                    _lastBatchMilliseconds = global::System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    CompleteCancelledLocked();
                }
            }
        }
        finally
        {
            _dispatchGate.Release();
            var restart = false;
            lock (_gate)
            {
                _dispatchScheduled = false;
                if (!_disposed && ReferenceEquals(_backend, expectedBackend))
                {
                    foreach (var item in _pending.Values)
                    {
                        if (item.IsDispatched || item.IsCancellationRequested) continue;
                        _dispatchScheduled = true;
                        restart = true;
                        break;
                    }
                    CompleteCancelledLocked();
                }
            }
            if (restart) _ = DispatchAsync(expectedBackend);
            else TryDisposeSynchronization();
        }
    }

    private void TryDisposeSynchronization()
    {
        lock (_gate)
        {
            if (!_disposed || _dispatchScheduled) return;
        }
        if (Interlocked.Exchange(ref _synchronizationDisposed, 1) != 0) return;
        _dispatchGate.Dispose();
        _shutdown.Dispose();
        _shutdownCompletion.TrySetResult(true);
    }

    private void CompleteCancelledLocked()
    {
        List<long>? cancelled = null;
        foreach (var pair in _pending)
        {
            if (!pair.Value.IsDispatched && pair.Value.IsCancellationRequested)
            {
                cancelled ??= new List<long>();
                cancelled.Add(pair.Key);
            }
        }
        if (cancelled is null) return;
        for (var i = 0; i < cancelled.Count; i++)
        {
            var request = _pending[cancelled[i]];
            _pending.Remove(cancelled[i]);
            request.Cancel();
            _cancelled++;
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class PendingRequest
    {
        private readonly CancellationToken _cancellationToken;
        public PendingRequest(GpuPickRequest3D request, CancellationToken cancellationToken)
        {
            Request = request;
            _cancellationToken = cancellationToken;
            Completion = new TaskCompletionSource<GpuPickResult3D>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        public GpuPickRequest3D Request { get; }
        public TaskCompletionSource<GpuPickResult3D> Completion { get; }
        public bool IsDispatched { get; set; }
        public bool IsCancellationRequested => _cancellationToken.IsCancellationRequested;
        public void Complete(GpuPickResult3D result) => Completion.TrySetResult(result);
        public void Fail(Exception exception) => Completion.TrySetException(exception);
        public void Cancel(Exception? exception = null)
        {
            if (exception is not null) Completion.TrySetException(exception);
            else if (_cancellationToken.CanBeCanceled) Completion.TrySetCanceled(_cancellationToken);
            else Completion.TrySetCanceled();
        }
    }
}
