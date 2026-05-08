using System;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Avalonia.Controls;
using ThreeDEngine.Core.Culling;
using ThreeDEngine.Core.Environment;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Instancing;
using ThreeDEngine.Core.Particles;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Lighting;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Rendering.Shadows;
using ThreeDEngine.Core.Rendering.Pipeline;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.WebGL.Rendering;

internal static class WebGlScenePacketBuilder
{
    private const int InstanceFloatStride = 20;

    public static WebGlScenePacket Build(Scene3D scene, float width, float height, RenderStats? stats = null, List<WebGlRetainedBatchPacket>? retainedHighScaleBatches = null)
    {
        width = MathF.Max(width, 1f);
        height = MathF.Max(height, 1f);
        var aspect = width / height;
        var view = scene.Camera.GetViewMatrix();
        var projection = scene.Camera.GetProjectionMatrix(aspect);
        var viewProjection = view * projection;

        var batchMap = new Dictionary<string, WebGlMeshBatchPacket>(StringComparer.Ordinal);
        var controls = new List<WebGlControlPlanePacket>();
        var liveMeshIds = CollectLiveMeshIds(scene);
        var liveTextureIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var obj in scene.Registry.Renderables)
        {
            if (stats is not null && obj is ParticleSystem3D particles)
            {
                stats.ParticleSystemCount++;
                stats.ParticleCount += particles.AliveCount;
            }
            if (stats is not null && obj is InstancedMesh3D instancedMesh)
            {
                stats.InstancedMeshLayerCount++;
                stats.InstancedMeshInstanceCount += instancedMesh.Instances.Count;
            }
            if (obj is ParticleSystem3D particleSystem)
            {
                particleSystem.SetBillboardBasis(scene.Camera.Right, scene.Camera.SafeUp, scene.Camera.Forward);
            }
            var mesh = obj.GetMesh();
            if (stats is not null && obj is ParticleSystem3D)
            {
                stats.ParticleVertexCount += mesh.Positions.Length;
                stats.ParticleMeshUploadBytes += mesh.RenderGeometry.EstimatedUploadBytes;
                stats.ThroughputFallbackDrawCount++;
            }
            var model = obj.GetModelMatrix();
            if (!FrustumCuller3D.IntersectsLocalBounds(mesh.LocalBounds, model, viewProjection))
            {
                if (stats is not null) stats.CulledObjectCount++;
                continue;
            }

            var distanceAlpha = ResolveDistanceAlpha(scene, model);
            if (distanceAlpha <= 0.001f)
            {
                if (stats is not null) stats.CulledObjectCount++;
                continue;
            }

            var color = ApplyDistanceAlpha(ResolveColor(obj), distanceAlpha);
            var material = obj.Material;
            var lighting = ToLightingUniform(material.Lighting);
            var normalMapStrength = material.HasNormalMap ? material.NormalMapStrength : 0f;
            var baseColorTextureId = material.HasBaseColorTexture ? material.BaseColorTextureKey : null;
            var normalTextureId = material.HasNormalMap ? material.NormalMapTextureKey : null;
            var metallicRoughnessTextureId = material.HasMetallicRoughnessTexture ? material.MetallicRoughnessTextureKey : null;
            var emissiveTextureId = material.HasEmissiveTexture ? material.EmissiveTextureKey : null;
            AddLiveTexture(liveTextureIds, baseColorTextureId);
            AddLiveTexture(liveTextureIds, normalTextureId);
            AddLiveTexture(liveTextureIds, metallicRoughnessTextureId);
            AddLiveTexture(liveTextureIds, emissiveTextureId);
            var batch = GetBatch(batchMap, mesh.ResourceKey, lighting, normalMapStrength, baseColorTextureId, normalTextureId, metallicRoughnessTextureId, emissiveTextureId, material.Metallic, material.Roughness, material.Surface == SurfaceMode.Transparent ? 0f : material.AlphaCutoff, material.EmissiveColor);
            AddInstance(batch, model, color);
            if (stats is not null)
            {
                stats.VisibleMeshCount++;
                if (obj.Material.HasNormalMap) stats.NormalMappedMeshCount++;
                stats.TriangleCount += mesh.Indices.Length / 3;
            }
        }

        if (retainedHighScaleBatches is null)
        {
        foreach (var layer in EnumerateHighScaleLayers(scene))
        {
            if (!layer.IsVisible || layer.Instances.Count == 0)
            {
                continue;
            }

            if (layer.Chunks.RebuildRequested)
            {
                layer.Chunks.Rebuild(layer.Instances, layer.Template.LocalBounds);
            }

            var visibleChunks = layer.Chunks.QueryVisible(viewProjection);
            if (stats is not null)
            {
                stats.TotalChunkCount += layer.Chunks.Chunks.Count;
                stats.VisibleChunkCount += visibleChunks.Count;
            }

            foreach (var chunk in visibleChunks)
            {
                foreach (var instanceIndex in chunk.InstanceIndices)
                {
                    var record = layer.Instances[instanceIndex];
                    if ((record.Flags & InstanceFlags3D.Visible) == 0)
                    {
                        continue;
                    }

                    var lod = layer.LodPolicy.Resolve(scene.Camera.Position, record.Transform);
                    if (lod == HighScaleLodLevel3D.Culled)
                    {
                        if (stats is not null) { stats.LodCulledCount++; stats.CulledObjectCount++; }
                        continue;
                    }

                    var renderLod = lod == HighScaleLodLevel3D.Billboard ? HighScaleLodLevel3D.Proxy : lod;
                    if (lod == HighScaleLodLevel3D.Billboard && stats is not null) stats.LodBillboardCount++;

                    var parts = layer.Template.ResolveParts(renderLod);
                    if (stats is not null)
                    {
                        if (lod == HighScaleLodLevel3D.Detailed) stats.LodDetailedCount++;
                        if (lod == HighScaleLodLevel3D.Simplified) stats.LodSimplifiedCount++;
                        if (lod == HighScaleLodLevel3D.Proxy) stats.LodProxyCount++;
                    }

                    for (var p = 0; p < parts.Count; p++)
                    {
                        var part = parts[p];
                        var model = part.LocalTransform * record.Transform;
                        var lighting = ToLightingUniform(part.LightingMode);
                        var batch = GetBatch(batchMap, part.Mesh.ResourceKey, lighting, 0f, null, null, null, null, 0f, 1f, 0.5f, ColorRgba.Transparent);
                        var color = ApplyDistanceAlpha(layer.ResolveColor(part, record), layer.LodPolicy.ResolveFadeAlpha(scene.Camera.Position, record.Transform));
                        if (color.A <= 0.001f) continue;
                        AddInstance(batch, model, color);
                        if (stats is not null)
                        {
                            stats.VisibleMeshCount++;
                            stats.TriangleCount += part.Mesh.Indices.Length / 3;
                        }
                    }

                    if (stats is not null) stats.HighScaleInstanceCount++;
                }
            }
        }

        }

        foreach (var obj in scene.Registry.AllObjects)
        {
            if (obj is not ControlPlane3D plane || !plane.IsVisible || plane.Snapshot is null)
            {
                continue;
            }

            var corners = ControlPlaneGeometry.GetWorldCorners(plane, scene.Camera);
            var vertices = new float[20];
            // Keep the same UV convention as OpenGL: ControlPlaneGeometry returns
            // corners in top-left, top-right, bottom-right, bottom-left order.
            WriteControlVertex(vertices, 0, corners[0], 0f, 0f);
            WriteControlVertex(vertices, 5, corners[1], 1f, 0f);
            WriteControlVertex(vertices, 10, corners[2], 1f, 1f);
            WriteControlVertex(vertices, 15, corners[3], 0f, 1f);

            liveTextureIds.Add(plane.Id);
            controls.Add(new WebGlControlPlanePacket
            {
                Id = plane.Id,
                TextureId = plane.Id,
                Vertices = vertices,
                AverageDepth = ComputeAverageDepth(corners, viewProjection)
            });
        }

        controls.Sort((a, b) => b.AverageDepth.CompareTo(a.AverageDepth));
        var light = ResolveLight(scene);
        var shadow = DirectionalShadowResolver3D.Resolve(scene);
        var skybox = scene.Environment.Skybox;
        var skyboxTextureId = skybox.HasEquirectangularTexture ? skybox.EquirectangularTextureKey : null;
        var skyboxCubemapTextureIds = BuildCubemapTextureIds(skybox);
        AddLiveTexture(liveTextureIds, skyboxTextureId);
        for (var i = 0; i < skyboxCubemapTextureIds.Length; i++) AddLiveTexture(liveTextureIds, skyboxCubemapTextureIds[i]);
        var pipeline = RenderPipelinePlanner3D.Plan(scene, BackendKind.WebGlBrowser);
        var batches = new List<WebGlMeshBatchPacket>(batchMap.Values);
        if (stats is not null)
        {
            stats.DrawCallCount = batches.Count + (retainedHighScaleBatches?.Count ?? 0) + controls.Count;
            stats.EstimatedDrawCallCount = stats.DrawCallCount;
            stats.InstancedBatchCount = batches.Count + (retainedHighScaleBatches?.Count ?? 0);
            stats.ControlPlaneCount = controls.Count;
            stats.DirectionalLightCount = scene.Lights.Count;
            stats.PointLightCount = scene.PointLights.Count;
            stats.SpotLightCount = scene.SpotLights.Count;
            stats.SkyboxEnabled = skybox.Mode != SkyboxMode3D.None;
            stats.SkyboxMode = (int)skybox.Mode;
            stats.DirectionalShadowEnabled = shadow.IsEnabled;
            stats.ShadowMapResolution = shadow.Resolution;
            stats.ShadowMapReason = shadow.Reason;
            ApplyPipelineStats(stats, scene, pipeline);
        }

        return new WebGlScenePacket
        {
            Width = width,
            Height = height,
            ClearColor = new[] { scene.BackgroundColor.R, scene.BackgroundColor.G, scene.BackgroundColor.B, scene.BackgroundColor.A },
            ViewProjection = ToArray(viewProjection),
            CameraPosition = new[] { scene.Camera.Position.X, scene.Camera.Position.Y, scene.Camera.Position.Z },
            CameraRight = ToArray(scene.Camera.Right),
            CameraUp = ToArray(scene.Camera.SafeUp),
            CameraForward = ToArray(scene.Camera.Forward),
            AmbientLight = light.Ambient,
            DirectionalLightDirection = light.Direction,
            DirectionalLightColor = light.DirectionalColor,
            PointLightPosition = light.PointPosition,
            PointLightColor = light.PointColor,
            SpotLightPosition = light.SpotPosition,
            SpotLightDirection = light.SpotDirection,
            SpotLightColor = light.SpotColor,
            SpotLightCone = light.SpotCone,
            SkyboxEnabled = skybox.Mode != SkyboxMode3D.None,
            SkyboxMode = (int)skybox.Mode,
            SkyboxTopColor = skybox.TopColor.ToArray(),
            SkyboxHorizonColor = skybox.HorizonColor.ToArray(),
            SkyboxBottomColor = skybox.BottomColor.ToArray(),
            SkyboxIntensity = skybox.Intensity,
            SkyboxTextureId = skyboxTextureId,
            SkyboxCubemapTextureIds = skyboxCubemapTextureIds,
            DirectionalShadowEnabled = shadow.IsEnabled,
            DirectionalShadowResolution = shadow.Resolution,
            DirectionalShadowStrength = shadow.Strength,
            DirectionalShadowBias = shadow.Bias,
            DirectionalShadowReason = shadow.Reason,
            DirectionalShadowLightViewProjection = ToArray(shadow.LightViewProjection),
            RenderPipelineMode = (int)pipeline.ActiveMode,
            DeferredRequested = pipeline.DeferredRequested,
            SsaoEnabled = pipeline.SsaoRequested,
            SsaoParams = new[] { scene.RenderPipeline.Ssao.Strength, scene.RenderPipeline.Ssao.Radius, scene.RenderPipeline.Ssao.Bias, (float)scene.RenderPipeline.Ssao.SampleCount },
            HdrEnabled = pipeline.HdrActive,
            ToneMappingMode = (int)pipeline.ToneMappingMode,
            ToneMappingParams = new[] { scene.RenderPipeline.ToneMapping.Exposure, scene.RenderPipeline.ToneMapping.Gamma, pipeline.ToneMappingActive ? 1f : 0f, 0f },
            MotionVectorMetadataEnabled = pipeline.MotionVectorsRequested,
            ShowWireframeOverlay = scene.Debug.ShowWireframeOverlay,
            ShowSilhouetteOverlay = scene.Debug.ShowSilhouetteOverlay,
            Batches = batches,
            RetainedBatches = retainedHighScaleBatches ?? new List<WebGlRetainedBatchPacket>(),
            ControlPlanes = controls,
            LiveMeshIds = new List<string>(liveMeshIds),
            LiveTextureIds = new List<string>(liveTextureIds)
        };
    }


    private static string?[] BuildCubemapTextureIds(Skybox3D skybox)
    {
        var ids = new string?[6];
        if (!skybox.HasCubemapTextures) return ids;
        for (var i = 0; i < ids.Length && i < skybox.CubemapTextureKeys.Count; i++) ids[i] = skybox.CubemapTextureKeys[i];
        return ids;
    }

    private static HashSet<string> CollectLiveMeshIds(Scene3D scene)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var obj in scene.Registry.Renderables)
        {
            ids.Add(obj.GetMesh().ResourceKey);
        }

        foreach (var layer in EnumerateHighScaleLayers(scene))
        {
            foreach (var lod in new[] { HighScaleLodLevel3D.Detailed, HighScaleLodLevel3D.Simplified, HighScaleLodLevel3D.Proxy, HighScaleLodLevel3D.Billboard })
            {
                foreach (var part in layer.Template.ResolveParts(lod))
                {
                    ids.Add(part.Mesh.ResourceKey);
                }
            }
        }

        return ids;
    }

    private static WebGlMeshBatchPacket GetBatch(
        Dictionary<string, WebGlMeshBatchPacket> batches,
        string meshId,
        float lightingEnabled,
        float normalMapStrength,
        string? baseColorTextureId,
        string? normalTextureId,
        string? metallicRoughnessTextureId,
        string? emissiveTextureId,
        float metallic,
        float roughness,
        float alphaCutoff,
        ColorRgba emissiveColor)
    {
        var key = meshId +
                  "|l:" + lightingEnabled.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                  "|n:" + MathF.Round(normalMapStrength, 4).ToString(System.Globalization.CultureInfo.InvariantCulture) +
                  TextureKey("base", baseColorTextureId) +
                  TextureKey("normal", normalTextureId) +
                  TextureKey("mr", metallicRoughnessTextureId) +
                  TextureKey("em", emissiveTextureId) +
                  "|m:" + F(metallic) + "|r:" + F(roughness) + "|cut:" + F(alphaCutoff) +
                  "|ec:" + F(emissiveColor.R) + "," + F(emissiveColor.G) + "," + F(emissiveColor.B) + "," + F(emissiveColor.A);
        if (!batches.TryGetValue(key, out var batch))
        {
            batch = new WebGlMeshBatchPacket
            {
                Id = meshId,
                LightingEnabled = lightingEnabled,
                NormalMapStrength = normalMapStrength,
                BaseColorTextureId = baseColorTextureId,
                NormalTextureId = normalTextureId,
                MetallicRoughnessTextureId = metallicRoughnessTextureId,
                EmissiveTextureId = emissiveTextureId,
                Metallic = metallic,
                Roughness = roughness,
                AlphaCutoff = alphaCutoff,
                EmissiveColor = new[] { emissiveColor.R, emissiveColor.G, emissiveColor.B, emissiveColor.A },
                InstanceData = new List<float>(InstanceFloatStride * 64)
            };
            batches[key] = batch;
        }

        return batch;
    }

    private static void AddLiveTexture(HashSet<string> liveTextureIds, string? textureId)
    {
        if (!string.IsNullOrWhiteSpace(textureId)) liveTextureIds.Add(textureId);
    }

    private static string TextureKey(string role, string? id)
        => string.IsNullOrWhiteSpace(id) ? string.Empty : "|" + role + ":" + id;

    private static string F(float value)
        => MathF.Round(value, 4).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    private static IEnumerable<HighScaleInstanceLayer3D> EnumerateHighScaleLayers(Scene3D scene)
    {
        foreach (var obj in scene.Registry.AllObjects)
        {
            if (obj is HighScaleInstanceLayer3D layer)
            {
                yield return layer;
            }
        }
    }

    private static float ResolveDistanceAlpha(Scene3D scene, Matrix4x4 model)
    {
        var drawDistance = scene.Performance.DrawDistance;
        if (drawDistance <= 0f || float.IsPositiveInfinity(drawDistance)) return 1f;
        var pos = new Vector3(model.M41, model.M42, model.M43);
        var distance = Vector3.Distance(scene.Camera.Position, pos);
        if (distance > drawDistance) return 0f;
        if (!scene.Performance.EnableDistanceFade || scene.Performance.DistanceFadeBand <= 0.001f) return 1f;
        var fadeStart = MathF.Max(0f, drawDistance - scene.Performance.DistanceFadeBand);
        if (distance <= fadeStart) return 1f;
        return System.Math.Clamp(1f - ((distance - fadeStart) / MathF.Max(scene.Performance.DistanceFadeBand, 0.001f)), 0f, 1f);
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

    private static void ApplyPipelineStats(RenderStats stats, Scene3D scene, RenderPipelinePlan3D pipeline)
    {
        stats.RenderPipelineMode = (int)pipeline.ActiveMode;
        stats.DeferredRequested = pipeline.DeferredRequested;
        stats.DeferredActive = pipeline.DeferredActive;
        stats.GBufferActive = pipeline.GBufferActive;
        stats.GBufferTargetCount = pipeline.GBufferActive ? 4 : 0;
        stats.SsaoRequested = pipeline.SsaoRequested;
        stats.SsaoActive = pipeline.SsaoActive;
        stats.SsaoSampleCount = scene.RenderPipeline.Ssao.SampleCount;
        stats.HdrRequested = pipeline.HdrRequested;
        stats.HdrActive = pipeline.HdrActive;
        stats.ToneMappingMode = (int)pipeline.ToneMappingMode;
        stats.ToneMappingActive = pipeline.ToneMappingActive;
        stats.ToneMappingExposure = scene.RenderPipeline.ToneMapping.Exposure;
        stats.ToneMappingGamma = scene.RenderPipeline.ToneMapping.Gamma;
        stats.RenderPassCount = pipeline.Passes.Count;
        stats.MotionVectorsRequested = pipeline.MotionVectorsRequested;
        stats.MotionVectorsActive = pipeline.MotionVectorsActive;
        stats.RenderPipelineReason = pipeline.Reason;
    }

    private static (float[] Ambient, float[] Direction, float[] DirectionalColor, float[] PointPosition, float[] PointColor, float[] SpotPosition, float[] SpotDirection, float[] SpotColor, float[] SpotCone) ResolveLight(Scene3D scene)
    {
        var light = SceneLightingResolver3D.Resolve(scene);
        return (ToArray(light.Ambient), ToArray(light.DirectionalDirection), ToArray(light.DirectionalColor), ToArray(light.PointPosition), ToArray(light.PointColor), ToArray(light.SpotPosition), ToArray(light.SpotDirection), ToArray(light.SpotColor), ToArray(light.SpotCone));
    }

    private static float ToLightingUniform(LightingMode mode)
        => mode == LightingMode.Unlit ? 0f : mode == LightingMode.Lambert ? 1f : mode == LightingMode.Phong ? 2f : 3f;

    private static float[] ToArray(Vector3 value) => new[] { value.X, value.Y, value.Z };

    private static float[] ToArray(Vector4 value) => new[] { value.X, value.Y, value.Z, value.W };

    private static void WriteControlVertex(float[] buffer, int baseIndex, Vector3 position, float u, float v)
    {
        buffer[baseIndex] = position.X;
        buffer[baseIndex + 1] = position.Y;
        buffer[baseIndex + 2] = position.Z;
        buffer[baseIndex + 3] = u;
        buffer[baseIndex + 4] = v;
    }

    private static float ComputeAverageDepth(Vector3[] worldCorners, Matrix4x4 viewProjection)
    {
        var sum = 0f;
        for (var i = 0; i < worldCorners.Length; i++)
        {
            var clip = Vector4.Transform(new Vector4(worldCorners[i], 1f), viewProjection);
            if (MathF.Abs(clip.W) > 0.00001f)
            {
                sum += clip.Z / clip.W;
            }
        }

        return sum / worldCorners.Length;
    }

    private static void WriteMatrix(List<float> data, Matrix4x4 matrix)
    {
        data.Add(matrix.M11); data.Add(matrix.M12); data.Add(matrix.M13); data.Add(matrix.M14);
        data.Add(matrix.M21); data.Add(matrix.M22); data.Add(matrix.M23); data.Add(matrix.M24);
        data.Add(matrix.M31); data.Add(matrix.M32); data.Add(matrix.M33); data.Add(matrix.M34);
        data.Add(matrix.M41); data.Add(matrix.M42); data.Add(matrix.M43); data.Add(matrix.M44);
    }

    private static float[] ToArray(Matrix4x4 matrix)
    {
        return new[]
        {
            matrix.M11, matrix.M12, matrix.M13, matrix.M14,
            matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34,
            matrix.M41, matrix.M42, matrix.M43, matrix.M44
        };
    }

    private static void AddInstance(WebGlMeshBatchPacket batch, Matrix4x4 model, ColorRgba color)
    {
        WriteMatrix(batch.InstanceData, model);
        batch.InstanceData.Add(color.R);
        batch.InstanceData.Add(color.G);
        batch.InstanceData.Add(color.B);
        batch.InstanceData.Add(color.A);
        batch.InstanceCount++;
    }
}

internal sealed class WebGlScenePacket
{
    public required float Width { get; init; }
    public required float Height { get; init; }
    public required float[] ClearColor { get; init; }
    public required float[] ViewProjection { get; init; }
    public required float[] CameraPosition { get; init; }
    public required float[] CameraRight { get; init; }
    public required float[] CameraUp { get; init; }
    public required float[] CameraForward { get; init; }
    public required float[] AmbientLight { get; init; }
    public required float[] DirectionalLightDirection { get; init; }
    public required float[] DirectionalLightColor { get; init; }
    public required float[] PointLightPosition { get; init; }
    public required float[] PointLightColor { get; init; }
    public required float[] SpotLightPosition { get; init; }
    public required float[] SpotLightDirection { get; init; }
    public required float[] SpotLightColor { get; init; }
    public required float[] SpotLightCone { get; init; }
    public bool SkyboxEnabled { get; init; }
    public int SkyboxMode { get; init; }
    public float[] SkyboxTopColor { get; init; } = Array.Empty<float>();
    public float[] SkyboxHorizonColor { get; init; } = Array.Empty<float>();
    public float[] SkyboxBottomColor { get; init; } = Array.Empty<float>();
    public float SkyboxIntensity { get; init; }
    public string? SkyboxTextureId { get; init; }
    public string?[] SkyboxCubemapTextureIds { get; init; } = Array.Empty<string?>();
    public bool DirectionalShadowEnabled { get; init; }
    public int DirectionalShadowResolution { get; init; }
    public float DirectionalShadowStrength { get; init; }
    public float DirectionalShadowBias { get; init; }
    public string DirectionalShadowReason { get; init; } = string.Empty;
    public float[] DirectionalShadowLightViewProjection { get; init; } = Array.Empty<float>();
    public int RenderPipelineMode { get; init; }
    public bool DeferredRequested { get; init; }
    public bool SsaoEnabled { get; init; }
    public float[] SsaoParams { get; init; } = Array.Empty<float>();
    public bool HdrEnabled { get; init; }
    public int ToneMappingMode { get; init; }
    public float[] ToneMappingParams { get; init; } = Array.Empty<float>();
    public bool MotionVectorMetadataEnabled { get; init; }
    public bool ShowWireframeOverlay { get; init; }
    public bool ShowSilhouetteOverlay { get; init; }
    public required List<WebGlMeshBatchPacket> Batches { get; init; }
    public required List<WebGlRetainedBatchPacket> RetainedBatches { get; init; }
    public required List<WebGlControlPlanePacket> ControlPlanes { get; init; }
    public required List<string> LiveMeshIds { get; init; }
    public required List<string> LiveTextureIds { get; init; }
}

internal sealed class WebGlMeshBatchPacket
{
    public required string Id { get; init; }
    public required float LightingEnabled { get; init; }
    public float NormalMapStrength { get; init; }
    public string? BaseColorTextureId { get; init; }
    public string? NormalTextureId { get; init; }
    public string? MetallicRoughnessTextureId { get; init; }
    public string? EmissiveTextureId { get; init; }
    public float Metallic { get; init; }
    public float Roughness { get; init; } = 1f;
    public float AlphaCutoff { get; init; } = 0.5f;
    public float[] EmissiveColor { get; init; } = Array.Empty<float>();
    public required List<float> InstanceData { get; init; }
    public int InstanceCount { get; set; }
}

internal sealed class WebGlRetainedBatchPacket
{
    public required string Id { get; init; }
}

internal sealed class WebGlControlPlanePacket
{
    public required string Id { get; init; }
    public required string TextureId { get; init; }
    public required float[] Vertices { get; init; }
    public required float AverageDepth { get; init; }
}
