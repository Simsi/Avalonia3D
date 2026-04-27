using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Rendering;

public static class SceneRenderPacketBuilder
{
    public static SceneRenderPacket Build(
        Scene3D scene,
        Vector2 viewportSize,
        IDictionary<string, int>? geometryVersionCache = null)
    {
        viewportSize.X = System.MathF.Max(viewportSize.X, 1f);
        viewportSize.Y = System.MathF.Max(viewportSize.Y, 1f);

        var aspect = viewportSize.X / viewportSize.Y;
        var view = scene.Camera.GetViewMatrix();
        var projection = scene.Camera.GetProjectionMatrix(aspect);

        var objects = new List<RenderObjectPacket>();

        foreach (var obj in scene.Registry.Renderables)
        {
            var mesh = obj.GetMesh();
            var model = obj.GetModelMatrix();
            var mvp = model * view * projection;
            var geometryKey = mesh.ResourceKey;

            RenderMeshPayload? payload = null;
            if (geometryVersionCache is null ||
                !geometryVersionCache.TryGetValue(geometryKey, out var knownVersion) ||
                knownVersion != obj.GeometryVersion)
            {
                payload = new RenderMeshPayload
                {
                    Positions = Flatten(mesh.Positions),
                    Normals = Flatten(mesh.Normals),
                    Indices = (int[])mesh.Indices.Clone()
                };

                if (geometryVersionCache is not null)
                {
                    geometryVersionCache[geometryKey] = obj.GeometryVersion;
                }
            }

            var color = obj.Material.EffectiveColor;
            if (obj.IsEffectivelyHovered)
            {
                color = color.BlendTowards(Primitives.ColorRgba.White, 0.10f);
            }

            if (obj.IsEffectivelySelected)
            {
                color = color.BlendTowards(Primitives.ColorRgba.White, 0.22f);
            }

            objects.Add(new RenderObjectPacket
            {
                Id = obj.Id,
                Name = obj.Name,
                GeometryKey = geometryKey,
                Model = ToArray(model),
                Mvp = ToArray(mvp),
                Color = color.ToArray(),
                Mesh = payload
            });
        }

        return new SceneRenderPacket
        {
            Width = viewportSize.X,
            Height = viewportSize.Y,
            ClearColor = scene.BackgroundColor.ToArray(),
            Objects = objects
        };
    }

    private static float[] Flatten(Vector3[] values)
    {
        var result = new float[values.Length * 3];
        for (var i = 0; i < values.Length; i++)
        {
            var baseIndex = i * 3;
            result[baseIndex] = values[i].X;
            result[baseIndex + 1] = values[i].Y;
            result[baseIndex + 2] = values[i].Z;
        }

        return result;
    }

    private static float[] ToArray(Matrix4x4 matrix)
    {
        return new float[]
        {
            matrix.M11, matrix.M12, matrix.M13, matrix.M14,
            matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34,
            matrix.M41, matrix.M42, matrix.M43, matrix.M44
        };
    }
}
