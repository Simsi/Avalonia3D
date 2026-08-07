using System;
using System.Numerics;
using ThreeDEngine.Core.Validation;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Particles;

public sealed class ParticleEmitter3D
{
    private readonly Random _random;
    private Vector3 _direction = Vector3.UnitY;
    private Vector3 _gravity = new(0f, -0.35f, 0f);
    internal Func<SceneAccessLease3D>? MutationScopeRequested { get; set; }

    public ParticleEmitter3D(int seed = 154235467)
    {
        _random = new Random(seed);
    }

    public Vector3 Direction
    {
        get => _direction;
        set
        {
            using var mutation = EnterMutationScope();
            value = Guard3D.Finite(value, nameof(value));
            if (value.LengthSquared() <= 0.000001f) throw new ArgumentOutOfRangeException(nameof(value), value, "Emitter direction must be non-zero.");
            _direction = Vector3.Normalize(value);
        }
    }

    public Vector3 Gravity
    {
        get => _gravity;
        set
        {
            using var mutation = EnterMutationScope();
            _gravity = Guard3D.Finite(value, nameof(value));
        }
    }

    public Particle3D Create(ParticleSystemSettings3D settings)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        var random = new Vector3(NextSigned(), NextSigned(), NextSigned()) * settings.Spread;
        var candidate = _direction + random;
        if (candidate.LengthSquared() <= 0.000001f) candidate = _direction;
        var velocity = Vector3.Normalize(candidate) * settings.InitialSpeed;
        return new Particle3D
        {
            Position = Vector3.Zero,
            Velocity = velocity,
            Lifetime = settings.ParticleLifetimeSeconds,
            StartSize = settings.StartSize,
            EndSize = settings.EndSize,
            StartColor = settings.StartColor,
            EndColor = settings.EndColor,
            Alive = true
        };
    }

    public void Integrate(ref Particle3D particle, float deltaSeconds)
    {
        Guard3D.NonNegative(deltaSeconds, nameof(deltaSeconds));
        particle.Velocity += Gravity * deltaSeconds;
        particle.Position += particle.Velocity * deltaSeconds;
        particle.Age += deltaSeconds;
        if (particle.Age >= particle.Lifetime) particle.Alive = false;
    }

    private SceneAccessLease3D EnterMutationScope()
        => MutationScopeRequested?.Invoke() ?? default;

    private float NextSigned() => (float)(_random.NextDouble() * 2.0 - 1.0);
}
