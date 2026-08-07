using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThreeDEngine.Core.Collision;

namespace ThreeDEngine.Core.HighScale;

public readonly struct HighScaleChunkKey3D : IEquatable<HighScaleChunkKey3D>
{
    public HighScaleChunkKey3D(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public int X { get; }
    public int Y { get; }
    public int Z { get; }

    public bool Equals(HighScaleChunkKey3D other) => X == other.X && Y == other.Y && Z == other.Z;
    public override bool Equals(object? obj) => obj is HighScaleChunkKey3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public override string ToString() => $"{X}:{Y}:{Z}";
}

public sealed class HighScaleChunk3D
{
    private readonly List<int> _indices = new();
    private readonly ReadOnlyCollection<int> _indicesView;

    public HighScaleChunk3D(HighScaleChunkKey3D key, Bounds3D bounds)
    {
        if (!bounds.IsValid) throw new ArgumentException("Chunk bounds must be valid.", nameof(bounds));
        Key = key;
        Bounds = bounds;
        _indicesView = _indices.AsReadOnly();
    }

    public HighScaleChunkKey3D Key { get; }
    public Bounds3D Bounds { get; internal set; }
    public IReadOnlyList<int> InstanceIndices => _indicesView;
    public int Version { get; private set; }
    public bool IsDirty { get; private set; } = true;
    internal bool BoundsDirty { get; private set; }

    internal void Clear()
    {
        _indices.Clear();
        Version++;
        IsDirty = true;
    }

    internal void Add(int instanceIndex)
    {
        if (instanceIndex < 0) throw new ArgumentOutOfRangeException(nameof(instanceIndex));
        _indices.Add(instanceIndex);
        Version++;
        IsDirty = true;
    }

    internal void MarkDirty()
    {
        Version++;
        IsDirty = true;
    }

    internal void MarkBoundsDirty() => BoundsDirty = true;

    internal void MarkBoundsClean() => BoundsDirty = false;

    internal void MarkClean() => IsDirty = false;
}
