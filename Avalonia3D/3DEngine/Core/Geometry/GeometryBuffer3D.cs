using System;
using System.Collections;
using System.Collections.Generic;

namespace ThreeDEngine.Core.Geometry;

/// <summary>
/// Immutable, contiguous geometry stream. The constructor takes an ownership-safe snapshot;
/// consumers receive read-only memory and cannot invalidate retained GPU resources.
/// </summary>
public sealed class GeometryBuffer3D<T> : IReadOnlyList<T>
    where T : struct
{
    private readonly T[] _storage;

    private GeometryBuffer3D(T[] storage)
    {
        _storage = storage;
    }

    public static GeometryBuffer3D<T> Empty { get; } = new(Array.Empty<T>());

    public int Count => _storage.Length;
    public int Length => _storage.Length;
    public long LongLength => _storage.LongLength;
    public bool IsEmpty => _storage.Length == 0;
    public ReadOnlyMemory<T> Memory => ToArray();
    public ReadOnlySpan<T> Span => _storage;
    public T this[int index] => _storage[index];

    public T[] ToArray() => _storage.Length == 0 ? Array.Empty<T>() : (T[])_storage.Clone();

    public ReadOnlySpan<T> Slice(int start, int length) => _storage.AsSpan(start, length);

    public void CopyTo(Span<T> destination)
    {
        if (destination.Length < _storage.Length)
        {
            throw new ArgumentException("Destination is smaller than the geometry buffer.", nameof(destination));
        }

        _storage.AsSpan().CopyTo(destination);
    }

    internal static GeometryBuffer3D<T> CopyFrom(T[]? source)
        => source is null || source.Length == 0 ? Empty : new GeometryBuffer3D<T>((T[])source.Clone());

    internal static GeometryBuffer3D<T> TakeOwnership(T[]? storage)
        => storage is null || storage.Length == 0 ? Empty : new GeometryBuffer3D<T>(storage);

    internal T[] Storage => _storage;

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_storage).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _storage.GetEnumerator();
}
