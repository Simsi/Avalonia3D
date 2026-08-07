using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThreeDEngine.Core.Validation;
using ThreeDEngine.Core.Geometry;

namespace ThreeDEngine.Core.Rendering.Rhi;

internal enum RhiPassKind3D
{
    ForwardOpaque = 0,
    ForwardTransparent = 1,
    Overlay = 2,
    ControlPlane = 3,
    PostProcess = 4
}

/// <summary>A contiguous range in the canonical draw stream executed with one pass contract.</summary>
internal readonly struct RhiPass3D
{
    public RhiPass3D(string name, RhiPassKind3D kind, int firstCommand, int commandCount, RhiFeature3D requiredFeatures)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("RHI pass name cannot be empty.", nameof(name));
        if (firstCommand < 0) throw new ArgumentOutOfRangeException(nameof(firstCommand));
        if (commandCount < 0) throw new ArgumentOutOfRangeException(nameof(commandCount));
        Name = name;
        Kind = Guard3D.Defined(kind, nameof(kind));
        FirstCommand = firstCommand;
        CommandCount = commandCount;
        RequiredFeatures = requiredFeatures;
    }

    public string Name { get; }
    public RhiPassKind3D Kind { get; }
    public int FirstCommand { get; }
    public int CommandCount { get; }
    public RhiFeature3D RequiredFeatures { get; }
}

/// <summary>
/// Backend-neutral, allocation-reusable command/resource contract for a frame. It owns no native
/// object and contains no GL enum, JavaScript handle or backend-specific fallback decision.
/// </summary>
internal sealed class RhiFrameSubmission3D
{
    private readonly List<RhiPass3D> _passes = new(4);
    private readonly ReadOnlyCollection<RhiPass3D> _passesView;
    private CategoryState _ordinaryState;
    private CategoryState _particleState;
    private CategoryState _highScaleState;
    private int _retainedTextureCount;

    internal RhiFrameSubmission3D()
    {
        _passesView = _passes.AsReadOnly();
    }

    public IReadOnlyList<RhiPass3D> Passes => _passesView;
    public RhiFeature3D RequiredFeatures { get; private set; }
    public int DrawCommandCount { get; private set; }
    public int MeshCount { get; private set; }
    public int TextureCount { get; private set; }
    public bool UsesGpuSkinning { get; private set; }
    public bool UsesUInt32Indices { get; private set; }

    internal void Build(SceneRenderPlan3D plan)
    {
        _passes.Clear();
        if (plan.IncludesOrdinary) _ordinaryState = default;
        if (plan.IncludesParticles) _particleState = default;
        if (plan.IncludesHighScale) _highScaleState = default;
        if (plan.Resources.IsCompleteForMeshSweep) _retainedTextureCount = plan.Resources.Textures.Count;

        // One canonical command walk updates every included category. The previous implementation
        // rescanned the entire stream three times, which was measurable in mixed ordinary/particle/
        // high-scale stress scenes despite producing no different submission contract.
        for (var i = 0; i < plan.DrawCommands.Count; i++)
        {
            var command = plan.DrawCommands[i];
            switch (GetCategory(command.Kind))
            {
                case RenderCategory.Ordinary when plan.IncludesOrdinary:
                    AnalyzeCommand(command, ref _ordinaryState);
                    break;
                case RenderCategory.Particle when plan.IncludesParticles:
                    AnalyzeCommand(command, ref _particleState);
                    break;
                case RenderCategory.HighScale when plan.IncludesHighScale:
                    AnalyzeCommand(command, ref _highScaleState);
                    break;
            }
        }

        DrawCommandCount = _ordinaryState.DrawCount + _particleState.DrawCount + _highScaleState.DrawCount;
        MeshCount = _ordinaryState.MeshReferenceCount + _particleState.MeshReferenceCount + _highScaleState.MeshReferenceCount;
        TextureCount = _retainedTextureCount;
        UsesGpuSkinning = _ordinaryState.UsesGpuSkinning || _particleState.UsesGpuSkinning || _highScaleState.UsesGpuSkinning;
        UsesUInt32Indices = _ordinaryState.UsesUInt32Indices || _particleState.UsesUInt32Indices || _highScaleState.UsesUInt32Indices;
        RequiredFeatures = DrawCommandCount == 0
            ? RhiFeature3D.None
            : RhiDeviceCapabilities3D.RequiredRasterFeatures;
        if (MeshCount > 0) RequiredFeatures |= RhiFeature3D.VertexArrayObjects | RhiFeature3D.BufferSubData;
        if (TextureCount > 0) RequiredFeatures |= RhiFeature3D.Texture2D;
        if (UsesUInt32Indices) RequiredFeatures |= RhiFeature3D.UInt32Indices;
        if (UsesGpuSkinning) RequiredFeatures |= RhiFeature3D.VertexTextureFetch | RhiFeature3D.FloatTextures;

        var opaqueCount = _ordinaryState.OpaqueCount + _particleState.OpaqueCount + _highScaleState.OpaqueCount;
        if (opaqueCount > 0)
        {
            _passes.Add(new RhiPass3D("forward-opaque", RhiPassKind3D.ForwardOpaque, 0, opaqueCount,
                RhiFeature3D.InstancedDrawing | RhiFeature3D.VertexArrayObjects));
        }
        var transparentCount = _ordinaryState.TransparentCount + _particleState.TransparentCount + _highScaleState.TransparentCount;
        if (transparentCount > 0)
        {
            _passes.Add(new RhiPass3D("forward-transparent", RhiPassKind3D.ForwardTransparent, opaqueCount, transparentCount,
                RhiFeature3D.InstancedDrawing | RhiFeature3D.VertexArrayObjects));
        }
    }

    /// <summary>
    /// Encodes the canonical frame phases into an executable command buffer. Stage 3 adapters
    /// execute the existing mature draw implementations at explicit pass boundaries; stage 4
    /// replaces the backend-stage commands with individual pipeline/bind/draw commands.
    /// </summary>
    internal void Encode(RhiCommandEncoder3D encoder, bool includeSurfaceOverlays, bool includeControlPlanes, bool includePostProcess = false)
    {
        if (encoder is null) throw new ArgumentNullException(nameof(encoder));
        encoder.PushDebugGroup("scene-frame");
        encoder.ExecuteBackendStage(RhiBackendStage3D.PrepareResources);

        using (var background = encoder.BeginRenderPass(new RhiRenderPassDescriptor3D(
                   "background", RhiPassKind3D.ForwardOpaque,
                   RhiLoadOperation3D.Clear, RhiStoreOperation3D.Store,
                   RhiLoadOperation3D.Clear, RhiStoreOperation3D.Store)))
        {
            background.ExecuteBackendStage(RhiBackendStage3D.Background);
        }

        using (var scene = encoder.BeginRenderPass(new RhiRenderPassDescriptor3D(
                   "forward-scene", RhiPassKind3D.ForwardOpaque)))
        {
            scene.ExecuteBackendStage(RhiBackendStage3D.ForwardScene, 0, DrawCommandCount);
        }

        if (includeSurfaceOverlays)
        {
            using var overlays = encoder.BeginRenderPass(new RhiRenderPassDescriptor3D("surface-overlays", RhiPassKind3D.Overlay));
            overlays.ExecuteBackendStage(RhiBackendStage3D.SurfaceOverlays);
        }

        if (includeControlPlanes)
        {
            using var controls = encoder.BeginRenderPass(new RhiRenderPassDescriptor3D("control-planes", RhiPassKind3D.ControlPlane));
            controls.ExecuteBackendStage(RhiBackendStage3D.ControlPlanes);
        }

        if (includePostProcess)
        {
            using var post = encoder.BeginRenderPass(new RhiRenderPassDescriptor3D("post-process", RhiPassKind3D.PostProcess));
            post.ExecuteBackendStage(RhiBackendStage3D.PostProcess);
        }

        encoder.ExecuteBackendStage(RhiBackendStage3D.Present);
        encoder.PopDebugGroup();
    }

    private static void AnalyzeCommand(SceneRenderCommand3D command, ref CategoryState state)
    {
        state.DrawCount++;
        if (command.Transparent) state.TransparentCount++;
        else state.OpaqueCount++;
        AnalyzeCommandMeshes(command, ref state);
    }

    private static void AnalyzeCommandMeshes(SceneRenderCommand3D command, ref CategoryState state)
    {
        switch (command.Kind)
        {
            case SceneRenderCommandKind3D.OrdinaryBatch:
                AnalyzeMesh(command.OrdinaryBatch?.Mesh, ref state);
                break;
            case SceneRenderCommandKind3D.TransparentOrdinaryItem:
                AnalyzeMesh(command.TransparentOrdinary?.Item.Mesh, ref state);
                break;
            case SceneRenderCommandKind3D.TransparentOrdinaryBatch:
                AnalyzeMesh(command.TransparentOrdinaryBatch?.Mesh, ref state);
                break;
            case SceneRenderCommandKind3D.ParticleSystem:
                AnalyzeMesh(command.Particle?.Mesh, ref state);
                break;
            case SceneRenderCommandKind3D.HighScaleLayer:
                var parts = command.HighScaleLayer?.Template.Parts;
                if (parts is null) break;
                for (var i = 0; i < parts.Count; i++) AnalyzeMesh(parts[i].Mesh, ref state);
                break;
        }
    }

    private static void AnalyzeMesh(Mesh3D? mesh, ref CategoryState state)
    {
        if (mesh is null) return;
        var geometry = mesh.RenderGeometry;
        if (geometry.VertexCount == 0) return;
        state.MeshReferenceCount++;
        state.UsesUInt32Indices |= geometry.Indices.Format == IndexFormat3D.UInt32;
        state.UsesGpuSkinning |= geometry.HasSkinWeights;
    }

    private static RenderCategory GetCategory(SceneRenderCommandKind3D kind)
        => kind switch
        {
            SceneRenderCommandKind3D.ParticleSystem => RenderCategory.Particle,
            SceneRenderCommandKind3D.HighScaleLayer => RenderCategory.HighScale,
            _ => RenderCategory.Ordinary
        };

    private enum RenderCategory { Ordinary, Particle, HighScale }

    private struct CategoryState
    {
        public int DrawCount;
        public int OpaqueCount;
        public int TransparentCount;
        public int MeshReferenceCount;
        public bool UsesGpuSkinning;
        public bool UsesUInt32Indices;
    }
}
