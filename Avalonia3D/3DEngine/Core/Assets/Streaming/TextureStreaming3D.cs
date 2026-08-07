using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ThreeDEngine.Core.Hosting;

namespace ThreeDEngine.Core.Assets.Streaming;

public enum TexturePixelFormat3D
{
    Rgba8Unorm = 0,
    Rgba8Srgb = 1,
    Bc1RgbaUnorm = 2,
    Bc3RgbaUnorm = 3,
    Bc7RgbaUnorm = 4,
    Etc2Rgba8Unorm = 5,
    Astc4x4RgbaUnorm = 6
}

public readonly record struct TextureAssetDescriptor3D(
    string Key,
    int Width,
    int Height,
    int MipLevelCount,
    TexturePixelFormat3D Format)
{
    public TextureAssetDescriptor3D Validate()
    {
        if (string.IsNullOrWhiteSpace(Key)) throw new ArgumentException("Texture key cannot be empty.", nameof(Key));
        if (Width <= 0 || Height <= 0) throw new ArgumentOutOfRangeException(nameof(Width));
        if (MipLevelCount <= 0) throw new ArgumentOutOfRangeException(nameof(MipLevelCount));
        if (!Enum.IsDefined(Format)) throw new ArgumentOutOfRangeException(nameof(Format));
        var maximum = 1;
        for (var size = global::System.Math.Max(Width, Height); size > 1; size >>= 1) maximum++;
        if (MipLevelCount > maximum) throw new ArgumentOutOfRangeException(nameof(MipLevelCount), "Mip count exceeds the complete chain for the texture dimensions.");
        return this with { Key = Key.Trim() };
    }

    public int GetMipWidth(int mipLevel) => global::System.Math.Max(1, Width >> ValidateMip(mipLevel));
    public int GetMipHeight(int mipLevel) => global::System.Math.Max(1, Height >> ValidateMip(mipLevel));

    private int ValidateMip(int mipLevel)
    {
        if ((uint)mipLevel >= (uint)MipLevelCount) throw new ArgumentOutOfRangeException(nameof(mipLevel));
        return mipLevel;
    }
}

public readonly struct TextureMipPayload3D
{
    private readonly byte[] _data;

    public TextureMipPayload3D(string key, int mipLevel, int width, int height, int rowPitch, ReadOnlySpan<byte> data)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Texture key cannot be empty.", nameof(key));
        if (mipLevel < 0) throw new ArgumentOutOfRangeException(nameof(mipLevel));
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (rowPitch <= 0) throw new ArgumentOutOfRangeException(nameof(rowPitch));
        if (data.IsEmpty) throw new ArgumentException("Mip payload cannot be empty.", nameof(data));
        Key = key.Trim();
        MipLevel = mipLevel;
        Width = width;
        Height = height;
        RowPitch = rowPitch;
        _data = data.ToArray();
    }

    public string Key { get; }
    public int MipLevel { get; }
    public int Width { get; }
    public int Height { get; }
    public int RowPitch { get; }
    public int ByteLength => _data?.Length ?? 0;
    /// <summary>Returns an isolated copy so resident GPU-ready payloads remain immutable.</summary>
    public ReadOnlyMemory<byte> Data => CopyData();
    internal ReadOnlyMemory<byte> DataInternal => _data ?? Array.Empty<byte>();
    public byte[] CopyData() => _data is null ? Array.Empty<byte>() : (byte[])_data.Clone();
}

/// <summary>
/// Asynchronous texture source. It must expose already transcoded GPU-ready mip payloads; the
/// streaming manager never performs synchronous image decoding or format fallback.
/// </summary>
public interface ITextureMipSource3D
{
    ValueTask<TextureAssetDescriptor3D> DescribeAsync(string key, CancellationToken cancellationToken = default);
    ValueTask<TextureMipPayload3D> LoadMipAsync(TextureAssetDescriptor3D descriptor, int mipLevel, CancellationToken cancellationToken = default);
}

public readonly record struct TextureStreamingStatistics3D(
    bool Configured,
    int ResidentTextures,
    int ResidentMipLevels,
    int PinnedTextures,
    int ActiveLoads,
    long ResidentBytes,
    long ResidentByteBudget,
    long Requests,
    long CacheHits,
    long MipLoads,
    long Evictions,
    long Failures);

public sealed class TextureResidencyLease3D : IDisposable
{
    private TextureStreamingManager3D? _owner;
    private readonly string _key;

    internal TextureResidencyLease3D(TextureStreamingManager3D owner, string key, TextureResidencySnapshot3D snapshot)
    {
        _owner = owner;
        _key = key;
        Snapshot = snapshot;
    }

    public TextureResidencySnapshot3D Snapshot { get; }

    public void Dispose()
    {
        var owner = Interlocked.Exchange(ref _owner, null);
        owner?.Release(_key);
    }
}

public sealed class TextureResidencySnapshot3D
{
    private readonly TextureMipPayload3D[] _mips;
    private readonly ReadOnlyCollection<TextureMipPayload3D> _mipsView;

    internal TextureResidencySnapshot3D(TextureAssetDescriptor3D descriptor, int mostDetailedMip, TextureMipPayload3D[] mips)
    {
        Descriptor = descriptor;
        MostDetailedResidentMip = mostDetailedMip;
        _mips = mips ?? throw new ArgumentNullException(nameof(mips));
        _mipsView = Array.AsReadOnly(_mips);
    }

    public TextureAssetDescriptor3D Descriptor { get; }
    public int MostDetailedResidentMip { get; }
    public IReadOnlyList<TextureMipPayload3D> Mips => _mipsView;
    public long ResidentBytes
    {
        get
        {
            long total = 0;
            for (var i = 0; i < _mips.Length; i++) total = checked(total + _mips[i].ByteLength);
            return total;
        }
    }
}

/// <summary>
/// Engine-scoped asynchronous mip residency manager. It loads coarse-to-fine, coalesces requests,
/// pins active leases and evicts only unpinned textures. GPU backends consume the returned
/// GPU-ready payloads through their upload rings; this class never substitutes lower quality data.
/// </summary>
public sealed class TextureStreamingManager3D : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _loadGate;
    private readonly ITextureMipSource3D? _source;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TaskCompletionSource<bool> _shutdownCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly long _budget;
    private long _residentBytes;
    private int _activeLoads;
    private long _requests;
    private long _hits;
    private long _loads;
    private long _evictions;
    private long _failures;
    private int _backgroundOperations;
    private bool _synchronizationDisposed;
    private bool _disposed;

    internal TextureStreamingManager3D(IEngineServiceProvider3D services, AssetStreamingConfiguration3D configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryGetService<ITextureMipSource3D>(out _source);
        _budget = configuration.TextureResidentByteBudget;
        _loadGate = new SemaphoreSlim(configuration.MaximumConcurrentTextureLoads, configuration.MaximumConcurrentTextureLoads);
    }

    public bool IsConfigured => _source is not null && !Volatile.Read(ref _disposed);
    internal Task ShutdownCompletion => _shutdownCompletion.Task;

    public TextureStreamingStatistics3D Statistics
    {
        get
        {
            lock (_gate)
            {
                var mips = 0;
                var pinned = 0;
                foreach (var entry in _entries.Values)
                {
                    mips += entry.Mips.Count;
                    if (entry.PinCount > 0) pinned++;
                }
                return new TextureStreamingStatistics3D(_source is not null && !_disposed, _entries.Count, mips, pinned, _activeLoads, _residentBytes, _budget, _requests, _hits, _loads, _evictions, _failures);
            }
        }
    }

    public async ValueTask<TextureResidencyLease3D> AcquireAsync(
        string key,
        int mostDetailedMip = 0,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Texture key cannot be empty.", nameof(key));
        key = key.Trim();
        ITextureMipSource3D source;
        Entry entry;
        lock (_gate)
        {
            ThrowIfDisposed();
            source = _source ?? throw new InvalidOperationException("Texture streaming requires an ITextureMipSource3D registration. Synchronous decoding and format fallback are prohibited.");
            _requests++;
            if (!_entries.TryGetValue(key, out var existing))
            {
                entry = new Entry(key);
                _entries.Add(key, entry);
            }
            else
            {
                entry = existing;
            }
            entry.ReservationCount = checked(entry.ReservationCount + 1);
            entry.LastAccess = Stopwatch.GetTimestamp();
        }

        try
        {
            var descriptor = await GetDescriptorAsync(source, entry, cancellationToken).ConfigureAwait(false);
            if ((uint)mostDetailedMip >= (uint)descriptor.MipLevelCount) throw new ArgumentOutOfRangeException(nameof(mostDetailedMip));
            for (var mip = descriptor.MipLevelCount - 1; mip >= mostDetailedMip; mip--)
                _ = await GetMipAsync(source, entry, descriptor, mip, cancellationToken).ConfigureAwait(false);

            lock (_gate)
            {
                ThrowIfDisposed();
                if (!_entries.TryGetValue(key, out var current) || !ReferenceEquals(current, entry))
                    throw new ObjectDisposedException(nameof(TextureStreamingManager3D), "Texture residency entry was removed before its lease could be published.");
                if (entry.ReservationCount <= 0) throw new InvalidOperationException($"Texture residency reservation underflow for '{key}'.");
                var snapshot = BuildSnapshot(entry, mostDetailedMip);
                entry.ReservationCount--;
                entry.PinCount = checked(entry.PinCount + 1);
                entry.LastAccess = Stopwatch.GetTimestamp();
                return new TextureResidencyLease3D(this, key, snapshot);
            }
        }
        catch
        {
            ReleaseReservation(entry);
            throw;
        }
    }

    public void Trim()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            TrimLocked();
        }
    }

    public void Dispose()
    {
        List<TaskCompletionSource<TextureAssetDescriptor3D>>? descriptors = null;
        List<TaskCompletionSource<TextureMipPayload3D>>? mips = null;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var entry in _entries.Values)
            {
                if (entry.DescriptorCompletion is { Task.IsCompleted: false } descriptor)
                {
                    descriptors ??= new List<TaskCompletionSource<TextureAssetDescriptor3D>>();
                    descriptors.Add(descriptor);
                }
                foreach (var completion in entry.MipCompletions.Values)
                {
                    if (completion.Task.IsCompleted) continue;
                    mips ??= new List<TaskCompletionSource<TextureMipPayload3D>>();
                    mips.Add(completion);
                }
            }
            _entries.Clear();
            _residentBytes = 0;
        }
        try
        {
            _shutdown.Cancel();
        }
        catch (AggregateException exception)
        {
            ThreeDEngine.Core.Diagnostics.EngineLog3D.Warning("TextureStreaming", $"{exception.InnerExceptions.Count} cancellation callback(s) failed during shutdown; cleanup continues.");
        }
        var disposed = new ObjectDisposedException(nameof(TextureStreamingManager3D));
        if (descriptors is not null) for (var i = 0; i < descriptors.Count; i++) descriptors[i].TrySetException(disposed);
        if (mips is not null) for (var i = 0; i < mips.Count; i++) mips[i].TrySetException(disposed);
        TryDisposeSynchronization();
    }

    private void ReleaseReservation(Entry entry)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(entry.Key, out var current) || !ReferenceEquals(current, entry)) return;
            if (entry.ReservationCount > 0) entry.ReservationCount--;
            entry.LastAccess = Stopwatch.GetTimestamp();
            if (_residentBytes > _budget)
            {
                try { TrimLocked(); }
                catch (InvalidOperationException exception)
                {
                    ThreeDEngine.Core.Diagnostics.EngineLog3D.Warning("TextureStreaming", $"Residency remained above budget while unwinding a failed request for '{entry.Key}': {exception.Message}");
                }
            }
        }
    }

    internal void Release(string key)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry)) return;
            if (entry.PinCount <= 0) throw new InvalidOperationException($"Texture residency lease underflow for '{key}'.");
            entry.PinCount--;
            entry.LastAccess = Stopwatch.GetTimestamp();
            TrimLocked();
        }
    }

    private async ValueTask<TextureAssetDescriptor3D> GetDescriptorAsync(
        ITextureMipSource3D source,
        Entry entry,
        CancellationToken cancellationToken)
    {
        Task<TextureAssetDescriptor3D> task;
        TaskCompletionSource<TextureAssetDescriptor3D>? created = null;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (entry.HasDescriptor)
            {
                _hits++;
                return entry.Descriptor;
            }
            if (entry.DescriptorCompletion is null)
            {
                created = new TaskCompletionSource<TextureAssetDescriptor3D>(TaskCreationOptions.RunContinuationsAsynchronously);
                entry.DescriptorCompletion = created;
                _backgroundOperations++;
            }
            task = entry.DescriptorCompletion.Task;
        }
        if (created is not null) _ = LoadDescriptorCoreAsync(source, entry, created);
        return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task LoadDescriptorCoreAsync(
        ITextureMipSource3D source,
        Entry entry,
        TaskCompletionSource<TextureAssetDescriptor3D> completion)
    {
        var gateEntered = false;
        var activeRegistered = false;
        try
        {
            await _loadGate.WaitAsync(_shutdown.Token).ConfigureAwait(false);
            gateEntered = true;
            lock (_gate)
            {
                ThrowIfDisposed();
                _activeLoads++;
                activeRegistered = true;
            }
            var descriptor = (await source.DescribeAsync(entry.Key, _shutdown.Token).ConfigureAwait(false)).Validate();
            if (!StringComparer.Ordinal.Equals(descriptor.Key, entry.Key))
                throw new InvalidOperationException($"Texture source returned descriptor key '{descriptor.Key}' for request '{entry.Key}'.");
            lock (_gate)
            {
                ThrowIfDisposed();
                if (!_entries.TryGetValue(entry.Key, out var current) || !ReferenceEquals(current, entry))
                    throw new ObjectDisposedException(nameof(TextureStreamingManager3D), "Texture residency entry was removed while its descriptor was loading.");
                if (entry.HasDescriptor && !entry.Descriptor.Equals(descriptor))
                    throw new InvalidOperationException($"Texture descriptor for '{entry.Key}' changed while requests were in flight.");
                entry.Descriptor = descriptor;
                entry.HasDescriptor = true;
                completion.TrySetResult(descriptor);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            completion.TrySetException(new ObjectDisposedException(nameof(TextureStreamingManager3D)));
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                _failures++;
                if (_entries.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry) && entry.Mips.Count == 0 && entry.PinCount == 0)
                    _entries.Remove(entry.Key);
            }
            completion.TrySetException(exception);
        }
        finally
        {
            if (activeRegistered)
            {
                lock (_gate) _activeLoads--;
            }
            if (gateEntered) _loadGate.Release();
            CompleteBackgroundOperation();
        }
    }

    private async ValueTask<TextureMipPayload3D> GetMipAsync(
        ITextureMipSource3D source,
        Entry entry,
        TextureAssetDescriptor3D descriptor,
        int mip,
        CancellationToken cancellationToken)
    {
        Task<TextureMipPayload3D> task;
        TaskCompletionSource<TextureMipPayload3D>? created = null;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (entry.Mips.TryGetValue(mip, out var resident))
            {
                _hits++;
                return resident;
            }
            if (!entry.MipCompletions.TryGetValue(mip, out var completion))
            {
                created = new TaskCompletionSource<TextureMipPayload3D>(TaskCreationOptions.RunContinuationsAsynchronously);
                entry.MipCompletions.Add(mip, created);
                completion = created;
                _backgroundOperations++;
            }
            task = completion.Task;
        }
        if (created is not null) _ = LoadMipCoreAsync(source, entry, descriptor, mip, created);
        return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task LoadMipCoreAsync(
        ITextureMipSource3D source,
        Entry entry,
        TextureAssetDescriptor3D descriptor,
        int mip,
        TaskCompletionSource<TextureMipPayload3D> completion)
    {
        var gateEntered = false;
        var activeRegistered = false;
        try
        {
            await _loadGate.WaitAsync(_shutdown.Token).ConfigureAwait(false);
            gateEntered = true;
            lock (_gate)
            {
                ThrowIfDisposed();
                _activeLoads++;
                activeRegistered = true;
            }
            var payload = await source.LoadMipAsync(descriptor, mip, _shutdown.Token).ConfigureAwait(false);
            ValidatePayload(descriptor, mip, payload);
            lock (_gate)
            {
                ThrowIfDisposed();
                if (!_entries.TryGetValue(entry.Key, out var current) || !ReferenceEquals(current, entry))
                    throw new ObjectDisposedException(nameof(TextureStreamingManager3D), "Texture residency entry was removed while a mip was loading.");
                var added = false;
                if (!entry.Mips.ContainsKey(mip))
                {
                    var committedResidentBytes = checked(_residentBytes + payload.ByteLength);
                    entry.Mips.Add(mip, payload);
                    _residentBytes = committedResidentBytes;
                    added = true;
                }
                try
                {
                    TrimLocked();
                    if (added)
                    {
                        _loads++;
                    }
                    entry.MipCompletions.Remove(mip);
                    completion.TrySetResult(entry.Mips[mip]);
                }
                catch
                {
                    if (added && entry.Mips.Remove(mip, out var rolledBack))
                    {
                        _residentBytes -= rolledBack.ByteLength;
                    }
                    entry.MipCompletions.Remove(mip);
                    throw;
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            completion.TrySetException(new ObjectDisposedException(nameof(TextureStreamingManager3D)));
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                _failures++;
                entry.MipCompletions.Remove(mip);
            }
            completion.TrySetException(exception);
        }
        finally
        {
            if (activeRegistered)
            {
                lock (_gate) _activeLoads--;
            }
            if (gateEntered) _loadGate.Release();
            CompleteBackgroundOperation();
        }
    }

    private void TrimLocked()
    {
        if (_residentBytes <= _budget) return;
        var candidates = new List<Entry>();
        foreach (var entry in _entries.Values)
            if (entry.PinCount == 0 && entry.ReservationCount == 0 && entry.MipCompletions.Count == 0) candidates.Add(entry);
        candidates.Sort(static (a, b) => a.LastAccess.CompareTo(b.LastAccess));
        for (var i = 0; i < candidates.Count && _residentBytes > _budget; i++)
        {
            var entry = candidates[i];
            foreach (var payload in entry.Mips.Values) _residentBytes -= payload.ByteLength;
            _entries.Remove(entry.Key);
            _evictions++;
        }
        if (_residentBytes > _budget)
            throw new InvalidOperationException($"Texture residency {_residentBytes} exceeds budget {_budget}; all remaining textures are pinned or loading. Quality reduction is not permitted.");
    }

    private static TextureResidencySnapshot3D BuildSnapshot(Entry entry, int mostDetailedMip)
    {
        var count = entry.Descriptor.MipLevelCount - mostDetailedMip;
        var mips = new TextureMipPayload3D[count];
        for (var mip = mostDetailedMip; mip < entry.Descriptor.MipLevelCount; mip++)
            mips[mip - mostDetailedMip] = entry.Mips.TryGetValue(mip, out var payload)
                ? payload
                : throw new InvalidOperationException($"Texture '{entry.Key}' mip {mip} is missing after a completed residency request.");
        return new TextureResidencySnapshot3D(entry.Descriptor, mostDetailedMip, mips);
    }

    private static void ValidatePayload(TextureAssetDescriptor3D descriptor, int mip, TextureMipPayload3D payload)
    {
        if (!StringComparer.Ordinal.Equals(payload.Key, descriptor.Key) || payload.MipLevel != mip)
            throw new InvalidOperationException($"Texture source returned mismatched payload '{payload.Key}' mip {payload.MipLevel}; expected '{descriptor.Key}' mip {mip}.");
        if (payload.Width != descriptor.GetMipWidth(mip) || payload.Height != descriptor.GetMipHeight(mip))
            throw new InvalidOperationException($"Texture '{descriptor.Key}' mip {mip} dimensions are {payload.Width}x{payload.Height}; expected {descriptor.GetMipWidth(mip)}x{descriptor.GetMipHeight(mip)}.");

        var blockWidth = descriptor.Format is TexturePixelFormat3D.Rgba8Unorm or TexturePixelFormat3D.Rgba8Srgb ? 1 : 4;
        var blockHeight = blockWidth;
        var bytesPerBlock = descriptor.Format switch
        {
            TexturePixelFormat3D.Rgba8Unorm or TexturePixelFormat3D.Rgba8Srgb => 4,
            TexturePixelFormat3D.Bc1RgbaUnorm => 8,
            TexturePixelFormat3D.Bc3RgbaUnorm or TexturePixelFormat3D.Bc7RgbaUnorm or
                TexturePixelFormat3D.Etc2Rgba8Unorm or TexturePixelFormat3D.Astc4x4RgbaUnorm => 16,
            _ => throw new InvalidOperationException($"Texture format '{descriptor.Format}' is unsupported by the streaming payload validator.")
        };
        var blocksWide = checked((payload.Width + blockWidth - 1) / blockWidth);
        var blocksHigh = checked((payload.Height + blockHeight - 1) / blockHeight);
        var minimumRowPitch = checked(blocksWide * bytesPerBlock);
        var minimumByteLength = checked(payload.RowPitch * blocksHigh);
        if (payload.RowPitch < minimumRowPitch)
            throw new InvalidOperationException($"Texture '{descriptor.Key}' mip {mip} row pitch {payload.RowPitch} is smaller than required {minimumRowPitch} for {descriptor.Format}.");
        if (payload.ByteLength < minimumByteLength)
            throw new InvalidOperationException($"Texture '{descriptor.Key}' mip {mip} contains {payload.ByteLength} bytes; at least {minimumByteLength} are required by its row pitch and format.");
    }

    private void CompleteBackgroundOperation()
    {
        lock (_gate)
        {
            if (_backgroundOperations <= 0) throw new InvalidOperationException("Texture streaming background-operation accounting underflow.");
            _backgroundOperations--;
        }
        TryDisposeSynchronization();
    }

    private void TryDisposeSynchronization()
    {
        var dispose = false;
        lock (_gate)
        {
            if (_disposed && _backgroundOperations == 0 && !_synchronizationDisposed)
            {
                _synchronizationDisposed = true;
                dispose = true;
            }
        }
        if (!dispose) return;
        _loadGate.Dispose();
        _shutdown.Dispose();
        _shutdownCompletion.TrySetResult(true);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class Entry
    {
        public Entry(string key) { Key = key; LastAccess = Stopwatch.GetTimestamp(); }
        public string Key { get; }
        public TextureAssetDescriptor3D Descriptor { get; set; }
        public bool HasDescriptor { get; set; }
        public TaskCompletionSource<TextureAssetDescriptor3D>? DescriptorCompletion { get; set; }
        public Dictionary<int, TextureMipPayload3D> Mips { get; } = new();
        public Dictionary<int, TaskCompletionSource<TextureMipPayload3D>> MipCompletions { get; } = new();
        public int PinCount { get; set; }
        public int ReservationCount { get; set; }
        public long LastAccess { get; set; }
    }
}
