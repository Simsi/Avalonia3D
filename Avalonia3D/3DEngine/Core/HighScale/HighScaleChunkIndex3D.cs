using System;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Culling;

namespace ThreeDEngine.Core.HighScale;

public sealed class HighScaleChunkIndex3D
{
    private readonly Dictionary<HighScaleChunkKey3D, HighScaleChunk3D> _chunks = new();
    private readonly List<HighScaleChunk3D> _visibleScratch = new();
    private HighScaleChunkKey3D[] _instanceChunkKeys = Array.Empty<HighScaleChunkKey3D>();

    public HighScaleChunkIndex3D(float cellSize = 24f)
    {
        CellSize = MathF.Max(1f, cellSize);
    }

    public float CellSize { get; set; }
    public IReadOnlyCollection<HighScaleChunk3D> Chunks => _chunks.Values;
    public int Version { get; private set; }
    public bool RebuildRequested { get; private set; }

    public void ClearRebuildRequested() => RebuildRequested = false;

    public void Rebuild(InstanceStore3D instances, Bounds3D templateLocalBounds)
    {
        _chunks.Clear();
        EnsureInstanceKeyCapacity(instances.Count);
        for (var i = 0; i < instances.Count; i++)
        {
            AddInstance(i, instances[i].Transform, templateLocalBounds);
        }

        Version++;
        ClearRebuildRequested();
    }

    public void AddInstance(int index, Matrix4x4 transform, Bounds3D templateLocalBounds)
    {
        EnsureInstanceKeyCapacity(index + 1);
        var key = ResolveKey(transform);
        _instanceChunkKeys[index] = key;
        var chunk = GetOrCreateChunk(key, templateLocalBounds.Transform(transform));
        chunk.Add(index);
        Version++;
    }

    public bool UpdateInstance(int index, Matrix4x4 transform, Bounds3D templateLocalBounds)
    {
        EnsureInstanceKeyCapacity(index + 1);
        var oldKey = _instanceChunkKeys[index];
        var newKey = ResolveKey(transform);
        if (oldKey.Equals(newKey))
        {
            MarkInstanceDirty(index);
            return false;
        }

        RebuildRequested = true;
        Version++;
        return true;
    }

    public void MarkInstanceDirty(int index)
    {
        if ((uint)index >= (uint)_instanceChunkKeys.Length)
        {
            return;
        }

        if (_chunks.TryGetValue(_instanceChunkKeys[index], out var chunk))
        {
            chunk.MarkDirty();
        }
    }

    public IReadOnlyList<HighScaleChunk3D> QueryVisible(Matrix4x4 viewProjection)
    {
        _visibleScratch.Clear();
        foreach (var chunk in _chunks.Values)
        {
            if (FrustumCuller3D.IntersectsLocalBounds(chunk.Bounds, Matrix4x4.Identity, viewProjection))
            {
                _visibleScratch.Add(chunk);
            }
        }

        return _visibleScratch;
    }

    public HighScaleChunkKey3D ResolveKey(Matrix4x4 transform)
    {
        var p = new Vector3(transform.M41, transform.M42, transform.M43);
        return new HighScaleChunkKey3D(
            FastFloor(p.X / CellSize),
            FastFloor(p.Y / CellSize),
            FastFloor(p.Z / CellSize));
    }

    private HighScaleChunk3D GetOrCreateChunk(HighScaleChunkKey3D key, Bounds3D initialBounds)
    {
        if (_chunks.TryGetValue(key, out var chunk))
        {
            chunk.Bounds = chunk.Bounds.Encapsulate(initialBounds);
            return chunk;
        }

        chunk = new HighScaleChunk3D(key, initialBounds);
        _chunks[key] = chunk;
        return chunk;
    }

    private void EnsureInstanceKeyCapacity(int required)
    {
        if (_instanceChunkKeys.Length >= required)
        {
            return;
        }

        var newSize = System.Math.Max(required, System.Math.Max(4, _instanceChunkKeys.Length * 2));
        Array.Resize(ref _instanceChunkKeys, newSize);
    }

    private static int FastFloor(float value)
    {
        var i = (int)value;
        return value < i ? i - 1 : i;
    }
}
