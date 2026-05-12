using System;
using System.Numerics;
using ThreeDEngine.Core.Rendering.Pipeline;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Backend-neutral per-frame render context: registry snapshot, camera matrices and
/// pipeline plan computed once per frame by the engine core.
/// </summary>
public sealed class SceneRenderFrameContext3D
{
    private SceneRenderFrameContext3D(
        Scene3D scene,
        SceneFrameSnapshot3D snapshot,
        float width,
        float height,
        Matrix4x4 view,
        Matrix4x4 projection,
        RenderPipelinePlan3D pipeline)
    {
        Scene = scene;
        Snapshot = snapshot;
        Width = width;
        Height = height;
        Aspect = width / height;
        View = view;
        Projection = projection;
        ViewProjection = view * projection;
        Pipeline = pipeline;
    }

    public Scene3D Scene { get; }
    public SceneFrameSnapshot3D Snapshot { get; }
    public float Width { get; }
    public float Height { get; }
    public float Aspect { get; }
    public Matrix4x4 View { get; }
    public Matrix4x4 Projection { get; }
    public Matrix4x4 ViewProjection { get; }
    public RenderPipelinePlan3D Pipeline { get; }

    public static SceneRenderFrameContext3D Build(
        Scene3D scene,
        float width,
        float height,
        BackendKind backendKind,
        bool updateInterpolator = true)
    {
        if (scene is null) throw new ArgumentNullException(nameof(scene));
        width = MathF.Max(width, 1f);
        height = MathF.Max(height, 1f);
        if (updateInterpolator)
        {
            scene.FrameInterpolator.UpdateAlpha();
        }

        var snapshot = scene.Registry.GetFrameSnapshot();
        var aspect = width / height;
        var view = scene.Camera.GetViewMatrix();
        var projection = scene.Camera.GetProjectionMatrix(aspect);
        var pipeline = RenderPipelinePlanner3D.Plan(scene, backendKind);
        return new SceneRenderFrameContext3D(scene, snapshot, width, height, view, projection, pipeline);
    }

    public RenderStats CreateBaseStats()
    {
        return new RenderStats
        {
            ObjectCount = Snapshot.AllObjects.Length,
            RenderableCount = Snapshot.Renderables.Length,
            PickableCount = Snapshot.Pickables.Length,
            ColliderCount = Snapshot.Colliders.Length,
            DynamicBodyCount = Snapshot.DynamicBodies.Length,
            StaticColliderCount = Snapshot.StaticColliders.Length,
            RegistryVersion = Snapshot.RegistryVersion,
            MeshCacheCount = ThreeDEngine.Core.Geometry.MeshCache3D.Shared.Count,
            DirectionalLightCount = Scene.Lights.Count,
            PointLightCount = Scene.PointLights.Count,
            SpotLightCount = Scene.SpotLights.Count,
            InterpolationAlpha = Scene.FrameInterpolator.Alpha
        };
    }
}
