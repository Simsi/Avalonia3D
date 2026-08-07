using System;
using System.Numerics;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.Validation;

internal static class Guard3D
{
    public static float Finite(float value, string name)
        => float.IsFinite(value) ? value : throw new ArgumentOutOfRangeException(name, value, "Value must be finite.");

    public static double Finite(double value, string name)
        => double.IsFinite(value) ? value : throw new ArgumentOutOfRangeException(name, value, "Value must be finite.");

    public static float Range(float value, float min, float max, string name)
    {
        Finite(value, name);
        if (value < min || value > max)
            throw new ArgumentOutOfRangeException(name, value, $"Value must be in the inclusive range [{min}, {max}].");
        return value;
    }

    public static double Range(double value, double min, double max, string name)
    {
        Finite(value, name);
        if (value < min || value > max)
            throw new ArgumentOutOfRangeException(name, value, $"Value must be in the inclusive range [{min}, {max}].");
        return value;
    }

    public static float Positive(float value, string name)
    {
        Finite(value, name);
        if (value <= 0f) throw new ArgumentOutOfRangeException(name, value, "Value must be greater than zero.");
        return value;
    }

    public static float NonNegative(float value, string name)
    {
        Finite(value, name);
        if (value < 0f) throw new ArgumentOutOfRangeException(name, value, "Value must be non-negative.");
        return value;
    }

    public static int Range(int value, int min, int max, string name)
    {
        if (value < min || value > max)
            throw new ArgumentOutOfRangeException(name, value, $"Value must be in the inclusive range [{min}, {max}].");
        return value;
    }

    public static long Range(long value, long min, long max, string name)
    {
        if (value < min || value > max)
            throw new ArgumentOutOfRangeException(name, value, $"Value must be in the inclusive range [{min}, {max}].");
        return value;
    }

    public static int Positive(int value, string name)
        => value > 0 ? value : throw new ArgumentOutOfRangeException(name, value, "Value must be greater than zero.");

    public static int NonNegative(int value, string name)
        => value >= 0 ? value : throw new ArgumentOutOfRangeException(name, value, "Value must be non-negative.");

    public static TEnum Defined<TEnum>(TEnum value, string name) where TEnum : struct, Enum
        => Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(name, value, "Unknown enum value.");

    public static string RequiredText(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value cannot be null, empty, or whitespace.", name);
        return value;
    }

    public static byte[] RequiredBytes(byte[]? value, string name)
    {
        if (value is not { Length: > 0 }) throw new ArgumentException("A non-empty byte array is required.", name);
        return value;
    }

    public static Vector2 Finite(Vector2 value, string name)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
            throw new ArgumentOutOfRangeException(name, value, "Vector components must be finite.");
        return value;
    }

    public static Vector3 Finite(Vector3 value, string name)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new ArgumentOutOfRangeException(name, value, "Vector components must be finite.");
        return value;
    }

    public static Vector4 Finite(Vector4 value, string name)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) || !float.IsFinite(value.W))
            throw new ArgumentOutOfRangeException(name, value, "Vector components must be finite.");
        return value;
    }

    public static Quaternion NormalizedQuaternion(Quaternion value, string name)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) || !float.IsFinite(value.W))
            throw new ArgumentOutOfRangeException(name, value, "Quaternion components must be finite.");
        var lengthSquared = value.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared <= 0.000001f)
            throw new ArgumentOutOfRangeException(name, value, "Quaternion must be non-degenerate.");
        return Quaternion.Normalize(value);
    }

    public static ColorRgba Color(ColorRgba value, string name)
    {
        if (!float.IsFinite(value.R) || !float.IsFinite(value.G) || !float.IsFinite(value.B) || !float.IsFinite(value.A))
            throw new ArgumentOutOfRangeException(name, value, "Color components must be finite.");
        return value;
    }

    public static Matrix4x4 FiniteMatrix(Matrix4x4 value, string name, bool requireInvertible = false)
    {
        if (!IsFinite(value)) throw new ArgumentOutOfRangeException(name, value, "Matrix components must be finite.");
        if (requireInvertible)
        {
            var determinant = value.GetDeterminant();
            if (!float.IsFinite(determinant) || global::System.MathF.Abs(determinant) <= 0.0000001f)
                throw new ArgumentOutOfRangeException(name, value, "Matrix must be invertible.");
        }
        return value;
    }

    public static bool IsFinite(Matrix4x4 value)
        => float.IsFinite(value.M11) && float.IsFinite(value.M12) && float.IsFinite(value.M13) && float.IsFinite(value.M14)
        && float.IsFinite(value.M21) && float.IsFinite(value.M22) && float.IsFinite(value.M23) && float.IsFinite(value.M24)
        && float.IsFinite(value.M31) && float.IsFinite(value.M32) && float.IsFinite(value.M33) && float.IsFinite(value.M34)
        && float.IsFinite(value.M41) && float.IsFinite(value.M42) && float.IsFinite(value.M43) && float.IsFinite(value.M44);
}
