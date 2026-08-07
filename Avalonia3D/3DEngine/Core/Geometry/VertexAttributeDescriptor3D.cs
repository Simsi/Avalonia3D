using System;

namespace ThreeDEngine.Core.Geometry;

public readonly struct VertexAttributeDescriptor3D : IEquatable<VertexAttributeDescriptor3D>
{
    public VertexAttributeDescriptor3D(VertexAttributeKind3D kind, VertexAttributeFormat3D format, int offsetBytes, bool normalized = false)
    {
        if (offsetBytes < 0) throw new ArgumentOutOfRangeException(nameof(offsetBytes));
        Kind = kind;
        Format = format;
        OffsetBytes = offsetBytes;
        Normalized = normalized || format is VertexAttributeFormat3D.SNorm16x4 or VertexAttributeFormat3D.UNorm8x4 or VertexAttributeFormat3D.UNorm16x4;
    }

    public VertexAttributeKind3D Kind { get; }
    public VertexAttributeFormat3D Format { get; }
    public int OffsetBytes { get; }
    public bool Normalized { get; }
    public int ComponentCount => Format switch
    {
        VertexAttributeFormat3D.Float1 or VertexAttributeFormat3D.UInt16x1 => 1,
        VertexAttributeFormat3D.Float2 or VertexAttributeFormat3D.Half2 => 2,
        VertexAttributeFormat3D.Float3 => 3,
        VertexAttributeFormat3D.Float4 or VertexAttributeFormat3D.Int4 or VertexAttributeFormat3D.SNorm16x4 or
            VertexAttributeFormat3D.UNorm8x4 or VertexAttributeFormat3D.UInt16x4 or VertexAttributeFormat3D.UNorm16x4 => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(Format), Format, "Unknown vertex attribute format.")
    };

    public int ByteCount => Format switch
    {
        VertexAttributeFormat3D.Float1 => sizeof(float),
        VertexAttributeFormat3D.Float2 => sizeof(float) * 2,
        VertexAttributeFormat3D.Float3 => sizeof(float) * 3,
        VertexAttributeFormat3D.Float4 => sizeof(float) * 4,
        VertexAttributeFormat3D.Int4 => sizeof(int) * 4,
        VertexAttributeFormat3D.Half2 => sizeof(ushort) * 2,
        VertexAttributeFormat3D.SNorm16x4 => sizeof(short) * 4,
        VertexAttributeFormat3D.UNorm8x4 => sizeof(byte) * 4,
        VertexAttributeFormat3D.UInt16x1 => sizeof(ushort),
        VertexAttributeFormat3D.UInt16x4 => sizeof(ushort) * 4,
        VertexAttributeFormat3D.UNorm16x4 => sizeof(ushort) * 4,
        _ => throw new ArgumentOutOfRangeException(nameof(Format), Format, "Unknown vertex attribute format.")
    };

    internal int AlignmentBytes => Format switch
    {
        VertexAttributeFormat3D.UNorm8x4 => 1,
        VertexAttributeFormat3D.Half2 or VertexAttributeFormat3D.SNorm16x4 or VertexAttributeFormat3D.UInt16x1 or
            VertexAttributeFormat3D.UInt16x4 or VertexAttributeFormat3D.UNorm16x4 => 2,
        _ => 4
    };

    public bool Equals(VertexAttributeDescriptor3D other)
        => Kind == other.Kind && Format == other.Format && OffsetBytes == other.OffsetBytes && Normalized == other.Normalized;

    public override bool Equals(object? obj) => obj is VertexAttributeDescriptor3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Kind, Format, OffsetBytes, Normalized);
    public override string ToString() => $"{Kind}:{Format}@{OffsetBytes}" + (Normalized ? ":normalized" : string.Empty);
}
