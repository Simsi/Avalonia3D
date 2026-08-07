using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace ThreeDEngine.Core.Geometry;

/// <summary>
/// Immutable index stream stored in the narrowest lossless GPU format. UInt16 meshes no longer
/// retain an Int32 copy and are never compacted again on an OpenGL/WebGL upload path.
/// </summary>
public sealed class GeometryIndexBuffer3D : IReadOnlyList<int>
{
    private readonly ushort[]? _indices16;
    private readonly int[]? _indices32;
    private byte[]? _uploadBytes;

    private GeometryIndexBuffer3D(ushort[] indices)
    {
        _indices16 = indices;
        Format = IndexFormat3D.UInt16;
    }

    private GeometryIndexBuffer3D(int[] indices)
    {
        _indices32 = indices;
        Format = IndexFormat3D.UInt32;
    }

    public static GeometryIndexBuffer3D Empty { get; } = new(Array.Empty<ushort>());

    public IndexFormat3D Format { get; }
    public int ElementSizeBytes => (int)Format;
    public int Count => _indices16?.Length ?? _indices32?.Length ?? 0;
    public int Length => Count;
    public long LongLength => Count;
    public long ByteCount => (long)Count * ElementSizeBytes;
    public bool IsEmpty => Count == 0;
    public int this[int index] => _indices16 is not null ? _indices16[index] : _indices32![index];

    public int[] ToArray()
    {
        if (_indices32 is not null) return _indices32.Length == 0 ? Array.Empty<int>() : (int[])_indices32.Clone();
        if (_indices16 is null || _indices16.Length == 0) return Array.Empty<int>();
        var copy = new int[_indices16.Length];
        for (var i = 0; i < copy.Length; i++) copy[i] = _indices16[i];
        return copy;
    }

    public void CopyTo(Span<int> destination)
    {
        if (destination.Length < Count)
        {
            throw new ArgumentException("Destination is smaller than the index buffer.", nameof(destination));
        }

        if (_indices32 is not null)
        {
            _indices32.AsSpan().CopyTo(destination);
            return;
        }

        for (var i = 0; i < _indices16!.Length; i++) destination[i] = _indices16[i];
    }

    public bool TryGetUInt16Memory(out ReadOnlyMemory<ushort> memory)
    {
        memory = _indices16 is null || _indices16.Length == 0
            ? ReadOnlyMemory<ushort>.Empty
            : new ReadOnlyMemory<ushort>((ushort[])_indices16.Clone());
        return _indices16 is not null;
    }

    public bool TryGetUInt32Memory(out ReadOnlyMemory<int> memory)
    {
        memory = _indices32 is null || _indices32.Length == 0
            ? ReadOnlyMemory<int>.Empty
            : new ReadOnlyMemory<int>((int[])_indices32.Clone());
        return _indices32 is not null;
    }

    internal static GeometryIndexBuffer3D CopyFrom(int[]? source)
    {
        if (source is null || source.Length == 0) return Empty;
        var max = 0;
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] < 0) throw new ArgumentOutOfRangeException(nameof(source), "Geometry indices cannot be negative.");
            if (source[i] > max) max = source[i];
        }

        if (max <= ushort.MaxValue)
        {
            var compact = new ushort[source.Length];
            for (var i = 0; i < compact.Length; i++) compact[i] = checked((ushort)source[i]);
            return new GeometryIndexBuffer3D(compact);
        }

        return new GeometryIndexBuffer3D((int[])source.Clone());
    }


    internal static GeometryIndexBuffer3D TakeOwnership(int[]? source)
    {
        if (source is null || source.Length == 0) return Empty;
        var max = 0;
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] < 0) throw new ArgumentOutOfRangeException(nameof(source), "Geometry indices cannot be negative.");
            if (source[i] > max) max = source[i];
        }
        if (max <= ushort.MaxValue)
        {
            var compact = new ushort[source.Length];
            for (var i = 0; i < compact.Length; i++) compact[i] = checked((ushort)source[i]);
            return new GeometryIndexBuffer3D(compact);
        }
        return new GeometryIndexBuffer3D(source);
    }

    internal byte[] GetUploadBytes()
    {
        if (_uploadBytes is not null) return _uploadBytes;
        if (Count == 0) return _uploadBytes = Array.Empty<byte>();
        var bytes = new byte[checked(Count * ElementSizeBytes)];
        if (_indices16 is not null)
        {
            Buffer.BlockCopy(_indices16, 0, bytes, 0, bytes.Length);
        }
        else
        {
            Buffer.BlockCopy(_indices32!, 0, bytes, 0, bytes.Length);
        }

        return Interlocked.CompareExchange(ref _uploadBytes, bytes, null) ?? bytes;
    }

    internal ushort[]? UInt16Storage => _indices16;
    internal int[]? UInt32Storage => _indices32;

    public IEnumerator<int> GetEnumerator()
    {
        for (var i = 0; i < Count; i++) yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
