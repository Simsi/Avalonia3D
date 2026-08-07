using System;
using System.Collections.Generic;

namespace ThreeDEngine.Core.Rendering.Rhi;

/// <summary>The concrete graphics API backing an RHI device.</summary>
public enum RhiBackendApi3D
{
    Validation = 0,
    OpenGl = 1,
    WebGl2 = 2,
    WebGpu = 3,
    Vulkan = 4,
    Direct3D12 = 5,
    Metal = 6
}

/// <summary>
/// Capability profiles are explicit product contracts. A backend either satisfies the selected
/// profile or device creation fails; profiles never authorize CPU or reduced-quality fallback.
/// </summary>
public enum RhiCapabilityProfile3D
{
    LegacyRaster = 0,
    ModernRaster = 1,
    GpuDriven = 2,
    WebGpuBaseline = 3
}

[Flags]
public enum RhiFeature3D : ulong
{
    None = 0,
    VertexArrayObjects = 1UL << 0,
    BufferSubData = 1UL << 1,
    InstancedDrawing = 1UL << 2,
    UInt32Indices = 1UL << 3,
    RenderTargets = 1UL << 4,
    DepthTextures = 1UL << 5,
    Texture2D = 1UL << 6,
    VertexTextureFetch = 1UL << 7,
    FloatTextures = 1UL << 8,
    TimerQueries = 1UL << 9,
    TextureArrays = 1UL << 10,
    ComputeShaders = 1UL << 11,
    StorageBuffers = 1UL << 12,
    MultiDrawIndirect = 1UL << 13,
    BindlessTextures = 1UL << 14,
    StorageTextures = 1UL << 15,
    CommandBuffers = 1UL << 16,
    BindGroups = 1UL << 17,
    PipelineLayouts = 1UL << 18,
    ExplicitBarriers = 1UL << 19,
    CopyCommands = 1UL << 20,
    IndirectBuffers = 1UL << 21,
    TimestampQueries = 1UL << 22,
    SamplerObjects = 1UL << 23,
    ShaderReflection = 1UL << 24
}

/// <summary>Immutable limits queried from the active GPU context, never guessed from the backend name.</summary>
public sealed class RhiDeviceLimits3D
{
    public RhiDeviceLimits3D(
        int maxTextureSize,
        int maxCombinedTextureUnits,
        int maxVertexTextureUnits,
        int maxVertexAttributes,
        int maxRenderbufferSize,
        int maxSamples,
        int maxUniformBlockSize = 0,
        int maxStorageBufferBindings = 0,
        int maxBindGroups = 0,
        int maxBindingsPerGroup = 0,
        int maxComputeWorkgroupSizeX = 0,
        int maxComputeWorkgroupSizeY = 0,
        int maxComputeWorkgroupSizeZ = 0,
        int maxComputeInvocationsPerWorkgroup = 0,
        long maxBufferSize = 0)
    {
        MaxTextureSize = RequireNonNegative(maxTextureSize, nameof(maxTextureSize));
        MaxCombinedTextureUnits = RequireNonNegative(maxCombinedTextureUnits, nameof(maxCombinedTextureUnits));
        MaxVertexTextureUnits = RequireNonNegative(maxVertexTextureUnits, nameof(maxVertexTextureUnits));
        MaxVertexAttributes = RequireNonNegative(maxVertexAttributes, nameof(maxVertexAttributes));
        MaxRenderbufferSize = RequireNonNegative(maxRenderbufferSize, nameof(maxRenderbufferSize));
        MaxSamples = RequireNonNegative(maxSamples, nameof(maxSamples));
        MaxUniformBlockSize = RequireNonNegative(maxUniformBlockSize, nameof(maxUniformBlockSize));
        MaxStorageBufferBindings = RequireNonNegative(maxStorageBufferBindings, nameof(maxStorageBufferBindings));
        MaxBindGroups = RequireNonNegative(maxBindGroups, nameof(maxBindGroups));
        MaxBindingsPerGroup = RequireNonNegative(maxBindingsPerGroup, nameof(maxBindingsPerGroup));
        MaxComputeWorkgroupSizeX = RequireNonNegative(maxComputeWorkgroupSizeX, nameof(maxComputeWorkgroupSizeX));
        MaxComputeWorkgroupSizeY = RequireNonNegative(maxComputeWorkgroupSizeY, nameof(maxComputeWorkgroupSizeY));
        MaxComputeWorkgroupSizeZ = RequireNonNegative(maxComputeWorkgroupSizeZ, nameof(maxComputeWorkgroupSizeZ));
        MaxComputeInvocationsPerWorkgroup = RequireNonNegative(maxComputeInvocationsPerWorkgroup, nameof(maxComputeInvocationsPerWorkgroup));
        MaxBufferSize = maxBufferSize >= 0 ? maxBufferSize : throw new ArgumentOutOfRangeException(nameof(maxBufferSize));
    }

    public int MaxTextureSize { get; }
    public int MaxCombinedTextureUnits { get; }
    public int MaxVertexTextureUnits { get; }
    public int MaxVertexAttributes { get; }
    public int MaxRenderbufferSize { get; }
    public int MaxSamples { get; }
    public int MaxUniformBlockSize { get; }
    public int MaxStorageBufferBindings { get; }
    public int MaxBindGroups { get; }
    public int MaxBindingsPerGroup { get; }
    public int MaxComputeWorkgroupSizeX { get; }
    public int MaxComputeWorkgroupSizeY { get; }
    public int MaxComputeWorkgroupSizeZ { get; }
    public int MaxComputeInvocationsPerWorkgroup { get; }
    public long MaxBufferSize { get; }

    private static int RequireNonNegative(int value, string name)
        => value >= 0 ? value : throw new ArgumentOutOfRangeException(name);
}

/// <summary>Actual feature set and limits for one live backend context.</summary>
public sealed class RhiDeviceCapabilities3D
{
    public static readonly RhiFeature3D RequiredRasterFeatures =
        RhiFeature3D.VertexArrayObjects |
        RhiFeature3D.BufferSubData |
        RhiFeature3D.InstancedDrawing |
        RhiFeature3D.UInt32Indices |
        RhiFeature3D.RenderTargets |
        RhiFeature3D.DepthTextures |
        RhiFeature3D.Texture2D |
        RhiFeature3D.CommandBuffers;

    public static readonly RhiFeature3D RequiredGpuDrivenFeatures =
        RequiredRasterFeatures |
        RhiFeature3D.ComputeShaders |
        RhiFeature3D.StorageBuffers |
        RhiFeature3D.StorageTextures |
        RhiFeature3D.IndirectBuffers |
        RhiFeature3D.MultiDrawIndirect |
        RhiFeature3D.ExplicitBarriers |
        RhiFeature3D.PipelineLayouts |
        RhiFeature3D.BindGroups |
        RhiFeature3D.CopyCommands;

    public RhiDeviceCapabilities3D(
        RhiBackendApi3D api,
        string adapterName,
        string apiVersion,
        RhiFeature3D features,
        RhiDeviceLimits3D limits)
    {
        Api = api;
        AdapterName = string.IsNullOrWhiteSpace(adapterName) ? "Unknown GPU" : adapterName;
        ApiVersion = string.IsNullOrWhiteSpace(apiVersion) ? "unknown" : apiVersion;
        Features = features;
        Limits = limits ?? throw new ArgumentNullException(nameof(limits));
        ApiName = api.ToString();
        FeatureSummary = features.ToString();
        LimitsSummary = $"tex:{Limits.MaxTextureSize}, units:{Limits.MaxCombinedTextureUnits}, " +
            $"vtxTex:{Limits.MaxVertexTextureUnits}, attribs:{Limits.MaxVertexAttributes}, samples:{Limits.MaxSamples}, " +
            $"groups:{Limits.MaxBindGroups}, bindings:{Limits.MaxBindingsPerGroup}, maxBuffer:{Limits.MaxBufferSize}";
    }

    public RhiBackendApi3D Api { get; }
    public string AdapterName { get; }
    public string ApiVersion { get; }
    public RhiFeature3D Features { get; }
    public RhiDeviceLimits3D Limits { get; }
    public string ApiName { get; }
    public string FeatureSummary { get; }
    public string LimitsSummary { get; }

    public bool Supports(RhiFeature3D features) => (Features & features) == features;

    public void Require(RhiFeature3D features, string operation)
    {
        var missing = features & ~Features;
        if (missing != RhiFeature3D.None)
        {
            throw new RhiCapabilityException3D(Api, operation, missing, this);
        }
    }

    public void RequireProfile(RhiCapabilityProfile3D profile, string operation)
    {
        Require(GetRequiredFeatures(profile), operation);
        var limits = Limits;
        if (profile is RhiCapabilityProfile3D.ModernRaster or RhiCapabilityProfile3D.GpuDriven or RhiCapabilityProfile3D.WebGpuBaseline)
        {
            if (limits.MaxBindGroups < 4 || limits.MaxBindingsPerGroup < 9 || limits.MaxBufferSize < 64 * 1024)
            {
                throw new RhiDeviceLimitException3D(
                    Api,
                    operation,
                    "MaxBindGroups >= 4, MaxBindingsPerGroup >= 9 and MaxBufferSize >= 65536",
                    this);
            }
        }

        if (profile is RhiCapabilityProfile3D.GpuDriven or RhiCapabilityProfile3D.WebGpuBaseline)
        {
            if (limits.MaxStorageBufferBindings < 8 ||
                limits.MaxComputeWorkgroupSizeX < 64 ||
                limits.MaxComputeInvocationsPerWorkgroup < 64)
            {
                throw new RhiDeviceLimitException3D(
                    Api,
                    operation,
                    "MaxStorageBufferBindings >= 8, MaxComputeWorkgroupSizeX >= 64 and MaxComputeInvocationsPerWorkgroup >= 64",
                    this);
            }
        }
    }

    public string ToDiagnosticString()
        => $"{ApiName} {ApiVersion}; GPU={AdapterName}; Features={FeatureSummary}; Limits={LimitsSummary}";

    private static RhiFeature3D GetRequiredFeatures(RhiCapabilityProfile3D profile)
        => profile switch
        {
            RhiCapabilityProfile3D.LegacyRaster => RequiredRasterFeatures,
            RhiCapabilityProfile3D.ModernRaster => RequiredRasterFeatures |
                RhiFeature3D.PipelineLayouts |
                RhiFeature3D.BindGroups |
                RhiFeature3D.CopyCommands |
                RhiFeature3D.SamplerObjects |
                RhiFeature3D.ShaderReflection,
            RhiCapabilityProfile3D.GpuDriven => RequiredGpuDrivenFeatures |
                RhiFeature3D.SamplerObjects |
                RhiFeature3D.ShaderReflection,
            RhiCapabilityProfile3D.WebGpuBaseline => RequiredGpuDrivenFeatures |
                RhiFeature3D.SamplerObjects |
                RhiFeature3D.ShaderReflection |
                RhiFeature3D.TextureArrays,
            _ => throw new ArgumentOutOfRangeException(nameof(profile))
        };
}

public sealed class RhiCapabilityException3D : InvalidOperationException
{
    public RhiCapabilityException3D(
        RhiBackendApi3D api,
        string operation,
        RhiFeature3D missingFeatures,
        RhiDeviceCapabilities3D capabilities)
        : base($"RHI {api} cannot execute '{operation}': required GPU features are unavailable: {Format(missingFeatures)}. " +
               $"No CPU or legacy rendering fallback is permitted. Device: {capabilities.ToDiagnosticString()}")
    {
        Api = api;
        Operation = operation ?? string.Empty;
        MissingFeatures = missingFeatures;
    }

    public RhiBackendApi3D Api { get; }
    public string Operation { get; }
    public RhiFeature3D MissingFeatures { get; }

    private static string Format(RhiFeature3D features)
    {
        var names = new List<string>();
        foreach (RhiFeature3D feature in Enum.GetValues(typeof(RhiFeature3D)))
        {
            if (feature != RhiFeature3D.None && (features & feature) == feature) names.Add(feature.ToString());
        }

        return names.Count == 0 ? features.ToString() : string.Join(", ", names);
    }
}

public sealed class RhiDeviceLimitException3D : InvalidOperationException
{
    public RhiDeviceLimitException3D(RhiBackendApi3D api, string operation, string requirement, RhiDeviceCapabilities3D capabilities)
        : base($"RHI {api} cannot execute '{operation}': required GPU limit is unavailable ({requirement}). " +
               $"No reduced-quality or legacy rendering fallback is permitted. Device: {capabilities.ToDiagnosticString()}")
    {
        Api = api;
        Operation = operation ?? string.Empty;
        Requirement = requirement ?? string.Empty;
    }

    public RhiBackendApi3D Api { get; }
    public string Operation { get; }
    public string Requirement { get; }
}
