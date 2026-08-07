using System;
using System.Collections.Generic;
using ThreeDEngine.Core.Diagnostics;
using ThreeDEngine.Core.Rendering.Rhi;

namespace ThreeDEngine.Core.Rendering.GpuDriven;

/// <summary>
/// GPU-driven frame coordinator. It encodes clustered lighting, meshlet visibility/LOD,
/// GPU particle simulation, multi-draw indirect rendering and HDR tone mapping. The class is
/// consumed only by explicit GPU backends whose device satisfies the GpuDriven capability profile.
/// </summary>
internal sealed class GpuDrivenRenderer3D : IDisposable
{
    private const int IndexedIndirectStride = 20;
    private const int ParticleIndirectStride = 16;
    private readonly GpuDrivenSceneDatabase3D _scene = new();
    private readonly GpuParticlePipeline3D _particles = new();
    private readonly GpuDrivenPipelineState3D _pipelines = new();
    private RenderGraph3D? _graph;
    private GraphResources _graphResources;
    private GraphSignature _graphSignature;
    private GpuDrivenSceneUpload3D _currentScene;
    private GpuParticleFrame3D _currentParticles;
    private RhiResourceHandle3D _sceneGroup;
    private RhiResourceHandle3D _lightingGroup;
    private RhiResourceHandle3D _particleGroup;
    private RhiResourceHandle3D _postGroup;
    private bool _disposed;

    public GpuDrivenFrameStatistics3D LastFrameStatistics { get; private set; }

    public GpuDrivenFrameStatistics3D Render(
        SceneRenderPlan3D plan,
        RhiDevice3D device,
        IRhiCommandExecutor3D executor,
        RhiResourceHandle3D outputTarget,
        RhiTextureDescriptor3D outputDescriptor,
        GpuDrivenRenderSettings3D? settings = null,
        double gpuFrameMilliseconds = double.NaN)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(executor);
        ObjectDisposedException.ThrowIf(_disposed, this);
        settings ??= GpuDrivenRenderSettings3D.Default;
        if (settings.EnableOcclusionCulling)
            throw new InvalidOperationException("Hi-Z occlusion culling requires the explicit depth-pyramid backend contract, which is not available in this source drop. No CPU occlusion fallback is permitted.");
        device.Capabilities.RequireProfile(RhiCapabilityProfile3D.GpuDriven, "GPU-driven frame");
        if (device.Profile is not RhiCapabilityProfile3D.GpuDriven and not RhiCapabilityProfile3D.WebGpuBaseline)
            throw new InvalidOperationException($"GPU-driven rendering requires a GpuDriven/WebGpuBaseline device profile, but {device.Profile} is active.");
        device.Resources.RequireKind(outputTarget, RhiResourceKind3D.Texture, "GPU-driven output target");
        if ((outputDescriptor.Usage & RhiTextureUsage3D.RenderTarget) == 0)
            throw new InvalidOperationException("GPU-driven output texture must declare RenderTarget usage.");
        if (outputDescriptor.Format != RhiTextureFormat3D.Rgba8Unorm)
            throw new InvalidOperationException("GPU-driven presentation target must use Rgba8Unorm; HDR is rendered to an internal floating-point target and tone-mapped explicitly.");

        var frameIndex = device.BeginFrame(plan.RhiSubmission);
        var encoder = device.CreateCommandEncoder();
        encoder.Reset($"gpu-driven-frame-{frameIndex}");
        try
        {
            _pipelines.Ensure(device, settings);
            _currentScene = _scene.Prepare(plan, device, encoder, settings);
            _currentParticles = _particles.Prepare(plan, device, encoder, settings);
            EnsureGraph(device, outputTarget, outputDescriptor, settings);

            var clusterGrid = _graph!.GetResource(_graphResources.ClusterGrid);
            var clusterIndices = _graph.GetResource(_graphResources.ClusterLightIndices);
            _sceneGroup = _pipelines.CreateSceneBindGroup(device, _currentScene, frameIndex);
            _lightingGroup = _pipelines.CreateLightingBindGroup(device, _currentScene, clusterGrid, clusterIndices, frameIndex);
            _particleGroup = _currentParticles.EmitterCount == 0
                ? default
                : _pipelines.CreateParticleBindGroup(device, _currentParticles, frameIndex);
            _postGroup = settings.EnableHdr
                ? _pipelines.CreatePostBindGroup(device, _graph.GetResource(_graphResources.HdrColor), frameIndex)
                : default;

            _graph.Encode(encoder);
            using var commandBuffer = encoder.Finish();
            var fence = device.Submit(commandBuffer, executor);
            device.EndFrame(fence, gpuFrameMilliseconds);
            if (_currentParticles.EmitterCount != 0) _particles.CompleteFrame();

            var graphStats = _graph.Statistics;
            LastFrameStatistics = new GpuDrivenFrameStatistics3D(
                frameIndex,
                _currentScene.ObjectCount,
                _currentScene.MeshCount,
                _currentScene.MaterialCount,
                _currentScene.MeshletCount,
                _currentParticles.ParticleCapacity,
                1 + (settings.EnableClusteredLighting ? 1 : 0) + (_currentParticles.EmitterCount == 0 ? 0 : 1),
                1 + (_currentParticles.EmitterCount == 0 ? 0 : 1) + (settings.EnableHdr ? 1 : 0),
                graphStats.BarrierCount,
                _currentScene.IndirectCommandCapacity,
                checked(_currentScene.UploadedBytes + _currentParticles.UploadedBytes),
                graphStats.PhysicalResourceCount,
                graphStats.AliasedResourceCount,
                settings.EnableOcclusionCulling,
                _currentParticles.EmitterCount != 0,
                settings.EnableClusteredLighting,
                gpuFrameMilliseconds);
            return LastFrameStatistics;
        }
        catch (Exception ex)
        {
            device.AbortFrame();
            EngineLog3D.Critical("GpuDrivenRenderer", "GPU-driven frame encoding/submission failed. No legacy or CPU fallback was attempted.", ex);
            throw;
        }
    }

    public void ApplyStats(RenderStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        var value = LastFrameStatistics;
        stats.GpuDrivenActive = value.FrameIndex > 0;
        stats.GpuDrivenObjectCount = value.ObjectCount;
        stats.GpuDrivenMeshCount = value.MeshCount;
        stats.GpuDrivenMaterialCount = value.MaterialCount;
        stats.GpuDrivenMeshletCount = value.MeshletCount;
        stats.GpuDrivenParticleCapacity = value.ParticleCapacity;
        stats.GpuDrivenComputePassCount = value.ComputePassCount;
        stats.GpuDrivenRenderPassCount = value.RenderPassCount;
        stats.GpuDrivenBarrierCount = value.BarrierCount;
        stats.GpuDrivenIndirectCommandCapacity = value.IndirectCommandCapacity;
        stats.GpuDrivenUploadedBytes = value.UploadedBytes;
        stats.GpuDrivenPhysicalResourceCount = value.RenderGraphPhysicalResources;
        stats.GpuDrivenAliasedResourceCount = value.RenderGraphAliasedResources;
        stats.GpuDrivenOcclusionCullingActive = value.OcclusionCullingEnabled;
        stats.GpuDrivenParticlesActive = value.GpuParticlesEnabled;
        stats.GpuDrivenClusteredLightingActive = value.ClusteredLightingEnabled;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _graph?.Dispose();
        _graph = null;
        _scene.Dispose();
        _particles.Dispose();
        _pipelines.Dispose();
    }

    private void EnsureGraph(
        RhiDevice3D device,
        RhiResourceHandle3D outputTarget,
        RhiTextureDescriptor3D outputDescriptor,
        GpuDrivenRenderSettings3D settings)
    {
        var signature = new GraphSignature(
            device.Resources.ContextGeneration,
            outputTarget,
            outputDescriptor.Width,
            outputDescriptor.Height,
            settings.EnableHdr,
            settings.EnableClusteredLighting,
            settings.ClusterCountX,
            settings.ClusterCountY,
            settings.ClusterCountZ,
            settings.MaximumLightsPerCluster,
            settings.EnableMeshletConeCulling,
            _currentParticles.EmitterCount,
            _currentScene.FrameConstants,
            _currentScene.Objects,
            _currentScene.Meshes,
            _currentScene.Meshlets,
            _currentScene.Materials,
            _currentScene.SkinMatrices,
            _currentScene.DirectionalLights,
            _currentScene.PointLights,
            _currentScene.SpotLights,
            _currentScene.VisibleMeshlets,
            _currentScene.IndirectCommands,
            _currentScene.IndirectCounters,
            _currentParticles.Emitters,
            _currentParticles.SourceStates,
            _currentParticles.DestinationStates,
            _currentParticles.Counters,
            _currentParticles.IndirectCommands);
        if (_graph is not null && signature.Equals(_graphSignature)) return;
        _graph?.Dispose();
        _graph = new RenderGraph3D();
        _graphSignature = signature;
        BuildGraph(_graph, device, outputTarget, outputDescriptor, settings);
        _graph.Compile(device, $"gpu-driven-framegraph-{device.Resources.ContextGeneration}");
    }

    private void BuildGraph(
        RenderGraph3D graph,
        RhiDevice3D device,
        RhiResourceHandle3D outputTarget,
        RhiTextureDescriptor3D outputDescriptor,
        GpuDrivenRenderSettings3D settings)
    {
        var frame = graph.ImportBuffer("frame", _currentScene.FrameConstants, Buffer(device, _currentScene.FrameConstants));
        var objects = graph.ImportBuffer("objects", _currentScene.Objects, Buffer(device, _currentScene.Objects));
        var meshes = graph.ImportBuffer("meshes", _currentScene.Meshes, Buffer(device, _currentScene.Meshes));
        var meshlets = graph.ImportBuffer("meshlets", _currentScene.Meshlets, Buffer(device, _currentScene.Meshlets));
        var materials = graph.ImportBuffer("materials", _currentScene.Materials, Buffer(device, _currentScene.Materials));
        var skinMatrices = graph.ImportBuffer("skin-matrices", _currentScene.SkinMatrices, Buffer(device, _currentScene.SkinMatrices));
        var directionalLights = graph.ImportBuffer("directional-lights", _currentScene.DirectionalLights, Buffer(device, _currentScene.DirectionalLights));
        var pointLights = graph.ImportBuffer("point-lights", _currentScene.PointLights, Buffer(device, _currentScene.PointLights));
        var spotLights = graph.ImportBuffer("spot-lights", _currentScene.SpotLights, Buffer(device, _currentScene.SpotLights));
        var visibleMeshlets = graph.ImportBuffer("visible-meshlets", _currentScene.VisibleMeshlets, Buffer(device, _currentScene.VisibleMeshlets));
        var indirectCommands = graph.ImportBuffer("indirect-commands", _currentScene.IndirectCommands, Buffer(device, _currentScene.IndirectCommands));
        var indirectCounters = graph.ImportBuffer("indirect-counters", _currentScene.IndirectCounters, Buffer(device, _currentScene.IndirectCounters));
        var clusterGrid = graph.CreateBuffer("cluster-grid",
            new RhiBufferDescriptor3D(checked((long)settings.ClusterCount * 8), RhiBufferUsage3D.Storage | RhiBufferUsage3D.CopyDestination));
        var clusterLightIndices = graph.CreateBuffer("cluster-light-indices",
            new RhiBufferDescriptor3D(
                checked((long)settings.ClusterCount * settings.MaximumLightsPerCluster * sizeof(uint)),
                RhiBufferUsage3D.Storage | RhiBufferUsage3D.CopyDestination));
        var output = graph.ImportTexture("output", outputTarget, outputDescriptor);
        var depth = graph.CreateTexture("depth", new RhiTextureDescriptor3D(
            outputDescriptor.Width, outputDescriptor.Height, RhiTextureFormat3D.Depth32Float,
            RhiTextureUsage3D.DepthStencil | RhiTextureUsage3D.CopySource));
        var hdr = settings.EnableHdr
            ? graph.CreateTexture("hdr-color", new RhiTextureDescriptor3D(
                outputDescriptor.Width, outputDescriptor.Height, RhiTextureFormat3D.Rgba16Float,
                RhiTextureUsage3D.RenderTarget | RhiTextureUsage3D.Sampled | RhiTextureUsage3D.Storage))
            : output;

        if (settings.EnableClusteredLighting)
        {
            graph.AddPass("clustered-light-assignment", (context, encoder) =>
                {
                    encoder.ClearBuffer(context.GetResource(clusterGrid), 0, BufferBytes(device, context.GetResource(clusterGrid)));
                    encoder.ClearBuffer(context.GetResource(clusterLightIndices), 0, BufferBytes(device, context.GetResource(clusterLightIndices)));
                    using var pass = encoder.BeginComputePass(new RhiComputePassDescriptor3D("clustered-light-assignment"));
                    pass.SetPipeline(_pipelines.ClusterPipeline);
                    pass.SetBindGroup(0, _sceneGroup);
                    pass.SetBindGroup(1, _lightingGroup);
                    pass.Dispatch(DivideRoundUp(settings.ClusterCount, 64));
                })
                .Read(frame, RhiPipelineStage3D.Compute, RhiResourceAccess3D.UniformRead)
                .Read(directionalLights, RhiPipelineStage3D.Compute, RhiResourceAccess3D.ShaderRead)
                .Read(pointLights, RhiPipelineStage3D.Compute, RhiResourceAccess3D.ShaderRead)
                .Read(spotLights, RhiPipelineStage3D.Compute, RhiResourceAccess3D.ShaderRead)
                .Write(clusterGrid, RhiPipelineStage3D.Compute, RhiResourceAccess3D.ShaderWrite)
                .Write(clusterLightIndices, RhiPipelineStage3D.Compute, RhiResourceAccess3D.ShaderWrite);
        }

        graph.AddPass("meshlet-visibility-lod", (_, encoder) =>
            {
                using var pass = encoder.BeginComputePass(new RhiComputePassDescriptor3D("meshlet-visibility-lod"));
                pass.SetPipeline(_pipelines.CullPipeline);
                pass.SetBindGroup(0, _sceneGroup);
                pass.Dispatch(DivideRoundUp(_currentScene.ObjectCount, settings.CullingWorkgroupSize));
            })
            .Read(frame, RhiPipelineStage3D.Compute, RhiResourceAccess3D.UniformRead)
            .Read(objects, RhiPipelineStage3D.Compute, RhiResourceAccess3D.ShaderRead)
            .Read(meshes, RhiPipelineStage3D.Compute, RhiResourceAccess3D.ShaderRead)
            .Read(meshlets, RhiPipelineStage3D.Compute, RhiResourceAccess3D.ShaderRead)
            .Read(materials, RhiPipelineStage3D.Compute, RhiResourceAccess3D.ShaderRead)
            .Write(visibleMeshlets, RhiPipelineStage3D.Compute, RhiResourceAccess3D.ShaderWrite)
            .Write(indirectCommands, RhiPipelineStage3D.Compute, RhiResourceAccess3D.ShaderWrite)
            .Write(indirectCounters, RhiPipelineStage3D.Compute, RhiResourceAccess3D.ShaderWrite);

        RenderGraphResourceHandle3D particleEmitters = default;
        RenderGraphResourceHandle3D particleSource = default;
        RenderGraphResourceHandle3D particleDestination = default;
        RenderGraphResourceHandle3D particleCounters = default;
        RenderGraphResourceHandle3D particleIndirect = default;
        if (_currentParticles.EmitterCount != 0)
        {
            particleEmitters = graph.ImportBuffer("particle-emitters", _currentParticles.Emitters, Buffer(device, _currentParticles.Emitters));
            particleSource = graph.ImportBuffer("particle-source", _currentParticles.SourceStates, Buffer(device, _currentParticles.SourceStates));
            particleDestination = graph.ImportBuffer("particle-destination", _currentParticles.DestinationStates, Buffer(device, _currentParticles.DestinationStates));
            particleCounters = graph.ImportBuffer("particle-counters", _currentParticles.Counters, Buffer(device, _currentParticles.Counters));
            particleIndirect = graph.ImportBuffer("particle-indirect", _currentParticles.IndirectCommands, Buffer(device, _currentParticles.IndirectCommands));
            graph.AddPass("gpu-particle-simulation", (_, encoder) =>
                {
                    using var pass = encoder.BeginComputePass(new RhiComputePassDescriptor3D("gpu-particle-simulation"));
                    pass.SetPipeline(_pipelines.ParticleComputePipeline);
                    pass.SetBindGroup(0, _sceneGroup);
                    pass.SetBindGroup(2, _particleGroup);
                    pass.Dispatch(_currentParticles.WorkgroupCount);
                })
                .Read(frame, RhiPipelineStage3D.Compute, RhiResourceAccess3D.UniformRead)
                .Read(particleEmitters, RhiPipelineStage3D.Compute, RhiResourceAccess3D.ShaderRead)
                .Read(particleSource, RhiPipelineStage3D.Compute, RhiResourceAccess3D.ShaderRead)
                .Write(particleDestination, RhiPipelineStage3D.Compute, RhiResourceAccess3D.ShaderWrite)
                .Write(particleCounters, RhiPipelineStage3D.Compute, RhiResourceAccess3D.ShaderWrite)
                .Write(particleIndirect, RhiPipelineStage3D.Compute, RhiResourceAccess3D.ShaderWrite);
        }

        graph.AddPass("gpu-driven-forward", (context, encoder) =>
            {
                var colorTarget = context.GetResource(hdr);
                var depthTarget = context.GetResource(depth);
                using var pass = encoder.BeginRenderPass(new RhiRenderPassDescriptor3D(
                    "gpu-driven-forward", RhiPassKind3D.ForwardOpaque,
                    RhiLoadOperation3D.Clear, RhiStoreOperation3D.Store,
                    RhiLoadOperation3D.Clear, RhiStoreOperation3D.Store,
                    colorTarget, depthTarget));
                pass.SetPipeline(_pipelines.ForwardPipeline);
                pass.SetBindGroup(0, _sceneGroup);
                pass.SetBindGroup(1, _lightingGroup);
                var groups = _currentScene.MeshGroups;
                for (var i = 0; i < groups.Count; i++)
                {
                    var group = groups[i];
                    pass.SetVertexBuffer(0, group.VertexBuffer);
                    pass.SetIndexBuffer(group.IndexBuffer);
                    pass.MultiDrawIndexedIndirect(_currentScene.IndirectCommands, group.IndirectByteOffset, group.IndirectCommandCapacity, IndexedIndirectStride);
                }
            })
            .Read(frame, RhiPipelineStage3D.Vertex, RhiResourceAccess3D.UniformRead)
            .Read(objects, RhiPipelineStage3D.Vertex, RhiResourceAccess3D.ShaderRead)
            .Read(meshes, RhiPipelineStage3D.Vertex, RhiResourceAccess3D.ShaderRead)
            .Read(meshlets, RhiPipelineStage3D.Vertex, RhiResourceAccess3D.ShaderRead)
            .Read(materials, RhiPipelineStage3D.AllGraphics, RhiResourceAccess3D.ShaderRead)
            .Read(skinMatrices, RhiPipelineStage3D.Vertex, RhiResourceAccess3D.ShaderRead)
            .Read(clusterGrid, RhiPipelineStage3D.Fragment, RhiResourceAccess3D.ShaderRead)
            .Read(clusterLightIndices, RhiPipelineStage3D.Fragment, RhiResourceAccess3D.ShaderRead)
            .Read(indirectCommands, RhiPipelineStage3D.Indirect, RhiResourceAccess3D.IndirectRead)
            .Write(hdr, RhiPipelineStage3D.Fragment, RhiResourceAccess3D.RenderTargetWrite)
            .Write(depth, RhiPipelineStage3D.Fragment, RhiResourceAccess3D.DepthStencilWrite);

        if (_currentParticles.EmitterCount != 0)
        {
            graph.AddPass("gpu-particle-render", (context, encoder) =>
                {
                    using var pass = encoder.BeginRenderPass(new RhiRenderPassDescriptor3D(
                        "gpu-particle-render", RhiPassKind3D.ForwardTransparent,
                        RhiLoadOperation3D.Load, RhiStoreOperation3D.Store,
                        RhiLoadOperation3D.Load, RhiStoreOperation3D.Store,
                        context.GetResource(hdr), context.GetResource(depth)));
                    pass.SetPipeline(_pipelines.ParticleRenderPipeline);
                    pass.SetBindGroup(0, _sceneGroup);
                    pass.SetBindGroup(2, _particleGroup);
                    for (var i = 0; i < _currentParticles.EmitterCount; i++)
                        pass.DrawIndirect(_currentParticles.IndirectCommands, checked((long)i * ParticleIndirectStride));
                })
                .Read(frame, RhiPipelineStage3D.Vertex, RhiResourceAccess3D.UniformRead)
                .Read(particleDestination, RhiPipelineStage3D.Vertex, RhiResourceAccess3D.ShaderRead)
                .Read(particleIndirect, RhiPipelineStage3D.Indirect, RhiResourceAccess3D.IndirectRead)
                .Write(hdr, RhiPipelineStage3D.Fragment, RhiResourceAccess3D.RenderTargetWrite)
                .Write(depth, RhiPipelineStage3D.Fragment, RhiResourceAccess3D.DepthStencilWrite);
        }

        if (settings.EnableHdr)
        {
            graph.AddPass("hdr-tone-map", (context, encoder) =>
                {
                    using var pass = encoder.BeginRenderPass(new RhiRenderPassDescriptor3D(
                        "hdr-tone-map", RhiPassKind3D.PostProcess,
                        RhiLoadOperation3D.Clear, RhiStoreOperation3D.Store,
                        colorTarget: context.GetResource(output)));
                    pass.SetPipeline(_pipelines.ToneMapPipeline);
                    pass.SetBindGroup(3, _postGroup);
                    pass.Draw(3);
                })
                .Read(hdr, RhiPipelineStage3D.Fragment, RhiResourceAccess3D.ShaderRead)
                .Write(output, RhiPipelineStage3D.Fragment, RhiResourceAccess3D.RenderTargetWrite);
        }

        _graphResources = new GraphResources(clusterGrid, clusterLightIndices, hdr, depth, output);
    }

    private static RhiBufferDescriptor3D Buffer(RhiDevice3D device, RhiResourceHandle3D handle)
        => device.Resources.GetDescriptor<RhiBufferDescriptor3D>(handle, "GPU-driven graph import");

    private static long BufferBytes(RhiDevice3D device, RhiResourceHandle3D handle)
        => device.Resources.GetByteSize(handle);

    private static int DivideRoundUp(int value, int divisor)
        => value <= 0 ? 1 : checked((value + divisor - 1) / divisor);

    private readonly record struct GraphResources(
        RenderGraphResourceHandle3D ClusterGrid,
        RenderGraphResourceHandle3D ClusterLightIndices,
        RenderGraphResourceHandle3D HdrColor,
        RenderGraphResourceHandle3D Depth,
        RenderGraphResourceHandle3D Output);

    private readonly record struct GraphSignature(
        uint DeviceGeneration,
        RhiResourceHandle3D Output,
        int Width,
        int Height,
        bool Hdr,
        bool Clustered,
        int ClusterCountX,
        int ClusterCountY,
        int ClusterCountZ,
        int MaximumLightsPerCluster,
        bool ConeCulling,
        int ParticleEmitterCount,
        RhiResourceHandle3D Frame,
        RhiResourceHandle3D Objects,
        RhiResourceHandle3D Meshes,
        RhiResourceHandle3D Meshlets,
        RhiResourceHandle3D Materials,
        RhiResourceHandle3D SkinMatrices,
        RhiResourceHandle3D DirectionalLights,
        RhiResourceHandle3D PointLights,
        RhiResourceHandle3D SpotLights,
        RhiResourceHandle3D VisibleMeshlets,
        RhiResourceHandle3D IndirectCommands,
        RhiResourceHandle3D IndirectCounters,
        RhiResourceHandle3D ParticleEmitters,
        RhiResourceHandle3D ParticleSource,
        RhiResourceHandle3D ParticleDestination,
        RhiResourceHandle3D ParticleCounters,
        RhiResourceHandle3D ParticleIndirect);
}
