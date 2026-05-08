using System.Collections.Generic;
using ThreeDEngine.Core.Rendering;

namespace ThreeDEngine.Core.Rendering.Capabilities;

public sealed class RendererCapabilities3D
{
    private readonly HashSet<RenderingFeature3D> _features;

    public RendererCapabilities3D(BackendKind backend, IEnumerable<RenderingFeature3D> features)
    {
        Backend = backend;
        _features = features is null ? new HashSet<RenderingFeature3D>() : new HashSet<RenderingFeature3D>(features);
    }

    public BackendKind Backend { get; }
    public bool Supports(RenderingFeature3D feature) => _features.Contains(feature);
    public IReadOnlyCollection<RenderingFeature3D> Features => _features;

    public static RendererCapabilities3D OpenGlDesktop { get; } = new(
        BackendKind.OpenGlDesktop,
        new[]
        {
            RenderingFeature3D.BaseColorTexture,
            RenderingFeature3D.NormalTexture,
            RenderingFeature3D.MetallicRoughness,
            RenderingFeature3D.Emissive,
            RenderingFeature3D.EquirectangularSkybox,
            RenderingFeature3D.CubemapSkybox,
            RenderingFeature3D.AlphaBlend,
            RenderingFeature3D.AlphaMask,
            RenderingFeature3D.DoubleSided,
            RenderingFeature3D.Shadows,
            RenderingFeature3D.StarFieldBackground,
            RenderingFeature3D.ControlPlane3D
        });

    public static RendererCapabilities3D WebGlBrowser { get; } = new(
        BackendKind.WebGlBrowser,
        new[]
        {
            RenderingFeature3D.BaseColorTexture,
            RenderingFeature3D.NormalTexture,
            RenderingFeature3D.MetallicRoughness,
            RenderingFeature3D.Emissive,
            RenderingFeature3D.EquirectangularSkybox,
            RenderingFeature3D.CubemapSkybox,
            RenderingFeature3D.AlphaBlend,
            RenderingFeature3D.AlphaMask,
            RenderingFeature3D.DoubleSided,
            RenderingFeature3D.StarFieldBackground,
            RenderingFeature3D.ControlPlane3D
        });
}
