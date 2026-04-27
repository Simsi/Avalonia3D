using System;
using System.Globalization;

namespace ThreeDEngine.Core.Geometry;

public readonly struct MeshResourceKey : IEquatable<MeshResourceKey>
{
    public MeshResourceKey(string value)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;

    public bool Equals(MeshResourceKey other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is MeshResourceKey other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(MeshResourceKey left, MeshResourceKey right) => left.Equals(right);
    public static bool operator !=(MeshResourceKey left, MeshResourceKey right) => !left.Equals(right);

    public static MeshResourceKey Box(float width, float height, float depth)
        => new("box:" + F(width) + ":" + F(height) + ":" + F(depth));

    public static MeshResourceKey Plane(float width, float height, int segmentsX, int segmentsY)
        => new("plane:" + F(width) + ":" + F(height) + ":" + segmentsX + ":" + segmentsY);

    public static MeshResourceKey Ellipse(float width, float height, float depth, int segments)
        => new("ellipse:" + F(width) + ":" + F(height) + ":" + F(depth) + ":" + segments);

    public static MeshResourceKey Cylinder(float radius, float height, int segments)
        => new("cylinder:" + F(radius) + ":" + F(height) + ":" + segments);

    public static MeshResourceKey Cone(float radius, float height, int segments)
        => new("cone:" + F(radius) + ":" + F(height) + ":" + segments);

    public static MeshResourceKey Sphere(float radius, int segments, int rings)
        => new("sphere:" + F(radius) + ":" + segments + ":" + rings);

    private static string F(float value) => MathF.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);
}
