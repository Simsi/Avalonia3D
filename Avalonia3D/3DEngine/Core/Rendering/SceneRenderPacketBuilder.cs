using System.Collections.Generic;
using System.Numerics;
using ThreeDEngine.Core.Environment;
using ThreeDEngine.Core.Lighting;
using ThreeDEngine.Core.Rendering.Shadows;
using ThreeDEngine.Core.Rendering.Pipeline;
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
            if (obj is ThreeDEngine.Core.Particles.ParticleSystem3D particles)
            {
                particles.SetBillboardBasis(scene.Camera.Right, scene.Camera.SafeUp, scene.Camera.Forward);
            }
            var mesh = obj.GetMesh();
            var model = obj.GetModelMatrix();
            var mvp = model * view * projection;
            var geometry = mesh.RenderGeometry;
            var geometryKey = geometry.ResourceKey;

            RenderMeshPayload? payload = null;
            if (geometryVersionCache is null ||
                !geometryVersionCache.TryGetValue(geometryKey, out var knownVersion) ||
                knownVersion != mesh.GeometryVersion)
            {
                payload = new RenderMeshPayload
                {
                    Positions = geometry.FlattenPositions(),
                    Normals = geometry.FlattenNormals(),
                    TexCoords0 = geometry.FlattenTexCoords0(),
                    Tangents = geometry.FlattenTangents(),
                    BoneIndices0 = geometry.FlattenBoneIndices0(),
                    BoneWeights0 = geometry.FlattenBoneWeights0(),
                    MaterialSlots = geometry.HasMaterialSlots ? (float[])geometry.MaterialSlots.Clone() : System.Array.Empty<float>(),
                    Indices = (int[])geometry.Indices.Clone(),
                    WireframeIndices = (int[])geometry.WireframeIndices.Clone(),
                    VertexLayout = geometry.Layout.ToString(),
                    EstimatedUploadBytes = geometry.EstimatedUploadBytes
                };

                if (geometryVersionCache is not null)
                {
                    geometryVersionCache[geometryKey] = mesh.GeometryVersion;
                }
            }

            var material = MaterialBinding3D.FromMaterial(obj.Material);
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
                LightingMode = (int)material.Lighting,
                SpecularColor = new[] { material.SpecularColor.R, material.SpecularColor.G, material.SpecularColor.B },
                SpecularParams = new[] { material.SpecularStrength, material.Shininess, material.Metallic, material.Roughness },
                MaterialStrengths = new[] { material.AmbientStrength, material.DiffuseStrength, material.NormalMapStrength, material.HasNormalMap ? 1f : 0f },
                Mesh = payload
            });
        }

        var light = SceneLightingResolver3D.Resolve(scene);
        var shadow = DirectionalShadowResolver3D.Resolve(scene);
        var skybox = scene.Environment.Skybox;
        var pipeline = RenderPipelinePlanner3D.Plan(scene, BackendKind.WebGlBrowser);
        return new SceneRenderPacket
        {
            Width = viewportSize.X,
            Height = viewportSize.Y,
            ClearColor = scene.BackgroundColor.ToArray(),
            CameraPosition = new[] { scene.Camera.Position.X, scene.Camera.Position.Y, scene.Camera.Position.Z },
            AmbientLight = ToArray(light.Ambient),
            DirectionalLightDirection = ToArray(light.DirectionalDirection),
            DirectionalLightColor = ToArray(light.DirectionalColor),
            PointLightPosition = ToArray(light.PointPosition),
            PointLightColor = ToArray(light.PointColor),
            SpotLightPosition = ToArray(light.SpotPosition),
            SpotLightDirection = ToArray(light.SpotDirection),
            SpotLightColor = ToArray(light.SpotColor),
            SpotLightCone = ToArray(light.SpotCone),
            SkyboxEnabled = skybox.Mode != SkyboxMode3D.None,
            SkyboxMode = (int)skybox.Mode,
            SkyboxTopColor = skybox.TopColor.ToArray(),
            SkyboxHorizonColor = skybox.HorizonColor.ToArray(),
            SkyboxBottomColor = skybox.BottomColor.ToArray(),
            SkyboxIntensity = skybox.Intensity,
            DirectionalShadowEnabled = shadow.IsEnabled,
            DirectionalShadowResolution = shadow.Resolution,
            DirectionalShadowStrength = shadow.Strength,
            DirectionalShadowBias = shadow.Bias,
            DirectionalShadowNormalBias = shadow.NormalBias,
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
            Objects = objects
        };
    }

    private static float[] ToArray(Vector3 value) => new[] { value.X, value.Y, value.Z };

    private static float[] ToArray(Vector4 value) => new[] { value.X, value.Y, value.Z, value.W };

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
