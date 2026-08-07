using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Numerics;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Culling;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.HighScale;

public sealed class HighScaleChunkIndex3D
{
    private readonly Dictionary<HighScaleChunkKey3D, HighScaleChunk3D> _chunks = new();
    private readonly List<HighScaleChunk3D> _visibleScratch = new();
    private readonly ReadOnlyCollection<HighScaleChunk3D> _visibleView;
    private HighScaleChunkKey3D[] _instanceChunkKeys = Array.Empty<HighScaleChunkKey3D>();

    public HighScaleChunkIndex3D(float cellSize = 24f)
    {
        CellSize = Guard3D.Positive(cellSize, nameof(cellSize));
        _visibleView = _visibleScratch.AsReadOnly();
    }

    public float CellSize { get; }
    public IReadOnlyCollection<HighScaleChunk3D> Chunks => _chunks.Values;
    public int Version { get; private set; }
    public bool RebuildRequested { get; private set; }

    private void ClearRebuildRequested() => RebuildRequested = false;

    public void Rebuild(InstanceStore3D instances, Bounds3D templateLocalBounds)
    {
        if (instances is null) throw new ArgumentNullException(nameof(instances));
        if (!templateLocalBounds.IsValid) throw new ArgumentOutOfRangeException(nameof(templateLocalBounds), "Template bounds must be valid.");
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
        Guard3D.NonNegative(index, nameof(index));
        Guard3D.FiniteMatrix(transform, nameof(transform), requireInvertible: true);
        if (!templateLocalBounds.IsValid) throw new ArgumentOutOfRangeException(nameof(templateLocalBounds), "Template bounds must be valid.");
        EnsureInstanceKeyCapacity(index + 1);
        var key = ResolveKey(transform);
        _instanceChunkKeys[index] = key;
        var chunk = GetOrCreateChunk(key, templateLocalBounds.Transform(transform));
        chunk.Add(index);
        Version++;
    }

    public bool UpdateInstance(int index, Matrix4x4 transform, Bounds3D templateLocalBounds)
    {
        Guard3D.NonNegative(index, nameof(index));
        Guard3D.FiniteMatrix(transform, nameof(transform), requireInvertible: true);
        if (!templateLocalBounds.IsValid) throw new ArgumentOutOfRangeException(nameof(templateLocalBounds), "Template bounds must be valid.");
        EnsureInstanceKeyCapacity(index + 1);
        var oldKey = _instanceChunkKeys[index];
        var newKey = ResolveKey(transform);
        if (oldKey.Equals(newKey))
        {
            MarkInstanceDirty(index);
            if (_chunks.TryGetValue(oldKey, out var chunk))
            {
                chunk.MarkBoundsDirty();
            }
            return false;
        }

        RebuildRequested = true;
        Version++;
        return true;
    }

    public void MarkInstanceDirty(int index)
    {
        if ((uint)index >= (uint)_instanceChunkKeys.Length)
            throw new ArgumentOutOfRangeException(nameof(index), index, "Chunk membership has not been created for this instance index.");

        if (_chunks.TryGetValue(_instanceChunkKeys[index], out var chunk))
        {
            chunk.MarkDirty();
        }
    }

    internal IReadOnlyList<HighScaleChunk3D> QueryVisible(
        Matrix4x4 viewProjection,
        InstanceStore3D instances,
        Bounds3D templateLocalBounds)
    {
        _visibleScratch.Clear();
        foreach (var chunk in _chunks.Values)
        {
            EnsureExactBounds(chunk, instances, templateLocalBounds);
            if (FrustumCuller3D.IntersectsLocalBounds(chunk.Bounds, Matrix4x4.Identity, viewProjection))
            {
                _visibleScratch.Add(chunk);
            }
        }

        return _visibleView;
    }

    private static void EnsureExactBounds(
        HighScaleChunk3D chunk,
        InstanceStore3D instances,
        Bounds3D templateLocalBounds)
    {
        if (!chunk.BoundsDirty)
        {
            return;
        }

        var bounds = Bounds3D.Empty;
        var indices = chunk.InstanceIndices;
        for (var i = 0; i < indices.Count; i++)
        {
            var instanceIndex = indices[i];
            if ((uint)instanceIndex >= (uint)instances.Count)
            {
                continue;
            }

            bounds = bounds.Encapsulate(templateLocalBounds.Transform(instances[instanceIndex].Transform));
        }

        chunk.Bounds = bounds;
        chunk.MarkBoundsClean();
    }

    public HighScaleChunkKey3D ResolveKey(Matrix4x4 transform)
    {
        Guard3D.FiniteMatrix(transform, nameof(transform), requireInvertible: true);
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
