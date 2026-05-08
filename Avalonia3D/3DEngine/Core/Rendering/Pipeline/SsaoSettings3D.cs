using System;

namespace ThreeDEngine.Core.Rendering.Pipeline;

public sealed class SsaoSettings3D
{
    private bool _enabled;
    private float _radius = 0.75f;
    private float _strength = 0.8f;
    private float _bias = 0.025f;
    private int _sampleCount = 16;
    private float _resolutionScale = 0.5f;

    public event EventHandler? Changed;

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public float Radius { get => _radius; set => Set(ref _radius, MathF.Max(0.001f, value)); }
    public float Strength { get => _strength; set => Set(ref _strength, MathF.Max(0f, value)); }
    public float Bias { get => _bias; set => Set(ref _bias, MathF.Max(0f, value)); }
    public int SampleCount { get => _sampleCount; set => Set(ref _sampleCount, global::System.Math.Clamp(value, 4, 64)); }
    public float ResolutionScale { get => _resolutionScale; set => Set(ref _resolutionScale, global::System.Math.Clamp(value, 0.25f, 1f)); }

    private void Set<T>(ref T field, T value) where T : IEquatable<T>
    {
        if (field.Equals(value)) return;
        field = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
