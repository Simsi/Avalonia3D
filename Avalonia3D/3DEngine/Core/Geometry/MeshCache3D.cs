using System;
using System.Collections.Concurrent;
using System.Threading;
using ThreeDEngine.Core.Diagnostics;

namespace ThreeDEngine.Core.Geometry;

public sealed class MeshCache3D : IDisposable
{
    private readonly ConcurrentDictionary<MeshResourceKey, Lazy<Mesh3D>> _meshes = new();
    private readonly object _lifecycleGate = new();
    private long _hits;
    private long _misses;
    private bool _disposed;

    public int Count => _meshes.Count;
    public long HitCount => Interlocked.Read(ref _hits);
    public long MissCount => Interlocked.Read(ref _misses);

    public Mesh3D GetOrCreate(MeshResourceKey key, Func<Mesh3D> factory)
    {
        if (factory is null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        var candidate = new Lazy<Mesh3D>(factory, LazyThreadSafetyMode.ExecutionAndPublication);
        Lazy<Mesh3D> stored;
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            stored = _meshes.GetOrAdd(key, candidate);
        }
        if (ReferenceEquals(stored, candidate)) Interlocked.Increment(ref _misses);
        else Interlocked.Increment(ref _hits);
        try
        {
            return stored.Value ?? throw new InvalidOperationException($"Mesh factory for '{key}' returned null.");
        }
        catch
        {
            _meshes.TryRemove(key, out _);
            throw;
        }
    }

    public void Clear()
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _meshes.Clear();
        }
    }

    public void Dispose()
    {
        int count;
        lock (_lifecycleGate)
        {
            if (_disposed) return;
            _disposed = true;
            count = _meshes.Count;
            _meshes.Clear();
        }
        EngineLog3D.Information("Geometry.Cache", $"Engine mesh cache disposed; entries={count}, hits={HitCount}, misses={MissCount}.");
    }
}
