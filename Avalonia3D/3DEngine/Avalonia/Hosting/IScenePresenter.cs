using Avalonia.Controls;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Scene;

namespace ThreeDEngine.Avalonia.Hosting;

public interface IScenePresenter : System.IDisposable
{
    event System.EventHandler<SceneFrameRenderedEventArgs>? FrameRendered;
    BackendKind Kind { get; }
    IRenderDeviceDiagnostics3D? RenderDevice { get; }
    Control View { get; }
    Scene3D Scene { get; set; }
    void RequestRender();
}
