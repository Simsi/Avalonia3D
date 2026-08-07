using System;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Core.Collision;
using ThreeDEngine.Core.Math;
using ThreeDEngine.Core.Scene;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Spatial;

public sealed class SpatialQueryScratch3D
{
    public readonly List<Object3D> Results = new(64);
    internal readonly HashSet<Object3D> Seen = new(SpatialHashGrid3D.ObjectReferenceComparer.Instance);

    public void Clear()
    {
        Results.Clear();
        Seen.Clear();
    }
}

public sealed class SpatialHashGrid3D
{
    private const int MaxCellsPerObject = 10000;
    private const int MaxCellsPerQuery = 20000;
    private const int MaximumOverflowObjects = 65_536;
    private readonly Dictionary<CellKey, List<Object3D>> _cells = new();
    private readonly Dictionary<Object3D, List<CellKey>> _objectCells = new(ObjectReferenceComparer.Instance);
    private readonly HashSet<Object3D> _overflowObjects = new(ObjectReferenceComparer.Instance);
    private float _cellSize;
    private int _version;

    public SpatialHashGrid3D(float cellSize = 8f)
    {
        CellSize = cellSize;
    }

    public float CellSize
    {
        get => _cellSize;
        set
        {
            var validated = Guard3D.Positive(value, nameof(CellSize));
            if (_objectCells.Count != 0 && validated != _cellSize)
                throw new InvalidOperationException("CellSize cannot change while objects are indexed. Call Clear() before reconfiguring the grid.");
            _cellSize = validated;
        }
    }
    public int Version => _version;
    public int IndexedObjectCount => _objectCells.Count;
    public int OverflowObjectCount => _overflowObjects.Count;

    public void Clear()
    {
        _cells.Clear();
        _objectCells.Clear();
        _overflowObjects.Clear();
        IncrementVersion();
    }

    public void Add(Object3D obj, Bounds3D bounds)
    {
        if (obj is null) throw new ArgumentNullException(nameof(obj));

        // Reuse the per-object cell list across transform-only updates. The old
        // implementation allocated a new List<CellKey> for every Add/Update, which
        // showed up in pointer-picking and physics-heavy scenes.
        if (!_objectCells.TryGetValue(obj, out var keys))
        {
            keys = new List<CellKey>(8);
            _objectCells[obj] = keys;
        }
        else if (keys.Count > 0)
        {
            RemoveFromCells(obj, keys);
            keys.Clear();
        }

        _overflowObjects.Remove(obj);
        if (!IsUsable(bounds))
        {
            AddOverflowObject(obj);
            IncrementVersion();
            return;
        }

        if (!TryToCell(bounds.Min, out var min) || !TryToCell(bounds.Max, out var max))
        {
            AddOverflowObject(obj);
            IncrementVersion();
            return;
        }
        if (!CanEnumerateCellRange(min, max, MaxCellsPerObject))
        {
            AddOverflowObject(obj);
            IncrementVersion();
            return;
        }

        for (long x = min.X; x <= max.X; x++)
        for (long y = min.Y; y <= max.Y; y++)
        for (long z = min.Z; z <= max.Z; z++)
        {
            var key = new CellKey((int)x, (int)y, (int)z);
            if (!_cells.TryGetValue(key, out var bucket))
            {
                bucket = new List<Object3D>(4);
                _cells[key] = bucket;
            }

            bucket.Add(obj);
            keys.Add(key);
        }

        IncrementVersion();
    }

    public bool Remove(Object3D obj)
    {
        if (obj is null || !_objectCells.TryGetValue(obj, out var keys)) return false;
        RemoveFromCells(obj, keys);
        _objectCells.Remove(obj);
        _overflowObjects.Remove(obj);
        keys.Clear();
        IncrementVersion();
        return true;
    }

    public void Update(Object3D obj, Bounds3D bounds)
    {
        Add(obj, bounds);
    }

    private void RemoveFromCells(Object3D obj, List<CellKey> keys)
    {
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            if (!_cells.TryGetValue(key, out var bucket)) continue;
            for (var j = bucket.Count - 1; j >= 0; j--)
            {
                if (ReferenceEquals(bucket[j], obj)) bucket.RemoveAt(j);
            }
            if (bucket.Count == 0) _cells.Remove(key);
        }
    }

    public IReadOnlyList<Object3D> QueryBounds(Bounds3D bounds)
    {
        var scratch = new SpatialQueryScratch3D();
        QueryBounds(bounds, scratch);
        return scratch.Results.ToArray();
    }

    public List<Object3D> QueryBounds(Bounds3D bounds, SpatialQueryScratch3D scratch)
    {
        ArgumentNullException.ThrowIfNull(scratch);
        scratch.Clear();
        if (!IsUsable(bounds)) throw new ArgumentException("Spatial query bounds must be valid and finite.", nameof(bounds));
        if (!TryToCell(bounds.Min, out var min) || !TryToCell(bounds.Max, out var max))
            throw new ArgumentOutOfRangeException(nameof(bounds), "Spatial query bounds exceed the representable grid coordinate range.");
        if (!CanEnumerateCellRange(min, max, MaxCellsPerQuery))
            throw new InvalidOperationException($"Spatial query spans more than {MaxCellsPerQuery} cells. Partition the query; silent incomplete results and full-scan fallback are prohibited.");

        for (long x = min.X; x <= max.X; x++)
        for (long y = min.Y; y <= max.Y; y++)
        for (long z = min.Z; z <= max.Z; z++)
        {
            AddCellObjects(new CellKey((int)x, (int)y, (int)z), scratch.Seen, scratch.Results);
        }
        AddOverflowObjects(scratch);
        return scratch.Results;
    }

    public IReadOnlyList<Object3D> QueryRay(Ray ray, float maxDistance = 10000f, int maxSteps = 4096)
    {
        var scratch = new SpatialQueryScratch3D();
        QueryRay(ray, scratch, maxDistance, maxSteps);
        return scratch.Results.ToArray();
    }

    public List<Object3D> QueryRay(Ray ray, SpatialQueryScratch3D scratch, float maxDistance = 10000f, int maxSteps = 4096)
    {
        ArgumentNullException.ThrowIfNull(scratch);
        scratch.Clear();
        if (!IsFinite(ray.Origin) || !IsFinite(ray.Direction) || ray.Direction.LengthSquared() < 0.000001f)
            throw new ArgumentException("Spatial ray origin/direction must be finite and direction must be non-zero.", nameof(ray));
        if (!float.IsFinite(maxDistance) || maxDistance <= 0f) throw new ArgumentOutOfRangeException(nameof(maxDistance));
        if (maxSteps <= 0) throw new ArgumentOutOfRangeException(nameof(maxSteps));

        var direction = Vector3.Normalize(ray.Direction);
        if (!TryToCell(ray.Origin, out var cell))
            throw new ArgumentOutOfRangeException(nameof(ray), "Spatial ray origin exceeds the representable grid coordinate range.");
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
        var steps = 0;
        for (; steps < maxSteps && distance <= maxDistance; steps++)
        {
            AddCellObjects(cell, scratch.Seen, scratch.Results);

            if (tMaxX <= tMaxY && tMaxX <= tMaxZ)
            {
                distance = tMaxX;
                cell = new CellKey(AdvanceCoordinate(cell.X, stepX), cell.Y, cell.Z);
                tMaxX += tDeltaX;
            }
            else if (tMaxY <= tMaxZ)
            {
                distance = tMaxY;
                cell = new CellKey(cell.X, AdvanceCoordinate(cell.Y, stepY), cell.Z);
                tMaxY += tDeltaY;
            }
            else
            {
                distance = tMaxZ;
                cell = new CellKey(cell.X, cell.Y, AdvanceCoordinate(cell.Z, stepZ));
                tMaxZ += tDeltaZ;
            }

            if (!float.IsFinite(distance)) break;
        }
        if (steps >= maxSteps && distance <= maxDistance)
            throw new InvalidOperationException($"Spatial ray traversal exhausted maxSteps={maxSteps} before reaching maxDistance={maxDistance}. Increase maxSteps or partition the query; incomplete results are prohibited.");
        AddOverflowObjects(scratch);
        return scratch.Results;
    }

    private void AddOverflowObject(Object3D obj)
    {
        if (_overflowObjects.Contains(obj)) return;
        if (_overflowObjects.Count >= MaximumOverflowObjects)
            throw new InvalidOperationException($"Spatial overflow capacity {MaximumOverflowObjects} is exhausted. Bounds must be partitioned or corrected; full-scan fallback is prohibited.");
        _overflowObjects.Add(obj);
    }

    private void AddOverflowObjects(SpatialQueryScratch3D scratch)
    {
        foreach (var obj in _overflowObjects)
        {
            if (scratch.Seen.Add(obj)) scratch.Results.Add(obj);
        }
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

    private bool TryToCell(Vector3 point, out CellKey cell)
    {
        var x = point.X / CellSize;
        var y = point.Y / CellSize;
        var z = point.Z / CellSize;
        if (!CanConvertToCellCoordinate(x) || !CanConvertToCellCoordinate(y) || !CanConvertToCellCoordinate(z))
        {
            cell = default;
            return false;
        }
        cell = new CellKey(FastFloor(x), FastFloor(y), FastFloor(z));
        return true;
    }

    private static bool CanConvertToCellCoordinate(float value)
        => float.IsFinite(value) && value >= int.MinValue && value < int.MaxValue;

    private static int AdvanceCoordinate(int value, int step)
    {
        if ((step > 0 && value == int.MaxValue) || (step < 0 && value == int.MinValue))
            throw new InvalidOperationException("Spatial ray traversal exceeded the representable grid coordinate range.");
        return value + step;
    }

    private static bool IsUsable(Bounds3D bounds) => bounds.IsValid && IsFinite(bounds.Min) && IsFinite(bounds.Max);
    private static bool IsFinite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);

    private static bool CanEnumerateCellRange(CellKey min, CellKey max, int limit)
    {
        if (max.X < min.X || max.Y < min.Y || max.Z < min.Z) return false;
        var x = (long)max.X - min.X + 1L;
        var y = (long)max.Y - min.Y + 1L;
        var z = (long)max.Z - min.Z + 1L;
        if (x <= 0L || y <= 0L || z <= 0L || limit <= 0) return false;
        if (x > limit || y > limit / x) return false;
        var xy = x * y;
        return z <= limit / xy;
    }

    private void IncrementVersion() => _version = checked(_version + 1);

    private static int FastFloor(float value)
    {
        var i = (int)value;
        return value < i ? i - 1 : i;
    }

    internal sealed class ObjectReferenceComparer : IEqualityComparer<Object3D>
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
