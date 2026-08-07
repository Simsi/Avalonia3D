using System;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Validation;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Particles;

[Flags]
public enum ParticleSettingsChangeKind3D
{
    None = 0,
    Simulation = 1 << 0,
    Capacity = 1 << 1,
    Geometry = 1 << 2,
    Appearance = 1 << 3,
    Playback = 1 << 4
}

public sealed class ParticleSettingsChangedEventArgs3D : EventArgs
{
    public ParticleSettingsChangedEventArgs3D(ParticleSettingsChangeKind3D kind, string propertyName)
    {
        Kind = kind;
        PropertyName = propertyName;
    }

    public ParticleSettingsChangeKind3D Kind { get; }
    public string PropertyName { get; }
}

public sealed class ParticleSystemSettings3D
{
    private int _capacity = 1024;
    private float _emissionRatePerSecond = 64f;
    private float _particleLifetimeSeconds = 2.5f;
    private float _startSize = 0.08f;
    private float _endSize = 0.02f;
    private ColorRgba _startColor = ColorRgba.White;
    private ColorRgba _endColor = new(1f, 1f, 1f, 0f);
    private float _initialSpeed = 1.5f;
    private float _spread = 0.35f;
    private ParticleSimulationSpace3D _simulationSpace = ParticleSimulationSpace3D.Local;
    private bool _looping = true;
    private bool _prewarm;
    private ParticleRenderMode3D _renderMode = ParticleRenderMode3D.CameraFacingQuad;

    public event EventHandler<ParticleSettingsChangedEventArgs3D>? Changed;
    internal Func<SceneAccessLease3D>? MutationScopeRequested { get; set; }

    public int Capacity
    {
        get => _capacity;
        set { using var mutation = EnterMutationScope(); value = Guard3D.Positive(value, nameof(value)); if (_capacity == value) return; _capacity = value; RaiseChanged(ParticleSettingsChangeKind3D.Capacity, nameof(Capacity)); }
    }

    public float EmissionRatePerSecond
    {
        get => _emissionRatePerSecond;
        set { using var mutation = EnterMutationScope(); value = Guard3D.NonNegative(value, nameof(value)); if (NearlyEqual(_emissionRatePerSecond, value)) return; _emissionRatePerSecond = value; RaiseChanged(ParticleSettingsChangeKind3D.Simulation, nameof(EmissionRatePerSecond)); }
    }

    public float ParticleLifetimeSeconds
    {
        get => _particleLifetimeSeconds;
        set { using var mutation = EnterMutationScope(); value = Guard3D.Positive(value, nameof(value)); if (NearlyEqual(_particleLifetimeSeconds, value)) return; _particleLifetimeSeconds = value; RaiseChanged(ParticleSettingsChangeKind3D.Simulation, nameof(ParticleLifetimeSeconds)); }
    }

    public float StartSize
    {
        get => _startSize;
        set { using var mutation = EnterMutationScope(); value = Guard3D.NonNegative(value, nameof(value)); if (NearlyEqual(_startSize, value)) return; _startSize = value; RaiseChanged(ParticleSettingsChangeKind3D.Appearance, nameof(StartSize)); }
    }

    public float EndSize
    {
        get => _endSize;
        set { using var mutation = EnterMutationScope(); value = Guard3D.NonNegative(value, nameof(value)); if (NearlyEqual(_endSize, value)) return; _endSize = value; RaiseChanged(ParticleSettingsChangeKind3D.Appearance, nameof(EndSize)); }
    }

    public ColorRgba StartColor
    {
        get => _startColor;
        set { using var mutation = EnterMutationScope(); value = Guard3D.Color(value, nameof(value)); if (_startColor.Equals(value)) return; _startColor = value; RaiseChanged(ParticleSettingsChangeKind3D.Appearance, nameof(StartColor)); }
    }

    public ColorRgba EndColor
    {
        get => _endColor;
        set { using var mutation = EnterMutationScope(); value = Guard3D.Color(value, nameof(value)); if (_endColor.Equals(value)) return; _endColor = value; RaiseChanged(ParticleSettingsChangeKind3D.Appearance, nameof(EndColor)); }
    }

    public float InitialSpeed
    {
        get => _initialSpeed;
        set { using var mutation = EnterMutationScope(); value = Guard3D.Finite(value, nameof(value)); if (NearlyEqual(_initialSpeed, value)) return; _initialSpeed = value; RaiseChanged(ParticleSettingsChangeKind3D.Simulation, nameof(InitialSpeed)); }
    }

    public float Spread
    {
        get => _spread;
        set { using var mutation = EnterMutationScope(); value = Guard3D.NonNegative(value, nameof(value)); if (NearlyEqual(_spread, value)) return; _spread = value; RaiseChanged(ParticleSettingsChangeKind3D.Simulation, nameof(Spread)); }
    }

    public ParticleSimulationSpace3D SimulationSpace
    {
        get => _simulationSpace;
        set { using var mutation = EnterMutationScope(); value = Guard3D.Defined(value, nameof(value)); if (_simulationSpace == value) return; _simulationSpace = value; RaiseChanged(ParticleSettingsChangeKind3D.Simulation | ParticleSettingsChangeKind3D.Appearance, nameof(SimulationSpace)); }
    }

    public bool Looping
    {
        get => _looping;
        set { using var mutation = EnterMutationScope(); if (_looping == value) return; _looping = value; RaiseChanged(ParticleSettingsChangeKind3D.Playback, nameof(Looping)); }
    }

    public bool Prewarm
    {
        get => _prewarm;
        set { using var mutation = EnterMutationScope(); if (_prewarm == value) return; _prewarm = value; RaiseChanged(ParticleSettingsChangeKind3D.Playback, nameof(Prewarm)); }
    }

    public ParticleRenderMode3D RenderMode
    {
        get => _renderMode;
        set { using var mutation = EnterMutationScope(); value = Guard3D.Defined(value, nameof(value)); if (_renderMode == value) return; _renderMode = value; RaiseChanged(ParticleSettingsChangeKind3D.Geometry, nameof(RenderMode)); }
    }

    public ParticleSystemSettings3D Clone()
    {
        return new ParticleSystemSettings3D
        {
            _capacity = _capacity,
            _emissionRatePerSecond = _emissionRatePerSecond,
            _particleLifetimeSeconds = _particleLifetimeSeconds,
            _startSize = _startSize,
            _endSize = _endSize,
            _startColor = _startColor,
            _endColor = _endColor,
            _initialSpeed = _initialSpeed,
            _spread = _spread,
            _simulationSpace = _simulationSpace,
            _looping = _looping,
            _prewarm = _prewarm,
            _renderMode = _renderMode
        };
    }

    private SceneAccessLease3D EnterMutationScope()
        => MutationScopeRequested?.Invoke() ?? default;

    private static bool NearlyEqual(float left, float right) => MathF.Abs(left - right) < 0.0001f;
    private void RaiseChanged(ParticleSettingsChangeKind3D kind, string propertyName)
        => Changed?.Invoke(this, new ParticleSettingsChangedEventArgs3D(kind, propertyName));
}
