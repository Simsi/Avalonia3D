using System;
using ThreeDEngine.Core.Rendering.Rhi;

namespace ThreeDEngine.Core.Rendering.GpuDriven;

/// <summary>Device-generation-scoped layouts, shaders and pipelines for the GPU-driven frame.</summary>
internal sealed class GpuDrivenPipelineState3D : IDisposable
{
    private readonly string _owner = "gpu-driven-pipelines";
    private RhiDevice3D? _device;
    private uint _generation;
    private bool _hdr;
    private RhiResourceHandle3D _sceneLayout;
    private RhiResourceHandle3D _lightingLayout;
    private RhiResourceHandle3D _particleLayout;
    private RhiResourceHandle3D _postLayout;
    private RhiResourceHandle3D _pipelineLayout;
    private RhiResourceHandle3D _sampler;
    private RhiResourceHandle3D _cullShader;
    private RhiResourceHandle3D _clusterShader;
    private RhiResourceHandle3D _particleComputeShader;
    private RhiResourceHandle3D _forwardVertexShader;
    private RhiResourceHandle3D _forwardFragmentShader;
    private RhiResourceHandle3D _particleVertexShader;
    private RhiResourceHandle3D _particleFragmentShader;
    private RhiResourceHandle3D _toneMapVertexShader;
    private RhiResourceHandle3D _toneMapFragmentShader;
    private bool _disposed;

    public RhiResourceHandle3D CullPipeline { get; private set; }
    public RhiResourceHandle3D ClusterPipeline { get; private set; }
    public RhiResourceHandle3D ParticleComputePipeline { get; private set; }
    public RhiResourceHandle3D ForwardPipeline { get; private set; }
    public RhiResourceHandle3D ParticleRenderPipeline { get; private set; }
    public RhiResourceHandle3D ToneMapPipeline { get; private set; }

    public void Ensure(RhiDevice3D device, GpuDrivenRenderSettings3D settings)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(_disposed, this);
        device.Capabilities.RequireProfile(RhiCapabilityProfile3D.GpuDriven, "GPU-driven pipeline initialization");
        var generation = device.Resources.ContextGeneration;
        if (ReferenceEquals(device, _device) && generation == _generation && _hdr == settings.EnableHdr) return;
        ReleaseOwned();
        _device = device;
        _generation = generation;
        _hdr = settings.EnableHdr;

        _sceneLayout = device.CreateBindGroupLayout(
            $"{_owner}:scene-layout",
            new RhiBindGroupLayoutDescriptor3D("gpu-driven-scene-layout", new[]
            {
                E(0, RhiBindingType3D.UniformBuffer, RhiShaderStage3D.All),
                E(1, RhiBindingType3D.ReadOnlyStorageBuffer, RhiShaderStage3D.Vertex | RhiShaderStage3D.Compute),
                E(2, RhiBindingType3D.ReadOnlyStorageBuffer, RhiShaderStage3D.Vertex | RhiShaderStage3D.Compute),
                E(3, RhiBindingType3D.ReadOnlyStorageBuffer, RhiShaderStage3D.Vertex | RhiShaderStage3D.Compute),
                E(4, RhiBindingType3D.ReadOnlyStorageBuffer, RhiShaderStage3D.All),
                E(5, RhiBindingType3D.StorageBuffer, RhiShaderStage3D.Compute),
                E(6, RhiBindingType3D.StorageBuffer, RhiShaderStage3D.Compute),
                E(7, RhiBindingType3D.StorageBuffer, RhiShaderStage3D.Compute),
                E(8, RhiBindingType3D.ReadOnlyStorageBuffer, RhiShaderStage3D.Vertex)
            }), 1, _owner);
        _lightingLayout = device.CreateBindGroupLayout(
            $"{_owner}:lighting-layout",
            new RhiBindGroupLayoutDescriptor3D("gpu-driven-lighting-layout", new[]
            {
                E(0, RhiBindingType3D.ReadOnlyStorageBuffer, RhiShaderStage3D.Fragment | RhiShaderStage3D.Compute),
                E(1, RhiBindingType3D.ReadOnlyStorageBuffer, RhiShaderStage3D.Fragment | RhiShaderStage3D.Compute),
                E(2, RhiBindingType3D.ReadOnlyStorageBuffer, RhiShaderStage3D.Fragment | RhiShaderStage3D.Compute),
                E(3, RhiBindingType3D.StorageBuffer, RhiShaderStage3D.Fragment | RhiShaderStage3D.Compute),
                E(4, RhiBindingType3D.StorageBuffer, RhiShaderStage3D.Fragment | RhiShaderStage3D.Compute)
            }), 1, _owner);
        _particleLayout = device.CreateBindGroupLayout(
            $"{_owner}:particle-layout",
            new RhiBindGroupLayoutDescriptor3D("gpu-driven-particle-layout", new[]
            {
                E(0, RhiBindingType3D.ReadOnlyStorageBuffer, RhiShaderStage3D.Compute),
                E(1, RhiBindingType3D.ReadOnlyStorageBuffer, RhiShaderStage3D.Compute),
                E(2, RhiBindingType3D.StorageBuffer, RhiShaderStage3D.Vertex | RhiShaderStage3D.Compute),
                E(3, RhiBindingType3D.StorageBuffer, RhiShaderStage3D.Compute),
                E(4, RhiBindingType3D.StorageBuffer, RhiShaderStage3D.Compute)
            }), 1, _owner);
        _postLayout = device.CreateBindGroupLayout(
            $"{_owner}:post-layout",
            new RhiBindGroupLayoutDescriptor3D("gpu-driven-post-layout", new[]
            {
                E(0, RhiBindingType3D.SampledTexture, RhiShaderStage3D.Fragment),
                E(1, RhiBindingType3D.Sampler, RhiShaderStage3D.Fragment)
            }), 1, _owner);
        _pipelineLayout = device.CreatePipelineLayout(
            $"{_owner}:pipeline-layout",
            new RhiPipelineLayoutDescriptor3D("gpu-driven-pipeline-layout", new[] { _sceneLayout, _lightingLayout, _particleLayout, _postLayout }),
            1, _owner);
        _sampler = device.CreateSampler(
            $"{_owner}:linear-sampler",
            new RhiSamplerDescriptor3D(addressU: RhiAddressMode3D.ClampToEdge, addressV: RhiAddressMode3D.ClampToEdge, addressW: RhiAddressMode3D.ClampToEdge),
            1, _owner);

        _cullShader = Shader(device, "cull", GpuDrivenShaderCatalog3D.CreateCullMeshlets());
        _clusterShader = Shader(device, "clusters", GpuDrivenShaderCatalog3D.CreateBuildClusters());
        _particleComputeShader = Shader(device, "particles-compute", GpuDrivenShaderCatalog3D.CreateSimulateParticles());
        _forwardVertexShader = Shader(device, "forward-vs", GpuDrivenShaderCatalog3D.CreateForwardVertex());
        _forwardFragmentShader = Shader(device, "forward-fs", GpuDrivenShaderCatalog3D.CreateForwardFragment());
        _particleVertexShader = Shader(device, "particle-vs", GpuDrivenShaderCatalog3D.CreateParticleVertex());
        _particleFragmentShader = Shader(device, "particle-fs", GpuDrivenShaderCatalog3D.CreateParticleFragment());
        _toneMapVertexShader = Shader(device, "tonemap-vs", GpuDrivenShaderCatalog3D.CreateToneMapVertex());
        _toneMapFragmentShader = Shader(device, "tonemap-fs", GpuDrivenShaderCatalog3D.CreateToneMapFragment());

        CullPipeline = device.GetOrCreateComputePipeline(new RhiComputePipelineDescriptor3D("gpu-driven-cull", _pipelineLayout, _cullShader));
        ClusterPipeline = device.GetOrCreateComputePipeline(new RhiComputePipelineDescriptor3D("gpu-driven-clusters", _pipelineLayout, _clusterShader));
        ParticleComputePipeline = device.GetOrCreateComputePipeline(new RhiComputePipelineDescriptor3D("gpu-driven-particles", _pipelineLayout, _particleComputeShader));
        var colorFormat = settings.EnableHdr ? RhiTextureFormat3D.Rgba16Float : RhiTextureFormat3D.Rgba8Unorm;
        ForwardPipeline = device.GetOrCreateRenderPipeline(new RhiRenderPipelineDescriptor3D(
            "gpu-driven-forward", _pipelineLayout, _forwardVertexShader, _forwardFragmentShader,
            colorFormat: colorFormat, depthFormat: RhiTextureFormat3D.Depth32Float,
            vertexBuffers: new[] { CreateGpuVertexLayout() }));
        ParticleRenderPipeline = device.GetOrCreateRenderPipeline(new RhiRenderPipelineDescriptor3D(
            "gpu-driven-particle-render", _pipelineLayout, _particleVertexShader, _particleFragmentShader,
            cullMode: RhiCullMode3D.None, colorFormat: colorFormat, depthFormat: RhiTextureFormat3D.Depth32Float));
        ToneMapPipeline = device.GetOrCreateRenderPipeline(new RhiRenderPipelineDescriptor3D(
            "gpu-driven-tonemap", _pipelineLayout, _toneMapVertexShader, _toneMapFragmentShader,
            cullMode: RhiCullMode3D.None, colorFormat: RhiTextureFormat3D.Rgba8Unorm, depthFormat: null));
    }

    public RhiResourceHandle3D CreateSceneBindGroup(RhiDevice3D device, in GpuDrivenSceneUpload3D scene, long frameIndex)
        => device.CreateBindGroup(
            $"{_owner}:scene-group",
            new RhiBindGroupDescriptor3D("gpu-driven-scene-group", _sceneLayout, new[]
            {
                B(0, scene.FrameConstants), B(1, scene.Objects), B(2, scene.Meshes), B(3, scene.Meshlets),
                B(4, scene.Materials), B(5, scene.VisibleMeshlets), B(6, scene.IndirectCommands), B(7, scene.IndirectCounters),
                B(8, scene.SkinMatrices)
            }), frameIndex, _owner);

    public RhiResourceHandle3D CreateLightingBindGroup(
        RhiDevice3D device,
        in GpuDrivenSceneUpload3D scene,
        RhiResourceHandle3D clusterGrid,
        RhiResourceHandle3D clusterLightIndices,
        long frameIndex)
        => device.CreateBindGroup(
            $"{_owner}:lighting-group",
            new RhiBindGroupDescriptor3D("gpu-driven-lighting-group", _lightingLayout, new[]
            {
                B(0, scene.DirectionalLights), B(1, scene.PointLights), B(2, scene.SpotLights),
                B(3, clusterGrid), B(4, clusterLightIndices)
            }), frameIndex, _owner);

    public RhiResourceHandle3D CreateParticleBindGroup(RhiDevice3D device, in GpuParticleFrame3D particles, long frameIndex)
        => device.CreateBindGroup(
            $"{_owner}:particle-group",
            new RhiBindGroupDescriptor3D("gpu-driven-particle-group", _particleLayout, new[]
            {
                B(0, particles.Emitters), B(1, particles.SourceStates), B(2, particles.DestinationStates),
                B(3, particles.Counters), B(4, particles.IndirectCommands)
            }), frameIndex, _owner);

    public RhiResourceHandle3D CreatePostBindGroup(RhiDevice3D device, RhiResourceHandle3D hdrTexture, long frameIndex)
        => device.CreateBindGroup(
            $"{_owner}:post-group",
            new RhiBindGroupDescriptor3D("gpu-driven-post-group", _postLayout, new[] { B(0, hdrTexture), B(1, _sampler) }),
            frameIndex, _owner);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseOwned();
    }

    private RhiResourceHandle3D Shader(RhiDevice3D device, string name, RhiShaderModuleDescriptor3D descriptor)
        => device.CreateShaderModule($"{_owner}:{name}", descriptor, 1, _owner);

    private void ReleaseOwned()
    {
        var device = _device;
        if (device is not null && !device.IsDisposed && _generation == device.Resources.ContextGeneration)
        {
            Release(_sceneLayout); Release(_lightingLayout); Release(_particleLayout); Release(_postLayout);
            Release(_pipelineLayout); Release(_sampler); Release(_cullShader); Release(_clusterShader);
            Release(_particleComputeShader); Release(_forwardVertexShader); Release(_forwardFragmentShader);
            Release(_particleVertexShader); Release(_particleFragmentShader); Release(_toneMapVertexShader); Release(_toneMapFragmentShader);
        }
        _device = null;
        _generation = 0;
        _sceneLayout = default; _lightingLayout = default; _particleLayout = default; _postLayout = default;
        _pipelineLayout = default; _sampler = default; _cullShader = default; _clusterShader = default;
        _particleComputeShader = default; _forwardVertexShader = default; _forwardFragmentShader = default;
        _particleVertexShader = default; _particleFragmentShader = default; _toneMapVertexShader = default; _toneMapFragmentShader = default;
        CullPipeline = default; ClusterPipeline = default; ParticleComputePipeline = default;
        ForwardPipeline = default; ParticleRenderPipeline = default; ToneMapPipeline = default;
    }

    private void Release(RhiResourceHandle3D handle)
    {
        if (handle.IsValid) _device?.Resources.Release(handle);
    }

    private static RhiVertexBufferLayout3D CreateGpuVertexLayout()
        => new(100, new[]
        {
            new RhiVertexAttribute3D(0, 0, RhiVertexFormat3D.Float32x3),
            new RhiVertexAttribute3D(1, 12, RhiVertexFormat3D.Float32x3),
            new RhiVertexAttribute3D(2, 24, RhiVertexFormat3D.Float32x2),
            new RhiVertexAttribute3D(3, 32, RhiVertexFormat3D.Float32x4),
            new RhiVertexAttribute3D(4, 48, RhiVertexFormat3D.Float32x4),
            new RhiVertexAttribute3D(5, 64, RhiVertexFormat3D.Float32),
            new RhiVertexAttribute3D(6, 68, RhiVertexFormat3D.Float32x4),
            new RhiVertexAttribute3D(7, 84, RhiVertexFormat3D.Float32x4)
        });

    private static RhiBindGroupLayoutEntry3D E(int binding, RhiBindingType3D type, RhiShaderStage3D stages)
        => new(binding, type, stages);
    private static RhiBindGroupEntry3D B(int binding, RhiResourceHandle3D resource)
        => new(binding, resource);
}
