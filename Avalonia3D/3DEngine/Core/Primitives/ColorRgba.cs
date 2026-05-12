using System;
using System.Numerics;

namespace ThreeDEngine.Core.Primitives;

public readonly record struct ColorRgba(float R, float G, float B, float A)
{
    public static ColorRgba White => new(1f, 1f, 1f, 1f);
    public static ColorRgba Black => new(0f, 0f, 0f, 1f);
    public static ColorRgba Transparent => new(0f, 0f, 0f, 0f);

    public static ColorRgba FromRgb(float r, float g, float b) => new(r, g, b, 1f);

    public ColorRgba BlendTowards(ColorRgba other, float amount)
    {
        amount = System.Math.Clamp(amount, 0f, 1f);
        return new ColorRgba(
            R + (other.R - R) * amount,
            G + (other.G - G) * amount,
            B + (other.B - B) * amount,
            A + (other.A - A) * amount);
    }

    public Vector3 ToVector3() => new(R, G, B);

    public float[] ToArray() => new float[] { R, G, B, A };
}
