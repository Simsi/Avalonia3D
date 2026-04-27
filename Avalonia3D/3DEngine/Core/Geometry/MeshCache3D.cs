using System;
using System.Collections.Concurrent;

namespace ThreeDEngine.Core.Geometry;

public sealed class MeshCache3D
{
    private readonly ConcurrentDictionary<MeshResourceKey, Mesh3D> _meshes = new();

    public static MeshCache3D Shared { get; } = new MeshCache3D();

    public int Count => _meshes.Count;

    public Mesh3D GetOrCreate(MeshResourceKey key, Func<Mesh3D> factory)
    {
        if (factory is null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        return _meshes.GetOrAdd(key, static (k, f) => f(), factory);
    }

    public void Clear() => _meshes.Clear();
}
