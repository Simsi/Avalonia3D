using System;
using System.Collections.Generic;

namespace ThreeDEngine.Core.Rendering.Pipeline;

public sealed class RenderPipelineSettings3D
{
    private RenderPipelineMode3D _mode = RenderPipelineMode3D.Forward;
    private bool _enableDeferredLighting;
    private bool _enableHdr;
    private bool _enableTransparentForwardPass = true;
    private bool _enableMotionVectorMetadata;

    public RenderPipelineSettings3D()
    {
        Ssao.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        ToneMapping.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Changed;

    public RenderPipelineMode3D Mode { get => _mode; set => Set(ref _mode, value); }
    public bool EnableDeferredLighting { get => _enableDeferredLighting; set => Set(ref _enableDeferredLighting, value); }
    public bool EnableHdr { get => _enableHdr; set => Set(ref _enableHdr, value); }
    public bool EnableTransparentForwardPass { get => _enableTransparentForwardPass; set => Set(ref _enableTransparentForwardPass, value); }
    public bool EnableMotionVectorMetadata { get => _enableMotionVectorMetadata; set => Set(ref _enableMotionVectorMetadata, value); }
    public SsaoSettings3D Ssao { get; } = new();
    public HdrToneMappingSettings3D ToneMapping { get; } = new();

    public static RenderPipelineSettings3D CreateDeferredPreview()
    {
        var settings = new RenderPipelineSettings3D
        {
            Mode = RenderPipelineMode3D.DeferredIfSupported,
            EnableDeferredLighting = true,
            EnableHdr = true,
            EnableMotionVectorMetadata = true
        };
        settings.Ssao.Enabled = true;
        settings.ToneMapping.Enabled = true;
        return settings;
    }

    private void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
