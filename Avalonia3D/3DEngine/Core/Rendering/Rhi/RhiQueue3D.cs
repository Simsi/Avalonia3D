using System;

namespace ThreeDEngine.Core.Rendering.Rhi;

/// <summary>
/// Ordered submission queue for backend-neutral command buffers. Current OpenGL/WebGL adapters
/// complete synchronously because their host APIs expose a single immediate queue; the fence and
/// serial contract remains identical to explicit asynchronous backends.
/// </summary>
internal sealed class RhiQueue3D
{
    private readonly RhiDeviceCapabilities3D _capabilities;
    private readonly RhiResourceRegistry3D _resources;
    private ulong _nextSubmissionId;
    private ulong _completedSubmissionId;
    private long _submissionCount;
    private long _executedCommandCount;
    private bool _submitting;

    public RhiQueue3D(RhiDeviceCapabilities3D capabilities, RhiResourceRegistry3D resources)
    {
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
    }

    public ulong CompletedSubmissionId => _completedSubmissionId;
    public long SubmissionCount => _submissionCount;
    public long ExecutedCommandCount => _executedCommandCount;
    public bool IsSubmitting => _submitting;

    public RhiFence3D Submit(RhiCommandBuffer3D commandBuffer, IRhiCommandExecutor3D executor)
    {
        if (commandBuffer is null) throw new ArgumentNullException(nameof(commandBuffer));
        if (executor is null) throw new ArgumentNullException(nameof(executor));
        if (_submitting) throw new InvalidOperationException("Nested RHI queue submission is not permitted.");
        if (commandBuffer.Count == 0) throw new InvalidOperationException("An empty RHI command buffer cannot be submitted.");
        if (commandBuffer.WasSubmitted) throw new InvalidOperationException("RHI command buffers are single-submit.");
        _capabilities.Require(commandBuffer.RequiredFeatures, commandBuffer.Label);

        var submissionId = ++_nextSubmissionId;
        if (submissionId == 0) throw new InvalidOperationException("RHI queue submission id space was exhausted.");
        commandBuffer.MarkSubmitted();
        _submitting = true;
        try
        {
            for (var i = 0; i < commandBuffer.Count; i++)
            {
                var command = commandBuffer.GetCommand(i);
                ValidateCommand(command);
                Execute(command, executor);
                _executedCommandCount++;
            }
            executor.CompleteSubmission(submissionId);
            _completedSubmissionId = submissionId;
            _submissionCount++;
            return new RhiFence3D(_resources.ContextGeneration, submissionId);
        }
        finally
        {
            _submitting = false;
        }
    }

    public bool IsComplete(RhiFence3D fence)
    {
        if (!fence.IsValid) return false;
        if (fence.DeviceGeneration != _resources.ContextGeneration) return false;
        return fence.SubmissionId <= _completedSubmissionId;
    }

    public void RequireComplete(RhiFence3D fence, string operation)
    {
        if (!fence.IsValid) throw new ArgumentException("RHI fence is invalid.", nameof(fence));
        if (fence.DeviceGeneration != _resources.ContextGeneration)
            throw new InvalidOperationException($"RHI fence {fence} is stale during '{operation}'. Active generation is {_resources.ContextGeneration}.");
        if (fence.SubmissionId > _completedSubmissionId)
            throw new InvalidOperationException($"RHI fence {fence} has not completed during '{operation}'. Explicit blocking waits are not available on this backend.");
    }

    internal void InvalidateContext()
    {
        _nextSubmissionId = 0;
        _completedSubmissionId = 0;
        _submitting = false;
    }

    private void ValidateCommand(in RhiCommand3D command)
    {
        switch (command.Kind)
        {
            case RhiCommandKind3D.BeginRenderPass:
                if (command.RenderPass.ColorTarget.IsValid)
                    _resources.RequireKind(command.RenderPass.ColorTarget, RhiResourceKind3D.Texture, command.Kind.ToString());
                if (command.RenderPass.DepthTarget.IsValid)
                    _resources.RequireKind(command.RenderPass.DepthTarget, RhiResourceKind3D.Texture, command.Kind.ToString());
                break;
            case RhiCommandKind3D.SetRenderPipeline:
            case RhiCommandKind3D.SetComputePipeline:
                _resources.RequireKind(command.Resource0, RhiResourceKind3D.Pipeline, command.Kind.ToString());
                break;
            case RhiCommandKind3D.SetBindGroup:
                _resources.RequireKind(command.Resource0, RhiResourceKind3D.BindGroup, command.Kind.ToString());
                break;
            case RhiCommandKind3D.SetVertexBuffer:
            case RhiCommandKind3D.SetIndexBuffer:
            case RhiCommandKind3D.DrawIndirect:
            case RhiCommandKind3D.DrawIndexedIndirect:
            case RhiCommandKind3D.MultiDrawIndexedIndirect:
            case RhiCommandKind3D.DispatchIndirect:
            case RhiCommandKind3D.WriteBuffer:
            case RhiCommandKind3D.ClearBuffer:
                _resources.RequireKind(command.Resource0, RhiResourceKind3D.Buffer, command.Kind.ToString());
                break;
            case RhiCommandKind3D.CopyBuffer:
                _resources.RequireKind(command.Resource0, RhiResourceKind3D.Buffer, command.Kind.ToString());
                _resources.RequireKind(command.Resource1, RhiResourceKind3D.Buffer, command.Kind.ToString());
                break;
            case RhiCommandKind3D.CopyBufferToTexture:
                _resources.RequireKind(command.Resource0, RhiResourceKind3D.Buffer, command.Kind.ToString());
                _resources.RequireKind(command.Resource1, RhiResourceKind3D.Texture, command.Kind.ToString());
                break;
            case RhiCommandKind3D.Barrier:
                _resources.RequireLive(command.Resource0, command.Kind.ToString());
                break;
        }
    }

    private static void Execute(in RhiCommand3D command, IRhiCommandExecutor3D executor)
    {
        switch (command.Kind)
        {
            case RhiCommandKind3D.PushDebugGroup: executor.PushDebugGroup(command.Label ?? "rhi"); break;
            case RhiCommandKind3D.PopDebugGroup: executor.PopDebugGroup(); break;
            case RhiCommandKind3D.BeginRenderPass: executor.BeginRenderPass(command.RenderPass); break;
            case RhiCommandKind3D.EndRenderPass: executor.EndRenderPass(); break;
            case RhiCommandKind3D.BeginComputePass: executor.BeginComputePass(command.ComputePass); break;
            case RhiCommandKind3D.EndComputePass: executor.EndComputePass(); break;
            case RhiCommandKind3D.SetRenderPipeline: executor.SetRenderPipeline(command.Resource0); break;
            case RhiCommandKind3D.SetComputePipeline: executor.SetComputePipeline(command.Resource0); break;
            case RhiCommandKind3D.SetBindGroup: executor.SetBindGroup(command.Value0, command.Resource0); break;
            case RhiCommandKind3D.SetVertexBuffer: executor.SetVertexBuffer(command.Value0, command.Resource0, command.Offset0); break;
            case RhiCommandKind3D.SetIndexBuffer: executor.SetIndexBuffer(command.Resource0, command.Offset0); break;
            case RhiCommandKind3D.Draw: executor.Draw(command.Value0, command.Value1, command.Value2, command.Value3); break;
            case RhiCommandKind3D.DrawIndexed: executor.DrawIndexed(command.Value0, command.Value1, command.Value2, command.Value3); break;
            case RhiCommandKind3D.DrawIndirect: executor.DrawIndirect(command.Resource0, command.Offset0); break;
            case RhiCommandKind3D.DrawIndexedIndirect: executor.DrawIndexedIndirect(command.Resource0, command.Offset0); break;
            case RhiCommandKind3D.MultiDrawIndexedIndirect: executor.MultiDrawIndexedIndirect(command.Resource0, command.Offset0, command.Value0, command.Value1); break;
            case RhiCommandKind3D.Dispatch: executor.Dispatch(command.Value0, command.Value1, command.Value2); break;
            case RhiCommandKind3D.DispatchIndirect: executor.DispatchIndirect(command.Resource0, command.Offset0); break;
            case RhiCommandKind3D.CopyBuffer: executor.CopyBuffer(command.Resource0, command.Offset0, command.Resource1, command.Offset1, command.ByteCount); break;
            case RhiCommandKind3D.CopyBufferToTexture: executor.CopyBufferToTexture(command.Resource0, command.Offset0, command.Resource1, command.ByteCount); break;
            case RhiCommandKind3D.WriteBuffer: executor.WriteBuffer(command.Resource0, command.Offset0, command.Payload); break;
            case RhiCommandKind3D.ClearBuffer: executor.ClearBuffer(command.Resource0, command.Offset0, command.ByteCount); break;
            case RhiCommandKind3D.Barrier: executor.Barrier(command.Barrier); break;
            case RhiCommandKind3D.ExecuteBackendStage: executor.ExecuteBackendStage(command.BackendStage, command.Value0, command.Value1); break;
            default: throw new ArgumentOutOfRangeException(nameof(command), command.Kind, "Unknown RHI command kind.");
        }
    }
}
