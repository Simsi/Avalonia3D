using System;
using System.Collections.Generic;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Validation;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Rendering.Pipeline;

public sealed class RenderPipelineSettings3D
{
    private RenderPipelineMode3D _mode = RenderPipelineMode3D.Forward;
    private bool _enableDeferredLighting;
    private bool _enableHdr;
    private bool _enableTransparentForwardPass = true;
    private bool _enableMotionVectorMetadata;
    private RenderPipelinePlan3D? _cachedOpenGlPlan;
    private RenderPipelinePlan3D? _cachedWebGlPlan;

    public RenderPipelineSettings3D()
    {
        Ssao.Changed += (_, _) => OnChanged();
        ToneMapping.Changed += (_, _) => OnChanged();
    }

    public event EventHandler? Changed;

    internal Func<SceneAccessLease3D>? MutationScopeRequested
    {
        get => _mutationScopeRequested;
        set
        {
            _mutationScopeRequested = value;
            Ssao.MutationScopeRequested = value;
            ToneMapping.MutationScopeRequested = value;
        }
    }
    private Func<SceneAccessLease3D>? _mutationScopeRequested;

    public RenderPipelineMode3D Mode { get => _mode; set => Set(ref _mode, Guard3D.Defined(value, nameof(value))); }
    public bool EnableDeferredLighting { get => _enableDeferredLighting; set => Set(ref _enableDeferredLighting, value); }
    public bool EnableHdr { get => _enableHdr; set => Set(ref _enableHdr, value); }
    public bool EnableTransparentForwardPass { get => _enableTransparentForwardPass; set => Set(ref _enableTransparentForwardPass, value); }
    public bool EnableMotionVectorMetadata { get => _enableMotionVectorMetadata; set => Set(ref _enableMotionVectorMetadata, value); }
    public SsaoSettings3D Ssao { get; } = new();
    public HdrToneMappingSettings3D ToneMapping { get; } = new();

    internal RenderPipelinePlan3D? GetCachedPlan(BackendKind backend) => Guard3D.Defined(backend, nameof(backend)) switch
    {
        BackendKind.OpenGlDesktop => _cachedOpenGlPlan,
        BackendKind.WebGlBrowser => _cachedWebGlPlan,
        _ => null
    };

    internal void CachePlan(BackendKind backend, RenderPipelinePlan3D plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        switch (Guard3D.Defined(backend, nameof(backend)))
        {
            case BackendKind.OpenGlDesktop:
                _cachedOpenGlPlan = plan;
                break;
            case BackendKind.WebGlBrowser:
                _cachedWebGlPlan = plan;
                break;
        }
    }

    private void Set<T>(ref T field, T value)
    {
        using var mutation = _mutationScopeRequested?.Invoke() ?? default;
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnChanged();
    }

    private void OnChanged()
    {
        _cachedOpenGlPlan = null;
        _cachedWebGlPlan = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
