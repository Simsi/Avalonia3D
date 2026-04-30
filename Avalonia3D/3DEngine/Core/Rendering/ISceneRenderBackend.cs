using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Core.Rendering;

public interface ISceneRenderBackend
{
    RenderBackendCapabilities Capabilities { get; }

    void NotifySceneChanged(SceneChangedEventArgs change, RendererInvalidationKind invalidation);

    RenderStats Render(Scene3D scene, SceneRenderPacket packet);
}

public interface IRenderResourceCache
{
    void Invalidate(RendererInvalidationKind invalidation);
    void Clear();
}

public interface IDebugDrawBackend
{
    void ClearDebugPrimitives();
    void DrawSceneDebug(Scene3D scene);
}

public interface IHighScaleRenderRuntime
{
    bool IsClientOwned { get; }
    void InvalidateStructure();
    void ApplyStatePatch();
}
