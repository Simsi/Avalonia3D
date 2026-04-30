using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.OpenGL;
using ThreeDEngine.Avalonia.Controls;
using ThreeDEngine.Core.Culling;
using ThreeDEngine.Core.Geometry;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.OpenGL.Rendering;

internal sealed class OpenGlSceneRenderer : ISceneRenderBackend, IRenderResourceCache, IDebugDrawBackend
{
    private const int GlColorBufferBit = 0x00004000;
    private const int GlDepthBufferBit = 0x00000100;
    private const int GlFramebuffer = 0x8D40;
    private const int GlTriangles = 0x0004;
    private const int GlFloat = 0x1406;
    private const int GlUnsignedInt = 0x1405;
    private const int GlArrayBuffer = 0x8892;
    private const int GlElementArrayBuffer = 0x8893;
    private const int GlStaticDraw = 0x88E4;
    private const int GlDynamicDraw = 0x88E8;
    private const int GlDepthTest = 0x0B71;
    private const int GlBlend = 0x0BE2;
    private const int GlTexture2D = 0x0DE1;
    private const int GlTexture0 = 0x84C0;
    private const int GlTexture1 = 0x84C1;
    private const int GlTextureMinFilter = 0x2801;
    private const int GlTextureMagFilter = 0x2800;
    private const int GlTextureWrapS = 0x2802;
    private const int GlTextureWrapT = 0x2803;
    private const int GlNearest = 0x2600;
    private const int GlLinear = 0x2601;
    private const int GlClampToEdge = 0x812F;
    private const int GlRgba = 0x1908;
    private const int GlUnsignedByte = 0x1401;
    private const int GlVertexShader = 0x8B31;
    private const int GlFragmentShader = 0x8B30;
    private const int GlSrcAlpha = 0x0302;
    private const int GlOneMinusSrcAlpha = 0x0303;
    private const int InstanceFloatStride = 20;
    private const int InstanceByteStride = InstanceFloatStride * sizeof(float);
    private const int HighScaleTransformFloatStride = 16;
    private const int HighScaleTransformByteStride = HighScaleTransformFloatStride * sizeof(float);
    private const int HighScaleStateFloatStride = 4;
    private const int HighScaleStateByteStride = HighScaleStateFloatStride * sizeof(float);
    private const int MaxHighScaleMaterialVariants = 32;
    private static readonly HighScaleChunkKey3D AggregateChunkKey = new(int.MinValue, int.MinValue, int.MinValue);

    private readonly Dictionary<string, MeshGpuResource> _meshResources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ControlTextureResource> _controlTextures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MeshBatchData> _meshBatches = new(StringComparer.Ordinal);
    private readonly Dictionary<HighScaleBatchKey, HighScaleGpuBatchData> _highScaleGpuBatches = new();
    private readonly float[] _matrixUploadBuffer = new float[16];
    private readonly float[] _controlVertexData = new float[20];
    private readonly List<string> _meshSweepScratch = new();
    private readonly List<string> _textureSweepScratch = new();
    private int _lastSweptRegistryVersion = -1;
    private int _highScaleTransformBatchUploadsThisFrame;

    private int _meshProgram;
    private int _texturedProgram;
    private int _meshPositionLocation;
    private int _meshNormalLocation;
    private int _meshInstanceModel0Location;
    private int _meshInstanceModel1Location;
    private int _meshInstanceModel2Location;
    private int _meshInstanceModel3Location;
    private int _meshInstanceColorLocation;
    private int _meshInstanceStateColorLocation;
    private int _meshMaterialSlotLocation;
    private int _meshAmbientLightLocation;
    private int _meshDirectionalLightDirectionLocation;
    private int _meshDirectionalLightColorLocation;
    private int _meshPointLightPositionLocation;
    private int _meshPointLightColorLocation;
    private int _meshColorLocation;
    private int _meshUseInstancingLocation;
    private int _meshLightingEnabledLocation;
    private int _meshModelLocation;
    private int _meshViewProjLocation;
    private int _meshPartLocalLocation;
    private int _meshUsePartLocalLocation;
    private int _meshUseHighScaleStateLocation;
    private int _meshUsePaletteTextureLocation;
    private int _meshPaletteTextureLocation;
    private int _meshPaletteWidthLocation;
    private int _meshPaletteHeightLocation;
    private readonly int[] _meshVariantColorLocations = new int[MaxHighScaleMaterialVariants];
    private int _texturePositionLocation;
    private int _textureUvLocation;
    private int _textureSamplerLocation;
    private int _textureViewProjLocation;
    private int _controlVertexBuffer;
    private int _controlIndexBuffer;
    private int _meshInstanceBuffer;
    private int _paletteTexture;
    private byte[] _paletteUploadBuffer = Array.Empty<byte>();
    private bool _initialized;
    private bool _supportsInstancing;
    private GlBlendFuncDelegate? _blendFunc;
    private GlDepthMaskDelegate? _depthMask;
    private GlDisableDelegate? _disable;
    private GlUniform1iDelegate? _uniform1i;
    private GlUniform1fDelegate? _uniform1f;
    private GlUniform4fDelegate? _uniform4f;
    private GlUniform3fDelegate? _uniform3f;
    private GlUniformMatrix4fvDelegate? _uniformMatrix4fv;
    private GlVertexAttribDivisorDelegate? _vertexAttribDivisor;
    private GlDrawElementsInstancedDelegate? _drawElementsInstanced;
    private GlBufferSubDataDelegate? _bufferSubData;

    public RenderBackendCapabilities Capabilities { get; } = new(
        BackendKind.OpenGlDesktop,
        SupportsRetainedResources: true,
        SupportsHighScaleRuntime: true,
        SupportsGpuInstancing: true,
        SupportsDebugDraw: true,
        SupportsTransparentSorting: true);

    private RendererInvalidationKind _pendingInvalidation = RendererInvalidationKind.FullFrame;

    public void NotifySceneChanged(SceneChangedEventArgs change, RendererInvalidationKind invalidation)
    {
        _pendingInvalidation |= invalidation;
    }

    public void Invalidate(RendererInvalidationKind invalidation)
    {
        _pendingInvalidation |= invalidation;
    }

    public void Clear() => Reset();

    public void ClearDebugPrimitives()
    {
    }

    public void DrawSceneDebug(Scene3D scene)
    {
    }

    public RenderStats Render(Scene3D scene, SceneRenderPacket packet)
    {
        throw new NotSupportedException(
            "OpenGlSceneRenderer requires an active OpenGL context. Use the Avalonia presenter Render(...) path; the contract-only ISceneRenderBackend adapter is not wired to a GL context yet.");
    }

    public void Initialize(GlInterface gl)
    {
        if (_initialized) return;

        _blendFunc = LoadDelegate<GlBlendFuncDelegate>(gl, "glBlendFunc");
        _depthMask = LoadDelegate<GlDepthMaskDelegate>(gl, "glDepthMask");
        _disable = LoadDelegate<GlDisableDelegate>(gl, "glDisable");
        _uniform1i = LoadDelegate<GlUniform1iDelegate>(gl, "glUniform1i");
        _uniform1f = LoadDelegate<GlUniform1fDelegate>(gl, "glUniform1f");
        _uniform4f = LoadDelegate<GlUniform4fDelegate>(gl, "glUniform4f");
        _uniform3f = LoadDelegate<GlUniform3fDelegate>(gl, "glUniform3f");
        _uniformMatrix4fv = LoadDelegate<GlUniformMatrix4fvDelegate>(gl, "glUniformMatrix4fv");
        _vertexAttribDivisor = LoadDelegate<GlVertexAttribDivisorDelegate>(gl, "glVertexAttribDivisor")
                                ?? LoadDelegate<GlVertexAttribDivisorDelegate>(gl, "glVertexAttribDivisorARB");
        _drawElementsInstanced = LoadDelegate<GlDrawElementsInstancedDelegate>(gl, "glDrawElementsInstanced")
                                 ?? LoadDelegate<GlDrawElementsInstancedDelegate>(gl, "glDrawElementsInstancedARB");
        _bufferSubData = LoadDelegate<GlBufferSubDataDelegate>(gl, "glBufferSubData");
        _supportsInstancing = _vertexAttribDivisor is not null && _drawElementsInstanced is not null;

        _meshProgram = CreateProgram(gl, MeshVertexSource, MeshFragmentSource,
            (0, "aPosition"), (1, "aNormal"), (2, "aInstanceModel0"), (3, "aInstanceModel1"),
            (4, "aInstanceModel2"), (5, "aInstanceModel3"), (6, "aInstanceColor"), (7, "aInstanceState"), (8, "aMaterialSlot"));
        _meshPositionLocation = gl.GetAttribLocationString(_meshProgram, "aPosition");
        _meshNormalLocation = gl.GetAttribLocationString(_meshProgram, "aNormal");
        _meshInstanceModel0Location = gl.GetAttribLocationString(_meshProgram, "aInstanceModel0");
        _meshInstanceModel1Location = gl.GetAttribLocationString(_meshProgram, "aInstanceModel1");
        _meshInstanceModel2Location = gl.GetAttribLocationString(_meshProgram, "aInstanceModel2");
        _meshInstanceModel3Location = gl.GetAttribLocationString(_meshProgram, "aInstanceModel3");
        _meshInstanceColorLocation = gl.GetAttribLocationString(_meshProgram, "aInstanceColor");
        _meshInstanceStateColorLocation = gl.GetAttribLocationString(_meshProgram, "aInstanceState");
        _meshMaterialSlotLocation = gl.GetAttribLocationString(_meshProgram, "aMaterialSlot");
        _meshColorLocation = gl.GetUniformLocationString(_meshProgram, "uColor");
        _meshUseInstancingLocation = gl.GetUniformLocationString(_meshProgram, "uUseInstancing");
        _meshLightingEnabledLocation = gl.GetUniformLocationString(_meshProgram, "uLightingEnabled");
        _meshModelLocation = gl.GetUniformLocationString(_meshProgram, "uModel");
        _meshViewProjLocation = gl.GetUniformLocationString(_meshProgram, "uViewProj");
        _meshPartLocalLocation = gl.GetUniformLocationString(_meshProgram, "uPartLocal");
        _meshUsePartLocalLocation = gl.GetUniformLocationString(_meshProgram, "uUsePartLocal");
        _meshUseHighScaleStateLocation = gl.GetUniformLocationString(_meshProgram, "uUseHighScaleState");
        _meshUsePaletteTextureLocation = gl.GetUniformLocationString(_meshProgram, "uUsePaletteTexture");
        _meshPaletteTextureLocation = gl.GetUniformLocationString(_meshProgram, "uPaletteTexture");
        _meshPaletteWidthLocation = gl.GetUniformLocationString(_meshProgram, "uPaletteWidth");
        _meshPaletteHeightLocation = gl.GetUniformLocationString(_meshProgram, "uPaletteHeight");
        for (var i = 0; i < _meshVariantColorLocations.Length; i++)
        {
            _meshVariantColorLocations[i] = gl.GetUniformLocationString(_meshProgram, $"uVariantColors[{i}]");
        }
        _meshAmbientLightLocation = gl.GetUniformLocationString(_meshProgram, "uAmbientLight");
        _meshDirectionalLightDirectionLocation = gl.GetUniformLocationString(_meshProgram, "uDirectionalLightDirection");
        _meshDirectionalLightColorLocation = gl.GetUniformLocationString(_meshProgram, "uDirectionalLightColor");
        _meshPointLightPositionLocation = gl.GetUniformLocationString(_meshProgram, "uPointLightPosition");
        _meshPointLightColorLocation = gl.GetUniformLocationString(_meshProgram, "uPointLightColor");

        _texturedProgram = CreateProgram(gl, TexturedVertexSource, TexturedFragmentSource, (0, "aPosition"), (1, "aTexCoord"));
        _texturePositionLocation = gl.GetAttribLocationString(_texturedProgram, "aPosition");
        _textureUvLocation = gl.GetAttribLocationString(_texturedProgram, "aTexCoord");
        _textureSamplerLocation = gl.GetUniformLocationString(_texturedProgram, "uTexture");
        _textureViewProjLocation = gl.GetUniformLocationString(_texturedProgram, "uViewProj");

        _meshInstanceBuffer = gl.GenBuffer();
        _paletteTexture = gl.GenTexture();
        _controlVertexBuffer = gl.GenBuffer();
        _controlIndexBuffer = gl.GenBuffer();
        gl.BindBuffer(GlElementArrayBuffer, _controlIndexBuffer);
        UploadInts(gl, GlElementArrayBuffer, new[] { 0, 1, 2, 0, 2, 3 }, GlStaticDraw);
        gl.BindBuffer(GlElementArrayBuffer, 0);
        _initialized = true;
    }

    public RenderStats Render(GlInterface gl, int framebuffer, Scene3D scene, Rect bounds, RendererInvalidationKind invalidation = RendererInvalidationKind.FullFrame)
    {
        if (!_initialized) Initialize(gl);
        _pendingInvalidation |= invalidation;
        var effectiveInvalidation = _pendingInvalidation;
        ApplyRendererInvalidation(gl, scene);
        var width = System.Math.Max((int)System.Math.Ceiling(bounds.Width), 1);
        var height = System.Math.Max((int)System.Math.Ceiling(bounds.Height), 1);
        var stats = RenderSceneCore(gl, framebuffer, width, height, scene);
        stats.RendererInvalidation = effectiveInvalidation;
        if ((effectiveInvalidation & RendererInvalidationKind.BatchRebuild) != 0) stats.FullRebuildReason = effectiveInvalidation.ToString();
        stats.RenderTargetWidth = width;
        stats.RenderTargetHeight = height;
        return stats;
    }

    private RenderStats RenderSceneCore(GlInterface gl, int framebuffer, int width, int height, Scene3D scene)
    {
        gl.BindFramebuffer(GlFramebuffer, framebuffer);
        gl.Viewport(0, 0, width, height);
        gl.Enable(GlDepthTest);
        gl.ClearColor(scene.BackgroundColor.R, scene.BackgroundColor.G, scene.BackgroundColor.B, scene.BackgroundColor.A);
        gl.Clear(GlColorBufferBit | GlDepthBufferBit);

        var aspect = (float)width / height;
        scene.FrameInterpolator.UpdateAlpha();
        var viewProjection = scene.Camera.GetViewMatrix() * scene.Camera.GetProjectionMatrix(aspect);

        SweepUnusedResources(gl, scene);
        var stats = new RenderStats
        {
            ObjectCount = scene.Registry.AllObjects.Count,
            RenderableCount = scene.Registry.Renderables.Count,
            PickableCount = scene.Registry.Pickables.Count,
            ColliderCount = scene.Registry.Colliders.Count,
            DynamicBodyCount = scene.Registry.DynamicBodies.Count,
            StaticColliderCount = scene.Registry.StaticColliders.Count,
            RegistryVersion = scene.Registry.Version,
            MeshCacheCount = MeshCache3D.Shared.Count,
            InterpolationAlpha = scene.FrameInterpolator.Alpha
        };

        DrawMeshes(gl, scene, viewProjection, stats);
        DrawControlPlanes(gl, scene, viewProjection, stats);

        gl.BindBuffer(GlArrayBuffer, 0);
        gl.BindBuffer(GlElementArrayBuffer, 0);
        gl.BindTexture(GlTexture2D, 0);
        gl.UseProgram(0);
        return stats;
    }

    private void ApplyRendererInvalidation(GlInterface gl, Scene3D scene)
    {
        var invalidation = _pendingInvalidation;
        if (invalidation == RendererInvalidationKind.None)
        {
            return;
        }

        if ((invalidation & RendererInvalidationKind.ResourceUpload) != 0 ||
            (invalidation & RendererInvalidationKind.HighScaleStructure) != 0 ||
            (invalidation & RendererInvalidationKind.FullFrame) == RendererInvalidationKind.FullFrame)
        {
            _lastSweptRegistryVersion = -1;
        }

        if ((invalidation & RendererInvalidationKind.BatchRebuild) != 0)
        {
            foreach (var batch in _meshBatches.Values) batch.Reset();
        }

        _pendingInvalidation = RendererInvalidationKind.None;
    }

    public void Deinitialize(GlInterface gl)
    {
        foreach (var resource in _meshResources.Values) resource.Dispose(gl);
        foreach (var texture in _controlTextures.Values) texture.Dispose(gl);
        foreach (var batch in _highScaleGpuBatches.Values) batch.Dispose(gl);
        _meshResources.Clear();
        _controlTextures.Clear();
        _highScaleGpuBatches.Clear();
        if (_meshInstanceBuffer != 0) gl.DeleteBuffer(_meshInstanceBuffer);
        if (_paletteTexture != 0) gl.DeleteTexture(_paletteTexture);
        if (_controlVertexBuffer != 0) gl.DeleteBuffer(_controlVertexBuffer);
        if (_controlIndexBuffer != 0) gl.DeleteBuffer(_controlIndexBuffer);
        if (_meshProgram != 0) gl.DeleteProgram(_meshProgram);
        if (_texturedProgram != 0) gl.DeleteProgram(_texturedProgram);
        _meshInstanceBuffer = _controlVertexBuffer = _controlIndexBuffer = _meshProgram = _texturedProgram = 0;
        _paletteTexture = 0;
        _initialized = false;
    }

    public void Reset()
    {
        _initialized = false;
        _meshResources.Clear();
        _controlTextures.Clear();
        _highScaleGpuBatches.Clear();
        _meshBatches.Clear();
        _highScaleGpuBatches.Clear();
        _meshInstanceBuffer = _controlVertexBuffer = _controlIndexBuffer = _meshProgram = _texturedProgram = 0;
        _paletteTexture = 0;
        _lastSweptRegistryVersion = -1;
    }

    private void SweepUnusedResources(GlInterface gl, Scene3D scene)
    {
        var registryVersion = scene.Registry.Version;
        if (_lastSweptRegistryVersion == registryVersion) return;

        var liveMeshes = new HashSet<string>(StringComparer.Ordinal);
        var liveControlPlanes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var obj in scene.Registry.Renderables) liveMeshes.Add(obj.GetMesh().ResourceKey);
        foreach (var layer in EnumerateHighScaleLayers(scene))
        {
            foreach (var part in layer.Template.Parts) liveMeshes.Add(part.Mesh.ResourceKey);
        }
        foreach (var obj in scene.Registry.AllObjects)
        {
            if (obj is ControlPlane3D plane && plane.IsVisible && plane.Snapshot is not null) liveControlPlanes.Add(plane.Id);
        }
        _meshSweepScratch.Clear();
        foreach (var pair in _meshResources)
        {
            if (!liveMeshes.Contains(pair.Key)) _meshSweepScratch.Add(pair.Key);
        }
        foreach (var key in _meshSweepScratch)
        {
            _meshResources[key].Dispose(gl);
            _meshResources.Remove(key);
        }

        _textureSweepScratch.Clear();
        foreach (var pair in _controlTextures)
        {
            if (!liveControlPlanes.Contains(pair.Key)) _textureSweepScratch.Add(pair.Key);
        }
        foreach (var key in _textureSweepScratch)
        {
            _controlTextures[key].Dispose(gl);
            _controlTextures.Remove(key);
        }
        _lastSweptRegistryVersion = registryVersion;
    }

    private void DrawMeshes(GlInterface gl, Scene3D scene, Matrix4x4 viewProjection, RenderStats stats)
    {
        BuildBatches(scene, viewProjection, stats);
        var hasHighScale = HasHighScaleLayers(scene);
        if (_meshBatches.Count == 0 && !hasHighScale) return;

        gl.UseProgram(_meshProgram);
        UploadLighting(scene);
        UploadMatrix(_uniformMatrix4fv, _meshViewProjLocation, viewProjection, _matrixUploadBuffer);
        UploadFloat(_uniform1f, _meshUsePartLocalLocation, 0f);
        UploadFloat(_uniform1f, _meshUseHighScaleStateLocation, 0f);
        UploadFloat(_uniform1f, _meshUsePaletteTextureLocation, 0f);

        if (_supportsInstancing)
        {
            UploadFloat(_uniform1f, _meshUseInstancingLocation, 1f);
            DrawInstancedBatches(gl, stats);
            DrawHighScaleLayers(gl, scene, viewProjection, stats);
        }
        else
        {
            UploadFloat(_uniform1f, _meshUseInstancingLocation, 0f);
            DrawLegacyBatches(gl, stats);
            if (hasHighScale)
            {
                stats.HighScaleInstanceCount = 0;
            }
        }
    }

    private void BuildBatches(Scene3D scene, Matrix4x4 viewProjection, RenderStats stats)
    {
        foreach (var batch in _meshBatches.Values) batch.Reset();

        foreach (var obj in scene.Registry.Renderables)
        {
            var mesh = obj.GetMesh();
            var model = scene.FrameInterpolator.TryGetInterpolatedModel(obj.Id, out var interpolatedModel) ? interpolatedModel : obj.GetModelMatrix();
            if (!FrustumCuller3D.IntersectsLocalBounds(mesh.LocalBounds, model, viewProjection))
            {
                stats.CulledObjectCount++;
                continue;
            }
            var distanceAlpha = ResolveDistanceAlpha(scene, model);
            if (distanceAlpha <= 0.001f)
            {
                stats.CulledObjectCount++;
                continue;
            }
            var color = ApplyDistanceAlpha(ResolveColor(obj), distanceAlpha);
            var lighting = obj.Material.Lighting == LightingMode.Lambert ? 1 : 0;
            var batch = GetBatch(mesh.ResourceKey, mesh, lighting);
            batch.Add(model, color);
            stats.VisibleMeshCount++;
            stats.TriangleCount += mesh.Indices.Length / 3;
        }

        // HighScaleInstanceLayer3D is intentionally not expanded into the normal per-frame mesh batch.
        // It is rendered by DrawHighScaleLayers using retained chunk/part instance buffers.
    }

    private MeshBatchData GetBatch(string meshKey, Mesh3D mesh, int lighting)
    {
        var key = meshKey + "|l:" + lighting.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!_meshBatches.TryGetValue(key, out var batch))
        {
            batch = new MeshBatchData(meshKey, mesh, lighting);
            _meshBatches[key] = batch;
        }
        else
        {
            batch.Mesh = mesh;
        }
        return batch;
    }

    private static bool HasHighScaleLayers(Scene3D scene)
    {
        foreach (var obj in scene.Registry.AllObjects)
            if (obj is HighScaleInstanceLayer3D layer && layer.IsVisible && layer.Instances.Count > 0)
                return true;
        return false;
    }

    private static IEnumerable<HighScaleInstanceLayer3D> EnumerateHighScaleLayers(Scene3D scene)
    {
        foreach (var obj in scene.Registry.AllObjects)
            if (obj is HighScaleInstanceLayer3D layer)
                yield return layer;
    }

    private void DrawInstancedBatches(GlInterface gl, RenderStats stats)
    {
        foreach (var batch in _meshBatches.Values)
        {
            if (batch.InstanceCount == 0) continue;
            var resource = EnsureMeshResource(gl, batch.MeshKey, 0, batch.Mesh, stats);
            BindMeshAttributes(gl, resource);
            gl.BindBuffer(GlArrayBuffer, _meshInstanceBuffer);
            UploadFloats(gl, GlArrayBuffer, batch.Data, batch.FloatCount, GlDynamicDraw);
            EnableInstanceAttributes(gl);
            UploadFloat(_uniform1f, _meshLightingEnabledLocation, batch.LightingEnabled);
            _drawElementsInstanced?.Invoke(GlTriangles, resource.IndexCount, GlUnsignedInt, IntPtr.Zero, batch.InstanceCount);
            stats.DrawCallCount++;
            stats.EstimatedDrawCallCount++;
            stats.InstancedBatchCount++;
        }
        ResetInstanceAttributeDivisors();
    }

    private void DrawLegacyBatches(GlInterface gl, RenderStats stats)
    {
        foreach (var batch in _meshBatches.Values)
        {
            if (batch.InstanceCount == 0) continue;
            var resource = EnsureMeshResource(gl, batch.MeshKey, 0, batch.Mesh, stats);
            BindMeshAttributes(gl, resource);
            UploadFloat(_uniform1f, _meshLightingEnabledLocation, batch.LightingEnabled);
            var data = batch.Data;
            for (var i = 0; i < batch.InstanceCount; i++)
            {
                var offset = i * InstanceFloatStride;
                UploadMatrixFromInstanceData(_uniformMatrix4fv, _meshModelLocation, data, offset, _matrixUploadBuffer);
                UploadColor(_uniform4f, _meshColorLocation, new ColorRgba(data[offset + 16], data[offset + 17], data[offset + 18], data[offset + 19]));
                gl.DrawElements(GlTriangles, resource.IndexCount, GlUnsignedInt, IntPtr.Zero);
                stats.DrawCallCount++;
            }
        }
    }

    private void BindMeshAttributes(GlInterface gl, MeshGpuResource resource)
    {
        gl.BindBuffer(GlArrayBuffer, resource.VertexBuffer);
        gl.EnableVertexAttribArray(_meshPositionLocation);
        gl.VertexAttribPointer(_meshPositionLocation, 3, GlFloat, 0, sizeof(float) * 3, IntPtr.Zero);
        gl.BindBuffer(GlArrayBuffer, resource.NormalBuffer);
        gl.EnableVertexAttribArray(_meshNormalLocation);
        gl.VertexAttribPointer(_meshNormalLocation, 3, GlFloat, 0, sizeof(float) * 3, IntPtr.Zero);
        if (_meshMaterialSlotLocation >= 0)
        {
            gl.BindBuffer(GlArrayBuffer, resource.MaterialSlotBuffer);
            gl.EnableVertexAttribArray(_meshMaterialSlotLocation);
            gl.VertexAttribPointer(_meshMaterialSlotLocation, 1, GlFloat, 0, sizeof(float), IntPtr.Zero);
            _vertexAttribDivisor?.Invoke(_meshMaterialSlotLocation, 0);
        }
        gl.BindBuffer(GlElementArrayBuffer, resource.IndexBuffer);
    }

    private void EnableInstanceAttributes(GlInterface gl)
    {
        EnableInstanceAttribute(gl, _meshInstanceModel0Location, 4, InstanceByteStride, 0);
        EnableInstanceAttribute(gl, _meshInstanceModel1Location, 4, InstanceByteStride, sizeof(float) * 4);
        EnableInstanceAttribute(gl, _meshInstanceModel2Location, 4, InstanceByteStride, sizeof(float) * 8);
        EnableInstanceAttribute(gl, _meshInstanceModel3Location, 4, InstanceByteStride, sizeof(float) * 12);
        EnableInstanceAttribute(gl, _meshInstanceColorLocation, 4, InstanceByteStride, sizeof(float) * 16);
    }

    private void EnableHighScaleInstanceAttributes(GlInterface gl, HighScaleGpuBatchData batch)
    {
        gl.BindBuffer(GlArrayBuffer, batch.TransformBuffer);
        EnableInstanceAttribute(gl, _meshInstanceModel0Location, 4, HighScaleTransformByteStride, 0);
        EnableInstanceAttribute(gl, _meshInstanceModel1Location, 4, HighScaleTransformByteStride, sizeof(float) * 4);
        EnableInstanceAttribute(gl, _meshInstanceModel2Location, 4, HighScaleTransformByteStride, sizeof(float) * 8);
        EnableInstanceAttribute(gl, _meshInstanceModel3Location, 4, HighScaleTransformByteStride, sizeof(float) * 12);

        gl.BindBuffer(GlArrayBuffer, batch.StateBuffer);
        EnableInstanceAttribute(gl, _meshInstanceStateColorLocation, 4, HighScaleStateByteStride, 0);
    }

    private void EnableInstanceAttribute(GlInterface gl, int location, int size, int stride, int offset)
    {
        if (location < 0) return;
        gl.EnableVertexAttribArray(location);
        gl.VertexAttribPointer(location, size, GlFloat, 0, stride, new IntPtr(offset));
        _vertexAttribDivisor?.Invoke(location, 1);
    }

    private void ResetInstanceAttributeDivisors()
    {
        ResetDivisor(_meshInstanceModel0Location);
        ResetDivisor(_meshInstanceModel1Location);
        ResetDivisor(_meshInstanceModel2Location);
        ResetDivisor(_meshInstanceModel3Location);
        ResetDivisor(_meshInstanceColorLocation);
        ResetDivisor(_meshInstanceStateColorLocation);
        ResetDivisor(_meshMaterialSlotLocation);
    }

    private void ResetDivisor(int location)
    {
        if (location >= 0) _vertexAttribDivisor?.Invoke(location, 0);
    }

    private void DrawHighScaleLayers(GlInterface gl, Scene3D scene, Matrix4x4 viewProjection, RenderStats stats)
    {
        UploadFloat(_uniform1f, _meshUsePartLocalLocation, 1f);
        UploadFloat(_uniform1f, _meshUseHighScaleStateLocation, 1f);
        var cameraPosition = scene.Camera.Position;
        _highScaleTransformBatchUploadsThisFrame = 0;
        foreach (var layer in EnumerateHighScaleLayers(scene))
        {
            if (!layer.IsVisible || layer.Instances.Count == 0) continue;
            if (layer.Chunks.RebuildRequested)
            {
                layer.Chunks.Rebuild(layer.Instances, layer.Template.LocalBounds);
            }

            if (ShouldUseAggregateLayerBatches(layer, scene.Performance))
            {
                DrawHighScaleAggregateLayer(gl, scene, layer, cameraPosition, scene.Performance, stats);
                layer.StateBuffer.ClearDirty();
                continue;
            }

            var visibleChunks = layer.Chunks.QueryVisible(viewProjection);
            stats.TotalChunkCount += layer.Chunks.Chunks.Count;
            var visibleChunkLimit = scene.Performance.MaxVisibleHighScaleChunks > 0 ? System.Math.Min(scene.Performance.MaxVisibleHighScaleChunks, visibleChunks.Count) : visibleChunks.Count;
            stats.VisibleChunkCount += visibleChunkLimit;

            for (var visibleChunkIndex = 0; visibleChunkIndex < visibleChunkLimit; visibleChunkIndex++)
            {
                var chunk = visibleChunks[visibleChunkIndex];
                var planStart = Stopwatch.GetTimestamp();
                var plan = BuildHighScaleChunkPlan(layer, chunk, cameraPosition, stats, scene.Performance);
                stats.HighScalePlanMilliseconds += GetElapsedMilliseconds(planStart);

                DrawHighScaleLod(gl, layer, chunk, HighScaleLodLevel3D.Detailed, plan.Detailed, cameraPosition, scene.Performance, stats);
                DrawHighScaleLod(gl, layer, chunk, HighScaleLodLevel3D.Simplified, plan.Simplified, cameraPosition, scene.Performance, stats);
                DrawHighScaleLod(gl, layer, chunk, HighScaleLodLevel3D.Proxy, plan.Proxy, cameraPosition, scene.Performance, stats);
                DrawHighScaleLod(gl, layer, chunk, HighScaleLodLevel3D.Billboard, plan.Billboard, cameraPosition, scene.Performance, stats);
                chunk.MarkClean();
            }

            layer.StateBuffer.ClearDirty();

        }
        UploadFloat(_uniform1f, _meshUseHighScaleStateLocation, 0f);
        UploadFloat(_uniform1f, _meshUsePaletteTextureLocation, 0f);
        UploadFloat(_uniform1f, _meshUsePartLocalLocation, 0f);
        ResetInstanceAttributeDivisors();
    }


    private static bool ShouldUseAggregateLayerBatches(HighScaleInstanceLayer3D layer, ScenePerformanceOptions performance)
        => performance.EnableHighScaleAggregateLayerBatches &&
           layer.Instances.Count > 0 &&
           layer.Instances.Count <= performance.HighScaleAggregateLayerInstanceThreshold;

    private void DrawHighScaleAggregateLayer(
        GlInterface gl,
        Scene3D scene,
        HighScaleInstanceLayer3D layer,
        Vector3 cameraPosition,
        ScenePerformanceOptions performance,
        RenderStats stats)
    {
        stats.TotalChunkCount += layer.Chunks.Chunks.Count;
        stats.VisibleChunkCount += layer.Chunks.Chunks.Count;

        var planStart = Stopwatch.GetTimestamp();
        var plan = BuildHighScaleLayerPlan(layer, cameraPosition, stats, performance);
        stats.HighScalePlanMilliseconds += GetElapsedMilliseconds(planStart);

        DrawHighScaleAggregateLod(gl, layer, HighScaleLodLevel3D.Detailed, plan.Detailed, cameraPosition, performance, stats);
        DrawHighScaleAggregateLod(gl, layer, HighScaleLodLevel3D.Simplified, plan.Simplified, cameraPosition, performance, stats);
        DrawHighScaleAggregateLod(gl, layer, HighScaleLodLevel3D.Proxy, plan.Proxy, cameraPosition, performance, stats);
        DrawHighScaleAggregateLod(gl, layer, HighScaleLodLevel3D.Billboard, plan.Billboard, cameraPosition, performance, stats);
    }

    private HighScaleChunkFramePlan BuildHighScaleLayerPlan(HighScaleInstanceLayer3D layer, Vector3 cameraPosition, RenderStats stats, ScenePerformanceOptions performance)
    {
        var plan = HighScaleChunkFramePlan.Shared;
        plan.Reset();

        var count = layer.Instances.Count;
        for (var index = 0; index < count; index++)
        {
            var record = layer.Instances[index];

            if (performance.MaxHighScaleVisibleInstances > 0 && stats.HighScaleInstanceCount >= performance.MaxHighScaleVisibleInstances)
            {
                stats.LodCulledCount++;
                stats.CulledObjectCount++;
                continue;
            }

            var lod = layer.LodPolicy.Resolve(cameraPosition, record.Transform);
            if (lod == HighScaleLodLevel3D.Culled)
            {
                stats.LodCulledCount++;
                stats.CulledObjectCount++;
                continue;
            }

            stats.HighScaleInstanceCount++;
            if (lod == HighScaleLodLevel3D.Detailed)
            {
                stats.LodDetailedCount++;
                plan.Detailed.Add(index);
            }
            else if (lod == HighScaleLodLevel3D.Simplified)
            {
                stats.LodSimplifiedCount++;
                plan.Simplified.Add(index);
            }
            else if (lod == HighScaleLodLevel3D.Proxy)
            {
                stats.LodProxyCount++;
                plan.Proxy.Add(index);
            }
            else if (lod == HighScaleLodLevel3D.Billboard)
            {
                stats.LodBillboardCount++;
                plan.Billboard.Add(index);
            }
        }

        return plan;
    }

    private void DrawHighScaleAggregateLod(
        GlInterface gl,
        HighScaleInstanceLayer3D layer,
        HighScaleLodLevel3D lod,
        List<int> instanceIndices,
        Vector3 cameraPosition,
        ScenePerformanceOptions performance,
        RenderStats stats)
    {
        if (instanceIndices.Count == 0) return;

        var buildStart = Stopwatch.GetTimestamp();
        var key = new HighScaleBatchKey(layer.Id, AggregateChunkKey, lod);
        var batch = EnsureHighScaleGpuBatch(gl, layer, key, false, lod, instanceIndices, cameraPosition, performance, stats);
        stats.HighScaleBufferBuildMilliseconds += GetElapsedMilliseconds(buildStart);
        if (batch.InstanceCount == 0) return;

        var parts = layer.Template.ResolveParts(lod);
        for (var partIndex = 0; partIndex < parts.Count; partIndex++)
        {
            var part = parts[partIndex];
            var meshResource = EnsureMeshResource(gl, part.Mesh.ResourceKey, 0, part.Mesh, stats);
            BindMeshAttributes(gl, meshResource);
            EnableHighScaleInstanceAttributes(gl, batch);
            UploadHighScalePalette(gl, layer, part, performance, stats);
            UploadMatrix(_uniformMatrix4fv, _meshPartLocalLocation, part.LocalTransform, _matrixUploadBuffer);
            UploadFloat(_uniform1f, _meshLightingEnabledLocation, part.LightingMode == LightingMode.Lambert ? 1f : 0f);
            _drawElementsInstanced?.Invoke(GlTriangles, meshResource.IndexCount, GlUnsignedInt, IntPtr.Zero, batch.InstanceCount);
            stats.DrawCallCount++;
            stats.EstimatedDrawCallCount++;
            stats.InstancedBatchCount++;
            stats.VisibleMeshCount += batch.InstanceCount;
            stats.HighScaleVisiblePartInstanceCount += batch.InstanceCount;
            if (part.UsesVertexMaterialSlots) stats.BakedHighScalePartDraws++;
            stats.TriangleCount += (part.Mesh.Indices.Length / 3) * batch.InstanceCount;
        }
    }

    private HighScaleChunkFramePlan BuildHighScaleChunkPlan(HighScaleInstanceLayer3D layer, HighScaleChunk3D chunk, Vector3 cameraPosition, RenderStats stats, ScenePerformanceOptions performance)
    {
        var plan = HighScaleChunkFramePlan.Shared;
        plan.Reset();

        if (ShouldUseChunkLevelLodPlanning(layer, chunk, performance))
        {
            AddChunkAsSingleLod(layer, chunk, cameraPosition, stats, performance, plan);
            return plan;
        }

        foreach (var index in chunk.InstanceIndices)
        {
            var record = layer.Instances[index];

            if (performance.MaxHighScaleVisibleInstances > 0 && stats.HighScaleInstanceCount >= performance.MaxHighScaleVisibleInstances)
            {
                stats.LodCulledCount++;
                stats.CulledObjectCount++;
                continue;
            }

            var lod = layer.LodPolicy.Resolve(cameraPosition, record.Transform);
            if (lod == HighScaleLodLevel3D.Culled)
            {
                stats.LodCulledCount++;
                stats.CulledObjectCount++;
                continue;
            }

            stats.HighScaleInstanceCount++;
            if (lod == HighScaleLodLevel3D.Detailed)
            {
                stats.LodDetailedCount++;
                plan.Detailed.Add(index);
            }
            else if (lod == HighScaleLodLevel3D.Simplified)
            {
                stats.LodSimplifiedCount++;
                plan.Simplified.Add(index);
            }
            else if (lod == HighScaleLodLevel3D.Proxy)
            {
                stats.LodProxyCount++;
                plan.Proxy.Add(index);
            }
            else if (lod == HighScaleLodLevel3D.Billboard)
            {
                stats.LodBillboardCount++;
                plan.Billboard.Add(index);
            }
        }

        return plan;
    }

    private static bool ShouldUseChunkLevelLodPlanning(HighScaleInstanceLayer3D layer, HighScaleChunk3D chunk, ScenePerformanceOptions performance)
    {
        if (!performance.EnableHighScaleChunkLodPlanning) return false;
        if (layer.Instances.Count < performance.HighScaleChunkLodPlanningInstanceThreshold) return false;
        return chunk.InstanceIndices.Count >= performance.HighScaleChunkLodPlanningChunkThreshold;
    }

    private static void AddChunkAsSingleLod(HighScaleInstanceLayer3D layer, HighScaleChunk3D chunk, Vector3 cameraPosition, RenderStats stats, ScenePerformanceOptions performance, HighScaleChunkFramePlan plan)
    {
        var lod = ResolveChunkLod(layer, chunk, cameraPosition);
        var indices = chunk.InstanceIndices;
        var remaining = performance.MaxHighScaleVisibleInstances > 0
            ? System.Math.Max(0, performance.MaxHighScaleVisibleInstances - stats.HighScaleInstanceCount)
            : indices.Count;
        var count = System.Math.Min(indices.Count, remaining);

        if (count <= 0 || lod == HighScaleLodLevel3D.Culled)
        {
            stats.LodCulledCount += indices.Count;
            stats.CulledObjectCount += indices.Count;
            return;
        }

        var target = lod == HighScaleLodLevel3D.Detailed ? plan.Detailed :
            lod == HighScaleLodLevel3D.Simplified ? plan.Simplified :
            lod == HighScaleLodLevel3D.Billboard ? plan.Billboard :
            plan.Proxy;

        for (var i = 0; i < count; i++)
        {
            target.Add(indices[i]);
        }

        stats.HighScaleInstanceCount += count;
        if (lod == HighScaleLodLevel3D.Detailed) stats.LodDetailedCount += count;
        else if (lod == HighScaleLodLevel3D.Simplified) stats.LodSimplifiedCount += count;
        else if (lod == HighScaleLodLevel3D.Proxy) stats.LodProxyCount += count;
        else if (lod == HighScaleLodLevel3D.Billboard) stats.LodBillboardCount += count;

        if (count < indices.Count)
        {
            var culled = indices.Count - count;
            stats.LodCulledCount += culled;
            stats.CulledObjectCount += culled;
        }
    }

    private static HighScaleLodLevel3D ResolveChunkLod(HighScaleInstanceLayer3D layer, HighScaleChunk3D chunk, Vector3 cameraPosition)
    {
        var center = chunk.Bounds.Center;
        var d2 = Vector3.DistanceSquared(cameraPosition, center);
        var policy = layer.LodPolicy;
        if (d2 > policy.DrawDistance * policy.DrawDistance) return HighScaleLodLevel3D.Culled;
        if (d2 <= policy.DetailedDistance * policy.DetailedDistance) return HighScaleLodLevel3D.Detailed;
        if (d2 <= policy.SimplifiedDistance * policy.SimplifiedDistance) return HighScaleLodLevel3D.Simplified;
        if (d2 <= policy.ProxyDistance * policy.ProxyDistance) return HighScaleLodLevel3D.Proxy;
        return policy.EnableBillboardFallback ? HighScaleLodLevel3D.Billboard : HighScaleLodLevel3D.Proxy;
    }

    private void DrawHighScaleLod(
        GlInterface gl,
        HighScaleInstanceLayer3D layer,
        HighScaleChunk3D chunk,
        HighScaleLodLevel3D lod,
        List<int> instanceIndices,
        Vector3 cameraPosition,
        ScenePerformanceOptions performance,
        RenderStats stats)
    {
        if (instanceIndices.Count == 0) return;

        var buildStart = Stopwatch.GetTimestamp();
        var key = new HighScaleBatchKey(layer.Id, chunk.Key, lod);
        var batch = EnsureHighScaleGpuBatch(gl, layer, key, chunk.IsDirty, lod, instanceIndices, cameraPosition, performance, stats);
        stats.HighScaleBufferBuildMilliseconds += GetElapsedMilliseconds(buildStart);
        if (batch.InstanceCount == 0) return;

        var parts = layer.Template.ResolveParts(lod);
        for (var partIndex = 0; partIndex < parts.Count; partIndex++)
        {
            var part = parts[partIndex];
            var meshResource = EnsureMeshResource(gl, part.Mesh.ResourceKey, 0, part.Mesh, stats);
            BindMeshAttributes(gl, meshResource);
            EnableHighScaleInstanceAttributes(gl, batch);
            UploadHighScalePalette(gl, layer, part, performance, stats);
            UploadMatrix(_uniformMatrix4fv, _meshPartLocalLocation, part.LocalTransform, _matrixUploadBuffer);
            UploadFloat(_uniform1f, _meshLightingEnabledLocation, part.LightingMode == LightingMode.Lambert ? 1f : 0f);
            _drawElementsInstanced?.Invoke(GlTriangles, meshResource.IndexCount, GlUnsignedInt, IntPtr.Zero, batch.InstanceCount);
            stats.DrawCallCount++;
            stats.EstimatedDrawCallCount++;
            stats.InstancedBatchCount++;
            stats.VisibleMeshCount += batch.InstanceCount;
            stats.HighScaleVisiblePartInstanceCount += batch.InstanceCount;
            if (part.UsesVertexMaterialSlots) stats.BakedHighScalePartDraws++;
            stats.TriangleCount += (part.Mesh.Indices.Length / 3) * batch.InstanceCount;
        }
    }

    private HighScaleGpuBatchData EnsureHighScaleGpuBatch(
        GlInterface gl,
        HighScaleInstanceLayer3D layer,
        HighScaleBatchKey key,
        bool structuralDirty,
        HighScaleLodLevel3D lod,
        List<int> instanceIndices,
        Vector3 cameraPosition,
        ScenePerformanceOptions performance,
        RenderStats stats)
    {
        if (!_highScaleGpuBatches.TryGetValue(key, out var batch))
        {
            batch = new HighScaleGpuBatchData
            {
                TransformBuffer = gl.GenBuffer(),
                StateBuffer = gl.GenBuffer()
            };
            _highScaleGpuBatches[key] = batch;
        }

        var dynamicFadeState = performance.EnableHighScaleDynamicFadeState;
        var fadeVersion = dynamicFadeState ? QuantizeCameraForFade(cameraPosition) : 0;
        var rebuildNeeded = structuralDirty || !batch.Matches(instanceIndices);
        if (rebuildNeeded)
        {
            var uploadLimit = performance.HighScaleMaxTransformBatchUploadsPerFrame;
            if (uploadLimit > 0 && _highScaleTransformBatchUploadsThisFrame >= uploadLimit)
            {
                return batch;
            }

            RebuildHighScaleGpuBatch(gl, layer, instanceIndices, cameraPosition, batch, fadeVersion, dynamicFadeState, stats);
            _highScaleTransformBatchUploadsThisFrame++;
            return batch;
        }

        if (batch.StateVersion != layer.StateBuffer.Version ||
            batch.MaterialResolverVersion != layer.MaterialResolverVersion ||
            batch.LodPolicyVersion != layer.LodPolicy.Version ||
            batch.FadeVersion != fadeVersion)
        {
            var stateStart = Stopwatch.GetTimestamp();
            UpdateHighScaleStateBuffer(gl, layer, cameraPosition, batch, fadeVersion, dynamicFadeState, performance, stats);
            stats.HighScaleUploadMilliseconds += GetElapsedMilliseconds(stateStart);
        }

        return batch;
    }

    private void RebuildHighScaleGpuBatch(
        GlInterface gl,
        HighScaleInstanceLayer3D layer,
        List<int> instanceIndices,
        Vector3 cameraPosition,
        HighScaleGpuBatchData batch,
        int fadeVersion,
        bool dynamicFadeState,
        RenderStats stats)
    {
        batch.ResetCpuData();
        for (var i = 0; i < instanceIndices.Count; i++)
        {
            var instanceIndex = instanceIndices[i];
            var record = layer.Instances[instanceIndex];
            batch.Add(
                instanceIndex,
                record.Transform,
                record.MaterialVariantId,
                IsHighScaleVisible(record),
                ResolveHighScaleStateAlpha(layer, record, cameraPosition, dynamicFadeState));
        }

        var uploadStart = Stopwatch.GetTimestamp();
        gl.BindBuffer(GlArrayBuffer, batch.TransformBuffer);
        UploadFloats(gl, GlArrayBuffer, batch.TransformData, batch.TransformFloatCount, GlStaticDraw);
        gl.BindBuffer(GlArrayBuffer, batch.StateBuffer);
        UploadFloats(gl, GlArrayBuffer, batch.StateData, batch.StateFloatCount, GlDynamicDraw);
        stats.HighScaleUploadMilliseconds += GetElapsedMilliseconds(uploadStart);

        batch.StateVersion = layer.StateBuffer.Version;
        batch.MaterialResolverVersion = layer.MaterialResolverVersion;
        batch.LodPolicyVersion = layer.LodPolicy.Version;
        batch.FadeVersion = fadeVersion;
        batch.TransformBufferCapacityBytes = batch.TransformFloatCount * sizeof(float);
        batch.StateBufferCapacityBytes = batch.StateFloatCount * sizeof(float);
        stats.InstanceBufferUploads++;
        stats.StateBufferUploads++;
        stats.InstanceUploadBytes += batch.TransformBufferCapacityBytes;
        stats.StateUploadBytes += batch.StateBufferCapacityBytes;
    }

    private void UpdateHighScaleStateBuffer(
        GlInterface gl,
        HighScaleInstanceLayer3D layer,
        Vector3 cameraPosition,
        HighScaleGpuBatchData batch,
        int fadeVersion,
        bool dynamicFadeState,
        ScenePerformanceOptions performance,
        RenderStats stats)
    {
        if (batch.InstanceCount == 0)
        {
            batch.StateVersion = layer.StateBuffer.Version;
            batch.MaterialResolverVersion = layer.MaterialResolverVersion;
            batch.LodPolicyVersion = layer.LodPolicy.Version;
            batch.FadeVersion = fadeVersion;
            return;
        }

        var dirtyIndices = layer.StateBuffer.DirtyIndices;
        var resolverChanged = batch.MaterialResolverVersion != layer.MaterialResolverVersion;
        var lodPolicyChanged = batch.LodPolicyVersion != layer.LodPolicy.Version;
        var fadeChanged = batch.FadeVersion != fadeVersion;

        // Important: dirtyIndices is global for the layer, while this batch contains only
        // one visible chunk/LOD subset. The previous implementation compared the global
        // dirty count with this batch size and therefore forced a full state upload for
        // almost every visible batch under telemetry load. That made state upload scale
        // like visible part/batch work instead of changed logical instances.
        var forceFullUpdate = resolverChanged || lodPolicyChanged || fadeChanged || _bufferSubData is null ||
                              (dirtyIndices.Count == 0 && batch.StateVersion != layer.StateBuffer.Version);

        if (!forceFullUpdate)
        {
            batch.ResetDirtyOffsets();
            for (var i = 0; i < dirtyIndices.Count; i++)
            {
                var instanceIndex = dirtyIndices[i];
                if (!batch.TryGetOffset(instanceIndex, out var offset))
                {
                    continue;
                }

                var record = layer.Instances[instanceIndex];
                batch.WriteState(offset, record.MaterialVariantId, IsHighScaleVisible(record), ResolveHighScaleStateAlpha(layer, record, cameraPosition, dynamicFadeState));
                batch.AddDirtyOffset(offset);
            }

            // Decide full-vs-partial per batch after we know how many dirty instances
            // actually belong to this batch. This is the core fix for 10k/50k telemetry.
            forceFullUpdate = batch.DirtyOffsetCount > System.Math.Max(32, batch.InstanceCount / 3);
        }

        if (forceFullUpdate)
        {
            for (var offset = 0; offset < batch.InstanceCount; offset++)
            {
                var instanceIndex = batch.GetInstanceIndexAt(offset);
                var record = layer.Instances[instanceIndex];
                batch.WriteState(offset, record.MaterialVariantId, IsHighScaleVisible(record), ResolveHighScaleStateAlpha(layer, record, cameraPosition, dynamicFadeState));
            }

            gl.BindBuffer(GlArrayBuffer, batch.StateBuffer);
            if (_bufferSubData is not null && batch.StateBufferCapacityBytes >= batch.StateFloatCount * sizeof(float))
            {
                UploadFloatsSubData(GlArrayBuffer, 0, batch.StateData, 0, batch.StateFloatCount);
                stats.StateBufferSubDataUploads++;
            }
            else
            {
                UploadFloats(gl, GlArrayBuffer, batch.StateData, batch.StateFloatCount, GlDynamicDraw);
                batch.StateBufferCapacityBytes = batch.StateFloatCount * sizeof(float);
                stats.StateBufferUploads++;
            }

            stats.StateUploadBytes += batch.StateFloatCount * sizeof(float);
        }
        else if (batch.DirtyOffsetCount > 0)
        {
            gl.BindBuffer(GlArrayBuffer, batch.StateBuffer);
            batch.SortDirtyOffsets();

            var mergeGap = System.Math.Max(0, performance.HighScalePartialStateMergeGap);
            var rangeStart = batch.GetDirtyOffsetAt(0);
            var previous = rangeStart;
            for (var i = 1; i <= batch.DirtyOffsetCount; i++)
            {
                var current = i < batch.DirtyOffsetCount ? batch.GetDirtyOffsetAt(i) : -1;
                if (current >= 0 && current <= previous + 1 + mergeGap)
                {
                    previous = current;
                    continue;
                }

                var floatOffset = rangeStart * HighScaleStateFloatStride;
                var floatCount = (previous - rangeStart + 1) * HighScaleStateFloatStride;
                UploadFloatsSubData(GlArrayBuffer, floatOffset * sizeof(float), batch.StateData, floatOffset, floatCount);
                stats.StateBufferSubDataUploads++;
                stats.StateUploadBytes += floatCount * sizeof(float);
                rangeStart = current;
                previous = current;
            }
        }

        batch.StateVersion = layer.StateBuffer.Version;
        batch.MaterialResolverVersion = layer.MaterialResolverVersion;
        batch.LodPolicyVersion = layer.LodPolicy.Version;
        batch.FadeVersion = fadeVersion;
    }

    private void UploadHighScalePalette(GlInterface gl, HighScaleInstanceLayer3D layer, CompositePartTemplate3D part, ScenePerformanceOptions performance, RenderStats stats)
    {
        if (part.UsesVertexMaterialSlots && _paletteTexture != 0)
        {
            UploadFloat(_uniform1f, _meshUsePaletteTextureLocation, 1f);
            UploadHighScalePaletteTexture(gl, layer, part);
            return;
        }

        UploadFloat(_uniform1f, _meshUsePaletteTextureLocation, 0f);
        var count = ResolveActiveVariantSlotCount(layer);
        for (var i = 0; i < count; i++)
        {
            UploadColor(_uniform4f, _meshVariantColorLocations[i], layer.Template.ResolveColor(part, i));
        }
    }

    private void UploadHighScalePaletteTexture(GlInterface gl, HighScaleInstanceLayer3D layer, CompositePartTemplate3D part)
    {
        var variantCount = ResolveActiveVariantSlotCount(layer);
        var slotCount = System.Math.Clamp(part.MaterialSlotBaseColors.Count, 1, 64);
        var required = variantCount * slotCount * 4;
        if (_paletteUploadBuffer.Length < required)
        {
            _paletteUploadBuffer = new byte[required];
        }

        for (var variant = 0; variant < variantCount; variant++)
        {
            for (var slot = 0; slot < slotCount; slot++)
            {
                var baseColor = slot < part.MaterialSlotBaseColors.Count ? part.MaterialSlotBaseColors[slot] : part.BaseColor;
                var color = layer.Template.ResolveColor(slot, baseColor, variant);
                var offset = ((variant * slotCount) + slot) * 4;
                _paletteUploadBuffer[offset] = ToByte(color.R);
                _paletteUploadBuffer[offset + 1] = ToByte(color.G);
                _paletteUploadBuffer[offset + 2] = ToByte(color.B);
                _paletteUploadBuffer[offset + 3] = ToByte(color.A);
            }
        }

        var handle = GCHandle.Alloc(_paletteUploadBuffer, GCHandleType.Pinned);
        try
        {
            gl.ActiveTexture(GlTexture1);
            gl.BindTexture(GlTexture2D, _paletteTexture);
            gl.TexParameteri(GlTexture2D, GlTextureMinFilter, GlNearest);
            gl.TexParameteri(GlTexture2D, GlTextureMagFilter, GlNearest);
            gl.TexParameteri(GlTexture2D, GlTextureWrapS, GlClampToEdge);
            gl.TexParameteri(GlTexture2D, GlTextureWrapT, GlClampToEdge);
            gl.TexImage2D(GlTexture2D, 0, GlRgba, slotCount, variantCount, 0, GlRgba, GlUnsignedByte, handle.AddrOfPinnedObject());
            if (_meshPaletteTextureLocation >= 0) _uniform1i?.Invoke(_meshPaletteTextureLocation, 1);
            UploadFloat(_uniform1f, _meshPaletteWidthLocation, slotCount);
            UploadFloat(_uniform1f, _meshPaletteHeightLocation, variantCount);
            gl.ActiveTexture(GlTexture0);
        }
        finally
        {
            handle.Free();
        }
    }

    private static byte ToByte(float value)
        => (byte)System.Math.Clamp((int)System.MathF.Round(System.Math.Clamp(value, 0f, 1f) * 255f), 0, 255);

    private static int ResolveActiveVariantSlotCount(HighScaleInstanceLayer3D layer)
    {
        var max = 0;
        foreach (var id in layer.Template.MaterialVariants.Keys)
        {
            if (id > max) max = id;
        }

        return System.Math.Clamp(max + 1, 1, MaxHighScaleMaterialVariants);
    }

    private static bool IsHighScaleVisible(InstanceRecord3D record)
        => (record.Flags & InstanceFlags3D.Visible) != 0;

    private static float ResolveHighScaleStateAlpha(HighScaleInstanceLayer3D layer, InstanceRecord3D record, Vector3 cameraPosition, bool dynamicFadeState)
    {
        if (!IsHighScaleVisible(record)) return 0f;
        return dynamicFadeState ? layer.LodPolicy.ResolveFadeAlpha(cameraPosition, record.Transform) : 1f;
    }

    private static int QuantizeCameraForFade(Vector3 cameraPosition)
    {
        // Fade alpha is allowed to update less frequently than raw camera motion.
        // This keeps retained transform batches stable while still preventing abrupt draw-distance popping.
        const float cell = 2f;
        return HashCode.Combine(
            (int)System.MathF.Floor(cameraPosition.X / cell),
            (int)System.MathF.Floor(cameraPosition.Y / cell),
            (int)System.MathF.Floor(cameraPosition.Z / cell));
    }

    private static float ResolveDistanceAlpha(Scene3D scene, Matrix4x4 model)
    {
        var drawDistance = scene.Performance.DrawDistance;
        if (drawDistance <= 0f || float.IsPositiveInfinity(drawDistance))
        {
            return 1f;
        }

        var camera = scene.Camera.Position;
        var pos = new Vector3(model.M41, model.M42, model.M43);
        var distance = Vector3.Distance(camera, pos);
        if (distance > drawDistance)
        {
            return 0f;
        }

        if (!scene.Performance.EnableDistanceFade || scene.Performance.DistanceFadeBand <= 0.001f)
        {
            return 1f;
        }

        var fadeStart = System.MathF.Max(0f, drawDistance - scene.Performance.DistanceFadeBand);
        if (distance <= fadeStart)
        {
            return 1f;
        }

        return System.Math.Clamp(1f - ((distance - fadeStart) / System.MathF.Max(scene.Performance.DistanceFadeBand, 0.001f)), 0f, 1f);
    }

    private static ColorRgba ApplyDistanceAlpha(ColorRgba color, float alpha)
        => alpha >= 0.999f ? color : new ColorRgba(color.R, color.G, color.B, color.A * alpha);

    private static ColorRgba ResolveColor(Object3D obj)
    {
        var color = obj.Material.EffectiveColor;
        if (obj.IsEffectivelyHovered) color = color.BlendTowards(ColorRgba.White, 0.10f);
        if (obj.IsEffectivelySelected) color = color.BlendTowards(ColorRgba.White, 0.22f);
        return color;
    }

    private void DrawControlPlanes(GlInterface gl, Scene3D scene, Matrix4x4 viewProjection, RenderStats stats)
    {
        var planes = new List<(ControlPlane3D Plane, float Depth)>();
        foreach (var obj in scene.Registry.AllObjects)
        {
            if (obj is not ControlPlane3D plane || !plane.IsVisible || plane.Snapshot is null) continue;
            var corners = ControlPlaneGeometry.GetWorldCorners(plane, scene.Camera);
            var depth = 0f;
            for (var i = 0; i < corners.Length; i++) depth += Vector3.DistanceSquared(scene.Camera.Position, corners[i]);
            planes.Add((plane, depth / 4f));
        }
        if (planes.Count == 0) return;

        gl.Enable(GlBlend);
        _blendFunc?.Invoke(GlSrcAlpha, GlOneMinusSrcAlpha);
        _depthMask?.Invoke(0);
        gl.UseProgram(_texturedProgram);
        gl.ActiveTexture(GlTexture0);
        if (_textureSamplerLocation >= 0) _uniform1i?.Invoke(_textureSamplerLocation, 0);
        UploadMatrix(_uniformMatrix4fv, _textureViewProjLocation, viewProjection, _matrixUploadBuffer);
        planes.Sort((a, b) => b.Depth.CompareTo(a.Depth));

        foreach (var (plane, _) in planes)
        {
            var texture = EnsureControlTexture(gl, plane, stats);
            if (texture is null) continue;
            var corners = ControlPlaneGeometry.GetWorldCorners(plane, scene.Camera);
            BuildWorldControlVertices(corners, _controlVertexData);
            gl.BindTexture(GlTexture2D, texture.TextureId);
            gl.BindBuffer(GlArrayBuffer, _controlVertexBuffer);
            UploadFloats(gl, GlArrayBuffer, _controlVertexData, _controlVertexData.Length, GlDynamicDraw);
            gl.BindBuffer(GlElementArrayBuffer, _controlIndexBuffer);
            gl.EnableVertexAttribArray(_texturePositionLocation);
            gl.VertexAttribPointer(_texturePositionLocation, 3, GlFloat, 0, sizeof(float) * 5, IntPtr.Zero);
            gl.EnableVertexAttribArray(_textureUvLocation);
            gl.VertexAttribPointer(_textureUvLocation, 2, GlFloat, 0, sizeof(float) * 5, new IntPtr(sizeof(float) * 3));
            gl.DrawElements(GlTriangles, 6, GlUnsignedInt, IntPtr.Zero);
            stats.ControlPlaneCount++;
            stats.DrawCallCount++;
        }
        _depthMask?.Invoke(1);
        _disable?.Invoke(GlBlend);
    }

    private MeshGpuResource EnsureMeshResource(GlInterface gl, string id, int geometryVersion, Mesh3D mesh, RenderStats stats)
    {
        if (_meshResources.TryGetValue(id, out var resource) && resource.GeometryVersion == geometryVersion) return resource;
        resource?.Dispose(gl);
        resource = new MeshGpuResource
        {
            GeometryVersion = geometryVersion,
            VertexBuffer = gl.GenBuffer(),
            NormalBuffer = gl.GenBuffer(),
            MaterialSlotBuffer = gl.GenBuffer(),
            IndexBuffer = gl.GenBuffer(),
            IndexCount = mesh.Indices.Length
        };
        gl.BindBuffer(GlArrayBuffer, resource.VertexBuffer);
        UploadVector3(gl, GlArrayBuffer, mesh.Positions, GlStaticDraw);
        gl.BindBuffer(GlArrayBuffer, resource.NormalBuffer);
        UploadVector3(gl, GlArrayBuffer, GetNormalsOrDefault(mesh), GlStaticDraw);
        gl.BindBuffer(GlArrayBuffer, resource.MaterialSlotBuffer);
        UploadFloats(gl, GlArrayBuffer, GetMaterialSlotsOrDefault(mesh), mesh.Positions.Length, GlStaticDraw);
        gl.BindBuffer(GlElementArrayBuffer, resource.IndexBuffer);
        UploadInts(gl, GlElementArrayBuffer, mesh.Indices, GlStaticDraw);
        _meshResources[id] = resource;
        stats.DirtyMeshUploads++;
        return resource;
    }

    private ControlTextureResource? EnsureControlTexture(GlInterface gl, ControlPlane3D plane, RenderStats stats)
    {
        var snapshot = plane.Snapshot;
        if (snapshot is null) return null;
        if (!_controlTextures.TryGetValue(plane.Id, out var resource))
        {
            resource = new ControlTextureResource { TextureId = gl.GenTexture(), SnapshotVersion = -1 };
            _controlTextures[plane.Id] = resource;
        }
        if (resource.SnapshotVersion == plane.SnapshotVersion) return resource;

        var pixelWidth = System.Math.Max(plane.RenderPixelWidth, 1);
        var pixelHeight = System.Math.Max(plane.RenderPixelHeight, 1);
        var stride = pixelWidth * 4;
        var bufferSize = stride * pixelHeight;
        var bgraPixels = new byte[bufferSize];
        var bgraHandle = GCHandle.Alloc(bgraPixels, GCHandleType.Pinned);
        try { snapshot.CopyPixels(new PixelRect(0, 0, pixelWidth, pixelHeight), bgraHandle.AddrOfPinnedObject(), bufferSize, stride); }
        finally { bgraHandle.Free(); }

        var rgbaPixels = new byte[bufferSize];
        for (var i = 0; i < bufferSize; i += 4)
        {
            rgbaPixels[i] = bgraPixels[i + 2];
            rgbaPixels[i + 1] = bgraPixels[i + 1];
            rgbaPixels[i + 2] = bgraPixels[i];
            rgbaPixels[i + 3] = bgraPixels[i + 3];
        }
        var rgbaHandle = GCHandle.Alloc(rgbaPixels, GCHandleType.Pinned);
        try
        {
            gl.BindTexture(GlTexture2D, resource.TextureId);
            gl.TexParameteri(GlTexture2D, GlTextureMinFilter, GlLinear);
            gl.TexParameteri(GlTexture2D, GlTextureMagFilter, GlLinear);
            gl.TexParameteri(GlTexture2D, GlTextureWrapS, GlClampToEdge);
            gl.TexParameteri(GlTexture2D, GlTextureWrapT, GlClampToEdge);
            gl.TexImage2D(GlTexture2D, 0, GlRgba, pixelWidth, pixelHeight, 0, GlRgba, GlUnsignedByte, rgbaHandle.AddrOfPinnedObject());
            resource.SnapshotVersion = plane.SnapshotVersion;
            resource.Width = pixelWidth;
            resource.Height = pixelHeight;
            stats.DirtyTextureUploads++;
            stats.TextureUploadBytes += bufferSize;
        }
        finally { rgbaHandle.Free(); }
        return resource;
    }

    private static void BuildWorldControlVertices(Vector3[] worldCorners, float[] vertexData)
    {
        for (var i = 0; i < 4; i++)
        {
            var baseIndex = i * 5;
            vertexData[baseIndex] = worldCorners[i].X;
            vertexData[baseIndex + 1] = worldCorners[i].Y;
            vertexData[baseIndex + 2] = worldCorners[i].Z;
        }
        vertexData[3] = 0f; vertexData[4] = 0f;
        vertexData[8] = 1f; vertexData[9] = 0f;
        vertexData[13] = 1f; vertexData[14] = 1f;
        vertexData[18] = 0f; vertexData[19] = 1f;
    }

    private static void UploadFloats(GlInterface gl, int target, float[] data, int count, int usage)
    {
        if (count <= 0) return;
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try { gl.BufferData(target, new IntPtr(count * sizeof(float)), handle.AddrOfPinnedObject(), usage); }
        finally { handle.Free(); }
    }

    private void UploadFloatsSubData(int target, int byteOffset, float[] data, int floatOffset, int count)
    {
        if (count <= 0 || _bufferSubData is null) return;
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var source = IntPtr.Add(handle.AddrOfPinnedObject(), floatOffset * sizeof(float));
            _bufferSubData(target, new IntPtr(byteOffset), new IntPtr(count * sizeof(float)), source);
        }
        finally
        {
            handle.Free();
        }
    }

    private static void UploadVector3(GlInterface gl, int target, Vector3[] data, int usage)
    {
        var floats = new float[data.Length * 3];
        for (var i = 0; i < data.Length; i++)
        {
            var baseIndex = i * 3;
            floats[baseIndex] = data[i].X;
            floats[baseIndex + 1] = data[i].Y;
            floats[baseIndex + 2] = data[i].Z;
        }
        UploadFloats(gl, target, floats, floats.Length, usage);
    }

    private static void UploadInts(GlInterface gl, int target, int[] data, int usage)
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try { gl.BufferData(target, new IntPtr(data.Length * sizeof(int)), handle.AddrOfPinnedObject(), usage); }
        finally { handle.Free(); }
    }

    private void UploadLighting(Scene3D scene)
    {
        UploadVector3(_uniform3f, _meshAmbientLightLocation, new Vector3(0.28f, 0.28f, 0.28f));
        var directionalColor = Vector3.Zero;
        var directionalDirection = Vector3.Normalize(new Vector3(-0.35f, -0.75f, -0.55f));
        foreach (var light in scene.Lights)
        {
            if (!light.IsEnabled) continue;
            directionalDirection = light.Direction.LengthSquared() < 0.000001f ? directionalDirection : Vector3.Normalize(light.Direction);
            directionalColor = new Vector3(light.Color.R, light.Color.G, light.Color.B) * light.Intensity;
            break;
        }
        UploadVector3(_uniform3f, _meshDirectionalLightDirectionLocation, directionalDirection);
        UploadVector3(_uniform3f, _meshDirectionalLightColorLocation, directionalColor);

        var pointPosition = new Vector4(0f, 0f, 0f, 1f);
        var pointColor = new Vector4(0f, 0f, 0f, 0f);
        foreach (var light in scene.PointLights)
        {
            if (!light.IsEnabled) continue;
            pointPosition = new Vector4(light.Position, light.Range);
            pointColor = new Vector4(light.Color.R * light.Intensity, light.Color.G * light.Intensity, light.Color.B * light.Intensity, 1f);
            break;
        }
        UploadVector4(_uniform4f, _meshPointLightPositionLocation, pointPosition);
        UploadVector4(_uniform4f, _meshPointLightColorLocation, pointColor);
    }

    private static float[] GetMaterialSlotsOrDefault(Mesh3D mesh)
    {
        if (mesh.MaterialSlots.Length == mesh.Positions.Length) return mesh.MaterialSlots;
        return new float[mesh.Positions.Length];
    }

    private static Vector3[] GetNormalsOrDefault(Mesh3D mesh)
    {
        if (mesh.Normals.Length == mesh.Positions.Length) return mesh.Normals;
        var normals = new Vector3[mesh.Positions.Length];
        for (var i = 0; i < normals.Length; i++) normals[i] = Vector3.UnitZ;
        return normals;
    }

    private static void UploadVector3(GlUniform3fDelegate? uniform3f, int location, Vector3 value)
    {
        if (location >= 0) uniform3f?.Invoke(location, value.X, value.Y, value.Z);
    }

    private static void UploadVector4(GlUniform4fDelegate? uniform4f, int location, Vector4 value)
    {
        if (location >= 0) uniform4f?.Invoke(location, value.X, value.Y, value.Z, value.W);
    }

    private static void UploadFloat(GlUniform1fDelegate? uniform1f, int location, float value)
    {
        if (location >= 0) uniform1f?.Invoke(location, value);
    }

    private static void UploadColor(GlUniform4fDelegate? uniform4f, int location, ColorRgba color)
    {
        if (location >= 0) uniform4f?.Invoke(location, color.R, color.G, color.B, color.A);
    }

    private static void UploadMatrix(GlUniformMatrix4fvDelegate? uniformMatrix4fv, int location, Matrix4x4 matrix, float[] buffer)
    {
        if (location < 0 || uniformMatrix4fv is null) return;
        WriteMatrix(buffer, 0, matrix);
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try { uniformMatrix4fv(location, 1, 0, handle.AddrOfPinnedObject()); }
        finally { handle.Free(); }
    }

    private static void UploadMatrixFromInstanceData(GlUniformMatrix4fvDelegate? uniformMatrix4fv, int location, float[] data, int offset, float[] buffer)
    {
        if (location < 0 || uniformMatrix4fv is null) return;
        Array.Copy(data, offset, buffer, 0, 16);
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try { uniformMatrix4fv(location, 1, 0, handle.AddrOfPinnedObject()); }
        finally { handle.Free(); }
    }

    private static void WriteMatrix(float[] buffer, int offset, Matrix4x4 matrix)
    {
        buffer[offset] = matrix.M11; buffer[offset + 1] = matrix.M12; buffer[offset + 2] = matrix.M13; buffer[offset + 3] = matrix.M14;
        buffer[offset + 4] = matrix.M21; buffer[offset + 5] = matrix.M22; buffer[offset + 6] = matrix.M23; buffer[offset + 7] = matrix.M24;
        buffer[offset + 8] = matrix.M31; buffer[offset + 9] = matrix.M32; buffer[offset + 10] = matrix.M33; buffer[offset + 11] = matrix.M34;
        buffer[offset + 12] = matrix.M41; buffer[offset + 13] = matrix.M42; buffer[offset + 14] = matrix.M43; buffer[offset + 15] = matrix.M44;
    }


    private static double GetElapsedMilliseconds(long startTimestamp)
    {
        return (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;
    }
    private static int CreateProgram(GlInterface gl, string vertexSource, string fragmentSource, params (int Location, string Name)[] attributes)
    {
        var vertexShader = gl.CreateShader(GlVertexShader);
        var vertexError = gl.CompileShaderAndGetError(vertexShader, vertexSource);
        if (!string.IsNullOrWhiteSpace(vertexError)) throw new InvalidOperationException($"Vertex shader compilation failed: {vertexError}");
        var fragmentShader = gl.CreateShader(GlFragmentShader);
        var fragmentError = gl.CompileShaderAndGetError(fragmentShader, fragmentSource);
        if (!string.IsNullOrWhiteSpace(fragmentError)) throw new InvalidOperationException($"Fragment shader compilation failed: {fragmentError}");
        var program = gl.CreateProgram();
        gl.AttachShader(program, vertexShader);
        gl.AttachShader(program, fragmentShader);
        foreach (var (location, name) in attributes) gl.BindAttribLocationString(program, location, name);
        var linkError = gl.LinkProgramAndGetError(program);
        gl.DeleteShader(vertexShader);
        gl.DeleteShader(fragmentShader);
        if (!string.IsNullOrWhiteSpace(linkError)) throw new InvalidOperationException($"Program link failed: {linkError}");
        return program;
    }

    private static T? LoadDelegate<T>(GlInterface gl, string procName) where T : class
    {
        var proc = gl.GetProcAddress(procName);
        return proc == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer(proc, typeof(T)) as T;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlBlendFuncDelegate(int sfactor, int dfactor);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlDepthMaskDelegate(byte flag);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlDisableDelegate(int cap);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlUniform1iDelegate(int location, int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlUniform1fDelegate(int location, float value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlUniform3fDelegate(int location, float x, float y, float z);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlUniform4fDelegate(int location, float x, float y, float z, float w);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlUniformMatrix4fvDelegate(int location, int count, byte transpose, IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlVertexAttribDivisorDelegate(int index, int divisor);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlDrawElementsInstancedDelegate(int mode, int count, int type, IntPtr indices, int instanceCount);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void GlBufferSubDataDelegate(int target, IntPtr offset, IntPtr size, IntPtr data);


    private sealed class HighScaleChunkFramePlan
    {
        [ThreadStatic]
        private static HighScaleChunkFramePlan? _shared;

        public static HighScaleChunkFramePlan Shared => _shared ??= new HighScaleChunkFramePlan();

        public readonly List<int> Detailed = new(256);
        public readonly List<int> Simplified = new(1024);
        public readonly List<int> Proxy = new(1024);
        public readonly List<int> Billboard = new(1024);

        public void Reset()
        {
            Detailed.Clear();
            Simplified.Clear();
            Proxy.Clear();
            Billboard.Clear();
        }
    }
    private sealed class MeshGpuResource
    {
        public int GeometryVersion { get; init; }
        public int VertexBuffer { get; init; }
        public int NormalBuffer { get; init; }
        public int MaterialSlotBuffer { get; init; }
        public int IndexBuffer { get; init; }
        public int IndexCount { get; init; }
        public void Dispose(GlInterface gl)
        {
            if (NormalBuffer != 0) gl.DeleteBuffer(NormalBuffer);
            if (MaterialSlotBuffer != 0) gl.DeleteBuffer(MaterialSlotBuffer);
            if (VertexBuffer != 0) gl.DeleteBuffer(VertexBuffer);
            if (IndexBuffer != 0) gl.DeleteBuffer(IndexBuffer);
        }
    }

    private sealed class MeshBatchData
    {
        private float[] _data = new float[InstanceFloatStride * 64];
        public MeshBatchData(string meshKey, Mesh3D mesh, int lightingEnabled) { MeshKey = meshKey; Mesh = mesh; LightingEnabled = lightingEnabled; }
        public string MeshKey { get; }
        public Mesh3D Mesh { get; set; }
        public int LightingEnabled { get; }
        public int InstanceCount { get; private set; }
        public int FloatCount => InstanceCount * InstanceFloatStride;
        public float[] Data => _data;
        public void Reset() => InstanceCount = 0;
        public void Add(Matrix4x4 model, ColorRgba color)
        {
            EnsureCapacity((InstanceCount + 1) * InstanceFloatStride);
            var offset = InstanceCount * InstanceFloatStride;
            WriteMatrix(_data, offset, model);
            _data[offset + 16] = color.R; _data[offset + 17] = color.G; _data[offset + 18] = color.B; _data[offset + 19] = color.A;
            InstanceCount++;
        }
        private void EnsureCapacity(int required)
        {
            if (_data.Length >= required) return;
            var next = _data.Length;
            while (next < required) next *= 2;
            Array.Resize(ref _data, next);
        }
    }

    private readonly struct HighScaleBatchKey : IEquatable<HighScaleBatchKey>
    {
        private readonly string _layerId;
        private readonly HighScaleChunkKey3D _chunkKey;
        private readonly HighScaleLodLevel3D _lod;

        public HighScaleBatchKey(string layerId, HighScaleChunkKey3D chunkKey, HighScaleLodLevel3D lod)
        {
            _layerId = layerId;
            _chunkKey = chunkKey;
            _lod = lod;
        }

        public bool Equals(HighScaleBatchKey other)
            => string.Equals(_layerId, other._layerId, StringComparison.Ordinal) &&
               _chunkKey.Equals(other._chunkKey) &&
               _lod == other._lod;

        public override bool Equals(object? obj) => obj is HighScaleBatchKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(_layerId, _chunkKey, _lod);
    }

    private sealed class HighScaleGpuBatchData
    {
        private float[] _transformData = new float[HighScaleTransformFloatStride * 128];
        private float[] _stateData = new float[HighScaleStateFloatStride * 128];
        private int[] _instanceIndices = new int[128];
        private readonly Dictionary<int, int> _offsetByInstanceIndex = new();
        private readonly List<int> _dirtyOffsets = new(256);

        public int TransformBuffer { get; init; }
        public int StateBuffer { get; init; }
        public int StateVersion { get; set; }
        public int MaterialResolverVersion { get; set; }
        public int LodPolicyVersion { get; set; }
        public int FadeVersion { get; set; }
        public int TransformBufferCapacityBytes { get; set; }
        public int StateBufferCapacityBytes { get; set; }
        public int InstanceCount { get; private set; }
        public int TransformFloatCount => InstanceCount * HighScaleTransformFloatStride;
        public int StateFloatCount => InstanceCount * HighScaleStateFloatStride;
        public float[] TransformData => _transformData;
        public float[] StateData => _stateData;
        public int DirtyOffsetCount => _dirtyOffsets.Count;

        public bool Matches(IReadOnlyList<int> indices)
        {
            if (InstanceCount != indices.Count)
            {
                return false;
            }

            for (var i = 0; i < indices.Count; i++)
            {
                if (_instanceIndices[i] != indices[i])
                {
                    return false;
                }
            }

            return true;
        }

        public void ResetCpuData()
        {
            InstanceCount = 0;
            _offsetByInstanceIndex.Clear();
            _dirtyOffsets.Clear();
        }

        public void Add(int instanceIndex, Matrix4x4 model, int materialVariantId, bool visible, float fadeAlpha)
        {
            EnsureCapacity(InstanceCount + 1);
            _instanceIndices[InstanceCount] = instanceIndex;
            _offsetByInstanceIndex[instanceIndex] = InstanceCount;

            var transformOffset = InstanceCount * HighScaleTransformFloatStride;
            WriteMatrix(_transformData, transformOffset, model);
            WriteState(InstanceCount, materialVariantId, visible, fadeAlpha);
            InstanceCount++;
        }

        public int GetInstanceIndexAt(int offset) => _instanceIndices[offset];

        public bool TryGetOffset(int instanceIndex, out int offset) => _offsetByInstanceIndex.TryGetValue(instanceIndex, out offset);

        public void WriteState(int offset, int materialVariantId, bool visible, float fadeAlpha)
        {
            var stateOffset = offset * HighScaleStateFloatStride;
            _stateData[stateOffset] = System.Math.Clamp(materialVariantId, 0, MaxHighScaleMaterialVariants - 1);
            _stateData[stateOffset + 1] = visible ? 1f : 0f;
            _stateData[stateOffset + 2] = System.Math.Clamp(fadeAlpha, 0f, 1f);
            _stateData[stateOffset + 3] = 0f;
        }

        public void ResetDirtyOffsets() => _dirtyOffsets.Clear();

        public void AddDirtyOffset(int offset) => _dirtyOffsets.Add(offset);

        public void SortDirtyOffsets() => _dirtyOffsets.Sort();

        public int GetDirtyOffsetAt(int index) => _dirtyOffsets[index];

        public void Dispose(GlInterface gl)
        {
            if (TransformBuffer != 0) gl.DeleteBuffer(TransformBuffer);
            if (StateBuffer != 0) gl.DeleteBuffer(StateBuffer);
        }

        private void EnsureCapacity(int requiredInstances)
        {
            if (_instanceIndices.Length >= requiredInstances) return;
            var next = _instanceIndices.Length;
            while (next < requiredInstances) next *= 2;
            Array.Resize(ref _instanceIndices, next);
            Array.Resize(ref _transformData, next * HighScaleTransformFloatStride);
            Array.Resize(ref _stateData, next * HighScaleStateFloatStride);
        }
    }

    private sealed class ControlTextureResource
    {
        public int TextureId { get; init; }
        public int SnapshotVersion { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public void Dispose(GlInterface gl) { if (TextureId != 0) gl.DeleteTexture(TextureId); }
    }

    private const string MeshVertexSource = @"attribute vec3 aPosition;
attribute vec3 aNormal;
attribute vec4 aInstanceModel0;
attribute vec4 aInstanceModel1;
attribute vec4 aInstanceModel2;
attribute vec4 aInstanceModel3;
attribute vec4 aInstanceColor;
attribute vec4 aInstanceState;
attribute float aMaterialSlot;
uniform mat4 uModel;
uniform mat4 uPartLocal;
uniform mat4 uViewProj;
uniform vec4 uColor;
uniform float uUseInstancing;
uniform float uUsePartLocal;
uniform float uUseHighScaleState;
uniform float uUsePaletteTexture;
uniform vec4 uVariantColors[32];
varying vec3 vWorldPos;
varying vec3 vNormal;
varying vec4 vColor;
varying float vVariantIndex;
varying float vMaterialSlot;
varying float vUsePaletteTexture;
void main()
{
    mat4 instanceModel = mat4(aInstanceModel0, aInstanceModel1, aInstanceModel2, aInstanceModel3);
    mat4 model = uUseInstancing > 0.5 ? instanceModel : uModel;
    if (uUsePartLocal > 0.5) model = uPartLocal * model;
    vec4 world = model * vec4(aPosition, 1.0);
    vWorldPos = world.xyz;
    vNormal = normalize(mat3(model) * aNormal);
    vVariantIndex = 0.0;
    vMaterialSlot = aMaterialSlot;
    vUsePaletteTexture = 0.0;
    if (uUseHighScaleState > 0.5)
    {
        float variantIndex = clamp(aInstanceState.x, 0.0, 31.0);
        vVariantIndex = floor(variantIndex + 0.5);
        vUsePaletteTexture = uUsePaletteTexture;
        if (uUsePaletteTexture > 0.5)
        {
            vColor = vec4(1.0, 1.0, 1.0, aInstanceState.y * aInstanceState.z);
        }
        else
        {
            int variantUniformIndex = int(vVariantIndex);
            vec4 stateColor = uVariantColors[variantUniformIndex];
            stateColor.a *= aInstanceState.y * aInstanceState.z;
            vColor = stateColor;
        }
    }
    else
    {
        vColor = uUseInstancing > 0.5 ? aInstanceColor : uColor;
    }
    gl_Position = uViewProj * world;
}";

    private const string MeshFragmentSource = @"#ifdef GL_ES
precision mediump float;
#endif
uniform float uLightingEnabled;
uniform vec3 uAmbientLight;
uniform vec3 uDirectionalLightDirection;
uniform vec3 uDirectionalLightColor;
uniform vec4 uPointLightPosition;
uniform vec4 uPointLightColor;
uniform sampler2D uPaletteTexture;
uniform float uPaletteWidth;
uniform float uPaletteHeight;
varying vec3 vWorldPos;
varying vec3 vNormal;
varying vec4 vColor;
varying float vVariantIndex;
varying float vMaterialSlot;
varying float vUsePaletteTexture;
void main()
{
    vec4 materialColor = vColor;
    if (vUsePaletteTexture > 0.5)
    {
        float slot = clamp(floor(vMaterialSlot + 0.5), 0.0, max(uPaletteWidth - 1.0, 0.0));
        float variant = clamp(floor(vVariantIndex + 0.5), 0.0, max(uPaletteHeight - 1.0, 0.0));
        vec2 uv = vec2((slot + 0.5) / max(uPaletteWidth, 1.0), (variant + 0.5) / max(uPaletteHeight, 1.0));
        vec4 paletteColor = texture2D(uPaletteTexture, uv);
        materialColor = vec4(paletteColor.rgb, paletteColor.a * vColor.a);
    }
    if (materialColor.a <= 0.001) discard;
    if (materialColor.a < 0.999)
    {
        float threshold = mod(floor(gl_FragCoord.x) + floor(gl_FragCoord.y), 4.0) * 0.25;
        if (threshold > materialColor.a) discard;
    }
    vec3 outColor = materialColor.rgb;
    if (uLightingEnabled > 0.5)
    {
        vec3 n = normalize(vNormal);
        vec3 light = uAmbientLight;
        vec3 dir = normalize(-uDirectionalLightDirection);
        light += max(dot(n, dir), 0.0) * uDirectionalLightColor;
        if (uPointLightColor.a > 0.5)
        {
            vec3 toPoint = uPointLightPosition.xyz - vWorldPos;
            float dist = length(toPoint);
            float att = clamp(1.0 - dist / max(uPointLightPosition.w, 0.01), 0.0, 1.0);
            light += max(dot(n, normalize(toPoint)), 0.0) * uPointLightColor.rgb * att * att;
        }
        outColor *= clamp(light, 0.0, 2.0);
    }
    gl_FragColor = vec4(outColor, materialColor.a);
}";

    private const string TexturedVertexSource = @"attribute vec3 aPosition;
attribute vec2 aTexCoord;
uniform mat4 uViewProj;
varying vec2 vTexCoord;
void main()
{
    vTexCoord = aTexCoord;
    gl_Position = uViewProj * vec4(aPosition, 1.0);
}";

    private const string TexturedFragmentSource = @"#ifdef GL_ES
precision mediump float;
#endif
uniform sampler2D uTexture;
varying vec2 vTexCoord;
void main()
{
    gl_FragColor = texture2D(uTexture, vTexCoord);
}";
}
