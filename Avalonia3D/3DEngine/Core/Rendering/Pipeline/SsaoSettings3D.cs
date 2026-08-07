using System;
using ThreeDEngine.Core.Validation;
using ThreeDEngine.Core.Scene;

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
    internal Func<SceneAccessLease3D>? MutationScopeRequested { get; set; }

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public float Radius { get => _radius; set => Set(ref _radius, Guard3D.Positive(value, nameof(value))); }
    public float Strength { get => _strength; set => Set(ref _strength, Guard3D.NonNegative(value, nameof(value))); }
    public float Bias { get => _bias; set => Set(ref _bias, Guard3D.NonNegative(value, nameof(value))); }
    public int SampleCount { get => _sampleCount; set => Set(ref _sampleCount, value is >= 4 and <= 64 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "SSAO sample count must be between 4 and 64.")); }
    public float ResolutionScale { get => _resolutionScale; set => Set(ref _resolutionScale, Guard3D.Range(value, 0.25f, 1f, nameof(value))); }

    private void Set<T>(ref T field, T value) where T : IEquatable<T>
    {
        using var mutation = MutationScopeRequested?.Invoke() ?? default;
        if (field.Equals(value)) return;
        field = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
