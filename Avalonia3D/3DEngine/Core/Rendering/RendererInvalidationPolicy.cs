using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Rendering;

public static class RendererInvalidationPolicy
{
    public static RendererInvalidationKind FromSceneChange(SceneChangeKind kind)
    {
        return kind switch
        {
            SceneChangeKind.Structure => RendererInvalidationKind.FullFrame,
            SceneChangeKind.HighScaleStructure => RendererInvalidationKind.HighScaleStructure | RendererInvalidationKind.BatchRebuild | RendererInvalidationKind.ResourceUpload,
            SceneChangeKind.Geometry => RendererInvalidationKind.ResourceUpload | RendererInvalidationKind.BatchRebuild | RendererInvalidationKind.RegistryRebuild | RendererInvalidationKind.DrawOnly,
            SceneChangeKind.Material => RendererInvalidationKind.MaterialUpload | RendererInvalidationKind.DrawOnly,
            SceneChangeKind.Transform => RendererInvalidationKind.TransformUpload | RendererInvalidationKind.RegistryRebuild | RendererInvalidationKind.DrawOnly,
            SceneChangeKind.Visibility => RendererInvalidationKind.BatchRebuild | RendererInvalidationKind.RegistryRebuild | RendererInvalidationKind.DrawOnly,
            SceneChangeKind.Picking => RendererInvalidationKind.RegistryRebuild,
            SceneChangeKind.Collider => RendererInvalidationKind.RegistryRebuild | RendererInvalidationKind.DebugOverlay,
            SceneChangeKind.Rigidbody => RendererInvalidationKind.RegistryRebuild | RendererInvalidationKind.DebugOverlay,
            SceneChangeKind.Physics => RendererInvalidationKind.TransformUpload | RendererInvalidationKind.RegistryRebuild | RendererInvalidationKind.DebugOverlay | RendererInvalidationKind.DrawOnly,
            SceneChangeKind.Control => RendererInvalidationKind.ResourceUpload | RendererInvalidationKind.DrawOnly,
            SceneChangeKind.Camera => RendererInvalidationKind.DrawOnly,
            SceneChangeKind.Lighting => RendererInvalidationKind.DrawOnly,
            SceneChangeKind.Debug => RendererInvalidationKind.DebugOverlay | RendererInvalidationKind.DrawOnly,
            SceneChangeKind.DebugVisual => RendererInvalidationKind.DebugOverlay | RendererInvalidationKind.DrawOnly,
            SceneChangeKind.Selection => RendererInvalidationKind.DebugOverlay | RendererInvalidationKind.DrawOnly,
            SceneChangeKind.HighScaleState => RendererInvalidationKind.HighScaleState | RendererInvalidationKind.DrawOnly,
            _ => RendererInvalidationKind.FullFrame
        };
    }

    public static bool RequiresRegistry(RendererInvalidationKind invalidation)
        => (invalidation & RendererInvalidationKind.RegistryRebuild) != 0;

    public static bool RequiresBatchRebuild(RendererInvalidationKind invalidation)
        => (invalidation & RendererInvalidationKind.BatchRebuild) != 0;

    public static bool RequiresResourceUpload(RendererInvalidationKind invalidation)
        => (invalidation & (RendererInvalidationKind.ResourceUpload | RendererInvalidationKind.MaterialUpload | RendererInvalidationKind.TransformUpload)) != 0;
}
