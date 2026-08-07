using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ThreeDEngine.Core.Rendering.Extensions;

/// <summary>
/// Engine-scoped extension registry. Mutation is serialized and every frame consumes one immutable
/// snapshot so extension changes cannot tear a render graph. Duplicate ids and pass ids fail fast.
/// </summary>
public sealed class RenderExtensionRegistry3D
{
    private readonly object _gate = new();
    private readonly Dictionary<string, IRenderExtension3D> _extensions = new(StringComparer.Ordinal);
    private long _version;

    public long Version { get { lock (_gate) return _version; } }
    public int Count { get { lock (_gate) return _extensions.Count; } }

    public void Register(IRenderExtension3D extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        var frozen = Freeze(extension);
        lock (_gate)
        {
            if (_extensions.ContainsKey(frozen.Id)) throw new InvalidOperationException($"Render extension '{frozen.Id}' is already registered.");
            _extensions.Add(frozen.Id, frozen);
            _version = checked(_version + 1);
        }
    }

    public void Replace(IRenderExtension3D extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        var frozen = Freeze(extension);
        lock (_gate)
        {
            if (!_extensions.TryGetValue(frozen.Id, out var existing)) throw new KeyNotFoundException($"Render extension '{frozen.Id}' is not registered.");
            if (frozen.Version <= existing.Version)
                throw new InvalidOperationException($"Render extension '{frozen.Id}' replacement version {frozen.Version} must be greater than current version {existing.Version}.");
            _extensions[frozen.Id] = frozen;
            _version = checked(_version + 1);
        }
    }

    public bool Remove(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        lock (_gate)
        {
            if (!_extensions.Remove(id.Trim())) return false;
            _version = checked(_version + 1);
            return true;
        }
    }

    public RenderExtensionSnapshot3D CaptureSnapshot()
    {
        lock (_gate)
        {
            var ordered = _extensions.Values.OrderBy(static extension => extension.Id, StringComparer.Ordinal).ToArray();
            return new RenderExtensionSnapshot3D(_version, ordered);
        }
    }

    private static IRenderExtension3D Freeze(IRenderExtension3D extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        var id = extension.Id;
        var version = extension.Version;
        var sourcePasses = extension.Passes;
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Render extension id cannot be empty.", nameof(extension));
        if (!StringComparer.Ordinal.Equals(id, id.Trim())) throw new ArgumentException("Render extension id cannot contain leading or trailing whitespace.", nameof(extension));
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(extension), "Render extension version must be positive.");
        if (sourcePasses is null || sourcePasses.Count == 0) throw new ArgumentException($"Render extension '{id}' must declare at least one pass.", nameof(extension));

        var passes = new RenderExtensionPass3D[sourcePasses.Count];
        var passIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < passes.Length; i++)
        {
            var pass = sourcePasses[i] ?? throw new ArgumentException($"Render extension '{id}' contains a null pass.", nameof(extension));
            if (!passIds.Add(pass.Id)) throw new ArgumentException($"Render extension '{id}' contains duplicate pass id '{pass.Id}'.", nameof(extension));
            passes[i] = pass;
        }
        return new FrozenRenderExtension3D(id, version, passes);
    }

    private sealed class FrozenRenderExtension3D : IRenderExtension3D
    {
        private readonly ReadOnlyCollection<RenderExtensionPass3D> _passes;
        public FrozenRenderExtension3D(string id, int version, RenderExtensionPass3D[] passes)
        {
            Id = id;
            Version = version;
            _passes = Array.AsReadOnly(passes);
        }
        public string Id { get; }
        public int Version { get; }
        public IReadOnlyList<RenderExtensionPass3D> Passes => _passes;
    }

}

public sealed class RenderExtensionSnapshot3D
{
    private readonly ReadOnlyCollection<IRenderExtension3D> _extensions;

    internal RenderExtensionSnapshot3D(long version, IRenderExtension3D[] extensions)
    {
        Version = version;
        ArgumentNullException.ThrowIfNull(extensions);
        _extensions = Array.AsReadOnly(extensions);
        PassCount = extensions.Sum(static extension => extension.Passes.Count);
    }

    public long Version { get; }
    public int PassCount { get; }
    public IReadOnlyList<IRenderExtension3D> Extensions => _extensions;
}
