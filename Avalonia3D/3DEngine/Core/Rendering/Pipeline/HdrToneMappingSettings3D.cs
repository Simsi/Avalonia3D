using System;
using System.Collections.Generic;

namespace ThreeDEngine.Core.Rendering.Pipeline;

public sealed class HdrToneMappingSettings3D
{
    private bool _enabled;
    private ToneMappingMode3D _mode = ToneMappingMode3D.Reinhard;
    private float _exposure = 1f;
    private float _gamma = 2.2f;

    public event EventHandler? Changed;

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public ToneMappingMode3D Mode { get => _mode; set => Set(ref _mode, value); }
    public float Exposure { get => _exposure; set => Set(ref _exposure, MathF.Max(0.001f, value)); }
    public float Gamma { get => _gamma; set => Set(ref _gamma, MathF.Max(0.1f, value)); }

    private void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
