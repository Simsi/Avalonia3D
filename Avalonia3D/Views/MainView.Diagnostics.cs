using ThreeDEngine.Core.Diagnostics;
using ThreeDEngine.Core.Rendering;
using ThreeDEngine.Core.Rendering.Capabilities;

namespace Avalonia3D.Views;

public partial class MainView
{
    private void ShowSceneDiagnostics()
    {
        var capabilities = _lastFrame?.Kind == BackendKind.WebGlBrowser
            ? RendererCapabilities3D.WebGlBrowser
            : RendererCapabilities3D.OpenGlDesktop;
        _statusText.Text = SceneDiagnosticsCollector3D.Format(_sceneControl.Scene, capabilities);
    }

}
