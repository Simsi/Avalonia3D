using System;

namespace ThreeDEngine.Core.Rendering.Rhi;

/// <summary>
/// Shared capability contract for the strategic WebGPU/native-explicit backend. No presenter is
/// exposed until an adapter satisfies this contract; unsupported systems fail during device creation.
/// </summary>
internal static class WebGpuRhiContract3D
{
    public static readonly RhiFeature3D RequiredFeatures =
        RhiDeviceCapabilities3D.RequiredGpuDrivenFeatures |
        RhiFeature3D.SamplerObjects |
        RhiFeature3D.ShaderReflection |
        RhiFeature3D.TextureArrays;

    public static void Validate(RhiDeviceCapabilities3D capabilities, string operation = "WebGPU backend initialization")
    {
        if (capabilities is null) throw new ArgumentNullException(nameof(capabilities));
        if (capabilities.Api != RhiBackendApi3D.WebGpu)
            throw new InvalidOperationException($"{operation} requires a WebGPU adapter, but {capabilities.Api} was supplied.");
        capabilities.RequireProfile(RhiCapabilityProfile3D.WebGpuBaseline, operation);
        capabilities.Require(RequiredFeatures, operation);
    }
}
