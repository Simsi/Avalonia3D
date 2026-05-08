using System;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Math;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Spatial;

public sealed class SpatialHashGrid3D
{
    private const int MaxCellsPerObject = 10000;
    private const int MaxCellsPerQuery = 20000;
    private readonly Dictionary<CellKey, List<Object3D>> _cells = new();
    private float _cellSize;

    public SpatialHashGrid3D(float cellSize = 8f)
    {
        CellSize = cellSize;
    }

    public float CellSize
    {
        get => _cellSize;
        set => _cellSize = MathF.Max(0.5f, float.IsFinite(value) ? value : 8f);
    }
    public int Version { get; private set; }

    public void Clear()
    {
        _cells.Clear();
        Version++;
    }

    public void Add(Object3D obj, Bounds3D bounds)
    {
        if (obj is null || !IsUsable(bounds)) return;
        var min = ToCell(bounds.Min);
        var max = ToCell(bounds.Max);
        if (!CanEnumerateCellRange(min, max, MaxCellsPerObject)) return;

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

            if (!bucket.Contains(obj)) bucket.Add(obj);
        }
    }

    public IReadOnlyList<Object3D> QueryBounds(Bounds3D bounds)
    {
        var result = new List<Object3D>();
        if (!IsUsable(bounds)) return result;
        var seen = new HashSet<Object3D>(ObjectReferenceComparer.Instance);
        var min = ToCell(bounds.Min);
        var max = ToCell(bounds.Max);
        if (!CanEnumerateCellRange(min, max, MaxCellsPerQuery)) return result;

        for (var x = min.X; x <= max.X; x++)
        for (var y = min.Y; y <= max.Y; y++)
        for (var z = min.Z; z <= max.Z; z++)
        {
            if (!_cells.TryGetValue(new CellKey(x, y, z), out var bucket)) continue;
            for (var i = 0; i < bucket.Count; i++)
            {
                var obj = bucket[i];
                if (seen.Add(obj)) result.Add(obj);
            }
        }
        return result;
    }

    public IReadOnlyList<Object3D> QueryRay(Ray ray, float maxDistance = 10000f, int maxSteps = 2048)
    {
        var result = new List<Object3D>();
        if (!IsFinite(ray.Origin) || !IsFinite(ray.Direction) || ray.Direction.LengthSquared() < 0.000001f) return result;
        if (!float.IsFinite(maxDistance) || maxDistance <= 0f || maxSteps <= 0) return result;

        var seen = new HashSet<Object3D>(ObjectReferenceComparer.Instance);
        var direction = Vector3.Normalize(ray.Direction);
        var cell = ToCell(ray.Origin);
        var stepX = direction.X >= 0f ? 1 : -1;
        var stepY = direction.Y >= 0f ? 1 : -1;
        var stepZ = direction.Z >= 0f ? 1 : -1;

        var nextBoundaryX = (direction.X >= 0f ? cell.X + 1 : cell.X) * CellSize;
        var nextBoundaryY = (direction.Y >= 0f ? cell.Y + 1 : cell.Y) * CellSize;
        var nextBoundaryZ = (direction.Z >= 0f ? cell.Z + 1 : cell.Z) * CellSize;

        var tMaxX = MathF.Abs(direction.X) < 0.000001f ? float.PositiveInfinity : (nextBoundaryX - ray.Origin.X) / direction.X;
        var tMaxY = MathF.Abs(direction.Y) < 0.000001f ? float.PositiveInfinity : (nextBoundaryY - ray.Origin.Y) / direction.Y;
        var tMaxZ = MathF.Abs(direction.Z) < 0.000001f ? float.PositiveInfinity : (nextBoundaryZ - ray.Origin.Z) / direction.Z;
        var tDeltaX = MathF.Abs(direction.X) < 0.000001f ? float.PositiveInfinity : CellSize / MathF.Abs(direction.X);
        var tDeltaY = MathF.Abs(direction.Y) < 0.000001f ? float.PositiveInfinity : CellSize / MathF.Abs(direction.Y);
        var tDeltaZ = MathF.Abs(direction.Z) < 0.000001f ? float.PositiveInfinity : CellSize / MathF.Abs(direction.Z);

        var distance = 0f;
        for (var i = 0; i < maxSteps && distance <= maxDistance; i++)
        {
            AddCellObjects(cell, seen, result);

            if (tMaxX <= tMaxY && tMaxX <= tMaxZ)
            {
                distance = tMaxX;
                cell = new CellKey(cell.X + stepX, cell.Y, cell.Z);
                tMaxX += tDeltaX;
            }
            else if (tMaxY <= tMaxZ)
            {
                distance = tMaxY;
                cell = new CellKey(cell.X, cell.Y + stepY, cell.Z);
                tMaxY += tDeltaY;
            }
            else
            {
                distance = tMaxZ;
                cell = new CellKey(cell.X, cell.Y, cell.Z + stepZ);
                tMaxZ += tDeltaZ;
            }

            if (!float.IsFinite(distance)) break;
        }
        return result;
    }

    private void AddCellObjects(CellKey key, HashSet<Object3D> seen, List<Object3D> result)
    {
        if (!_cells.TryGetValue(key, out var bucket)) return;
        for (var b = 0; b < bucket.Count; b++)
        {
            var obj = bucket[b];
            if (seen.Add(obj)) result.Add(obj);
        }
    }

    private CellKey ToCell(Vector3 p) => new(FastFloor(p.X / CellSize), FastFloor(p.Y / CellSize), FastFloor(p.Z / CellSize));

    private static bool IsUsable(Bounds3D bounds) => bounds.IsValid && IsFinite(bounds.Min) && IsFinite(bounds.Max);
    private static bool IsFinite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);

    private static bool CanEnumerateCellRange(CellKey min, CellKey max, int limit)
    {
        if (max.X < min.X || max.Y < min.Y || max.Z < min.Z) return false;
        var x = (long)max.X - min.X + 1L;
        var y = (long)max.Y - min.Y + 1L;
        var z = (long)max.Z - min.Z + 1L;
        if (x <= 0L || y <= 0L || z <= 0L) return false;
        return x * y * z <= limit;
    }

    private static int FastFloor(float value)
    {
        var i = (int)value;
        return value < i ? i - 1 : i;
    }

    private sealed class ObjectReferenceComparer : IEqualityComparer<Object3D>
    {
        public static readonly ObjectReferenceComparer Instance = new();
        public bool Equals(Object3D? x, Object3D? y) => ReferenceEquals(x, y);
        public int GetHashCode(Object3D obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
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
