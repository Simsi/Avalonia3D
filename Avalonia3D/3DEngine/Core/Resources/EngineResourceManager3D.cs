using System;
using System.Collections.Generic;
using ThreeDEngine.Core.Rendering;

namespace ThreeDEngine.Core.Resources;

/// <summary>
/// Engine-scoped immutable CPU resource catalog. Content is interned by physical hash, owners
/// hold explicit reference sets, and only unreferenced entries may be evicted to satisfy budgets.
/// </summary>
public sealed class EngineResourceManager3D : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, TextureEntry> _textures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ShaderEntry> _shaders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OwnerSet> _owners = new(StringComparer.Ordinal);
    private readonly EngineResourceConfiguration3D _configuration;
    private long _residentTextureBytes;
    private long _residentShaderBytes;
    private long _peakResidentTextureBytes;
    private long _peakResidentShaderBytes;
    private long _clock;
    private long _nextOwnerId;
    private bool _disposed;

    internal EngineResourceManager3D(EngineResourceConfiguration3D configuration)
        => _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

    public EngineResourceConfiguration3D Configuration => _configuration;
    public bool IsDisposed => _disposed;
    public int TextureCount { get { lock (_gate) return _textures.Count; } }
    public int ShaderCount { get { lock (_gate) return _shaders.Count; } }
    public int OwnerCount { get { lock (_gate) return _owners.Count; } }
    public long ResidentTextureBytes { get { lock (_gate) return _residentTextureBytes; } }
    public long ResidentShaderBytes { get { lock (_gate) return _residentShaderBytes; } }
    public long PeakResidentTextureBytes { get { lock (_gate) return _peakResidentTextureBytes; } }
    public long PeakResidentShaderBytes { get { lock (_gate) return _peakResidentShaderBytes; } }

    public EngineResourceOwner3D CreateOwner(string? name = null)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            var sequence = checked(++_nextOwnerId);
            var normalizedName = string.IsNullOrWhiteSpace(name) ? "resource-owner" : name.Trim();
            return new EngineResourceOwner3D(this, $"owner:{sequence:x16}", normalizedName);
        }
    }

    public TextureResource3D Intern(TextureResource3D resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        lock (_gate)
        {
            ThrowIfDisposed();
            return InternTextureLocked(resource).Resource;
        }
    }

    public ShaderResource3D Intern(ShaderResource3D resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        lock (_gate)
        {
            ThrowIfDisposed();
            return InternShaderLocked(resource).Resource;
        }
    }

    internal void SynchronizeOwnerTextures(string ownerId, IReadOnlyList<TextureResource3D> resources)
    {
        ValidateOwnerId(ownerId);
        ArgumentNullException.ThrowIfNull(resources);
        lock (_gate)
        {
            ThrowIfDisposed();
            var next = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Dictionary<string, TextureResource3D>(StringComparer.Ordinal);
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < resources.Count; i++)
            {
                var resource = resources[i] ?? throw new ArgumentException($"Texture resource at index {i} is null.", nameof(resources));
                ValidateTextureCandidateLocked(resource, pending);
                next.Add(resource.ResourceKey);
                ValidateAlias(aliases, resource.LogicalKey, resource.ResourceKey, "Texture");
            }
            CommitTextureOwnerSetLocked(ownerId, next, pending);
        }
    }

    internal void SynchronizeOwnerTextures(string ownerId, IReadOnlyList<RenderTextureResource3D> resources)
    {
        ValidateOwnerId(ownerId);
        ArgumentNullException.ThrowIfNull(resources);
        lock (_gate)
        {
            ThrowIfDisposed();
            var next = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Dictionary<string, TextureResource3D>(StringComparer.Ordinal);
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < resources.Count; i++)
            {
                var descriptor = resources[i];
                var resource = descriptor.Resource ?? throw new ArgumentException($"Texture resource at index {i} is null.", nameof(resources));
                ValidateTextureCandidateLocked(resource, pending);
                next.Add(resource.ResourceKey);
                ValidateAlias(aliases, descriptor.LogicalKey, resource.ResourceKey, "Texture");
            }
            CommitTextureOwnerSetLocked(ownerId, next, pending);
        }
    }

    internal void SynchronizeOwnerShaders(string ownerId, IReadOnlyList<ShaderResource3D> resources)
    {
        ValidateOwnerId(ownerId);
        ArgumentNullException.ThrowIfNull(resources);
        lock (_gate)
        {
            ThrowIfDisposed();
            var next = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Dictionary<string, ShaderResource3D>(StringComparer.Ordinal);
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < resources.Count; i++)
            {
                var resource = resources[i] ?? throw new ArgumentException($"Shader resource at index {i} is null.", nameof(resources));
                ValidateShaderCandidateLocked(resource, pending);
                next.Add(resource.ResourceKey);
                ValidateAlias(aliases, resource.LogicalKey, resource.ResourceKey, "Shader");
            }
            CommitShaderOwnerSetLocked(ownerId, next, pending);
        }
    }

    internal void ClearOwner(string ownerId)
    {
        ValidateOwnerId(ownerId);
        lock (_gate)
        {
            if (_disposed) return;
            ReleaseOwnerLocked(ownerId);
        }
    }

    internal void ReleaseOwner(string ownerId)
    {
        if (string.IsNullOrWhiteSpace(ownerId)) return;
        lock (_gate)
        {
            if (_disposed) return;
            ReleaseOwnerLocked(ownerId);
        }
    }

    public EngineResourceSnapshot3D CaptureSnapshot()
    {
        lock (_gate)
        {
            var referencedTextures = 0;
            foreach (var entry in _textures.Values) if (entry.ReferenceCount > 0) referencedTextures++;
            var referencedShaders = 0;
            foreach (var entry in _shaders.Values) if (entry.ReferenceCount > 0) referencedShaders++;
            return new EngineResourceSnapshot3D(
                _textures.Count,
                referencedTextures,
                _shaders.Count,
                referencedShaders,
                _owners.Count,
                _residentTextureBytes,
                _residentShaderBytes,
                _peakResidentTextureBytes,
                _peakResidentShaderBytes,
                _configuration.MaxCpuTextureBytes,
                _configuration.MaxCpuShaderBytes);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _owners.Clear();
            _textures.Clear();
            _shaders.Clear();
            _residentTextureBytes = 0;
            _residentShaderBytes = 0;
            _disposed = true;
        }
    }

    private void ValidateTextureCandidateLocked(TextureResource3D resource, Dictionary<string, TextureResource3D> pending)
    {
        if (_textures.TryGetValue(resource.ResourceKey, out var existing))
        {
            if (!existing.Resource.ContentEquals(resource))
                throw new InvalidOperationException($"Texture content-hash collision for '{resource.ResourceKey}'.");
            return;
        }

        if (pending.TryGetValue(resource.ResourceKey, out var candidate))
        {
            if (!candidate.ContentEquals(resource))
                throw new InvalidOperationException($"Texture content-hash collision for '{resource.ResourceKey}'.");
            return;
        }

        pending.Add(resource.ResourceKey, resource);
    }

    private void ValidateShaderCandidateLocked(ShaderResource3D resource, Dictionary<string, ShaderResource3D> pending)
    {
        if (_shaders.TryGetValue(resource.ResourceKey, out var existing))
        {
            if (!existing.Resource.ContentEquals(resource))
                throw new InvalidOperationException($"Shader content-hash collision for '{resource.ResourceKey}'.");
            return;
        }

        if (pending.TryGetValue(resource.ResourceKey, out var candidate))
        {
            if (!candidate.ContentEquals(resource))
                throw new InvalidOperationException($"Shader content-hash collision for '{resource.ResourceKey}'.");
            return;
        }

        pending.Add(resource.ResourceKey, resource);
    }

    private void CommitTextureOwnerSetLocked(
        string ownerId,
        HashSet<string> next,
        Dictionary<string, TextureResource3D> pending)
    {
        _owners.TryGetValue(ownerId, out var owner);
        var previous = owner?.TextureKeys;
        ValidateTextureReferenceTransitionLocked(previous, next, pending);

        var addedKeys = new List<string>(pending.Count);
        var referencesAdjusted = false;
        try
        {
            foreach (var pair in pending)
            {
                var entry = new TextureEntry(pair.Value, ++_clock);
                _textures.Add(pair.Key, entry);
                addedKeys.Add(pair.Key);
                _residentTextureBytes = checked(_residentTextureBytes + pair.Value.ByteLength);
            }

            IncrementExistingTextureReferencesLocked(previous, next);
            DecrementRemovedTextureReferencesLocked(previous, next);
            referencesAdjusted = true;
            foreach (var key in pending.Keys) _textures[key].ReferenceCount = 1;
            foreach (var key in next) _textures[key].LastUse = ++_clock;

            if (owner is null)
            {
                if (next.Count != 0)
                {
                    owner = new OwnerSet { TextureKeys = next };
                    _owners.Add(ownerId, owner);
                }
            }
            else
            {
                owner.TextureKeys = next;
                RemoveOwnerIfEmptyLocked(ownerId, owner);
            }
        }
        catch
        {
            for (var i = addedKeys.Count - 1; i >= 0; i--)
            {
                var key = addedKeys[i];
                if (!_textures.Remove(key, out var entry)) continue;
                _residentTextureBytes -= entry.Resource.ByteLength;
            }
            if (referencesAdjusted) RollbackTextureReferenceTransitionLocked(previous, next);
            throw;
        }

        TrimUnreferencedTexturesLocked(0);
        if (_residentTextureBytes > _peakResidentTextureBytes) _peakResidentTextureBytes = _residentTextureBytes;
    }

    private void CommitShaderOwnerSetLocked(
        string ownerId,
        HashSet<string> next,
        Dictionary<string, ShaderResource3D> pending)
    {
        _owners.TryGetValue(ownerId, out var owner);
        var previous = owner?.ShaderKeys;
        ValidateShaderReferenceTransitionLocked(previous, next, pending);

        var addedKeys = new List<string>(pending.Count);
        var referencesAdjusted = false;
        try
        {
            foreach (var pair in pending)
            {
                var entry = new ShaderEntry(pair.Value, ++_clock);
                _shaders.Add(pair.Key, entry);
                addedKeys.Add(pair.Key);
                _residentShaderBytes = checked(_residentShaderBytes + pair.Value.ByteLengthInternal);
            }

            IncrementExistingShaderReferencesLocked(previous, next);
            DecrementRemovedShaderReferencesLocked(previous, next);
            referencesAdjusted = true;
            foreach (var key in pending.Keys) _shaders[key].ReferenceCount = 1;
            foreach (var key in next) _shaders[key].LastUse = ++_clock;

            if (owner is null)
            {
                if (next.Count != 0)
                {
                    owner = new OwnerSet { ShaderKeys = next };
                    _owners.Add(ownerId, owner);
                }
            }
            else
            {
                owner.ShaderKeys = next;
                RemoveOwnerIfEmptyLocked(ownerId, owner);
            }
        }
        catch
        {
            for (var i = addedKeys.Count - 1; i >= 0; i--)
            {
                var key = addedKeys[i];
                if (!_shaders.Remove(key, out var entry)) continue;
                _residentShaderBytes -= entry.Resource.ByteLengthInternal;
            }
            if (referencesAdjusted) RollbackShaderReferenceTransitionLocked(previous, next);
            throw;
        }

        TrimUnreferencedShadersLocked(0);
        if (_residentShaderBytes > _peakResidentShaderBytes) _peakResidentShaderBytes = _residentShaderBytes;
    }

    private void ValidateTextureReferenceTransitionLocked(
        HashSet<string>? previous,
        HashSet<string> next,
        Dictionary<string, TextureResource3D> pending)
    {
        long referencedBytesAfter = 0;
        foreach (var pair in _textures)
        {
            var count = pair.Value.ReferenceCount;
            if (previous?.Contains(pair.Key) ?? false)
            {
                if (count <= 0)
                    throw new InvalidOperationException($"Texture resource '{pair.Key}' has an invalid owner reference count.");
                if (!next.Contains(pair.Key)) count--;
            }
            else if (next.Contains(pair.Key))
            {
                if (count == int.MaxValue)
                    throw new InvalidOperationException($"Texture resource '{pair.Key}' owner reference count overflowed.");
                count++;
            }

            if (count > 0) referencedBytesAfter = checked(referencedBytesAfter + pair.Value.Resource.ByteLength);
        }

        if (previous is not null)
        {
            foreach (var key in previous)
            {
                if (!_textures.ContainsKey(key))
                    throw new InvalidOperationException($"Texture owner references missing resource '{key}'.");
            }
        }

        foreach (var resource in pending.Values)
            referencedBytesAfter = checked(referencedBytesAfter + resource.ByteLength);
        if (referencedBytesAfter > _configuration.MaxCpuTextureBytes)
        {
            throw new InvalidOperationException(
                $"Engine CPU texture budget exhausted by referenced resources: required={referencedBytesAfter}, budget={_configuration.MaxCpuTextureBytes}. " +
                "Release resource owners or increase EngineResourceOptions3D.MaxCpuTextureBytes.");
        }
    }

    private void ValidateShaderReferenceTransitionLocked(
        HashSet<string>? previous,
        HashSet<string> next,
        Dictionary<string, ShaderResource3D> pending)
    {
        long referencedBytesAfter = 0;
        foreach (var pair in _shaders)
        {
            var count = pair.Value.ReferenceCount;
            if (previous?.Contains(pair.Key) ?? false)
            {
                if (count <= 0)
                    throw new InvalidOperationException($"Shader resource '{pair.Key}' has an invalid owner reference count.");
                if (!next.Contains(pair.Key)) count--;
            }
            else if (next.Contains(pair.Key))
            {
                if (count == int.MaxValue)
                    throw new InvalidOperationException($"Shader resource '{pair.Key}' owner reference count overflowed.");
                count++;
            }

            if (count > 0) referencedBytesAfter = checked(referencedBytesAfter + pair.Value.Resource.ByteLengthInternal);
        }

        if (previous is not null)
        {
            foreach (var key in previous)
            {
                if (!_shaders.ContainsKey(key))
                    throw new InvalidOperationException($"Shader owner references missing resource '{key}'.");
            }
        }

        foreach (var resource in pending.Values)
            referencedBytesAfter = checked(referencedBytesAfter + resource.ByteLengthInternal);
        if (referencedBytesAfter > _configuration.MaxCpuShaderBytes)
        {
            throw new InvalidOperationException(
                $"Engine CPU shader budget exhausted by referenced resources: required={referencedBytesAfter}, budget={_configuration.MaxCpuShaderBytes}. " +
                "Release resource owners or increase EngineResourceOptions3D.MaxCpuShaderBytes.");
        }
    }

    private void IncrementExistingTextureReferencesLocked(HashSet<string>? previous, HashSet<string> next)
    {
        foreach (var key in next)
        {
            if (previous?.Contains(key) ?? false) continue;
            if (_textures.TryGetValue(key, out var entry)) entry.ReferenceCount++;
        }
    }

    private void IncrementExistingShaderReferencesLocked(HashSet<string>? previous, HashSet<string> next)
    {
        foreach (var key in next)
        {
            if (previous?.Contains(key) ?? false) continue;
            if (_shaders.TryGetValue(key, out var entry)) entry.ReferenceCount++;
        }
    }

    private void DecrementRemovedTextureReferencesLocked(HashSet<string>? previous, HashSet<string> next)
    {
        if (previous is null) return;
        foreach (var key in previous)
        {
            if (!next.Contains(key)) _textures[key].ReferenceCount--;
        }
    }

    private void DecrementRemovedShaderReferencesLocked(HashSet<string>? previous, HashSet<string> next)
    {
        if (previous is null) return;
        foreach (var key in previous)
        {
            if (!next.Contains(key)) _shaders[key].ReferenceCount--;
        }
    }

    private void RollbackTextureReferenceTransitionLocked(HashSet<string>? previous, HashSet<string> next)
    {
        foreach (var key in next)
        {
            if (previous?.Contains(key) ?? false) continue;
            if (_textures.TryGetValue(key, out var entry)) entry.ReferenceCount--;
        }
        if (previous is null) return;
        foreach (var key in previous)
        {
            if (!next.Contains(key) && _textures.TryGetValue(key, out var entry)) entry.ReferenceCount++;
        }
    }

    private void RollbackShaderReferenceTransitionLocked(HashSet<string>? previous, HashSet<string> next)
    {
        foreach (var key in next)
        {
            if (previous?.Contains(key) ?? false) continue;
            if (_shaders.TryGetValue(key, out var entry)) entry.ReferenceCount--;
        }
        if (previous is null) return;
        foreach (var key in previous)
        {
            if (!next.Contains(key) && _shaders.TryGetValue(key, out var entry)) entry.ReferenceCount++;
        }
    }

    private void ReleaseOwnerLocked(string ownerId)
    {
        if (!_owners.Remove(ownerId, out var owner)) return;
        DecrementReferences(owner.TextureKeys, _textures, static entry => entry.ReferenceCount, static (entry, value) => entry.ReferenceCount = value);
        DecrementReferences(owner.ShaderKeys, _shaders, static entry => entry.ReferenceCount, static (entry, value) => entry.ReferenceCount = value);
        TrimUnreferencedTexturesLocked(0);
        TrimUnreferencedShadersLocked(0);
    }

    private static void DecrementReferences<TEntry>(
        HashSet<string> keys,
        Dictionary<string, TEntry> entries,
        Func<TEntry, int> getReferenceCount,
        Action<TEntry, int> setReferenceCount)
        where TEntry : class
    {
        foreach (var key in keys)
        {
            if (!entries.TryGetValue(key, out var entry)) continue;
            var count = getReferenceCount(entry);
            if (count <= 0) throw new InvalidOperationException($"Resource '{key}' has an invalid owner reference count.");
            setReferenceCount(entry, count - 1);
        }
    }

    private void RemoveOwnerIfEmptyLocked(string ownerId, OwnerSet owner)
    {
        if (owner.TextureKeys.Count == 0 && owner.ShaderKeys.Count == 0) _owners.Remove(ownerId);
    }

    private TextureEntry InternTextureLocked(TextureResource3D resource)
    {
        if (_textures.TryGetValue(resource.ResourceKey, out var existing))
        {
            if (!existing.Resource.ContentEquals(resource))
                throw new InvalidOperationException($"Texture content-hash collision for '{resource.ResourceKey}'.");
            existing.LastUse = ++_clock;
            return existing;
        }

        EnsureTextureCapacityLocked(resource.ByteLength);
        var entry = new TextureEntry(resource, ++_clock);
        _textures.Add(resource.ResourceKey, entry);
        _residentTextureBytes = checked(_residentTextureBytes + resource.ByteLength);
        if (_residentTextureBytes > _peakResidentTextureBytes) _peakResidentTextureBytes = _residentTextureBytes;
        return entry;
    }

    private ShaderEntry InternShaderLocked(ShaderResource3D resource)
    {
        if (_shaders.TryGetValue(resource.ResourceKey, out var existing))
        {
            if (!existing.Resource.ContentEquals(resource))
                throw new InvalidOperationException($"Shader content-hash collision for '{resource.ResourceKey}'.");
            existing.LastUse = ++_clock;
            return existing;
        }

        EnsureShaderCapacityLocked(resource.ByteLengthInternal);
        var entry = new ShaderEntry(resource, ++_clock);
        _shaders.Add(resource.ResourceKey, entry);
        _residentShaderBytes = checked(_residentShaderBytes + resource.ByteLengthInternal);
        if (_residentShaderBytes > _peakResidentShaderBytes) _peakResidentShaderBytes = _residentShaderBytes;
        return entry;
    }

    private void EnsureTextureCapacityLocked(long incomingBytes)
    {
        if (incomingBytes > _configuration.MaxCpuTextureBytes)
            throw new InvalidOperationException($"Texture resource requires {incomingBytes} bytes, exceeding the engine CPU texture budget of {_configuration.MaxCpuTextureBytes} bytes.");
        var overflow = checked(_residentTextureBytes + incomingBytes - _configuration.MaxCpuTextureBytes);
        if (overflow <= 0) return;
        TrimUnreferencedTexturesLocked(overflow);
        if (_residentTextureBytes + incomingBytes > _configuration.MaxCpuTextureBytes)
        {
            throw new InvalidOperationException(
                $"Engine CPU texture budget exhausted: resident={_residentTextureBytes}, incoming={incomingBytes}, budget={_configuration.MaxCpuTextureBytes}. " +
                "Release resource owners or increase EngineResourceOptions3D.MaxCpuTextureBytes.");
        }
    }

    private void EnsureShaderCapacityLocked(long incomingBytes)
    {
        if (incomingBytes > _configuration.MaxCpuShaderBytes)
            throw new InvalidOperationException($"Shader resource requires {incomingBytes} bytes, exceeding the engine CPU shader budget of {_configuration.MaxCpuShaderBytes} bytes.");
        var overflow = checked(_residentShaderBytes + incomingBytes - _configuration.MaxCpuShaderBytes);
        if (overflow <= 0) return;
        TrimUnreferencedShadersLocked(overflow);
        if (_residentShaderBytes + incomingBytes > _configuration.MaxCpuShaderBytes)
        {
            throw new InvalidOperationException(
                $"Engine CPU shader budget exhausted: resident={_residentShaderBytes}, incoming={incomingBytes}, budget={_configuration.MaxCpuShaderBytes}. " +
                "Release resource owners or increase EngineResourceOptions3D.MaxCpuShaderBytes.");
        }
    }

    private void TrimUnreferencedTexturesLocked(long requiredBytes)
    {
        while (_residentTextureBytes > _configuration.MaxCpuTextureBytes || requiredBytes > 0)
        {
            var candidate = FindOldestUnreferenced(_textures);
            if (candidate.Key is null || candidate.Entry is null) break;
            _textures.Remove(candidate.Key);
            _residentTextureBytes -= candidate.Entry.Resource.ByteLength;
            requiredBytes -= candidate.Entry.Resource.ByteLength;
        }
    }

    private void TrimUnreferencedShadersLocked(long requiredBytes)
    {
        while (_residentShaderBytes > _configuration.MaxCpuShaderBytes || requiredBytes > 0)
        {
            var candidate = FindOldestUnreferenced(_shaders);
            if (candidate.Key is null || candidate.Entry is null) break;
            _shaders.Remove(candidate.Key);
            _residentShaderBytes -= candidate.Entry.Resource.ByteLengthInternal;
            requiredBytes -= candidate.Entry.Resource.ByteLengthInternal;
        }
    }

    private static (string? Key, TEntry? Entry) FindOldestUnreferenced<TEntry>(Dictionary<string, TEntry> entries)
        where TEntry : ResourceEntry
    {
        string? oldestKey = null;
        TEntry? oldest = null;
        foreach (var pair in entries)
        {
            var candidate = pair.Value;
            if (candidate.ReferenceCount != 0) continue;
            if (oldest is null || candidate.LastUse < oldest.LastUse)
            {
                oldest = candidate;
                oldestKey = pair.Key;
            }
        }
        return (oldestKey, oldest);
    }

    private static void ValidateAlias(Dictionary<string, string> aliases, string logicalKey, string resourceKey, string kind)
    {
        if (!aliases.TryAdd(logicalKey, resourceKey) &&
            !string.Equals(aliases[logicalKey], resourceKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{kind} alias collision: '{logicalKey}' identifies more than one immutable content resource in the same owner scope.");
        }
    }

    private static void ValidateOwnerId(string ownerId)
    {
        if (string.IsNullOrWhiteSpace(ownerId)) throw new ArgumentException("A resource owner id is required.", nameof(ownerId));
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class OwnerSet
    {
        public HashSet<string> TextureKeys { get; set; } = new(StringComparer.Ordinal);
        public HashSet<string> ShaderKeys { get; set; } = new(StringComparer.Ordinal);
    }

    private abstract class ResourceEntry
    {
        protected ResourceEntry(long lastUse) => LastUse = lastUse;
        public int ReferenceCount { get; set; }
        public long LastUse { get; set; }
    }

    private sealed class TextureEntry : ResourceEntry
    {
        public TextureEntry(TextureResource3D resource, long lastUse) : base(lastUse) => Resource = resource;
        public TextureResource3D Resource { get; }
    }

    private sealed class ShaderEntry : ResourceEntry
    {
        public ShaderEntry(ShaderResource3D resource, long lastUse) : base(lastUse) => Resource = resource;
        public ShaderResource3D Resource { get; }
    }
}

public readonly record struct EngineResourceSnapshot3D(
    int TextureCount,
    int ReferencedTextureCount,
    int ShaderCount,
    int ReferencedShaderCount,
    int OwnerCount,
    long ResidentTextureBytes,
    long ResidentShaderBytes,
    long PeakResidentTextureBytes,
    long PeakResidentShaderBytes,
    long TextureBudgetBytes,
    long ShaderBudgetBytes);
