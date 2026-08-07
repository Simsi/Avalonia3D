using System;
using System.Numerics;
using ThreeDEngine.Core.Rendering.Pipeline;
using ThreeDEngine.Core.Scene;
using ThreeDEngine.Core.Validation;

namespace ThreeDEngine.Core.Rendering;

/// <summary>
/// Backend-neutral render-frame context. The regular <see cref="Build"/> method creates an
/// allocating diagnostic/test publication; both it and the renderer-owned
/// <see cref="SceneRenderFrameScratch3D"/> path acquire a scene read lease while mutable
/// values are copied. Backends may release that lease before native/GPU submission after they
/// have captured every required value. The context itself must always be disposed.
/// </summary>
internal sealed class SceneRenderFrameContext3D : IDisposable
{
    private SceneAccessLease3D _sceneAccess;
    private SceneRenderFrameScratch3D? _scratchOwner;
    private bool _ownsSceneAccess;
    private bool _active;

    internal SceneRenderFrameContext3D()
    {
        Scene = null!;
        Published = null!;
        Snapshot = null!;
        Pipeline = null!;
    }

    public Scene3D Scene { get; private set; }
    public SceneRenderSnapshot3D Published { get; private set; }
    public SceneFrameSnapshot3D Snapshot { get; private set; }
    public float Width { get; private set; }
    public float Height { get; private set; }
    public float Aspect { get; private set; }
    public Matrix4x4 View { get; private set; }
    public Matrix4x4 Projection { get; private set; }
    public Matrix4x4 ViewProjection { get; private set; }
    public RenderPipelinePlan3D Pipeline { get; private set; }

    public static SceneRenderFrameContext3D Build(
        Scene3D scene,
        float width,
        float height,
        BackendKind backendKind)
    {
        if (scene is null) throw new ArgumentNullException(nameof(scene));
        width = Guard3D.Positive(width, nameof(width));
        height = Guard3D.Positive(height, nameof(height));
        backendKind = Guard3D.Defined(backendKind, nameof(backendKind));
        var sceneAccess = scene.EnterRenderReadScope();
        try
        {
            var published = SceneRenderSnapshot3D.Capture(scene, width / height, backendKind);
            var frame = new SceneRenderFrameContext3D();
            frame.Reset(scene, published, width, height, sceneAccess, scratchOwner: null);
            return frame;
        }
        catch
        {
            sceneAccess.Dispose();
            throw;
        }
    }

    internal void Reset(
        Scene3D scene,
        SceneRenderSnapshot3D published,
        float width,
        float height,
        SceneAccessLease3D sceneAccess,
        SceneRenderFrameScratch3D? scratchOwner)
    {
        if (_active)
        {
            throw new InvalidOperationException("A reusable render frame must be disposed before the scratch workspace can begin another frame.");
        }

        ResetCore(scene, published, width, height);
        _sceneAccess = sceneAccess;
        _scratchOwner = scratchOwner;
        _ownsSceneAccess = true;
        _active = true;
    }

    private void ResetCore(Scene3D scene, SceneRenderSnapshot3D published, float width, float height)
    {
        Scene = scene ?? throw new ArgumentNullException(nameof(scene));
        Published = published ?? throw new ArgumentNullException(nameof(published));
        Snapshot = published.Registry;
        Width = width;
        Height = height;
        Aspect = width / height;
        View = published.View;
        Projection = published.Projection;
        ViewProjection = published.ViewProjection;
        Pipeline = published.Pipeline;
    }

    public RenderStats CreateBaseStats()
    {
        var meshCache = Scene.Engine.Services.GetRequiredService<ThreeDEngine.Core.Geometry.MeshCache3D>();
        return new RenderStats
        {
            ObjectCount = Snapshot.AllObjectsInternal.Length,
            RenderableCount = Snapshot.RenderablesInternal.Length,
            PickableCount = Snapshot.PickablesInternal.Length,
            ColliderCount = Snapshot.CollidersInternal.Length,
            DynamicBodyCount = Snapshot.DynamicBodiesInternal.Length,
            StaticColliderCount = Snapshot.StaticCollidersInternal.Length,
            RegistryVersion = Snapshot.RegistryVersion,
            RegistryFullRebuildCount = Scene.Registry.FullRebuildCount,
            RegistryIncrementalChangeCount = Scene.Registry.IncrementalChangeCount,
            RegistrySpatialRefreshCount = Scene.Registry.SpatialRefreshCount,
            RegistrySnapshotBuildCount = Scene.Registry.SnapshotBuildCount,
            SceneChangeSequence = Published.SceneChangeSequence,
            RetainedSceneChangeCount = Scene.RetainedChangeCount,
            MeshCacheCount = meshCache.Count,
            MeshCacheHitCount = meshCache.HitCount,
            MeshCacheMissCount = meshCache.MissCount,
            DirectionalLightCount = Published.DirectionalLights.Length,
            PointLightCount = Published.PointLights.Length,
            SpotLightCount = Published.SpotLights.Length,
            InterpolationAlpha = Published.InterpolationAlpha,
            SimulationTick = Published.SimulationTick,
            SimulationTimeSeconds = Published.SimulationTimeSeconds,
            FixedUpdatesPerSecond = Published.FixedUpdatesPerSecond,
            SimulationAccumulatorSeconds = Published.SimulationAccumulatorSeconds,
            DroppedSimulationSeconds = Published.DroppedSimulationSeconds,
            LastSimulationStepCount = Published.LastSimulationStepCount,
            SimulationCommandsExecuted = Published.SimulationMetrics.CommandsExecuted,
            SimulationCommandsMilliseconds = Published.SimulationMetrics.CommandsMilliseconds,
            SimulationJobsExecuted = Published.SimulationMetrics.JobsExecuted,
            SimulationJobCommandsCommitted = Published.SimulationMetrics.JobCommandsCommitted,
            SimulationParallelJobBatches = Published.SimulationMetrics.ParallelJobBatches,
            SimulationJobsSnapshotMilliseconds = Published.SimulationMetrics.JobsSnapshotMilliseconds,
            SimulationJobsExecutionMilliseconds = Published.SimulationMetrics.JobsExecutionMilliseconds,
            SimulationJobsCommitMilliseconds = Published.SimulationMetrics.JobsCommitMilliseconds,
            SimulationJobsTotalMilliseconds = Published.SimulationMetrics.JobsTotalMilliseconds,
            SimulationUserUpdateMilliseconds = Published.SimulationMetrics.UserUpdateMilliseconds,
            SimulationAnimationMilliseconds = Published.SimulationMetrics.AnimationMilliseconds,
            SimulationPhysicsMilliseconds = Published.SimulationMetrics.PhysicsMilliseconds,
            SimulationParticleMilliseconds = Published.SimulationMetrics.ParticleMilliseconds,
            SimulationCompletionMilliseconds = Published.SimulationMetrics.CompletionMilliseconds,
            SimulationTotalMilliseconds = Published.SimulationMetrics.TotalMilliseconds,
            SimulationPaused = Published.SimulationPaused,
            SimulationFaulted = Published.SimulationFaulted
        };
    }

    /// <summary>
    /// Releases the mutable-scene read lease after a backend has copied every value required
    /// for submission. The frame/scratch object remains active until <see cref="Dispose"/>.
    /// </summary>
    internal void ReleaseSceneAccess()
    {
        if (!_ownsSceneAccess) return;
        _ownsSceneAccess = false;
        _sceneAccess.Dispose();
        _sceneAccess = default;
    }

    public void Dispose()
    {
        if (!_active) return;
        _active = false;
        try
        {
            ReleaseSceneAccess();
        }
        finally
        {
            var owner = _scratchOwner;
            _scratchOwner = null;
            owner?.Release();
        }
    }
}
