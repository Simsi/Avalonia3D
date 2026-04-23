using System;
using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Avalonia.Controls;
using ThreeDEngine.Core.Primitives;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.WebGL.Rendering;

internal static class WebGlScenePacketBuilder
{
    public static WebGlScenePacket Build(Scene3D scene, float width, float height)
    {
        width = System.MathF.Max(width, 1f);
        height = System.MathF.Max(height, 1f);

        var aspect = width / height;
        var view = scene.Camera.GetViewMatrix();
        var projection = scene.Camera.GetProjectionMatrix(aspect);
        var viewProjection = view * projection;

        var meshes = new List<WebGlMeshDrawPacket>(scene.Objects.Count);
        var controls = new List<WebGlControlPlanePacket>();

        foreach (var obj in scene.Objects)
        {
            if (!obj.IsVisible)
            {
                continue;
            }

            if (obj.UseMeshRendering)
            {
                var color = obj.Fill;
                if (obj.IsHovered)
                {
                    color = color.BlendTowards(ColorRgba.White, 0.10f);
                }
                if (obj.IsSelected)
                {
                    color = color.BlendTowards(ColorRgba.White, 0.22f);
                }

                meshes.Add(new WebGlMeshDrawPacket
                {
                    Id = obj.Id,
                    Model = ToArray(obj.GetModelMatrix()),
                    Color = new[] { color.R, color.G, color.B, color.A }
                });
                continue;
            }

            if (obj is ControlPlane3D plane && plane.Snapshot is not null)
            {
                var corners = ControlPlaneGeometry.GetWorldCorners(plane, scene.Camera);
                var vertices = new float[20]; // 4 * (xyz + uv)
                // TL, TR, BR, BL
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
        }

        controls.Sort((a, b) => a.AverageDepth.CompareTo(b.AverageDepth));

        return new WebGlScenePacket
        {
            Width = width,
            Height = height,
            ClearColor = new[]
            {
                scene.BackgroundColor.R,
                scene.BackgroundColor.G,
                scene.BackgroundColor.B,
                scene.BackgroundColor.A
            },
            ViewProjection = ToArray(viewProjection),
            Meshes = meshes,
            ControlPlanes = controls
        };
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
            if (System.MathF.Abs(clip.W) > 0.00001f)
            {
                sum += clip.Z / clip.W;
            }
        }

        return sum / worldCorners.Length;
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
}

internal sealed class WebGlScenePacket
{
    public required float Width { get; init; }
    public required float Height { get; init; }
    public required float[] ClearColor { get; init; }
    public required float[] ViewProjection { get; init; }
    public required List<WebGlMeshDrawPacket> Meshes { get; init; }
    public required List<WebGlControlPlanePacket> ControlPlanes { get; init; }
}

internal sealed class WebGlMeshDrawPacket
{
    public required string Id { get; init; }
    public required float[] Model { get; init; }
    public required float[] Color { get; init; }
}

internal sealed class WebGlControlPlanePacket
{
    public required string Id { get; init; }
    public required string TextureId { get; init; }
    public required float[] Vertices { get; init; }
    public required float AverageDepth { get; init; }
}
