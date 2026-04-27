using System;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Math;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Spatial;

public sealed class SpatialHashGrid3D
{
    private readonly Dictionary<CellKey, List<Object3D>> _cells = new();
    private readonly List<Object3D> _scratch = new();

    public SpatialHashGrid3D(float cellSize = 8f)
    {
        CellSize = MathF.Max(0.5f, cellSize);
    }

    public float CellSize { get; set; }
    public int Version { get; private set; }

    public void Clear()
    {
        _cells.Clear();
        Version++;
    }

    public void Add(Object3D obj, Bounds3D bounds)
    {
        if (!bounds.IsValid) return;
        var min = ToCell(bounds.Min);
        var max = ToCell(bounds.Max);
        for (var x = min.X; x <= max.X; x++)
        for (var y = min.Y; y <= max.Y; y++)
        for (var z = min.Z; z <= max.Z; z++)
        {
            var key = new CellKey(x, y, z);
            if (!_cells.TryGetValue(key, out var bucket))
            {
                bucket = new List<Object3D>(4);
                _cells[key] = bucket;
            }
            bucket.Add(obj);
        }
    }

    public IReadOnlyList<Object3D> QueryBounds(Bounds3D bounds)
    {
        _scratch.Clear();
        if (!bounds.IsValid) return _scratch;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var min = ToCell(bounds.Min);
        var max = ToCell(bounds.Max);
        for (var x = min.X; x <= max.X; x++)
        for (var y = min.Y; y <= max.Y; y++)
        for (var z = min.Z; z <= max.Z; z++)
        {
            if (!_cells.TryGetValue(new CellKey(x, y, z), out var bucket)) continue;
            for (var i = 0; i < bucket.Count; i++)
            {
                var obj = bucket[i];
                if (seen.Add(obj.Id)) _scratch.Add(obj);
            }
        }
        return _scratch;
    }

    public IReadOnlyList<Object3D> QueryRay(Ray ray, float maxDistance = 10000f, int maxSteps = 2048)
    {
        _scratch.Clear();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var step = MathF.Max(CellSize * 0.5f, 0.25f);
        var distance = 0f;
        for (var i = 0; i < maxSteps && distance <= maxDistance; i++, distance += step)
        {
            var p = ray.Origin + ray.Direction * distance;
            var key = ToCell(p);
            if (!_cells.TryGetValue(key, out var bucket)) continue;
            for (var b = 0; b < bucket.Count; b++)
            {
                var obj = bucket[b];
                if (seen.Add(obj.Id)) _scratch.Add(obj);
            }
        }
        return _scratch;
    }

    private CellKey ToCell(Vector3 p) => new(FastFloor(p.X / CellSize), FastFloor(p.Y / CellSize), FastFloor(p.Z / CellSize));

    private static int FastFloor(float value)
    {
        var i = (int)value;
        return value < i ? i - 1 : i;
    }

    private readonly struct CellKey : IEquatable<CellKey>
    {
        public CellKey(int x, int y, int z) { X = x; Y = y; Z = z; }
        public int X { get; }
        public int Y { get; }
        public int Z { get; }
        public bool Equals(CellKey other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object? obj) => obj is CellKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    }
}
