using System.Numerics;
using ThreeDEngine.Core.Primitives;

namespace ThreeDEngine.Core.Particles;

public struct Particle3D
{
    public Vector3 Position;
    public Vector3 Velocity;
    public float Age;
    public float Lifetime;
    public float StartSize;
    public float EndSize;
    public ColorRgba StartColor;
    public ColorRgba EndColor;
    public bool Alive;

    public float NormalizedAge => Lifetime <= 0.0001f ? 1f : System.Math.Clamp(Age / Lifetime, 0f, 1f);
}
