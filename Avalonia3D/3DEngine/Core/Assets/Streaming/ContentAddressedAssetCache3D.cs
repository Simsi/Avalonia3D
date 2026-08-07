using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ThreeDEngine.Core.Diagnostics;

namespace ThreeDEngine.Core.Assets.Streaming;

/// <summary>
/// Bounded SHA-256 addressed immutable byte cache. In-memory entries are deduplicated and LRU
/// evicted. Optional desktop persistence uses atomic file replacement and never changes cache
/// identity. Browser persistence must be supplied by a dedicated storage adapter.
/// </summary>
public sealed class ContentAddressedAssetCache3D : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _lru = new();
    private readonly AssetStreamingConfiguration3D _configuration;
    private long _residentBytes;
    private bool _disposed;

    internal ContentAddressedAssetCache3D(AssetStreamingConfiguration3D configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        if (_configuration.PersistContentCache)
        {
            Directory.CreateDirectory(_configuration.PersistentContentCacheDirectory!);
        }
    }

    public long ResidentBytes { get { lock (_gate) return _residentBytes; } }
    public int Count { get { lock (_gate) return _entries.Count; } }

    public async ValueTask<ContentAddressedAssetBlob3D> StoreAsync(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (content.IsEmpty) throw new ArgumentException("Asset content cannot be empty.", nameof(content));
        cancellationToken.ThrowIfCancellationRequested();

        var hashBytes = SHA256.HashData(content.Span);
        var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        byte[] snapshot;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entries.TryGetValue(hash, out var existing))
            {
                Touch(existing);
                snapshot = existing.Bytes;
            }
            else
            {
                snapshot = content.ToArray();
                var committedResidentBytes = checked(_residentBytes + snapshot.LongLength);
                var node = _lru.AddFirst(hash);
                _entries.Add(hash, new Entry(snapshot, node));
                _residentBytes = committedResidentBytes;
                TrimCore();
            }
        }

        if (_configuration.PersistContentCache)
        {
            await PersistAsync(hash, snapshot, cancellationToken).ConfigureAwait(false);
        }
        return new ContentAddressedAssetBlob3D(hash, snapshot);
    }

    public async ValueTask<ContentAddressedAssetBlob3D?> TryGetAsync(
        string contentHash,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        contentHash = NormalizeContentHash(contentHash, nameof(contentHash));
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_entries.TryGetValue(contentHash, out var existing))
            {
                Touch(existing);
                return new ContentAddressedAssetBlob3D(contentHash, existing.Bytes);
            }
        }

        if (!_configuration.PersistContentCache) return null;
        var path = GetPersistentPath(contentHash);
        if (!File.Exists(path)) return null;
        var fileInfo = new FileInfo(path);
        if (fileInfo.Length <= 0 || fileInfo.Length > Array.MaxLength)
        {
            TryDeleteCorruptPersistentFile(path, contentHash);
            throw new InvalidDataException($"Persistent asset cache entry '{contentHash}' has invalid length {fileInfo.Length} and was removed.");
        }
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var verified = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!StringComparer.Ordinal.Equals(verified, contentHash))
        {
            TryDeleteCorruptPersistentFile(path, contentHash);
            throw new InvalidDataException($"Persistent asset cache entry '{contentHash}' failed SHA-256 verification and was removed.");
        }
        return await StoreAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    public bool Remove(string contentHash)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(contentHash)) return false;
        contentHash = NormalizeContentHash(contentHash, nameof(contentHash));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_entries.Remove(contentHash, out var entry)) return false;
            _lru.Remove(entry.Node);
            _residentBytes -= entry.Bytes.LongLength;
        }
        if (_configuration.PersistContentCache)
        {
            var path = GetPersistentPath(contentHash);
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { EngineLog3D.Warning("AssetCache", $"Failed to delete persistent cache entry '{contentHash}': {ex.Message}"); }
        }
        return true;
    }

    public void Clear()
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _entries.Clear();
            _lru.Clear();
            _residentBytes = 0;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _entries.Clear();
            _lru.Clear();
            _residentBytes = 0;
        }
    }

    private void Touch(Entry entry)
    {
        _lru.Remove(entry.Node);
        _lru.AddFirst(entry.Node);
    }

    private void TrimCore()
    {
        while (_residentBytes > _configuration.ContentCacheByteBudget && _lru.Last is { } tail)
        {
            var key = tail.Value;
            var entry = _entries[key];
            _entries.Remove(key);
            _lru.RemoveLast();
            _residentBytes -= entry.Bytes.LongLength;
        }
    }

    private async Task PersistAsync(string hash, byte[] bytes, CancellationToken cancellationToken)
    {
        var path = GetPersistentPath(hash);
        if (File.Exists(path))
        {
            if (await VerifyPersistentFileAsync(path, hash, bytes.Length, cancellationToken).ConfigureAwait(false)) return;
            TryDeleteCorruptPersistentFile(path, hash);
        }

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            try
            {
                File.Move(temporary, path, overwrite: false);
            }
            catch (IOException) when (File.Exists(path))
            {
                if (!await VerifyPersistentFileAsync(path, hash, bytes.Length, cancellationToken).ConfigureAwait(false))
                {
                    TryDeleteCorruptPersistentFile(path, hash);
                    throw new InvalidDataException($"Persistent asset cache entry '{hash}' was concurrently replaced with different or corrupt content and was removed.");
                }
            }
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception exception) { EngineLog3D.Warning("AssetCache", $"Failed to remove temporary content-cache file '{temporary}': {exception.Message}"); }
        }
    }

    private static async ValueTask<bool> VerifyPersistentFileAsync(
        string path,
        string expectedHash,
        int expectedLength,
        CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != expectedLength || info.Length > Array.MaxLength) return false;
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            if (bytes.Length != expectedLength) return false;
            var actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            return StringComparer.Ordinal.Equals(actualHash, expectedHash);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void TryDeleteCorruptPersistentFile(string path, string hash)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception)
        {
            throw new IOException($"Persistent asset cache entry '{hash}' is corrupt and could not be removed.", exception);
        }
    }

    private string GetPersistentPath(string hash)
    {
        hash = NormalizeContentHash(hash, nameof(hash));
        var root = Path.GetFullPath(_configuration.PersistentContentCacheDirectory!);
        var path = Path.GetFullPath(Path.Combine(root, hash + ".bin"));
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(rootPrefix, comparison))
            throw new InvalidOperationException("Content cache path escaped its configured root.");
        return path;
    }

    private static string NormalizeContentHash(string? hash, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(hash)) throw new ArgumentException("Content hash cannot be empty.", parameterName);
        hash = hash.Trim();
        if (hash.Length != 64) throw new ArgumentException("SHA-256 content hash must contain exactly 64 hexadecimal characters.", parameterName);
        for (var index = 0; index < hash.Length; index++)
        {
            var character = hash[index];
            var hexadecimal = character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!hexadecimal) throw new ArgumentException("SHA-256 content hash must contain only hexadecimal characters.", parameterName);
        }
        return hash.ToLowerInvariant();
    }

    private void ThrowIfDisposed()
    {
        lock (_gate) ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class Entry
    {
        public Entry(byte[] bytes, LinkedListNode<string> node) { Bytes = bytes; Node = node; }
        public byte[] Bytes { get; }
        public LinkedListNode<string> Node { get; }
    }
}

public readonly struct ContentAddressedAssetBlob3D
{
    private readonly byte[] _bytes;

    internal ContentAddressedAssetBlob3D(string contentHash, byte[] bytes)
    {
        ContentHash = contentHash;
        _bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
    }

    public string ContentHash { get; }
    public int ByteLength => _bytes?.Length ?? 0;

    /// <summary>
    /// Returns an isolated copy. The cache never exposes its canonical byte array because callers
    /// could otherwise mutate data after its SHA-256 identity had been established.
    /// </summary>
    public ReadOnlyMemory<byte> Content => CopyBytes();

    internal byte[] BytesInternal => _bytes ?? Array.Empty<byte>();
    public byte[] CopyBytes() => _bytes is null ? Array.Empty<byte>() : (byte[])_bytes.Clone();
}
