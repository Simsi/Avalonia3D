using System;
using System.Collections.Generic;
using ThreeDEngine.Core.Validation;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Rendering.Pipeline;

public sealed class HdrToneMappingSettings3D
{
    private bool _enabled;
    private ToneMappingMode3D _mode = ToneMappingMode3D.Reinhard;
    private float _exposure = 1f;
    private float _gamma = 2.2f;

    public event EventHandler? Changed;
    internal Func<SceneAccessLease3D>? MutationScopeRequested { get; set; }

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public ToneMappingMode3D Mode { get => _mode; set => Set(ref _mode, Guard3D.Defined(value, nameof(value))); }
    public float Exposure { get => _exposure; set => Set(ref _exposure, Guard3D.Positive(value, nameof(value))); }
    public float Gamma { get => _gamma; set => Set(ref _gamma, Guard3D.Positive(value, nameof(value))); }

    private void Set<T>(ref T field, T value)
    {
        using var mutation = MutationScopeRequested?.Invoke() ?? default;
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
