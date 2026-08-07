using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ThreeDEngine.Core.Assets.Models;
using ThreeDEngine.Core.Diagnostics;
using ThreeDEngine.Core.Hosting;

namespace ThreeDEngine.Core.Assets.Streaming;

/// <summary>
/// Engine-scoped prioritized model streaming and CPU residency manager. Requests are coalesced by
/// canonical path/options key. Resident assets are retained by leases and otherwise evicted LRU
/// when the configured budget is exceeded.
/// </summary>
public sealed class AssetManager3D : IDisposable
{
    private readonly object _gate = new();
    private readonly PriorityQueue<QueueItem, QueuePriority> _queue = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _queueSignal = new(0);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task[] _workers;
    private readonly Task _workersCompletion;
    private readonly TaskCompletionSource<bool> _shutdownCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IEngineServiceProvider3D _services;
    private readonly AssetStreamingConfiguration3D _configuration;
    private long _queueSequence;
    private int _activeLoads;
    private int _backgroundOperations;
    private long _residentBytes;
    private long _cacheHits;
    private long _cacheMisses;
    private long _coalesced;
    private long _evictions;
    private long _failed;
    private long _completed;
    private int _synchronizationDisposed;
    private bool _disposed;

    internal AssetManager3D(
        IEngineServiceProvider3D services,
        AssetStreamingConfiguration3D configuration,
        ContentAddressedAssetCache3D contentCache)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        ContentCache = contentCache ?? throw new ArgumentNullException(nameof(contentCache));
        _workers = new Task[_configuration.MaximumConcurrentLoads];
        for (var i = 0; i < _workers.Length; i++)
        {
            var workerId = i;
            _workers[i] = Task.Run(() => WorkerLoopAsync(workerId, _shutdown.Token));
        }
        _workersCompletion = Task.WhenAll(_workers);
    }

    public ContentAddressedAssetCache3D ContentCache { get; }
    internal Task ShutdownCompletion => _shutdownCompletion.Task;

    public AssetStreamingStatistics3D Statistics
    {
        get
        {
            lock (_gate)
            {
                var pinned = 0;
                var resident = 0;
                foreach (var entry in _entries.Values)
                {
                    if (entry.State == AssetResidencyState3D.Resident) resident++;
                    if (entry.PinCount > 0) pinned++;
                }
                return new AssetStreamingStatistics3D(
                    _queue.Count,
                    _activeLoads,
                    resident,
                    pinned,
                    _residentBytes,
                    _configuration.CpuResidentByteBudget,
                    _cacheHits,
                    _cacheMisses,
                    _coalesced,
                    _evictions,
                    _failed,
                    _completed,
                    ContentCache.ResidentBytes,
                    ContentCache.Count);
            }
        }
    }

    public async ValueTask<ModelAsset3D> LoadModelAsync(
        string path,
        ModelImportOptions? options = null,
        AssetLoadPriority3D priority = AssetLoadPriority3D.Normal,
        CancellationToken cancellationToken = default)
    {
        var entry = QueueModel(path, options, priority, reserveLease: false);
        return await entry.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<AssetLease3D> AcquireModelAsync(
        string path,
        ModelImportOptions? options = null,
        AssetLoadPriority3D priority = AssetLoadPriority3D.Normal,
        CancellationToken cancellationToken = default)
    {
        var entry = QueueModel(path, options, priority, reserveLease: true);
        try
        {
            var asset = await entry.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                ThrowIfDisposed();
                if (!_entries.TryGetValue(entry.Key, out var current) || !ReferenceEquals(current, entry) || current.State != AssetResidencyState3D.Resident || current.PinCount <= 0)
                    throw new InvalidOperationException($"Asset '{entry.Key}' did not retain its reserved lease through residency publication.");
                current.LastAccessTimestamp = Stopwatch.GetTimestamp();
            }
            return new AssetLease3D(this, entry.Key, asset);
        }
        catch
        {
            CancelReservedLease(entry);
            throw;
        }
    }

    public AssetResidencyState3D GetState(string path, ModelImportOptions? options = null)
    {
        ThrowIfDisposed();
        var key = BuildKey(path, options);
        lock (_gate) return _entries.TryGetValue(key, out var entry) ? entry.State : AssetResidencyState3D.Unloaded;
    }

    public bool Evict(string path, ModelImportOptions? options = null)
    {
        ThrowIfDisposed();
        var key = BuildKey(path, options);
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry) || entry.PinCount != 0 || entry.State != AssetResidencyState3D.Resident) return false;
            EvictCore(entry);
            return true;
        }
    }

    public void Trim()
    {
        ThrowIfDisposed();
        lock (_gate) TrimCore();
    }

    public void ClearUnpinned()
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            var candidates = new List<Entry>();
            foreach (var entry in _entries.Values)
                if (entry.State == AssetResidencyState3D.Resident && entry.PinCount == 0) candidates.Add(entry);
            for (var i = 0; i < candidates.Count; i++) EvictCore(candidates[i]);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        try
        {
            _shutdown.Cancel();
        }
        catch (AggregateException exception)
        {
            EngineLog3D.Warning("AssetStreaming", $"{exception.InnerExceptions.Count} cancellation callback(s) failed during shutdown; cleanup continues.");
        }
        _queueSignal.Release(_workers.Length);
        var workersStopped = _workersCompletion.IsCompleted;
        if (!OperatingSystem.IsBrowser() && !workersStopped)
        {
            try
            {
                workersStopped = Task.WaitAll(_workers, _configuration.ShutdownTimeout);
                if (!workersStopped)
                    EngineLog3D.Warning("AssetStreaming", $"Asset worker shutdown exceeded {_configuration.ShutdownTimeout.TotalMilliseconds:0} ms; cleanup will complete asynchronously after cancellation unwinds.");
            }
            catch (AggregateException ex)
            {
                workersStopped = true;
                EngineLog3D.Warning("AssetStreaming", $"Asset workers stopped with {ex.InnerExceptions.Count} exception(s).");
            }
        }
        else if (OperatingSystem.IsBrowser() && !workersStopped)
        {
            EngineLog3D.Information("AssetStreaming", "Browser asset shutdown was made non-blocking; service dependencies remain alive until workers observe cancellation.");
        }

        if (workersStopped)
        {
            DisposeSynchronization();
        }
        else
        {
            _ = _workersCompletion.ContinueWith(
                static (_, state) =>
                {
                    var owner = (AssetManager3D)state!;
                    owner.DisposeSynchronization();
                    owner.TryCompleteShutdown();
                },
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        lock (_gate)
        {
            foreach (var entry in _entries.Values)
            {
                entry.Completion.TrySetException(new ObjectDisposedException(nameof(AssetManager3D)));
            }
            _entries.Clear();
            _queue.Clear();
            _residentBytes = 0;
        }
        TryCompleteShutdown();
    }

    private void CancelReservedLease(Entry entry)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(entry.Key, out var current) || !ReferenceEquals(current, entry)) return;
            if (current.State == AssetResidencyState3D.Resident)
            {
                if (current.PinCount > 0) current.PinCount--;
                current.LastAccessTimestamp = Stopwatch.GetTimestamp();
                try { TrimCore(); }
                catch (InvalidOperationException exception)
                {
                    EngineLog3D.Warning("AssetStreaming", $"Residency remained above budget while unwinding a failed lease for '{entry.Path}': {exception.Message}");
                }
                return;
            }
            if (current.ReservedLeaseCount > 0) current.ReservedLeaseCount--;
        }
    }

    internal void ReleaseLease(string key)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry)) return;
            if (entry.PinCount <= 0) throw new InvalidOperationException($"Asset lease underflow for '{key}'.");
            entry.PinCount--;
            entry.LastAccessTimestamp = Stopwatch.GetTimestamp();
            TrimCore();
        }
    }

    private Entry QueueModel(string path, ModelImportOptions? options, AssetLoadPriority3D priority, bool reserveLease)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Model path cannot be empty.", nameof(path));
        if (!Enum.IsDefined(priority)) throw new ArgumentOutOfRangeException(nameof(priority));
        path = path.Trim();
        options = options?.Clone();
        var key = BuildKey(path, options);
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_entries.TryGetValue(key, out var existing))
            {
                existing.LastAccessTimestamp = Stopwatch.GetTimestamp();
                if (existing.State == AssetResidencyState3D.Resident)
                {
                    _cacheHits++;
                    if (reserveLease) existing.PinCount = checked(existing.PinCount + 1);
                    return existing;
                }
                if (existing.State is AssetResidencyState3D.Queued or AssetResidencyState3D.Loading)
                {
                    _coalesced++;
                    if (reserveLease) existing.ReservedLeaseCount = checked(existing.ReservedLeaseCount + 1);
                    if (existing.State == AssetResidencyState3D.Queued && priority > existing.Priority)
                    {
                        existing.Priority = priority;
                        existing.QueueVersion = checked(existing.QueueVersion + 1);
                        EnqueueCore(existing);
                    }
                    return existing;
                }
                _entries.Remove(key);
            }

            if (_queue.Count >= _configuration.MaximumQueuedRequests)
                throw new InvalidOperationException($"Asset request queue capacity {_configuration.MaximumQueuedRequests} is exhausted. No synchronous load fallback is allowed.");
            var created = new Entry(key, path, options, priority) { ReservedLeaseCount = reserveLease ? 1 : 0 };
            _entries.Add(key, created);
            _cacheMisses++;
            EnqueueCore(created);
            return created;
        }
    }

    private void EnqueueCore(Entry entry)
    {
        entry.State = AssetResidencyState3D.Queued;
        var sequence = _queueSequence = checked(_queueSequence + 1);
        _queue.Enqueue(new QueueItem(entry, entry.QueueVersion), new QueuePriority(-(int)entry.Priority, sequence));
        _queueSignal.Release();
    }

    private async Task WorkerLoopAsync(int workerId, CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                await _queueSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            QueueItem item;
            lock (_gate)
            {
                if (_disposed) return;
                if (!_queue.TryDequeue(out item, out _)) continue;
                if (item.Version != item.Entry.QueueVersion || item.Entry.State != AssetResidencyState3D.Queued) continue;
                item.Entry.State = AssetResidencyState3D.Loading;
                _activeLoads++;
            }

            var started = Stopwatch.GetTimestamp();
            try
            {
                var asset = await LoadCoreAsync(item.Entry, cancellationToken).ConfigureAwait(false);
                var estimatedBytes = EstimateBytes(asset);
                lock (_gate)
                {
                    if (_disposed) return;
                    var committedPinCount = checked(item.Entry.PinCount + item.Entry.ReservedLeaseCount);
                    var committedResidentBytes = checked(_residentBytes + estimatedBytes);
                    item.Entry.Asset = asset;
                    item.Entry.EstimatedBytes = estimatedBytes;
                    item.Entry.State = AssetResidencyState3D.Resident;
                    item.Entry.LastAccessTimestamp = Stopwatch.GetTimestamp();
                    item.Entry.PinCount = committedPinCount;
                    item.Entry.ReservedLeaseCount = 0;
                    _residentBytes = committedResidentBytes;
                    try
                    {
                        TrimCore();
                    }
                    catch
                    {
                        if (_entries.TryGetValue(item.Entry.Key, out var current) && ReferenceEquals(current, item.Entry))
                        {
                            _residentBytes -= item.Entry.EstimatedBytes;
                            item.Entry.Asset = null;
                            item.Entry.EstimatedBytes = 0;
                            item.Entry.PinCount = 0;
                            item.Entry.State = AssetResidencyState3D.Faulted;
                        }
                        throw;
                    }
                    _completed++;
                    item.Entry.Completion.TrySetResult(asset);
                }
                var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                EngineLog3D.Information("AssetStreaming", $"Worker {workerId} loaded '{item.Entry.Path}' in {elapsed:0.##} ms; estimatedResidentBytes={estimatedBytes}.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                item.Entry.Completion.TrySetCanceled(cancellationToken);
                return;
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    if (_entries.TryGetValue(item.Entry.Key, out var current) && ReferenceEquals(current, item.Entry))
                    {
                        item.Entry.State = AssetResidencyState3D.Faulted;
                        item.Entry.Fault = ex;
                    }
                    _failed++;
                    item.Entry.Completion.TrySetException(ex);
                }
                EngineLog3D.Error("AssetStreaming", $"Failed to load '{item.Entry.Path}'.", ex);
            }
            finally
            {
                lock (_gate) _activeLoads--;
            }
        }
    }

    private async ValueTask<ModelAsset3D> LoadCoreAsync(Entry entry, CancellationToken cancellationToken)
    {
        var loader = _services.GetRequiredService<IModelAssetLoader3D>();
        if (loader is IAsyncModelAssetLoader3D asynchronous)
        {
            return await asynchronous.LoadAsync(entry.Path, entry.Options, cancellationToken).ConfigureAwait(false);
        }
        if (OperatingSystem.IsBrowser() && _configuration.RejectSynchronousLoaderInBrowser)
        {
            throw new InvalidOperationException(
                $"Registered model loader '{loader.GetType().FullName}' is synchronous. Browser asset streaming requires IAsyncModelAssetLoader3D; blocking the browser UI thread is prohibited.");
        }
        return await Task.Run(() => loader.Load(entry.Path, entry.Options), cancellationToken).ConfigureAwait(false);
    }

    private void TrimCore()
    {
        if (_residentBytes <= _configuration.CpuResidentByteBudget) return;
        var candidates = new List<Entry>();
        foreach (var entry in _entries.Values)
            if (entry.State == AssetResidencyState3D.Resident && entry.PinCount == 0) candidates.Add(entry);
        candidates.Sort(static (a, b) => a.LastAccessTimestamp.CompareTo(b.LastAccessTimestamp));
        for (var i = 0; i < candidates.Count && _residentBytes > _configuration.CpuResidentByteBudget; i++)
            EvictCore(candidates[i]);
        if (_residentBytes > _configuration.CpuResidentByteBudget)
            throw new InvalidOperationException($"CPU asset residency {_residentBytes} exceeds budget {_configuration.CpuResidentByteBudget}; all remaining models are pinned or loading. Synchronous eviction of pinned assets and quality reduction are prohibited.");
    }

    private void EvictCore(Entry entry)
    {
        if (entry.Asset is null || entry.State != AssetResidencyState3D.Resident || entry.PinCount != 0) return;
        _residentBytes -= entry.EstimatedBytes;
        entry.Asset = null;
        entry.State = AssetResidencyState3D.Evicted;
        _entries.Remove(entry.Key);
        _evictions++;
        ScheduleLoaderCacheRemoval(entry);
    }

    private void ScheduleLoaderCacheRemoval(Entry entry)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _backgroundOperations = checked(_backgroundOperations + 1);
        }
        var queued = ThreadPool.QueueUserWorkItem(static state =>
        {
            var work = (LoaderRemovalWork)state!;
            try
            {
                work.Owner.RemoveLoaderCacheIfStillEvicted(work.Entry);
            }
            finally
            {
                work.Owner.CompleteBackgroundOperation();
            }
        }, new LoaderRemovalWork(this, entry));
        if (!queued)
        {
            CompleteBackgroundOperation();
            EngineLog3D.Warning("AssetStreaming", $"Could not queue loader-cache removal for '{entry.Path}'. The cache entry remains owned by its loader until loader disposal.");
        }
    }

    private void RemoveLoaderCacheIfStillEvicted(Entry entry)
    {
        lock (_gate)
        {
            if (_disposed || _entries.ContainsKey(entry.Key)) return;
        }
        try
        {
            if (_services.TryGetService<IModelAssetLoader3D>(out var loader) && loader is not null)
                loader.Remove(entry.Path, entry.Options?.BaseDirectory);
        }
        catch (Exception exception)
        {
            EngineLog3D.Warning("AssetStreaming", $"Failed to release loader cache entry for '{entry.Path}': {exception.Message}");
        }
    }

    private static long EstimateBytes(ModelAsset3D asset)
    {
        long bytes = 0;
        for (var meshIndex = 0; meshIndex < asset.Meshes.Count; meshIndex++)
        {
            var mesh = asset.Meshes[meshIndex];
            for (var primitiveIndex = 0; primitiveIndex < mesh.Primitives.Count; primitiveIndex++)
            {
                var geometry = mesh.Primitives[primitiveIndex].RenderGeometry;
                bytes = checked(bytes + geometry.VertexCount * 12L + geometry.IndexCount * 4L);
                bytes = checked(bytes + geometry.Normals.Length * 12L + geometry.TexCoords0.Length * 8L);
                bytes = checked(bytes + geometry.BoneIndices0.Length * 16L + geometry.BoneWeights0.Length * 16L);
            }
        }
        for (var i = 0; i < asset.Textures.Count; i++) bytes = checked(bytes + (asset.Textures[i].DataInternal?.LongLength ?? 0L));
        return global::System.Math.Max(bytes, 1L);
    }

    private static string BuildKey(string path, ModelImportOptions? options)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Model path cannot be empty.", nameof(path));
        options ??= new ModelImportOptions();
        var builder = new StringBuilder(384);
        builder.Append(NormalizeLogicalPath(options.BaseDirectory, path));
        builder.Append("|resolver=").Append(GetResolverIdentity(options.AssetResolver));
        builder.Append("|extBuf=").Append(options.ResolveExternalBuffers);
        builder.Append("|extImg=").Append(options.ResolveExternalImages);
        builder.Append("|data=").Append(options.ResolveDataUris);
        builder.Append("|sidecar=").Append(options.ResolveSidecarImages);
        builder.Append("|strict=").Append(options.StrictValidation);
        builder.Append("|strictGlb=").Append(options.StrictGlbValidation);
        builder.Append("|normals=").Append(options.GenerateMissingNormals);
        builder.Append("|warningsAsErrors=").Append(options.TreatWarningsAsErrors);
        builder.Append("|maxFile=").Append(options.MaxFileBytes);
        builder.Append("|maxJson=").Append(options.MaxJsonBytes);
        builder.Append("|maxBin=").Append(options.MaxBinaryChunkBytes);
        builder.Append("|maxTex=").Append(options.MaxTextureBytes);
        builder.Append("|maxV=").Append(options.MaxVerticesPerPrimitive);
        builder.Append("|maxI=").Append(options.MaxIndicesPerPrimitive);
        return builder.ToString();
    }

    private static string NormalizeLogicalPath(string? baseDirectory, string path)
    {
        path = path.Trim();
        if (global::System.IO.Path.IsPathRooted(path)) return NormalizeFilePath(path);

        var normalizedPath = path.Replace('\\', '/');
        if (Uri.TryCreate(normalizedPath, UriKind.Absolute, out var absoluteUri))
        {
            if (absoluteUri.IsFile) return NormalizeFilePath(absoluteUri.LocalPath);
            return absoluteUri.GetComponents(UriComponents.AbsoluteUri, UriFormat.SafeUnescaped);
        }

        var normalizedBase = string.IsNullOrWhiteSpace(baseDirectory)
            ? string.Empty
            : baseDirectory.Trim().Replace('\\', '/').TrimEnd('/');
        var combined = normalizedBase.Length == 0
            ? normalizedPath
            : normalizedBase + "/" + normalizedPath.TrimStart('/');

        if (global::System.IO.Path.IsPathRooted(combined)) return NormalizeFilePath(combined);
        if (Uri.TryCreate(combined, UriKind.Absolute, out var uri))
        {
            if (uri.IsFile) return NormalizeFilePath(uri.LocalPath);
            return uri.GetComponents(UriComponents.AbsoluteUri, UriFormat.SafeUnescaped);
        }

        var parts = combined.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new List<string>(parts.Length);
        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];
            if (part == ".") continue;
            if (part == "..")
            {
                if (stack.Count == 0) throw new ArgumentException("Model path escapes its logical base directory.", nameof(path));
                stack.RemoveAt(stack.Count - 1);
                continue;
            }
            stack.Add(part);
        }
        if (stack.Count == 0) throw new ArgumentException("Model path resolves to an empty location.", nameof(path));
        return string.Join('/', stack);
    }

    private static string NormalizeFilePath(string path)
    {
        var normalized = global::System.IO.Path.GetFullPath(path).Replace('\\', '/');
        return OperatingSystem.IsWindows() ? normalized.ToUpperInvariant() : normalized;
    }

    private static string GetResolverIdentity(object? resolver)
    {
        if (resolver is null) return "<none>";
        var type = resolver.GetType();
        return (type.AssemblyQualifiedName ?? type.FullName ?? type.Name) + "#" + RuntimeHelpers.GetHashCode(resolver).ToString("X8");
    }


    private void CompleteBackgroundOperation()
    {
        lock (_gate)
        {
            if (_backgroundOperations <= 0) throw new InvalidOperationException("Asset streaming background-operation accounting underflow.");
            _backgroundOperations--;
        }
        TryCompleteShutdown();
    }

    private void TryCompleteShutdown()
    {
        var complete = false;
        lock (_gate)
        {
            complete = _disposed && _workersCompletion.IsCompleted && _backgroundOperations == 0 && Volatile.Read(ref _synchronizationDisposed) != 0;
        }
        if (!complete) return;
        if (_workersCompletion.IsFaulted && _workersCompletion.Exception is not null)
            _shutdownCompletion.TrySetException(_workersCompletion.Exception.Flatten().InnerExceptions);
        else if (_workersCompletion.IsCanceled)
            _shutdownCompletion.TrySetCanceled();
        else
            _shutdownCompletion.TrySetResult(true);
    }

    private void DisposeSynchronization()
    {
        if (Interlocked.Exchange(ref _synchronizationDisposed, 1) != 0) return;
        _queueSignal.Dispose();
        _shutdown.Dispose();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);

    private sealed class Entry
    {
        public Entry(string key, string path, ModelImportOptions? options, AssetLoadPriority3D priority)
        {
            Key = key;
            Path = path;
            Options = options;
            Priority = priority;
            Completion = new TaskCompletionSource<ModelAsset3D>(TaskCreationOptions.RunContinuationsAsynchronously);
            State = AssetResidencyState3D.Queued;
            LastAccessTimestamp = Stopwatch.GetTimestamp();
        }
        public string Key { get; }
        public string Path { get; }
        public ModelImportOptions? Options { get; }
        public TaskCompletionSource<ModelAsset3D> Completion { get; }
        public AssetLoadPriority3D Priority { get; set; }
        public AssetResidencyState3D State { get; set; }
        public ModelAsset3D? Asset { get; set; }
        public Exception? Fault { get; set; }
        public int PinCount { get; set; }
        public int ReservedLeaseCount { get; set; }
        public int QueueVersion { get; set; }
        public long EstimatedBytes { get; set; }
        public long LastAccessTimestamp { get; set; }
    }

    private readonly record struct LoaderRemovalWork(AssetManager3D Owner, Entry Entry);
    private readonly record struct QueueItem(Entry Entry, int Version);
    private readonly record struct QueuePriority(int Priority, long Sequence) : IComparable<QueuePriority>
    {
        public int CompareTo(QueuePriority other)
        {
            var priority = Priority.CompareTo(other.Priority);
            return priority != 0 ? priority : Sequence.CompareTo(other.Sequence);
        }
    }
}
