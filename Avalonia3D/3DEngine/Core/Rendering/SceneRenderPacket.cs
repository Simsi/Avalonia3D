using System.Collections.Generic;

namespace ThreeDEngine.Core.Rendering;

public sealed class SceneRenderPacket
{
    public required float Width { get; init; }
    public required float Height { get; init; }
    public required float[] ClearColor { get; init; }
    public float[] CameraPosition { get; init; } = System.Array.Empty<float>();
    public float[] AmbientLight { get; init; } = System.Array.Empty<float>();
    public float[] DirectionalLightDirection { get; init; } = System.Array.Empty<float>();
    public float[] DirectionalLightColor { get; init; } = System.Array.Empty<float>();
    public float[] PointLightPosition { get; init; } = System.Array.Empty<float>();
    public float[] PointLightColor { get; init; } = System.Array.Empty<float>();
    public float[] SpotLightPosition { get; init; } = System.Array.Empty<float>();
    public float[] SpotLightDirection { get; init; } = System.Array.Empty<float>();
    public float[] SpotLightColor { get; init; } = System.Array.Empty<float>();
    public float[] SpotLightCone { get; init; } = System.Array.Empty<float>();
    public bool SkyboxEnabled { get; init; }
    public int SkyboxMode { get; init; }
    public float[] SkyboxTopColor { get; init; } = System.Array.Empty<float>();
    public float[] SkyboxHorizonColor { get; init; } = System.Array.Empty<float>();
    public float[] SkyboxBottomColor { get; init; } = System.Array.Empty<float>();
    public float SkyboxIntensity { get; init; }
    public bool DirectionalShadowEnabled { get; init; }
    public int DirectionalShadowResolution { get; init; }
    public float DirectionalShadowStrength { get; init; }
    public float DirectionalShadowBias { get; init; }
    public float DirectionalShadowNormalBias { get; init; }
    public string DirectionalShadowReason { get; init; } = string.Empty;
    public float[] DirectionalShadowLightViewProjection { get; init; } = System.Array.Empty<float>();
    public int RenderPipelineMode { get; init; }
    public bool DeferredRequested { get; init; }
    public bool SsaoEnabled { get; init; }
    public float[] SsaoParams { get; init; } = System.Array.Empty<float>();
    public bool HdrEnabled { get; init; }
    public int ToneMappingMode { get; init; }
    public float[] ToneMappingParams { get; init; } = System.Array.Empty<float>();
    public bool MotionVectorMetadataEnabled { get; init; }
    public required List<RenderObjectPacket> Objects { get; init; }
}
