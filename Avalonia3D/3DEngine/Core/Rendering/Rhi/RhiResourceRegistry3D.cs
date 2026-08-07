using System;
using System.Collections.Generic;
using ThreeDEngine.Core.Resources;

namespace ThreeDEngine.Core.Rendering.Rhi;

/// <summary>
/// Generation-checked ownership ledger for logical GPU resources. Native backends commit a
/// logical entry only after the corresponding native allocation succeeds. A context loss
/// invalidates every handle atomically; stale handles never alias a later resource.
/// </summary>
internal sealed class RhiResourceRegistry3D
{
    private readonly Dictionary<ResourceKey, Entry> _entries = new();
    private readonly Dictionary<ulong, Entry> _entriesById = new();
    private readonly long _maxResidentBytes;
    private readonly long _maxTextureBytes;
    private ulong _nextId;
    private uint _contextGeneration = 1;
    private long _residentBytes;
    private long _textureBytes;
    private long _peakResidentBytes;
    private long _creates;
    private long _updates;
    private long _releases;
    private int _bufferCount;
    private int _textureCount;
    private bool _disposed;

    public RhiResourceRegistry3D(EngineResourceConfiguration3D? configuration = null)
    {
        _maxResidentBytes = configuration?.MaxGpuResidentBytes ?? long.MaxValue;
        _maxTextureBytes = configuration?.MaxGpuTextureBytes ?? long.MaxValue;
    }

    public uint ContextGeneration => _contextGeneration;
    public bool IsDisposed => _disposed;

    public RhiResourceHandle3D RegisterBuffer(string key, RhiBufferDescriptor3D descriptor, long contentVersion, string? owner = null)
        => Register(key, RhiResourceKind3D.Buffer, descriptor.ByteSize, contentVersion, descriptor, owner);

    public RhiResourceHandle3D RegisterTexture(string key, RhiTextureDescriptor3D descriptor, long contentVersion, string? owner = null)
        => Register(key, RhiResourceKind3D.Texture, descriptor.EstimatedByteSize, contentVersion, descriptor, owner);

    public RhiResourceHandle3D RegisterSampler(string key, RhiSamplerDescriptor3D descriptor, long contentVersion, string? owner = null)
        => Register(key, RhiResourceKind3D.Sampler, 0, contentVersion, descriptor, owner);

    public RhiResourceHandle3D RegisterShaderModule(string key, RhiShaderModuleDescriptor3D descriptor, long contentVersion, string? owner = null)
        => Register(key, RhiResourceKind3D.ShaderModule, 0, contentVersion, descriptor ?? throw new ArgumentNullException(nameof(descriptor)), owner);

    public RhiResourceHandle3D RegisterBindGroupLayout(string key, RhiBindGroupLayoutDescriptor3D descriptor, long contentVersion, string? owner = null)
        => Register(key, RhiResourceKind3D.BindGroupLayout, 0, contentVersion, descriptor ?? throw new ArgumentNullException(nameof(descriptor)), owner);

    public RhiResourceHandle3D RegisterPipelineLayout(string key, RhiPipelineLayoutDescriptor3D descriptor, long contentVersion, string? owner = null)
        => Register(key, RhiResourceKind3D.PipelineLayout, 0, contentVersion, descriptor ?? throw new ArgumentNullException(nameof(descriptor)), owner);

    public RhiResourceHandle3D RegisterRenderPipeline(string key, RhiRenderPipelineDescriptor3D descriptor, long contentVersion, string? owner = null)
        => Register(key, RhiResourceKind3D.Pipeline, 0, contentVersion, descriptor ?? throw new ArgumentNullException(nameof(descriptor)), owner);

    public RhiResourceHandle3D RegisterComputePipeline(string key, RhiComputePipelineDescriptor3D descriptor, long contentVersion, string? owner = null)
        => Register(key, RhiResourceKind3D.Pipeline, 0, contentVersion, descriptor ?? throw new ArgumentNullException(nameof(descriptor)), owner);

    public RhiResourceHandle3D RegisterBindGroup(string key, RhiBindGroupDescriptor3D descriptor, long contentVersion, string? owner = null)
        => Register(key, RhiResourceKind3D.BindGroup, 0, contentVersion, descriptor ?? throw new ArgumentNullException(nameof(descriptor)), owner);

    public void ValidateTextureRegistration(string key, RhiTextureDescriptor3D descriptor, long contentVersion)
        => ValidateRegistration(key, RhiResourceKind3D.Texture, descriptor.EstimatedByteSize, contentVersion, descriptor);

    public RhiResourceHandle3D RegisterAllocation(string key, RhiResourceKind3D kind, long byteSize, long contentVersion, string? owner = null)
    {
        if (kind is RhiResourceKind3D.Buffer or RhiResourceKind3D.Texture or RhiResourceKind3D.Sampler or
            RhiResourceKind3D.ShaderModule or RhiResourceKind3D.BindGroupLayout or RhiResourceKind3D.PipelineLayout or RhiResourceKind3D.BindGroup)
        {
            throw new ArgumentException("Use a typed registration overload for resources with a descriptor.", nameof(kind));
        }
        return Register(key, kind, byteSize, contentVersion, descriptor: null, owner);
    }

    public bool Contains(RhiResourceHandle3D handle)
        => !_disposed && handle.IsValid && handle.Generation == _contextGeneration &&
           _entriesById.TryGetValue(handle.Id, out var entry) && entry.Handle.Equals(handle);

    public void RequireLive(RhiResourceHandle3D handle, string operation)
    {
        if (!Contains(handle))
            throw new InvalidOperationException($"RHI resource {handle} is stale or was released before '{operation}'. Current context generation is {_contextGeneration}.");
    }

    public void RequireKind(RhiResourceHandle3D handle, RhiResourceKind3D expectedKind, string operation)
    {
        RequireLive(handle, operation);
        if (handle.Kind != expectedKind)
            throw new InvalidOperationException($"RHI operation '{operation}' requires {expectedKind}, but received {handle.Kind} ({handle}).");
    }

    public long GetByteSize(RhiResourceHandle3D handle)
    {
        RequireLive(handle, "read allocation size");
        return _entriesById[handle.Id].ByteSize;
    }

    public T GetDescriptor<T>(RhiResourceHandle3D handle, string operation) where T : notnull
    {
        RequireLive(handle, operation);
        if (_entriesById[handle.Id].Descriptor is not T descriptor)
            throw new InvalidOperationException($"RHI resource {handle} does not carry descriptor {typeof(T).Name} required by '{operation}'.");
        return descriptor;
    }

    public bool TryGetDescriptor<T>(RhiResourceHandle3D handle, out T? descriptor) where T : class
    {
        descriptor = null;
        if (!Contains(handle)) return false;
        descriptor = _entriesById[handle.Id].Descriptor as T;
        return descriptor is not null;
    }

    public void AddOwner(RhiResourceHandle3D handle, string owner)
    {
        ThrowIfDisposed();
        RequireLive(handle, "add resource owner");
        _entriesById[handle.Id].Owners.Add(RequireOwner(owner));
    }

    public bool ReleaseOwner(RhiResourceHandle3D handle, string owner)
    {
        ThrowIfDisposed();
        if (!Contains(handle)) return false;
        var entry = _entriesById[handle.Id];
        if (!entry.Owners.Remove(RequireOwner(owner))) return false;
        if (entry.Owners.Count == 0) Remove(entry.Key, entry);
        return true;
    }

    public bool Release(string key, RhiResourceKind3D kind)
    {
        ThrowIfDisposed();
        var resourceKey = new ResourceKey(RequireKey(key), kind);
        if (!_entries.TryGetValue(resourceKey, out var entry)) return false;
        Remove(resourceKey, entry);
        return true;
    }

    public bool Release(RhiResourceHandle3D handle)
    {
        ThrowIfDisposed();
        if (!Contains(handle)) return false;
        var entry = _entriesById[handle.Id];
        Remove(entry.Key, entry);
        return true;
    }

    internal void InvalidateContext()
    {
        ThrowIfDisposed();
        _releases += _entries.Count;
        _entries.Clear();
        _entriesById.Clear();
        _residentBytes = 0;
        _textureBytes = 0;
        _bufferCount = 0;
        _textureCount = 0;
        _contextGeneration = _contextGeneration == uint.MaxValue ? 1u : _contextGeneration + 1u;
    }

    public RhiResourceSnapshot3D CaptureSnapshot()
    {
        var ownerCount = 0;
        foreach (var entry in _entries.Values) ownerCount += entry.Owners.Count;
        return new RhiResourceSnapshot3D(
            _entries.Count, _bufferCount, _textureCount, ownerCount,
            _residentBytes, _textureBytes, _peakResidentBytes,
            _maxResidentBytes, _maxTextureBytes,
            _creates, _updates, _releases, _contextGeneration);
    }

    private void ValidateRegistration(string key, RhiResourceKind3D kind, long byteSize, long contentVersion, object? descriptor)
    {
        ThrowIfDisposed();
        key = RequireKey(key);
        if (byteSize < 0) throw new ArgumentOutOfRangeException(nameof(byteSize));
        var resourceKey = new ResourceKey(key, kind);
        if (_entries.TryGetValue(resourceKey, out var existing))
        {
            if (existing.ContentVersion == contentVersion && existing.ByteSize != byteSize)
                throw new InvalidOperationException($"RHI resource '{key}' changed allocation size without a content-version change.");
            if (existing.ContentVersion == contentVersion && descriptor is not null && !Equals(existing.Descriptor, descriptor))
                throw new InvalidOperationException($"RHI resource '{key}' changed descriptor without a content-version change.");
            if (existing.ContentVersion != contentVersion || existing.ByteSize != byteSize || !Equals(existing.Descriptor, descriptor))
                ValidateBudget(kind, byteSize, existing.ByteSize, existing.Handle.Kind == RhiResourceKind3D.Texture ? existing.ByteSize : 0);
            return;
        }
        ValidateBudget(kind, byteSize, 0, 0);
    }

    private RhiResourceHandle3D Register(string key, RhiResourceKind3D kind, long byteSize, long contentVersion, object? descriptor, string? owner)
    {
        ThrowIfDisposed();
        key = RequireKey(key);
        owner = string.IsNullOrWhiteSpace(owner) ? "resource:" + key : RequireOwner(owner);
        if (byteSize < 0) throw new ArgumentOutOfRangeException(nameof(byteSize));
        var resourceKey = new ResourceKey(key, kind);
        if (_entries.TryGetValue(resourceKey, out var existing))
        {
            if (existing.ContentVersion == contentVersion && existing.ByteSize != byteSize)
                throw new InvalidOperationException($"RHI resource '{key}' changed allocation size without a content-version change.");
            if (existing.ContentVersion == contentVersion && descriptor is not null && !Equals(existing.Descriptor, descriptor))
                throw new InvalidOperationException($"RHI resource '{key}' changed descriptor without a content-version change.");
            if (existing.ContentVersion != contentVersion || existing.ByteSize != byteSize || !Equals(existing.Descriptor, descriptor))
            {
                ValidateBudget(kind, byteSize, existing.ByteSize, existing.Handle.Kind == RhiResourceKind3D.Texture ? existing.ByteSize : 0);
                _residentBytes = checked(_residentBytes - existing.ByteSize + byteSize);
                if (kind == RhiResourceKind3D.Texture) _textureBytes = checked(_textureBytes - existing.ByteSize + byteSize);
                existing.ByteSize = byteSize;
                existing.ContentVersion = contentVersion;
                existing.Descriptor = descriptor;
                _updates++;
                UpdatePeak();
            }
            existing.Owners.Add(owner);
            return existing.Handle;
        }

        ValidateBudget(kind, byteSize, 0, 0);
        var id = ++_nextId;
        if (id == 0) throw new InvalidOperationException("RHI logical resource id space was exhausted.");
        var handle = new RhiResourceHandle3D(id, _contextGeneration, kind);
        var entry = new Entry(resourceKey, handle, byteSize, contentVersion, descriptor, owner);
        _entries.Add(resourceKey, entry);
        _entriesById.Add(id, entry);
        _residentBytes = checked(_residentBytes + byteSize);
        if (kind == RhiResourceKind3D.Texture) _textureBytes = checked(_textureBytes + byteSize);
        _creates++;
        if (kind == RhiResourceKind3D.Buffer) _bufferCount++;
        if (kind == RhiResourceKind3D.Texture) _textureCount++;
        UpdatePeak();
        return handle;
    }

    private void ValidateBudget(RhiResourceKind3D kind, long newBytes, long replacedBytes, long replacedTextureBytes)
    {
        var nextResident = checked(_residentBytes - replacedBytes + newBytes);
        if (nextResident > _maxResidentBytes)
            throw new InvalidOperationException($"GPU resident resource budget exceeded: requested={nextResident}, budget={_maxResidentBytes} bytes.");
        if (kind == RhiResourceKind3D.Texture)
        {
            var nextTextures = checked(_textureBytes - replacedTextureBytes + newBytes);
            if (nextTextures > _maxTextureBytes)
                throw new InvalidOperationException($"GPU texture budget exceeded: requested={nextTextures}, budget={_maxTextureBytes} bytes.");
        }
    }

    private void Remove(ResourceKey key, Entry entry)
    {
        _entries.Remove(key);
        _entriesById.Remove(entry.Handle.Id);
        _residentBytes -= entry.ByteSize;
        if (entry.Handle.Kind == RhiResourceKind3D.Texture) _textureBytes -= entry.ByteSize;
        _releases++;
        if (entry.Handle.Kind == RhiResourceKind3D.Buffer) _bufferCount--;
        if (entry.Handle.Kind == RhiResourceKind3D.Texture) _textureCount--;
    }

    private void UpdatePeak() { if (_residentBytes > _peakResidentBytes) _peakResidentBytes = _residentBytes; }
    private static string RequireKey(string key) => string.IsNullOrWhiteSpace(key) ? throw new ArgumentException("RHI resource key cannot be empty.", nameof(key)) : key;
    private static string RequireOwner(string owner) => string.IsNullOrWhiteSpace(owner) ? throw new ArgumentException("RHI resource owner cannot be empty.", nameof(owner)) : owner;

    internal void Dispose()
    {
        if (_disposed) return;
        if (_entries.Count != 0) InvalidateContext();
        _disposed = true;
    }

    private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(RhiResourceRegistry3D)); }

    private readonly struct ResourceKey : IEquatable<ResourceKey>
    {
        public ResourceKey(string value, RhiResourceKind3D kind) { Value = value; Kind = kind; }
        public string Value { get; }
        public RhiResourceKind3D Kind { get; }
        public bool Equals(ResourceKey other) => Kind == other.Kind && string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is ResourceKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(Value), Kind);
    }

    private sealed class Entry
    {
        public Entry(ResourceKey key, RhiResourceHandle3D handle, long byteSize, long contentVersion, object? descriptor, string owner)
        {
            Key = key;
            Handle = handle;
            ByteSize = byteSize;
            ContentVersion = contentVersion;
            Descriptor = descriptor;
            Owners = new HashSet<string>(StringComparer.Ordinal) { owner };
        }
        public ResourceKey Key { get; }
        public RhiResourceHandle3D Handle { get; }
        public long ByteSize { get; set; }
        public long ContentVersion { get; set; }
        public object? Descriptor { get; set; }
        public HashSet<string> Owners { get; }
    }
}
