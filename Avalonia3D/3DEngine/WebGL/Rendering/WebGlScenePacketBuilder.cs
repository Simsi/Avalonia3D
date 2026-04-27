using System;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Avalonia.Controls;
using ThreeDEngine.Core.Culling;
using ThreeDEngine.Core.HighScale;
using ThreeDEngine.Core.Materials;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.WebGL.Rendering;

internal static class WebGlScenePacketBuilder
{
    private const int InstanceFloatStride = 20;

    public static WebGlScenePacket Build(Scene3D scene, float width, float height, RenderStats? stats = null)
    {
        width = MathF.Max(width, 1f);
        height = MathF.Max(height, 1f);
        var aspect = width / height;
        var view = scene.Camera.GetViewMatrix();
        var projection = scene.Camera.GetProjectionMatrix(aspect);
        var viewProjection = view * projection;

        var batchMap = new Dictionary<string, WebGlMeshBatchPacket>(StringComparer.Ordinal);
        var controls = new List<WebGlControlPlanePacket>();

        foreach (var obj in scene.Registry.Renderables)
        {
            var mesh = obj.GetMesh();
            var model = obj.GetModelMatrix();
            if (!FrustumCuller3D.IntersectsLocalBounds(mesh.LocalBounds, model, viewProjection))
            {
                if (stats is not null) stats.CulledObjectCount++;
                continue;
            }

            var color = ResolveColor(obj);
            var lighting = obj.Material.Lighting == LightingMode.Lambert ? 1f : 0f;
            var batch = GetBatch(batchMap, mesh.ResourceKey, lighting);
            AddInstance(batch, model, color);
            if (stats is not null)
            {
                stats.VisibleMeshCount++;
                stats.TriangleCount += mesh.Indices.Length / 3;
            }
        }

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
                    if (lod == HighScaleLodLevel3D.Billboard)
                    {
                        if (stats is not null) stats.LodBillboardCount++;
                        continue;
                    }

                    var parts = layer.Template.ResolveParts(lod);
                    if (stats is not null)
                    {
                        if (lod == HighScaleLodLevel3D.Simplified) stats.LodSimplifiedCount++;
                        if (lod == HighScaleLodLevel3D.Proxy) stats.LodProxyCount++;
                    }

                    for (var p = 0; p < parts.Count; p++)
                    {
                        var part = parts[p];
                        var model = part.LocalTransform * record.Transform;
                        var lighting = part.LightingMode == LightingMode.Lambert ? 1f : 0f;
                        var batch = GetBatch(batchMap, part.Mesh.ResourceKey, lighting);
                        AddInstance(batch, model, layer.ResolveColor(part, record));
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

        foreach (var obj in scene.Registry.AllObjects)
        {
            if (obj is not ControlPlane3D plane || !plane.IsVisible || plane.Snapshot is null)
            {
                continue;
            }

            var corners = ControlPlaneGeometry.GetWorldCorners(plane, scene.Camera);
            var vertices = new float[20];
            WriteControlVertex(vertices, 0, corners[0], 0f, 0f);
            WriteControlVertex(vertices, 5, corners[1], 1f, 0f);
            WriteControlVertex(vertices, 10, corners[2], 1f, 1f);
            WriteControlVertex(vertices, 15, corners[3], 0f, 1f);

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
        var batches = new List<WebGlMeshBatchPacket>(batchMap.Values);
        if (stats is not null)
        {
            stats.DrawCallCount = batches.Count + controls.Count;
            stats.EstimatedDrawCallCount = stats.DrawCallCount;
            stats.InstancedBatchCount = batches.Count;
            stats.ControlPlaneCount = controls.Count;
        }

        return new WebGlScenePacket
        {
            Width = width,
            Height = height,
            ClearColor = new[] { scene.BackgroundColor.R, scene.BackgroundColor.G, scene.BackgroundColor.B, scene.BackgroundColor.A },
            ViewProjection = ToArray(viewProjection),
            AmbientLight = light.Ambient,
            DirectionalLightDirection = light.Direction,
            DirectionalLightColor = light.DirectionalColor,
            PointLightPosition = light.PointPosition,
            PointLightColor = light.PointColor,
            Batches = batches,
            ControlPlanes = controls
        };
    }

    private static WebGlMeshBatchPacket GetBatch(Dictionary<string, WebGlMeshBatchPacket> batches, string meshId, float lightingEnabled)
    {
        var key = meshId + "|l:" + lightingEnabled.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!batches.TryGetValue(key, out var batch))
        {
            batch = new WebGlMeshBatchPacket
            {
                Id = meshId,
                LightingEnabled = lightingEnabled,
                InstanceData = new List<float>(InstanceFloatStride * 64)
            };
            batches[key] = batch;
        }

        return batch;
    }

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

    private static ColorRgba ResolveColor(Object3D obj)
    {
        var color = obj.Material.EffectiveColor;
        if (obj.IsEffectivelyHovered) color = color.BlendTowards(ColorRgba.White, 0.10f);
        if (obj.IsEffectivelySelected) color = color.BlendTowards(ColorRgba.White, 0.22f);
        return color;
    }

    private static (float[] Ambient, float[] Direction, float[] DirectionalColor, float[] PointPosition, float[] PointColor) ResolveLight(Scene3D scene)
    {
        var ambient = new[] { 0.28f, 0.28f, 0.28f };
        var dir = new[] { -0.35f, -0.75f, -0.55f };
        var dirColor = new[] { 0f, 0f, 0f };
        foreach (var light in scene.Lights)
        {
            if (!light.IsEnabled) continue;
            var direction = light.Direction.LengthSquared() < 0.000001f ? new Vector3(-0.35f, -0.75f, -0.55f) : Vector3.Normalize(light.Direction);
            dir = new[] { direction.X, direction.Y, direction.Z };
            dirColor = new[] { light.Color.R * light.Intensity, light.Color.G * light.Intensity, light.Color.B * light.Intensity };
            break;
        }

        var pointPos = new[] { 0f, 0f, 0f, 1f };
        var pointColor = new[] { 0f, 0f, 0f, 0f };
        foreach (var light in scene.PointLights)
        {
            if (!light.IsEnabled) continue;
            pointPos = new[] { light.Position.X, light.Position.Y, light.Position.Z, light.Range };
            pointColor = new[] { light.Color.R * light.Intensity, light.Color.G * light.Intensity, light.Color.B * light.Intensity, 1f };
            break;
        }

        return (ambient, dir, dirColor, pointPos, pointColor);
    }

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
    public required float[] AmbientLight { get; init; }
    public required float[] DirectionalLightDirection { get; init; }
    public required float[] DirectionalLightColor { get; init; }
    public required float[] PointLightPosition { get; init; }
    public required float[] PointLightColor { get; init; }
    public required List<WebGlMeshBatchPacket> Batches { get; init; }
    public required List<WebGlControlPlanePacket> ControlPlanes { get; init; }
}

internal sealed class WebGlMeshBatchPacket
{
    public required string Id { get; init; }
    public required float LightingEnabled { get; init; }
    public required List<float> InstanceData { get; init; }
    public int InstanceCount { get; set; }
}

internal sealed class WebGlControlPlanePacket
{
    public required string Id { get; init; }
    public required string TextureId { get; init; }
    public required float[] Vertices { get; init; }
    public required float AverageDepth { get; init; }
}
