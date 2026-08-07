using System;
using Avalonia.OpenGL;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Rendering.Pipeline;
using ThreeDEngine.Core.Rendering.Rhi;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.OpenGL.Rendering;

internal sealed partial class OpenGlSceneRenderer
{
    private RhiDevice3D? _rhiEncoderDevice;
    private RhiCommandEncoder3D? _rhiCommandEncoder;
    private GlInterface? _rhiGl;
    private Scene3D? _rhiScene;
    private SceneRenderFrameContext3D? _rhiFrame;
    private SceneRenderPlan3D? _rhiPlan;
    private RenderStats? _rhiStats;
    private RenderPipelinePlan3D? _rhiPipeline;
    private int _rhiFramebuffer;
    private int _rhiWidth;
    private int _rhiHeight;
    private int _rhiPassDepth;

    private void ExecuteRhiFrame(
        GlInterface gl,
        int framebuffer,
        int width,
        int height,
        Scene3D scene,
        SceneRenderFrameContext3D frame,
        SceneRenderPlan3D plan,
        RenderStats stats,
        RenderPipelinePlan3D pipeline,
        RhiDevice3D device)
    {
        if (_rhiGl is not null) throw new InvalidOperationException("Nested OpenGL RHI execution is not permitted.");
        if (!ReferenceEquals(_rhiEncoderDevice, device))
        {
            _rhiEncoderDevice = device;
            _rhiCommandEncoder = device.CreateCommandEncoder();
        }

        _rhiGl = gl;
        _rhiScene = scene;
        _rhiFrame = frame;
        _rhiPlan = plan;
        _rhiStats = stats;
        _rhiPipeline = pipeline;
        _rhiFramebuffer = framebuffer;
        _rhiWidth = width;
        _rhiHeight = height;
        _rhiPassDepth = 0;

        RhiFence3D fence = default;
        BeginGpuFrameTimer();
        try
        {
            var encoder = _rhiCommandEncoder ?? throw new InvalidOperationException("OpenGL RHI command encoder is unavailable.");
            encoder.Reset("opengl-scene-frame");
            plan.RhiSubmission.Encode(
                encoder,
                includeSurfaceOverlays: scene.Debug.ShowWireframeOverlay || scene.Debug.ShowSilhouetteOverlay,
                includeControlPlanes: true);
            using var commands = encoder.Finish();
            fence = device.Submit(commands, this);
            if (_rhiPassDepth != 0) throw new InvalidOperationException("OpenGL RHI executor ended with an open render pass.");
        }
        catch
        {
            device.AbortFrame();
            throw;
        }
        finally
        {
            EndGpuFrameTimer();
            _rhiGl = null;
            _rhiScene = null;
            _rhiFrame = null;
            _rhiPlan = null;
            _rhiStats = null;
            _rhiPipeline = null;
            _rhiPassDepth = 0;
        }
        device.EndFrame(fence, _lastGpuFrameMilliseconds);
    }

    void IRhiCommandExecutor3D.PushDebugGroup(string label) { }
    void IRhiCommandExecutor3D.PopDebugGroup() { }
    void IRhiCommandExecutor3D.BeginRenderPass(in RhiRenderPassDescriptor3D descriptor) => _rhiPassDepth++;
    void IRhiCommandExecutor3D.EndRenderPass()
    {
        if (_rhiPassDepth <= 0) throw new InvalidOperationException("OpenGL RHI pass stack underflow.");
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
        var gl = _rhiGl ?? throw new InvalidOperationException("OpenGL RHI executor has no active context.");
        var scene = _rhiScene ?? throw new InvalidOperationException("OpenGL RHI executor has no active scene.");
        var frame = _rhiFrame ?? throw new InvalidOperationException("OpenGL RHI executor has no active frame.");
        var plan = _rhiPlan ?? throw new InvalidOperationException("OpenGL RHI executor has no active render plan.");
        var stats = _rhiStats ?? throw new InvalidOperationException("OpenGL RHI executor has no active stats object.");
        var pipeline = _rhiPipeline ?? throw new InvalidOperationException("OpenGL RHI executor has no active pipeline plan.");

        switch (stage)
        {
            case RhiBackendStage3D.PrepareResources:
                BuildBatches(gl, plan, stats);
                break;
            case RhiBackendStage3D.Background:
                gl.BindFramebuffer(GlFramebuffer, _rhiFramebuffer);
                gl.Viewport(0, 0, _rhiWidth, _rhiHeight);
                gl.ClearColor(scene.BackgroundColor.R, scene.BackgroundColor.G, scene.BackgroundColor.B, scene.BackgroundColor.A);
                gl.Clear(GlColorBufferBit | GlDepthBufferBit);
                DrawSkybox(gl, frame, stats);
                break;
            case RhiBackendStage3D.ForwardScene:
                DrawMeshes(gl, plan, stats, pipeline);
                break;
            case RhiBackendStage3D.SurfaceOverlays:
                DrawSurfaceOverlays(gl, plan, stats);
                break;
            case RhiBackendStage3D.ControlPlanes:
                DrawControlPlanes(gl, plan, stats);
                break;
            case RhiBackendStage3D.PostProcess:
                throw Unsupported("post-process stage");
            case RhiBackendStage3D.Present:
                RestoreHostState(gl);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(stage));
        }
    }

    void IRhiCommandExecutor3D.CompleteSubmission(ulong submissionId) { }

    private void RestoreHostState(GlInterface gl)
    {
        _depthMask!(1);
        _depthFunc!(GlLess);
        _disable!(GlBlend);
        gl.Enable(GlDepthTest);
        ApplyCullMode(gl, ThreeDEngine.Core.Materials.CullMode.None);
        BindVertexArray(0);
        gl.BindBuffer(GlArrayBuffer, 0);
        gl.BindBuffer(GlElementArrayBuffer, 0);
        gl.BindTexture(GlTexture2D, 0);
        gl.UseProgram(0);
    }

    private static InvalidOperationException Unsupported(string operation)
        => new($"The legacy OpenGL adapter cannot execute the RHI {operation}. No CPU fallback is permitted; select a backend capability profile that supports it.");
}
