using System;
using System.Buffers.Binary;
using System.Numerics;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.Geometry;

internal static class PackedVertexBufferBuilder3D
{
    public static InterleavedVertexBuffer3D Build(RenderGeometry3D geometry)
    {
        var layout = geometry.Layout;
        var storage = new byte[checked(geometry.VertexCount * layout.StrideBytes)];
        var normals = geometry.Normals;
        var tangents = geometry.HasTangents ? geometry.Tangents : GeometryBuffer3D<Vector4>.Empty;
        for (var vertex = 0; vertex < geometry.VertexCount; vertex++)
        {
            var baseOffset = vertex * layout.StrideBytes;
            for (var attributeIndex = 0; attributeIndex < layout.Attributes.Count; attributeIndex++)
            {
                var attribute = layout.Attributes[attributeIndex];
                var destination = storage.AsSpan(baseOffset + attribute.OffsetBytes, attribute.ByteCount);
                switch (attribute.Kind)
                {
                    case VertexAttributeKind3D.Position:
                        WriteVector3(destination, geometry.Positions[vertex], attribute.Format);
                        break;
                    case VertexAttributeKind3D.Normal:
                        WriteVector3(destination, normals[vertex], attribute.Format);
                        break;
                    case VertexAttributeKind3D.TexCoord0:
                        WriteVector2(destination, geometry.TexCoords0[vertex], attribute.Format);
                        break;
                    case VertexAttributeKind3D.Tangent:
                        WriteVector4(destination, tangents[vertex], attribute.Format);
                        break;
                    case VertexAttributeKind3D.Color0:
                        WriteColor(destination, geometry.Colors0[vertex], attribute.Format);
                        break;
                    case VertexAttributeKind3D.MaterialSlot:
                        WriteScalar(destination, geometry.MaterialSlots[vertex], attribute.Format);
                        break;
                    case VertexAttributeKind3D.BoneIndices:
                        WriteVector4(destination, geometry.BoneIndices0[vertex], attribute.Format);
                        break;
                    case VertexAttributeKind3D.BoneWeights:
                        WriteVector4(destination, geometry.BoneWeights0[vertex], attribute.Format);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(attribute.Kind), attribute.Kind, "Unknown vertex attribute kind.");
                }
            }
        }
        return new InterleavedVertexBuffer3D(storage, layout, geometry.VertexCount);
    }

    private static void WriteScalar(Span<byte> destination, float value, VertexAttributeFormat3D format)
    {
        switch (format)
        {
            case VertexAttributeFormat3D.Float1:
                WriteSingle(destination, value);
                break;
            case VertexAttributeFormat3D.UInt16x1:
                BinaryPrimitives.WriteUInt16LittleEndian(destination, checked((ushort)MathF.Round(value)));
                break;
            default:
                throw Unsupported(format);
        }
    }

    private static void WriteVector2(Span<byte> destination, Vector2 value, VertexAttributeFormat3D format)
    {
        switch (format)
        {
            case VertexAttributeFormat3D.Float2:
                WriteSingle(destination, value.X);
                WriteSingle(destination[4..], value.Y);
                break;
            case VertexAttributeFormat3D.Half2:
                BinaryPrimitives.WriteUInt16LittleEndian(destination, BitConverter.HalfToUInt16Bits((Half)value.X));
                BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], BitConverter.HalfToUInt16Bits((Half)value.Y));
                break;
            default:
                throw Unsupported(format);
        }
    }

    private static void WriteVector3(Span<byte> destination, Vector3 value, VertexAttributeFormat3D format)
    {
        switch (format)
        {
            case VertexAttributeFormat3D.Float3:
                WriteSingle(destination, value.X);
                WriteSingle(destination[4..], value.Y);
                WriteSingle(destination[8..], value.Z);
                break;
            case VertexAttributeFormat3D.SNorm16x4:
                WriteSNorm16x4(destination, new Vector4(value, 0f));
                break;
            default:
                throw Unsupported(format);
        }
    }

    private static void WriteVector4(Span<byte> destination, Vector4 value, VertexAttributeFormat3D format)
    {
        switch (format)
        {
            case VertexAttributeFormat3D.Float4:
                WriteSingle(destination, value.X);
                WriteSingle(destination[4..], value.Y);
                WriteSingle(destination[8..], value.Z);
                WriteSingle(destination[12..], value.W);
                break;
            case VertexAttributeFormat3D.SNorm16x4:
                WriteSNorm16x4(destination, value);
                break;
            case VertexAttributeFormat3D.UInt16x4:
                BinaryPrimitives.WriteUInt16LittleEndian(destination, checked((ushort)MathF.Round(value.X)));
                BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], checked((ushort)MathF.Round(value.Y)));
                BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], checked((ushort)MathF.Round(value.Z)));
                BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], checked((ushort)MathF.Round(value.W)));
                break;
            case VertexAttributeFormat3D.UNorm16x4:
                BinaryPrimitives.WriteUInt16LittleEndian(destination, ToUNorm16(value.X));
                BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], ToUNorm16(value.Y));
                BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], ToUNorm16(value.Z));
                BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], ToUNorm16(value.W));
                break;
            default:
                throw Unsupported(format);
        }
    }

    private static void WriteColor(Span<byte> destination, ColorRgba value, VertexAttributeFormat3D format)
    {
        switch (format)
        {
            case VertexAttributeFormat3D.Float4:
                WriteVector4(destination, new Vector4(value.R, value.G, value.B, value.A), format);
                break;
            case VertexAttributeFormat3D.UNorm8x4:
                destination[0] = ToUNorm8(value.R);
                destination[1] = ToUNorm8(value.G);
                destination[2] = ToUNorm8(value.B);
                destination[3] = ToUNorm8(value.A);
                break;
            default:
                throw Unsupported(format);
        }
    }

    private static void WriteSNorm16x4(Span<byte> destination, Vector4 value)
    {
        BinaryPrimitives.WriteInt16LittleEndian(destination, ToSNorm16(value.X));
        BinaryPrimitives.WriteInt16LittleEndian(destination[2..], ToSNorm16(value.Y));
        BinaryPrimitives.WriteInt16LittleEndian(destination[4..], ToSNorm16(value.Z));
        BinaryPrimitives.WriteInt16LittleEndian(destination[6..], ToSNorm16(value.W));
    }

    private static void WriteSingle(Span<byte> destination, float value)
        => BinaryPrimitives.WriteInt32LittleEndian(destination, BitConverter.SingleToInt32Bits(value));

    private static short ToSNorm16(float value)
        => (short)global::System.Math.Clamp((int)MathF.Round(global::System.Math.Clamp(value, -1f, 1f) * short.MaxValue), short.MinValue + 1, short.MaxValue);

    private static byte ToUNorm8(float value)
        => (byte)global::System.Math.Clamp((int)MathF.Round(global::System.Math.Clamp(value, 0f, 1f) * byte.MaxValue), byte.MinValue, byte.MaxValue);

    private static ushort ToUNorm16(float value)
        => (ushort)global::System.Math.Clamp((int)MathF.Round(global::System.Math.Clamp(value, 0f, 1f) * ushort.MaxValue), ushort.MinValue, ushort.MaxValue);

    private static InvalidOperationException Unsupported(VertexAttributeFormat3D format)
        => new($"Vertex format '{format}' is not valid for the selected attribute value.");
}
