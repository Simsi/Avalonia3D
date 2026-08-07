using System;

namespace ThreeDEngine.Core.Rendering.Rhi;

internal enum RhiBackendStage3D
{
    PrepareResources = 0,
    Background = 1,
    ForwardScene = 2,
    SurfaceOverlays = 3,
    ControlPlanes = 4,
    PostProcess = 5,
    Present = 6
}

internal enum RhiCommandKind3D
{
    PushDebugGroup = 0,
    PopDebugGroup = 1,
    BeginRenderPass = 2,
    EndRenderPass = 3,
    BeginComputePass = 4,
    EndComputePass = 5,
    SetRenderPipeline = 6,
    SetComputePipeline = 7,
    SetBindGroup = 8,
    SetVertexBuffer = 9,
    SetIndexBuffer = 10,
    Draw = 11,
    DrawIndexed = 12,
    Dispatch = 13,
    CopyBuffer = 14,
    CopyBufferToTexture = 15,
    Barrier = 16,
    ExecuteBackendStage = 17,
    WriteBuffer = 18,
    ClearBuffer = 19,
    DrawIndirect = 20,
    DrawIndexedIndirect = 21,
    MultiDrawIndexedIndirect = 22,
    DispatchIndirect = 23
}

internal enum RhiLoadOperation3D
{
    Load = 0,
    Clear = 1,
    Discard = 2
}

internal enum RhiStoreOperation3D
{
    Store = 0,
    Discard = 1
}

internal readonly struct RhiRenderPassDescriptor3D
{
    public RhiRenderPassDescriptor3D(
        string label,
        RhiPassKind3D kind,
        RhiLoadOperation3D colorLoad = RhiLoadOperation3D.Load,
        RhiStoreOperation3D colorStore = RhiStoreOperation3D.Store,
        RhiLoadOperation3D depthLoad = RhiLoadOperation3D.Load,
        RhiStoreOperation3D depthStore = RhiStoreOperation3D.Store,
        RhiResourceHandle3D colorTarget = default,
        RhiResourceHandle3D depthTarget = default)
    {
        Label = string.IsNullOrWhiteSpace(label) ? throw new ArgumentException("Render-pass label cannot be empty.", nameof(label)) : label;
        Kind = kind;
        ColorLoad = colorLoad;
        ColorStore = colorStore;
        DepthLoad = depthLoad;
        DepthStore = depthStore;
        if (colorTarget.IsValid && colorTarget.Kind != RhiResourceKind3D.Texture)
            throw new ArgumentException("Color target must be a texture handle.", nameof(colorTarget));
        if (depthTarget.IsValid && depthTarget.Kind != RhiResourceKind3D.Texture)
            throw new ArgumentException("Depth target must be a texture handle.", nameof(depthTarget));
        ColorTarget = colorTarget;
        DepthTarget = depthTarget;
    }

    public string Label { get; }
    public RhiPassKind3D Kind { get; }
    public RhiLoadOperation3D ColorLoad { get; }
    public RhiStoreOperation3D ColorStore { get; }
    public RhiLoadOperation3D DepthLoad { get; }
    public RhiStoreOperation3D DepthStore { get; }
    public RhiResourceHandle3D ColorTarget { get; }
    public RhiResourceHandle3D DepthTarget { get; }
}

internal readonly struct RhiComputePassDescriptor3D
{
    public RhiComputePassDescriptor3D(string label)
    {
        Label = string.IsNullOrWhiteSpace(label) ? throw new ArgumentException("Compute-pass label cannot be empty.", nameof(label)) : label;
    }

    public string Label { get; }
}

[Flags]
internal enum RhiPipelineStage3D
{
    None = 0,
    Copy = 1 << 0,
    Vertex = 1 << 1,
    Fragment = 1 << 2,
    Compute = 1 << 3,
    Indirect = 1 << 4,
    AllGraphics = Vertex | Fragment,
    All = Copy | Vertex | Fragment | Compute | Indirect
}

[Flags]
internal enum RhiResourceAccess3D
{
    None = 0,
    CopyRead = 1 << 0,
    CopyWrite = 1 << 1,
    VertexRead = 1 << 2,
    IndexRead = 1 << 3,
    UniformRead = 1 << 4,
    ShaderRead = 1 << 5,
    ShaderWrite = 1 << 6,
    IndirectRead = 1 << 7,
    RenderTargetWrite = 1 << 8,
    DepthStencilWrite = 1 << 9
}

internal readonly struct RhiResourceBarrier3D
{
    public RhiResourceBarrier3D(
        RhiResourceHandle3D resource,
        RhiPipelineStage3D beforeStage,
        RhiResourceAccess3D beforeAccess,
        RhiPipelineStage3D afterStage,
        RhiResourceAccess3D afterAccess)
    {
        if (!resource.IsValid) throw new ArgumentException("Barrier resource handle is invalid.", nameof(resource));
        if (beforeStage == RhiPipelineStage3D.None || afterStage == RhiPipelineStage3D.None)
            throw new ArgumentOutOfRangeException(nameof(beforeStage), "Barrier stages cannot be empty.");
        Resource = resource;
        BeforeStage = beforeStage;
        BeforeAccess = beforeAccess;
        AfterStage = afterStage;
        AfterAccess = afterAccess;
    }

    public RhiResourceHandle3D Resource { get; }
    public RhiPipelineStage3D BeforeStage { get; }
    public RhiResourceAccess3D BeforeAccess { get; }
    public RhiPipelineStage3D AfterStage { get; }
    public RhiResourceAccess3D AfterAccess { get; }
}

internal readonly struct RhiFence3D : IEquatable<RhiFence3D>
{
    internal RhiFence3D(uint deviceGeneration, ulong submissionId)
    {
        DeviceGeneration = deviceGeneration;
        SubmissionId = submissionId;
    }

    public uint DeviceGeneration { get; }
    public ulong SubmissionId { get; }
    public bool IsValid => DeviceGeneration != 0 && SubmissionId != 0;

    public bool Equals(RhiFence3D other) => DeviceGeneration == other.DeviceGeneration && SubmissionId == other.SubmissionId;
    public override bool Equals(object? obj) => obj is RhiFence3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(DeviceGeneration, SubmissionId);
    public override string ToString() => IsValid ? $"fence:{SubmissionId}@{DeviceGeneration}" : "invalid";
}

internal readonly struct RhiCommand3D
{
    private RhiCommand3D(
        RhiCommandKind3D kind,
        string? label,
        RhiRenderPassDescriptor3D renderPass,
        RhiComputePassDescriptor3D computePass,
        RhiBackendStage3D backendStage,
        RhiResourceHandle3D resource0,
        RhiResourceHandle3D resource1,
        RhiResourceBarrier3D barrier,
        long offset0,
        long offset1,
        long byteCount,
        int value0,
        int value1,
        int value2,
        int value3)
    {
        Kind = kind;
        Label = label;
        RenderPass = renderPass;
        ComputePass = computePass;
        BackendStage = backendStage;
        Resource0 = resource0;
        Resource1 = resource1;
        Barrier = barrier;
        Offset0 = offset0;
        Offset1 = offset1;
        ByteCount = byteCount;
        Value0 = value0;
        Value1 = value1;
        Value2 = value2;
        Value3 = value3;
        Payload = default;
    }

    private RhiCommand3D(
        RhiCommandKind3D kind,
        RhiResourceHandle3D resource,
        long offset,
        long byteCount,
        int value0,
        int value1,
        ReadOnlyMemory<byte> payload)
    {
        Kind = kind;
        Label = null;
        RenderPass = default;
        ComputePass = default;
        BackendStage = default;
        Resource0 = resource;
        Resource1 = default;
        Barrier = default;
        Offset0 = offset;
        Offset1 = 0;
        ByteCount = byteCount;
        Value0 = value0;
        Value1 = value1;
        Value2 = 0;
        Value3 = 0;
        Payload = payload;
    }

    public RhiCommandKind3D Kind { get; }
    public string? Label { get; }
    public RhiRenderPassDescriptor3D RenderPass { get; }
    public RhiComputePassDescriptor3D ComputePass { get; }
    public RhiBackendStage3D BackendStage { get; }
    public RhiResourceHandle3D Resource0 { get; }
    public RhiResourceHandle3D Resource1 { get; }
    public RhiResourceBarrier3D Barrier { get; }
    public long Offset0 { get; }
    public long Offset1 { get; }
    public long ByteCount { get; }
    public int Value0 { get; }
    public int Value1 { get; }
    public int Value2 { get; }
    public int Value3 { get; }
    public ReadOnlyMemory<byte> Payload { get; }

    public static RhiCommand3D Debug(RhiCommandKind3D kind, string? label = null)
        => new(kind, label, default, default, default, default, default, default, 0, 0, 0, 0, 0, 0, 0);

    public static RhiCommand3D BeginRenderPass(RhiRenderPassDescriptor3D descriptor)
        => new(RhiCommandKind3D.BeginRenderPass, descriptor.Label, descriptor, default, default, default, default, default, 0, 0, 0, 0, 0, 0, 0);

    public static RhiCommand3D BeginComputePass(RhiComputePassDescriptor3D descriptor)
        => new(RhiCommandKind3D.BeginComputePass, descriptor.Label, default, descriptor, default, default, default, default, 0, 0, 0, 0, 0, 0, 0);

    public static RhiCommand3D Resource(RhiCommandKind3D kind, RhiResourceHandle3D resource, int slot = 0, long offset = 0)
        => new(kind, null, default, default, default, resource, default, default, offset, 0, 0, slot, 0, 0, 0);

    public static RhiCommand3D Draw(RhiCommandKind3D kind, int count, int instanceCount, int first, int firstInstance)
        => new(kind, null, default, default, default, default, default, default, 0, 0, 0, count, instanceCount, first, firstInstance);

    public static RhiCommand3D Dispatch(int x, int y, int z)
        => new(RhiCommandKind3D.Dispatch, null, default, default, default, default, default, default, 0, 0, 0, x, y, z, 0);

    public static RhiCommand3D Copy(RhiCommandKind3D kind, RhiResourceHandle3D source, long sourceOffset, RhiResourceHandle3D destination, long destinationOffset, long byteCount)
        => new(kind, null, default, default, default, source, destination, default, sourceOffset, destinationOffset, byteCount, 0, 0, 0, 0);

    public static RhiCommand3D BarrierCommand(RhiResourceBarrier3D barrier)
        => new(RhiCommandKind3D.Barrier, null, default, default, default, barrier.Resource, default, barrier, 0, 0, 0, 0, 0, 0, 0);

    public static RhiCommand3D BackendStageCommand(RhiBackendStage3D stage, int firstCommand, int commandCount)
        => new(RhiCommandKind3D.ExecuteBackendStage, null, default, default, stage, default, default, default, 0, 0, 0, firstCommand, commandCount, 0, 0);

    public static RhiCommand3D WriteBufferCommand(RhiResourceHandle3D destination, long destinationOffset, ReadOnlyMemory<byte> data)
        => new(RhiCommandKind3D.WriteBuffer, destination, destinationOffset, data.Length, 0, 0, data);

    public static RhiCommand3D ClearBufferCommand(RhiResourceHandle3D destination, long destinationOffset, long byteCount)
        => new(RhiCommandKind3D.ClearBuffer, destination, destinationOffset, byteCount, 0, 0, default);

    public static RhiCommand3D Indirect(RhiCommandKind3D kind, RhiResourceHandle3D buffer, long offset, int drawCount = 1, int stride = 0)
        => new(kind, buffer, offset, 0, drawCount, stride, default);
}

internal interface IRhiCommandExecutor3D
{
    void PushDebugGroup(string label);
    void PopDebugGroup();
    void BeginRenderPass(in RhiRenderPassDescriptor3D descriptor);
    void EndRenderPass();
    void BeginComputePass(in RhiComputePassDescriptor3D descriptor);
    void EndComputePass();
    void SetRenderPipeline(RhiResourceHandle3D pipeline);
    void SetComputePipeline(RhiResourceHandle3D pipeline);
    void SetBindGroup(int slot, RhiResourceHandle3D bindGroup);
    void SetVertexBuffer(int slot, RhiResourceHandle3D buffer, long offset);
    void SetIndexBuffer(RhiResourceHandle3D buffer, long offset);
    void Draw(int vertexCount, int instanceCount, int firstVertex, int firstInstance);
    void DrawIndexed(int indexCount, int instanceCount, int firstIndex, int firstInstance);
    void DrawIndirect(RhiResourceHandle3D indirectBuffer, long offset);
    void DrawIndexedIndirect(RhiResourceHandle3D indirectBuffer, long offset);
    void MultiDrawIndexedIndirect(RhiResourceHandle3D indirectBuffer, long offset, int drawCount, int stride);
    void Dispatch(int x, int y, int z);
    void DispatchIndirect(RhiResourceHandle3D indirectBuffer, long offset);
    void CopyBuffer(RhiResourceHandle3D source, long sourceOffset, RhiResourceHandle3D destination, long destinationOffset, long byteCount);
    void CopyBufferToTexture(RhiResourceHandle3D source, long sourceOffset, RhiResourceHandle3D destination, long byteCount);
    void WriteBuffer(RhiResourceHandle3D destination, long destinationOffset, ReadOnlyMemory<byte> data);
    void ClearBuffer(RhiResourceHandle3D destination, long destinationOffset, long byteCount);
    void Barrier(in RhiResourceBarrier3D barrier);
    void ExecuteBackendStage(RhiBackendStage3D stage, int firstCommand, int commandCount);
    void CompleteSubmission(ulong submissionId);
}

internal sealed class RhiCommandBuffer3D : IDisposable
{
    private RhiCommandEncoder3D? _owner;
    private RhiCommand3D[]? _commands;

    internal void Activate(RhiCommandEncoder3D owner, RhiCommand3D[] commands, int count, string label, RhiFeature3D requiredFeatures)
    {
        if (_owner is not null) throw new InvalidOperationException("RHI command buffer is already active.");
        _owner = owner;
        _commands = commands;
        Count = count;
        Label = label;
        RequiredFeatures = requiredFeatures;
        WasSubmitted = false;
    }

    public int Count { get; private set; }
    public string Label { get; private set; } = string.Empty;
    public RhiFeature3D RequiredFeatures { get; private set; }
    public bool WasSubmitted { get; private set; }

    internal RhiCommand3D GetCommand(int index)
    {
        if (_commands is null || (uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        return _commands[index];
    }

    internal void MarkSubmitted()
    {
        if (_owner is null) throw new ObjectDisposedException(nameof(RhiCommandBuffer3D));
        if (WasSubmitted) throw new InvalidOperationException("RHI command buffers are single-submit.");
        WasSubmitted = true;
    }

    public void Dispose()
    {
        var owner = _owner;
        if (owner is null) return;
        _owner = null;
        _commands = null;
        Count = 0;
        RequiredFeatures = RhiFeature3D.None;
        Label = string.Empty;
        WasSubmitted = false;
        owner.ReleaseFinishedBuffer();
    }
}

internal sealed class RhiCommandEncoder3D
{
    private readonly RhiDeviceCapabilities3D _capabilities;
    private readonly RhiResourceRegistry3D _resources;
    private readonly RhiCommandBuffer3D _buffer = new();
    private readonly RhiRenderPassEncoder3D _renderPass;
    private readonly RhiComputePassEncoder3D _computePass;
    private RhiCommand3D[] _commands = new RhiCommand3D[32];
    private int _count;
    private bool _finishedBufferActive;
    private PassState _passState;
    private int _debugDepth;
    private string _label = "command-buffer";
    private RhiFeature3D _requiredFeatures;

    internal RhiCommandEncoder3D(RhiDeviceCapabilities3D capabilities, RhiResourceRegistry3D resources)
    {
        _capabilities = capabilities;
        _resources = resources;
        _renderPass = new RhiRenderPassEncoder3D(this);
        _computePass = new RhiComputePassEncoder3D(this);
    }

    public void Reset(string label)
    {
        if (_finishedBufferActive) throw new InvalidOperationException("The previous RHI command buffer must be disposed before resetting its encoder.");
        if (_passState != PassState.None) throw new InvalidOperationException("Cannot reset an RHI encoder with an open pass.");
        _count = 0;
        _debugDepth = 0;
        _requiredFeatures = RhiFeature3D.CommandBuffers;
        _label = string.IsNullOrWhiteSpace(label) ? "command-buffer" : label;
    }

    public void PushDebugGroup(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Debug-group label cannot be empty.", nameof(label));
        Add(RhiCommand3D.Debug(RhiCommandKind3D.PushDebugGroup, label));
        _debugDepth++;
    }

    public void PopDebugGroup()
    {
        if (_debugDepth <= 0) throw new InvalidOperationException("RHI debug-group stack is empty.");
        Add(RhiCommand3D.Debug(RhiCommandKind3D.PopDebugGroup));
        _debugDepth--;
    }

    public RhiRenderPassEncoder3D BeginRenderPass(RhiRenderPassDescriptor3D descriptor)
    {
        RequireNoPass();
        ValidateRenderTarget(descriptor.ColorTarget, depth: false, descriptor.Label);
        ValidateRenderTarget(descriptor.DepthTarget, depth: true, descriptor.Label);
        _passState = PassState.Render;
        _requiredFeatures |= RhiFeature3D.RenderTargets;
        Add(RhiCommand3D.BeginRenderPass(descriptor));
        _renderPass.Begin();
        return _renderPass;
    }

    public RhiComputePassEncoder3D BeginComputePass(RhiComputePassDescriptor3D descriptor)
    {
        RequireNoPass();
        _capabilities.Require(RhiFeature3D.ComputeShaders | RhiFeature3D.StorageBuffers, descriptor.Label);
        _passState = PassState.Compute;
        _requiredFeatures |= RhiFeature3D.ComputeShaders | RhiFeature3D.StorageBuffers;
        Add(RhiCommand3D.BeginComputePass(descriptor));
        _computePass.Begin();
        return _computePass;
    }

    public void CopyBuffer(RhiResourceHandle3D source, long sourceOffset, RhiResourceHandle3D destination, long destinationOffset, long byteCount)
    {
        RequireNoPass();
        ValidateBufferRange(source, sourceOffset, byteCount, "copy source");
        ValidateBufferRange(destination, destinationOffset, byteCount, "copy destination");
        _requiredFeatures |= RhiFeature3D.CopyCommands;
        Add(RhiCommand3D.Copy(RhiCommandKind3D.CopyBuffer, source, sourceOffset, destination, destinationOffset, byteCount));
    }

    public void CopyBufferToTexture(RhiResourceHandle3D source, long sourceOffset, RhiResourceHandle3D destination, long byteCount)
    {
        RequireNoPass();
        ValidateBufferRange(source, sourceOffset, byteCount, "texture-copy source");
        _resources.RequireKind(destination, RhiResourceKind3D.Texture, "texture-copy destination");
        _requiredFeatures |= RhiFeature3D.CopyCommands | RhiFeature3D.Texture2D;
        Add(RhiCommand3D.Copy(RhiCommandKind3D.CopyBufferToTexture, source, sourceOffset, destination, 0, byteCount));
    }

    public void WriteBuffer(RhiResourceHandle3D destination, long destinationOffset, ReadOnlyMemory<byte> data)
    {
        RequireNoPass();
        if (data.IsEmpty) throw new ArgumentException("RHI write-buffer payload cannot be empty.", nameof(data));
        ValidateBufferRange(destination, destinationOffset, data.Length, "write-buffer destination");
        var descriptor = _resources.GetDescriptor<RhiBufferDescriptor3D>(destination, "write-buffer destination");
        if ((descriptor.Usage & RhiBufferUsage3D.CopyDestination) == 0)
            throw new InvalidOperationException($"RHI buffer {destination} was not created with CopyDestination usage.");
        _requiredFeatures |= RhiFeature3D.CopyCommands;
        Add(RhiCommand3D.WriteBufferCommand(destination, destinationOffset, data));
    }

    public void ClearBuffer(RhiResourceHandle3D destination, long destinationOffset, long byteCount)
    {
        RequireNoPass();
        ValidateBufferRange(destination, destinationOffset, byteCount, "clear-buffer destination");
        var descriptor = _resources.GetDescriptor<RhiBufferDescriptor3D>(destination, "clear-buffer destination");
        if ((descriptor.Usage & RhiBufferUsage3D.CopyDestination) == 0)
            throw new InvalidOperationException($"RHI buffer {destination} was not created with CopyDestination usage.");
        _requiredFeatures |= RhiFeature3D.CopyCommands;
        Add(RhiCommand3D.ClearBufferCommand(destination, destinationOffset, byteCount));
    }

    public void Barrier(RhiResourceBarrier3D barrier)
    {
        RequireNoPass();
        _resources.RequireLive(barrier.Resource, "resource barrier");
        _capabilities.Require(RhiFeature3D.ExplicitBarriers, "resource barrier");
        _requiredFeatures |= RhiFeature3D.ExplicitBarriers;
        Add(RhiCommand3D.BarrierCommand(barrier));
    }

    public void ExecuteBackendStage(RhiBackendStage3D stage, int firstCommand = 0, int commandCount = 0)
    {
        if (firstCommand < 0) throw new ArgumentOutOfRangeException(nameof(firstCommand));
        if (commandCount < 0) throw new ArgumentOutOfRangeException(nameof(commandCount));
        Add(RhiCommand3D.BackendStageCommand(stage, firstCommand, commandCount));
    }

    public RhiCommandBuffer3D Finish()
    {
        if (_finishedBufferActive) throw new InvalidOperationException("This RHI encoder already has an active command buffer.");
        RequireNoPass();
        if (_debugDepth != 0) throw new InvalidOperationException("RHI command buffer ended with unbalanced debug groups.");
        _capabilities.Require(_requiredFeatures, _label);
        _finishedBufferActive = true;
        _buffer.Activate(this, _commands, _count, _label, _requiredFeatures);
        return _buffer;
    }

    internal void EndRenderPass()
    {
        if (_passState != PassState.Render) throw new InvalidOperationException("No RHI render pass is open.");
        Add(RhiCommand3D.Debug(RhiCommandKind3D.EndRenderPass));
        _passState = PassState.None;
    }

    internal void EndComputePass()
    {
        if (_passState != PassState.Compute) throw new InvalidOperationException("No RHI compute pass is open.");
        Add(RhiCommand3D.Debug(RhiCommandKind3D.EndComputePass));
        _passState = PassState.None;
    }

    internal void SetResource(RhiCommandKind3D kind, RhiResourceHandle3D resource, RhiResourceKind3D expectedKind, int slot = 0, long offset = 0)
    {
        _resources.RequireKind(resource, expectedKind, kind.ToString());
        Add(RhiCommand3D.Resource(kind, resource, slot, offset));
    }

    internal void SetBuffer(RhiCommandKind3D kind, RhiResourceHandle3D buffer, int slot, long offset)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        _resources.RequireKind(buffer, RhiResourceKind3D.Buffer, kind.ToString());
        if (_resources.GetByteSize(buffer) < offset) throw new ArgumentOutOfRangeException(nameof(offset));
        Add(RhiCommand3D.Resource(kind, buffer, slot, offset));
    }

    internal void AddDraw(RhiCommandKind3D kind, int count, int instanceCount, int first, int firstInstance)
    {
        if (count < 0 || instanceCount < 0 || first < 0 || firstInstance < 0) throw new ArgumentOutOfRangeException(nameof(count));
        Add(RhiCommand3D.Draw(kind, count, instanceCount, first, firstInstance));
    }

    internal void AddIndirect(RhiCommandKind3D kind, RhiResourceHandle3D buffer, long offset, int drawCount = 1, int stride = 0)
    {
        if (offset < 0 || (offset & 3L) != 0) throw new ArgumentOutOfRangeException(nameof(offset), "Indirect offsets must be non-negative and 4-byte aligned.");
        if (drawCount <= 0) throw new ArgumentOutOfRangeException(nameof(drawCount));
        if (stride < 0 || (stride != 0 && (stride & 3) != 0)) throw new ArgumentOutOfRangeException(nameof(stride));
        _resources.RequireKind(buffer, RhiResourceKind3D.Buffer, kind.ToString());
        var descriptor = _resources.GetDescriptor<RhiBufferDescriptor3D>(buffer, kind.ToString());
        if ((descriptor.Usage & RhiBufferUsage3D.Indirect) == 0)
            throw new InvalidOperationException($"RHI buffer {buffer} was not created with Indirect usage.");
        _requiredFeatures |= RhiFeature3D.IndirectBuffers;
        var commandSize = kind switch
        {
            RhiCommandKind3D.DrawIndirect => 16,
            RhiCommandKind3D.DrawIndexedIndirect => 20,
            RhiCommandKind3D.MultiDrawIndexedIndirect => 20,
            RhiCommandKind3D.DispatchIndirect => 12,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported indirect command kind.")
        };
        var effectiveStride = stride == 0 ? commandSize : stride;
        if (effectiveStride < commandSize)
            throw new ArgumentOutOfRangeException(nameof(stride), stride, $"Indirect stride must be at least {commandSize} bytes.");
        var requiredBytes = checked((long)(drawCount - 1) * effectiveStride + commandSize);
        if (checked(offset + requiredBytes) > descriptor.ByteSize)
            throw new ArgumentOutOfRangeException(nameof(offset), "Indirect command range exceeds the buffer allocation.");
        if (kind == RhiCommandKind3D.MultiDrawIndexedIndirect)
        {
            _capabilities.Require(RhiFeature3D.MultiDrawIndirect, "multi-draw indexed indirect");
            _requiredFeatures |= RhiFeature3D.MultiDrawIndirect;
        }
        Add(RhiCommand3D.Indirect(kind, buffer, offset, drawCount, effectiveStride));
    }

    internal void AddDispatch(int x, int y, int z)
    {
        if (x <= 0 || y <= 0 || z <= 0) throw new ArgumentOutOfRangeException(nameof(x));
        // Dispatch dimensions are workgroup counts. MaxComputeWorkgroupSize* describes the
        // shader's local workgroup size and must not be used to cap dispatch counts. Explicit
        // backends validate API-specific maxComputeWorkgroupsPerDimension at device creation.
        Add(RhiCommand3D.Dispatch(x, y, z));
    }

    internal void ReleaseFinishedBuffer() => _finishedBufferActive = false;

    private void ValidateRenderTarget(RhiResourceHandle3D handle, bool depth, string operation)
    {
        if (!handle.IsValid) return;
        _resources.RequireKind(handle, RhiResourceKind3D.Texture, operation);
        var descriptor = _resources.GetDescriptor<RhiTextureDescriptor3D>(handle, operation);
        var required = depth ? RhiTextureUsage3D.DepthStencil : RhiTextureUsage3D.RenderTarget;
        if ((descriptor.Usage & required) != required)
            throw new InvalidOperationException($"Render-pass target {handle} does not declare required usage {required}.");
        var isDepth = descriptor.Format is RhiTextureFormat3D.Depth16 or RhiTextureFormat3D.Depth24 or RhiTextureFormat3D.Depth24Stencil8 or RhiTextureFormat3D.Depth32Float;
        if (isDepth != depth)
            throw new InvalidOperationException(depth ? "Depth attachment uses a color format." : "Color attachment uses a depth format.");
    }

    private void ValidateBufferRange(RhiResourceHandle3D handle, long offset, long byteCount, string operation)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (byteCount <= 0) throw new ArgumentOutOfRangeException(nameof(byteCount));
        _resources.RequireKind(handle, RhiResourceKind3D.Buffer, operation);
        if (checked(offset + byteCount) > _resources.GetByteSize(handle))
            throw new ArgumentOutOfRangeException(nameof(byteCount), $"RHI {operation} exceeds the buffer allocation.");
    }

    private void RequireNoPass()
    {
        if (_passState != PassState.None) throw new InvalidOperationException("Operation is not valid while an RHI pass is open.");
    }

    private void Add(RhiCommand3D command)
    {
        if (_finishedBufferActive) throw new InvalidOperationException("Cannot modify an RHI encoder while its command buffer is active.");
        if (_count == _commands.Length) Array.Resize(ref _commands, checked(_commands.Length * 2));
        _commands[_count++] = command;
    }

    private enum PassState { None, Render, Compute }
}

internal sealed class RhiRenderPassEncoder3D : IDisposable
{
    private readonly RhiCommandEncoder3D _owner;
    private bool _active;

    internal RhiRenderPassEncoder3D(RhiCommandEncoder3D owner) => _owner = owner;
    internal void Begin() => _active = true;

    public void SetPipeline(RhiResourceHandle3D pipeline) { RequireActive(); _owner.SetResource(RhiCommandKind3D.SetRenderPipeline, pipeline, RhiResourceKind3D.Pipeline); }
    public void SetBindGroup(int slot, RhiResourceHandle3D bindGroup) { RequireActive(); _owner.SetResource(RhiCommandKind3D.SetBindGroup, bindGroup, RhiResourceKind3D.BindGroup, slot); }
    public void SetVertexBuffer(int slot, RhiResourceHandle3D buffer, long offset = 0) { RequireActive(); _owner.SetBuffer(RhiCommandKind3D.SetVertexBuffer, buffer, slot, offset); }
    public void SetIndexBuffer(RhiResourceHandle3D buffer, long offset = 0) { RequireActive(); _owner.SetBuffer(RhiCommandKind3D.SetIndexBuffer, buffer, 0, offset); }
    public void Draw(int vertexCount, int instanceCount = 1, int firstVertex = 0, int firstInstance = 0) { RequireActive(); _owner.AddDraw(RhiCommandKind3D.Draw, vertexCount, instanceCount, firstVertex, firstInstance); }
    public void DrawIndexed(int indexCount, int instanceCount = 1, int firstIndex = 0, int firstInstance = 0) { RequireActive(); _owner.AddDraw(RhiCommandKind3D.DrawIndexed, indexCount, instanceCount, firstIndex, firstInstance); }
    public void DrawIndirect(RhiResourceHandle3D indirectBuffer, long offset = 0) { RequireActive(); _owner.AddIndirect(RhiCommandKind3D.DrawIndirect, indirectBuffer, offset); }
    public void DrawIndexedIndirect(RhiResourceHandle3D indirectBuffer, long offset = 0) { RequireActive(); _owner.AddIndirect(RhiCommandKind3D.DrawIndexedIndirect, indirectBuffer, offset); }
    public void MultiDrawIndexedIndirect(RhiResourceHandle3D indirectBuffer, long offset, int drawCount, int stride) { RequireActive(); _owner.AddIndirect(RhiCommandKind3D.MultiDrawIndexedIndirect, indirectBuffer, offset, drawCount, stride); }
    public void ExecuteBackendStage(RhiBackendStage3D stage, int firstCommand = 0, int commandCount = 0) { RequireActive(); _owner.ExecuteBackendStage(stage, firstCommand, commandCount); }

    public void Dispose()
    {
        if (!_active) return;
        _active = false;
        _owner.EndRenderPass();
    }

    private void RequireActive()
    {
        if (!_active) throw new ObjectDisposedException(nameof(RhiRenderPassEncoder3D));
    }
}

internal sealed class RhiComputePassEncoder3D : IDisposable
{
    private readonly RhiCommandEncoder3D _owner;
    private bool _active;

    internal RhiComputePassEncoder3D(RhiCommandEncoder3D owner) => _owner = owner;
    internal void Begin() => _active = true;

    public void SetPipeline(RhiResourceHandle3D pipeline) { RequireActive(); _owner.SetResource(RhiCommandKind3D.SetComputePipeline, pipeline, RhiResourceKind3D.Pipeline); }
    public void SetBindGroup(int slot, RhiResourceHandle3D bindGroup) { RequireActive(); _owner.SetResource(RhiCommandKind3D.SetBindGroup, bindGroup, RhiResourceKind3D.BindGroup, slot); }
    public void Dispatch(int x, int y = 1, int z = 1) { RequireActive(); _owner.AddDispatch(x, y, z); }
    public void DispatchIndirect(RhiResourceHandle3D indirectBuffer, long offset = 0) { RequireActive(); _owner.AddIndirect(RhiCommandKind3D.DispatchIndirect, indirectBuffer, offset); }
    public void ExecuteBackendStage(RhiBackendStage3D stage, int firstCommand = 0, int commandCount = 0) { RequireActive(); _owner.ExecuteBackendStage(stage, firstCommand, commandCount); }

    public void Dispose()
    {
        if (!_active) return;
        _active = false;
        _owner.EndComputePass();
    }

    private void RequireActive()
    {
        if (!_active) throw new ObjectDisposedException(nameof(RhiComputePassEncoder3D));
    }
}

