using System;

namespace ThreeDEngine.Core.Geometry;

/// <summary>Immutable packed backend upload buffer built once per geometry resource.</summary>
public sealed class InterleavedVertexBuffer3D
{
    internal InterleavedVertexBuffer3D(byte[] storage, VertexLayout3D layout, int vertexCount)
    {
        Storage = storage ?? throw new ArgumentNullException(nameof(storage));
        Layout = layout ?? throw new ArgumentNullException(nameof(layout));
        if (vertexCount < 0) throw new ArgumentOutOfRangeException(nameof(vertexCount));
        if (storage.Length != checked(vertexCount * layout.StrideBytes))
            throw new ArgumentException("Interleaved storage length does not match vertex count and layout stride.", nameof(storage));
        VertexCount = vertexCount;
    }

    public int VertexCount { get; }
    public VertexLayout3D Layout { get; }
    public ReadOnlyMemory<byte> Memory => Storage.Length == 0 ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>((byte[])Storage.Clone());
    public long ByteCount => Storage.LongLength;
    internal byte[] Storage { get; }
}
