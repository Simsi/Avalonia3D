using System;
using ThreeDEngine.Core.Diagnostics;
using ThreeDEngine.Core.Resources;

namespace ThreeDEngine.Core.Rendering.Rhi;

/// <summary>
/// Executable per-context rendering device. It owns command submission, frame resources,
/// logical resources, pipeline descriptors and fence-gated lifetime. Backend adapters execute
/// command buffers through <see cref="IRhiCommandExecutor3D"/>; missing GPU capabilities fail at
/// device or pipeline creation and are never replaced with CPU work.
/// </summary>
internal sealed class RhiDevice3D : IDisposable, ThreeDEngine.Core.Rendering.IRenderDeviceDiagnostics3D
{
    private bool _disposed;
    private bool _frameOpen;
    private long _validationCount;
    private double _lastGpuFrameMilliseconds = double.NaN;
    private RhiFence3D _lastFence;
    private RhiFrameResourceLease3D _currentFrame;

    public RhiDevice3D(
        RhiDeviceCapabilities3D capabilities,
        EngineResourceConfiguration3D? resourceConfiguration = null,
        RhiCapabilityProfile3D profile = RhiCapabilityProfile3D.LegacyRaster)
    {
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        Profile = profile;
        Capabilities.RequireProfile(profile, "RHI device initialization");
        ValidateRequiredLimits(Capabilities);
        Resources = new RhiResourceRegistry3D(resourceConfiguration);
        Queue = new RhiQueue3D(Capabilities, Resources);
        FrameResources = new RhiFrameResources3D();
        PipelineCache = new RhiPipelineCache3D(Resources);
        DeferredLifetime = new RhiDeferredLifetime3D();
        EngineLog3D.Info("RHI", $"Executable device initialized: profile={Profile}; {Capabilities.ToDiagnosticString()}");
    }

    public RhiDeviceCapabilities3D Capabilities { get; }
    public RhiCapabilityProfile3D Profile { get; }
    public RhiResourceRegistry3D Resources { get; }
    public RhiQueue3D Queue { get; }
    public RhiFrameResources3D FrameResources { get; }
    public RhiPipelineCache3D PipelineCache { get; }
    public RhiDeferredLifetime3D DeferredLifetime { get; }
    public long FrameIndex => FrameResources.FrameIndex;
    public long ValidationCount => _validationCount;
    public bool IsDisposed => _disposed;
    public bool GpuTimingSupported => Capabilities.Supports(RhiFeature3D.TimerQueries) || Capabilities.Supports(RhiFeature3D.TimestampQueries);
    public double LastGpuFrameMilliseconds => _lastGpuFrameMilliseconds;
    public RhiFence3D LastFence => _lastFence;
    public RhiUploadRing3D? CurrentUploadRing => FrameResources.ActiveUploadRing;

    public RhiResourceSnapshot3D CaptureResourceSnapshot() => Resources.CaptureSnapshot();
    public RhiCommandEncoder3D CreateCommandEncoder() { ThrowIfDisposed(); return new RhiCommandEncoder3D(Capabilities, Resources); }

    public void ValidateSubmission(RhiFrameSubmission3D submission)
    {
        ThrowIfDisposed();
        if (submission is null) throw new ArgumentNullException(nameof(submission));
        Capabilities.Require(submission.RequiredFeatures, "frame submission");
        _validationCount++;
    }

    public long BeginFrame(RhiFrameSubmission3D submission)
    {
        ThrowIfDisposed();
        if (_frameOpen) throw new InvalidOperationException("An RHI frame is already open.");
        ValidateSubmission(submission);
        DeferredLifetime.Collect(Queue);
        _currentFrame = FrameResources.BeginFrame(Queue);
        _frameOpen = true;
        return _currentFrame.FrameIndex;
    }

    public RhiFence3D Submit(RhiCommandBuffer3D commandBuffer, IRhiCommandExecutor3D executor)
    {
        ThrowIfDisposed();
        if (!_frameOpen) throw new InvalidOperationException("BeginFrame must be called before RHI submission.");
        _lastFence = Queue.Submit(commandBuffer, executor);
        return _lastFence;
    }

    public void EndFrame(RhiFence3D fence, double gpuFrameMilliseconds = double.NaN)
    {
        ThrowIfDisposed();
        if (!_frameOpen) throw new InvalidOperationException("No RHI frame is open.");
        if (!fence.IsValid || !fence.Equals(_lastFence))
            throw new InvalidOperationException("EndFrame requires the fence returned by this frame's queue submission.");
        Queue.RequireComplete(fence, "end frame");
        if (!double.IsNaN(gpuFrameMilliseconds) && (!double.IsFinite(gpuFrameMilliseconds) || gpuFrameMilliseconds < 0d))
            throw new ArgumentOutOfRangeException(nameof(gpuFrameMilliseconds));
        _lastGpuFrameMilliseconds = gpuFrameMilliseconds;
        FrameResources.EndFrame(fence);
        _frameOpen = false;
    }

    public void AbortFrame()
    {
        if (_disposed || !_frameOpen) return;
        FrameResources.AbortFrame();
        _frameOpen = false;
    }

    public RhiResourceHandle3D CreateBuffer(string key, RhiBufferDescriptor3D descriptor, long contentVersion, string? owner = null)
    {
        ThrowIfDisposed();
        ValidateBuffer(descriptor, key);
        return Resources.RegisterBuffer(key, descriptor, contentVersion, owner);
    }

    public RhiResourceHandle3D CreateTexture(string key, RhiTextureDescriptor3D descriptor, long contentVersion, string? owner = null)
    {
        ThrowIfDisposed();
        ValidateTexture(descriptor, key);
        return Resources.RegisterTexture(key, descriptor, contentVersion, owner);
    }

    public RhiResourceHandle3D CreateSampler(string key, RhiSamplerDescriptor3D descriptor, long contentVersion, string? owner = null)
    {
        ThrowIfDisposed();
        Capabilities.Require(RhiFeature3D.SamplerObjects, key);
        return Resources.RegisterSampler(key, descriptor, contentVersion, owner);
    }

    public RhiResourceHandle3D CreateShaderModule(string key, RhiShaderModuleDescriptor3D descriptor, long contentVersion, string? owner = null)
    {
        ThrowIfDisposed();
        Capabilities.Require(RhiFeature3D.ShaderReflection, key);
        return Resources.RegisterShaderModule(key, descriptor, contentVersion, owner);
    }

    public RhiResourceHandle3D CreateBindGroupLayout(string key, RhiBindGroupLayoutDescriptor3D descriptor, long contentVersion, string? owner = null)
    {
        ThrowIfDisposed();
        Capabilities.Require(RhiFeature3D.BindGroups, key);
        if (Capabilities.Limits.MaxBindingsPerGroup > 0 && descriptor.Entries.Length > Capabilities.Limits.MaxBindingsPerGroup)
            throw new RhiDeviceLimitException3D(Capabilities.Api, key, $"bindings <= {Capabilities.Limits.MaxBindingsPerGroup}", Capabilities);
        return Resources.RegisterBindGroupLayout(key, descriptor, contentVersion, owner);
    }

    public RhiResourceHandle3D CreatePipelineLayout(string key, RhiPipelineLayoutDescriptor3D descriptor, long contentVersion, string? owner = null)
    {
        ThrowIfDisposed();
        Capabilities.Require(RhiFeature3D.PipelineLayouts, key);
        if (Capabilities.Limits.MaxBindGroups > 0 && descriptor.BindGroupLayouts.Length > Capabilities.Limits.MaxBindGroups)
            throw new RhiDeviceLimitException3D(Capabilities.Api, key, $"bind groups <= {Capabilities.Limits.MaxBindGroups}", Capabilities);
        foreach (var handle in descriptor.BindGroupLayouts) Resources.RequireKind(handle, RhiResourceKind3D.BindGroupLayout, key);
        return Resources.RegisterPipelineLayout(key, descriptor, contentVersion, owner);
    }

    public RhiResourceHandle3D CreateBindGroup(string key, RhiBindGroupDescriptor3D descriptor, long contentVersion, string? owner = null)
    {
        ThrowIfDisposed();
        Capabilities.Require(RhiFeature3D.BindGroups, key);
        Resources.RequireKind(descriptor.Layout, RhiResourceKind3D.BindGroupLayout, key);
        ValidateBindGroup(descriptor, Resources.GetDescriptor<RhiBindGroupLayoutDescriptor3D>(descriptor.Layout, key), key);
        return Resources.RegisterBindGroup(key, descriptor, contentVersion, owner);
    }

    public RhiResourceHandle3D GetOrCreateRenderPipeline(RhiRenderPipelineDescriptor3D descriptor)
    {
        ThrowIfDisposed();
        ValidateRenderPipeline(descriptor);
        return PipelineCache.GetOrCreate(descriptor);
    }

    public RhiResourceHandle3D GetOrCreateComputePipeline(RhiComputePipelineDescriptor3D descriptor)
    {
        ThrowIfDisposed();
        Capabilities.Require(RhiFeature3D.ComputeShaders | RhiFeature3D.StorageBuffers, descriptor.Label);
        Resources.RequireKind(descriptor.Layout, RhiResourceKind3D.PipelineLayout, descriptor.Label);
        Resources.RequireKind(descriptor.ComputeShader, RhiResourceKind3D.ShaderModule, descriptor.Label);
        var computeLayout = Resources.GetDescriptor<RhiPipelineLayoutDescriptor3D>(descriptor.Layout, descriptor.Label);
        var computeModule = Resources.GetDescriptor<RhiShaderModuleDescriptor3D>(descriptor.ComputeShader, descriptor.Label);
        ValidateShaderReflection(computeModule.Reflection, RhiShaderStage3D.Compute, computeLayout, descriptor.Label);
        return PipelineCache.GetOrCreate(descriptor);
    }

    public void ValidateBuffer(RhiBufferDescriptor3D descriptor, string operation)
    {
        ThrowIfDisposed();
        var required = RhiFeature3D.None;
        if ((descriptor.Usage & RhiBufferUsage3D.Storage) != 0) required |= RhiFeature3D.StorageBuffers;
        if ((descriptor.Usage & RhiBufferUsage3D.Indirect) != 0) required |= RhiFeature3D.IndirectBuffers;
        if ((descriptor.Usage & (RhiBufferUsage3D.CopySource | RhiBufferUsage3D.CopyDestination)) != 0) required |= RhiFeature3D.CopyCommands;
        Capabilities.Require(required, operation);
        if (Capabilities.Limits.MaxBufferSize > 0 && descriptor.ByteSize > Capabilities.Limits.MaxBufferSize)
            throw new RhiDeviceLimitException3D(Capabilities.Api, operation, $"buffer bytes <= {Capabilities.Limits.MaxBufferSize}", Capabilities);
    }

    public void ValidateTexture(RhiTextureDescriptor3D descriptor, string operation)
    {
        ThrowIfDisposed();
        var requiredFeatures = RhiFeature3D.Texture2D;
        if ((descriptor.Usage & RhiTextureUsage3D.RenderTarget) != 0) requiredFeatures |= RhiFeature3D.RenderTargets;
        if ((descriptor.Usage & RhiTextureUsage3D.DepthStencil) != 0) requiredFeatures |= RhiFeature3D.DepthTextures;
        if (descriptor.Format is RhiTextureFormat3D.Rgba16Float or RhiTextureFormat3D.Rgba32Float) requiredFeatures |= RhiFeature3D.FloatTextures;
        if ((descriptor.Usage & RhiTextureUsage3D.Storage) != 0) requiredFeatures |= RhiFeature3D.StorageTextures;
        if ((descriptor.Usage & (RhiTextureUsage3D.CopySource | RhiTextureUsage3D.CopyDestination)) != 0) requiredFeatures |= RhiFeature3D.CopyCommands;
        Capabilities.Require(requiredFeatures, operation);
        if (descriptor.Width > Capabilities.Limits.MaxTextureSize || descriptor.Height > Capabilities.Limits.MaxTextureSize)
            throw new RhiDeviceLimitException3D(Capabilities.Api, operation, $"texture dimensions <= {Capabilities.Limits.MaxTextureSize}", Capabilities);
        if (descriptor.Samples > 1 && descriptor.Samples > Capabilities.Limits.MaxSamples)
            throw new RhiDeviceLimitException3D(Capabilities.Api, operation, $"samples <= {Capabilities.Limits.MaxSamples}", Capabilities);
        if ((descriptor.Usage & RhiTextureUsage3D.RenderTarget) != 0 &&
            (descriptor.Width > Capabilities.Limits.MaxRenderbufferSize || descriptor.Height > Capabilities.Limits.MaxRenderbufferSize))
            throw new RhiDeviceLimitException3D(Capabilities.Api, operation, $"render-target dimensions <= {Capabilities.Limits.MaxRenderbufferSize}", Capabilities);
    }

    public void InvalidateContext(string reason)
    {
        ThrowIfDisposed();
        AbortFrame();
        DeferredLifetime.ClearWithoutRelease();
        PipelineCache.Clear(releaseResources: false);
        Resources.InvalidateContext();
        Queue.InvalidateContext();
        FrameResources.InvalidateContext();
        _lastFence = default;
        _lastGpuFrameMilliseconds = double.NaN;
        EngineLog3D.Warning("RHI", $"GPU context invalidated; generation={Resources.ContextGeneration}; reason={reason}");
    }

    public void ApplyStats(RenderStats stats)
    {
        if (stats is null) throw new ArgumentNullException(nameof(stats));
        var snapshot = Resources.CaptureSnapshot();
        stats.RhiBackend = Capabilities.ApiName;
        stats.RhiAdapterName = Capabilities.AdapterName;
        stats.RhiApiVersion = Capabilities.ApiVersion;
        stats.RhiFeatures = Capabilities.FeatureSummary;
        stats.RhiLimits = Capabilities.LimitsSummary;
        stats.RhiResourceCount = snapshot.LiveCount;
        stats.RhiBufferCount = snapshot.BufferCount;
        stats.RhiTextureCount = snapshot.TextureCount;
        stats.RhiOwnershipReferences = snapshot.OwnershipReferences;
        stats.RhiResidentBytes = snapshot.ResidentBytes;
        stats.RhiTextureBytes = snapshot.TextureBytes;
        stats.RhiResidentBudgetBytes = snapshot.MaxResidentBytes;
        stats.RhiTextureBudgetBytes = snapshot.MaxTextureBytes;
        stats.RhiPeakResidentBytes = snapshot.PeakResidentBytes;
        stats.RhiResourceCreates = snapshot.Creates;
        stats.RhiResourceUpdates = snapshot.Updates;
        stats.RhiResourceReleases = snapshot.Releases;
        stats.RhiContextGeneration = snapshot.ContextGeneration;
        stats.RhiValidationCount = _validationCount;
        stats.RhiCapabilityProfile = Profile.ToString();
        stats.RhiQueueSubmissionCount = Queue.SubmissionCount;
        stats.RhiQueueCommandCount = Queue.ExecutedCommandCount;
        stats.RhiCompletedSubmissionId = Queue.CompletedSubmissionId;
        stats.RhiFrameResourceSlot = _currentFrame.Slot;
        stats.RhiBufferedFrameCount = FrameResources.BufferedFrameCount;
        var uploadRing = _currentFrame.UploadRing;
        stats.RhiUploadRingCapacity = uploadRing?.Capacity ?? 0;
        stats.RhiUploadRingUsed = uploadRing?.Used ?? 0;
        stats.RhiUploadRingPeakUsed = uploadRing?.PeakUsed ?? 0;
        stats.RhiPipelineCacheCount = PipelineCache.Count;
        stats.RhiPipelineCacheHits = PipelineCache.Hits;
        stats.RhiPipelineCacheMisses = PipelineCache.Misses;
        stats.RhiDeferredReleaseCount = DeferredLifetime.PendingCount;
        stats.GpuTimingAvailable = GpuTimingSupported && !double.IsNaN(_lastGpuFrameMilliseconds);
        stats.GpuFrameMilliseconds = _lastGpuFrameMilliseconds;
    }

    public void Dispose()
    {
        if (_disposed) return;
        AbortFrame();
        DeferredLifetime.DrainAll();
        PipelineCache.Clear(releaseResources: true);
        var snapshot = Resources.CaptureSnapshot();
        if (snapshot.LiveCount != 0)
            EngineLog3D.Warning("RHI", $"Device disposed with {snapshot.LiveCount} live logical GPU resources ({snapshot.ResidentBytes} bytes); invalidating them.");
        Resources.Dispose();
        _disposed = true;
        EngineLog3D.Info("RHI", $"Device disposed after {FrameIndex} frames, {Queue.SubmissionCount} queue submissions and {_validationCount} validations.");
    }

    private void ValidateRenderPipeline(RhiRenderPipelineDescriptor3D descriptor)
    {
        if (descriptor is null) throw new ArgumentNullException(nameof(descriptor));
        Resources.RequireKind(descriptor.Layout, RhiResourceKind3D.PipelineLayout, descriptor.Label);
        Resources.RequireKind(descriptor.VertexShader, RhiResourceKind3D.ShaderModule, descriptor.Label);
        Resources.RequireKind(descriptor.FragmentShader, RhiResourceKind3D.ShaderModule, descriptor.Label);
        var layout = Resources.GetDescriptor<RhiPipelineLayoutDescriptor3D>(descriptor.Layout, descriptor.Label);
        var vertex = Resources.GetDescriptor<RhiShaderModuleDescriptor3D>(descriptor.VertexShader, descriptor.Label);
        var fragment = Resources.GetDescriptor<RhiShaderModuleDescriptor3D>(descriptor.FragmentShader, descriptor.Label);
        ValidateShaderReflection(vertex.Reflection, RhiShaderStage3D.Vertex, layout, descriptor.Label);
        ValidateShaderReflection(fragment.Reflection, RhiShaderStage3D.Fragment, layout, descriptor.Label);
        var vertexAttributes = 0;
        var vertexBuffers = descriptor.VertexBuffers;
        for (var i = 0; i < vertexBuffers.Length; i++)
        {
            vertexAttributes = checked(vertexAttributes + vertexBuffers[i].Attributes.Length);
            if (vertexBuffers[i].ArrayStride > 2048)
                throw new InvalidOperationException($"Render pipeline '{descriptor.Label}' declares a vertex stride above the portable 2048-byte limit.");
        }
        if (Capabilities.Limits.MaxVertexAttributes > 0 && vertexAttributes > Capabilities.Limits.MaxVertexAttributes)
            throw new RhiDeviceLimitException3D(Capabilities.Api, descriptor.Label, $"vertex attributes <= {Capabilities.Limits.MaxVertexAttributes}", Capabilities);
        if (descriptor.SampleCount > Capabilities.Limits.MaxSamples)
            throw new RhiDeviceLimitException3D(Capabilities.Api, descriptor.Label, $"samples <= {Capabilities.Limits.MaxSamples}", Capabilities);
    }

    private void ValidateShaderReflection(RhiShaderReflection3D reflection, RhiShaderStage3D stage, RhiPipelineLayoutDescriptor3D pipelineLayout, string operation)
    {
        var bindings = reflection.Bindings;
        var groupLayouts = pipelineLayout.BindGroupLayouts;
        for (var i = 0; i < bindings.Length; i++)
        {
            var binding = bindings[i];
            if ((binding.Visibility & stage) == 0) continue;
            if ((uint)binding.Group >= (uint)groupLayouts.Length)
                throw new InvalidOperationException($"Shader binding {binding.Group}:{binding.Binding} in '{operation}' exceeds the pipeline layout.");
            var layout = Resources.GetDescriptor<RhiBindGroupLayoutDescriptor3D>(groupLayouts[binding.Group], operation);
            var entries = layout.Entries;
            var found = false;
            for (var j = 0; j < entries.Length; j++)
            {
                if (entries[j].Binding != binding.Binding) continue;
                found = true;
                if (entries[j].Type != binding.Type || (entries[j].Visibility & stage) == 0 || entries[j].MinimumByteSize < binding.MinimumByteSize)
                    throw new InvalidOperationException($"Shader binding {binding.Group}:{binding.Binding} is incompatible with pipeline layout '{layout.Label}'.");
                break;
            }
            if (!found) throw new InvalidOperationException($"Shader binding {binding.Group}:{binding.Binding} is absent from pipeline layout '{layout.Label}'.");
        }
    }

    private void ValidateBindGroup(RhiBindGroupDescriptor3D descriptor, RhiBindGroupLayoutDescriptor3D layout, string operation)
    {
        var entries = descriptor.Entries;
        var expected = layout.Entries;
        if (entries.Length != expected.Length)
            throw new InvalidOperationException($"Bind group '{descriptor.Label}' has {entries.Length} entries, but its layout requires {expected.Length}.");
        for (var i = 0; i < expected.Length; i++)
        {
            if (entries[i].Binding != expected[i].Binding)
                throw new InvalidOperationException($"Bind group '{descriptor.Label}' is missing binding {expected[i].Binding}.");
            ValidateBindingResource(entries[i], expected[i], operation);
        }
    }

    private void ValidateBindingResource(in RhiBindGroupEntry3D entry, in RhiBindGroupLayoutEntry3D layout, string operation)
    {
        var expectedKind = layout.Type switch
        {
            RhiBindingType3D.UniformBuffer or RhiBindingType3D.ReadOnlyStorageBuffer or RhiBindingType3D.StorageBuffer => RhiResourceKind3D.Buffer,
            RhiBindingType3D.SampledTexture or RhiBindingType3D.StorageTexture => RhiResourceKind3D.Texture,
            RhiBindingType3D.Sampler or RhiBindingType3D.ComparisonSampler => RhiResourceKind3D.Sampler,
            _ => throw new ArgumentOutOfRangeException(nameof(layout))
        };
        Resources.RequireKind(entry.Resource, expectedKind, operation);
        if (expectedKind == RhiResourceKind3D.Buffer)
        {
            var descriptor = Resources.GetDescriptor<RhiBufferDescriptor3D>(entry.Resource, operation);
            var requiredUsage = layout.Type switch
            {
                RhiBindingType3D.UniformBuffer => RhiBufferUsage3D.Uniform,
                RhiBindingType3D.ReadOnlyStorageBuffer or RhiBindingType3D.StorageBuffer => RhiBufferUsage3D.Storage,
                _ => RhiBufferUsage3D.None
            };
            if ((descriptor.Usage & requiredUsage) != requiredUsage)
                throw new InvalidOperationException($"Buffer binding {entry.Binding} does not declare required usage {requiredUsage}.");
            var size = Resources.GetByteSize(entry.Resource);
            var bindingSize = entry.ByteSize == 0 ? size - entry.Offset : entry.ByteSize;
            if (entry.Offset > size || bindingSize < layout.MinimumByteSize || checked(entry.Offset + bindingSize) > size)
                throw new InvalidOperationException($"Buffer binding {entry.Binding} does not satisfy the layout byte range.");
        }
        else if (expectedKind == RhiResourceKind3D.Texture)
        {
            var descriptor = Resources.GetDescriptor<RhiTextureDescriptor3D>(entry.Resource, operation);
            var requiredUsage = layout.Type == RhiBindingType3D.StorageTexture ? RhiTextureUsage3D.Storage : RhiTextureUsage3D.Sampled;
            if ((descriptor.Usage & requiredUsage) != requiredUsage)
                throw new InvalidOperationException($"Texture binding {entry.Binding} does not declare required usage {requiredUsage}.");
        }
        else if (expectedKind == RhiResourceKind3D.Sampler)
        {
            var descriptor = Resources.GetDescriptor<RhiSamplerDescriptor3D>(entry.Resource, operation);
            if (layout.Type == RhiBindingType3D.ComparisonSampler && descriptor.Compare is null)
                throw new InvalidOperationException($"Sampler binding {entry.Binding} requires a comparison sampler.");
            if (layout.Type == RhiBindingType3D.Sampler && descriptor.Compare is not null)
                throw new InvalidOperationException($"Sampler binding {entry.Binding} requires a non-comparison sampler.");
        }
    }

    private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(RhiDevice3D)); }

    private static void ValidateRequiredLimits(RhiDeviceCapabilities3D capabilities)
    {
        var limits = capabilities.Limits;
        if (limits.MaxTextureSize < 1 || limits.MaxRenderbufferSize < 1 || limits.MaxCombinedTextureUnits < 7 || limits.MaxVertexAttributes < 13)
            throw new RhiDeviceLimitException3D(capabilities.Api, "RHI device-limit validation", "MaxTextureSize >= 1, MaxRenderbufferSize >= 1, MaxCombinedTextureUnits >= 7 and MaxVertexAttributes >= 13", capabilities);
        if (capabilities.Supports(RhiFeature3D.VertexTextureFetch) && limits.MaxVertexTextureUnits < 1)
            throw new RhiDeviceLimitException3D(capabilities.Api, "vertex-texture-fetch limit validation", "MaxVertexTextureUnits >= 1", capabilities);
    }
}
