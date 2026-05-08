using System;
using System.Numerics;

namespace ThreeDEngine.Core.Particles;

public sealed class ParticleEmitter3D
{
    private readonly Random _random;

    public ParticleEmitter3D(int seed = 154235467)
    {
        _random = new Random(seed);
    }

    public Vector3 Direction { get; set; } = Vector3.UnitY;
    public Vector3 Gravity { get; set; } = new(0f, -0.35f, 0f);

    public Particle3D Create(ParticleSystemSettings3D settings)
    {
        var direction = Direction.LengthSquared() > 0.000001f ? Vector3.Normalize(Direction) : Vector3.UnitY;
        var spread = settings.Spread;
        var random = new Vector3(NextSigned(), NextSigned(), NextSigned()) * spread;
        var velocity = Vector3.Normalize(direction + random) * settings.InitialSpeed;
        return new Particle3D
        {
            Position = Vector3.Zero,
            Velocity = velocity,
            Lifetime = MathF.Max(0.001f, settings.ParticleLifetimeSeconds),
            StartSize = MathF.Max(0.0001f, settings.StartSize),
            EndSize = MathF.Max(0.0001f, settings.EndSize),
            StartColor = settings.StartColor,
            EndColor = settings.EndColor,
            Alive = true
        };
    }

    public void Integrate(ref Particle3D particle, float deltaSeconds)
    {
        particle.Velocity += Gravity * deltaSeconds;
        particle.Position += particle.Velocity * deltaSeconds;
        particle.Age += deltaSeconds;
        if (particle.Age >= particle.Lifetime)
        {
            particle.Alive = false;
        }
    }

    private float NextSigned() => (float)(_random.NextDouble() * 2.0 - 1.0);
}
