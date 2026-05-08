using System;
using System.Collections.Generic;
using System.Linq;

namespace ThreeDEngine.Core.Geometry;

public sealed class VertexLayout3D : IEquatable<VertexLayout3D>
{
    public VertexLayout3D(IEnumerable<VertexAttributeDescriptor3D> attributes, int strideBytes)
    {
        if (strideBytes <= 0) throw new ArgumentOutOfRangeException(nameof(strideBytes));
        Attributes = attributes?.ToArray() ?? throw new ArgumentNullException(nameof(attributes));
        StrideBytes = strideBytes;
    }

    public static VertexLayout3D PositionNormal { get; } = new(new[]
    {
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.Position, VertexAttributeFormat3D.Float3, 0),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.Normal, VertexAttributeFormat3D.Float3, sizeof(float) * 3)
    }, sizeof(float) * 6);

    public static VertexLayout3D PositionNormalTexCoord { get; } = new(new[]
    {
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.Position, VertexAttributeFormat3D.Float3, 0),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.Normal, VertexAttributeFormat3D.Float3, sizeof(float) * 3),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.TexCoord0, VertexAttributeFormat3D.Float2, sizeof(float) * 6)
    }, sizeof(float) * 8);


    public static VertexLayout3D PositionNormalTexCoordTangent { get; } = new(new[]
    {
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.Position, VertexAttributeFormat3D.Float3, 0),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.Normal, VertexAttributeFormat3D.Float3, sizeof(float) * 3),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.TexCoord0, VertexAttributeFormat3D.Float2, sizeof(float) * 6),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.Tangent, VertexAttributeFormat3D.Float4, sizeof(float) * 8)
    }, sizeof(float) * 12);

    public static VertexLayout3D PositionNormalTexCoordTangentColor { get; } = new(new[]
    {
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.Position, VertexAttributeFormat3D.Float3, 0),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.Normal, VertexAttributeFormat3D.Float3, sizeof(float) * 3),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.TexCoord0, VertexAttributeFormat3D.Float2, sizeof(float) * 6),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.Tangent, VertexAttributeFormat3D.Float4, sizeof(float) * 8),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.Color0, VertexAttributeFormat3D.Float4, sizeof(float) * 12)
    }, sizeof(float) * 16);



    public static VertexLayout3D PositionNormalTexCoordSkin { get; } = new(new[]
    {
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.Position, VertexAttributeFormat3D.Float3, 0),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.Normal, VertexAttributeFormat3D.Float3, sizeof(float) * 3),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.TexCoord0, VertexAttributeFormat3D.Float2, sizeof(float) * 6),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.BoneIndices, VertexAttributeFormat3D.Float4, sizeof(float) * 8),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.BoneWeights, VertexAttributeFormat3D.Float4, sizeof(float) * 12)
    }, sizeof(float) * 16);

    public static VertexLayout3D PositionNormalTexCoordTangentSkin { get; } = new(new[]
    {
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.Position, VertexAttributeFormat3D.Float3, 0),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.Normal, VertexAttributeFormat3D.Float3, sizeof(float) * 3),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.TexCoord0, VertexAttributeFormat3D.Float2, sizeof(float) * 6),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.Tangent, VertexAttributeFormat3D.Float4, sizeof(float) * 8),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.BoneIndices, VertexAttributeFormat3D.Float4, sizeof(float) * 12),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.BoneWeights, VertexAttributeFormat3D.Float4, sizeof(float) * 16)
    }, sizeof(float) * 20);

    public static VertexLayout3D PositionNormalColor { get; } = new(new[]
    {
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.Position, VertexAttributeFormat3D.Float3, 0),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.Normal, VertexAttributeFormat3D.Float3, sizeof(float) * 3),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.Color0, VertexAttributeFormat3D.Float4, sizeof(float) * 6)
    }, sizeof(float) * 10);

    public static VertexLayout3D PositionNormalTexCoordColor { get; } = new(new[]
    {
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.Position, VertexAttributeFormat3D.Float3, 0),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.Normal, VertexAttributeFormat3D.Float3, sizeof(float) * 3),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.TexCoord0, VertexAttributeFormat3D.Float2, sizeof(float) * 6),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.Color0, VertexAttributeFormat3D.Float4, sizeof(float) * 8)
    }, sizeof(float) * 12);

    public IReadOnlyList<VertexAttributeDescriptor3D> Attributes { get; }
    public int StrideBytes { get; }

    public bool Has(VertexAttributeKind3D kind)
    {
        for (var i = 0; i < Attributes.Count; i++)
        {
            if (Attributes[i].Kind == kind) return true;
        }

        return false;
    }

    public VertexAttributeDescriptor3D? Find(VertexAttributeKind3D kind)
    {
        for (var i = 0; i < Attributes.Count; i++)
        {
            if (Attributes[i].Kind == kind) return Attributes[i];
        }

        return null;
    }

    public bool Equals(VertexLayout3D? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null || StrideBytes != other.StrideBytes || Attributes.Count != other.Attributes.Count) return false;
        for (var i = 0; i < Attributes.Count; i++)
        {
            if (!Attributes[i].Equals(other.Attributes[i])) return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is VertexLayout3D other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(StrideBytes);
        for (var i = 0; i < Attributes.Count; i++) hash.Add(Attributes[i]);
        return hash.ToHashCode();
    }

    public override string ToString() => string.Join(",", Attributes) + $";stride={StrideBytes}";
}
