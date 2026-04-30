using Avalonia.Controls;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.Hosting;

public interface IScenePresenter
{
    event System.EventHandler<SceneFrameRenderedEventArgs>? FrameRendered;
    BackendKind Kind { get; }
    Control View { get; }
    Scene3D Scene { get; set; }
    void NotifySceneChanged(SceneChangedEventArgs change, RendererInvalidationKind invalidation);
    void RequestRender();
}
