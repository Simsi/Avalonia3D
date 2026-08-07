using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.Geometry;

public sealed class VertexLayout3D : IEquatable<VertexLayout3D>
{
    public VertexLayout3D(IEnumerable<VertexAttributeDescriptor3D> attributes, int strideBytes)
    {
        if (strideBytes <= 0) throw new ArgumentOutOfRangeException(nameof(strideBytes));
        var snapshot = attributes?.ToArray() ?? throw new ArgumentNullException(nameof(attributes));
        if (snapshot.Length == 0) throw new ArgumentException("A vertex layout must contain at least one attribute.", nameof(attributes));
        var kinds = new HashSet<VertexAttributeKind3D>();
        for (var i = 0; i < snapshot.Length; i++)
        {
            var attribute = snapshot[i];
            if (!Enum.IsDefined(attribute.Kind)) throw new ArgumentOutOfRangeException(nameof(attributes), $"Unknown vertex attribute kind '{attribute.Kind}'.");
            if (!Enum.IsDefined(attribute.Format)) throw new ArgumentOutOfRangeException(nameof(attributes), $"Unknown vertex attribute format '{attribute.Format}'.");
            if (!kinds.Add(attribute.Kind)) throw new ArgumentException($"Vertex attribute '{attribute.Kind}' is declared more than once.", nameof(attributes));
            if (attribute.OffsetBytes + attribute.ByteCount > strideBytes)
                throw new ArgumentOutOfRangeException(nameof(attributes), $"Vertex attribute '{attribute.Kind}' lies outside the {strideBytes}-byte stride.");
            if (attribute.OffsetBytes % attribute.AlignmentBytes != 0)
                throw new ArgumentException($"Vertex attribute '{attribute.Kind}' is not aligned to {attribute.AlignmentBytes} bytes.", nameof(attributes));
            for (var previousIndex = 0; previousIndex < i; previousIndex++)
            {
                var previous = snapshot[previousIndex];
                var overlaps = attribute.OffsetBytes < previous.OffsetBytes + previous.ByteCount &&
                    previous.OffsetBytes < attribute.OffsetBytes + attribute.ByteCount;
                if (overlaps)
                    throw new ArgumentException($"Vertex attributes '{previous.Kind}' and '{attribute.Kind}' overlap inside the layout.", nameof(attributes));
            }
        }
        if (!kinds.Contains(VertexAttributeKind3D.Position)) throw new ArgumentException("A vertex layout must contain a position attribute.", nameof(attributes));

        Attributes = Array.AsReadOnly(snapshot);
        StrideBytes = strideBytes;
    }

    public const int GpuMeshFloatStride = 25;

    /// <summary>Legacy full-precision layout retained for explicit compatibility/preprocessing tests.</summary>
    public static VertexLayout3D GpuMesh { get; } = new(new[]
    {
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.Position, VertexAttributeFormat3D.Float3, sizeof(float) * 0),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.Normal, VertexAttributeFormat3D.Float3, sizeof(float) * 3),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.TexCoord0, VertexAttributeFormat3D.Float2, sizeof(float) * 6),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.Tangent, VertexAttributeFormat3D.Float4, sizeof(float) * 8),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.Color0, VertexAttributeFormat3D.Float4, sizeof(float) * 12),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.MaterialSlot, VertexAttributeFormat3D.Float1, sizeof(float) * 16),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.BoneIndices, VertexAttributeFormat3D.Float4, sizeof(float) * 17),
        new VertexAttributeDescriptor3D(VertexAttributeKind3D.BoneWeights, VertexAttributeFormat3D.Float4, sizeof(float) * 21)
    }, sizeof(float) * GpuMeshFloatStride);

    public static VertexLayout3D PositionNormal { get; } = Create(false, false, false, false, false, false, false, false, null, null, null, null, null);
    public static VertexLayout3D PositionNormalTexCoord { get; } = Create(false, false, true, false, false, false, false, false, null, null, null, null, null);
    public static VertexLayout3D PositionNormalTexCoordTangent { get; } = Create(false, false, true, true, false, false, false, false, null, null, null, null, null);
    public static VertexLayout3D PositionNormalTexCoordTangentColor { get; } = Create(false, false, true, true, true, false, false, false, null, null, null, null, null);
    public static VertexLayout3D PositionNormalTexCoordSkin { get; } = Create(false, false, true, false, false, false, true, false, null, null, null, null, null);
    public static VertexLayout3D PositionNormalTexCoordTangentSkin { get; } = Create(false, false, true, true, false, false, true, false, null, null, null, null, null);
    public static VertexLayout3D PositionNormalColor { get; } = Create(false, false, false, false, true, false, false, false, null, null, null, null, null);
    public static VertexLayout3D PositionNormalTexCoordColor { get; } = Create(false, false, true, false, true, false, false, false, null, null, null, null, null);

    public IReadOnlyList<VertexAttributeDescriptor3D> Attributes { get; }
    public int StrideBytes { get; }

    public bool Has(VertexAttributeKind3D kind) => Find(kind).HasValue;

    public VertexAttributeDescriptor3D? Find(VertexAttributeKind3D kind)
    {
        for (var i = 0; i < Attributes.Count; i++) if (Attributes[i].Kind == kind) return Attributes[i];
        return null;
    }

    internal static VertexLayout3D CreateForGeometry(
        bool pack,
        bool allowHalfPrecisionTexCoords,
        bool hasTexCoords,
        bool hasTangents,
        bool hasColors,
        bool hasMaterialSlots,
        bool hasSkinWeights,
        Vector2[] texCoords,
        ColorRgba[] colors,
        float[] materialSlots,
        Vector4[] boneIndices,
        Vector4[] boneWeights,
        Vector3[]? suppliedNormals,
        Vector4[]? suppliedTangents)
        => Create(
            pack,
            allowHalfPrecisionTexCoords,
            hasTexCoords,
            hasTangents,
            hasColors,
            hasMaterialSlots,
            hasSkinWeights,
            suppliedNormals is not null && !CanPackSignedUnit(suppliedNormals),
            texCoords,
            colors,
            materialSlots,
            boneIndices,
            boneWeights,
            suppliedTangents is not null && !CanPackSignedUnit(suppliedTangents));

    private static VertexLayout3D Create(
        bool pack,
        bool allowHalfPrecisionTexCoords,
        bool hasTexCoords,
        bool hasTangents,
        bool hasColors,
        bool hasMaterialSlots,
        bool hasSkinWeights,
        bool forceFloatNormals,
        Vector2[]? texCoords,
        ColorRgba[]? colors,
        float[]? materialSlots,
        Vector4[]? boneIndices,
        Vector4[]? boneWeights,
        bool forceFloatTangents = false)
    {
        var attributes = new List<VertexAttributeDescriptor3D>(8);
        var offset = 0;
        Add(VertexAttributeKind3D.Position, VertexAttributeFormat3D.Float3);
        Add(VertexAttributeKind3D.Normal, pack && !forceFloatNormals ? VertexAttributeFormat3D.SNorm16x4 : VertexAttributeFormat3D.Float3);
        if (hasTexCoords) Add(VertexAttributeKind3D.TexCoord0, pack && allowHalfPrecisionTexCoords && CanPackHalf(texCoords) ? VertexAttributeFormat3D.Half2 : VertexAttributeFormat3D.Float2);
        if (hasTangents) Add(VertexAttributeKind3D.Tangent, pack && !forceFloatTangents ? VertexAttributeFormat3D.SNorm16x4 : VertexAttributeFormat3D.Float4);
        if (hasColors) Add(VertexAttributeKind3D.Color0, pack && CanPackUnorm8(colors) ? VertexAttributeFormat3D.UNorm8x4 : VertexAttributeFormat3D.Float4);
        if (hasMaterialSlots) Add(VertexAttributeKind3D.MaterialSlot, pack && CanPackUInt16(materialSlots) ? VertexAttributeFormat3D.UInt16x1 : VertexAttributeFormat3D.Float1);
        if (hasSkinWeights)
        {
            Add(VertexAttributeKind3D.BoneIndices, pack && CanPackUInt16(boneIndices) ? VertexAttributeFormat3D.UInt16x4 : VertexAttributeFormat3D.Float4);
            Add(VertexAttributeKind3D.BoneWeights, pack && CanPackUnorm16(boneWeights) ? VertexAttributeFormat3D.UNorm16x4 : VertexAttributeFormat3D.Float4);
        }
        return new VertexLayout3D(attributes, Align(offset, 4));

        void Add(VertexAttributeKind3D kind, VertexAttributeFormat3D format)
        {
            var descriptor = new VertexAttributeDescriptor3D(kind, format, 0);
            offset = Align(offset, descriptor.AlignmentBytes);
            attributes.Add(new VertexAttributeDescriptor3D(kind, format, offset));
            offset += descriptor.ByteCount;
        }
    }

    private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;

    private static bool CanPackHalf(Vector2[]? values)
    {
        if (values is null) return true;
        const float maxHalf = 65504f;
        for (var i = 0; i < values.Length; i++)
        {
            if (global::System.MathF.Abs(values[i].X) > maxHalf || global::System.MathF.Abs(values[i].Y) > maxHalf) return false;
        }
        return true;
    }

    private static bool CanPackSignedUnit(Vector3[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            if (value.X < -1f || value.X > 1f || value.Y < -1f || value.Y > 1f || value.Z < -1f || value.Z > 1f) return false;
        }
        return true;
    }

    private static bool CanPackSignedUnit(Vector4[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            if (value.X < -1f || value.X > 1f || value.Y < -1f || value.Y > 1f || value.Z < -1f || value.Z > 1f || value.W < -1f || value.W > 1f) return false;
        }
        return true;
    }

    private static bool CanPackUnorm8(ColorRgba[]? values)
    {
        if (values is null) return true;
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            if (value.R < 0f || value.R > 1f || value.G < 0f || value.G > 1f || value.B < 0f || value.B > 1f || value.A < 0f || value.A > 1f) return false;
        }
        return true;
    }

    private static bool CanPackUInt16(float[]? values)
    {
        if (values is null) return true;
        for (var i = 0; i < values.Length; i++) if (values[i] > ushort.MaxValue) return false;
        return true;
    }

    private static bool CanPackUInt16(Vector4[]? values)
    {
        if (values is null) return true;
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            if (value.X > ushort.MaxValue || value.Y > ushort.MaxValue || value.Z > ushort.MaxValue || value.W > ushort.MaxValue) return false;
        }
        return true;
    }

    private static bool CanPackUnorm16(Vector4[]? values)
    {
        if (values is null) return true;
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            if (value.X < 0f || value.X > 1f || value.Y < 0f || value.Y > 1f || value.Z < 0f || value.Z > 1f || value.W < 0f || value.W > 1f) return false;
        }
        return true;
    }

    public bool Equals(VertexLayout3D? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null || StrideBytes != other.StrideBytes || Attributes.Count != other.Attributes.Count) return false;
        for (var i = 0; i < Attributes.Count; i++) if (!Attributes[i].Equals(other.Attributes[i])) return false;
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
