using System;
using System.Collections.Generic;
using System.Linq;
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

internal sealed class OpenGlSceneRenderer
{
    private const int GlColorBufferBit = 0x00004000;
    private const int GlDepthBufferBit = 0x00000100;
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
    private const int GlTextureMinFilter = 0x2801;
    private const int GlTextureMagFilter = 0x2800;
    private const int GlTextureWrapS = 0x2802;
    private const int GlTextureWrapT = 0x2803;
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

    private readonly Dictionary<string, MeshGpuResource> _meshResources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ControlTextureResource> _controlTextures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MeshBatchData> _meshBatches = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HighScaleGpuBatchData> _highScaleGpuBatches = new(StringComparer.Ordinal);
    private readonly float[] _matrixUploadBuffer = new float[16];
    private readonly float[] _controlVertexData = new float[20];
    private int _lastSweptRegistryVersion = -1;

    private int _meshProgram;
    private int _texturedProgram;
    private int _meshPositionLocation;
    private int _meshNormalLocation;
    private int _meshInstanceModel0Location;
    private int _meshInstanceModel1Location;
    private int _meshInstanceModel2Location;
    private int _meshInstanceModel3Location;
    private int _meshInstanceColorLocation;
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
    private int _texturePositionLocation;
    private int _textureUvLocation;
    private int _textureSamplerLocation;
    private int _textureViewProjLocation;
    private int _controlVertexBuffer;
    private int _controlIndexBuffer;
    private int _meshInstanceBuffer;
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
        _supportsInstancing = _vertexAttribDivisor is not null && _drawElementsInstanced is not null;

        _meshProgram = CreateProgram(gl, MeshVertexSource, MeshFragmentSource,
            (0, "aPosition"), (1, "aNormal"), (2, "aInstanceModel0"), (3, "aInstanceModel1"),
            (4, "aInstanceModel2"), (5, "aInstanceModel3"), (6, "aInstanceColor"));
        _meshPositionLocation = gl.GetAttribLocationString(_meshProgram, "aPosition");
        _meshNormalLocation = gl.GetAttribLocationString(_meshProgram, "aNormal");
        _meshInstanceModel0Location = gl.GetAttribLocationString(_meshProgram, "aInstanceModel0");
        _meshInstanceModel1Location = gl.GetAttribLocationString(_meshProgram, "aInstanceModel1");
        _meshInstanceModel2Location = gl.GetAttribLocationString(_meshProgram, "aInstanceModel2");
        _meshInstanceModel3Location = gl.GetAttribLocationString(_meshProgram, "aInstanceModel3");
        _meshInstanceColorLocation = gl.GetAttribLocationString(_meshProgram, "aInstanceColor");
        _meshColorLocation = gl.GetUniformLocationString(_meshProgram, "uColor");
        _meshUseInstancingLocation = gl.GetUniformLocationString(_meshProgram, "uUseInstancing");
        _meshLightingEnabledLocation = gl.GetUniformLocationString(_meshProgram, "uLightingEnabled");
        _meshModelLocation = gl.GetUniformLocationString(_meshProgram, "uModel");
        _meshViewProjLocation = gl.GetUniformLocationString(_meshProgram, "uViewProj");
        _meshPartLocalLocation = gl.GetUniformLocationString(_meshProgram, "uPartLocal");
        _meshUsePartLocalLocation = gl.GetUniformLocationString(_meshProgram, "uUsePartLocal");
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
        _controlVertexBuffer = gl.GenBuffer();
        _controlIndexBuffer = gl.GenBuffer();
        gl.BindBuffer(GlElementArrayBuffer, _controlIndexBuffer);
        UploadInts(gl, GlElementArrayBuffer, new[] { 0, 1, 2, 0, 2, 3 }, GlStaticDraw);
        gl.BindBuffer(GlElementArrayBuffer, 0);
        _initialized = true;
    }

    public RenderStats Render(GlInterface gl, int framebuffer, Scene3D scene, Rect bounds)
    {
        if (!_initialized) Initialize(gl);
        var width = System.Math.Max((int)System.Math.Ceiling(bounds.Width), 1);
        var height = System.Math.Max((int)System.Math.Ceiling(bounds.Height), 1);

        gl.BindFramebuffer(0x8D40, framebuffer);
        gl.Viewport(0, 0, width, height);
        gl.Enable(GlDepthTest);
        gl.ClearColor(scene.BackgroundColor.R, scene.BackgroundColor.G, scene.BackgroundColor.B, scene.BackgroundColor.A);
        gl.Clear(GlColorBufferBit | GlDepthBufferBit);

        var aspect = (float)width / height;
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
            MeshCacheCount = MeshCache3D.Shared.Count
        };

        DrawMeshes(gl, scene, viewProjection, stats);
        DrawControlPlanes(gl, scene, viewProjection, stats);

        gl.BindBuffer(GlArrayBuffer, 0);
        gl.BindBuffer(GlElementArrayBuffer, 0);
        gl.BindTexture(GlTexture2D, 0);
        gl.UseProgram(0);
        return stats;
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
        if (_controlVertexBuffer != 0) gl.DeleteBuffer(_controlVertexBuffer);
        if (_controlIndexBuffer != 0) gl.DeleteBuffer(_controlIndexBuffer);
        if (_meshProgram != 0) gl.DeleteProgram(_meshProgram);
        if (_texturedProgram != 0) gl.DeleteProgram(_texturedProgram);
        _meshInstanceBuffer = _controlVertexBuffer = _controlIndexBuffer = _meshProgram = _texturedProgram = 0;
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
        foreach (var pair in _meshResources.ToArray())
        {
            if (!liveMeshes.Contains(pair.Key)) { pair.Value.Dispose(gl); _meshResources.Remove(pair.Key); }
        }
        foreach (var pair in _controlTextures.ToArray())
        {
            if (!liveControlPlanes.Contains(pair.Key)) { pair.Value.Dispose(gl); _controlTextures.Remove(pair.Key); }
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
            DrawHighScaleLayersLegacy(gl, scene, viewProjection, stats);
        }
    }

    private void BuildBatches(Scene3D scene, Matrix4x4 viewProjection, RenderStats stats)
    {
        foreach (var batch in _meshBatches.Values) batch.Reset();

        foreach (var obj in scene.Registry.Renderables)
        {
            var mesh = obj.GetMesh();
            var model = obj.GetModelMatrix();
            if (!FrustumCuller3D.IntersectsLocalBounds(mesh.LocalBounds, model, viewProjection))
            {
                stats.CulledObjectCount++;
                continue;
            }
            var color = ResolveColor(obj);
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
        gl.BindBuffer(GlElementArrayBuffer, resource.IndexBuffer);
    }

    private void EnableInstanceAttributes(GlInterface gl)
    {
        EnableInstanceAttribute(gl, _meshInstanceModel0Location, 4, 0);
        EnableInstanceAttribute(gl, _meshInstanceModel1Location, 4, sizeof(float) * 4);
        EnableInstanceAttribute(gl, _meshInstanceModel2Location, 4, sizeof(float) * 8);
        EnableInstanceAttribute(gl, _meshInstanceModel3Location, 4, sizeof(float) * 12);
        EnableInstanceAttribute(gl, _meshInstanceColorLocation, 4, sizeof(float) * 16);
    }

    private void EnableInstanceAttribute(GlInterface gl, int location, int size, int offset)
    {
        if (location < 0) return;
        gl.EnableVertexAttribArray(location);
        gl.VertexAttribPointer(location, size, GlFloat, 0, InstanceByteStride, new IntPtr(offset));
        _vertexAttribDivisor?.Invoke(location, 1);
    }

    private void ResetInstanceAttributeDivisors()
    {
        ResetDivisor(_meshInstanceModel0Location);
        ResetDivisor(_meshInstanceModel1Location);
        ResetDivisor(_meshInstanceModel2Location);
        ResetDivisor(_meshInstanceModel3Location);
        ResetDivisor(_meshInstanceColorLocation);
    }

    private void ResetDivisor(int location)
    {
        if (location >= 0) _vertexAttribDivisor?.Invoke(location, 0);
    }

    private void DrawHighScaleLayers(GlInterface gl, Scene3D scene, Matrix4x4 viewProjection, RenderStats stats)
    {
        UploadFloat(_uniform1f, _meshUsePartLocalLocation, 1f);
        foreach (var layer in EnumerateHighScaleLayers(scene))
        {
            if (!layer.IsVisible || layer.Instances.Count == 0) continue;
            if (layer.Chunks.RebuildRequested)
            {
                layer.Chunks.Rebuild(layer.Instances, layer.Template.LocalBounds);
            }

            var visibleChunks = layer.Chunks.QueryVisible(viewProjection);
            stats.TotalChunkCount += layer.Chunks.Chunks.Count;
            stats.VisibleChunkCount += visibleChunks.Count;

            foreach (var chunk in visibleChunks)
            {
                foreach (var lod in new[] { HighScaleLodLevel3D.Detailed, HighScaleLodLevel3D.Simplified, HighScaleLodLevel3D.Proxy })
                {
                    var parts = layer.Template.ResolveParts(lod);
                    for (var partIndex = 0; partIndex < parts.Count; partIndex++)
                    {
                        var part = parts[partIndex];
                        var batch = EnsureHighScaleGpuBatch(gl, layer, chunk, lod, partIndex, part, scene.Camera.Position, stats);
                        if (batch.InstanceCount == 0) continue;
                        var meshResource = EnsureMeshResource(gl, part.Mesh.ResourceKey, 0, part.Mesh, stats);
                        BindMeshAttributes(gl, meshResource);
                        gl.BindBuffer(GlArrayBuffer, batch.InstanceBuffer);
                        EnableInstanceAttributes(gl);
                        UploadMatrix(_uniformMatrix4fv, _meshPartLocalLocation, part.LocalTransform, _matrixUploadBuffer);
                        UploadFloat(_uniform1f, _meshLightingEnabledLocation, part.LightingMode == LightingMode.Lambert ? 1f : 0f);
                        _drawElementsInstanced?.Invoke(GlTriangles, meshResource.IndexCount, GlUnsignedInt, IntPtr.Zero, batch.InstanceCount);
                        stats.DrawCallCount++;
                        stats.EstimatedDrawCallCount++;
                        stats.InstancedBatchCount++;
                        stats.VisibleMeshCount += batch.InstanceCount;
                        stats.TriangleCount += (part.Mesh.Indices.Length / 3) * batch.InstanceCount;
                    }
                }
            }
        }
        UploadFloat(_uniform1f, _meshUsePartLocalLocation, 0f);
        ResetInstanceAttributeDivisors();
    }

    private void DrawHighScaleLayersLegacy(GlInterface gl, Scene3D scene, Matrix4x4 viewProjection, RenderStats stats)
    {
        UploadFloat(_uniform1f, _meshUsePartLocalLocation, 0f);
        foreach (var layer in EnumerateHighScaleLayers(scene))
        {
            if (!layer.IsVisible || layer.Instances.Count == 0) continue;
            if (layer.Chunks.RebuildRequested) layer.Chunks.Rebuild(layer.Instances, layer.Template.LocalBounds);
            var visibleChunks = layer.Chunks.QueryVisible(viewProjection);
            stats.TotalChunkCount += layer.Chunks.Chunks.Count;
            stats.VisibleChunkCount += visibleChunks.Count;
            foreach (var chunk in visibleChunks)
            {
                foreach (var index in chunk.InstanceIndices)
                {
                    var record = layer.Instances[index];
                    if ((record.Flags & InstanceFlags3D.Visible) == 0) continue;
                    var lod = layer.LodPolicy.Resolve(scene.Camera.Position, record.Transform);
                    if (lod == HighScaleLodLevel3D.Billboard) { stats.LodBillboardCount++; continue; }
                    var parts = layer.Template.ResolveParts(lod);
                    for (var i = 0; i < parts.Count; i++)
                    {
                        var part = parts[i];
                        var resource = EnsureMeshResource(gl, part.Mesh.ResourceKey, 0, part.Mesh, stats);
                        BindMeshAttributes(gl, resource);
                        var model = part.LocalTransform * record.Transform;
                        UploadMatrix(_uniformMatrix4fv, _meshModelLocation, model, _matrixUploadBuffer);
                        UploadColor(_uniform4f, _meshColorLocation, layer.ResolveColor(part, record));
                        UploadFloat(_uniform1f, _meshLightingEnabledLocation, part.LightingMode == LightingMode.Lambert ? 1f : 0f);
                        gl.DrawElements(GlTriangles, resource.IndexCount, GlUnsignedInt, IntPtr.Zero);
                        stats.DrawCallCount++;
                        stats.VisibleMeshCount++;
                        stats.TriangleCount += part.Mesh.Indices.Length / 3;
                    }
                    stats.HighScaleInstanceCount++;
                }
            }
        }
    }

    private HighScaleGpuBatchData EnsureHighScaleGpuBatch(
        GlInterface gl,
        HighScaleInstanceLayer3D layer,
        HighScaleChunk3D chunk,
        HighScaleLodLevel3D lod,
        int partIndex,
        CompositePartTemplate3D part,
        Vector3 cameraPosition,
        RenderStats stats)
    {
        var key = layer.Id + "|" + chunk.Key + "|" + lod + "|" + partIndex + "|" + part.Mesh.ResourceKey;
        if (!_highScaleGpuBatches.TryGetValue(key, out var batch))
        {
            batch = new HighScaleGpuBatchData { InstanceBuffer = gl.GenBuffer() };
            _highScaleGpuBatches[key] = batch;
        }

        var requiredVersion = HashCode.Combine(chunk.Version, layer.Instances.Version, layer.MaterialResolverVersion, (int)lod, partIndex);
        if (!chunk.IsDirty && batch.Version == requiredVersion && batch.InstanceCount > 0)
        {
            return batch;
        }

        batch.ResetCpuData();
        foreach (var index in chunk.InstanceIndices)
        {
            var record = layer.Instances[index];
            if ((record.Flags & InstanceFlags3D.Visible) == 0) continue;
            var resolvedLod = layer.LodPolicy.Resolve(cameraPosition, record.Transform);
            if (resolvedLod != lod) continue;
            batch.Add(record.Transform, layer.ResolveColor(part, record));
        }

        gl.BindBuffer(GlArrayBuffer, batch.InstanceBuffer);
        UploadFloats(gl, GlArrayBuffer, batch.Data, batch.FloatCount, GlDynamicDraw);
        batch.Version = requiredVersion;
        batch.MarkUploaded();
        stats.InstanceBufferUploads++;
        stats.InstanceUploadBytes += batch.FloatCount * sizeof(float);
        return batch;
    }

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
            IndexBuffer = gl.GenBuffer(),
            IndexCount = mesh.Indices.Length
        };
        gl.BindBuffer(GlArrayBuffer, resource.VertexBuffer);
        UploadVector3(gl, GlArrayBuffer, mesh.Positions, GlStaticDraw);
        gl.BindBuffer(GlArrayBuffer, resource.NormalBuffer);
        UploadVector3(gl, GlArrayBuffer, GetNormalsOrDefault(mesh), GlStaticDraw);
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

    private sealed class MeshGpuResource
    {
        public int GeometryVersion { get; init; }
        public int VertexBuffer { get; init; }
        public int NormalBuffer { get; init; }
        public int IndexBuffer { get; init; }
        public int IndexCount { get; init; }
        public void Dispose(GlInterface gl)
        {
            if (NormalBuffer != 0) gl.DeleteBuffer(NormalBuffer);
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

    private sealed class HighScaleGpuBatchData
    {
        private float[] _data = new float[InstanceFloatStride * 128];
        public int InstanceBuffer { get; init; }
        public int Version { get; set; }
        public int InstanceCount { get; private set; }
        public int FloatCount => InstanceCount * InstanceFloatStride;
        public float[] Data => _data;
        public void ResetCpuData() => InstanceCount = 0;
        public void Add(Matrix4x4 model, ColorRgba color)
        {
            EnsureCapacity((InstanceCount + 1) * InstanceFloatStride);
            var offset = InstanceCount * InstanceFloatStride;
            WriteMatrix(_data, offset, model);
            _data[offset + 16] = color.R;
            _data[offset + 17] = color.G;
            _data[offset + 18] = color.B;
            _data[offset + 19] = color.A;
            InstanceCount++;
        }
        public void MarkUploaded() { }
        public void Dispose(GlInterface gl)
        {
            if (InstanceBuffer != 0) gl.DeleteBuffer(InstanceBuffer);
        }
        private void EnsureCapacity(int required)
        {
            if (_data.Length >= required) return;
            var next = _data.Length;
            while (next < required) next *= 2;
            Array.Resize(ref _data, next);
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
uniform mat4 uModel;
uniform mat4 uPartLocal;
uniform mat4 uViewProj;
uniform vec4 uColor;
uniform float uUseInstancing;
uniform float uUsePartLocal;
varying vec3 vWorldPos;
varying vec3 vNormal;
varying vec4 vColor;
void main()
{
    mat4 instanceModel = mat4(aInstanceModel0, aInstanceModel1, aInstanceModel2, aInstanceModel3);
    mat4 model = uUseInstancing > 0.5 ? instanceModel : uModel;
    if (uUsePartLocal > 0.5) model = uPartLocal * model;
    vec4 world = model * vec4(aPosition, 1.0);
    vWorldPos = world.xyz;
    vNormal = normalize(mat3(model) * aNormal);
    vColor = uUseInstancing > 0.5 ? aInstanceColor : uColor;
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
varying vec3 vWorldPos;
varying vec3 vNormal;
varying vec4 vColor;
void main()
{
    vec3 outColor = vColor.rgb;
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
    gl_FragColor = vec4(outColor, vColor.a);
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
