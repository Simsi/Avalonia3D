using System;

namespace ThreeDEngine.Core.Geometry;

public readonly struct VertexAttributeDescriptor3D : IEquatable<VertexAttributeDescriptor3D>
{
    public VertexAttributeDescriptor3D(VertexAttributeKind3D kind, VertexAttributeFormat3D format, int offsetBytes, bool normalized = false)
    {
        Kind = kind;
        Format = format;
        OffsetBytes = offsetBytes;
        Normalized = normalized;
    }

    public VertexAttributeKind3D Kind { get; }
    public VertexAttributeFormat3D Format { get; }
    public int OffsetBytes { get; }
    public bool Normalized { get; }
    public int ComponentCount => Format == VertexAttributeFormat3D.Int4 ? 4 : (int)Format;
    public int ByteCount => Format == VertexAttributeFormat3D.Int4 ? sizeof(int) * 4 : sizeof(float) * ComponentCount;

    public bool Equals(VertexAttributeDescriptor3D other)
        => Kind == other.Kind && Format == other.Format && OffsetBytes == other.OffsetBytes && Normalized == other.Normalized;

    public override bool Equals(object? obj) => obj is VertexAttributeDescriptor3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Kind, Format, OffsetBytes, Normalized);
    public override string ToString() => $"{Kind}:{Format}@{OffsetBytes}" + (Normalized ? ":normalized" : string.Empty);
}
