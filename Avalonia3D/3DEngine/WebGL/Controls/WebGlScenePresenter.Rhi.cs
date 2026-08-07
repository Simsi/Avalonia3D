using System;
using ThreeDEngine.Avalonia.WebGL.Interop;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Rendering.Rhi;

namespace ThreeDEngine.Avalonia.WebGL.Controls;

internal sealed partial class WebGlScenePresenter
{
    private RhiDevice3D? _rhiEncoderDevice;
    private RhiCommandEncoder3D? _rhiCommandEncoder;
    private RenderStats? _rhiExecutionStats;
    private RetainedFrameState _rhiRetainedFrameState;
    private bool _rhiExecutionActive;
    private int _rhiPassDepth;

    private void ExecuteRhiFrame(SceneRenderPlan3D plan, RenderStats stats, in RetainedFrameState retainedFrameState, RhiDevice3D device)
    {
        if (_rhiExecutionActive) throw new InvalidOperationException("Nested WebGL RHI execution is not permitted.");
        if (!ReferenceEquals(_rhiEncoderDevice, device))
        {
            _rhiEncoderDevice = device;
            _rhiCommandEncoder = device.CreateCommandEncoder();
        }

        _rhiExecutionStats = stats;
        _rhiRetainedFrameState = retainedFrameState;
        _rhiExecutionActive = true;
        _rhiPassDepth = 0;
        RhiFence3D fence = default;
        var gpuTimerStarted = WebGlInterop.BeginGpuFrameTimer(_hostId);
        try
        {
            var encoder = _rhiCommandEncoder ?? throw new InvalidOperationException("WebGL RHI command encoder is unavailable.");
            encoder.Reset("webgl-scene-frame");
            plan.RhiSubmission.Encode(encoder, includeSurfaceOverlays: false, includeControlPlanes: false);
            using var commands = encoder.Finish();
            fence = device.Submit(commands, this);
            if (_rhiPassDepth != 0) throw new InvalidOperationException("WebGL RHI executor ended with an open render pass.");
        }
        catch
        {
            device.AbortFrame();
            throw;
        }
        finally
        {
            if (gpuTimerStarted) WebGlInterop.EndGpuFrameTimer(_hostId);
            _rhiExecutionStats = null;
            _rhiExecutionActive = false;
            _rhiPassDepth = 0;
        }
        var gpuMilliseconds = WebGlInterop.GetLastGpuFrameMilliseconds(_hostId);
        device.EndFrame(fence, gpuMilliseconds >= 0d ? gpuMilliseconds : double.NaN);
    }

    void IRhiCommandExecutor3D.PushDebugGroup(string label) { }
    void IRhiCommandExecutor3D.PopDebugGroup() { }
    void IRhiCommandExecutor3D.BeginRenderPass(in RhiRenderPassDescriptor3D descriptor) => _rhiPassDepth++;
    void IRhiCommandExecutor3D.EndRenderPass()
    {
        if (_rhiPassDepth <= 0) throw new InvalidOperationException("WebGL RHI pass stack underflow.");
        _rhiPassDepth--;
    }
    void IRhiCommandExecutor3D.BeginComputePass(in RhiComputePassDescriptor3D descriptor) => throw Unsupported("compute pass");
    void IRhiCommandExecutor3D.EndComputePass() => throw Unsupported("compute pass");
    void IRhiCommandExecutor3D.SetRenderPipeline(RhiResourceHandle3D pipeline) => throw Unsupported("generic render pipeline");
    void IRhiCommandExecutor3D.SetComputePipeline(RhiResourceHandle3D pipeline) => throw Unsupported("compute pipeline");
    void IRhiCommandExecutor3D.SetBindGroup(int slot, RhiResourceHandle3D bindGroup) => throw Unsupported("generic bind group");
    void IRhiCommandExecutor3D.SetVertexBuffer(int slot, RhiResourceHandle3D buffer, long offset) => throw Unsupported("generic vertex buffer binding");
    void IRhiCommandExecutor3D.SetIndexBuffer(RhiResourceHandle3D buffer, long offset) => throw Unsupported("generic index buffer binding");
    void IRhiCommandExecutor3D.Draw(int vertexCount, int instanceCount, int firstVertex, int firstInstance) => throw Unsupported("generic draw");
    void IRhiCommandExecutor3D.DrawIndexed(int indexCount, int instanceCount, int firstIndex, int firstInstance) => throw Unsupported("generic indexed draw");
    void IRhiCommandExecutor3D.DrawIndirect(RhiResourceHandle3D indirectBuffer, long offset) => throw Unsupported("indirect draw");
    void IRhiCommandExecutor3D.DrawIndexedIndirect(RhiResourceHandle3D indirectBuffer, long offset) => throw Unsupported("indexed indirect draw");
    void IRhiCommandExecutor3D.MultiDrawIndexedIndirect(RhiResourceHandle3D indirectBuffer, long offset, int drawCount, int stride) => throw Unsupported("multi-draw indexed indirect");
    void IRhiCommandExecutor3D.Dispatch(int x, int y, int z) => throw Unsupported("compute dispatch");
    void IRhiCommandExecutor3D.DispatchIndirect(RhiResourceHandle3D indirectBuffer, long offset) => throw Unsupported("indirect compute dispatch");
    void IRhiCommandExecutor3D.CopyBuffer(RhiResourceHandle3D source, long sourceOffset, RhiResourceHandle3D destination, long destinationOffset, long byteCount) => throw Unsupported("buffer copy");
    void IRhiCommandExecutor3D.CopyBufferToTexture(RhiResourceHandle3D source, long sourceOffset, RhiResourceHandle3D destination, long byteCount) => throw Unsupported("buffer-to-texture copy");
    void IRhiCommandExecutor3D.WriteBuffer(RhiResourceHandle3D destination, long destinationOffset, ReadOnlyMemory<byte> data) => throw Unsupported("write buffer");
    void IRhiCommandExecutor3D.ClearBuffer(RhiResourceHandle3D destination, long destinationOffset, long byteCount) => throw Unsupported("clear buffer");
    void IRhiCommandExecutor3D.Barrier(in RhiResourceBarrier3D barrier) => throw Unsupported("explicit barrier");

    void IRhiCommandExecutor3D.ExecuteBackendStage(RhiBackendStage3D stage, int firstCommand, int commandCount)
    {
        if (!_rhiExecutionActive) throw new InvalidOperationException("WebGL RHI executor has no active frame.");
        switch (stage)
        {
            case RhiBackendStage3D.PrepareResources:
            case RhiBackendStage3D.Background:
            case RhiBackendStage3D.SurfaceOverlays:
            case RhiBackendStage3D.ControlPlanes:
            case RhiBackendStage3D.Present:
                break;
            case RhiBackendStage3D.ForwardScene:
                var stats = _rhiExecutionStats ?? throw new InvalidOperationException("WebGL RHI executor has no active stats object.");
                RenderRetainedFrameDirect(stats, in _rhiRetainedFrameState);
                break;
            case RhiBackendStage3D.PostProcess:
                throw Unsupported("post-process stage");
            default:
                throw new ArgumentOutOfRangeException(nameof(stage));
        }
    }

    void IRhiCommandExecutor3D.CompleteSubmission(ulong submissionId) { }

    private static InvalidOperationException Unsupported(string operation)
        => new($"The legacy WebGL 2 adapter cannot execute the RHI {operation}. No CPU fallback is permitted; use a WebGPU-capable backend profile.");
}
